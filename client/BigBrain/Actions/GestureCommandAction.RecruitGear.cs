using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool recruitGearInProgress, recruitBodyKitAttempted;
        private int recruitGearGeneration;

        private bool TryStartRecruitLooseEquipment(Item item)
        {
            bool firearm = item is Weapon && item.GetItemComponent<KnifeComponent>() == null;
            // Loose supplies use the same explicit reevaluation as Give Items. A previously
            // carried secondary can become usable without a separate inventory scanner.
            if (followerData?.IsSquadMate != false || (!firearm && !(item is Magazine) && !(item is Ammo))) return false;
            if (TeammateGearSwap.Current != null) return true;
            var target = activeLootItem;
            var request = ActiveLootRequest;
            int generation = ++recruitGearGeneration;
            recruitGearInProgress = true;
            bool started = TeammateGearSwap.TryCommandLoot(BotOwner, item, new[] { item }, false,
                () => generation == recruitGearGeneration && ReferenceEquals(ActiveLootRequest, request) &&
                    target != null && target.gameObject != null &&
                    Vector3.Distance(BotOwner.Position, target.transform.position) <= 2f,
                committed =>
                {
                    if (generation != recruitGearGeneration) return;
                    recruitGearInProgress = false;
                    if (!ReferenceEquals(ActiveLootRequest, request))
                    {
                        activeLootItem = null;
                        lootPickupReadyAt = 0f;
                        return;
                    }
                    if (committed && IsLootNowInBotInventory(BotOwner.GetPlayer, item))
                        FinishLootPickupSuccess(BotOwner.GetPlayer, item, "TakeLoot:done");
                    else
                    {
                        BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                        ClearTakeLootState("TakeLoot:recruitUpgradeRejected");
                    }
                });
            if (!started)
            {
                recruitGearInProgress = false;
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                ClearTakeLootState("TakeLoot:recruitUpgradeUnavailable");
            }
            return true;
        }

        private bool TryStartRecruitBodyKit(InventoryEquipment source, InventoryEquipment kit)
        {
            var mode = ActiveLootRequest?.Mode ?? FollowerLootMode.Normal;
            if (followerData?.IsSquadMate != false || recruitBodyKitAttempted ||
                ActiveLootRequest?.GearExchangeCommitted == true ||
                (mode != FollowerLootMode.LootAndGetGear && mode != FollowerLootMode.LootAndGetWeapon)) return false;
            if (TeammateGearSwap.Current != null) return true;
            recruitBodyKitAttempted = true;
            var roots = new List<Item>();
            if (mode == FollowerLootMode.LootAndGetGear)
            {
                roots.AddRange(GiveGearPlanner.EquipmentSlots.Select(s => source.GetSlot(s)?.ContainedItem).Where(i => i != null));
                if (source.GetSlot(EquipmentSlot.Pockets)?.ContainedItem is SearchableItem pockets)
                    roots.AddRange(pockets.Grids.SelectMany(g => g.Items));
            }
            else
            {
                var primary = source.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
                var secondary = source.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
                var gun = primary ?? secondary;
                var pistol = kit.GetSlot(EquipmentSlot.Holster)?.ContainedItem == null
                    ? source.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon : null;
                if (ActiveLootRequest?.SelectiveWeapons == true)
                {
                    gun = new[] { primary, secondary }.FirstOrDefault(w => w?.Id == ActiveLootRequest.SelectedLongGunId);
                    if (pistol?.Id != ActiveLootRequest.SelectedPistolId) pistol = null;
                }
                foreach (var weapon in new[] { gun, pistol }.Where(w => w != null))
                {
                    roots.Add(weapon);
                    roots.AddRange(GetBodyOperationalMagazineCandidates(source, weapon).Select(c => c.Item));
                    roots.AddRange(GetBodyWeaponLooseAmmoCandidates(source, weapon).Select(c => c.Item));
                }
            }
            // Commanded recovery keeps Restricted's protected-root exclusion. A manual
            // Give Gear loan is a separate player action, not permission to strip a corpse.
            var offering = roots.Where(i => i != null && !InteractableObjects.IsProtectedFollowerEquipment(i)).Distinct().ToArray();
            if (offering.Length == 0) { ActiveLootRequest?.CompletePriority(); return false; }
            // Parent packages own descendants. Do not also move an inserted magazine or
            // a supply that is already carried inside another selected root.
            var nested = new HashSet<string>(offering.SelectMany(root => TeammateGearSwap.Tree(root).Skip(1)).Select(i => (string)i.Id));
            offering = offering.Where(i => !nested.Contains(i.Id)).ToArray();
            foreach (var item in offering.SelectMany(TeammateGearSwap.Tree)) bodyLootAttemptedItemIds.Add(item.Id);
            var corpse = activeBodyLootCorpse;
            var request = ActiveLootRequest;
            string primaryBefore = kit.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem?.Id;
            int generation = ++recruitGearGeneration;
            recruitGearInProgress = true;
            bool started = TeammateGearSwap.TryCommandLoot(BotOwner, source, offering, true,
                () => generation == recruitGearGeneration && ReferenceEquals(ActiveLootRequest, request) &&
                    corpse != null && corpse.gameObject != null &&
                    Vector3.Distance(BotOwner.Position, corpse.transform.position) <= 2f,
                committed =>
                {
                    if (generation != recruitGearGeneration) return;
                    recruitGearInProgress = false;
                    if (!ReferenceEquals(ActiveLootRequest, request))
                    {
                        // The protected commit may finish after Attention/replacement. Reset
                        // only this action's old search cache, never the replacement request
                        // or its reservation in InteractableObjects.
                        activeBodyLootCorpse = null;
                        recruitBodyKitAttempted = bodyLootSearchStarted = bodyLootSuccessSpoken = false;
                        bodyLootReadyAt = bodyLootNextMoveAt = 0f;
                        bodyLootMovesSucceeded = bodyLootReportedMovesSucceeded = 0;
                        bodyLootAttemptedItemIds.Clear();
                        return;
                    }
                    if (!committed)
                    {
                        BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                        ClearBodyLootState("TakeBodyGear:recruitUpgradeRejected");
                        return;
                    }
                    ActiveLootRequest?.CompleteGearExchange();
                    bodyLootMovesSucceeded++;
                    bodyLootReportedMovesSucceeded++;
                    if (!bodyLootSuccessSpoken)
                    {
                        bool newPrimary = kit.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem?.Id != primaryBefore;
                        BotOwner.BotTalk.TrySay(newPrimary ? EPhraseTrigger.LootWeapon : EPhraseTrigger.LootGeneric, false);
                        bodyLootSuccessSpoken = true;
                    }
                    bodyLootNextMoveAt = Time.time + LootPickupSuccessLeadSeconds;
                });
            if (!started)
            {
                recruitGearInProgress = false;
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                ClearBodyLootState("TakeBodyGear:recruitUpgradeUnavailable");
            }
            return true;
        }

        private void ResetRecruitGearCommand()
        {
            recruitGearGeneration++;
            recruitGearInProgress = false;
            recruitBodyKitAttempted = false;
        }
    }
}
