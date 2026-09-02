using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerEnemyRetentionService
    {
        public static bool ShouldAllowAcquire(BotOwner owner, IPlayer enemy, out string reason)
        {
            if (!SAINAddonToggles.EnableForcedEnemyRetention)
            {
                reason = "retention_disabled";
                return true;
            }

            reason = "allow_non_follower";
            if (owner == null) return false;
            if (!BossPlayers.IsFollower(owner)) return true;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(owner))
            {
                reason = "blocked_attention_suppression";
                return false;
            }

            string enemyId = enemy.ProfileId;

            if (BossPlayers.IsPlayerBoss(enemyId))
            {
                reason = "is_player_boss";
                return false;
            }

            BotOwner allied = owner.BotFollower.BossToFollow?.Followers.Find(f => string.Equals(f.ProfileId, enemyId, StringComparison.Ordinal));
            if (allied != null)
            {
                reason = "is_allied_follower";
                return false;
            }

            return true;
        }

        public static bool ShouldAllowRelationshipAcquire(BotOwner owner, IPlayer enemy, out string reason)
        {
            reason = "allow_relationship";
            if (owner == null || enemy == null)
            {
                return false;
            }

            Player enemyPlayer = enemy as Player;
            if (enemyPlayer == null)
            {
                reason = "blocked_non_player";
                return false;
            }

            if (FollowerCalcGoalEnemyAcquire.ShouldBlockCandidateForMissingHostileIntent(owner, enemyPlayer))
            {
                reason = "blocked_missing_hostile_intent";
                return false;
            }

            reason = "allow_relationship_or_hostile_intent";
            return true;
        }
    }
}
