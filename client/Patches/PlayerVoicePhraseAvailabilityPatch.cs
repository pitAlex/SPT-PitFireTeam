using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class PlayerVoicePhraseAvailability
    {
        private static readonly EPhraseTrigger[] RequiredCommandPhrases =
        [
            EPhraseTrigger.Cooperation,
            EPhraseTrigger.FollowMe
        ];

        private static readonly Dictionary<EPhraseTrigger, TagBank> PlaceholderBanks = new Dictionary<EPhraseTrigger, TagBank>();
        private static readonly HashSet<string> LoggedVoicePatches = new HashSet<string>();

        public static void EnsureCommandPhrases(EFT.BaseSpeaker speaker, EPlayerSide side, string playerVoice)
        {
            if (speaker == null || !speaker.OnDemandOnly)
            {
                return;
            }

            if (side != EPlayerSide.Bear && side != EPlayerSide.Usec)
            {
                return;
            }

            bool addedAny = false;
            List<string> added = new List<string>();

            foreach (EPhraseTrigger phrase in RequiredCommandPhrases)
            {
                if (speaker.PhrasesBanks.TryGetValue(phrase, out TagBank existingBank) && existingBank != null)
                {
                    continue;
                }

                speaker.PhrasesBanks[phrase] = GetPlaceholderBank(phrase);
                addedAny = true;
                added.Add(phrase.ToString());
            }

            if (!addedAny)
            {
                return;
            }

            string logKey = $"{playerVoice}:{side}";
            if (LoggedVoicePatches.Add(logKey))
            {
                Modules.Logger.LogInfo($"[Voice] Added silent command phrase availability for player voice '{playerVoice}' ({side}): {string.Join(", ", added)}");
            }
        }

        private static TagBank GetPlaceholderBank(EPhraseTrigger phrase)
        {
            if (PlaceholderBanks.TryGetValue(phrase, out TagBank bank) && bank != null)
            {
                return bank;
            }

            bank = ScriptableObject.CreateInstance<TagBank>();
            bank.name = $"pitFireTeam Silent {phrase}";
            bank.Trigger = phrase;
            bank.SpreadGroups = Array.Empty<SpreadGroup>();
            bank.Clips = Array.Empty<TaggedClip>();
            bank.ChainEvent = new Chain();
            bank.Importance = 0;
            bank.Blocker = 0f;
            bank.IgnoreTags = true;

            PlaceholderBanks[phrase] = bank;
            return bank;
        }
    }

    internal sealed class PlayerVoicePhraseAvailabilityInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EFT.BaseSpeaker),
                nameof(EFT.BaseSpeaker.Init),
                new[] { typeof(EPlayerSide), typeof(int), typeof(string), typeof(bool) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.BaseSpeaker __instance, EPlayerSide side, string playerVoice)
        {
            PlayerVoicePhraseAvailability.EnsureCommandPhrases(__instance, side, playerVoice);
        }
    }

    internal sealed class PlayerVoicePhraseAvailabilityReplacePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EFT.BaseSpeaker),
                nameof(EFT.BaseSpeaker.ReplaceVoice),
                new[] { typeof(EPlayerSide), typeof(string) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EFT.BaseSpeaker __instance, EPlayerSide side, string playerVoice)
        {
            PlayerVoicePhraseAvailability.EnsureCommandPhrases(__instance, side, playerVoice);
        }
    }
}
