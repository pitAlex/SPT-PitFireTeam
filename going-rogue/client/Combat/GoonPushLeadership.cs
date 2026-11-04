using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

internal static class GoonPushLeadership
{
    private sealed class PlayerPush
    {
        internal int Sequence, Directive;
        internal string KnightTarget;
        internal float IssuedAt;
        internal BotOwner Leader;
    }
    private static readonly Dictionary<string, bool> Verdicts = new();
    private static readonly Dictionary<BotOwner, PlayerPush> PlayerPushes = new();
    private static readonly Dictionary<BotOwner, (string Target, bool Advance, bool Searching)> AutonomousIntents = new();
    private static readonly Dictionary<BotOwner, (int Revision, float At)> IntentRevisions = new();
    private static int nextIntentRevision;
    private static int currentSequence;
    private static string currentRequester;

    internal static void Reset() { Verdicts.Clear(); PlayerPushes.Clear(); AutonomousIntents.Clear(); IntentRevisions.Clear(); nextIntentRevision = 0; currentRequester = null; }

    internal static void PublishAutonomousIntent(BotOwner knight, string target, bool advance, bool searching = false)
    {
        if (string.IsNullOrEmpty(target)) { ClearAutonomousIntent(knight); return; }
        if (!AutonomousIntents.TryGetValue(knight, out var previous) || previous != (target, advance, searching))
            IntentRevisions[knight] = (++nextIntentRevision, Time.time);
        AutonomousIntents[knight] = (target, advance, searching);
    }
    internal static void ClearAutonomousIntent(BotOwner owner) { AutonomousIntents.Remove(owner); IntentRevisions.Remove(owner); }

    internal static BotOwner FindCommandLeader(BotOwner member) => member?.Profile?.Info?.Settings?.Role == WildSpawnType.exUsec
        ? ExUsecCombatExtension.FindLeader(member) : FindKnight(member);

    internal static bool TryGetAdvanceOrder(BotOwner leader, out EnemyInfo target, out int revision, out float publishedAt)
    {
        target = null; revision = 0; publishedAt = 0f;
        if (leader == null || leader.IsDead || leader.BotState != EBotState.Active ||
            leader.GetPlayer?.HealthController?.IsAlive != true || leader.Medecine.Using || leader.Memory.IsUnderFire ||
            FollowerCombatCommon.WasHitRecently(leader, 1f) ||
            !AutonomousIntents.TryGetValue(leader, out var intent) || !intent.Advance ||
            !IntentRevisions.TryGetValue(leader, out var stamp) || leader.Memory.GoalEnemy?.ProfileId != intent.Target) return false;
        target = KnownTarget(leader, intent.Target);
        revision = stamp.Revision; publishedAt = stamp.At;
        return target != null;
    }

    internal static bool HasAutonomousAdvanceIntent(BotOwner actor, string target)
        => actor != null && AutonomousIntents.TryGetValue(actor, out var intent) &&
           intent.Target == target && intent.Advance;

    internal static bool TryGetSearchLeader(BotOwner member, EnemyInfo target, out BotOwner knight)
    {
        return TryGetAdvanceLeader(member, target, out knight) &&
            AutonomousIntents.TryGetValue(knight, out var intent) && intent.Searching;
    }

    internal static bool TryGetAdvanceLeader(BotOwner member, EnemyInfo target, out BotOwner knight)
    {
        knight = FindCommandLeader(member);
        // Following an allied advance uses admitted shared memory, not personal sight.
        return knight != null && target != null && KnownTarget(member, target.ProfileId) != null &&
            KnownTarget(knight, target.ProfileId) != null && knight.Memory.GoalEnemy?.ProfileId == target.ProfileId &&
            !knight.Memory.IsUnderFire && !FollowerCombatCommon.WasHitRecently(knight, 1f) && !knight.Medecine.Using &&
            AutonomousIntents.TryGetValue(knight, out var intent) && intent.Target == target.ProfileId &&
            intent.Advance;
    }

    internal static bool CanAutonomousAdvance(BotOwner member, EnemyInfo target)
    {
        if (target?.Person?.HealthController?.IsAlive != true) return false;
        var leader = BossPlayers.Instance?.GetFollower(member)?.GetBoss();
        if (leader?.realPlayer?.IsYourPlayer != true) return false;
        var knight = FindCommandLeader(member);
        if (Delegated(member, leader.realPlayer.Position, target.ProfileId, knight, out var known)) return true;
        return known != null && knight.Memory.GoalEnemy?.ProfileId == target.ProfileId &&
            !knight.Memory.IsUnderFire && !FollowerCombatCommon.WasHitRecently(knight, 1f) && !knight.Medecine.Using &&
            AutonomousIntents.TryGetValue(knight, out var intent) && intent.Target == target.ProfileId && intent.Advance;
    }

