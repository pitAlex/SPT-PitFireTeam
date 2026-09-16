using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches
{
    // General SAIN compatibility: every bot shares this loop, regardless of tactic/addon.
    internal static class SainVisionRecoveryPatch
    {
        private static MethodInfo? factory;
        private static FieldInfo? disposed;
        private static PropertyInfo? controller;
        [ThreadStatic] private static bool creatingReplacement;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type? type = Type.GetType("SAIN.Components.VisionRaycastJob, SAIN");
                factory = type?.GetMethod("UpdateEFTVision", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                disposed = type?.GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                controller = type?.GetProperty("BotController", BindingFlags.Instance | BindingFlags.Public);
                if (factory == null || factory.ReturnType != typeof(IEnumerator) ||
                    disposed?.FieldType != typeof(bool) || controller == null ||
                    !typeof(UnityEngine.Object).IsAssignableFrom(controller.PropertyType))
                {
                    Modules.Logger.LogError("[SAIN] Shared vision recovery skipped: native coroutine/lifecycle signature changed.");
                    return;
                }
                harmony.Patch(factory, postfix: new HarmonyMethod(typeof(SainVisionRecoveryPatch), nameof(Wrap)));
                Modules.Logger.LogInfo("[SAIN] Shared EFT vision recovery enabled for all SAIN bots (core).");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Shared vision recovery could not be installed: {ex}");
            }
        }

        private static void Wrap(object __instance, ref IEnumerator __result)
        {
            if (creatingReplacement || __result == null || __result is SainVisionRecoveryEnumerator) return;
            __result = new SainVisionRecoveryEnumerator(__result,
                () => CreateReplacement(__instance),
                () => !(bool)disposed!.GetValue(__instance) &&
                    controller!.GetValue(__instance) is UnityEngine.Object owner && owner != null,
                () => Time.realtimeSinceStartup,
                new WaitForSeconds(1f),
                (error, failures) => Modules.Logger.LogError(
                    $"[SAIN] Shared EFT vision update failed; resetting its iterator in 1 second " +
                    $"(failures={failures}, repeated reports limited to 30 seconds). Enemy memory and raycast job retained. {error}"));
        }

        private static IEnumerator CreateReplacement(object job)
        {
            // Calling the patched factory must not nest another recovery wrapper.
            creatingReplacement = true;
            try { return (IEnumerator)factory!.Invoke(job, null); }
            finally { creatingReplacement = false; }
        }
    }
}
