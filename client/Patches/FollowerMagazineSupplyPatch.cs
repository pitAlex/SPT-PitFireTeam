using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.BigBrain;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // Native combat/patrol refill callers share the same real, finite supply allocation.
    // Ordinary bots and the native timing/decision to request a refill remain untouched.
    internal sealed class FollowerMagazineSupplyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotReload), nameof(BotReload.AddAmmoToMag));
        [PatchPrefix]
        private static bool Prefix(BotReload __instance, Weapon weapon, Magazine foundMag)
        {
            BotOwner bot = __instance._owner;
            if (bot == null || !BossPlayers.IsFollower(bot)) return true;
            try
            {
                if (!bot.IsDead && bot.GetPlayer?.HealthController?.IsAlive == true &&
                    FollowerOutOfCombatReloadPolicy.TryFillMagazineWithLooseAmmo(bot, foundMag, weapon))
                    __instance.NoAmmoForReloadCached = false;
            }
            catch (Exception ex) { Logger.LogError("[MagazineSupply] Refill rejected: " + ex); }
            return false;
        }
    }
}
