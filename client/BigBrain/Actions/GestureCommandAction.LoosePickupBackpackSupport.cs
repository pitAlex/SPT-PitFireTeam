using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private Weapon? loosePickupBackpackSupportWeapon;
        private bool loosePickupBackpackSupportCompleted;
        private bool loosePickupBackpackSupportMoveInProgress;
        private readonly Queue<BodyGearCandidate> pendingLoosePickupBackpackSupportFollowUps =
            new Queue<BodyGearCandidate>();
        private readonly HashSet<string> loosePickupBackpackSupportRejectedItemIds =
            new HashSet<string>(StringComparer.Ordinal);

        private bool TryPrepareLoosePickupWeaponFromBackpack(
            Weapon weapon,
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            if (weapon == null ||
                inventory == null ||
                followerEquipment == null ||
                weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine ||
                followerEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null ||
                followerEquipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem != null)
            {
                return false;
            }

            if (!IsSameLootItem(loosePickupBackpackSupportWeapon, weapon))
            {
                ResetLoosePickupBackpackSupport();
                loosePickupBackpackSupportWeapon = weapon;
                AdoptCompatibleLoosePickupBackpackSupport(followerEquipment, weapon);
            }

            if (loosePickupBackpackSupportCompleted)
            {
                return false;
            }

            if (loosePickupBackpackSupportMoveInProgress)
            {
                return true;
            }

            if (TryStartPendingLoosePickupBackpackSupportFollowUp(inventory, followerEquipment))
            {
                return true;
            }

            Item backpack = followerEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack == null)
            {
                loosePickupBackpackSupportCompleted = true;
                return false;
            }

            List<BodyGearCandidate> refillableMagazines =
                GetFollowerBackpackOperationalMagazineCandidates(
                        followerEquipment,
                        weapon,
                        includeStrictCargo: true,
                        includeEmptyForTopOff: true)
                    .Where(candidate => !WasLoosePickupBackpackSupportRejected(candidate?.Item))
                    .ToList();
            List<BodyGearCandidate> looseAmmo = GetWeaponLooseAmmoItems(backpack, weapon)
                .Where(ammo => !WasLoosePickupBackpackSupportRejected(ammo))
                .Select(ammo => CreateWeaponLooseAmmoCandidate(
                    ammo,
                    weapon,
                    "LoosePickup.Backpack.WeaponLooseAmmo"))
                .ToList();

            // Reuse the body/container magazine maintenance policy before moving the weapon.
            // Every operation settles before this method scans again, so the final pickup
            // destination is based only on the follower's live magazine and ammunition state.
            if (TryBuildPrimaryMagazineTopOffStagingMove(
                    inventory,
                    followerEquipment,
                    backpack,
                    weapon,
                    refillableMagazines,
                    looseAmmo,
                    out BodyGearMove? topOffMove))
            {
                StartLoosePickupBackpackSupportMove(inventory, topOffMove);
                return true;
            }

            List<BodyGearCandidate> loadedMagazines =
                GetFollowerBackpackOperationalMagazineCandidates(
                        followerEquipment,
                        weapon,
                        includeStrictCargo: true)
                    .Where(candidate => !WasLoosePickupBackpackSupportRejected(candidate?.Item))
                    .ToList();
            OperationalMagazinePlan magazinePlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                loadedMagazines);

            if (!HasInsertedMagazine(weapon))
            {
                BodyGearCandidate weaponCandidate = new BodyGearCandidate(
                    weapon,
                    null,
                    "LoosePickup.Weapon",
                    0,
                    bypassPriceThreshold: true,
                    bypassCategoryFilter: true,
                    bypassBodyGearLootability: true,
                    weaponSupportWeapon: weapon);
                if (TryBuildEmptyWeaponMagazineInsertionMove(
                        inventory,
                        weaponCandidate,
                        magazinePlan,
                        out BodyGearMove? insertionMove,
                        out string insertionReason,
                        allowFollowerInventoryMagazine: true))
                {
                    StartLoosePickupBackpackSupportMove(inventory, insertionMove);
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} result=retainWithoutInsertedMagazine reason={insertionReason}");
            }
            else
            {
                foreach (BodyGearCandidate candidate in magazinePlan.FollowUps.Where(IsOperationalFastAccessFollowUp))
                {
                    if (WasLoosePickupBackpackSupportRejected(candidate?.Item))
                    {
                        continue;
                    }

                    if (TryBuildSupportMagazineFollowUpMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? magazineMove,
                            out string magazineReason))
                    {
                        StartLoosePickupBackpackSupportMove(inventory, magazineMove);
                        return true;
                    }

                    RememberLoosePickupBackpackSupportRejection(candidate?.Item);
                    Modules.Logger.LogInfo(
                        $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(weapon)} magazine={DescribeLootDebugItem(candidate?.Item)} " +
                        $"result=moveRejected reason={magazineReason}");
                }
            }

            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon,
                ammo => !InteractableObjects.IsStrictCargoItem(BotOwner, ammo));
            loosePickupBackpackSupportCompleted = true;
            Modules.Logger.LogInfo(
                $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} result=preparationComplete " +
                $"backpackMags={loadedMagazines.Count} looseAmmo={looseAmmo.Count} {readiness.ToDiagnosticString()}");
            return false;
        }

        private void AdoptCompatibleLoosePickupBackpackSupport(
            InventoryEquipment followerEquipment,
            Weapon weapon)
        {
            Item backpack = followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack == null)
            {
                return;
            }

            List<MagazineItemClass> magazines = GetOperationalMagazineItems(
                    backpack,
                    weapon,
                    "LoosePickup.Backpack.WeaponSupportMagazine",
                    includeEmptyForTopOff: true)
                .ToList();
            List<AmmoItemClass> looseAmmo = GetWeaponLooseAmmoItems(backpack, weapon).ToList();
            foreach (Item item in magazines.Cast<Item>().Concat(looseAmmo))
            {
                InteractableObjects.ClearStrictCargoTree(BotOwner, item);
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} result=adopted compatibleMags={magazines.Count} " +
                $"compatibleLooseAmmo={looseAmmo.Count}");
        }

        private bool TryStartPendingLoosePickupBackpackSupportFollowUp(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingLoosePickupBackpackSupportFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingLoosePickupBackpackSupportFollowUps.Dequeue();
                BodyGearMove? move = null;
                string reason = "unsupportedFollowUp";
                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.TopOffWeaponMagazine)
                {
                    TryBuildMagazineTopOffMove(inventory, candidate, out move, out reason);
                }
                else if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.RestoreMagazineToWeapon)
                {
                    TryBuildRestoreMagazineToWeaponMove(inventory, candidate, out move, out reason);
                }
                else if (candidate != null && IsOperationalFastAccessFollowUp(candidate))
                {
                    TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out move,
                        out reason);
                }

                if (move != null)
                {
                    StartLoosePickupBackpackSupportMove(inventory, move);
                    return true;
                }

                RememberLoosePickupBackpackSupportRejection(candidate?.Item);
                Modules.Logger.LogInfo(
                    $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(loosePickupBackpackSupportWeapon)} " +
                    $"followUp={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(candidate?.Item)} result=skipped reason={reason}");
            }

            return false;
        }

        private void StartLoosePickupBackpackSupportMove(
            InventoryController inventory,
            BodyGearMove move)
        {
            loosePickupBackpackSupportMoveInProgress = true;
            Modules.Logger.LogInfo(
                $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(loosePickupBackpackSupportWeapon)} result=moveStarting " +
                $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)}");
            RunBodyGearMoveTransaction(
                inventory,
                move,
                new Callback(result => CompleteLoosePickupBackpackSupportMove(result, move)));
        }

        private void CompleteLoosePickupBackpackSupportMove(IResult result, BodyGearMove move)
        {
            loosePickupBackpackSupportMoveInProgress = false;
            bool stagingApplied = move?.IsStagingOperation == true &&
                                  move.StagingWeapon != null &&
                                  (IsItemInsideRoot(move.Item, move.StagingWeapon) ||
                                   (move.StagingWeaponLoadedRoundsBefore >= 0 &&
                                    FollowerWeaponLooseFeedReadiness.GetLoadedRounds(move.StagingWeapon) >
                                    move.StagingWeaponLoadedRoundsBefore) ||
                                   (move.StagingMagazine != null &&
                                    move.StagingMagazineRoundsBefore >= 0 &&
                                    move.StagingMagazine.Count > move.StagingMagazineRoundsBefore));
            bool failedTopOffDetach = IsInsertedMagazineTopOffDetachMove(move) &&
                                      !DidInsertedMagazineTopOffDetachSettle(move);
            bool succeeded = failedTopOffDetach
                ? false
                : result?.Succeed == true || stagingApplied;
            if (succeeded)
            {
                InteractableObjects.ClearStrictCargoTree(BotOwner, move.Item);
                if (move.Item is MagazineItemClass magazine &&
                    move.ApprovedReloadWeapon != null)
                {
                    InteractableObjects.RegisterLootedWeaponMagazine(
                        BotOwner,
                        move.ApprovedReloadWeapon,
                        magazine);
                }
            }
            else
            {
                RememberLoosePickupBackpackSupportRejection(move?.Item);
                foreach (Item supplyItem in GetInsertedMagazineTopOffSupplyItems(move))
                {
                    RememberLoosePickupBackpackSupportRejection(supplyItem);
                }
            }

            if (succeeded || (move?.ContinueFollowUpsOnFailure == true && !failedTopOffDetach))
            {
                foreach (BodyGearCandidate candidate in move?.FollowUpCandidates ??
                         Array.Empty<BodyGearCandidate>())
                {
                    pendingLoosePickupBackpackSupportFollowUps.Enqueue(candidate);
                }
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][BackpackSupport] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(loosePickupBackpackSupportWeapon)} result={(succeeded ? "moveComplete" : "moveFailed")} " +
                $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)} " +
                $"followUps={pendingLoosePickupBackpackSupportFollowUps.Count}");
        }

        private bool WasLoosePickupBackpackSupportRejected(Item? item)
        {
            return item != null &&
                   !string.IsNullOrEmpty(item.Id) &&
                   loosePickupBackpackSupportRejectedItemIds.Contains(item.Id);
        }

        private void RememberLoosePickupBackpackSupportRejection(Item? item)
        {
            if (item != null && !string.IsNullOrEmpty(item.Id))
            {
                loosePickupBackpackSupportRejectedItemIds.Add(item.Id);
            }
        }

        private void ResetLoosePickupBackpackSupport()
        {
            loosePickupBackpackSupportWeapon = null;
            loosePickupBackpackSupportCompleted = false;
            loosePickupBackpackSupportMoveInProgress = false;
            pendingLoosePickupBackpackSupportFollowUps.Clear();
            loosePickupBackpackSupportRejectedItemIds.Clear();
        }
    }
}
