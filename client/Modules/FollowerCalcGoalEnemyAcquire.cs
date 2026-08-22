using Comfort.Common;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    public static class FollowerCalcGoalEnemyAcquire
    {
        private const float CalcGoalGuardBaseSeconds = 0.2f;
        private const float CalcGoalGuardPerFollowerSeconds = 0.1f;
        private const float CalcGoalGuardMaxSeconds = 2f;
        private const float CalcGoalForwardScanDistance = 80f;
        private const float CalcGoalForwardScanMinDot = 0.45f;
        private const int CalcGoalMaxVisionChecksPerScan = 6;
        private const float SameSideHostileIntentDebounceSeconds = 1f;

        private static readonly List<Player> CalcGoalForwardCandidates = new List<Player>(16);
        private static readonly Dictionary<string, float> NextCalcGoalScanAtByBot = new Dictionary<string, float>(64);
        private static readonly Dictionary<string, float> SameSideHostileIntentSinceByBossCandidate =
            new Dictionary<string, float>(64);
        private static readonly Dictionary<string, HashSet<string>> ProcessedFriendlySeenByBoss =
            new Dictionary<string, HashSet<string>>(8);

        public static void HandleCalcGoal(BotOwner owner)
        {
            if (owner == null || !BossPlayers.IsFollower(owner)) return;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(owner)) return;
            if (!CanRunCalcGoalScanNow(owner)) return;
            if (owner.Memory?.HaveEnemy == true) return;

            List<Player> candidates = CollectAlivePlayersInLookDirection(
                owner,
                CalcGoalForwardCandidates,
                CalcGoalForwardScanDistance,
                CalcGoalForwardScanMinDot);

            if (candidates.Count == 0)
            {
                return;
            }

            Player botPlayer = owner.GetPlayer;
            Player firstVisible = null;
            int visionChecks = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Player candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (IsProcessedFriendlySeen(owner, candidate.ProfileId))
                {
                    bool currentlyHostileToBossSide =
                        CandidateHasBossOrFollowerAsEnemy(owner, candidate) ||
                        CandidateHasGoalEnemyBossOrFollower(owner, candidate);
                    if (HasDebouncedSameSideHostileIntent(owner, candidate.ProfileId, currentlyHostileToBossSide))
                    {
                        RemoveProcessedFriendlySeen(owner, candidate.ProfileId);
                    }
                    else
                    {
                        continue;
                    }
                }

                visionChecks++;
                if (HasSimpleVision(botPlayer, candidate))
                {
                    if (ShouldSkipSameSideCandidate(owner, candidate))
                    {
                        AddProcessedFriendlySeen(owner, candidate.ProfileId);
                        continue;
                    }

                    firstVisible = candidate;
                    break;
                }

                if (visionChecks >= CalcGoalMaxVisionChecksPerScan)
                {
                    break;
                }
            }

            if (firstVisible == null)
            {
                return;
            }

            EnemyInfo? info = Enemy.MakeEnemy(
                owner,
                firstVisible,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (info == null)
            {
                return;
            }

            bool ownerHasDirectContact = FollowerEnemyInfoCorrection.RefreshDirectContactForAcquisition(owner, info);
            Enemy.RepairPersonalMemory(info, firstVisible.Position, ownerHasDirectContact);
            if (!ownerHasDirectContact)
            {
                return;
            }

            PromoteGoalEnemy(owner, info, hasDirectVisibility: true);

            owner.BotFollower.BossToFollow?.Followers?.ForEach(follower =>
            {
                if (follower == null || follower.IsDead || follower.GetPlayer == null || follower == owner)
                {
                    return;
                }
                if (FollowerEnemyEnforceSuppression.IsSuppressed(follower))
                {
                    return;
                }

                if (follower.ProfileId == owner.ProfileId)
                {
                    return;
                }

                EnemyInfo? followerInfo = Enemy.MakeEnemy(
                    follower,
                    firstVisible,
                    EBotEnemyCause.checkAddTODO,
                    countSharedSeenAsPersonal: false);
                bool siblingHasDirectContact = FollowerEnemyInfoCorrection.RefreshDirectContactForAcquisition(follower, followerInfo);
                Enemy.RepairPersonalMemory(followerInfo, firstVisible.Position, siblingHasDirectContact);
                if (!siblingHasDirectContact)
                {
                    return;
                }

                PromoteGoalEnemy(follower, followerInfo, hasDirectVisibility: true);
            });
        }

        private static void PromoteGoalEnemy(BotOwner follower, EnemyInfo? info, bool hasDirectVisibility)
        {
            if (follower?.Memory == null || info == null)
            {
                return;
            }

            bool willPromote = follower.Memory.GoalEnemy == null ||
                               !follower.Memory.HaveEnemy ||
                               follower.Memory.GoalEnemy.Person?.HealthController?.IsAlive != true;
            if (willPromote)
            {
                follower.Memory.IsPeace = false;
                info.IgnoreUntilAggression = false;
                using (FollowerGoalEnemyTracker.Begin("FollowerCalcGoalEnemyAcquire.PromoteGoalEnemy", "idleOrSiblingAcquire"))
                {
                    follower.Memory.GoalEnemy = info;
                }
                FollowerContactEnemyRetention.RegisterCurrentGoal(follower, prioritized: false);
            }
        }

        public static bool CandidateHasBossOrFollowerAsEnemy(BotOwner owner, Player candidate)
        {
            if (!candidate.IsAI)
            {
                return false;
            }

            BotOwner candidateBot = candidate.AIData?.BotOwner;
            if (candidateBot?.BotsGroup == null)
            {
                return false;
            }

            if (owner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            if (candidateBot.BotsGroup.IsEnemy(boss.realPlayer))
            {
                return true;
            }

            List<BotOwner> followers = boss.Followers;
            if (followers == null || followers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower.IsDead || follower.GetPlayer == null)
                {
                    continue;
                }

                if (candidateBot.BotsGroup.IsEnemy(follower.GetPlayer))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CandidateHasGoalEnemyBossOrFollower(BotOwner owner, Player candidate)
        {
            if (!candidate.IsAI)
            {
                return false;
            }

            BotOwner candidateBot = candidate.AIData?.BotOwner;
            EnemyInfo goalEnemy = candidateBot?.Memory?.GoalEnemy;
            if (goalEnemy?.ProfileId == null)
            {
                return false;
            }

            if (owner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            string goalEnemyId = goalEnemy.ProfileId;
            if (string.Equals(goalEnemyId, boss.realPlayer.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            List<BotOwner> followers = boss.Followers;
            if (followers == null || followers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
                {
                    continue;
                }

                if (string.Equals(goalEnemyId, follower.ProfileId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasDebouncedSameSideHostileIntent(BotOwner owner, string candidateProfileId, bool currentlyHostile)
        {
            if (string.IsNullOrEmpty(candidateProfileId))
            {
                return false;
            }

            string bossId = GetBossProfileId(owner);
            if (string.IsNullOrEmpty(bossId))
            {
                return false;
            }

            string key = bossId + "|" + candidateProfileId;
            if (!currentlyHostile)
            {
                SameSideHostileIntentSinceByBossCandidate.Remove(key);
                return false;
            }

            float now = Time.time;
            if (!SameSideHostileIntentSinceByBossCandidate.TryGetValue(key, out float since))
            {
                SameSideHostileIntentSinceByBossCandidate[key] = now;
                return false;
            }

            return now - since >= SameSideHostileIntentDebounceSeconds;
        }

        public static List<Player> CollectAlivePlayersInLookDirection(
            BotOwner owner,
            List<Player> results,
            float maxDistance,
            float minDot = 0.45f)
        {
            if (results == null)
            {
                return new List<Player>(0);
            }

            results.Clear();

            if (owner == null || owner.IsDead || owner.GetPlayer == null)
            {
                return results;
            }

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                return results;
            }

            List<Player> alivePlayers = gameWorld.AllAlivePlayersList;
            if (alivePlayers == null || alivePlayers.Count == 0)
            {
                return results;
            }

            Player botPlayer = owner.GetPlayer;
            Vector3 origin = owner.Position;
            if (botPlayer.MainParts != null && botPlayer.MainParts.TryGetValue(BodyPartType.head, out var headPart) && headPart != null)
            {
                origin = headPart.Position;
            }

            Vector3 lookDir = owner.LookDirection;
            if (lookDir.sqrMagnitude < 0.0001f)
            {
                lookDir = botPlayer.MovementContext?.LookDirection ?? botPlayer.Transform.forward;
            }
            lookDir.Normalize();

            float maxDistanceSqr = maxDistance * maxDistance;
            string ownerId = owner.ProfileId;
            string mainPlayerId = gameWorld.MainPlayer?.ProfileId;

            for (int i = 0; i < alivePlayers.Count; i++)
            {
                Player candidate = alivePlayers[i];
                if (candidate == null || candidate.HealthController?.IsAlive != true)
                {
                    continue;
                }

                string candidateId = candidate.ProfileId;
                if (string.IsNullOrEmpty(candidateId) || string.Equals(candidateId, ownerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(mainPlayerId) && string.Equals(candidateId, mainPlayerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (BossPlayers.IsPlayerBoss(candidateId))
                {
                    continue;
                }

                if (candidate.IsAI)
                {
                    BotOwner candidateBot = candidate.AIData?.BotOwner;
                    if (candidateBot == null)
                    {
                        continue;
                    }

                    if (BossPlayers.IsFollower(candidateBot))
                    {
                        continue;
                    }

                    if (IsFriendlyBotType(candidateBot))
                    {
                        continue;
                    }

                    if (owner.BotFollower?.BossToFollow?.Followers?.Contains(candidateBot) == true)
                    {
                        continue;
                    }

                    if (owner.BotsGroup?.Contains(candidateBot) == true)
                    {
                        continue;
                    }
                }

                Vector3 toCandidate = candidate.Transform.position - origin;
                float sqrDistance = toCandidate.sqrMagnitude;
                if (sqrDistance < 0.01f || sqrDistance > maxDistanceSqr)
                {
                    continue;
                }

                float invDistance = 1f / Mathf.Sqrt(sqrDistance);
                float dot = Vector3.Dot(lookDir, toCandidate * invDistance);
                if (dot < minDot)
                {
                    continue;
                }

                results.Add(candidate);
            }

            return results;
        }

        private static bool CanRunCalcGoalScanNow(BotOwner owner)
        {
            string botId = owner.ProfileId;
            if (string.IsNullOrEmpty(botId))
            {
                return false;
            }

            float now = Time.time;
            if (NextCalcGoalScanAtByBot.TryGetValue(botId, out float nextAt) && now < nextAt)
            {
                return false;
            }

            int followerCount = GetBossFollowerCount(owner);
            float interval = CalcGoalGuardBaseSeconds + (CalcGoalGuardPerFollowerSeconds * followerCount);
            interval = Mathf.Min(interval, CalcGoalGuardMaxSeconds);
            NextCalcGoalScanAtByBot[botId] = now + interval;
            return true;
        }

        private static int GetBossFollowerCount(BotOwner owner)
        {
            List<BotOwner> followers = owner.BotFollower?.BossToFollow?.Followers;
            if (followers == null || followers.Count == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower.IsDead)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool ShouldSkipSameSideCandidate(BotOwner owner, Player candidate)
        {
            if (owner == null || candidate == null)
            {
                return false;
            }

            if (owner.Side != candidate.Side)
            {
                return false;
            }

            bool hasBossOrFollowerAsEnemy =
                CandidateHasBossOrFollowerAsEnemy(owner, candidate) ||
                CandidateHasGoalEnemyBossOrFollower(owner, candidate);
            hasBossOrFollowerAsEnemy = HasDebouncedSameSideHostileIntent(owner, candidate.ProfileId, hasBossOrFollowerAsEnemy);

            bool isPmcSide = owner.Side == EPlayerSide.Bear || owner.Side == EPlayerSide.Usec;
            if (!isPmcSide)
            {
                return !hasBossOrFollowerAsEnemy;
            }

            if (Utils.Utils.FlagGet("isBadGuy"))
            {
                return false;
            }

            return !hasBossOrFollowerAsEnemy;
        }

        private static bool IsProcessedFriendlySeen(BotOwner owner, string candidateProfileId)
        {
            if (string.IsNullOrEmpty(candidateProfileId))
            {
                return false;
            }

            string bossId = GetBossProfileId(owner);
            if (string.IsNullOrEmpty(bossId))
            {
                return false;
            }

            return ProcessedFriendlySeenByBoss.TryGetValue(bossId, out HashSet<string> set) && set.Contains(candidateProfileId);
        }

        private static void AddProcessedFriendlySeen(BotOwner owner, string candidateProfileId)
        {
            if (string.IsNullOrEmpty(candidateProfileId))
            {
                return;
            }

            string bossId = GetBossProfileId(owner);
            if (string.IsNullOrEmpty(bossId))
            {
                return;
            }

            if (!ProcessedFriendlySeenByBoss.TryGetValue(bossId, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                ProcessedFriendlySeenByBoss[bossId] = set;
            }

            set.Add(candidateProfileId);
        }

        private static void RemoveProcessedFriendlySeen(BotOwner owner, string candidateProfileId)
        {
            if (string.IsNullOrEmpty(candidateProfileId))
            {
                return;
            }

            string bossId = GetBossProfileId(owner);
            if (string.IsNullOrEmpty(bossId))
            {
                return;
            }

            if (!ProcessedFriendlySeenByBoss.TryGetValue(bossId, out HashSet<string> set))
            {
                return;
            }

            set.Remove(candidateProfileId);
            if (set.Count == 0)
            {
                ProcessedFriendlySeenByBoss.Remove(bossId);
            }
        }

        private static string GetBossProfileId(BotOwner owner)
        {
            if (owner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return null;
            }

            return boss.realPlayer.ProfileId;
        }

        private static bool IsFriendlyBotType(BotOwner candidateBot)
        {
            WildSpawnType? role = candidateBot?.Profile?.Info?.Settings?.Role;
            return role.HasValue && Props.friendlyBotTypes.Contains(role.Value);
        }

        private static bool HasSimpleVision(Player from, Player to)
        {
            if (from?.MainParts == null || to?.MainParts == null)
            {
                return false;
            }

            if (!from.MainParts.TryGetValue(BodyPartType.head, out var fromHead) || fromHead == null)
            {
                return false;
            }

            if (!to.MainParts.TryGetValue(BodyPartType.head, out var toHead) || toHead == null)
            {
                return false;
            }

            Vector3 direction = toHead.Position - fromHead.Position;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                return false;
            }

            return !Physics.Raycast(
                new Ray(fromHead.Position, direction),
                out RaycastHit _,
                distance,
                LayersMaskController.HighPolyWithTerrainMask);
        }
    }
}
