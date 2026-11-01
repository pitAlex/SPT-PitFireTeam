using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Shared;

[Flags]
public enum EArmorPlateCollider
{
    Plate_Granit_SAPI_chest = 1, Plate_Granit_SAPI_back = 2, Plate_Granit_SSAPI_side_left_high = 4,
    Plate_Korund_chest = 64, Plate_6B13_back = 128
}
public enum EBodyPartColliderType { RibcageUp, RibcageLow, SpineTop, SpineDown, LeftSideChestDown, NeckFront }

public class ArmorSlot(Item parent) : Slot(parent, EquipmentSlot.ArmorVest)
{
    public bool Locked;
    public override bool CanAccept(Item item) => item is ArmorPlate plate && plate.Fits;
}

static class Program
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main()
    {
        foreach (int basis in new[] { 0, 20, 50, 95, 100 })
        {
            Check(GiveGearInvitePolicy.Apply(basis, null) == basis, "No donation preserves base chance");
            Check(GiveGearInvitePolicy.Apply(basis, new[] { "Headwear", "Headwear", "Backpack", "Pockets", "Unknown", null }) == Math.Min(100, basis + 20),
                "Only distinct supported slots add ten percentage points, capped at 100");
        }
        Check(GiveGearInvitePolicy.Apply(-20, new[] { "FirstPrimaryWeapon" }) == 10, "Invalid base does not erase a real slot bonus");
        CheckBackpackReplacement();
        CheckFixedCompanionEquipment();
        CheckFixedCompanionSupplies();
#if DEBUG
        CheckGearDiagnostics();
#endif
        CheckPackedVestUpgrades();
        CheckPrimaryKitPriority();
        CheckDeferredSupplies();
        CheckStoredSecondaryHandover();
        CheckLowAmmoFallback();
        CheckArmoredMaskCapConflict();
        CheckWeaponCadence();
        CheckWeaponEffectiveRange();
        var cargoRig = new Vest("cargo-rig"); cargoRig.Grids = new[] { new Grid(cargoRig, 4, 4) };
        cargoRig.Grids[0].Put(Armor("cargo-helmet", 6, 1, EquipmentSlot.Headwear));
        Check(GiveGearPlanner.Protection(cargoRig) == 0, "Loose armor inside a vest is cargo, not installed protection");
        Check(GiveGearPlanner.EvaluateBodyProtection(cargoRig).Score == 0, "Loose grid armor never contributes torso protection");
        foreach (float frontCondition in new[] { -0.1f, 0f, 0.00001f, 0.3f, 1f })
        foreach (bool primary in new[] { false, true })
        {
            var s = new Scenario(); var originalRig = s.Get(EquipmentSlot.TacticalVest);
            var oldArmor = Carrier("damaged-body", 6, frontCondition, 6, 1f);
            InstallProtection(oldArmor, "soft-front", 3, 1f, 0, EBodyPartColliderType.RibcageUp);
            InstallProtection(oldArmor, "high-side", 6, 1f, EArmorPlateCollider.Plate_Granit_SSAPI_side_left_high);
            s.Equip(oldArmor, EquipmentSlot.ArmorVest);
            Weapon weapon = null; Magazine spare = null;
            if (primary) { weapon = Gun("retained-primary", 50); s.Equip(weapon, EquipmentSlot.FirstPrimaryWeapon); spare = Mag("retained-spare", 50); s.AddFast(spare); }
            var incoming = Rig("balanced-TV-style", 4, 1f, 4, 1f, 2, 4); s.Offer(incoming);
            Check(GiveGearPlanner.Protection(oldArmor) > GiveGearPlanner.Protection(incoming), "Reproduces old best-component score favoring the high rear/side plate");
            bool upgrade = frontCondition < 1f;
            Check((GiveGearPlanner.EvaluateBodyProtection(incoming).Score > GiveGearPlanner.EvaluateBodyProtection(oldArmor).Score) == upgrade,
                "Front/back comparison recognizes the damaged front, not a good rear or side plate alone");
            s.Run();
            Check((s.Get(EquipmentSlot.TacticalVest) == incoming) == upgrade &&
                (upgrade ? s.Get(EquipmentSlot.ArmorVest) == null : s.Get(EquipmentSlot.ArmorVest) == oldArmor),
                "Balanced incoming carrier replaces spent-front armor plus larger plain rig, but not a healthy higher-class kit");
            if (primary) Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == weapon && s.InFastAccess(spare) &&
                pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Smaller carrier retains primary magazines and sequential reload room");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.ArmorVest) == oldArmor && s.Get(EquipmentSlot.TacticalVest) == originalRig && s.InOffer(incoming),
                "Carrier replacement rollback restores exact equipment and offering");
        }
        {
            var empty = Carrier("empty-carrier", 0, 0, 0, 0);
            Check(GiveGearPlanner.EvaluateBodyProtection(empty).Score == 0, "Empty carrier shell has no implied protection");
            Check(GiveGearPlanner.EvaluateBodyProtection(Carrier("destroyed-plate", 6, .00001f, 0, 0)).Front == 0,
                "Native-destroyed plate contributes zero even when durability is slightly above zero");
            InstallProtection(empty, "collar", 6, 1, 0, EBodyPartColliderType.NeckFront);
            InstallProtection(empty, "side", 6, 1, EArmorPlateCollider.Plate_Granit_SSAPI_side_left_high);
            Check(GiveGearPlanner.EvaluateBodyProtection(empty).Score == 0, "Collar and side protection cannot impersonate front/back plates");
            var soft = Armor("soft-body", 3, 1, EquipmentSlot.ArmorVest);
            Check(GiveGearPlanner.EvaluateBodyProtection(soft).Score == 300, "Non-plate soft armor still scores its actual torso coverage");
            soft.Armor.ArmorColliders = new[] { EBodyPartColliderType.RibcageLow, EBodyPartColliderType.SpineDown };
            Check(GiveGearPlanner.EvaluateBodyProtection(soft).Score == 300, "Lower front/back soft coverage uses the same torso directions");
            var front = Carrier("front-only", 4, 1, 0, 0);
            var back = Carrier("back-only", 0, 0, 5, 1);
            var combined = GiveGearPlanner.EvaluateBodyProtection(front, back);
            Check(combined.Front == 400 && combined.Back == 500 && combined.Score == 425, "Compatible layers combine per-zone coverage without double-counting");
            Check(GiveGearPlanner.EvaluateBodyProtection(front).Score > GiveGearPlanner.EvaluateBodyProtection(Carrier("only-back", 0, 0, 4, 1)).Score,
                "Front protection has greater kit-comparison weight than rear protection");
            var worn = Carrier("worn", 4, .5f, 4, 1);
            Check(GiveGearPlanner.EvaluateBodyProtection(worn).Front == 250 && GiveGearPlanner.EvaluateBodyProtection(worn).Back == 400,
                "Condition is evaluated independently for each installed plate");
            var helmet = Armor("helmet-unchanged", 5, .5f, EquipmentSlot.Headwear);
            Check(GiveGearPlanner.Protection(helmet) == 312.5f, "Helmet protection retains its existing calibration");
        }
        {
            var s = new Scenario(); var old = Carrier("worn-current", 6, 0, 6, 1); s.Equip(old, EquipmentSlot.ArmorVest);
            var gun = Gun("working-primary", 50); s.Equip(gun, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(Mag("working-spare", 50));
            var tiny = Rig("no-reload-room", 4, 1, 4, 1, 1, 1); s.Offer(tiny); s.Run();
            Check(s.Get(EquipmentSlot.ArmorVest) == old && s.Get(EquipmentSlot.TacticalVest) != tiny,
                "Better protection still cannot replace a kit with no valid reload landing space");
            s.AssertConserved();
        }
        foreach (bool reverse in new[] { false, true })
        {
            var scenario = new Scenario();
            var old = Gun("old", 20); scenario.Equip(old, EquipmentSlot.FirstPrimaryWeapon);
            scenario.AddFast(Mag("old-spare", 20));
            var best = Gun("best", 50); var weaker = Gun("weaker", 30);
            foreach (var gun in reverse ? new[] { best, weaker } : new[] { weaker, best }) scenario.Offer(gun);
            scenario.Offer(Mag("new-spare", 50));
            scenario.Run();
            Check(scenario.Get(EquipmentSlot.FirstPrimaryWeapon) == best, "Best offered weapon wins independently of drag order");
            Check(scenario.Get(EquipmentSlot.SecondPrimaryWeapon) == old || scenario.InKit(old), "Displaced primary is conserved as support or cargo");
            scenario.AssertConserved();
            scenario.Plan.Rollback();
            Check(scenario.Get(EquipmentSlot.FirstPrimaryWeapon) == old && scenario.InOffer(best), "Planner rollback restores exact original equipment/offering");
        }
        {
            var s = new Scenario(); s.Equip(Gun("current", 60), EquipmentSlot.FirstPrimaryWeapon);
            s.Offer(Gun("worse", 10)); var helmet = Armor("helmet", 5, 1, EquipmentSlot.Headwear); s.Offer(helmet); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon).Id == "current" && s.Get(EquipmentSlot.Headwear) == helmet,
                "Rejected weapon does not block independent helmet upgrade"); s.AssertConserved();
        }
        foreach (bool deeper in new[] { false, true })
        {
            var s = new Scenario(); var bag = Bag("offered-bag", 6, 6);
            var inner = deeper ? Bag("inner-bag", 4, 4) : bag;
            var gun = Gun("packed-primary", 100);
            var helmet = Armor("packed-helmet", 5, 1, EquipmentSlot.Headwear);
            var armor = Armor("packed-armor", 5, 1, EquipmentSlot.ArmorVest);
            foreach (var item in new[] { (Item)gun, helmet, armor }) inner.Grids[0].Put(item);
            if (deeper) bag.Grids[0].Put(inner);
            s.Offer(bag); s.Offer(Mag("packed-primary-spare", 100)); s.Run();
            Check((s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun) == !deeper &&
                (s.Get(EquipmentSlot.Headwear) == helmet) == !deeper &&
                (s.Get(EquipmentSlot.ArmorVest) == armor) == !deeper,
                "All equipment is evaluated one container deep, never inside a second container");
            Check(s.Plan.OfferedWeaponIds.Contains(gun.Id) == !deeper,
                "Later-handover weapon history includes only directly offered and one-level weapons");
            if (deeper) Check(new[] { (Item)gun, helmet, armor }.All(i => i.CurrentAddress.Container.ParentItem == inner),
                "Two-level equipment remains intact inside the nested cargo container");
            s.AssertConserved();
            s.Plan.Rollback();
            Check(new[] { (Item)gun, helmet, armor }.All(i => i.CurrentAddress.Container.ParentItem == inner),
                "Rollback restores all packed equipment to its original container");
        }
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario(false);
            var oldRig = (Vest)s.Get(EquipmentSlot.TacticalVest);
            oldRig.Armor = new ArmorComponent { ArmorClass = 3 };
            var oldPrimary = Gun("scav-original-primary", 10); s.Equip(oldPrimary, EquipmentSlot.FirstPrimaryWeapon);
            s.Equip(Armor("scav-original-helmet", 2, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            var bag = Bag("full-kit-backpack", 10, 10);
            var gun = Gun("full-kit-primary", 70);
            var armor = Armor("full-kit-armor", 5, 1, EquipmentSlot.ArmorVest);
            var helmet = Armor("full-kit-helmet", 5, 1, EquipmentSlot.Headwear);
            var headset = new Item("full-kit-headset") { AcceptedSlot = EquipmentSlot.Earpiece };
            foreach (var item in new[] { (Item)gun, armor, helmet, headset }) bag.Grids[0].Put(item);
            var vest = new Vest("separate-supply-vest"); vest.Grids = new[] { new Grid(vest, 2, 6) };
            var magazines = Enumerable.Range(0, 3).Select(i => Mag("full-kit-spare-" + i, 70)).ToArray();
            foreach (var mag in magazines) vest.Grids[0].Put(mag);
            foreach (var item in reverse ? new Item[] { vest, bag } : new Item[] { bag, vest }) s.Offer(item);
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun && s.Get(EquipmentSlot.ArmorVest) == armor &&
                s.Get(EquipmentSlot.Headwear) == helmet && s.Get(EquipmentSlot.TacticalVest) == vest,
                "Packed weapon/helmet/armor combine with a separately offered magazine vest, regardless of drag order");
            Check(s.Get(EquipmentSlot.Backpack) == bag && s.Get(EquipmentSlot.Earpiece) == headset,
                "Packed accessories remain eligible after their containing backpack becomes equipped");
            Check(magazines.All(s.InFastAccess) && pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "The selected kit retains all fitting compatible magazines and its reload opening");
            Check(s.InKit(oldPrimary) || s.InOverflow(oldPrimary), "The original primary retains an explicit cargo/support/drop outcome");
            s.AssertConserved();
            s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == oldPrimary && s.Get(EquipmentSlot.TacticalVest) == oldRig &&
                new[] { (Item)gun, armor, helmet, headset }.All(i => i.CurrentAddress.Container.ParentItem == bag) &&
                magazines.All(i => i.CurrentAddress.Container.ParentItem == vest),
                "Full packed-kit rollback restores exact original slots and both donated container trees");
        }
        foreach (bool deeper in new[] { false, true })
        {
            var s = new Scenario(); var gun = Gun("offered", 50); s.Offer(gun);
            var bag = Bag("supply-bag", 6, 6); var magazine = Mag("bag-mag", 50);
            if (deeper) { var inner = Bag("inner", 2, 2); inner.Grids[0].Put(magazine); bag.Grids[0].Put(inner); }
            else bag.Grids[0].Put(magazine);
            s.Offer(bag); s.Run();
            Check((s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun) == !deeper,
                "Magazine supply is allowed one level deep, not in nested containers"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var current = Gun("case-current-primary", 90);
            s.Equip(current, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(Mag("case-current-spare", 90));
            var box = new SearchableItem("equipment-case"); box.Grids = new[] { new Grid(box, 10, 10) };
            var rig = new Vest("packed-armored-rig") { Armor = new ArmorComponent { ArmorClass = 5 } };
            rig.Grids = new[] { new Grid(rig, 4, 4) };
            var bag = Bag("packed-bigger-backpack", 9, 9);
            var secondary = Gun("packed-secondary", 20); var pistol = Gun("packed-pistol", 10); pistol.Pistol = true;
            var face = new Item("packed-face-cover") { AcceptedSlot = EquipmentSlot.FaceCover };
            var eyes = new Item("packed-eyewear") { AcceptedSlot = EquipmentSlot.Eyewear };
            var ears = new Item("packed-earpiece") { AcceptedSlot = EquipmentSlot.Earpiece };
            var items = new Item[] { rig, bag, secondary, pistol, face, eyes, ears,
                Mag("packed-secondary-spare", 20), Mag("packed-pistol-spare", 10) };
            foreach (var item in items) box.Grids[0].Put(item);
            s.Offer(box); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.Get(EquipmentSlot.SecondPrimaryWeapon) == secondary &&
                s.Get(EquipmentSlot.Holster) == pistol, "One-level weapons use the existing primary/secondary/holster rules");
            Check(s.Get(EquipmentSlot.TacticalVest) == rig && s.Get(EquipmentSlot.Backpack) == bag &&
                s.Get(EquipmentSlot.FaceCover) == face && s.Get(EquipmentSlot.Eyewear) == eyes && s.Get(EquipmentSlot.Earpiece) == ears,
                "The one-level rule covers vests, backpacks and every empty accessory slot, including generic offered containers");
            s.AssertConserved(); s.Plan.Rollback();
            Check(items.All(i => i.CurrentAddress.Container.ParentItem == box), "Rollback restores all one-level categories to their original case");
        }
        {
            var s = new Scenario(); var box = new SearchableItem("outer-case"); box.Grids = new[] { new Grid(box, 4, 4) };
            var bag = Bag("inner-upgrade-backpack", 9, 9);
            var gun = Gun("two-level-weapon", 100);
            var helmet = Armor("two-level-helmet", 6, 1, EquipmentSlot.Headwear);
            var armor = Armor("two-level-armor", 6, 1, EquipmentSlot.ArmorVest);
            var ears = new Item("two-level-headset") { AcceptedSlot = EquipmentSlot.Earpiece };
            var items = new Item[] { gun, helmet, armor, ears, Mag("two-level-spare", 100) };
            foreach (var item in items) bag.Grids[0].Put(item);
            box.Grids[0].Put(bag); s.Offer(box); s.Run();
            Check(s.Get(EquipmentSlot.Backpack) == bag && s.Get(EquipmentSlot.FirstPrimaryWeapon) == null &&
                s.Get(EquipmentSlot.Headwear) == null && s.Get(EquipmentSlot.ArmorVest) == null && s.Get(EquipmentSlot.Earpiece) == null,
                "Equipping a one-level backpack does not expose its originally two-level contents as new candidates");
            Check(items.All(i => i.CurrentAddress.Container.ParentItem == bag) && !s.Plan.OfferedWeaponIds.Contains(gun.Id),
                "Deeper equipment/supplies stay intact and out of later weapon history after their container moves");
            s.AssertConserved(); s.Plan.Rollback();
            Check(bag.CurrentAddress.Container.ParentItem == box && items.All(i => i.CurrentAddress.Container.ParentItem == bag),
                "Rollback restores the complete deeper container tree");
        }
        foreach (bool laterHandover in new[] { false, true })
        {
            var s = new Scenario(); var keptVest = s.Get(EquipmentSlot.TacticalVest);
            var primary = Gun("vest-supported-primary", 70); s.Offer(primary);
            if (laterHandover)
            {
                s.Offer(Mag("initial-spare", 70)); s.Run(); s.AssertConserved();
            }
            var vest = new Vest("offered-supply-vest"); vest.Grids = new[] { new Grid(vest, 4, 4) };
            var magazines = Enumerable.Range(0, 3).Select(i => Mag("vest-spare-" + i, 70)).ToArray();
            foreach (var magazine in magazines) vest.Grids[0].Put(magazine);
            var ordinaryLoot = new Item("vest-loot") { Width = 1, Height = 1 }; vest.Grids[0].Put(ordinaryLoot);
            s.Offer(vest); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.TacticalVest) == keptVest,
                "A supported primary keeps the current vest instead of requiring the offered vest");
            Check(magazines.All(s.InFastAccess),
                "All fitting offered-vest magazines reach fast access, including a later supply-only handover");
            Check(ordinaryLoot.CurrentAddress.Container.ParentItem == vest,
                "Magazine extraction does not unpack ordinary vest cargo");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Additional primary magazines preserve the reload opening"); s.AssertConserved();
            s.Plan.Rollback();
            Check(magazines.All(m => m.CurrentAddress.Container.ParentItem == vest),
                "Rollback restores every extracted magazine to the original offered vest");
        }
        foreach (bool oversized in new[] { false, true })
        {
            var s = new Scenario(); var keptVest = new Vest("limited-vest");
            keptVest.Grids = new[] { new Grid(keptVest, 1, 6) }; s.Equip(keptVest, EquipmentSlot.TacticalVest);
            s.Equip(Armor("retained-body-protection", 4, 1, EquipmentSlot.ArmorVest), EquipmentSlot.ArmorVest);
            var primary = Gun("limited-primary", 70); s.Offer(primary);
            var incoming = Rig("incoming-full-vest", 1, 1, 1, 1, 4, 4);
            var first = Mag("a-first-spare", 70); var blocked = Mag("b-blocked-spare", 70); var last = Mag("c-last-spare", 70);
            if (oversized) blocked.Height = 4;
            foreach (var magazine in new[] { first, blocked, last }) incoming.Grids[0].Put(magazine);
            s.Offer(incoming); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.TacticalVest) == keptVest,
                "Partial magazine acceptance does not force a lower-protection carrier or reject the primary");
            Check(new[] { first, blocked, last }.Count(s.InFastAccess) == 2,
                "Extra magazines fill available space but never occupy the last reload landing opening");
            Check((oversized ? blocked : last).CurrentAddress.Container.ParentItem == incoming,
                "A magazine that would block reloading stays inside the unused offered vest");
            Check(!oversized || s.InFastAccess(last), "A failed larger magazine does not skip a later fitting magazine");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Limited-capacity extraction still permits the inserted magazine to land"); s.AssertConserved();
        }
        foreach (bool usePockets in new[] { false, true })
        {
            var s = new Scenario(); var vest = new Vest("wide-reload-vest");
            var reloadGrid = new Grid(vest, 2, 2);
            var alternate = usePockets ? new SearchableItem("normal-pockets") { AcceptedSlot = EquipmentSlot.Pockets } : vest;
            var supplyGrid = new Grid(alternate, 1, 4);
            vest.Grids = usePockets ? new[] { reloadGrid } : new[] { reloadGrid, supplyGrid };
            if (usePockets) { alternate.Grids = new[] { supplyGrid }; s.Equip(alternate, EquipmentSlot.Pockets); }
            s.Equip(vest, EquipmentSlot.TacticalVest);
            var primary = Gun("wide-loaded-primary", 70); primary.GetCurrentMagazine().Width = 2;
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var incoming = new Vest("offered-alternate-supply"); incoming.Grids = new[] { new Grid(incoming, 2, 2) };
            var magazines = new[] { Mag("alternate-a", 70), Mag("alternate-b", 70) };
            foreach (var magazine in magazines) incoming.Grids[0].Put(magazine);
            s.Offer(incoming); s.Run();
            Check(magazines.All(m => m.CurrentAddress.Container == supplyGrid),
                "Magazine placement tries another vest grid or normal pockets when the first grid is the reload opening");
            Check(!reloadGrid.Items.Any(), "The only opening fitting the wider inserted magazine remains empty");
            Check(FollowerWeaponPrimaryReadiness.EvaluateActual(s.Controller, primary).PrimaryReady,
                "Alternate-grid supply restores primary readiness without a weapon or vest replacement"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var primary = Gun("compatible-primary", 70); s.Offer(primary);
            var vest = new Vest("mixed-mag-vest"); vest.Grids = new[] { new Grid(vest, 4, 4) };
            var matching = new[] { Mag("matching-a", 70), Mag("matching-b", 70) };
            var wrongMagazine = Mag("wrong-magazine", 70); wrongMagazine.MechanicallyCompatible = false;
            var wrongAmmo = Mag("wrong-ammunition", 70); wrongAmmo.LoadedCartridgesCompatible = false;
            foreach (var magazine in matching.Concat(new[] { wrongMagazine, wrongAmmo })) vest.Grids[0].Put(magazine);
            s.Offer(vest); s.Run();
            Check(matching.All(s.InFastAccess), "All fitting compatible magazines are extracted");
            Check(new[] { wrongMagazine, wrongAmmo }.All(m => m.CurrentAddress.Container.ParentItem == vest),
                "Incompatible magazines and loaded cartridges remain cargo rather than operational supplies"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var old = Bag("old-bag", 2, 2); var content = new Item("old-content") { Width = 2, Height = 2 };
            old.Grids[0].Put(content); s.Equip(old, EquipmentSlot.Backpack); var bigger = Bag("bigger", 3, 3); s.Offer(bigger); s.Run();
            Check(s.Get(EquipmentSlot.Backpack) == bigger && bigger.Grids[0].Items.Contains(content), "Larger backpack migrates real old contents");
            Check(!old.Grids[0].Items.Any() && s.InOverflow(old) && !s.InKit(old), "Empty displaced backpack always goes to the drop plan"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var old = Bag("old-wide", 2, 2); var wide = new Item("wide") { Width = 2, Height = 2 };
            old.Grids[0].Put(wide); s.Equip(old, EquipmentSlot.Backpack); var bigger = Bag("bigger-narrow", 1, 6); s.Offer(bigger); s.Run();
            Check(s.Get(EquipmentSlot.Backpack) == old && old.Grids[0].Items.Contains(wide), "More cells cannot bypass backpack geometry/migration"); s.AssertConserved();
        }
        {
            var s = new Scenario(false); s.Equip(Armor("original-helmet", 2, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            var better = Armor("better-helmet", 5, 1, EquipmentSlot.Headwear); s.Offer(better); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == better && s.InOverflowId("original-helmet"), "Better equipment displaces old kit to ground-drop plan when cargo is full");
            s.AssertConserved();
        }
        {
            var s = new Scenario(); s.Equip(Armor("healthy", 5, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            var damaged = Armor("nearly-broken", 6, 0.01f, EquipmentSlot.Headwear); s.Offer(damaged); s.Run();
            Check(s.Get(EquipmentSlot.Headwear).Id == "healthy", "Nearly broken high-class helmet is not treated as fully protected"); s.AssertConserved();
        }
        foreach (bool cargo in new[] { false, true })
        foreach (bool beanie in new[] { false, true })
        foreach (bool reverseConflict in new[] { false, true })
        {
            var s = new Scenario(cargo);
            var face = new Item("cosmetic-beard") { AcceptedSlot = EquipmentSlot.FaceCover };
            var helmet = Armor("ACHHC-style", 4, 1, EquipmentSlot.Headwear);
            (reverseConflict ? face : helmet).Conflicts.Add((reverseConflict ? helmet : face).Id);
            var old = beanie ? Armor("old-beanie", 0, 1, EquipmentSlot.Headwear) : null;
            if (old != null) s.Equip(old, EquipmentSlot.Headwear);
            s.Equip(face, EquipmentSlot.FaceCover); s.Offer(helmet); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == helmet && s.Get(EquipmentSlot.FaceCover) == null,
                "Protective helmet displaces a conflicting cosmetic beard, with or without existing headwear");
            Check(cargo ? s.InKit(face) : s.InOverflow(face), "Displaced face cover is carried when possible, otherwise explicitly dropped");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Headwear) == old && s.Get(EquipmentSlot.FaceCover) == face && s.InOffer(helmet),
                "Rollback restores exact headwear, beard and offering after cosmetic displacement");
        }
        foreach (bool protective in new[] { false, true })
        {
            var s = new Scenario(); var old = Armor("old-beanie", 0, 1, EquipmentSlot.Headwear);
            s.Equip(old, EquipmentSlot.Headwear);
            var face = protective ? Armor("protective-mask", 3, 1, EquipmentSlot.FaceCover) :
                new Item("nonconflicting-mask") { AcceptedSlot = EquipmentSlot.FaceCover };
            var helmet = Armor("new-helmet", 5, 1, EquipmentSlot.Headwear);
            if (protective) helmet.Conflicts.Add(face.Id);
            s.Equip(face, EquipmentSlot.FaceCover); s.Offer(helmet); s.Run();
            Check(s.Get(EquipmentSlot.FaceCover) == face && s.Get(EquipmentSlot.Headwear) == (protective ? old : helmet),
                "Keep protective conflicting masks and compatible cosmetic face covers");
            s.AssertConserved();
        }
        {
            var s = new Scenario(); var old = Armor("old-beanie", 0, 1, EquipmentSlot.Headwear);
            var face = new Item("beard") { AcceptedSlot = EquipmentSlot.FaceCover };
            var ears = new Item("headset") { AcceptedSlot = EquipmentSlot.Earpiece };
            var helmet = Armor("doubly-blocked-helmet", 5, 1, EquipmentSlot.Headwear);
            helmet.Conflicts.UnionWith(new[] { face.Id, ears.Id });
            s.Equip(old, EquipmentSlot.Headwear); s.Equip(face, EquipmentSlot.FaceCover); s.Equip(ears, EquipmentSlot.Earpiece);
            s.Offer(helmet); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == old && s.Get(EquipmentSlot.FaceCover) == face && s.Get(EquipmentSlot.Earpiece) == ears,
                "Failed cosmetic retry restores every existing slot; real headset restrictions remain enforced");
            s.AssertConserved();
        }
        {
            var s = new Scenario(); var healthy = Armor("healthy-helmet", 5, 1, EquipmentSlot.Headwear);
            var face = new Item("beard") { AcceptedSlot = EquipmentSlot.FaceCover };
            var inferior = Armor("inferior-helmet", 4, 1, EquipmentSlot.Headwear); inferior.Conflicts.Add(face.Id);
            s.Equip(healthy, EquipmentSlot.Headwear); s.Equip(face, EquipmentSlot.FaceCover); s.Offer(inferior); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == healthy && s.Get(EquipmentSlot.FaceCover) == face,
                "Cosmetic conflict resolution never admits an inferior helmet"); s.AssertConserved();
        }
        {
            var s = new Scenario(); s.Equip(Gun("current", 20), EquipmentSlot.FirstPrimaryWeapon);
            var loaded = Gun("loaded", 60); s.Offer(loaded); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon).Id == "current", "Insufficient primary magazine support prevents an unsupported upgrade"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var oldRig = new Vest("armored-old") { Armor = new ArmorComponent { ArmorClass = 3 } };
            oldRig.Grids = new[] { new Grid(oldRig, 4, 4) }; s.Equip(oldRig, EquipmentSlot.TacticalVest);
            s.Equip(Gun("old-primary", 20), EquipmentSlot.FirstPrimaryWeapon);
            var armor = Armor("separate-armor", 5, 1, EquipmentSlot.ArmorVest);
            var smallRig = new Vest("small-rig"); smallRig.Grids = new[] { new Grid(smallRig, 2, 2) };
            var better = Gun("better-primary", 55); s.Offer(armor); s.Offer(smallRig); s.Offer(better); s.Offer(Mag("new-reserve", 55));
            s.Run();
            Check(s.Get(EquipmentSlot.ArmorVest) == armor && s.Get(EquipmentSlot.TacticalVest) == smallRig &&
                s.Get(EquipmentSlot.FirstPrimaryWeapon) == better, "Better primary and armor can use a smaller separate vest instead of an armored rig");
            s.AssertConserved();
        }
        {
            var s = new Scenario(); var tiny = new Vest("tiny"); tiny.Grids = new[] { new Grid(tiny, 1, 2) }; s.Equip(tiny, EquipmentSlot.TacticalVest);
            s.Equip(Gun("old", 20), EquipmentSlot.FirstPrimaryWeapon); var better = Gun("better", 60);
            var vest = new Vest("bigger-vest"); vest.Grids = new[] { new Grid(vest, 2, 2) };
            s.Offer(better); s.Offer(vest); s.Offer(Mag("reserve", 60)); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == better && s.Get(EquipmentSlot.TacticalVest) == vest,
                "A weapon can use an incoming vest without requiring incoming armor"); s.AssertConserved();
        }
        {
            var s = new Scenario(); s.Equip(Gun("old", 20), EquipmentSlot.FirstPrimaryWeapon);
            var later = Gun("later", 70); s.Offer(later); s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon).Id == "old" && s.InKit(later), "Unsupported first gift is carried, not forced into service");
            s.Bot.Gifts.Add(later.Id); s.Offer(Mag("later-reserve", 70)); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == later, "A still-carried earlier gift is reconsidered when support arrives later"); s.AssertConserved();
        }
        foreach (bool existingPrimary in new[] { false, true })
        foreach (int looseRounds in new[] { 10, 25, 40 })
        {
            var s = new Scenario(); var gun = Gun("partial-primary", 70);
            gun.GetCurrentMagazine().Cartridges.Items[0].StackObjectsCount = 5;
            var spare = Mag("partial-spare", 70);
            spare.Cartridges.Items[0].StackObjectsCount = 20;
            var ammo = new Ammo("top-off-ammo") { StackObjectsCount = looseRounds, PenetrationPower = 70 };
            if (existingPrimary) { s.Equip(gun, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(spare); }
            else { s.Offer(gun); s.Offer(spare); }
            s.Offer(ammo); s.Run();
            if (looseRounds >= 35)
            {
                Check(gun.GetCurrentMagazine().Count == 5 && spare.Count == 20 && ammo.StackObjectsCount == looseRounds,
                    "Readiness allocation never loads or splits real draft ammunition");
            }
            Check((s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun) == (existingPrimary || looseRounds >= 35),
                "Top-off permits a genuinely ready new primary without relaxing readiness for insufficient rounds");
            s.AssertConserved(); s.Plan.Rollback();
            Check(gun.GetCurrentMagazine().Count == 5 && spare.Count == 20 && ammo.StackObjectsCount == looseRounds,
                "Rollback restores inserted/spare counts and exact loose-ammunition quantity");
        }
        foreach (bool donatedAmmo in new[] { false, true })
        {
            var s = new Scenario(); s.Equip(Gun("old", 20), EquipmentSlot.FirstPrimaryWeapon);
            var candidate = new Weapon("empty-new"); candidate.Magazine.Put(new Magazine("empty-loaded"));
            var ammo = new Ammo("loose-ammo") { StackObjectsCount = 90, PenetrationPower = 70 };
            if (donatedAmmo) s.Offer(ammo); else s.AddFast(ammo);
            s.Offer(candidate); s.Offer(new Magazine("empty-spare")); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == candidate && candidate.GetCurrentMagazine().Count == 0 && ammo.StackObjectsCount == 90,
                "Loose compatible ammo plus empty magazines supports a deferred-fill primary package");
            Check(s.NativeSplits.Count == 0, "Planning never creates cartridge identities");
            s.AssertConserved();
        }
        {
            var s = new Scenario();
            var gun = Gun("diagnostic-primary", 70); gun.GetCurrentMagazine().Cartridges.Items[0].StackObjectsCount = 5;
            s.Offer(gun); s.Offer(Mag("diagnostic-spare", 70));
            var ammo = new Ammo("diagnostic-ammo") { StackObjectsCount = 25, PenetrationPower = 70 }; s.Offer(ammo);
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun && gun.GetCurrentMagazine().Count == 5,
                "Planning selects the supported primary without changing its inserted ammunition");
            s.AssertConserved(); s.Plan.Rollback();
            Check(gun.GetCurrentMagazine().Count == 5 && ammo.StackObjectsCount == 25,
                "Exact native-operation rollback preserves the original ammunition counts");
        }
        {
            var s = new Scenario(); s.Equip(Gun("diagnostic-current", 90), EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(Mag("diagnostic-current-spare", 90));
            var worse = Gun("diagnostic-worse", 10); s.Offer(worse); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon).Id == "diagnostic-current",
                "A supported stronger primary is not replaced by a weaker offering"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var vest = new Vest("diagnostic-tiny-vest");
            vest.Grids = new[] { new Grid(vest, 1, 2) }; s.Equip(vest, EquipmentSlot.TacticalVest);
            var gun = Gun("diagnostic-no-space", 70); s.Offer(gun); s.Offer(Mag("diagnostic-blocking-spare", 70)); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) != gun,
                "An offering without safe reload landing space cannot become primary"); s.AssertConserved();
        }
        {
            var s = new Scenario(); var primary = Gun("keep-primary", 90); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(Mag("primary-spare", 90));
            var support = Gun("support", 10); var spare = Mag("support-spare", 10);
            s.Offer(support); s.Offer(spare); s.Run();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == support &&
                ((SearchableItem)s.Get(EquipmentSlot.TacticalVest)).Grids.Any(g => g.Items.Contains(spare)),
                "Support acquisition puts offered spare magazines in fast access, not merely backpack cargo");
            Check(FollowerWeaponPrimaryReadiness.EvaluateActual(s.Controller, primary).PrimaryReady,
                "Optional support acquisition preserves operational primary readiness"); s.AssertConserved();
        }
        {
            var s = new Scenario(); s.Equip(Gun("original-primary", 10), EquipmentSlot.FirstPrimaryWeapon);
            var loan = Gun("borrowed-primary", 70); var spare = Mag("borrowed-spare", 70);
            var helmet = Armor("borrowed-helmet", 6, 1, EquipmentSlot.Headwear);
            s.Offer(loan); s.Offer(spare); s.Offer(helmet);
            foreach (var item in new Item[] { loan, spare, helmet }.SelectMany(TeammateGearSwap.Tree)) s.BorrowedIds.Add(item.Id);
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == loan && s.Get(EquipmentSlot.Headwear) == helmet,
                "Non-permanent borrowed weapon, magazines and helmet are eligible raid upgrades");
            s.AssertConserved();
        }
        CheckCommandSourceTransactions();
        Console.WriteLine($"PASS: {checks} production Give Gear planner/invitation checks.");
    }
    static void CheckCommandSourceTransactions()
    {
        foreach (int failure in new[] { 0, 1, 2 })
        {
            var s = new Scenario();
            var old = Gun("old-command-primary", 10); var oldSpare = Mag("old-command-spare", 10);
            s.Equip(old, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(oldSpare);
            var source = new SearchableItem("command-source"); source.Grids = new[] { new Grid(source, 12, 12) };
            var gun = Gun("command-new-primary", 80); var spare = Mag("command-new-spare", 80);
            var helmet = Armor("command-new-helmet", 4, 1, EquipmentSlot.Headwear);
            foreach (var item in new Item[] { gun, spare, helmet }) source.Grids[0].Put(item);
            var roots = new Item[] { source, s.Controller.Inventory.Equipment, s.OfferRoot, s.Overflow };
            var before = roots.SelectMany(TeammateGearSwap.Tree).ToArray();
            var beforeIds = before.Select(i => i.Id).ToHashSet();
            int rounds = before.OfType<Ammo>().Sum(i => i.StackObjectsCount);
            var edits = new List<Func<Action>>();
            foreach (Item item in new Item[] { gun, spare, helmet })
                edits.Add(() =>
                {
                    var move = ItemManipulator.Move(item, s.OfferRoot.Grids[0].FindLocationForItem(item), s.Controller, false);
                    if (move.Failed) throw new Exception(move.Error.ToString());
                    return move.Value.RollBack;
                });
            edits.Add(() =>
            {
                if (failure == 1) throw new InvalidOperationException("source changed before planning");
                s.Run();
                return s.Plan.Rollback;
            });
            bool rejected = false;
            try { GearSwapTransaction.Execute(edits, undo => undo(), () => { if (failure == 2) throw new InvalidOperationException("source replay mismatch"); }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected == (failure != 0), "Command source prelude and planner share one transaction outcome");
            if (failure == 0)
            {
                Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun && s.Get(EquipmentSlot.Headwear) == helmet,
                    "Commanded source kit uses actual Give Gear weapon and helmet decisions");
                Check(!source.Grids[0].Items.Any() && s.InKit(old), "Accepted source leaves corpse and displaced weapon is retained as cargo/support");
            }
            else
            {
                Check(source.Grids[0].Items.ToHashSet().SetEquals(new Item[] { gun, spare, helmet }), "Rejected command returns every source root");
                Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == old && s.Get(EquipmentSlot.Headwear) == null && s.InFastAccess(oldSpare),
                    "Rejected command restores original complete kit and support");
                Check(!s.OfferRoot.Grids[0].Items.Any() && !s.Overflow.Grids[0].Items.Any(), "Rejected command leaves no escrow cargo");
            }
            var after = roots.SelectMany(TeammateGearSwap.Tree).ToArray();
            Check(after.Select(i => i.Id).ToHashSet().SetEquals(beforeIds) && after.Length == before.Length, "Three-party source/offer/kit transfer conserves exact identities");
            Check(after.OfType<Ammo>().Sum(i => i.StackObjectsCount) == rounds, "Three-party transfer conserves actual rounds");
        }
    }
    static void CheckDeferredSupplies()
    {
        // This exact-fill package used to consume a root stack through native Merge, then
        // try to carry the removed root. Native loading is forbidden by the fixture below.
        var exact = new Scenario(); var gun = Gun("exact-primary", 70); SetRounds(gun.GetCurrentMagazine(), 5);
        var spare = Mag("exact-spare", 70); var exactAmmo = new Ammo("exact-ammo") { StackObjectsCount = 25, PenetrationPower = 70 };
        exact.Offer(gun); exact.Offer(spare); exact.Offer(exactAmmo); exact.Run();
        Check(exact.Get(EquipmentSlot.FirstPrimaryWeapon) == gun && exact.InKit(exactAmmo) && exactAmmo.StackObjectsCount == 25,
            "Exact-fill ammunition remains a complete carried root rather than a consumed/phantom stack");
        Check(gun.GetCurrentMagazine().Count == 5 && spare.Count == 30 && exact.NativeSplits.Count == 0,
            "Give Gear does not load, split or create ammunition during a successful exchange");
        exact.AssertConserved(); exact.Plan.Rollback(); Check(exact.InOffer(exactAmmo), "Exact-fill rollback restores the offered root");
        {
            var s = new Scenario(); var single = Gun("single-large-mag-primary", 70); var magazine = single.GetCurrentMagazine();
            magazine.MaxCount = 60; SetRounds(magazine, 0);
            var rounds = new Ammo("single-large-mag-ammo") { StackObjectsCount = 60, PenetrationPower = 70 };
            s.Offer(single); s.Offer(rounds); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == single && single.GetCurrentMagazine() == null && s.InFastAccess(magazine),
                "A sole empty large magazine is staged as a reachable spare for ordinary fill/reload");
            Check(magazine.Count == 0 && rounds.StackObjectsCount == 60, "Even lone-magazine preparation leaves rounds untouched");
            s.AssertConserved(); s.Plan.Rollback(); Check(single.GetCurrentMagazine() == magazine, "Deferred lone-magazine placement rolls back exactly");
        }

        foreach (int stock in new[] { 20, 55, 100, 200 })
        {
            var s = new Scenario(); var primary = Gun("budget-primary", 70); SetRounds(primary.GetCurrentMagazine(), 5);
            var support = Gun("budget-support", 70); SetRounds(support.GetCurrentMagazine(), 10);
            support.Pistol = true;
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon); s.Equip(support, EquipmentSlot.Holster);
            var primarySpare = Mag("budget-primary-spare", 70); var supportSpare = Mag("budget-support-spare", 70); SetRounds(supportSpare, 0);
            s.AddFast(primarySpare); s.AddFast(supportSpare);
            var first = new Ammo("budget-small-stack") { StackObjectsCount = Math.Min(stock, 10) };
            var second = new Ammo("budget-large-stack") { StackObjectsCount = stock - first.StackObjectsCount };
            ((Backpack)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(first);
            ((Backpack)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(second);
            var budget = FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment,
                (weapon, magazine) => weapon == primary ? magazine == primarySpare : magazine == supportSpare);
            int supportFill = new[] { first, second }.Sum(stack => budget.Fill(support.GetCurrentMagazine(), stack) + budget.Fill(supportSpare, stack));
            Check(supportFill == Math.Min(50, Math.Max(0, stock - 55)), "A shared-ammo pistol uses only surplus after primary fill and tactical reserve");
            Check(budget.Rounds(primary) == 35 + Math.Min(25, stock), "Primary receives ammunition first, independently of the held weapon");
            foreach (Ammo stack in new[] { first, second })
                Check(new[] { primary.GetCurrentMagazine(), primarySpare, support.GetCurrentMagazine(), supportSpare }.Sum(m => budget.Fill(m, stack)) <= stack.StackObjectsCount,
                    "Multiple magazine projections never allocate a round twice");
            Check(!budget.Owns(support, primarySpare), "Support cannot claim the primary's registered magazine");
            Check(first.StackObjectsCount + second.StackObjectsCount == stock && primary.GetCurrentMagazine().Count == 5,
                "Supply allocation is read-only");
            var shared = FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment);
            Check(shared.Owns(primary, supportSpare) && !shared.Owns(support, supportSpare), "Shared unassigned magazines get primary priority");
        }
        {
            var s = new Scenario(); var primary = FamilyGun("distinct-primary", 70, "rifle");
            var support = FamilyGun("distinct-secondary", 70, "pistol"); SetRounds(support.GetCurrentMagazine(), 5);
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon); s.Equip(support, EquipmentSlot.SecondPrimaryWeapon);
            var ammo = new Ammo("independent-ammo") { WeaponFamily = "pistol", StackObjectsCount = 60 }; s.AddFast(ammo);
            var budget = FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment);
            Check(budget.Fill(support.GetCurrentMagazine(), ammo) == 25, "An independent caliber can refill despite a primary shortage");
        }
        {
            var s = new Scenario(false); var rig = (Vest)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 1, 4) };
            var primary = Gun("secure-primary", 70); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(Mag("secure-spare", 70));
            var secure = Bag("secure", 4, 4); secure.AcceptedSlot = EquipmentSlot.SecuredContainer; s.Equip(secure, EquipmentSlot.SecuredContainer);
            var native = new Ammo("native-secure-stack") { StackObjectsCount = 40 }; secure.Grids[0].Put(native);
            var donation = new Ammo("secure-donation") { StackObjectsCount = 25 }; s.Offer(donation); s.Run();
            Check(donation.CurrentAddress.Container.ParentItem == secure && donation.StackObjectsCount == 25 && native.StackObjectsCount == 40,
                "A full rig/no backpack uses secure ammo overflow without merging native reserve stacks");
            s.AssertConserved(); s.Plan.Rollback(); Check(s.InOffer(donation) && native.CurrentAddress.Container.ParentItem == secure,
                "Secure ammo overflow rolls back without changing existing contents");
        }
        {
            var s = new Scenario(false); var secure = Bag("temporary-secure", 4, 4); secure.AcceptedSlot = EquipmentSlot.SecuredContainer;
            s.Equip(secure, EquipmentSlot.SecuredContainer);
            var old = Armor("temporary-helmet", 2, 1, EquipmentSlot.Headwear); old.Width = old.Height = 2; s.Equip(old, EquipmentSlot.Headwear);
            var better = Armor("new-helmet", 4, 1, EquipmentSlot.Headwear); s.Offer(better); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == better && old.CurrentAddress.Container.ParentItem == s.Overflow,
                "Temporary secure staging never turns displaced ordinary gear into hidden permanent cargo");
            Check(!secure.Grids[0].Items.Any(), "Temporary secure space is cleared before commit");
            s.AssertConserved(); s.Plan.Rollback(); Check(s.Get(EquipmentSlot.Headwear) == old && s.InOffer(better),
                "Temporary staging retains exact transaction rollback");
        }
        {
            var s = new Scenario(false); var rig = (Vest)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 1, 4) };
            var old = Gun("scratch-old-primary", 10); s.Equip(old, EquipmentSlot.FirstPrimaryWeapon);
            s.Equip(Gun("scratch-retained-secondary", 10), EquipmentSlot.SecondPrimaryWeapon); s.AddFast(Mag("scratch-shared-spare", 70));
            var secure = Bag("scratch-secure", 4, 2); secure.AcceptedSlot = EquipmentSlot.SecuredContainer; s.Equip(secure, EquipmentSlot.SecuredContainer);
            var replacement = Gun("scratch-replacement", 100); var rounds = new Ammo("scratch-donated-rounds") { StackObjectsCount = 25, PenetrationPower = 100 };
            s.Offer(replacement); s.Offer(rounds); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == replacement && rounds.CurrentAddress.Container.ParentItem == secure,
                "Temporary old gear yields secure space to retained ammunition instead of causing an ammo drop");
            s.AssertConserved(); s.Plan.Rollback(); Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == old && s.InOffer(rounds),
                "Evacuating scratch space is fully reversible");
        }
        {
            var s = new Scenario(); var secure = Bag("demotion-secure", 4, 2); secure.AcceptedSlot = EquipmentSlot.SecuredContainer;
            s.Equip(secure, EquipmentSlot.SecuredContainer);
            var original = Gun("demotion-original", 10); s.Equip(original, EquipmentSlot.FirstPrimaryWeapon);
            var replacement = Gun("demotion-replacement", 100); s.Offer(replacement); s.Offer(Mag("demotion-spare", 100)); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == replacement && s.Get(EquipmentSlot.SecondPrimaryWeapon) == original,
                "An original primary staged in secure storage remains eligible for secondary, not just escrow weapons");
            s.AssertConserved(); s.Plan.Rollback(); Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == original && s.InOffer(replacement),
                "Secure-staged primary demotion rolls back exactly");
        }
    }
    static void CheckFixedCompanionSupplies()
    {
        var policy = new CompanionGearPolicy(true, true, 0.10f);
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = FamilyGun("new-secondary-primary", 40, "primary"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("new-secondary-primary-spare", 40, "primary"));
            var secondary = FamilyGun("new-secondary-gift", 40, "secondary");
            var magazine = FamilyMag("new-secondary-spare", 40, "secondary");
            s.Offer(secondary); s.Offer(magazine); s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == secondary && s.InFastAccess(magazine),
                "Fixed kit can accept a qualifying secondary together with its compatible spare magazine");
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && primary.GetCurrentMagazine().Count == 30,
                "Secondary package leaves the primary and its ammunition intact");
            s.Plan.Rollback(); Check(s.InOffer(secondary) && s.InOffer(magazine), "New secondary package rolls back exactly");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("depleted-primary", 40); primary.GetCurrentMagazine().Cartridges.Items.Clear();
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var small = new Ammo("small-useful-gift") { StackObjectsCount = 3 }; s.Offer(small);
            s.Run(); s.AssertConserved();
            Check(s.InKit(small) && primary.GetCurrentMagazine().Count == 0 && small.StackObjectsCount == 3,
                "Small compatible ammo gift is accepted for a depleted primary without packing its inserted magazine");
            s.Plan.Rollback(); Check(s.InOffer(small), "Depleted-primary supply gift rolls back exactly");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            s.Equip(Gun("secure-ammo-primary", 40), EquipmentSlot.FirstPrimaryWeapon);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 1, 2) };
            var secure = Bag("ammo-secure", 1, 1); secure.AcceptedSlot = EquipmentSlot.SecuredContainer;
            s.Equip(secure, EquipmentSlot.SecuredContainer);
            var ammo = new Ammo("secure-ammo-gift"); s.Offer(ammo); s.Run(); s.AssertConserved();
            Check(secure.Grids[0].Items.Contains(ammo) &&
                pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Compatible ammo falls back to the boss secure container without using the reload opening");
            s.Plan.Rollback(); Check(s.InOffer(ammo), "Secure ammunition placement rolls back exactly");
        }
        foreach (bool room in new[] { true, false })
        foreach (bool secure in new[] { false, true })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("medical-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest);
            if (!room) rig.Grids = new[] { new Grid(rig, 1, 2) };
            if (secure) { var bag = Bag("boss-secure", 2, 2); bag.AcceptedSlot = EquipmentSlot.SecuredContainer; s.Equip(bag, EquipmentSlot.SecuredContainer); }
            var medicine = new Meds("adrenaline"); s.Offer(medicine);
            var other = new Item("unwanted-cargo"); s.Offer(other);
            s.Run(); s.AssertConserved();
            Check(s.InKit(medicine) == (room || secure), "Boss medical supply uses real vest/pocket or secure capacity");
            Check(!TeammateGearSwap.Tree(s.Get(EquipmentSlot.Backpack)).Contains(medicine) && s.InOverflow(other),
                "Medical exception does not admit backpack cargo or unrelated items");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Medical placement preserves the primary magazine landing opening");
            Check(!s.Plan.OnlyPlateUpgrades, "Medical supply does not receive the plate-only validation exception");
            s.Plan.Rollback(); Check(s.InOffer(medicine), "Accepted or rejected medical supply rolls back exactly");
        }
        foreach (bool secondary in new[] { false, true })
        foreach (bool room in new[] { false, true })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = FamilyGun("supply-primary", 40, "primary"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var target = secondary ? FamilyGun("existing-secondary", 40, "secondary") : primary;
            if (secondary) s.Equip(target, EquipmentSlot.SecondPrimaryWeapon);
            var inserted = target.GetCurrentMagazine();
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest);
            if (!room) rig.Grids = new[] { new Grid(rig, 1, 2) };
            var magazine = FamilyMag("later-compatible-magazine", 40, target.MagazineFamily);
            var wrong = FamilyMag("wrong-magazine", 40, "unrelated");
            var wrongRounds = FamilyMag("wrong-loaded-rounds", 40, target.MagazineFamily); wrongRounds.LoadedCartridgesCompatible = false;
            var ammo = new Ammo("compatible-ammunition") { WeaponFamily = target.MagazineFamily, StackObjectsCount = 3 };
            var wrongAmmo = new Ammo("wrong-ammunition") { WeaponFamily = "unrelated" };
            s.Offer(magazine); s.Offer(wrong); s.Offer(wrongRounds); s.Offer(ammo); s.Offer(wrongAmmo);
            s.Run(); s.AssertConserved();
            Check(s.InFastAccess(magazine) == room && s.InKit(ammo) == room,
                "Later compatible primary/secondary magazines and even small ammo supplies require actual room");
            Check(s.InOverflow(wrong) && s.InOverflow(wrongRounds) && s.InOverflow(wrongAmmo),
                "Boss resupply rejects incompatible magazines, cartridges and ammunition");
            Check(target.GetCurrentMagazine() == inserted && inserted.Count == 30 && ammo.StackObjectsCount == 3,
                "Resupply never cycles the inserted magazine, packs rounds or changes stack quantities");
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary &&
                (!secondary || s.Get(EquipmentSlot.SecondPrimaryWeapon) == target), "Supply-only handover preserves equipped weapons");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Additional magazines preserve the shared native reload opening");
            s.Plan.Rollback(); Check(s.InOffer(magazine) && s.InOffer(ammo), "Supply-only handover rolls back exactly");
        }
    }

    static void CheckFixedCompanionEquipment()
    {
        var policy = new CompanionGearPolicy(true, true, 0.10f);
        foreach (bool includeRejectedGear in new[] { false, true })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("packed-plate-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var rig = (Vest)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 1, 2) };
            var spare = Mag("packed-plate-spare", 40); s.AddFast(spare);
            var pockets = Bag("packed-plate-pockets", 1, 2); pockets.AcceptedSlot = EquipmentSlot.Pockets;
            var grenade1 = new ThrowWeap("packed-pocket-grenade-one"); var grenade2 = new ThrowWeap("packed-pocket-grenade-two");
            pockets.Grids[0].Put(grenade1); pockets.Grids[0].Put(grenade2); s.Equip(pockets, EquipmentSlot.Pockets);
            var oldPlate = new ArmorPlate("packed-old-plate", 0); var slot = new ArmorSlot(rig); rig.Slots.Add(slot); slot.Put(oldPlate);
            var incoming = new ArmorPlate("packed-new-plate", 30); s.Offer(incoming);
            if (includeRejectedGear) { s.Offer(new ThrowWeap("packed-rejected-grenade")); s.Offer(Gun("packed-rejected-secondary", 40)); }
            Check(!pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Reported generated-kit geometry starts with no reload opening in vest or pockets");
            s.Run(); s.AssertConserved();
            Check(s.Plan.OnlyPlateUpgrades && slot.ContainedItem == incoming && s.InOverflow(oldPlate),
                "Actual accepted plate replacement is classified independently of full magazines and rejected offerings");
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.TacticalVest) == rig &&
                s.InFastAccess(spare) && pockets.Grids[0].Items.Contains(grenade1) && pockets.Grids[0].Items.Contains(grenade2) &&
                !pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Plate-only planning leaves existing weapons, magazines and packed fast-access layout untouched");
            s.Plan.Rollback();
            Check(!s.Plan.OnlyPlateUpgrades && slot.ContainedItem == oldPlate && s.InOffer(incoming),
                "Rollback restores exact plates and clears the plate-only classification");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            s.Equip(Gun("mixed-plate-primary", 40), EquipmentSlot.FirstPrimaryWeapon);
            var rig = (Vest)s.Get(EquipmentSlot.TacticalVest);
            var slot = new ArmorSlot(rig); rig.Slots.Add(slot); slot.Put(new ArmorPlate("mixed-old-plate", 0));
            var incoming = new ArmorPlate("mixed-new-plate", 30); var grenade = new ThrowWeap("mixed-accepted-grenade");
            s.Offer(incoming); s.Offer(grenade); s.Run(); s.AssertConserved();
            Check(!s.Plan.OnlyPlateUpgrades && slot.ContainedItem == incoming && s.InFastAccess(grenade),
                "Accepted plate plus grenade retains the normal reload-space validation path");
        }
        foreach (bool space in new[] { true, false })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("grenade-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest);
            var backpack = s.Get(EquipmentSlot.Backpack);
            if (!space) rig.Grids = new[] { new Grid(rig, 1, 2) }; // Only the magazine's landing space remains.
            var grenade = new ThrowWeap("offered-grenade"); s.Offer(grenade);
            s.Run(); s.AssertConserved();
            Check(s.InFastAccess(grenade) == space && s.InOverflow(grenade) != space,
                "Grenade intake requires real fast-access room beyond the retained reload reserve");
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.TacticalVest) == rig &&
                s.Get(EquipmentSlot.Backpack) == backpack && !TeammateGearSwap.Tree(backpack).Contains(grenade),
                "Grenade resupply preserves the fixed kit and never uses backpack cargo space");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Accepted or rejected grenade leaves native magazine landing space available");
            s.Plan.Rollback(); Check(s.InOffer(grenade), "Grenade acceptance/rejection rolls back to the original offered item");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("grenade-pocket-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest);
            rig.Grids = new[] { new Grid(rig, 1, 2) };
            var pockets = Bag("grenade-pockets", 1, 1); pockets.AcceptedSlot = EquipmentSlot.Pockets; s.Equip(pockets, EquipmentSlot.Pockets);
            var grenade = new ThrowWeap("pocket-grenade"); s.Offer(grenade);
            s.Run(); s.AssertConserved();
            Check(pockets.Grids[0].Items.Contains(grenade), "Pocket placement works when rig cells are reserved for reload");
            s.Plan.Rollback(); Check(s.InOffer(grenade), "Pocket grenade transfer rolls back exactly");
        }
        Check(policy.AllowsSecondaryScore(100, 90) && policy.AllowsSecondaryScore(100, 110) &&
            !policy.AllowsSecondaryScore(100, 89) && !policy.AllowsSecondaryScore(100, 111) &&
            !policy.AllowsSecondaryScore(0, 0) && !policy.AllowsSecondaryScore(100, float.NaN), "Secondary score window is finite and within ten percent");
        foreach (bool occupied in new[] { false, true })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("fixed-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var helmet = Armor("fixed-helmet", 1, 0, EquipmentSlot.Headwear); s.Equip(helmet, EquipmentSlot.Headwear);
            var vest = (Vest)s.Get(EquipmentSlot.TacticalVest); var bag = s.Get(EquipmentSlot.Backpack);
            var secondary = Gun("fixed-existing-secondary", 40);
            if (occupied) s.Equip(secondary, EquipmentSlot.SecondPrimaryWeapon);
            var candidate = Gun("fixed-matching-secondary", 40);
            s.Offer(candidate); s.Offer(Armor("better-helmet", 6, 1, EquipmentSlot.Headwear));
            s.Offer(Rig("better-vest", 6, 1, 6, 1, 8, 8)); s.Offer(Bag("bigger-backpack", 10, 10));
            s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.Headwear) == helmet &&
                s.Get(EquipmentSlot.TacticalVest) == vest && s.Get(EquipmentSlot.Backpack) == bag,
                "Fixed kit retains primary, consumed helmet, vest and backpack identities");
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == (occupied ? secondary : candidate),
                "Only an empty shoulder slot accepts a near-score secondary");
            Check(!TeammateGearSwap.Tree(bag).Any(item => item.Id == "better-helmet" || item.Id == "bigger-backpack"),
                "Rejected gifts never become backpack cargo");
            s.Plan.Rollback(); Check(s.InOffer(candidate), "Fixed-kit rollback returns the offered secondary");
        }
        foreach (int penetration in new[] { 1, 200 })
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("score-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var candidate = Gun("outside-score-window", penetration); s.Offer(candidate);
            s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.SecondPrimaryWeapon) == null && s.InOverflow(candidate),
                "Neither a much weaker nor much stronger gift replaces the fixed primary or fills the secondary");
        }
        foreach (bool compatible in new[] { true, false })
        {
            var s = new Scenario { GearPolicy = policy };
            var vest = (Vest)s.Get(EquipmentSlot.TacticalVest);
            var consumed = new ArmorPlate("consumed", 0); var healthy = new ArmorPlate("healthy", 20);
            var front = new ArmorSlot(vest); var back = new ArmorSlot(vest); vest.Slots.Add(front); vest.Slots.Add(back);
            front.Put(consumed); back.Put(healthy);
            var donor = new ArmorPlate("offered-plate", 30) { Fits = compatible }; s.Offer(donor);
            s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.TacticalVest) == vest && back.ContainedItem == healthy &&
                front.ContainedItem == (compatible ? donor : consumed), "A depleted compatible removable vest plate can be restored");
            Check(compatible ? s.InOverflow(consumed) : s.InOverflow(donor), "Depleted/rejected plates keep one real disposition");
            s.Plan.Rollback();
            Check(front.ContainedItem == consumed && back.ContainedItem == healthy && s.InOffer(donor), "Plate rollback restores exact carrier/plate identities");
        }
        foreach (var test in new (int CurrentClass, float CurrentDurability, int IncomingClass, float IncomingDurability, bool Accept)[] {
            (5, 10f, 5, 20f, true),
            (5, 20f, 5, 10f, false), (5, 20f, 5, 20f, false),
            (4, 30f, 5, 30f, true), (6, 30f, 5, 30f, false), (6, 0f, 4, 5f, true),
            (5, 20f, 6, 0f, false) })
        {
            var s = new Scenario { GearPolicy = policy };
            var vest = (Vest)s.Get(EquipmentSlot.TacticalVest);
            var current = new ArmorPlate("current-compared-plate", test.CurrentDurability);
            current.Armor.ArmorClass = test.CurrentClass;
            var slot = new ArmorSlot(vest); vest.Slots.Add(slot); slot.Put(current);
            var incoming = new ArmorPlate("incoming-compared-plate", test.IncomingDurability);
            incoming.Armor.ArmorClass = test.IncomingClass; s.Offer(incoming);
            s.Run(); s.AssertConserved();
            Check(slot.ContainedItem == (test.Accept ? incoming : current) && s.Get(EquipmentSlot.TacticalVest) == vest,
                "Retained vest plate changes only for strictly better condition-aware protection");
            s.Plan.Rollback();
            Check(slot.ContainedItem == current && s.InOffer(incoming), "Plate comparison rollback restores exact original ownership");
        }
        {
            var s = new Scenario(false) { GearPolicy = policy };
            var primary = Gun("no-room-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var candidate = Gun("no-room-secondary", 40); s.Offer(candidate);
            s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == null && s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.InOverflow(candidate),
                "A near-score secondary is refused when reload landing space does not fit");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            var primary = Gun("low-stock-primary", 40); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            foreach (var round in primary.GetCurrentMagazine().Cartridges.Items) round.StackObjectsCount = 0;
            var reserve = Mag("only-primary-spare", 40); s.AddFast(reserve);
            var candidate = Gun("empty-near-score-secondary", 40);
            foreach (var round in candidate.GetCurrentMagazine().Cartridges.Items) round.StackObjectsCount = 0;
            s.Offer(candidate); s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == null && s.InFastAccess(reserve) && reserve.Count == 30,
                "Optional secondary cannot consume the primary's existing supply even below full readiness");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            var vest = (Vest)s.Get(EquipmentSlot.TacticalVest);
            var consumed = new ArmorPlate("locked-consumed", 0); var locked = new ArmorSlot(vest) { Locked = true };
            vest.Slots.Add(locked); locked.Put(consumed);
            var helmet = new CompoundItem("fixed-plated-helmet") { AcceptedSlot = EquipmentSlot.Headwear };
            var helmetSlot = new ArmorSlot(helmet); var helmetPlate = new ArmorPlate("depleted-helmet-plate", 0);
            helmet.Slots.Add(helmetSlot); helmetSlot.Put(helmetPlate); s.Equip(helmet, EquipmentSlot.Headwear);
            var donor = new ArmorPlate("unused-fresh-plate", 30); s.Offer(donor);
            s.Run(); s.AssertConserved();
            Check(locked.ContainedItem == consumed && helmetSlot.ContainedItem == helmetPlate && s.InOverflow(donor),
                "Locked body plates and all helmet parts remain untouched even when depleted");
        }
        {
            var s = new Scenario { GearPolicy = policy };
            var vest = (Vest)s.Get(EquipmentSlot.TacticalVest);
            var old = new ArmorPlate("worn-out-front", 0); var front = new ArmorSlot(vest); vest.Slots.Add(front); front.Put(old);
            var donorVest = new Vest("offered-donor-vest"); donorVest.Grids = new[] { new Grid(donorVest, 4, 4) };
            var donorSlot = new ArmorSlot(donorVest); donorVest.Slots.Add(donorSlot);
            var plate = new ArmorPlate("installed-offered-plate", 30); donorSlot.Put(plate); s.Offer(donorVest);
            s.Run(); s.AssertConserved();
            Check(s.Get(EquipmentSlot.TacticalVest) == vest && front.ContainedItem == plate && donorSlot.ContainedItem == null &&
                s.InOverflow(donorVest) && s.InOverflow(old), "A supplied carrier can donate a direct removable plate without replacing Knight's vest");
            s.Plan.Rollback(); Check(front.ContainedItem == old && donorSlot.ContainedItem == plate && s.InOffer(donorVest),
                "Donor-carrier rollback restores original plate nesting");
        }
    }

    static Weapon Gun(string id, int pen) { var gun = new Weapon(id); gun.Magazine.Put(Mag(id + "-loaded", pen)); return gun; }
    static Weapon FamilyGun(string id, int pen, string family, int magazineWidth = 1, int magazineHeight = 2)
    {
        var gun = Gun(id, pen); gun.MagazineFamily = family;
        gun.AmmoCaliber = family == "9x39" ? "Caliber9x39" : family == "9x19" ? "Caliber9x19PARA" : "Caliber556x45NATO";
        var mag = gun.GetCurrentMagazine(); mag.MagazineFamily = family; mag.Width = magazineWidth; mag.Height = magazineHeight;
        foreach (var ammo in mag.Cartridges.Items.OfType<Ammo>()) ammo.WeaponFamily = family;
        return gun;
    }
    static Magazine FamilyMag(string id, int pen, string family, int width = 1, int height = 2)
    {
        var mag = Mag(id, pen); mag.MagazineFamily = family; mag.Width = width; mag.Height = height;
        foreach (var ammo in mag.Cartridges.Items.OfType<Ammo>()) ammo.WeaponFamily = family;
        return mag;
    }
    static Vest NarrowRig(string id, bool armored)
    {
        var rig = new Vest(id); rig.Grids = new[] { new Grid(rig, 1, 2), new Grid(rig, 1, 2) };
        if (armored)
        {
            InstallProtection(rig, "front", 5, 1, EArmorPlateCollider.Plate_Granit_SAPI_chest);
            InstallProtection(rig, "back", 5, 1, EArmorPlateCollider.Plate_Granit_SAPI_back);
        }
        return rig;
    }
    static void SetRounds(Magazine magazine, int rounds) => magazine.Cartridges.Items[0].StackObjectsCount = rounds;
    static void CheckWeaponEffectiveRange()
    {
        foreach (var row in new[] { (-10,0f), (0,0f), (70,14f), (100,20f), (200,40f), (300,60f), (301,60f), (1000,60f), (int.MaxValue,60f) })
        {
            var weapon = new Weapon("range-boundary"); weapon.Template.bEffDist=row.Item1;
            Check(Math.Abs(GiveGearPlanner.GetWeaponRangeBonus(weapon,out int meters)-row.Item2)<.0001f && meters==Math.Max(0,row.Item1),
                "Range uses native effective-distance metadata with exact bounded credit");
            Check(weapon.Template.bEffDist==row.Item1, "Range scoring never edits shared template metadata");
        }
        Check(GiveGearPlanner.GetWeaponRangeBonus(null,out _)==0 &&
            GiveGearPlanner.GetWeaponRangeBonus(new Weapon("unknown-range") { Template=null },out _)==0, "Missing range stays unknown, not invented");
        var observedRifle = new Weapon("SV98-range") { Template=new WeaponTemplate { bEffDist=1000 } };
        var observedShotgun = new Weapon("MP133-range") { Template=new WeaponTemplate { bEffDist=70 } };
        float rifleScore = 68.1f + GiveGearPlanner.GetWeaponRangeBonus(observedRifle,out _);
        float shotgunScore = 86.13f + GiveGearPlanner.GetWeaponRangeBonus(observedShotgun,out _);
        Check(Math.Abs(rifleScore-128.1f)<.001f && Math.Abs(shotgunScore-100.13f)<.001f && rifleScore>shotgunScore*1.1f+1f,
            "Logged SV98/MP133 quality gate replay now credits useful rifle reach after the bolt penalty");
        foreach (int mode in Enumerable.Range(0, 13))
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario();
            var current = FamilyGun("short-range-current", mode == 3 ? 100 : 30, "current");
            var rifle = FamilyGun("long-range-rifle", mode == 3 ? 1 : mode == 2 ? 100 : 30, "rifle", mode == 8 ? 2 : 1, 2);
            current.Template.bEffDist = mode == 5 ? 300 : 70;
            current.Template.BoltAction = mode == 5;
            rifle.Template.bEffDist = mode == 1 ? 0 : 1000;
            rifle.Template.BoltAction = mode != 4;
            if (mode == 11) { current.Template.bEffDist=500; rifle.Template.bEffDist=500; rifle.Template.BoltAction=false; }
            if (mode == 12) { current.Template.bEffDist=1000; current.Template.BoltAction=true; rifle.Template.bEffDist=70; rifle.Template.BoltAction=false; }
            if (mode == 6) rifle.Template = null;
            if (mode == 7) rifle.MissingVitalParts.Add(new Item("missing-vital-part"));
            s.Equip(current, EquipmentSlot.FirstPrimaryWeapon);
            if (mode == 8)
            {
                var rig = (Vest)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig,1,2) };
                current.GetCurrentMagazine().MaxCount = 60; SetRounds(current.GetCurrentMagazine(),60);
                rifle.GetCurrentMagazine().MaxCount = 60; SetRounds(rifle.GetCurrentMagazine(),60);
            }
            else s.AddFast(FamilyMag("short-range-current-spare", mode == 3 ? 100 : 30, "current"));
            if (mode == 2) SetRounds(rifle.GetCurrentMagazine(),1);
            var offered = new List<Item>();
            if (mode == 9)
            {
                ((SearchableItem)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(rifle);
                s.Bot.Gifts.Add(rifle.Id);
            }
            else if (mode == 10) { s.Equip(rifle,EquipmentSlot.SecondPrimaryWeapon); s.Bot.Gifts.Add(rifle.Id); }
            else offered.Add(rifle);
            if (mode != 2 && mode != 8) offered.Add(FamilyMag("long-range-rifle-spare", mode == 3 ? 1 : 30, "rifle"));
            foreach (var item in reverse ? offered.AsEnumerable().Reverse() : offered) s.Offer(item);
            s.Run();
            bool shouldSwitch = mode == 0 || mode == 4 || mode == 9 || mode == 10;
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == (shouldSwitch ? rifle : current),
                $"Range credits a supported useful rifle, not missing data, weak ammunition, broken/unready packages or unsafe reloads (mode {mode})");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && (mode == 9 ? s.InKit(rifle) :
                mode == 10 ? s.Get(EquipmentSlot.SecondPrimaryWeapon) == rifle : s.InOffer(rifle)),
                "Range-aware replacement/decline rolls back the exact original weapon ownership");
        }
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario(); var shortGun = FamilyGun("short-fast",30,"short"); shortGun.Template.bEffDist=70;
            var rifle = FamilyGun("long-bolt",30,"long"); rifle.Template.bEffDist=1000; rifle.Template.BoltAction=true;
            var offered = new Item[] { shortGun, FamilyMag("short-spare",30,"short"), rifle, FamilyMag("long-spare",30,"long") };
            foreach (var item in reverse ? offered.Reverse() : offered) s.Offer(item);
            s.Run(); Check(s.Get(EquipmentSlot.FirstPrimaryWeapon)==rifle, "Range participates in best-first candidate ranking, independent of offering order");
            s.AssertConserved(); s.Plan.Rollback();
        }
    }

    static void CheckWeaponCadence()
    {
        foreach (var row in new[]
        {
            (false, new[] { Weapon.EFireMode.single }, 450, 30, 1f),
            (false, new[] { Weapon.EFireMode.single }, 450, 800, 1f),
            (false, new[] { Weapon.EFireMode.single, Weapon.EFireMode.fullauto }, 450, 850, 1f),
            (false, new[] { Weapon.EFireMode.single }, 360, 800, 1f),
            (false, new[] { Weapon.EFireMode.single }, 300, 800, .875f),
            (false, new[] { Weapon.EFireMode.semiauto }, 300, 60, .875f),
            (false, new[] { Weapon.EFireMode.single }, 240, 800, .75f),
            (false, new[] { Weapon.EFireMode.single }, 120, 800, .75f),
            (false, new[] { Weapon.EFireMode.single }, 0, 800, 1f),
            (false, new[] { Weapon.EFireMode.single }, -1, 800, 1f),
            (false, new[] { Weapon.EFireMode.single, Weapon.EFireMode.fullauto }, 240, 800, 1f),
            (false, new[] { Weapon.EFireMode.single, Weapon.EFireMode.burst }, 240, 800, 1f),
            (false, Array.Empty<Weapon.EFireMode>(), 240, 800, 1f),
            (false, new[] { Weapon.EFireMode.doublet }, 240, 800, 1f),
            (true, new[] { Weapon.EFireMode.single }, 450, 30, .75f)
        })
        {
            var weapon = new Weapon("cadence-formula");
            var template = weapon.Template;
            template.BoltAction = row.Item1; template.weapFireType = row.Item2;
            template.SingleFireRate = row.Item3; template.bFirerate = row.Item4;
            Check(Math.Abs(GiveGearPlanner.GetWeaponCadenceMultiplier(weapon, out _) - row.Item5) < .0001f,
                "Cadence has exact bounded values, native cooldown floor, supported modes and no displayed-rate shortcut");
            Check(template.BoltAction == row.Item1 && ReferenceEquals(template.weapFireType, row.Item2) &&
                template.SingleFireRate == row.Item3 && template.bFirerate == row.Item4,
                "Cadence comparison is read-only even for invalid shared template values");
        }
        foreach (int mode in Enumerable.Range(0, 11))
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario();
            var current = FamilyGun("cadence-current", mode == 6 ? 100 : 30, "current");
            var candidate = FamilyGun("cadence-candidate", mode == 6 ? 20 : 30, "candidate");
            current.Template.BoltAction = mode == 0 || mode == 6 || mode == 7;
            current.Template.weapFireType = new[] { Weapon.EFireMode.single };
            current.Template.SingleFireRate = mode == 1 ? 240 : mode == 2 ? 300 : mode == 3 ? 350 : mode == 4 ? 360 : 450;
            if (mode == 5) candidate.Template.BoltAction = true;
            if (mode == 8) current.Template.weapFireType = new[] { Weapon.EFireMode.single, Weapon.EFireMode.fullauto };
            if (mode == 8) current.Template.SingleFireRate = 240;
            if (mode == 9) current.Template.SingleFireRate = 0;
            if (mode == 10) current.Template = null;
            var currentSpare = FamilyMag("cadence-current-spare", mode == 6 ? 100 : 30, "current");
            s.Equip(current, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(currentSpare);
            var candidateSpare = FamilyMag("cadence-candidate-spare", mode == 6 ? 20 : 30, "candidate");
            if (mode == 7) SetRounds(candidate.GetCurrentMagazine(), 1);
            var offered = mode == 7 ? new Item[] { candidate } : new Item[] { candidate, candidateSpare };
            foreach (var item in reverse ? offered.Reverse() : offered) s.Offer(item);
            s.Run();
            bool shouldSwitch = mode == 0 || mode == 1 || mode == 2;
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == (shouldSwitch ? candidate : current),
                "Cadence ranks bolts/slow semi lower without forcing swaps, overriding ammo quality, penalizing fast/automatic guns or guessing missing metadata");
            if (current.Template != null && mode == 9) Check(current.Template.SingleFireRate == 0,
                "Comparison never repairs or mutates shared cadence metadata");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment), "Cadence does not bypass reload geometry");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.InOffer(candidate) && current.GetCurrentMagazine().Count == 30,
                "Cadence comparison and failed support preserve exact kit/offer rollback");
        }
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario(); var bolt = FamilyGun("rank-bolt", 70, "bolt"); bolt.Template.BoltAction = true;
            var fast = FamilyGun("rank-fast", 60, "fast");
            var offered = new Item[] { bolt, FamilyMag("rank-bolt-spare",70,"bolt"), fast, FamilyMag("rank-fast-spare",60,"fast") };
            foreach (var item in reverse ? offered.Reverse() : offered) s.Offer(item);
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == fast, "Best-first ranking includes cadence, not offering order");
            s.AssertConserved(); s.Plan.Rollback();
        }
    }

    static void CheckArmoredMaskCapConflict()
    {
        foreach (bool cargo in new[] { false, true })
        foreach (bool reverseConflict in new[] { false, true })
        foreach (bool packed in new[] { false, true })
        {
            var s = new Scenario(cargo);
            var cap = Armor("r2-cap", 0, 1, EquipmentSlot.Headwear);
            var mask = Armor("ballistic-mask", 4, 1, EquipmentSlot.FaceCover);
            (reverseConflict ? cap : mask).Conflicts.Add((reverseConflict ? mask : cap).Id);
            s.Equip(cap, EquipmentSlot.Headwear);
            SearchableItem incoming = null;
            if (packed) { incoming = new Vest("offered-vest"); incoming.Grids = new[] { new Grid(incoming, 4, 4) }; incoming.Grids[0].Put(mask); s.Offer(incoming); }
            else s.Offer(mask);
            s.Run();
            Check(s.Get(EquipmentSlot.FaceCover) == mask && s.Get(EquipmentSlot.Headwear) == null,
                "Protective mask displaces conflicting non-protective cap, loose or one level deep");
            Check(cargo || packed ? s.InKit(cap) : s.InOverflow(cap), "Displaced cap uses available cargo, including space gained from the offered vest upgrade");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Headwear) == cap && s.Get(EquipmentSlot.FaceCover) == null &&
                (packed ? mask.CurrentAddress.Container.ParentItem == incoming : s.InOffer(mask)),
                "Rollback restores cap and exact offered mask location");
        }
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            var s = new Scenario();
            var cap = Armor("retained-headwear", mode == 1 ? 3 : 0, 1, EquipmentSlot.Headwear);
            var mask = Armor("offered-mask", mode == 2 ? 0 : 4, 1, EquipmentSlot.FaceCover);
            var ears = new Item("retained-headset") { AcceptedSlot = EquipmentSlot.Earpiece };
            var oldFace = new Item("retained-face-cover") { AcceptedSlot = EquipmentSlot.FaceCover };
            if (mode != 0) mask.Conflicts.Add(cap.Id);
            if (mode == 3) { mask.Conflicts.Add(ears.Id); s.Equip(ears, EquipmentSlot.Earpiece); }
            if (mode == 4) s.Equip(oldFace, EquipmentSlot.FaceCover);
            s.Equip(cap, EquipmentSlot.Headwear); s.Offer(mask); s.Run();
            Check(s.Get(EquipmentSlot.Headwear) == cap, "Compatible/protective headwear stays; failed retry restores cap");
            Check(s.Get(EquipmentSlot.FaceCover) == (mode == 0 ? mask : mode == 4 ? oldFace : null),
                "Native compatibility, protective mask and empty FaceCover requirements remain mandatory");
            if (mode == 3) Check(s.Get(EquipmentSlot.Earpiece) == ears, "Mask retry never strips a conflicting headset");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Headwear) == cap && s.InOffer(mask), "Rejected/compatible mask draft rolls back exactly");
        }
    }

    static void CheckLowAmmoFallback()
    {
        // Depleted VSS versus supplied PP-19; sufficient current ammo; refill from loose ammo,
        // offered one-level magazines or carried backpack magazines; unsupported replacement.
        foreach (int mode in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario(); var current = FamilyGun("VSS-like", 60, "9x39");
            var spare = FamilyMag("VSS-spare", 60, "9x39");
            int originalLoaded = mode >= 7 ? 20 : mode == 1 ? 30 : 8;
            int originalSpare = mode >= 7 ? mode == 7 ? 19 : 20 : mode == 1 ? 30 : 9;
            SetRounds(current.GetCurrentMagazine(), originalLoaded); SetRounds(spare, originalSpare);
            s.Equip(current, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(spare);
            var replacement = FamilyGun("PP19-like", 20, "9x19"); SetRounds(replacement.GetCurrentMagazine(), 19);
            var items = new List<Item> { replacement };
            if (mode != 5)
                foreach (int i in Enumerable.Range(0, 3))
                {
                    var mag = FamilyMag("PP19-spare-" + i, 20, "9x19"); SetRounds(mag, 20); items.Add(mag);
                }
            if (mode == 2 || mode == 6)
            {
                var ammo = new Ammo("VSS-loose") { StackObjectsCount = 60, PenetrationPower = 60, WeaponFamily = mode == 6 ? "wrong-caliber" : "9x39" };
                ((SearchableItem)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(ammo);
            }
            if (mode == 3 || mode == 4)
            {
                var bag = mode == 3 ? Bag("offered-current-supplies", 4, 4) : (Backpack)s.Get(EquipmentSlot.Backpack);
                bag.Grids[0].Put(FamilyMag("VSS-replenishment-one", 60, "9x39"));
                bag.Grids[0].Put(FamilyMag("VSS-replenishment-two", 60, "9x39"));
                if (mode == 3) items.Add(bag);
            }
            if (reverse) items.Reverse();
            foreach (var item in items) s.Offer(item);
            s.Run();
            bool switchExpected = mode == 0 || mode == 6 || mode == 7;
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == (switchExpected ? replacement : current),
                "Low-ammo fallback switches only when current resupply fails and the weaker replacement is operational");
            if (mode != 5)
                Check(FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment).Rounds((Weapon)s.Get(EquipmentSlot.FirstPrimaryWeapon)) >=
                    FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(((Weapon)s.Get(EquipmentSlot.FirstPrimaryWeapon)).AmmoCaliber),
                    "Selected/resupplied primary has a supported future refill allocation");
            if (mode == 5) Check(current.GetCurrentMagazine().Count == 8 && spare.Count == 9,
                "Two unready packages do not force a downgrade or lose the depleted original ammunition");
            if (mode == 2) Check(current.GetCurrentMagazine().Count == originalLoaded && spare.Count == originalSpare,
                "Current-primary resupply is predicted without changing magazine contents");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Low-ammo rescue never bypasses equipped-weapon reload geometry");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.InOffer(replacement) &&
                current.GetCurrentMagazine().Count == originalLoaded && spare.Count == originalSpare,
                "Fallback/resupply rollback restores exact weapon identities and ammunition counts");
        }
        {
            var s = new Scenario(); var current = FamilyGun("current-needs-vest", 60, "9x39");
            var spare = FamilyMag("low-spare", 60, "9x39"); SetRounds(current.GetCurrentMagazine(), 8); SetRounds(spare, 9);
            var oldRig = (Vest)s.Get(EquipmentSlot.TacticalVest); oldRig.Grids = new[] { new Grid(oldRig, 1, 2) };
            s.Equip(current, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(spare);
            ((Backpack)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(new Ammo("current-loose") { WeaponFamily = "9x39", PenetrationPower = 60, StackObjectsCount = 60 });
            var replacement = FamilyGun("weaker-incoming", 20, "9x19");
            var rig = NarrowRig("replenishment-vest", false);
            s.Offer(replacement); s.Offer(FamilyMag("weaker-spare", 20, "9x19")); s.Offer(rig); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.Get(EquipmentSlot.TacticalVest) == rig &&
                FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment).Rounds(current) >= FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(current.AmmoCaliber),
                "Current primary can use incoming suitable gear to recover instead of being needlessly replaced");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.TacticalVest) == oldRig && current.GetCurrentMagazine().Count == 8 && spare.Count == 9,
                "Incoming-gear resupply also rolls back exact layout and refill quantities");
        }
    }
    static void CheckPrimaryKitPriority()
    {
        // Starting M60 primary; M60 already secondary; later gear-only handover after an earlier upgrade.
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (bool armored in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        foreach (bool cargo in new[] { false, true })
        {
            var s = new Scenario(cargo); var oldRig = (Vest)s.Get(EquipmentSlot.TacticalVest);
            oldRig.Grids = new[] { new Grid(oldRig, 4, 4) };
            var oldArmor = Armor("old-armor", 3, 1, EquipmentSlot.ArmorVest); s.Equip(oldArmor, EquipmentSlot.ArmorVest);
            var large = FamilyGun("M60-like", 20, "belt", 2, 2);
            var primary = mode == 0 ? large : FamilyGun("current-primary", mode == 2 ? 100 : 25, mode == 2 ? "new" : "old");
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            if (mode != 0) s.Equip(large, EquipmentSlot.SecondPrimaryWeapon);
            var oldSpare = FamilyMag("large-spare", 20, "belt", 2, 2); s.AddFast(oldSpare);
            if (mode != 0) s.AddFast(FamilyMag("current-spare", mode == 2 ? 100 : 25, primary.MagazineFamily));
            var pistol = FamilyGun("existing-pistol", 10, "pistol"); pistol.Pistol = true; s.Equip(pistol, EquipmentSlot.Holster);
            s.AddFast(FamilyMag("pistol-spare", 10, "pistol"));
            var candidate = mode == 2 ? null : FamilyGun("better-primary", 100, "new");
            var incoming = NarrowRig("smaller-vest", armored);
            var body = armored ? null : Armor("new-armor", 5, 1, EquipmentSlot.ArmorVest);
            var offerings = new List<Item> { incoming };
            if (body != null) offerings.Add(body);
            if (candidate != null) { offerings.Add(candidate); offerings.Add(FamilyMag("new-spare", 100, "new")); }
            if (reverse) offerings.Reverse();
            foreach (var item in offerings) s.Offer(item);
            s.Run();
            var winner = candidate ?? primary;
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == winner && s.Get(EquipmentSlot.TacticalVest) == incoming &&
                s.Get(EquipmentSlot.ArmorVest) == body,
                "Primary-first kit accepts better protection and narrow vest without a mandatory M60 secondary opening");
            Check(FollowerWeaponPrimaryReadiness.EvaluateActual(s.Controller, winner).PrimaryReady &&
                pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Selected primary keeps real spare-magazine readiness and reload space");
            Check((s.Get(EquipmentSlot.SecondPrimaryWeapon) == large ? large.GetCurrentMagazine() == null : s.InKit(large) || s.InOverflow(large)) &&
                (s.InKit(oldSpare) || s.InOverflow(oldSpare)) && !s.InFastAccess(oldSpare),
                "Old M60 can use an otherwise empty secondary without its oversize magazine, but cannot displace another usable secondary or veto the primary kit");
            Check(s.Get(EquipmentSlot.Holster) == pistol, "Existing supported holster weapon is restored to its original slot");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.TacticalVest) == oldRig &&
                s.Get(EquipmentSlot.ArmorVest) == oldArmor && s.Get(EquipmentSlot.Holster) == pistol && s.InFastAccess(oldSpare) &&
                (mode == 0 || s.Get(EquipmentSlot.SecondPrimaryWeapon) == large) && offerings.All(s.InOffer),
                "Primary/outfit/deferred-support rollback restores the exact original kit and offered roots");
        }
        foreach (bool unsupportedUpgrade in new[] { false, true })
        foreach (bool armored in new[] { false, true })
        {
            var s = new Scenario(); var oldRig = s.Get(EquipmentSlot.TacticalVest);
            var current = FamilyGun("required-M60", 60, "belt", 2, 2); s.Equip(current, EquipmentSlot.FirstPrimaryWeapon);
            var spare = FamilyMag("required-spare", 60, "belt", 2, 2); s.AddFast(spare);
            var oldArmor = Armor("current-armor", 3, 1, EquipmentSlot.ArmorVest); s.Equip(oldArmor, EquipmentSlot.ArmorVest);
            var pistol = FamilyGun("retained-pistol", 10, "pistol"); pistol.Pistol = true; s.Equip(pistol, EquipmentSlot.Holster);
            var secondary = FamilyGun("retained-secondary", 10, "second"); s.Equip(secondary, EquipmentSlot.SecondPrimaryWeapon);
            var candidate = FamilyGun("rejected-primary", unsupportedUpgrade ? 100 : 1, "new");
            s.Offer(candidate);
            if (!unsupportedUpgrade) s.Offer(FamilyMag("inferior-spare", 1, "new"));
            var rig = NarrowRig("cannot-support-current-M60", armored); s.Offer(rig);
            var body = armored ? null : Armor("better-body", 5, 1, EquipmentSlot.ArmorVest);
            if (body != null) s.Offer(body);
            var helmet = Armor("independent-helmet", 5, 1, EquipmentSlot.Headwear); s.Offer(helmet);
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.Get(EquipmentSlot.TacticalVest) == oldRig &&
                s.Get(EquipmentSlot.ArmorVest) == (body ?? oldArmor) && s.Get(EquipmentSlot.Holster) == pistol &&
                s.Get(EquipmentSlot.SecondPrimaryWeapon) == secondary && s.InFastAccess(spare),
                "Inferior or unsupported incoming primary cannot sacrifice M60 support; better separate armor may still use the retained rig");
            Check(FollowerWeaponPrimaryReadiness.EvaluateActual(s.Controller, current).PrimaryReady &&
                pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Rejected outfit leaves the current primary and reload support intact");
            Check(s.Get(EquipmentSlot.Headwear) == helmet, "Rejected primary/outfit still allows unrelated useful gear");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Holster) == pistol && s.Get(EquipmentSlot.FirstPrimaryWeapon) == current && s.InOffer(candidate),
                "Failed upgraded-primary trials do not leak deferred-support changes");
        }
        {
            var s = new Scenario(); var original = FamilyGun("original-secondary", 10, "second");
            s.Equip(FamilyGun("old-primary", 20, "old"), EquipmentSlot.FirstPrimaryWeapon);
            s.Equip(original, EquipmentSlot.SecondPrimaryWeapon);
            var best = FamilyGun("new-primary", 100, "new"); s.Offer(best); s.Offer(FamilyMag("new-primary-spare", 100, "new"));
            s.Offer(FamilyGun("alternative-secondary", 60, "other")); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == best && s.Get(EquipmentSlot.SecondPrimaryWeapon) == original,
                "Deferred original secondary gets its slot back when supported; no occupied-secondary upgrade scorer is introduced");
            s.AssertConserved();
        }
    }
