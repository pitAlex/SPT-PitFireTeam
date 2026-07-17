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
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Container move timed out for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={activeContainerLootMove?.SourceName ?? "unknown"} item={DescribeLootDebugItem(activeContainerLootMove?.Item)}");
                    activeContainerLootMoveGeneration = 0;
                    activeContainerLootMove = null;
                    ClearContainerLootState("TakeContainerLoot:moveTimeout");
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
                // 2. finish weapon-equip/vest-swap follow-ups
                // 3. optionally equip or narrowly swap tactical vest protection
                // 4. optionally equip an empty primary or add a usable support beside a working primary
                // 5. promote a tracked backpack cargo weapon when newly found magazines complete it
                // 6. otherwise move eligible filtered cargo into backpack/pockets
                if (TryStartPendingContainerLootMove(inventory))
                {
                    return;
                }

                if (TryStartPendingContainerGearSwapFollowUpMove(inventory, followerEquipment))
                {
                    return;
                }

                if (TryStartEasyContainerTacticalVestMove(inventory, containerRoot, followerEquipment))
                {
                    return;
                }

                // Prefer completing the follower's tracked support weapon over introducing a
                // second candidate before weapon-package comparison has been implemented.
                if (TryStartContainerSecondaryWeaponPromotionMove(inventory, containerRoot, followerEquipment))
                {
                    return;
                }

                if (TryStartEasyContainerWeaponEquipMove(inventory, containerRoot, followerEquipment))
                {
                    return;
                }

                if (TryStartContainerBackpackCargoWeaponPromotionMove(inventory, containerRoot, followerEquipment))
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

                    IEnumerable<BodyGearCandidate>? operationalMagazineCandidates = candidate.Item is Weapon weapon
                        ? GetContainerOperationalMagazineCandidates(containerRoot, weapon)
                        : null;
                    IEnumerable<BodyGearCandidate>? operationalAmmoCandidates = candidate.Item is Weapon internalWeapon
                        ? GetContainerWeaponLooseAmmoCandidates(containerRoot, internalWeapon)
                        : null;
                    if (!TryBuildFilteredLootMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            operationalMagazineCandidates,
                            operationalAmmoCandidates,
                            out BodyGearMove? move))
                    {
                        containerLootAttemptedItemIds.Add(candidate.Item.Id);
                        containerLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    if (!move.IsStagingOperation)
                    {
                        containerLootAttemptedItemIds.Add(candidate.Item.Id);
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
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Container move starting for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)} " +
                $"followUps={move?.FollowUpCandidates?.Count ?? 0} lootCue={move?.SuccessPhrase}");
            containerLootMoveInProgress = true;
            containerLootAttemptStartedAt = Time.time;
            int moveGeneration = ++containerLootMoveGeneration;
            activeContainerLootMoveGeneration = moveGeneration;
            activeContainerLootMove = move;
            RunBodyGearMoveTransaction(
                inventory,
                move,
                new Callback(result => CompleteContainerLootMove(result, move, moveGeneration)));
        }

        private void EnqueueContainerGearSwapFollowUps(BodyGearMove move)
        {
            if (move?.FollowUpCandidates == null || move.FollowUpCandidates.Count == 0)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container move has no follow-ups for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)}");
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Container move enqueue follow-ups for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={move.SourceName} item={DescribeLootDebugItem(move.Item)} count={move.FollowUpCandidates.Count}");

            foreach (BodyGearCandidate candidate in move.FollowUpCandidates)
            {
                bool allowAlreadyAttempted =
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.SecondaryWeaponEquip ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateWeaponDestination ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.SalvageMagazineAmmo;
                if (candidate?.Item != null &&
                    !string.IsNullOrEmpty(candidate.Item.Id) &&
                    (allowAlreadyAttempted || !containerLootAttemptedItemIds.Contains(candidate.Item.Id)))
                {
                    pendingContainerGearSwapFollowUps.Enqueue(candidate);
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Container follow-up enqueued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)} " +
                        $"queue={pendingContainerGearSwapFollowUps.Count}");
                    continue;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container follow-up not enqueued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} item={DescribeLootDebugItem(candidate?.Item)} " +
                    $"reason={(candidate?.Item == null ? "itemMissing" : string.IsNullOrEmpty(candidate.Item.Id) ? "missingId" : containerLootAttemptedItemIds.Contains(candidate.Item.Id) ? "alreadyAttempted" : "unknown")}");
            }
        }

        private void CompleteContainerLootMove(IResult result, BodyGearMove move, int moveGeneration)
        {
            try
            {
                if (moveGeneration != activeContainerLootMoveGeneration)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Ignored stale container move callback for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={move?.SourceName ?? "unknown"} generation={moveGeneration}");
                    return;
                }

                activeContainerLootMoveGeneration = 0;
                activeContainerLootMove = null;
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                containerLootNextMoveAt = Time.time + 0.2f;

                Item completedItem = ResolveCompletedBodyGearMoveItem(move, result?.Succeed == true);
                bool stagingApplied = move.IsStagingOperation &&
                                      move.StagingWeapon != null &&
                                      (IsItemInsideRoot(move.Item, move.StagingWeapon) ||
                                       (move.StagingWeaponLoadedRoundsBefore >= 0 &&
                                        FollowerWeaponLooseFeedReadiness.GetLoadedRounds(move.StagingWeapon) >
                                        move.StagingWeaponLoadedRoundsBefore));
                if (result?.Succeed == true ||
                    stagingApplied ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, completedItem))
                {
                    if (!move.IsStagingOperation)
                    {
                        containerLootMovesSucceeded++;
                        if (!move.ReportAsLootNothing && !IsDogtagLoot(completedItem))
                        {
                            containerLootReportedMovesSucceeded++;
                        }

                        InteractableObjects.ClearStrictCargoTree(BotOwner, completedItem);
                        InteractableObjects.RegisterLootedWeaponTree(BotOwner, completedItem);
                    }

                    RegisterAmmoSalvageTargetReplacement(move, completedItem);
                    if (move.PrependFollowUps)
                    {
                        // Complete the current vanilla-style unload stack before advancing to the
                        // next planned cartridge group from the source magazine.
                        PrependFollowUps(pendingContainerGearSwapFollowUps, move.FollowUpCandidates);
                    }
                    else
                    {
                        EnqueueContainerGearSwapFollowUps(move);
                    }

                    if (!move.IsStagingOperation &&
                        move.StoreAsLoot &&
                        followerData?.IsSquadMate == true)
                    {
                        InteractableObjects.StoreItem(BotOwner, completedItem);
                    }

                    if (completedItem is Weapon && completedItem.GetItemComponent<KnifeComponent>() == null)
                    {
                        Weapon slottedPrimary = BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                            ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
                        if (move.RebindAsPrimaryWeapon || IsSameLootItem(slottedPrimary, completedItem))
                        {
                            // Keep collecting until this container is complete. The post-loot
                            // handoff performs selection only after the interaction is closed.
                            pendingContainerPrimaryWeaponSelection = completedItem as Weapon;
                        }
                        else
                        {
                            Weapon slottedSecondary = BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
                            if (IsSameLootItem(slottedSecondary, completedItem))
                            {
                                FollowerLootedPrimaryWeaponBinding.RegisterSupport(
                                    BotOwner,
                                    completedItem as Weapon,
                                    "containerLootMove");
                            }
                        }

                        RefreshLootedWeaponPresentation(completedItem);
                        containerLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Container loot move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
                if (move.IsStagingOperation && move.StagingWeapon != null)
                {
                    containerLootAttemptedItemIds.Add(move.StagingWeapon.Id);
                }

                if (move.ContinueFollowUpsOnFailure)
                {
                    EnqueueContainerGearSwapFollowUps(move);
                }

                RemovePendingAmmoSalvageTransfers(
                    pendingContainerGearSwapFollowUps,
                    move.AmmoSalvageMagazineId);

                containerLootHadEligibleButNoSpace = true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeContainerLoot move completion failed");
                Modules.Logger.LogError(ex);
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                activeContainerLootMoveGeneration = 0;
                activeContainerLootMove = null;
                containerLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishContainerLootNoMoreMoves()
        {
            Weapon primaryWeaponToSelect = pendingContainerPrimaryWeaponSelection;
            pendingContainerPrimaryWeaponSelection = null;

            if (containerLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                containerLootWeaponListDirty = false;
            }

            // Mark searched before closing so a player who opens the same container afterward does
            // not have to wait through vanilla search timers for the tree the follower just searched.
            TryMarkContainerLootSearchedForBoss();
            InteractableObjects.MarkContainerLootTargetChecked(activeLootContainer);
            TryCloseActiveLootContainerAfterSearch();

            if (containerLootReportedMovesSucceeded > 0)
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                ClearContainerLootState("TakeContainerLoot:done");
                QueuePostLootPrimaryWeaponSelection(primaryWeaponToSelect, "containerLootComplete");
                return;
            }

            BotOwner.BotTalk.TrySay(containerLootHadEligibleButNoSpace ? EPhraseTrigger.Negative : EPhraseTrigger.LootNothing, false);
            ClearContainerLootState("TakeContainerLoot:noSpace");
            QueuePostLootPrimaryWeaponSelection(primaryWeaponToSelect, "containerLootComplete");
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
                !containerLootHadEligibleButNoSpace &&
                containerLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            StopLootSearchSound();
            containerLootMoveInProgress = false;
            activeContainerLootMoveGeneration = 0;
            activeContainerLootMove = null;
            pendingContainerLootMove = null;
            pendingContainerGearSwapFollowUps.Clear();
            ClearAmmoSalvageRuntimeState();
            containerLootReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            containerLootAttemptStartedAt = 0f;
            pendingContainerLootMoveReadyAt = 0f;
            containerLootMovesSucceeded = 0;
            containerLootReportedMovesSucceeded = 0;
            containerLootHadEligibleButNoSpace = false;
            containerLootSuccessSpoken = false;
            containerLootWeaponListDirty = false;
            pendingContainerPrimaryWeaponSelection = null;
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
            // weapon equip move support mags first, then classify the weapon from settled live inventory.
            public BodyGearMove(
                Item item,
                IRaiseEvents operation,
                string sourceName,
                bool reportAsLootNothing,
                IReadOnlyList<BodyGearCandidate>? followUpCandidates = null,
                bool storeAsLoot = true,
                EPhraseTrigger successPhrase = EPhraseTrigger.LootGeneric,
                bool rebindAsPrimaryWeapon = false,
                bool continueFollowUpsOnFailure = false,
                bool isStagingOperation = false,
                Weapon? stagingWeapon = null,
                string? ammoSalvageMagazineId = null,
                bool resolveResultItemById = false,
                bool prependFollowUps = false,
                string? ammoSalvageReplacementSourceId = null,
                bool useVanillaAmmoTransaction = false,
                int stagingWeaponLoadedRoundsBefore = -1)
            {
                Item = item;
                Operation = operation;
                SourceName = sourceName;
                ReportAsLootNothing = reportAsLootNothing;
                FollowUpCandidates = followUpCandidates ?? Array.Empty<BodyGearCandidate>();
                StoreAsLoot = storeAsLoot;
                SuccessPhrase = successPhrase;
                RebindAsPrimaryWeapon = rebindAsPrimaryWeapon;
                ContinueFollowUpsOnFailure = continueFollowUpsOnFailure;
                IsStagingOperation = isStagingOperation;
                StagingWeapon = stagingWeapon;
                AmmoSalvageMagazineId = ammoSalvageMagazineId;
                ResolveResultItemById = resolveResultItemById;
                PrependFollowUps = prependFollowUps;
                AmmoSalvageReplacementSourceId = ammoSalvageReplacementSourceId;
                UseVanillaAmmoTransaction = useVanillaAmmoTransaction;
                StagingWeaponLoadedRoundsBefore = stagingWeaponLoadedRoundsBefore;
            }

            public Item Item { get; }
            public IRaiseEvents Operation { get; }
            public string SourceName { get; }
            public bool ReportAsLootNothing { get; }
            public IReadOnlyList<BodyGearCandidate> FollowUpCandidates { get; }
            public bool StoreAsLoot { get; }
            public EPhraseTrigger SuccessPhrase { get; }
            public bool RebindAsPrimaryWeapon { get; }
            public bool ContinueFollowUpsOnFailure { get; }
            public bool IsStagingOperation { get; }
            public Weapon? StagingWeapon { get; }
            public string? AmmoSalvageMagazineId { get; }
            public bool ResolveResultItemById { get; }
            public bool PrependFollowUps { get; }
            public string? AmmoSalvageReplacementSourceId { get; }
            public bool UseVanillaAmmoTransaction { get; }
            public int StagingWeaponLoadedRoundsBefore { get; }

            public BodyGearMove WithFollowUps(
                IReadOnlyList<BodyGearCandidate> followUpCandidates,
                EPhraseTrigger? successPhrase = null,
                bool continueOnFailure = false)
            {
                return new BodyGearMove(
                    Item,
                    Operation,
                    SourceName,
                    ReportAsLootNothing,
                    followUpCandidates,
                    StoreAsLoot,
                    successPhrase ?? SuccessPhrase,
                    RebindAsPrimaryWeapon,
                    continueOnFailure,
                    IsStagingOperation,
                    StagingWeapon,
                    AmmoSalvageMagazineId,
                    ResolveResultItemById,
                    PrependFollowUps,
                    AmmoSalvageReplacementSourceId,
                    UseVanillaAmmoTransaction,
                    StagingWeaponLoadedRoundsBefore);
            }
        }

        private sealed class BodyGearCandidate
        {
            // Candidate flags describe why an item is allowed through normal filters. Dogtags and
            // gear add/swap candidates are the main bypass cases; operational magazines use their
            // own helper so they do not inherit ordinary loot price/category/body filters.
            public BodyGearCandidate(
                Item item,
                EquipmentSlot? sourceSlot,
                string sourceName,
                int sourceTier,
                bool skipMagazine = false,
                bool bypassPriceThreshold = false,
                bool bypassCategoryFilter = false,
                bool bypassBodyGearLootability = false,
                bool reportAsLootNothing = false,
                BodyGearFollowUpDestination followUpDestination = BodyGearFollowUpDestination.Default,
                Weapon? ammoSalvageWeapon = null,
                MagazineItemClass? ammoSalvageMagazine = null,
                AmmoItemClass? ammoSalvageTargetStack = null,
                int ammoSalvageTransferCount = 0,
                Weapon? weaponSupportWeapon = null,
                bool forcePrimaryForLauncherPreference = false)
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
                FollowUpDestination = followUpDestination;
                AmmoSalvageWeapon = ammoSalvageWeapon;
                AmmoSalvageMagazine = ammoSalvageMagazine;
                AmmoSalvageTargetStack = ammoSalvageTargetStack;
                AmmoSalvageTransferCount = ammoSalvageTransferCount;
                WeaponSupportWeapon = weaponSupportWeapon;
                ForcePrimaryForLauncherPreference = forcePrimaryForLauncherPreference;
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
            public BodyGearFollowUpDestination FollowUpDestination { get; }
            public Weapon? AmmoSalvageWeapon { get; }
            public MagazineItemClass? AmmoSalvageMagazine { get; }
            public AmmoItemClass? AmmoSalvageTargetStack { get; }
            public int AmmoSalvageTransferCount { get; }
            public Weapon? WeaponSupportWeapon { get; }
            public bool ForcePrimaryForLauncherPreference { get; }

            public BodyGearCandidate WithFollowUpDestination(BodyGearFollowUpDestination destination)
            {
                return new BodyGearCandidate(
                    Item,
                    SourceSlot,
                    SourceName,
                    SourceTier,
                    SkipMagazine,
                    BypassPriceThreshold,
                    BypassCategoryFilter,
                    BypassBodyGearLootability,
                    ReportAsLootNothing,
                    destination,
                    AmmoSalvageWeapon,
                    AmmoSalvageMagazine,
                    AmmoSalvageTargetStack,
                    AmmoSalvageTransferCount,
                    WeaponSupportWeapon,
                    ForcePrimaryForLauncherPreference);
            }

            public BodyGearCandidate WithForcedPrimaryForLauncherPreference()
            {
                return new BodyGearCandidate(
                    Item,
                    SourceSlot,
                    SourceName,
                    SourceTier,
                    SkipMagazine,
                    BypassPriceThreshold,
                    BypassCategoryFilter,
                    BypassBodyGearLootability,
                    ReportAsLootNothing,
                    FollowUpDestination,
                    AmmoSalvageWeapon,
                    AmmoSalvageMagazine,
                    AmmoSalvageTargetStack,
                    AmmoSalvageTransferCount,
                    WeaponSupportWeapon,
                    forcePrimaryForLauncherPreference: true);
            }

            public BodyGearCandidate WithAmmoSalvageContext(
                BodyGearFollowUpDestination destination,
                Weapon weapon,
                MagazineItemClass magazine,
                AmmoItemClass? targetStack = null,
                int transferCount = 0)
            {
                return new BodyGearCandidate(
                    Item,
                    SourceSlot,
                    SourceName,
                    SourceTier,
                    SkipMagazine,
                    BypassPriceThreshold,
                    BypassCategoryFilter,
                    BypassBodyGearLootability,
                    ReportAsLootNothing,
                    destination,
                    weapon,
                    magazine,
                    targetStack,
                    transferCount,
                    WeaponSupportWeapon,
                    ForcePrimaryForLauncherPreference);
            }
        }

        private enum BodyGearFollowUpDestination
        {
            Default,
            OperationalVest,
            OperationalPockets,
            LoadMagazineIntoWeapon,
            BackpackCargo,
            PrimaryWeaponEquip,
            SecondaryWeaponEquip,
            EvaluateWeaponDestination,
            EvaluateSecondaryWeaponPromotion,
            EvaluateCargoWeaponPromotion,
            InternalAmmoCarry,
            WeaponSupportLooseAmmo,
            SalvageMagazineAmmo,
            SalvagedAmmoSecuredContainer,
            SalvagedAmmoPockets,
            SalvagedAmmoBackpack,
            SalvagedAmmoVest,
            SalvagedAmmoStackTransfer
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
