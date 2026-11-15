using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class FollowAction
    {
        private const float FormationIdleSeconds = 0.75f;
        private const float FormationTravelSeconds = 0.65f;
        private const float FormationRunTravelDistance = 4f;
        private const float FormationWalkTravelDistance = 6f;
        private const float FormationInRangeSprintStartGap = 7f;
        private const float FormationCatchUpSeconds = 3f;
        private const float FormationArrivalDistance = 1.5f;
        private const float FormationSprintStopDistance = 3.5f;
        private const float FormationSprintStartDistance = 7f;
        private const float FormationMovingLeaderSprintStartDistance = 5f;
        private const float FormationNearLeaderDistance = 2.5f;
        private const float FormationSideSpacing = 2.5f;
        private const float FormationRowSpacing = 2.5f;
        private const float FormationTargetRefreshDistance = 1.5f;
        private const float FormationSampleRadius = 0.8f;
        private const float FormationLeadSeconds = 0.35f;
        private const float FormationMaxLeadDistance = 2f;
        private const float FormationApproachRefreshDistance = 5f;
        private const float FormationMinimumRefreshInterval = 0.5f;

        private bool followingFormation;
        private float formationLastLeaderMoveAt;
        private float formationTravelStartedAt = -1f;
        private Vector3 formationTravelStart;
        private float nextFormationPathAt;
        private float nextFormationEarlyRefreshAt;
        private Vector3 formationHeading;
        private Vector3? formationDestination;
        private Vector3 formationLeaderPositionAtRefresh;

        private void ResetFollowFormation()
        {
            ClearFormationRoute();
            formationLastLeaderMoveAt = 0f;
            formationTravelStartedAt = -1f;
        }

        private void ClearFormationRoute()
        {
            if (!followingFormation) return;
            followingFormation = false;
            nextFormationPathAt = 0f;
            nextFormationEarlyRefreshAt = 0f;
            formationHeading = Vector3.zero;
            formationDestination = null;
        }

        private bool UpdateFollowFormation(
            Player leader, Vector3 leaderPosition, float leaderDistance, float followDistance, bool inRange)
        {
            MovementContext? movement = leader.MovementContext;
            if (movement == null) return false;
            Vector3 velocity = movement.Velocity;
            velocity.y = 0f;
            float now = Time.time;
            bool moving = velocity.sqrMagnitude > MaxBossSpeedForSettle * MaxBossSpeedForSettle;
            if (moving)
            {
                if (formationTravelStartedAt < 0f || now - formationLastLeaderMoveAt >= FormationIdleSeconds)
                {
                    formationTravelStartedAt = now;
                    formationTravelStart = leaderPosition;
                }
                formationLastLeaderMoveAt = now;
            }

            if (!followingFormation && !HasLeaderStartedFormationTravel(leaderPosition, now, moving, movement.IsSprintEnabled))
            {
                return false;
            }

            BotMover mover = BotOwner.Mover;
            float idleTime = now - formationLastLeaderMoveAt;
            bool approachingFormation = formationDestination is Vector3 destination &&
                mover.HasPathAndNoComplete &&
                (destination - BotOwner.Position).sqrMagnitude > FormationArrivalDistance * FormationArrivalDistance;
            if (!moving && inRange && idleTime >= FormationIdleSeconds &&
                (!approachingFormation || idleTime >= FormationCatchUpSeconds))
            {
                return false;
            }

            if (!followingFormation)
            {
                followingFormation = true;
                movingToSettlePoint = false;
                ReleaseSettleDestinationClaim();
                coverCommitment.ClearCommitment();
            }

            bool leaderRunning = moving && movement.IsSprintEnabled;
            bool refreshEarly = leaderRunning && now >= nextFormationEarlyRefreshAt &&
                (!mover.HasPathAndNoComplete || (formationDestination is Vector3 current &&
                 (current - BotOwner.Position).sqrMagnitude <=
                 FormationApproachRefreshDistance * FormationApproachRefreshDistance));
            if ((now >= nextFormationPathAt || refreshEarly) && (moving || !approachingFormation))
            {
                nextFormationPathAt = now + Utils.Utils.Random(1f, 1.5f);
                nextFormationEarlyRefreshAt = now + FormationMinimumRefreshInterval;
                RefreshFormationPath(leaderPosition, leaderDistance, followDistance, inRange, velocity);
            }

            float separation = GetFormationPaceDistance(leaderPosition, leaderDistance, leaderRunning);
            float startDistance = leaderRunning
                ? FormationMovingLeaderSprintStartDistance
                : FormationSprintStartDistance;
            if (!formationDestination.HasValue)
            {
                startDistance = Mathf.Min(followDistance + 3f, 16f) + FollowSprintStartMargin;
            }

            bool canStartSprint = leaderRunning
                ? !inRange || leaderDistance >= FormationInRangeSprintStartGap
                : !inRange && leaderDistance >= Mathf.Min(followDistance + 3f, 16f) + FollowSprintStartMargin;
            followSprintRequested = (followSprintRequested || canStartSprint) &&
                mover.HasPathAndNoComplete && !BotOwner.DoorOpener.NearDoor &&
                mover.CurrentState == EBotMoverState.Default &&
                FollowerSprintDistancePolicy.ShouldSprint(
                    separation, followSprintRequested, startDistance, FormationSprintStopDistance);
            ApplyFollowChaseMovementState(followSprintRequested);
            return true;
        }

        private float GetFormationPaceDistance(Vector3 leaderPosition, float leaderDistance, bool leaderRunning)
        {
            if (formationDestination is not Vector3 destination) return leaderDistance;
            if (leaderRunning)
            {
                float travel = Vector3.Dot(leaderPosition - formationLeaderPositionAtRefresh, formationHeading);
                destination += formationHeading * Mathf.Clamp(travel, 0f, FormationApproachRefreshDistance);
            }
            return Vector3.Distance(BotOwner.Position, destination);
        }

        private bool HasLeaderStartedFormationTravel(Vector3 leaderPosition, float now, bool moving, bool sprinting)
        {
            if (!moving || now - formationTravelStartedAt < FormationTravelSeconds) return false;

            Vector3 displacement = leaderPosition - formationTravelStart;
            displacement.y = 0f;
            float minimumDistance = sprinting ? FormationRunTravelDistance : FormationWalkTravelDistance;
            return displacement.sqrMagnitude >= minimumDistance * minimumDistance;
        }

        private void RefreshFormationPath(
            Vector3 leaderPosition, float leaderDistance, float followDistance, bool inRange, Vector3 leaderVelocity)
        {
            BotMover mover = BotOwner.Mover;
            if (leaderVelocity.sqrMagnitude > MaxBossSpeedForSettle * MaxBossSpeedForSettle)
            {
                Vector3 travelDirection = leaderVelocity.normalized;
                formationHeading = Vector3.Dot(formationHeading, travelDirection) > 0f
                    ? Vector3.Lerp(formationHeading, travelDirection, 0.6f).normalized
                    : travelDirection;
            }

            if (!BotOwner.DoorOpener.NearDoor && mover.CurrentState == EBotMoverState.Default &&
                TryGetFormationPoint(leaderPosition, leaderVelocity.magnitude, followDistance, out Vector3 point))
            {
                if ((point - BotOwner.Position).sqrMagnitude <= FormationArrivalDistance * FormationArrivalDistance)
                {
                    if (mover.HasPathAndNoComplete) BotOwner.StopMove();
                    formationDestination = point;
                    formationLeaderPositionAtRefresh = leaderPosition;
                    return;
                }

                if (mover.HasPathAndNoComplete && mover.TargetPoint is Vector3 activeTarget &&
                    formationDestination is Vector3 current &&
                    (activeTarget - current).sqrMagnitude <= FormationSampleRadius * FormationSampleRadius &&
                    (point - current).sqrMagnitude < FormationTargetRefreshDistance * FormationTargetRefreshDistance)
                {
                    return;
                }

                FollowPathResult result = SetFollowPath(point, "formation", onlyShortTrie: true);
                if (result == FollowPathResult.Deferred) return;
                if (result == FollowPathResult.Accepted)
                {
                    formationDestination = point;
                    formationLeaderPositionAtRefresh = leaderPosition;
                    isPathBlocked = false;
                    return;
                }
            }

            if (inRange && leaderDistance <= FormationNearLeaderDistance &&
                !BotOwner.DoorOpener.NearDoor && mover.CurrentState == EBotMoverState.Default)
            {
                if (mover.HasPathAndNoComplete) BotOwner.StopMove();
                formationDestination = null;
                return;
            }
            UpdateFollowPath(leaderPosition);
            if (!mover.HasPathAndNoComplete || mover.TargetPoint is not Vector3 retainedTarget ||
                formationDestination is not Vector3 previousTarget ||
                (retainedTarget - previousTarget).sqrMagnitude > FormationSampleRadius * FormationSampleRadius)
            {
                formationDestination = null;
            }
        }

        private bool TryGetFormationPoint(Vector3 leaderPosition, float leaderSpeed, float followDistance, out Vector3 point)
        {
            point = default;
            if (formationHeading == Vector3.zero || bossData == null) return false;

            // Use roster order so followers keep their side as they overtake one another.
            int slot = bossData.Followers.IndexOf(BotOwner);
            if (slot < 0) return false;
            int side = slot % 2 == 0 ? -1 : 1;
            float trailingDistance = 3.5f + Mathf.Min(slot / 2, 2) * FormationRowSpacing;
            trailingDistance -= Mathf.Min(leaderSpeed * FormationLeadSeconds, FormationMaxLeadDistance);
            float lateralDistance = FormationSideSpacing * (1 + slot / 6);
            Vector3 right = Vector3.Cross(Vector3.up, formationHeading);
            Vector3 offset = -formationHeading * trailingDistance + right * (side * lateralDistance);
            Vector3 desired = leaderPosition + Vector3.ClampMagnitude(offset, followDistance - FormationSampleRadius);
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, FormationSampleRadius, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(leaderPosition, out NavMeshHit leaderHit, FormationSampleRadius, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - leaderPosition.y) > FormationSampleRadius ||
                Mathf.Abs(leaderHit.position.y - leaderPosition.y) > FormationSampleRadius ||
                NavMesh.Raycast(leaderHit.position, hit.position, out _, NavMesh.AllAreas))
            {
                return false;
            }

            point = hit.position;
            return true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordFollowSample(Vector3 leaderPosition, bool inRange)
        {
            BattleRecorder.RecordPeacefulFollowSample(
                BotOwner, leaderPosition, inRange, movingToSettlePoint, followingFormation, formationDestination,
                bossPlayer?.ProfileId);
        }
    }
}
