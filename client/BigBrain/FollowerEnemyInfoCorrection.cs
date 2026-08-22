using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal static class FollowerEnemyInfoCorrection
    {
        private const float MaxReasonableEnemyDistance = 1000f;
        private const float DistanceCorrectionTolerance = 0.25f;
        private const float CloseFoliageVisibilityDistance = 10f;
        private const float CloseTargetShootFallbackDistance = 12f;
        private const float StaleVisibleFlagMaxAge = 0.75f;
        [System.ThreadStatic]
        private static int lookCheckDepth;
        [System.ThreadStatic]
        private static RaycastHit[]? raycastBuffer;

        public static bool IsInsideLookCheck => lookCheckDepth > 0;

        public static void BeginLookCheck()
        {
            lookCheckDepth++;
        }

        public static void EndLookCheck()
        {
            if (lookCheckDepth > 0)
            {
                lookCheckDepth--;
            }
        }

        public static SensorState CaptureState(EnemyInfo? enemyInfo)
        {
            if (enemyInfo == null)
            {
                return default;
            }

            return new SensorState
            {
                HasValue = true,
                IsVisible = enemyInfo.IsVisible,
                CanShoot = enemyInfo.CanShoot,
                VisibleType = enemyInfo.VisibleType,
                HaveSeenPersonal = enemyInfo.HaveSeenPersonal,
                PersonalSeenTime = enemyInfo.PersonalSeenTime,
                PersonalLastSeenTime = enemyInfo.PersonalLastSeenTime,
                PersonalLastPos = enemyInfo.PersonalLastPos,
                FirstTimeSeen = enemyInfo.FirstTimeSeen,
            };
        }

        public static void CorrectAfterLookCheck(EnemyInfo? enemyInfo)
        {
            CorrectAfterLookCheck(enemyInfo, default);
        }

        public static void CorrectAfterLookCheck(EnemyInfo? enemyInfo, SensorState previousState)
        {
            if (enemyInfo?.Owner == null ||
                !BossPlayers.IsFollower(enemyInfo.Owner))
            {
                return;
            }

            if (enemyInfo.Person?.HealthController?.IsAlive != true)
            {
                ForceInvisibleAndUnshootable(enemyInfo);
                return;
            }

            BotOwner botOwner = enemyInfo.Owner;
            if (!TryGetReliableDistance(botOwner, enemyInfo, out float distance, out Vector3 enemyPosition))
            {
                enemyInfo.SetCanShoot(false);
                DemoteVisibility(enemyInfo, previousState);
                return;
            }

            CorrectDistanceAndDirection(botOwner, enemyInfo, distance, enemyPosition);

            if (!ShouldRunLineCorrection(enemyInfo, previousState, distance))
            {
                enemyInfo.SetCanShoot(false);
                DemoteVisibility(enemyInfo, previousState);
                return;
            }

            bool directlyVisible = HasDirectVisibility(botOwner, enemyInfo, distance);
            bool canShoot = directlyVisible && HasVerifiedShootLane(botOwner, enemyInfo, distance);

            if (directlyVisible)
            {
                PromoteDirectVisible(enemyInfo, enemyPosition, previousState);
            }
            else
            {
                DemoteVisibility(enemyInfo, previousState);
            }

            enemyInfo.SetCanShoot(canShoot);
        }

        public static void CorrectAfterExternalSetInvisible(EnemyInfo? enemyInfo)
        {
            if (enemyInfo?.Owner == null ||
                !BossPlayers.IsFollower(enemyInfo.Owner))
            {
                return;
            }

            ForceInvisibleAndUnshootable(enemyInfo);
        }

        public static bool TryGetReliableDistance(BotOwner botOwner, EnemyInfo goalEnemy, out float distance)
        {
            return TryGetReliableDistance(botOwner, goalEnemy, out distance, out _);
        }

        public static void CorrectDistanceOnly(BotOwner? botOwner, EnemyInfo? enemyInfo)
        {
            if (botOwner == null ||
                enemyInfo == null ||
                enemyInfo.Owner == null ||
                enemyInfo.Owner != botOwner ||
                !BossPlayers.IsFollower(botOwner))
            {
                return;
            }

            if (enemyInfo.Person?.HealthController?.IsAlive != true)
            {
                ForceInvisibleAndUnshootable(enemyInfo);
                return;
            }

            if (TryGetReliableDistance(botOwner, enemyInfo, out float distance, out Vector3 enemyPosition))
            {
                CorrectDistanceAndDirection(botOwner, enemyInfo, distance, enemyPosition);
                ClearStaleVisibleOrShootFlag(enemyInfo);
            }
        }

        public static bool HasVerifiedShootLane(BotOwner botOwner, EnemyInfo goalEnemy)
        {
            return TryGetReliableDistance(botOwner, goalEnemy, out float distance, out _) &&
                   HasVerifiedShootLane(botOwner, goalEnemy, distance);
        }

        public static bool HasFreshPersonalVisual(EnemyInfo? enemyInfo, float maxAge)
        {
            if (enemyInfo == null)
            {
                return false;
            }

            float lastSeenTime = Mathf.Max(enemyInfo.PersonalLastSeenTime, enemyInfo.PersonalSeenTime);
            return lastSeenTime > 0f && Time.time - lastSeenTime <= maxAge;
        }

        public static bool RefreshDirectContactForAcquisition(BotOwner botOwner, EnemyInfo? enemyInfo)
        {
            if (botOwner == null ||
                enemyInfo?.Owner == null ||
                enemyInfo.Owner != botOwner ||
                !BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            SensorState previousState = CaptureState(enemyInfo);
            if (enemyInfo.Person?.HealthController?.IsAlive != true)
            {
                ForceInvisibleAndUnshootable(enemyInfo);
                return false;
            }

            if (!TryGetReliableDistance(botOwner, enemyInfo, out float distance, out Vector3 enemyPosition))
            {
                enemyInfo.SetCanShoot(false);
                DemoteVisibility(enemyInfo, previousState);
                return false;
            }

            CorrectDistanceAndDirection(botOwner, enemyInfo, distance, enemyPosition);

            bool directlyVisible = HasDirectVisibility(botOwner, enemyInfo, distance);
            bool canShoot = directlyVisible && HasVerifiedShootLane(botOwner, enemyInfo, distance);
            if (directlyVisible)
            {
                PromoteDirectVisible(enemyInfo, enemyPosition, previousState);
            }
            else
            {
                DemoteVisibility(enemyInfo, previousState);
            }

            enemyInfo.SetCanShoot(canShoot);
            return directlyVisible || canShoot;
        }

        private static void CorrectDistanceAndDirection(
            BotOwner botOwner,
            EnemyInfo enemyInfo,
            float distance,
            Vector3 enemyPosition)
        {
            Vector3 direction = enemyPosition - botOwner.Position;
            enemyInfo.Direction = direction;

            if (!IsFinite(enemyInfo.Distance) ||
                enemyInfo.Distance <= 0f ||
                enemyInfo.Distance >= MaxReasonableEnemyDistance ||
                Mathf.Abs(enemyInfo.Distance - distance) > DistanceCorrectionTolerance)
            {
                enemyInfo.Distance = distance;
            }
        }

        private static void PromoteDirectVisible(EnemyInfo enemyInfo, Vector3 enemyPosition, SensorState previousState)
        {
            bool wasAlreadyDirectVisible = previousState.HasValue &&
                                           previousState.IsVisible &&
                                           previousState.VisibleType == EEnemyPartVisibleType.Visible;

            if (previousState.HasValue && previousState.HaveSeenPersonal)
            {
                enemyInfo.HaveSeenPersonal = true;
                enemyInfo.FirstTimeSeen = previousState.FirstTimeSeen > 0f
                    ? previousState.FirstTimeSeen
                    : Time.time;
            }
            else if (!enemyInfo.HaveSeenPersonal)
            {
                enemyInfo.HaveSeenPersonal = true;
                enemyInfo.FirstTimeSeen = Time.time;
            }

            enemyInfo.IsVisible = true;
            enemyInfo.PersonalSeenTime = wasAlreadyDirectVisible
                ? previousState.PersonalSeenTime > 0f ? previousState.PersonalSeenTime : Time.time
                : Time.time;
            enemyInfo.PersonalLastSeenTime = Time.time;
            enemyInfo.PersonalLastPos = enemyPosition;
            enemyInfo.SetVisibleType(EEnemyPartVisibleType.Visible);
            enemyInfo.PrevIsVisible(true);
        }

        private static void DemoteVisibility(EnemyInfo enemyInfo, SensorState previousState)
        {
            enemyInfo.IsVisible = false;
            enemyInfo.SetVisibleType(EEnemyPartVisibleType.NotVisible);
            enemyInfo.PrevIsVisible(false);

            if (!previousState.HasValue)
            {
                return;
            }

            enemyInfo.HaveSeenPersonal = previousState.HaveSeenPersonal;
            enemyInfo.PersonalSeenTime = previousState.PersonalSeenTime;
            enemyInfo.PersonalLastSeenTime = previousState.PersonalLastSeenTime;
            enemyInfo.PersonalLastPos = previousState.PersonalLastPos;
            enemyInfo.FirstTimeSeen = previousState.FirstTimeSeen;
        }

        private static bool TryGetReliableDistance(
            BotOwner botOwner,
            EnemyInfo goalEnemy,
            out float distance,
            out Vector3 enemyPosition)
        {
            enemyPosition = GetEnemyPosition(goalEnemy);
            distance = goalEnemy.Distance;

            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            float computedDistance = Vector3.Distance(botOwner.Position, enemyPosition);
            if (!IsValidDistance(computedDistance))
            {
                return false;
            }

            distance = computedDistance;
            return true;
        }

        private static Vector3 GetEnemyPosition(EnemyInfo goalEnemy)
        {
            if (goalEnemy.Person?.Transform != null)
            {
                return goalEnemy.Person.Transform.position;
            }

            return goalEnemy.CurrPosition;
        }

        private static bool HasDirectVisibility(BotOwner botOwner, EnemyInfo goalEnemy, float distance)
        {
            if (botOwner.LookSensor == null ||
                distance > botOwner.LookSensor.VisibleDist + 0.25f)
            {
                return false;
            }

            LayerMask visibilityMask = GetVisibilityMask(botOwner, distance);
            Vector3 origin = GetLookOrigin(botOwner);

            return CanSeeMainPart(botOwner, goalEnemy, BodyPartType.head, origin, visibilityMask) ||
                   CanSeeMainPart(botOwner, goalEnemy, BodyPartType.body, origin, visibilityMask);
        }

        private static bool ShouldRunLineCorrection(EnemyInfo enemyInfo, SensorState previousState, float distance)
        {
            if (distance <= CloseFoliageVisibilityDistance)
            {
                return true;
            }

            if (enemyInfo.IsVisible ||
                enemyInfo.CanShoot ||
                enemyInfo.VisibleType == EEnemyPartVisibleType.Visible)
            {
                return true;
            }

            return previousState.HasValue &&
                   previousState.IsVisible &&
                   previousState.VisibleType == EEnemyPartVisibleType.Visible;
        }

        private static bool HasVerifiedShootLane(BotOwner botOwner, EnemyInfo goalEnemy, float distance)
        {
            if (botOwner.LookSensor == null ||
                !botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            Vector3 fireOrigin = GetFireOrigin(botOwner);
            LayerMask shootMask = LayersMaskController.HighPolyWithTerrainMask;

            if (CanShootMainPart(botOwner, goalEnemy, BodyPartType.head, fireOrigin, shootMask) ||
                CanShootMainPart(botOwner, goalEnemy, BodyPartType.body, fireOrigin, shootMask))
            {
                return true;
            }

            if (distance > CloseTargetShootFallbackDistance)
            {
                return false;
            }

            ShootToPoint? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint != null)
            {
                return HasClearLine(fireOrigin, shootPoint.Point, shootMask, shootPoint.DistCoef);
            }

            Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
            return IsFinite(bodyPoint) &&
                   HasClearLine(fireOrigin, bodyPoint, shootMask);
        }

        private static bool CanSeeMainPart(
            BotOwner botOwner,
            EnemyInfo goalEnemy,
            BodyPartType partType,
            Vector3 origin,
            LayerMask mask)
        {
            return TryGetMainPartPosition(goalEnemy, partType, out Vector3 partPosition) &&
                   IsPointInsideFollowerVisionCone(botOwner, partPosition) &&
                   HasClearLine(origin, partPosition, mask);
        }

        private static bool CanShootMainPart(
            BotOwner botOwner,
            EnemyInfo goalEnemy,
            BodyPartType partType,
            Vector3 origin,
            LayerMask mask)
        {
            return TryGetMainPartPosition(goalEnemy, partType, out Vector3 partPosition) &&
                   HasClearLine(origin, partPosition, mask);
        }

        private static bool HasClearLine(Vector3 origin, Vector3 target, LayerMask mask, float distanceCoefficient = 1f)
        {
            if (!IsFinite(origin) || !IsFinite(target))
            {
                return false;
            }

            Vector3 direction = target - origin;
            float distanceSqr = direction.sqrMagnitude;
            if (distanceSqr <= 0.000001f)
            {
                return false;
            }

            float distance = Mathf.Sqrt(distanceSqr);
            RaycastHit[] buffer = raycastBuffer ??= new RaycastHit[1];
            return Physics.RaycastNonAlloc(
                new Ray(origin, direction / distance),
                buffer,
                distance * distanceCoefficient,
                mask) == 0;
        }

        private static bool TryGetMainPartPosition(
            EnemyInfo goalEnemy,
            BodyPartType partType,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (goalEnemy.Person is Player enemy &&
                enemy.MainParts != null &&
                enemy.MainParts.TryGetValue(partType, out EnemyPart part) &&
                part != null &&
                IsFinite(part.Position))
            {
                position = part.Position;
                return true;
            }

            if (partType == BodyPartType.body)
            {
                position = goalEnemy.GetBodyPartPosition();
                return IsFinite(position);
            }

            return false;
        }

        private static bool IsPointInsideFollowerVisionCone(BotOwner botOwner, Vector3 position)
        {
            LookSensor? lookSensor = botOwner.LookSensor;
            if (lookSensor == null)
            {
                return false;
            }

            if (lookSensor.IsFullSectorView)
            {
                return true;
            }

            Vector3 toPoint = position - botOwner.Position;
            Vector3 lookDirection = botOwner.LookDirection;
            if (lookDirection.sqrMagnitude <= 0.001f && botOwner.Transform != null)
            {
                lookDirection = botOwner.Transform.forward;
            }

            if (toPoint.sqrMagnitude <= 0.001f || lookDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            float denominator = Mathf.Sqrt(toPoint.sqrMagnitude * lookDirection.sqrMagnitude);
            if (denominator <= 0.0001f)
            {
                return false;
            }

            float dot = Vector3.Dot(lookDirection, toPoint) / denominator;
            float requiredDot = botOwner.NightVision?.UsingNow == true
                ? lookSensor.VISIBLE_ANGLE_NIGHTVISION
                : botOwner.BotLight?.IsEnable == true
                    ? lookSensor.VISIBLE_ANGLE_LIGHT
                    : lookSensor.VISIBLE_ANGLE;

            return dot >= requiredDot;
        }

        private static LayerMask GetVisibilityMask(BotOwner botOwner, float distance)
        {
            if (distance <= CloseFoliageVisibilityDistance)
            {
                return LayersMaskController.HighPolyWithTerrainMask;
            }

            return botOwner.LookSensor?.Mask ?? LayersMaskController.HighPolyWithTerrainMaskAI;
        }

        private static Vector3 GetFireOrigin(BotOwner botOwner)
        {
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            return IsFinite(fireOrigin) && fireOrigin.sqrMagnitude > 0.01f
                ? fireOrigin
                : botOwner.Position + Vector3.up * 1.2f;
        }

        private static Vector3 GetLookOrigin(BotOwner botOwner)
        {
            Vector3 headPoint = botOwner.LookSensor?.HeadPoint ?? Vector3.zero;
            if (IsFinite(headPoint) && headPoint.sqrMagnitude > 0.01f)
            {
                return headPoint;
            }

            if (botOwner.MyHead != null &&
                IsFinite(botOwner.MyHead.position) &&
                botOwner.MyHead.position.sqrMagnitude > 0.01f)
            {
                return botOwner.MyHead.position;
            }

            return botOwner.Position + Vector3.up * 1.6f;
        }

        private static void ForceInvisibleAndUnshootable(EnemyInfo enemyInfo)
        {
            enemyInfo.SetCanShoot(false);
            enemyInfo.IsVisible = false;
            enemyInfo.SetVisibleType(EEnemyPartVisibleType.NotVisible);
            enemyInfo.PrevIsVisible(false);
        }

        private static void ClearStaleVisibleOrShootFlag(EnemyInfo enemyInfo)
        {
            if (!enemyInfo.IsVisible &&
                !enemyInfo.CanShoot &&
                enemyInfo.VisibleType != EEnemyPartVisibleType.Visible)
            {
                return;
            }

            if (HasFreshPersonalVisual(enemyInfo, StaleVisibleFlagMaxAge))
            {
                return;
            }

            ForceInvisibleAndUnshootable(enemyInfo);
        }

        private static bool IsValidDistance(float value)
        {
            return IsFinite(value) && value > 0f && value < MaxReasonableEnemyDistance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        public struct SensorState
        {
            public bool HasValue;
            public bool IsVisible;
            public bool CanShoot;
            public EEnemyPartVisibleType VisibleType;
            public bool HaveSeenPersonal;
            public float PersonalSeenTime;
            public float PersonalLastSeenTime;
            public Vector3 PersonalLastPos;
            public float FirstTimeSeen;
        }
    }
}
