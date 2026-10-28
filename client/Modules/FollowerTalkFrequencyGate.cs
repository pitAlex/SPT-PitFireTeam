using EFT;
using pitTeam.Components;
using pitTeam.Patches;
using System;

namespace pitTeam.Modules
{
    public static class FollowerTalkFrequencyGate
    {
        private static readonly Random Random = new Random();

        public static bool ShouldBlockCombatTalk(BotOwner owner, EPhraseTrigger phrase)
        {
            if (owner == null || !BossPlayers.IsFollower(owner))
            {
                return false;
            }

            // Allied bosses keep their native combat chatter without the follower percentage roll.
            if (owner.Profile?.Info?.Settings != null &&
                Utils.Props.BossFollowersType.Contains(owner.Profile.Info.Settings.Role))
            {
                return false;
            }

            // First-contact callouts are tactical information, not ambient combat chatter. They are
            // still deduplicated by FollowerContactPhraseGate and explicitly suppressed for player
            // Contact / Over There commands, but the trash-talk percentage must not mute them.
            if (FollowerContactPhraseGate.IsContactPhrase(phrase))
            {
                return false;
            }

            if (!IsCombatTalkPhrase(owner, phrase))
            {
                return false;
            }

            return ShouldBlockForConfiguredFrequency();
        }

        private static bool IsCombatTalkPhrase(BotOwner owner, EPhraseTrigger phrase)
        {
            return phrase == EPhraseTrigger.OnFight
                || (owner.Memory?.HaveEnemy == true && (phrase == EPhraseTrigger.MumblePhrase || phrase == EPhraseTrigger.OnMutter));
        }

        private static bool ShouldBlockForConfiguredFrequency()
        {
            int freq = pitFireTeam.botTalk.Value;
            if (freq <= 0)
            {
                return true;
            }

            if (freq >= 100)
            {
                return false;
            }

            lock (Random)
            {
                return Random.Next(1, 101) > freq;
            }
        }
    }
}
