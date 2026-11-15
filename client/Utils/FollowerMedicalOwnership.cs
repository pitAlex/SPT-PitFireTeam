using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class FollowerMedicalOwnership
    {
        private const float OrphanGraceSeconds = 1.5f;
        private const float RetryDelaySeconds = 0.75f;
        private static readonly ConditionalWeakTable<BotAbstractMedsToPart, Attempt> Attempts = new();

        private sealed class Attempt
        {
            internal readonly Meds Item;
            internal float OrphanedAt = -1f;
            internal bool DrawCompleted;

            internal Attempt(Meds item) => Item = item;
        }

        internal static Callback<IMedsController> TrackDraw(BotAbstractMedsToPart medicine, Meds item, Callback<IMedsController> callback)
        {
            var attempt = new Attempt(item);
            Attempts.Remove(medicine);
            Attempts.Add(medicine, attempt);
            return result =>
            {
                if (!Owns(medicine, attempt) || attempt.DrawCompleted) return;
                attempt.DrawCompleted = true;
                if (!result.Succeed || result.Value == null)
                {
                    try
                    {
                        if (!result.Succeed) callback(result);
                    }
                    finally
                    {
                        if (Owns(medicine, attempt)) Release(medicine);
                    }
                    return;
                }

                callback(result);
                // EFT owns the actual use and supplies its completion callback once the
                // hands controller is ready. Keep an older completion from clearing a newer use.
                IQuickUseItem hands = result.Value;
                var complete = hands.GetOnUsedCallback();
                if (complete != null)
                    hands.SetOnUsedCallback(outcome =>
                    {
                        if (!Owns(medicine, attempt)) return;
                        Attempts.Remove(medicine);
                        complete(outcome);
                    });
            };
        }

        internal static void Reconcile(BotOwner bot)
        {
            var player = bot.GetPlayer;
            if (player == null || bot.Medecine == null) return;
            Reconcile(bot.Medecine.FirstAid, player);
            Reconcile(bot.Medecine.SurgicalKit, player);
        }

        internal static bool IsSettling(BotAbstractMedsToPart medicine) =>
            medicine?._using == true && Attempts.TryGetValue(medicine, out _);

        private static void Reconcile(BotAbstractMedsToPart medicine, Player player)
        {
            if (medicine == null || !Attempts.TryGetValue(medicine, out var attempt)) return;

            // Allow Unity to finish the current draw, hide or weapon return before
            // concluding that both the medical hands and callback were lost.
            if (player.ProcessStatus != Player.EProcessStatus.None || player.ScheduledProcess != null ||
                (player.HandsController is Player.MedsController hands && ReferenceEquals(hands.Item, attempt.Item)) ||
                (player.ActiveHealthController?.FindExistingEffect<IMedEffect>(EBodyPart.Common) is ActiveHealthController.MedEffect effect &&
                 ReferenceEquals(effect.MedItem, attempt.Item)))
            {
                attempt.OrphanedAt = -1f;
                return;
            }

            if (attempt.OrphanedAt < 0f) attempt.OrphanedAt = Time.time;
            if (Time.time - attempt.OrphanedAt < OrphanGraceSeconds) return;
            if (medicine._using) Release(medicine);
            else Attempts.Remove(medicine);
        }

        private static bool Owns(BotAbstractMedsToPart medicine, Attempt attempt) =>
            Attempts.TryGetValue(medicine, out var current) && ReferenceEquals(current, attempt);

        private static void Release(BotAbstractMedsToPart medicine)
        {
            Attempts.Remove(medicine);
            var controllers = medicine._owner.Medecine;
            medicine._nextPosibleUseTime = Math.Max(medicine._nextPosibleUseTime, Time.time + RetryDelaySeconds);
            medicine._lastIsCanceled = false;
            medicine.Using = false;
            controllers.AidCallback(controllers.FirstAid?.Using == true ||
                controllers.SurgicalKit?.Using == true || controllers.Stimulators?.Using == true);
        }
    }
}
