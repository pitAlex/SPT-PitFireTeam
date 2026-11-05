using pitTeam.GoingRogue.Quests;
using pitTeam.GoingRogue.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

internal static class QuestWaitChecks
{
    internal static void Run(Dictionary<MongoId, Quest> definitions, Action<bool, string> check)
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
    }
}
