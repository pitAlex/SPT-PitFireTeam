using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Out-of-combat follower patrol action. It keeps the follower near the boss, chooses stable
    /// settle/cover positions, resumes chase when out of range, and plays peaceful look/watch actions
    /// when no command or combat layer owns the bot.
    /// </summary>
    internal class FollowAction : CustomLogic
    {
        private const float FallbackFollowDistance = 12f;
        private const bool EnableFollowDebug = false;
        private const float MaxBossSpeedForSettle = 0.35f;
        private const float SettleSpacing = 2.5f;
        private const float SettleSpacingSqr = SettleSpacing * SettleSpacing;
        private const float SettlePathSpacing = 1.5f;
        private const float SettlePathSpacingSqr = SettlePathSpacing * SettlePathSpacing;
        private const float SettlePathStartPadding = 0.75f;
        private const float SettlePathEndPadding = 0.4f;
        private const float SettleDestinationClaimTtlSeconds = 3f;
        private const float SettleDestinationClaimReleaseTolerance = 0.5f;
        private const float FollowPathRecoveryCooldown = 0.5f;
        private const float FollowSprintEngagementFailureSeconds = 1.25f;
        private const float FollowWalkFallbackSeconds = 3f;
        private const float PatrolLeaderIdleRequiredSeconds = 5f;
        private const float PatrolLeaderMoveInputThresholdSqr = 0.01f;
        private const float PatrolLeaderMoveSampleInterval = 0.35f;
        private const float PatrolLeaderMoveSampleDeltaThresholdSqr = 0.09f;
        // Patrol camp anchor radius. Active On Your Own patrol only reanchors when
        // the boss/player moves this far from the remembered patrol anchor.
        private const float PatrolCampRadius = 20f;

        private Player? bossPlayer;
        private pitAIBossPlayer? bossData;
        private BotFollowerPlayer? followerData;

        private float nextFollowUpdateAt;
        private float nextFollowPathRecoveryAt;
        private float followSprintEngagementFailureSince;
        private float followWalkFallbackUntil;
        private float nextSettlePointAt;
        private bool isPathBlocked;

        private float holdPositionUntil;
        private float nextPatrolUpdateAt;
        private bool movingToPatrolPoint;
        private bool movingToSettlePoint;
        private bool patrolEnabled;
        private bool hasClaimedSettleDestination;
        private Vector3 claimedSettleDestination;

        private bool isPatrolCampInitialized;
        private Vector3 activePatrolCampCenter;
        private bool returningToLeaderSector;
        private bool patrolRuntimeArmed;
        private bool hasLastPatrolLeaderPosition;
        private Vector3 lastPatrolLeaderPosition;
        private float patrolLeaderStoppedSince;
        private float nextPatrolLeaderMoveSampleAt;

        private CustomNavigationPoint? lastCoverPoint;
        private bool noCoverFound;
        private float patrolPerimeterRadius;

        private bool playPeacefulActions;
        private bool playPeaceLook;
        private bool playPeaceHardAim;
        private bool playSecondWeaponWatch;

        private bool poseCorrected;

        /// <summary>
        /// Tracks committed cover point and sector anchor for stable hold behavior.
        /// Prevents constant cover-switching by committing to a cover within a 20m sector.
        /// </summary>
        private FollowerCoverCommitment coverCommitment;

        public FollowAction(BotOwner botOwner) : base(botOwner)
        {
            patrolPerimeterRadius = pitFireTeam.patrolRadius.Value;
            coverCommitment = new FollowerCoverCommitment();
        }

        public override void Start()
        {
            base.Start();
            ReleaseSettleDestinationClaim();
            isPathBlocked = false;
            isPatrolCampInitialized = false;
            movingToPatrolPoint = false;
            movingToSettlePoint = false;
            patrolEnabled = false;
            hasClaimedSettleDestination = false;
            claimedSettleDestination = Vector3.zero;

            nextFollowUpdateAt = 0f;
            nextFollowPathRecoveryAt = 0f;
            followSprintEngagementFailureSince = 0f;
            followWalkFallbackUntil = 0f;
            nextSettlePointAt = 0f;
            holdPositionUntil = 0f;
            nextPatrolUpdateAt = 0f;
            activePatrolCampCenter = Vector3.zero;
            returningToLeaderSector = false;
            patrolRuntimeArmed = false;
            hasLastPatrolLeaderPosition = false;
            lastPatrolLeaderPosition = Vector3.zero;
            patrolLeaderStoppedSince = 0f;
            nextPatrolLeaderMoveSampleAt = 0f;

            poseCorrected = false;

            lastCoverPoint = null;
            noCoverFound = false;

            coverCommitment.ClearCommitment();

            ResetPeaceActions();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;

            // Patrol never owns firearm use. Keep a delayed vanilla reload-cancel or stale combat
            // trigger from producing bursts after Attention/combat release.
            FollowerRecovery.StopShooting(BotOwner);
            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData?.IsBackpackInspectionActive == true)
            {
                HoldForBackpackInspection();
                return;
            }

            if (!TryGetBossAndPlayer())
            {
                BotOwner.StopMove();
                return;
            }

            SetPatrolEnabled(ShouldRunPatrolMode(followerData?.CanPatrol == true));

            try
            {
                if (patrolEnabled)
                {
                    Patrol();
                }
                else
                {
                    Follow();
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
                BotOwner.StopMove();
            }
        }

        public override void Stop()
        {
            ReleaseSettleDestinationClaim();
            followerData?.SetBackpackInspectionActive(false);
            base.Stop();
        }

        private void HoldForBackpackInspection()
        {
            movingToPatrolPoint = false;
            movingToSettlePoint = false;
            patrolEnabled = false;
            ReleaseSettleDestinationClaim();
            holdPositionUntil = 0f;
            isPatrolCampInitialized = false;
            nextPatrolUpdateAt = 0f;
            nextFollowUpdateAt = 0f;
            nextFollowPathRecoveryAt = 0f;
            coverCommitment.ClearCommitment();

            BotOwner.StopMove();

            if (BotOwner.Mover != null)
            {
                BotOwner.Mover.Pause = true;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                if (BotOwner.Mover.TargetPose > 0.15f || BotOwner.Mover.TargetPose < 0.05f)
                {
                    BotOwner.Mover.SetPose(0.1f);
                }
            }

            if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner) &&
                BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss &&
                boss.realPlayer != null)
            {
                BotOwner.Steering.LookToPoint(boss.realPlayer.Position + Vector3.up * 1.2f);
            }
        }

        private bool TryGetBossAndPlayer()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            bossData = boss;
            bossPlayer = boss.realPlayer;
            return true;
        }

        private Vector3 GetLeaderTargetPosition()
        {
            if (bossPlayer == null) return BotOwner.Position;
            return bossPlayer.Transform.position;
        }

        private float GetEffectiveFollowDistance()
        {
            float distance = pitFireTeam.followDistance?.Value ?? FallbackFollowDistance;
            return Mathf.Clamp(distance, 8f, 30f);
        }

        private void Follow(bool forceFollow = false, float forcedDistance = 0f)
        {
            if (bossPlayer == null) return;

            // A follower that is already walking to a settle point should keep the point active.
            // Otherwise normalize posture once so the bot does not continue patrol crouched/prone
            // after combat, looting, or a hold command.
            if (movingToSettlePoint)
            {
                RefreshSettleDestinationClaim();
                BotOwner.GoToSomePointData.UpdateToGo(false);
            }
            else if (BotOwner.Mover.TargetPose != 1f && !poseCorrected)
            {
                poseCorrected = true;
                BotOwner.Mover.SetPose(1f);
            }

            Vector3 leaderPosition = GetLeaderTargetPosition();
            float distance = forceFollow
                ? forcedDistance
                : Mathf.Abs((leaderPosition - BotOwner.Position).magnitude);

            float followDistance = GetEffectiveFollowDistance();
            bool inRange = distance < followDistance;

            if (nextFollowUpdateAt > Time.time && !forceFollow)
            {
                if (!inRange)
                {
                    MaintainOutOfRangeChase(leaderPosition, distance, followDistance);
                }
                return;
            }
            nextFollowUpdateAt = Time.time + Utils.Utils.Random(1f, 2f);

            if (inRange)
            {
                // Close enough to the boss: either settle into a committed local cover/position or
                // hold the existing one. This is patrol stabilization, not combat cover selection.
                if (isPathBlocked)
                {
                    movingToSettlePoint = false;
                    ReleaseSettleDestinationClaim();
                    BotOwner.StopMove();
                    return;
                }

                // ──────────────────────────────────────────────────────────────────────────
                // Sector-based cover commitment system
                // ──────────────────────────────────────────────────────────────────────────

                // Check if boss moved to a new sector (>20m from anchor)
                if (coverCommitment.SectorChanged(leaderPosition))
                {
                    coverCommitment.OnSectorChange(leaderPosition);
                    movingToSettlePoint = false;
                    ReleaseSettleDestinationClaim();
                }

                // Validate committed cover; clear if no longer valid
                if (coverCommitment.IsCommitted && !coverCommitment.IsCoverStillValid(BotOwner, leaderPosition))
                {
                    coverCommitment.ClearCommitment();
                    movingToSettlePoint = false;
                    ReleaseSettleDestinationClaim();
                }

                // Determine current strategy
                FollowerCoverCommitment.FollowStrategy strategy = coverCommitment.GetStrategy();

                if (strategy == FollowerCoverCommitment.FollowStrategy.Move)
                {
                    // MOVE: Search for and commit to a new cover in 80m radius
                    if (nextSettlePointAt > Time.time)
                    {
                        return;
                    }

                    nextSettlePointAt = Time.time + 3f; // Check less frequently during move state

                    CustomNavigationPoint? newCover = TryFindExpandedCoverPoint(leaderPosition);
                    if (newCover != null)
                    {
                        // Commit to this cover for the current sector
                        coverCommitment.CommitToCover(newCover, leaderPosition);
                        BotOwner.Memory.SetCoverPoints(newCover);
                        BotOwner.GoToSomePointData.SetPoint(newCover.Position);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        if (!TryApplyCommandLookOverride())
                        {
                            BotOwner.Steering.LookToPathDestPoint();
                        }
                        movingToSettlePoint = true;
                        nextFollowUpdateAt = Time.time + 0.5f;
                        return;
                    }

                    // No cover found: follow boss at medium range (boss-proximity mode)
                    if (TryGetRandomSettlePoint(leaderPosition, followDistance, out Vector3 settlePosition))
                    {
                        BotOwner.GoToSomePointData.SetPoint(settlePosition);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        if (!TryApplyCommandLookOverride())
                        {
                            BotOwner.Steering.LookToPathDestPoint();
                        }
                        movingToSettlePoint = true;
                        nextFollowUpdateAt = Time.time + 0.5f;
                    }
                    return;
                }
                else
                {
                    // HOLD: Bot is committed to a cover. Just hold and scan for engagement/support.
                    // The combat layer will handle engagement detection while holding.
                    if (movingToSettlePoint)
                    {
                        if (BotOwner.GoToSomePointData.IsCome())
                        {
                            movingToSettlePoint = false;
                            ReleaseSettleDestinationClaim();
                            BotOwner.StopMove();
                        }
                        return;
                    }

                    // At committed cover position: hold and let combat layer scan
                    BotOwner.StopMove();
                    return;
                }
            }

            // OUT OF RANGE: Chase boss
            movingToSettlePoint = false;
            ReleaseSettleDestinationClaim();
            UpdateFollowPath(leaderPosition);

            // Normal follow is allowed to sprint when the boss has opened distance. Once close enough,
            // the settle logic above takes over and moves the follower into a stable local position.
            ApplyFollowChaseMovementState(ShouldSprintFollow(distance, followDistance));
        }

        private void MaintainOutOfRangeChase(Vector3 leaderPosition, float distance, float followDistance)
        {
            movingToSettlePoint = false;
            ReleaseSettleDestinationClaim();
            ApplyFollowChaseMovementState(ShouldSprintFollow(distance, followDistance));

            if (BotOwner.Mover?.HasPathAndNoComplete == true)
            {
                return;
            }

            if (Time.time < nextFollowPathRecoveryAt)
            {
                return;
            }

            nextFollowPathRecoveryAt = Time.time + FollowPathRecoveryCooldown;
            UpdateFollowPath(leaderPosition);
        }

        private static bool ShouldSprintFollow(float distance, float followDistance)
        {
            return distance > Mathf.Min(followDistance + 3f, 16f);
        }

        private void ApplyFollowChaseMovementState(bool shouldSprint)
        {
            bool actualSprintEngaged = FollowerCombatActionBase.IsActuallySprinting(BotOwner);
            if (!shouldSprint)
            {
                followSprintEngagementFailureSince = 0f;
                followWalkFallbackUntil = 0f;
                ApplyFollowWalkState(actualSprintEngaged);
                return;
            }

            if (Time.time < followWalkFallbackUntil)
            {
                ApplyFollowWalkState(actualSprintEngaged);
                return;
            }

            bool canSprint = BotOwner.CanSprintPlayer && BotOwner.Mover.NoSprint != true;
            if (!canSprint)
            {
                BeginFollowWalkFallback(actualSprintEngaged);
                return;
            }

            bool sprintEngaged =
                BotOwner.Mover.HasPathAndNoComplete &&
                BotOwner.Mover.Sprinting &&
                actualSprintEngaged;
            if (sprintEngaged)
            {
                followSprintEngagementFailureSince = 0f;
            }
            else if (followSprintEngagementFailureSince <= 0f)
            {
                followSprintEngagementFailureSince = Time.time;
            }
            else if (Time.time - followSprintEngagementFailureSince >= FollowSprintEngagementFailureSeconds)
            {
                BeginFollowWalkFallback(actualSprintEngaged);
                return;
            }

            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            BotOwner.Mover.SetTargetMoveSpeed(1f);
            if (!BotOwner.Mover.Sprinting || !actualSprintEngaged)
            {
                BotOwner.Mover.Sprint(true, false);
            }
        }

        private void BeginFollowWalkFallback(bool actualSprintEngaged)
        {
            followSprintEngagementFailureSince = 0f;
            followWalkFallbackUntil = Time.time + FollowWalkFallbackSeconds;
            ApplyFollowWalkState(actualSprintEngaged);
        }

        private void ApplyFollowWalkState(bool actualSprintEngaged)
        {
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            BotOwner.Mover.SetTargetMoveSpeed(1f);
            if (BotOwner.Mover.Sprinting || actualSprintEngaged)
            {
                BotOwner.Mover.Sprint(false, false);
            }
        }

        private CustomNavigationPoint? TryGetSettleCoverPoint(Vector3 leaderPosition, float settleDistance)
        {
            if (lastCoverPoint != null || noCoverFound)
            {
                return lastCoverPoint;
            }

            if (bossPlayer == null) return null;

            List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(bossPlayer.Transform.position, settleDistance + 5f);
            float maxDistance = settleDistance;
            NavMeshPath navMeshPath = new NavMeshPath();
            List<CustomNavigationPoint> availableCover = new List<CustomNavigationPoint>();

            foreach (var point in coverPoints)
            {
                if (point == null) continue;
                if (!point.IsFreeById(BotOwner.Id)) continue;
                if (!IsSettlePositionClear(point.Position)) continue;
                if (Utils.Utils.GetNavDistance(leaderPosition, point.Position, navMeshPath) <= maxDistance)
                {
                    availableCover.Add(point);
                }
            }

            if (availableCover.Count == 0) return null;
            return availableCover[UnityEngine.Random.Range(0, availableCover.Count)];
        }

        /// <summary>
        /// Find best cover within 80m of boss for sector-based commitment.
        /// Prefers cover closest to boss to maintain protective positioning.
        /// Avoids crowded/occupied cover and spotted positions.
        /// </summary>
        private CustomNavigationPoint? TryFindExpandedCoverPoint(Vector3 bossPosition)
        {
            if (bossPlayer == null) return null;

            float searchRadius = FollowerCoverCommitment.GetCoverSearchRadius();
            float maxBossDist = FollowerCoverCommitment.GetCoverMaxBossDistance();

            List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(bossPosition, searchRadius);
            List<CustomNavigationPoint> validCandidates = new List<CustomNavigationPoint>();

            foreach (var point in coverPoints)
            {
                if (point == null) continue;
                if (!point.IsFreeById(BotOwner.Id)) continue;
                if (point.IsSpotted) continue;
                if (!IsSettlePositionClear(point.Position)) continue;
                if (!IsSettlePathClear(point.Position)) continue;
                if (HasSettleDestinationClaimConflict(point.Position)) continue;

                // Must be within max distance of boss
                if ((point.Position - bossPosition).sqrMagnitude > maxBossDist * maxBossDist)
                {
                    continue;
                }

                validCandidates.Add(point);
            }

            if (validCandidates.Count == 0)
            {
                return null;
            }

            // Sort by distance to boss (prefer closest for protective positioning)
            validCandidates.Sort((a, b) =>
            {
                float distA = (a.Position - bossPosition).sqrMagnitude;
                float distB = (b.Position - bossPosition).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            for (int i = 0; i < validCandidates.Count; i++)
            {
                CustomNavigationPoint candidate = validCandidates[i];
                if (TryReserveSettleDestination(candidate.Position))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool TryGetRandomSettlePoint(Vector3 leaderPosition, float settleDistance, out Vector3 settlePosition)
        {
            float minOffset = Mathf.Min(1f, settleDistance * 0.19f);
            float maxOffset = Mathf.Min(5f, settleDistance * 0.65f);
            float xOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
            float zOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
            Vector3 candidate = new Vector3(leaderPosition.x + xOffset, leaderPosition.y, leaderPosition.z + zOffset);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, 2f, -1))
            {
                if (!IsSettlePositionClear(navMeshHit.position))
                {
                    settlePosition = default;
                    return false;
                }

                if (!IsSettlePathClear(navMeshHit.position) ||
                    !TryReserveSettleDestination(navMeshHit.position))
                {
                    settlePosition = default;
                    return false;
                }

                settlePosition = navMeshHit.position;
                return true;
            }

            settlePosition = default;
            return false;
        }

        private bool IsSettlePositionClear(Vector3 position)
        {
            if (bossPlayer != null && (bossPlayer.Transform.position - position).sqrMagnitude < SettleSpacingSqr)
            {
                return false;
            }

            if (bossData != null)
            {
                foreach (BotOwner follower in bossData.Followers)
                {
                    if (follower == null || follower == BotOwner || follower.GetPlayer == null || follower.IsDead) continue;
                    if ((follower.GetPlayer.Transform.position - position).sqrMagnitude < SettleSpacingSqr)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsSettlePathClear(Vector3 destination)
        {
            Vector3 start = BotOwner.Position;
            if ((destination - start).sqrMagnitude <= 0.01f)
            {
                return true;
            }

            if (bossPlayer != null &&
                IsPointTooCloseToSettlePath(bossPlayer.Transform.position, start, destination))
            {
                return false;
            }

            if (bossData != null)
            {
                foreach (BotOwner follower in bossData.Followers)
                {
                    if (follower == null || follower == BotOwner || follower.GetPlayer == null || follower.IsDead) continue;
                    if (IsPointTooCloseToSettlePath(follower.GetPlayer.Transform.position, start, destination))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsPointTooCloseToSettlePath(Vector3 point, Vector3 start, Vector3 end)
        {
            return DistancePointToSegmentSqrXZ(
                point,
                start,
                end,
                SettlePathStartPadding,
                SettlePathEndPadding) < SettlePathSpacingSqr;
        }

        private static float DistancePointToSegmentSqrXZ(
            Vector3 point,
            Vector3 start,
            Vector3 end,
            float startPadding,
            float endPadding)
        {
            Vector2 segment = new Vector2(end.x - start.x, end.z - start.z);
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= 0.001f)
            {
                float dx = point.x - start.x;
                float dz = point.z - start.z;
                return dx * dx + dz * dz;
            }

            Vector2 pointFromStart = new Vector2(point.x - start.x, point.z - start.z);
            float t = Mathf.Clamp01(Vector2.Dot(pointFromStart, segment) / lengthSqr);
            float length = Mathf.Sqrt(lengthSqr);
            if (t * length <= startPadding || (1f - t) * length <= endPadding)
            {
                return float.MaxValue;
            }

            float closestX = start.x + segment.x * t;
            float closestZ = start.z + segment.y * t;
            float dxToClosest = point.x - closestX;
            float dzToClosest = point.z - closestZ;
            return dxToClosest * dxToClosest + dzToClosest * dzToClosest;
        }

        private bool HasSettleDestinationClaimConflict(Vector3 destination)
        {
            return bossData?.CombatEvents.HasDestinationClaimConflict(BotOwner, destination, SettleSpacing) == true;
        }

        private bool TryReserveSettleDestination(Vector3 destination)
        {
            if (!IsSettlePositionClear(destination) ||
                !IsSettlePathClear(destination) ||
                HasSettleDestinationClaimConflict(destination))
            {
                return false;
            }

            if (bossData?.CombatEvents.UpsertDestinationClaim(BotOwner, destination, SettleDestinationClaimTtlSeconds) != true)
            {
                return false;
            }

            hasClaimedSettleDestination = true;
            claimedSettleDestination = destination;
            return true;
        }

        private void RefreshSettleDestinationClaim()
        {
            if (!hasClaimedSettleDestination)
            {
                return;
            }

            GetCombatEvents()?.UpsertDestinationClaim(BotOwner, claimedSettleDestination, SettleDestinationClaimTtlSeconds);
        }

        private void ReleaseSettleDestinationClaim()
        {
            if (!hasClaimedSettleDestination)
            {
                return;
            }

            GetCombatEvents()?.TryReleaseDestinationClaim(
                BotOwner,
                claimedSettleDestination,
                SettleDestinationClaimReleaseTolerance);
            hasClaimedSettleDestination = false;
            claimedSettleDestination = Vector3.zero;
        }

        private CombatEvents? GetCombatEvents()
        {
            return bossData?.CombatEvents ??
                   (BotOwner?.BotFollower?.BossToFollow as pitAIBossPlayer)?.CombatEvents;
        }

        private void Patrol()
        {
            if (bossPlayer == null || bossData == null) return;

            movingToSettlePoint = false;
            ReleaseSettleDestinationClaim();

            Vector3 leaderPosition = bossPlayer.Transform.position;
            Vector3 botPosition = BotOwner.GetPlayer.Transform.position;
            float followDistance = GetEffectiveFollowDistance();
            if (followerData == null)
            {
                Follow();
                return;
            }

            if (!followerData.TryGetPatrolLeaderSectorAnchor(out Vector3 leaderAnchor))
            {
                followerData.SetPatrolLeaderSectorAnchor(leaderPosition);
                activePatrolCampCenter = botPosition;
                isPatrolCampInitialized = true;
            }
            else if (HasLeaderLeftPatrolAnchor(leaderPosition, leaderAnchor))
            {
                followerData.SetPatrolLeaderSectorAnchor(leaderPosition);
                activePatrolCampCenter = leaderPosition;
                returningToLeaderSector = true;
                isPatrolCampInitialized = false;
                movingToPatrolPoint = false;
                holdPositionUntil = 0f;
            }

            if (returningToLeaderSector)
            {
                float distanceToLeader = Mathf.Abs((leaderPosition - botPosition).magnitude);
                if (distanceToLeader >= followDistance)
                {
                    Follow(true, distanceToLeader);
                    return;
                }

                returningToLeaderSector = false;
                isPatrolCampInitialized = true;
                activePatrolCampCenter = leaderPosition;
                movingToPatrolPoint = false;
                holdPositionUntil = 0f;
            }

            if (!isPatrolCampInitialized)
            {
                activePatrolCampCenter = botPosition;
                isPatrolCampInitialized = true;
            }

            if (!isPatrolCampInitialized) return;
            if (nextPatrolUpdateAt > Time.time) return;

            nextPatrolUpdateAt = Time.time + 1.5f;
            Vector3 campCenter = activePatrolCampCenter;

            if (holdPositionUntil > Time.time)
            {
                if (playPeacefulActions)
                {
                    BotOwner.PeacefulActions.UpdateAction();
                }
                else if (playPeaceLook)
                {
                    BotOwner.PeaceLook.ManualUpdate();
                }
                else if (playPeaceHardAim)
                {
                    BotOwner.PeaceHardAim.ManualUpdate();
                }
                else if (playSecondWeaponWatch)
                {
                    BotOwner.SecondWeaponData.ManualUpdate();
                }

                movingToPatrolPoint = false;
                return;
            }

            if (movingToPatrolPoint)
            {
                if (BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false))
                {
                    BotOwner.StopMove();
                    BotOwner.LookData.SetLookPointByHearing(null);
                    holdPositionUntil = Time.time + Utils.Utils.Random(6f, 10f);
                }
                else
                {
                    if (!TryApplyCommandLookOverride())
                    {
                        BotOwner.Steering.LookToMovingDirection();
                    }
                }
                return;
            }

            List<Vector3> keepClearPositions = new List<Vector3> { leaderPosition };
            foreach (BotOwner follower in bossData.Followers)
            {
                keepClearPositions.Add(follower.GetPlayer.Transform.position);
            }
            Vector3[] finalKeepClearPositions = keepClearPositions.ToArray();

            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPosition = campCenter + UnityEngine.Random.insideUnitSphere * patrolPerimeterRadius;
                if (!NavMesh.SamplePosition(randomPosition, out NavMeshHit navMeshHit, 10f, -1)) continue;
                if (!Utils.Utils.IsDangerPositionFarEnough(navMeshHit.position, finalKeepClearPositions, 2f * 2f)) continue;

                if (BotOwner.GoToPoint(navMeshHit.position, true, -1f, false, false) != NavMeshPathStatus.PathComplete) continue;

                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }
                BotOwner.Mover.SetTargetMoveSpeed(0.5f);
                if (!TryApplyCommandLookOverride())
                {
                    BotOwner.Steering.LookToPoint(navMeshHit.position + Vector3.up * 1.5f);
                }

                ResetPeaceActions();

                bool hasPeaceful = BotOwner.PeacefulActions.HaveActions();
                bool hasLook = BotOwner.PeaceLook.HaveActions();
                bool hasHardAim = BotOwner.PeaceHardAim.HaveActions();
                bool hasSecondWeaponWatch = BotOwner.SecondWeaponData.HaveActions();

                if (hasPeaceful && UnityEngine.Random.value > 0.5f) playPeacefulActions = true;
                if (!playPeacefulActions && hasLook && UnityEngine.Random.value > 0.5f) playPeaceLook = true;
                if (!playPeacefulActions && !playPeaceLook && hasHardAim && UnityEngine.Random.value > 0.5f) playPeaceHardAim = true;
                if (!playPeacefulActions && !playPeaceLook && !playPeaceHardAim && hasSecondWeaponWatch && UnityEngine.Random.value > 0.5f)
                {
                    playSecondWeaponWatch = true;
                }

                movingToPatrolPoint = true;
                return;
            }
        }

        private bool ShouldRunPatrolMode(bool canPatrol)
        {
            if (!canPatrol || bossPlayer == null)
            {
                ResetPatrolRuntimeGate(true);
                return false;
            }

            Vector3 leaderPosition = GetLeaderTargetPosition();
            Vector3 botPosition = BotOwner.GetPlayer.Transform.position;
            float followDistance = GetEffectiveFollowDistance();
            bool inFollowRange = (leaderPosition - botPosition).sqrMagnitude < followDistance * followDistance;
            bool leaderMoved = HasLeaderMovedForPatrolGate(leaderPosition);

            if (leaderMoved && !patrolRuntimeArmed)
            {
                ResetPatrolRuntimeGate(true);
                return false;
            }

            if (patrolLeaderStoppedSince <= 0f)
            {
                patrolLeaderStoppedSince = Time.time;
            }

            if (!patrolRuntimeArmed && !inFollowRange)
            {
                ResetActivePatrolState();
                return false;
            }

            if (!patrolRuntimeArmed && Time.time - patrolLeaderStoppedSince >= PatrolLeaderIdleRequiredSeconds)
            {
                patrolRuntimeArmed = true;
                followerData?.ClearPatrolLeaderSectorAnchor();
            }

            return patrolRuntimeArmed;
        }

        private bool HasLeaderMovedForPatrolGate(Vector3 leaderPosition)
        {
            if (bossPlayer?.MovementContext != null)
            {
                Vector2 direction = bossPlayer.MovementContext.MovementDirection;
                if (direction.sqrMagnitude > PatrolLeaderMoveInputThresholdSqr &&
                    bossPlayer.MovementContext.CharacterMovementSpeed > 0.05f)
                {
                    lastPatrolLeaderPosition = leaderPosition;
                    hasLastPatrolLeaderPosition = true;
                    nextPatrolLeaderMoveSampleAt = Time.time + PatrolLeaderMoveSampleInterval;
                    return true;
                }
            }

            if (!hasLastPatrolLeaderPosition)
            {
                lastPatrolLeaderPosition = leaderPosition;
                hasLastPatrolLeaderPosition = true;
                nextPatrolLeaderMoveSampleAt = Time.time + PatrolLeaderMoveSampleInterval;
                return false;
            }

            if (Time.time < nextPatrolLeaderMoveSampleAt)
            {
                return false;
            }

            Vector3 delta = leaderPosition - lastPatrolLeaderPosition;
            delta.y = 0f;
            bool movedByPosition = delta.sqrMagnitude > PatrolLeaderMoveSampleDeltaThresholdSqr;
            lastPatrolLeaderPosition = leaderPosition;
            nextPatrolLeaderMoveSampleAt = Time.time + PatrolLeaderMoveSampleInterval;
            return movedByPosition;
        }

        private void ResetPatrolRuntimeGate(bool clearLeaderAnchor)
        {
            patrolRuntimeArmed = false;
            patrolLeaderStoppedSince = 0f;
            ResetActivePatrolState();

            if (clearLeaderAnchor)
            {
                followerData?.ClearPatrolLeaderSectorAnchor();
            }
        }

        private void ResetActivePatrolState()
        {
            movingToPatrolPoint = false;
            holdPositionUntil = 0f;
            isPatrolCampInitialized = false;
            activePatrolCampCenter = Vector3.zero;
            returningToLeaderSector = false;
            nextPatrolUpdateAt = 0f;
            ResetPeaceActions();
        }

        private static bool HasLeaderLeftPatrolAnchor(Vector3 leaderPosition, Vector3 anchor)
        {
            Vector3 delta = leaderPosition - anchor;
            delta.y = 0f;
            return delta.sqrMagnitude > PatrolCampRadius * PatrolCampRadius;
        }


        private NavMeshPathStatus GoToPosition(Vector3 position)
        {
            NavMeshPathStatus pathStatus = BotOwner.GoToPoint(position, true, -1f, false, false);
            if (pathStatus == NavMeshPathStatus.PathComplete)
            {
                if (!TryApplyCommandLookOverride())
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }

            return pathStatus;
        }

        private bool TryApplyCommandLookOverride()
        {
            return BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner);
        }

        private void UpdateFollowPath(Vector3 leaderPosition)
        {
            isPathBlocked = false;
            if (GoToPosition(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                return;
            }

            if (NavMesh.SamplePosition(leaderPosition, out _, 5f, -1) && GoToPosition(leaderPosition) != NavMeshPathStatus.PathComplete)
            {
                isPathBlocked = true;
            }

            if (isPathBlocked)
            {
                CustomNavigationPoint freeClosePoint = BotOwner.Covers.GetFreeClosePoint(leaderPosition, 1f, false);
                if (freeClosePoint != null)
                {
                    GoToPosition(freeClosePoint.Position);
                }
            }
        }

        private void SetPatrolEnabled(bool state)
        {
            patrolEnabled = state;

            if (!state)
            {
                ResetActivePatrolState();
                ReleaseSettleDestinationClaim();
            }
        }

        private void ResetPeaceActions()
        {
            playPeacefulActions = false;
            playPeaceLook = false;
            playPeaceHardAim = false;
            playSecondWeaponWatch = false;
        }

    }
}
