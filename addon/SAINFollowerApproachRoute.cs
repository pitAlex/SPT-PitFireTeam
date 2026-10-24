using pitTeam.BigBrain;
using pitTeam.Modules;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.SAINAddon;

// Only called after forward cover and the direct provisional step fail.
internal sealed class SAINFollowerApproachRoute
{
    private NavMeshPath path;
    private readonly Vector3[] corners = new Vector3[64];

    internal bool TryStep(Vector3 from, Vector3 known, out Vector3 step, out string failure)
    {
        step = default; failure = "approachSampleFailed";
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, 2f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(known, out NavMeshHit end, 2f, NavMesh.AllAreas) ||
            !SainRegroupBridge.SameLevel(from, start.position) || !SainRegroupBridge.SameLevel(known, end.position)) return false;
        path ??= new NavMeshPath();
        failure = "approachRouteIncomplete";
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;
        int count = path.GetCornersNonAlloc(corners);
        failure = "approachRouteCorners";
        // Reject a truncated buffer and a route that does not reach our sampled knowledge.
        if (count < 2 || count >= corners.Length || (corners[0] - start.position).sqrMagnitude > 4f ||
            (corners[count - 1] - end.position).sqrMagnitude > 4f) return false;
        float remaining = 20f;
        step = corners[0];
        for (int i = 1; i < count; i++)
        {
            Vector3 segment = corners[i] - step;
            float length = segment.magnitude;
            if (length >= remaining) { step += segment.normalized * remaining; break; }
            step = corners[i]; remaining -= length;
        }
        failure = "approachStepUnsampled";
        // Native RunToPoint/WalkToPoint samples its destination within 0.5m.
        // A point interpolated between path corners may be outside that radius.
        if (!NavMesh.SamplePosition(step, out NavMeshHit accepted, 0.5f, NavMesh.AllAreas)) return false;
        step = accepted.position;
        failure = "approachStepInvalid";
        // Progress is along the complete route: a necessary detour may initially
        // increase straight-line distance. Recheck the bounded leg before committing it.
        if ((step - from).sqrMagnitude < 4f ||
            !SainRegroupBridge.TryGetDistance(from, step, out float lengthToStep) ||
            lengthToStep > FollowerPushGeometry.MaxForwardRoute) return false;
        failure = null;
        return true;
    }
}
