using EFT;
using UnityEngine;

namespace pitTeam.BigBrain
{
    /// <summary>Resolves a rifle suppression point from current sight or a recent position report.</summary>
    internal static class FollowerSuppressTargetPolicy
    {
        public const float MaxContactAgeSeconds = 2f;

        public static bool TryGetTarget(EnemyInfo? enemy, out Vector3 target)
        {
            target = Vector3.zero;
            if (enemy == null)
            {
                return false;
            }

            if (enemy.IsVisible)
            {
                target = enemy.GetBodyPartPosition();
                return FollowerCombatCommon.IsFinite(target);
            }

            // Keep each position paired with the timestamp of that report. A recent visual
            // report must not renew a different, older sensed position (or vice versa).
            float newestReportTime = 0f;
            ConsiderReport(enemy.PersonalLastPos, enemy.PersonalLastSeenTime, ref newestReportTime, ref target);
            BotGroupEnemyInfo? group = enemy.GroupInfo;
            if (group != null)
            {
                ConsiderReport(enemy.EnemyLastPositionReal, group.EnemyLastSeenTimeReal, ref newestReportTime, ref target);
                ConsiderReport(enemy.EnemyLastPosition, group.EnemyLastSeenTimeSense, ref newestReportTime, ref target);
            }

            return newestReportTime > 0f;
        }

        private static void ConsiderReport(Vector3 position, float observedAt, ref float newestReportTime, ref Vector3 target)
        {
            float age = Time.time - observedAt;
            if (observedAt <= newestReportTime ||
                !(age >= 0f && age <= MaxContactAgeSeconds) ||
                !FollowerCombatCommon.IsFinite(position) ||
                position.sqrMagnitude <= 0.01f)
            {
                return;
            }

            newestReportTime = observedAt;
            target = position + BotOwner.STAY_HEIGHT;
        }
    }
}
