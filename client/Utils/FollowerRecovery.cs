using EFT;
using HarmonyLib;
using System;

namespace pitTeam.Utils
{
    public static class FollowerRecovery
    {
        public static void SoftReset(BotOwner? bot)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) return;

            StopShooting(bot);
            bot.Mover.Pause = false;
            bot.PatrollingData?.Pause();

            if (bot.BotRequestController?.CurRequest != null)
            {
                bot.BotRequestController.CurRequest.Complete();
                bot.BotRequestController.CurRequest = null;
            }

            BaseBrain? baseBrain = bot.Brain?.BaseBrain;
            if (baseBrain == null) return;

            if (baseBrain.CurLayerInfo is BaseLogicLayer simpleLayer)
            {
                simpleLayer.CalcActionNextFrame(null);
            }
            else if (baseBrain.CurLayerInfo is BaseLogicLayerSimple baseLayer)
            {
                baseLayer._nextFrameDropAction = true;
            }

            baseBrain.CalcActionNextFrame();
        }

        public static void StopShooting(BotOwner? bot)
        {
            if (bot == null)
            {
                return;
            }

            bot.ShootData?.EndShoot();
            bot.WeaponManager?.ShootController?.SetTriggerPressed(false);
        }

        public static void CheckReloadTimeout(BotOwner? bot)
        {
            BotReload? reload = bot?.WeaponManager?.Reload;
            if (reload?.Reloading != true)
            {
                return;
            }

            float timeout = reload._reloadType == BotReload.EReloadType.MagReload
                ? BotReload.MAG_RELOAD_MAX_TIME
                : BotReload.AMMO_RELOAD_MAX_TIME;
            if (UnityEngine.Time.time - reload._reloadStartTime <= timeout)
            {
                return;
            }

            // EFT owns the reload transaction and its safe timeout thresholds. Our custom layers
            // must keep polling the same watchdog when a completion callback is lost. Vanilla only
            // clears the first timed-out transaction on a reload object, so retain the same public
            // flag fallback for a later lost callback instead of waiting forever.
            reload.CheckReloadLongTime();
            if (reload.Reloading)
            {
                reload.Reloading = false;
            }
        }

    }
}
