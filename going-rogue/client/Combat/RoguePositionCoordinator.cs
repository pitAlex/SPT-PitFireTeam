using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

// Native Knight slots remain Pipe/Birdeye's. Rogues share a sparse, validated
// position pool under whichever living commander currently owns the unit.
internal static class RoguePositionCoordinator
{
    internal const float Spacing = 6f;
    private sealed class Member
    {
        internal BotOwner Leader;
        internal string Enemy;
        internal Vector3 Point, LeaderAnchor, EnemyAnchor;
        internal bool Assigned;
        internal int NextSeed;
        internal int AvailableCount;
        internal readonly FollowerCombatCommon Contacts;
        internal Member(BotOwner owner) => Contacts = new FollowerCombatCommon(owner);
    }
    private static readonly Dictionary<BotOwner, Member> Members = new();
    private static float nextShare;
    private static int probeWindow = -1, probes;

    internal static void Reset() { Members.Clear(); nextShare = 0f; probeWindow = -1; probes = 0; }
    private static bool Eligible(BotOwner bot) => bot != null && !bot.IsDead && bot.BotState == EBotState.Active &&
        bot.GetPlayer?.HealthController?.IsAlive == true && bot.Profile?.Info?.Settings?.Role == WildSpawnType.exUsec &&
        BossPlayers.Instance?.GetFollower(bot)?.GetBot() == bot;
    internal static void ReleaseReservation(BotOwner bot)
    { if (Members.TryGetValue(bot, out var member)) member.Assigned = false; }
    internal static void Refresh(IEnumerable<BotFollowerPlayer> current)
    {
        var actors = current.Select(f => f.GetBot()).ToArray();
        foreach (var old in Members.Keys.Where(bot => !actors.Contains(bot)).ToArray()) Members.Remove(old);
        foreach (var bot in actors)
        {
            if (!Members.TryGetValue(bot, out var member)) Members[bot] = member = new Member(bot);
            var leader = ExUsecCombatExtension.FindLeader(bot);
            if (member.Leader != leader) { member.Assigned = false; member.NextSeed = 0; }
            member.Leader = leader;
        }
        if (Time.time < nextShare) return;
        nextShare = Time.time + 1f;
        foreach (var pair in Members)
        {
            var bot = pair.Key; var member = pair.Value;
            var enemy = member.Leader?.Memory.GoalEnemy?.Person?.AIData?.BotOwner;
            var follower = BossPlayers.Instance?.GetFollower(bot);
            if (enemy == null || follower == null || follower.CombatRegroupUsesBossAnchor || follower.HasOrderedPushTargetLock ||
                follower.GetBoss()?.GetBossLogic()?.IsHitted == true && !FollowerCombatAnchor.IsCombatIndependent(bot) ||
                follower.TryPeekActiveCommand(out _, out _, out _) || bot.Medecine.Using || bot.Memory.IsUnderFire ||
                bot.Memory.GoalEnemy?.IsVisible == true || bot.Memory.GoalEnemy?.CanShoot == true ||
                bot.Memory.GoalEnemy is { } own && Time.time - own.PersonalLastSeenTime < 2.5f ||
                GoonPushLeadership.KnownTarget(bot, enemy.ProfileId) == null) continue;
            member.Contacts.TryUseSupportGoalEnemy(enemy, "exUsec.command.report", out _);
        }
    }

    private static Member State(BotOwner bot)
    {
        if (!Members.TryGetValue(bot, out var member)) Members[bot] = member = new Member(bot);
        var leader = ExUsecCombatExtension.FindLeader(bot);
        if (member.Leader != leader) { member.Assigned = false; member.NextSeed = 0; }
        member.Leader = leader;
        return member;
    }
    private static bool Current(BotOwner bot, Member member, EnemyInfo enemy)
        => member.Assigned && member.Leader != null && member.Leader == ExUsecCombatExtension.FindLeader(bot) &&
            member.Enemy == enemy?.ProfileId && member.Leader.Memory.GoalEnemy?.ProfileId == member.Enemy &&
            enemy.Person?.HealthController?.IsAlive == true &&
            FollowerEnemyTracking.TryGetKnownPosition(enemy, out var anchor, out _) &&
            (anchor - member.EnemyAnchor).sqrMagnitude <= 100f &&
            (member.Leader.Position - member.LeaderAnchor).sqrMagnitude <= 64f;

