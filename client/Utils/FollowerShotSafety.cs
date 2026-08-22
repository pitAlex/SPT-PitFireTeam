using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Utils
{
    public static class FollowerShotSafety
    {
        private const float LaneRadius = 0.55f;
        private const float CloseFrontDistance = 2.5f;
        private const float CloseFrontDot = 0.25f;
        private const float DistancePadding = 1.0f;
        private const float SuppressionLaneRadius = 1.25f;
        private const float SuppressionCloseFrontDistance = 4.0f;
        private const float SuppressionCloseFrontDot = 0.1f;
        private const float SuppressionDistancePadding = 2.0f;
        private const float AimLaneRadius = 0.5f;
        private const float AimLaneDistancePadding = 0.5f;
        private const float GrenadeThrowOriginFallbackHeight = 1.2f;
        private const float GrenadeNearOriginClearanceRadius = 0.25f;
        private const float GrenadeNearOriginClearanceDistance = 3.0f;
        public const float RegularGrenadeUnsafeRadius = 8f;

        public static bool IsFriendlyInShotLane(BotOwner shooter, Vector3 targetPosition)
        {
            Vector3 origin = GetFireOrigin(shooter, Vector3.zero);
            if (!TryBuildLane(origin, targetPosition, out Vector3 direction, out float maxDistance))
            {
                return false;
            }

            return IsFriendlyInShotLaneCore(
                shooter,
                origin,
                direction,
                maxDistance,
                LaneRadius,
                CloseFrontDistance,
                CloseFrontDot,
                DistancePadding);
        }

        public static bool IsFriendlyInShotLane(BotOwner shooter, Vector3 weaponFirePort, Vector3 targetPosition)
        {
            Vector3 origin = GetFireOrigin(shooter, weaponFirePort);
            if (!TryBuildLane(origin, targetPosition, out Vector3 direction, out float maxDistance))
            {
                return false;
            }

            return IsFriendlyInShotLaneCore(
                shooter,
                origin,
                direction,
                maxDistance,
                LaneRadius,
                CloseFrontDistance,
                CloseFrontDot,
                DistancePadding);
        }

        public static bool IsFriendlyInShotLane(BotOwner shooter, Vector3 weaponFirePort, Vector3 weaponPointDirection, float distance)
        {
            if (distance <= 0.05f || weaponPointDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 origin = GetFireOrigin(shooter, weaponFirePort);
            Vector3 direction = weaponPointDirection.normalized;
            return IsFriendlyInShotLaneCore(
                shooter,
                origin,
                direction,
                distance,
                LaneRadius,
                CloseFrontDistance,
                CloseFrontDot,
                DistancePadding);
        }

        public static bool IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 targetPosition)
        {
            Vector3 origin = GetFireOrigin(shooter, Vector3.zero);
            if (!TryBuildLane(origin, targetPosition, out Vector3 direction, out float maxDistance))
            {
                return false;
            }

            return IsFriendlyInSuppressionLaneCore(shooter, origin, direction, maxDistance);
        }

        public static bool IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 weaponFirePort, Vector3 targetPosition)
        {
            Vector3 origin = GetFireOrigin(shooter, weaponFirePort);
            if (!TryBuildLane(origin, targetPosition, out Vector3 direction, out float maxDistance))
            {
                return false;
            }

            return IsFriendlyInSuppressionLaneCore(shooter, origin, direction, maxDistance);
        }

        public static bool IsFriendlyInAimLane(BotOwner shooter, Vector3 weaponFirePort, Vector3 aimDirection, float distance)
        {
            if (distance <= 0.05f || aimDirection.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 origin = GetFireOrigin(shooter, weaponFirePort);
            return IsFriendlyInShotLaneCore(
                shooter,
                origin,
                aimDirection.normalized,
                distance,
                AimLaneRadius,
                0f,
                1f,
                AimLaneDistancePadding);
        }

        public static bool IsFriendlyNearImpact(BotOwner shooter, Vector3 impactPosition, float unsafeRadius)
        {
            if (unsafeRadius <= 0f ||
                shooter?.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.realPlayer == null)
            {
                return false;
            }

            float unsafeRadiusSqr = unsafeRadius * unsafeRadius;
            if (boss.realPlayer.HealthController?.IsAlive == true &&
                (boss.realPlayer.Position - impactPosition).sqrMagnitude <= unsafeRadiusSqr)
            {
                return true;
            }

            var followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == shooter || follower.IsDead)
                {
                    continue;
                }

                Player player = follower.GetPlayer;
                if (player?.HealthController?.IsAlive == true &&
                    (player.Position - impactPosition).sqrMagnitude <= unsafeRadiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsFriendlyNearGrenadeImpact(
            BotOwner shooter,
            Vector3 impactPosition,
            float unsafeRadius,
            bool includeMovementPrediction,
            out string reason)
        {
            reason = string.Empty;
            if (unsafeRadius <= 0f ||
                shooter?.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.realPlayer == null ||
                !IsFinite(impactPosition))
            {
                return false;
            }

            if (IsPlayerNearGrenadeImpact(boss.realPlayer, impactPosition, unsafeRadius))
            {
                reason = "bossNearImpact";
                return true;
            }

            var followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == shooter || follower.IsDead)
                {
                    continue;
                }

                Player player = follower.GetPlayer;
                if (player?.HealthController?.IsAlive != true)
                {
                    continue;
                }

                if (IsPlayerNearGrenadeImpact(player, impactPosition, unsafeRadius))
                {
                    reason = $"allyNearImpact:{GetSafeName(follower)}";
                    return true;
                }

                if (includeMovementPrediction &&
                    IsFollowerMovementSegmentNearImpact(follower, impactPosition, unsafeRadius))
                {
                    reason = $"allyMovementCrossesImpact:{GetSafeName(follower)}";
                    return true;
                }
            }

            return false;
        }

        public static Vector3 GetGrenadeThrowOrigin(BotOwner shooter, BotGrenadeController grenades)
        {
            try
            {
                if (grenades != null && IsFinite(grenades.StartThrow))
                {
                    return grenades.StartThrow;
                }
            }
            catch
            {
                // Some vanilla controllers expose the throw origin through live player bones only.
            }

            try
            {
                Vector3 weaponRoot = shooter?.GetPlayer?.WeaponRoot.position ?? Vector3.zero;
                if (IsFinite(weaponRoot) && weaponRoot != Vector3.zero)
                {
                    return weaponRoot;
                }
            }
            catch
            {
                // Fall back to a stable body-height origin below.
            }

            return shooter != null && IsFinite(shooter.Position)
                ? shooter.Position + Vector3.up * GrenadeThrowOriginFallbackHeight
                : Vector3.zero;
        }

        public static bool IsRegularGrenadeTrajectoryUnsafeForThrower(
            BotOwner shooter,
            BotGrenadeController grenades,
            AIGreanageThrowData throwData,
            out string reason)
        {
            reason = string.Empty;
            if (shooter == null || grenades == null || throwData == null)
            {
                reason = "trajectoryMissing";
                return true;
            }

            Vector3 target = throwData.Target;
            if (!IsFinite(target))
            {
                reason = "trajectoryTargetMissing";
                return true;
            }

            if (IsPlayerNearGrenadeImpact(shooter.GetPlayer, target, RegularGrenadeUnsafeRadius))
            {
                reason = "throwerNearImpact";
                return true;
            }

            Vector3 origin = GetGrenadeThrowOrigin(shooter, grenades);
            if (!IsFinite(origin))
            {
                reason = "trajectoryOriginMissing";
                return true;
            }

            if (!TryGetGrenadeAngle(throwData.Ang, out AIGreandeAng angle))
            {
                reason = $"trajectoryAngleUnknown:{throwData.Ang:0.#}";
                return true;
            }

            AIGreanageThrowData currentOriginThrow = AIGrenadeHelper.CanThrowGrenade2(
                origin,
                target,
                grenades,
                angle,
                shooter.Settings.FileSettings.Grenade.MIN_THROW_GRENADE_DIST_SQRT);
            if (currentOriginThrow == null || !currentOriginThrow.CanThrow)
            {
                reason = $"trajectoryBlockedCurrentOrigin:{angle}";
                return true;
            }

            if (IsGrenadeArcNearOriginBlocked(origin, currentOriginThrow, out reason))
            {
                return true;
            }

            if (IsPredictedGrenadeDangerPointNearThrower(shooter, origin, currentOriginThrow, grenades, out reason))
            {
                return true;
            }

            return false;
        }

        private static bool IsPlayerNearGrenadeImpact(Player player, Vector3 impactPosition, float unsafeRadius)
        {
            if (player == null || !IsFinite(player.Position))
            {
                return false;
            }

            float unsafeRadiusSqr = unsafeRadius * unsafeRadius;
            return (player.Position - impactPosition).sqrMagnitude <= unsafeRadiusSqr;
        }

        private static bool IsGrenadeArcNearOriginBlocked(
            Vector3 origin,
            AIGreanageThrowData throwData,
            out string reason)
        {
            reason = string.Empty;
            Vector3 direction = throwData.Direction;
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.0001f)
            {
                reason = "trajectoryDirectionMissing";
                return true;
            }

            float targetDistance = (throwData.Target - origin).magnitude;
            float castDistance = Mathf.Min(GrenadeNearOriginClearanceDistance, targetDistance - 0.5f);
            if (castDistance <= 0.25f)
            {
                return false;
            }

            if (Physics.SphereCast(
                    origin,
                    GrenadeNearOriginClearanceRadius,
                    direction.normalized,
                    out RaycastHit hit,
                    castDistance,
                    LayersMaskController.HighPolyWithTerrainMask,
                    QueryTriggerInteraction.Ignore))
            {
                string hitName = hit.collider?.gameObject?.name ?? "geometry";
                reason = $"trajectoryNearOriginBlocked:{hitName}";
                return true;
            }

            return false;
        }

        private static bool IsPredictedGrenadeDangerPointNearThrower(
            BotOwner shooter,
            Vector3 origin,
            AIGreanageThrowData throwData,
            BotGrenadeController grenades,
            out string reason)
        {
            reason = string.Empty;
            Vector3 direction = throwData.Direction;
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            float mass = grenades.Mass > 0.01f ? grenades.Mass : 0.5f;
            Vector3 force = direction.normalized * (throwData.Force * mass);
            Vector3 dangerPoint = AIGrenadeHelper.FindDangerPoint(origin, force, mass);
            if (!IsFinite(dangerPoint))
            {
                return false;
            }

            if (IsPlayerNearGrenadeImpact(shooter.GetPlayer, dangerPoint, RegularGrenadeUnsafeRadius))
            {
                reason = "throwerNearPredictedImpact";
                return true;
            }

            return false;
        }

        private static bool TryGetGrenadeAngle(float angleDegrees, out AIGreandeAng angle)
        {
            switch (Mathf.RoundToInt(angleDegrees))
            {
                case 5:
                    angle = AIGreandeAng.ang5;
                    return true;
                case 15:
                    angle = AIGreandeAng.ang15;
                    return true;
                case 25:
                    angle = AIGreandeAng.ang25;
                    return true;
                case 35:
                    angle = AIGreandeAng.ang35;
                    return true;
                case 45:
                    angle = AIGreandeAng.ang45;
                    return true;
                case 55:
                    angle = AIGreandeAng.ang55;
                    return true;
                case 65:
                    angle = AIGreandeAng.ang65;
                    return true;
                default:
                    angle = AIGreandeAng.ang45;
                    return false;
            }
        }

        private static bool IsFollowerMovementSegmentNearImpact(
            BotOwner follower,
            Vector3 impactPosition,
            float unsafeRadius)
        {
            if (follower?.GoToSomePointData?.HaveTarget() != true ||
                follower.GoToSomePointData.IsCome() ||
                !IsFinite(follower.Position) ||
                !IsFinite(follower.GoToSomePointData.Point))
            {
                return false;
            }

            Vector3 from = follower.Position;
            Vector3 to = follower.GoToSomePointData.Point;
            if ((to - from).sqrMagnitude <= 0.25f)
            {
                return false;
            }

            float distanceSqr = DistancePointToSegmentXZSqr(impactPosition, from, to);
            return distanceSqr <= unsafeRadius * unsafeRadius;
        }

        private static float DistancePointToSegmentXZSqr(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(segmentStart.x, segmentStart.z);
            Vector2 b = new Vector2(segmentEnd.x, segmentEnd.z);
            Vector2 ab = b - a;
            float abSqr = ab.sqrMagnitude;
            if (abSqr <= 0.0001f)
            {
                return (p - a).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abSqr);
            Vector2 closest = a + ab * t;
            return (p - closest).sqrMagnitude;
        }

        private static string GetSafeName(BotOwner bot)
        {
            return bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown";
        }

        private static bool IsFinite(Vector3 value)
        {
            return !(float.IsNaN(value.x) ||
                     float.IsNaN(value.y) ||
                     float.IsNaN(value.z) ||
                     float.IsInfinity(value.x) ||
                     float.IsInfinity(value.y) ||
                     float.IsInfinity(value.z));
        }

        private static bool IsFriendlyInSuppressionLaneCore(BotOwner shooter, Vector3 origin, Vector3 direction, float maxDistance)
        {
            return IsFriendlyInShotLaneCore(
                shooter,
                origin,
                direction,
                maxDistance,
                SuppressionLaneRadius,
                SuppressionCloseFrontDistance,
                SuppressionCloseFrontDot,
                SuppressionDistancePadding);
        }

        private static bool IsFriendlyInShotLaneCore(
            BotOwner shooter,
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            float laneRadius,
            float closeFrontDistance,
            float closeFrontDot,
            float distancePadding)
        {
            if (shooter?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            if (IsPlayerInLane(shooter, boss.realPlayer, origin, direction, maxDistance, laneRadius, closeFrontDistance, closeFrontDot, distancePadding))
            {
                return true;
            }

            var followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == shooter || follower.IsDead || follower.GetPlayer == null)
                {
                    continue;
                }

                if (IsPlayerInLane(shooter, follower.GetPlayer, origin, direction, maxDistance, laneRadius, closeFrontDistance, closeFrontDot, distancePadding))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildLane(Vector3 origin, Vector3 targetPosition, out Vector3 direction, out float maxDistance)
        {
            direction = Vector3.zero;
            maxDistance = 0f;

            Vector3 toTarget = targetPosition - origin;
            maxDistance = toTarget.magnitude;
            if (maxDistance < 0.2f)
            {
                return false;
            }

            direction = toTarget / maxDistance;
            return true;
        }

        private static Vector3 GetFireOrigin(BotOwner shooter, Vector3 weaponFirePort)
        {
            if (weaponFirePort != Vector3.zero)
            {
                return weaponFirePort;
            }

            Vector3 origin = shooter.Position + Vector3.up * 1.2f;
            var root = shooter.GetPlayer?.PlayerBones?.WeaponRoot;
            if (root != null)
            {
                origin = root.position;
            }

            return origin;
        }

        private static bool IsPlayerInLane(
            BotOwner shooter,
            Player ally,
            Vector3 origin,
            Vector3 dir,
            float maxDistance,
            float laneRadius,
            float closeFrontDistance,
            float closeFrontDot,
            float distancePadding)
        {
            if (ally == null || ally.HealthController?.IsAlive != true || string.IsNullOrEmpty(ally.ProfileId))
            {
                return false;
            }

            if (string.Equals(ally.ProfileId, shooter.ProfileId, System.StringComparison.Ordinal))
            {
                return false;
            }

            Vector3 feet = ally.Transform.position;
            Vector3 torso = feet + Vector3.up * 1.0f;
            Vector3 head = feet + Vector3.up * 1.55f;

            return IsPointInLane(origin, dir, maxDistance, feet, laneRadius, closeFrontDistance, closeFrontDot, distancePadding) ||
                   IsPointInLane(origin, dir, maxDistance, torso, laneRadius, closeFrontDistance, closeFrontDot, distancePadding) ||
                   IsPointInLane(origin, dir, maxDistance, head, laneRadius, closeFrontDistance, closeFrontDot, distancePadding);
        }

        private static bool IsPointInLane(
            Vector3 origin,
            Vector3 dir,
            float maxDistance,
            Vector3 point,
            float laneRadius,
            float closeFrontDistance,
            float closeFrontDot,
            float distancePadding)
        {
            Vector3 toPoint = point - origin;
            float forward = Vector3.Dot(toPoint, dir);
            if (forward < 0f)
            {
                return false;
            }

            if (forward <= maxDistance + distancePadding)
            {
                Vector3 closest = origin + dir * forward;
                float lateral = (point - closest).magnitude;
                if (lateral <= laneRadius)
                {
                    return true;
                }
            }

            float dist = toPoint.magnitude;
            if (dist <= closeFrontDistance)
            {
                float dot = dist > 0.001f ? Vector3.Dot(toPoint / dist, dir) : 1f;
                if (dot >= closeFrontDot)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
