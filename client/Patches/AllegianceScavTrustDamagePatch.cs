using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class AllegianceScavTrustDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player), nameof(Player.ApplyDamageInfo),
            new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(EBodyPartColliderType), typeof(float) });

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void PatchPrefix(Player __instance, DamageInfo damageInfo)
        {
            try
            {
                // Native health damage can dispatch death before either hit event.
                // Capture before health processing and BotMemory retaliation alike.
                AllegianceScavRelations.RememberBeforeDamage(damageInfo, __instance);
            }
            catch (Exception ex)
            {
                Logger.LogError("[ScavRelations] Failed to capture pre-damage trust: " + ex);
            }
        }
    }
}
