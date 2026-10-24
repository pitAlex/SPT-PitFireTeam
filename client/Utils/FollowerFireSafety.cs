using EFT;
using pitTeam.BigBrain.Actions;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

namespace pitTeam.Utils
{
    // Shared execution safety, independent of the owning combat brain. Never asks a
    // decision/aim provider to update, and never caches a clear lane between frames.
    internal static class FollowerFireSafety
    {
        internal static bool IsUnsafe(BotOwner owner, Vector3? target = null, bool suppression = false)
        {
            if (owner == null || !BossPlayers.IsFollower(owner))
                return false;
            if (!FollowerCombatActionBase.TryGetActualFirearmShotVector(owner, out Vector3 origin, out Vector3 direction))
                return target.HasValue || (owner.GetPlayer?.HandsController is Player.FirearmController firearm && firearm.CurrentFireport != null);

            if (!Finite(origin) || !Finite(direction) || direction.sqrMagnitude <= 0.0001f)
                return true;

            // A projectile can continue past its aim point. Only squad members are
            // examined, so checking the entire forward lane adds no world query.
            if (FollowerShotSafety.IsFriendlyInAimLane(owner, origin, direction, float.MaxValue))
                return true;

            if (!target.HasValue) return false;
            Vector3 point = target.Value;
            if (!Finite(point) || (point - origin).sqrMagnitude <= 0.0001f) return true;
            return suppression
                ? FollowerShotSafety.IsFriendlyInSuppressionLane(owner, origin, point)
                : FollowerShotSafety.IsFriendlyInShotLane(owner, origin, point);
        }

        internal static void Stop(BotOwner owner)
        {
            FollowerRecovery.StopShooting(owner);
            try { FollowerSainManualFireSafety.StopManualFire(owner); }
            catch { /* Native trigger release still stands if optional state is unavailable. */ }
        }

        internal static bool IsActiveFireUnsafe(BotOwner owner)
        {
            Vector3? target = null;
            bool suppression = false;
            if (FollowerSainManualFireSafety.TryGetManualTarget(owner, out Vector3 manualTarget, out suppression))
                target = manualTarget;
            else if (owner?.AimingManager?.CurrentAiming?.IsReady == true)
                target = owner.AimingManager.CurrentAiming.EndTargetPoint;
            return IsUnsafe(owner, target, suppression);
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
