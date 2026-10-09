using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // EFT ReloadMag silently returns for blindfire or an interaction surviving
    // ForceStopInteractions. Complete only that proven idle/no-start path via EFT's callback.
    internal sealed class FollowerMagazineReloadStartPatch : ModulePatch
    {
        internal sealed class Attempt
        {
            internal BotOwner Bot;
            internal Player.FirearmController.FirearmOperation Operation;
            internal Callback Original;
            internal Callback Wrapped;
            internal bool CallbackObserved;

            internal void Complete(IResult result)
            {
                CallbackObserved = true;
                Trace(this, null, "callback", result);
                Original(result);
            }
        }

        private static void Trace(Attempt attempt, Player.FirearmController controller, string phase,
            IResult completion = null, string reason = null)
        {
            try
            {
                if (!pitFireTeam.IsDebugBuild) return;
                Logger.LogInfo($"[ReloadStart] follower='{attempt.Bot.Profile?.Nickname}' weapon={controller?.Item?.Id} " +
                    $"result={phase} success={completion?.Succeed} reason={reason} " +
                    $"operation={controller?.CurrentOperation?.GetType().Name}");
            }
            catch { } // Logging must not suppress native completion or affect a genuine start.
        }

        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player.FirearmController), nameof(Player.FirearmController.ReloadMag),
            new[] { typeof(Magazine), typeof(ItemAddress), typeof(Callback) });

        [PatchPrefix]
        private static void PatchPrefix(Player.FirearmController __instance, ref Callback callback, out Attempt __state)
        {
            __state = null;
            BotOwner bot = __instance._player?.AIData?.BotOwner;
            if (callback == null || bot == null || !BossPlayers.IsFollower(bot) ||
                !(__instance.CurrentOperation is Player.FirearmController.Idling)) return;

            __state = new Attempt { Bot = bot, Operation = __instance.CurrentOperation, Original = callback };
            __state.Wrapped = __state.Complete;
            callback = __state.Wrapped;
        }

        [PatchPostfix]
        private static void PatchPostfix(Player.FirearmController __instance, Attempt __state, bool __runOriginal)
        {
            if (!__runOriginal || __state == null) return;
            if (!ReferenceEquals(__state.Operation, __instance.CurrentOperation))
            {
                FollowerReloadStartRecovery.Clear(__state.Bot); // A genuine start supersedes an older skipped attempt.
                Trace(__state, __instance, "started");
                return;
            }
            if (__state.CallbackObserved) return;

            try
            {
                bool blindfire = __instance.Blindfire;
                bool interaction = __instance._player.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying;
                if (!blindfire && !interaction) return; // Unproven/deferred starts retain native ownership.

                string reason = blindfire ? "blindfire" : "interactionPlaying";
                if (__state.Bot.Memory?.HaveEnemy != true)
                    FollowerReloadStartRecovery.Record(__state.Bot, __instance.Item.Id, reason);
                Trace(__state, __instance, "skipped", reason: reason);
                // Native BotReload's completion callback clears its own flag and applies
                // its cooldown. Never clear Reloading, fast-forward hands or cancel a live operation.
                __state.Wrapped.Fail("Follower magazine reload did not start: " + reason);
            }
            catch (Exception ex) { Logger.LogError("[ReloadStart] Skipped-start completion failed: " + ex); }
        }
    }
}
