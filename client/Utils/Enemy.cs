using EFT;
using pitTeam.Modules;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Utils
{
    public class Enemy
    {
        private struct CachedEnemyInfo
        {
            public float EnemyCount;
            public Vector3 CachedPosition;
            public float EvaluatedAt;

            public CachedEnemyInfo(float enemyCount, Vector3 cachedPosition, float evaluatedAt)
            {
                EnemyCount = enemyCount;
                CachedPosition = cachedPosition;
                EvaluatedAt = evaluatedAt;
            }
        }

        private struct VisibilityState
        {
            public float VisibleSince;
            public float LastRawVisibleTime;
            public float LastStableVisibleTime;
            public bool StableVisible;
        }

        private const float StableVisiblePromoteSeconds = 0.12f;
        private const float StableVisibleDecaySeconds = 0.22f;
        private const float EnemyLocationCacheSeconds = 0.35f;

        private static ConcurrentDictionary<(Vector3, string), CachedEnemyInfo> enemyLocationCache = new ConcurrentDictionary<(Vector3, string), CachedEnemyInfo>();
        private static ConcurrentDictionary<(string botId, string enemyId), VisibilityState> visibilityCache = new ConcurrentDictionary<(string botId, string enemyId), VisibilityState>();
        private static ConcurrentDictionary<string, byte> trackedEnemies = new ConcurrentDictionary<string, byte>();

        public enum EnemyDistance
        {
            VeryClose = 0,
            Close = 1,
            Mid = 2,
            Distant = 3,
            Far = 4
        }

        public enum ProxyDistance
        {
            VeryClose = 0,
            Close = 1,
            Mid = 2,
            Distant = 3,
            Far = 4
        }
        public static bool IsClose(BotOwner bot)
        {
            if (!bot.Memory.HaveEnemy) return false;

            EnemyInfo goalEnemy = bot.Memory.GoalEnemy;
            return (Distance(goalEnemy) <= EnemyDistance.Close && IsVisible(bot, goalEnemy)) || Distance(goalEnemy) == EnemyDistance.VeryClose;
        }

        public static bool IsVisible(BotOwner bot, EnemyInfo goalEnemy)
        {
            if (bot == null || goalEnemy == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            string enemyId = goalEnemy.ProfileId ?? goalEnemy.Person?.ProfileId ?? string.Empty;
            if (string.IsNullOrEmpty(enemyId))
            {
                return goalEnemy.IsVisible;
            }

            var key = (bot.ProfileId, enemyId);
            VisibilityState state = visibilityCache.GetOrAdd(key, _ => default);
            float now = Time.time;
            bool rawVisible = goalEnemy.IsVisible;
            bool immediateVisible = rawVisible &&
                                    (goalEnemy.CanShoot || Distance(goalEnemy) == EnemyDistance.VeryClose);

            if (rawVisible)
            {
                if (state.VisibleSince <= 0f || now - state.LastRawVisibleTime > StableVisibleDecaySeconds)
                {
                    state.VisibleSince = now;
                }

                state.LastRawVisibleTime = now;

                if (immediateVisible || now - state.VisibleSince >= StableVisiblePromoteSeconds)
                {
                    state.StableVisible = true;
                    state.LastStableVisibleTime = now;
                }

                visibilityCache[key] = state;
                return state.StableVisible;
            }

            if (state.StableVisible && now - state.LastStableVisibleTime <= StableVisibleDecaySeconds)
            {
                visibilityCache[key] = state;
                return true;
            }

            state.StableVisible = false;
            state.VisibleSince = 0f;
            visibilityCache[key] = state;
            return false;
        }

        public static bool HasReliableKnownPosition(BotOwner bot, EnemyInfo goalEnemy)
        {
            return TryGetReliableKnownPosition(bot, goalEnemy, out _);
        }

        public static bool TryGetReliableKnownPosition(BotOwner bot, EnemyInfo goalEnemy, out Vector3 position)
        {
            position = Vector3.zero;
            if (bot == null || goalEnemy == null)
            {
                return false;
            }

            Vector3 currentPosition = goalEnemy.CurrPosition;
            if (IsVisible(bot, goalEnemy) &&
                IsFinitePosition(currentPosition) &&
                (currentPosition - bot.Position).sqrMagnitude > 0.01f)
            {
                position = currentPosition;
                return true;
            }

            Vector3 personalLastPos = goalEnemy.PersonalLastPos;
            if (IsFinitePosition(personalLastPos) &&
                personalLastPos.sqrMagnitude > 0.01f &&
                (personalLastPos - bot.Position).sqrMagnitude > 0.01f)
            {
                position = personalLastPos;
                return true;
            }

            Vector3 sharedLastPos = goalEnemy.EnemyLastPositionReal;
            if (IsFinitePosition(sharedLastPos) &&
                sharedLastPos.sqrMagnitude > 0.01f &&
                (sharedLastPos - bot.Position).sqrMagnitude > 0.01f &&
                (goalEnemy.HaveSeen || goalEnemy.PersonalLastSeenTime > 0f))
            {
                position = sharedLastPos;
                return true;
            }

            return false;
        }

        public static ProxyDistance DistanceProxy(BotOwner bot, Vector3 position)
        {
            if (!bot.Memory.HaveEnemy) return ProxyDistance.Far;
            Vector3 enemyPosition = bot.Memory.GoalEnemy.CurrPosition;

            float distance = Vector3.Distance(position, enemyPosition);

            if (distance < 17f) return ProxyDistance.VeryClose;

            if (distance <= 30f)
            {
                return ProxyDistance.Close;
            }

            if (distance < 55f)
            {
                return ProxyDistance.Mid;
            }

            if (distance < 110f)
            {
                return ProxyDistance.Distant;
            }

            return ProxyDistance.Far;
        }

        public static EnemyDistance Distance(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return EnemyDistance.Far;
            }

            float distance = goalEnemy.Distance;

            if (distance < 18f) return EnemyDistance.VeryClose;

            if (distance < 31f)
            {
                return EnemyDistance.Close;
            }

            if (distance < 55f)
            {
                return EnemyDistance.Mid;
            }

            if (distance < 96f)
            {
                return EnemyDistance.Distant;
            }

            return EnemyDistance.Far;
        }

        public static float GetEnemiesAtLocation(BotOwner bot, EnemyInfo enemy, Vector3 position, float radius = 17f)
        {
            int nearbyGroupMembers = 1;
            try
            {
                if (bot == null || enemy == null || string.IsNullOrEmpty(enemy.ProfileId))
                {
                    return nearbyGroupMembers;
                }

                string enemyId = enemy.ProfileId;
                nearbyGroupMembers = CountNearbyLivingGroupMembers(enemy, position, radius);

                // Register cleanup exactly once for this enemy. The previous bag check never added
                // the id and therefore attached another death callback on every uncached call.
                if (enemy.Person != null && trackedEnemies.TryAdd(enemyId, 0))
                {
                    enemy.Person.OnIPlayerDeadOrUnspawn += (IPlayer pl) =>
                    {
                        ClearEnemyLocations(enemyId);
                        trackedEnemies.TryRemove(enemyId, out _);
                    };
                }

                // Use a 10x10x10 grid for caching
                Vector3 cacheKey = new Vector3(
                    Mathf.Floor(position.x / 10f) * 10f,
                    Mathf.Floor(position.y / 10f) * 10f,
                    Mathf.Floor(position.z / 10f) * 10f
                );
                (Vector3, string) cacheKeyWithId = (cacheKey, enemyId);

                // Check cache first
                if (enemyLocationCache.TryGetValue(cacheKeyWithId, out CachedEnemyInfo cachedInfo))
                {
                    float cacheAge = Time.time - cachedInfo.EvaluatedAt;
                    if (cacheAge >= 0f && cacheAge <= EnemyLocationCacheSeconds)
                    {
                        return Mathf.Max(cachedInfo.EnemyCount, nearbyGroupMembers);
                    }
                }

                // Find enemies in range
                Collider[] hits = new Collider[20]; // Pre-allocated array to prevent memory allocation
                int numHits = Physics.OverlapSphereNonAlloc(position, radius, hits, LayersMaskController.PlayerMask);

                if (numHits == 0)
                {
                    enemyLocationCache[cacheKeyWithId] = new CachedEnemyInfo(0f, cacheKey, Time.time);
                    return nearbyGroupMembers;
                }

                int nr = 0;
                HashSet<string> processedEnemies = new HashSet<string>();

                for (int i = 0; i < numHits; i++)
                {
                    Player pl = bot.ShootData.GetPlayerByCollider(hits[i]);
                    if (pl == null || !pl.HealthController.IsAlive || processedEnemies.Contains(pl.ProfileId))
                        continue;

                    if (bot.EnemiesController.IsEnemy(pl) ||
                        bot.Settings.FileSettings.Mind.ENEMY_BOT_TYPES.Contains(pl.GetPlayer.Profile.Info.Settings.Role))
                    {
                        if (bot.GetPlayer.ProfileId != pl.ProfileId &&
                            !(pl.IsAI && bot.BotsGroup.Contains(pl.AIData.BotOwner)) &&
                            !bot.BotsGroup.IsAlly(pl))
                        {
                            nr++;
                            processedEnemies.Add(pl.ProfileId);
                        }
                    }
                }

                // Store in cache
                enemyLocationCache[cacheKeyWithId] = new CachedEnemyInfo(nr, cacheKey, Time.time);

                // Group membership is used only as a cautious threat-count floor. It does not add
                // unseen teammates to the follower's enemy controller or grant visibility/fire.
                return Mathf.Max(nr, nearbyGroupMembers);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("GetEnemiesAtLocation Error");
                Modules.Logger.LogError(ex);
                return Mathf.Max(1, nearbyGroupMembers);
            }
        }

        private static int CountNearbyLivingGroupMembers(EnemyInfo enemy, Vector3 position, float radius)
        {
            BotOwner? enemyOwner = enemy.Person?.AIData?.BotOwner;
            BotsGroup? enemyGroup = enemyOwner?.BotsGroup;
            if (enemyGroup == null || enemyGroup.MembersCount <= 0)
            {
                return 1;
            }

            float radiusSqr = radius * radius;
            int nearbyMembers = 0;
            int memberCount = enemyGroup.MembersCount;
            for (int i = 0; i < memberCount; i++)
            {
                // Groups can shrink as a teammate dies or despawns. Stop safely if EFT mutates
                // the list between the count snapshot and this main-thread evaluation.
                if (i >= enemyGroup.MembersCount)
                {
                    break;
                }

                BotOwner member = enemyGroup.Member(i);
                if (member == null ||
                    member.IsDead ||
                    member.BotState != EBotState.Active ||
                    member.GetPlayer?.HealthController?.IsAlive != true ||
                    (member.Position - position).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                nearbyMembers++;
            }

            // The queried living target itself is always one threat even during a transient group
            // mutation where EFT has not yet inserted it into Members.
            return Mathf.Max(1, nearbyMembers);
        }


        public static EnemyInfo? MakeEnemy(
            BotOwner bot,
            Player enemy,
            EBotEnemyCause cause = EBotEnemyCause.addPlayerToBoss,
            bool countSharedSeenAsPersonal = true)
        {
            if (bot == null || enemy == null) return null;
            if (bot.BotsGroup == null || bot.Memory == null || bot.EnemiesController == null) return null;

            if (BossPlayers.IsFollower(bot))
            {
                if (BossPlayers.IsPlayerBoss(enemy.ProfileId))
                {
                    return null;
                }

                if (enemy.IsAI && enemy.AIData?.BotOwner != null && BossPlayers.IsFollower(enemy.AIData.BotOwner))
                {
                    return null;
                }

                if (enemy.IsAI)
                {
                    WildSpawnType? role = enemy.Profile?.Info?.Settings?.Role;
                    if (role.HasValue && Props.friendlyBotTypes.Contains(role.Value))
                    {
                        return null;
                    }
                }
            }

            BotGroupEnemyInfo groupInfo;
            bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);

            if (groupInfo == null)
            {
                bot.BotsGroup.AddEnemy(enemy, cause);
                bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);

                // Some group validation paths can reject specific causes for same-side hostile AI.
                // Retry with checkAddTODO so BotsGroup can still mark enemy/neutrals/allies correctly.
                if (groupInfo == null && cause != EBotEnemyCause.checkAddTODO)
                {
                    bot.BotsGroup.AddEnemy(enemy, EBotEnemyCause.checkAddTODO);
                    bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);
                }
            }

            if (groupInfo == null)
            {
                groupInfo = new BotGroupEnemyInfo(enemy, bot.BotsGroup, cause);

                bot.Memory.AddEnemy(enemy, groupInfo, false);
            }

            EnemyInfo info;

            bot.EnemiesController.EnemyInfos.TryGetValue(enemy, out info);

            if (info == null)
            {
                groupInfo.EnemyLastPosition = enemy.Transform.position;
                info = bot.EnemiesController.AddNew(bot.BotsGroup, enemy, groupInfo);

                bot.EnemiesController.SetInfo(enemy, info);
            }

            info.IgnoreUntilAggression = false;
            bool canPromoteSharedSeenToPersonal =
                countSharedSeenAsPersonal &&
                !RequiresAcquisitionAwarenessGate(groupInfo?.Cause);
            bool countAsSeen = info.IsVisible || info.CanShoot || (canPromoteSharedSeenToPersonal && info.HaveSeen);
            RepairPersonalMemory(info, enemy.Transform.position, countAsSeen);

            return info;

        }

        public static bool HasDirectPersonalContact(EnemyInfo? info)
        {
            return info?.IsVisible == true || info?.CanShoot == true;
        }

        public static bool HasPersonalContactRecord(EnemyInfo? info)
        {
            return HasDirectPersonalContact(info) ||
                   info?.HaveSeenPersonal == true ||
                   info?.PersonalSeenTime > 0f ||
                   info?.PersonalLastSeenTime > 0f;
        }

        public static bool IsMemoryOnlyAcquisitionWithoutPersonalContact(EnemyInfo? info)
        {
            if (info == null || HasPersonalContactRecord(info))
            {
                return false;
            }

            return RequiresAcquisitionAwarenessGate(info.GroupInfo?.Cause);
        }

        public static bool IsRelationOnlyBossShareWithoutPersonalContact(EnemyInfo? info)
        {
            return info != null &&
                   !HasPersonalContactRecord(info) &&
                   info.GroupInfo?.Cause == EBotEnemyCause.addPlayerToBoss;
        }

        public static bool RequiresAcquisitionAwarenessGate(EBotEnemyCause? cause)
        {
            if (!cause.HasValue)
            {
                return true;
            }

            switch (cause.Value)
            {
                case EBotEnemyCause.byKill:
                case EBotEnemyCause.followGetHit:
                case EBotEnemyCause.addPlayer:
                case EBotEnemyCause.callBot:
                case EBotEnemyCause.gifterKill:
                case EBotEnemyCause.bossKillArena:
                case EBotEnemyCause.KillaSyncTagilla:
                case EBotEnemyCause.tagillaFindENemy:
                case EBotEnemyCause.fuckGestus:
                case EBotEnemyCause.pmcBossKill:
                case EBotEnemyCause.christmas:
                case EBotEnemyCause.synWithKilla:
                case EBotEnemyCause.ravangeZryachiy:
                case EBotEnemyCause.partisanBadKarma:
                case EBotEnemyCause.attackBTR:
                case EBotEnemyCause.tagillaAlarm:
                case EBotEnemyCause.MarkOfUnknowsDist:
                case EBotEnemyCause.zryachiyLogic:
                case EBotEnemyCause.pairLogic:
                    return false;
                default:
                    return true;
            }
        }

        public static void RepairPersonalMemory(EnemyInfo? info, Vector3 fallbackPosition, bool countAsSeen)
        {
            if (info == null)
            {
                return;
            }

            Vector3 enemyPosition = IsFinite(info.CurrPosition)
                ? info.CurrPosition
                : fallbackPosition;

            if (!IsFinite(enemyPosition) || enemyPosition.sqrMagnitude <= 0.01f)
            {
                enemyPosition = info.PersonalLastPos;
            }

            if (!IsFinite(enemyPosition) || enemyPosition.sqrMagnitude <= 0.01f)
            {
                return;
            }

            if (!IsFinite(info.PersonalLastPos) || info.PersonalLastPos.sqrMagnitude <= 0.01f)
            {
                info.PersonalLastPos = enemyPosition;
            }

            if (countAsSeen)
            {
                if (info.PersonalSeenTime <= 0f)
                {
                    info.PersonalSeenTime = Time.time;
                }

                if (info.PersonalLastSeenTime <= 0f)
                {
                    info.PersonalLastSeenTime = Time.time;
                }

                info.HaveSeenPersonal = true;
            }

            if (info.GroupInfo != null)
            {
                if (info.GroupInfo.EnemyLastSeenTimeSense <= 0f ||
                    !IsFinite(info.EnemyLastPosition) ||
                    info.EnemyLastPosition.sqrMagnitude <= 0.01f)
                {
                    info.GroupInfo.EnemyLastPosition = enemyPosition;
                }

                if (countAsSeen &&
                    (info.GroupInfo.EnemyLastSeenTimeReal <= 0f ||
                     !IsFinite(info.EnemyLastPositionReal) ||
                     info.EnemyLastPositionReal.sqrMagnitude <= 0.01f))
                {
                    info.GroupInfo.EnemyLastVisiblePosition = enemyPosition;
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static void ForceIgnoreUntilAggressionOff(BotOwner bot)
        {
            if (bot?.EnemiesController?.EnemyInfos == null) return;

            foreach (var kv in bot.EnemiesController.EnemyInfos)
            {
                EnemyInfo info = kv.Value;
                if (info != null)
                {
                    info.IgnoreUntilAggression = false;
                }
            }
        }

        public static void ForceIgnoreUntilAggressionOff(BotsGroup group)
        {
            if (group == null) return;

            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                ForceIgnoreUntilAggressionOff(member);
            }
        }

        public static void ClearEnemyLocations(string enemyId)
        {
            var keysToRemove = enemyLocationCache.Keys.Where(key => key.Item2 == enemyId).ToList();
            foreach (var key in keysToRemove)
            {
                enemyLocationCache.TryRemove(key, out _);
            }

            var visibilityKeys = visibilityCache.Keys.Where(key => key.enemyId == enemyId).ToList();
            foreach (var key in visibilityKeys)
            {
                visibilityCache.TryRemove(key, out _);
            }
        }

        public static void ClearEnemiesLocations()
        {
            enemyLocationCache.Clear();
            visibilityCache.Clear();
            trackedEnemies.Clear();
        }

        public static bool IsClosestEnemy(BotOwner botOwner_0)
        {
            bool result = true;
            EnemyInfo enemyInfo = botOwner_0.Memory.GoalEnemy;
            foreach (EnemyInfo enemy in botOwner_0.EnemiesController.EnemyInfos.Values)
            {
                if (enemy.Distance < enemyInfo.Distance)
                {
                    result = false;
                    break;
                }
            }

            return result;
        }

        private static bool IsFinitePosition(Vector3 position)
        {
            return !float.IsNaN(position.x) &&
                   !float.IsNaN(position.y) &&
                   !float.IsNaN(position.z) &&
                   !float.IsInfinity(position.x) &&
                   !float.IsInfinity(position.y) &&
                   !float.IsInfinity(position.z);
        }
    }
}
