using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Combat;

internal static class ExUsecCombatExtension
{
    private static readonly Dictionary<BotOwner, BotFollowerPlayer> Anchors = new();
    internal static bool OwnsCombat(string role) => string.Equals(role, "exUsec", StringComparison.OrdinalIgnoreCase);

    internal static BotOwner FindLeader(BotOwner member)
    {
        var human = BossPlayers.Instance?.GetFollower(member)?.GetBoss();
        if (human?.realPlayer?.IsYourPlayer != true) return null;
        var members = BossPlayers.GetFollowers().Where(f => ReferenceEquals(f.GetBoss(), human))
            .Select(f => f.GetBot()).Where(bot => bot != null && bot != member && !bot.IsDead &&
                bot.BotState == EBotState.Active && bot.GetPlayer?.HealthController?.IsAlive == true);
        return members.FirstOrDefault(bot => bot.Profile?.Info?.Settings?.Role == WildSpawnType.bossKnight) ??
            members.FirstOrDefault(bot => bot.Profile?.Info?.Settings?.Role == WildSpawnType.followerBigPipe);
    }

    internal static void RefreshAnchors()
    {
        var current = BossPlayers.GetFollowers().Where(f => f.GetBot() is { } bot &&
            GoonCombatLayer.IsOwned(bot, "exUsec")).ToArray();
        foreach (var old in Anchors.ToArray())
            if (!current.Contains(old.Value))
            { old.Value.ClearCompanionCombatPreferences(); Anchors.Remove(old.Key); }
        foreach (var follower in current)
        {
            var bot = follower.GetBot();
            var leader = FindLeader(bot);
            bool knightPresent = leader?.Profile?.Info?.Settings?.Role == WildSpawnType.bossKnight;
            follower.SetCompanionCombatPreferences(knightPresent ? 68f : 60f,
                knightPresent ? 100f : follower.SelfDetermination);
            follower.SetCompanionCombatLeader(leader, controlsPosition: true);
            Anchors[bot] = follower;
        }
        RoguePositionCoordinator.Refresh(current);
    }

    internal static bool TryGetPushOverride(BotOwner member, out EnemyInfo target, out UnityEngine.Vector3 point)
    {
        target = null; point = default;
        if (member.Profile?.Info?.Settings?.Role != WildSpawnType.exUsec)
            return KnightSquadCoordinator.TryGetPushOverride(member, out target, out point);
        var leader = FindLeader(member);
        if (leader == null || !GoonPushLeadership.TryGetAdvanceOrder(leader, out target, out int revision, out float publishedAt) ||
            !GoonPushLeadership.CanSupersede(member, target.ProfileId, revision, publishedAt)) return false;
        target = GoonPushLeadership.KnownTarget(member, target.ProfileId);
        if (target == null) return false;
        return RoguePositionCoordinator.TryGetOrderSeed(member, target, out point);
    }

    internal static void Reset()
    {
        foreach (var follower in Anchors.Values) follower.ClearCompanionCombatPreferences();
        Anchors.Clear();
        RoguePositionCoordinator.Reset();
    }
}
