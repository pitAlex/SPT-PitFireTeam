using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using UnityEngine;

namespace pitTeam.Patches
{
    // Do not disable Player/GameObject updates: native hands callbacks and body loads must finish.
    internal sealed class GearSwapApplyBotTickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotOwner), nameof(BotOwner.UpdateManual));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(BotOwner __instance)
        {
            TeammateGearSwap.CheckApplyDeadline();
            return !TeammateGearSwap.Protects(__instance?.GetPlayer);
        }
    }

    internal sealed class GearSwapApplyBrainTickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(AICoreAgent<BotLogicDecision>), "Update");
        // Must run before BigBrain's replacement prefix, which itself executes the action.
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(AICoreAgent<BotLogicDecision> __instance) =>
            !ReferenceEquals(TeammateGearSwap.Current?.Bot?.Brain?.Agent, __instance)
            || !TeammateGearSwap.Protects(TeammateGearSwap.Current?.Bot?.GetPlayer);
    }

    internal sealed class GearSwapApplyMovementPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.Move), new[] { typeof(Vector2) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, ref Vector2 direction)
        {
            if (TeammateGearSwap.Protects(__instance)) direction = Vector2.zero;
        }
    }

    internal sealed class GearSwapApplyTriggerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player.FirearmController),
            nameof(Player.FirearmController.SetTriggerPressed), new[] { typeof(bool) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(Player ____player, ref bool pressed)
        {
            if (TeammateGearSwap.Protects(____player)) pressed = false;
        }
    }

    internal sealed class GearSwapApplyWeaponSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotWeaponSelector), nameof(BotWeaponSelector.TryChangeToSlot));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(BotWeaponSelector __instance, ref bool __result)
        {
            if (!TeammateGearSwap.Protects(__instance?._owner?.GetPlayer)) return true;
            __result = false;
            return false;
        }
    }

    internal sealed class GearSwapApplyShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ApplyShot));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) => !TeammateGearSwap.Protects(__instance);
    }

    internal sealed class GearSwapApplyAutomaticFirePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player.FirearmController.AutomaticFireOperation), "InternalOnFireEvent", Type.EmptyTypes);
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player.FirearmController.AutomaticFireOperation __instance)
        {
            if (!TeammateGearSwap.Protects(__instance.Player)) return true;
            __instance.SwitchToIdling();
            return false;
        }
    }

    internal sealed class GearSwapApplyDamageInfoPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ApplyDamageInfo));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) => !TeammateGearSwap.Protects(__instance);
    }

    internal sealed class GearSwapApplyExplosionArmorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ApplyExplosionDamageToArmor));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance) => !TeammateGearSwap.Protects(__instance);
    }

    internal sealed class GearSwapApplyHealthDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ActiveHealthController __instance, ref float __result)
        {
            if (!TeammateGearSwap.Protects(__instance)) return true;
            __result = 0f;
            return false;
        }
    }

    internal sealed class GearSwapApplyHealthDrainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ActiveHealthController __instance, float value) => value >= 0f || !TeammateGearSwap.Protects(__instance);
    }

    internal sealed class GearSwapActorDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(ActiveHealthController __instance) => TeammateGearSwap.ActorDying(__instance);
    }

    internal sealed class GearSwapRaidStopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(LocalGame), nameof(LocalGame.Stop),
            new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => TeammateGearSwap.EndRaid();
    }

    internal sealed class GearSwapActorDeadFallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Player), "OnDead");
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance) => TeammateGearSwap.ActorDying(__instance?.ActiveHealthController);
    }

    internal sealed class GearSwapBotsStopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotsController), "StopGettingInfo");
        [PatchPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix() => TeammateGearSwap.EndRaid();
    }
}
