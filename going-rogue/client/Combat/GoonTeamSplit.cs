using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Combat;

// The latch survives peaceful intervals; only the combat episode changes execution.
internal static class GoonTeamSplit
{
    private static readonly Dictionary<pitAIBossPlayer, BotOwner> Splits = new();
    private static readonly HashSet<BotFollowerPlayer> Applied = new();
    private static bool refreshing;

    private static bool Alive(BotOwner bot) => bot != null && !bot.IsDead &&
        bot.BotState != EBotState.Disposed && bot.GetPlayer?.HealthController?.IsAlive == true;
    private static bool UnitRole(BotOwner bot) => bot.Profile?.Info?.Settings?.Role is
        WildSpawnType.bossKnight or WildSpawnType.followerBigPipe or WildSpawnType.followerBirdEye or WildSpawnType.exUsec;
    private static BotOwner Commander(IEnumerable<BotFollowerPlayer> members) =>
        members.Select(f => f.GetBot()).FirstOrDefault(b => b.Profile.Info.Settings.Role == WildSpawnType.bossKnight) ??
        members.Select(f => f.GetBot()).FirstOrDefault(b => b.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe);
    private static BotFollowerPlayer[] Members(pitAIBossPlayer boss) => BossPlayers.GetFollowers()
        .Where(f => ReferenceEquals(f.GetBoss(), boss) && Alive(f.GetBot())).ToArray();
    private static bool Eligible(BotFollowerPlayer[] members, BotOwner commander) => commander != null &&
        members.Any(f => f.GetBot() != commander && UnitRole(f.GetBot())) &&
        members.Any(f => !UnitRole(f.GetBot()));

    internal static bool Available(pitAIBossPlayer boss)
    {
        if (GameplayModeAddons.Current != GameplayMode.Allegiance || boss?.realPlayer?.IsYourPlayer != true) return false;
        var members = Members(boss);
        return Eligible(members, Commander(members));
    }

    internal static bool SetSplit(pitAIBossPlayer boss, bool split)
    {
        if (split)
        {
            var members = Members(boss);
            var commander = Commander(members);
            if (!Eligible(members, commander) || Splits.ContainsKey(boss)) return false;
            Splits[boss] = commander;
        }
        else if (!Splits.Remove(boss)) return false;
        Logger.LogInfo($"[GoingRogue][TeamSplit] boss={boss.realPlayer.ProfileId} split={split}");
        Refresh();
        return true;
    }

    internal static BotOwner CombatCommander(BotOwner member)
    {
        var follower = BossPlayers.Instance?.GetFollower(member);
        return follower?.TeamSplitCombatActive == true && follower.GetBoss() is { } boss &&
            Splits.TryGetValue(boss, out var commander) && commander != member && Alive(commander) ? commander : null;
    }

    internal static void Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var desired = new HashSet<BotFollowerPlayer>();
            foreach (var pair in Splits.ToArray())
            {
                var boss = pair.Key;
                var members = Members(boss);
                if (GameplayModeAddons.Current != GameplayMode.Allegiance ||
                    boss.realPlayer?.IsYourPlayer != true ||
                    !Eligible(members, pair.Value) || Commander(members) != pair.Value ||
                    !members.Any(f => f.GetBot() == pair.Value))
                {
                    Splits.Remove(boss);
                    Logger.LogInfo("[GoingRogue][TeamSplit] discarded: composition changed");
                    continue;
                }
                if (!boss.HasCombatCommandContext) continue;
                foreach (var follower in members.Where(f => UnitRole(f.GetBot())))
                {
                    var role = follower.GetBot().Profile.Info.Settings.Role;
                    follower.SetTeamSplitCombat(true, role == WildSpawnType.exUsec ? (float?)null : 70f, pair.Value);
                    desired.Add(follower);
                }
            }
            foreach (var old in Applied.Where(f => !desired.Contains(f)).ToArray()) old.SetTeamSplitCombat(false);
            Applied.Clear();
            Applied.UnionWith(desired);
        }
        finally { refreshing = false; }
    }

    internal static void Reset()
    {
        foreach (var follower in Applied) follower.SetTeamSplitCombat(false);
        Applied.Clear(); Splits.Clear();
    }
}
