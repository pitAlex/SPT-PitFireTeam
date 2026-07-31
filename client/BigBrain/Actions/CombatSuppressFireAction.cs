using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Suppressive-fire action. It fires at the suppress target selected by the decision/objective,
    /// optionally moves to a suppress-from point, corrects close threat aim, and stops shooting when
    /// the suppress task is complete or unsafe.
    /// </summary>
    internal sealed class CombatSuppressFireAction : FollowerCombatActionBase
    {
        private const float CloseThreatSuppressCorrectionDistance = 18f;
        private const float CloseThreatSuppressFireAlignmentDistance = 6f;
        private const float CloseThreatSuppressFireMaxAngle = 35f;
        private const float SuppressPointCorrectionAngle = 25f;
        private const float LauncherSuppressFireMaxAimAngle = 12f;
        private const float WeaponSuppressFireMaxAimAngle = 18f;

        private readonly GClass281 baseLogic;
        private string? lastLauncherSuppressSafetyRejectReason;
        private float nextLauncherSuppressSafetyRejectAt;
        private float nextLauncherSuppressAimHoldRecordAt;
        private float nextLauncherSuppressShootRecordAt;

        public CombatSuppressFireAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass281(botOwner);
        }

        public override void Stop()
        {
            StopCombatShooting();
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                StopCombatShooting();
                return;
            }

            string? reason = ResolveSuppressReason(data);
            if (StopUnownedGrenadeLauncherFire(reason, goalEnemy))
            {
                return;
            }

            // Follower suppress reasons use the mod-owned target and optional suppress-from point
            // instead of the vanilla node selecting its own target.
            if (IsFollowerSuppressActive(reason))
            {
                UpdateFollowerSuppress(reason);
                return;
            }

            // Recent-contact suppression is a very short continuity window after losing direct LOS.
            // It keeps the bot firing at a fresh last-seen point without becoming blind-fire movement.
            if (FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                Vector3 target = FollowerImmediateFirePolicy.GetRecentContactSuppressTarget(goalEnemy);
                Vector3 fireOrigin = BotOwner.WeaponRoot != null
                    ? BotOwner.WeaponRoot.position
                    : BotOwner.Position + Vector3.up * 1.2f;

                BotOwner.Steering.LookToPoint(target);
                if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target))
                {
                    StopCombatShooting();
                    return;
                }

                if (IsCurrentSuppressionAimUnsafe(fireOrigin, target))
                {
                    StopCombatShooting();
                    return;
                }

                if (ShouldHoldSuppressFireUntilAimed(fireOrigin, target))
                {
                    return;
                }

                BotOwner.ShootData.Shoot();
                return;
            }

            // The vanilla suppress node can still fire through squadmates, so keep friendly lane
            // safety as the final hard gate before delegating to it.
            if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                StopCombatShooting();
                return;
            }

            if (IsCurrentSuppressionAimUnsafe(BotOwner.WeaponRoot != null
                    ? BotOwner.WeaponRoot.position
                    : BotOwner.Position + Vector3.up * 1.2f,
                goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                StopCombatShooting();
                return;
            }

            Vector3 suppressTarget = GetRawData(data) is GClass27 suppressData && suppressData.PointToShoot.HasValue
                ? suppressData.PointToShoot.Value
                : BotOwner.SuppressShoot?.GetPoint() ?? goalEnemy.CurrPosition;
            if (ShouldHoldSuppressFireUntilAimed(
                    BotOwner.WeaponRoot != null ? BotOwner.WeaponRoot.position : BotOwner.Position + Vector3.up * 1.2f,
                    suppressTarget))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }

        private string? ResolveSuppressReason(CustomLayer.ActionData data)
        {
            string? dataReason = GetReason(data);
            string? lastReason = BotOwner.Brain?.Agent?.LastResult().Reason;
            if (FollowerCombatCommon.IsFollowerSuppressReason(dataReason))
            {
                return dataReason;
            }

            if (FollowerCombatCommon.IsFollowerSuppressReason(lastReason))
            {
                return lastReason;
            }

            return dataReason ?? lastReason;
        }

        private bool IsFollowerSuppressActive(string? reason)
        {
            if (!FollowerCombatCommon.IsFollowerSuppressReason(reason))
            {
                return false;
            }

            if (FollowerCombatCommon.IsAutoSuppressReason(reason) ||
                FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason) ||
                FollowerCombatGrenadierObjective.IsGrenadierReason(reason))
            {
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }

        private void UpdateFollowerSuppress(string? reason)
        {
            bool launcherSuppress = FollowerCombatCommon.IsGrenadeLauncherSuppressReason(reason);
            float launcherUnsafeRadius = launcherSuppress ? GetLauncherSuppressUnsafeRadius(reason) : 0f;
            Vector3? target = BotOwner.SuppressShoot?.GetPoint();
            if (!target.HasValue)
            {
                StopCombatShooting();
                return;
            }

            // If the stored suppress point is off to the side but the close enemy is actively
            // looking at the follower, correct toward the real threat when the lane is clean.
            if (!launcherSuppress)
            {
                target = CorrectCloseThreatSuppressPoint(target.Value);
            }

            bool standingSuppress = IsStandingSuppressReason(reason);
            if (launcherSuppress || standingSuppress)
            {
                BotOwner.SetPose(1f);
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 launcherFireOrigin = fireOrigin;
            Vector3 aimTarget = launcherSuppress
                ? GetLauncherSuppressAimPoint(launcherFireOrigin, target.Value)
                : target.Value;
            BotOwner.Steering.LookToPoint(aimTarget);
            float effectiveLauncherUnsafeRadius = launcherSuppress
                ? FollowerCombatCommon.GetGrenadeLauncherImpactUnsafeRadius(fireOrigin, target.Value, launcherUnsafeRadius)
                : launcherUnsafeRadius;

            if (launcherSuppress && FollowerShotSafety.IsFriendlyNearImpact(BotOwner, target.Value, effectiveLauncherUnsafeRadius))
            {
                RecordLauncherSuppressSafetyReject($"{reason}:launcherImpactUnsafe", target.Value);
                StopCombatShooting();
                return;
            }

            if (launcherSuppress)
            {
                if (!CanLauncherSuppressFromCurrentOrStandingPosition(
                        fireOrigin,
                        target.Value,
                        effectiveLauncherUnsafeRadius,
                        out string initialLauncherLaneRejectReason,
                        out launcherFireOrigin))
                {
                    RecordLauncherSuppressSafetyReject($"{reason}:{initialLauncherLaneRejectReason}", target.Value);
                    StopCombatShooting();
                    return;
                }

                aimTarget = GetLauncherSuppressAimPoint(launcherFireOrigin, target.Value);
                BotOwner.Steering.LookToPoint(aimTarget);
            }
            else if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target.Value))
            {
                StopCombatShooting();
                return;
            }

            Vector3 activeFireOrigin = launcherSuppress ? launcherFireOrigin : fireOrigin;
            if (IsCurrentSuppressionAimUnsafe(activeFireOrigin, aimTarget, launcherSuppress))
            {
                if (launcherSuppress)
                {
                    RecordLauncherSuppressAimHold($"{reason}:launcherAimNotAligned", target.Value);
                    BotOwner.Steering.LookToPoint(aimTarget);
                    BotOwner.SetPose(1f);
                    return;
                }

                StopCombatShooting();
                return;
            }

            if (ShouldHoldCloseThreatSuppressFire(target.Value))
            {
                StopCombatShooting();
                return;
            }

            CustomNavigationPoint suppressFrom = BotOwner.SuppressShoot?.PointToSuppressFrom;
            if (launcherSuppress && !IsGrenadeLauncherSelectedForSuppress())
            {
                HoldLauncherSuppressPosition(aimTarget, suppressFrom);

                if (TryRestoreGrenadeLauncherSuppressSelection())
                {
                    return;
                }

                if (BotOwner?.WeaponManager?.Selector?.IsWeaponReady == false)
                {
                    return;
                }

                StopCombatShooting();
                return;
            }

            if (suppressFrom != null && !HasReachedSuppressFromPoint(suppressFrom))
            {
                // Suppression does not wait until arrival. The bot keeps shooting while moving to
                // a better suppress-from point when the objective prepared one, but only if the
                // current moving lane is clear or soft-obstructed. If the current lane is a wall,
                // move first and start firing once the suppress-from point gives a real lane.
                BotOwner.Steering.LookToPoint(aimTarget);
                BotOwner.GoToSomePointData.SetPoint(suppressFrom.Position);
                BotOwner.GoToSomePointData.UpdateToGo(true);
                if (launcherSuppress)
                {
                    StopCombatShooting();
                    return;
                }

                if (ShouldHoldCloseThreatSuppressFire(target.Value))
                {
                    StopCombatShooting();
                    return;
                }

                if (!CanSuppressFromCurrentPosition(fireOrigin, target.Value))
                {
                    StopCombatShooting();
                    return;
                }

                if (IsCurrentSuppressionAimUnsafe(fireOrigin, aimTarget, launcherSuppress))
                {
                    if (launcherSuppress)
                    {
                        RecordLauncherSuppressAimHold($"{reason}:launcherAimNotAligned", target.Value);
                    }

                    StopCombatShooting();
                    return;
                }

                if (ShouldHoldSuppressFireUntilAimed(fireOrigin, aimTarget, launcherSuppress))
                {
                    return;
                }

                if (ShouldAbortFinalSuppressShot(reason, fireOrigin, target.Value, launcherSuppress, launcherUnsafeRadius))
                {
                    return;
                }

                BotOwner.ShootData.Shoot();
                return;
            }

            if (suppressFrom != null)
            {
                BotOwner.StopMove();
            }

            if (ShouldHoldCloseThreatSuppressFire(target.Value))
            {
                StopCombatShooting();
                return;
            }

            if (standingSuppress && !CanSuppressFromCurrentPosition(fireOrigin, target.Value))
            {
                StopCombatShooting();
                return;
            }

            if (launcherSuppress &&
                !CanLauncherSuppressFromCurrentOrStandingPosition(
                    fireOrigin,
                    target.Value,
                    effectiveLauncherUnsafeRadius,
                    out string readyLauncherLaneRejectReason,
                    out launcherFireOrigin))
            {
                RecordLauncherSuppressSafetyReject($"{reason}:{readyLauncherLaneRejectReason}", target.Value);
                StopCombatShooting();
                return;
            }
            if (launcherSuppress)
            {
                aimTarget = GetLauncherSuppressAimPoint(launcherFireOrigin, target.Value);
                activeFireOrigin = launcherFireOrigin;
                BotOwner.Steering.LookToPoint(aimTarget);
            }

            if (launcherSuppress)
            {
                if (ShouldHoldEmptyLauncherSuppress())
                {
                    StopCombatShooting();
                    return;
                }

                if (ShouldHoldSuppressFireUntilAimed(activeFireOrigin, aimTarget, launcherSuppress))
                {
                    return;
                }

                if (ShouldAbortFinalSuppressShot(reason, activeFireOrigin, target.Value, launcherSuppress, effectiveLauncherUnsafeRadius))
                {
                    return;
                }

                FireLauncherSuppressShot(reason, target.Value, aimTarget);
                return;
            }

            if (ShouldHoldSuppressFireUntilAimed(fireOrigin, target.Value))
            {
                return;
            }

            if (ShouldAbortFinalSuppressShot(reason, fireOrigin, target.Value, launcherSuppress, launcherUnsafeRadius))
            {
                return;
            }

            BotOwner.ShootData?.Shoot();
        }

        private void HoldLauncherSuppressPosition(Vector3 target, CustomNavigationPoint suppressFrom)
        {
            BotOwner.Steering.LookToPoint(target);
            BotOwner.SetPose(1f);
            if (suppressFrom != null && !HasReachedSuppressFromPoint(suppressFrom))
            {
                BotOwner.GoToSomePointData.SetPoint(suppressFrom.Position);
                BotOwner.GoToSomePointData.UpdateToGo(true);
                return;
            }

            BotOwner.StopMove();
        }

        private bool IsGrenadeLauncherSelectedForSuppress()
        {
            return FollowerCombatCommon.IsEquippedGrenadeLauncherSelectedAndActive(BotOwner);
        }

        private bool TryRestoreGrenadeLauncherSuppressSelection()
        {
            return FollowerCombatCommon.TrySelectEquippedGrenadeLauncher(
                BotOwner,
                out _,
                out _);
        }

        private Vector3 GetLauncherSuppressAimPoint(Vector3 fireOrigin, Vector3 impactTarget)
        {
            return FollowerCombatCommon.GetGrenadeLauncherSuppressAimPoint(BotOwner, fireOrigin, impactTarget);
        }

        private bool ShouldHoldEmptyLauncherSuppress()
        {
            BotWeaponManager? weaponManager = BotOwner?.WeaponManager;
            if (weaponManager == null)
            {
                return false;
            }

            if (weaponManager.Reload?.Reloading == true)
            {
                return true;
            }

            Weapon? activeWeapon = weaponManager.ShootController?.Item ?? weaponManager.CurrentWeapon;
            if (!FollowerCombatCommon.IsGrenadeLauncherWeapon(activeWeapon) ||
                FollowerCombatCommon.IsSingleUseLauncherWeapon(activeWeapon) ||
                FollowerCombatCommon.CountLoadedRounds(activeWeapon) > 0)
            {
                return false;
            }

            FollowerCombatCommon.TryStartActiveGrenadeLauncherLooseAmmoReload(
                BotOwner,
                activeWeapon,
                out _);

            return true;
        }

        private static float GetLauncherSuppressUnsafeRadius(string? reason)
        {
            return FollowerCombatCommon.IsAutonomousSuppressReason(reason) ? 18f : 12f;
        }

        private static bool IsStandingSuppressReason(string? reason)
        {
            return reason != null &&
                   reason.IndexOf(".stand", System.StringComparison.Ordinal) >= 0;
        }

        private bool ShouldAbortFinalSuppressShot(
            string? reason,
            Vector3 fireOrigin,
            Vector3 target,
            bool launcherSuppress,
            float launcherUnsafeRadius)
        {
            if (launcherSuppress && FollowerShotSafety.IsFriendlyNearImpact(BotOwner, target, launcherUnsafeRadius))
            {
                RecordLauncherSuppressSafetyReject($"{reason}:launcherImpactUnsafeFinal", target);
                StopCombatShooting();
                return true;
            }

            if (launcherSuppress)
            {
                if (!CanLauncherSuppressFromCurrentOrStandingPosition(
                        fireOrigin,
                        target,
                        launcherUnsafeRadius,
                        out string launcherLaneRejectReason))
                {
                    RecordLauncherSuppressSafetyReject($"{reason}:launcherLaneUnsafeFinal:{launcherLaneRejectReason}", target);
                    StopCombatShooting();
                    return true;
                }

                return false;
            }

            if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target))
            {
                StopCombatShooting();
                return true;
            }

            return false;
        }

        private bool IsCurrentSuppressionAimUnsafe(
            Vector3 fireOrigin,
            Vector3 suppressTarget,
            bool launcherSuppress = false)
        {
            Vector3 aimDirection = BotOwner.LookDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            float distance = Vector3.Distance(fireOrigin, suppressTarget);
            if (launcherSuppress)
            {
                return IsLauncherAimNotAligned(fireOrigin, suppressTarget, aimDirection);
            }

            return FollowerShotSafety.IsFriendlyInAimLane(BotOwner, fireOrigin, aimDirection, distance);
        }

        private bool ShouldHoldSuppressFireUntilAimed(
            Vector3 fireOrigin,
            Vector3 suppressTarget,
            bool launcherSuppress = false)
        {
            BotOwner.Steering.LookToPoint(suppressTarget);

            IBotAiming aiming = BotOwner.AimingManager?.CurrentAiming;
            if (aiming == null)
            {
                StopCombatShooting();
                return true;
            }

            aiming.SetTarget(suppressTarget);
            BotOwner.AimingManager.NodeUpdate();

            Vector3 aimDirection = launcherSuppress
                ? BotOwner.LookDirection
                : BotOwner.WeaponRoot != null
                    ? BotOwner.WeaponRoot.forward
                    : BotOwner.LookDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= 0.0001f)
            {
                StopCombatShooting();
                return true;
            }

            float maxAngle = launcherSuppress ? LauncherSuppressFireMaxAimAngle : WeaponSuppressFireMaxAimAngle;
            bool aimNotAligned = IsSuppressAimNotAligned(fireOrigin, suppressTarget, aimDirection, maxAngle);
            if (launcherSuppress)
            {
                if (aimNotAligned)
                {
                    return true;
                }

                BotWeaponManager? weaponManager = BotOwner?.WeaponManager;
                bool weaponNotReady = weaponManager?.IsWeaponReady == false || weaponManager?.Reload?.Reloading == true;
                if (weaponNotReady)
                {
                    RecordLauncherSuppressAimHold(
                        $"launcherWeaponNotReady:ready={weaponManager?.IsWeaponReady}:reloading={weaponManager?.Reload?.Reloading}",
                        suppressTarget);
                }

                return weaponNotReady;
            }

            if (!aiming.IsReady || aimNotAligned)
            {
                StopCombatShooting();
                return true;
            }

            return false;
        }

        private static bool IsLauncherAimNotAligned(Vector3 fireOrigin, Vector3 suppressTarget, Vector3 aimDirection)
        {
            return IsSuppressAimNotAligned(fireOrigin, suppressTarget, aimDirection, LauncherSuppressFireMaxAimAngle);
        }

        private static bool IsSuppressAimNotAligned(
            Vector3 fireOrigin,
            Vector3 suppressTarget,
            Vector3 aimDirection,
            float maxAngle)
        {
            Vector3 targetDirection = suppressTarget - fireOrigin;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(aimDirection.normalized, targetDirection.normalized) > maxAngle;
        }

        private void RecordLauncherSuppressSafetyReject(string reason, Vector3 target)
        {
            if (string.Equals(lastLauncherSuppressSafetyRejectReason, reason, System.StringComparison.Ordinal) &&
                Time.time < nextLauncherSuppressSafetyRejectAt)
            {
                return;
            }

            lastLauncherSuppressSafetyRejectReason = reason;
            nextLauncherSuppressSafetyRejectAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherReject",
                reason,
                goalEnemy: BotOwner.Memory?.GoalEnemy,
                target: target);
        }

        private void RecordLauncherSuppressAimHold(string reason, Vector3 target)
        {
            if (Time.time < nextLauncherSuppressAimHoldRecordAt)
            {
                return;
            }

            nextLauncherSuppressAimHoldRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherHold",
                reason,
                goalEnemy: BotOwner.Memory?.GoalEnemy,
                target: target);
        }

        private bool CanSuppressFromCurrentPosition(Vector3 fireOrigin, Vector3 target, bool requireDirectLane = false)
        {
            if (Utils.Utils.CanShootToTarget(
                    new ShootPointClass(target, 1f),
                    fireOrigin,
                    BotOwner.LookSensor.Mask,
                    false))
            {
                return true;
            }

            if (requireDirectLane)
            {
                return false;
            }

            return FollowerCombatCommon.IsSoftObstructedSuppressionLane(fireOrigin, target, BotOwner.LookSensor.Mask);
        }

        private bool CanLauncherSuppressFromCurrentOrStandingPosition(
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius,
            out string rejectReason)
        {
            return CanLauncherSuppressFromCurrentOrStandingPosition(
                fireOrigin,
                target,
                unsafeRadius,
                out rejectReason,
                out _);
        }

        private bool CanLauncherSuppressFromCurrentOrStandingPosition(
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius,
            out string rejectReason,
            out Vector3 acceptedFireOrigin)
        {
            return FollowerCombatCommon.TryCanFireGrenadeLauncherAtTarget(
                BotOwner,
                fireOrigin,
                target,
                unsafeRadius,
                out rejectReason,
                out acceptedFireOrigin);
        }

        private void FireLauncherSuppressShot(string? reason, Vector3 target, Vector3 aimTarget)
        {
            BotWeaponManager? weaponManager = BotOwner?.WeaponManager;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            bool normalShoot = BotOwner?.ShootData?.Shoot() == true;
            bool haveBullets = weaponManager?.HaveBullets == true;
            int loadedRounds = FollowerCombatCommon.CountLoadedRounds(activeWeapon);
            float aimRaise = Mathf.Max(0f, aimTarget.y - target.y);

            if (!FollowerCombatCommon.IsGrenadeLauncherWeapon(activeWeapon) ||
                loadedRounds <= 0 ||
                haveBullets && normalShoot)
            {
                RecordLauncherSuppressShootAttempt(
                    $"{reason}:launcherShootNormal={normalShoot}:loaded={loadedRounds}:haveBullets={haveBullets}:aimRaise={aimRaise:0.0}",
                    target);
                return;
            }

            if (weaponManager?.ShootController == null ||
                weaponManager.IsWeaponReady == false ||
                weaponManager.Reload?.Reloading == true ||
                BotOwner?.ShootData?.CanShootByState == false)
            {
                RecordLauncherSuppressShootAttempt(
                    $"{reason}:launcherShootBlocked:normal={normalShoot}:loaded={loadedRounds}:haveBullets={haveBullets}:aimRaise={aimRaise:0.0}:ready={weaponManager?.IsWeaponReady}:reloading={weaponManager?.Reload?.Reloading}:state={BotOwner?.ShootData?.CanShootByState}",
                    target);
                return;
            }

            weaponManager.ShootController.IsInLauncherMode();
            weaponManager.ShootController.SetTriggerPressed(true);
            BotOwner.AimingManager?.CurrentAiming?.TriggerPressedDone();

            ShootData? shootData = BotOwner.ShootData;
            if (shootData != null)
            {
                shootData.LastTriggerPressd = Time.time;
                shootData.TimeFingerDown = Time.time;
                shootData.NextFingerDownCan = Time.time + 0.25f;
            }

            RecordLauncherSuppressShootAttempt(
                $"{reason}:launcherDirectTrigger:normal={normalShoot}:loaded={loadedRounds}:haveBullets={haveBullets}:aimRaise={aimRaise:0.0}",
                target);
        }

        private void RecordLauncherSuppressShootAttempt(string reason, Vector3 target)
        {
            if (Time.time < nextLauncherSuppressShootRecordAt)
            {
                return;
            }

            nextLauncherSuppressShootRecordAt = Time.time + 1f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherShootAttempt",
                reason,
                goalEnemy: BotOwner.Memory?.GoalEnemy,
                target: target);
        }

        private bool ShouldHoldCloseThreatSuppressFire(Vector3 suppressTarget)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                !goalEnemy.IsVisible ||
                goalEnemy.Distance > CloseThreatSuppressFireAlignmentDistance)
            {
                return false;
            }

            Vector3 lookDirection = BotOwner.LookDirection;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            Vector3 targetDirection = suppressTarget - BotOwner.Position;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Angle(lookDirection.normalized, targetDirection.normalized) > CloseThreatSuppressFireMaxAngle;
        }

        private Vector3 CorrectCloseThreatSuppressPoint(Vector3 suppressPoint)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                goalEnemy.Distance > CloseThreatSuppressCorrectionDistance ||
                !BotOwner.IsEnemyLookingAtMe(goalEnemy))
            {
                return suppressPoint;
            }

            Vector3 enemyPoint = goalEnemy.IsVisible
                ? goalEnemy.GetBodyPartPosition()
                : goalEnemy.CurrPosition + BotOwner.STAY_HEIGHT;

            Vector3 suppressDirection = suppressPoint - BotOwner.Position;
            Vector3 enemyDirection = enemyPoint - BotOwner.Position;
            suppressDirection.y = 0f;
            enemyDirection.y = 0f;
            if (suppressDirection.sqrMagnitude <= 0.01f ||
                enemyDirection.sqrMagnitude <= 0.01f ||
                Vector3.Angle(suppressDirection, enemyDirection) <= SuppressPointCorrectionAngle)
            {
                return suppressPoint;
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 fireDirection = enemyPoint - fireOrigin;
            if (fireDirection.sqrMagnitude <= 0.01f ||
                Physics.Raycast(fireOrigin, fireDirection.normalized, fireDirection.magnitude, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return suppressPoint;
            }

            return enemyPoint;
        }

        private bool HasReachedSuppressFromPoint(CustomNavigationPoint suppressFrom)
        {
            if (BotOwner.GoToSomePointData?.IsCome() == true)
            {
                return true;
            }

            return (BotOwner.Position - suppressFrom.Position).sqrMagnitude <= 1.5f * 1.5f;
        }
    }
}
