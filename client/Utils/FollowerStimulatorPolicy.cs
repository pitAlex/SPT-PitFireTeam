using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Utils
{
    // Shared item selection only; each combat brain owns treatment timing and safety.
    public sealed class FollowerStimulatorPolicy
    {
        private readonly List<Meds> stimSearchBuffer = new List<Meds>();

        public static bool NeedsBlackStomachPainRelief(BotOwner botOwner)
        {
            Player player = botOwner.GetPlayer;
            return player?.ActiveHealthController?.IsBodyPartDestroyed(EBodyPart.Stomach) == true &&
                   player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) != true;
        }
        public static bool CanUseStimulatorNow(BotOwner botOwner, out BotStimulators stims)
        {
            stims = botOwner.Medecine?.Stimulators;
            return stims != null &&
                   !stims.Using &&
                   Time.time - stims.LastEndUseTime > 3f &&
                   stims.CanUseNow() &&
                   botOwner.WeaponManager?.Reload?.Reloading != true;
        }

        public static bool ShouldUsePainStimForDestroyedPartAtHealCover(BotOwner botOwner, bool refreshSurgeryPart = true)
        {
            Player player = botOwner.GetPlayer;
            if (player == null ||
                player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) == true ||
                botOwner.Medecine?.SurgicalKit?.HaveWork != true)
            {
                return false;
            }

            EBodyPart? targetPart = botOwner.Medecine.SurgicalKit._bodyPartToHeal;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            if (refreshSurgeryPart) botOwner.Medecine.SurgicalKit.FindDamagedPart();
            targetPart = botOwner.Medecine.SurgicalKit._bodyPartToHeal;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            return HasDestroyedPainManagedPart(player);
        }

        private static bool HasDestroyedPainManagedPart(Player player)
        {
            return IsDestroyedPainManagedPart(player, EBodyPart.Stomach) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftLeg) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightLeg);
        }

        private static bool IsDestroyedPainManagedPart(Player player, EBodyPart part)
        {
            return part != EBodyPart.Head &&
                   part != EBodyPart.Chest &&
                   player.ActiveHealthController?.IsBodyPartDestroyed(part) == true;
        }

        public bool TrySelectPainStimulator(BotOwner botOwner, BotStimulators stims, bool refreshOnMiss = true)
        {
            return TrySelectStimulator(botOwner, stims, HasPainReliefEffect, refreshOnMiss);
        }

        public bool TrySelectPositiveHealthRateStimulator(BotOwner botOwner, BotStimulators stims, bool refreshOnMiss = true)
        {
            return TrySelectStimulator(botOwner, stims, HasPositiveHealthRateBuff, refreshOnMiss);
        }

        private bool TrySelectStimulator(BotOwner botOwner, BotStimulators stims, Func<EFT.InventoryLogic.Stimulator, bool> predicate, bool refreshOnMiss)
        {
            Player player = botOwner.GetPlayer;
            if (player == null || player.InventoryController == null)
            {
                return false;
            }

            EquipmentSlot[] searchSlots = stims._shallUseInSafe ? BotMedecine.secureSlots : BotMedecine.anySlots;
            stimSearchBuffer.Clear();
            player.InventoryController.GetAcceptableItemsNonAlloc<EFT.InventoryLogic.Meds>(searchSlots, stimSearchBuffer, null, null);

            for (int i = 0; i < stimSearchBuffer.Count; i++)
            {
                if (stimSearchBuffer[i] is not EFT.InventoryLogic.Stimulator stimulator)
                {
                    continue;
                }

                if (!predicate(stimulator))
                {
                    continue;
                }

                stims._stimulator = stimulator;
                stims.HaveSmt = true;
                return true;
            }

            if (refreshOnMiss) stims.Refresh();
            return false;
        }

        private static bool HasPainReliefEffect(EFT.InventoryLogic.Stimulator stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            return effects?.DamageEffects?.ContainsKey(EDamageEffectType.Pain) == true;
        }

        private static bool HasPositiveHealthRateBuff(EFT.InventoryLogic.Stimulator stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            if (effects == null)
            {
                return false;
            }

            EFT.HealthSystem.EffectsSettings.StimulatorSettings.StimulatorBuffSettings[] buffs = effects.BuffSettings;
            for (int i = 0; i < buffs.Length; i++)
            {
                if (buffs[i].BuffType == EStimulatorBuffType.HealthRate &&
                    buffs[i].Value > 0f)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
