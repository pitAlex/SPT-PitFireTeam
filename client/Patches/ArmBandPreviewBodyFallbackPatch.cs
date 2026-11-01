using EFT.UI;
using EFT.Visual;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal sealed class ArmBandPreviewBodyFallbackPatch : ModulePatch
    {
        private static readonly FieldInfo Meshes = AccessTools.Field(typeof(ArmBandView), "_dict");
        private static readonly HashSet<string> Reported = new HashSet<string>();
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ArmBandView), "SetBodyHandler");

        [PatchPrefix]
        private static void Prefix(ArmBandView __instance, ref string __0)
        {
            // Only rendered menu previews. Live player/bot dressing retains native ownership.
            if (__instance == null || __instance.GetComponentInParent<PlayerModelView>() == null || __0 == null
                || !(Meshes?.GetValue(__instance) is Dictionary<string, Mesh[]> meshes) || meshes.ContainsKey(__0) || meshes.Count == 0)
                return;
            // This is the exact first entry EFT itself uses after reporting a missing mesh.
            // Resolve it before the call; do not alter shared mesh tables or saved clothing.
            string fallback = meshes.First().Key;
            if (Reported.Add(__0))
                pitFireTeam.Log.LogWarning($"[UI] Custom body '{__0}' has no armband mesh; using EFT's preview fallback '{fallback}'.");
            __0 = fallback;
        }
    }
}
