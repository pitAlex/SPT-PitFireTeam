using EFT;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Shared look-control helpers for attack-moving style actions. These methods keep movement
    /// actions from aiming into walls or away from a close threat while the action itself still owns
    /// pathing and movement speed.
    /// </summary>
    internal static class CombatAttackMoveLook
    {
        private const float MaxForcedTurnAngle = 145f;

        public static bool TryLookThreatFacing(BotOwner botOwner, EnemyInfo? goalEnemy, bool allowHardTurn = false)
        {
            if (goalEnemy == null)
            {
                botOwner.LookData.SetLookPointByHearing(null);
                return false;
            }

            return TryLookPointFacing(botOwner, GetThreatLookPoint(goalEnemy), allowHardTurn);
        }

        /// <summary>
        /// Resolves only follower-owned visual or remembered enemy positions. Unlike the close-threat
        /// helper above, this must not fall through to the enemy's hidden live transform: ordinary
        /// movement uses it when deciding whether threat-facing is safer than route-facing.
        /// </summary>
        public static bool TryGetReliableThreatLookPoint(
            BotOwner botOwner,
            EnemyInfo? goalEnemy,
            out Vector3 lookPoint)
        {
            lookPoint = Vector3.zero;
            if (botOwner == null || goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                try
                {
                    Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
                    if (FollowerCombatCommon.IsFinite(bodyPoint) &&
                        bodyPoint.sqrMagnitude > 0.01f &&
                        (bodyPoint - botOwner.Position).sqrMagnitude > 0.01f)
                    {
                        lookPoint = bodyPoint;
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (!Enemy.TryGetReliableKnownPosition(botOwner, goalEnemy, out Vector3 knownPosition))
            {
                return false;
            }

            lookPoint = knownPosition + Vector3.up * 0.8f;
            return FollowerCombatCommon.IsFinite(lookPoint) &&
                   (lookPoint - botOwner.Position).sqrMagnitude > 0.01f;
        }

        public static bool TryLookReliableThreatFacing(
            BotOwner botOwner,
            EnemyInfo? goalEnemy,
            bool allowHardTurn = false)
        {
            return TryGetReliableThreatLookPoint(botOwner, goalEnemy, out Vector3 lookPoint) &&
                   TryLookPointFacing(botOwner, lookPoint, allowHardTurn);
        }

        private static bool TryLookPointFacing(BotOwner botOwner, Vector3 lookPoint, bool allowHardTurn)
        {
            if (!FollowerCombatCommon.IsFinite(lookPoint))
            {
                return false;
            }

            Vector3 lookDirection = lookPoint - botOwner.Position;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return false;
            }

            // Attack-moving should keep the weapon locked to the threat lane while body/pathing handles
            // strafing or backpedaling. If the threat is too far behind current view, do not force a
            // full backwards twist every tick; let normal movement/look control recover instead.
            if (!allowHardTurn && Vector3.Angle(botOwner.LookDirection, lookDirection) > MaxForcedTurnAngle)
            {
                botOwner.LookData.SetLookPointByHearing(null);
                return false;
            }

            botOwner.LookData.SetLookPointByHearing(null);
            botOwner.Memory?.botObserveData?.Stop();
            botOwner.Steering.LookToPoint(lookPoint);
            return true;
        }

        public static float GetThreatLookAngle(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner == null || goalEnemy == null)
            {
                return 180f;
            }

            Vector3 lookPoint = GetThreatLookPoint(goalEnemy);
            Vector3 lookDirection = lookPoint - botOwner.Position;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return 0f;
            }

            return Vector3.Angle(botOwner.LookDirection, lookDirection);
        }

        private static Vector3 GetThreatLookPoint(EnemyInfo goalEnemy)
        {
            try
            {
                Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
                if (FollowerCombatCommon.IsFinite(bodyPoint) && bodyPoint.sqrMagnitude > 0.01f)
                {
                    return bodyPoint;
                }
            }
            catch
            {
            }

            Vector3 currentPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);
            if (FollowerCombatCommon.IsFinite(currentPosition) && currentPosition.sqrMagnitude > 0.01f)
            {
                return currentPosition + Vector3.up * 0.8f;
            }

            return goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
        }
    }
}
