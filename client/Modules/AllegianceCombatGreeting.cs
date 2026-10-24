using EFT;
using UnityEngine;

namespace pitTeam.Modules
{
    // Additional raid-owned speech. It never owns movement, gestures or combat decisions.
    internal sealed class AllegianceCombatGreeting
    {
        private bool cancelled, finished, started;
        private int helpCount;
        private float nextCheck, nextPhrase;

        internal void Cancel()
        {
            cancelled = true;
            started = false;
            helpCount = 0;
            nextCheck = nextPhrase = 0f;
        }

        internal void Update(BotOwner bot, Player human)
        {
            if (cancelled || finished || !AllegiancePmcFriendship.IsEnabled || bot == null || bot.IsDead ||
                bot.BotState != EBotState.Active || BossPlayers.IsFollower(bot) ||
                bot.GetPlayer?.HealthController?.IsAlive != true || human?.HealthController?.IsAlive != true) return;
            float now = Time.time;
            if (now < nextCheck) return;
            nextCheck = now + 0.5f;
            bool inCombat = bot.Memory?.HaveEnemy == true || SainGoalEnemyBridge.HasEnemy(bot);
            if (!inCombat)
            {
                // End abandoned combat speech without spending the peaceful greeting.
                if (started || helpCount > 0) finished = true;
                nextPhrase = 0f;
                return;
            }
            if (now < nextPhrase)
            {
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
                return;
            }
            if (SainGoalEnemyBridge.HasVisibleEnemy(bot) ||
                !AllegianceFriendlyGreeting.CanUseSocialHands(bot) ||
                !AllegianceFriendlyGreeting.HasSight(bot, human, 50f)) return;

            if (!started && bot.Side != EPlayerSide.Savage)
            {
                if (!AllegianceFriendlyGreeting.TrySpeak(bot, EPhraseTrigger.HoldFire)) return;
                started = true;
                nextPhrase = now + UnityEngine.Random.Range(1f, 2f);
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
                return;
            }
            if (!AllegianceFriendlyGreeting.TrySpeak(bot, EPhraseTrigger.NeedHelp)) return;
            started = true;
            helpCount++;
            Logger.LogInfo($"[FriendlyEncounter] candidate={bot.ProfileId} combat-help={helpCount}/2");
            if (helpCount >= 2 || UnityEngine.Random.value >= 0.5f)
            {
                finished = true;
                nextPhrase = 0f;
                return;
            }
            nextPhrase = now + UnityEngine.Random.Range(3f, 6f);
            nextCheck = System.Math.Min(nextCheck, nextPhrase);
        }
    }
}
