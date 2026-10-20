using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using pitTeam.BigBrain.Actions;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>Runs only on independent drafts; every accepted operation is replayable.</summary>
    internal sealed class GiveGearPlanner
    {
        internal static readonly EquipmentSlot[] EquipmentSlots = {
            EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster,
            EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest, EquipmentSlot.Headwear,
            EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear, EquipmentSlot.Backpack };
        private static readonly EquipmentSlot[] SupportSlots = { EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
        private const float MinimumCadenceMultiplier = 0.75f;
        private const int FastSemiAutoRate = 360;
        private const int NativeMinimumSingleRate = 240;
        private const int MaximumScoredEffectiveRange = 300;
        private const float RangeScorePerMeter = 0.2f;
        private readonly InventoryController _controller;
        private readonly InventoryEquipment _kit;
        private readonly SearchableItem _offer, _overflow;
        private readonly Action<GearSwapEdit> _record;
        private readonly List<IOperationResult> _operations = new List<IOperationResult>();
        private readonly List<GearSwapEdit> _edits = new List<GearSwapEdit>();
        private readonly Item[] _roots;
        private readonly Item[] _offered;
        private readonly Item[] _supplies;
        private readonly Weapon[] _weapons;
        internal readonly HashSet<string> OfferedWeaponIds;
        private readonly HashSet<string> _playerOwned;
        private readonly Action<Item> _createdItem;
        private readonly Action<string> _nativeSplit;
        private readonly Action<string> _trace;
        private readonly List<string> _nativeCreatedIds = new List<string>();
        private readonly BotOwner _bot;

        internal GiveGearPlanner(InventoryController controller, SearchableItem offer, SearchableItem overflow,
            BotOwner bot, Action<GearSwapEdit> record, IEnumerable<string> playerOwned = null,
            Action<Item> createdItem = null, Action<string> nativeSplit = null, Action<string> trace = null)
        {
            _controller = controller; _kit = controller.Inventory.Equipment;
            _offer = offer; _overflow = overflow; _record = record;
            _bot = bot; _createdItem = createdItem; _nativeSplit = nativeSplit; _trace = trace;
            _roots = offer.Grids.SelectMany(g => g.Items).ToArray();
            // Snapshot the original offering depth before planning moves any container.
            // Every category may use immediate grid contents, never a second container's contents.
            _offered = _roots.Concat(_roots.OfType<SearchableItem>()
                .SelectMany(i => i.Grids).SelectMany(g => g.Items)).Distinct().ToArray();
            _playerOwned = new HashSet<string>(playerOwned ?? _roots.SelectMany(TeammateGearSwap.Tree).Select(i => (string)i.Id));
            OfferedWeaponIds = new HashSet<string>(_offered.OfType<Weapon>().Select(w => (string)w.Id));
            _supplies = _offered.Concat(CarriedSupplies()).Where(i => i is Magazine || i is Ammo).Distinct().ToArray();
            var previous = bot == null ? Enumerable.Empty<Weapon>() : GiveGearHistory.CarriedWeapons(bot, _kit);
            _weapons = _offered.OfType<Weapon>().Concat(previous).Distinct().Where(FunctionalWeapon).ToArray();
        }

        private Item Equipped(EquipmentSlot slot) => _kit.GetSlot(slot)?.ContainedItem;
        private IEnumerable<Item> CarriedSupplies() => new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets,
            EquipmentSlot.Backpack, EquipmentSlot.SecuredContainer }.Select(Equipped).OfType<SearchableItem>()
            .SelectMany(i => i.Grids).SelectMany(g => g.Items);
        private bool InKit(Item item) => TeammateGearSwap.Tree(_kit).Contains(item);
        private bool InFastAccess(Item item) => new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }
            .Select(Equipped).OfType<SearchableItem>().Any(r => r.Grids.Any(g => g.Items.Contains(item)));

        internal void Build()
        {
            try
            {
                Trace("begin", null, "planning", $"roots={_roots.Length} candidates={_offered.Length} weapons={_weapons.Length}");
                foreach (Weapon rejected in _offered.OfType<Weapon>().Where(w => !_weapons.Contains(w)))
                    Trace("primary", rejected, "rejected", "incompleteOrBroken");
                var primary = Equipped(EquipmentSlot.FirstPrimaryWeapon) as Weapon;
                var originalSupport = SupportSlots.ToDictionary(slot => slot, slot => Equipped(slot) as Weapon);
                bool currentPrimarySupported = primary != null && FollowerWeaponPrimaryReadiness.EvaluateActual(_controller, primary).PrimaryReady;
                bool checkedCurrentResupply = currentPrimarySupported;
                foreach (Weapon candidate in _weapons.OrderByDescending(WeaponScore).ThenBy(w => (string)w.Id, StringComparer.Ordinal))
                {
                    if (candidate == primary) continue;
                    if (!CanUseSlot(EquipmentSlot.FirstPrimaryWeapon, candidate))
                    { Trace("primary", candidate, "rejected", "incompatibleSlot"); continue; }
                    float candidateScore = WeaponScore(candidate), currentScore = WeaponScore(primary);
                    Trace("primary/comparison", candidate, "comparison",
                        $"score={candidateScore:0.##} current={currentScore:0.##} required={currentScore * 1.10f + 1f:0.##} " +
                        $"cadence={DescribeWeaponCadence(candidate)} currentCadence={DescribeWeaponCadence(primary)} " +
                        $"range={DescribeWeaponRange(candidate)} currentRange={DescribeWeaponRange(primary)}");
                    if (primary != null && candidateScore <= currentScore * 1.10f + 1f)
                    {
                        if (!checkedCurrentResupply)
                        {
                            checkedCurrentResupply = true;
                            // Keep the stronger gun if carried/offered supplies can actually make
                            // it operational, including through a suitable offered outfit.
                            currentPrimarySupported = Try(() => { DeferSupportWeapons(); Support(primary, true); },
                                "primary/replenishCurrent", primary) || TryOutfit(primary, armoredRig: false) || TryOutfit(primary, armoredRig: true);
                            currentScore = WeaponScore(primary);
                            candidateScore = WeaponScore(candidate);
                        }
                        if (currentPrimarySupported && candidateScore <= currentScore * 1.10f + 1f)
                        { Trace("primary", candidate, "rejected", $"notMeaningfulUpgrade score={candidateScore:0.##} current={currentScore:0.##}"); continue; }
                        if (!currentPrimarySupported)
                            Trace("primary", candidate, "fallback", $"currentPrimaryUnready score={candidateScore:0.##} current={currentScore:0.##}");
                    }
                    if (Try(() =>
                    {
                        Stow(primary); DeferSupportWeapons();
                        Equip(candidate, EquipmentSlot.FirstPrimaryWeapon); Support(candidate, true);
                    }, "primary/currentGear", candidate)) break;
                    // Three bounded arrangements, not a cross-product outfit search.
                    if (TryOutfit(candidate, armoredRig: false) || TryOutfit(candidate, armoredRig: true)) break;
                }
                // Armor upgrades are independent of a successful weapon upgrade.
                TryOutfit(null, armoredRig: false);
                TryOutfit(null, armoredRig: true);
                if (Equipped(EquipmentSlot.TacticalVest) == null)
                    foreach (var vest in _offered.OfType<Vest>().OrderByDescending(Capacity).ThenBy(v => (string)v.Id))
                        if (Try(() => { Equip(vest, EquipmentSlot.TacticalVest); RequireReloadSpace(); }, "emptyVest", vest)) break;
                UpgradeHelmet();
                UpgradeBackpack();
                foreach (EquipmentSlot slot in new[] { EquipmentSlot.Earpiece, EquipmentSlot.FaceCover, EquipmentSlot.Eyewear })
                    if (Equipped(slot) == null)
                        foreach (Item item in _offered.Where(i => !IsEquipped(i) && CanUseSlot(slot, i)))
                            if (TryEquipEmptyAccessory(item, slot)) break;

                // Existing primary/support readiness and shared reload-reserve policy own usability.
                var supportCandidates = _weapons.Concat(_overflow.Grids.SelectMany(g => g.Items).OfType<Weapon>())
                    .Distinct().OrderByDescending(WeaponScore).ThenBy(w => (string)w.Id, StringComparer.Ordinal).ToArray();
                var finalPrimary = Equipped(EquipmentSlot.FirstPrimaryWeapon) as Weapon;
                // A later supply-only handover still supports the retained primary before cargo.
                if (finalPrimary != null) Try(() => Support(finalPrimary, true), "primary/finalSupport", finalPrimary);
                bool preservePrimaryReadiness = finalPrimary != null &&
                    FollowerWeaponPrimaryReadiness.EvaluateActual(_controller, finalPrimary).PrimaryReady;
                foreach (var slot in SupportSlots)
                    if (Equipped(slot) == null)
                        foreach (Weapon candidate in new[] { originalSupport[slot] }.Concat(supportCandidates)
                            .Where(w => w != null && !IsEquipped(w)).Distinct())
                            if (Try(() =>
                            {
                                Equip(candidate, slot); Support(candidate, false);
                                if (preservePrimaryReadiness && !FollowerWeaponPrimaryReadiness.EvaluateActual(_controller, finalPrimary).PrimaryReady)
                                    throw new InvalidOperationException("Optional support consumed required primary supplies.");
                            }, "support/" + slot, candidate)) break;

                // Preserve the operational landing opening while adding ordinary cargo.
                foreach (Item item in _roots.Concat(_overflow.Grids.SelectMany(g => g.Items).ToArray()).Distinct().ToArray())
                {
                    if (InKit(item)) continue;
                    if (!Try(() => Carry(item), "cargo", item))
                    { Stow(item); Trace("drop", item, "planned", "noSafeCargoPlacement"); }
                }
                // No offering may survive in the temporary UI container after submission.
                foreach (Item item in _offer.Grids.SelectMany(g => g.Items).ToArray()) Stow(item);
                foreach (var edit in _edits) _record(edit);
                foreach (string id in _nativeCreatedIds.Where(id => id != null)) _nativeSplit?.Invoke(id);
                Trace("complete", null, "planned", $"edits={_edits.Count} dropRoots={_overflow.Grids.Sum(g => g.Items.Count())}");
            }
            catch (Exception ex) { Rollback(); Trace("complete", null, "rolledBack", ex.Message); throw; }
        }

        private bool TryOutfit(Weapon candidate, bool armoredRig)
        {
            Item oldArmor = Equipped(EquipmentSlot.ArmorVest), oldRig = Equipped(EquipmentSlot.TacticalVest);
            BodyArmorProtection oldProtection = EvaluateBodyProtection(oldArmor, oldRig);
            Item armor = armoredRig ? null : _offered.Where(i => !IsEquipped(i) && CanUseSlot(EquipmentSlot.ArmorVest, i))
                .OrderByDescending(i => EvaluateBodyProtection(i).Score).ThenBy(i => (string)i.Id).FirstOrDefault();
            Item rig = _offered.OfType<Vest>().Where(i => !IsEquipped(i) && (Protection(i) > 0) == armoredRig)
                .OrderByDescending(i => armoredRig ? EvaluateBodyProtection(i).Score : Capacity(i)).ThenBy(i => (string)i.Id).FirstOrDefault();
            string decision = candidate != null ? "primary/" : "armor/";
            decision += armoredRig ? "armoredRig" : "separateArmor";
            if (armoredRig && rig == null) return false;
            if (!armoredRig && (armor == null || EvaluateBodyProtection(armor).Score < EvaluateBodyProtection(oldArmor).Score)) armor = oldArmor;
            if (!armoredRig && armor == null && rig == null) return false;
            BodyArmorProtection newProtection = armoredRig ? EvaluateBodyProtection(rig) : EvaluateBodyProtection(armor, rig ?? oldRig);
            string comparison = $"protection={newProtection.Score:0.##} current={oldProtection.Score:0.##} " +
                $"front={newProtection.Front:0.##} back={newProtection.Back:0.##} currentFront={oldProtection.Front:0.##} currentBack={oldProtection.Back:0.##}";
            if (candidate != null ? newProtection.Score < oldProtection.Score : newProtection.Score <= oldProtection.Score &&
                (oldArmor != null || oldRig != null || newProtection.Score > 0))
            { Trace(decision, candidate ?? rig ?? armor, "rejected", comparison); return false; }
            Trace(decision, candidate ?? rig ?? armor, "comparison", comparison);
            var previousPrimary = Equipped(EquipmentSlot.FirstPrimaryWeapon) as Weapon;
            var previousReadiness = previousPrimary == null ? null : FollowerWeaponPrimaryReadiness.EvaluateActual(_controller, previousPrimary);
            bool wasUsable = previousReadiness?.InsertedRounds > 0;
            return Try(() =>
            {
                // Only this trial defers optional support. A rejected outfit/primary restores it.
                if (candidate != null || wasUsable) DeferSupportWeapons();
                // Migrate existing loose contents; do not silently throw the bot's supplies with a shell.
                var contents = oldRig is SearchableItem old ? old.Grids.SelectMany(g => g.Items).ToArray() : new Item[0];
                Stow(oldArmor); Stow(oldRig);
                if (armoredRig) Equip(rig, EquipmentSlot.TacticalVest);
                else
                {
                    if (armor != null) Equip(armor, EquipmentSlot.ArmorVest);
                    if (rig != null) Equip(rig, EquipmentSlot.TacticalVest);
                    else if (oldRig != null) Equip(oldRig, EquipmentSlot.TacticalVest);
                }
                // Selected primary support wins fast-access space before general old-rig contents.
                if (rig is SearchableItem incoming && _offered.Contains(rig))
                    foreach (Item content in incoming.Grids.SelectMany(g => g.Items).Where(_offered.Contains).ToArray()) Stow(content);
                if (candidate != null)
                {
                    Stow(Equipped(EquipmentSlot.FirstPrimaryWeapon));
                    Equip(candidate, EquipmentSlot.FirstPrimaryWeapon);
                }
                var current = Equipped(EquipmentSlot.FirstPrimaryWeapon) as Weapon;
                bool primaryFirst = current != null && (candidate != null || wasUsable);
                if (primaryFirst)
                    Support(current, candidate != null || previousReadiness?.PrimaryReady == true);
                foreach (Item item in contents.Where(i => !InKit(i)))
                {
                    // Do not refill the new vest with the deferred weapon's spares before its
                    // optional support pass. Generic cargo gets another chance after that pass.
                    if (primaryFirst && item is Magazine magazine &&
                        !FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(current, magazine)) Stow(item);
                    else if (!Try(() => Carry(item))) Stow(item);
                }
                RequireReloadSpace();
            }, decision + $" armor={armor?.Id} vest={rig?.Id}", candidate ?? rig ?? armor);
        }

        private void DeferSupportWeapons()
        {
            foreach (EquipmentSlot slot in SupportSlots)
                if (Equipped(slot) is Weapon weapon)
                {
                    Stow(weapon);
                    Trace("support/defer/" + slot, weapon, "planned", "evaluateAfterPrimaryOutfit");
                }
        }

        private void UpgradeHelmet()
        {
            Item current = Equipped(EquipmentSlot.Headwear);
            foreach (Item helmet in _offered.Where(i => CanUseSlot(EquipmentSlot.Headwear, i))
                .OrderByDescending(Protection).ThenBy(i => (string)i.Id))
            {
                if (Protection(helmet) <= Protection(current))
                { Trace("helmet", helmet, "rejected", "notBetterProtection"); continue; }
                if (Try(() => { Stow(current); Equip(helmet, EquipmentSlot.Headwear); }, "helmet", helmet)) break;
                Item faceCover = Equipped(EquipmentSlot.FaceCover);
                // Let native equipment admission prove whether this cosmetic item is the blocker.
                // Never strip protective face gear or leave it removed after a failed retry.
                if (faceCover != null && Protection(faceCover) == 0 && Try(() =>
                {
                    Stow(faceCover); Stow(current); Equip(helmet, EquipmentSlot.Headwear);
                }, "helmet/displaceNonProtectiveFaceCover", helmet))
                {
                    Trace("helmet/displacedFaceCover", faceCover, "planned", $"helmet={helmet.Id}");
                    break;
                }
            }
        }

        private bool TryEquipEmptyAccessory(Item item, EquipmentSlot slot)
        {
            if (Try(() => Equip(item, slot), "emptySlot/" + slot, item)) return true;
            Item headwear = Equipped(EquipmentSlot.Headwear);
            // Mirror the helmet/cosmetic-face-cover retry, without overriding native admission
            // or stripping protective headwear for a mask. Other accessory conflicts stay native.
            if (slot != EquipmentSlot.FaceCover || Protection(item) <= 0 ||
                headwear == null || Protection(headwear) > 0) return false;
            if (!Try(() =>
            {
                Stow(headwear); Equip(item, slot);
            }, "faceCover/displaceNonProtectiveHeadwear", item)) return false;
            Trace("faceCover/displacedHeadwear", headwear, "planned", $"mask={item.Id}");
            return true;
        }

        private void UpgradeBackpack()
        {
            var old = Equipped(EquipmentSlot.Backpack) as SearchableItem;
            foreach (var bag in _offered.OfType<Backpack>().OrderByDescending(Capacity)
                .ThenBy(b => (string)b.Id))
            {
                if (ReferenceEquals(old, bag)) continue;
                if (Capacity(bag) <= Capacity(old))
                { Trace("backpack", bag, "rejected", "notLargerCapacity"); continue; }
                if (Try(() =>
                {
                    var contents = old?.Grids.SelectMany(g => g.Items).ToArray() ?? new Item[0];
                    Stow(old); Equip(bag, EquipmentSlot.Backpack);
                    foreach (Item item in contents) MoveToGrids(item, bag.Grids);
                }, "backpack/migrateContents", bag)) break;
            }
        }

        private void Support(Weapon weapon, bool primary)
        {
            if (!FunctionalWeapon(weapon)) throw new InvalidOperationException("Incomplete or broken weapon.");
            if (primary)
            {
                var retained = new[] { EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster }
                    .Select(Equipped).OfType<Weapon>().ToArray();
                foreach (var magazine in FastGrids().SelectMany(g => g.Items).OfType<Magazine>().ToArray())
                    if (!FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, magazine) &&
                        !retained.Any(w => FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(w, magazine)))
                        Stow(magazine);
            }
            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                foreach (var ammo in _supplies.OfType<Ammo>().Where(a => FollowerWeaponLooseFeedReadiness.IsCompatibleLooseAmmo(weapon, a)))
                    if (!InKit(ammo)) Try(() => Carry(ammo));
            }
            else
            {
                var magazines = _supplies.OfType<Magazine>().Where(m =>
                    FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, m) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, m))
                    .OrderByDescending(m => m.Count).ThenBy(m => (string)m.Id).ToList();
                var current = weapon.GetCurrentMagazine();
                if (current != null && !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, current))
                {
                    Stow(current); current = null;
                }
                TopOffInsertedMagazine(weapon);
                if (current != null && current.Count == 0)
                {
                    Stow(current);
                    if (!magazines.Contains(current)) magazines.Insert(0, current);
                    current = null;
                }
                if (current == null || current.Count == 0)
                {
                    var loaded = magazines.FirstOrDefault(m => m.Count > 0);
                    if (loaded == null)
                        foreach (var magazine in magazines)
                            if (Try(() => RefillMagazine(weapon, magazine))) { loaded = magazine; break; }
                    if (loaded == null) throw new InvalidOperationException("No usable loaded magazine.");
                    Stow(current); Move(loaded, weapon.GetMagazineSlot().CreateItemAddress());
                    TopOffInsertedMagazine(weapon);
                }
                foreach (var magazine in magazines.Where(m => !ReferenceEquals(m, weapon.GetCurrentMagazine())))
                {
                    // Readiness is the minimum, not a collection cap. Each extra magazine
                    // must independently fit fast access without consuming reload space.
                    if (!primary && InFastAccess(magazine) && magazine.Count > 0) continue;
                    Try(() =>
                    {
                        if (!InFastAccess(magazine)) MoveToGrids(magazine, FastGrids(), reserveReloadSpace: true);
                        RequireReloadSpace(); // Prove landing geometry before using loose rounds.
                        RefillMagazine(weapon, magazine);
                    }, "spareMagazine", magazine);
                }
            }
            var readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(_controller, weapon);
            bool usable = primary ? readiness.PrimaryReady : readiness.InsertedRounds > 0 ||
                (readiness.HasInsertedMagazine && readiness.FastAccessMagazineRounds.Any(n => n > 0));
            if (!usable) throw new InvalidOperationException($"Weapon package is not operational: primary={primary} inserted={readiness.InsertedRounds} fastAccessRounds={string.Join(",", readiness.FastAccessMagazineRounds)}.");
            RequireReloadSpace();
        }

        private void TopOffInsertedMagazine(Weapon weapon)
        {
            var magazine = weapon.GetCurrentMagazine();
            if (magazine == null || magazine.Count >= magazine.MaxCount) return;
            if (!_supplies.OfType<Ammo>().Any(a => a.StackObjectsCount > 0 &&
                FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, a) && !AllegianceScavCargo.HasMixedStack(_bot, a)))
            { Trace("topOffInserted", magazine, "skipped", "noEligibleLooseAmmo"); return; }
            int before = magazine.Count;
            if (Try(() =>
            {
                // EFT rejects loading an inserted detachable magazine. These draft moves
                // replay atomically after hands release; a failed refill restores the seat.
                Stow(magazine);
                RefillMagazine(weapon, magazine);
                if (magazine.Count <= before) throw new InvalidOperationException("No eligible ammunition could be added.");
                Move(magazine, weapon.GetMagazineSlot().CreateItemAddress());
            }, "topOffInserted", magazine))
                Trace("magazineRounds", magazine, "planned", $"inserted={before}->{magazine.Count}");
        }

        private void RefillMagazine(Weapon weapon, Magazine magazine)
        {
            foreach (var ammo in _supplies.OfType<Ammo>().Where(a => a.StackObjectsCount > 0 &&
                FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, a) && !AllegianceScavCargo.HasMixedStack(_bot, a)))
            {
                if (magazine.Count >= magazine.MaxCount) break;
                var refill = magazine.Apply(_controller, ammo, Math.Min(ammo.StackObjectsCount,
                    magazine.MaxCount - magazine.Count), false);
                if (refill.Failed) { Trace("refillMagazine", magazine, "rejected", refill.Error); continue; }
                var operation = refill.Value is AbstractMagOperationResult wrapper ? wrapper.OperationResult : refill.Value;
                try { Record(operation); }
                catch (InvalidOperationException ex) { Trace("refillMagazine", magazine, "rejected", ex.Message); }
            }
            if (magazine.Count == 0) throw new InvalidOperationException("No usable ammunition for this magazine.");
        }

        private IEnumerable<Grid> FastGrids() => new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }
            .Select(Equipped).OfType<SearchableItem>().SelectMany(i => i.Grids);
        private void RequireReloadSpace()
        {
            if (!GestureCommandAction.CanFitGearSwapReloadReserves(_kit))
                throw new InvalidOperationException("Weapon magazine cannot land during reload.");
        }
        private void Carry(Item item)
        {
            var roots = new[] { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }
                .Select(Equipped).OfType<SearchableItem>().Where(r => !ReferenceEquals(r, item));
            MoveToGrids(item, roots.SelectMany(i => i.Grids)); RequireReloadSpace();
        }
        private void MoveToGrids(Item item, IEnumerable<Grid> grids, bool reserveReloadSpace = false)
        {
            foreach (var grid in grids)
            {
                ItemAddress address = grid.FindLocationForItem(item);
                if (address != null && Try(() =>
                {
                    Move(item, address);
                    if (reserveReloadSpace) RequireReloadSpace();
                }, reserveReloadSpace ? "reloadSafePlacement" : null, item)) return;
            }
            throw new InvalidOperationException("No fitting grid location.");
        }
        private void Stow(Item item)
        {
            if (item == null || _overflow.Grids.Any(g => g.Items.Contains(item))) return;
            MoveToGrids(item, _overflow.Grids);
        }
        private void Equip(Item item, EquipmentSlot slot)
        {
            if (item == null || Equipped(slot) != null || !CanUseSlot(slot, item))
                throw new InvalidOperationException("Equipment slot is unavailable or incompatible.");
            Move(item, _kit.GetSlot(slot).CreateItemAddress());
        }
        private bool CanUseSlot(EquipmentSlot slot, Item item) => item != null && _kit.GetSlot(slot)?.CanAccept(item) == true;
        private bool IsEquipped(Item item) => EquipmentSlots.Any(s => ReferenceEquals(Equipped(s), item));
        private void Move(Item item, ItemAddress address)
        {
            var operation = ItemManipulator.Move(item, address, _controller, false);
            if (operation.Failed) throw new InvalidOperationException(operation.Error.ToString());
            Record(operation.Value);
        }
        private void Record(IOperationResult operation)
        {
            try
            {
                var edit = GearSwapEdit.Capture(operation);
                edit.ValidateProvenance(_playerOwned);
                string nativeId = operation is SplitResult split && !_playerOwned.Contains(split.Item.Id) ? (string)split.ResultItem.Id : null;
                edit.UpdateProvenance(_playerOwned);
                if (operation is SplitResult created) _createdItem?.Invoke(created.ResultItem);
                _edits.Add(edit); _operations.Add(operation); _nativeCreatedIds.Add(nativeId);
            }
            catch { operation.RollBack(); throw; }
        }
        private bool Try(Action action, string decision = null, Item item = null)
        {
            int checkpoint = _operations.Count;
            try { action(); if (decision != null) Trace(decision, item, "planned", "accepted"); return true; }
            catch (InvalidOperationException ex) { RollbackTo(checkpoint); if (decision != null) Trace(decision, item, "rejected", ex.Message); return false; }
            catch { RollbackTo(checkpoint); throw; }
        }
        private void Trace(string decision, Item item, string result, object reason)
        {
            if (_trace == null) return;
            try { _trace($"action='{decision}' item='{item?.Id}' type='{item?.GetType().Name}' result='{result}' reason='{reason}'"); }
            catch { /* Diagnostics must never change a plan, rollback or ownership result. */ }
        }
        internal void Rollback() => RollbackTo(0);
        private void RollbackTo(int checkpoint)
        {
            for (int index = _operations.Count - 1; index >= checkpoint; index--)
            {
                _operations[index].RollBack(); _operations.RemoveAt(index); _edits.RemoveAt(index); _nativeCreatedIds.RemoveAt(index);
            }
        }
        internal static int Capacity(Item item) => item is SearchableItem container ?
            container.Grids.Sum(g => g.GridWidth * g.GridHeight) : 0;
        private static IEnumerable<Item> InstalledTree(Item item)
        {
            if (item == null) yield break;
            yield return item;
            if (item is CompoundItem compound)
                foreach (var slot in compound.Slots)
                    foreach (Item child in InstalledTree(slot.ContainedItem)) yield return child;
        }
        internal static float Protection(Item item) => item == null ? 0 : InstalledTree(item)
            .Select(i => new { Armor = i.GetItemComponent<ArmorComponent>(), Repair = i.GetItemComponent<RepairableComponent>() })
            .Where(c => c.Armor != null && (c.Repair == null || c.Repair.Durability > 0))
            .Select(c => c.Armor.ArmorClass * 100f * (c.Repair == null ? 1f :
                0.25f + 0.75f * Mathf.Clamp01(c.Repair.Durability / Math.Max(1f, c.Repair.TemplateDurability))))
            .DefaultIfEmpty(0).Max();

        internal readonly struct BodyArmorProtection
        {
            internal readonly float Front, Back;
            // Initial kit-comparison calibration, not a simulation of penetration probability.
            internal float Score => Front * 0.75f + Back * 0.25f;
            internal BodyArmorProtection(float front, float back) { Front = front; Back = back; }
        }

        internal static BodyArmorProtection EvaluateBodyProtection(params Item[] items)
        {
            float front = 0f, back = 0f;
            foreach (Item item in items.Where(i => i != null).SelectMany(InstalledTree).Distinct())
            {
                ArmorComponent armor = item.GetItemComponent<ArmorComponent>();
                RepairableComponent repair = item.GetItemComponent<RepairableComponent>();
                if (armor == null || (repair != null && repair.Durability < 0.01f)) continue;
                float score = armor.ArmorClass * 100f * (repair == null ? 1f :
                    0.25f + 0.75f * Mathf.Clamp01(repair.Durability / Math.Max(1f, repair.TemplateDurability)));
                // These properties include the actual installed ArmorSlot coverage restrictions.
                EArmorPlateCollider plates = armor.ArmorPlateCollidersMask;
                var soft = armor.ArmorColliders?.ToArray() ?? new EBodyPartColliderType[0];
                if ((plates & (EArmorPlateCollider.Plate_Granit_SAPI_chest | EArmorPlateCollider.Plate_Korund_chest)) != 0 ||
                    soft.Contains(EBodyPartColliderType.RibcageUp) || soft.Contains(EBodyPartColliderType.RibcageLow))
                    front = Math.Max(front, score);
                if ((plates & (EArmorPlateCollider.Plate_Granit_SAPI_back | EArmorPlateCollider.Plate_6B13_back)) != 0 ||
                    soft.Contains(EBodyPartColliderType.SpineTop) || soft.Contains(EBodyPartColliderType.SpineDown))
                    back = Math.Max(back, score);
            }
            return new BodyArmorProtection(front, back);
        }
        private float WeaponScore(Weapon weapon)
        {
            if (!FunctionalWeapon(weapon)) return 0;
            var ammo = _supplies.OfType<Ammo>().Where(a => FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, a))
                .Concat(_supplies.OfType<Magazine>().Where(m => FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, m))
                    .SelectMany(m => m.Cartridges.Items).OfType<Ammo>())
                .Concat(TeammateGearSwap.Tree(weapon).OfType<Ammo>())
                .Where(a => FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, a)).Distinct().ToArray();
            if (ammo.Length == 0) return 0;
            var loaded = weapon.GetCurrentMagazine()?.Cartridges.Items.OfType<Ammo>()
                .FirstOrDefault(a => FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, a));
            float quality = loaded == null ? ammo.Max(a => a.PenetrationPower * 2f + a.Damage * 0.2f) :
                loaded.PenetrationPower * 2f + loaded.Damage * 0.2f;
            // Combat capability, not market value. Bounded ammo quantity cannot outweigh
            // ammunition quality indefinitely; native readiness remains a separate hard gate.
            float score = Math.Max(1, quality +
                Math.Min(90, ammo.Sum(a => a.StackObjectsCount)) * 0.4f +
                weapon.ErgonomicsTotal * 0.4f - weapon.RecoilTotal * 0.05f);
            return Math.Max(1, score * GetWeaponCadenceMultiplier(weapon, out _) +
                GetWeaponRangeBonus(weapon, out _));
        }

        internal static float GetWeaponRangeBonus(Weapon weapon, out int rangeMeters)
        {
            // This is EFT's native effective-distance attribute, not sight zeroing or maximum
            // projectile travel. Value useful general-purpose reach without rewarding infinity.
            rangeMeters = Math.Max(0, (weapon?.Template as WeaponTemplate)?.bEffDist ?? 0);
            return Math.Min(MaximumScoredEffectiveRange, rangeMeters) * RangeScorePerMeter;
        }

        private static string DescribeWeaponRange(Weapon weapon)
        {
            float bonus = GetWeaponRangeBonus(weapon, out int rangeMeters);
            return $"{rangeMeters}m(+{bonus:0.##})";
        }

        internal static float GetWeaponCadenceMultiplier(Weapon weapon, out string reason)
        {
            reason = "unknownCadence";
            if (weapon?.Template is not WeaponTemplate template) return 1f;
            if (template.BoltAction)
            {
                reason = "boltAction";
                return MinimumCadenceMultiplier;
            }
            Weapon.EFireMode[] modes = template.weapFireType;
            if (modes == null || modes.Length == 0) return 1f;
            // Judge available capability, not whichever mode happens to be selected in the draft.
            if (modes.Contains(Weapon.EFireMode.fullauto) || modes.Contains(Weapon.EFireMode.burst))
            {
                reason = "fastModeAvailable";
                return 1f;
            }
            if ((!modes.Contains(Weapon.EFireMode.single) && !modes.Contains(Weapon.EFireMode.semiauto)) ||
                template.SingleFireRate <= 0) return 1f;
            // Match EFT's cooldown floor without invoking Weapon.SingleFireRate, whose getter
            // repairs invalid shared templates. bFirerate is not the semi-auto cooldown limit.
            int rate = Math.Max(NativeMinimumSingleRate, template.SingleFireRate);
            if (rate >= FastSemiAutoRate)
            {
                reason = $"singleShotCooldown rpm={rate}";
                return 1f;
            }
            reason = $"slowSingleShotCooldown rpm={rate} templateRpm={template.SingleFireRate}";
            return MinimumCadenceMultiplier + (1f - MinimumCadenceMultiplier) *
                (rate - NativeMinimumSingleRate) / (FastSemiAutoRate - NativeMinimumSingleRate);
        }

        private static string DescribeWeaponCadence(Weapon weapon)
        {
            float multiplier = GetWeaponCadenceMultiplier(weapon, out string reason);
            return $"{multiplier:0.###}({reason})";
        }
        private static bool FunctionalWeapon(Weapon weapon) => weapon != null && !weapon.MissingVitalParts.Any() &&
            (weapon.GetItemComponent<RepairableComponent>() == null || weapon.GetItemComponent<RepairableComponent>().Durability > 0);
    }
}
