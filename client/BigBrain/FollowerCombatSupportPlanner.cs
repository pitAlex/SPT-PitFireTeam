using EFT;
using pitTeam.Modules;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.BigBrain
{
    // Shared Rifleman/optional-brain plan builder. The caller owns its combat router and
    // arrival/hold state. This class never publishes decisions or emits a push event.
    internal sealed class FollowerCombatSupportPlanner
    {
        private const float AllyPushSupportMaxStraightDistance = 45f;
        private const float AllyPushSupportMaxNavDistance = 65f;
        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        internal FollowerCombatSupportPlanner(BotOwner owner, FollowerCombatCommon common)
        {
            botOwner = owner;
            combatCommon = common;
        }
        internal bool TryBuild(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (TryPush(out decision)) return true;
            var engagement = combatCommon.TryGetAllyEngagementSupportDecision();
            if (!engagement.HasValue) return false;
            decision = engagement.Value;
            return true;
        }
        private bool TryPush(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            // Rifleman helper path: consume another follower's active autonomous push event and
            // convert it into support. This branch never emits a new push event and never chooses
            // direct go-to-enemy/run-to-enemy as its support objective.
            if (!combatCommon.TryGetNearbyActivePushEventForCurrentEnemy(
                    AllyPushSupportMaxStraightDistance,
                    AllyPushSupportMaxNavDistance,
                    out CombatEvents.PushEvent pushEvent))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (combatCommon.CanShootFromCurrentCover(out _))
            {
                // Best support is doing nothing fancy: stay in cover and shoot if this position
                // already contributes to the pusher's fight.
                combatCommon.ExtendCommittedCover();
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover,
                    "allyPushSupport.currentCover");
                return true;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediate =
                combatCommon.TryGetImmediateShootDecision("allyPushSupport.immediateShoot");
            if (immediate != null)
            {
                decision = immediate.Value;
                return true;
            }

            if (pushEvent.IsSearchPush && combatCommon.HasCommittedPosition(out decision))
            {
                // The search leader's event remains active while this follower settles. Preserve
                // the arrival hold before any support branch is allowed to generate another point.
                return true;
            }

            if (pushEvent.IsSearchPush &&
                (botOwner.Position - pushEvent.Owner.Position).sqrMagnitude <= 20f * 20f &&
                combatCommon.TryCreateTeamSearchSupportDecision(
                    pushEvent,
                    goalEnemy,
                    "allyPushSupport.teamSearch",
                    out decision))
            {
                // Search pushes use a small flank/backstop point near the pusher, not the enemy.
                return true;
            }

            if (combatCommon.TryCommitPushSupportCover(
                    goalEnemy,
                    pushEvent.Owner.Position,
                    pushEvent.EnemyPosition,
                    pushEvent.Destination,
                    "allyPushSupport.cover",
                    out string coverReason))
            {
                // Normal support push: take a cover that can watch the enemy or pusher destination.
                decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            Vector3 supportAnchor = FollowerCombatCommon.IsFinite(pushEvent.EnemyPosition)
                ? pushEvent.EnemyPosition
                : pushEvent.Destination;
            // Final fallback is still a support firing point, not a direct assault push.
            return combatCommon.TryCreateSupportFiringPositionDecision(
                goalEnemy,
                supportAnchor,
                "allyPushSupport.position",
                out decision,
                preferBackline: false,
                enforceMarksmanPositionPolicy: false);
        }

    }
}
