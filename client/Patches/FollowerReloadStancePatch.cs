using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // SAIN only re-evaluates CanSetPatrol when its desired stance changes. An already
    // relaxed bot can therefore keep the hands animator in patrol throughout a reload.
    internal sealed class FollowerReloadStancePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.PropertySetter(typeof(BotReload), nameof(BotReload.Reloading));

        [PatchPostfix]
        private static void Postfix(BotReload __instance, bool value)
        {
            if (!value || !__instance.Reloading) return;
            try
            {
                BotOwner bot = __instance._owner;
                if (bot == null || bot.IsDead || !BossPlayers.IsFollower(bot) ||
                    bot.WeaponManager?.ShootController == null ||
                    !ReferenceEquals(bot.WeaponManager?.Reload, __instance) ||
                    !ReferenceEquals(bot.GetPlayer?.HandsController, bot.WeaponManager?.ShootController)) return;

                MovementContext movement = bot.GetPlayer?.MovementContext;
                if (movement == null || !movement._isInPatrol) return;

                // Run before ReloadMagazine/ReloadAmmo starts its animation. Use the native
                // setter so BlockFirearms still wins; never change the reload or hands operation.
                movement.SetPatrol(false);
                BattleRecorder.RecordAmmoRefill(bot, "stance", "reloadStart", () => new
                {
                    patrolStance = movement._isInPatrol,
                    blockFirearms = movement.BlockFirearms
                });
            }
            catch (Exception ex) { Logger.LogError("[ReloadStance] Failed to leave patrol stance: " + ex); }
        }
    }
}
