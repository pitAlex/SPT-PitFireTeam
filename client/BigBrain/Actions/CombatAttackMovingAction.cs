using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Moving fire action used for tactical retreats, regroup-with-contact, and pressure movement.
    /// It keeps EFT's attack-moving node as the base, then adds follower-specific primary-weapon
    /// preference, threat-facing, optional suppressive bursts, and unsafe close-threat guards.
    /// </summary>
    internal class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly FollowerAttackMovingLogic baseLogic;
        private readonly FollowerCombatFireOverlay fireOverlay;

        protected CombatAttackMovingAction(
            BotOwner botOwner,
            bool withSuppress,
            bool autoCover = false,
            bool forceThreatLookWhenShootable = false) : base(botOwner)
        {
            fireOverlay = new FollowerCombatFireOverlay(botOwner);
            baseLogic = new FollowerAttackMovingLogic(
                botOwner,
                fireOverlay,
                withSuppress,
                autoCover,
                forceThreatLookWhenShootable);
        }

        public CombatAttackMovingAction(BotOwner botOwner) : this(botOwner, withSuppress: false)
        {
        }

        public override void Stop()
        {
            fireOverlay.Stop();
            StopCombatShooting();
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data);
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            bool hasLiveGoalEnemy = goalEnemy?.Person?.HealthController?.IsAlive == true;
            bool hasNoEnemyThreatMove =
                !hasLiveGoalEnemy &&
                FollowerCombatCommon.IsNoEnemyThreatCoverReason(reason) &&
                FollowerAwareness.TryGetRecentThreatLookPoint(BotOwner, out _);
            if (!hasLiveGoalEnemy && !hasNoEnemyThreatMove)
            {
                StopCombatShooting();
                BotOwner.LookData.SetLookPointByHearing(null);
                BotOwner.Mover.Stop();
                return;
            }

            // Attack-moving can run for a while, so keep non-marksman followers on their primary at
            // range and pass the current decision reason into the wrapped node for suppress behavior.
            if (hasLiveGoalEnemy)
            {
                TryPreferPrimaryAtRange(goalEnemy!, reason);
                if (HoldPushMovementUntilLongGunReady(reason))
                {
                    return;
                }

                if (StopUnownedGrenadeLauncherFire(reason, goalEnemy))
                {
                    return;
                }
            }

            baseLogic.SetCurrentReason(reason);
            baseLogic.UpdateNodeByBrain(GetRawData(data));
            EnforceCloseThreatStandingPose("attackMoving", reason, goalEnemy);
        }

        /// <summary>
        /// Wrapper around EFT's attack-moving node. The follower planner owns the movement
        /// destination; this wrapper keeps vanilla aiming/reload behavior while preventing its
        /// periodic cover search from replacing that destination.
        /// </summary>
        private sealed class FollowerAttackMovingLogic : GClass205
        {
            private const float ArrivalThreatLookAngle = 95f;
            private const float NearCoverDistance = 2f;
            private const float RecentThreatLookSeconds = 2.5f;
            private const float UnsafeCloseThreatDistance = 8f;
            private const float UnsafeCloseThreatLookAngle = 70f;
            private const float RegroupCatchUpDestinationDistance = 25f;

            private readonly bool autoCover;
            private readonly bool forceThreatLookWhenShootable;
            private readonly bool withSuppress;
            private readonly FollowerCombatFireOverlay fireOverlay;
            private string? currentReason;
            private float nextThreatLookTime;

            public FollowerAttackMovingLogic(
                BotOwner botOwner,
                FollowerCombatFireOverlay fireOverlay,
                bool withSuppress,
                bool autoCover,
                bool forceThreatLookWhenShootable) : base(botOwner)
            {
                this.fireOverlay = fireOverlay;
                this.withSuppress = withSuppress;
                this.autoCover = autoCover;
                this.forceThreatLookWhenShootable = forceThreatLookWhenShootable;
            }

            public void SetCurrentReason(string? reason)
            {
                currentReason = reason;
            }

            public override void UpdateNodeByBrain(GClass26 data)
            {
                if (!autoCover)
                {
                    // GClass205 asks BotAttackManager for another cover every two seconds. The
                    // combat planner already selected this action's destination, so keep the
                    // vanilla timer deferred and execute the assigned cover/point exactly.
                    Float_2 = Time.time + 2f;
                    ForcePlannerDestination();
                }

                bool regroupCatchUp = ShouldUseRegroupCatchUp();
                if (regroupCatchUp)
                {
                    ForceRegroupCatchUpMovement();
                }

                if (BotOwner_0.Memory?.GoalEnemy == null)
                {
                    UpdateWithoutGoalEnemy(data);
                }
                else
                {
                    base.UpdateNodeByBrain(data);
                }

                if (!autoCover)
                {
                    ForcePlannerDestination();
                }

                if (regroupCatchUp)
                {
                    ForceRegroupCatchUpMovement();
                }

                // Retreat actions own look direction independently from path steering. EFT's
                // movement update can overwrite steering after GoToPoint/cover pathing, which was
                // turning the follower's back toward a close enemy on heal retreats. Re-assert the
                // threat lane after movement has finished its update so forward/back/side path
                // choices never change which way the weapon is facing.
                if (forceThreatLookWhenShootable)
                {
                    TryMaintainThreatFacing(BotOwner_0.Memory?.GoalEnemy);
                }
            }

            private void UpdateWithoutGoalEnemy(GClass26 data)
            {
                // recovery.noEnemyThreatCover deliberately survives a temporary GoalEnemy gap so
                // the follower can keep moving and suppressing toward concrete incoming-fire evidence.
                // Vanilla GClass205 is not valid in that state: while in cover it dereferences
                // Memory.GoalEnemy.EnemyLastPosition without a null check. Preserve the planner-owned
                // movement and our recent-threat fire overlay, but skip that enemy-dependent update.
                base.method_0(true);
                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Sprint(false, true);
                BotOwner_0.SetPose(1f);
                BotOwner_0.Mover.SetPose(1f);
                BotOwner_0.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher();
                AimingAndShoot(data);
            }

            public override void AimingAndShoot(GClass26 data)
            {
                if (ShouldUseRegroupCatchUp())
                {
                    StopShooting();
                    BotOwner_0.LookData.SetLookPointByHearing(null);
                    BotOwner_0.Steering.LookToMovingDirection();
                    return;
                }

                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner_0))
                {
                    TryMaintainThreatFacing(goalEnemy);
                }

                bool unsafeCloseRetreat = TryStopUnsafeCloseThreatRetreat(goalEnemy);
                bool allowThreatSuppression =
                    withSuppress ||
                    global::pitTeam.BigBrain.FollowerCombatCommon.IsReasonOrSubreason(
                        currentReason,
                        "moveToHealPoint");
                if (fireOverlay.Update(
                        goalEnemy,
                        currentReason,
                        allowThreatSuppression,
                        forceThreatLook: forceThreatLookWhenShootable || allowThreatSuppression,
                        out _))
                {
                    return;
                }

                if (unsafeCloseRetreat)
                {
                    return;
                }

                if (goalEnemy != null && nextThreatLookTime < Time.time)
                {
                    nextThreatLookTime = Time.time + GClass856.Random(2f, 3f);
                    BotOwner_0.Steering.LookToPoint(goalEnemy.EnemyLastPosition + new Vector3(0f, 0.6f, 0f));
                }
            }

            private bool TryStopUnsafeCloseThreatRetreat(EnemyInfo? goalEnemy)
            {
                if (!forceThreatLookWhenShootable ||
                    goalEnemy == null ||
                    !IsCloseActiveThreat(goalEnemy, UnsafeCloseThreatDistance, 0.75f))
                {
                    return false;
                }

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, allowHardTurn: true);
                if (CombatAttackMoveLook.GetThreatLookAngle(BotOwner_0, goalEnemy) <= UnsafeCloseThreatLookAngle)
                {
                    return false;
                }

                BotOwner_0.Mover.Stop();
                BotOwner_0.Sprint(false, true);
                return true;
            }

            private void TryMaintainThreatFacing(EnemyInfo? goalEnemy)
            {
                if (goalEnemy == null ||
                    (!forceThreatLookWhenShootable && !ShouldCorrectArrivalLook(goalEnemy)))
                {
                    return;
                }

                Vector3 threatPoint = goalEnemy.IsVisible
                    ? goalEnemy.GetBodyPartPosition()
                    : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
                Vector3 lookDirection = threatPoint - BotOwner_0.Position;
                if (lookDirection.sqrMagnitude < 0.01f)
                {
                    return;
                }

                if (!forceThreatLookWhenShootable &&
                    Vector3.Angle(BotOwner_0.LookDirection, lookDirection) < ArrivalThreatLookAngle)
                {
                    return;
                }

                bool allowHardTurn =
                    forceThreatLookWhenShootable ||
                    BotOwner_0.Memory.IsInCover ||
                    global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason);

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, allowHardTurn);
            }

            private bool ShouldCorrectArrivalLook(EnemyInfo goalEnemy)
            {
                if (IsCloseActiveThreat(goalEnemy, UnsafeCloseThreatDistance, 0.75f))
                {
                    return true;
                }

                if (!goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > RecentThreatLookSeconds)
                {
                    return false;
                }

                if (BotOwner_0.Memory.IsInCover)
                {
                    return true;
                }

                if (global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) &&
                    BotOwner_0.GoToSomePointData != null &&
                    BotOwner_0.GoToSomePointData.IsCome())
                {
                    return true;
                }

                CustomNavigationPoint? cover = BotOwner_0.Memory?.CurCustomCoverPoint;
                if (cover == null)
                {
                    return false;
                }

                return (BotOwner_0.Position - cover.Position).sqrMagnitude <= NearCoverDistance * NearCoverDistance;
            }

            private bool IsCloseActiveThreat(EnemyInfo goalEnemy, float maxDistance, float recentSeenWindow)
            {
                return goalEnemy != null &&
                       goalEnemy.Distance <= maxDistance &&
                       SainGoalEnemyBridge.IsEnemyLookingAtFollower(BotOwner_0, goalEnemy) &&
                       (goalEnemy.IsVisible ||
                        Time.time - goalEnemy.PersonalSeenTime <= recentSeenWindow ||
                        Time.time - goalEnemy.PersonalLastSeenTime <= recentSeenWindow);
            }

            private void ForceCurrentCoverDestination()
            {
                CustomNavigationPoint cover = BotOwner_0.Memory.CurCustomCoverPoint;
                bool withShoot = BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                                 BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Sprint(false, true);
                BotOwner_0.SetPose(1f);
                BotOwner_0.Memory.SetCoverPoints(cover, string.Empty);
                if (!BotOwner_0.HasPathAndNotComplete ||
                    !BotOwner_0.Mover.TargetPoint.HasValue ||
                    (BotOwner_0.Mover.TargetPoint.Value - cover.Position).sqrMagnitude > 1f)
                {
                    BotOwner_0.GoToPoint(cover);
                }

                if (!cover.CanIShootToEnemy && withShoot)
                {
                    BotOwner_0.BotAttackManager.UpdateNextTick();
                }
            }

            private void ForcePlannerDestination()
            {
                bool explicitPointOwned =
                    global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) ||
                    global::pitTeam.BigBrain.FollowerCombatCommon.IsReasonOrSubreason(currentReason, "moveToHealPoint");
                if (explicitPointOwned &&
                    TryForceExplicitPointDestination())
                {
                    return;
                }

                if (BotOwner_0.Memory?.CurCustomCoverPoint != null)
                {
                    ForceCurrentCoverDestination();
                    return;
                }

                TryForceExplicitPointDestination();
            }

            private bool TryForceExplicitPointDestination()
            {
                if (BotOwner_0.GoToSomePointData?.HaveTarget() != true)
                {
                    return false;
                }

                Vector3 target = BotOwner_0.GoToSomePointData.Point;
                if (BotOwner_0.HasPathAndNotComplete &&
                    BotOwner_0.Mover.TargetPoint.HasValue &&
                    (BotOwner_0.Mover.TargetPoint.Value - target).sqrMagnitude <= 1f)
                {
                    return true;
                }

                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Sprint(false, true);
                BotOwner_0.SetPose(1f);
                BotOwner_0.GoToPoint(target, true, -1f, false, false);
                return true;
            }

            private bool ShouldUseRegroupCatchUp()
            {
                if (!global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) ||
                    BotOwner_0.GoToSomePointData?.HaveTarget() != true)
                {
                    return false;
                }

                Vector3 target = BotOwner_0.GoToSomePointData.Point;
                return (BotOwner_0.Position - target).sqrMagnitude >
                       RegroupCatchUpDestinationDistance * RegroupCatchUpDestinationDistance;
            }

            private void ForceRegroupCatchUpMovement()
            {
                BotOwner_0.SetPose(1f);
                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Mover.Sprint(true, false);
            }

            private void StopShooting()
            {
                BotOwner_0.ShootData?.EndShoot();
                BotOwner_0.WeaponManager?.ShootController?.SetTriggerPressed(false);
            }
        }
    }
}
