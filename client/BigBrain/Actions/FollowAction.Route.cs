using System;
using EFT;
using pitTeam.Modules;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class FollowAction
    {
        private const float FollowRouteProgressInterval = 0.2f;
        private const float FollowRouteProgressLifetime = 0.4f;
        private const float FollowRouteMinimumProgress = 0.1f;
        private const float FollowRouteRetryDelay = 0.4f;
        private const float FollowRouteOriginSampleRadius = 0.15f;
        private const float FollowRouteReverseDot = -0.5f;
        private const float FollowRouteLeaderTurnDistance = 1f;
        private const int FollowRouteJoinCornerLimit = 3;

        private enum FollowPathResult { Failed, Accepted, Deferred }

        private AbstractBotPath? observedFollowPath;
        private Vector3 followRouteProgressPosition;
        private float nextFollowRouteProgressAt;
        private float lastFollowRouteProgressAt;
        private float deferredFollowRouteUntil;

        private void ResetFollowRouteProgress()
        {
            observedFollowPath = null;
            nextFollowRouteProgressAt = 0f;
            lastFollowRouteProgressAt = float.NegativeInfinity;
            deferredFollowRouteUntil = 0f;
        }

        private void ObserveFollowRouteProgress()
        {
            BotMover mover = BotOwner.Mover;
            AbstractBotPath? path = mover.HasPathAndNoComplete ? mover.ActualPathController.CurPath : null;
            float now = Time.time;
            if (!ReferenceEquals(path, observedFollowPath))
            {
                ResetFollowRouteProgress();
                observedFollowPath = path;
                followRouteProgressPosition = BotOwner.Position;
                nextFollowRouteProgressAt = now + FollowRouteProgressInterval;
                return;
            }
            if (path == null || now < nextFollowRouteProgressAt) return;

            Vector3 displacement = BotOwner.Position - followRouteProgressPosition;
            if (Vector3.Dot(displacement, FollowPlanarDirection(mover.DirCurPoint)) >= FollowRouteMinimumProgress)
            {
                lastFollowRouteProgressAt = now;
            }
            followRouteProgressPosition = BotOwner.Position;
            nextFollowRouteProgressAt = now + FollowRouteProgressInterval;
        }

        private FollowPathResult SetFollowPath(Vector3 position, string source, bool onlyShortTrie = false)
        {
            BotMover mover = BotOwner.Mover;
            BotPathController controller = mover.ActualPathController;
            AbstractBotPath? previousPath = mover.HasPathAndNoComplete ? controller.CurPath : null;
            int skippedCorners = 0;
            NavMeshPathStatus status;
            if (previousPath != null && mover.CurrentState == EBotMoverState.Default && !BotOwner.DoorOpener.NearDoor &&
                !controller.IsSameWay(position, mover.PositionOnWay) &&
                BotPathFinderCorePoints.FindPath(BotOwner.Position, position, out Vector3[] corners) &&
                corners.Length > 0)
            {
                Vector3 origin = mover.PositionOnWay;
                Vector3 forward = FollowPlanarDirection(mover.DirCurPoint);
                int firstLeg = FirstFollowLeg(corners, origin);
                bool backwardStart = firstLeg < corners.Length &&
                    Vector3.Dot(forward, FollowPlanarDirection(corners[firstLeg] - origin)) <= FollowRouteReverseDot;
                if (backwardStart && !IsLeaderTurningBack(position, previousPath.TargetPoint.Position, forward))
                {
                    skippedCorners = FindForwardJoin(corners, firstLeg, origin, forward);
                    if (skippedCorners == 0 && ReferenceEquals(previousPath, observedFollowPath) &&
                        Time.time - lastFollowRouteProgressAt <= FollowRouteProgressLifetime &&
                        (deferredFollowRouteUntil == 0f || Time.time < deferredFollowRouteUntil))
                    {
                        if (deferredFollowRouteUntil == 0f)
                        {
                            deferredFollowRouteUntil = Time.time + FollowRouteRetryDelay;
                        }
                        nextFollowUpdateAt = nextFormationPathAt = deferredFollowRouteUntil;
                        BattleRecorder.RecordPeacefulFollowPath(
                            BotOwner, source, position, NavMeshPathStatus.PathComplete, 0, deferred: true);
                        return FollowPathResult.Deferred;
                    }
                }

                status = CommitPreparedFollowPath(mover, controller, position, corners, skippedCorners);
            }
            else
            {
                status = BotOwner.GoToPoint(position, slowAtTheEnd: true,
                    mustHaveWay: false, onlyShortTrie: onlyShortTrie);
            }

            if (status == NavMeshPathStatus.PathComplete)
            {
                // Animation motion queued on the old route must not advance the new cursor.
                if (controller.CurPath != previousPath && mover is BotMoverImpostor impostor &&
                    impostor._lastContext.NextTargetPath is AbstractBotPath pendingPath &&
                    !ReferenceEquals(pendingPath, mover.ActualPathController.CurPath))
                {
                    impostor._lastContext.Used = true;
                }
                if (!TryApplyCommandLookOverride())
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }

            BattleRecorder.RecordPeacefulFollowPath(BotOwner, source, position, status,
                status == NavMeshPathStatus.PathComplete ? skippedCorners : 0);
            return status == NavMeshPathStatus.PathComplete ? FollowPathResult.Accepted : FollowPathResult.Failed;
        }

        private NavMeshPathStatus CommitPreparedFollowPath(
            BotMover mover, BotPathController controller, Vector3 target, Vector3[] corners, int skippedCorners)
        {
            BotPathFinderCorePoints pathFinder = mover.ActualPathFinder;
            if (!pathFinder.PreStartWay(target, slowAtTheEnd: true, getUpWithCheck: true))
            {
                return NavMeshPathStatus.PathInvalid;
            }
            if (skippedCorners > 0)
            {
                var remaining = new Vector3[corners.Length - skippedCorners];
                Array.Copy(corners, skippedCorners, remaining, 0, remaining.Length);
                corners = remaining;
            }
            pathFinder._pathFindErrorsInRow = 0;
            controller.GoToByWay(corners, BotOwner.Settings.FileSettings.Move.REACH_DIST);
            return NavMeshPathStatus.PathComplete;
        }

        private bool IsLeaderTurningBack(Vector3 destination, Vector3 previousDestination, Vector3 forward)
        {
            Vector3 velocity = bossPlayer?.MovementContext?.Velocity ?? Vector3.zero;
            return Vector3.Dot(velocity, forward) <= -MaxBossSpeedForSettle &&
                   Vector3.Dot(destination - previousDestination, forward) <= -FollowRouteLeaderTurnDistance;
        }

        private static int FirstFollowLeg(Vector3[] corners, Vector3 origin)
        {
            // Match the mover's treatment of a path sample already under its navigation position.
            int index = 0;
            while (index < corners.Length - 1 && FollowPlanarDirection(corners[index] - origin) == Vector3.zero)
            {
                index++;
            }
            return index;
        }

        private static int FindForwardJoin(Vector3[] corners, int firstLeg, Vector3 origin, Vector3 forward)
        {
            if (!NavMesh.SamplePosition(origin, out NavMeshHit hit, FollowRouteOriginSampleRadius, NavMesh.AllAreas))
            {
                return 0;
            }
            for (int i = firstLeg + 1; i < corners.Length && i <= FollowRouteJoinCornerLimit; i++)
            {
                if (Vector3.Dot(forward, FollowPlanarDirection(corners[i] - origin)) > 0f &&
                    !NavMesh.Raycast(hit.position, corners[i], out _, NavMesh.AllAreas))
                {
                    return i;
                }
            }
            return 0;
        }

        private static Vector3 FollowPlanarDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.zero : direction.normalized;
        }
    }
}
