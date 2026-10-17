using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using EFT.Interactive;
using UnityEngine;

namespace pitTeam.Modules
{
    // Shared locomotion safety: neither combat brain owns permission to enter a live trap.
    internal static class FollowerHazardAvoidance
    {
        private sealed class Hazard
        {
            internal TriggerZone Zone;
            internal BoxCollider Collider;
        }

        private static readonly List<Hazard> Hazards = new List<Hazard>();
        internal sealed class WireVolume
        {
            internal MonoBehaviour Source;
            internal Collider Collider;
            internal string TriggerId;

            internal bool Active => Source != null && Source.isActiveAndEnabled && Collider != null &&
                Collider.enabled && Collider.gameObject.activeInHierarchy &&
                (TriggerId == null || world?.TriggersEmitter?._ignoreTriggers.Contains(TriggerId) == false);
        }

        private static readonly List<WireVolume> Wires = new List<WireVolume>();
        private static GameWorld world;
        private static GameWorld mapWorld;
        private static string mapId;
        private static bool mapSupported;
        private static bool reportedFailure;

        // Every public entry point gates on this before scene, actor, geometry or navigation work.
        // Cache by both world and location: LocationId may become available after world creation.
        internal static bool IsSupportedMap
        {
            get
            {
                GameWorld current = Singleton<GameWorld>.Instance;
                string location = current == null ? null : current.LocationId;
                if (!ReferenceEquals(mapWorld, current) || !string.Equals(mapId, location, StringComparison.Ordinal))
                {
                    ClearVolumes();
                    world = null;
                    mapWorld = current;
                    mapId = location;
                    mapSupported = string.Equals(location, "labyrinth", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(location, "6733700029c367a3d40b02af", StringComparison.Ordinal);
                    reportedFailure = false;
                }
                return mapSupported;
            }
        }

        private static void ClearVolumes()
        {
            Hazards.Clear();
            Wires.Clear();
            FollowerWireNavigation.ClearCoverCache();
        }

        internal static void Reset()
        {
            ClearVolumes();
            world = null;
            mapWorld = null;
            mapId = null;
            mapSupported = false;
            reportedFailure = false;
        }

        private static void Discover(GameWorld current)
        {
            if (!IsSupportedMap) return;
            ClearVolumes();
            world = current;
            foreach (BarbedWire wire in UnityEngine.Object.FindObjectsOfType<BarbedWire>(true))
                RegisterWire(wire);

            var switchable = new HashSet<string>(StringComparer.Ordinal);
            foreach (HandlerTriggerState handler in UnityEngine.Object.FindObjectsOfType<HandlerTriggerState>(true))
            {
                if (handler._targetState || handler._controlledTriggerId == null) continue;
                foreach (string id in handler._controlledTriggerId)
                    if (!string.IsNullOrEmpty(id)) switchable.Add(id);
            }

            var damaging = new HashSet<string>(StringComparer.Ordinal);
            foreach (HandlerDamage handler in UnityEngine.Object.FindObjectsOfType<HandlerDamage>(true))
                if (!string.IsNullOrEmpty(handler._triggerIdStartDamage)) damaging.Add(handler._triggerIdStartDamage);

            foreach (TriggerZone zone in UnityEngine.Object.FindObjectsOfType<TriggerZone>(true))
            {
                string id = zone._triggerId;
                if (!string.IsNullOrEmpty(id) && id.StartsWith("wire_enter_zone_", StringComparison.Ordinal) && damaging.Contains(id))
                    foreach (BoxCollider collider in zone.GetComponents<BoxCollider>())
                        Wires.Add(new WireVolume { Source = zone, Collider = collider, TriggerId = id });
                if (string.IsNullOrEmpty(id) || !switchable.Contains(id) ||
                    (!damaging.Contains(id) && !id.StartsWith("guntrap_trigger_", StringComparison.Ordinal))) continue;
                foreach (BoxCollider collider in zone.GetComponents<BoxCollider>())
                    Hazards.Add(new Hazard { Zone = zone, Collider = collider });
            }
            Logger.LogInfo($"[HazardAvoidance] Cached {Hazards.Count} switch-controlled Labyrinth damage volumes.");
        }

        internal static IReadOnlyList<WireVolume> GetWires()
        {
            if (!IsSupportedMap) return Wires;
            GameWorld current = Singleton<GameWorld>.Instance;
            if (!ReferenceEquals(world, current)) Discover(current);
            return Wires;
        }

        // Native contact also registers barbed wire spawned after the one-time scene discovery.
        internal static void RegisterWire(BarbedWire wire)
        {
            if (!IsSupportedMap || wire == null) return;
            Collider collider = wire.GetComponent<Collider>();
            if (collider == null) return;
            for (int i = 0; i < Wires.Count; i++)
                if (ReferenceEquals(Wires[i].Collider, collider)) return;
            Wires.Add(new WireVolume { Source = wire, Collider = collider });
            FollowerWireNavigation.ClearCoverCache();
        }

        internal static bool ShouldStop(Player player, Vector3 motion)
        {
            if (!IsSupportedMap) return false;
            // Leave gravity alone, including a stationary bot standing above an underground hazard.
            if (motion.x * motion.x + motion.z * motion.z < 0.00000001f ||
                player?.IsAI != true || !BossPlayers.IsFollower(player.AIData?.BotOwner)) return false;
            try
            {
                GameWorld current = Singleton<GameWorld>.Instance;
                if (current == null) return false;
                if (!ReferenceEquals(world, current)) Discover(current);
                if (Hazards.Count == 0) return false;
                TriggersEmitter emitter = current.TriggersEmitter;
                if (emitter == null) return false;

                // Water ends below the walkway (0.184 m versus feet at 0.484 m).
                // Include one metre below the feet so entry is stopped before stepping off the ledge.
                Vector3 start = player.Position + Vector3.up * 0.4f;
                Vector3 end = start + new Vector3(motion.x, 0f, motion.z);
                Vector3 padding = new Vector3(0.6f, 1.4f, 0.6f);
                for (int i = 0; i < Hazards.Count; i++)
                {
                    Hazard hazard = Hazards[i];
                    TriggerZone zone = hazard.Zone;
                    BoxCollider collider = hazard.Collider;
                    if (zone == null || !zone.isActiveAndEnabled || collider == null || !collider.enabled ||
                        !collider.gameObject.activeInHierarchy || emitter._ignoreTriggers.Contains(zone._triggerId)) continue;

                    Bounds broad = collider.bounds;
                    broad.Expand(padding * 2f);
                    if (!FollowerHazardGeometry.Intersects(start - broad.center, end - broad.center, broad.extents)) continue;

                    Matrix4x4 inverse = collider.transform.worldToLocalMatrix;
                    Vector3 localPadding = new Vector3(
                        Mathf.Abs(inverse.m00) * padding.x + Mathf.Abs(inverse.m01) * padding.y + Mathf.Abs(inverse.m02) * padding.z,
                        Mathf.Abs(inverse.m10) * padding.x + Mathf.Abs(inverse.m11) * padding.y + Mathf.Abs(inverse.m12) * padding.z,
                        Mathf.Abs(inverse.m20) * padding.x + Mathf.Abs(inverse.m21) * padding.y + Mathf.Abs(inverse.m22) * padding.z);
                    if (FollowerHazardGeometry.BlocksEntry(
                        inverse.MultiplyPoint3x4(start) - collider.center,
                        inverse.MultiplyPoint3x4(end) - collider.center,
                        collider.size * 0.5f + localPadding)) return true;
                }
            }
            catch (Exception ex)
            {
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    Logger.LogError($"[HazardAvoidance] Could not evaluate follower trap movement. {ex}");
                }
            }
            return false;
        }
    }

