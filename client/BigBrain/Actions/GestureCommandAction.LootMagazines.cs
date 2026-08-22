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
        private IEnumerable<BodyGearCandidate> GetBodyOperationalMagazineCandidates(
            InventoryEquipment corpseEquipment,
            Weapon weapon,
            bool includeEmptyForTopOff = false)
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
                foreach (EFT.InventoryLogic.Magazine magazine in GetOperationalMagazineItems(
                             root,
                             weapon,
                             $"{slot}.WeaponSupportMagazine",
                             includeEmptyForTopOff))
                {
                    yield return new BodyGearCandidate(
                        magazine,
                        null,
                        $"{slot}.WeaponSupportMagazine",
                        0,
                        bypassPriceThreshold: true,
                        bypassCategoryFilter: true,
                        weaponSupportWeapon: weapon);
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerOperationalMagazineCandidates(
            EFT.InventoryLogic.SearchableItem containerRoot,
            Weapon weapon,
            bool includeEmptyForTopOff = false)
        {
            foreach (EFT.InventoryLogic.Magazine magazine in GetOperationalMagazineItems(
                         containerRoot,
                         weapon,
                         "Container.WeaponSupportMagazine",
                         includeEmptyForTopOff))
            {
                yield return new BodyGearCandidate(
                    magazine,
                    null,
                    "Container.WeaponSupportMagazine",
                    0,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true,
                    weaponSupportWeapon: weapon);
            }
        }

        private IEnumerable<BodyGearCandidate> GetFollowerBackpackOperationalMagazineCandidates(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            bool includeStrictCargo = false,
            bool includeEmptyForTopOff = false)
        {
            Item backpack = followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            foreach (EFT.InventoryLogic.Magazine magazine in GetOperationalMagazineItems(
                         backpack,
                         weapon,
                         "FollowerBackpack.WeaponSupportMagazine",
                         includeEmptyForTopOff))
            {
                if (!includeStrictCargo &&
                    InteractableObjects.IsStrictCargoItem(BotOwner, magazine))
                {
                    continue;
                }

                yield return new BodyGearCandidate(
                    magazine,
                    null,
                    "FollowerBackpack.WeaponSupportMagazine",
                    0,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true,
                    weaponSupportWeapon: weapon);
            }
        }

        private IEnumerable<EFT.InventoryLogic.Magazine> GetOperationalMagazineItems(
            Item root,
            Weapon weapon,
            string sourceName,
            bool includeEmptyForTopOff = false)
        {
#if DEBUG
            OperationalMagazineScanStats stats = new OperationalMagazineScanStats(sourceName, root, weapon);
#endif
            if (root == null || weapon == null)
            {
#if DEBUG
                stats.AddRejectedMagazine(DescribeLootDebugItem(root), root == null ? "rootMissing" : "weaponMissing");
                LogOperationalMagazineScan(stats);
#endif
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
#if DEBUG
            stats.TreeItemsScanned = snapshot.Count;
#endif
            foreach (Item item in snapshot)
            {
                if (item is not EFT.InventoryLogic.Magazine magazine)
                {
                    continue;
                }

#if DEBUG
                stats.MagazinesSeen++;
                string magazineDescription = DescribeLootDebugItem(magazine);
#endif
                if (string.IsNullOrEmpty(magazine.Id))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "missingId");
#endif
                    continue;
                }

                if (weaponTreeItemIds.Contains(magazine.Id))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "inWeaponTree");
#endif
                    continue;
                }

                if (string.Equals(currentMagazineId, magazine.Id, StringComparison.Ordinal))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "currentWeaponMag");
#endif
                    continue;
                }

                if (IsMagazineInstalledInWeapon(magazine))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "installedInWeapon");
#endif
                    continue;
                }

                if (magazine.Count <= 0 && !includeEmptyForTopOff)
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "empty");
#endif
                    continue;
                }

                if (IsItemInsideRoot(magazine, weapon))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "insideWeapon");
#endif
                    continue;
                }

                if (!IsMagazineCompatibleWithWeapon(weapon, magazine))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "incompatibleMagazine");
