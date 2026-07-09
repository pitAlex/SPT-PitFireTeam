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
        // Container looting mirrors body filtered-loot behavior, but owns the physical open/search/
        // close loop so the player does not inherit a stuck open container after a completed search.
        private void HandleTakeContainerLoot()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueContainerLootCommand(out string? guardFailureReason))
            {
                ClearContainerLootState(guardFailureReason ?? "TakeContainerLoot:invalidState");
                return;
            }

            activeLootContainer ??= InteractableObjects.GetAssignedLootContainerTarget(BotOwner) ?? InteractableObjects.GetCurLootContainerTarget();
            if (activeLootContainer == null || activeLootContainer.gameObject == null)
            {
                ClearContainerLootState("TakeContainerLoot:containerMissing");
                return;
            }

            if (!activeLootContainer.isActiveAndEnabled || activeLootContainer.DoorState == EDoorState.Locked)
            {
                ClearContainerLootState("TakeContainerLoot:containerUnavailable");
                return;
            }

            if (InteractableObjects.GetCurLootContainerTarget() == null)
            {
                InteractableObjects.SetCurLootContainerTarget(activeLootContainer);
            }

            Vector3 containerPosition;
            try
            {
                containerPosition = InteractableObjects.GetContainerLootPosition(BotOwner);
            }
            catch
            {
                ClearContainerLootState("TakeContainerLoot:missingContainerPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, containerPosition);
            if (distance > 1.9f)
            {
                containerLootReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(containerPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeLootContainer.transform.position);

            if (activeLootContainer.DoorState != EDoorState.Open)
            {
                if (!containerLootOpened && activeLootContainer.DoorState == EDoorState.Shut)
                {
                    if (BotOwner.GetPlayer == null)
                    {
                        ClearContainerLootState("TakeContainerLoot:noPlayer");
                        return;
                    }

                    InteractLootContainer(activeLootContainer, BotOwner.GetPlayer, EInteractionType.Open);
                    containerLootOpened = true;
                    containerLootOpenRequestedAt = Time.time;
                    return;
                }

                if (containerLootOpened &&
                    containerLootOpenRequestedAt > 0f &&
                    Time.time - containerLootOpenRequestedAt > LootContainerOpenTimeoutSeconds)
                {
                    ClearContainerLootState("TakeContainerLoot:openTimeout");
                }

                return;
            }

            if (containerLootMoveInProgress)
            {
                if (containerLootAttemptStartedAt > 0f && Time.time - containerLootAttemptStartedAt > 4f)
                {
                    containerLootMoveInProgress = false;
                    containerLootAttemptStartedAt = 0f;
                    containerLootNextMoveAt = Time.time + 0.25f;
                }

                return;
            }

            if (!containerLootSearchStarted)
            {
                if (!TryGetContainerLootExecutionContext(
                        out InventoryController? inventory,
                        out SearchableItemItemClass? containerRoot,
                        out InventoryEquipment? followerEquipment,
                        out string contextFailureReason))
                {
                    ClearContainerLootState(contextFailureReason);
                    return;
                }

                StartContainerLootSearchDelay(inventory, containerRoot, followerEquipment);
                return;
            }

            if (Time.time < containerLootReadyAt || Time.time < containerLootNextMoveAt)
            {
                return;
            }

            TryStartNextContainerLootMove();
        }

        private void StartContainerLootSearchDelay(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            containerLootSearchStarted = true;
            followerData?.BeginCommittedLootCommand(FollowerCommandType.TakeContainerLoot);

            int gridCells = GetSearchableGridCellCount(containerRoot);
            containerLootReadyAt = Time.time + CalculateLootSearchDelaySeconds(gridCells);

            StartLootSearchSound(containerRoot, activeLootContainer?.transform?.position ?? BotOwner?.Position ?? Vector3.zero);
        }

        private bool CanContinueContainerLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeContainerLoot:botInvalid";
                return false;
            }

            if (followerData?.CanHandleBodyContainerLootCommands != true)
            {
                reason = "TakeContainerLoot:notSquadMate";
                return false;
            }

            if (!InteractableObjects.IsContainerLootTaker(BotOwner))
            {
                if (!InteractableObjects.SetContainerLootTaker(BotOwner) || !InteractableObjects.IsContainerLootTaker(BotOwner))
                {
                    reason = "TakeContainerLoot:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeContainerLoot:enemy";
                return false;
            }

            return true;
        }

        private bool TryGetContainerLootExecutionContext(
            out InventoryController? inventory,
            out SearchableItemItemClass? containerRoot,
            out InventoryEquipment? followerEquipment,
            out string reason)
        {
            inventory = BotOwner?.GetPlayer?.InventoryController;
            followerEquipment = inventory?.Inventory?.Equipment;
            containerRoot = activeLootContainer?.ItemOwner?.Items?.FirstOrDefault() as SearchableItemItemClass;

            if (!CanContinueContainerLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeContainerLoot:invalidState";
                return false;
            }

            if (activeLootContainer == null || activeLootContainer.gameObject == null)
            {
                reason = "TakeContainerLoot:containerMissing";
                return false;
            }

            if (!activeLootContainer.isActiveAndEnabled || activeLootContainer.DoorState == EDoorState.Locked)
            {
                reason = "TakeContainerLoot:containerUnavailable";
                return false;
            }

            if (inventory == null || followerEquipment == null)
            {
                reason = "TakeContainerLoot:noInventory";
                return false;
            }

            if (containerRoot == null)
            {
                reason = "TakeContainerLoot:noContainerRoot";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void TryStartNextContainerLootMove()
        {
            try
            {
                if (!TryGetContainerLootExecutionContext(out InventoryController? inventory, out SearchableItemItemClass? containerRoot, out InventoryEquipment? followerEquipment, out string reason))
                {
                    ClearContainerLootState(reason);
                    return;
                }

                // Scenario order for containers:
                // 1. finish delayed pickup-success moves
                // 2. finish weapon-equip magazine follow-ups
                // 3. optionally equip an empty primary slot
                // 4. otherwise move eligible filtered cargo into backpack/pockets
                if (TryStartPendingContainerLootMove(inventory))
                {
                    return;
                }

                if (TryStartPendingContainerGearSwapFollowUpMove(inventory, followerEquipment))
                {
                    return;
                }

                if (TryStartEasyContainerWeaponEquipMove(inventory, containerRoot, followerEquipment))
                {
                    return;
                }

                foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                             containerRoot,
                             "Container.Contents",
                             skipMagazines: false))
                {
                    if (!CanTryFilteredLootCandidate(candidate, containerLootAttemptedItemIds) ||
                        IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                    {
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    if (!TryBuildFilteredLootMove(inventory, followerEquipment, candidate, null, out BodyGearMove? move))
                    {
                        continue;
                    }

                    if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                    {
                        return;
                    }

                    StartContainerLootMove(inventory, move);
                    return;
                }

                FinishContainerLootNoMoreMoves();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeContainerLoot planning failed");
                Modules.Logger.LogError(ex);
                ClearContainerLootState("TakeContainerLoot:planningException");
            }
        }

        private bool TryStartPendingContainerLootMove(InventoryController inventory)
        {
            if (pendingContainerLootMove == null)
            {
                return false;
            }

            if (Time.time < pendingContainerLootMoveReadyAt)
            {
                return true;
            }

            BodyGearMove move = pendingContainerLootMove;
            pendingContainerLootMove = null;
            pendingContainerLootMoveReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            StartContainerLootMove(inventory, move);
            return true;
        }

        private void StartContainerLootMove(InventoryController inventory, BodyGearMove move)
        {
            containerLootMoveInProgress = true;
            containerLootAttemptStartedAt = Time.time;
            inventory.RunNetworkTransaction(move.Operation, new Callback(result => CompleteContainerLootMove(result, move)));
        }

        private void EnqueueContainerGearSwapFollowUps(BodyGearMove move)
        {
            if (move?.FollowUpCandidates == null || move.FollowUpCandidates.Count == 0)
            {
                return;
            }

            foreach (BodyGearCandidate candidate in move.FollowUpCandidates)
            {
                if (candidate?.Item != null &&
                    !string.IsNullOrEmpty(candidate.Item.Id) &&
                    !containerLootAttemptedItemIds.Contains(candidate.Item.Id))
                {
                    pendingContainerGearSwapFollowUps.Enqueue(candidate);
                }
            }
        }

        private void CompleteContainerLootMove(IResult result, BodyGearMove move)
        {
            try
            {
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                containerLootNextMoveAt = Time.time + 0.2f;

                if (result?.Succeed == true || IsLootNowInBotInventory(BotOwner?.GetPlayer, move.Item))
                {
                    containerLootMovesSucceeded++;
                    if (!move.ReportAsLootNothing && !IsDogtagLoot(move.Item))
                    {
                        containerLootReportedMovesSucceeded++;
                    }

                    InteractableObjects.RegisterLootedWeaponTree(BotOwner, move.Item);
                    EnqueueContainerGearSwapFollowUps(move);

                    if (followerData?.IsSquadMate == true)
                    {
                        InteractableObjects.StoreItem(BotOwner, move.Item);
                    }

                    if (move.Item is Weapon && move.Item.GetItemComponent<KnifeComponent>() == null)
                    {
                        RefreshLootedWeaponPresentation(move.Item);
                        containerLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Container loot move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeContainerLoot move completion failed");
                Modules.Logger.LogError(ex);
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                containerLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishContainerLootNoMoreMoves()
        {
            if (containerLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                containerLootWeaponListDirty = false;
            }

            // Mark searched before closing so a player who opens the same container afterward does
            // not have to wait through vanilla search timers for the tree the follower just searched.
            TryMarkContainerLootSearchedForBoss();
            TryCloseActiveLootContainerAfterSearch();

            if (containerLootReportedMovesSucceeded > 0)
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                ClearContainerLootState("TakeContainerLoot:done");
                return;
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.LootNothing, false);
            ClearContainerLootState("TakeContainerLoot:noSpace");
        }

        private void TryCloseActiveLootContainerAfterSearch()
        {
            if (activeLootContainer == null ||
                activeLootContainer.gameObject == null ||
                activeLootContainer.DoorState != EDoorState.Open)
            {
                return;
            }

            IPlayer player = BotOwner?.GetPlayer;
            if (player == null)
            {
                return;
            }

            try
            {
                InteractLootContainer(activeLootContainer, player, EInteractionType.Close);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to close looted container: {ex.Message}");
            }
        }

        private void CleanupContainerLootInteraction(string reason)
        {
            if (!containerLootMoveInProgress &&
                pendingContainerLootMove == null &&
                containerLootReadyAt <= 0f &&
                containerLootNextMoveAt <= 0f &&
                containerLootAttemptStartedAt <= 0f &&
                activeLootContainer == null &&
                activeLootSearchSource == null &&
                containerLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            StopLootSearchSound();
            containerLootMoveInProgress = false;
            pendingContainerLootMove = null;
            pendingContainerGearSwapFollowUps.Clear();
            containerLootReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            containerLootAttemptStartedAt = 0f;
            pendingContainerLootMoveReadyAt = 0f;
            containerLootMovesSucceeded = 0;
            containerLootReportedMovesSucceeded = 0;
            containerLootGenericSpoken = false;
            containerLootWeaponListDirty = false;
            containerLootOpened = false;
            containerLootOpenRequestedAt = 0f;
            containerLootSearchStarted = false;
            activeLootSearchSource = null;
            containerLootAttemptedItemIds.Clear();
            activeLootContainer = null;
            followerData?.EndCommittedLootCommand(FollowerCommandType.TakeContainerLoot);

            if (BotOwner != null)
            {
                InteractableObjects.RemoveContainerLootTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurLootContainerTarget();
        }

        private void ClearContainerLootState(string reason)
        {
            if (!string.Equals(reason, "TakeContainerLoot:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeContainerLoot:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Container loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupContainerLootInteraction(reason);
            if (string.Equals(reason, "TakeContainerLoot:done", StringComparison.Ordinal))
            {
                followerData?.CompleteTakeContainerLoot();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

        private static void InteractLootContainer(LootableContainer container, IPlayer player, EInteractionType action)
        {
            InteractionResult result = new InteractionResult(action);
            container.InteractingPlayer = player;
            container.Interact(result);
        }

        private sealed class BodyGearMove
        {
            // One executable inventory transaction plus optional follow-ups. Follow-ups let easy
            // weapon equip move the weapon first, then move a supporting mag after inventory settles.
            public BodyGearMove(
                Item item,
                GInterface424 operation,
                string sourceName,
                bool reportAsLootNothing,
                IReadOnlyList<BodyGearCandidate>? followUpCandidates = null)
            {
                Item = item;
                Operation = operation;
                SourceName = sourceName;
                ReportAsLootNothing = reportAsLootNothing;
                FollowUpCandidates = followUpCandidates ?? Array.Empty<BodyGearCandidate>();
            }

            public Item Item { get; }
            public GInterface424 Operation { get; }
            public string SourceName { get; }
            public bool ReportAsLootNothing { get; }
            public IReadOnlyList<BodyGearCandidate> FollowUpCandidates { get; }

            public BodyGearMove WithFollowUps(IReadOnlyList<BodyGearCandidate> followUpCandidates)
            {
                return new BodyGearMove(Item, Operation, SourceName, ReportAsLootNothing, followUpCandidates);
            }
        }

        private sealed class BodyGearCandidate
        {
            // Candidate flags describe why an item is allowed through normal filters. Dogtags are
            // the main bypass case; support magazines use SkipMagazine/SourceName to track policy.
            public BodyGearCandidate(
                Item item,
                EquipmentSlot? sourceSlot,
                string sourceName,
                int sourceTier,
                bool skipMagazine = false,
                bool bypassPriceThreshold = false,
                bool bypassCategoryFilter = false,
                bool bypassBodyGearLootability = false,
                bool reportAsLootNothing = false)
            {
                Item = item;
                SourceSlot = sourceSlot;
                SourceName = sourceName;
                SourceTier = sourceTier;
                SkipMagazine = skipMagazine;
                BypassPriceThreshold = bypassPriceThreshold;
                BypassCategoryFilter = bypassCategoryFilter;
                BypassBodyGearLootability = bypassBodyGearLootability;
                ReportAsLootNothing = reportAsLootNothing;
            }

            public Item Item { get; }
            public EquipmentSlot? SourceSlot { get; }
            public string SourceName { get; }
            public int SourceTier { get; }
            public bool SkipMagazine { get; }
            public bool BypassPriceThreshold { get; }
            public bool BypassCategoryFilter { get; }
            public bool BypassBodyGearLootability { get; }
            public bool ReportAsLootNothing { get; }
        }

        private static readonly EquipmentSlot[] BodyGearTopLevelSlotOrder =
        {
            // Mirrors the recovery priority used for player/fallen gear, with backpack already
            // attempted in a capacity-first pass. Scabbard, armband, and secure container are omitted.
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.ArmorVest,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Headwear,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster,
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets,
            EquipmentSlot.Earpiece,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Eyewear
        };

        private static readonly EquipmentSlot[] BodyGearContentSlotOrder =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets
        };

        private static readonly EquipmentSlot[] BodyGearCarrySlotOrder =
        {
            EquipmentSlot.Backpack,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets
        };

        private static readonly EquipmentSlot[] BodyGearWeaponSlotOrder =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        private static readonly EquipmentSlot[] FilteredLootCarrySlotOrder =
        {
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets
        };

    }
}
