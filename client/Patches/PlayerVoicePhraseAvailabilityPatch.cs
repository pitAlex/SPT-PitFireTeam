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
        private static readonly Dictionary<TagBank, TagBank> ContactAudioFallbackBanks = new Dictionary<TagBank, TagBank>();
        private static readonly HashSet<string> LoggedVoicePatches = new HashSet<string>();
        private static readonly HashSet<string> LoggedContactAudioFallbacks = new HashSet<string>();

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

        public static bool TryGetContactAudioFallback(EFT.BaseSpeaker speaker, out TagBank fallbackBank)
        {
            fallbackBank = null;

            if (speaker == null ||
                !ReferenceEquals(GamePlayerOwner.MyPlayer?.Speaker, speaker) ||
                HasPlayableBank(speaker, EPhraseTrigger.OnRepeatedContact) ||
                !speaker.PhrasesBanks.TryGetValue(EPhraseTrigger.OnFirstContact, out TagBank firstContactBank) ||
                firstContactBank?.Clips == null ||
                firstContactBank.Clips.Length == 0)
            {
                return false;
            }

            if (!ContactAudioFallbackBanks.TryGetValue(firstContactBank, out fallbackBank) || fallbackBank == null)
            {
                fallbackBank = ScriptableObject.CreateInstance<TagBank>();
                fallbackBank.name = $"pitFireTeam Contact Audio Fallback ({speaker.PlayerVoice})";
                fallbackBank.Trigger = EPhraseTrigger.OnRepeatedContact;
                fallbackBank.SpreadGroups = firstContactBank.SpreadGroups;
                fallbackBank.Clips = firstContactBank.Clips;
                fallbackBank.ChainEvent = new Chain();
                fallbackBank.Importance = firstContactBank.Importance;
                fallbackBank.Blocker = 0f;
                fallbackBank.IgnoreTags = firstContactBank.IgnoreTags;
                ContactAudioFallbackBanks[firstContactBank] = fallbackBank;
            }

            if (LoggedContactAudioFallbacks.Add(speaker.PlayerVoice ?? "<unknown>"))
            {
                Modules.Logger.LogInfo(
                    $"[Voice] Player voice '{speaker.PlayerVoice}' lacks {EPhraseTrigger.OnRepeatedContact}; " +
                    $"using {EPhraseTrigger.OnFirstContact} for Contact audio only.");
            }

            return true;
        }

        private static bool HasPlayableBank(EFT.BaseSpeaker speaker, EPhraseTrigger phrase)
        {
            return speaker.PhrasesBanks.TryGetValue(phrase, out TagBank bank) &&
                   bank?.Clips != null &&
                   bank.Clips.Length > 0;
        }
    }

    internal sealed class PlayerContactAudioFallbackPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EFT.BaseSpeaker),
                nameof(EFT.BaseSpeaker.Play),
                new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(bool), typeof(int?) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(
            EFT.BaseSpeaker __instance,
            EPhraseTrigger trigger,
            ETagStatus tags,
            int? importance,
            ref TagBank __result)
        {
            if (trigger != EPhraseTrigger.OnRepeatedContact)
            {
                return true;
            }

            try
            {
                if (!PlayerVoicePhraseAvailability.TryGetContactAudioFallback(__instance, out TagBank fallbackBank))
                {
                    return true;
                }

                __instance.PlayExternal(fallbackBank, EPhraseTrigger.OnRepeatedContact, tags, importance);
                __result = fallbackBank;
                return false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Voice] Contact audio fallback failed; using normal playback. {ex}");
                return true;
            }
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
