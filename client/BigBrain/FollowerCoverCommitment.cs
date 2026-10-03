using EFT;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    /// <summary>
    /// Tracks follower's committed cover point and sector anchor.
    /// Once a cover is committed in a sector, the follower HOLDS until:
    /// - Cover becomes invalid (spotted, occupied, out of range)
    /// - Boss moves to a new sector (>20m from anchor)
    /// - Follower is explicitly forced to search new cover
    /// </summary>
    internal class FollowerCoverCommitment
    {
        private const float SectorRadiusMeter = 20f;
        private const float SectorRadiusMeterSqr = SectorRadiusMeter * SectorRadiusMeter;
        private const float CoverSearchRadius = 80f;
        private const float CoverMaxBossDistanceSqr = 100f * 100f;

        private CustomNavigationPoint? committedCover;
        private Vector3 sectorAnchor;
        private bool hasSectorAnchor;
        private float commitTimeAt;

        /// <summary>
        /// Current committed cover point, or null if no valid commitment.
        /// </summary>
        public CustomNavigationPoint? CommittedCover => committedCover;

        /// <summary>
        /// True if this follower is in a sector and has committed to a cover.
        /// </summary>
        public bool IsCommitted => committedCover != null && hasSectorAnchor;

        /// <summary>
        /// True if boss has moved into a new sector (>20m from anchor).
        /// </summary>
        public bool SectorChanged(Vector3 currentBossPosition)
        {
            if (!hasSectorAnchor)
            {
                return false;
            }

            return (currentBossPosition - sectorAnchor).sqrMagnitude > SectorRadiusMeterSqr;
        }

        /// <summary>
        /// Check if the committed cover is still valid for this follower.
        /// Validates both physical state (occupied, spotted, distance) and tactical soundness
        /// (enemy can shoot, under sustained fire, grenade danger, engagement opportunity).
        /// </summary>
        public bool IsCoverStillValid(BotOwner botOwner, Vector3 bossPosition)
        {
            if (committedCover == null)
            {
                return false;
            }

            // Cover is occupied by another follower
            if (!committedCover.IsFreeById(botOwner.Id))
            {
                return false;
            }

            // Cover has been spotted/exposed
            if (committedCover.IsSpotted)
            {
                return false;
            }

            // Cover is too far from boss
            if ((committedCover.Position - bossPosition).sqrMagnitude > CoverMaxBossDistanceSqr)
            {
                return false;
            }

            // TACTICAL VALIDITY CHECKS

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            // Enemy can shoot the cover → position is compromised, need to move
            if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            // Taking sustained fire at this cover → cover isn't protecting, need to relocate
            if (botOwner.Memory.IsUnderFire)
            {
                // Allow brief settling time (1.5s), then abandon if fire continues
                if (Time.time - commitTimeAt > 1.5f)
                {
                    return false;
                }
            }

            // Close-range enemy (dogfight range) → cover may constrain engagement
            if (goalEnemy != null && goalEnemy.IsVisible && pitTeam.Modules.FollowerEnemyTracking.Distance(goalEnemy) < 8f)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Commit to a cover point in the current sector.
        /// </summary>
        public void CommitToCover(CustomNavigationPoint cover, Vector3 bossPosition)
        {
            committedCover = cover;
            sectorAnchor = bossPosition;
            hasSectorAnchor = true;
            commitTimeAt = Time.time;
        }

        /// <summary>
        /// Clear the commitment and reset sector anchor.
        /// </summary>
        public void ClearCommitment()
        {
            committedCover = null;
            hasSectorAnchor = false;
            sectorAnchor = Vector3.zero;
            commitTimeAt = 0f;
        }

        /// <summary>
        /// Handle sector change: clear commitment and reanchor.
        /// </summary>
        public void OnSectorChange(Vector3 newBossPosition)
        {
            committedCover = null;
            sectorAnchor = newBossPosition;
            hasSectorAnchor = true;
            commitTimeAt = 0f;
        }

        /// <summary>
        /// Get the search/hold strategy for this sector.
        /// </summary>
        public FollowStrategy GetStrategy()
        {
            if (IsCommitted)
            {
                return FollowStrategy.Hold;
            }

            return FollowStrategy.Move;
        }

        /// <summary>
        /// Follower movement/hold strategy in patrol mode.
        /// </summary>
        public enum FollowStrategy
        {
            /// <summary>
            /// No cover committed yet or cover was invalidated. Search for new cover.
            /// </summary>
            Move,

            /// <summary>
            /// Cover committed in current sector. Hold position and scan for engagement/support.
            /// </summary>
            Hold
        }

        /// <summary>
        /// Get cover search parameters for the current sector.
        /// Search within 80m of boss, prefer closer points, avoid crowded areas.
        /// </summary>
        public static float GetCoverSearchRadius() => CoverSearchRadius;
        public static float GetCoverMaxBossDistance() => Mathf.Sqrt(CoverMaxBossDistanceSqr);
    }
}
