using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Decisive sprint push toward an enemy or committed push destination. Unlike go-to-enemy, this
    /// action prioritizes closing distance quickly, while still refreshing pathing, handling stairs,
    /// and choosing safe look modes during the sprint.
    /// </summary>
    internal sealed class CombatRunToEnemyAction : FollowerCombatActionBase
    {
        private enum MovementMode
        {
            Run,
            Walk
        }

        private enum RunLookMode
        {
            None,
            Enemy,
            ThreatAnchor,
            MovingDirection,
            KeepCurrent
        }

        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.4f;
        private const int StalledRefreshThreshold = 3;
        private const float MinEnemyRunPointDistance = 5f;
        private const float MaxEnemyRunPointDistance = 10f;
        private const float RunPointNavSampleRadius = 2.25f;
        private const float LookCommitMinSeconds = 0.2f;
        private const float LookCommitMaxSeconds = 0.4f;
        private const float CloseKnownThreatLookDistance = 12f;
        private const float CloseKnownThreatStopDistance = 4f;
        private const float CloseKnownThreatBadLookAngle = 65f;
        private const float CloseKnownThreatBadPathAngle = 75f;
        private const float WalkingThreatLookDistance = 90f;
        private static readonly float[] RunPointDistances = { 8f, 10f, 6.5f, 5f };
        private static readonly float[] RunPointAngles = { 0f, -25f, 25f, -50f, 50f, -90f, 90f, 180f };

        private readonly CombatGoToEnemyAction walkFallback;
        private readonly FallbackRunRestoreGate restoreRunGate = new FallbackRunRestoreGate();
        private MovementMode movementMode;
        private float nextMoveRefreshTime;
        private Vector3 committedRunPoint;
        private bool hasCommittedRunPoint;
        private Vector3 lastProgressPosition;
        private float nextProgressCheckTime;
        private int stalledProgressChecks;
        private RunLookMode committedLookMode;
        private float committedLookModeUntil;

        public CombatRunToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            walkFallback = new CombatGoToEnemyAction(botOwner);
        }

        public override void Start()
        {
            base.Start();
            movementMode = MovementMode.Run;
            restoreRunGate.Reset();
            StartRunMode();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            bool canRun = CanActuallyRun();
            if (movementMode == MovementMode.Walk)
            {
                UpdateWalkFallback(data, canRun);
                return;
            }

            if (!canRun)
            {
                SwitchToWalkFallback(data);
                return;
            }

            UpdateRun(data, canRun);
        }

        public override void Stop()
        {
            if (movementMode == MovementMode.Walk)
            {
                walkFallback.Stop();
            }
            else
            {
                StopRunMode();
            }

            base.Stop();
        }

        private void StartRunMode()
        {
            nextMoveRefreshTime = 0f;
            committedRunPoint = Vector3.zero;
            hasCommittedRunPoint = false;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = 0f;
            stalledProgressChecks = 0;
            committedLookMode = RunLookMode.None;
            committedLookModeUntil = 0f;

            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy != null)
            {
                TryMoveToEnemy(goalEnemy);
            }
            SetCombatSprint(true);
        }

        private void StopRunMode()
        {
            ClearCommittedRunPoint();
            SetCombatSprint(false);
            committedLookMode = RunLookMode.None;
            committedLookModeUntil = 0f;
        }

        private void SwitchToWalkFallback(CustomLayer.ActionData data)
        {
            StopRunMode();
            movementMode = MovementMode.Walk;
            restoreRunGate.Reset();
            walkFallback.Start();
            walkFallback.Update(data);
        }

        private void UpdateWalkFallback(CustomLayer.ActionData data, bool canRun)
        {
            walkFallback.Update(data);

            if (!restoreRunGate.ShouldRestoreToRun(canRun, BotOwner.Memory?.GoalEnemy))
            {
                return;
            }

            walkFallback.Stop();
            movementMode = MovementMode.Run;
            restoreRunGate.Reset();
            StartRunMode();
            UpdateRun(data, true);
        }

        private void UpdateRun(CustomLayer.ActionData data, bool canRun)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            if (TryStopUnsafeCloseKnownThreatAdvance(goalEnemy))
            {
                return;
            }

            if (!HasCommittedRunPoint() && ShouldStopForImmediateFire(goalEnemy))
            {
                StopRunForFire(goalEnemy);
                return;
            }

            BotOwner.SetTargetMoveSpeed(1f);
            RefreshProgressState();
            NotMovingCheck(goalEnemy);
            string? reason = GetReason(data);
            TryPreferPrimaryAtRange(goalEnemy, reason);
            if (HoldPushMovementUntilLongGunReady(reason))
            {
                return;
            }

            BotOwner.SetPose(1f);
            SetCombatSprint(canRun);
            UpdateLook(goalEnemy, canRun);

            if (!BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false, null))
            {
                return;
            }

            ClearCommittedRunPoint();
            BotOwner.StopMove();
            if (!TryContinueAdvance(goalEnemy))
            {
                BotOwner.StopMove();
            }
        }

        private bool CanActuallyRun()
        {
            if (!BotOwner.CanSprintPlayer || BotOwner.Mover?.NoSprint == true)
            {
                return false;
            }

            Player? player = BotOwner.GetPlayer ?? BotOwner.AIData?.Player;
            if (player?.HealthController != null &&
                (player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg)))
            {
                return false;
            }

            DoorInteractionStatus doorStatus = BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            return !IsDoorInteractionBlockingSprint(doorStatus);
        }

        private void NotMovingCheck(EnemyInfo goalEnemy)
        {
            if (nextMoveRefreshTime > Time.time)
            {
                return;
            }

            nextMoveRefreshTime = Time.time + 3f;
            if (BotOwner.Mover.HasPathAndNoComplete && HasCommittedRunPoint())
            {
                return;
            }

            ClearCommittedRunPoint();
            TryMoveToEnemy(goalEnemy);
        }

        private bool TryContinueAdvance(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            nextMoveRefreshTime = Time.time + 0.25f;
            return TryMoveToEnemy(goalEnemy);
        }

        private bool ShouldStopForImmediateFire(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (!BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            return shootPoint != null &&
                   Utils.Utils.CanShootToTarget(shootPoint, BotOwner.WeaponRoot.position, BotOwner.LookSensor.Mask, false);
        }

        private void StopRunForFire(EnemyInfo goalEnemy)
        {
            ClearCommittedRunPoint();
            BotOwner.StopMove();
            SetCombatSprint(false);
            BotOwner.SetPose(1f);
            CommitLookMode(RunLookMode.Enemy);
            BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
        }

        private void UpdateLook(EnemyInfo goalEnemy, bool canRun)
        {
            if (BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                return;
            }

            if (TryApplyCommittedLook(goalEnemy, canRun))
            {
                return;
            }

            if (TryLookAtCloseKnownThreat(goalEnemy))
            {
                return;
            }

            if (!BotOwner.Mover.Sprinting && TryLookAtKnownThreat(goalEnemy, WalkingThreatLookDistance))
            {
                return;
            }

            if (BotOwner.Mover.Sprinting && BotOwner.Mover.HasPathAndNoComplete)
            {
                CommitLookMode(RunLookMode.MovingDirection);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.LookData.SetLookPointByHearing(null);
            CommitLookMode(RunLookMode.KeepCurrent);
            BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
        }

        private bool TryApplyCommittedLook(EnemyInfo goalEnemy, bool canRun)
        {
            if (committedLookMode == RunLookMode.None || committedLookModeUntil <= Time.time)
            {
                return false;
            }

            switch (committedLookMode)
            {
                case RunLookMode.Enemy:
                    if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot)
                    {
                        BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
                        return true;
                    }

                    return false;

                case RunLookMode.ThreatAnchor:
                    if (TryGetThreatLookPoint(
                            goalEnemy,
                            WalkingThreatLookDistance,
                            out Vector3 enemyAnchor))
                    {
                        BotOwner.Steering.LookToPoint(enemyAnchor);
                        return true;
                    }

                    return false;

                case RunLookMode.MovingDirection:
                    if (BotOwner.Mover.Sprinting && BotOwner.Mover.HasPathAndNoComplete)
                    {
                        BotOwner.Steering.LookToMovingDirection();
                        return true;
                    }

                    return false;

                case RunLookMode.KeepCurrent:
                    BotOwner.LookData.SetLookPointByHearing(null);
                    BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
                    return true;
            }

            return false;
        }

        private bool TryLookAtKnownThreat(EnemyInfo goalEnemy, float maxDistance)
        {
            if (!TryGetThreatLookPoint(goalEnemy, maxDistance, out Vector3 lookPoint))
            {
                return false;
            }

            BotOwner.LookData.SetLookPointByHearing(null);
            CommitLookMode(RunLookMode.ThreatAnchor);
            BotOwner.Steering.LookToPoint(lookPoint);
            return true;
        }

        private bool TryGetThreatLookPoint(EnemyInfo goalEnemy, float maxDistance, out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            Vector3 anchor;
            if (goalEnemy.IsVisible)
            {
                anchor = goalEnemy.GetBodyPartPosition();
            }
            else if (Enemy.TryGetReliableKnownPosition(BotOwner, goalEnemy, out Vector3 knownPosition))
            {
                anchor = knownPosition + Vector3.up * 0.8f;
            }
            else
            {
                anchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy) + Vector3.up * 0.8f;
            }

            if (!IsFinite(anchor))
            {
                return false;
            }

            Vector3 flat = Flatten(anchor - BotOwner.Position);
            if (flat.sqrMagnitude <= 0.01f || flat.magnitude > maxDistance)
            {
                return false;
            }

            lookPoint = anchor;
            return true;
        }

        private void CommitLookMode(RunLookMode mode)
        {
            if (mode == committedLookMode && committedLookModeUntil > Time.time)
            {
                return;
            }

            committedLookMode = mode;
            committedLookModeUntil = Time.time + GClass856.Random(LookCommitMinSeconds, LookCommitMaxSeconds);
        }

        private bool TryStopUnsafeCloseKnownThreatAdvance(EnemyInfo goalEnemy)
        {
            if (!TryGetCloseKnownThreatData(
                    goalEnemy,
                    CloseKnownThreatLookDistance,
                    out Vector3 enemyAnchor,
                    out Vector3 toEnemy,
                    out float distance))
            {
                return false;
            }

            float lookAngle = Vector3.Angle(Flatten(BotOwner.LookDirection), toEnemy);
            bool pointBlank = distance <= CloseKnownThreatStopDistance;
            bool badLook = lookAngle >= CloseKnownThreatBadLookAngle;
            bool badPath = TryGetMoveTargetDirection(out Vector3 toMoveTarget) &&
                           Vector3.Angle(toMoveTarget, toEnemy) >= CloseKnownThreatBadPathAngle;

            if (!pointBlank && !badLook && !badPath)
            {
                return false;
            }

            ClearCommittedRunPoint();
            BotOwner.StopMove();
            SetCombatSprint(false);
            BotOwner.SetPose(1f);
            LookAtThreatAnchor(enemyAnchor);
            return true;
        }

        private bool TryLookAtCloseKnownThreat(EnemyInfo goalEnemy)
        {
            if (!TryGetCloseKnownThreatData(
                    goalEnemy,
                    CloseKnownThreatLookDistance,
                    out Vector3 enemyAnchor,
                    out _,
                    out _))
            {
                return false;
            }

            LookAtThreatAnchor(enemyAnchor);
            return true;
        }

        private bool TryGetCloseKnownThreatData(
            EnemyInfo goalEnemy,
            float maxDistance,
            out Vector3 enemyAnchor,
            out Vector3 toEnemy,
            out float distance)
        {
            enemyAnchor = Vector3.zero;
            toEnemy = Vector3.zero;
            distance = float.MaxValue;

            if (goalEnemy == null || goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (!Enemy.TryGetReliableKnownPosition(BotOwner, goalEnemy, out enemyAnchor))
            {
                enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            }

            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            toEnemy = Flatten(enemyAnchor - BotOwner.Position);
            if (toEnemy.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            distance = toEnemy.magnitude;
            if (distance > maxDistance)
            {
                return false;
            }

            toEnemy /= distance;
            return true;
        }

        private bool TryGetMoveTargetDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!BotOwner.Mover.TargetPoint.HasValue)
            {
                return false;
            }

            direction = Flatten(BotOwner.Mover.TargetPoint.Value - BotOwner.Position);
            if (direction.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            direction.Normalize();
            return true;
        }

        private void LookAtThreatAnchor(Vector3 enemyAnchor)
        {
            BotOwner.LookData.SetLookPointByHearing(null);
            CommitLookMode(RunLookMode.ThreatAnchor);
            BotOwner.Steering.LookToPoint(enemyAnchor + Vector3.up * 0.8f);
        }

        private bool TryMoveToEnemy(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (TryFindEnemyRunPoint(goalEnemy, out Vector3 attackPoint) &&
                TryMoveToPoint(attackPoint))
            {
                return true;
            }

            return TryMoveToEnemyFallback(goalEnemy.CurrPosition);
        }

        private bool TryMoveToEnemyFallback(Vector3 targetPoint)
        {
            if (BotOwner.MoveToEnemyData.method_0(targetPoint, out Vector3 currentTargetPos) &&
                TryMoveToPoint(currentTargetPos))
            {
                return true;
            }

            if (BotOwner.Mover.HasPathAndNoComplete &&
                HasCommittedRunPoint() &&
                !BotOwner.MoveToEnemyData.ShallRecalWay(out _) &&
                Time.time - BotOwner.Mover.LastPathSetTime < 10f)
            {
                return true;
            }

            if (BotOwner.GoToPoint(targetPoint, true, -1f, false, false) == NavMeshPathStatus.PathComplete &&
                BotOwner.Mover.TargetPoint is Vector3 curPathLastPoint &&
                (targetPoint - curPathLastPoint).magnitude < 2f)
            {
                CommitRunPoint(curPathLastPoint);
                return true;
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                TryMoveToPoint(navMeshHit.position, false))
            {
                return true;
            }

            CustomNavigationPoint fallbackPoint = null;
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.hide);
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f, searchTypeOverride: searchType);
            if (closePoints.Count > 0)
            {
                fallbackPoint = closePoints.RandomElement();
            }

            if (fallbackPoint == null)
            {
                fallbackPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f, searchTypeOverride: searchType);
            }

            if (fallbackPoint != null &&
                Mathf.Abs(fallbackPoint.Position.y - targetPoint.y) <= VerticalTolerance &&
                TryMoveToPoint(fallbackPoint.Position))
            {
                return true;
            }

            return false;
        }

        private bool TryMoveToPoint(Vector3 targetPoint, bool withAttack = true)
        {
            if (BotOwner.GoToPoint(targetPoint, withAttack, -1f, false, false) != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            CommitRunPoint(targetPoint);
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
                    ClearCommittedRunPoint();
                    nextMoveRefreshTime = 0f;
                    BotOwner.StopMove();
                    stalledProgressChecks = 0;
                }
            }

            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private void CommitRunPoint(Vector3 point)
        {
            committedRunPoint = point;
            hasCommittedRunPoint = true;
            stalledProgressChecks = 0;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private bool HasCommittedRunPoint()
        {
            if (!hasCommittedRunPoint)
            {
                return false;
            }

            if (!BotOwner.Mover.TargetPoint.HasValue)
            {
                hasCommittedRunPoint = false;
                return false;
            }

            Vector3 targetPoint = BotOwner.Mover.TargetPoint.Value;
            if ((targetPoint - committedRunPoint).sqrMagnitude > 1f)
            {
                hasCommittedRunPoint = false;
                return false;
            }

            return true;
        }

        private bool TryFindEnemyRunPoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 enemyPosition = goalEnemy.CurrPosition;
            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            Vector3 baseDirection = BotOwner.Position - enemyPosition;
            baseDirection.y = 0f;
            if (baseDirection.sqrMagnitude <= 0.01f)
            {
                baseDirection = BotOwner.Transform != null ? -BotOwner.Transform.forward : -Vector3.forward;
                baseDirection.y = 0f;
            }

            if (baseDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            baseDirection.Normalize();
            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true) ?? new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);

            Vector3 fallbackPoint = Vector3.zero;
            bool hasFallbackPoint = false;
            for (int distanceIndex = 0; distanceIndex < RunPointDistances.Length; distanceIndex++)
            {
                float distance = RunPointDistances[distanceIndex];
                for (int angleIndex = 0; angleIndex < RunPointAngles.Length; angleIndex++)
                {
                    Vector3 direction = Quaternion.Euler(0f, RunPointAngles[angleIndex], 0f) * baseDirection;
                    Vector3 candidate = enemyPosition + direction * distance;
                    if (!TrySampleRunPoint(candidate, enemyPosition, out Vector3 navPoint))
                    {
                        continue;
                    }

                    if (!hasFallbackPoint)
                    {
                        fallbackPoint = navPoint;
                        hasFallbackPoint = true;
                    }

                    if (CanShootEnemyFromPoint(shootPoint, navPoint))
                    {
                        point = navPoint;
                        return true;
                    }
                }
            }

            if (hasFallbackPoint)
            {
                point = fallbackPoint;
                return true;
            }

            return false;
        }

        private static bool TrySampleRunPoint(Vector3 candidate, Vector3 enemyPosition, out Vector3 navPoint)
        {
            navPoint = Vector3.zero;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, RunPointNavSampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 sampledPoint = navMeshHit.position;
            float enemyDistanceSqr = (sampledPoint - enemyPosition).sqrMagnitude;
            return enemyDistanceSqr >= MinEnemyRunPointDistance * MinEnemyRunPointDistance &&
                   enemyDistanceSqr <= MaxEnemyRunPointDistance * MaxEnemyRunPointDistance &&
                   Mathf.Abs(sampledPoint.y - enemyPosition.y) <= VerticalTolerance;
        }

        private bool CanShootEnemyFromPoint(ShootPointClass shootPoint, Vector3 point)
        {
            if (shootPoint == null)
            {
                return false;
            }

            Vector3 weaponOffset = BotOwner.ShootData != null ? BotOwner.ShootData.WeaponRootOffset : Vector3.up * 1.4f;
            return Utils.Utils.CanShootToTarget(shootPoint, point + weaponOffset, BotOwner.LookSensor.Mask, false) ||
                   Utils.Utils.CanShootToTarget(shootPoint, point + weaponOffset * 0.5f, BotOwner.LookSensor.Mask, false);
        }

        private void ClearCommittedRunPoint()
        {
            hasCommittedRunPoint = false;
            committedRunPoint = Vector3.zero;
        }

        private void StopAdvance()
        {
            ClearCommittedRunPoint();
            BotOwner.StopMove();
            BotOwner.LookData.SetLookPointByHearing(null);
            SetCombatSprint(false);
            BotOwner.SetPose(0f);
            committedLookMode = RunLookMode.None;
            committedLookModeUntil = 0f;
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
