using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;

internal static class RemovalCourierTests
{
    public static void Run(string work, Action<bool, string> check)
    {
        string initialDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = work;
        try
        {
            var json = new JsonUtil([new SptJsonConverterRegistrator()]);
            var session = new MongoId("eeeeeeeeeeeeeeeeeeeeeeee");
            var storage = new FriendlyTeammateStorage(new FileUtil(), json,
                DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>(), ModeStorageTests.CreateSettings());
            storage.InitializeProfile(session);
            var cloner = new FastCloner();
            var constructor = typeof(FriendlyTeammateService).GetConstructors().Single();
            var service = (FriendlyTeammateService)constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name switch
            {
                "cloner" => (object)cloner, "jsonUtil" => json, "storage" => storage,
                "logger" => DispatchProxy.Create<ISptLogger<FriendlyTeammateService>, TestLogger>(), _ => null
            }).ToArray());
            var equipment = new MongoId();
            var gun = new MongoId();
            var scope = new MongoId();
            var pockets = new MongoId();
            var med = new MongoId();
            var tag = new MongoId();
            var teammate = new BotBase { Aid = 701, Inventory = new BotBaseInventory { Equipment = equipment, Items = [
                new Item { Id = scope, Template = new MongoId(), ParentId = gun, SlotId = "mod_scope" },
                new Item { Id = gun, Template = new MongoId(), ParentId = equipment, SlotId = "FirstPrimaryWeapon" },
                new Item { Id = pockets, Template = new MongoId(), ParentId = equipment, SlotId = "Pockets" },
                new Item { Id = med, Template = new MongoId(), ParentId = pockets, SlotId = "pocket1", Upd = new Upd { StackObjectsCount = 3 } },
                new Item { Id = tag, Template = new MongoId(), ParentId = equipment, SlotId = "Dogtag" }
            ] } };
            var selector = typeof(FriendlyTeammateService).GetMethod("BuildCurrentTeammateKitDeliveryItems", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var items = (List<Item>)selector.Invoke(service, [teammate, false])!;
            check(items.Count == 3 && items[0].Id == gun && items[0].ParentId == null, "native selection puts the actual weapon root before its previously preceding attachment");
            check(items.Single(item => item.Id == scope).ParentId == gun.ToString() && items.Single(item => item.Id == med).ParentId == null,
                "native selection preserves mounted children and returns pocket contents as a root");
            check(teammate.Inventory.Items.Single(item => item.Id == gun).ParentId == equipment.ToString(), "FastCloner keeps saved equipment immutable during preparation");
            var helperConstructor = typeof(ItemHelper).GetConstructors().Single();
            var helper = (ItemHelper)helperConstructor.Invoke(new object?[helperConstructor.GetParameters().Length]);
            var normalized = helper.ReplaceIDs(items, null).ToList();
            var stash = new MongoId();
            foreach (var root in normalized.Where(item => item.ParentId == null)) { root.ParentId = stash; root.SlotId = "main"; root.Location = null; }
            check(normalized.All(item => item.Id != gun && item.Id != scope && item.Id != med), "native ID remapping gives every returned item a fresh identity");
            check(normalized.All(item => item.ParentId == stash.ToString() || normalized.Any(parent => parent.Id.ToString() == item.ParentId)),
                "native ID remapping preserves complete mail attachment trees");
            var trader = (MongoId)typeof(FriendlyTeammateService).Assembly.GetType("pitTeam.Server.Services.FriendlyCourierTraderProfile")!
                .GetField("CourierTraderId", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            var message = new Message { Id = new MongoId(), UserId = trader, MessageType = MessageType.NpcTraderMessage,
                Text = "native test", DateTime = 100, HasRewards = true, RewardCollected = false, MaxStorageTime = 86400,
                Items = new MessageItems { Stash = stash, Data = normalized } };
            var journal = new FriendlyTeammateDeletionJournal { Aid = 701, State = "delivering", Delivery = message,
                DeliveryReceipt = 123, DeliverySourceIds = [gun.ToString(), scope.ToString(), med.ToString()] };
            storage.Write(session, "pending-deletion.json", journal);
            var saved = storage.Read<FriendlyTeammateDeletionJournal>(session, "pending-deletion.json")!;
            check(saved.Delivery!.Id == message.Id && saved.Delivery.Items!.Data!.Count == 3 && saved.DeliveryReceipt == 123,
                "native SPT JSON and encrypted database preserve prepared mailbox identity, receipt and equipment");
            var profile = new SptProfile { SptData = new Spt { Migrations = new() { ["pitFireTeam/removal-courier"] = 123 } },
                DialogueRecords = new() { [trader] = new Dialogue { Id = trader, Messages = [message], Type = MessageType.NpcTraderMessage } } };
            var restored = json.Deserialize<SptProfile>(json.Serialize(profile)!)!;
            check(restored.SptData!.Migrations!["pitFireTeam/removal-courier"] == 123 && restored.DialogueRecords![trader].Messages!.Single().Id == message.Id,
                "native player save round trip retains the mod receipt alongside a valid courier dialogue");
            var notification = new NotifierHelper(null!).CreateNewMessageNotification(message);
            check(notification.Message!.Id == message.Id, "native notification refers to the saved courier message");
            var diagnostics = new FollowerInsuranceRaidDiagnostics(storage, null!, null!, json, null!, null!,
                DispatchProxy.Create<ISptLogger<FollowerInsuranceRaidDiagnostics>, TestLogger>());
            foreach (var document in new[] { "insurance-raid-diagnostic.json", "insurance-previous-raid-diagnostic.json" })
            {
                var raid = new FollowerInsuranceRaidDiagnostic { Enabled = true, SettlementEligible = true, EndReceived = true,
                    PlayerInventoryKnown = true, ReportsComplete = true, Participants = [new() { Aid = "701", OutcomeReceived = true }],
                    InsuredItems = [new(gun.ToString(), "gun", "prapor", "701", "equipment", "FirstPrimaryWeapon"),
                        new("unrelated", "other", "prapor", "701", "equipment", "Headwear")] };
                check(FollowerInsuranceSettlementPlanner.Plan(raid).Count == 2, "native ownership fixture starts with two eligible deferred claims");
                storage.Write(session, document, raid);
            }
            diagnostics.ObserveMemberRemovalCourier(session, [gun.ToString(), gun.ToString()]);
            diagnostics.ObserveMemberRemovalCourier(session, [gun.ToString()]);
            foreach (var document in new[] { "insurance-raid-diagnostic.json", "insurance-previous-raid-diagnostic.json" })
            {
                var raid = storage.Read<FollowerInsuranceRaidDiagnostic>(session, document)!;
                check(raid.CourierItemIds.SequenceEqual(new[] { gun.ToString() }), "native durable ownership evidence is idempotent in current and preceding raid ledgers");
                check(FollowerInsuranceSettlementPlanner.Plan(raid).Single().ItemId == "unrelated", "removal suppresses only the returned item from future insurance claims");
                check(string.IsNullOrEmpty(raid.SettlementState), "ownership observation never invokes settlement or reserves a reward");
            }
        }
        finally { Environment.CurrentDirectory = initialDirectory; }
    }
}
