using EFT;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>Shared validity and steering for follower threat look targets, independent of search destinations.</summary>
    internal static class CombatAttackMoveLook
    {
        private const float MaxForcedTurnAngle = 145f;
        private const float RecentThreatMemorySeconds = 12f;
        private const float FreshThreatSeconds = 2f;
        private const float RememberedPointArrivalDistance = 2f;
        private const float RememberedPointFloorTolerance = 2f;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BotOwner, RememberedLookState> RememberedLooks = new();

        private sealed class RememberedLookState
        {
            public string EnemyProfileId = string.Empty;
            public float ReportTime;
            public Vector3 PreviousBotPosition;
            public bool HasPreviousPosition;
            public bool Reached;
        }

        public static bool TryLookThreatFacing(BotOwner botOwner, EnemyInfo? goalEnemy, bool allowHardTurn = false)
        {
            return TryGetCombatThreatLookPoint(botOwner, goalEnemy, out Vector3 point) &&
                   TryLookPointFacing(botOwner, point, allowHardTurn);
        }

        public static bool TryGetCombatThreatLookPoint(BotOwner botOwner, EnemyInfo? goalEnemy, out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;
            if (botOwner == null)
            {
                return false;
            }
            if (TryGetReliableThreatLookPoint(botOwner, goalEnemy, out lookPoint, FreshThreatSeconds))
            {
                return true;
            }

            // Incoming fire supplies its own short-lived bearing. Never substitute an unseen
            // enemy's live transform just because dogfight or retreat still owns the action.
            return global::pitTeam.Utils.FollowerAwareness.TryGetRecentFireThreatLookPoint(botOwner, out lookPoint, out _) &&
                   FollowerCombatCommon.IsFinite(lookPoint) &&
                   (lookPoint - GetLookOrigin(botOwner)).sqrMagnitude > 0.01f;
        }

        public static bool TryGetReliableThreatLookPoint(
            BotOwner botOwner,
            EnemyInfo? goalEnemy,
            out Vector3 lookPoint,
            float maxReportAge = RecentThreatMemorySeconds)
        {
            lookPoint = Vector3.zero;
            if (botOwner == null || goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                try
                {
                    Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
                    if (FollowerCombatCommon.IsFinite(bodyPoint) &&
                        (bodyPoint - GetLookOrigin(botOwner)).sqrMagnitude > 0.01f)
                    {
                        RememberedLooks.Remove(botOwner);
                        lookPoint = bodyPoint;
                        return true;
                    }
                }
                catch
                {
                    // An enemy's body parts may disappear during despawn; do not invent a target.
                }
                return false;
            }

            if (!TryGetRecentThreatPosition(goalEnemy, maxReportAge, out Vector3 position, out float reportTime) ||
                !IsRememberedPointUseful(botOwner, goalEnemy, ref position, reportTime))
            {
                return false;
            }

            lookPoint = position + Vector3.up * 0.8f;
            return (lookPoint - GetLookOrigin(botOwner)).sqrMagnitude > 0.01f;
        }

        private static bool TryGetRecentThreatPosition(EnemyInfo goalEnemy, float maxAge, out Vector3 position, out float reportTime)
        {
            position = Vector3.zero;
            reportTime = 0f;
            float personalTime = goalEnemy.PersonalLastSeenTime;
            float sharedTime = goalEnemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f;
            Vector3 personalPosition = goalEnemy.PersonalLastPos;
            Vector3 sharedPosition = goalEnemy.GroupInfo != null ? goalEnemy.EnemyLastPositionReal : Vector3.zero;
            bool personalValid = IsRecentThreatPosition(personalPosition, personalTime, maxAge);
            bool sharedValid = IsRecentThreatPosition(sharedPosition, sharedTime, maxAge);
            if (sharedValid && (!personalValid || sharedTime > personalTime))
            {
                position = sharedPosition;
                reportTime = sharedTime;
                return true;
            }
            if (personalValid)
            {
                position = personalPosition;
                reportTime = personalTime;
                return true;
            }
            return false;
        }

        private static bool IsRecentThreatPosition(Vector3 position, float seenTime, float maxAge)
        {
            float age = Time.time - seenTime;
            return seenTime > 0f && age >= 0f && age <= maxAge &&
                   FollowerCombatCommon.IsFinite(position) && position.sqrMagnitude > 0.01f;
        }

        private static bool IsRememberedPointUseful(BotOwner bot, EnemyInfo enemy, ref Vector3 position, float reportTime)
        {
            RememberedLookState state = RememberedLooks.GetOrCreateValue(bot);
            string enemyId = enemy.ProfileId ?? string.Empty;
            if (state.EnemyProfileId != enemyId || reportTime > state.ReportTime)
            {
                state.EnemyProfileId = enemyId;
                state.ReportTime = reportTime;
                state.HasPreviousPosition = false;
                state.Reached = false;
            }
            else if (reportTime < state.ReportTime)
            {
                return false;
            }

            // Re-querying the same report, changing actions, or moving away again must not
            // revive a point already visited. Only a new observation can renew that point.
            Vector3 toPoint = position - bot.Position;
            bool sameFloor = Mathf.Abs(toPoint.y) <= RememberedPointFloorTolerance;
            toPoint.y = 0f;
            if (sameFloor && toPoint.sqrMagnitude <= RememberedPointArrivalDistance * RememberedPointArrivalDistance)
            {
                state.Reached = true;
            }
            if (sameFloor && state.HasPreviousPosition &&
                Mathf.Abs(position.y - state.PreviousBotPosition.y) <= RememberedPointFloorTolerance &&
                PassedRememberedPoint(state.PreviousBotPosition, bot.Position, position))
            {
                state.Reached = true;
            }
            state.PreviousBotPosition = bot.Position;
            state.HasPreviousPosition = true;
            return (!sameFloor || toPoint.sqrMagnitude > 0.01f) &&
                   (!state.Reached || Time.time - reportTime <= FreshThreatSeconds);
        }

        private static bool PassedRememberedPoint(Vector3 previous, Vector3 current, Vector3 point)
        {
            Vector3 before = point - previous;
            Vector3 after = point - current;
            Vector3 travel = current - previous;
            before.y = after.y = travel.y = 0f;
            if (travel.sqrMagnitude <= 0.01f || Vector3.Dot(before, after) > 0f)
            {
                return false;
            }
            Vector3 nearest = before - travel * Mathf.Clamp01(Vector3.Dot(before, travel) / travel.sqrMagnitude);
            return nearest.sqrMagnitude <= RememberedPointArrivalDistance * RememberedPointArrivalDistance;
        }

        public static bool TryLookReliableThreatFacing(BotOwner botOwner, EnemyInfo? goalEnemy, bool allowHardTurn = false)
        {
            return TryGetReliableThreatLookPoint(botOwner, goalEnemy, out Vector3 point) &&
                   TryLookPointFacing(botOwner, point, allowHardTurn);
        }

        public static bool TryLookPointFacing(BotOwner botOwner, Vector3 lookPoint, bool allowHardTurn = false)
        {
            if (!TryGetLookDirection(botOwner, lookPoint, out Vector3 direction))
            {
                return false;
            }
            if (!allowHardTurn && Vector3.Angle(botOwner.LookDirection, direction) > MaxForcedTurnAngle)
            {
                botOwner.LookData.SetLookPointByHearing(null);
                return false;
            }
            botOwner.LookData.SetLookPointByHearing(null);
            botOwner.Memory?.botObserveData?.Stop();
            botOwner.Steering.LookToPoint(lookPoint);
            return true;
        }

        public static Vector3 GetLookOrigin(BotOwner botOwner)
        {
            return botOwner.WeaponRoot != null && FollowerCombatCommon.IsFinite(botOwner.WeaponRoot.position)
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
        }

        public static bool TryGetLookDirection(BotOwner botOwner, Vector3 point, out Vector3 direction)
        {
            direction = point - GetLookOrigin(botOwner);
            return FollowerCombatCommon.IsFinite(point) && FollowerCombatCommon.IsFinite(direction) && direction.sqrMagnitude > 0.01f;
        }

        public static Vector3 GetMovementOrLevelDirection(BotOwner botOwner)
        {
            Vector3 direction = botOwner.Mover.HasPathAndNoComplete ? botOwner.Mover.DirCurPoint : Vector3.zero;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.01f && botOwner.Mover.HasPathAndNoComplete && botOwner.Mover.TargetPoint.HasValue)
            {
                direction = botOwner.Mover.TargetPoint.Value - botOwner.Position;
                direction.y = 0f;
            }
            if (!FollowerCombatCommon.IsFinite(direction) || direction.sqrMagnitude <= 0.01f)
            {
                direction = botOwner.LookDirection;
                direction.y = 0f;
            }
            return FollowerCombatCommon.IsFinite(direction) && direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.forward;
        }

        public static void LookAlongMovementOrLevel(BotOwner botOwner)
        {
            botOwner.LookData.SetLookPointByHearing(null);
            botOwner.Memory?.botObserveData?.Stop();
            botOwner.Steering.LookToDirection(GetMovementOrLevelDirection(botOwner));
        }

        public static float GetThreatLookAngle(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            return botOwner != null &&
                   TryGetCombatThreatLookPoint(botOwner, goalEnemy, out Vector3 point) &&
                   TryGetLookDirection(botOwner, point, out Vector3 direction)
                ? Vector3.Angle(botOwner.LookDirection, direction)
                : 180f;
        }
    }
}
