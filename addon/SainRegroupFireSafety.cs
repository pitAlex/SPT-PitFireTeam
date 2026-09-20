using HarmonyLib;
using FollowerShotSafety = pitTeam.Utils.FollowerShotSafety;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.WeaponFunction;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Keep native suppression selection/cadence. Only addon regroup adds Core's
// wider target lane and independent current muzzle lane before the trigger.
internal static class SainRegroupFireSafety
{
    internal static void Apply(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(ManualShootClass), nameof(ManualShootClass.TryShoot),
            new[] { typeof(Enemy), typeof(Vector3), typeof(bool), typeof(EShootReason) }),
        prefix: new HarmonyMethod(typeof(SainRegroupFireSafety), nameof(BeforeManualShoot)));

    private static bool BeforeManualShoot(ManualShootClass __instance, Vector3 __1,
        EShootReason __3, ref bool __result)
    {
        if (__3 != EShootReason.Suppress ||
            __instance.Bot.CurrentAction is not SAINFollowerSquadRegroupAction) return true;
        if (IsSafe(__instance.Bot, __1)) return true;
        __instance.Bot.Suppression.ResetSuppressing();
        __result = false;
        return false;
    }

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
