using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Classes;
using SAIN.Components;
using System;
using System.Reflection;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerAimSwayPatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(PlayerMovementController),
                nameof(PlayerMovementController.UpdateTurnSettings),
                new[] { typeof(float), typeof(BotOwner), typeof(BotComponent), typeof(bool) });

            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find PlayerMovementController.UpdateTurnSettings for follower aim-sway patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerAimSwayPatch), nameof(Prefix_UpdateTurnSettings)));
            Modules.Logger.LogInfo("[Init] SAIN follower aim-sway suppression patch applied.");
        }

        private static void Prefix_UpdateTurnSettings(BotOwner botOwner, BotComponent botComponent, ref bool randomSwayEnabled)
        {
            try
            {
                if (!randomSwayEnabled || botOwner == null || botComponent == null || !BossPlayers.IsFollower(botOwner))
                {
                    return;
                }

                if (!HasVisibleAimTarget(botOwner, botComponent))
                {
                    return;
                }

                randomSwayEnabled = false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower aim-sway patch.");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool HasVisibleAimTarget(BotOwner botOwner, BotComponent botComponent)
        {
            if (botOwner.AimingManager?.CurrentAiming is not BotAimingData aimClass)
            {
                return false;
            }

            if (aimClass.Status == AimStatus.NoTarget)
            {
                return false;
            }

            return botComponent.GoalEnemy?.IsVisible == true && botComponent.GoalEnemy.CanShoot;
        }
    }
}
