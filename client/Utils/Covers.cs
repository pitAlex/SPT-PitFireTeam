using EFT;
using pitTeam.Components;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.Utils
{
    public class Covers
    {


        private static float STATIC_DISTANCE = 150f;
        private static float SQUARE_SIZE = 15f;
        private const float CombatFriendSpacing = 1.5f;
        private const float HardCoverThreatEyeHeight = 1.4f;
        private const float HardCoverChestHeight = 1.05f;
        private const float HardCoverHeadHeight = 1.55f;
        private const float HardCoverHeadHalfWidth = 0.18f;
        private const float HardCoverMaxBlockerDistance = 3f;
        public static int MAX_COVERS_IRT = 20;

        public static void ResetMaxCoversIritation()
        {
            MAX_COVERS_IRT = 20;
            STATIC_DISTANCE = 150f;
        }

        public static void SetLowMaxCoversIritation()
        {
            MAX_COVERS_IRT = 10;
            STATIC_DISTANCE = 40f;
        }

        /**
         *  Get closest cover point for the bot to given to the position, within the search radius, optionally filtered by an extra check function
         */
        public static CustomNavigationPoint GetClosestCoverPoint(BotOwner botOwner, Vector3 centerPosition, float searchRadius, Func<CustomNavigationPoint, bool>? extraChecks = null, CoverSearchType? searchTypeOverride = null)
        {
            pitAIBossPlayer boss = botOwner.BotFollower.HaveBoss ? botOwner.BotFollower.BossToFollow as pitAIBossPlayer : null;

            searchRadius = Math.Min(searchRadius, STATIC_DISTANCE);
            List<CustomNavigationPoint> areaCovers = GetVanillaBackedCandidates(botOwner, centerPosition, searchRadius, null, centerPosition, "ClosestCoverPoint", null, searchTypeOverride);

            // ensure point is not too close to teammates to avoid clustering
            List<Vector3> friendsPositions = GetFriendsPositions(botOwner, boss);

            CustomNavigationPoint pt = ClosestPoint(botOwner.Id, botOwner.GetPlayer.Transform.position, centerPosition, areaCovers, (CustomNavigationPoint point) =>
            {
                return extraChecks == null ? true : extraChecks(point);

            }, CombatFriendSpacing, friendsPositions);

            botOwner.Memory.SetCoverPoints(pt);

            return pt;
        }
        /**
         *  Get closest cover point for the bot to pointA within the area between pointA and pointB, at a min safe distance from danger and optionally filtered by an extra check function 
         */
        public static CustomNavigationPoint GetClosestCoverPointBetween(BotOwner botOwner, Vector3 pointA, Vector3 pointB, float safeDistance = 5f, Func<GroupPoint, bool>? eligibilityCheck = null, CoverSearchType? searchTypeOverride = null)
        {
            pitAIBossPlayer boss = botOwner.BotFollower.HaveBoss ? botOwner.BotFollower.BossToFollow as pitAIBossPlayer : null;


            Vector3 centerPosition = (pointA + pointB) / 2f;
            float searchRadius = Math.Min((pointA - pointB).magnitude, STATIC_DISTANCE);

            ShootPointClass shootPointClass = botOwner.CurrentEnemyTargetPosition(true);
            List<CustomNavigationPoint> areaCovers = GetVanillaBackedCandidates(botOwner, centerPosition, searchRadius, shootPointClass, centerPosition, "ClosestCoverPointBetween", null, searchTypeOverride);
            List<Vector3> friendsPositions = GetFriendsPositions(botOwner, boss);

            CustomNavigationPoint pt = ClosestPoint(botOwner.Id, botOwner.GetPlayer.Transform.position, centerPosition, areaCovers, point =>
            {
                // ensure point is between the two points
                if (!IsPointBetween(point.Position, pointA, pointB)) return false;
                // ensure point is not too close to teammates to avoid clustering
                if (!IsFarEnoughFromPositions(point.Position, friendsPositions, CombatFriendSpacing * CombatFriendSpacing)) return false;
                // ensure point is not too close to the enemy
                if ((point.Position - shootPointClass.Point).sqrMagnitude < safeDistance * safeDistance) return false;

                if (eligibilityCheck != null && !eligibilityCheck(point.GroupPoint)) return false;

                return true;
            });

            botOwner.Memory.SetCoverPoints(pt);

            return pt;
        }

        /**
         *  Get the closest cover point toward the target direction, choosing the eligible point closest to the target within the search radius around origin.
         */
        public static CustomNavigationPoint GetClosestCoverPointTowardPoint(
            BotOwner botOwner,
            Vector3 originPosition,
            Vector3 targetPosition,
            float searchRadius,
            Func<CustomNavigationPoint, bool>? eligibilityCheck = null,
            float minForwardDot = 0.1f,
            CoverSearchType? searchTypeOverride = null)
        {
            pitAIBossPlayer? boss = botOwner.BotFollower.HaveBoss ? botOwner.BotFollower.BossToFollow as pitAIBossPlayer : null;

            searchRadius = Math.Min(searchRadius, STATIC_DISTANCE);

            Vector3 targetDirection = targetPosition - originPosition;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude <= 0.01f)
            {
                return null;
            }

            targetDirection.Normalize();

            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            List<CustomNavigationPoint> areaCovers = GetVanillaBackedCandidates(botOwner, originPosition, searchRadius, shootPoint, targetPosition, "ClosestCoverTowardPoint", null, searchTypeOverride);
            List<Vector3> friendsPositions = GetFriendsPositions(botOwner, boss);

            CustomNavigationPoint pt = ClosestPoint(botOwner.Id, botOwner.GetPlayer.Transform.position, targetPosition, areaCovers, point =>
            {
                Vector3 pointDirection = point.Position - originPosition;
                pointDirection.y = 0f;
                if (pointDirection.sqrMagnitude <= 0.01f)
                {
                    return false;
                }

                pointDirection.Normalize();
                if (Vector3.Dot(pointDirection, targetDirection) < minForwardDot)
                {
                    return false;
                }

                return eligibilityCheck == null || eligibilityCheck(point);
            }, CombatFriendSpacing, friendsPositions);

            botOwner.Memory.SetCoverPoints(pt);

            return pt;
        }
        /**
         * Get all cover poins around the center position
         */
        public static List<CustomNavigationPoint> GetCoverPoints(BotOwner botOwner, Vector3 centerPosition, float searchRadius, Func<CustomNavigationPoint, bool>? eligibilityCheck = null, int iritations = -1, CoverSearchType? searchTypeOverride = null)
        {
            List<CustomNavigationPoint> points = new List<CustomNavigationPoint>();


            Vector3 targetArea = new Vector3(
               Mathf.Floor(centerPosition.x / SQUARE_SIZE) * SQUARE_SIZE,
               Mathf.Floor(centerPosition.y / 3f) * 3f,
               Mathf.Floor(centerPosition.z / SQUARE_SIZE) * SQUARE_SIZE
           );

            float radius = Mathf.Min(searchRadius * 1.2f, STATIC_DISTANCE);

            int max_irt = iritations == -1 ? (int)Math.Round(MAX_COVERS_IRT * 1.2f) : iritations;

            List<CustomNavigationPoint> areaCovers = GetVanillaBackedCandidates(botOwner, targetArea, radius, botOwner.CurrentEnemyTargetPosition(true), centerPosition, "GetCoverPoints", max_irt, searchTypeOverride);

            float searchRadiusSqr = searchRadius * searchRadius;

            foreach (CustomNavigationPoint pt in areaCovers)
            {
                if ((centerPosition - pt.Position).sqrMagnitude <= searchRadiusSqr && pt.IsFreeById(botOwner.Id))
                {
                    if (eligibilityCheck != null && !eligibilityCheck(pt)) continue;
                    points.Add(pt);
                }

            }

            return points;
        }
        /**
         *  Get a random cover point for the bot around the given position, within the specified radius
         */
        public static CustomNavigationPoint GetCoverPoint(BotOwner botOwner, Vector3 centerPosition, float searchRadius, Func<CustomNavigationPoint, bool>? eligibilityCheck = null, CoverSearchType? searchTypeOverride = null)
        {
            searchRadius = Math.Min(searchRadius, STATIC_DISTANCE);

            List<CustomNavigationPoint> points = GetCoverPoints(botOwner, centerPosition, searchRadius, eligibilityCheck, -1, searchTypeOverride);

            CustomNavigationPoint point = null;

            if (points.Count > 0)
            {
                point = points.Random();

            }

            botOwner.Memory.SetCoverPoints(point);

            return point;


        }
        /** Utility to use CustomNavigationPoint when searching for a point. It is meant to replace FindPoint in BaseLogicLayerSimpleClass of bots **/
        public static CustomNavigationPoint FindPoint(BotOwner botOwner, CustomNavigationPoint customNavigationPoint, float searchRadius = 50f)
        {
            searchRadius = Math.Min(searchRadius, STATIC_DISTANCE);

            if (customNavigationPoint != null && (!customNavigationPoint.IsFreeById(botOwner.Id) || customNavigationPoint.IsSpotted))
            {
                customNavigationPoint = null;
            }
            if (customNavigationPoint != null)
            {
                return customNavigationPoint;
            }
            else
            {
                NavMeshPath navMeshPath = new NavMeshPath();
                customNavigationPoint = GetCoverPoint(botOwner, botOwner.GetPlayer.Transform.position, searchRadius, (CustomNavigationPoint point) =>
                {
                    return IsNavigablePoint(botOwner.GetPlayer.Transform.position, point.Position, searchRadius, navMeshPath);
                });
            }


            return customNavigationPoint;
        }
        /** Get closest cover to the desired position based on an eligibility check */
        public static CustomNavigationPoint GetClosestCover(
            BotOwner botOwner,
            Vector3 desiredPostion,
            Func<GroupPoint, bool> eligibiityCheck
        )
        {

            if (!botOwner.Memory.HaveEnemy) return null;

            pitAIBossPlayer boss = botOwner.BotFollower.HaveBoss ? botOwner.BotFollower.BossToFollow as pitAIBossPlayer : null;

            List<Vector3> friendsPositions = new List<Vector3>((boss?.Followers?.Count ?? 0) + 1);
            if (boss != null)
            {
                friendsPositions.Add(boss.realPlayer.Transform.position);
                for (int i = 0; i < boss.Followers.Count; i++)
                {
                    BotOwner follower = boss.Followers[i];
                    if (follower != botOwner)
                    {
                        friendsPositions.Add(follower.GetPlayer.Transform.position);
                    }
                }
            }
            ;

            botOwner.Covers.GetClosestPoint(desiredPostion, point =>
            {
                if (boss != null && !Utils.IsDangerPositionFarEnough(point.Position, friendsPositions, 1.5f * 1.5f)) return false;
                if (!eligibiityCheck(point)) return false;
                return true;
            });

            return null;
        }

        /** Get the point among the given ones that is the closest to centerPosition that meets the eligibility check **/
        public static CustomNavigationPoint ClosestPoint(
            int botOwnerId,
            Vector3 botPosition,
            Vector3 centerPosition,
            List<CustomNavigationPoint> areaPoints,
            Func<CustomNavigationPoint, bool> eligibleCheck,
            float safeDistance = 5f,
            IReadOnlyList<Vector3>? dangerPositions = null
        )
        {
            CustomNavigationPoint closest = null;

            float lastsqr = Mathf.Infinity;

            foreach (CustomNavigationPoint point in areaPoints)
            {
                if (
                    !(point.CoverLevel == CoverLevel.Sit || point.CoverLevel == CoverLevel.Stay) ||
                    !point.IsFreeById(botOwnerId) ||
                    !IsFarEnoughFromPositions(point.Position, dangerPositions, safeDistance * safeDistance) ||
                    !eligibleCheck(point)
                )
                {
                    continue;
                }

                float dist = (centerPosition - point.Position).sqrMagnitude;
                if (dist <= lastsqr)
                {
                    closest = point;
                    lastsqr = dist;
                }
            }

            return closest;
        }

        private static List<Vector3> GetFriendsPositions(BotOwner botOwner, pitAIBossPlayer? boss)
        {
            List<Vector3> friendsPositions = new List<Vector3>((boss?.Followers?.Count ?? 0) + 1);
            if (boss == null)
            {
                return friendsPositions;
            }

            friendsPositions.Add(boss.realPlayer.Transform.position);
            for (int i = 0; i < boss.Followers.Count; i++)
            {
                BotOwner follower = boss.Followers[i];
                if (follower != botOwner)
                {
                    friendsPositions.Add(follower.GetPlayer.Transform.position);
                }
            }
            return friendsPositions;
        }

        private static List<CustomNavigationPoint> GetVanillaBackedCandidates(
            BotOwner botOwner,
            Vector3 centerPosition,
            float searchRadius,
            ShootPointClass? shootPoint,
            Vector3? pointToBeClose,
            string label,
            int? maxCandidates = null,
            CoverSearchType? searchTypeOverride = null)
        {
            searchRadius = Math.Min(searchRadius, STATIC_DISTANCE);
            int takeCount = maxCandidates ?? MAX_COVERS_IRT;
            Vector3 closePoint = pointToBeClose ?? centerPosition;
            List<CustomNavigationPoint> areaCovers = new List<CustomNavigationPoint>(Math.Max(0, takeCount));
            if (takeCount > 0)
            {
                foreach (CustomNavigationPoint cover in botOwner.BotsGroup.CoverPointMaster.GetClosePoints(centerPosition, botOwner, searchRadius))
                {
                    if (cover == null)
                    {
                        continue;
                    }

                    InsertClosestCandidate(areaCovers, cover, closePoint, takeCount);
                }
            }

            CoverSearchData searchData = BuildCoverSearchData(botOwner, centerPosition, searchRadius, shootPoint, pointToBeClose, label, maxCandidates, searchTypeOverride);
            float searchRadiusSqr = searchRadius * searchRadius;
            CustomNavigationPoint? primary = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, false);
            if (primary != null &&
                (primary.Position - centerPosition).sqrMagnitude <= searchRadiusSqr)
            {
                areaCovers.RemoveAll(point => point == null || point.Id == primary.Id);
                areaCovers.Insert(0, primary);
            }

            return areaCovers;
        }

        private static void InsertClosestCandidate(List<CustomNavigationPoint> candidates, CustomNavigationPoint cover, Vector3 closePoint, int maxCount)
        {
            float score = (cover.Position - closePoint).sqrMagnitude;
            int insertAt = candidates.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                float existingScore = (candidates[i].Position - closePoint).sqrMagnitude;
                if (score < existingScore)
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= maxCount)
            {
                return;
            }

            candidates.Insert(insertAt, cover);
            if (candidates.Count > maxCount)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }
        }

        private static bool IsFarEnoughFromPositions(Vector3 positionToCheck, IReadOnlyList<Vector3>? positionsIMustCare, float minSqrDistance)
        {
            if (positionsIMustCare == null)
            {
                return true;
            }

            for (int i = 0; i < positionsIMustCare.Count; i++)
            {
                if ((positionsIMustCare[i] - positionToCheck).sqrMagnitude < minSqrDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private static CoverSearchData BuildCoverSearchData(
            BotOwner botOwner,
            Vector3 centerPosition,
            float searchRadius,
            ShootPointClass? shootPoint,
            Vector3? pointToBeClose,
            string label,
            int? maxIterations = null,
            CoverSearchType? searchTypeOverride = null)
        {
            CoverShootType shootType = shootPoint != null ? CoverShootType.shoot : CoverShootType.hide;
            CoverSearchType searchType = searchTypeOverride ?? (pointToBeClose.HasValue ? CoverSearchType.closerToSelectedPoint : CoverSearchType.distToToCenter);
            CoverSearchData searchData = new CoverSearchData(
                centerPosition,
                botOwner.CoverSearchInfo,
                shootType,
                searchRadius * searchRadius,
                0f,
                searchType,
                shootPoint,
                botOwner.Covers.ClosestFriendCoverPoint(),
                pointToBeClose,
                ECheckSHootHide.shootAndHide,
                new CoverSearchDefenceDataClass(botOwner.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL),
                PointsArrayType.byShootType,
                false,
                null,
                maxIterations,
                label);
            searchData.UseLineCastToCover = true;
            return searchData;
        }

        public static bool IsPointBetween(Vector3 point, Vector3 start, Vector3 end)
        {
            return (point.x >= Math.Min(start.x, end.x) && point.x <= Math.Max(start.x, end.x)) &&
                       (point.y >= Math.Min(start.y, end.y) && point.y <= Math.Max(start.y, end.y)) &&
                       (point.z >= Math.Min(start.z, end.z) && point.z <= Math.Max(start.z, end.z));
        }

        /// <summary>
        /// SAIN-style hard-cover validation for an existing EFT cover point. Foliage cannot make
        /// a hard result, and the first solid blocker must be close to the candidate rather than
        /// somewhere else along the threat lane. Deterministic standing chest/head probes match
        /// the close-combat posture the follower will actually use.
        /// </summary>
        public static bool IsHardCoverFromThreat(CustomNavigationPoint? cover, Vector3 threatPosition)
        {
            if (cover == null ||
                cover.CoverType == CoverType.Foliage ||
                !IsFinitePoint(cover.Position) ||
                !IsFinitePoint(threatPosition))
            {
                return false;
            }

            return IsHardCoverFromThreat(cover.Position, threatPosition);
        }

        /// <summary>
        /// Hard-cover validation for a NavMesh point that does not have an EFT cover-point wrapper.
        /// The high-poly/terrain mask intentionally excludes foliage and grass.
        /// </summary>
        public static bool IsHardCoverFromThreat(Vector3 coverPosition, Vector3 threatPosition)
        {
            if (!IsFinitePoint(coverPosition) || !IsFinitePoint(threatPosition))
            {
                return false;
            }

            Vector3 threatEye = threatPosition + Vector3.up * HardCoverThreatEyeHeight;
            Vector3 headCenter = coverPosition + Vector3.up * HardCoverHeadHeight;
            Vector3 threatDirection = threatEye - headCenter;
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            Vector3 headSide = Vector3.Cross(Vector3.up, threatDirection.normalized) * HardCoverHeadHalfWidth;
            return HasNearbyHardBlocker(coverPosition + Vector3.up * HardCoverChestHeight, threatEye) &&
                   HasNearbyHardBlocker(headCenter - headSide, threatEye) &&
                   HasNearbyHardBlocker(headCenter + headSide, threatEye);
        }

        private static bool HasNearbyHardBlocker(Vector3 origin, Vector3 target)
        {
            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                return false;
            }

            return Physics.Raycast(
                       origin,
                       direction / distance,
                       out RaycastHit hit,
                       distance,
                       LayerMaskClass.HighPolyWithTerrainMask,
                       QueryTriggerInteraction.Ignore) &&
                   hit.distance <= HardCoverMaxBlockerDistance;
        }

        private static bool IsFinitePoint(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                   !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                   !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }

        public static bool IsNavigablePoint(Vector3 botPosition, Vector3 point, float maxDistance, NavMeshPath existingMesh = null)
        {
            NavMeshPath navMeshPath = existingMesh != null ? existingMesh : new NavMeshPath();

            navMeshPath.ClearCorners();
            bool result = NavMesh.CalculatePath(botPosition, point, -1, navMeshPath);
            if (result && navMeshPath.status == NavMeshPathStatus.PathComplete)
            {
                float dist = navMeshPath.CalculatePathLength();
                if (dist <= maxDistance)
                {
                    return true;
                }
            }

            return false;
        }
        /** Find a position from where the bot can shoot at the given target **/
        public static Vector3? FindShootPosition(BotOwner botOwner, float minDistance, float maxRadius, Func<Vector3, bool> eligibleCheck = null, Vector3? manualTarget = null)
        {
            if (!botOwner.Memory.HaveEnemy) return null;

            Vector3 botPosition = botOwner.GetPlayer.Transform.position;
            Vector3 botWeaponOffset = botOwner.ShootData.WeaponRootOffset;
            LayerMask Mask = botOwner.LookSensor.Mask;

            Vector3 targetPosition = botOwner.Memory.GoalEnemy.CurrPosition;

            if (manualTarget.HasValue) targetPosition = manualTarget.Value;

            NavMeshPath mesh = new NavMeshPath();

            List<Vector3> shootTarget = new List<Vector3>
            {
                botOwner.Memory.GoalEnemy.Person.MainParts[BodyPartType.head].Position,
                botOwner.Memory.GoalEnemy.Person.MainParts[BodyPartType.body].Position
            };

            // define the angular steps and the scan distance
            int numSteps = 72; // e.g., 36 steps for a 10° interval, adjust for precision/performance
            float angleStep = 360f / numSteps;
            float scanDistance = maxRadius;

            float _weaponShootDistMaxSqr = botOwner.LookSensor.MaxShootDist * botOwner.LookSensor.MaxShootDist;

            // evaluate positions around the bot in a circular pattern
            for (int i = 0; i < numSteps; i++)
            {
                float angle = i * angleStep;
                Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 scanPosition = targetPosition + direction * scanDistance;

                NavMeshHit navMeshHit;
                if (!NavMesh.SamplePosition(scanPosition, out navMeshHit, 10f, NavMesh.AllAreas)) continue;

                // - check if the position is valid based on conditions
                if ((navMeshHit.position - targetPosition).sqrMagnitude < minDistance * minDistance) continue;
                if (!IsNavigablePoint(botPosition, navMeshHit.position, 150f, mesh)) continue;

                // - check if position meets the eligibility requirements
                if (eligibleCheck != null && !eligibleCheck(navMeshHit.position)) continue;

                // - check if bot can shoot from this position to the target (head/torso of the enemy)
                bool canShoot = false;
                foreach (var target in shootTarget)
                {
                    ShootPointClass shootPoint = new ShootPointClass(target, 1f);

                    if ((navMeshHit.position - shootPoint.Point).sqrMagnitude >= _weaponShootDistMaxSqr)
                    {
                        break;
                    }

                    if (Utils.CanShootToTarget(shootPoint, navMeshHit.position + botWeaponOffset, Mask, false) ||
                        Utils.CanShootToTarget(shootPoint, navMeshHit.position + botWeaponOffset * 0.5f, Mask, false))
                    {
                        canShoot = true;
                        break;
                    }
                }

                if (canShoot) return navMeshHit.position;
            }

            return null;
        }

        /// <summary>
        /// Returns true if walking from <paramref name="from"/> to <paramref name="to"/> along the
        /// NavMesh path exposes the walker to line-of-sight from <paramref name="enemyPosition"/> at
        /// any of <paramref name="sampleCount"/> evenly-spaced points along the path.
        /// A standing head-height offset (1.4 m) is applied to both sides before the visibility test.
        /// Returns true (exposed) when no complete NavMesh path exists.
        /// </summary>
        public static bool IsPathExposedToEnemy(
            Vector3 from,
            Vector3 to,
            Vector3 enemyPosition,
            LayerMask mask,
            int sampleCount = 4)
        {
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, -1, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return true;
            }

            Vector3[] corners = path.corners;
            if (corners.Length < 2)
            {
                return false;
            }

            float totalLength = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                totalLength += (corners[i] - corners[i - 1]).magnitude;
            }

            if (totalLength <= 0f)
            {
                return false;
            }

            Vector3 enemyEye = enemyPosition + Vector3.up * 1.4f;
            float stepLength = totalLength / (sampleCount + 1);
            float accumulated = 0f;
            int cornerIdx = 1;

            for (int s = 1; s <= sampleCount; s++)
            {
                float targetDist = s * stepLength;

                while (cornerIdx < corners.Length - 1)
                {
                    float segLen = (corners[cornerIdx] - corners[cornerIdx - 1]).magnitude;
                    if (accumulated + segLen >= targetDist)
                    {
                        break;
                    }

                    accumulated += segLen;
                    cornerIdx++;
                }

                float seg = (corners[cornerIdx] - corners[cornerIdx - 1]).magnitude;
                float t = seg > 0f ? (targetDist - accumulated) / seg : 0f;
                Vector3 sample = Vector3.Lerp(corners[cornerIdx - 1], corners[cornerIdx], Mathf.Clamp01(t));
                Vector3 sampleEye = sample + Vector3.up * 1.4f;

                if (!Physics.Linecast(enemyEye, sampleEye, mask))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPathTooCloseToEnemy(
            Vector3 from,
            Vector3 to,
            Vector3 enemyPosition,
            float minDistance)
        {
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, -1, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return true;
            }

            Vector3[] corners = path.corners;
            if (corners.Length == 0)
            {
                return false;
            }

            float minDistanceSqr = minDistance * minDistance;
            for (int i = 0; i < corners.Length; i++)
            {
                if (DistanceXZSqr(corners[i], enemyPosition) < minDistanceSqr)
                {
                    return true;
                }
            }

            for (int i = 1; i < corners.Length; i++)
            {
                if (DistancePointToSegmentXZSqr(enemyPosition, corners[i - 1], corners[i]) < minDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private static float DistanceXZSqr(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static float DistancePointToSegmentXZSqr(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(segmentStart.x, segmentStart.z);
            Vector2 b = new Vector2(segmentEnd.x, segmentEnd.z);

            Vector2 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;
            if (abLenSqr <= 0.0001f)
            {
                return (p - a).sqrMagnitude;
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr);
            Vector2 closest = a + ab * t;
            return (p - closest).sqrMagnitude;
        }

        public static CustomNavigationPoint FindPointForAssault(BotOwner botOwner)
        {

            ShootPointClass shootPointClass = botOwner.CurrentEnemyTargetPosition(true);
            CoverShootType coverShootType = CoverShootType.shoot;
            if (shootPointClass == null)
            {
                coverShootType = CoverShootType.hide;
            }
            PointsArrayType pointsArrayType = PointsArrayType.both;
            float num = 1600f;
            int num2 = 20;
            CoverSearchData coverSearchData = new CoverSearchData((botOwner.Position + botOwner.Memory.GoalEnemy.CurrPosition) * 0.5f, botOwner.CoverSearchInfo, coverShootType, num, 0f, CoverSearchType.distToToCenter, shootPointClass, null, new Vector3?(botOwner.Position), ECheckSHootHide.shootAndHide, new CoverSearchDefenceDataClass(0f), PointsArrayType.covers, false, null, new int?(num2), "Default");
            coverSearchData.UseSelfFindPoint = false;
            coverSearchData.ArrayType = pointsArrayType;
            coverSearchData.UseLineCastToCover = true;
            CustomNavigationPoint coverPointMain = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(coverSearchData, false);
            botOwner.Memory.BotCurrentCoverInfo.SetCover(coverPointMain, true);

            return coverPointMain;
        }
    }
}
