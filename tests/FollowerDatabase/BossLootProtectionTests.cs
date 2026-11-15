using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

internal static class BossLootProtectionTests
{
    internal static void Run(FriendlyTeammateService service, FriendlyTeammateStorage storage,
        FriendlyServerSettingsService settings, JsonUtil json, ItemHelper itemHelper,
        MongoId session, BotBase knight, Action<bool, string> check)
    {
        var saved = storage.Read<BotBase>(session, "123.json")!;
        Item New(string template, string parent, string slot = "main") => new()
        { Id = new MongoId(), Template = new MongoId(template), ParentId = parent, SlotId = slot };
        var raid = json.Deserialize<BotBase>(json.Serialize(knight)!)!;
        string root = raid.Inventory!.Equipment!.Value.ToString();
        Item vest = New("5e4ac41886f77406a511c9a8", root, "ArmorVest"),
            plate = New("655746010177119f4a097ff7", vest.Id.ToString(), "Front_plate"),
            coin = New("5d235b4d86f7742e017bc88a", root),
            key = New("59387a4986f77401cc236e62", root), // Dorm 114 key.
            ammo = New("54527a984bdc2d4e668b4567", root),
            meds = New("590c678286f77426c9660122", root),
            food = New("5d1b33a686f7742523398398", root),
            grenade = New("5448be9a4bdc2dfd2f8b456a", root);
        raid.Inventory.Items!.AddRange([vest, plate, coin, key, ammo, meds, food, grenade]);
        // Identical templates with new IDs represent gifts made after the spawn snapshot.
        Item givenPlate = New(plate.Template.ToString(), root),
            givenCoin = New(coin.Template.ToString(), root),
            givenWeapon = New(raid.Inventory.Items[1].Template.ToString(), root);
        var postRaid = (FriendlyPostRaidService)typeof(FriendlyPostRaidService).GetConstructors().Single().Invoke(
            typeof(FriendlyPostRaidService).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name switch
            {
                "itemHelper" => (object)itemHelper,
                "teammateService" => service,
                "logger" => DispatchProxy.Create<ISptLogger<FriendlyPostRaidService>, TestLogger>(), _ => null
            }).ToArray());
        var strip = typeof(FriendlyPostRaidService).GetMethod("RemoveItemTreesById", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registerSpawn = typeof(FriendlyPostRaidService).GetMethod("RegisterCompanionSpawnProtection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var beginRaid = typeof(FriendlyPostRaidService).GetMethod("BeginCompanionRaid", BindingFlags.Static | BindingFlags.NonPublic)!;
        var filterReturns = typeof(FriendlyPostRaidService).GetMethod("FilterReturnItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshot = typeof(FriendlyPostRaidService).GetMethod("SnapshotProtectedRaidItemIds", BindingFlags.Static | BindingFlags.NonPublic)!;
        const string freshStart = "{\"err\":0,\"data\":{\"transition\":{\"transitionCount\":0}}}";
        try
        {
            foreach (string mode in new[] { "Restricted", "Immersive", "Extreme" })
            {
                settings.SaveAndApply(new() { GameplayMode = "Allegiance", LoadoutManagementMode = mode });
                var protectedIds = service.GetProtectedSpawnItemIdsForExtraction(raid);
                foreach (var item in new[] { vest, plate, coin, key, raid.Inventory.Items[1] })
                    check(protectedIds.Contains(item.Id.ToString()), mode + " native non-consumable is protected: " + item.Template);
                foreach (var item in new[] { ammo, meds, food, grenade })
                    check(!protectedIds.Contains(item.Id.ToString()), mode + " native consumable remains recoverable: " + item.Template);
                check(!protectedIds.Contains(root), mode + " equipment root is not a loot item");
                foreach (var gift in new[] { givenPlate, givenCoin, givenWeapon })
                    check(!protectedIds.Contains(gift.Id.ToString()), mode + " later gift has no native protection despite matching template");

                // Move looted contents into the player's equipment root before real cleanup.
                var extracted = json.Deserialize<List<Item>>(json.Serialize(raid.Inventory.Items)!)!;
                extracted.AddRange([givenPlate, givenCoin, givenWeapon]);
                strip.Invoke(postRaid, [extracted, protectedIds]);
                check(!extracted.Any(item => protectedIds.Contains(item.Id.ToString())), mode + " extraction strips protected native IDs");
                foreach (var item in new[] { ammo, meds, food, grenade, givenPlate, givenCoin, givenWeapon })
                    check(extracted.Any(found => found.Id == item.Id), mode + " extraction retains consumables and gifts: " + item.Id);

                storage.Write(session, "123.json", raid);
                check(!service.GetProtectedTeammateItemIdsForExtraction(session).Overlaps(protectedIds),
                    mode + " a stored companion preview does not protect an inactive kit");

                string profileId = raid.Id!.Value.ToString(), plateId = plate.Id.ToString(), coinId = coin.Id.ToString();
                string otherCompanionId = new MongoId().ToString();
                registerSpawn.Invoke(postRaid, [session, profileId, protectedIds]);
                check(((HashSet<string>)snapshot.Invoke(null, [session])!).SetEquals(protectedIds),
                    mode + " spawning registers the exact protected IDs");
                registerSpawn.Invoke(postRaid, [session, otherCompanionId, new HashSet<string> { givenCoin.Id.ToString() }]);
                FriendlyPostRaidProtectedItemsRequest drop = new()
                {
                    DroppingCompanionProfileId = otherCompanionId,
                    DroppedCompanionItemIds = [plateId]
                };
                check(postRaid.RegisterProtectedRaidItems(session, drop).Length == 0,
                    mode + " another actor cannot release the original owner's protected plate");

                drop.DroppingCompanionProfileId = profileId;
                drop.DroppedCompanionItemIds = [plateId, plateId, givenCoin.Id.ToString()];
                check(postRaid.RegisterProtectedRaidItems(session, drop).SequenceEqual([plateId]) &&
                      postRaid.RegisterProtectedRaidItems(session, drop).SequenceEqual([plateId]),
                    mode + " a confirmed drop releases only matching protected IDs and is idempotent");
                var dropped = json.Deserialize<List<Item>>(json.Serialize(raid.Inventory.Items)!)!;
                dropped.Single(item => item.Id == plate.Id).ParentId = root;
                var returned = json.Deserialize<List<Item>>(json.Serialize(dropped)!)!;
                postRaid.RemoveProtectedTeammateItemsFromExtractedProfile(session, new()
                {
                    Results = new() { Profile = new() { Inventory = new() { Items = dropped } } }
                });
                check(dropped.Any(item => item.Id == plate.Id) &&
                      dropped.All(item => item.Id != vest.Id && item.Id != coin.Id),
                    mode + " extraction keeps the dropped plate and strips the protected corpse kit");
                filterReturns.Invoke(postRaid, [session, returned]);
                check(returned.Any(item => item.Id == plate.Id) &&
                      returned.All(item => item.Id != vest.Id && item.Id != coin.Id),
                    mode + " return mail applies the same dropped item release");

                beginRaid.Invoke(null, [session, "{\"err\":0,\"data\":{\"transition\":{\"transitionCount\":1}}}"]);
                var carried = (HashSet<string>)snapshot.Invoke(null, [session])!;
                check(!carried.Contains(plateId) && carried.Contains(coinId) &&
                      postRaid.RegisterProtectedRaidItems(session, drop).SequenceEqual([plateId]),
                    mode + " transit preserves individual releases and original owner registration");

                beginRaid.Invoke(null, [session, freshStart]);
                check(!((HashSet<string>)snapshot.Invoke(null, [session])!).Contains(coinId) &&
                      postRaid.RegisterProtectedRaidItems(session, drop).Length == 0,
                    mode + " a fresh raid clears the previous spawn registration");
                postRaid.RemoveProtectedTeammateItemsFromExtractedProfile(session, new()
                {
                    Results = new() { Profile = new() { Inventory = new() { Items = dropped } } }
                });
                check(dropped.Any(item => item.Id == plate.Id),
                    mode + " a future raid keeps the extracted plate even while the old preview still lists it");
            }
        }
        finally
        {
            beginRaid.Invoke(null, [session, freshStart]);
            storage.Write(session, "123.json", saved);
            settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        }
    }
}
