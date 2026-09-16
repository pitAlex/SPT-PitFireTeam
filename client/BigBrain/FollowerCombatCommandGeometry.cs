using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain;

// Shared geometry/limits for Core combat gestures and addon-native execution.
public static class FollowerCombatCommandGeometry
{
    public const float ArrivalDistance = 1.25f;
    public const float ProgressDistance = 0.35f;
    public const float StallSeconds = 4f;
    public const float ComeCoverMinimumProgress = 1f;
    public static bool IsFinite(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
        !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
        !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    public static bool TryBossApproach(Vector3 origin, Vector3 bossPosition, out Vector3 fallbackPoint)
    {
        const float BossApproachStopDistance = 1.5f;
        const float BossApproachMaxDistance = 2f;

        fallbackPoint = default;
        if (!IsFinite(bossPosition))
        {
            return false;
        }

        if (!NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, BossApproachMaxDistance, NavMesh.AllAreas))
        {
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(origin, bossHit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete ||
            path.corners == null ||
            path.corners.Length == 0)
        {
            return false;
        }

        Vector3 target = GetPointBackFromPathEnd(path.corners, BossApproachStopDistance);
        if (!NavMesh.SamplePosition(target, out NavMeshHit targetHit, 1f, NavMesh.AllAreas))
        {
            return false;
        }

        if ((targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
        {
            target = GetPointBackFromPathEnd(path.corners, 1f);
            if (!NavMesh.SamplePosition(target, out targetHit, 1f, NavMesh.AllAreas) ||
                (targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
            {
                return false;
            }
        }

        fallbackPoint = targetHit.position;
        return IsFinite(fallbackPoint);
    }

    private static Vector3 GetPointBackFromPathEnd(Vector3[] corners, float distanceFromEnd)
    {
        Vector3 target = corners[corners.Length - 1];
        float remaining = Mathf.Max(0f, distanceFromEnd);

        for (int i = corners.Length - 2; i >= 0 && remaining > 0f; i--)
        {
            Vector3 previous = corners[i];
            Vector3 segment = previous - target;
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.01f)
            {
                target = previous;
                continue;
            }

            if (segmentLength >= remaining)
            {
                return target + segment / segmentLength * remaining;
            }

            remaining -= segmentLength;
            target = previous;
        }

        return target;
    }

}
