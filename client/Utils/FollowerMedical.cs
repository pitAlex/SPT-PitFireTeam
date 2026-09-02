using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class FollowerMedical
    {
        private const float MedicalProgressSampleInterval = 0.25f;
        private const float FirstAidStallTimeout = 15f;
        private const float FirstAidHardTimeout = 45f;
        private const float StimulatorStallTimeout = 5f;
        private const float StimulatorHardTimeout = 15f;
        private const float SurgeryStallTimeout = 40f;
        private const float SurgeryHardTimeout = 90f;
        private const float HandsAfterMedicalStallTimeout = 5f;
        private const float HandsAfterMedicalHardTimeout = 20f;
        private const float RecentMedicalWindow = 8f;
        private const float FirstAidMinVisibleNormalizedHealth = 0.06f;
        private const float EmergencySurgeryHealthPenalty = 0.5f;
        private const float FirstAidTopOffMinMissingHealth = 0.5f;
        private const float PostCombatFullHealRestoreDelay = 12f;

        private static readonly EBodyPart[] SurgeryRecoveryParts =
        {
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg,
            EBodyPart.Stomach
        };

        private sealed class MedicalHandsWatchState
        {
            public string? Reason;
            public float StartedAt;
            public float LastProgressAt;
            public float StallTimeout;
            public float HardTimeout;
            public float NextProgressSampleAt;
            public float RecentMedicalUntil;
            public MedicalProgressSnapshot Progress;
        }

        private readonly struct MedicalProgressSnapshot
        {
            public MedicalProgressSnapshot(
                Meds? activeMeds,
                bool firstAidPending,
                bool surgeryPending,
                bool handsInteractionActive,
                float currentHealth,
                float maximumHealth)
            {
                ActiveMeds = activeMeds;
                FirstAidPending = firstAidPending;
                SurgeryPending = surgeryPending;
                HandsInteractionActive = handsInteractionActive;
                CurrentHealth = currentHealth;
                MaximumHealth = maximumHealth;
            }

            public Meds? ActiveMeds { get; }
            public bool FirstAidPending { get; }
            public bool SurgeryPending { get; }
            public bool HandsInteractionActive { get; }
            public float CurrentHealth { get; }
            public float MaximumHealth { get; }
        }

        private sealed class PostCombatFullHealState
        {
            public float StartedAt;
            public float HealStartedAt;
            public float LastWorkSeenAt;
        }

        private static readonly Dictionary<string, MedicalHandsWatchState> HandsWatchStates = new Dictionary<string, MedicalHandsWatchState>();
        private static readonly Dictionary<string, PostCombatFullHealState> PostCombatFullHealStates = new Dictionary<string, PostCombatFullHealState>();

        public static void ForceHeal(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelAllHealing(bot, recoverDestroyedSurgeryParts: true);

            Player player = bot.GetPlayer;
            RestoreAllBodyPartsToMaximum(player);
            RemoveBleedingEffects(player);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RestoreAllBodyPartsToMaximum(bot.AIData.Player);
                RemoveBleedingEffects(bot.AIData.Player);
            }

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: true);
            TryRecoverStuckMedicalHands(bot, "forceHeal");
            TryReturnToMainWeapon(bot);
        }

        public static void CancelAllHealing(BotOwner bot, bool recoverDestroyedSurgeryParts)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelActiveMedical(bot);

            if (recoverDestroyedSurgeryParts)
            {
                NormalizeSurgeryRecoveredPartsForFirstAid(bot.GetPlayer);
                if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }
            }

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: recoverDestroyedSurgeryParts);
            TryReturnToMainWeapon(bot);
        }

        public static void CompleteHealing(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            NormalizeSurgeryRecoveredPartsForFirstAid(bot.GetPlayer);
            if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
            {
                NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
            }

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: true);

            if (!IsUsingMedical(bot) &&
                bot.WeaponManager?.Selector != null &&
                bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                TryReturnToMainWeapon(bot);
            }
        }

        public static void AbortHealing(BotOwner bot, bool recoverDestroyedSurgeryParts)
        {
            CancelAllHealing(bot, recoverDestroyedSurgeryParts);
        }

        public static void BeginPostCombatFullHeal(BotOwner bot)
        {
            string key = GetBotKey(bot);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!PostCombatFullHealStates.TryGetValue(key, out PostCombatFullHealState state))
            {
                state = new PostCombatFullHealState();
                PostCombatFullHealStates[key] = state;
            }

            state.StartedAt = Time.time;
            state.HealStartedAt = 0f;
            state.LastWorkSeenAt = Time.time;
        }

        public static void MarkPostCombatFullHealActionStarted(BotOwner bot)
        {
            string key = GetBotKey(bot);
            if (string.IsNullOrEmpty(key) ||
                !PostCombatFullHealStates.TryGetValue(key, out PostCombatFullHealState state))
            {
                return;
            }

            if (state.HealStartedAt <= 0f)
            {
                state.HealStartedAt = Time.time;
            }

            state.LastWorkSeenAt = Time.time;
        }

        public static bool IsPostCombatFullHealActive(BotOwner bot)
        {
            string key = GetBotKey(bot);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return PostCombatFullHealStates.ContainsKey(key);
        }

        public static void CompletePostCombatFullHeal(BotOwner bot)
        {
            string key = GetBotKey(bot);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            PostCombatFullHealStates.Remove(key);
        }

        public static bool IsPostCombatFullHealRestoreWindowElapsed(BotOwner bot)
        {
            string key = GetBotKey(bot);
            if (string.IsNullOrEmpty(key) ||
                !PostCombatFullHealStates.TryGetValue(key, out PostCombatFullHealState state) ||
                state.HealStartedAt <= 0f)
            {
                return false;
            }

            return Time.time - state.HealStartedAt >= PostCombatFullHealRestoreDelay;
        }

        public static bool ShouldKeepPostCombatFullHeal(
            BotOwner bot,
            bool hasKnownWork = false,
            bool scanForWork = true)
        {
            try
            {
                string key = GetBotKey(bot);
                if (string.IsNullOrEmpty(key) ||
                    !PostCombatFullHealStates.TryGetValue(key, out PostCombatFullHealState state))
                {
                    return false;
                }

                if (hasKnownWork || (scanForWork && HasPostCombatFullHealWork(bot)))
                {
                    state.LastWorkSeenAt = Time.time;
                    return true;
                }

                if (state.HealStartedAt > 0f)
                {
                    return true;
                }

                PostCombatFullHealStates.Remove(key);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCompletePostCombatFullHealRestore(BotOwner bot)
        {
            try
            {
                string key = GetBotKey(bot);
                if (string.IsNullOrEmpty(key) ||
                    !PostCombatFullHealStates.TryGetValue(key, out PostCombatFullHealState state))
                {
                    return false;
                }

                if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
                {
                    PostCombatFullHealStates.Remove(key);
                    return false;
                }

                if (state.HealStartedAt <= 0f ||
                    Time.time - state.HealStartedAt < PostCombatFullHealRestoreDelay)
                {
                    return false;
                }

                if (IsUsingMedical(bot))
                {
                    return false;
                }

                if (bot.WeaponManager != null && !bot.WeaponManager.IsWeaponReady)
                {
                    TryReturnToMainWeapon(bot);
                    return false;
                }

                if (!IsMainWeaponSelected(bot))
                {
                    TryReturnToMainWeapon(bot);
                    return false;
                }

                ForceHeal(bot);
                PostCombatFullHealStates.Remove(key);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasPostCombatFullHealWork(BotOwner bot)
        {
            Player player = bot?.GetPlayer;
            BotFirstAid firstAid = bot?.Medecine?.FirstAid;
            if (player?.HealthController == null ||
                player.ActiveHealthController == null ||
                firstAid == null ||
                bot.HealthController?.IsAlive != true ||
                bot.Settings?.FileSettings?.Mind?.CAN_USE_MEDS != true)
            {
                return false;
            }

            if (firstAid.Using ||
                bot.Medecine.SurgicalKit?.Using == true ||
                firstAid.Have2Do ||
                bot.Medecine.SurgicalKit?.HaveWork == true)
            {
                return true;
            }

            if (TryGetActiveBleeding(player, out EDamageEffectType bleedingType))
            {
                return CanTreatDamageEffect(firstAid.CurUsingMeds, bleedingType) ||
                       FindBestBleedTreatment(firstAid, bleedingType) != null;
            }

            return HasRecoverableFirstAidDamage(bot);
        }

        public static void UpdateMedicalHandsWatchdog(BotOwner bot)
        {
            if (bot?.ProfileId == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            MedicalHandsWatchState state = GetWatchState(bot.ProfileId);
            string? reason = GetMedicalBusyReason(
                bot,
                state,
                out float stallTimeout,
                out float hardTimeout);
            if (reason == null)
            {
                if (!IsHandsInteractionActive(bot.GetPlayer))
                {
                    ResetWatchState(state);
                }
                return;
            }

            if (!string.Equals(state.Reason, reason, StringComparison.Ordinal))
            {
                state.Reason = reason;
                state.StartedAt = Time.time;
                state.LastProgressAt = Time.time;
                state.StallTimeout = stallTimeout;
                state.HardTimeout = hardTimeout;
                state.NextProgressSampleAt = Time.time + MedicalProgressSampleInterval;
                state.Progress = CaptureMedicalProgress(bot, reason);
                return;
            }

            if (state.StartedAt <= 0f)
            {
                state.StartedAt = Time.time;
                state.LastProgressAt = Time.time;
            }

            if (Time.time >= state.NextProgressSampleAt)
            {
                MedicalProgressSnapshot progress = CaptureMedicalProgress(bot, reason);
                if (HasMedicalProgressed(state.Progress, progress))
                {
                    state.LastProgressAt = Time.time;
                }

                state.Progress = progress;
                state.NextProgressSampleAt = Time.time + MedicalProgressSampleInterval;
            }

            bool progressStalled = Time.time - state.LastProgressAt > state.StallTimeout;
            bool hardTimeoutReached = Time.time - state.StartedAt > state.HardTimeout;
            if (!progressStalled && !hardTimeoutReached)
            {
                return;
            }

            TryRecoverStuckMedicalHands(
                bot,
                hardTimeoutReached ? $"{reason}.hardTimeout" : $"{reason}.stalled");
            state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
            ResetWatchState(state);
        }

        private static void RestoreAllBodyPartsToMaximum(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    player.ActiveHealthController.FullRestoreBodyPart(part);
                }

                ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
                float missingHealth = health.Maximum - health.Current;
                if (missingHealth.Positive())
                {
                    player.ActiveHealthController.ChangeHealth(part, missingHealth, EFT.HealthSystem.DamageHelper.MedKitUse);
                }
            }
        }

        private static void RemoveBleedingEffects(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            List<EFT.HealthSystem.IHealthEffect> bleedingEffects = new List<EFT.HealthSystem.IHealthEffect>();
            foreach (EFT.HealthSystem.IHealthEffect effect in player.ActiveHealthController.GetAllEffects(EBodyPart.Common))
            {
                if (effect is EFT.HealthSystem.IBleeding &&
                    effect.State != EEffectState.None &&
                    effect.State != EEffectState.Removed)
                {
                    bleedingEffects.Add(effect);
                }
            }

            for (int i = 0; i < bleedingEffects.Count; i++)
            {
                ForceRemoveEffect(bleedingEffects[i]);
            }
        }

        private static void ForceRemoveEffect(EFT.HealthSystem.IHealthEffect effect)
        {
            if (effect == null)
            {
                return;
            }

            try
            {
                MethodInfo? forceRemove = effect.GetType().GetMethod(
                    "ForceRemove",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                forceRemove?.Invoke(effect, null);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Medical] Failed to remove follower bleeding effect {effect.GetType().Name}.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void NormalizeSurgeryRecoveredPartsForFirstAid(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            for (int i = 0; i < SurgeryRecoveryParts.Length; i++)
            {
                EBodyPart part = SurgeryRecoveryParts[i];
                ClampImpossibleBodyPartHealth(player, part);

                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    player.ActiveHealthController.RestoreBodyPart(part, EmergencySurgeryHealthPenalty);
                    ClampImpossibleBodyPartHealth(player, part);
                }

                ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
                if (health.Maximum <= 0f || health.Current <= 0f)
                {
                    continue;
                }

                float minHealth = GetFirstAidVisibleHealth(player, part);
                if (health.Current < minHealth)
                {
                    player.ActiveHealthController.ChangeHealth(part, minHealth - health.Current, EFT.HealthSystem.DamageHelper.MedKitUse);
                }
            }
        }

        private static float GetFirstAidVisibleHealth(Player player, EBodyPart part)
        {
            ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
            return Mathf.Max(1f, health.Maximum * FirstAidMinVisibleNormalizedHealth);
        }

        private static void ClampImpossibleBodyPartHealth(Player player, EBodyPart part)
        {
            if (player?.ActiveHealthController?.BodyState == null ||
                player.Profile?.Health?.BodyParts == null ||
                !player.Profile.Health.BodyParts.TryGetValue(part, out Profile.HealthInfo.BodyPartInfo profilePart) ||
                profilePart?.Health == null ||
                profilePart.Health.Maximum <= 0f ||
                !player.ActiveHealthController.BodyState.TryGetValue(part, out EFT.HealthSystem.BaseHealthController<ActiveHealthController.Effect>.BodyPartState state) ||
                state?.Health == null)
            {
                return;
            }

            float profileMaximum = profilePart.Health.Maximum;
            if (state.Health.Maximum <= profileMaximum * 1.1f)
            {
                return;
            }

            float oldMaximum = state.Health.Maximum;
            float current = Mathf.Min(state.Health.Current, profileMaximum);
            float minimum = Mathf.Min(state.Health.Minimum, profileMaximum);
            state.Health = new HealthValue(current, profileMaximum, minimum);
            Modules.Logger.LogInfo($"[Medical] Clamped impossible follower {part} health max from {oldMaximum:0.##} to {profileMaximum:0.##}: {player.Profile?.Nickname ?? player.name}");
        }

        private static void RefreshBotMovementAfterHealing(BotOwner bot, bool ignoreBrokenLegPenalty)
        {
            if (bot?.GetPlayer == null)
            {
                return;
            }

            Player player = bot.GetPlayer;
            RefreshMovementHealthPenalty(player, ignoreBrokenLegPenalty);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RefreshMovementHealthPenalty(bot.AIData.Player, ignoreBrokenLegPenalty);
            }

            if (bot.Mover != null)
            {
                bot.Mover.Pause = false;
                bot.Mover._sprintStopEnd = 0f;
                bot.Mover.SetTargetMoveSpeed(1f);
            }

            if (!HasDamagedLeg(player, ignoreBrokenLegPenalty))
            {
                player.EnableSprint(true);
                if (bot.AIData?.Player != null && bot.AIData.Player != player)
                {
                    bot.AIData.Player.EnableSprint(true);
                }
            }
        }

        private static void RefreshMovementHealthPenalty(Player player, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null || player.MovementContext == null)
            {
                return;
            }

            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.UsingMeds, false);
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.HealingLegs, false);

            bool rightLegDamaged = IsMovementDamagedLeg(player, EBodyPart.RightLeg, ignoreBrokenLegPenalty);
            bool leftLegDamaged = IsMovementDamagedLeg(player, EBodyPart.LeftLeg, ignoreBrokenLegPenalty);

            player.UpdateSpeedLimitByHealth();
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.RightLegDamaged, rightLegDamaged);
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.LeftLegDamaged, leftLegDamaged);

            if (!rightLegDamaged && !leftLegDamaged)
            {
                player.RemoveStateSpeedLimit(Player.ESpeedLimit.HealthCondition);
                player.EnableSprint(true);
            }
        }

        private static bool HasDamagedLeg(Player player, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null)
            {
                return false;
            }

            return IsMovementDamagedLeg(player, EBodyPart.RightLeg, ignoreBrokenLegPenalty) ||
                   IsMovementDamagedLeg(player, EBodyPart.LeftLeg, ignoreBrokenLegPenalty);
        }

        private static bool IsMovementDamagedLeg(Player player, EBodyPart part, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null)
            {
                return false;
            }

            if (player.HealthController.IsBodyPartDestroyed(part))
            {
                return true;
            }

            return !ignoreBrokenLegPenalty && player.HealthController.IsBodyPartBroken(part);
        }

        private static string? GetMedicalBusyReason(
            BotOwner bot,
            MedicalHandsWatchState state,
            out float stallTimeout,
            out float hardTimeout)
        {
            stallTimeout = 0f;
            hardTimeout = 0f;

            if (bot.Medecine?.Stimulators?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                stallTimeout = StimulatorStallTimeout;
                hardTimeout = StimulatorHardTimeout;
                return "stimulator";
            }

            if (bot.Medecine?.FirstAid?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                stallTimeout = FirstAidStallTimeout;
                hardTimeout = FirstAidHardTimeout;
                return "firstAid";
            }

            if (bot.Medecine?.SurgicalKit?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                stallTimeout = SurgeryStallTimeout;
                hardTimeout = SurgeryHardTimeout;
                return "surgery";
            }

            BotLogicDecision currentDecision = bot.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.dogFight;
            bool isHealDecision = currentDecision == BotLogicDecision.heal ||
                                  currentDecision == BotLogicDecision.healStimulators;
            if (isHealDecision)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
            }

            if ((isHealDecision || Time.time < state.RecentMedicalUntil) &&
                IsHandsInteractionActive(bot.GetPlayer))
            {
                stallTimeout = HandsAfterMedicalStallTimeout;
                hardTimeout = HandsAfterMedicalHardTimeout;
                return "medicalHands";
            }

            return null;
        }

        private static MedicalProgressSnapshot CaptureMedicalProgress(BotOwner bot, string reason)
        {
            Meds? activeMeds = reason switch
            {
                "firstAid" => bot.Medecine?.FirstAid?.CurUsingMeds,
                "surgery" => bot.Medecine?.SurgicalKit?.CurUsingMeds,
                _ => null,
            };

            float currentHealth = 0f;
            float maximumHealth = 0f;
            try
            {
                ActiveHealthController? healthController = bot.GetPlayer?.ActiveHealthController;
                if (healthController != null)
                {
                    foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
                    {
                        ValueStruct health = healthController.GetBodyPartHealth(part, false);
                        currentHealth += health.Current;
                        maximumHealth += health.Maximum;
                    }
                }
            }
            catch
            {
                // Health is supporting evidence only. The remaining controller/hands signals still
                // distinguish an advancing medical sequence from one frozen in a single state.
            }

            return new MedicalProgressSnapshot(
                activeMeds,
                bot.Medecine?.FirstAid?.Have2Do == true,
                bot.Medecine?.SurgicalKit?.HaveWork == true,
                IsHandsInteractionActive(bot.GetPlayer),
                currentHealth,
                maximumHealth);
        }

        private static bool HasMedicalProgressed(
            MedicalProgressSnapshot previous,
            MedicalProgressSnapshot current)
        {
            return !ReferenceEquals(previous.ActiveMeds, current.ActiveMeds) ||
                   previous.FirstAidPending != current.FirstAidPending ||
                   previous.SurgeryPending != current.SurgeryPending ||
                   previous.HandsInteractionActive != current.HandsInteractionActive ||
                   Mathf.Abs(previous.CurrentHealth - current.CurrentHealth) > 0.01f ||
                   Mathf.Abs(previous.MaximumHealth - current.MaximumHealth) > 0.01f;
        }

        private static bool IsHandsInteractionActive(Player player)
        {
            try
            {
                return player?.HandsController != null &&
                       (player.HandsController.IsInInteraction() ||
                        player.HandsController.IsInInteractionStrictCheck());
            }
            catch
            {
                return false;
            }
        }

        public static void CancelActiveMedical(BotOwner bot)
        {
            if (bot?.Medecine == null)
            {
                return;
            }

            if (bot.Medecine.FirstAid?.Using == true)
            {
                bot.Medecine.FirstAid.CancelCurrent();
            }

            if (bot.Medecine.SurgicalKit?.Using == true)
            {
                bot.Medecine.SurgicalKit.CancelCurrent();
            }

            if (bot.Medecine.Stimulators?.Using == true)
            {
                bot.Medecine.Stimulators.CancelCurrent();
            }
        }

        public static bool IsUsingMedical(BotOwner bot)
        {
            return bot?.Medecine != null &&
                   (bot.Medecine.FirstAid?.Using == true ||
                    bot.Medecine.SurgicalKit?.Using == true ||
                    bot.Medecine.Stimulators?.Using == true);
        }

        public static bool HasRecoverableFirstAidDamage(BotOwner bot)
        {
            try
            {
                return TryFindFirstAidTopOffTarget(bot, out _, out _);
            }
            catch
            {
                return false;
            }
        }

        public static bool CanStartFirstAidTopOff(BotOwner bot)
        {
            try
            {
                return CanAttemptFirstAidTopOff(bot) &&
                       TryFindFirstAidTopOffTarget(bot, out _, out _);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryStartFirstAidTopOff(BotOwner bot)
        {
            try
            {
                if (!CanAttemptFirstAidTopOff(bot))
                {
                    return false;
                }

                if (!TryFindFirstAidTopOffTarget(bot, out EBodyPart bodyPart, out EFT.InventoryLogic.Meds med))
                {
                    return false;
                }

                BotFirstAid firstAid = bot.Medecine.FirstAid;
                firstAid.CurUsingMeds = med;
                firstAid._bodyPartToHeal = bodyPart;
                firstAid._isBleedingLight = false;
                firstAid._isBleedingHeavy = false;
                firstAid.Damaged = true;
                firstAid.TryApplyToCurrentPart();
                return firstAid.Using;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanAttemptFirstAidTopOff(BotOwner bot)
        {
            return bot?.Medecine?.FirstAid != null &&
                   !bot.Medecine.Using &&
                   !bot.Medecine.FirstAid.Using &&
                   bot.Medecine.SurgicalKit?.HaveWork != true &&
                   bot.Medecine.SurgicalKit?.Using != true &&
                   bot.WeaponManager?.Grenades?.ThrowindNow != true &&
                   bot.WeaponManager?.Reload?.Reloading != true &&
                   bot.Medecine.FirstAid.CanUseByTime();
        }

        private static bool TryFindFirstAidTopOffTarget(BotOwner bot, out EBodyPart bodyPart, out EFT.InventoryLogic.Meds med)
        {
            bodyPart = default;
            med = null;

            Player player = bot?.GetPlayer;
            BotFirstAid firstAid = bot?.Medecine?.FirstAid;
            if (player?.HealthController == null ||
                player.ActiveHealthController == null ||
                firstAid == null ||
                bot.HealthController?.IsAlive != true ||
                bot.Settings?.FileSettings?.Mind?.CAN_USE_MEDS != true ||
                !ShouldAllowManualFirstAidTopOff(bot) ||
                bot.Memory?.HaveEnemy == true ||
                HasVisibleKnownEnemy(bot) ||
                firstAid.Using ||
                bot.Medecine.SurgicalKit?.HaveWork == true ||
                bot.Medecine.SurgicalKit?.Using == true ||
                TryGetActiveBleeding(player, out _))
            {
                return false;
            }

            return TryFindFirstAidTopOffTargetCore(player, firstAid, out bodyPart, out med);
        }

        private static bool ShouldAllowManualFirstAidTopOff(BotOwner bot)
        {
            return IsPostCombatFullHealActive(bot) &&
                   !IsPostCombatFullHealRestoreWindowElapsed(bot);
        }

        private static bool TryFindFirstAidTopOffTargetCore(
            Player player,
            BotFirstAid firstAid,
            out EBodyPart bodyPart,
            out EFT.InventoryLogic.Meds med)
        {
            bodyPart = default;
            med = null;

            if (player?.InventoryController == null ||
                player.HealthController == null ||
                player.ActiveHealthController == null ||
                firstAid == null)
            {
                return false;
            }

            EquipmentSlot[] searchSlots = firstAid._shallUseInSafe ? BotMedecine.secureSlots : BotMedecine.anySlots;
            List<EFT.InventoryLogic.Meds> meds = new List<EFT.InventoryLogic.Meds>();
            player.InventoryController.GetAcceptableItemsNonAlloc<EFT.InventoryLogic.Meds>(searchSlots, meds, null, null);
            if (meds.Count == 0)
            {
                return false;
            }

            float bestNormalized = float.MaxValue;
            float bestMissing = 0f;
            float bestMedScore = float.MaxValue;
            foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    continue;
                }

                ValueStruct health = player.HealthController.GetBodyPartHealth(part, false);
                float missing = health.Maximum - health.Current;
                if (health.Maximum <= 0f || missing <= FirstAidTopOffMinMissingHealth)
                {
                    continue;
                }

                if (!TrySelectTopOffMed(player, meds, part, missing, out EFT.InventoryLogic.Meds candidateMed, out float medScore))
                {
                    continue;
                }

                float normalized = health.Current / health.Maximum;
                bool betterPart = normalized < bestNormalized - 0.001f ||
                                  (Mathf.Abs(normalized - bestNormalized) <= 0.001f && missing > bestMissing + 0.1f);
                bool samePartBetterMed = Mathf.Abs(normalized - bestNormalized) <= 0.001f &&
                                         Mathf.Abs(missing - bestMissing) <= 0.1f &&
                                         medScore < bestMedScore;

                if (!betterPart && !samePartBetterMed)
                {
                    continue;
                }

                bodyPart = part;
                med = candidateMed;
                bestNormalized = normalized;
                bestMissing = missing;
                bestMedScore = medScore;
            }

            return med != null;
        }

        private static bool IsMainWeaponSelected(BotOwner bot)
        {
            BotWeaponSelector selector = bot?.WeaponManager?.Selector;
            if (selector == null)
            {
                return true;
            }

            Weapon? firstPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            if (firstPrimary == null)
            {
                return true;
            }

            return selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon;
        }

        private static bool TrySelectTopOffMed(
            Player player,
            List<EFT.InventoryLogic.Meds> meds,
            EBodyPart bodyPart,
            float missingHealth,
            out EFT.InventoryLogic.Meds selected,
            out float selectedScore)
        {
            selected = null;
            selectedScore = float.MaxValue;

            for (int i = 0; i < meds.Count; i++)
            {
                EFT.InventoryLogic.Meds med = meds[i];
                if (med == null ||
                    !med.TryGetItemComponent<MedKitComponent>(out MedKitComponent medKit) ||
                    medKit.HpResource <= 0f ||
                    player.HealthController.CanApplyItem(med, bodyPart) != true)
                {
                    continue;
                }

                float score = medKit.HpResource >= missingHealth
                    ? medKit.HpResource - missingHealth
                    : 10000f - medKit.HpResource;
                if (score >= selectedScore)
                {
                    continue;
                }

                selected = med;
                selectedScore = score;
            }

            return selected != null;
        }

        private static bool HasVisibleKnownEnemy(BotOwner bot)
        {
            try
            {
                var infos = bot?.EnemiesController?.EnemyInfos;
                if (infos == null || infos.Count == 0)
                {
                    return false;
                }

                foreach (var kv in infos)
                {
                    EnemyInfo info = kv.Value;
                    if (info?.IsVisible == true &&
                        info.Person?.HealthController?.IsAlive == true)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryRecoverStuckMedicalHands(BotOwner bot, string reason)
        {
            Player player = bot?.GetPlayer;
            if (player == null && bot?.ProfileId != null)
            {
                player = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(bot.ProfileId);
            }

            if (player?.InventoryController == null)
            {
                return false;
            }

            try
            {
                CancelActiveMedical(bot);
                NormalizeSurgeryRecoveredPartsForFirstAid(player);
                if (bot?.AIData?.Player != null && bot.AIData.Player != player)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }

                try
                {
                    player.FastForwardCurrentOperations();
                }
                catch
                {
                    // Hands may already be mid-transition; continue with inventory-event cleanup.
                }

                EFT.InventoryLogic.ItemEventArgs[] activeEvents = player.InventoryController.ActiveEvents.ToArray();
                for (int i = 0; i < activeEvents.Length; i++)
                {
                    player.InventoryController.RemoveActiveEvent(activeEvents[i]);
                }

                player.ProcessStatus = Player.EProcessStatus.None;
                player.SetInventoryOpened(false);
                player.TrySetLastEquippedWeapon(true, null);
                TryReturnToMainWeapon(bot);

                Modules.Logger.LogInfo($"[Medical] Recovered stuck follower hands after {reason}: {bot.Profile?.Nickname ?? bot.name}");
                return true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Medical] Failed to recover stuck follower hands after {reason}: {bot?.Profile?.Nickname ?? bot?.name ?? "<null>"}");
                Modules.Logger.LogError(ex);
                return false;
            }
        }

        private static void TryReturnToMainWeapon(BotOwner bot)
        {
            if (bot?.WeaponManager?.Selector == null)
            {
                return;
            }

            bot.WeaponManager.Selector.TakePrevWeapon();

            if (bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                bot.WeaponManager.Selector.TryChangeToMain();
            }
        }

        public static void RefreshMedicalWork(BotOwner bot)
        {
            try
            {
                if (bot?.Medecine == null || bot.GetPlayer == null)
                {
                    return;
                }

                NormalizeSurgeryRecoveredPartsForFirstAid(bot.GetPlayer);
                if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }

                bot.Medecine.RefreshCurMeds();
                SelectUsableBleedFirstAid(bot);
                bot.Medecine.GetDamaged();
                bot.Medecine.SurgicalKit.FindDamagedPart();
                bot.Medecine.FirstAid.CheckParts();
            }
            catch
            {
                // Medical state can be mid-transition while a vanilla heal node is being cancelled.
                // Stale flags will refresh on the next tick.
            }
        }

        private static void SelectUsableBleedFirstAid(BotOwner bot)
        {
            BotFirstAid firstAid = bot?.Medecine?.FirstAid;
            Player player = bot?.GetPlayer;
            if (firstAid == null ||
                firstAid.Using ||
                player?.HealthController == null ||
                !TryGetActiveBleeding(player, out EDamageEffectType bleedingType))
            {
                return;
            }

            EFT.InventoryLogic.Meds current = firstAid.CurUsingMeds;
            if (CanTreatDamageEffect(current, bleedingType))
            {
                return;
            }

            EFT.InventoryLogic.Meds best = FindBestBleedTreatment(firstAid, bleedingType);
            if (best != null)
            {
                firstAid.CurUsingMeds = best;
                Modules.Logger.LogInfo(
                    $"[Medical] Selected usable {bleedingType} med for {bot.Profile?.Nickname ?? bot.name}: {best.TemplateId}");
                return;
            }

            if (ClaimsDamageEffect(current, bleedingType))
            {
                firstAid.CurUsingMeds = null;
                Modules.Logger.LogInfo(
                    $"[Medical] Cleared unusable {bleedingType} med for {bot.Profile?.Nickname ?? bot.name}: {current.TemplateId}");
            }
        }

        private static bool TryGetActiveBleeding(Player player, out EDamageEffectType bleedingType)
        {
            bleedingType = default;

            if (player?.HealthController == null)
            {
                return false;
            }

            if (player.HealthController.FindExistingEffect<EFT.HealthSystem.IHeavyBleeding>(EBodyPart.Common) != null)
            {
                bleedingType = EDamageEffectType.HeavyBleeding;
                return true;
            }

            if (player.HealthController.FindExistingEffect<EFT.HealthSystem.ILightBleeding>(EBodyPart.Common) != null)
            {
                bleedingType = EDamageEffectType.LightBleeding;
                return true;
            }

            return false;
        }

        public static bool HasActiveBleeding(Player player)
        {
            return TryGetActiveBleeding(player, out _);
        }

        private static EFT.InventoryLogic.Meds FindBestBleedTreatment(BotFirstAid firstAid, EDamageEffectType bleedingType)
        {
            EFT.InventoryLogic.Meds best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < firstAid._medsList.Count; i++)
            {
                EFT.InventoryLogic.Meds med = firstAid._medsList[i];
                if (!CanTreatDamageEffect(med, bleedingType, out int cost, out MedKitComponent medKit))
                {
                    continue;
                }

                float score = GetBleedTreatmentScore(medKit, cost);
                if (score < bestScore)
                {
                    best = med;
                    bestScore = score;
                }
            }

            return best;
        }

        private static float GetBleedTreatmentScore(MedKitComponent medKit, int cost)
        {
            if (medKit == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, medKit.HpResource - cost) + (medKit.MaxHpResource * 0.01f);
        }

        private static bool CanTreatDamageEffect(EFT.InventoryLogic.Meds med, EDamageEffectType bleedingType)
        {
            return CanTreatDamageEffect(med, bleedingType, out _, out _);
        }

        private static bool CanTreatDamageEffect(
            EFT.InventoryLogic.Meds med,
            EDamageEffectType bleedingType,
            out int cost,
            out MedKitComponent medKit)
        {
            cost = 0;
            medKit = null;

            if (!TryGetDamageEffectCost(med, bleedingType, out cost))
            {
                return false;
            }

            return !med.TryGetItemComponent<MedKitComponent>(out medKit) ||
                   medKit.HpResource + Mathf.Epsilon >= cost;
        }

        private static bool ClaimsDamageEffect(EFT.InventoryLogic.Meds med, EDamageEffectType bleedingType)
        {
            return TryGetDamageEffectCost(med, bleedingType, out _);
        }

        private static bool TryGetDamageEffectCost(EFT.InventoryLogic.Meds med, EDamageEffectType bleedingType, out int cost)
        {
            cost = 0;
            if (med == null ||
                !med.TryGetItemComponent<HealthEffectsComponent>(out HealthEffectsComponent healthEffects) ||
                healthEffects?.DamageEffects == null ||
                !healthEffects.DamageEffects.TryGetValue(bleedingType, out JsonType.DamageEffectSpecification effect))
            {
                return false;
            }

            cost = effect?.Cost ?? 0;
            return true;
        }

        private static MedicalHandsWatchState GetWatchState(string profileId)
        {
            if (!HandsWatchStates.TryGetValue(profileId, out MedicalHandsWatchState state))
            {
                state = new MedicalHandsWatchState();
                HandsWatchStates[profileId] = state;
            }

            return state;
        }

        private static string GetBotKey(BotOwner bot)
        {
            return bot?.ProfileId ?? bot?.Profile?.Id ?? string.Empty;
        }

        private static void ResetWatchState(MedicalHandsWatchState state)
        {
            state.Reason = null;
            state.StartedAt = 0f;
            state.LastProgressAt = 0f;
            state.StallTimeout = 0f;
            state.HardTimeout = 0f;
            state.NextProgressSampleAt = 0f;
            state.Progress = default;
        }
    }
}
