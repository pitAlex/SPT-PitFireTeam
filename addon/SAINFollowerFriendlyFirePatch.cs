using EFT;
using HarmonyLib;
using SAIN;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using SAIN.Preset.Shared.Enums;
using pitTeam.Modules;
using pitTeam.Utils;
using System.Reflection;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerFriendlyFirePatch
    {
        private static readonly FieldInfo? FriendlyFireStatusField =
            AccessTools.Field(typeof(SAINFriendlyFireClass), "<FriendlyFireStatus>k__BackingField");

        public static void Apply(Harmony harmony)
        {
            MethodInfo? targetByPoint = AccessTools.Method(
                typeof(SAINFriendlyFireClass),
                nameof(SAINFriendlyFireClass.UpdateFriendlyFireStatus),
                new[] { typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(BotComponent) });
            MethodInfo? targetByDistance = AccessTools.Method(
                typeof(SAINFriendlyFireClass),
                nameof(SAINFriendlyFireClass.UpdateFriendlyFireStatus),
                new[] { typeof(float), typeof(Vector3), typeof(Vector3), typeof(BotComponent) });
            if (targetByPoint == null || targetByDistance == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINFriendlyFireClass.UpdateFriendlyFireStatus overloads for follower FF patch.");
                return;
            }

            harmony.Patch(targetByPoint, postfix: new HarmonyMethod(typeof(SAINFollowerFriendlyFirePatch), nameof(Postfix_UpdateFriendlyFireStatusByPoint)));
            harmony.Patch(targetByDistance, postfix: new HarmonyMethod(typeof(SAINFollowerFriendlyFirePatch), nameof(Postfix_UpdateFriendlyFireStatusByDistance)));
            Modules.Logger.LogInfo("[Init] SAIN follower shot-lane friendly-fire patch applied.");
        }

        private static void Postfix_UpdateFriendlyFireStatusByPoint(
            SAINFriendlyFireClass __instance,
            Vector3 target,
            Vector3 weaponFirePort,
            Vector3 weaponPointDirection,
            BotComponent bot,
            ref bool __result)
        {
            if (!ShouldBlockFollowerShot(bot, weaponFirePort, weaponPointDirection, target, null))
            {
                return;
            }

            ForceFriendlyBlock(__instance, ref __result);
        }

        private static void Postfix_UpdateFriendlyFireStatusByDistance(
            SAINFriendlyFireClass __instance,
            float distance,
            Vector3 weaponFirePort,
            Vector3 weaponPointDirection,
            BotComponent bot,
            ref bool __result)
        {
            if (!ShouldBlockFollowerShot(bot, weaponFirePort, weaponPointDirection, null, distance))
            {
                return;
            }

            ForceFriendlyBlock(__instance, ref __result);
        }

        private static bool ShouldBlockFollowerShot(
            BotComponent bot,
            Vector3 weaponFirePort,
            Vector3 weaponPointDirection,
            Vector3? target,
            float? distance)
        {
            if (bot?.BotOwner == null || !BossPlayers.IsFollower(bot.BotOwner))
            {
                return false;
            }

            BotOwner shooter = bot.BotOwner;
            IBotAiming? currentAiming = shooter.AimingManager?.CurrentAiming;
            Vector3 realTargetPoint = currentAiming?.RealTargetPoint ?? Vector3.zero;
            if (realTargetPoint != Vector3.zero)
            {
                return FollowerShotSafety.IsFriendlyInShotLane(shooter, weaponFirePort, realTargetPoint);
            }

            if (target.HasValue && target.Value != Vector3.zero)
            {
                return FollowerShotSafety.IsFriendlyInShotLane(shooter, weaponFirePort, target.Value);
            }

            if (distance.HasValue)
            {
                return FollowerShotSafety.IsFriendlyInShotLane(shooter, weaponFirePort, weaponPointDirection, distance.Value);
            }

            return false;
        }

        private static void ForceFriendlyBlock(SAINFriendlyFireClass? instance, ref bool result)
        {
            result = false;
            if (instance != null && FriendlyFireStatusField != null)
            {
                FriendlyFireStatusField.SetValue(instance, FriendlyFireStatus.FriendlyBlock);
            }
        }
    }
}
