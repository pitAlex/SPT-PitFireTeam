using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private const int LootedPrimarySwitchMaxAttempts = 8;
        private const float LootedPrimarySwitchRetryDelaySeconds = 0.45f;

        private bool TryStartEasyBodyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetBodyWeaponEquipCandidates(corpseEquipment))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                IEnumerable<BodyGearCandidate> magazineCandidates = GetBodyOperationalMagazineCandidates(corpseEquipment, (Weapon)swapCandidate.Item);
                if (!TryBuildEasyWeaponEquipMove(
                        inventory,
                        followerEquipment,
                        swapCandidate,
                        magazineCandidates,
                        out BodyGearMove? move,
                        out bool handledByGearPolicy))
                {
                    if (handledByGearPolicy)
                    {
                        // The no-fast-access policy is terminal. Do not let ordinary price/category
                        // looting move this weapon somewhere the gear planner explicitly rejected.
                        bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                    }

                    continue;
                }

                bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartBodyGearMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartEasyBodyTacticalVestMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            Item vest = corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (vest is not VestItemClass || string.IsNullOrEmpty(vest.Id))
            {
                return false;
            }

            BodyGearCandidate cargoCandidate = new BodyGearCandidate(
                vest,
                EquipmentSlot.TacticalVest,
                "TacticalVest.EquipmentUpgrade",
                2);
            BodyGearCandidate swapCandidate = CreateGearSwapCandidate(cargoCandidate);
            if (!CanConsiderFilteredLootCandidate(swapCandidate, bodyLootAttemptedItemIds) ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
            {
                return false;
            }

            if (TryBuildTacticalVestEquipMove(inventory, followerEquipment, swapCandidate, out BodyGearMove? equipMove))
            {
                if (ReferenceEquals(equipMove.Item, swapCandidate.Item))
                {
                    bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }

                if (TryQueueBodyLootMoveAfterPickupSuccess(equipMove))
                {
                    return true;
                }

                StartBodyGearMove(inventory, equipMove);
                return true;
            }

            // If the found vest is a real protection upgrade but cannot be safely equipped without
            // disturbing current rig contents, treat it as cargo instead of throwing the old vest.
            if (CanConsiderFilteredLootCandidate(cargoCandidate, bodyLootAttemptedItemIds) &&
                IsPotentialTacticalVestProtectionUpgrade(followerEquipment, vest) &&
                TryBuildFilteredLootMove(inventory, followerEquipment, cargoCandidate, null, out BodyGearMove? cargoMove))
            {
                bodyLootAttemptedItemIds.Add(cargoCandidate.Item.Id);
                if (TryQueueBodyLootMoveAfterPickupSuccess(cargoMove))
                {
                    return true;
                }

                StartBodyGearMove(inventory, cargoMove);
                return true;
            }

            return false;
        }

        private bool TryStartEasyContainerWeaponEquipMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetContainerWeaponEquipCandidates(containerRoot))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                IEnumerable<BodyGearCandidate> magazineCandidates = GetContainerOperationalMagazineCandidates(containerRoot, (Weapon)swapCandidate.Item);
                if (!TryBuildEasyWeaponEquipMove(
                        inventory,
                        followerEquipment,
                        swapCandidate,
                        magazineCandidates,
                        out BodyGearMove? move,
                        out bool handledByGearPolicy))
                {
                    if (handledByGearPolicy)
                    {
                        containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                    }

                    continue;
                }

                containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartEasyContainerTacticalVestMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                         containerRoot,
                         "Container.Contents",
                         skipMagazines: false).Where(IsTacticalVestEquipCandidate))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                if (!TryBuildTacticalVestEquipMove(inventory, followerEquipment, swapCandidate, out BodyGearMove? move))
                {
                    continue;
                }

                if (ReferenceEquals(move.Item, swapCandidate.Item))
                {
                    containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }

                if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private static bool IsEasyWeaponEquipCandidate(BodyGearCandidate candidate)
        {
            return candidate?.Item is Weapon weapon &&
                   weapon.GetItemComponent<KnifeComponent>() == null &&
                   weapon is not PistolItemClass &&
                   weapon is not RevolverItemClass;
        }

        private static IEnumerable<BodyGearCandidate> GetBodyWeaponEquipCandidates(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                yield break;
            }

            HashSet<string> yieldedItemIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (EquipmentSlot slot in BodyGearWeaponSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (TryCreateWeaponEquipCandidate(item, slot, slot.ToString(), 2, yieldedItemIds, out BodyGearCandidate? candidate))
                {
                    yield return candidate;
                }
            }

            // Weapon equip is its own tactical scenario. It must not depend on generic body-loot
            // filtering, where vest/pocket magazines are intentionally skipped as cargo.
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pockets, EquipmentSlot.TacticalVest })
            {
                foreach (BodyGearCandidate candidate in GetWeaponEquipCandidatesFromRoot(
                             corpseEquipment.GetSlot(slot)?.ContainedItem,
                             $"{slot}.WeaponEquip",
                             1,
                             yieldedItemIds))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<BodyGearCandidate> GetContainerWeaponEquipCandidates(SearchableItemItemClass containerRoot)
        {
            HashSet<string> yieldedItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in GetWeaponEquipCandidatesFromRoot(
                         containerRoot,
                         "Container.WeaponEquip",
                         1,
                         yieldedItemIds))
            {
                yield return candidate;
            }
        }

        private static IEnumerable<BodyGearCandidate> GetWeaponEquipCandidatesFromRoot(
            Item root,
            string sourceName,
            int sourceTier,
            HashSet<string> yieldedItemIds)
        {
            if (root == null)
            {
                yield break;
            }

            if (TryCreateWeaponEquipCandidate(root, null, sourceName, sourceTier, yieldedItemIds, out BodyGearCandidate? rootCandidate))
            {
                yield return rootCandidate;
            }

            foreach (Item item in SnapshotLootTreeItems(root))
            {
                if (ReferenceEquals(item, root))
                {
                    continue;
                }

                if (TryCreateWeaponEquipCandidate(item, null, sourceName, sourceTier, yieldedItemIds, out BodyGearCandidate? candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static bool TryCreateWeaponEquipCandidate(
            Item item,
            EquipmentSlot? sourceSlot,
            string sourceName,
            int sourceTier,
            HashSet<string> yieldedItemIds,
            out BodyGearCandidate? candidate)
        {
            candidate = null;
            if (item == null || string.IsNullOrEmpty(item.Id))
            {
                return false;
            }

            BodyGearCandidate possibleCandidate = new BodyGearCandidate(item, sourceSlot, sourceName, sourceTier);
            if (!IsEasyWeaponEquipCandidate(possibleCandidate) || !yieldedItemIds.Add(item.Id))
            {
                return false;
            }

            candidate = possibleCandidate;
            return true;
        }

        private static bool IsTacticalVestEquipCandidate(BodyGearCandidate candidate)
        {
            return candidate?.Item is VestItemClass;
        }

        private static BodyGearCandidate CreateGearSwapCandidate(BodyGearCandidate candidate)
        {
            // Allow Gear Swapping is the only user-facing filter for add/swap planning. Price,
            // Pickup Gear, and normal magazine-skip rules belong to ordinary cargo looting.
            // Protection checks and executable inventory placement still apply downstream.
            return new BodyGearCandidate(
                item: candidate.Item,
                sourceSlot: candidate.SourceSlot,
                sourceName: candidate.SourceName,
                sourceTier: candidate.SourceTier,
                skipMagazine: candidate.SkipMagazine,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: candidate.BypassBodyGearLootability,
                reportAsLootNothing: candidate.ReportAsLootNothing);
        }

        private bool TryBuildTacticalVestEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                candidate?.Item is not VestItemClass foundVest)
            {
                return false;
            }

            if (TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out move))
            {
                return true;
            }

            if (!CanReplaceOccupiedGearSlot())
            {
                return false;
            }

            Item currentVest = followerEquipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (!IsSafeTacticalVestSwapCandidate(followerEquipment, currentVest, foundVest) ||
                !TryFindBackpackAddressForItem(followerEquipment, currentVest, out ItemAddress? preserveAddress) ||
                !TryCreateBodyGearMove(
                    inventory,
                    new BodyGearCandidate(currentVest, EquipmentSlot.TacticalVest, "TacticalVest.PreserveOld", 0, reportAsLootNothing: true),
                    preserveAddress,
                    out BodyGearMove? preserveMove,
                    storeAsLoot: false))
            {
                return false;
            }

            move = preserveMove.WithFollowUps(new[] { candidate });
            return true;
        }

        private bool TryBuildTacticalVestEquipIntoEmptySlot(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            return TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.TacticalVest, candidate.Item, out ItemAddress? vestAddress) &&
                   TryCreateBodyGearMove(inventory, candidate, vestAddress, out move, storeAsLoot: ShouldReturnGearSwapAsCargo());
        }

        private static bool IsSafeTacticalVestSwapCandidate(InventoryEquipment followerEquipment, Item currentVest, Item foundVest)
        {
            if (followerEquipment == null ||
                currentVest == null ||
                foundVest == null ||
                HasOperationalTacticalVestContents(currentVest))
            {
                return false;
            }

            return IsPotentialTacticalVestProtectionUpgrade(followerEquipment, foundVest);
        }

        private static bool IsPotentialTacticalVestProtectionUpgrade(InventoryEquipment followerEquipment, Item foundVest)
        {
            if (followerEquipment == null ||
                foundVest == null ||
                !TryGetTacticalVestProtectionScore(foundVest, out float foundScore))
            {
                return false;
            }

            Item armorVest = followerEquipment.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem;
            Item currentVest = followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            bool currentProtected = TryGetTacticalVestProtectionScore(currentVest, out float currentScore);
            if (!currentProtected && armorVest == null)
            {
                return true;
            }

            return currentProtected && foundScore > currentScore + 25f;
        }

        private static bool HasOperationalTacticalVestContents(Item vest)
        {
            return GetDirectLootChildren(vest).Any(item => item is not ArmorPlateItemClass);
        }

        private static bool TryGetTacticalVestProtectionScore(Item vest, out float score)
        {
            score = 0f;
            if (vest == null)
            {
                return false;
            }

            foreach (ArmorComponent armor in vest.GetItemComponentsInChildren<ArmorComponent>(true))
            {
                RepairableComponent repairable = armor?.Repairable;
                if (repairable == null || repairable.TemplateDurability <= 0 || repairable.Durability <= 1f)
                {
                    continue;
                }

                float durabilityRatio = Mathf.Clamp01(repairable.Durability / repairable.TemplateDurability);
                score = Mathf.Max(score, armor.ArmorClass * 100f + durabilityRatio * 100f);
            }

            return score > 0f;
        }

        private bool TryBuildEasyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            out BodyGearMove? move,
            out bool handledByGearPolicy)
        {
            move = null;
            handledByGearPolicy = false;
            // Gear swapping phase 1 only equips an empty primary. Replacing a spawned primary is
            // deferred because vanilla bot weapon/reload state is cached beyond the slot item.
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                candidate?.Item is not Weapon weapon ||
                !IsEasyWeaponEquipCandidate(candidate) ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null ||
                !TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.FirstPrimaryWeapon, weapon, out _))
            {
                return false;
            }

            handledByGearPolicy = true;

            OperationalMagazinePlan magazinePlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                operationalMagazineCandidates);
            LogOperationalMagazinePlan(weapon, magazinePlan);

            // Vanilla detachable-mag reloads only search fast-access slots. When no spare can fit
            // there, backpack magazines are not operational and must not justify primary equip.
            if (magazinePlan.OperationalVestCount == 0)
            {
                if (HasFullHighCapacityMagazineForPrimary(weapon))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] No-fast-access weapon policy for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"primary with full 60+ magazine weapon={DescribeLootDebugItem(weapon)}");
                    return TryBuildPrimaryWeaponEquipMove(inventory, followerEquipment, candidate, out move, out _);
                }

                bool builtSupportCargo = TryBuildNoFastAccessWeaponCargoMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out string cargoDestination);
                Modules.Logger.LogInfo(
                    $"[LootCommand] No-fast-access weapon policy for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"destination={cargoDestination} weapon={DescribeLootDebugItem(weapon)}");
                return builtSupportCargo;
            }

            if (magazinePlan.FollowUps.Count > 0)
            {
                BodyGearCandidate firstMagazineCandidate = magazinePlan.FollowUps[0];
                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        firstMagazineCandidate,
                        out BodyGearMove? firstMagazineMove,
                        out string firstMagazineReason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Primary equip mag-first build rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"weapon={DescribeLootDebugItem(weapon)} firstMag={DescribeLootDebugItem(firstMagazineCandidate?.Item)} reason={firstMagazineReason}");
                    return false;
                }

                List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                for (int i = 1; i < magazinePlan.FollowUps.Count; i++)
                {
                    followUps.Add(magazinePlan.FollowUps[i]);
                }

                followUps.Add(candidate.WithFollowUpDestination(BodyGearFollowUpDestination.PrimaryWeaponEquip));
                move = firstMagazineMove.WithFollowUps(followUps, EPhraseTrigger.LootWeapon);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Primary equip mag-first chain built for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"weapon={DescribeLootDebugItem(weapon)} firstMag={DescribeLootDebugItem(firstMagazineCandidate?.Item)} " +
                    $"remainingFollowUps={followUps.Count} plannedMags={magazinePlan.FollowUps.Count}");
                return true;
            }

            if (!TryBuildPrimaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out BodyGearMove? primaryMove,
                    out _))
            {
                return false;
            }

            move = primaryMove.WithFollowUps(magazinePlan.FollowUps);
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Primary equip move built for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={DescribeLootDebugItem(weapon)} followUps={magazinePlan.FollowUps.Count}");
            return true;
        }

        private bool TryBuildNoFastAccessWeaponCargoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string destination)
        {
            move = null;
            destination = "leftOnSource";
            Item weapon = candidate?.Item;
            if (weapon == null)
            {
                return false;
            }

            // Secondary is a support/cargo slot in this phase. It is never displaced.
            if (TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    weapon,
                    out ItemAddress? secondaryAddress) &&
                TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    secondaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootWeapon))
            {
                destination = "SecondPrimaryWeapon";
                return true;
            }

            if (TryFindBackpackAddressForItem(followerEquipment, weapon, out ItemAddress? backpackAddress) &&
                TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    backpackAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootWeapon))
            {
                destination = "BackpackCargo";
                return true;
            }

            return false;
        }

        private bool TryBuildPrimaryWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = string.Empty;

            if (candidate?.Item is not Weapon weapon)
            {
                reason = "notWeapon";
                return false;
            }

            if (!IsEasyWeaponEquipCandidate(candidate))
            {
                reason = "notEasyWeaponCandidate";
                return false;
            }

            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null)
            {
                reason = "primaryOccupied";
                return false;
            }

            if (!TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.FirstPrimaryWeapon, weapon, out ItemAddress? firstPrimaryAddress))
            {
                reason = "noPrimaryAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    firstPrimaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootWeapon,
                    rebindAsPrimaryWeapon: true))
            {
                reason = "primaryMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private void RebindLootedPrimaryWeapon(Weapon weapon)
        {
            if (!TryRebindLootedPrimaryWeaponInfo(weapon, out string reason))
            {
                Modules.Logger.LogInfo($"[LootCommand] Skipped looted primary rebind: {reason}");
                return;
            }

            TryEnsureLootedPrimarySelected(weapon, 0);
        }

        private bool TryRebindLootedPrimaryWeaponInfo(Weapon weapon, out string reason)
        {
            reason = string.Empty;
            if (weapon == null)
            {
                reason = "weaponMissing";
                return false;
            }

            if (BotOwner?.WeaponManager == null)
            {
                reason = "weaponManagerMissing";
                return false;
            }

            try
            {
                BotWeaponManager weaponManager = BotOwner.WeaponManager;
                BotWeaponSelector selector = weaponManager.Selector;
                if (selector == null)
                {
                    reason = "selectorMissing";
                    return false;
                }

                Weapon slottedPrimary = BotOwner.GetPlayer?.InventoryController?.Inventory?.Equipment
                    ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
                if (!IsSameLootItem(slottedPrimary, weapon))
                {
                    reason = "weaponNotInPrimarySlot";
                    return false;
                }

                // A physical inventory move does not rebuild the bot's spawn-time weapon info.
                // Rebind the main slot explicitly so combat/reload logic sees the new primary.
                selector.UpdateWeaponsList();
                if (!IsSameLootItem(selector.FirstPrimaryWeaponItem, weapon))
                {
                    reason = "selectorPrimaryCacheMismatch";
                    return false;
                }

                selector.MainWeapon = EquipmentSlot.FirstPrimaryWeapon;
                BotWeaponInfo mainInfo = new BotWeaponInfo(
                    BotOwner,
                    weapon,
                    EquipmentSlot.FirstPrimaryWeapon,
                    weaponManager.method_5);
                weaponManager.Info[EquipmentSlot.FirstPrimaryWeapon] = mainInfo;

                if (weaponManager.CurrentWeaponInfo == null ||
                    selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
                {
                    weaponManager.CurrentWeaponInfo = mainInfo;
                }

                selector.IsWeaponReady = true;
                selector.NextChangeTime = 0f;
                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private bool TryEnsureLootedPrimarySelected(Weapon weapon, int attempt)
        {
            try
            {
                if (!TryRebindLootedPrimaryWeaponInfo(weapon, out string rebindReason))
                {
                    LogLootedPrimarySwitchFinalFailure(weapon, attempt, rebindReason);
                    return false;
                }

                BotWeaponManager weaponManager = BotOwner.WeaponManager;
                BotWeaponSelector selector = weaponManager.Selector;
                if (IsLootedPrimarySelected(weaponManager, selector, weapon))
                {
                    return true;
                }

                string blockReason = GetLootedPrimarySwitchBlockReason(weaponManager, selector);
                if (string.IsNullOrEmpty(blockReason))
                {
                    blockReason = selector.ChangeToMain()
                        ? "switchRequested"
                        : "selectorRejectedChangeToMain";
                }

                if (attempt >= LootedPrimarySwitchMaxAttempts)
                {
                    LogLootedPrimarySwitchFinalFailure(weapon, attempt, blockReason);
                    return false;
                }

                QueueLootedPrimarySwitchRetry(weapon, attempt + 1);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt >= LootedPrimarySwitchMaxAttempts)
                {
                    LogLootedPrimarySwitchFinalFailure(weapon, attempt, ex.Message);
                }
                else
                {
                    QueueLootedPrimarySwitchRetry(weapon, attempt + 1);
                }

                return false;
            }
        }

        private bool IsLootedPrimarySelected(
            BotWeaponManager weaponManager,
            BotWeaponSelector selector,
            Weapon weapon)
        {
            Weapon activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            return selector?.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon &&
                   IsSameLootItem(activeWeapon, weapon) &&
                   weaponManager?.MainWeaponInfo?.weapon != null &&
                   IsSameLootItem(weaponManager.MainWeaponInfo.weapon, weapon);
        }

        private static string GetLootedPrimarySwitchBlockReason(
            BotWeaponManager weaponManager,
            BotWeaponSelector selector)
        {
            if (weaponManager == null)
            {
                return "weaponManagerMissing";
            }

            if (selector == null)
            {
                return "selectorMissing";
            }

            if (selector.IsChanging)
            {
                return "selectorChanging";
            }

            if (!selector.IsWeaponReady)
            {
                return "weaponNotReady";
            }

            if (weaponManager.Reload?.Reloading == true)
            {
                return "reloading";
            }

            if (!weaponManager.CanChangeHands())
            {
                return "handsBusy";
            }

            return string.Empty;
        }

        private void QueueLootedPrimarySwitchRetry(Weapon weapon, int attempt)
        {
            try
            {
                if (BotOwner?.AITaskManager == null)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Looted primary switch retry unavailable for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': taskManagerMissing");
                    return;
                }

                BotOwner.AITaskManager.RegisterDelayedTask(
                    BotOwner,
                    LootedPrimarySwitchRetryDelaySeconds,
                    () => TryEnsureLootedPrimarySelected(weapon, attempt));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Looted primary switch retry failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {ex.Message}");
            }
        }

        private void LogLootedPrimarySwitchFinalFailure(Weapon weapon, int attempt, string reason)
        {
            if (attempt < LootedPrimarySwitchMaxAttempts)
            {
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand] Looted primary switch did not complete for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"{weapon?.TemplateId ?? "unknown"} reason={reason}");
        }

        private bool TryStartPendingBodyGearSwapFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingBodyGearSwapFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingBodyGearSwapFollowUps.Dequeue();
                if (candidate?.Item is VestItemClass)
                {
                    if (!TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out BodyGearMove? vestMove))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, vestMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Body primary equip follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} item={DescribeLootDebugItem(candidate.Item)} pendingAfterDequeue={pendingBodyGearSwapFollowUps.Count}");

                    if (!TryBuildPrimaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? primaryMove,
                            out string primaryReason))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body primary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item?.TemplateId ?? "unknown"} reason={primaryReason}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, primaryMove);
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(candidate?.Item)} pendingAfterDequeue={pendingBodyGearSwapFollowUps.Count}");

                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out BodyGearMove? move,
                        out string reason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Body support-mag follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"{candidate?.SourceName ?? "unknown"}:{candidate?.Item?.TemplateId ?? "unknown"} reason={reason}");
                    continue;
                }

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body follow-up starting move for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)}");
                StartBodyGearMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartPendingContainerGearSwapFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingContainerGearSwapFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingContainerGearSwapFollowUps.Dequeue();
                if (candidate?.Item is VestItemClass)
                {
                    if (!TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out BodyGearMove? vestMove))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, vestMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Container primary equip follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} item={DescribeLootDebugItem(candidate.Item)} pendingAfterDequeue={pendingContainerGearSwapFollowUps.Count}");

                    if (!TryBuildPrimaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? primaryMove,
                            out string primaryReason))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container primary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item?.TemplateId ?? "unknown"} reason={primaryReason}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, primaryMove);
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(candidate?.Item)} pendingAfterDequeue={pendingContainerGearSwapFollowUps.Count}");

                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out BodyGearMove? move,
                        out string reason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Container support-mag follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"{candidate?.SourceName ?? "unknown"}:{candidate?.Item?.TemplateId ?? "unknown"} reason={reason}");
                    continue;
                }

                containerLootAttemptedItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container follow-up starting move for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)}");
                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryBuildSupportMagazineFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Follow-up build start for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                $"item={DescribeLootDebugItem(candidate?.Item)}");

            if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine, out string validationReason))
            {
                reason = validationReason;
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up build rejected validation for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"reason={reason} item={DescribeLootDebugItem(candidate?.Item)}");
                return false;
            }

            if (candidate.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo)
            {
                bool backpackOnly = TryBuildBackpackMagazineCargoMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up backpack-only result for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"ok={backpackOnly} reason={reason} item={DescribeLootDebugItem(magazine)}");
                return backpackOnly;
            }

            if (TryBuildOperationalMagazineVestMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out string vestReason))
            {
                reason = "ok";
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up vest result for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"ok=True item={DescribeLootDebugItem(magazine)}");
                return true;
            }

            if (TryBuildBackpackMagazineCargoMove(
                inventory,
                followerEquipment,
                candidate.WithFollowUpDestination(BodyGearFollowUpDestination.BackpackCargo),
                out move,
                out string backpackReason))
            {
                reason = $"vestFallback:{vestReason}";
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up backpack fallback result for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"ok=True vestReason={vestReason} item={DescribeLootDebugItem(magazine)}");
                return true;
            }

            reason = $"vest:{vestReason};backpack:{backpackReason}";
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Follow-up build rejected placement for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"reason={reason} item={DescribeLootDebugItem(magazine)}");
            return false;
        }

        private void LogOperationalMagazinePlan(Weapon weapon, OperationalMagazinePlan plan)
        {
            if (plan == null)
            {
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand] Support-mag plan for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={weapon?.TemplateId ?? "unknown"} scanned={plan.ScannedCount} loaded={plan.ValidLoadedCount} " +
                $"queued={plan.FollowUps.Count} vest={plan.OperationalVestCount} rejects={string.Join(",", plan.RejectionReasons)}");
        }

        private static bool ShouldReturnGearSwapAsCargo()
        {
            // Simple/Restricted gear additions are temporary combat upgrades and must return by mail
            // like normal follower cargo. Immersive/Realistic leave them untracked so the escaped
            // teammate's live equipment snapshot can persist the new kit.
            return !pitFireTeam.IsFollowerLoadoutLootableMode();
        }

        private static bool CanReplaceOccupiedGearSlot()
        {
            // Simple/Restricted can add into empty equipment slots, but cannot replace spawned kit.
            // Actual occupied-slot swapping is reserved for the lootable Immersive/Realistic modes.
            return pitFireTeam.IsFollowerLoadoutLootableMode();
        }
    }
}
