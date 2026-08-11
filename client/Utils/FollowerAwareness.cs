using Comfort.Common;
using EFT;
using UnityEngine;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.Utils
{
    internal static class FollowerAwareness
    {
        private const float ProtectedImpactMinCoverThickness = 0.75f;
        private const float BulletAwarenessOriginHeight = 1.2f;

        private sealed class State
        {
            public float DamagedUntil;
            public float ThreatenedUntil;
            public bool HasThreatLookPoint;
            public Vector3 ThreatLookPoint;
            public float ThreatSourceDistance = float.MaxValue;
            public float LastSoundTime;
            public float LastGunshotTime;
            public float NextBulletReactionAt;
            public readonly System.Collections.Generic.List<Vector3> ProcessedSoundZones = new();
        }

        private static readonly System.Collections.Generic.Dictionary<string, State> States = new();

        public static bool WasRecentlyHit(BotOwner bot)
        {
            return WasRecentlyDamaged(bot) || WasRecentlyThreatened(bot);
        }

        public static bool WasRecentlyDamaged(BotOwner bot)
        {
            var state = GetState(bot);
            return state != null && state.DamagedUntil > Time.time;
        }

        public static bool WasRecentlyThreatened(BotOwner bot)
        {
            var state = GetState(bot);
            return state != null && state.ThreatenedUntil > Time.time;
        }

        public static bool TryGetRecentThreatLookPoint(BotOwner bot, out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;
            var state = GetState(bot);
            if (state == null ||
                !state.HasThreatLookPoint ||
                Mathf.Max(state.DamagedUntil, state.ThreatenedUntil) <= Time.time)
            {
                return false;
            }

            lookPoint = state.ThreatLookPoint;
            return lookPoint != Vector3.zero;
        }

        public static bool TryGetRecentCloseThreatLookPoint(BotOwner bot, float maxSourceDistance, out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;
            var state = GetState(bot);
            if (state == null ||
                !state.HasThreatLookPoint ||
                state.ThreatSourceDistance > maxSourceDistance ||
                state.ThreatenedUntil <= Time.time)
            {
                return false;
            }

            lookPoint = state.ThreatLookPoint;
            return lookPoint != Vector3.zero;
        }

        public static void ClearTransientState(BotOwner bot)
        {
            if (string.IsNullOrEmpty(bot?.ProfileId))
            {
                return;
            }

            States.Remove(bot.ProfileId);
        }

        public static void FollowerHit(BotOwner bot, DamageInfoStruct damageInfo)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active)
            {
                return;
            }
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot))
            {
                return;
            }

            Vector3 lookPoint = Vector3.zero;
            string shooterId = damageInfo.Player?.iPlayer?.ProfileId;
            Player shooter = !string.IsNullOrEmpty(shooterId)
                ? Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(shooterId)
                : null;

            if (shooter != null)
            {
                lookPoint = shooter.Transform.position;
                if (shooter.MainParts != null && shooter.MainParts.TryGetValue(BodyPartType.body, out var bodyPart) && bodyPart != null)
                {
                    lookPoint = bodyPart.Position;
                }

                if (!TryPromoteIncomingThreat(bot, shooter, "directHit", countAsVisible: false) &&
                    CanBotShootEnemy(bot, shooter))
                {
                    TryAcquireVisibleHostileOfBossGroup(bot, shooter);
                }
            }

            if (lookPoint == Vector3.zero && damageInfo.MasterOrigin != Vector3.zero)
            {
                lookPoint = damageInfo.MasterOrigin;
            }

            if (lookPoint == Vector3.zero && damageInfo.Direction.sqrMagnitude > 0.001f)
            {
                lookPoint = bot.Position - damageInfo.Direction.normalized * 20f;
            }

            if (lookPoint != Vector3.zero)
            {
                RegisterThreatLookPoint(bot, lookPoint, 3f);
                RegisterDamage(bot, 3f);
            }
            else
            {
                var state = GetState(bot);
                if (state != null)
                {
                    state.DamagedUntil = Time.time + 3f;
                    state.ThreatenedUntil = Time.time + 3f;
                }
            }
        }

        public static bool FakeShot(BotOwner bot, Vector3 lookPoint, float sourceDistance = float.MaxValue)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) return false;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return false;
            if (bot.Memory.HaveEnemy && bot.Memory.GoalEnemy != null && bot.Memory.GoalEnemy.IsVisible)
            {
                return false;
            }

            Vector3 botPos = bot.GetPlayer?.Transform?.position ?? bot.Position;
            Vector3 targetDirection = lookPoint - botPos;
            if (targetDirection.sqrMagnitude < 0.01f)
            {
                targetDirection = bot.LookDirection.sqrMagnitude > 0.01f ? bot.LookDirection : Vector3.forward;
                lookPoint = botPos + targetDirection.normalized * 5f;
            }

            RegisterThreatLookPoint(bot, lookPoint, 3f, sourceDistance);
            bot.Steering.LookToPoint(lookPoint, CalcTurnSpeed(bot.LookDirection, targetDirection));
            return true;
        }

        public static void SoundHeard(BotOwner bot, Player enemy, Vector3 position, float distance, AISoundType type)
        {
            if (bot == null || enemy == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return;
            var state = GetState(bot);
            if (state == null) return;
            bool gunSound = type == AISoundType.silencedGun || type == AISoundType.gun;
            if (gunSound && Time.time < state.LastGunshotTime + 3f)
            {
                return;
            }

            if (gunSound && !WasRecentlyHit(bot))
            {
                Vector3 lookPoint2 = BuildLookPoint(bot, position, 20f);
                bool hostileToBossGroup = IsHostileToBossGroup(bot, enemy);

                if (distance <= CombatDistanceConfiguration.Instance.GetSoundHeardDistance())
                {
                    FakeShot(bot, lookPoint2, distance);
                    if (CanBotShootEnemy(bot, enemy))
                    {
                        bool acquired = TryAutoAcquireCloseThreat(bot, enemy, distance);
                        if (!acquired && hostileToBossGroup && CanBotShootEnemy(bot, enemy))
                        {
                            TryAcquireVisibleHostileOfBossGroup(bot, enemy);
                        }
                    }

                    state.LastGunshotTime = Time.time;
                }
                else if (CanBotShootEnemy(bot, enemy))
                {
                    FakeShot(bot, lookPoint2, distance);
                    if (hostileToBossGroup)
                    {
                        TryAcquireVisibleHostileOfBossGroup(bot, enemy);
                    }
                    state.LastGunshotTime = Time.time;
                }
                return;
            }

            if (type != AISoundType.step)
            {
                return;
            }

            Vector3 positionZone = new(
                Mathf.Floor(position.x / 8f) * 8f,
                Mathf.Floor(position.y / 8f) * 8f,
                Mathf.Floor(position.z / 8f) * 8f
            );

            bool wasProcessed = state.ProcessedSoundZones.Contains(positionZone);
            if (wasProcessed && Time.time - state.LastSoundTime < 5f)
            {
                return;
            }

            Vector3 lookPoint = BuildLookPoint(bot, position, 20f);

            if (!wasProcessed)
            {
                state.ProcessedSoundZones.Add(positionZone);
                if (state.ProcessedSoundZones.Count > 20) state.ProcessedSoundZones.RemoveAt(0);
            }

            if (distance <= CombatDistanceConfiguration.Instance.GetTooCloseDistance())
            {
                FakeShot(bot, lookPoint, distance);
                if (CanBotShootEnemy(bot, enemy))
                {
                    TryAutoAcquireCloseThreat(bot, enemy, distance);
                }
            }
            else
            {
                state.LastSoundTime = Time.time;
                FakeShot(bot, lookPoint, distance);
            }
        }

        public static void BulletFelt(BotOwner bot, EftBulletClass bullet, Vector3? impactPoint = null)
        {
            if (bot == null || bullet == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return;

            var state = GetState(bot);
            if (state == null) return;
            if (Time.time < state.NextBulletReactionAt)
            {
                return;
            }

            Player shooter = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(bullet.PlayerProfileID);
            if (shooter == null)
            {
                return;
            }

            bool isEnemy = bot.EnemiesController.IsEnemy(shooter) ||
                (bullet.Player?.iPlayer != null && bot.BotsGroup.IsEnemy(bullet.Player.iPlayer));
            if (!isEnemy)
            {
                bool hostileToBossGroup = IsHostileToBossGroup(bot, shooter);
                if (!hostileToBossGroup)
                {
                    return;
                }
            }

            Vector3 impact = impactPoint ?? bullet.HitPoint;
            if (impact == Vector3.zero)
            {
                impact = bullet.CurrentPosition;
            }

            float distanceSqr = (impact - bot.Position).sqrMagnitude;
            if (distanceSqr > CombatDistanceConfiguration.Instance.GetBulletHearDistanceSqr())
            {
                return;
            }

            if (IsImpactProtectedByHardCover(bot, impact))
            {
                return;
            }

            state.NextBulletReactionAt = Time.time + 1f;
            float dispersion = distanceSqr / CombatDistanceConfiguration.Instance.GetBulletImpactDispersionSqr(); ;
            Vector3 random = Random.onUnitSphere;
            random.y = 0f;
            random = random.normalized * dispersion;
            Vector3 estimatedShooterPos = shooter.Transform.position + random;
            RegisterThreatLookPoint(bot, estimatedShooterPos, 3f);

            bool acquired = TryAutoAcquireCloseThreat(bot, shooter, Mathf.Sqrt(distanceSqr));
            if (acquired)
            {
                return;
            }

            if (CanBotShootEnemy(bot, shooter) && TryAcquireVisibleHostileOfBossGroup(bot, shooter))
            {
                return;
            }
            FakeShot(bot, estimatedShooterPos);
        }

        private static void RegisterThreatLookPoint(
            BotOwner bot,
            Vector3 lookPoint,
            float duration,
            float sourceDistance = float.MaxValue)
        {
            var state = GetState(bot);
            if (state == null)
            {
                return;
            }

            state.ThreatenedUntil = Time.time + duration;
            state.ThreatLookPoint = lookPoint;
            state.HasThreatLookPoint = true;
            state.ThreatSourceDistance = sourceDistance;
        }

        private static void RegisterDamage(BotOwner bot, float duration)
        {
            var state = GetState(bot);
            if (state == null)
            {
                return;
            }

            state.DamagedUntil = Time.time + duration;
        }

        private static bool IsImpactProtectedByHardCover(BotOwner bot, Vector3 impact)
        {
            Vector3 origin = GetBulletAwarenessOrigin(bot);
            Vector3 toImpact = impact - origin;
            float distance = toImpact.magnitude;
            if (distance <= 0.25f)
            {
                return false;
            }

            if (!Physics.Linecast(origin, impact, out RaycastHit hit, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return false;
            }

            // If the first hard hit is well before the bullet impact, the bullet struck the far
            // side of cover. That is not a near miss at the follower's feet.
            return (impact - hit.point).magnitude >= ProtectedImpactMinCoverThickness;
        }

        private static Vector3 GetBulletAwarenessOrigin(BotOwner bot)
        {
            Vector3 origin = bot.GetPlayer?.Transform?.position ?? bot.Position;
            origin.y += BulletAwarenessOriginHeight;
            return origin;
        }

        private static Vector3 BuildLookPoint(BotOwner bot, Vector3 sourceWorldPos, float distance)
        {
            Vector3 botPos = bot.GetPlayer?.Transform?.position ?? bot.Position;
            Vector3 dir = sourceWorldPos - botPos;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = bot.LookDirection.sqrMagnitude > 0.01f ? bot.LookDirection : Vector3.forward;
            }
            return botPos + dir.normalized * distance;
        }

        private static bool TryAutoAcquireCloseThreat(BotOwner bot, Player enemy, float distance)
        {
            if (bot == null || enemy == null) return false;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return false;
            if (distance > CombatDistanceConfiguration.Instance.GetCloseThreatAutoAcquireDistance())
            {
                return false;
            }

            EnemyInfo? enemyInfo = Enemy.MakeEnemy(
                bot,
                enemy,
                countSharedSeenAsPersonal: false);
            enemyInfo?.SetVisible(true);
            Enemy.RepairPersonalMemory(enemyInfo, enemy.Transform.position, true);
            return TryPromoteIncomingThreat(bot, enemy, "closeThreat", countAsVisible: true, enemyInfo);
        }

        private static bool TryAcquireVisibleHostileOfBossGroup(BotOwner bot, Player enemy)
        {
            if (bot == null || enemy == null) return false;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return false;
            EnemyInfo? enemyInfo = Enemy.MakeEnemy(
                bot,
                enemy,
                countSharedSeenAsPersonal: false);
            enemyInfo?.SetVisible(true);
            Enemy.RepairPersonalMemory(enemyInfo, enemy.Transform.position, true);
            return enemyInfo != null;
        }

        private static bool TryPromoteIncomingThreat(
            BotOwner bot,
            Player enemy,
            string reason,
            bool countAsVisible,
            EnemyInfo? existingInfo = null)
        {
            if (bot?.Memory == null || enemy == null || enemy.HealthController?.IsAlive != true)
            {
                return false;
            }
            if (FollowerEnemyEnforceSuppression.IsSuppressed(bot))
            {
                return false;
            }

            if (!IsKnownOrBossGroupHostile(bot, enemy))
            {
                return false;
            }

            EnemyInfo? enemyInfo = existingInfo ?? Enemy.MakeEnemy(
                bot,
                enemy,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: countAsVisible);
            if (enemyInfo == null)
            {
                return false;
            }

            if (countAsVisible)
            {
                enemyInfo.SetVisible(true);
            }

            EnemyInfo? currentGoal = bot.Memory.GoalEnemy;
            bool alreadyGoal = string.Equals(currentGoal?.ProfileId, enemy.ProfileId, System.StringComparison.Ordinal);
            bool temporaryThreatRegistered = TryRegisterOrderedPushTemporaryThreat(bot, enemyInfo, enemy, reason);
            bool shouldPromote = alreadyGoal ||
                                 temporaryThreatRegistered ||
                                 ShouldReplaceGoalWithIncomingThreat(bot, currentGoal, enemy);

            if (!shouldPromote)
            {
                return false;
            }

            if (!alreadyGoal && !temporaryThreatRegistered)
            {
                FollowerContactEnemyRetention.ClearAndAllowNextGoalClear(bot);
                using (FollowerGoalEnemyTracker.Begin("FollowerAwareness.TryPromoteIncomingThreat", $"clearPrevious:{reason}"))
                {
                    bot.Memory.GoalEnemy = null;
                }
                bot.Memory.LastEnemy = null;
            }
            else if (!alreadyGoal)
            {
                bot.Memory.LastEnemy = null;
            }

            enemyInfo.PriorityIndex = 0;
            enemyInfo.IgnoreUntilAggression = false;
            Enemy.RepairPersonalMemory(
                enemyInfo,
                enemy.Transform.position,
                countAsVisible || Enemy.HasDirectPersonalContact(enemyInfo));
            bot.Memory.IsPeace = false;
            using (FollowerGoalEnemyTracker.Begin("FollowerAwareness.TryPromoteIncomingThreat", reason))
            {
                bot.Memory.GoalEnemy = enemyInfo;
            }
            if (!temporaryThreatRegistered)
            {
                FollowerContactEnemyRetention.Register(bot, enemy, countAsVisible || enemyInfo.IsVisible || enemyInfo.CanShoot, prioritized: true);
            }

            return true;
        }

        private static bool TryRegisterOrderedPushTemporaryThreat(BotOwner bot, EnemyInfo enemyInfo, Player enemy, string reason)
        {
            if (!string.Equals(reason, "directHit", System.StringComparison.Ordinal) ||
                string.IsNullOrEmpty(enemy?.ProfileId) ||
                enemyInfo == null)
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(bot);
            if (followerData == null ||
                !followerData.TryGetOrderedPushTargetLock(out string targetProfileId, out _) ||
                string.Equals(targetProfileId, enemy.ProfileId, System.StringComparison.Ordinal))
            {
                return false;
            }

            return FollowerCombatTargetCommitments.TryRegisterTemporaryTarget(
                bot,
                enemyInfo,
                "directHit",
                out _);
        }

        private static bool ShouldReplaceGoalWithIncomingThreat(BotOwner bot, EnemyInfo? currentGoal, Player incomingEnemy)
        {
            if (currentGoal == null || currentGoal.Person?.HealthController?.IsAlive != true)
            {
                return true;
            }

            if (!currentGoal.IsVisible && !currentGoal.CanShoot)
            {
                return true;
            }

            float incomingDistance = GetPlanarDistance(bot.Position, incomingEnemy.Position);
            float currentDistance = GetPlanarDistance(bot.Position, currentGoal.Person?.Position ?? currentGoal.CurrPosition);
            return incomingDistance + 8f < currentDistance;
        }

        private static bool IsKnownOrBossGroupHostile(BotOwner bot, Player enemy)
        {
            if (bot?.EnemiesController == null || bot.BotsGroup == null || enemy == null)
            {
                return false;
            }

            return bot.EnemiesController.IsEnemy(enemy) ||
                   bot.BotsGroup.IsEnemy(enemy) ||
                   IsHostileToBossGroup(bot, enemy);
        }

        private static float GetPlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public static bool IsHostileToBossGroupForReaction(BotOwner followerBot, Player enemyPlayer)
        {
            return IsHostileToBossGroup(followerBot, enemyPlayer);
        }

        private static bool IsHostileToBossGroup(BotOwner followerBot, Player enemyPlayer)
        {
            if (followerBot == null || enemyPlayer == null || !enemyPlayer.IsAI) return false;
            BotOwner enemyBot = enemyPlayer.AIData?.BotOwner;
            if (enemyBot == null) return false;

            var followerData = BossPlayers.Instance?.GetFollower(followerBot);
            var boss = followerData?.GetBoss();
            if (boss?.realPlayer == null) return false;

            if (enemyBot.BotsGroup?.IsEnemy(boss.realPlayer) == true)
            {
                return true;
            }

            string goalEnemyId = enemyBot.Memory?.GoalEnemy?.ProfileId;
            if (!string.IsNullOrEmpty(goalEnemyId) && string.Equals(goalEnemyId, boss.realPlayer.ProfileId, System.StringComparison.Ordinal))
            {
                return true;
            }

            if (boss.Followers != null)
            {
                foreach (BotOwner member in boss.Followers)
                {
                    if (member == null || member.IsDead) continue;
                    if (member.ProfileId == enemyBot.ProfileId) continue;
                    if (enemyBot.BotsGroup?.IsEnemy(member.GetPlayer) == true)
                    {
                        return true;
                    }

                    if (!string.IsNullOrEmpty(goalEnemyId) &&
                        !string.IsNullOrEmpty(member.ProfileId) &&
                        string.Equals(goalEnemyId, member.ProfileId, System.StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CanBotShootEnemy(BotOwner bot, Player enemy)
        {
            try
            {
                if (bot == null || enemy == null || bot.LookSensor == null) return false;
                if (enemy.MainParts == null) return false;

                Vector3 firePos = bot.PlayerBones?.WeaponRoot?.position ?? (bot.Position + Vector3.up * 1.2f);

                if (enemy.MainParts.TryGetValue(BodyPartType.head, out var enemyHead) && enemyHead != null)
                {
                    if (Utils.CanShootToTarget(
                        new ShootPointClass(enemyHead.Position, 1f),
                        firePos,
                        bot.LookSensor.Mask
                    ))
                    {
                        return true;
                    }
                }

                if (enemy.MainParts.TryGetValue(BodyPartType.body, out var enemyBody) && enemyBody != null)
                {
                    return Utils.CanShootToTarget(
                        new ShootPointClass(enemyBody.Position, 1f),
                        firePos,
                        bot.LookSensor.Mask
                    );
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static State GetState(BotOwner bot)
        {
            if (bot == null) return null;
            if (!States.TryGetValue(bot.ProfileId, out State state))
            {
                state = new State();
                States[bot.ProfileId] = state;
            }
            return state;
        }

        private static float CalcTurnSpeed(Vector3 currentLookDirection, Vector3 targetDirection)
        {
            const float min = 125f;
            const float max = 360f;
            const float maxAngle = 150f;
            const float minAngle = 5f;

            float angle = Vector3.Angle(currentLookDirection, targetDirection.normalized);
            if (angle >= maxAngle) return max;
            if (angle <= minAngle) return min;

            float ratio = (angle - minAngle) / (maxAngle - minAngle);
            return Mathf.Lerp(min, max, ratio);
        }
    }
}