    private static bool CommandTarget(Member member, EnemyInfo enemy)
        => member.Leader != null && enemy != null && member.Leader.Memory.GoalEnemy?.ProfileId == enemy.ProfileId &&
            !member.Leader.Medecine.Using && !member.Leader.Memory.IsUnderFire &&
            !FollowerCombatCommon.WasHitRecently(member.Leader, 1f) &&
            GoonPushLeadership.KnownTarget(member.Leader, enemy.ProfileId) != null;

    internal static bool TryGetOrderSeed(BotOwner bot, EnemyInfo enemy, out Vector3 seed)
    {
        seed = default;
        if (!Eligible(bot)) return false;
        var member = State(bot);
        if (!CommandTarget(member, enemy) || GoonPushLeadership.KnownTarget(bot, enemy.ProfileId) == null ||
            !FollowerEnemyTracking.TryGetKnownPosition(enemy, out var anchor, out _)) return false;
        if (Current(bot, member, enemy)) { seed = member.Point; return true; }
        return TrySeed(bot, member, anchor, member.NextSeed++ % 6, out seed);
    }

    private static bool TrySeed(BotOwner bot, Member member, Vector3 enemyAnchor, int attempt, out Vector3 point)
    {
        point = default;
        var available = BossPlayers.GetFollowers().Where(f => f.GetBot() is { } actor &&
            actor.Profile?.Info?.Settings?.Role == WildSpawnType.exUsec && !actor.IsDead &&
            actor.BotState == EBotState.Active && actor.GetPlayer?.HealthController?.IsAlive == true &&
            ExUsecCombatExtension.FindLeader(actor) == member.Leader && !f.CombatRegroupUsesBossAnchor &&
            !f.HasOrderedPushTargetLock && !f.TryPeekActiveCommand(out _, out _, out _) && !actor.Medecine.Using &&
            !actor.Memory.IsUnderFire && !FollowerCombatCommon.WasHitRecently(actor, 1f) &&
            !(actor.Memory.GoalEnemy?.IsVisible == true && actor.Memory.GoalEnemy.CanShoot))
            .Select(f => f.GetBot()).OrderBy(actor => actor.ProfileId, StringComparer.Ordinal).ToArray();
        int index = Array.IndexOf(available, bot);
        member.AvailableCount = available.Length;
        // Ordered-push callers are temporarily absent from the ordinary assignment pool.
        if (index < 0) index = available.Length;
        int count = Math.Max(available.Length, index + 1);
        var forward = enemyAnchor - member.Leader.Position; forward.y = 0f;
        if (!FollowerCombatCommon.IsFinite(forward) || forward.sqrMagnitude < 0.01f) return false;
        forward.Normalize();
        var side = new Vector3(-forward.z, 0f, forward.x);
        float angle = (count == 1 ? 0f : -65f + 130f * index / (count - 1)) + (attempt % 3 - 1) * 12f;
        float radians = angle * Mathf.Deg2Rad;
        float radius = attempt < 3 ? 12f : 20f;
        point = member.Leader.Position - forward * (Mathf.Cos(radians) * radius) + side * (Mathf.Sin(radians) * radius);
        return true;
    }

