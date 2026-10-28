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
    }
    private static readonly Dictionary<string, bool> Verdicts = new();
    private static readonly Dictionary<BotOwner, PlayerPush> PlayerPushes = new();
    private static int currentSequence;
    private static string currentRequester;

    internal static void Reset() { Verdicts.Clear(); PlayerPushes.Clear(); currentRequester = null; }

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
        if (role != WildSpawnType.bossKnight && role != WildSpawnType.followerBigPipe) return null;
        var leader = BossPlayers.Instance?.GetFollower(actor)?.GetBoss();
        if (leader?.realPlayer?.IsYourPlayer != true || leader.realPlayer.ProfileId != context.Requester?.ProfileId) return false;
        if (currentSequence != context.Sequence || currentRequester != context.Requester.ProfileId)
        {
            Verdicts.Clear(); currentSequence = context.Sequence; currentRequester = context.Requester.ProfileId;
        }
        var knight = role == WildSpawnType.bossKnight ? actor : FindKnight(actor);
        bool delegated = knight == null;
        EnemyInfo known = null;
        if (knight != null)
        {
            bool nearby = FollowerCombatCommon.IsFinite(actor.Position) && FollowerCombatCommon.IsFinite(context.Requester.Position) &&
                (actor.Position - context.Requester.Position).sqrMagnitude <= 30f * 30f;
            var knightEnemy = knight.Memory.GoalEnemy;
            bool differentFight = knightEnemy?.Person?.HealthController?.IsAlive == true &&
                knightEnemy.ProfileId != context.Target.ProfileId &&
                (knightEnemy.IsVisible || knight.Memory.IsUnderFire ||
                 knightEnemy.PersonalLastSeenTime > 0f && Time.time - knightEnemy.PersonalLastSeenTime <= 2.5f);
            known = KnownTarget(knight, context.Target.ProfileId);
            delegated = role == WildSpawnType.followerBigPipe && nearby &&
                ((knight.Position - context.Requester.Position).sqrMagnitude > 50f * 50f || differentFight || known == null);
        }
        bool accepted;
        if (delegated) accepted = true;
        else
        {
            string key = knight.ProfileId + ":" + context.Target.ProfileId;
            if (!Verdicts.TryGetValue(key, out accepted))
                Verdicts[key] = accepted = known != null && context.EvaluateNormal(knight, known);
        }
        if (accepted && role == WildSpawnType.followerBigPipe)
        {
            var follower = BossPlayers.Instance.GetFollower(actor);
            PlayerPushes[actor] = new PlayerPush
            {
                Sequence = unchecked(follower.PushEnemyIssueSequence + 1),
                Directive = KnightSquadCoordinator.DirectiveRevision(actor),
                KnightTarget = knight?.Memory.GoalEnemy?.ProfileId,
                IssuedAt = Time.time
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
            follower.GetBoss()?.GetBossLogic()?.IsHitted == true) return false;
        if (follower.TryPeekActiveCommand(out var command, out _, out _) && command != FollowerCommandType.PushEnemy) return false;
        return directive > push.Directive || publishedAt >= push.IssuedAt && target != push.KnightTarget;
    }

    internal static void Complete(BotOwner member) => PlayerPushes.Remove(member);
}
