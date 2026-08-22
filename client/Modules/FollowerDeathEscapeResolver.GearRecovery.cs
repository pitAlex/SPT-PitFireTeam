using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class FollowerDeathEscapeResolver
    {
        private const float FallbackEscapeCarrierWeightKg = 48f;

        private static void ApplyDeathGearRecoveryToEscapedEquipment(
            List<FollowerDeathEscapeOutcomeEntry> entries,
            Dictionary<string, BotOwner> entryBotsByAid,
            List<BotOwner> escapedBots,
            List<RecoverableGearCandidate> deathGearSnapshot,
            List<RecoverableGearCandidate> trackedLootSnapshot)
        {
            if (entries == null || entries.Count == 0 || escapedBots == null || escapedBots.Count == 0)
            {
                return;
            }

            try
            {
                bool recoverTeammateGear = pitFireTeam.IsFollowerLoadoutLootableMode();
                Dictionary<string, InventoryEquipment> escapedEquipment = new Dictionary<string, InventoryEquipment>(StringComparer.Ordinal);
                Dictionary<string, RecoveryCarrierState> carrierStates = new Dictionary<string, RecoveryCarrierState>(StringComparer.Ordinal);
                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (!entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        continue;
                    }

                    InventoryEquipment equipmentClone = CloneFollowerEquipment(bot);
                    if (equipmentClone != null)
                    {
                        // Player-given/tracked loot is intentionally dropped from the carrier
                        // before calculating space and weight. Survivors first try to carry
                        // player/fallen gear, then pick this tracked loot back up only if room
                        // remains after higher-priority recovery.
                        StripTrackedLootFromCarrierEquipment(equipmentClone, bot);
                        escapedEquipment[entry.Aid] = equipmentClone;
                        carrierStates[entry.Aid] = new RecoveryCarrierState(
                            equipmentClone,
                            GetCarrierWalkDrainWeightLimitKg(bot));
                    }
                }

                HashSet<string> escapedOwnerIds = new HashSet<string>(
                    entries
                        .Where(entry => entry.Escaped)
                        .SelectMany(entry => new[] { entry.Aid, entry.ProfileId })
                        .Where(id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.Ordinal);

                int recovered = 0;
                List<Item> gearToReturn = new List<Item>();
                HashSet<string> recoveredGearIds = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> coveredByRecoveredTreeIds = new HashSet<string>(StringComparer.Ordinal);
                RecoveryAttemptStats stats = new RecoveryAttemptStats();

                // Player gear is recoverable in every mode. Fallen teammate gear is recoverable
                // only in Immersive/Realistic. In both cases, escaped teammates only provide the
                // carry-space simulation; anything they successfully carry is returned by mail.
                List<RecoverableGearCandidate> recoverableCandidates = deathGearSnapshot
                    .Where(candidate => candidate.IsPlayer ||
                                        (recoverTeammateGear && !escapedOwnerIds.Contains(candidate.OwnerId)))
                    .ToList();

                Logger.LogInfo(
                    $"[DeathEscape] Death gear recovery start. teammateGearRecovery={recoverTeammateGear} " +
                    $"escapedCarriers={escapedBots.Count} equipmentSnapshots={escapedEquipment.Count} " +
                    $"snapshotItems={deathGearSnapshot.Count} candidateItems={recoverableCandidates.Count} " +
                    $"trackedLootItems={trackedLootSnapshot?.Count ?? 0} " +
                    $"pickupRadius={DeathGearRecoveryDistance:0}m weightLimit=walk-drain-threshold");

                // Backpack shells are packed before priority items so they can add container
                // space for the rest of the recovery simulation. This includes the player's
                // backpack; its contents are separate low-priority candidates, not auto-returned.
                foreach (RecoverableGearCandidate candidate in recoverableCandidates
                             .Where(candidate => candidate.UseAsRecoveryCapacity)
                             .OrderBy(candidate => candidate.OwnerPriority)
                             .ThenBy(candidate => candidate.Sequence))
                {
                    if (TryRecoverDeathGearCandidate(candidate, escapedBots, carrierStates, stats))
                    {
                        recovered++;
                        gearToReturn.Add(CloneRecoveredItemForMail(candidate));
                        recoveredGearIds.Add(candidate.Item.Id);
                    }
                }

                foreach (RecoverableGearCandidate candidate in recoverableCandidates
                             .Where(candidate => !candidate.UseAsRecoveryCapacity)
                             .OrderBy(candidate => candidate.OwnerPriority)
                             .ThenBy(candidate => candidate.ItemPriority)
                             .ThenBy(candidate => candidate.Sequence))
                {
                    if (coveredByRecoveredTreeIds.Contains(candidate.Item.Id))
                    {
                        Logger.LogInfo(
                            $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                            "item is already covered by a recovered container tree.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(candidate.CoveredByItemId) &&
                        recoveredGearIds.Contains(candidate.CoveredByItemId))
                    {
                        Logger.LogInfo(
                            $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                            "parent carrier item was already recovered with its contents.");
                        continue;
                    }

                    if (TryRecoverDeathGearCandidate(candidate, escapedBots, carrierStates, stats))
                    {
                        RemoveAlreadyMailedCoveredItems(candidate.Item, gearToReturn, recoveredGearIds);
                        TrackRecoveredContainerTree(candidate.Item, candidate.Slot, coveredByRecoveredTreeIds);
                        recovered++;
                        gearToReturn.Add(CloneRecoveredItemForMail(candidate));
                        recoveredGearIds.Add(candidate.Item.Id);
                    }
                }

                // Tracked follower loot has the lowest priority in player-death escape. It was
                // removed from carrier snapshots above, so it cannot block player gear. Anything
                // that still fits after death gear is recovered gets mailed back; the rest is lost.
                foreach (RecoverableGearCandidate candidate in trackedLootSnapshot ?? new List<RecoverableGearCandidate>())
                {
                    if (TryRecoverDeathGearCandidate(candidate, escapedBots, carrierStates, stats))
                    {
                        recovered++;
                        gearToReturn.Add(CloneRecoveredItemForMail(candidate));
                        recoveredGearIds.Add(candidate.Item.Id);
                    }
                }

                if (recoveredGearIds.Count > 0)
                {
                    // Recovered death gear is mailed to the player. Strip it back out of the
                    // simulated carrier equipment before that snapshot is sent to the server, so
                    // escaped teammate profiles keep only their own surviving loadout state.
                    RemoveRecoveredDeathGearFromEscapedEquipment(escapedEquipment.Values, recoveredGearIds);
                }

                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (recoverTeammateGear && escapedEquipment.TryGetValue(entry.Aid, out InventoryEquipment equipment))
                    {
                        entry.EquipmentItems = SerializeEquipment(equipment);
                    }
                    else if (entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        entry.EquipmentItems = SerializeFollowerEquipment(bot);
                    }
                }

                if (recovered > 0)
                {
                    Logger.LogInfo($"[DeathEscape] Recovered {recovered} nearby death-gear item(s) in carry simulation.");
                }
                else if (recoverableCandidates.Count > 0)
                {
                    Logger.LogInfo(
                        $"[DeathEscape] No death gear was recovered. noNearby={stats.NoNearbyCarrier} " +
                        $"missingEquipmentSnapshot={stats.NoEquipmentSnapshot} overweight={stats.OverWeight} " +
                        $"backpackLimit={stats.BackpackLimit} noSpace={stats.NoSpace}");
                }

                if (gearToReturn.Count > 0)
                {
                    Logger.LogInfo($"[DeathEscape] Sending {gearToReturn.Count} recovered death-gear item(s) through return mail.");
                    InteractableObjects.SendDeathEscapeRecoveredGear(gearToReturn);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to apply nearby death gear recovery.");
                Logger.LogError(ex);

                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        entry.EquipmentItems = SerializeFollowerEquipment(bot);
                    }
                }
            }
        }

        private static bool TryRecoverDeathGearCandidate(
            RecoverableGearCandidate candidate,
            List<BotOwner> escapedBots,
            Dictionary<string, RecoveryCarrierState> carrierStates,
            RecoveryAttemptStats stats)
        {
            List<BotOwner> nearbyCarriers = escapedBots
                .Where(bot => bot != null && IsNear(bot.Position, candidate.Position, DeathGearRecoveryDistance))
                .OrderBy(bot => Vector3.Distance(bot.Position, candidate.Position))
                .ToList();

            if (nearbyCarriers.Count == 0)
            {
                stats.NoNearbyCarrier++;
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    $"no escaped teammate within {DeathGearRecoveryDistance:0}m.");
                return false;
            }

            bool hadEquipmentSnapshot = false;
            bool attemptedPlacement = false;
            foreach (BotOwner escapedBot in nearbyCarriers)
            {
                string aid = escapedBot.Profile?.AccountId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(aid) || !carrierStates.TryGetValue(aid, out RecoveryCarrierState state))
                {
                    stats.NoEquipmentSnapshot++;
                    continue;
                }

                hadEquipmentSnapshot = true;
                float itemWeight = GetRecoveryCandidateWeight(candidate);
                if (!state.CanCarryWeight(itemWeight))
                {
                    stats.OverWeight++;
                    Logger.LogInfo(
                        $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}' for " +
                        $"'{escapedBot.Profile?.Nickname ?? "Squadmate"}': weight {state.CurrentWeightKg:0.0}+{itemWeight:0.0}kg exceeds {state.MaxWeightKg:0.0}kg.");
                    continue;
                }

                if (!state.CanCarryBackpack(candidate))
                {
                    stats.BackpackLimit++;
                    Logger.LogInfo(
                        $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}' for " +
                        $"'{escapedBot.Profile?.Nickname ?? "Squadmate"}': backpack carry limit reached.");
                    continue;
                }

                attemptedPlacement = true;
                // Candidate.Item is already a player-death snapshot. Clone again so a failed
                // placement attempt cannot mutate the stored snapshot before another survivor tries.
                Item clone = candidate.Item.CloneItemWithSameId();
                if (!TryPlaceRecoveredGear(state, candidate, clone))
                {
                    continue;
                }

                state.RecordRecovered(candidate, itemWeight);
                Logger.LogInfo(
                    $"[DeathEscape] Recovered death gear '{candidate.Item.ShortName?.Localized(null) ?? candidate.Item.Name ?? candidate.Item.Id}' " +
                    $"from '{candidate.OwnerName}' into '{escapedBot.Profile?.Nickname ?? "Squadmate"}' " +
                    $"carryWeight={state.CurrentWeightKg:0.0}/{state.MaxWeightKg:0.0}kg backpacks={state.RecoveredBackpacks}/{state.BackpackCarryCapacity}.");
                return true;
            }

            if (hadEquipmentSnapshot && attemptedPlacement)
            {
                stats.NoSpace++;
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    "nearby escaped teammates had no valid equipment slot or container space.");
            }
            else if (hadEquipmentSnapshot)
            {
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    "nearby escaped teammates were blocked by weight or backpack carry limits.");
            }
            else
            {
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    "nearby escaped teammates had no serializable equipment snapshot.");
            }

            return false;
        }

        private static InventoryEquipment CloneFollowerEquipment(BotOwner bot)
        {
            try
            {
                return bot?.GetPlayer?.InventoryController?.Inventory?.Equipment?.CloneItemWithSameId() as InventoryEquipment;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeathEscape] Failed to clone escaped follower equipment for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}'.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static void StripTrackedLootFromCarrierEquipment(InventoryEquipment equipment, BotOwner bot)
        {
            if (equipment == null || bot == null)
            {
                return;
            }

            HashSet<string> trackedRootIds = new HashSet<string>(
                InteractableObjects.GetTrackedReturnItemRoots(bot)
                    .Where(item => item != null)
                    .Select(item => item.Id),
                StringComparer.Ordinal);
            if (trackedRootIds.Count == 0)
            {
                return;
            }

            RemoveItemTreesFromEquipment(equipment, trackedRootIds, "tracked follower loot");
        }

        private static void RemoveRecoveredDeathGearFromEscapedEquipment(
            IEnumerable<InventoryEquipment> escapedEquipment,
            HashSet<string> recoveredGearIds)
        {
            if (escapedEquipment == null || recoveredGearIds == null || recoveredGearIds.Count == 0)
            {
                return;
            }

            foreach (InventoryEquipment equipment in escapedEquipment)
            {
                RemoveItemTreesFromEquipment(equipment, recoveredGearIds, "mailed death gear");
            }
        }

        private static void RemoveItemTreesFromEquipment(InventoryEquipment equipment, HashSet<string> itemIds, string context)
        {
            if (equipment == null || itemIds == null || itemIds.Count == 0)
            {
                return;
            }

            List<Item> itemsToRemove = new List<Item>();
            HashSet<string> removeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item root in equipment.GetAllItems()
                         .Where(item => item != null && itemIds.Contains(item.Id))
                         .ToList())
            {
                foreach (Item child in root.GetAllItems().Reverse())
                {
                    if (child != null && removeIds.Add(child.Id))
                    {
                        itemsToRemove.Add(child);
                    }
                }
            }

            foreach (Item item in itemsToRemove)
            {
                try
                {
                    item.Parent?.Remove(item, false);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[DeathEscape] Failed to strip {context} '{item.Id}' from escaped teammate equipment snapshot.");
                    Logger.LogError(ex);
                }
            }
        }

        private static bool TryPlaceRecoveredGear(RecoveryCarrierState state, RecoverableGearCandidate candidate, Item item)
        {
            if (state?.Equipment == null || item == null)
            {
                return false;
            }

            if (TryEquipRecoveredGear(state.Equipment, candidate, item) || TryPackRecoveredGear(state, item))
            {
                return true;
            }

            if (!candidate.CountsAsBackpackCarry)
            {
                return false;
            }

            // Backpack carry is intentionally not limited to normal inventory grids. A survivor
            // can carry one extra backpack if already wearing one, or two if their backpack slot
            // is empty. Empty recovered backpack grids still expand simulated carry space.
            state.AddExternallyCarriedBackpack(item);
            return true;
        }

        private static bool TryEquipRecoveredGear(InventoryEquipment equipment, RecoverableGearCandidate candidate, Item item)
        {
            if (equipment == null || item == null)
            {
                return false;
            }

            foreach (EquipmentSlot slot in GetRecoveryEquipmentSlotOrder(candidate.Slot))
            {
                Slot equipmentSlot = equipment.GetSlot(slot);
                if (equipmentSlot?.ContainedItem != null)
                {
                    continue;
                }

                if (equipmentSlot?.Add(item, false).Succeeded == true)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<EquipmentSlot> GetRecoveryEquipmentSlotOrder(EquipmentSlot sourceSlot)
        {
            switch (sourceSlot)
            {
                case EquipmentSlot.FirstPrimaryWeapon:
                    yield return EquipmentSlot.SecondPrimaryWeapon;
                    yield return EquipmentSlot.FirstPrimaryWeapon;
                    break;
                case EquipmentSlot.SecondPrimaryWeapon:
                    yield return EquipmentSlot.SecondPrimaryWeapon;
                    yield return EquipmentSlot.FirstPrimaryWeapon;
                    break;
                case EquipmentSlot.Holster:
                    yield return EquipmentSlot.Holster;
                    break;
                case EquipmentSlot.ArmorVest:
                    yield return EquipmentSlot.ArmorVest;
                    break;
                case EquipmentSlot.TacticalVest:
                    yield return EquipmentSlot.TacticalVest;
                    break;
                case EquipmentSlot.Headwear:
                    yield return EquipmentSlot.Headwear;
                    break;
                case EquipmentSlot.Earpiece:
                    yield return EquipmentSlot.Earpiece;
                    break;
                case EquipmentSlot.FaceCover:
                    yield return EquipmentSlot.FaceCover;
                    break;
                case EquipmentSlot.Eyewear:
                    yield return EquipmentSlot.Eyewear;
                    break;
                case EquipmentSlot.Backpack:
                    yield return EquipmentSlot.Backpack;
                    break;
            }
        }

        private static bool TryPackRecoveredGear(RecoveryCarrierState state, Item item)
        {
            if (state?.Equipment == null || item == null)
            {
                return false;
            }

            foreach (EFT.InventoryLogic.SearchableItem container in GetRecoveryContainers(state))
            {
                if (container?.Grids == null)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.Grid grid in container.Grids)
                {
                    if (grid?.AddAnywhere(item, EErrorHandlingType.Ignore).Succeeded == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<EFT.InventoryLogic.SearchableItem> GetRecoveryContainers(RecoveryCarrierState state)
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentSlot slot in GetRecoveryContainerOrder())
            {
                EFT.InventoryLogic.SearchableItem rootContainer = state.Equipment.GetSlot(slot)?.ContainedItem as EFT.InventoryLogic.SearchableItem;
                if (rootContainer == null || !yielded.Add(rootContainer.Id))
                {
                    continue;
                }

                yield return rootContainer;

                // If a recovered backpack/container was packed into another container, use
                // its grids too. This is how fallen teammates' backpacks expand survivor space.
                foreach (EFT.InventoryLogic.SearchableItem nested in rootContainer.GetAllItems().OfType<EFT.InventoryLogic.SearchableItem>())
                {
                    if (nested != null && yielded.Add(nested.Id))
                    {
                        yield return nested;
                    }
                }
            }

            foreach (EFT.InventoryLogic.SearchableItem carriedBackpack in state.ExternallyCarriedBackpacks)
            {
                if (carriedBackpack == null || !yielded.Add(carriedBackpack.Id))
                {
                    continue;
                }

                yield return carriedBackpack;
            }
        }

        private static IEnumerable<EquipmentSlot> GetRecoveryContainerOrder()
        {
            yield return EquipmentSlot.Backpack;
            yield return EquipmentSlot.TacticalVest;
            yield return EquipmentSlot.Pockets;
        }

        private static int GetDeathGearItemPriority(EquipmentSlot slot, Item item)
        {
            if (slot == EquipmentSlot.FirstPrimaryWeapon)
            {
                return 0;
            }

            if (slot == EquipmentSlot.ArmorVest || IsArmoredVest(slot, item))
            {
                return 1;
            }

            if (slot == EquipmentSlot.Headwear)
            {
                return 2;
            }

            if (slot == EquipmentSlot.SecondPrimaryWeapon)
            {
                return 3;
            }

            if (slot == EquipmentSlot.Holster)
            {
                return 4;
            }

            if (slot == EquipmentSlot.TacticalVest)
            {
                return 5;
            }

            if (slot == EquipmentSlot.Backpack)
            {
                return 6;
            }

            if (slot == EquipmentSlot.Pockets)
            {
                return 7;
            }

            return 8;
        }

        private static void TrackRecoveredContainerTree(Item item, EquipmentSlot sourceSlot, HashSet<string> coveredByRecoveredTreeIds)
        {
            if (item == null || coveredByRecoveredTreeIds == null || sourceSlot == EquipmentSlot.Backpack)
            {
                return;
            }

            foreach (Item child in item.GetAllItems())
            {
                if (child != null && !string.Equals(child.Id, item.Id, StringComparison.Ordinal))
                {
                    coveredByRecoveredTreeIds.Add(child.Id);
                }
            }
        }

        private static void RemoveAlreadyMailedCoveredItems(Item item, List<Item> gearToReturn, HashSet<string> recoveredGearIds)
        {
            if (item == null || gearToReturn == null || gearToReturn.Count == 0)
            {
                return;
            }

            HashSet<string> coveredIds = new HashSet<string>(
                item.GetAllItems()
                    .Where(child => child != null && !string.Equals(child.Id, item.Id, StringComparison.Ordinal))
                    .Select(child => child.Id),
                StringComparer.Ordinal);
            if (coveredIds.Count == 0)
            {
                return;
            }

            foreach (Item removed in gearToReturn
                         .Where(returned => returned != null && coveredIds.Contains(returned.Id))
                         .ToList())
            {
                gearToReturn.Remove(removed);
                Logger.LogInfo(
                    $"[DeathEscape] Removed separately mailed item '{removed.ShortName?.Localized(null) ?? removed.Name ?? removed.Id}' " +
                    "because its recovered container now carries it.");
            }
        }

        private static bool IsArmoredVest(EquipmentSlot slot, Item item)
        {
            if (slot != EquipmentSlot.TacticalVest || item == null)
            {
                return false;
            }

            return item.TryGetItemComponent<ArmorHolderComponent>(out _)
                   || item.GetItemComponentsInChildren<ArmorComponent>(true).Any()
                   || item.GetItemComponentsInChildren<CompositeArmorComponent>(true).Any();
        }

        private static float GetItemWeight(Item item)
        {
            try
            {
                return Mathf.Max(0f, item?.TotalWeight ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private static float GetItemShellWeight(Item item)
        {
            try
            {
                return Mathf.Max(0f, item?.Weight ?? 0f);
            }
            catch
            {
                return 0f;
            }
        }

        private static float GetRecoveryCandidateWeight(RecoverableGearCandidate candidate)
        {
            // Backpack shells are treated as carried capacity, not carried load. Their contents
            // still count when they are recovered as separate backpack-content candidates.
            if (candidate.IgnoreCarryWeight)
            {
                return 0f;
            }

            // Backpack shells are capacity, not carried load. For nested backpacks, only their
            // contents count toward the survivor's weight budget.
            return candidate.CountsAsBackpackCarry
                ? Mathf.Max(0f, GetItemWeight(candidate.Item) - GetItemShellWeight(candidate.Item))
                : GetItemWeight(candidate.Item);
        }

        private static float GetCarrierStartingWeightKg(InventoryEquipment equipment)
        {
            float weight = GetItemWeight(equipment);
            try
            {
                Item backpack = equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
                weight -= GetItemShellWeight(backpack);

                // Secure containers are excluded from the escape-carry budget in every mode.
                // They are protected/special-purpose containers, not general squad recovery space.
                Item secureContainer = equipment?.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem;
                weight -= GetItemWeight(secureContainer);

                return Mathf.Max(0f, weight);
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to adjust carrier start weight for backpack/secure-container policy.");
                Logger.LogError(ex);
                return weight;
            }
        }

        private static float GetCarrierWalkDrainWeightLimitKg(BotOwner bot)
        {
            try
            {
                // Match EFT's player weight-limit formula from BasePhysicalClass.UpdateWeightLimits:
                // WalkOverweightLimits.x is scaled by Strength carry bonus and health/stim modifiers.
                float baseLimit = Singleton<EFT.GlobalConfiguration>.Instantiated
                    ? Singleton<EFT.GlobalConfiguration>.Instance.Stamina.WalkOverweightLimits.x
                    : FallbackEscapeCarrierWeightKg;
                float skillRelative = bot?.GetPlayer?.Skills?.CarryingWeightRelativeModifier ?? 1f;
                float healthRelative = bot?.GetPlayer?.HealthController?.CarryingWeightRelativeModifier ?? 1f;
                float healthAbsolute = bot?.GetPlayer?.HealthController?.CarryingWeightAbsoluteModifier ?? 0f;
                return Mathf.Max(1f, baseLimit * skillRelative * healthRelative + healthAbsolute);
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to calculate carrier walk-drain weight limit; using fallback.");
                Logger.LogError(ex);
                return FallbackEscapeCarrierWeightKg;
            }
        }

        private static string DescribeRecoverableItem(RecoverableGearCandidate candidate)
        {
            Item item = candidate.Item;
            string itemName = item?.ShortName?.Localized(null) ?? item?.Name ?? item?.Id ?? "unknown";
            return $"{itemName}/{candidate.Slot}";
        }

        private static Item CloneRecoveredItemForMail(RecoverableGearCandidate candidate)
        {
            return candidate.CloneReturnWithNewIds
                ? candidate.Item.CloneItem()
                : candidate.Item.CloneItemWithSameId();
        }

        private static JsonType.FlatItem[] SerializeFollowerEquipment(BotOwner bot)
        {
            try
            {
                Item equipment = bot?.GetPlayer?.InventoryController?.Inventory?.Equipment;
                if (equipment == null)
                {
                    return null;
                }

                return Singleton<EFT.ItemFactory>.Instance.TreeToFlatItems(new Item[] { equipment });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeathEscape] Failed to serialize escaped follower equipment for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}'.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static JsonType.FlatItem[] SerializeEquipment(InventoryEquipment equipment)
        {
            try
            {
                return equipment == null
                    ? null
                    : Singleton<EFT.ItemFactory>.Instance.TreeToFlatItems(new Item[] { equipment });
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to serialize recovered escaped follower equipment.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static string[] GetTrackedFollowerItemIds(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return Array.Empty<string>();
            }

            return InteractableObjects.GetStoredItems(bot.ProfileId)?.ToArray() ?? Array.Empty<string>();
        }
    }
}
