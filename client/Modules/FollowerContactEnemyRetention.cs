using Comfort.Common;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    public static class FollowerContactEnemyRetention
    {
        private const float MinimumRetainSeconds = 1f;

        private sealed class RetainedContact
        {
            public string EnemyProfileId = string.Empty;
            public bool WasVisible;
            public bool Prioritized;
            public float Until;
            public float LastContactAt;
        }

        private static readonly Dictionary<string, RetainedContact> RetainedByFollowerId = new(StringComparer.Ordinal);
        private static readonly HashSet<string> AllowNextGoalClearByFollowerId = new(StringComparer.Ordinal);

        public static void Register(BotOwner follower, Player enemy, bool visible, bool prioritized)
        {
            if (follower == null || enemy == null || string.IsNullOrEmpty(follower.ProfileId) || string.IsNullOrEmpty(enemy.ProfileId))
            {
                return;
            }

            float now = Time.time;
            float retainSeconds = GetRetainSeconds();

            if (RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact existing) &&
                now <= existing.Until)
            {
                if (!prioritized && existing.Prioritized && existing.EnemyProfileId != enemy.ProfileId)
                {
                    return;
                }

                if (existing.EnemyProfileId == enemy.ProfileId)
                {
                    existing.WasVisible |= visible;
                    existing.Prioritized |= prioritized;
                    if (visible)
                    {
                        existing.LastContactAt = now;
                        existing.Until = now + retainSeconds;
                    }
                    else
                    {
                        // Do not let invisible bookkeeping refreshes keep a dead contact alive forever.
                        existing.Until = Mathf.Min(now + retainSeconds, existing.LastContactAt + retainSeconds);
                    }

                    return;
                }
            }

            RetainedByFollowerId[follower.ProfileId] = new RetainedContact
            {
                EnemyProfileId = enemy.ProfileId,
                WasVisible = visible,
                Prioritized = prioritized,
                Until = now + retainSeconds,
                LastContactAt = now,
            };
        }

        public static bool RegisterCurrentGoal(BotOwner follower, bool prioritized)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (follower == null || goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return false;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                return false;
            }

            bool hasFreshContact = goalEnemy.IsVisible || goalEnemy.CanShoot;
            float retainSeconds = GetRetainSeconds();
            if (!hasFreshContact &&
                Time.time - goalEnemy.PersonalLastSeenTime > retainSeconds &&
                Time.time - goalEnemy.PersonalSeenTime > retainSeconds)
            {
                return false;
            }

            Register(follower, enemy, hasFreshContact, prioritized);
            return true;
        }

        public static bool HasActiveRetainedContact(BotOwner follower)
        {
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time <= contact.Until)
            {
                if (TryAdoptDifferentLiveVisibleGoal(follower, contact.EnemyProfileId, out _, out _))
                {
                    return true;
                }

                Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
                if (enemy?.HealthController?.IsAlive == true)
                {
                    return true;
                }
            }

            RetainedByFollowerId.Remove(follower.ProfileId);
            return false;
        }

        public static bool TryGetActiveRetainedEnemy(BotOwner follower, out Player? enemy, out bool prioritized)
        {
            enemy = null;
            prioritized = false;
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (TryAdoptDifferentLiveVisibleGoal(follower, contact.EnemyProfileId, out _, out Player? adoptedEnemy))
            {
                enemy = adoptedEnemy;
                prioritized = true;
                return enemy != null;
            }

            enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                enemy = null;
                return false;
            }

            prioritized = contact.Prioritized;
            return true;
        }

        public static bool ShouldBlockGoalEnemyClear(BotOwner follower, EnemyInfo? currentGoal)
        {
            if (follower == null || currentGoal == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (AllowNextGoalClearByFollowerId.Remove(follower.ProfileId))
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until || currentGoal.ProfileId != contact.EnemyProfileId)
            {
                if (TryAdoptDifferentLiveVisibleGoal(follower, contact.EnemyProfileId, out _, out _))
                {
                    return true;
                }

                return false;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            return true;
        }

        public static bool TryRestore(BotOwner follower, out EnemyInfo? restored)
        {
            restored = null;
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            EnemyInfo? currentGoal = follower.Memory?.GoalEnemy;
            if (FollowerCombatTargetCommitments.IsActiveTemporaryTarget(follower, currentGoal))
            {
                // A mission may temporarily yield to a close self-defence target. Retention is
                // allowed to keep combat alive, but it must not replace that target during the
                // short visibility-loss grace owned by FollowerCombatTargetCommitments.
                restored = currentGoal;
                return true;
            }

            if (TryAdoptDifferentLiveVisibleGoal(follower, contact.EnemyProfileId, out EnemyInfo? adoptedInfo, out _))
            {
                restored = adoptedInfo;
                return restored != null;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (follower.Memory?.GoalEnemy?.ProfileId == contact.EnemyProfileId)
            {
                restored = follower.Memory.GoalEnemy;
                return true;
            }

            EnemyInfo? info = Enemy.MakeEnemy(
                follower,
                enemy,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (info == null)
            {
                return false;
            }

            info.PriorityIndex = 0;
            info.PersonalLastPos = enemy.Position;
            info.SetVisible(contact.WasVisible);
            Enemy.RepairPersonalMemory(info, enemy.Position, contact.WasVisible || Enemy.HasDirectPersonalContact(info));
            follower.Memory.IsPeace = false;
            using (FollowerGoalEnemyTracker.Begin("FollowerContactEnemyRetention.TryRestore", "retainedContactRestore"))
            {
                follower.Memory.GoalEnemy = info;
            }
            restored = info;

            return true;
        }

        private static bool TryAdoptDifferentLiveVisibleGoal(
            BotOwner follower,
            string retainedEnemyProfileId,
            out EnemyInfo? adoptedInfo,
            out Player? adoptedEnemy)
        {
            adoptedInfo = null;
            adoptedEnemy = null;

            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                string.Equals(goalEnemy.ProfileId, retainedEnemyProfileId, StringComparison.Ordinal) ||
                goalEnemy.Person?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!goalEnemy.IsVisible && !goalEnemy.CanShoot)
            {
                return false;
            }

            if (!ShouldAdoptDifferentLiveVisibleGoal(follower, retainedEnemyProfileId, goalEnemy, out string rejectReason))
            {
                return false;
            }

            Player? enemy = goalEnemy.Person as Player;
            if (enemy?.HealthController?.IsAlive != true && !string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
            }

            if (enemy?.HealthController?.IsAlive != true)
            {
                return false;
            }

            bool preserveMissionRetention = FollowerCombatTargetCommitments.IsActiveTemporaryTarget(follower, goalEnemy);
            if (!preserveMissionRetention)
            {
                Register(follower, enemy, visible: true, prioritized: true);
            }

            adoptedInfo = goalEnemy;
            adoptedEnemy = enemy;

            return true;
        }

        public static bool ShouldAllowGoalEnemySet(
            BotOwner follower,
            EnemyInfo? currentGoal,
            EnemyInfo candidate,
            string reason,
            out string? blockedReason)
        {
            blockedReason = null;
            if (follower == null ||
                candidate == null ||
                string.IsNullOrEmpty(follower.ProfileId) ||
                string.IsNullOrEmpty(candidate.ProfileId) ||
                !string.Equals(reason, "unscopedSetter", StringComparison.Ordinal) ||
                FollowerCombatTargetCommitments.HasMission(follower))
            {
                return true;
            }

            if (string.Equals(currentGoal?.ProfileId, candidate.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return true;
            }

            if (Time.time > contact.Until)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return true;
            }

            if (string.Equals(candidate.ProfileId, contact.EnemyProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            Player retainedEnemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (retainedEnemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return true;
            }

            string rejectReason = string.Empty;
            if ((candidate.IsVisible || candidate.CanShoot) &&
                ShouldAdoptDifferentLiveVisibleGoal(follower, contact.EnemyProfileId, candidate, out rejectReason))
            {
                Player? candidatePlayer = candidate.Person as Player;
                if (candidatePlayer?.HealthController?.IsAlive != true)
                {
                    candidatePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(candidate.ProfileId);
                }

                if (candidatePlayer?.HealthController?.IsAlive == true)
                {
                    Register(follower, candidatePlayer, visible: true, prioritized: true);
                    return true;
                }

                rejectReason = "candidateDead";
            }
            else if (!candidate.IsVisible && !candidate.CanShoot)
            {
                rejectReason = "notVisibleOrShootable";
            }

            blockedReason = "retentionBlockedSet:" + (string.IsNullOrEmpty(rejectReason) ? "notEngageable" : rejectReason);
            return false;
        }

        private static bool ShouldAdoptDifferentLiveVisibleGoal(
            BotOwner follower,
            string retainedEnemyProfileId,
            EnemyInfo candidate,
            out string rejectReason)
        {
            rejectReason = string.Empty;
            if (follower == null || candidate == null)
            {
                rejectReason = "missingFollowerOrCandidate";
                return false;
            }

            if (FollowerCombatTargetCommitments.HasMission(follower))
            {
                if (FollowerCombatTargetCommitments.IsMissionTarget(follower, candidate) ||
                    FollowerCombatTargetCommitments.IsActiveTemporaryTarget(follower, candidate))
                {
                    return true;
                }

                rejectReason = "targetMission";
                return false;
            }

            if (!TryGetOrderedPushTargetLock(follower, out string orderedTargetProfileId))
            {
                return true;
            }

            if (string.Equals(candidate.ProfileId, orderedTargetProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(retainedEnemyProfileId, orderedTargetProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            if (IsStrongOrderedPushSelfDefenseThreat(follower, candidate))
            {
                return true;
            }

            rejectReason = "orderedTargetLock";
            return false;
        }

        private static bool TryGetOrderedPushTargetLock(BotOwner follower, out string orderedTargetProfileId)
        {
            orderedTargetProfileId = string.Empty;
            var followerData = BossPlayers.Instance?.GetFollower(follower);
            if (followerData == null ||
                !followerData.TryGetOrderedPushTargetLock(out orderedTargetProfileId, out _))
            {
                return false;
            }

            return !string.IsNullOrEmpty(orderedTargetProfileId);
        }

        private static bool IsStrongOrderedPushSelfDefenseThreat(BotOwner follower, EnemyInfo candidate)
        {
            if (candidate == null || !candidate.IsVisible || !candidate.CanShoot)
            {
                return false;
            }

            if (FollowerImmediateFirePolicy.HasReliableImmediateFireLane(follower, candidate))
            {
                return true;
            }

            float hardSwitchDistance = Mathf.Min(
                12f,
                CombatDistanceConfiguration.Instance.GetCloseQuarterDistance());
            return candidate.Distance <= hardSwitchDistance;
        }

        public static void Clear(BotOwner follower)
        {
            if (string.IsNullOrEmpty(follower?.ProfileId))
            {
                return;
            }

            RetainedByFollowerId.Remove(follower.ProfileId);
        }

        public static void ClearAndAllowNextGoalClear(BotOwner follower)
        {
            if (string.IsNullOrEmpty(follower?.ProfileId))
            {
                return;
            }

            RetainedByFollowerId.Remove(follower.ProfileId);
            AllowNextGoalClearByFollowerId.Add(follower.ProfileId);
        }

        private static float GetRetainSeconds()
        {
            int configuredSeconds = pitFireTeam.enemyRemember?.Value ?? 12;
            return Mathf.Max(MinimumRetainSeconds, configuredSeconds);
        }
    }
}
