using FollowerShotSafety = pitTeam.Utils.FollowerShotSafety;
using SAIN.Components;
using SAIN.Models.Enums;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Regroup's action-local early hold. Shared trigger and continuing-fire guards
// belong to Core and cover all follower actions, including native suppression.
internal static class SainRegroupFireSafety
{
    internal static bool IsSafe(BotComponent bot, Vector3 target)
    {
        Vector3 origin = bot.Transform.WeaponData.FirePort;
        Vector3 direction = bot.Transform.WeaponData.PointDirection;
        Vector3 delta = target - origin;
        if (!Finite(target) || !Finite(origin) || !Finite(direction) ||
            delta.sqrMagnitude <= 0.0001f || direction.sqrMagnitude <= 0.0001f) return false;
        return !FollowerShotSafety.IsFriendlyInSuppressionLane(bot.BotOwner, origin, target) &&
            !FollowerShotSafety.IsFriendlyInAimLane(bot.BotOwner, origin, direction, delta.magnitude);
    }

    internal static void CheckActiveBurst(BotComponent bot)
    {
        // Native TrySuppressAnyEnemy can reuse an active burst without TryShoot.
        // Recheck before the regroup movement throttle; never cache a clear lane.
        if (bot.ManualShoot.Reason == EShootReason.Suppress && !IsSafe(bot, bot.ManualShoot.ShootPosition))
            bot.Suppression.ResetSuppressing();
    }

    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
        !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
}
