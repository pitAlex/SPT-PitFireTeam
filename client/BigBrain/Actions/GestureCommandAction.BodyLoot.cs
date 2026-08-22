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
        // Body looting has two intentionally different scenarios:
        // - teammate corpses: recover gear/capacity using the older body-gear behavior
        // - non-teammate corpses: search contents through the filtered loot pipeline
        private void HandleTakeBodyGear()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueBodyLootCommand(out string? guardFailureReason))
            {
                ClearBodyLootState(guardFailureReason ?? "TakeBodyGear:invalidState");
                return;
            }

            activeBodyLootCorpse ??= InteractableObjects.GetAssignedBodyLootTarget(BotOwner) ?? InteractableObjects.GetCurBodyLootTarget();
            if (activeBodyLootCorpse == null || activeBodyLootCorpse.gameObject == null)
            {
                ClearBodyLootState("TakeBodyGear:corpseMissing");
                return;
            }

            if (InteractableObjects.GetCurBodyLootTarget() == null)
            {
                InteractableObjects.SetCurBodyLootTarget(activeBodyLootCorpse);
            }

            Vector3 bodyPosition;
            try
            {
                bodyPosition = InteractableObjects.GetBodyLootPosition(BotOwner);
            }
            catch
            {
                ClearBodyLootState("TakeBodyGear:missingBodyPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, bodyPosition);
            if (distance > 1.9f)
            {
                bodyLootReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(bodyPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeBodyLootCorpse.transform.position);

            if (bodyLootMoveInProgress)
            {
                if (bodyLootAttemptStartedAt > 0f &&
                    Time.time - bodyLootAttemptStartedAt > LootInventoryTransactionTimeoutSeconds)
                {
                    BodyGearMove timedOutMove = activeBodyLootMove;
                    int timedOutGeneration = activeBodyLootMoveGeneration;
                    if (timedOutMove?.TransactionRecoveryAttempted != true)
                    {
                        timedOutMove.TransactionRecoveryAttempted = true;
                        bodyLootAttemptStartedAt = Time.time;
                        bool recoverySubmitted = TryFastForwardLootInventoryTransaction(
                            BotOwner?.GetPlayer?.InventoryController,
                            timedOutMove,
                            "bodyTimeout");
                        if (!bodyLootMoveInProgress ||
                            activeBodyLootMoveGeneration != timedOutGeneration ||
                            !ReferenceEquals(activeBodyLootMove, timedOutMove) ||
                            recoverySubmitted)
                        {
                            return;
                        }
                    }

                    // The EFT transaction may still complete after its callback timeout. Abort this
                    // loot command and invalidate the callback instead of issuing an overlapping
                    // retry against an inventory tree whose final state is unknown.
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Body move timed out for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={activeBodyLootMove?.SourceName ?? "unknown"} item={DescribeLootDebugItem(activeBodyLootMove?.Item)}");
                    activeBodyLootMoveGeneration = 0;
                    activeBodyLootMove = null;
                    ClearBodyLootState("TakeBodyGear:moveTimeout");
                }

                return;
            }

            if (!bodyLootSearchStarted)
            {
                if (!TryGetBodyLootExecutionContext(
                        out InventoryController? inventory,
                        out InventoryEquipment? corpseEquipment,
                        out InventoryEquipment? followerEquipment,
                        out string contextFailureReason))
                {
                    ClearBodyLootState(contextFailureReason);
                    return;
                }

                StartBodyLootSearchDelay(inventory, corpseEquipment, followerEquipment);
                return;
            }

            if (Time.time < bodyLootReadyAt || Time.time < bodyLootNextMoveAt)
            {
                return;
            }

            TryStartNextBodyGearMove();
        }

        private void StartBodyLootSearchDelay(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            bodyLootSearchStarted = true;
            followerData?.BeginCommittedLootCommand(FollowerCommandType.TakeBodyGear);

            Item? soundSource = GetBestBodyLootSearchSoundSource(corpseEquipment);
            int gridCells = GetBodyLootSearchGridCells(corpseEquipment);
            bodyLootReadyAt = Time.time + CalculateLootSearchDelaySeconds(gridCells);

            StartLootSearchSound(soundSource, BotOwner?.Position ?? Vector3.zero);
        }

        private bool CanContinueBodyLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeBodyGear:botInvalid";
                return false;
            }

            if (followerData?.CanHandleBodyContainerLootCommands != true)
            {
                reason = "TakeBodyGear:notSquadMate";
                return false;
            }

            if (!InteractableObjects.IsBodyLootTaker(BotOwner))
            {
                if (!InteractableObjects.SetBodyLootTaker(BotOwner) || !InteractableObjects.IsBodyLootTaker(BotOwner))
                {
                    reason = "TakeBodyGear:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeBodyGear:enemy";
                return false;
            }

            return true;
        }

        private void TryStartNextBodyGearMove()
        {
            try
            {
                if (!TryGetBodyLootExecutionContext(out InventoryController? inventory, out InventoryEquipment? corpseEquipment, out InventoryEquipment? followerEquipment, out string reason))
                {
                    ClearBodyLootState(reason);
                    return;
                }

                if (TryStartPendingBodyLootMove(inventory))
                {
                    return;
                }

                // Non-teammates are never treated as "take the whole kit." Search the body
                // contents under the configured price/type filters, with dogtag handled specially.
                if (!TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment))
                {
                    TryStartNextFilteredBodyLootMove(inventory, corpseEquipment, followerEquipment);
                    return;
                }

                // Try the corpse backpack first as a capacity source, but only if it can be
                // carried inside the follower's own backpack. After that move succeeds, the next
                // planning pass sees its nested grids through normal live inventory state.
                BodyGearMove? backpackCapacityMove = TryBuildCorpseBackpackCapacityMove(inventory, corpseEquipment, followerEquipment);
                if (backpackCapacityMove != null)
                {
                    if (TryQueueBodyLootMoveAfterPickupSuccess(backpackCapacityMove))
                    {
                        return;
                    }

                    StartBodyGearMove(inventory, backpackCapacityMove);
                    return;
                }

                // Plan one live inventory transaction at a time. This keeps interruption behavior
                // simple: completed moves remain valid cargo, and unmoved body gear stays on the corpse.
                foreach (BodyGearCandidate candidate in GetBodyGearCandidates(corpseEquipment))
                {
                    if (candidate.Item == null ||
                        string.IsNullOrEmpty(candidate.Item.Id) ||
                        bodyLootAttemptedItemIds.Contains(candidate.Item.Id) ||
                        InteractableObjects.IsProtectedFollowerEquipment(candidate.Item) ||
                        !IsBodyGearCandidateLootable(candidate.Item) ||
                        IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                    {
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    if (!TryBuildBodyGearMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                    {
                        return;
                    }

                    StartBodyGearMove(inventory, move);
                    return;
                }

                FinishBodyLootNoMoreMoves();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeBodyGear planning failed");
                Modules.Logger.LogError(ex);
                ClearBodyLootState("TakeBodyGear:planningException");
            }
        }

        private bool TryStartPendingBodyLootMove(InventoryController inventory)
        {
            if (pendingBodyLootMove == null)
            {
                return false;
            }

            if (Time.time < pendingBodyLootMoveReadyAt)
            {
                return true;
            }

            BodyGearMove move = pendingBodyLootMove;
            pendingBodyLootMove = null;
            pendingBodyLootMoveReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryGetBodyLootExecutionContext(
            out InventoryController? inventory,
            out InventoryEquipment? corpseEquipment,
            out InventoryEquipment? followerEquipment,
            out string reason)
        {
            inventory = BotOwner?.GetPlayer?.InventoryController;
            followerEquipment = inventory?.Inventory?.Equipment;
            corpseEquipment = activeBodyLootCorpse?.ItemOwner?.RootItem as InventoryEquipment;

            if (!CanContinueBodyLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeBodyGear:invalidState";
                return false;
            }

            if (activeBodyLootCorpse == null || activeBodyLootCorpse.gameObject == null)
            {
                reason = "TakeBodyGear:corpseMissing";
                return false;
            }

            if (inventory == null || followerEquipment == null)
            {
                reason = "TakeBodyGear:noInventory";
                return false;
            }

            if (corpseEquipment == null)
            {
                reason = "TakeBodyGear:noCorpseEquipment";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void TryStartNextFilteredBodyLootMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            // Scenario order for non-teammate bodies:
            // 1. secure the PMC dogtag before any equipment transaction can alter the action state
            // 2. finish any follow-up magazine move produced by easy weapon equip
            // 3. maintain the primary's inserted/source magazines before carrying surplus ammo
            // 4. fill an empty shoulder-support slot before optional holster maintenance can spend
            //    the same fast-access space
            // 5. maintain secondary magazines/ammo, then holster magazines/ammo, from unresolved loot
            // 6. optionally equip or narrowly swap tactical vest protection
            // 7. promote a tracked backpack cargo weapon when newly found magazines complete it
            // 8. otherwise loot eligible contents into backpack/pockets only
            if (TryStartNonTeammatePmcDogtagMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartPendingBodyGearSwapFollowUpMove(inventory, followerEquipment))
            {
                return;
            }

            if (TryStartPreferredBodyPrimaryWeaponMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartBodyPrimaryTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    allowLooseAmmoCarry: false))
            {
                return;
            }

            if (TryStartBodyPrimaryTacticalMagazineMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartBodyPrimaryTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    allowLooseAmmoCarry: true))
            {
                return;
            }

            if (TryStartEasyBodyWeaponEquipMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartBodySupportTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    allowLooseAmmoCarry: false))
            {
                return;
            }

            if (TryStartBodySupportTacticalMagazineMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon))
            {
                return;
            }

            if (TryStartBodySupportTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    allowLooseAmmoCarry: true))
            {
                return;
            }

            if (TryStartBodySupportLooseFeedAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon))
            {
                return;
            }

            if (TryStartBodySupportTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.Holster,
                    allowLooseAmmoCarry: false))
            {
                return;
            }

            if (TryStartBodySupportTacticalMagazineMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.Holster))
            {
                return;
            }

            if (TryStartBodySupportTacticalAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.Holster,
                    allowLooseAmmoCarry: true))
            {
                return;
            }

            if (TryStartBodySupportLooseFeedAmmoMove(
                    inventory,
                    corpseEquipment,
                    followerEquipment,
                    EquipmentSlot.Holster))
            {
                return;
            }

            if (TryStartEasyBodyTacticalVestMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            // Make an already-carried support weapon usable before considering another weapon.
            // This avoids choosing between weapon packages before comparison policy exists.
            if (TryStartBodySecondaryWeaponPromotionMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartEasyBodyHolsterWeaponEquipMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            if (TryStartBodyBackpackCargoWeaponPromotionMove(inventory, corpseEquipment, followerEquipment))
            {
                return;
            }

            foreach (BodyGearCandidate candidate in GetFilteredBodyLootCandidates(corpseEquipment))
            {
                if (!CanTryFilteredLootCandidate(candidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                IEnumerable<BodyGearCandidate>? operationalMagazineCandidates = candidate.Item is Weapon weapon
                    ? GetBodyOperationalMagazineCandidates(corpseEquipment, weapon)
                    : null;
                IEnumerable<BodyGearCandidate>? operationalAmmoCandidates = candidate.Item is Weapon internalWeapon
                    ? GetBodyWeaponLooseAmmoCandidates(corpseEquipment, internalWeapon)
                    : null;
                if (!TryBuildFilteredLootMove(
                        inventory,
                        followerEquipment,
                        corpseEquipment,
                        candidate,
                        operationalMagazineCandidates,
                        operationalAmmoCandidates,
                        out BodyGearMove? move))
                {
                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    bodyLootHadEligibleButNoSpace = true;
                    continue;
                }

                if (!move.IsStagingOperation)
                {
                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                }

                if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                {
                    return;
                }

                StartBodyGearMove(inventory, move);
                return;
            }

            FinishBodyLootNoMoreMoves();
        }

        private bool TryStartNonTeammatePmcDogtagMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!TryCreateNonTeammatePmcDogtagCandidate(corpseEquipment, out BodyGearCandidate candidate) ||
                !CanTryFilteredLootCandidate(candidate, bodyLootAttemptedItemIds) ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
            {
                return false;
            }

            // Mark it before building so a full inventory cannot trap the planner on the dogtag.
            // Dogtags still bypass filters and only use the normal backpack/pocket carry containers.
            bodyLootAttemptedItemIds.Add(candidate.Item.Id);
            if (!TryBuildFilteredLootMove(inventory, followerEquipment, corpseEquipment, candidate, null, null, out BodyGearMove move))
            {
                bodyLootHadEligibleButNoSpace = true;
                Modules.Logger.LogInfo(
                    $"[LootCommand] Dogtag left on body for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': no backpack/pocket space");
                return false;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand] Dogtag move planned first for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}'");
            StartBodyGearMove(inventory, move);
            return true;
        }

        private BodyGearMove? TryBuildCorpseBackpackCapacityMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (bodyLootBackpackCapacityAttempted)
            {
                return null;
            }

            bodyLootBackpackCapacityAttempted = true;

            Item corpseBackpack = corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (corpseBackpack == null || string.IsNullOrEmpty(corpseBackpack.Id))
            {
                return null;
            }

            if (InteractableObjects.IsProtectedFollowerEquipment(corpseBackpack) ||
                !IsBodyGearCandidateLootable(corpseBackpack))
            {
                return null;
            }

            BodyGearCandidate candidate = new BodyGearCandidate(
                corpseBackpack,
                EquipmentSlot.Backpack,
                "bodyBackpackCapacity",
                0);

            if (TryBuildBodyGearMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
            {
                bodyLootAttemptedItemIds.Add(corpseBackpack.Id);
                return move;
            }

            return null;
        }

        private bool TryBuildBodyGearMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;

            if (candidate.Item == null)
            {
                return false;
            }

            // Empty compatible slots are allowed because they increase carry capacity without
            // sacrificing the follower's current fighting kit. Existing gear is never thrown or swapped.
            if (TryFindBodyGearEquipmentSlot(followerEquipment, candidate, out ItemAddress? equipAddress) &&
                TryCreateBodyGearMove(inventory, candidate, equipAddress, out move))
            {
                return true;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetBodyGearCarryContainers(followerEquipment, candidate.Item))
            {
                if (!container.TryFindLocationForItem(candidate.Item, out ItemAddress packAddress) ||
                    object.Equals(candidate.Item.Parent, packAddress))
                {
                    continue;
                }

                if (TryCreateBodyGearMove(inventory, candidate, packAddress, out move))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildFilteredLootMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            IEnumerable<BodyGearCandidate>? operationalAmmoCandidates,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate.Item == null)
            {
                return false;
            }

            // Filtered looting never uses the follower rig for generic cargo. Fast-access writes
            // are tactical magazine moves for an accepted primary/support equipment plan.
            if (ShouldUseFilteredLootEquipmentSlot(candidate))
            {
                if (TryBuildEasyWeaponEquipMove(
                        inventory,
                        followerEquipment,
                        sourceRoot,
                        candidate,
                        operationalMagazineCandidates,
                        operationalAmmoCandidates,
                        out move,
                        out bool handledByGearPolicy))
                {
                    return true;
                }

                if (handledByGearPolicy)
                {
                    return false;
                }
            }

            if (CanBuildPotentialPrimaryWeaponCargoPackage(followerEquipment, candidate) &&
                operationalMagazineCandidates != null)
            {
                OperationalMagazinePlan cargoMagazinePlan = PlanOperationalMagazineFollowUps(
                    inventory,
                    followerEquipment,
                    candidate.Item as Weapon,
                    operationalMagazineCandidates);
                bool builtCargoPackage = TryBuildPotentialWeaponCargoChain(
                    inventory,
                    followerEquipment,
                    candidate,
                    cargoMagazinePlan,
                    "filteredPotentialWeaponCargo",
                    out move);
                if (builtCargoPackage && move != null && candidate.Item is Weapon cargoWeapon)
                {
                    move = AppendWeaponLooseAmmoSupportFollowUps(
                        move,
                        followerEquipment,
                        cargoWeapon,
                        operationalAmmoCandidates,
                        "potentialWeaponCargo",
                        GetOperationalMagazineCartridgeItems(cargoMagazinePlan));
                }

                return builtCargoPackage;
            }

            if (ShouldUseFilteredLootEquipmentSlot(candidate) &&
                TryFindFilteredWeaponCargoEquipmentSlot(followerEquipment, candidate, out ItemAddress? equipAddress) &&
                TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    equipAddress,
                    out move,
                    // Pickup Weapons may use an empty holster or second-primary slot as convenient
                    // cargo placement. Only a weapon becoming combat primary earns LootWeapon.
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                if (candidate.Item is Weapon slottedWeapon)
                {
                    move = AppendWeaponLooseAmmoSupportFollowUps(
                        move,
                        followerEquipment,
                        slottedWeapon,
                        operationalAmmoCandidates,
                        "filteredWeaponSlot");
                }

                return true;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetFilteredLootCarryContainers(followerEquipment))
            {
                if (!container.TryFindLocationForItem(candidate.Item, out ItemAddress packAddress) ||
                    object.Equals(candidate.Item.Parent, packAddress))
                {
                    continue;
                }

                if (TryCreateBodyGearMove(inventory, candidate, packAddress, out move))
                {
                    if (candidate.Item is Weapon cargoWeapon)
                    {
                        move = AppendWeaponLooseAmmoSupportFollowUps(
                            move,
                            followerEquipment,
                            cargoWeapon,
                            operationalAmmoCandidates,
                            "filteredWeaponCargo");
                    }

                    return true;
                }
            }

            return false;
        }

        private static bool ShouldUseFilteredLootEquipmentSlot(BodyGearCandidate candidate)
        {
            return candidate?.Item is Weapon weapon && weapon.GetItemComponent<KnifeComponent>() == null;
        }

        private bool TryCreateBodyGearMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            ItemAddress address,
            out BodyGearMove? move,
            bool storeAsLoot = true,
            EPhraseTrigger successPhrase = EPhraseTrigger.LootGeneric,
            bool rebindAsPrimaryWeapon = false,
            bool isStagingOperation = false,
            Weapon? stagingWeapon = null)
        {
            move = null;
            Item item = candidate?.Item;
            if (item == null)
            {
                return false;
            }

            Diz.LanguageExtensions.OperationResult<EFT.InventoryLogic.MoveResult> moveResult = EFT.InventoryLogic.ItemManipulator.Move(item, address, inventory, true);
            if (moveResult.Failed)
            {
                LogBodyGearMoveBuildRejection(inventory, candidate, address, "moveResultFailed", moveResult.Error, null);
                return false;
            }

            bool itemsDestroyRequired = moveResult.Value.ItemsDestroyRequired;
            bool canExecute = !itemsDestroyRequired && inventory.CanExecute(moveResult.Value);
            if (itemsDestroyRequired || !canExecute)
            {
                LogBodyGearMoveBuildRejection(
                    inventory,
                    candidate,
                    address,
                    itemsDestroyRequired ? "itemsDestroyRequired" : "operationCanExecuteFalse",
                    null,
                    moveResult.Value);
                return false;
            }

            move = new BodyGearMove(
                item,
                moveResult.Value,
                candidate.SourceName,
                candidate.ReportAsLootNothing,
                storeAsLoot: storeAsLoot,
                successPhrase: successPhrase,
                rebindAsPrimaryWeapon: rebindAsPrimaryWeapon,
                isStagingOperation: isStagingOperation,
                stagingWeapon: stagingWeapon,
                ammoSalvageMagazineId: candidate.AmmoSalvageMagazine?.Id,
                approvedReloadWeapon: candidate.WeaponSupportWeapon);
            return true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogBodyGearMoveBuildRejection(
            InventoryController inventory,
            BodyGearCandidate candidate,
            ItemAddress address,
            string reason,
            Error error,
            EFT.InventoryLogic.MoveResult operation)
        {
            if (candidate?.Item is not EFT.InventoryLogic.Magazine ||
                candidate.SourceName?.IndexOf("WeaponSupportMagazine", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            Item item = candidate.Item;
            string checkTo = DescribeInventoryEventResult(SafeCheckAction(item, address));
            string checkCurrent = DescribeInventoryEventResult(SafeCheckAction(item, null));
            string canBeMoved = "notGInterface409";
            if (item is EFT.InventoryLogic.IMoveCheckable movable)
            {
                canBeMoved = DescribeInventoryEventResult(SafeCanBeMoved(movable, address?.Container));
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Move build rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"reason={reason} error={DescribeInventoryError(error)} item={DescribeLootDebugItem(item)} " +
                $"source={candidate.SourceName} dest={candidate.FollowUpDestination} " +
                $"from={DescribeLootAddress(item.CurrentAddress)} to={DescribeLootAddress(address)} " +
                $"fromOwner={DescribeLootOwner(item.CurrentAddress)} toOwner={DescribeLootOwner(address)} " +
                $"checkTo={checkTo} checkCurrent={checkCurrent} canBeMoved={canBeMoved} " +
                $"operationCanExecute={(operation != null ? inventory?.CanExecute(operation).ToString() ?? "inventoryMissing" : "noOperation")}");
        }

        private void StartBodyGearMove(InventoryController inventory, BodyGearMove move)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Body move starting for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)} " +
                $"followUps={move?.FollowUpCandidates?.Count ?? 0} lootCue={move?.SuccessPhrase}");
            bodyLootMoveInProgress = true;
            bodyLootAttemptStartedAt = Time.time;
            int moveGeneration = ++bodyLootMoveGeneration;
            activeBodyLootMoveGeneration = moveGeneration;
            activeBodyLootMove = move;
            RunBodyGearMoveTransaction(
                inventory,
                move,
                new Callback(result => CompleteBodyGearMove(result, move, moveGeneration)));
        }

        private void EnqueueBodyGearSwapFollowUps(BodyGearMove move)
        {
            if (move?.FollowUpCandidates == null || move.FollowUpCandidates.Count == 0)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body move has no follow-ups for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={move?.SourceName ?? "unknown"} item={DescribeLootDebugItem(move?.Item)}");
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Body move enqueue follow-ups for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={move.SourceName} item={DescribeLootDebugItem(move.Item)} count={move.FollowUpCandidates.Count}");

            foreach (BodyGearCandidate candidate in move.FollowUpCandidates)
            {
                bool allowAlreadyAttempted =
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.SupportWeaponEquip ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateWeaponDestination ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.SalvageMagazineAmmo ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.TopOffWeaponMagazine ||
                    candidate?.FollowUpDestination == BodyGearFollowUpDestination.RestoreMagazineToWeapon;
                if (candidate?.Item != null &&
                    !string.IsNullOrEmpty(candidate.Item.Id) &&
                    (allowAlreadyAttempted || !bodyLootAttemptedItemIds.Contains(candidate.Item.Id)))
                {
                    pendingBodyGearSwapFollowUps.Enqueue(candidate);
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Body follow-up enqueued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)} " +
                        $"queue={pendingBodyGearSwapFollowUps.Count}");
                    continue;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body follow-up not enqueued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} item={DescribeLootDebugItem(candidate?.Item)} " +
                    $"reason={(candidate?.Item == null ? "itemMissing" : string.IsNullOrEmpty(candidate.Item.Id) ? "missingId" : bodyLootAttemptedItemIds.Contains(candidate.Item.Id) ? "alreadyAttempted" : "unknown")}");
            }
        }

        private void CompleteBodyGearMove(IResult result, BodyGearMove move, int moveGeneration)
        {
            try
            {
                if (moveGeneration != activeBodyLootMoveGeneration)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Ignored stale body move callback for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={move?.SourceName ?? "unknown"} generation={moveGeneration}");
                    return;
                }

                activeBodyLootMoveGeneration = 0;
                activeBodyLootMove = null;
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                bodyLootNextMoveAt = Time.time + 0.2f;

                Item completedItem = ResolveCompletedBodyGearMoveItem(move, result?.Succeed == true);
                bool stagingApplied = move.IsStagingOperation &&
                                      move.StagingWeapon != null &&
                                      (IsItemInsideRoot(move.Item, move.StagingWeapon) ||
                                       (move.StagingWeaponLoadedRoundsBefore >= 0 &&
                                         FollowerWeaponLooseFeedReadiness.GetLoadedRounds(move.StagingWeapon) >
                                         move.StagingWeaponLoadedRoundsBefore) ||
                                       (move.StagingMagazine != null &&
                                        move.StagingMagazineRoundsBefore >= 0 &&
                                        move.StagingMagazine.Count > move.StagingMagazineRoundsBefore));
                bool moveSucceeded = IsInsertedMagazineTopOffDetachMove(move)
                    ? DidInsertedMagazineTopOffDetachSettle(move)
                    : result?.Succeed == true ||
                      stagingApplied ||
                      (!move.IsStagingOperation &&
                       IsLootNowInBotInventory(BotOwner?.GetPlayer, completedItem));
                if (moveSucceeded)
                {
                    bool countsAsLootMove = !move.IsStagingOperation || !move.ReportAsLootNothing;
                    if (countsAsLootMove)
                    {
                        bodyLootMovesSucceeded++;
                        if (!move.ReportAsLootNothing && !IsDogtagLoot(completedItem))
                        {
                            bodyLootReportedMovesSucceeded++;
                        }
                    }

                    if (!move.IsStagingOperation)
                    {
                        InteractableObjects.ClearStrictCargoTree(BotOwner, completedItem);
                        InteractableObjects.RegisterLootedWeaponTree(BotOwner, completedItem);
                        if (completedItem is EFT.InventoryLogic.Magazine completedMagazine &&
                            move.ApprovedReloadWeapon != null)
                        {
                            InteractableObjects.RegisterLootedWeaponMagazine(
                                BotOwner,
                                move.ApprovedReloadWeapon,
                                completedMagazine);
                        }
                    }

                    RegisterAmmoSalvageTargetReplacement(move, completedItem);
                    if (move.PrependFollowUps)
                    {
                        // The loose one-round seed must be filled before the next cartridge group
                        // is touched, preserving EFT StackSlot's last-item-first unload order.
                        PrependFollowUps(pendingBodyGearSwapFollowUps, move.FollowUpCandidates);
                    }
                    else
                    {
                        EnqueueBodyGearSwapFollowUps(move);
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
                            // Selection belongs to command completion, after every remaining loot
                            // transaction and the request-layer interaction have released hands.
                            pendingBodyPrimaryWeaponSelection = completedItem as Weapon;
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
                                    EquipmentSlot.SecondPrimaryWeapon,
                                    "bodyLootMove");
                            }
                            else
                            {
                                Weapon slottedHolster = BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                                    ?.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
                                if (IsSameLootItem(slottedHolster, completedItem))
                                {
                                    FollowerLootedPrimaryWeaponBinding.RegisterSupport(
                                        BotOwner,
                                        completedItem as Weapon,
                                        EquipmentSlot.Holster,
                                        "bodyLootMove");
                                }
                            }
                        }

                        RefreshLootedWeaponPresentation(completedItem);
                        bodyLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Body gear move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
                bool failedTopOffDetach = IsInsertedMagazineTopOffDetachMove(move);
                if (failedTopOffDetach)
                {
                    foreach (Item supplyItem in GetInsertedMagazineTopOffSupplyItems(move))
                    {
                        if (!string.IsNullOrEmpty(supplyItem.Id))
                        {
                            bodyLootAttemptedItemIds.Add(supplyItem.Id);
                        }
                    }
                }
                else if (move.IsStagingOperation &&
                    move.TerminalOnStagingFailure &&
                    move.StagingWeapon != null)
                {
                    // A failed insertion leaves the source weapon empty. Mark the weapon terminal
                    // for this search so the planner does not rebuild the same failed transaction.
                    bodyLootAttemptedItemIds.Add(move.StagingWeapon.Id);
                }

                if (move.ContinueFollowUpsOnFailure && !failedTopOffDetach)
                {
                    // P2 weapon chains must still classify the candidate from the resulting live
                    // inventory when one planned fast-access magazine transaction fails.
                    EnqueueBodyGearSwapFollowUps(move);
                }

                RemovePendingAmmoSalvageTransfers(
                    pendingBodyGearSwapFollowUps,
                    move.AmmoSalvageMagazineId);

                bodyLootHadEligibleButNoSpace = true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeBodyGear move completion failed");
                Modules.Logger.LogError(ex);
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                activeBodyLootMoveGeneration = 0;
                activeBodyLootMove = null;
                bodyLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishBodyLootNoMoreMoves()
        {
            Weapon primaryWeaponToSelect = pendingBodyPrimaryWeaponSelection;
            Weapon secondaryWeaponToPromote = pendingBodySecondaryWeaponPromotion;
            pendingBodyPrimaryWeaponSelection = null;
            pendingBodySecondaryWeaponPromotion = null;

            if (bodyLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                bodyLootWeaponListDirty = false;
            }

            TryMarkBodyLootSearchedForBoss();
            InteractableObjects.MarkBodyLootTargetChecked(activeBodyLootCorpse);

            if (bodyLootReportedMovesSucceeded > 0)
            {
                ClearBodyLootState("TakeBodyGear:done");
                QueuePostLootPrimaryWeaponSelection(primaryWeaponToSelect, "bodyLootComplete");
                QueuePostLootSecondaryWeaponPromotion(secondaryWeaponToPromote, "bodyLootSecondaryPromotion");
                return;
            }

            BotOwner.BotTalk.TrySay(bodyLootHadEligibleButNoSpace ? EPhraseTrigger.Negative : EPhraseTrigger.LootNothing, false);
            ClearBodyLootState("TakeBodyGear:noSpace");
            QueuePostLootPrimaryWeaponSelection(primaryWeaponToSelect, "bodyLootComplete");
            QueuePostLootSecondaryWeaponPromotion(secondaryWeaponToPromote, "bodyLootSecondaryPromotion");
        }

        private void CleanupBodyLootInteraction(string reason)
        {
            if (!bodyLootMoveInProgress &&
                pendingBodyLootMove == null &&
                bodyLootReadyAt <= 0f &&
                bodyLootNextMoveAt <= 0f &&
                bodyLootAttemptStartedAt <= 0f &&
                activeBodyLootCorpse == null &&
                activeLootSearchSource == null &&
                !bodyLootHadEligibleButNoSpace &&
                bodyLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            if (bodyLootMoveInProgress &&
                reason?.StartsWith("CommandInterrupt:", StringComparison.Ordinal) == true &&
                activeBodyLootMove != null)
            {
                activeBodyLootMove.TransactionRecoveryAttempted = true;
                TryFastForwardLootInventoryTransaction(
                    BotOwner?.GetPlayer?.InventoryController,
                    activeBodyLootMove,
                    "bodyCombatInterrupt");
            }

            StopLootSearchSound();
            bodyLootMoveInProgress = false;
            activeBodyLootMoveGeneration = 0;
            activeBodyLootMove = null;
            pendingBodyLootMove = null;
            pendingBodyGearSwapFollowUps.Clear();
            ClearAmmoSalvageRuntimeState();
            bodyLootReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            bodyLootAttemptStartedAt = 0f;
            pendingBodyLootMoveReadyAt = 0f;
            bodyLootMovesSucceeded = 0;
            bodyLootReportedMovesSucceeded = 0;
            bodyLootHadEligibleButNoSpace = false;
            bodyLootSuccessSpoken = false;
            bodyLootWeaponListDirty = false;
            pendingBodyPrimaryWeaponSelection = null;
            pendingBodySecondaryWeaponPromotion = null;
            bodyLootBackpackCapacityAttempted = false;
            bodyLootSearchStarted = false;
            activeLootSearchSource = null;
            bodyLootAttemptedItemIds.Clear();
            activeBodyLootCorpse = null;
            followerData?.EndCommittedLootCommand(FollowerCommandType.TakeBodyGear);

            if (BotOwner != null)
            {
                InteractableObjects.RemoveBodyLootTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurBodyLootTarget();
        }

        private void ClearBodyLootState(string reason)
        {
            if (!string.Equals(reason, "TakeBodyGear:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeBodyGear:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Body gear loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupBodyLootInteraction(reason);
            if (string.Equals(reason, "TakeBodyGear:done", StringComparison.Ordinal))
            {
                if (!TryBeginPostLootMove(FollowerCommandType.TakeBodyGear))
                {
                    followerData?.CompleteTakeBodyGear();
                }
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

    }
}
