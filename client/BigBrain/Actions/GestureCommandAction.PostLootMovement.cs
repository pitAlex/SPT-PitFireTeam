using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private const float PostLootMoveMinRadius = 2.5f;
        private const float PostLootMoveMaxRadius = 3f;
        private const float PostLootSameLevelTolerance = 0.75f;
        private const float PostLootDestinationSpacing = 1.5f;
        private const float PostLootDestinationClaimSeconds = 3f;
        private const int PostLootTargetAttempts = 3;

        private bool TryBeginPostLootMove(FollowerCommandType completedLootCommand)
        {
            if (BotOwner == null ||
                followerData == null ||
                !TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                return false;
            }

            Vector3 origin = BotOwner.Position;
            float minimumMoveDistanceSqr = PostLootMoveMinRadius * PostLootMoveMinRadius;
            float maximumMoveDistanceSqr = PostLootMoveMaxRadius * PostLootMoveMaxRadius;
            for (int attempt = 0; attempt < PostLootTargetAttempts; attempt++)
            {
                // The spread helper is centered on the supplied position. Using the completed
                // loot position keeps this move local while reusing its same-level, spacing,
                // live-follower, destination-claim, and complete-path validation.
                if (!combatEvents.TryFindBossSpreadDestination(
                        BotOwner,
                        origin,
                        PostLootMoveMinRadius,
                        PostLootMoveMaxRadius,
                        PostLootSameLevelTolerance,
                        PostLootDestinationSpacing,
                        out Vector3 target))
                {
                    continue;
                }

                Vector3 planarMove = target - origin;
                planarMove.y = 0f;
                if (planarMove.sqrMagnitude < minimumMoveDistanceSqr ||
                    planarMove.sqrMagnitude > maximumMoveDistanceSqr)
                {
                    continue;
                }

                if (BotOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss &&
                    boss.realPlayer != null)
                {
                    Vector3 playerSpacing = target - boss.realPlayer.Position;
                    playerSpacing.y = 0f;
                    if (playerSpacing.sqrMagnitude <
                        PostLootDestinationSpacing * PostLootDestinationSpacing)
                    {
                        continue;
                    }
                }

                if (!followerData.TrySetPostLootMoveToPoint(completedLootCommand, target))
                {
                    return false;
                }

                combatEvents.UpsertDestinationClaim(BotOwner, target, PostLootDestinationClaimSeconds);
                return true;
            }

            return false;
        }
    }
}
