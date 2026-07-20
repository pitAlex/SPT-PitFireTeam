using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static readonly EquipmentSlot[] WeaponLooseAmmoDestinationOrder =
        {
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.TacticalVest
        };

        private static readonly EquipmentSlot[] LauncherLooseAmmoDestinationOrder =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };

        private IEnumerable<BodyGearCandidate> GetBodyWeaponLooseAmmoCandidates(
            InventoryEquipment corpseEquipment,
            Weapon weapon)
        {
            if (corpseEquipment == null || weapon == null)
            {
                yield break;
            }

            foreach (EquipmentSlot slot in new[]
                     {
                         EquipmentSlot.TacticalVest,
                         EquipmentSlot.Pockets,
                         EquipmentSlot.Backpack
                     })
            {
                Item root = corpseEquipment.GetSlot(slot)?.ContainedItem;
                foreach (AmmoItemClass ammo in GetWeaponLooseAmmoItems(root, weapon))
                {
                    yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, $"{slot}.WeaponLooseAmmo");
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerWeaponLooseAmmoCandidates(
            SearchableItemItemClass containerRoot,
            Weapon weapon)
        {
            foreach (AmmoItemClass ammo in GetWeaponLooseAmmoItems(containerRoot, weapon))
            {
                yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, "Container.WeaponLooseAmmo");
            }
        }

        private static IEnumerable<AmmoItemClass> GetWeaponLooseAmmoItems(Item root, Weapon weapon)
        {
            if (root == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AmmoItemClass ammo in SnapshotLootTreeItems(root).OfType<AmmoItemClass>())
            {
                if (string.IsNullOrEmpty(ammo.Id) ||
                    !yieldedIds.Add(ammo.Id) ||
                    !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                {
                    continue;
                }

                yield return ammo;
            }
        }

        private IEnumerable<AmmoItemClass> GetFollowerWeaponLooseAmmoItems(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            bool includeStrictCargo)
        {
            if (followerEquipment == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            EquipmentSlot[] destinationOrder = FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon)
                ? LauncherLooseAmmoDestinationOrder
                : WeaponLooseAmmoDestinationOrder;
            foreach (EquipmentSlot slot in destinationOrder)
            {
                Item root = followerEquipment.GetSlot(slot)?.ContainedItem;
                foreach (AmmoItemClass ammo in SnapshotLootTreeItems(root).OfType<AmmoItemClass>())
                {
                    if (string.IsNullOrEmpty(ammo.Id) ||
                        !yieldedIds.Add(ammo.Id) ||
                        (!includeStrictCargo && InteractableObjects.IsStrictCargoItem(BotOwner, ammo)) ||
                        !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                    {
                        continue;
                    }

                    yield return ammo;
                }
            }
        }

        private IEnumerable<AmmoItemClass> GetFollowerWeaponCartridgeItems(
            InventoryEquipment followerEquipment,
            Weapon weapon)
        {
            if (followerEquipment == null || weapon == null)
            {
                yield break;
            }

            // This is an ammunition-supply snapshot, not a reload-access scan. Rounds already in
            // weapons/magazines and manually placed cargo still matter when deciding whether the
            // follower needs a weaker loose stack, even when those items cannot satisfy immediate
            // detachable-magazine readiness by themselves.
            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AmmoItemClass ammo in SnapshotLootTreeItems(followerEquipment).OfType<AmmoItemClass>())
            {
                if (string.IsNullOrEmpty(ammo.Id) ||
                    !yieldedIds.Add(ammo.Id) ||
                    !FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, ammo))
                {
                    continue;
                }

                yield return ammo;
            }
        }

        private static TacticalAmmoDecision EvaluateTacticalAmmoCandidate(
            AmmoItemClass candidate,
            IEnumerable<AmmoItemClass> carriedAmmo,
            int candidateAvailableRounds,
            int reserveTargetRounds,
            bool allowUpgrade)
        {
            List<AmmoItemClass> compatible = carriedAmmo?
                .Where(ammo =>
                    ammo != null &&
                    ammo.StackObjectsCount > 0 &&
                    FollowerWeaponLooseAmmoSupport.IsSameCaliber(candidate, ammo))
                .ToList() ?? new List<AmmoItemClass>();
            int currentRounds = compatible.Sum(ammo => Math.Max(0, ammo.StackObjectsCount));
            double weightedPenetration = currentRounds > 0
                ? compatible.Sum(ammo =>
                      (double)Math.Max(0, ammo.StackObjectsCount) * ammo.PenetrationPower) /
                  currentRounds
                : 0d;
            return FollowerTacticalAmmoPolicy.Evaluate(
                currentRounds,
                weightedPenetration,
                candidate?.PenetrationPower ?? 0,
                candidateAvailableRounds,
                reserveTargetRounds,
                allowUpgrade);
        }

        private int ResolveWeaponTacticalAmmoReserveTarget(
            InventoryController inventory,
            Weapon weapon,
            IEnumerable<AmmoItemClass> availableAmmo)
        {
            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon);
            if (readiness?.Threshold > 0)
            {
                return readiness.Threshold;
            }

            List<MagazineItemClass> availableMagazines = GetFastAccessMagazines(
                    inventory?.Inventory?.Equipment)
                .Where(magazine => IsMagazineCompatibleWithWeapon(weapon, magazine))
                .ToList();
            MagazineItemClass insertedMagazine = GetCurrentMagazineSafely(weapon);
            if (insertedMagazine != null && IsMagazineCompatibleWithWeapon(weapon, insertedMagazine))
            {
                availableMagazines.Add(insertedMagazine);
            }

            int largestAvailableMagazine = availableMagazines
                .Select(magazine => Math.Max(0, magazine.MaxCount))
                .DefaultIfEmpty(0)
                .Max();
            if (largestAvailableMagazine > 0)
            {
                return Math.Min(30, largestAvailableMagazine) * 2;
            }

            int stackCapacity = availableAmmo?
                .Where(ammo => ammo != null)
                .Select(ammo => Math.Max(1, ammo.StackMaxSize))
                .DefaultIfEmpty(1)
                .Max() ?? 1;
            return stackCapacity * (FollowerWeaponLooseAmmoSupport.IsShotgun(weapon) ? 3 : 2);
        }

        private static BodyGearCandidate CreateWeaponLooseAmmoCandidate(
            AmmoItemClass ammo,
            Weapon weapon,
            string sourceName)
        {
            return new BodyGearCandidate(
                ammo,
                null,
                sourceName,
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true,
                followUpDestination: BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                weaponSupportWeapon: weapon);
        }

        private List<BodyGearCandidate> SelectWeaponLooseAmmoSupportCandidates(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceCandidates,
            BodyGearFollowUpDestination destination,
            string evaluation,
            IEnumerable<AmmoItemClass>? additionalCarriedAmmo = null)
        {
            List<BodyGearCandidate> source = sourceCandidates?
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<BodyGearCandidate>();
            if (source.Count == 0)
            {
                return source;
            }

            // Ammunition need is based on every compatible cartridge already carried, including
            // rounds loaded in weapons and magazines. Reload readiness remains stricter elsewhere;
            // this broader snapshot only prevents collecting weaker ammunition unnecessarily.
            List<AmmoItemClass> carried = GetFollowerWeaponCartridgeItems(followerEquipment, weapon)
                .Concat(additionalCarriedAmmo ?? Enumerable.Empty<AmmoItemClass>())
                .Where(ammo => ammo != null && !string.IsNullOrEmpty(ammo.Id))
                .GroupBy(ammo => ammo.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            InventoryController inventory = BotOwner?.GetPlayer?.InventoryController;
            int reserveTargetRounds = ResolveWeaponTacticalAmmoReserveTarget(
                inventory,
                weapon,
                carried.Concat(source.Select(candidate => (AmmoItemClass)candidate.Item)));
            Dictionary<string, int> remainingSourceRoundsByTemplate = source
                .GroupBy(candidate => candidate.Item.TemplateId.ToString(), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(candidate => Math.Max(0, candidate.Item.StackObjectsCount)),
                    StringComparer.Ordinal);

            List<BodyGearCandidate> accepted = new List<BodyGearCandidate>();
            foreach (BodyGearCandidate candidate in source
                         .OrderByDescending(entry => ((AmmoItemClass)entry.Item).PenetrationPower)
                         .ThenByDescending(entry => ((AmmoItemClass)entry.Item).Damage)
                         .ThenByDescending(entry => ((AmmoItemClass)entry.Item).ArmorDamage))
            {
                AmmoItemClass ammo = (AmmoItemClass)candidate.Item;
                string ammoTemplateId = ammo.TemplateId.ToString();
                int sourceRoundsOfType = remainingSourceRoundsByTemplate.TryGetValue(
                    ammoTemplateId,
                    out int remainingRounds)
                    ? remainingRounds
                    : ammo.StackObjectsCount;
                TacticalAmmoDecision decision = EvaluateTacticalAmmoCandidate(
                    ammo,
                    carried,
                    sourceRoundsOfType,
                    reserveTargetRounds,
                    allowUpgrade: true);
                Modules.Logger.LogInfo(
                    $"[LootCommand][LooseAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluation} ammo={DescribeLooseAmmo(ammo)} " +
                    decision.ToDiagnosticString());
                if (decision.ShouldAcquire)
                {
                    accepted.Add(candidate.WithFollowUpDestination(destination));
                    // Planning several source stacks is sequential. Count accepted rounds in the
                    // next decision so a large source cannot bypass the reserve/opportunity limit
                    // merely because its stacks have different item ids.
                    carried.Add(ammo);
                    remainingSourceRoundsByTemplate[ammoTemplateId] = Math.Max(
                        0,
                        sourceRoundsOfType - ammo.StackObjectsCount);
                }
            }

            return accepted;
        }

        private BodyGearMove AppendWeaponLooseAmmoSupportFollowUps(
            BodyGearMove move,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceCandidates,
            string evaluation,
            IEnumerable<AmmoItemClass>? additionalCarriedAmmo = null)
        {
            if (move == null)
            {
                return move;
            }

            List<BodyGearCandidate> looseAmmo = SelectWeaponLooseAmmoSupportCandidates(
                followerEquipment,
                weapon,
                sourceCandidates,
                BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                evaluation,
                additionalCarriedAmmo);
            if (looseAmmo.Count == 0)
            {
                return move;
            }

            List<BodyGearCandidate> followUps = move.FollowUpCandidates.ToList();
            followUps.AddRange(looseAmmo);
            return move.WithFollowUps(
                followUps,
                move.SuccessPhrase,
                move.ContinueFollowUpsOnFailure);
        }

        private static IEnumerable<AmmoItemClass> GetOperationalMagazineCartridgeItems(
            OperationalMagazinePlan plan)
        {
            if (plan == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in plan.FollowUps.Where(IsOperationalFastAccessFollowUp))
            {
                foreach (AmmoItemClass ammo in GetMagazineCartridgeItems(candidate.Item as MagazineItemClass))
                {
                    if (!string.IsNullOrEmpty(ammo.Id) && yieldedIds.Add(ammo.Id))
                    {
                        yield return ammo;
                    }
                }
            }
        }

        private static IEnumerable<AmmoItemClass> GetMagazineCartridgeItems(MagazineItemClass magazine)
        {
            return magazine?.Cartridges?.Items?
                .OfType<AmmoItemClass>()
                .Where(ammo => ammo != null && ammo.StackObjectsCount > 0) ??
                Enumerable.Empty<AmmoItemClass>();
        }

        private bool TryBuildWeaponLooseAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            bool requireWeaponOnFollower,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidAmmo";
            if (candidate?.Item is not AmmoItemClass ammo ||
                candidate.WeaponSupportWeapon is not Weapon weapon ||
                ammo.StackObjectsCount <= 0 ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, ammo) ||
                !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
            {
                return false;
            }

            if (requireWeaponOnFollower && !IsLootNowInBotInventory(BotOwner?.GetPlayer, weapon))
            {
                reason = "weaponNotAcquired";
                return false;
            }

            EquipmentSlot[] destinationOrder = FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon)
                ? LauncherLooseAmmoDestinationOrder
                : WeaponLooseAmmoDestinationOrder;
            foreach (EquipmentSlot slot in destinationOrder)
            {
                if (slot == EquipmentSlot.TacticalVest &&
                    !CanPlaceAmmoInVestWithReloadReserve(followerEquipment, ammo))
                {
                    continue;
                }

                if (!TryFindDirectEquipmentContainerAddress(
                        followerEquipment,
                        slot,
                        ammo,
                        out ItemAddress? address) ||
                    !TryCreateBodyGearMove(
                        inventory,
                        candidate,
                        address,
                        out move,
                        storeAsLoot: ShouldReturnGearSwapAsCargo(),
                        successPhrase: EPhraseTrigger.LootGeneric))
                {
                    continue;
                }

                reason = $"ok:{slot}";
                Modules.Logger.LogInfo(
                    $"[LootCommand][LooseAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} ammo={DescribeLooseAmmo(ammo)} destination={slot}");
                return true;
            }

            reason = "noProtectedDestinationSpace";
            return false;
        }

        private bool TrySimulateWeaponLooseAmmoPlacement(
            Weapon weapon,
            AmmoItemClass ammo,
            VestReloadReserveSet vestReloadReserves,
            ref SearchableItemItemClass? secure,
            ref SearchableItemItemClass? pockets,
            ref SearchableItemItemClass? backpack,
            ref SearchableItemItemClass? vest)
        {
            if (FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon))
            {
                // Launcher rounds must remain reload-accessible whenever possible. Each simulated
                // placement mutates its cloned container, so later one-round grenades naturally
                // spill from vest to pockets to backpack before secure storage is considered.
                if (TrySimulateContainerAdd(vest, ammo, out SearchableItemItemClass? nextLauncherVest) &&
                    CanFitVestReloadReserves(nextLauncherVest, vestReloadReserves))
                {
                    vest = nextLauncherVest;
                    return true;
                }

                if (TrySimulateContainerAdd(pockets, ammo, out SearchableItemItemClass? nextLauncherPockets))
                {
                    pockets = nextLauncherPockets;
                    return true;
                }

                if (TrySimulateContainerAdd(backpack, ammo, out SearchableItemItemClass? nextLauncherBackpack))
                {
                    backpack = nextLauncherBackpack;
                    return true;
                }

                if (TrySimulateContainerAdd(secure, ammo, out SearchableItemItemClass? nextLauncherSecure))
                {
                    secure = nextLauncherSecure;
                    return true;
                }

                return false;
            }

            if (TrySimulateContainerAdd(secure, ammo, out SearchableItemItemClass? nextSecure))
            {
                secure = nextSecure;
                return true;
            }

            if (TrySimulateContainerAdd(pockets, ammo, out SearchableItemItemClass? nextPockets))
            {
                pockets = nextPockets;
                return true;
            }

            if (TrySimulateContainerAdd(backpack, ammo, out SearchableItemItemClass? nextBackpack))
            {
                backpack = nextBackpack;
                return true;
            }

            if (TrySimulateContainerAdd(vest, ammo, out SearchableItemItemClass? nextVest) &&
                CanFitVestReloadReserves(nextVest, vestReloadReserves))
            {
                vest = nextVest;
                return true;
            }

            return false;
        }

        private static string DescribeLooseAmmo(AmmoItemClass ammo)
        {
            return ammo == null
                ? "none"
                : $"{DescribeLootDebugItem(ammo)}:caliber={ammo.Caliber}:rounds={ammo.StackObjectsCount}:" +
                  $"stackMax={ammo.StackMaxSize}:pen={ammo.PenetrationPower}:damage={ammo.Damage}:armorDamage={ammo.ArmorDamage}";
        }
    }
}
