using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Provides the SAIN-style fire opportunity that sits beside a combat action instead of
    /// requiring shooting to win the high-level decision slot. It is intentionally limited to
    /// non-sprinting movement/holds and keeps follower friendly-fire checks as hard gates.
    /// </summary>
    internal sealed class FollowerCombatFireOverlay
    {
        private const float FireAlignmentAngle = 18f;
        private const float VisibleAimSmoothingSpeed = 12f;
        private const float VisibleAimSnapDistance = 4f;
        private const float VisibleAimResetSeconds = 0.75f;

        private sealed class SharedAimState
        {
            public string? EnemyProfileId;
            public Vector3 Position;
            public float UpdatedAt;
            public bool HasPosition;

            public void Reset()
            {
                EnemyProfileId = null;
                Position = Vector3.zero;
                UpdatedAt = 0f;
                HasPosition = false;
            }
        }

        private static readonly ConditionalWeakTable<BotOwner, SharedAimState> AimStates =
            new ConditionalWeakTable<BotOwner, SharedAimState>();

        private readonly BotOwner botOwner;
        private readonly SharedAimState aimState;
        private readonly FollowerEmergencyFireGate emergencyFireGate = new FollowerEmergencyFireGate();
        private string? lastRecordedState;
        private float nextRecordAt;

        public FollowerCombatFireOverlay(BotOwner botOwner)
        {
            this.botOwner = botOwner;
            aimState = AimStates.GetValue(botOwner, _ => new SharedAimState());
        }

        public bool Update(
            EnemyInfo? goalEnemy,
            string? actionReason,
            bool allowThreatSuppression,
            bool forceThreatLook,
            out bool firing)
        {
            firing = false;
            bool hasLiveGoalEnemy = goalEnemy?.Person?.HealthController?.IsAlive == true;
            if (!hasLiveGoalEnemy &&
                (!allowThreatSuppression ||
                 !FollowerAwareness.TryGetRecentThreatLookPoint(botOwner, out _)))
            {
                aimState.Reset();
                return false;
            }

            if (FollowerCombatActionBase.IsActuallySprinting(botOwner))
            {
                Stop("sprinting", actionReason, null);
                return true;
            }

            if (!TryGetFireTarget(
                    goalEnemy,
                    allowThreatSuppression,
                    out Vector3 target,
                    out bool suppression,
                    out string targetReason))
            {
                Stop(targetReason, actionReason, null, targetReason: targetReason);
                return false;
            }

            if (forceThreatLook)
            {
                botOwner.Steering.LookToPoint(target);
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            ShootData? shootData = botOwner.ShootData;
            if (weaponManager == null || shootData == null)
            {
                Stop("shootControllerMissing", actionReason, target, targetReason: targetReason);
                return true;
            }

            if (weaponManager.Reload?.Reloading == true)
            {
                Stop("reloading", actionReason, target, targetReason: targetReason);
                return true;
            }

            if (!weaponManager.HaveBullets)
            {
                Stop("noBullets", actionReason, target, targetReason: targetReason);
                return true;
            }

            if (!weaponManager.IsWeaponReady)
            {
                Stop("weaponNotReady", actionReason, target, targetReason: targetReason);
                return true;
            }

            if (!shootData.CanShootByState)
            {
                Stop("shootStateBlocked", actionReason, target, targetReason: targetReason);
                return true;
            }

            FollowerCombatActionBase.TryGetCurrentShotVector(
                botOwner,
                out Vector3 fireOrigin,
                out Vector3 aimDirection);
            if (suppression)
            {
                if (FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, target))
                {
                    Stop("friendlySuppressionLane", actionReason, target, targetReason: targetReason);
                    return true;
                }

                bool directLane = Utils.Utils.CanShootToTarget(
                    new ShootPointClass(target, 1f),
                    fireOrigin,
                    botOwner.LookSensor.Mask,
                    false);
                if (!directLane &&
                    !FollowerCombatCommon.IsSoftObstructedSuppressionLane(
                        fireOrigin,
                        target,
                        botOwner.LookSensor.Mask))
                {
                    Stop("hardBlockedSuppressionLane", actionReason, target, targetReason: targetReason);
                    return true;
                }
            }
            else if (FollowerShotSafety.IsFriendlyInShotLane(botOwner, fireOrigin, target))
            {
                Stop("friendlyShotLane", actionReason, target, targetReason: targetReason);
                return true;
            }

            IBotAiming? aiming = botOwner.AimingManager?.CurrentAiming;
            if (aiming == null)
            {
                Stop("aimControllerMissing", actionReason, target, targetReason: targetReason);
                return true;
            }

            botOwner.Steering.LookToPoint(target);
            aiming.SetTarget(target);
            botOwner.AimingManager.NodeUpdate();

            FollowerCombatActionBase.TryGetCurrentShotVector(botOwner, out fireOrigin, out aimDirection);
            Vector3 targetDirection = target - fireOrigin;
            if (aimDirection.sqrMagnitude <= 0.0001f || targetDirection.sqrMagnitude <= 0.0001f)
            {
                Stop("aimDirectionMissing", actionReason, target, targetReason: targetReason);
                return true;
            }

            float targetDistance = targetDirection.magnitude;
            if (FollowerShotSafety.IsFriendlyInAimLane(
                    botOwner,
                    fireOrigin,
                    aimDirection,
                    targetDistance))
            {
                Stop("friendlyAimLane", actionReason, target, targetReason: targetReason);
                return true;
            }

            float aimAngle = Vector3.Angle(aimDirection, targetDirection);
            if (!aiming.IsReady || aimAngle > FireAlignmentAngle)
            {
                if (!aiming.IsReady &&
                    emergencyFireGate.TryFire(
                        botOwner,
                        goalEnemy,
                        target,
                        "fireOverlay",
                        actionReason,
                        suppression,
                        out firing))
                {
                    return true;
                }

                Stop(
                    !aiming.IsReady ? "aimNotReady" : "aimNotAligned",
                    actionReason,
                    target,
                    stopTrigger: true,
                    aimAngle: aimAngle,
                    targetReason: targetReason);
                return true;
            }

            bool alreadyShooting = shootData.Shooting;
            emergencyFireGate.Reset();
            bool shootStarted = alreadyShooting || shootData.Shoot();
            firing = shootData.Shooting || shootStarted;
            Record(
                firing ? (alreadyShooting ? "firing" : "shootStarted") : "shootRejected",
                actionReason,
                target,
                suppression,
                shootRequested: !alreadyShooting,
                shootStarted,
                aimAngle,
                targetReason);
            return true;
        }

        public void Stop(string reason = "actionStop")
        {
            emergencyFireGate.Reset();
            Stop(reason, null, null);
        }

        private bool TryGetFireTarget(
            EnemyInfo? goalEnemy,
            bool allowThreatSuppression,
            out Vector3 target,
            out bool suppression,
            out string reason)
        {
            if (goalEnemy?.Person?.HealthController?.IsAlive == true &&
                goalEnemy.IsVisible &&
                goalEnemy.CanShoot)
            {
                ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(false);
                Vector3 rawTarget = shootPoint?.Point ?? goalEnemy.GetBodyPartPosition();
                target = StabilizeVisibleTarget(goalEnemy, rawTarget);
                suppression = false;
                reason = "visibleEnemy";
                return IsFinite(target);
            }

            suppression = true;
            if (!allowThreatSuppression)
            {
                target = Vector3.zero;
                reason = "suppressionDisabled";
                return false;
            }

            if (FollowerAwareness.TryGetRecentThreatLookPoint(botOwner, out target) && IsFinite(target))
            {
                reason = "recentIncomingThreat";
                return true;
            }

            if (goalEnemy != null &&
                FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                target = FollowerImmediateFirePolicy.GetRecentContactSuppressTarget(goalEnemy);
                reason = "recentEnemyContact";
                return IsFinite(target);
            }

            target = Vector3.zero;
            reason = "noCredibleSuppressTarget";
            return false;
        }

        private Vector3 StabilizeVisibleTarget(EnemyInfo goalEnemy, Vector3 rawTarget)
        {
            if (!IsFinite(rawTarget))
            {
                return rawTarget;
            }

            string enemyProfileId = goalEnemy.ProfileId ?? goalEnemy.Person?.ProfileId ?? string.Empty;
            float now = Time.time;
            bool changedEnemy = !string.Equals(aimState.EnemyProfileId, enemyProfileId, StringComparison.Ordinal);
            bool stale = aimState.UpdatedAt <= 0f || now - aimState.UpdatedAt > VisibleAimResetSeconds;
            bool jumped = aimState.HasPosition &&
                          (rawTarget - aimState.Position).sqrMagnitude > VisibleAimSnapDistance * VisibleAimSnapDistance;
            if (!aimState.HasPosition || changedEnemy || stale || jumped)
            {
                aimState.EnemyProfileId = enemyProfileId;
                aimState.Position = rawTarget;
                aimState.UpdatedAt = now;
                aimState.HasPosition = true;
                return rawTarget;
            }

            float deltaTime = Mathf.Clamp(now - aimState.UpdatedAt, 0f, 0.1f);
            float blend = 1f - Mathf.Exp(-VisibleAimSmoothingSpeed * deltaTime);
            aimState.Position = Vector3.Lerp(aimState.Position, rawTarget, blend);
            aimState.UpdatedAt = now;
            return aimState.Position;
        }

        private void Stop(
            string gate,
            string? actionReason,
            Vector3? target,
            bool stopTrigger = true,
            float? aimAngle = null,
            string? targetReason = null)
        {
            if (stopTrigger)
            {
                botOwner.ShootData?.EndShoot();
                botOwner.WeaponManager?.ShootController?.SetTriggerPressed(false);
            }

            Record(
                gate,
                actionReason,
                target,
                suppression: targetReason != "visibleEnemy",
                shootRequested: false,
                shootStarted: false,
                aimAngle,
                targetReason);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void Record(
            string state,
            string? actionReason,
            Vector3? target,
            bool suppression,
            bool shootRequested,
            bool shootStarted,
            float? aimAngle,
            string? targetReason)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            string key = $"{state}:{actionReason}:{targetReason}";
            if (string.Equals(lastRecordedState, key, System.StringComparison.Ordinal) &&
                Time.time < nextRecordAt)
            {
                return;
            }

            lastRecordedState = key;
            nextRecordAt = Time.time + 1f;
            BattleRecorder.RecordCombatFireEvent(
                botOwner,
                "fireOverlay",
                actionReason,
                state,
                targetReason,
                suppression,
                shootRequested,
                shootStarted,
                aimAngle,
                target);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
