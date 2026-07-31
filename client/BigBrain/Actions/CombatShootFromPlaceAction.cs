using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Stationary combat fire action used when the decision tree wants the follower to stop moving
    /// and solve the fight from the current position. The action delegates aiming and shooting to
    /// EFT's shoot-from-place node, while keeping follower-specific safety gates around supported
    /// fire poses, recent-contact suppression continuity, and friendly shot-lane protection.
    /// </summary>
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private const float MinEnemyDistanceForProne = 80f;
        private const float SameSpotMaxDistanceSqr = 0.75f * 0.75f;
        private const float ProneFireProbeHeight = 0.35f;
        private const float StandingFireProbeHeight = 1.45f;
        private readonly GClass276 baseLogic;
        private float aimAlignStartedAt;
        private float nextLauncherNormalFireRejectAt;
        private float nextLauncherNormalFireRecordAt;
        private Vector3 startPosition;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Start()
        {
            base.Start();
            StopStationaryCombatMovement();
            startPosition = BotOwner.Position;
        }

        public override void Stop()
        {
            StopCombatShooting();
            aimAlignStartedAt = 0f;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;

            // First decide which fire poses are physically usable from this exact spot. The vanilla
            // node may crouch or prone by itself, but followers should not stay in a pose that has
            // no real shot lane, especially when cover/vegetation blocks the lower weapon origin.
            bool allowProne = goalEnemy != null &&
                              goalEnemy.Distance >= MinEnemyDistanceForProne &&
                              CanUseFirePose(goalEnemy, ProneFireProbeHeight);
            baseLogic.CanLay = allowProne;

            if (!allowProne && BotOwner.BotLay.IsLay)
            {
                BotOwner.BotLay.GetUp(false);
            }

            string? reason = GetReason(data) ?? BotOwner.Brain?.Agent?.LastResult().Reason;
            if (StopUnownedGrenadeLauncherFire(reason, goalEnemy))
            {
                return;
            }

            if (FollowerCombatCommon.IsGrenadeLauncherCombatReason(reason))
            {
                bool usingFirstPrimaryVisualGrace = false;
                if (!FollowerCombatCommon.TryCanUseGrenadeLauncherNormalFire(
                        BotOwner,
                        goalEnemy,
                        FollowerCombatGrenadierObjective.IsOrderedGrenadierReason(reason),
                        out Vector3 launcherImpactTarget,
                        out string launcherRejectReason))
                {
                    bool canContinueCommittedPrimaryShot =
                        string.Equals(launcherRejectReason, "enemyNotVisible", System.StringComparison.Ordinal) &&
                        FollowerCombatCommon.TryContinueFirstPrimaryGrenadeLauncherNormalFire(
                            BotOwner,
                            goalEnemy,
                            FollowerCombatGrenadierObjective.IsOrderedGrenadierReason(reason),
                            out launcherImpactTarget,
                            out _);
                    if (!canContinueCommittedPrimaryShot)
                    {
                        StopCombatShooting();
                        if (Time.time >= nextLauncherNormalFireRejectAt)
                        {
                            nextLauncherNormalFireRejectAt = Time.time + 2f;
                            BattleRecorder.RecordGrenadeEvent(
                                BotOwner,
                                "launcherNormalFireHold",
                                $"{reason}:{launcherRejectReason}",
                                goalEnemy: goalEnemy);
                        }

                        return;
                    }

                    usingFirstPrimaryVisualGrace = true;
                }

                UpdateGrenadeLauncherNormalFire(
                    launcherImpactTarget,
                    reason,
                    goalEnemy,
                    usingFirstPrimaryVisualGrace);
                return;
            }

            // If an immediate-fire decision briefly loses CanShoot because of foliage or a small
            // visibility flicker, keep a short suppressive shot at the last verified point instead
            // of dropping into movement churn.
            if (TryUpdateImmediateLostVisualSuppress(reason, goalEnemy))
            {
                return;
            }

            // Wait briefly for aim alignment before letting the EFT node run so it does not fire
            // while visibly off target.
            if (WaitForEnemyAimAlignment(ref aimAlignStartedAt, maxAngle: 15f, timeout: 0.18f))
            {
                return;
            }

            if (StopIfFriendlyInCurrentFireLane(goalEnemy))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
            EnforceSupportedFirePose(goalEnemy, allowProne);
        }

        /// <summary>
        /// Runs EFT's ordinary aim-and-trigger worker with an explicit ballistic point. The outer
        /// shoot-from-place node only invokes this worker when the straight rifle CanShoot flag is
        /// true, which would suppress valid arcing launcher shots. The grenadier objective has
        /// already validated the live target, impact radius, friendly lane, and sampled arc before
        /// this method is allowed to bypass that outer rifle gate.
        /// </summary>
        private void UpdateGrenadeLauncherNormalFire(
            Vector3 impactTarget,
            string? reason,
            EnemyInfo? goalEnemy,
            bool usingFirstPrimaryVisualGrace)
        {
            StopStationaryCombatMovement();
            BotOwner.SetPose(1f);
            baseLogic.CanLay = false;

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 aimPoint = FollowerCombatCommon.GetGrenadeLauncherSuppressAimPoint(
                BotOwner,
                fireOrigin,
                impactTarget);

            BotOwner.Steering.LookToPoint(aimPoint);
            baseLogic.Gclass178_0.UpdateNodeByBrain(new GClass27(aimPoint));

            if (Time.time >= nextLauncherNormalFireRecordAt)
            {
                nextLauncherNormalFireRecordAt = Time.time + 1f;
                BattleRecorder.RecordGrenadeEvent(
                    BotOwner,
                    "launcherNormalFire",
                    $"{reason}:canShoot={goalEnemy?.CanShoot == true}" +
                    $":visible={goalEnemy?.IsVisible == true}" +
                    $":visualGrace={usingFirstPrimaryVisualGrace}" +
                    $":aimReady={BotOwner.AimingManager?.CurrentAiming?.IsReady == true}" +
                    $":weaponReady={BotOwner.WeaponManager?.IsWeaponReady == true}" +
                    $":stateReady={BotOwner.ShootData?.CanShootByState == true}" +
                    $":shooting={BotOwner.ShootData?.Shooting == true}" +
                    $":loaded={FollowerCombatCommon.CountLoadedRounds(FollowerCombatCommon.GetActiveOrEquippedGrenadeLauncher(BotOwner))}" +
                    $":aimRaise={aimPoint.y - impactTarget.y:0.00}",
                    goalEnemy: goalEnemy,
                    target: impactTarget,
                    suppressFrom: aimPoint);
            }
        }

        /// <summary>
        /// Keep the final pose consistent with the lane probes after vanilla has updated. This is a
        /// cleanup pass because the underlying EFT node can still request crouch/prone internally.
        /// </summary>
        private void EnforceSupportedFirePose(EnemyInfo? goalEnemy, bool allowProne)
        {
            if (!allowProne && BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.BotLay.GetUp(false);
            }

            if (BotOwner.Mover.TargetPose < 1f &&
                !CanUseCrouchFirePose(goalEnemy) &&
                CanUseStandingFirePose(goalEnemy))
            {
                BotOwner.SetPose(1f);
            }
        }

        /// <summary>
        /// Probe whether a fire lane exists from a hypothetical body/weapon height. The result is
        /// used to prevent the bot from crouching or going prone into a pose that cannot actually
        /// see or shoot the target.
        /// </summary>
        private bool CanUseFirePose(EnemyInfo? goalEnemy, float probeHeight)
        {
            if (!CanEvaluateFirePose(goalEnemy, requireShootable: true))
            {
                return false;
            }

            ShootPointClass shootPoint = GetShootFromPlacePoint(goalEnemy!);
            return FollowerShootPoseSafety.HasReliablePoseLane(BotOwner, shootPoint.Point, probeHeight);
        }

        private bool CanUseCrouchFirePose(EnemyInfo? goalEnemy)
        {
            if (!CanEvaluateFirePose(goalEnemy, requireShootable: false))
            {
                return false;
            }

            ShootPointClass shootPoint = GetShootFromPlacePoint(goalEnemy!);
            return FollowerShootPoseSafety.HasReliableCrouchLane(BotOwner, shootPoint.Point);
        }

        private bool CanUseStandingFirePose(EnemyInfo? goalEnemy)
        {
            if (!CanEvaluateFirePose(goalEnemy, requireShootable: false))
            {
                return false;
            }

            ShootPointClass shootPoint = GetShootFromPlacePoint(goalEnemy!);
            return FollowerShootPoseSafety.HasReliablePoseLane(BotOwner, shootPoint.Point, StandingFireProbeHeight);
        }

        private bool CanEvaluateFirePose(EnemyInfo? goalEnemy, bool requireShootable)
        {
            if (goalEnemy == null ||
                !goalEnemy.IsVisible ||
                (requireShootable && !goalEnemy.CanShoot))
            {
                return false;
            }

            if (!BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            return true;
        }

        private ShootPointClass GetShootFromPlacePoint(EnemyInfo goalEnemy)
        {
            return BotOwner.CurrentEnemyTargetPosition(false) ??
                   new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);
        }

        /// <summary>
        /// Maintains very short fire continuity for immediate-fire decisions when the enemy just
        /// disappeared from the shoot sensor but the last seen point is still fresh and directly
        /// shootable. This is intentionally position-locked so it cannot turn into blind walking fire.
        /// </summary>
        private bool TryUpdateImmediateLostVisualSuppress(string? reason, EnemyInfo? goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                goalEnemy == null ||
                goalEnemy.CanShoot ||
                !FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            if ((BotOwner.Position - startPosition).sqrMagnitude > SameSpotMaxDistanceSqr)
            {
                StopCombatShooting();
                return true;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            if (!FollowerImmediateFirePolicy.HasDirectFireLane(BotOwner, target))
            {
                StopCombatShooting();
                return true;
            }

            if (StopIfFriendlyInCurrentFireLane(target))
            {
                BotOwner.Steering.LookToPoint(target);
                return true;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(target);
            BotOwner.ShootData.Shoot();
            return true;
        }

    }
}
