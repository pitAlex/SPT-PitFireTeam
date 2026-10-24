using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.Modules
{
    // Local, follower-only wire geometry. No NavMesh carving, trap state, or damage changes.
    public static class FollowerWireNavigation
    {
        private static readonly Dictionary<Vector3, (bool Unsafe, float Until)> CoverPositions = new();
        internal static void ClearCoverCache() => CoverPositions.Clear();
        public static bool IsCoverUnsafe(Vector3 position)
        {
            if (!FollowerHazardAvoidance.IsSupportedMap) return false;
            // Resolve world changes before consulting the bounded, one-second candidate cache.
            if (FollowerHazardAvoidance.GetWires().Count == 0) return false;
            if (CoverPositions.TryGetValue(position, out var cached) && Time.time < cached.Until) return cached.Unsafe;
            bool unsafePoint = IsInside(position);
            if (CoverPositions.Count >= 128) CoverPositions.Clear();
            CoverPositions[position] = (unsafePoint, Time.time + 1f);
            return unsafePoint;
        }
        private static NavMeshPath Path;
        private static readonly List<Vector3> Route = new List<Vector3>(16);
        // Native bot feet can be above low Labyrinth wire colliders, as on the water walkway.
        private static readonly Vector3 BodyPadding = new Vector3(0.35f, 1.2f, 0.35f);

        internal static bool Intersects(FollowerHazardAvoidance.WireVolume wire, Vector3 from, Vector3 to)
        {
            if (!FollowerHazardAvoidance.IsSupportedMap || !wire.Active) return false;
            from += Vector3.up * 0.6f;
            to += Vector3.up * 0.6f;
            Bounds bounds = wire.Collider.bounds;
            bounds.Expand(BodyPadding * 2f);
            if (!FollowerHazardGeometry.Intersects(from - bounds.center, to - bounds.center, bounds.extents)) return false;
            if (!(wire.Collider is BoxCollider box)) return true;
            Matrix4x4 inverse = box.transform.worldToLocalMatrix;
            Vector3 padding = new Vector3(
                Mathf.Abs(inverse.m00) * BodyPadding.x + Mathf.Abs(inverse.m01) * BodyPadding.y + Mathf.Abs(inverse.m02) * BodyPadding.z,
                Mathf.Abs(inverse.m10) * BodyPadding.x + Mathf.Abs(inverse.m11) * BodyPadding.y + Mathf.Abs(inverse.m12) * BodyPadding.z,
                Mathf.Abs(inverse.m20) * BodyPadding.x + Mathf.Abs(inverse.m21) * BodyPadding.y + Mathf.Abs(inverse.m22) * BodyPadding.z);
            return FollowerHazardGeometry.Intersects(inverse.MultiplyPoint3x4(from) - box.center,
                inverse.MultiplyPoint3x4(to) - box.center, box.size * 0.5f + padding);
        }

        internal static bool IsInside(Vector3 position)
        {
            if (!FollowerHazardAvoidance.IsSupportedMap) return false;
            var wires = FollowerHazardAvoidance.GetWires();
            for (int i = 0; i < wires.Count; i++)
                if (Intersects(wires[i], position, position)) return true;
            return false;
        }

        internal static bool TryPlan(BotOwner owner, bool allowEscape, out Vector3[] route, out bool escaping, ref float nextAttempt)
        {
            route = null;
            escaping = false;
            if (!FollowerHazardAvoidance.IsSupportedMap) return false;
            Vector3 start = owner.Position;
            var wires = FollowerHazardAvoidance.GetWires();
            FollowerHazardAvoidance.WireVolume inside = null;
            for (int i = 0; i < wires.Count; i++)
                if (Intersects(wires[i], start, start)) { inside = wires[i]; break; }
            if (inside != null)
            {
                escaping = true;
                if (!allowEscape || Time.time < nextAttempt) return false;
                nextAttempt = Time.time + 2f;
                Bounds bounds = inside.Collider.bounds;
                Vector3 min = bounds.min, max = bounds.max;
                float best = 12f;
                TryRoute(start, new Vector3(min.x - 0.65f, start.y, start.z), null, null, true, ref best, ref route);
                TryRoute(start, new Vector3(max.x + 0.65f, start.y, start.z), null, null, true, ref best, ref route);
                TryRoute(start, new Vector3(start.x, start.y, min.z - 0.65f), null, null, true, ref best, ref route);
                TryRoute(start, new Vector3(start.x, start.y, max.z + 0.65f), null, null, true, ref best, ref route);
                return route != null;
            }

            Vector3 end;
            if (SainAddonBridge.TryGetMovementCorner(owner, out Vector3? sainCorner))
            {
                if (!sainCorner.HasValue) return false;
                end = sainCorner.Value;
            }
            else
            {
                if (!owner.HasPathAndNotComplete || owner.Mover == null) return false;
                end = owner.Mover.CurrentCornerPoint;
            }
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.25f || Mathf.Abs(delta.y) > 1f) return false;
            if (delta.magnitude > 12f) end = start + delta.normalized * 12f;
            for (int i = 0; i < wires.Count; i++)
            {
                var wire = wires[i];
                if (!wire.Active || wire.Collider.bounds.SqrDistance(start) > 36f ||
                    !Intersects(wire, start, end) || Intersects(wire, end, end)) continue;
                if (Time.time < nextAttempt) return false;
                nextAttempt = Time.time + 2f;
                Bounds bounds = wire.Collider.bounds;
                Vector3 min = bounds.min - Vector3.one * 0.65f, max = bounds.max + Vector3.one * 0.65f;
                float best = Vector3.Distance(start, end) + 10f;
                if (Mathf.Abs(delta.z) >= Mathf.Abs(delta.x))
                {
                    float entry = delta.z > 0f ? min.z : max.z, exit = delta.z > 0f ? max.z : min.z;
                    TryRoute(start, end, new Vector3(min.x, start.y, entry), new Vector3(min.x, start.y, exit), false, ref best, ref route);
                    TryRoute(start, end, new Vector3(max.x, start.y, entry), new Vector3(max.x, start.y, exit), false, ref best, ref route);
                }
                else
                {
                    float entry = delta.x > 0f ? min.x : max.x, exit = delta.x > 0f ? max.x : min.x;
                    TryRoute(start, end, new Vector3(entry, start.y, min.z), new Vector3(exit, start.y, min.z), false, ref best, ref route);
                    TryRoute(start, end, new Vector3(entry, start.y, max.z), new Vector3(exit, start.y, max.z), false, ref best, ref route);
                }
                return route != null; // At most two bypasses / six complete-path probes per attempt.
            }
            return false;
        }

        private static void TryRoute(Vector3 start, Vector3 end, Vector3? first, Vector3? second,
            bool escaping, ref float best, ref Vector3[] result)
        {
            Route.Clear();
            Route.Add(start);
            if ((first.HasValue && !AddLeg(first.Value)) || (second.HasValue && !AddLeg(second.Value)) || !AddLeg(end)) return;
            var wires = FollowerHazardAvoidance.GetWires();
            float length = 0f;
            for (int i = 1; i < Route.Count; i++) length += Vector3.Distance(Route[i - 1], Route[i]);
            if (length >= best) return;
            for (int w = 0; w < wires.Count; w++)
            {
                bool left = !escaping || !Intersects(wires[w], start, start);
                for (int i = 1; i < Route.Count; i++)
                {
                    if (left && Intersects(wires[w], Route[i - 1], Route[i])) return;
                    if (!Intersects(wires[w], Route[i], Route[i])) left = true;
                }
                if (!left) return;
            }
            best = length;
            result = Route.ToArray();
        }

        private static bool AddLeg(Vector3 target)
        {
            Path ??= new NavMeshPath();
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 0.5f, NavMesh.AllAreas) ||
                Mathf.Abs(hit.position.y - target.y) > 0.5f ||
                !NavMesh.CalculatePath(Route[Route.Count - 1], hit.position, NavMesh.AllAreas, Path) ||
                Path.status != NavMeshPathStatus.PathComplete) return false;
            Vector3[] corners = Path.corners;
            if (corners.Length < 2 || (corners[corners.Length - 1] - hit.position).sqrMagnitude > 0.25f) return false;
            for (int i = 1; i < corners.Length; i++)
            {
                if (Mathf.Abs(corners[i].y - Route[0].y) > 1f) return false;
                Route.Add(corners[i]);
            }
            return true;
        }
    }
}