#endif
                    continue;
                }

                if (magazine.Count > 0 &&
                    !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine))
                {
#if DEBUG
                    stats.AddRejectedMagazine(magazineDescription, "incompatibleCartridge");
#endif
                    continue;
                }

                if (magazine.Count <= 0 && includeEmptyForTopOff && magazine.MaxCount > 0)
                {
#if DEBUG
                    stats.CompatibleCount++;
                    stats.AddAcceptedMagazine($"{magazineDescription}:refillableEmpty");
#endif
                    yield return magazine;
                    continue;
                }

                if (FollowerWeaponMagazineCompatibility.IsOperational(weapon, magazine))
                {
#if DEBUG
                    stats.CompatibleCount++;
                    stats.AddAcceptedMagazine(magazineDescription);
#endif
                    yield return magazine;
                    continue;
                }

#if DEBUG
                stats.AddRejectedMagazine(magazineDescription, "notOperational");
#endif
            }

#if DEBUG
            LogOperationalMagazineScan(stats);
#endif
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
            IEnumerable<BodyGearCandidate>? candidates,
            bool allowEmptyCandidates = false,
            Func<EFT.InventoryLogic.Magazine, bool>? existingFastAccessMagazineEligibility = null,
            IEnumerable<EFT.InventoryLogic.Magazine>? alternateReloadReserveItems = null)
        {
            OperationalMagazinePlan plan = new OperationalMagazinePlan();
            if (inventory == null || followerEquipment == null || weapon == null || candidates == null)
            {
                return plan;
            }

            List<BodyGearCandidate> candidateList = candidates.ToList();
            plan.ScannedCount = candidateList.Count;
            EFT.InventoryLogic.SearchableItem simulatedVest = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
            EFT.InventoryLogic.SearchableItem simulatedPockets = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem);
            EFT.InventoryLogic.SearchableItem simulatedBackpack = CloneSearchableContainer(
                followerEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem);
            List<EFT.InventoryLogic.Magazine> alternateReloadReserves = NormalizeReloadReserveItems(
                alternateReloadReserveItems);
            HashSet<string> consideredItemIds = new HashSet<string>(StringComparer.Ordinal);
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Plan start for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={DescribeOperationalWeapon(weapon)} candidates={candidateList.Count} " +
                $"vest={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem)} " +
                $"pockets={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem)} " +
                $"backpack={DescribeLootDebugItem(followerEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem)}");

            List<BodyGearCandidate> validCandidates = new List<BodyGearCandidate>();
            foreach (BodyGearCandidate candidate in candidateList)
            {
                if (!TryGetOperationalMagazineCandidate(
                        candidate,
                        out EFT.InventoryLogic.Magazine? magazine,
                        out string validationReason,
                        allowEmptyCandidates))
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
                validCandidates.Add(candidate);
                LogOperationalMagazinePlanStep(candidate, $"consider mag={DescribeLootDebugItem(magazine)}");
            }

            List<OperationalMagazineReserveOption> reserveOptions = validCandidates
                .Select(candidate => new OperationalMagazineReserveOption(
                    (EFT.InventoryLogic.Magazine)candidate.Item,
                    candidate))
                .Concat(GetFastAccessMagazines(followerEquipment)
                    .Where(magazine =>
                        (allowEmptyCandidates || magazine.Count > 0) &&
                        magazine.MaxCount > 0 &&
                        (existingFastAccessMagazineEligibility == null ||
                         existingFastAccessMagazineEligibility(magazine)) &&
                        IsMagazineCompatibleWithWeapon(weapon, magazine) &&
                        (magazine.Count <= 0 ||
                         FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine)))
                    .Select(magazine => new OperationalMagazineReserveOption(magazine, null)))
                .GroupBy(option => option.Magazine.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(option => GetMagazineCellArea(option.Magazine))
                .ThenByDescending(option => GetMagazineLongestSide(option.Magazine))
                .ThenByDescending(option => option.Magazine.Count)
                .ThenByDescending(option => option.Magazine.MaxCount)
                .ToList();

            BodyGearCandidate? reserveAnchorCandidate = null;
            foreach (OperationalMagazineReserveOption option in reserveOptions)
            {
                if (option.Candidate == null)
                {
                    // This magazine already occupies fast access. Only its matching landing slot
                    // must remain empty for a future reload.
                    if (CanFitFastAccessReserves(
                            simulatedVest,
                            simulatedPockets,
                            BuildReloadReserveSet(option.Magazine, alternateReloadReserves)))
                    {
                        plan.ReloadReserveMagazine = option.Magazine;
                        break;
                    }

                    continue;
                }

                if (!TrySimulateFastAccessAddWithReserves(
                        simulatedVest,
                        simulatedPockets,
                        option.Magazine,
                        BuildReloadReserveSet(option.Magazine, alternateReloadReserves),
                        out EFT.InventoryLogic.SearchableItem? anchorVest,
                        out EFT.InventoryLogic.SearchableItem? anchorPockets,
                        out BodyGearFollowUpDestination anchorDestination))
                {
                    continue;
                }

                BodyGearCandidate anchorCandidate = option.Candidate.WithFollowUpDestination(anchorDestination);
                if (!TryBuildOperationalMagazineFastAccessMove(
                        inventory,
                        followerEquipment,
                        anchorCandidate,
                        out _,
                        out _,
                        allowEmptyCandidates))
                {
                    continue;
                }

                simulatedVest = anchorVest;
                simulatedPockets = anchorPockets;
                plan.ReloadReserveMagazine = option.Magazine;
                reserveAnchorCandidate = option.Candidate;
                AddOperationalFastAccessFollowUp(plan, anchorCandidate, anchorDestination);
                break;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Reload reserve for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={DescribeOperationalWeapon(weapon)} reserve={DescribeLootDebugItem(plan.ReloadReserveMagazine)} " +
                $"anchor={(reserveAnchorCandidate == null ? "existingFastAccessOrNone" : DescribeLootDebugItem(reserveAnchorCandidate.Item))}");

            foreach (BodyGearCandidate candidate in validCandidates
                         .Where(candidate => !ReferenceEquals(candidate, reserveAnchorCandidate))
                         .OrderByDescending(candidate => GetMagazineCellArea((EFT.InventoryLogic.Magazine)candidate.Item))
                         .ThenByDescending(candidate => GetMagazineLongestSide((EFT.InventoryLogic.Magazine)candidate.Item))
                         .ThenByDescending(candidate => ((EFT.InventoryLogic.Magazine)candidate.Item).Count)
                         .ThenByDescending(candidate => ((EFT.InventoryLogic.Magazine)candidate.Item).MaxCount))
            {
                EFT.InventoryLogic.Magazine magazine = (EFT.InventoryLogic.Magazine)candidate.Item;
                bool placedInFastAccess = false;
                if (plan.ReloadReserveMagazine != null &&
                    TrySimulateFastAccessAddWithReserves(
                        simulatedVest,
                        simulatedPockets,
                        magazine,
                        BuildReloadReserveSet(plan.ReloadReserveMagazine, alternateReloadReserves),
                        out EFT.InventoryLogic.SearchableItem? nextVest,
                        out EFT.InventoryLogic.SearchableItem? nextPockets,
                        out BodyGearFollowUpDestination fastAccessDestination))
                {
                    BodyGearCandidate fastAccessCandidate = candidate.WithFollowUpDestination(fastAccessDestination);
                    if (TryBuildOperationalMagazineFastAccessMove(
                            inventory,
                            followerEquipment,
                            fastAccessCandidate,
                            out _,
                            out string fastAccessMoveFailure,
                            allowEmptyCandidates))
                    {
                        simulatedVest = nextVest;
                        simulatedPockets = nextPockets;
                        AddOperationalFastAccessFollowUp(plan, fastAccessCandidate, fastAccessDestination);
                        placedInFastAccess = true;
                    }
                    else
                    {
                        plan.AddRejection($"fastAccessMove:{fastAccessMoveFailure}");
                        LogOperationalMagazinePlanStep(fastAccessCandidate, $"reject fastAccessMove={fastAccessMoveFailure}");
                    }
                }
                else
                {
                    plan.AddRejection(plan.ReloadReserveMagazine == null
                        ? "reloadReserveUnavailable"
                        : "fastAccessFit");
                    LogOperationalMagazinePlanStep(candidate, plan.ReloadReserveMagazine == null
                        ? "reject reloadReserveUnavailable"
                        : "reject fastAccessFit");
                }

                if (placedInFastAccess)
                {
                    continue;
                }

                if (allowEmptyCandidates && magazine.Count <= 0)
                {
                    // Empty magazines participate only as prospective top-off targets. If their
                    // shape cannot satisfy fast-access plus reload reserve, do not turn them into
                    // ordinary backpack cargo during this temporary planning pass.
                    continue;
                }

                TryPlanOperationalMagazineBackpackFallback(
                    inventory,
                    followerEquipment,
                    candidate,
                    plan,
                    ref simulatedBackpack);
            }

            return plan;
        }

        private void AddOperationalFastAccessFollowUp(
            OperationalMagazinePlan plan,
            BodyGearCandidate candidate,
            BodyGearFollowUpDestination destination)
        {
            if (destination == BodyGearFollowUpDestination.OperationalVest)
            {
                plan.OperationalVestCount++;
            }
            else
            {
                plan.OperationalPocketsCount++;
            }

            plan.FollowUps.Add(candidate);
            LogOperationalMagazinePlanStep(candidate, $"queued destination={destination}");
        }

        private void TryPlanOperationalMagazineBackpackFallback(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            OperationalMagazinePlan plan,
            ref EFT.InventoryLogic.SearchableItem? simulatedBackpack)
        {
            EFT.InventoryLogic.Magazine magazine = candidate.Item as EFT.InventoryLogic.Magazine;
            if (IsLootNowInBotInventory(BotOwner?.GetPlayer, magazine))
            {
                // A loose follower-backpack magazine that cannot move to fast access is already
                // valid cargo and needs no additional transaction.
                plan.AddRejection("alreadyBackpackCargo");
                LogOperationalMagazinePlanStep(candidate, "retain destination=BackpackCargo");
                return;
            }

            if (simulatedBackpack != null &&
                TrySimulateContainerAdd(simulatedBackpack, magazine, out EFT.InventoryLogic.SearchableItem? nextBackpack))
            {
                BodyGearCandidate backpackCandidate = candidate.WithFollowUpDestination(
                    BodyGearFollowUpDestination.BackpackCargo);
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
                    return;
                }

                plan.AddRejection($"backpackMove:{backpackMoveFailure}");
                LogOperationalMagazinePlanStep(backpackCandidate, $"reject backpackMove={backpackMoveFailure}");
                return;
            }

            plan.AddRejection("backpackFit");
            LogOperationalMagazinePlanStep(candidate, "reject backpackFit");
        }

        private bool TryBuildOperationalMagazineFastAccessMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason,
            bool allowEmptyCandidate = false)
        {
            if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.OperationalPockets)
            {
                return TryBuildOperationalMagazinePocketsMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason,
                    allowEmptyCandidate);
            }

            return TryBuildOperationalMagazineVestMove(
                inventory,
                followerEquipment,
                candidate,
                out move,
                out reason,
                allowEmptyCandidate);
        }

        private bool TryBuildOperationalMagazineVestMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason,
            bool allowEmptyCandidate = false)
        {
            move = null;
            if (!TryGetOperationalMagazineCandidate(
                    candidate,
                    out EFT.InventoryLogic.Magazine? magazine,
                    out _,
                    allowEmptyCandidate))
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
            out string reason,
            bool allowEmptyCandidate = false)
        {
            move = null;
            if (!TryGetOperationalMagazineCandidate(
                    candidate,
                    out EFT.InventoryLogic.Magazine? magazine,
                    out _,
                    allowEmptyCandidate))
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
            if (!TryGetOperationalMagazineCandidate(candidate, out EFT.InventoryLogic.Magazine? magazine))
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

        private static EFT.InventoryLogic.SearchableItem? CloneSearchableContainer(Item item)
        {
            try
            {
                Item clone = item?.CloneItem();
                if (clone != null)
                {
                    clone.CurrentAddress = null;
                }

                return clone as EFT.InventoryLogic.SearchableItem;
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

        private static bool TrySimulateFastAccessAddWithReserves(
            EFT.InventoryLogic.SearchableItem vest,
            EFT.InventoryLogic.SearchableItem pockets,
            Item item,
            IEnumerable<EFT.InventoryLogic.Magazine> reserveItems,
            out EFT.InventoryLogic.SearchableItem? nextVest,
            out EFT.InventoryLogic.SearchableItem? nextPockets,
            out BodyGearFollowUpDestination destination)
        {
            nextVest = vest;
            nextPockets = pockets;
            destination = BodyGearFollowUpDestination.Default;

            if (vest != null &&
                TrySimulateFastAccessContainerAdd(vest, item, out EFT.InventoryLogic.SearchableItem? trialVest) &&
                CanFitFastAccessReserves(trialVest, pockets, reserveItems))
            {
                nextVest = trialVest;
                destination = BodyGearFollowUpDestination.OperationalVest;
                return true;
            }

            if (pockets != null &&
                TrySimulateFastAccessContainerAdd(pockets, item, out EFT.InventoryLogic.SearchableItem? trialPockets) &&
                CanFitFastAccessReserves(vest, trialPockets, reserveItems))
            {
                nextPockets = trialPockets;
                destination = BodyGearFollowUpDestination.OperationalPockets;
                return true;
            }

            return false;
        }

        private static bool CanFitFastAccessReserves(
            EFT.InventoryLogic.SearchableItem vest,
            EFT.InventoryLogic.SearchableItem pockets,
            IEnumerable<EFT.InventoryLogic.Magazine> reserveItems)
        {
            List<EFT.InventoryLogic.Magazine> reserves = NormalizeReloadReserveItems(reserveItems)
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ToList();
            return TryFitFastAccessReloadReserves(vest, pockets, reserves, 0);
        }

        private static bool TryFitFastAccessReloadReserves(
            EFT.InventoryLogic.SearchableItem vest,
            EFT.InventoryLogic.SearchableItem pockets,
            IReadOnlyList<EFT.InventoryLogic.Magazine> reserves,
            int index)
        {
            if (index >= reserves.Count)
            {
                return true;
            }

            EFT.InventoryLogic.Magazine reserve = reserves[index];
            if (vest != null &&
                TrySimulateFastAccessContainerAdd(vest, reserve, out EFT.InventoryLogic.SearchableItem? nextVest) &&
                TryFitFastAccessReloadReserves(nextVest, pockets, reserves, index + 1))
            {
                return true;
            }

            return pockets != null &&
                   TrySimulateFastAccessContainerAdd(pockets, reserve, out EFT.InventoryLogic.SearchableItem? nextPockets) &&
                   TryFitFastAccessReloadReserves(vest, nextPockets, reserves, index + 1);
        }

        private static List<EFT.InventoryLogic.Magazine> BuildReloadReserveSet(
            EFT.InventoryLogic.Magazine reloadReserve,
            IEnumerable<EFT.InventoryLogic.Magazine> alternateReloadReserves)
        {
            // The follower reloads only one weapon at a time. Preserve one shared opening sized
            // for the largest relevant magazine instead of reserving simultaneous landing spaces.
            return NormalizeReloadReserveItems(
                new[] { reloadReserve }.Concat(
                    alternateReloadReserves ?? Enumerable.Empty<EFT.InventoryLogic.Magazine>()))
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(magazine => magazine.MaxCount)
                .Take(1)
                .ToList();
        }

        private static List<EFT.InventoryLogic.Magazine> NormalizeReloadReserveItems(
            IEnumerable<EFT.InventoryLogic.Magazine> items)
        {
            return items?
                .Where(item => item != null && !string.IsNullOrEmpty(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<EFT.InventoryLogic.Magazine>();
        }

        private bool HasOperationalMagazineReloadLandingSpace(
            InventoryEquipment equipment,
            Weapon weapon)
        {
            if (FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(equipment, weapon))
            {
                return true;
            }

            // An oversized inserted magazine may be impossible for this rig and will be dropped
            // by vanilla on the first reload. In that case the usable cycle is defined by the
            // largest compatible fast-access magazine for which a matching landing slot remains.
            return GetFastAccessMagazines(equipment)
                .Where(magazine =>
                    magazine.Count > 0 &&
                    IsMagazineCompatibleWithWeapon(weapon, magazine) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine))
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(magazine => magazine.MaxCount)
                .Any(magazine =>
                    FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(equipment, magazine));
        }

        private static bool TrySimulateContainerAdd(
            EFT.InventoryLogic.SearchableItem container,
            Item item,
            out EFT.InventoryLogic.SearchableItem? nextContainer)
        {
            nextContainer = CloneSearchableContainer(container);
            if (nextContainer?.Grids == null || item == null)
            {
                nextContainer = null;
                return false;
            }

            foreach (EFT.InventoryLogic.Grid grid in nextContainer.Grids)
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

        private static bool TrySimulateFastAccessContainerAdd(
            EFT.InventoryLogic.SearchableItem container,
            Item item,
            out EFT.InventoryLogic.SearchableItem? nextContainer)
        {
            nextContainer = CloneSearchableContainer(container);
            if (nextContainer?.Grids == null || item == null)
            {
                nextContainer = null;
                return false;
            }

            foreach (EFT.InventoryLogic.Grid grid in OrderFastAccessGridsByBestFit(nextContainer.Grids, item))
            {
                Item planningItem = ClonePlanningItem(item);
                if (planningItem != null &&
                    grid.AddAnywhere(planningItem, EErrorHandlingType.Ignore).Succeeded == true)
                {
                    return true;
                }
            }

            nextContainer = null;
            return false;
        }

        private static IEnumerable<EFT.InventoryLogic.Grid> OrderFastAccessGridsByBestFit(
            IEnumerable<EFT.InventoryLogic.Grid> grids,
            Item item)
        {
            int itemArea = 0;
            try
            {
                IntVec2 size = item?.CalculateCellSize() ?? default;
                itemArea = Math.Max(0, size.X * size.Y);
            }
            catch
            {
                // Stable grid order remains the fallback when a modded item cannot report size.
            }

            return (grids ?? Enumerable.Empty<EFT.InventoryLogic.Grid>())
                .Where(grid => grid != null)
                .OrderBy(grid => Math.Max(0, grid.GridWidth * grid.GridHeight - itemArea))
                .ThenBy(grid => grid.GridWidth * grid.GridHeight);
        }

        private bool TryGetOperationalMagazineCandidate(
            BodyGearCandidate candidate,
            out EFT.InventoryLogic.Magazine? magazine)
        {
            return TryGetOperationalMagazineCandidate(candidate, out magazine, out _, allowEmptyCandidate: false);
        }

        private bool TryGetOperationalMagazineCandidate(
            BodyGearCandidate candidate,
            out EFT.InventoryLogic.Magazine? magazine,
            out string reason,
            bool allowEmptyCandidate = false)
        {
            magazine = null;
            reason = "ok";

            if (candidate?.Item is not EFT.InventoryLogic.Magazine candidateMagazine)
            {
                reason = "notMagazine";
                return false;
            }

            if (string.IsNullOrEmpty(candidateMagazine.Id))
            {
                reason = "missingId";
                return false;
            }

            if (candidateMagazine.Count <= 0 && !allowEmptyCandidate)
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

        private static bool IsMagazineCompatibleWithWeapon(Weapon weapon, EFT.InventoryLogic.Magazine magazine)
        {
            return FollowerWeaponMagazineCompatibility.IsMechanicallyCompatible(weapon, magazine);
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
            if (fastAccessRoot is not EFT.InventoryLogic.SearchableItem searchable)
            {
                return false;
            }

            HashSet<EFT.InventoryLogic.IContainer> visited = new HashSet<EFT.InventoryLogic.IContainer>();
            foreach (EFT.InventoryLogic.Grid grid in OrderFastAccessGridsByBestFit(searchable.Grids, magazine))
            {
                visited.Add(grid);
                if (grid.TryFindLocationForItem(magazine, out ItemAddress candidateAddress) &&
                    !object.Equals(magazine.Parent, candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
            {
                if (container != null &&
                    visited.Add(container) &&
                    container.TryFindLocationForItem(magazine, out ItemAddress candidateAddress) &&
                    !object.Equals(magazine.Parent, candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
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

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogOperationalMagazinePlanStep(BodyGearCandidate candidate, string message)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Plan step for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"{message} source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                $"item={DescribeLootDebugItem(candidate?.Item)}");
        }

        private static string DescribeOperationalWeapon(Weapon weapon)
        {
            EFT.InventoryLogic.Magazine currentMagazine = null;
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
                IntVec2 cellSize = item.CalculateCellSize();
                size = $"{cellSize.X}x{cellSize.Y}";
            }
            catch
            {
                size = "?";
            }

            string count = item is EFT.InventoryLogic.Magazine magazine
                ? $" count={magazine.Count}/{magazine.MaxCount}"
                : string.Empty;

            string templateId = item.TemplateId.ToString();
            if (string.IsNullOrEmpty(templateId))
            {
                templateId = "unknown";
            }

            return $"{item.GetType().Name}:{templateId} id={ShortLootId(item.Id)} size={size}{count}";
        }

        private static Diz.LanguageExtensions.Option SafeCheckAction(Item item, ItemAddress address)
        {
            try
            {
                return item?.CheckAction(address) ?? default;
            }
            catch (Exception ex)
            {
                return new Diz.LanguageExtensions.StringError(ex.Message);
            }
        }

        private static Diz.LanguageExtensions.Option<bool> SafeCanBeMoved(EFT.InventoryLogic.IMoveCheckable item, EFT.InventoryLogic.IContainer container)
        {
            try
            {
                if (item == null || container == null)
                {
                    return new Diz.LanguageExtensions.StringError(item == null ? "itemMissing" : "containerMissing");
                }

                return item.CanBeMoved(container);
            }
            catch (Exception ex)
            {
                return new Diz.LanguageExtensions.StringError(ex.Message);
            }
        }

        private static string DescribeInventoryEventResult(Diz.LanguageExtensions.Option result)
        {
            return result.Failed ? $"failed:{DescribeInventoryError(result.Error)}" : "ok";
        }

        private static string DescribeInventoryEventResult(Diz.LanguageExtensions.Option<bool> result)
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
            public EFT.InventoryLogic.Magazine? ReloadReserveMagazine { get; set; }
            public int OperationalVestCount { get; set; }
            public int OperationalPocketsCount { get; set; }
            public int OperationalFastAccessCount => OperationalVestCount + OperationalPocketsCount;
            public int ScannedCount { get; set; }
            public int ValidLoadedCount { get; set; }
            private List<string>? rejectionReasons;
            public List<string> RejectionReasons => rejectionReasons ??= new List<string>();

            [System.Diagnostics.Conditional("DEBUG")]
            public void AddRejection(string reason)
            {
                if (RejectionReasons.Count >= 5)
                {
                    return;
                }

                RejectionReasons.Add(reason);
            }
        }

        private sealed class OperationalMagazineReserveOption
        {
            public OperationalMagazineReserveOption(
                EFT.InventoryLogic.Magazine magazine,
                BodyGearCandidate? candidate)
            {
                Magazine = magazine;
                Candidate = candidate;
            }

            public EFT.InventoryLogic.Magazine Magazine { get; }
            public BodyGearCandidate? Candidate { get; }
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

            if (candidate.Item is EFT.InventoryLogic.Backpack)
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
