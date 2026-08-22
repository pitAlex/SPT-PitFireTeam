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
        private sealed class FollowerAttackMovingLogic : AttackMoving
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

            public override void UpdateNodeByBrain(CoreActionResultParams data)
            {
                if (!autoCover)
                {
                    // AttackMoving asks BotAttackManager for another cover every two seconds. The
                    // combat planner already selected this action's destination, so keep the
                    // vanilla timer deferred and execute the assigned cover/point exactly.
                    _nextCoverCheck = Time.time + 2f;
                    ForcePlannerDestination();
                }

                bool regroupCatchUp = ShouldUseRegroupCatchUp();
                if (regroupCatchUp)
                {
                    ForceRegroupCatchUpMovement();
                }

                if (_owner.Memory?.GoalEnemy == null)
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
                    TryMaintainThreatFacing(_owner.Memory?.GoalEnemy);
                }
            }

            private void UpdateWithoutGoalEnemy(CoreActionResultParams data)
            {
                // recovery.noEnemyThreatCover deliberately survives a temporary GoalEnemy gap so
                // the follower can keep moving and suppressing toward concrete incoming-fire evidence.
                // Vanilla AttackMoving is not valid in that state: while in cover it dereferences
                // Memory.GoalEnemy.EnemyLastPosition without a null check. Preserve the planner-owned
                // movement and our recent-threat fire overlay, but skip that enemy-dependent update.
                DoorOpen(true);
                _owner.SetTargetMoveSpeed(1f);
                _owner.Sprint(false, true);
                _owner.SetPose(1f);
                _owner.Mover.SetPose(1f);
                _owner.WeaponManager?.TryReloadWeaponOrUnderbarrelLauncher();
                AimingAndShoot(data);
            }

            public override void AimingAndShoot(CoreActionResultParams data)
            {
                if (ShouldUseRegroupCatchUp())
                {
                    StopShooting();
                    _owner.LookData.SetLookPointByHearing(null);
                    _owner.Steering.LookToMovingDirection();
                    return;
                }

                EnemyInfo? goalEnemy = _owner.Memory?.GoalEnemy;
                bool commandLookApplied = BotFollowerPlayer.TryApplyCommandLookOverride(_owner);
                bool threatFacingMaintained = false;
                if (!commandLookApplied)
                {
                    threatFacingMaintained = TryMaintainThreatFacing(goalEnemy);
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

                if (commandLookApplied || threatFacingMaintained || goalEnemy == null)
                {
                    return;
                }

                if (CombatAttackMoveLook.TryGetReliableThreatLookPoint(_owner, goalEnemy, out _))
                {
                    if (nextThreatLookTime < Time.time)
                    {
                        nextThreatLookTime = Time.time + MyExtensions.Random(2f, 3f);
                        CombatAttackMoveLook.TryLookReliableThreatFacing(_owner, goalEnemy);
                    }

                    return;
                }

                // A memory-only push can retain a very old group-sense point after the enemy has
                // moved away. Looking at that point when the route reaches it produces a downward or
                // backwards snap. Without follower-owned threat position, face the active route.
                nextThreatLookTime = 0f;
                if (_owner.Mover.HasPathAndNoComplete)
                {
                    _owner.LookData.SetLookPointByHearing(null);
                    _owner.Steering.LookToMovingDirection();
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

                CombatAttackMoveLook.TryLookThreatFacing(_owner, goalEnemy, allowHardTurn: true);
                if (CombatAttackMoveLook.GetThreatLookAngle(_owner, goalEnemy) <= UnsafeCloseThreatLookAngle)
                {
                    return false;
                }

                _owner.Mover.Stop();
                _owner.Sprint(false, true);
                return true;
            }

            private bool TryMaintainThreatFacing(EnemyInfo? goalEnemy)
            {
                if (goalEnemy == null ||
                    (!forceThreatLookWhenShootable && !ShouldCorrectArrivalLook(goalEnemy)))
                {
                    return false;
                }

                Vector3 threatPoint = goalEnemy.IsVisible
                    ? goalEnemy.GetBodyPartPosition()
                    : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
                Vector3 lookDirection = threatPoint - _owner.Position;
                if (lookDirection.sqrMagnitude < 0.01f)
                {
                    return false;
                }

                if (!forceThreatLookWhenShootable &&
                    Vector3.Angle(_owner.LookDirection, lookDirection) < ArrivalThreatLookAngle)
                {
                    return true;
                }

                bool allowHardTurn =
                    forceThreatLookWhenShootable ||
                    _owner.Memory.IsInCover ||
                    global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason);

                return CombatAttackMoveLook.TryLookThreatFacing(_owner, goalEnemy, allowHardTurn);
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

                if (_owner.Memory.IsInCover)
                {
                    return true;
                }

                if (global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) &&
                    _owner.GoToSomePointData != null &&
                    _owner.GoToSomePointData.IsCome())
                {
                    return true;
                }

                CustomNavigationPoint? cover = _owner.Memory?.CurCustomCoverPoint;
                if (cover == null)
                {
                    return false;
                }

                return (_owner.Position - cover.Position).sqrMagnitude <= NearCoverDistance * NearCoverDistance;
            }

            private bool IsCloseActiveThreat(EnemyInfo goalEnemy, float maxDistance, float recentSeenWindow)
            {
                return goalEnemy != null &&
                       goalEnemy.Distance <= maxDistance &&
                       SainGoalEnemyBridge.IsEnemyLookingAtFollower(_owner, goalEnemy) &&
                       (goalEnemy.IsVisible ||
                        Time.time - goalEnemy.PersonalSeenTime <= recentSeenWindow ||
                        Time.time - goalEnemy.PersonalLastSeenTime <= recentSeenWindow);
            }

            private void ForceCurrentCoverDestination()
            {
                CustomNavigationPoint cover = _owner.Memory.CurCustomCoverPoint;
                bool withShoot = _owner.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                                 _owner.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                _owner.SetTargetMoveSpeed(1f);
                _owner.Sprint(false, true);
                _owner.SetPose(1f);
                _owner.Memory.SetCoverPoints(cover, string.Empty);
                if (!_owner.HasPathAndNotComplete ||
                    !_owner.Mover.TargetPoint.HasValue ||
                    (_owner.Mover.TargetPoint.Value - cover.Position).sqrMagnitude > 1f)
                {
                    _owner.GoToPoint(cover);
                }

                if (!cover.CanIShootToEnemy && withShoot)
                {
                    _owner.BotAttackManager.UpdateNextTick();
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

                if (_owner.Memory?.CurCustomCoverPoint != null)
                {
                    ForceCurrentCoverDestination();
                    return;
                }

                TryForceExplicitPointDestination();
            }

            private bool TryForceExplicitPointDestination()
            {
                if (_owner.GoToSomePointData?.HaveTarget() != true)
                {
                    return false;
                }

                Vector3 target = _owner.GoToSomePointData.Point;
                if (_owner.HasPathAndNotComplete &&
                    _owner.Mover.TargetPoint.HasValue &&
                    (_owner.Mover.TargetPoint.Value - target).sqrMagnitude <= 1f)
                {
                    return true;
                }

                _owner.SetTargetMoveSpeed(1f);
                _owner.Sprint(false, true);
                _owner.SetPose(1f);
                _owner.GoToPoint(target, true, -1f, false, false);
                return true;
            }

            private bool ShouldUseRegroupCatchUp()
            {
                if (!global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) ||
                    _owner.GoToSomePointData?.HaveTarget() != true)
                {
                    return false;
                }

                Vector3 target = _owner.GoToSomePointData.Point;
                return (_owner.Position - target).sqrMagnitude >
                       RegroupCatchUpDestinationDistance * RegroupCatchUpDestinationDistance;
            }

            private void ForceRegroupCatchUpMovement()
            {
                _owner.SetPose(1f);
                _owner.SetTargetMoveSpeed(1f);
                _owner.Mover.Sprint(true, false);
            }

            private void StopShooting()
            {
                _owner.ShootData?.EndShoot();
                _owner.WeaponManager?.ShootController?.SetTriggerPressed(false);
            }
        }
    }
}
