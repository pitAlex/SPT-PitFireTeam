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
        private IEnumerable<BodyGearCandidate> GetBodyOperationalMagazineCandidates(InventoryEquipment corpseEquipment, Weapon weapon)
        {
            if (corpseEquipment == null || weapon == null)
            {
                yield break;
            }

            // These mags support a newly equipped weapon, so vest/pocket mags are allowed here.
            // Generic body looting still skips vest/pocket mags to avoid disrupting reload setups.
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets, EquipmentSlot.Backpack })
            {
                Item root = corpseEquipment.GetSlot(slot)?.ContainedItem;
                foreach (MagazineItemClass magazine in GetOperationalMagazineItems(root, weapon, $"{slot}.WeaponSupportMagazine"))
                {
                    yield return new BodyGearCandidate(
                        magazine,
                        null,
                        $"{slot}.WeaponSupportMagazine",
                        0,
                        bypassPriceThreshold: true,
                        bypassCategoryFilter: true);
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerOperationalMagazineCandidates(SearchableItemItemClass containerRoot, Weapon weapon)
        {
            foreach (MagazineItemClass magazine in GetOperationalMagazineItems(containerRoot, weapon, "Container.WeaponSupportMagazine"))
            {
                yield return new BodyGearCandidate(
                    magazine,
                    null,
                    "Container.WeaponSupportMagazine",
                    0,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true);
            }
        }

        private IEnumerable<BodyGearCandidate> GetFollowerBackpackOperationalMagazineCandidates(
            InventoryEquipment followerEquipment,
            Weapon weapon)
        {
            Item backpack = followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            foreach (MagazineItemClass magazine in GetOperationalMagazineItems(
                         backpack,
                         weapon,
                         "FollowerBackpack.WeaponSupportMagazine"))
            {
                if (InteractableObjects.IsStrictCargoItem(BotOwner, magazine))
                {
                    continue;
                }

                yield return new BodyGearCandidate(
                    magazine,
                    null,
                    "FollowerBackpack.WeaponSupportMagazine",
                    0,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true);
            }
        }

        private IEnumerable<MagazineItemClass> GetOperationalMagazineItems(Item root, Weapon weapon, string sourceName)
        {
            OperationalMagazineScanStats stats = new OperationalMagazineScanStats(sourceName, root, weapon);
            if (root == null || weapon == null)
            {
                stats.AddRejectedMagazine(DescribeLootDebugItem(root), root == null ? "rootMissing" : "weaponMissing");
                LogOperationalMagazineScan(stats);
                yield break;
            }

            HashSet<string> weaponTreeItemIds = SnapshotLootTreeItemIds(weapon);
            string? currentMagazineId = null;
            try
            {
                currentMagazineId = weapon.GetCurrentMagazine()?.Id;
            }
            catch
            {
                currentMagazineId = null;
            }

            // Snapshot before filtering. EFT item trees are live dictionaries and can mutate while
            // search/loot state changes, which otherwise turns a support-mag scan into a hard
            // planning failure.
            List<Item> snapshot = SnapshotLootTreeItems(root);
            stats.TreeItemsScanned = snapshot.Count;
            foreach (Item item in snapshot)
            {
                if (item is not MagazineItemClass magazine)
                {
                    continue;
                }

                stats.MagazinesSeen++;
                string magazineDescription = DescribeLootDebugItem(magazine);
                if (string.IsNullOrEmpty(magazine.Id))
                {
                    stats.AddRejectedMagazine(magazineDescription, "missingId");
                    continue;
                }

                if (weaponTreeItemIds.Contains(magazine.Id))
                {
                    stats.AddRejectedMagazine(magazineDescription, "inWeaponTree");
                    continue;
                }

                if (string.Equals(currentMagazineId, magazine.Id, StringComparison.Ordinal))
                {
                    stats.AddRejectedMagazine(magazineDescription, "currentWeaponMag");
                    continue;
                }

                if (IsMagazineInstalledInWeapon(magazine))
                {
                    stats.AddRejectedMagazine(magazineDescription, "installedInWeapon");
                    continue;
                }

                if (magazine.Count <= 0)
                {
                    stats.AddRejectedMagazine(magazineDescription, "empty");
                    continue;
                }

                if (IsItemInsideRoot(magazine, weapon))
                {
                    stats.AddRejectedMagazine(magazineDescription, "insideWeapon");
                    continue;
                }

                if (IsMagazineCompatibleWithWeapon(weapon, magazine))
                {
                    stats.CompatibleCount++;
                    stats.AddAcceptedMagazine(magazineDescription);
                    yield return magazine;
                    continue;
                }

                stats.AddRejectedMagazine(magazineDescription, "incompatible");
            }

            LogOperationalMagazineScan(stats);
        }

        private static bool IsMagazineInstalledInWeapon(Item magazine)
        {
            try
            {
                return magazine?.GetAllParentItems(false).Any(parent => parent is Weapon) == true;
            }
            catch
            {
                // A magazine whose ownership tree cannot be proven safe is not detached.
                return true;
            }
        }

        private OperationalMagazinePlan PlanOperationalMagazineFollowUps(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? candidates)
        {
            OperationalMagazinePlan plan = new OperationalMagazinePlan();
            if (inventory == null || followerEquipment == null || weapon == null || candidates == null)
            {
                return plan;
            }

            List<BodyGearCandidate> candidateList = candidates.ToList();
            plan.ScannedCount = candidateList.Count;
            SearchableItemItemClass simulatedVest = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
            SearchableItemItemClass simulatedPockets = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem);
            SearchableItemItemClass simulatedBackpack = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem);
            Item? reloadReserveMagazine = GetWeaponReloadReserveMagazine(weapon);
            HashSet<string> consideredItemIds = new HashSet<string>(StringComparer.Ordinal);
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Plan start for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={DescribeOperationalWeapon(weapon)} candidates={candidateList.Count} " +
                $"vest={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem)} " +
                $"pockets={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem)} " +
                $"backpack={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem)} " +
                $"reloadReserve={DescribeLootDebugItem(reloadReserveMagazine)}");

            foreach (BodyGearCandidate candidate in candidateList)
            {
                if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine, out string validationReason))
                {
                    plan.AddRejection(validationReason);
                    LogOperationalMagazinePlanStep(candidate, $"reject validation={validationReason}");
                    continue;
                }

                if (!consideredItemIds.Add(magazine.Id))
                {
                    plan.AddRejection("duplicate");
                    LogOperationalMagazinePlanStep(candidate, "reject duplicate");
                    continue;
                }

                plan.ValidLoadedCount++;
                plan.CompatibleLoadedCandidates.Add(candidate);
                LogOperationalMagazinePlanStep(candidate, $"consider mag={DescribeLootDebugItem(magazine)}");
                // Vanilla reload searches both tactical vest and pockets. Plan against both while
                // preserving one shared fast-access landing space for the inserted magazine.
                if (TrySimulateFastAccessAddWithReserve(
                        simulatedVest,
                        simulatedPockets,
                        magazine,
                        reloadReserveMagazine,
                        out SearchableItemItemClass? nextVest,
                        out SearchableItemItemClass? nextPockets,
                        out BodyGearFollowUpDestination fastAccessDestination))
                {
                    BodyGearCandidate fastAccessCandidate = candidate.WithFollowUpDestination(fastAccessDestination);
                    if (TryBuildOperationalMagazineFastAccessMove(
                            inventory,
                            followerEquipment,
                            fastAccessCandidate,
                            out _,
                            out string fastAccessMoveFailure))
                    {
                        simulatedVest = nextVest;
                        simulatedPockets = nextPockets;
                        if (fastAccessDestination == BodyGearFollowUpDestination.OperationalVest)
                        {
                            plan.OperationalVestCount++;
                        }
                        else
                        {
                            plan.OperationalPocketsCount++;
                        }

                        plan.FollowUps.Add(fastAccessCandidate);
                        LogOperationalMagazinePlanStep(fastAccessCandidate, $"queued destination={fastAccessDestination}");
                        continue;
                    }

                    plan.AddRejection($"fastAccessMove:{fastAccessMoveFailure}");
                    LogOperationalMagazinePlanStep(fastAccessCandidate, $"reject fastAccessMove={fastAccessMoveFailure}");
                }
                else
                {
                    plan.AddRejection("fastAccessFit");
                    LogOperationalMagazinePlanStep(candidate, "reject fastAccessFit");
                }

                if (IsLootNowInBotInventory(BotOwner?.GetPlayer, magazine))
                {
                    // A loose follower-backpack magazine may move to fast access, but when it
                    // does not fit it is already valid cargo and needs no backpack follow-up.
                    plan.AddRejection("alreadyBackpackCargo");
                    LogOperationalMagazinePlanStep(candidate, "retain destination=BackpackCargo");
                    continue;
                }

                if (simulatedBackpack != null &&
                    TrySimulateContainerAdd(simulatedBackpack, magazine, out SearchableItemItemClass? nextBackpack))
                {
                    BodyGearCandidate backpackCandidate = candidate.WithFollowUpDestination(BodyGearFollowUpDestination.BackpackCargo);
                    if (TryBuildBackpackMagazineCargoMove(
                            inventory,
                            followerEquipment,
                            backpackCandidate,
                            out _,
                            out string backpackMoveFailure))
                    {
                        simulatedBackpack = nextBackpack;
                        plan.FollowUps.Add(backpackCandidate);
                        LogOperationalMagazinePlanStep(backpackCandidate, "queued destination=BackpackCargo");
                        continue;
                    }

                    plan.AddRejection($"backpackMove:{backpackMoveFailure}");
                    LogOperationalMagazinePlanStep(backpackCandidate, $"reject backpackMove={backpackMoveFailure}");
                }
                else
                {
                    plan.AddRejection("backpackFit");
                    LogOperationalMagazinePlanStep(candidate, "reject backpackFit");
                }
            }

            return plan;
        }

        private bool TryBuildOperationalMagazineFastAccessMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.OperationalPockets)
            {
                return TryBuildOperationalMagazinePocketsMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason);
            }

            return TryBuildOperationalMagazineVestMove(
                inventory,
                followerEquipment,
                candidate,
                out move,
                out reason);
        }

        private bool TryBuildOperationalMagazineVestMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine))
            {
                reason = "invalidMagazine";
                return false;
            }

            if (!TryFindOperationalMagazineVestAddress(followerEquipment, magazine, out ItemAddress? address))
            {
                reason = "noVestAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    address,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo()))
            {
                reason = "vestMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private bool TryBuildOperationalMagazinePocketsMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine))
            {
                reason = "invalidMagazine";
                return false;
            }

            if (!TryFindOperationalMagazinePocketsAddress(followerEquipment, magazine, out ItemAddress? address))
            {
                reason = "noPocketsAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    address,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo()))
            {
                reason = "pocketsMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private bool TryBuildBackpackMagazineCargoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine))
            {
                reason = "invalidMagazine";
                return false;
            }

            if (!TryFindBackpackAddressForItem(followerEquipment, magazine, out ItemAddress? backpackAddress))
            {
                reason = "noBackpackAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    backpackAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo()))
            {
                reason = "backpackMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private static Item? GetWeaponReloadReserveMagazine(Weapon weapon)
        {
            try
            {
                // Only a magazine currently seated in the weapon needs a landing space during
                // reload. If the weapon has no mag inserted, the first spare can be loaded without
                // ejecting anything back into the vest.
                return weapon?.GetCurrentMagazine();
            }
            catch
            {
                return null;
            }
        }

        private static SearchableItemItemClass? CloneSearchableContainer(Item item)
        {
            try
            {
                Item clone = item?.CloneItem();
                if (clone != null)
                {
                    clone.CurrentAddress = null;
                }

                return clone as SearchableItemItemClass;
            }
            catch
            {
                return null;
            }
        }

        private static Item? ClonePlanningItem(Item item)
        {
            try
            {
                Item clone = item?.CloneItem();
                if (clone != null)
                {
                    clone.CurrentAddress = null;
                }

                return clone;
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySimulateFastAccessAddWithReserve(
            SearchableItemItemClass vest,
            SearchableItemItemClass pockets,
            Item item,
            Item? reserveItem,
            out SearchableItemItemClass? nextVest,
            out SearchableItemItemClass? nextPockets,
            out BodyGearFollowUpDestination destination)
        {
            nextVest = vest;
            nextPockets = pockets;
            destination = BodyGearFollowUpDestination.Default;

            if (vest != null &&
                TrySimulateContainerAdd(vest, item, out SearchableItemItemClass? trialVest) &&
                CanFitFastAccessReserve(trialVest, pockets, reserveItem))
            {
                nextVest = trialVest;
                destination = BodyGearFollowUpDestination.OperationalVest;
                return true;
            }

            if (pockets != null &&
                TrySimulateContainerAdd(pockets, item, out SearchableItemItemClass? trialPockets) &&
                CanFitFastAccessReserve(vest, trialPockets, reserveItem))
            {
                nextPockets = trialPockets;
                destination = BodyGearFollowUpDestination.OperationalPockets;
                return true;
            }

            return false;
        }

        private static bool CanFitFastAccessReserve(
            SearchableItemItemClass vest,
            SearchableItemItemClass pockets,
            Item? reserveItem)
        {
            if (reserveItem == null)
            {
                return true;
            }

            return (vest != null && TrySimulateContainerAdd(vest, reserveItem, out _)) ||
                   (pockets != null && TrySimulateContainerAdd(pockets, reserveItem, out _));
        }

        private static bool TrySimulateContainerAdd(
            SearchableItemItemClass container,
            Item item,
            out SearchableItemItemClass? nextContainer)
        {
            nextContainer = CloneSearchableContainer(container);
            if (nextContainer?.Grids == null || item == null)
            {
                nextContainer = null;
                return false;
            }

            foreach (StashGridClass grid in nextContainer.Grids)
            {
                Item planningItem = ClonePlanningItem(item);
                if (planningItem != null &&
                    grid?.AddAnywhere(planningItem, EErrorHandlingType.Ignore).Succeeded == true)
                {
                    return true;
                }
            }

            nextContainer = null;
            return false;
        }

        private bool TryGetOperationalMagazineCandidate(
            BodyGearCandidate candidate,
            out MagazineItemClass? magazine)
        {
            return TryGetOperationalMagazineCandidate(candidate, out magazine, out _);
        }

        private bool TryGetOperationalMagazineCandidate(
            BodyGearCandidate candidate,
            out MagazineItemClass? magazine,
            out string reason)
        {
            magazine = null;
            reason = "ok";

            if (candidate?.Item is not MagazineItemClass candidateMagazine)
            {
                reason = "notMagazine";
                return false;
            }

            if (string.IsNullOrEmpty(candidateMagazine.Id))
            {
                reason = "missingId";
                return false;
            }

            if (candidateMagazine.Count <= 0)
            {
                reason = "empty";
                return false;
            }

            if (IsLootNowInBotInventory(BotOwner?.GetPlayer, candidateMagazine) &&
                !IsFollowerBackpackOperationalCandidate(candidate))
            {
                reason = "alreadyInBotInventory";
                return false;
            }

            if (IsFollowerBackpackOperationalCandidate(candidate) &&
                InteractableObjects.IsStrictCargoItem(BotOwner, candidateMagazine))
            {
                reason = "strictCargo";
                return false;
            }

            if (InteractableObjects.IsProtectedFollowerEquipment(candidateMagazine))
            {
                reason = "protectedFollowerEquipment";
                return false;
            }

            magazine = candidateMagazine;
            return true;
        }

        private static bool IsFollowerBackpackOperationalCandidate(BodyGearCandidate candidate)
        {
            return string.Equals(
                candidate?.SourceName,
                "FollowerBackpack.WeaponSupportMagazine",
                StringComparison.Ordinal);
        }

        private static bool IsMagazineCompatibleWithWeapon(Weapon weapon, MagazineItemClass magazine)
        {
            if (weapon == null || magazine == null)
            {
                return false;
            }

            try
            {
                MagazineItemClass currentMagazine = weapon.GetCurrentMagazine();
                if (currentMagazine != null &&
                    string.Equals(currentMagazine.TemplateId, magazine.TemplateId, StringComparison.Ordinal))
                {
                    return true;
                }

                Slot magazineSlot = weapon.GetMagazineSlot();
                return magazineSlot != null && magazineSlot.CanAccept(magazine);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindOperationalMagazineVestAddress(
            InventoryEquipment followerEquipment,
            Item magazine,
            out ItemAddress? address)
        {
            return TryFindOperationalMagazineAddress(
                followerEquipment,
                EquipmentSlot.TacticalVest,
                magazine,
                out address);
        }

        private static bool TryFindOperationalMagazinePocketsAddress(
            InventoryEquipment followerEquipment,
            Item magazine,
            out ItemAddress? address)
        {
            return TryFindOperationalMagazineAddress(
                followerEquipment,
                EquipmentSlot.Pockets,
                magazine,
                out address);
        }

        private static bool TryFindOperationalMagazineAddress(
            InventoryEquipment followerEquipment,
            EquipmentSlot equipmentSlot,
            Item magazine,
            out ItemAddress? address)
        {
            address = null;
            Item fastAccessRoot = followerEquipment?.GetSlot(equipmentSlot)?.ContainedItem;
            if (fastAccessRoot is not SearchableItemItemClass searchable)
            {
                return false;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
            {
                if (container != null &&
                    container.TryFindLocationForItem(magazine, out ItemAddress candidateAddress) &&
                    !object.Equals(magazine.Parent, candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private void LogOperationalMagazineScan(OperationalMagazineScanStats stats)
        {
            if (stats == null)
            {
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Scan for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={stats.SourceName} root={stats.RootDescription} weapon={stats.WeaponDescription} " +
                $"items={stats.TreeItemsScanned} magsSeen={stats.MagazinesSeen} compatible={stats.CompatibleCount} " +
                $"rejects={stats.RejectionSummary}");

            if (stats.AcceptedSamples.Count > 0)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Scan accepted source={stats.SourceName}: {string.Join(" | ", stats.AcceptedSamples)}");
            }

            if (stats.RejectedSamples.Count > 0)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Scan rejected source={stats.SourceName}: {string.Join(" | ", stats.RejectedSamples)}");
            }
        }

        private void LogOperationalMagazinePlanStep(BodyGearCandidate candidate, string message)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Plan step for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"{message} source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                $"item={DescribeLootDebugItem(candidate?.Item)}");
        }

        private static string DescribeOperationalWeapon(Weapon weapon)
        {
            MagazineItemClass currentMagazine = null;
            try
            {
                currentMagazine = weapon?.GetCurrentMagazine();
            }
            catch
            {
                currentMagazine = null;
            }

            return $"{DescribeLootDebugItem(weapon)} currentMag={DescribeLootDebugItem(currentMagazine)}";
        }

        private static string DescribeLootDebugItem(Item item)
        {
            if (item == null)
            {
                return "none";
            }

            string size = "?";
            try
            {
                XYCellSizeStruct cellSize = item.CalculateCellSize();
                size = $"{cellSize.X}x{cellSize.Y}";
            }
            catch
            {
                size = "?";
            }

            string count = item is MagazineItemClass magazine
                ? $" count={magazine.Count}/{magazine.MaxCount}"
                : string.Empty;

            string templateId = item.TemplateId.ToString();
            if (string.IsNullOrEmpty(templateId))
            {
                templateId = "unknown";
            }

            return $"{item.GetType().Name}:{templateId} id={ShortLootId(item.Id)} size={size}{count}";
        }

        private static GStruct155 SafeCheckAction(Item item, ItemAddress address)
        {
            try
            {
                return item?.CheckAction(address) ?? default;
            }
            catch (Exception ex)
            {
                return new GClass1522(ex.Message);
            }
        }

        private static GStruct156<bool> SafeCanBeMoved(GInterface409 item, EFT.InventoryLogic.IContainer container)
        {
            try
            {
                if (item == null || container == null)
                {
                    return new GClass1522(item == null ? "itemMissing" : "containerMissing");
                }

                return item.CanBeMoved(container);
            }
            catch (Exception ex)
            {
                return new GClass1522(ex.Message);
            }
        }

        private static string DescribeInventoryEventResult(GStruct155 result)
        {
            return result.Failed ? $"failed:{DescribeInventoryError(result.Error)}" : "ok";
        }

        private static string DescribeInventoryEventResult(GStruct156<bool> result)
        {
            return result.Failed ? $"failed:{DescribeInventoryError(result.Error)}" : $"ok:{result.Value}";
        }

        private static string DescribeInventoryError(Error error)
        {
            return error?.ToString() ?? "none";
        }

        private static string DescribeLootAddress(ItemAddress address)
        {
            if (address == null)
            {
                return "none";
            }

            try
            {
                EFT.InventoryLogic.IContainer container = address.Container;
                return $"{address.GetType().Name}:container={container?.ID ?? "none"} parent={DescribeLootDebugItem(container?.ParentItem)}";
            }
            catch (Exception ex)
            {
                return $"error:{ex.Message}";
            }
        }

        private static string DescribeLootOwner(ItemAddress address)
        {
            if (address == null)
            {
                return "none";
            }

            try
            {
                IItemOwner owner = address.GetOwner();
                return owner == null
                    ? "none"
                    : $"{owner.GetType().Name}:name={owner.ContainerName ?? "unknown"} type={owner.OwnerType}";
            }
            catch (Exception ex)
            {
                return $"error:{ex.Message}";
            }
        }

        private static string ShortLootId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "none";
            }

            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        private sealed class OperationalMagazinePlan
        {
            public List<BodyGearCandidate> FollowUps { get; } = new List<BodyGearCandidate>();
            public List<BodyGearCandidate> CompatibleLoadedCandidates { get; } = new List<BodyGearCandidate>();
            public int OperationalVestCount { get; set; }
            public int OperationalPocketsCount { get; set; }
            public int OperationalFastAccessCount => OperationalVestCount + OperationalPocketsCount;
            public int ScannedCount { get; set; }
            public int ValidLoadedCount { get; set; }
            public List<string> RejectionReasons { get; } = new List<string>();

            public void AddRejection(string reason)
            {
                if (RejectionReasons.Count >= 5)
                {
                    return;
                }

                RejectionReasons.Add(reason);
            }
        }

        private sealed class OperationalMagazineScanStats
        {
            private readonly Dictionary<string, int> rejectionCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            public OperationalMagazineScanStats(string sourceName, Item root, Weapon weapon)
            {
                SourceName = sourceName ?? "unknown";
                RootDescription = DescribeLootDebugItem(root);
                WeaponDescription = DescribeOperationalWeapon(weapon);
            }

            public string SourceName { get; }
            public string RootDescription { get; }
            public string WeaponDescription { get; }
            public int TreeItemsScanned { get; set; }
            public int MagazinesSeen { get; set; }
            public int CompatibleCount { get; set; }
            public List<string> AcceptedSamples { get; } = new List<string>();
            public List<string> RejectedSamples { get; } = new List<string>();

            public string RejectionSummary
            {
                get
                {
                    if (rejectionCounts.Count == 0)
                    {
                        return "none";
                    }

                    return string.Join(",", rejectionCounts.Select(pair => $"{pair.Key}={pair.Value}"));
                }
            }

            public void AddAcceptedMagazine(string description)
            {
                if (AcceptedSamples.Count < 6)
                {
                    AcceptedSamples.Add(description);
                }
            }

            public void AddRejectedMagazine(string description, string reason)
            {
                if (!rejectionCounts.ContainsKey(reason))
                {
                    rejectionCounts[reason] = 0;
                }

                rejectionCounts[reason]++;
                if (RejectedSamples.Count < 8)
                {
                    RejectedSamples.Add($"{reason}:{description}");
                }
            }
        }

        private static bool TryFindBodyGearEquipmentSlot(
            InventoryEquipment equipment,
            BodyGearCandidate candidate,
            out ItemAddress? address)
        {
            address = null;

            if (candidate.Item is BackpackItemClass)
            {
                return false;
            }

            foreach (EquipmentSlot slotName in GetBodyGearEquipmentSlotOrder(candidate))
            {
                Slot slot = equipment.GetSlot(slotName);
                if (slot == null || slot.Deleted || slot.ContainedItem != null)
                {
                    continue;
                }

                Error error;
                ItemAddress candidateAddress = slot.FindLocationForItem(candidate.Item, out error);
                if (candidateAddress != null)
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

    }
}