#if DEBUG
    static void CheckGearDiagnostics()
    {
        foreach (bool protectiveFace in new[] { false, true })
        foreach (bool failingSink in new[] { false, true })
        {
            var messages = new List<string>();
            var s = new Scenario();
            s.Diagnostic = message => { if (failingSink) throw new Exception("Injected diagnostic sink failure"); messages.Add(message); };
            var face = Armor("conflicting-face", protectiveFace ? 4 : 0, 1, EquipmentSlot.FaceCover);
            s.Equip(face, EquipmentSlot.FaceCover);
            var helmet = Armor("offered-helmet", 5, 1, EquipmentSlot.Headwear);
            helmet.Conflicts.Add(face.Id); s.Offer(helmet); s.Run();
            Check((s.Get(EquipmentSlot.Headwear) == helmet) == !protectiveFace,
                "Diagnostics and failing sinks preserve cosmetic versus protective face-cover policy");
            Check((s.Get(EquipmentSlot.FaceCover) == face) == protectiveFace,
                "Logging never strips a protective mask or bypasses native conflicts");
            if (!failingSink)
            {
                Check(messages.Any(m => m.Contains("category=helmet candidate=")) &&
                    messages.Any(m => m.Contains("trial=helmet.keepFaceCover result=rejected")) &&
                    messages.Any(m => m.Contains("category=nativeMove") && m.Contains("errorType=")),
                    "Logs include helmet comparison, native rejection and its owning trial");
                Check(messages.Any(m => m.Contains(protectiveFace ? "removableCosmetic=False" : "trial=helmet.removeNonProtectiveFaceCover result=accepted")),
                    "Logs distinguish protected-face retention from successful cosmetic-face removal");
            }
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FaceCover) == face && s.InOffer(helmet), "Diagnostic handover preserves exact rollback");
        }
        {
            var messages = new List<string>(); var s = new Scenario { Diagnostic = messages.Add };
            s.Equip(Armor("better-helmet", 6, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            s.Equip(new Item("ComTac2") { AcceptedSlot = EquipmentSlot.Earpiece }, EquipmentSlot.Earpiece);
            s.Offer(Armor("weaker-helmet", 4, 1, EquipmentSlot.Headwear));
            s.Offer(new Item("ComTac4") { AcceptedSlot = EquipmentSlot.Earpiece });
            s.Offer(Rig("weaker-vest", 2, 1, 2, 1, 2, 3));
            s.Equip(Rig("better-vest", 5, 1, 5, 1, 4, 4), EquipmentSlot.TacticalVest);
            s.Run();
            Check(messages.Any(m => m.Contains("category=helmet rejected=notHigherProtection")), "Protection-score rejection is explicit");
            Check(messages.Any(m => m.Contains("slot=Earpiece") && m.Contains("policy=emptySlotOnly")), "Occupied headset policy is explicit");
            Check(messages.Any(m => m.Contains("category=outfit") && m.Contains("incomingFront=") && m.Contains("currentFront=")) &&
                messages.Any(m => m.Contains("rejected=protectionOrStorageNotBetter")), "Outfit logs preserve compared front/back protection and rejection policy");
            s.AssertConserved();
        }
        {
            var messages = new List<string>(); var s = new Scenario { Diagnostic = messages.Add };
            var old = Bag("old-grid", 2, 2); old.Grids[0].Put(new Item("wide-cargo")); s.Equip(old, EquipmentSlot.Backpack);
            s.Offer(Bag("narrow-grid", 1, 6)); s.Run();
            Check(messages.Any(m => m.Contains("category=backpack") && m.Contains("candidateCells=6") && m.Contains("currentCells=4")) &&
                messages.Any(m => m.Contains("trial=backpack.nativeEquipAndContentMigration result=rejected")),
                "Backpack logs distinguish capacity comparison from failed real content migration");
            s.AssertConserved();
        }
    }
#endif

    static void CheckBackpackReplacement()
    {
        foreach (bool full in new[] { false, true })
        foreach (int width in new[] { 5, 6 })
        {
            var s = new Scenario(false);
            var old = Bag("Beta2", 5, 6); old.Width = old.Height = 5;
            var contents = Enumerable.Range(0, full ? 30 : 0).Select(i => new Item("old-cargo-" + i) { Width = 1, Height = 1 }).ToArray();
            foreach (Item item in contents) old.Grids[0].Put(item);
            s.Equip(old, EquipmentSlot.Backpack);
            var bag = Bag(width == 5 ? "Attack2" : "Blackjack50", width, 7); bag.Width = 5; bag.Height = 7;
            s.Offer(bag); s.Run();
            Check(s.Get(EquipmentSlot.Backpack) == bag && contents.All(i => i.CurrentAddress.Container.ParentItem == bag),
                "Both larger bags migrate every Beta 2 item, even when no room remains for the old shell");
            Check(s.InOverflow(old) && !s.InKit(old) && !old.Grids[0].Items.Any(),
                "The replaced backpack always drops empty, even when it would fit as cargo");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Backpack) == old && s.InOffer(bag) && contents.All(i => i.CurrentAddress.Container.ParentItem == old),
                "Cancelled backpack swap restores original bag, contents and offering exactly");
        }
        foreach (bool useContents in new[] { false, true })
        {
            var s = new Scenario(false);
            var old = Bag("filled-Beta2", 5, 6); old.Width = old.Height = 5;
            var contents = Enumerable.Range(0, 30).Select(i => new Item("packed-original-" + i) { Width = 1, Height = 1 }).ToArray();
            foreach (Item item in contents) old.Grids[0].Put(item);
            s.Equip(old, EquipmentSlot.Backpack);
            var bag = Bag("offered-packed-Attack2", 5, 7); bag.Width = 5; bag.Height = 7;
            var helmet = Armor("packed-helmet", useContents ? 5 : 2, 1, EquipmentSlot.Headwear);
            helmet.Width = 2; helmet.Height = 3;
            if (!useContents) s.Equip(Armor("stronger-current-helmet", 6, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            var headset = new Item("packed-headset") { AcceptedSlot = EquipmentSlot.Earpiece };
            bag.Grids[0].Put(helmet); bag.Grids[0].Put(headset);
            var weapon = new Weapon("packed-empty-secondary");
            if (useContents) bag.Grids[0].Put(weapon);
            s.Offer(bag); s.Run();
            Check(s.Get(EquipmentSlot.Earpiece) == headset, "Independent packed accessory is used even if its backpack cannot replace the old one");
            if (useContents)
            {
                Check(s.Get(EquipmentSlot.Headwear) == helmet && s.Get(EquipmentSlot.SecondPrimaryWeapon) == weapon && s.Get(EquipmentSlot.Backpack) == bag,
                    "Backpack fit is checked after packed helmet, accessory and secondary leave it for equipment slots");
                Check(contents.All(i => i.CurrentAddress.Container.ParentItem == bag) && s.InOverflow(old),
                    "Freed incoming storage accepts all original belongings and the empty old bag drops");
            }
            else
            {
                Check(s.Get(EquipmentSlot.Backpack) == old && contents.All(i => i.CurrentAddress.Container.ParentItem == old),
                    "Unused packed gear still occupies storage and can prevent a nominally larger bag upgrade");
                Check(helmet.CurrentAddress.Container.ParentItem == bag && s.InOverflow(bag) && !s.InOverflow(old),
                    "An unusable backpack keeps its unused cargo tree intact; the retained old backpack is not dropped");
            }
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Backpack) == old && helmet.CurrentAddress.Container.ParentItem == bag && headset.CurrentAddress.Container.ParentItem == bag,
                "Packed-content selection and failed/successful bag trials fully roll back");
        }
        {
            var s = new Scenario(false); var old = Bag("old-with-cargo", 5, 6);
            var contents = Enumerable.Range(0, 30).Select(i => new Item("retained-" + i) { Width = 1, Height = 1 }).ToArray();
            foreach (Item item in contents) old.Grids[0].Put(item);
            s.Equip(old, EquipmentSlot.Backpack);
            s.Equip(Armor("good-current-helmet", 6, 1, EquipmentSlot.Headwear), EquipmentSlot.Headwear);
            var bag = Bag("large-with-unused-gear", 5, 7);
            var unused = Armor("weaker-packed-helmet", 2, 1, EquipmentSlot.Headwear); unused.Width = 2; unused.Height = 1;
            bag.Grids[0].Put(unused); s.Offer(bag); s.Run();
            Check(s.Get(EquipmentSlot.Backpack) == bag && unused.CurrentAddress.Container.ParentItem == bag &&
                contents.All(i => i.CurrentAddress.Container.ParentItem == bag) && s.InOverflow(old),
                "Unused offered gear may remain in an accepted backpack when everything still fits");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.Backpack) == old && unused.CurrentAddress.Container.ParentItem == bag && s.InOffer(bag),
                "Accepted unused packed cargo rolls back without loss or duplication");
        }
    }

    static void CheckPackedVestUpgrades()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (bool reverse in new[] { false, true })
        {
            var s = new Scenario();
            var carrier = Rig("kept-carrier", 4, 1, 4, 1, 4, 4); s.Equip(carrier, EquipmentSlot.TacticalVest);
            var primary = FamilyGun("old-primary", 10, "old"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("old-spare", 10, "old"));
            var gun = FamilyGun("gift-primary", 80, "gift");
            var vest = new Vest("packed-mag-vest"); vest.Grids = new[] { new Grid(vest, 3, 2) };
            var mags = Enumerable.Range(0, 3).Select(i => FamilyMag("packed-mag-" + i, 80, "gift")).ToArray();
            foreach (var mag in mags) vest.Grids[0].Put(mag);
            Item root = vest;
            for (int i = 0; i < depth; i++) { var bag = Bag("outer-bag-" + i, 6, 6); bag.Grids[0].Put(root); root = bag; }
            foreach (var item in reverse ? new[] { root, gun } : new[] { gun, root }) s.Offer(item);
            s.Run();
            Check((s.Get(EquipmentSlot.FirstPrimaryWeapon) == gun) == (depth <= 1),
                "Magazine scan allows a vest directly inside the offered backpack, never a vest behind another backpack");
            Check(s.Get(EquipmentSlot.TacticalVest) == carrier, "Compatible magazines are extracted even while keeping the protective current vest");
            Check(depth <= 1 ? mags.Count(s.InFastAccess) >= 1 : mags.All(m => m.CurrentAddress.Container.ParentItem == vest),
                "Allowed packed magazines reach fast access; excluded deeper supplies stay inside their original vest");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && mags.All(m => m.CurrentAddress.Container.ParentItem == vest) && s.InOffer(root),
                "Packed-vest supply evaluation restores exact nesting and original equipment on rollback");
        }
        {
            var s = new Scenario();
            var primary = FamilyGun("same-kit-primary", 80, "same"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("same-kit-spare", 80, "same"));
            var vest = new Vest("packed-capacity-vest"); vest.Grids = new[] { new Grid(vest, 6, 4) };
            var hiddenGun = FamilyGun("deeper-gun", 10, "hidden");
            var hiddenHelmet = Armor("deeper-helmet", 6, 1, EquipmentSlot.Headwear);
            var allowedMag = FamilyMag("allowed-vest-mag", 80, "same");
            foreach (var item in new Item[] { hiddenGun, hiddenHelmet, allowedMag }) vest.Grids[0].Put(item);
            var bag = Bag("outer-capacity-pack", 6, 6); bag.Grids[0].Put(vest); s.Offer(bag); s.Run();
            Check(s.Get(EquipmentSlot.TacticalVest) == vest && s.InFastAccess(allowedMag), "Packed vest can improve capacity while its direct magazine supplies are extracted");
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == null && s.Get(EquipmentSlot.Headwear) == null && !s.Plan.OfferedWeaponIds.Contains(hiddenGun.Id),
                "Clearing a selected vest cannot expose deeper weapons or helmets as acquisition candidates");
            s.AssertConserved(); s.Plan.Rollback();
            Check(new Item[] { hiddenGun, hiddenHelmet, allowedMag }.All(item => item.CurrentAddress.Container.ParentItem == vest),
                "Supply-only nesting exception preserves all original packed roots on rollback");
        }
        foreach (bool backpack in new[] { false, true })
        foreach (bool safeVest in new[] { false, true })
        {
            var s = new Scenario(backpack);
            var carrier = Rig("original-carrier", 4, 1, 4, 1, 4, 4); s.Equip(carrier, EquipmentSlot.TacticalVest);
            var primary = FamilyGun("retained-primary", 60, "retained"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("retained-spare", 60, "retained"));
            var vest = new Vest("earlier-plain-vest"); vest.Grids = new[] { new Grid(vest, 1, safeVest ? 4 : 1) };
            s.Offer(vest); s.Run();
            Check(s.Get(EquipmentSlot.TacticalVest) == carrier && s.InKit(vest), "An earlier plain vest stays cargo while the armored rig offers better protection");
            var previousContainer = vest.CurrentAddress.Container;
            var armor = Armor("later-better-armor", 5, 1, EquipmentSlot.ArmorVest); armor.Conflicts.Add(carrier.Id); s.Offer(armor); s.Run();
            Check(s.Get(EquipmentSlot.TacticalVest) == (safeVest ? vest : carrier) &&
                s.Get(EquipmentSlot.ArmorVest) == (safeVest ? armor : null),
                "Later armor pairs with a smaller carried vest only when real primary support and reload space fit");
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary &&
                pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Armor-plus-carried-vest transition cannot compromise the retained weapon or its reload opening");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.TacticalVest) == carrier && s.Get(EquipmentSlot.ArmorVest) == null &&
                vest.CurrentAddress.Container == previousContainer && s.InOffer(armor), "Later outfit rollback restores the earlier carried vest's exact location");
        }
        foreach (bool larger in new[] { false, true })
        foreach (bool packed in new[] { false, true })
        {
            var s = new Scenario(); var old = (Vest)s.Get(EquipmentSlot.TacticalVest); old.Grids = new[] { new Grid(old, 1, 4) };
            var armor = Armor("same-body-armor", 4, 1, EquipmentSlot.ArmorVest); s.Equip(armor, EquipmentSlot.ArmorVest);
            var primary = FamilyGun("same-primary", 60, "same"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var oldSpare = FamilyMag("existing-spare", 60, "same"); s.AddFast(oldSpare);
            var incoming = new Vest("full-larger-vest"); incoming.Grids = new[] { new Grid(incoming, larger ? 2 : 1, 4) };
            var mags = Enumerable.Range(0, larger ? 4 : 2).Select(i => FamilyMag("incoming-spare-" + i, 60, "same")).ToArray();
            foreach (var mag in mags) incoming.Grids[0].Put(mag);
            Item root = incoming;
            if (packed) { var bag = Bag("offered-pack", 6, 6); bag.Grids[0].Put(incoming); root = bag; }
            s.Offer(root); s.Run();
            Check(s.Get(EquipmentSlot.TacticalVest) == (larger ? incoming : old),
                "A full plain vest upgrades on greater capacity without requiring a protection increase; equal capacity stays unchanged");
            Check(s.Get(EquipmentSlot.ArmorVest) == armor && s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary,
                "Storage-only vest upgrade preserves the same primary and body armor");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Repacking a full incoming vest leaves actual reload space rather than accepting every magazine");
            if (larger) Check(incoming.Grids[0].FindLocationForItem(primary.GetCurrentMagazine()) != null && mags.Any(s.InFastAccess),
                "Selected full vest is cleared and repacked with useful support plus an ejection opening");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.TacticalVest) == old && s.InFastAccess(oldSpare) && s.InOffer(root) &&
                mags.All(m => m.CurrentAddress.Container.ParentItem == incoming), "Capacity-only upgrade rollback restores the original full vest and carried kit");
        }
        {
            var s = new Scenario();
            var oldCarrier = Rig("tight-original-carrier", 4, 1, 4, 1, 1, 4); s.Equip(oldCarrier, EquipmentSlot.TacticalVest);
            var oldPrimary = FamilyGun("old-supported-primary", 20, "old"); s.Equip(oldPrimary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("old-spare", 20, "old"));
            var gift = FamilyGun("earlier-gift-secondary", 100, "gift"); gift.GetCurrentMagazine().Cartridges.Items.Clear();
            s.Equip(gift, EquipmentSlot.SecondPrimaryWeapon); s.Bot.Gifts.Add(gift.Id);
            var carried = new Vest("full-carried-vest"); carried.Grids = new[] { new Grid(carried, 2, 4) };
            var mags = Enumerable.Range(0, 4).Select(i => FamilyMag("carried-gift-mag-" + i, 100, "gift")).ToArray();
            foreach (var mag in mags) carried.Grids[0].Put(mag);
            ((SearchableItem)s.Get(EquipmentSlot.Backpack)).Grids[0].Put(carried);
            var armor = Armor("later-armor-for-carried-kit", 5, 1, EquipmentSlot.ArmorVest); armor.Conflicts.Add(oldCarrier.Id);
            s.Offer(armor); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == gift && s.Get(EquipmentSlot.TacticalVest) == carried &&
                s.Get(EquipmentSlot.ArmorVest) == armor && mags.Any(s.InFastAccess),
                "Armor-only handover reuses a full carried vest and its magazines to support the earlier gifted secondary as primary");
            Check(pitTeam.BigBrain.Actions.GestureCommandAction.CanFitGearSwapReloadReserves(s.Controller.Inventory.Equipment),
                "Previously packed supplies are repacked around the new primary's reload opening");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == oldPrimary && s.Get(EquipmentSlot.SecondPrimaryWeapon) == gift &&
                s.Get(EquipmentSlot.TacticalVest) == oldCarrier && mags.All(m => m.CurrentAddress.Container.ParentItem == carried),
                "Later armor/full-carried-vest promotion restores exact weapons, carrier and nested magazine roots on rollback");
        }
        {
            var s = new Scenario(); var carrier = Rig("kept-until-valid-outfit", 4, 1, 4, 1, 4, 4);
            s.Equip(carrier, EquipmentSlot.TacticalVest);
            var primary = FamilyGun("required-current-primary", 60, "same"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            s.AddFast(FamilyMag("required-current-spare", 60, "same"));
            var blocked = new Vest("largest-unusable-vest"); blocked.Grids = Enumerable.Range(0, 8).Select(_ => new Grid(blocked, 1, 1)).ToArray();
            var fitting = new Vest("smaller-fitting-vest"); fitting.Grids = new[] { new Grid(fitting, 1, 4) };
            var armor = Armor("better-separate-armor", 5, 1, EquipmentSlot.ArmorVest); armor.Conflicts.Add(carrier.Id);
            s.Offer(blocked); s.Offer(fitting); s.Offer(armor); s.Run();
            Check(s.Get(EquipmentSlot.TacticalVest) == fitting && s.Get(EquipmentSlot.ArmorVest) == armor,
                "A larger vest's unsuitable grid shapes do not hide the next compatible usable vest");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.TacticalVest) == carrier && s.InOffer(blocked) && s.InOffer(fitting) && s.InOffer(armor),
                "Rejected first vest and accepted fallback both preserve exact trial rollback");
        }
    }
    static void CheckStoredSecondaryHandover()
    {
        foreach (bool backpack in new[] { false, true })
        foreach (bool inserted in new[] { false, true })
        {
            var s = new Scenario(backpack);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 4, 4) };
            var primary = FamilyGun("carry-primary", 70, "primary");
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(FamilyMag("primary-spare", 70, "primary"));
            var gun = new Weapon("empty-carried") { MagazineFamily = "gift" };
            if (inserted) gun.Magazine.Put(new Magazine("empty-inserted") { MagazineFamily = "gift" });
            s.Offer(gun); s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.SecondPrimaryWeapon) == gun,
                "An empty offered long gun uses the empty secondary before backpack cargo or dropping");
            Check(FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment).Rounds(gun) == 0,
                "Slot retention does not invent ammunition or declare an empty gun operational");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == null && s.InOffer(gun), "Carried-secondary fallback rolls back exactly");
        }
        foreach (bool promote in new[] { false, true })
        foreach (bool secure in new[] { false, true })
        foreach (bool looseAmmo in new[] { false, true })
        {
            var s = new Scenario(false);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 4, 4) };
            var primary = FamilyGun("retained-primary", promote ? 10 : 100, "primary");
            var oldSpare = FamilyMag("old-primary-spare", promote ? 10 : 100, "primary");
            s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon); s.AddFast(oldSpare);
            var secondary = new Weapon("earlier-empty-gift") { MagazineFamily = "gift" };
            var empty = new Magazine("gift-inserted") { MagazineFamily = "gift" }; secondary.Magazine.Put(empty);
            s.Equip(secondary, EquipmentSlot.SecondPrimaryWeapon); s.Bot.Gifts.Add(secondary.Id);
            Item nativeSecure = null;
            if (secure)
            {
                var root = new SearchableItem("full-secure") { AcceptedSlot = EquipmentSlot.SecuredContainer }; root.Grids = new[] { new Grid(root, 1, 1) };
                nativeSecure = new Item("native-secure-content") { Width = 1, Height = 1 }; root.Grids[0].Put(nativeSecure);
                s.Equip(root, EquipmentSlot.SecuredContainer);
            }
            int quality = promote ? 100 : 10;
            var spare = looseAmmo ? new Magazine("later-empty-spare") { MagazineFamily = "gift" } : FamilyMag("later-spare", quality, "gift");
            s.Offer(spare);
            if (looseAmmo) s.Offer(new Ammo("later-ammo") { WeaponFamily = "gift", StackObjectsCount = 60, PenetrationPower = quality });
            else s.Offer(FamilyMag("later-other-spare", quality, "gift"));
            s.Run();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == (promote ? secondary : primary) &&
                s.Get(EquipmentSlot.SecondPrimaryWeapon) == (promote ? primary : secondary),
                "Later supplies either promote the better stored gun or keep it as a supported secondary without a backpack");
            Check(s.InFastAccess(spare), "Later magazines are moved into fast access even when the weapon stays secondary");
            Check(FollowerWeaponSupplyBudget.Build(s.Controller.Inventory.Equipment).Rounds(secondary) > 0,
                "The stored gun receives compatible projected ammunition after a later handover");
            Check(!secure || nativeSecure.CurrentAddress.Container.ParentItem.Id == "full-secure", "Full secure contents remain untouched by slot exchange");
            Check(s.Get(EquipmentSlot.Backpack) == null, "Weapon-slot exchange never requires or fabricates a backpack");
            s.AssertConserved(); s.Plan.Rollback();
            Check(s.Get(EquipmentSlot.FirstPrimaryWeapon) == primary && s.Get(EquipmentSlot.SecondPrimaryWeapon) == secondary && s.InOffer(spare),
                "Two-slot promotion and supply changes roll back to both exact original weapon identities");
        }
        {
            var s = new Scenario(false);
            var rig = (SearchableItem)s.Get(EquipmentSlot.TacticalVest); rig.Grids = new[] { new Grid(rig, 1, 2) };
            var primary = FamilyGun("small-primary", 70, "primary"); s.Equip(primary, EquipmentSlot.FirstPrimaryWeapon);
            var gun = new Weapon("empty-large-mag") { MagazineFamily = "gift" };
            var mag = new Magazine("wide-empty-mag") { MagazineFamily = "gift", Width = 2, Height = 2 }; gun.Magazine.Put(mag);
            s.Offer(gun); s.Run();
            Check(s.Get(EquipmentSlot.SecondPrimaryWeapon) == gun && gun.GetCurrentMagazine() == null && s.InOverflow(mag),
                "An unusable oversize seated magazine cannot block retention of the empty secondary or primary reload room");
            s.AssertConserved(); s.Plan.Rollback();
            Check(gun.GetCurrentMagazine() == mag && s.InOffer(gun), "Detached dormant magazine rolls back into its exact original weapon");
        }
    }
    static Magazine Mag(string id, int pen) { var mag = new Magazine(id); mag.Cartridges.Items.Add(new Ammo(id + "-rounds") { StackObjectsCount = 30, PenetrationPower = pen }); return mag; }
    static Backpack Bag(string id, int w, int h) { var bag = new Backpack(id); bag.Grids = new[] { new Grid(bag, w, h) }; return bag; }
    static Item Armor(string id, int armorClass, float condition, EquipmentSlot slot) => new Item(id) { AcceptedSlot = slot,
        Armor = new ArmorComponent { ArmorClass = armorClass }, Repair = new RepairableComponent { Durability = condition * 100, TemplateDurability = 100 } };
    static void InstallProtection(CompoundItem carrier, string id, int armorClass, float condition, EArmorPlateCollider plates, params EBodyPartColliderType[] soft)
    {
        var part = Armor(carrier.Id + "-" + id, armorClass, condition, EquipmentSlot.ArmorVest);
        part.Armor.ArmorPlateCollidersMask = plates; part.Armor.ArmorColliders = soft;
        var slot = new Slot(carrier, EquipmentSlot.ArmorVest); carrier.Slots.Add(slot); slot.Put(part);
    }
    static CompoundItem Carrier(string id, int frontClass, float frontCondition, int backClass, float backCondition)
    {
        var carrier = new CompoundItem(id) { AcceptedSlot = EquipmentSlot.ArmorVest };
        InstallProtection(carrier, "front", frontClass, frontCondition, EArmorPlateCollider.Plate_Korund_chest);
        InstallProtection(carrier, "back", backClass, backCondition, EArmorPlateCollider.Plate_Granit_SAPI_back);
        return carrier;
    }
    static Vest Rig(string id, int frontClass, float frontCondition, int backClass, float backCondition, int width, int height)
    {
        var rig = new Vest(id); rig.Grids = new[] { new Grid(rig, width, height) };
        InstallProtection(rig, "front", frontClass, frontCondition, EArmorPlateCollider.Plate_Granit_SAPI_chest);
        InstallProtection(rig, "back", backClass, backCondition, EArmorPlateCollider.Plate_6B13_back);
        return rig;
    }
    sealed class Scenario
    {
        public InventoryController Controller = new();
        public SearchableItem OfferRoot = new("offer"), Overflow = new("overflow");
        public GiveGearPlanner Plan;
        public CompanionGearPolicy GearPolicy;
        public BotOwner Bot = new();
        readonly List<Item> initialRoots = new();
        HashSet<string> expectedIds;
        int expectedRounds;
        public List<string> NativeSplits = new();
        public HashSet<string> BorrowedIds = new();
#if DEBUG
        public Action<string> Diagnostic;
#endif
        public Scenario(bool cargo = true)
        {
            OfferRoot.Grids = new[] { new Grid(OfferRoot, 10, 12) }; Overflow.Grids = new[] { new Grid(Overflow, 64, 64) };
            var rig = new Vest("rig"); rig.Grids = new[] { new Grid(rig, 4, 4) }; Equip(rig, EquipmentSlot.TacticalVest);
            if (cargo) Equip(Bag("backpack", 8, 8), EquipmentSlot.Backpack);
            else rig.Grids = new[] { new Grid(rig, 1, 1) }; // no old helmet or full weapon can fit
        }
        public void Equip(Item item, EquipmentSlot slot)
        {
            var destination = Controller.Inventory.Equipment.GetSlot(slot);
            if (destination.ContainedItem is Item existing) { destination.Take(existing); initialRoots.Remove(existing); }
            destination.Put(item); initialRoots.Add(item);
        }
        public Item Get(EquipmentSlot slot) => Controller.Inventory.Equipment.GetSlot(slot).ContainedItem;
        public void AddFast(Item item) { ((SearchableItem)Get(EquipmentSlot.TacticalVest)).Grids[0].Put(item); }
        public void Offer(Item item) { OfferRoot.Grids[0].Put(item); initialRoots.Add(item); }
        public bool InKit(Item item) => TeammateGearSwap.Tree(Controller.Inventory.Equipment).Contains(item);
        public bool InFastAccess(Item item) => new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }
            .Select(Get).OfType<SearchableItem>().Any(r => r.Grids.Any(g => g.Items.Contains(item)));
        public bool InOffer(Item item) => TeammateGearSwap.Tree(OfferRoot).Contains(item);
        public bool InOverflow(Item item) => TeammateGearSwap.Tree(Overflow).Contains(item);
        public bool InOverflowId(string id) => TeammateGearSwap.Tree(Overflow).Any(i => i.Id == id);
        public void Run()
        {
            expectedIds = TeammateGearSwap.Tree(Controller.Inventory.Equipment).Concat(TeammateGearSwap.Tree(OfferRoot))
                .Concat(TeammateGearSwap.Tree(Overflow)).Select(i => i.Id).ToHashSet();
            expectedRounds = TeammateGearSwap.Tree(Controller.Inventory.Equipment).Concat(TeammateGearSwap.Tree(OfferRoot))
                .Concat(TeammateGearSwap.Tree(Overflow)).OfType<Ammo>().Sum(a => a.StackObjectsCount);
            Plan = new GiveGearPlanner(Controller, OfferRoot, Overflow, Bot, edit => { if (edit.SplitItem != null) NativeSplits.Add(edit.SplitItem.Id); },
                TeammateGearSwap.Tree(OfferRoot).Where(i => !BorrowedIds.Contains(i.Id)).Select(i => i.Id), GearPolicy);
#if DEBUG
            Plan.Diagnostic = Diagnostic;
#endif
            Plan.Build();
        }
        public void AssertConserved()
        {
            var all = TeammateGearSwap.Tree(Controller.Inventory.Equipment).Concat(TeammateGearSwap.Tree(OfferRoot)).Concat(TeammateGearSwap.Tree(Overflow)).ToArray();
            Check(initialRoots.All(i => all.Count(x => x.Id == i.Id) == 1), "Every original/offered root has exactly one final owner");
            Check(all.Select(i => i.Id).Distinct().Count() == all.Length, "No duplicated descendant IDs");
            var ids = all.Select(i => i.Id).ToHashSet();
            Check(expectedIds.SetEquals(ids), "Move-only Give Gear conserves every identity without creating cartridge children");
            Check(all.OfType<Ammo>().Sum(a => a.StackObjectsCount) == expectedRounds, "Ammunition quantities are conserved across package preparation");
        }
    }
}

