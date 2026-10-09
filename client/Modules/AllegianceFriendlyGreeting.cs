using EFT;
using pitTeam.Patches;
using UnityEngine;

namespace pitTeam.Modules
{
    // One instance per raid selection, retained across activation and lost sight.
    internal sealed class AllegianceFriendlyGreeting
    {
        private int spokenCount;
        private float nextCheck;
        private float nextPhrase = -1f;
        private bool secondRolled;
        private bool secondSkipped;

        internal void Update(BotOwner bot, Player human)
        {
            if (spokenCount >= 2 || secondSkipped || !GameplayModeRuntime.IsAllegiance || bot == null ||
                bot.IsDead || bot.BotState != EBotState.Active || BossPlayers.IsFollower(bot) ||
                bot.GetPlayer?.HealthController?.IsAlive != true || human?.HealthController?.IsAlive != true)
                return;

            float now = Time.time;
            if (now < nextCheck) return;
            nextCheck = now + 0.5f;

            var sight = bot.LookSensor;
            float distanceSquared = (bot.Position - human.Position).sqrMagnitude;
            if (sight == null || distanceSquared > 50f * 50f || sight.VisibleDist <= 0f ||
                distanceSquared > sight.VisibleDist * sight.VisibleDist ||
                !sight.IsPointInVisibleSector(human.Position) || !sight.CheckLookSimple(bot.GetPlayer, human))
                return;

            if (nextPhrase < 0f)
            {
                // Sample range at first sight: 0.2s up close, rising to 2s at 50m.
                nextPhrase = now + 0.2f + 1.8f * (float)System.Math.Sqrt(distanceSquared) / 50f;
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
                return;
            }
            if (now < nextPhrase)
            {
                // Do not round a short reaction or its deadline up to the sight-check interval.
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
                return;
            }
            var speaker = bot.GetPlayer.Speaker;
            if (speaker == null || speaker.Speaking || speaker.Busy ||
                bot.BotTalk == null || FollowerForcedPhraseGate.TryGetArmedPhrase(bot, out _)) return;

            if (spokenCount == 1 && !secondRolled)
            {
                // Roll once at the second line's eligible contact, using current range.
                // Near players need fewer repeat calls; a skipped call stays spent for the raid.
                float chance = (float)System.Math.Sqrt(distanceSquared) / 50f;
                float roll = UnityEngine.Random.value;
                secondRolled = true;
                secondSkipped = chance < 1f && roll >= chance;
                Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} second-hold-fire chance={chance:F2} roll={roll:F3} skipped={secondSkipped}");
                if (secondSkipped) return;
            }

            // The existing pre-recruitment speech scope bypasses only this exact
            // phrase in SAIN. Ordinary bot speech and shared presets remain owned by SAIN.
            FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, EPhraseTrigger.HoldFire, 0.9f);
            bot.BotTalk.SetSilence(-1f);
            bot.BotTalk.DropNextSayPeriod();
            bot.BotTalk.Say(EPhraseTrigger.HoldFire, true);
            if (!speaker.Speaking)
            {
                FollowerForcedPhraseGate.Clear(bot);
                return;
            }

            spokenCount++;
            nextPhrase = now + UnityEngine.Random.Range(1f, 3f);
            nextCheck = System.Math.Min(nextCheck, nextPhrase);
            Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} hold-fire={spokenCount}/2");
        }
    }
}