    internal static class FollowerHazardGeometry
    {
        internal static bool BlocksEntry(Vector3 start, Vector3 end, Vector3 half)
        {
            if (!Intersects(start, end, half)) return false;
            if (Inside(start, half))
            {
                // A recruited/teleported bot already inside may move outward, never farther inward.
                float nearest = -1f;
                bool outward = false;
                EscapeAxis(start.x, end.x, half.x, ref nearest, ref outward);
                EscapeAxis(start.y, end.y, half.y, ref nearest, ref outward);
                EscapeAxis(start.z, end.z, half.z, ref nearest, ref outward);
                return !outward;
            }
            return true;
        }

        private static bool Inside(Vector3 p, Vector3 half) =>
            Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;

        private static void EscapeAxis(float start, float end, float half, ref float nearest, ref bool outward)
        {
            if (Mathf.Abs(end - start) < 0.0000001f) return;
            float proximity = Mathf.Abs(start) / half;
            if (proximity <= nearest) return;
            nearest = proximity;
            outward = start == 0f || (end - start) * start > 0f;
        }

        internal static bool Intersects(Vector3 start, Vector3 end, Vector3 half)
        {
            float enter = 0f, exit = 1f;
            Vector3 delta = end - start;
            return Slab(start.x, delta.x, half.x, ref enter, ref exit) &&
                Slab(start.y, delta.y, half.y, ref enter, ref exit) &&
                Slab(start.z, delta.z, half.z, ref enter, ref exit);
        }

        private static bool Slab(float start, float delta, float half, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < 0.0000001f) return Mathf.Abs(start) <= half;
            float a = (-half - start) / delta, b = (half - start) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }
    }
}
