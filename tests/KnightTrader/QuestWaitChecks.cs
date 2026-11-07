using pitTeam.GoingRogue.Quests;
using pitTeam.GoingRogue.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

internal static class QuestWaitChecks
{
    internal static void Run(Dictionary<MongoId, Quest> definitions, JsonUtil json, Action<bool, string> check)
    {
        const double completed = 1793488235;
        var prerequisite = new QuestStatus
        {
            QId = new(KnightQuestPolicy.Thieves), Status = QuestStatusEnum.Success,
            StartTime = completed - 100, StatusTimers = new() { [QuestStatusEnum.Success] = completed },
            CompletedConditions = ["existing-condition"]
        };
        var player = new BotBase { Quests = [prerequisite] };
        check(KnightQuestWaits.Synchronize(player, definitions.Values), "installing a later delayed quest creates its native pending record");
        var pending = player.Quests.Single(q => q.QId.ToString() == KnightQuestPolicy.BadgeBandit);
        check(pending.Status == QuestStatusEnum.AvailableAfter && pending.AvailableAfter == completed + 3600 && pending.StartTime == 0,
            "pending deadline is one hour after actual launcher success, not installation or acceptance");
        check(!KnightQuestWaits.Synchronize(player, definitions.Values) && player.Quests.Count == 2,
            "repeated profile/list synchronization cannot restart the delay or duplicate quests");
        pending.AvailableAfter = completed + 30000;
        pending.CompletedConditions = ["preserved"];
        pending.StatusTimers = [];
        check(KnightQuestWaits.Synchronize(player, definitions.Values) && pending.AvailableAfter == completed + 3600 &&
            pending.CompletedConditions.Single() == "preserved" && pending.StatusTimers.Count == 0,
            "already affected first acceptance repairs only the wait deadline, preserving progress/status history");
        foreach (var status in new[] { QuestStatusEnum.Started, QuestStatusEnum.AvailableForFinish, QuestStatusEnum.Success, QuestStatusEnum.Fail, QuestStatusEnum.FailRestartable })
        {
            pending.Status = status; pending.AvailableAfter = completed + 50000; pending.StartTime = 123;
            check(!KnightQuestWaits.Synchronize(player, definitions.Values) && pending.Status == status && pending.StartTime == 123 &&
                pending.AvailableAfter == completed + 50000 && pending.CompletedConditions.Single() == "preserved",
                "wait repair never resets an accepted/finished/failed quest: " + status);
        }
        foreach (var status in new[] { QuestStatusEnum.Started, QuestStatusEnum.AvailableForFinish, QuestStatusEnum.Fail })
        {
            player.Quests = [prerequisite with { Status = status }];
            check(!KnightQuestWaits.Synchronize(player, definitions.Values) && player.Quests.Count == 1,
                "unfinished prerequisite cannot create a pending follow-up: " + status);
        }
        foreach (var invalid in new[] { 0d, double.NaN, double.PositiveInfinity })
        {
            player.Quests = [prerequisite with { StatusTimers = new() { [QuestStatusEnum.Success] = invalid } }];
            check(!KnightQuestWaits.Synchronize(player, definitions.Values), "unknown/invalid success time is not fabricated: " + invalid);
        }
        player.Quests = [prerequisite with { StatusTimers = [] }];
        check(!KnightQuestWaits.Synchronize(player, definitions.Values), "missing success timestamp leaves native wait behavior intact");
        player.Quests = [prerequisite with { QId = new(KnightQuestPolicy.Competition) }];
        KnightQuestWaits.Synchronize(player, definitions.Values);
        check(player.Quests.Single(q => q.QId.ToString() == KnightQuestPolicy.MyLand).AvailableAfter == completed + 3600 &&
            player.Quests.All(q => q.QId.ToString() != KnightQuestPolicy.BadgeBandit), "My Land uses its own prerequisite; Badge Bandit cannot inherit another quest's time");
        var foreign = definitions[new(KnightQuestPolicy.BadgeBandit)] with { Id = new("111111111111111111111111") };
        player.Quests = [prerequisite];
        check(!KnightQuestWaits.Synchronize(player, [foreign]) && player.Quests.Count == 1,
            "other traders' quests cannot be backfilled or retimed");
        check(prerequisite.Status == QuestStatusEnum.Success && prerequisite.StatusTimers[QuestStatusEnum.Success] == completed &&
            prerequisite.CompletedConditions.Single() == "existing-condition", "prerequisite completion, rewards and history remain untouched");

        const double reported = 1793975566.517;
        bool rejected = false;
        try { Newtonsoft.Json.JsonConvert.DeserializeObject<ClientWait>("{\"availableAfter\":1793975566.517}"); }
        catch (Newtonsoft.Json.JsonReaderException ex) { rejected = ex.Message.Contains("not a valid integer"); }
        check(rejected, "EFT's integer JSON contract reproduces the reported fractional-timestamp error");
        player.Quests = [prerequisite with { StatusTimers = new() { [QuestStatusEnum.Success] = reported - 3600 } }];
        KnightQuestWaits.Synchronize(player, definitions.Values);
        pending = player.Quests.Single(q => q.QId.ToString() == KnightQuestPolicy.BadgeBandit);
        check(pending.AvailableAfter == Math.Ceiling(reported), "fractional completion time produces a whole-second deadline without shortening the wait");
        var wire = json.Serialize(pending)!;
        check(Newtonsoft.Json.JsonConvert.DeserializeObject<ClientWait>(wire)!.AvailableAfter == 1793975567,
            "server quest serialization round-trips through EFT's strict integer reader");
        check(player.Quests[0].StatusTimers[QuestStatusEnum.Success] == reported - 3600,
            "fractional native completion history remains unchanged");
        check(!KnightQuestWaits.Synchronize(player, definitions.Values), "whole-second delayed quest calculation is idempotent");
        pending.AvailableAfter = reported;
        check(KnightQuestWaits.Synchronize(player, definitions.Values) && pending.AvailableAfter == Math.Ceiling(reported),
            "existing pending deadline is repaired by the same rounded calculation");

        foreach (var questId in definitions.Keys.Where(id => KnightQuestPolicy.Owns(id.ToString())))
        foreach (var status in new[] { QuestStatusEnum.AvailableAfter, QuestStatusEnum.Started, QuestStatusEnum.Success, QuestStatusEnum.Fail })
        {
            var saved = new QuestStatus
            {
                QId = questId, Status = status, StartTime = 123,
                AvailableAfter = reported, CompletedConditions = ["retained-objective"],
                StatusTimers = new() { [status] = reported - 3600 }
            };
            player.Quests = [saved]; // No prerequisite needed to repair the wire field.
            check(KnightQuestWaits.NormalizeAvailableAfter(player) && saved.AvailableAfter == 1793975567,
                "saved fractional deadline repaired: " + questId + " / " + status);
            check(saved.Status == status && saved.StartTime == 123 && saved.CompletedConditions.Single() == "retained-objective" &&
                saved.StatusTimers[status] == reported - 3600,
                "timestamp repair preserves quest progress and history: " + questId + " / " + status);
            check(Newtonsoft.Json.JsonConvert.DeserializeObject<ClientWait>(json.Serialize(saved)!)!.AvailableAfter == 1793975567 &&
                !KnightQuestWaits.NormalizeAvailableAfter(player), "repaired wire parses and a second read makes no changes");
        }
        foreach (double? valid in new double?[] { null, 0, 1793975567 })
        {
            pending.AvailableAfter = valid; player.Quests = [pending];
            check(!KnightQuestWaits.NormalizeAvailableAfter(player) && pending.AvailableAfter == valid,
                "absent, zero and whole-second values stay unchanged: " + valid);
        }
        pending.QId = foreign.Id; pending.AvailableAfter = reported;
        check(!KnightQuestWaits.NormalizeAvailableAfter(player) && pending.AvailableAfter == reported,
            "Knight repair never rewrites another trader's quest");
    }

    private sealed class ClientWait
    {
        [Newtonsoft.Json.JsonProperty("availableAfter")]
        public int AvailableAfter { get; set; }
    }
}
