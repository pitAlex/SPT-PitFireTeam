using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Controlled advance toward the enemy. This is the walking/pressure push action: it commits
    /// an advance point, refreshes pathing when progress stalls, shoots while moving when possible,
    /// and chooses look modes that avoid staring into nearby geometry.
    /// </summary>
    internal sealed class CombatGoToEnemyAction : FollowerCombatActionBase
    {
        private enum AdvanceLookMode
        {
            None,
            EnemyOwned,
            AdvancePoint,
            MovingDirection,
            KeepCurrent
        }

        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.4f;
        private const int StalledRefreshThreshold = 3;
        private const float TacticalReloadSafeDistance = 25f;
        private const float WallFacingProbeDistance = 1.25f;
        private const float WallFacingFallbackDebounceSeconds = 0.2f;
        private const float LookCommitMinSeconds = 0.2f;
        private const float LookCommitMaxSeconds = 0.4f;
        private const float EnemyLookLeaseSeconds = 0.75f;
        private const float NavigationLookCheckInterval = 0.25f;
        private const float NavigationLookSustainSeconds = 0.65f;
        private const float NavigationLookMinEnemyDistance = 32f;
        private const float NavigationLookBadPathAngle = 75f;
        private const float NavigationLookPathExtraDistance = 15f;
        private const float NavigationLookPathDistanceRatio = 1.35f;
        private const float SprintApproachStopDistance = 15f;
        private const float StaleLocalLookMinAge = 4f;
        private const float StaleLocalLookMaxDistance = 5f;
        private const float StaleLocalLookBackpedalAngle = 120f;
        private const float ActualMoveDirectionMinDeltaSqr = 0.0009f;
        private const float ActualMoveDirectionMaxAge = 0.35f;
        private const int LargeMagazineLowAmmoThreshold = 20;
        private const int PistolLargeMagazineLowAmmoThreshold = 10;

        private readonly AimingToGoalTarget shootLogic;
        private bool shouldSprint;
        private float nextMoveRefreshTime;
        private Vector3 committedAdvancePoint;
        private bool hasCommittedAdvancePoint;
        private Vector3 lastProgressPosition;
        private float nextProgressCheckTime;
        private int stalledProgressChecks;
        private AdvanceLookMode committedLookMode;
        private float committedLookModeUntil;
        private float wallFacingSince;
        private bool wallFacingActive;
        private float enemyLookLeaseUntil;
        private float navigationLookCandidateSince;
        private float nextNavigationLookCheckTime;
        private bool cachedShouldPreferNavigationLook;
        private Vector3 lastActualMovePosition;
        private Vector3 recentActualMoveDirection;
        private float recentActualMoveDirectionTime;

        public CombatGoToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new AimingToGoalTarget(botOwner);
        }

        public override void Start()
        {
            base.Start();
            shouldSprint = false;
            nextMoveRefreshTime = 0f;
            committedAdvancePoint = Vector3.zero;
            hasCommittedAdvancePoint = false;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = 0f;
            stalledProgressChecks = 0;
            committedLookMode = AdvanceLookMode.None;
            committedLookModeUntil = 0f;
            wallFacingSince = 0f;
            wallFacingActive = false;
            enemyLookLeaseUntil = 0f;
            navigationLookCandidateSince = 0f;
            nextNavigationLookCheckTime = 0f;
            cachedShouldPreferNavigationLook = false;
            lastActualMovePosition = BotOwner.Position;
            recentActualMoveDirection = Vector3.zero;
            recentActualMoveDirectionTime = 0f;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            string? reason = GetReason(data);
            TryPreferPrimaryAtRange(goalEnemy, reason);
            if (HoldPushMovementUntilLongGunReady(reason))
            {
                return;
            }

            if (StopUnownedGrenadeLauncherFire(reason, goalEnemy))
            {
                return;
            }

            RefreshEnemyLookLease(goalEnemy);
            bool sprintNow = ShouldSprintNow(goalEnemy);
            SetCombatSprint(sprintNow);

            // Push destinations are sticky, but nav/pathing in EFT can stall around rocks, stairs,
            // and tight walls. Track progress every update cycle and refresh when the committed point
            // is no longer making the bot advance.
            RefreshProgressState();
            RefreshActualMovementDirection();
            NotMovingCheck();
            bool hasPath = BotOwner.Mover.HasPathAndNoComplete;

            // Visibility alone should not hard-stop an advance. Let end-condition logic decide when
            // the shot is stable enough to break the push; otherwise keep moving and shoot while advancing.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.SetPose(1f);
                if (!ShouldPreferNavigationLook(goalEnemy))
                {
                    CommitLookMode(AdvanceLookMode.EnemyOwned);
                    TryApplyCommittedLook(goalEnemy);
                }

                if (!hasPath)
                {
                    AimingAndShoot(data);
                    return;
                }
            }

            if (!goalEnemy.IsVisible && Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeSense >= 5f)
            {
                AimingAndShoot(data);
            }
            else if (!hasPath || sprintNow)
            {
                LookTowardAdvance(goalEnemy);
            }

            if (hasPath)
            {
                BotOwner.SetPose(1f);
                bool reached = BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false);

                // Advancing followers can spend a long time between covers. Reload only when the
                // enemy state says it is tactically safe enough to avoid arriving empty.
                if (ShouldReloadWhileAdvancing(goalEnemy))
                {
                    BotOwner.WeaponManager.Reload.TryReload();
                }

                if (reached && goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    ClearCommittedAdvancePoint();
                    AimAndMove(data);
                    return;
                }

                if (reached)
                {
                    ClearCommittedAdvancePoint();
                    BotOwner.StopMove();
                    LookTowardAdvance(goalEnemy);
                    return;
                }

                if (!sprintNow)
                {
                    AimingAndShoot(data);
                }

                return;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
        }

        public override void Stop()
        {
            shouldSprint = false;
            ClearCommittedAdvancePoint();
            base.Stop();
        }

        private void NotMovingCheck()
        {
            if (BotOwner.Memory.GoalEnemy == null)
            {
                return;
            }

            if (nextMoveRefreshTime > Time.time)
            {
                return;
            }

            nextMoveRefreshTime = Time.time + 3f;
            if (BotOwner.Mover.HasPathAndNoComplete && HasCommittedAdvancePoint())
            {
                return;
            }

            // If we already committed a point and it still looks usable, try to re-acquire that path first
            // instead of clearing and recalculating immediately (reduces repath/look churn).
            if (TryContinueToCommittedAdvancePoint())
            {
                return;
            }

            ClearCommittedAdvancePoint();
            TryMoveToEnemy(BotOwner.Memory.GoalEnemy.CurrPosition);
        }

        private void AimAndMove(CustomLayer.ActionData data)
        {
            if (HasCommittedAdvancePoint())
            {
                BotOwner.BotAttackManager.UpdateNextTick();
                AimingAndShoot(data);
                return;
            }

            Vector3 enemyPos = BotOwner.Memory.GoalEnemy.EnemyLastPosition;
            Vector3 centerPos = BotOwner.Memory.IsInCover && !BotOwner.LookSensor.EnoughDistToShoot(out _)
                ? (BotOwner.Transform.position + enemyPos) / 2f
                : enemyPos;

            ShootToPoint shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.shoot);
            CustomNavigationPoint point = Covers.GetClosestCoverPoint(BotOwner, centerPos, 50f, cover =>
            {
                return Utils.Utils.CanShootToTarget(shootPoint, cover, BotOwner.LookSensor.Mask, false);
            }, searchType);

            if (point != null)
            {
                BotOwner.GoToPoint(point);
                CommitAdvancePoint(point.Position);
            }
            else if (BotOwner.GoToPoint(centerPos) == NavMeshPathStatus.PathComplete)
            {
                CommitAdvancePoint(centerPos);
            }

            BotOwner.BotAttackManager.UpdateNextTick();
            AimingAndShoot(data);
        }

        private void AimingAndShoot(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                shootLogic.UpdateNodeByBrain(GetData<AimingResultParams>(data));
                return;
            }

            // Look toward enemy last known position. Avoid navmesh corner points which are
            // in the middle of walls and cause the "shooting at the wall" behavior.
            LookTowardAdvance(goalEnemy);
        }

        private void LookTowardAdvance(EnemyInfo goalEnemy)
        {
            if (BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                return;
            }

            bool preferNavigationLook = ShouldPreferNavigationLook(goalEnemy);
            if (!preferNavigationLook && TryApplyCommittedLook(goalEnemy))
            {
                return;
            }

            if (preferNavigationLook && TryLookTowardAdvancePoint())
            {
                return;
            }

            if (!preferNavigationLook &&
                TryGetOwnedEnemyLookDirection(goalEnemy, out Vector3 lookDirection))
            {
                CommitLookMode(AdvanceLookMode.EnemyOwned);
                BotOwner.Steering.LookToDirection(lookDirection.normalized);
                return;
            }

            if (ShouldPreferMovingDirectionOverStaleLocalLook(goalEnemy))
            {
                CommitLookMode(AdvanceLookMode.MovingDirection);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            if (TryLookTowardAdvancePoint())
            {
                return;
            }

            if (BotOwner.Mover.HasPathAndNoComplete)
            {
                CommitLookMode(AdvanceLookMode.MovingDirection);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            CommitLookMode(AdvanceLookMode.KeepCurrent);
            BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
        }

        private void RefreshEnemyLookLease(EnemyInfo goalEnemy)
        {
            if (goalEnemy?.Person?.HealthController?.IsAlive == true &&
                (goalEnemy.IsVisible || goalEnemy.CanShoot))
            {
                enemyLookLeaseUntil = Mathf.Max(enemyLookLeaseUntil, Time.time + EnemyLookLeaseSeconds);
            }
        }

        private bool ShouldPreferNavigationLook(EnemyInfo goalEnemy)
        {
            if (Time.time < nextNavigationLookCheckTime)
            {
                return cachedShouldPreferNavigationLook;
            }

            nextNavigationLookCheckTime = Time.time + NavigationLookCheckInterval;
            if (!HasNavigationLookEvidence(goalEnemy))
            {
                navigationLookCandidateSince = 0f;
                cachedShouldPreferNavigationLook = false;
                return false;
            }

            if (navigationLookCandidateSince <= 0f)
            {
                navigationLookCandidateSince = Time.time;
                cachedShouldPreferNavigationLook = false;
                return false;
            }

            cachedShouldPreferNavigationLook = Time.time - navigationLookCandidateSince >= NavigationLookSustainSeconds;
            return cachedShouldPreferNavigationLook;
        }

        private bool HasNavigationLookEvidence(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || !BotOwner.Mover.HasPathAndNoComplete)
            {
                return false;
            }

            if (!TryGetMoveTargetDirection(out Vector3 toMoveTarget) ||
                !TryGetEnemyLookDirection(goalEnemy, out Vector3 toEnemy, out float enemyDistance))
            {
                return false;
            }

            if (enemyDistance < NavigationLookMinEnemyDistance ||
                Vector3.Angle(toMoveTarget, toEnemy) < NavigationLookBadPathAngle)
            {
                return false;
            }

            if (!TryGetEnemyPathAnchor(goalEnemy, out Vector3 enemyAnchor) ||
                !Utils.Utils.TryGetCompletePathDistance(BotOwner.Position, enemyAnchor, out float pathDistance))
            {
                return false;
            }

            float directDistance = Flatten(enemyAnchor - BotOwner.Position).magnitude;
            if (directDistance <= 0.01f)
            {
                return false;
            }

            bool indirectPath =
                pathDistance >= directDistance + NavigationLookPathExtraDistance &&
                pathDistance >= directDistance * NavigationLookPathDistanceRatio;
            if (!indirectPath)
            {
                return false;
            }

            // Recent contact gets a short lease so one-frame visibility/can-shoot flicker does not
            // turn ordinary strafing into path-looking. A sustained indirect route is allowed to win.
            return Time.time >= enemyLookLeaseUntil ||
                   navigationLookCandidateSince > 0f &&
                   Time.time - navigationLookCandidateSince >= NavigationLookSustainSeconds;
        }

        private bool TryLookTowardAdvancePoint()
        {
            Vector3? advanceTarget = GetAdvanceTargetPoint();
            if (!advanceTarget.HasValue)
            {
                return false;
            }

            Vector3 lookDirection = advanceTarget.Value - BotOwner.Position;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            if (IsFacingWall(lookDirection))
            {
                if (!wallFacingActive)
                {
                    wallFacingActive = true;
                    wallFacingSince = Time.time;
                }

                if (Time.time - wallFacingSince >= WallFacingFallbackDebounceSeconds)
                {
                    CommitLookMode(AdvanceLookMode.MovingDirection);
                    BotOwner.Steering.LookToMovingDirection();
                    return true;
                }

                CommitLookMode(AdvanceLookMode.AdvancePoint);
                BotOwner.Steering.LookToPoint(advanceTarget.Value + Vector3.up * 0.5f);
                return true;
            }

            wallFacingActive = false;
            wallFacingSince = 0f;

            CommitLookMode(AdvanceLookMode.AdvancePoint);
            BotOwner.Steering.LookToPoint(advanceTarget.Value + Vector3.up * 0.5f);
            return true;
        }

        private Vector3? GetAdvanceTargetPoint()
        {
            if (BotOwner.Mover.TargetPoint.HasValue)
            {
                return BotOwner.Mover.TargetPoint.Value;
            }

            if (hasCommittedAdvancePoint)
            {
                return committedAdvancePoint;
            }

            return null;
        }

        private bool IsFacingWall(Vector3 lookDirection)
        {
            Vector3 origin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position + Vector3.up * 0.1f
                : BotOwner.Position + Vector3.up * 1.4f;

            return Physics.Raycast(origin, lookDirection.normalized, WallFacingProbeDistance, LayersMaskController.HighPolyWithTerrainMask);
        }

        private bool TryMoveToEnemy(Vector3 targetPoint)
        {
            if (BotOwner.MoveToEnemyData.CanShootAtEndCurWay(targetPoint, out Vector3 currentTargetPos) &&
                TryGoToPoint(currentTargetPos, true))
            {
                return true;
            }

            if (BotOwner.Mover.HasPathAndNoComplete &&
                HasCommittedAdvancePoint() &&
                !BotOwner.MoveToEnemyData.ShallRecalWay(out _) &&
                Time.time - BotOwner.Mover.LastPathSetTime < 10f)
            {
                return true;
            }

            if (BotOwner.GoToPoint(targetPoint, true, -1f, false, false) == NavMeshPathStatus.PathComplete &&
                BotOwner.Mover.TargetPoint is Vector3 curPathLastPoint &&
                (targetPoint - curPathLastPoint).magnitude < 2f)
            {
                CommitAdvancePoint(curPathLastPoint);
                shouldSprint = !Utils.Utils.IsWithinDistance(curPathLastPoint, BotOwner.GetPlayer.Transform.position, 20f);
                return true;
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                TryGoToPoint(navMeshHit.position, false))
            {
                return true;
            }

            CustomNavigationPoint customNavigationPoint = null;
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.hide);
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f, searchTypeOverride: searchType);
            if (closePoints.Count > 0)
            {
                customNavigationPoint = closePoints.RandomElement();
            }

            if (customNavigationPoint == null)
            {
                customNavigationPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f, searchTypeOverride: searchType);
            }

            if (customNavigationPoint != null &&
                Mathf.Abs(customNavigationPoint.Position.y - targetPoint.y) <= VerticalTolerance &&
                TryGoToPoint(customNavigationPoint.Position, true))
            {
                return true;
            }

            return false;
        }

        private bool ShouldSprintNow(EnemyInfo goalEnemy)
        {
            if (!shouldSprint || goalEnemy == null || goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!FollowerCombatCommon.IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 toEnemy = enemyAnchor - BotOwner.Position;
            toEnemy.y = 0f;
            return toEnemy.sqrMagnitude > SprintApproachStopDistance * SprintApproachStopDistance;
        }

        private bool TryGoToPoint(Vector3 targetPoint, bool withAttack)
        {
            if (BotOwner.GoToPoint(targetPoint, withAttack, -1f, false, false) != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            CommitAdvancePoint(targetPoint);
            shouldSprint = !Utils.Utils.IsWithinDistance(targetPoint, BotOwner.GetPlayer.Transform.position, 20f);
            return true;
        }

        private void RefreshProgressState()
        {
            if (!BotOwner.Mover.HasPathAndNoComplete)
            {
                stalledProgressChecks = 0;
                lastProgressPosition = BotOwner.Position;
                nextProgressCheckTime = Time.time + ProgressCheckInterval;
                return;
            }

            if (nextProgressCheckTime > Time.time)
            {
                return;
            }

            float minProgressSqr = MinimumProgressDistance * MinimumProgressDistance;
            if ((BotOwner.Position - lastProgressPosition).sqrMagnitude >= minProgressSqr)
            {
                stalledProgressChecks = 0;
            }
            else
            {
                stalledProgressChecks++;
                if (stalledProgressChecks >= StalledRefreshThreshold)
                {
                    ClearCommittedAdvancePoint();
                    nextMoveRefreshTime = 0f;
                    BotOwner.StopMove();
                    stalledProgressChecks = 0;
                }
            }

            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private void RefreshActualMovementDirection()
        {
            Vector3 currentPosition = BotOwner.Position;
            Vector3 delta = Flatten(currentPosition - lastActualMovePosition);
            if (delta.sqrMagnitude > ActualMoveDirectionMinDeltaSqr)
            {
                recentActualMoveDirection = delta.normalized;
                recentActualMoveDirectionTime = Time.time;
                lastActualMovePosition = currentPosition;
                return;
            }

            if (Time.time - recentActualMoveDirectionTime > ActualMoveDirectionMaxAge)
            {
                recentActualMoveDirection = Vector3.zero;
                lastActualMovePosition = currentPosition;
            }
        }

        private bool TryGetRecentActualMovementDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (Time.time - recentActualMoveDirectionTime > ActualMoveDirectionMaxAge ||
                recentActualMoveDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            direction = recentActualMoveDirection;
            return true;
        }

        private void CommitAdvancePoint(Vector3 point)
        {
            committedAdvancePoint = point;
            hasCommittedAdvancePoint = true;
            stalledProgressChecks = 0;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private bool TryContinueToCommittedAdvancePoint()
        {
            if (!hasCommittedAdvancePoint)
            {
                return false;
            }

            if (!IsFinite(committedAdvancePoint) ||
                (committedAdvancePoint - BotOwner.Position).sqrMagnitude <= 0.25f)
            {
                return false;
            }

            if (TryGoToPoint(committedAdvancePoint, true))
            {
                nextMoveRefreshTime = Time.time + 4f;
                return true;
            }

            return false;
        }

        private bool HasCommittedAdvancePoint()
        {
            if (!hasCommittedAdvancePoint)
            {
                return false;
            }

            if (!BotOwner.Mover.TargetPoint.HasValue)
            {
                hasCommittedAdvancePoint = false;
                return false;
            }

            Vector3 targetPoint = BotOwner.Mover.TargetPoint.Value;
            if ((targetPoint - committedAdvancePoint).sqrMagnitude > 1f)
            {
                hasCommittedAdvancePoint = false;
                return false;
            }

            return true;
        }

        private void ClearCommittedAdvancePoint()
        {
            hasCommittedAdvancePoint = false;
            committedAdvancePoint = Vector3.zero;
        }

        private bool TryApplyCommittedLook(EnemyInfo goalEnemy)
        {
            if (committedLookMode == AdvanceLookMode.None || committedLookModeUntil <= Time.time)
            {
                return false;
            }

            switch (committedLookMode)
            {
                case AdvanceLookMode.EnemyOwned:
                    if (TryGetOwnedEnemyLookDirection(goalEnemy, out Vector3 lookDirection))
                    {
                        BotOwner.Steering.LookToDirection(lookDirection.normalized);
                        return true;
                    }
                    return false;

                case AdvanceLookMode.AdvancePoint:
                    if (TryGetAdvanceLookPoint(out Vector3 advancePoint))
                    {
                        BotOwner.Steering.LookToPoint(advancePoint + Vector3.up * 0.5f);
                        return true;
                    }
                    return false;

                case AdvanceLookMode.MovingDirection:
                    if (BotOwner.Mover.HasPathAndNoComplete)
                    {
                        BotOwner.Steering.LookToMovingDirection();
                        return true;
                    }
                    return false;

                case AdvanceLookMode.KeepCurrent:
                    BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
                    return true;
            }

            return false;
        }

        private void CommitLookMode(AdvanceLookMode mode)
        {
            if (mode == committedLookMode && committedLookModeUntil > Time.time)
            {
                return;
            }

            committedLookMode = mode;
            committedLookModeUntil = Time.time + MyExtensions.Random(LookCommitMinSeconds, LookCommitMaxSeconds);
        }

        private bool TryGetAdvanceLookPoint(out Vector3 point)
        {
            point = Vector3.zero;
            Vector3? advanceTarget = GetAdvanceTargetPoint();
            if (!advanceTarget.HasValue || !IsFinite(advanceTarget.Value))
            {
                return false;
            }

            point = advanceTarget.Value;
            return true;
        }

        private bool TryGetMoveTargetDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            Vector3? advanceTarget = GetAdvanceTargetPoint();
            if (!advanceTarget.HasValue)
            {
                return false;
            }

            direction = Flatten(advanceTarget.Value - BotOwner.Position);
            if (direction.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        private bool TryGetEnemyLookDirection(EnemyInfo goalEnemy, out Vector3 direction, out float distance)
        {
            direction = Vector3.zero;
            distance = float.MaxValue;
            if (!TryGetEnemyLookAnchor(goalEnemy, out Vector3 enemyAnchor))
            {
                return false;
            }

            direction = Flatten(enemyAnchor - BotOwner.Position);
            if (direction.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            distance = direction.magnitude;
            direction /= distance;
            return true;
        }

        private bool TryGetEnemyLookAnchor(EnemyInfo goalEnemy, out Vector3 enemyAnchor)
        {
            enemyAnchor = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                enemyAnchor = goalEnemy.GetBodyPartPosition();
            }
            else if (Enemy.TryGetReliableKnownPosition(BotOwner, goalEnemy, out Vector3 knownPosition))
            {
                enemyAnchor = knownPosition + Vector3.up * 0.8f;
            }
            else
            {
                enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy) + Vector3.up * 0.8f;
            }

            return IsFinite(enemyAnchor);
        }

        private bool TryGetEnemyPathAnchor(EnemyInfo goalEnemy, out Vector3 enemyAnchor)
        {
            enemyAnchor = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (IsFinite(enemyAnchor))
            {
                return true;
            }

            enemyAnchor = goalEnemy.CurrPosition;
            return IsFinite(enemyAnchor);
        }

        private void StopAdvance()
        {
            ClearCommittedAdvancePoint();
            BotOwner.StopMove();
            BotOwner.LookData.SetLookPointByHearing(null);
            BotOwner.SetPose(0f);
        }

        private bool ShouldReloadWhileAdvancing(EnemyInfo goalEnemy)
        {
            if (BotOwner?.WeaponManager?.Reload == null || BotOwner.WeaponManager.Reload.Reloading)
            {
                return false;
            }

            if (!BotOwner.WeaponManager.HaveBullets)
            {
                return true;
            }

            if (goalEnemy == null || goalEnemy.Distance < TacticalReloadSafeDistance)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            EFT.InventoryLogic.Magazine currentMagazine = currentWeapon?.GetCurrentMagazine();
            if (currentWeapon == null || currentMagazine == null)
            {
                return false;
            }

            int capacity = currentMagazine.MaxCount;
            int currentBullets = currentMagazine.Count;
            if (capacity <= 0 || currentBullets >= capacity)
            {
                return false;
            }

            return currentBullets <= GetTacticalReloadThreshold(currentWeapon, capacity);
        }

        private static int GetTacticalReloadThreshold(Weapon weapon, int capacity)
        {
            if (capacity <= 0)
            {
                return 0;
            }

            if (IsPistol(weapon))
            {
                return capacity > PistolLargeMagazineLowAmmoThreshold
                    ? PistolLargeMagazineLowAmmoThreshold
                    : capacity / 2;
            }

            if (capacity <= 20)
            {
                return capacity / 2;
            }

            if (capacity > 30)
            {
                return LargeMagazineLowAmmoThreshold;
            }

            return capacity / 2;
        }

        private static bool IsPistol(Weapon weapon)
        {
            return string.Equals(weapon?.Template?.weapClass, "pistol", System.StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetOwnedEnemyLookDirection(EnemyInfo goalEnemy, out Vector3 lookDirection)
        {
            lookDirection = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                lookDirection = goalEnemy.GetBodyPartPosition() - BotOwner.Position;
                return lookDirection.sqrMagnitude > 0.01f;
            }

            if (Time.time - goalEnemy.PersonalLastSeenTime <= 12f)
            {
                Vector3 personalLastPos = goalEnemy.PersonalLastPos;
                bool preferMovementOverPersonal = ShouldPreferMovingDirectionOverStaleLocalLook(goalEnemy);
                if (!preferMovementOverPersonal &&
                    (personalLastPos - BotOwner.Position).sqrMagnitude > 0.01f)
                {
                    lookDirection = personalLastPos - BotOwner.Position;
                    return true;
                }

                Vector3 lastKnownPosition = goalEnemy.EnemyLastPositionReal;
                if ((lastKnownPosition - BotOwner.Position).sqrMagnitude > 0.01f &&
                    (!preferMovementOverPersonal || !IsSameLocalLookPoint(personalLastPos, lastKnownPosition)))
                {
                    lookDirection = lastKnownPosition - BotOwner.Position;
                    return true;
                }
            }

            return false;
        }

        private bool ShouldPreferMovingDirectionOverStaleLocalLook(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null ||
                goalEnemy.IsVisible ||
                goalEnemy.CanShoot ||
                !BotOwner.Mover.HasPathAndNoComplete ||
                !TryGetRecentActualMovementDirection(out Vector3 movementDirection) ||
                !TryGetAdvanceLookPoint(out Vector3 advancePoint))
            {
                return false;
            }

            Vector3 personalLastPos = goalEnemy.PersonalLastPos;
            if (!IsFinite(personalLastPos) ||
                Time.time - goalEnemy.PersonalLastSeenTime < StaleLocalLookMinAge)
            {
                return false;
            }

            Vector3 toPersonal = Flatten(personalLastPos - BotOwner.Position);
            if (toPersonal.sqrMagnitude <= 0.01f ||
                toPersonal.sqrMagnitude > StaleLocalLookMaxDistance * StaleLocalLookMaxDistance)
            {
                return false;
            }

            Vector3 toAdvancePoint = Flatten(advancePoint - BotOwner.Position);
            if (toAdvancePoint.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Angle(movementDirection, toAdvancePoint.normalized) >= StaleLocalLookBackpedalAngle;
        }

        private static bool IsSameLocalLookPoint(Vector3 first, Vector3 second)
        {
            if (!IsFinite(first) || !IsFinite(second))
            {
                return false;
            }

            return Flatten(first - second).sqrMagnitude <= 1f;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