    private static bool Delegated(BotOwner actor, Vector3 requesterPosition, string target, BotOwner knight, out EnemyInfo known)
    {
        known = knight == null ? null : KnownTarget(knight, target);
        if (knight == null) return true;
        if (actor.Profile.Info.Settings.Role is not (WildSpawnType.followerBigPipe or WildSpawnType.exUsec)) return false;
        bool nearby = FollowerCombatCommon.IsFinite(actor.Position) && FollowerCombatCommon.IsFinite(requesterPosition) &&
            (actor.Position - requesterPosition).sqrMagnitude <= 30f * 30f;
        var knightEnemy = knight.Memory.GoalEnemy;
        bool differentFight = knightEnemy?.Person?.HealthController?.IsAlive == true && knightEnemy.ProfileId != target &&
            (knightEnemy.IsVisible || knight.Memory.IsUnderFire ||
             knightEnemy.PersonalLastSeenTime > 0f && Time.time - knightEnemy.PersonalLastSeenTime <= 2.5f);
        return nearby && ((knight.Position - requesterPosition).sqrMagnitude > 50f * 50f || differentFight || known == null);
    }

    internal static BotOwner FindKnight(BotOwner member)
    {
        var leader = BossPlayers.Instance?.GetFollower(member)?.GetBoss();
        if (leader?.realPlayer?.IsYourPlayer != true) return null;
        return BossPlayers.GetFollowers().Where(f => ReferenceEquals(f.GetBoss(), leader)).Select(f => f.GetBot())
            .FirstOrDefault(bot => bot != null && !bot.IsDead && bot.BotState == EBotState.Active &&
                bot.GetPlayer?.HealthController?.IsAlive == true && bot.Profile?.Info?.Settings?.Role == WildSpawnType.bossKnight);
    }

    internal static bool? Evaluate(CompanionAdvanceOrderContext context)
    {
        var actor = context.Follower;
        var role = actor.Profile?.Info?.Settings?.Role;
        if (role != WildSpawnType.bossKnight && role != WildSpawnType.followerBigPipe && role != WildSpawnType.exUsec) return null;
        var leader = BossPlayers.Instance?.GetFollower(actor)?.GetBoss();
        if (leader?.realPlayer?.IsYourPlayer != true || leader.realPlayer.ProfileId != context.Requester?.ProfileId) return false;
        if (currentSequence != context.Sequence || currentRequester != context.Requester.ProfileId)
        {
            Verdicts.Clear(); currentSequence = context.Sequence; currentRequester = context.Requester.ProfileId;
        }
        var knight = role == WildSpawnType.bossKnight ? actor : FindCommandLeader(actor);
        if (role == WildSpawnType.exUsec && knight == null) return null;
        bool delegated = Delegated(actor, context.Requester.Position, context.Target.ProfileId, knight, out var known);
        bool accepted;
        if (delegated) accepted = true;
        else
        {
            string key = knight.ProfileId + ":" + context.Target.ProfileId;
            if (!Verdicts.TryGetValue(key, out accepted))
                Verdicts[key] = accepted = known != null && context.EvaluateNormal(knight, known);
        }
        if (accepted && (role == WildSpawnType.followerBigPipe || role == WildSpawnType.exUsec))
        {
            var follower = BossPlayers.Instance.GetFollower(actor);
            PlayerPushes[actor] = new PlayerPush
            {
                Sequence = unchecked(follower.PushEnemyIssueSequence + 1),
                Directive = knight != null && (role == WildSpawnType.exUsec || knight.Profile?.Info?.Settings?.Role == WildSpawnType.followerBigPipe) && IntentRevisions.TryGetValue(knight, out var stamp)
                    ? stamp.Revision : KnightSquadCoordinator.DirectiveRevision(actor),
                KnightTarget = knight?.Memory.GoalEnemy?.ProfileId,
                IssuedAt = Time.time,
                Leader = knight
            };
        }
        return accepted;
    }

    internal static EnemyInfo KnownTarget(BotOwner owner, string profileId)
    {
        EnemyInfo target = owner.Memory.GoalEnemy?.ProfileId == profileId ? owner.Memory.GoalEnemy :
            owner.EnemiesController?.EnemyInfos?.Values.FirstOrDefault(info => info?.ProfileId == profileId);
        return target?.Person?.HealthController?.IsAlive == true && FollowerEnemyTracking.IsEligible(target) &&
            FollowerEnemyTracking.TryGetKnownPosition(target, out _, out _) ? target : null;
    }

    internal static bool CanSupersede(BotOwner member, string target, int directive, float publishedAt)
    {
        var follower = BossPlayers.Instance?.GetFollower(member);
        if (follower == null || follower.HasOrderedPushCancelRequest ||
            follower.CombatRegroupUsesBossAnchor ||
            !PlayerPushes.TryGetValue(member, out var push) || follower.PushEnemyIssueSequence != push.Sequence ||
            follower.GetBoss()?.GetBossLogic()?.IsHitted == true && !FollowerCombatAnchor.IsCombatIndependent(member)) return false;
        if (follower.TryPeekActiveCommand(out var command, out _, out _) && command != FollowerCommandType.PushEnemy) return false;
        if (member.Profile?.Info?.Settings?.Role == WildSpawnType.exUsec && FindCommandLeader(member) != push.Leader) return false;
        return directive > push.Directive || publishedAt >= push.IssuedAt && target != push.KnightTarget;
    }

    internal static void Complete(BotOwner member) => PlayerPushes.Remove(member);
}
