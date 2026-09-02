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
        private const float ContactConfirmationSeconds = 1f;
        private const float ContactRetrySeconds = 0.25f;
        private const string UpdateHubSubscriberId = "FollowerContactPhraseGate";

        private sealed class ContactState
        {
            public string LastEnemyProfileId;
            public string SuppressedEnemyProfileId;
            public float SuppressUntilTime;
            public string PendingEnemyProfileId;
            public EPhraseTrigger PendingPhrase;
            public ETagStatus? PendingAdditionalMask;
            public float ConfirmAtTime;
            public float NextAttemptTime;
            public bool ManualEmissionInProgress;
        }

        private static readonly Dictionary<string, ContactState> StateByFollower = new Dictionary<string, ContactState>(StringComparer.Ordinal);
        private static bool _updateRegistered;

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

            if (string.Equals(state.PendingEnemyProfileId, safeEnemyId, StringComparison.Ordinal))
            {
                // A player Contact / Over There command supersedes an autonomous contact callout
                // that was still waiting for its one-second persistence confirmation.
                state.LastEnemyProfileId = safeEnemyId;
                ClearPending(state);
            }

            EnsureUpdateRegistered();
        }

        public static bool ShouldAllowOrSchedule(
            BotOwner owner,
            EPhraseTrigger phrase,
            ETagStatus? additionalMask = null)
        {
            if (owner == null ||
                string.IsNullOrEmpty(owner.ProfileId) ||
                !IsContactPhrase(phrase))
            {
                return false;
            }

            if (StateByFollower.TryGetValue(owner.ProfileId, out ContactState manualState) &&
                manualState.ManualEmissionInProgress)
            {
                return true;
            }

            if (owner.Memory?.HaveEnemy != true || owner.Memory.GoalEnemy == null)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            EnemyInfo goalEnemy = owner.Memory.GoalEnemy;
            string enemyId = GetEnemyId(goalEnemy);
            ContactState state = GetOrCreateState(owner.ProfileId);
            float now = UnityEngine.Time.time;

            if (IsCommandSuppressed(state, enemyId, now))
            {
                // Player-directed Contact / Over There already told the follower where to fight. Mark
                // that enemy as handled so they do not acknowledge the command with a delayed contact callout.
                state.LastEnemyProfileId = enemyId;
                ClearPending(state);
                return false;
            }

            if (string.Equals(state.LastEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(state.PendingEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                // Preserve the first request and its original confirmation time. Visibility/sense
                // flicker must not restart the one-second proof window every time EFT asks again.
                if (phrase == EPhraseTrigger.OnFirstContact)
                {
                    state.PendingPhrase = phrase;
                    state.PendingAdditionalMask = additionalMask;
                }

                return false;
            }

            state.PendingEnemyProfileId = enemyId;
            state.PendingPhrase = phrase;
            state.PendingAdditionalMask = additionalMask;
            state.ConfirmAtTime = now + ContactConfirmationSeconds;
            state.NextAttemptTime = state.ConfirmAtTime;
            EnsureUpdateRegistered();

            // Delay the original request. UpdatePendingContact emits it manually only if the same
            // living GoalEnemy survives the anti-flicker confirmation window.
            return false;
        }

        private static void UpdatePendingContact(BotOwner owner)
        {
            if (owner == null ||
                string.IsNullOrEmpty(owner.ProfileId) ||
                !StateByFollower.TryGetValue(owner.ProfileId, out ContactState state))
            {
                return;
            }

            if (owner.Memory?.HaveEnemy != true || owner.Memory.GoalEnemy == null)
            {
                StateByFollower.Remove(owner.ProfileId);
                return;
            }

            if (string.IsNullOrEmpty(state.PendingEnemyProfileId))
            {
                return;
            }

            EnemyInfo goalEnemy = owner.Memory.GoalEnemy;
            string enemyId = GetEnemyId(goalEnemy);
            if (!string.Equals(state.PendingEnemyProfileId, enemyId, StringComparison.Ordinal) ||
                goalEnemy.Person?.HealthController?.IsAlive == false)
            {
                ClearPending(state);
                return;
            }

            float now = UnityEngine.Time.time;
            if (now < state.ConfirmAtTime || now < state.NextAttemptTime)
            {
                return;
            }

            if (IsCommandSuppressed(state, enemyId, now) ||
                string.Equals(state.LastEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                state.LastEnemyProfileId = enemyId;
                ClearPending(state);
                return;
            }

            BotTalk botTalk = owner.BotTalk;
            if (botTalk == null ||
                botTalk.IsSilenced ||
                FollowerForcedPhraseGate.ShouldBlock(owner, state.PendingPhrase))
            {
                state.NextAttemptTime = now + ContactRetrySeconds;
                return;
            }

            BotGroupTalk groupTalk = owner.BotsGroup?.GroupTalk;
            if (groupTalk != null && !groupTalk.CanSay(owner, state.PendingPhrase))
            {
                state.NextAttemptTime = now + ContactRetrySeconds;
                return;
            }

            EPhraseTrigger phrase = state.PendingPhrase;
            ETagStatus? additionalMask = state.PendingAdditionalMask;
            state.NextAttemptTime = now + ContactRetrySeconds;
            state.ManualEmissionInProgress = true;
            try
            {
                // The contact already survived the persistence proof, so bypass the normal speech
                // cooldown while preserving EFT's squad-level phrase reservation.
                botTalk.DropNextSayPeriod();
                groupTalk?.PhraseSad(owner, phrase);
                botTalk.Say(phrase, true, additionalMask);
                state.LastEnemyProfileId = enemyId;
                ClearPending(state);
            }
            finally
            {
                state.ManualEmissionInProgress = false;
            }
        }

        private static ContactState GetOrCreateState(string ownerProfileId)
        {
            if (!StateByFollower.TryGetValue(ownerProfileId, out ContactState state))
            {
                state = new ContactState();
                StateByFollower[ownerProfileId] = state;
            }

            return state;
        }

        private static string GetEnemyId(EnemyInfo goalEnemy)
        {
            return string.IsNullOrEmpty(goalEnemy?.ProfileId) ? "<unknown>" : goalEnemy.ProfileId;
        }

        private static bool IsCommandSuppressed(ContactState state, string enemyId, float now)
        {
            return state != null &&
                   now <= state.SuppressUntilTime &&
                   string.Equals(state.SuppressedEnemyProfileId, enemyId, StringComparison.Ordinal);
        }

        private static void ClearPending(ContactState state)
        {
            if (state == null)
            {
                return;
            }

            state.PendingEnemyProfileId = null;
            state.PendingPhrase = EPhraseTrigger.None;
            state.PendingAdditionalMask = null;
            state.ConfirmAtTime = 0f;
            state.NextAttemptTime = 0f;
        }

        private static void EnsureUpdateRegistered()
        {
            if (_updateRegistered)
            {
                return;
            }

            BotOwnerUpdateHub.RegisterFollower(UpdateHubSubscriberId, UpdatePendingContact);
            _updateRegistered = true;
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
            if (owner == null || !BossPlayers.IsFollower(owner) || !MutedFollowerTriggers.Contains(phrase))
            {
                return false;
            }

            // Vanilla requests Clear for every bot when memory enters peace. Keep that path muted,
            // but permit the one follower selected by the squad post-combat linger coordinator.
            return !FollowerPostCombatClearPhraseGate.IsAllowed(owner, phrase);
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
                if (!FollowerContactPhraseGate.ShouldAllowOrSchedule(__instance._owner, type, additionaMask))
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

            BotOwner owner = __instance.AIData?.BotOwner;
            if (FollowerPostCombatClearPhraseGate.IsAllowed(owner, phrase))
            {
                FollowerPostCombatClearPhraseGate.Consume(owner, phrase);
                return true;
            }

            return !FollowerTalkFrequencyGate.ShouldBlockCombatTalk(owner, phrase);
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
                if (!FollowerContactPhraseGate.ShouldAllowOrSchedule(__instance._owner, type, additionalMask))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
