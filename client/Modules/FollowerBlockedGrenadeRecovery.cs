using System;
using System.Runtime.CompilerServices;
using EFT;
using UnityEngine;

namespace pitTeam.Modules
{
    // A release veto has already prevented consumption/spawn. Complete only that
    // exact high-throw operation on a later update, then return to the main weapon.
    internal static class FollowerBlockedGrenadeRecovery
    {
        private sealed class Pending
        {
            internal Player.GrenadeHandsController Hands;
            internal Player.GrenadeHandsController.HighThrowOperation Operation;
            internal float NextCheck, Deadline;
            internal bool Completed;
        }

        private static readonly ConditionalWeakTable<BotOwner, Pending> States = new ConditionalWeakTable<BotOwner, Pending>();

        internal static void Record(BotOwner bot, Player.BaseGrenadeHandsController hands)
        {
            if (!(hands is Player.GrenadeHandsController grenadeHands) ||
                !(grenadeHands.CurrentGrenadeOperation is Player.GrenadeHandsController.HighThrowOperation operation) ||
                States.TryGetValue(bot, out _)) return;
            States.Add(bot, new Pending { Hands = grenadeHands, Operation = operation,
                NextCheck = Time.time + 0.2f, Deadline = Time.time + 5f });
        }

        internal static void Update(BotOwner bot)
        {
            if (bot == null || !States.TryGetValue(bot, out Pending state) || Time.time < state.NextCheck) return;
            state.NextCheck = Time.time + 0.25f;
            if (bot.IsDead || bot.BotState != EBotState.Active || Time.time > state.Deadline ||
                bot.GetPlayer == null || bot.WeaponManager?.Selector == null)
            { States.Remove(bot); return; }

            try
            {
                var hands = bot.GetPlayer.HandsController;
                if (bot.Medecine?.Using == true)
                { States.Remove(bot); return; }
                if (!state.Completed)
                {
                    state.Completed = true;
                    if (ReferenceEquals(hands, state.Hands) &&
                        ReferenceEquals(state.Hands.CurrentGrenadeOperation, state.Operation))
                    {
                        // This native endpoint is idempotent after the animation has finished.
                        // Do not fast-forward a grenade operation: it may spawn another grenade.
                        state.Operation.OnDropFinishedAction();
                        if (bot.WeaponManager.Grenades?.ThrowindNow == true)
                            bot.WeaponManager.Grenades.EndAll();
                    }
                    else if (!(hands is Player.FirearmController))
                    { States.Remove(bot); }
                    return; // Let the native previous-weapon callback settle before main selection.
                }

                if (bot.Medecine?.Using == true ||
                    (!ReferenceEquals(hands, state.Hands) && !(hands is Player.FirearmController)))
                { States.Remove(bot); return; }
                if (!bot.WeaponManager.Selector.IsWeaponReady || bot.WeaponManager.Reload?.Reloading == true) return;
                States.Remove(bot); // Exactly one main-weapon request per blocked release.
                if (hands is Player.FirearmController &&
                    bot.WeaponManager.Selector.LastEquipmentSlot == bot.WeaponManager.Selector._mainWeapon) return;
                bot.WeaponManager.Selector.TakeMainWeapon();
                BattleRecorder.RecordGrenadeEvent(bot, "recovery", "blockedReleaseReturnToMain");
            }
            catch (Exception ex)
            {
                States.Remove(bot);
                Logger.LogError($"[Grenade] Blocked-release recovery failed: {ex}");
            }
        }
    }
}
