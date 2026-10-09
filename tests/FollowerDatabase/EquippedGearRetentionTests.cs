using System.Reflection;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

internal static class EquippedGearRetentionTests
{
    private static int nextId;
    private static Item New(string? parent = null, string? slot = null) => new()
    {
        Id = new MongoId((++nextId).ToString("x24")), ParentId = parent, SlotId = slot,
        Upd = new Upd { StackObjectsCount = 1 }
    };
    public static void Run(Action<bool, string> check)
    {
        var merge = typeof(FriendlyTeammateService).GetMethod("MergeAcquiredEquipment", BindingFlags.NonPublic | BindingFlags.Static)!;
        List<Item> Merge(List<Item> saved, List<Item> live) => (List<Item>)merge.Invoke(null, [saved, live])!;
        Item root = New(), liveRoot = New();
        string oldId = root.Id.ToString(), liveId = liveRoot.Id.ToString();
        Item bag = New(oldId, "Backpack"), helmet = New(oldId, "Headwear"), pockets = New(oldId, "Pockets");
        Item meds = New(pockets.Id.ToString(), "main");
        var saved = new List<Item> { root, bag, helmet, pockets, meds };
        Item liveBag = bag with { ParentId = liveId, Upd = new Upd { StackObjectsCount = 9 } };
        Item liveHelmet = helmet with { ParentId = liveId, Upd = new Upd { StackObjectsCount = 8 } };
        var unchanged = Merge(saved, [liveRoot, liveBag, liveHelmet]);
        check(ReferenceEquals(unchanged.Single(item => item.Id == bag.Id), bag), "restored original bag preserves baseline state without upkeep");
        check(ReferenceEquals(unchanged.Single(item => item.Id == helmet.Id), helmet), "unchanged helmet state not overwritten");
        check(unchanged.Contains(meds), "baseline pockets preserved");
        foreach (string slot in new[] { "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Backpack", "TacticalVest", "ArmorVest", "Headwear", "Earpiece", "FaceCover", "Eyewear" })
        {
            Item acquired = New(liveId, slot), child = New(acquired.Id.ToString(), "mod_test");
            var result = Merge(saved, [liveRoot, acquired, child]);
            check(result.Any(item => item.Id == acquired.Id && item.ParentId == oldId), "kept acquired slot " + slot);
            check(result.Contains(child), "kept acquired attachment " + slot);
            check(result.Count(item => item.ParentId == oldId && item.SlotId == slot) == 1, "one root per slot " + slot);
        }
        Item oldGun = New(oldId, "FirstPrimaryWeapon"), oldMag = New(oldGun.Id.ToString(), "mod_magazine");
        Item newGun = New(liveId, "FirstPrimaryWeapon");
        var moved = Merge([root, oldGun, oldMag], [liveRoot, newGun,
            oldGun with { ParentId = liveId, SlotId = "SecondPrimaryWeapon" }, oldMag with { }]);
        check(moved.Count(item => item.Id == oldGun.Id) == 1 && moved.Count(item => item.Id == newGun.Id) == 1,
            "moving primary to secondary and adding primary saves each once");
        check(moved.Single(item => item.Id == oldGun.Id).SlotId == "SecondPrimaryWeapon", "old gun stays secondary");
        check(moved.Count(item => item.Id == oldMag.Id) == 1, "moved gun magazine saved once");
        Item oldPlate = New(helmet.Id.ToString(), "mod_plate"), newPlate = New(helmet.Id.ToString(), "mod_plate");
        var plates = Merge([root, helmet, oldPlate], [liveRoot, liveHelmet, newPlate]);
        check(plates.Contains(newPlate) && !plates.Contains(oldPlate), "new slotted attachment retained without upkeep");
        check(ReferenceEquals(plates.Single(item => item.Id == helmet.Id), helmet), "attachment change preserves carrier condition");
        Item cargo = New(bag.Id.ToString(), "main"); cargo.Location = new ItemLocation { X = 0, Y = 0 };
        Item pocketCargo = New(pockets.Id.ToString(), "main");
        var noCargo = Merge(saved, [liveRoot, liveBag, cargo, pockets with { ParentId = liveId }, pocketCargo]);
        check(!noCargo.Contains(cargo) && !noCargo.Contains(pocketCargo), "grid and pocket cargo not donated");
        check(saved.Single(item => item.Id == bag.Id).ParentId == oldId, "saved source not mutated");
    }
}
