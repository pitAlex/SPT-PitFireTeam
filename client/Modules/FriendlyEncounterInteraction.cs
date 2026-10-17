using EFT;
using EFT.Interactive;
using pitTeam.Patches;
using UnityEngine;

namespace pitTeam.Modules
{
    // Raid-owned state; layer activation never resets the answer or player rejection.
    internal sealed class FriendlyEncounterInteraction
    {
        internal const float Range = 17f;
        private const float RefusalPause = 1.5f;
        private static BotOwner invitingBot;
        private static float invitationUntil;
        private bool cancelled, playerDeclined, invitationPending, ownsHello, refusing;
        private float until, cooldownUntil, helloAt, nextSocialCheck, refusalStartedAt;
        internal bool Waiting { get; private set; }
        internal static BotOwner InvitingBot
        {
            get
            {
                if (invitingBot != null && (Time.time >= invitationUntil || invitingBot.IsDead ||
                    invitingBot.BotState != EBotState.Active || BossPlayers.IsFollower(invitingBot) ||
                    invitingBot.GetPlayer?.HealthController?.IsAlive != true)) invitingBot = null;
                return invitingBot;
            }
        }
        internal static void ResetRaid() => invitingBot = null;

        internal bool CanWait(BotOwner bot, Player human)
        {
            if (cancelled || bot == null || bot.IsDead || bot.BotState != EBotState.Active ||
                BossPlayers.IsFollower(bot) || bot.GetPlayer?.HealthController?.IsAlive != true ||
                human?.HealthController?.IsAlive != true ||
                !AllegiancePmcFriendship.IsEnabled ||
                (!refusing && bot.Side != EPlayerSide.Savage && BossPlayers.HasDeniedRecruitment(bot.ProfileId)))
            {
                End(bot);
                return false;
            }
            if (Waiting && (refusing ? Time.time - refusalStartedAt >= RefusalPause : Time.time >= until))
            {
                End(bot);
                return false;
            }
            if (!Waiting && (Time.time < cooldownUntil || (bot.Position - human.Position).sqrMagnitude > Range * Range)) return false;
            if (!AllegianceFriendlyGreeting.CanSignal(bot, Waiting && ownsHello, refusing))
            {
                End(bot);
                return false;
            }
            // Walking away does not shorten an active wait; only new waits need close sight.
            return Waiting || AllegianceFriendlyGreeting.HasSight(bot, human, Range);
        }

        internal void Begin(BotOwner bot, Player human)
        {
            if (Waiting || !CanWait(bot, human)) return;
            Waiting = true;
            ownsHello = false;
            until = Time.time + 20f;
            helloAt = Time.time + UnityEngine.Random.Range(0.5f, 3f);
            nextSocialCheck = Time.time;
            // A failed proactive roll is remembered, but is not a spoken refusal.
            AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot, human, true, out _);
        }

        internal void End(BotOwner bot)
        {
            // A spoken bot refusal is terminal even if urgent work interrupts its pause.
            if (refusing) cancelled = true;
            refusing = false;
            refusalStartedAt = 0f;
            if (Waiting) cooldownUntil = Time.time + 10f;
            Waiting = false;
            invitationPending = false;
            if (ReferenceEquals(invitingBot, bot)) invitingBot = null;
        }

        internal void Cancel(BotOwner bot)
        {
            cancelled = true;
            End(bot);
        }

        internal void BeginRefusal(BotOwner bot)
        {
            if (refusing) return; // Repeated requests cannot restart the pause.
            if (cancelled || !Waiting) { Cancel(bot); return; }
            refusing = true;
            refusalStartedAt = Time.time;
            invitationPending = false;
            if (ReferenceEquals(invitingBot, bot)) invitingBot = null;
        }

        internal void Update(BotOwner bot, Player human, AllegianceFriendlyGreeting greeting)
        {
            if (!Waiting || !CanWait(bot, human)) return;
            // Only the active layer's action calls this method. No route/aim/pose state is rewritten.
            if (bot.Mover?.Sprinting == true) bot.Mover.Sprint(false, false);
            bot.Mover?.Stop();
            bot.Steering?.LookToPoint(human.Position + BotOwner.STAY_HEIGHT);
            if (refusing) return;
            if (Time.time < nextSocialCheck) return;
            nextSocialCheck = Time.time + 0.5f;
            if (!AllegianceFriendlyGreeting.HasSight(bot, human, Range)) return;
            if (Time.time >= helloAt)
            {
                bool hadHello = greeting.HelloSent;
                greeting.TryHello(bot, human, (bot.Position - human.Position).sqrMagnitude);
                if (!hadHello && greeting.HelloSent) ownsHello = true;
            }
            var currentInviter = InvitingBot;
            if (playerDeclined || invitationPending || (currentInviter != null && !ReferenceEquals(currentInviter, bot)) ||
                !FollowRequestPatch.HasPickupCapacity(human) ||
                !AllegiancePmcFriendship.TryResolveRecruitmentAcceptance(bot, human, true, out bool accepted) || !accepted ||
                FollowerForcedPhraseGate.TryGetArmedPhrase(bot, out _)) return;

            // Direct native audio avoids Cooperation's implicit Hello and command broadcasts.
            EPhraseTrigger phrase = bot.Side == EPlayerSide.Savage ? EPhraseTrigger.FollowMe : EPhraseTrigger.Cooperation;
            if (!FollowRequestPatch.TryPlayDirectFollowerPhrase(bot, phrase)) return;
            invitingBot = bot;
            invitationUntil = until;
            invitationPending = true;
            Logger.LogInfo($"[FriendlyEncounter] candidate={bot.ProfileId} invitation={phrase}");
        }

        internal bool Respond(BotOwner bot, Player human, bool accept)
        {
            if (!Waiting || !invitationPending || !ReferenceEquals(InvitingBot, bot) ||
                !CanWait(bot, human) || !AllegianceFriendlyGreeting.HasSight(bot, human, Range)) return false;
            if (!accept) playerDeclined = true; // Never becomes a bot's recruitment refusal.
            End(bot);
            if (accept) bot.BotsGroup?.RequestsController?.TryAskFollowMeRequest(human, bot);
            return true;
        }
    }
}
