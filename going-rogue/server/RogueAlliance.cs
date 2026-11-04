using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace pitTeam.GoingRogue.Server;

internal static class RogueAlliance
{
    private static TemplateTable? templates;
    internal static void Configure(TemplateTable table) => templates = table;
    internal static int Completed(BotBase player) => RogueAlliancePolicy.Completed(id =>
        player.Quests?.Any(q => q.QId.ToString() == id && q.Status == QuestStatusEnum.Success) == true);
    // Resolve at admission, after other mods have registered their quests. Keep
    // repeatable definitions profile-local and never cache a player's conflict flag.
    internal static bool Conflict(BotBase player) => player.Quests?.Any(status =>
        status.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish &&
        HasHostileObjectives(Resolve(player, status.QId))) == true;
    private static Quest? Resolve(BotBase player, SPTarkov.Server.Core.Models.Common.MongoId id)
    {
        if (templates?.Quests?.TryGetValue(id, out var quest) == true) return quest;
        return player.RepeatableQuests?.SelectMany(group => group.ActiveQuests ?? [])
            .FirstOrDefault(quest => quest.Id == id);
    }
    internal static bool HasHostileObjectives(Quest? quest) => quest?.Conditions?.AvailableForFinish?.Any(objective =>
        objective.ConditionType == "CounterCreator" && objective.Counter?.Conditions?.Any(condition =>
            RogueAlliancePolicy.HostileKill(condition.ConditionType, condition.SavageRole ?? [])) == true) == true;
    internal static string? Restriction(BotBase player, string role)
    {
        if (Conflict(player)) return "RogueAllianceHostileTask";
        double standing = player.TradersInfo?.TryGetValue(KnightTraderCatalog.Id, out var info) == true ? info.Standing ?? 0d : 0d;
        return RogueAlliancePolicy.CanDeploy(role, standing, false) ? null : "RogueAllianceStandingLow";
    }
    internal static string? DeclineMessageKey(string restriction) => restriction switch
    {
        "RogueAllianceHostileTask" => "RogueAllianceHostileTaskReply",
        "RogueAllianceStandingLow" => "RogueAllianceStandingLowReply",
        _ => null
    };
    // The in-raid recruitment roll already decided willingness. Do not roll levels again.
    internal static int? RosterInvitation(BotBase player) => Completed(player) == RogueAlliancePolicy.Quests.Length && Restriction(player, "exUsec") == null ? 100 : 0;
    internal static bool PrepareMember(BotBase member)
    {
        if (member.Info == null || member.Info.Side == "Usec") return false;
        member.Info.Side = "Usec";
        return true;
    }
}
