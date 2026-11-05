using System.Collections.Concurrent;
using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

internal static partial class Program
{
    private static void TestMedkits(FriendlyTeammateService service, FriendlyTeammateStorage storage, SaveServer save,
        JsonUtil json, FastCloner cloner, MongoId session, PmcData player, GlobalTable globals)
    {
        var inventoryBefore = cloner.Clone(player.Inventory)!;
        string traderBefore = json.Serialize(player.TradersInfo)!;
        string ownHealth = json.Serialize(player.Health)!;
        var injury = storage.Read<BotBase>(session, "124.json")!;
        injury.Aid = 123;
        injury.Health!.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        void ResetHealth() => storage.Write(session, "123.json", cloner.Clone(injury)!);
        var inv = player.Inventory!;
        Item NewItem(string parent, string tpl = "544fb45d4bdc2dee738b4568", double? resource = null)
        {
            var item = new Item { Id = new MongoId(), Template = tpl, ParentId = parent, SlotId = "hideout" };
            if (resource.HasValue) item.Upd = new() { MedKit = new() { HpResource = resource } };
            inv.Items!.Add(item);
            return item;
        }
        var stash = inv.Stash!.Value.ToString();
        var equipment = NewItem(""); inv.Equipment = equipment.Id;
        var sorting = NewItem(""); inv.SortingTable = sorting.Id;
        var nestedContainer = NewItem(stash, new MongoId().ToString());
        var nestedContainer2 = NewItem(nestedContainer.Id.ToString(), new MongoId().ToString());
        var nested = NewItem(nestedContainer2.Id.ToString());
        var equipped = NewItem(equipment.Id.ToString());
        var sorted = NewItem(sorting.Id.ToString());
        var lockedContainer = NewItem(stash, new MongoId().ToString());
        lockedContainer.Upd = new() { PinLockState = PinLockState.Locked };
        var locked = NewItem(lockedContainer.Id.ToString());
        var selfLocked = NewItem(stash); selfLocked.Upd = new() { PinLockState = PinLockState.Locked };
        var detached = NewItem(new MongoId().ToString());
        var cycleA = NewItem(new MongoId().ToString());
        var cycleB = NewItem(cycleA.Id.ToString()); cycleA.ParentId = cycleB.Id.ToString();
        var empty = NewItem(stash, resource: 0);
        var tooMuch = NewItem(stash, resource: 401);
        var notMedkit = NewItem(stash, "544fb25a4bdc2dfb738b4567", 50); // bandage
        var surgery = NewItem(stash, "5d02778e86f774203e7dedbe", 5);
        var medsWithChild = NewItem(stash); NewItem(medsWithChild.Id.ToString(), new MongoId().ToString());
        ResetHealth();
        var available = service.GetTeammateHealth(session, new() { Aid = "123", IncludeMedkits = true }, false);
        Check(available.EligibleMedkitIds!.ToHashSet().SetEquals(new[] { nested.Id.ToString(), equipped.Id.ToString(), sorted.Id.ToString() }),
            "picker includes nested/equipment/sorting kits and excludes locked, detached, cyclic, empty, invalid and non-medkit items");
        Check(available.InsurancePlayerRoubles == null, "listing medkits does not transmit or mutate player payment state");
        foreach (var invalid in new[] { locked, selfLocked, detached, cycleA, empty, tooMuch, notMedkit, surgery, medsWithChild })
            Refuses(() => service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = invalid.Id.ToString() }, true), "ProfileHealingMedkitUnavailable");
        inv.Items!.Add(cloner.Clone(nested)!);
        Check(service.GetTeammateHealth(session, new() { Aid = "123", IncludeMedkits = true }, false).EligibleMedkitIds!.Count == 0,
            "ambiguous duplicate inventory IDs cannot be spent");
        inv.Items.RemoveAt(inv.Items.Count - 1);
        // Remove synthetic malformed entries before serializing a real payment.
        inv.Items = inv.Items.Where(item => item.Id == inv.Stash || item.Template == Money.ROUBLES
            || item == nestedContainer || item == nestedContainer2 || item == nested || item == equipment || item == equipped || item == sorting || item == sorted).ToList();
        string MoneySnapshot() => json.Serialize(player.Inventory!.Items!.Where(item => item.Template == Money.ROUBLES).ToList())!;
        string moneyBefore = MoneySnapshot();

        var quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = nested.Id.ToString() }, true);
        Check(nested.Upd?.MedKit == null && FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280,
            "medkit quote leaves default full resource and teammate HP unchanged");
        var request = new FriendlyTeammateTreatmentRequest { Aid = "123", QuoteToken = quote.QuoteToken };
        var result = service.HealTeammate(session, request);
        Check(result.CompletedToken == quote.QuoteToken && result.UsedMedkitId == nested.Id.ToString()
            && result.MedkitResourceRemaining == 120 && FriendlyTeammateTreatmentPricing.MissingHealth(result.Health) == 0,
            "full medkit spends exactly missing HP, not hpResourceRate, and returns receipt plus remaining resource");
        Check(MoneySnapshot() == moneyBefore && json.Serialize(player.TradersInfo) == traderBefore
            && json.Serialize(player.Health) == ownHealth, "medkit healing preserves roubles, Therapist state and player HP");
        var disk = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Profile.SptProfile>(File.ReadAllText(Path.Combine("user", "profiles", session + ".json")))!;
        Check(disk.CharacterData!.PmcData!.Inventory!.Items!.Single(item => item.Id == nested.Id).Upd!.MedKit!.HpResource == 120,
            "medkit resource debit is verified on disk before HP is granted");
        Check(service.HealTeammate(session, request).MedkitResourceRemaining == 120, "replaying completed medkit token cannot spend again");
        Check(service.GetTeammateHealth(session, new() { Aid = "123", QuoteToken = quote.QuoteToken, SyncPayment = true }, false).CompletedToken == quote.QuoteToken,
            "lost successful response can be resolved by matching receipt");
        Check(service.GetTeammateHealth(session, new() { Aid = "124", QuoteToken = quote.QuoteToken, SyncPayment = true }, false).CompletedToken == null,
            "receipt cannot be borrowed by another teammate");

        ResetHealth();
        var partial = player.Inventory!.Items!.Single(item => item.Id == equipped.Id);
        partial.Upd = new() { MedKit = new() { HpResource = 50.25 } };
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = partial.Id.ToString() }, true);
        result = service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken });
        Check(result.Health.BodyParts!["Head"].Health!.Current == 60 && result.Health.BodyParts["Chest"].Health!.Current == 30.25
            && result.Health.BodyParts["Stomach"].Health!.Current == 20, "partial medicine restores exact fractional HP in stable body-part order");
        Check(result.MedkitItem == null && result.MedkitResourceRemaining == 0
            && !player.Inventory.Items!.Any(item => item.Id == partial.Id), "exhausted kit is removed and partial wound remains");
        var recovered = service.GetTeammateHealth(session, new() { Aid = "123", QuoteToken = quote.QuoteToken, SyncPayment = true }, false);
        Check(recovered.CompletedToken == quote.QuoteToken && FriendlyTeammateTreatmentPricing.MissingHealth(recovered.Health) > 0,
            "partial healing is successful by receipt even though HP remains wounded");

        ResetHealth();
        var kit = player.Inventory.Items!.Single(item => item.Id == sorted.Id);
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = kit.Id.ToString() }, true);
        kit.Upd = new() { MedKit = new() { HpResource = 399 } };
        Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken }), "ProfileHealingMedkitUnavailable");
        Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280, "stale medkit resource quote grants no healing");
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = kit.Id.ToString() }, true);
        kit.Upd.PinLockState = PinLockState.Locked;
        Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken }), "ProfileHealingMedkitUnavailable");
        kit.Upd.PinLockState = null;
        // Therapist availability does not prevent using owned medicine.
        player.TradersInfo![Traders.THERAPIST].Unlocked = false;
        Check(service.GetTeammateHealth(session, new() { Aid = "123" }, true).Price == -1, "unavailable Therapist disables only money choice");
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = kit.Id.ToString() }, true);
        service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken });
        Check(player.TradersInfo[Traders.THERAPIST].Unlocked == false, "medkit treatment does not require or unlock Therapist");
        player.TradersInfo[Traders.THERAPIST].Unlocked = true;

        // Natural regeneration between selection and commit reduces resource consumption.
        ResetHealth();
        kit = player.Inventory.Items!.Single(item => item.Id == sorted.Id);
        kit.Upd!.MedKit!.HpResource = 400;
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = kit.Id.ToString() }, true);
        var journal = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
        var beforeRecovery = json.Deserialize<BotBaseHealth>(journal.HealthBefore)!;
        beforeRecovery.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        journal.HealthBefore = json.Serialize(beforeRecovery)!;
        storage.Write(session, "pending-treatment.json", journal);
        var recovering = cloner.Clone(injury)!; recovering.Health = beforeRecovery;
        storage.Write(session, "123.json", recovering);
        service.GetTeammateHealth(session, new() { Aid = "123" }, false);
        result = service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken });
        Check(result.MedkitResourceRemaining > 120 && result.MedkitResourceRemaining < 400,
            "elapsed hideout recovery reduces medkit consumption without invalidating the quote");

        // Player save failure leaves medicine debit journaled but grants no teammate HP.
        ResetHealth();
        quote = service.GetTeammateHealth(session, new() { Aid = "123", MedkitId = kit.Id.ToString() }, true);
        string playerFile = Path.Combine("user", "profiles", session + ".json");
        save.SaveProfileAsync(session).GetAwaiter().GetResult();
        File.Move(playerFile, playerFile + ".medkit-before-failure"); Directory.CreateDirectory(playerFile);
        Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = quote.QuoteToken }), "ProfileHealingRecoveryFailed");
        Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280,
            "failed medkit save never grants free HP");
        Directory.Delete(playerFile); File.Move(playerFile + ".medkit-before-failure", playerFile);
        Refuses(() => service.RecoverTeammateTreatment(session), "ProfileHealingRecoveryFailed");
        ((ConcurrentDictionary<MongoId, string>)typeof(SaveServer).GetField("saveMd5", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(save)!).Clear();
        service.RecoverTeammateTreatment(session);
        journal = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
        Check(journal.State == "complete" && FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) > 0,
            "verified retry completes partial medkit healing exactly once after native cached-hash failure");
        double missing = FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health);
        service.RecoverTeammateTreatment(session);
        Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == missing,
            "completed partial treatment recovery is idempotent");
        player.Inventory = inventoryBefore;
    }
}
