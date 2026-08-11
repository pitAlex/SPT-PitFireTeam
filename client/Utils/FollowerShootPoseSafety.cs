using EFT;
using pitTeam.BigBrain;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class FollowerShootPoseSafety
    {
        // Vanilla's crouch check uses a low body-origin lane. A waist-high obstruction can make
        // crouch unsafe even when standing is clear, so followers require both low and weapon-ish
        // crouch probes before allowing the shoot-from-place node to choose crouch.
        private const float VanillaCrouchProbeHeight = 0.6f;
        private const float CrouchWeaponProbeHeight = 0.95f;
        private const float MinCombatCrouchFireDistance = 50f;

        public static bool CanUseCombatCrouchFire(
            BotOwner botOwner,
            Vector3 target,
            out string reason,
            out float enemyDistance)
        {
            reason = "allowed";
            enemyDistance = botOwner?.Memory?.GoalEnemy?.Distance ??
                            (botOwner != null ? Vector3.Distance(botOwner.Position, target) : 0f);

            EnemyInfo? goalEnemy = botOwner?.Memory?.GoalEnemy;
            if (botOwner == null ||
                goalEnemy?.Person?.HealthController?.IsAlive != true ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot)
            {
                reason = "enemyNotShootable";
                return false;
            }

            if (enemyDistance < MinCombatCrouchFireDistance)
            {
                reason = "enemyTooClose";
                return false;
            }

            if (botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 1.25f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner))
            {
                reason = "damagePressure";
                return false;
            }

            if (!botOwner.Memory.IsInCover && FollowerAwareness.WasRecentlyThreatened(botOwner))
            {
                reason = "exposedThreat";
                return false;
            }

            if (!HasReliableCrouchLane(botOwner, target))
            {
                reason = "blockedLane";
                return false;
            }

            return true;
        }

        public static bool HasReliableCrouchLane(BotOwner botOwner, Vector3 target)
        {
            return HasReliablePoseLane(botOwner, target, VanillaCrouchProbeHeight) &&
                   HasReliablePoseLane(botOwner, target, CrouchWeaponProbeHeight);
        }

        public static bool HasReliablePoseLane(BotOwner botOwner, Vector3 target, float probeHeight)
        {
            if (botOwner == null || !IsFinite(target))
            {
                return false;
            }

            Vector3 origin = botOwner.Position + Vector3.up * probeHeight;
            LayerMask mask = botOwner.LookSensor != null
                ? botOwner.LookSensor.Mask
                : LayerMaskClass.HighPolyWithTerrainMask;

            if (!HasExactClearLine(origin, target, mask))
            {
                return false;
            }

            return botOwner.ShootData == null ||
                   !botOwner.ShootData.CheckFriendlyFire(origin, target);
        }

        private static bool HasExactClearLine(Vector3 origin, Vector3 target, LayerMask mask)
        {
            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            return !Physics.Raycast(new Ray(origin, direction), distance, mask);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
