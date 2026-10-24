using System;
using System.Reflection;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal class FollowerPoisonImmunityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(ActiveHealthController), nameof(ActiveHealthController.DoIntoxication));

        [PatchPrefix]
        internal static bool Prefix(ActiveHealthController __instance, ref ActiveHealthController.Effect __result)
        {
            if (!BossPlayers.IsFollower(__instance?.Player?.AIData?.BotOwner)) return true;
            __result = null;
            return false;
        }
    }

    internal class FollowerLethalPoisonImmunityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(ActiveHealthController), nameof(ActiveHealthController.DoLethalIntoxication));

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, ref ActiveHealthController.Effect __result) =>
            FollowerPoisonImmunityPatch.Prefix(__instance, ref __result);
    }

    internal class FollowerStimulatorSelectionPatch : ModulePatch
    {
        private static bool reported;
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotStimulators), nameof(BotStimulators.FindMeds));

        [PatchPostfix]
        internal static void FilterAntidotes(BotStimulators __instance)
        {
            if (!BossPlayers.IsFollower(__instance?._owner) || __instance._stimulator == null ||
                !FollowerStimulatorPolicy.HasAntidoteEffect(__instance._stimulator)) return;
            try
            {
                // Reuse the slot-filtered native cache; never rescan inventory here.
                __instance._stimulator = null;
                __instance.HaveSmt = false;
                foreach (Meds med in __instance._medsList)
                    if (med is EFT.InventoryLogic.Stimulator stim && !FollowerStimulatorPolicy.HasAntidoteEffect(stim))
                    { __instance._stimulator = stim; __instance.HaveSmt = true; break; }
            }
            catch (Exception ex)
            {
                if (reported) return;
                reported = true;
                Logger.LogError($"[Medical] Could not filter follower antidote selection. {ex}");
            }
        }
    }

    internal class FollowerStimulatorUsePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotStimulators), nameof(BotStimulators.TryApply));

        [PatchPrefix]
        private static void Prefix(BotStimulators __instance) => FollowerStimulatorSelectionPatch.FilterAntidotes(__instance);
    }
}
