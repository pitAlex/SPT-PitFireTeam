using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Narrow fail-safe for EFT aim controllers that remain not-ready while a close, aligned enemy
    /// is actively wounding the follower. ShootData still owns cadence, weapon state, recoil, and
    /// trigger behavior; this gate only bypasses the stuck IBotAiming.IsReady boolean after a
    /// continuous settle period and after repeating all follower friendly-fire checks.
    /// </summary>
    internal sealed class FollowerEmergencyFireGate
    {
        private const float MaxThreatDistance = 25f;
        private const float MaxAimAngle = 18f;
        private const float AimReadyBypassDelaySeconds = 0.75f;
        private const float RecordIntervalSeconds = 0.5f;

        private string? enemyProfileId;
        private float alignedNotReadySince;
        private float nextRecordAt;

        public void Reset()
        {
            enemyProfileId = null;
            alignedNotReadySince = 0f;
        }

        public bool TryFire(
            BotOwner botOwner,
            EnemyInfo? goalEnemy,
            Vector3 target,
            string action,
            string? reason,
            bool suppression,
            out bool firing)
        {
            firing = false;
            if (!CanAttempt(botOwner, goalEnemy, target))
            {
                Reset();
                return false;
            }

            IBotAiming? aiming = botOwner.AimingManager?.CurrentAiming;
            if (aiming == null || aiming.IsReady)
            {
                Reset();
                return false;
            }

            string currentEnemyId = goalEnemy!.ProfileId ?? string.Empty;
            if (!string.Equals(enemyProfileId, currentEnemyId, StringComparison.Ordinal))
            {
                enemyProfileId = currentEnemyId;
                alignedNotReadySince = 0f;
            }

            botOwner.Steering.LookToPoint(target);
            aiming.SetTarget(target);
            botOwner.AimingManager.NodeUpdate();

            if (!FollowerCombatActionBase.TryGetCurrentShotVector(
                    botOwner,
                    out Vector3 fireOrigin,
                    out Vector3 aimDirection))
            {
                Reset();
                return false;
            }

            Vector3 targetDirection = target - fireOrigin;
            if (targetDirection.sqrMagnitude <= 0.0001f)
            {
                Reset();
                return false;
            }

            float aimAngle = Vector3.Angle(aimDirection, targetDirection);
            if (aimAngle > MaxAimAngle)
            {
                alignedNotReadySince = 0f;
                return false;
            }

            float targetDistance = targetDirection.magnitude;
            bool friendlyLane = suppression
                ? FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, target)
                : FollowerShotSafety.IsFriendlyInShotLane(botOwner, fireOrigin, target);
            if (friendlyLane ||
                FollowerShotSafety.IsFriendlyInAimLane(
                    botOwner,
                    fireOrigin,
                    aimDirection,
                    targetDistance))
            {
                Reset();
                return false;
            }

            if (alignedNotReadySince <= 0f)
            {
                alignedNotReadySince = Time.time;
                return false;
            }

            if (Time.time - alignedNotReadySince < AimReadyBypassDelaySeconds)
            {
                return false;
            }

            ShootData? shootData = botOwner.ShootData;
            bool alreadyShooting = shootData?.Shooting == true;
            bool shootStarted = alreadyShooting || shootData?.Shoot() == true;
            firing = shootData?.Shooting == true || shootStarted;
            Record(botOwner, action, reason, suppression, !alreadyShooting, shootStarted, aimAngle, target);
            return true;
        }

        private static bool CanAttempt(BotOwner botOwner, EnemyInfo? goalEnemy, Vector3 target)
        {
            if (goalEnemy?.Person?.HealthController?.IsAlive != true ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                goalEnemy.Distance > MaxThreatDistance ||
                !FollowerCombatCommon.IsFinite(target) ||
                FollowerCombatActionBase.IsActuallySprinting(botOwner))
            {
                return false;
            }

            bool activePressure = botOwner.Memory?.IsUnderFire == true ||
                                  FollowerCombatCommon.WasHitRecently(botOwner, 1.5f) ||
                                  FollowerAwareness.WasRecentlyDamaged(botOwner) ||
                                  FollowerAwareness.WasRecentlyThreatened(botOwner);
            if (!activePressure)
            {
                return false;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            return weaponManager?.Reload?.Reloading != true &&
                   weaponManager?.HaveBullets == true &&
                   weaponManager.IsWeaponReady &&
                   botOwner.ShootData?.CanShootByState == true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void Record(
            BotOwner botOwner,
            string action,
            string? reason,
            bool suppression,
            bool shootRequested,
            bool shootStarted,
            float aimAngle,
            Vector3 target)
        {
            if (Time.time < nextRecordAt)
            {
                return;
            }

            nextRecordAt = Time.time + RecordIntervalSeconds;
            BattleRecorder.RecordCombatFireEvent(
                botOwner,
                action,
                reason,
                "emergencyAimReadyBypass",
                "closeVisiblePressure",
                suppression,
                shootRequested,
                shootStarted,
                aimAngle,
                target);
        }
    }
}
