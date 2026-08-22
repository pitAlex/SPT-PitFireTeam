using pitTeam.Modules;
using pitTeam.Components;
using HarmonyLib;
using EFT;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class FollowerForcedPhraseGate
    {
        private sealed class ForcedPhraseState
        {
            public EPhraseTrigger Phrase;
            public float UntilTime;
        }

        private static readonly Dictionary<string, ForcedPhraseState> StateByFollower = new Dictionary<string, ForcedPhraseState>(StringComparer.Ordinal);

        public static void Arm(BotOwner owner, EPhraseTrigger phrase, float durationSeconds)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            float safeDuration = Math.Max(0.1f, durationSeconds);
            StateByFollower[owner.ProfileId] = new ForcedPhraseState
            {
                Phrase = phrase,
                UntilTime = UnityEngine.Time.time + safeDuration
            };
        }

        public static void Clear(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            StateByFollower.Remove(owner.ProfileId);
        }

        public static bool ShouldBlock(BotOwner owner, EPhraseTrigger phrase)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!StateByFollower.TryGetValue(owner.ProfileId, out ForcedPhraseState state))
            {
                return false;
            }

            if (UnityEngine.Time.time > state.UntilTime)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            if (phrase == state.Phrase)
            {
                return false;
            }

            return true;
        }

        public static bool TryGetArmedPhrase(BotOwner owner, out EPhraseTrigger phrase)
        {
            phrase = EPhraseTrigger.None;

            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!StateByFollower.TryGetValue(owner.ProfileId, out ForcedPhraseState state))
            {
                return false;
            }

            if (UnityEngine.Time.time > state.UntilTime)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            phrase = state.Phrase;
            return true;
        }
    }

    public static class FollowerContactPhraseGate
    {
        private const float StableSeenWindowSeconds = 1.25f;

        private sealed class ContactState
        {
            public string LastEnemyProfileId;
            public string SuppressedEnemyProfileId;
            public float SuppressUntilTime;
        }

        private static readonly Dictionary<string, ContactState> StateByFollower = new Dictionary<string, ContactState>(StringComparer.Ordinal);

        public static bool IsContactPhrase(EPhraseTrigger phrase)
        {
            return phrase == EPhraseTrigger.OnFirstContact || phrase == EPhraseTrigger.OnRepeatedContact;
        }

        public static void SuppressCommandedContact(BotOwner owner, string enemyProfileId, float durationSeconds)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            string safeEnemyId = string.IsNullOrEmpty(enemyProfileId) ? "<unknown>" : enemyProfileId;
            if (!StateByFollower.TryGetValue(owner.ProfileId, out ContactState state))
            {
                state = new ContactState();
                StateByFollower[owner.ProfileId] = state;
            }

            state.SuppressedEnemyProfileId = safeEnemyId;
            state.SuppressUntilTime = UnityEngine.Time.time + Math.Max(0.1f, durationSeconds);
        }

        public static bool ShouldAllow(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (owner.Memory?.HaveEnemy != true || owner.Memory.GoalEnemy == null)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            EnemyInfo goalEnemy = owner.Memory.GoalEnemy;
            bool stableContact =
                goalEnemy.IsVisible ||
                (goalEnemy.PersonalLastSeenTime > 0f &&
                 UnityEngine.Time.time - goalEnemy.PersonalLastSeenTime <= StableSeenWindowSeconds);
            if (!stableContact)
            {
                return false;
            }

            string enemyId = goalEnemy.ProfileId;
            if (string.IsNullOrEmpty(enemyId))
            {
                enemyId = "<unknown>";
            }

            if (StateByFollower.TryGetValue(owner.ProfileId, out ContactState suppressedState) &&
                UnityEngine.Time.time <= suppressedState.SuppressUntilTime &&
                string.Equals(suppressedState.SuppressedEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                // Player-directed Contact / Over There already told the follower where to fight. Mark
                // that enemy as handled so they do not acknowledge the command with a delayed contact callout.
                suppressedState.LastEnemyProfileId = enemyId;
                return false;
            }

            if (!StateByFollower.TryGetValue(owner.ProfileId, out ContactState state))
            {
                StateByFollower[owner.ProfileId] = new ContactState { LastEnemyProfileId = enemyId };
                return true;
            }

            if (string.Equals(state.LastEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                return false;
            }

            state.LastEnemyProfileId = enemyId;
            return true;
        }
    }

    internal static class FollowerMutedCombatPhraseGate
    {
        private static readonly HashSet<EPhraseTrigger> MutedFollowerTriggers = new HashSet<EPhraseTrigger>
        {
            EPhraseTrigger.Clear,
            EPhraseTrigger.LostVisual,
            EPhraseTrigger.OnLostVisual
        };

        public static bool ShouldBlock(BotOwner owner, EPhraseTrigger phrase)
        {
            return owner != null && BossPlayers.IsFollower(owner) && MutedFollowerTriggers.Contains(phrase);
        }
    }

    internal static class FollowerReloadPhraseRemap
    {
        public static EPhraseTrigger Remap(BotOwner owner, EPhraseTrigger phrase)
        {
            if (phrase != EPhraseTrigger.NeedAmmo ||
                owner == null ||
                !BossPlayers.IsFollower(owner) ||
                owner.WeaponManager?.Reload?.Reloading != true)
            {
                return phrase;
            }

            // Vanilla BotReload announces NeedAmmo after it has already found ammunition and
            // entered the reload transaction. Preserve genuine NeedAmmo calls outside reload,
            // but use the semantically correct reload cue for followers here.
            return EPhraseTrigger.OnWeaponReload;
        }
    }

    // patch for preventing bots from talking if silenced command is active
    internal class BotTalkTrySayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type[] parameterTypes = new Type[] { typeof(EPhraseTrigger), typeof(ETagStatus?), typeof(bool) };
            return AccessTools.Method(typeof(BotTalk), "TrySay", parameterTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotTalk __instance, ref EPhraseTrigger type, ETagStatus? additionaMask, bool withGroupDelay)
        {
            type = FollowerReloadPhraseRemap.Remap(__instance._owner, type);
            if (FollowerForcedPhraseGate.ShouldBlock(__instance._owner, type))
            {
                return false;
            }

            if (FollowerMutedCombatPhraseGate.ShouldBlock(__instance._owner, type))
            {
                return false;
            }

            if (__instance.IsSilenced) return false;

            if (FollowerContactPhraseGate.IsContactPhrase(type) && BossPlayers.IsFollower(__instance._owner))
            {
                if (!FollowerContactPhraseGate.ShouldAllow(__instance._owner))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Gate actual EFT speech output after BotTalk has started its normal cooldown. Gating the raw
    // TrySay request lets rapidly repeated AI requests reroll a low percentage until one succeeds,
    // which makes values such as 10% sound far more frequent than configured.
    internal class PlayerSayFollowerTalkPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type[] parameterTypes = new Type[]
            {
                typeof(EPhraseTrigger),
                typeof(bool),
                typeof(float),
                typeof(ETagStatus),
                typeof(int),
                typeof(bool)
            };
            return AccessTools.Method(typeof(Player), nameof(Player.Say), parameterTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player __instance, EPhraseTrigger phrase)
        {
            if (__instance == null || !__instance.IsAI)
            {
                return true;
            }

            return !FollowerTalkFrequencyGate.ShouldBlockCombatTalk(__instance.AIData?.BotOwner, phrase);
        }
    }

    // patch for preventing bots from talking if silenced command is active
    internal class BotTalkSayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotTalk), "Say");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotTalk __instance, ref EPhraseTrigger type, bool sayImmediately = false, ETagStatus? additionalMask = null)
        {
            type = FollowerReloadPhraseRemap.Remap(__instance._owner, type);
            if (FollowerForcedPhraseGate.ShouldBlock(__instance._owner, type))
            {
                return false;
            }

            if (FollowerMutedCombatPhraseGate.ShouldBlock(__instance._owner, type))
            {
                return false;
            }

            if (__instance.IsSilenced) return false;

            if (FollowerContactPhraseGate.IsContactPhrase(type) && BossPlayers.IsFollower(__instance._owner))
            {
                if (!FollowerContactPhraseGate.ShouldAllow(__instance._owner))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
