using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    /// <summary>
    /// Gives followers first ownership of visible-part selection. This runs before SAIN's global
    /// EnemyInfo prefix, while the separate reflected patch removes SAIN's later center-mass clamp.
    /// </summary>
    internal sealed class FollowerAimTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.GetVisiblePartToShoot));
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(new[] { "me.sol.sain" })]
        private static bool PatchPrefix(EnemyInfo __instance, ref Vector3 __result)
        {
            try
            {
                if (!FollowerAimTargetPolicy.TrySelectFollowerShootPoint(
                        __instance,
                        out Vector3 shootPoint,
                        out bool hasShootPoint) ||
                    !hasShootPoint)
                {
                    return true;
                }

                __result = shootPoint;
                return false;
            }
            catch
            {
                // Fail open so an unexpected EFT body-part shape retains vanilla/SAIN selection.
                return true;
            }
        }
    }

    internal static class FollowerSainCenterMassPatch
    {
        internal static void Apply(Harmony harmony)
        {
            System.Type? shootDataType = System.Type.GetType("SAIN.SAINComponent.Classes.SAINShootData, SAIN");
            MethodInfo? findCenterMass = shootDataType != null
                ? AccessTools.Method(shootDataType, "FindCenterMassPoint")
                : null;
            MethodInfo? getEnemyPart = shootDataType != null
                ? AccessTools.Method(shootDataType, "GetEnemyPartToShoot", new[] { typeof(EnemyInfo) })
                : null;
            if (findCenterMass == null || getEnemyPart == null)
            {
                Modules.Logger.LogError("[SAIN] Failed to find SAINShootData follower target-selection methods.");
                return;
            }

            harmony.Patch(
                findCenterMass,
                prefix: new HarmonyMethod(
                    typeof(FollowerSainCenterMassPatch).GetMethod(
                        nameof(SkipCenterMassForFollower),
                        BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(
                getEnemyPart,
                prefix: new HarmonyMethod(
                    typeof(FollowerSainCenterMassPatch).GetMethod(
                        nameof(UseFollowerVisiblePart),
                        BindingFlags.Static | BindingFlags.NonPublic)));
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool SkipCenterMassForFollower(object __1, ref Vector3? __result)
        {
            if (!FollowerAimTargetPolicy.IsRegisteredSainFollower(__1))
            {
                return true;
            }

            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool UseFollowerVisiblePart(EnemyInfo __0, ref Vector3? __result)
        {
            if (!FollowerAimTargetPolicy.TrySelectFollowerShootPoint(
                    __0,
                    out Vector3 shootPoint,
                    out bool hasShootPoint))
            {
                return true;
            }

            __result = hasShootPoint ? shootPoint : (Vector3?)null;
            return false;
        }
    }
}
