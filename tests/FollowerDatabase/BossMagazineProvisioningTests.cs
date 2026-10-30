using pitTeam.GoingRogue.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

internal static class BossMagazineProvisioningTests
{
    internal static void Run(BossMagazineProvisioning policy, ItemHelper itemHelper,
        Func<BotBase> kit, JsonUtil json, Action<bool, string> check)
    {
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye" })
        {
            var profile = kit(); profile.Info!.Settings!.Role = role;
            string before = json.Serialize(profile)!;
            var items = profile.Inventory!.Items!;
            var weapon = items.Single(item => item.SlotId == "FirstPrimaryWeapon");
            var inserted = items.Single(item => item.SlotId == "mod_magazine");
            policy.Ensure(profile);
            var spare = items.Single(item => item.Template == inserted.Template && item.Id != inserted.Id);
            check(spare.ParentId == items.Single(item => item.SlotId == "Pockets").Id.ToString(),
                "new spare uses reload-accessible boss pockets: " + role);
            check(spare.SlotId!.StartsWith("pocket") && spare.Location != null,
                "native placement supplies a valid grid location: " + role);
            check(items.Where(item => item.ParentId == spare.Id.ToString()).Sum(item => item.Upd?.StackObjectsCount ?? 1) == 30,
                "new spare is fully loaded with generated ammunition: " + role);
            check(items.Where(item => item.ParentId == spare.Id.ToString()).All(item => item.Template.ToString() == "54527a984bdc2d4e668b4567"),
                "spare uses the generated ammo type: " + role);
            check(items.Select(item => item.Id).Distinct().Count() == items.Count,
                "spare and cartridges have unique IDs: " + role);
            string once = json.Serialize(profile)!; policy.Ensure(profile);
            check(json.Serialize(profile) == once, "existing compatible spare prevents duplicates: " + role);
            check(once != before && weapon.ParentId == profile.Inventory.Equipment!.ToString(),
                "provisioning preserves equipped weapon ownership: " + role);
        }

        var emptySpare = kit(); policy.Ensure(emptySpare);
        var e = emptySpare.Inventory!.Items!;
        var loose = e.Single(item => item.SlotId?.StartsWith("pocket") == true);
        e.RemoveAll(item => item.ParentId == loose.Id.ToString());
        string emptyBefore = json.Serialize(emptySpare)!; policy.Ensure(emptySpare);
        check(json.Serialize(emptySpare) == emptyBefore, "existing empty spare is retained for ordinary maintenance");

        var secondary = kit();
        secondary.Inventory!.Items!.Single(item => item.SlotId == "FirstPrimaryWeapon").SlotId = "SecondPrimaryWeapon";
        policy.Ensure(secondary);
        check(secondary.Inventory.Items!.Count(item => item.Template.ToString() == "55d4887d4bdc2d962f8b4570") == 2,
            "secondary primary-slot firearm also receives a spare");

        var noRoom = kit(); noRoom.Inventory!.Items!.RemoveAll(item => item.SlotId == "Pockets");
        string noRoomBefore = json.Serialize(noRoom)!;
        bool rejected = false;
        try { policy.Ensure(noRoom); } catch (InvalidDataException) { rejected = true; }
        check(rejected && json.Serialize(noRoom) == noRoomBefore,
            "no carry space rejects the kit without appending orphan items or stuffing a secure container");

        foreach (string template in new[] { "633ec7c2a6918cb895019c6c", "6275303a9f372d6ea97f9ec7" })
        {
            var internalWeapon = kit();
            internalWeapon.Inventory!.Items!.Single(item => item.SlotId == "FirstPrimaryWeapon").Template = new MongoId(template);
            string internalBefore = json.Serialize(internalWeapon)!; policy.Ensure(internalWeapon);
            check(json.Serialize(internalWeapon) == internalBefore, "internal magazine/revolver/launcher excluded: " + template);
        }

        var different = kit();
        var d = different.Inventory!.Items!;
        var pockets = d.Single(item => item.SlotId == "Pockets");
        var alternative = new Item { Id = new MongoId(), Template = new MongoId("5448c1d04bdc2dff2f8b4569"),
            ParentId = pockets.Id.ToString(), SlotId = "pocket1", Location = new ItemLocation { X = 0, Y = 0 } };
        d.Add(alternative); string alternativeBefore = json.Serialize(different)!;
        policy.Ensure(different);
        check(json.Serialize(different) == alternativeBefore, "different compatible STANAG magazine counts as an existing spare");

        var scar = kit();
        var s = scar.Inventory!.Items!;
        s.Single(item => item.SlotId == "FirstPrimaryWeapon").Template = new MongoId("6165ac306ef05c2ce828ef74");
        s.Single(item => item.SlotId == "mod_magazine").Template = new MongoId("6183d53f1cb55961fa0fdcda");
        foreach (var round in s.Where(item => item.Template.ToString() == "54527a984bdc2d4e668b4567"))
            round.Template = new MongoId("58dd3ad986f77403051cba8f");
        policy.Ensure(scar);
        var scarSpare = s.Single(item => item.Template.ToString() == "6183d53f1cb55961fa0fdcda" && item.SlotId != "mod_magazine");
        check(s.Where(item => item.ParentId == scarSpare.Id.ToString()).Sum(item => item.Upd?.StackObjectsCount ?? 1) == 20,
            "reported SCAR-H kit receives a 20-round spare");
        check(s.Where(item => item.ParentId == scarSpare.Id.ToString()).All(item => item.Template.ToString() == "58dd3ad986f77403051cba8f"),
            "SCAR-H spare retains generated M80 instead of the template default ammo");

        var dual = kit();
        var dualItems = dual.Inventory!.Items!;
        var secondWeapon = new Item { Id = new MongoId(), Template = new MongoId("6165ac306ef05c2ce828ef74"),
            ParentId = dual.Inventory.Equipment!.ToString(), SlotId = "SecondPrimaryWeapon" };
        var secondMagazine = new Item { Id = new MongoId(), Template = new MongoId("6183d53f1cb55961fa0fdcda"),
            ParentId = secondWeapon.Id.ToString(), SlotId = "mod_magazine" };
        dualItems.AddRange([secondWeapon, secondMagazine, new Item { Id = new MongoId(), Template = new MongoId("58dd3ad986f77403051cba8f"),
            ParentId = secondMagazine.Id.ToString(), SlotId = "cartridges", Upd = new() { StackObjectsCount = 20 } }]);
        policy.Ensure(dual);
        var twoSpares = dualItems.Where(item => item.SlotId?.StartsWith("pocket") == true).ToArray();
        check(twoSpares.Length == 2 && twoSpares.Select(item => item.SlotId).Distinct().Count() == 2,
            "two different firearms receive their own spares without overlapping pocket grids");
        check(twoSpares.Select(item => item.Template.ToString()).ToHashSet().SetEquals(
            ["55d4887d4bdc2d962f8b4570", "6183d53f1cb55961fa0fdcda"]), "dual-weapon spares each match their own firearm");
    }
}
