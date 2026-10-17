using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using EFT.HealthSystem;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        private sealed class AmmoRefillTraceState
        {
            internal float NextObservation;
            internal readonly Dictionary<string, float> NextGate = new Dictionary<string, float>();
        }

        private static readonly ConditionalWeakTable<BotOwner, AmmoRefillTraceState> AmmoRefillTraces =
            new ConditionalWeakTable<BotOwner, AmmoRefillTraceState>();

        [Conditional("DEBUG")]
        internal static void RecordAmmoRefillGate(BotOwner bot, string reason, bool passDone, float retryAt)
        {
            if (!CanRecordBot(bot)) return;
            var trace = AmmoRefillTraces.GetValue(bot, _ => new AmmoRefillTraceState());
            if (trace.NextGate.TryGetValue(reason, out float next) && Time.time < next) return;
            trace.NextGate[reason] = Time.time + 5f;
            RecordAmmoRefill(bot, "gate", reason, () => new
            {
                passDone,
                retryIn = Math.Max(0f, retryAt - Time.time),
                weaponReady = bot.WeaponManager?.IsWeaponReady,
                reloading = bot.WeaponManager?.Reload?.Reloading,
                weaponId = bot.WeaponManager?.CurrentWeapon?.Id,
                heldId = bot.GetPlayer?.HandsController?.Item?.Id,
                selectedSlot = bot.WeaponManager?.Selector?.LastEquipmentSlot.ToString()
            });
        }

        [Conditional("DEBUG")]
        internal static void RecordAmmoRefill(BotOwner bot, string stage, string reason, Func<object> details)
        {
            if (!CanRecordBot(bot)) return;
            object payload;
            try { payload = details(); }
            catch { return; } // A transient inventory view must not stop recording or affect the bot.
            try { WriteEventInternal("ammoRefill", bot, new { stage, reason, details = payload }); }
            catch (Exception ex) { StopAfterFatalRecorderFailure("Ammo refill recording failure.", ex); }
        }

        [Conditional("DEBUG")]
        private static void RecordAmmoRefillObservation(BotOwner bot, bool inCombat)
        {
            if (inCombat || !CanRecordBot(bot)) return;
            var trace = AmmoRefillTraces.GetValue(bot, _ => new AmmoRefillTraceState());
            if (Time.time < trace.NextObservation) return;
            trace.NextObservation = Time.time + 5f;
            RecordAmmoRefill(bot, "inventory", "peacefulObservation", () =>
            {
                var equipment = bot.GetPlayer?.InventoryController?.Inventory?.Equipment;
                var items = equipment?.GetAllItems().ToArray() ?? new Item[0];
                return new
                {
                    weaponReady = bot.WeaponManager?.IsWeaponReady,
                    reloading = bot.WeaponManager?.Reload?.Reloading,
                    firstAid = bot.Medecine?.FirstAid?.Using,
                    surgery = bot.Medecine?.SurgicalKit?.Using,
                    medical = BuildPeacefulMedicalObservation(bot, items),
                    brain = new { layer = bot.Brain?.BaseBrain?.CurLayerInfo?.Name(),
                        action = bot.Brain?.Agent?.LastResult().Action.ToString(), reason = bot.Brain?.Agent?.LastResult().Reason },
                    weapons = new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster }
                        .Select(slot => new { slot = slot.ToString(), weapon = equipment?.GetSlot(slot)?.ContainedItem as Weapon })
                        .Where(entry => entry.weapon != null)
                        .Select(entry => new { entry.slot, id = entry.weapon.Id, template = entry.weapon.TemplateId,
                            looted = InteractableObjects.IsLootedWeapon(bot, entry.weapon),
                            reloadMode = entry.weapon.ReloadMode.ToString() }).ToArray(),
                    magazines = items.OfType<Magazine>().Select(mag => new { id = mag.Id, template = mag.TemplateId,
                        rounds = mag.Count, capacity = mag.MaxCount, parent = mag.Parent?.Container?.ParentItem?.Id }).ToArray(),
                    looseAmmo = items.OfType<Ammo>().Where(ammo => !ammo.GetAllParentItems().Any(parent => parent is Weapon || parent is Magazine))
                        .Select(ammo => new { id = ammo.Id, template = ammo.TemplateId, rounds = ammo.StackObjectsCount,
                            parent = ammo.Parent?.Container?.ParentItem?.Id }).ToArray()
                };
            });
        }

        private static object BuildPeacefulMedicalObservation(BotOwner bot, Item[] items)
        {
            var health = bot.GetPlayer?.ActiveHealthController;
            return new
            {
                hands = bot.GetPlayer?.HandsController?.GetType().Name,
                heldId = bot.GetPlayer?.HandsController?.Item?.Id,
                stimUsing = bot.Medecine?.Stimulators?.Using,
                selectedStim = bot.Medecine?.Stimulators?._stimulator?.Id,
                firstAidPart = bot.Medecine?.FirstAid?._bodyPartToHeal?.ToString(),
                parts = health == null ? null : HealthHelper.RealBodyParts.Select(part => new
                {
                    part = part.ToString(),
                    current = health.GetBodyPartHealth(part, false).Current,
                    maximum = health.GetBodyPartHealth(part, false).Maximum
                }).ToArray(),
                poison = health?.Effects.Where(effect => effect is IIntoxication || effect is ILethalIntoxication)
                    .Select(effect => new { type = effect.GetType().Name, part = effect.BodyPart.ToString(), effect.Existing, effect.Active }).ToArray(),
                antidote = health?.Effects.OfType<IStimulator>()
                    .SelectMany(stim => stim.ActiveBuffs ?? Array.Empty<IStimulatorBuff>())
                    .Where(buff => buff?.Settings.BuffType == EStimulatorBuffType.Antidote)
                    .Select(buff => new { buff.Active, processed = (buff as ActiveHealthController.Stimulator.Buff)?.Processed,
                        buff.Settings.Delay, buff.Settings.Duration }).ToArray(),
                medicines = items.OfType<Meds>().Select(med => new { id = med.Id, template = med.TemplateId,
                    resource = med.TryGetItemComponent<MedKitComponent>(out var kit) ? (float?)kit.HpResource : null }).ToArray()
            };
        }
    }
}
