using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class FollowerFirearmTriggerSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player.FirearmController), nameof(Player.FirearmController.SetTriggerPressed), new[] { typeof(bool) });

        [PatchPrefix]
        private static void PatchPrefix(Player.FirearmController __instance, Player ____player, ref bool pressed)
        {
            if (!pressed) return;
            try
            {
                BotOwner owner = ____player?.AIData?.BotOwner;
                if (!ReferenceEquals(____player?.HandsController, __instance) || !FollowerFireSafety.IsUnsafe(owner)) return;
                pressed = false;
                FollowerFireSafety.Stop(owner);
            }
            catch { /* Preserve the native operation on unsupported hands state. */ }
        }
    }

    internal sealed class FollowerFirearmUpdateSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player.FirearmController), nameof(Player.FirearmController.ManualUpdate), new[] { typeof(float) });

        [PatchPrefix]
        private static void PatchPrefix(Player.FirearmController __instance, Player ____player)
        {
            try
            {
                BotOwner owner = ____player?.AIData?.BotOwner;
                if (owner == null || !BossPlayers.IsFollower(owner) ||
                    !ReferenceEquals(____player.HandsController, __instance) ||
                    (!__instance.IsTriggerPressed && owner.ShootData?.Shooting != true)) return;

                if (FollowerFireSafety.IsActiveFireUnsafe(owner)) FollowerFireSafety.Stop(owner);
                // Always run the operation so pending chamber/ejection/animation
                // work finishes normally. No ammunition or ballistic method is skipped.
            }
            catch { /* Fail open during teardown/unsupported controller state. */ }
        }
    }

    // EFT can reassert the trigger internally for fixed bursts and process several
    // automatic rounds in one update. Veto at round admission, before ammunition
    // consumption, using the operation's own idle transition.
    internal sealed class FollowerAutomaticFireSafetyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player.FirearmController.AutomaticFireOperation), "InternalOnFireEvent", Type.EmptyTypes);

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool PatchPrefix(Player.FirearmController.AutomaticFireOperation __instance)
        {
            bool blocked = false;
            try
            {
                BotOwner owner = __instance.Player?.AIData?.BotOwner;
                if (owner == null || !BossPlayers.IsFollower(owner) ||
                    !ReferenceEquals(__instance.Player.HandsController, __instance.Controller) ||
                    !FollowerFireSafety.IsActiveFireUnsafe(owner)) return true;
                blocked = true;
                FollowerFireSafety.Stop(owner);
                __instance.SwitchToIdling();
                return false;
            }
            catch { return !blocked; }
        }
    }
}