    internal static bool TryPlan(BotOwner bot, EnemyInfo enemy, FollowerCombatCommon common, out Vector3 point,
        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    {
        point = default; decision = default;
        if (!Eligible(bot)) return false;
        var member = State(bot);
        if (!CommandTarget(member, enemy) || GoonPushLeadership.KnownTarget(bot, enemy.ProfileId) == null) return false;
        if (Current(bot, member, enemy) && (member.Point - bot.Position).sqrMagnitude <= 4f) return false;
        int window = (int)(Time.time * 4f);
        if (window != probeWindow) { probeWindow = window; probes = 0; }
        float best = float.MaxValue;
        bool found = false;
        for (int attempt = 0; attempt < 6 && probes < 12; attempt++)
        {
            bool retained = Current(bot, member, enemy);
            if (!TryGetOrderSeed(bot, enemy, out var seed)) break;
            probes++;
            if (!common.TryPlanAssignedFiringPositionDecision(enemy, seed, "exUsec.command.position", out var candidate,
                    out var planned, preferBackline: false, enforceMarksmanPositionPolicy: false) ||
                !FollowerCombatCommon.IsFinite(candidate) || !FollowerCombatCommon.HasSupportedCombatGround(candidate) || !Free(bot, candidate))
            { if (retained) member.Assigned = false; continue; }
            float score = Vector3.Distance(bot.Position, candidate) + 0.35f * Vector3.Distance(member.Leader.Position, candidate);
            if (score >= best) continue;
            found = true; best = score; point = candidate; decision = planned;
            if (Current(bot, member, enemy)) break;
        }
        if (!found) return false;
        return TryReserve(bot, enemy, point);
    }

    internal static bool TryReserve(BotOwner bot, EnemyInfo enemy, Vector3 point)
    {
        if (!Eligible(bot)) return false;
        var member = State(bot);
        if (!CommandTarget(member, enemy) || GoonPushLeadership.KnownTarget(bot, enemy.ProfileId) == null || !FollowerCombatCommon.IsFinite(point) ||
            !FollowerCombatCommon.HasSupportedCombatGround(point) || !Free(bot, point) ||
            !FollowerEnemyTracking.TryGetKnownPosition(enemy, out var anchor, out _)) return false;
        bool changed = !member.Assigned || member.Enemy != enemy.ProfileId || (member.Point - point).sqrMagnitude > 1f;
        member.Assigned = true; member.Point = point; member.Enemy = enemy.ProfileId;
        member.LeaderAnchor = member.Leader.Position; member.EnemyAnchor = anchor;
        if (changed) BattleRecorder.RecordObjectiveDiagnostic(bot, "exUsec.command", "positionAssigned", "spacedFiringPoint",
            () => new { commander = member.Leader.ProfileId, available = member.AvailableCount, point, spacing = Spacing });
        return true;
    }

    private static bool Crowded(Vector3 point, Vector3 other)
    {
        if (!FollowerCombatCommon.IsFinite(other) || Mathf.Abs(point.y - other.y) > 3f) return false;
        var delta = point - other; delta.y = 0f;
        return delta.sqrMagnitude < Spacing * Spacing;
    }
    private static bool Free(BotOwner bot, Vector3 point)
    {
        var human = BossPlayers.Instance?.GetFollower(bot)?.GetBoss();
        if (human == null) return false;
        if (human.realPlayer != null && Crowded(point, human.realPlayer.Position)) return false;
        foreach (var follower in BossPlayers.GetFollowers().Where(f => ReferenceEquals(f.GetBoss(), human)))
        {
            var ally = follower.GetBot();
            if (ally == null || ally == bot || ally.IsDead || ally.GetPlayer?.HealthController?.IsAlive != true) continue;
            if (Crowded(point, ally.Position) || ally.Mover?.HasPathAndNoComplete == true &&
                ally.GoToSomePointData?.HaveTarget() == true && Crowded(point, ally.GoToSomePointData.Point)) return false;
            if (KnightSquadCoordinator.TryGetMemberPosition(ally, out var native) && Crowded(point, native)) return false;
        }
        foreach (var pair in Members)
            if (pair.Key != bot && !pair.Key.IsDead && pair.Key.GetPlayer?.HealthController?.IsAlive == true &&
                ReferenceEquals(BossPlayers.Instance?.GetFollower(pair.Key)?.GetBoss(), human) &&
                Current(pair.Key, pair.Value, pair.Key.Memory.GoalEnemy) && Crowded(point, pair.Value.Point)) return false;
        return true;
    }
}
