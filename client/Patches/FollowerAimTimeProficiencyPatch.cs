using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    /// <summary>
    /// Applies the follower-owned Aim Speed percentage after either EFT or SAIN has produced
    /// the regular-firearm aim duration. Underbarrel aiming uses a different controller.
    /// </summary>
    internal sealed class FollowerAimTimeProficiencyPatch : ModulePatch
    {
        private const float MinimumAimTimeSeconds = 0.02f;
        private const float MaximumAimTimeSeconds = 15f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BotAimingData),
                nameof(BotAimingData.CalcTimeShoot),
                new[] { typeof(float), typeof(float) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotAimingData __instance, ref float __result)
        {
            try
            {
                BotOwner botOwner = __instance?._owner;
                BotFollowerPlayer follower = botOwner != null
                    ? BossPlayers.GetFollowerByProfileId(botOwner.ProfileId)
                    : null;
                if (follower == null || float.IsNaN(__result) || float.IsInfinity(__result))
                {
                    return;
                }

                float baseAimTime = __result;
                float aimSpeedFactor = follower.Proficiency.Modifiers.SafeAimSpeedFactor;
                if (!Mathf.Approximately(aimSpeedFactor, 1f))
                {
                    __result = Mathf.Clamp(
                        baseAimTime / aimSpeedFactor,
                        MinimumAimTimeSeconds,
                        MaximumAimTimeSeconds);
                }

                follower.RecordProficiencyAimTime(baseAimTime, __result);
            }
            catch
            {
                // Fail open so compatibility changes cannot break the game's aiming controller.
            }
        }
    }
}