namespace UnityEngine { public static class Mathf { public static float Clamp01(float n) => Math.Clamp(n, 0, 1); } }
namespace EFT { public class BotOwner { public HashSet<string> Gifts = new(); } }
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, TacticalVest, ArmorVest, Headwear, Earpiece, FaceCover, Eyewear, Backpack, Pockets, SecuredContainer }
    public interface IContainer { Item ParentItem { get; } void Take(Item item); void Put(Item item, ItemAddress address = null); }
    public class ItemAddress
    {
        public IContainer Container;
        public override bool Equals(object obj) => obj is ItemAddress other && GetType() == other.GetType() && ReferenceEquals(Container, other.Container);
        public override int GetHashCode() => Container?.GetHashCode() ?? 0;
    }
    public class GridItemAddress : ItemAddress
    {
        public int X, Y;
        public override bool Equals(object obj) => base.Equals(obj) && obj is GridItemAddress other && X == other.X && Y == other.Y;
        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), X, Y);
    }
    public class Item
    {
        public string Id; public int Width = 2, Height = 2, StackObjectsCount = 1; public ItemAddress CurrentAddress;
        public EquipmentSlot? AcceptedSlot; public ArmorComponent Armor; public RepairableComponent Repair;
        public HashSet<string> Conflicts = new();
        public Item(string id) { Id = id; }
        public T GetItemComponent<T>() where T : class => (typeof(T) == typeof(ArmorComponent) ? Armor : (object)Repair) as T;
    }
    public class CompoundItem : Item { public Grid[] Grids = Array.Empty<Grid>(); public List<Slot> Slots = new(); public CompoundItem(string id) : base(id) { } }
    public class SearchableItem : CompoundItem { public SearchableItem(string id) : base(id) { } }
    public class Vest : SearchableItem { public Vest(string id) : base(id) { AcceptedSlot = EquipmentSlot.TacticalVest; } }
    public class Backpack : SearchableItem { public Backpack(string id) : base(id) { AcceptedSlot = EquipmentSlot.Backpack; } }
    public class InventoryEquipment : CompoundItem
    {
        public InventoryEquipment() : base("equipment") { foreach (var slot in Enum.GetValues<EquipmentSlot>()) Slots.Add(new Slot(this, slot)); }
        public Slot GetSlot(EquipmentSlot name) => Slots.Single(s => s.Name == name);
    }
    public class Inventory { public InventoryEquipment Equipment = new(); }
    public class InventoryController { public Inventory Inventory = new(); }
    public class Slot : IContainer
    {
        public Item ParentItem { get; } public EquipmentSlot Name; public Item ContainedItem;
        public Slot(Item parent, EquipmentSlot name) { ParentItem = parent; Name = name; }
        public virtual bool CanAccept(Item item) => ParentItem is Weapon ? item is Magazine : item is Weapon w ?
            (Name == EquipmentSlot.Holster ? w.Pistol : Name is EquipmentSlot.FirstPrimaryWeapon or EquipmentSlot.SecondPrimaryWeapon && !w.Pistol) : item.AcceptedSlot == Name;
        public ItemAddress CreateItemAddress() => new() { Container = this };
        public void Take(Item item) { if (ContainedItem != item) throw new Exception("Wrong slot source"); ContainedItem = null; item.CurrentAddress = null; }
        public void Put(Item item, ItemAddress address = null)
        {
            if (ContainedItem != null || !CanAccept(item)) throw new Exception("Slot blocked");
            if (ParentItem is InventoryEquipment kit)
                foreach (var incoming in Installed(item))
                    foreach (var worn in kit.Slots.Select(s => s.ContainedItem).SelectMany(Installed))
                        if (incoming.Conflicts.Contains(worn.Id) || worn.Conflicts.Contains(incoming.Id)) throw new Exception("Native-style conflicting equipment");
            ContainedItem = item; item.CurrentAddress = CreateItemAddress();
        }
        static IEnumerable<Item> Installed(Item item)
        {
            if (item == null) yield break;
            yield return item;
            if (item is CompoundItem compound)
                foreach (var child in compound.Slots.Select(s => s.ContainedItem).SelectMany(Installed)) yield return child;
        }
    }
    public class Grid : IContainer
    {
        public Item ParentItem { get; } public int GridWidth, GridHeight; public List<Item> Items = new();
        public Grid(Item parent, int w, int h) { ParentItem = parent; GridWidth = w; GridHeight = h; }
        public GridItemAddress FindLocationForItem(Item item)
        {
            for (int y = 0; y <= GridHeight - item.Height; y++) for (int x = 0; x <= GridWidth - item.Width; x++)
                if (!Items.Where(i => !ReferenceEquals(i, item)).Any(i => { var a = (GridItemAddress)i.CurrentAddress; return x < a.X + i.Width && x + item.Width > a.X && y < a.Y + i.Height && y + item.Height > a.Y; }))
                    return new() { Container = this, X = x, Y = y };
            return null;
        }
        public void Take(Item item) { if (!Items.Remove(item)) throw new Exception("Missing grid source"); item.CurrentAddress = null; }
        public void Put(Item item, ItemAddress address = null) { address ??= FindLocationForItem(item); if (address == null) throw new Exception("Grid full"); Items.Add(item); item.CurrentAddress = address; }
    }
    public class CartridgeStack : IContainer
    {
        public FilterSet Filters = new();
        public List<Item> Items = new(); public Item ParentItem { get; set; }
        public void Take(Item item) { Items.Remove(item); item.CurrentAddress = null; }
        public void Put(Item item, ItemAddress address = null) { Items.Add(item); item.CurrentAddress = address ?? new() { Container = this }; }
    }
    public class FilterSet { public bool CheckItemFilter(Ammo ammo) => true; }
    public class Magazine : CompoundItem
    {
        public string MagazineFamily = "shared";
        public bool MechanicallyCompatible = true, LoadedCartridgesCompatible = true;
        public CartridgeStack Cartridges = new(); public int MaxCount = 30;
        public int Count => Cartridges.Items.Sum(i => i.StackObjectsCount);
        public Magazine(string id) : base(id) { Width = 1; Height = 2; Cartridges.ParentItem = this; }
        public OperationResult Apply(InventoryController controller, Ammo ammo, int count, bool simulate)
        {
            throw new Exception("Give Gear must not load magazines, even temporarily.");
        }
    }
    public class Ammo : Item { public bool IsUsed; public string WeaponFamily = "shared"; public int PenetrationPower, Damage = 40; public Ammo(string id) : base(id) { Width = Height = 1; } }
    public class ThrowWeap : Item { public ThrowWeap(string id) : base(id) { Width = Height = 1; } }
    public class Meds : Item { public Meds(string id) : base(id) { Width = Height = 1; } }
    public class ArmorPlate : Item
    {
        public bool Fits = true;
        public ArmorPlate(string id, float durability) : base(id)
        { Width = Height = 1; Armor = new ArmorComponent { ArmorClass = 5 }; Repair = new RepairableComponent { Durability = durability, TemplateDurability = 30 }; }
    }
    public class WeaponTemplate
    {
        public bool BoltAction;
        public int SingleFireRate = 450, bFirerate = 800, bEffDist;
        public Weapon.EFireMode[] weapFireType = new[] { Weapon.EFireMode.single };
    }
    public class Weapon : CompoundItem
    {
        public int ChamberAmmoCount;
        public enum EFireMode { fullauto, single, doublet, burst, doubleaction, semiauto }
        public WeaponTemplate Template = new();
        public string MagazineFamily = "shared";
        public string AmmoCaliber = "Caliber556x45NATO";
        public bool Pistol; public float ErgonomicsTotal = 50, RecoilTotal = 80; public List<Item> MissingVitalParts = new(); public Slot Magazine;
        public Weapon(string id) : base(id) { Width = 4; Height = 2; Magazine = new Slot(this, EquipmentSlot.FirstPrimaryWeapon); Slots.Add(Magazine); }
        public Magazine GetCurrentMagazine() => Magazine.ContainedItem as Magazine; public Slot GetMagazineSlot() => Magazine;
    }
    public class ArmorComponent
    {
        public int ArmorClass;
        public EArmorPlateCollider ArmorPlateCollidersMask;
        public IEnumerable<EBodyPartColliderType> ArmorColliders = new[] { EBodyPartColliderType.RibcageUp, EBodyPartColliderType.SpineTop };
    }
    public class RepairableComponent { public float Durability, TemplateDurability; }
    public interface IOperationResult { void RollBack(); }
    public class MoveResult : IOperationResult
    {
        public Item Item; public ItemAddress Before;
        public virtual void RollBack() { Item.CurrentAddress.Container.Take(Item); Before.Container.Put(Item, Before); }
    }
    public class SplitResult : MoveResult
    {
        public Item ResultItem; public int Count;
        public override void RollBack() { ResultItem.CurrentAddress.Container.Take(ResultItem); Item.StackObjectsCount += Count; }
    }
    public class AbstractMagOperationResult : IOperationResult { public IOperationResult OperationResult; public void RollBack() => OperationResult.RollBack(); }
    public class OperationResult { public bool Failed; public string Error; public IOperationResult Value; }
    public static class ItemManipulator
    {
        public static OperationResult Move(Item item, ItemAddress target, InventoryController _, bool simulate)
        {
            var before = item.CurrentAddress;
            if (before.Equals(target)) return new() { Failed = true, Error = "Native ItemAlreadyThereError" };
            before.Container.Take(item);
            try { target.Container.Put(item, target); return new() { Value = new MoveResult { Item = item, Before = before } }; }
            catch { before.Container.Put(item, before); return new() { Failed = true, Error = "Blocked" }; }
        }
    }
}
namespace pitTeam.Modules
{
    internal class GearSwapEdit
    {
        IOperationResult operation;
        internal Item SplitItem => (operation as SplitResult)?.ResultItem;
        internal static GearSwapEdit Capture(IOperationResult operation) => new() { operation = operation };
        internal void ValidateProvenance(HashSet<string> _) { }
        internal void UpdateProvenance(HashSet<string> owned)
        {
            if (operation is SplitResult split && owned.Contains(split.Item.Id)) owned.Add(split.ResultItem.Id);
        }
    }
    internal static class AllegianceScavCargo { internal static bool HasMixedStack(BotOwner _, Item item) => false; }
    internal static class TeammateGearSwap
    {
        internal static IEnumerable<Item> Tree(Item item)
        {
            if (item == null) yield break; yield return item;
            if (item is Magazine magazine) foreach (var ammo in magazine.Cartridges.Items) yield return ammo;
            if (item is CompoundItem compound)
            {
                foreach (var child in compound.Grids.SelectMany(g => g.Items).Concat(compound.Slots.Select(s => s.ContainedItem)).Where(i => i != null))
                    foreach (var nested in Tree(child)) yield return nested;
            }
        }
    }
    internal static class GiveGearHistory { internal static IEnumerable<Weapon> CarriedWeapons(BotOwner bot, InventoryEquipment kit) =>
        TeammateGearSwap.Tree(kit).OfType<Weapon>().Where(w => bot.Gifts.Contains(w.Id) && (w.CurrentAddress is GridItemAddress ||
            ReferenceEquals(w.CurrentAddress?.Container.ParentItem, kit))); }
    internal static class FollowerWeaponLooseAmmoSupport
    {
        internal static bool IsCompatible(Weapon weapon, Ammo ammo) => ammo.StackObjectsCount > 0 && weapon.MagazineFamily == ammo.WeaponFamily;
        internal static bool IsCartridgeCompatible(Weapon weapon, Ammo ammo) => ammo.StackObjectsCount > 0 && weapon.MagazineFamily == ammo.WeaponFamily;
    }
    internal static class FollowerWeaponMagazineCompatibility
    {
        internal static bool IsMechanicallyCompatible(Weapon weapon, Magazine magazine) => magazine.MechanicallyCompatible && weapon.MagazineFamily == magazine.MagazineFamily;
        internal static bool AreLoadedCartridgesCompatible(Weapon _, Magazine magazine) => magazine.LoadedCartridgesCompatible;
    }
    internal static class FollowerWeaponLooseFeedReadiness { internal static bool IsSupported(Weapon _) => false; internal static bool IsCompatibleLooseAmmo(Weapon _, Ammo ammo) => false; }
    internal class Readiness { public bool PrimaryReady, HasInsertedMagazine; public int InsertedRounds; public int[] FastAccessMagazineRounds; }
    internal static class FollowerWeaponPrimaryReadiness
    {
        internal static Readiness EvaluateActual(InventoryController controller, Weapon weapon)
        {
            var counts = new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }.Select(s => controller.Inventory.Equipment.GetSlot(s).ContainedItem)
                .OfType<SearchableItem>().SelectMany(r => r.Grids).SelectMany(g => g.Items).OfType<Magazine>()
                .Where(m => FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, m) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, m)).Select(m => m.Count).ToArray();
            int loaded = weapon.GetCurrentMagazine()?.Count ?? 0;
            int required = FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(weapon.AmmoCaliber);
            return new() { PrimaryReady = required > 0 && loaded + counts.Sum() >= required, HasInsertedMagazine = weapon.GetCurrentMagazine() != null, InsertedRounds = loaded, FastAccessMagazineRounds = counts };
        }
    }
}
namespace pitTeam.BigBrain.Actions
{
    internal static class GestureCommandAction
    {
        internal static bool CanFitGearSwapReloadReserves(InventoryEquipment kit)
        {
            var grids = new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets }.Select(s => kit.GetSlot(s).ContainedItem).OfType<SearchableItem>().SelectMany(r => r.Grids).ToArray();
            return kit.Slots.Select(s => s.ContainedItem).OfType<Weapon>().All(w => w.GetCurrentMagazine() == null || grids.Any(g => g.FindLocationForItem(w.GetCurrentMagazine()) != null));
        }
    }
}
