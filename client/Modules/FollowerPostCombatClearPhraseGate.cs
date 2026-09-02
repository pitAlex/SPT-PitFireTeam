using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Narrow permit for the one follower selected to announce a completed squad combat episode.
    /// Normal EFT/SAIN Clear requests remain muted; the permit survives the BotTalk request/query
    /// path and is consumed only when Player.Say reaches actual output.
    /// </summary>
    public static class FollowerPostCombatClearPhraseGate
    {
        private static readonly Dictionary<string, float> PermitUntilByFollower =
            new Dictionary<string, float>(StringComparer.Ordinal);

        public static void Arm(BotOwner? owner, float durationSeconds)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            PermitUntilByFollower[owner.ProfileId] = Time.time + Math.Max(0.1f, durationSeconds);
        }

        public static bool IsAllowed(BotOwner? owner, EPhraseTrigger phrase)
        {
            if (phrase != EPhraseTrigger.Clear || owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!PermitUntilByFollower.TryGetValue(owner.ProfileId, out float permitUntil))
            {
                return false;
            }

            if (Time.time <= permitUntil)
            {
                return true;
            }

            PermitUntilByFollower.Remove(owner.ProfileId);
            return false;
        }

        public static void Consume(BotOwner? owner, EPhraseTrigger phrase)
        {
            if (phrase == EPhraseTrigger.Clear && owner != null && !string.IsNullOrEmpty(owner.ProfileId))
            {
                PermitUntilByFollower.Remove(owner.ProfileId);
            }
        }
    }
}
