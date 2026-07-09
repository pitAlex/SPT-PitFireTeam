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
                if (bodyLootAttemptStartedAt > 0f && Time.time - bodyLootAttemptStartedAt > 4f)
                {
                    bodyLootMoveInProgress = false;
                    bodyLootAttemptStartedAt = 0f;
                    bodyLootNextMoveAt = Time.time + 0.25f;
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
            // 1. finish any follow-up magazine move produced by easy weapon equip
            // 2. optionally equip an empty primary slot
            // 3. otherwise loot eligible contents into backpack/pockets only
            if (TryStartPendingBodyGearSwapFollowUpMove(inventory, followerEquipment))
            {
                return;
            }

            if (TryStartEasyBodyWeaponEquipMove(inventory, corpseEquipment, followerEquipment))
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

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                if (!TryBuildFilteredLootMove(inventory, followerEquipment, candidate, null, out BodyGearMove? move))
                {
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
                    candidate.Item.Parent.Equals(packAddress))
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
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate.Item == null)
            {
                return false;
            }

            // Filtered looting never uses the follower rig for generic cargo. The only vest writes
            // here are tactical: an easy primary weapon's first operational magazine.
            if (ShouldUseFilteredLootEquipmentSlot(candidate) &&
                TryBuildEasyWeaponEquipMove(inventory, followerEquipment, candidate, operationalMagazineCandidates, out move))
            {
                return true;
            }

            if (ShouldUseFilteredLootEquipmentSlot(candidate) &&
                TryFindFilteredWeaponCargoEquipmentSlot(followerEquipment, candidate, out ItemAddress? equipAddress) &&
                TryCreateBodyGearMove(inventory, candidate, equipAddress, out move))
            {
                return true;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetFilteredLootCarryContainers(followerEquipment))
            {
                if (!container.TryFindLocationForItem(candidate.Item, out ItemAddress packAddress) ||
                    candidate.Item.Parent.Equals(packAddress))
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

        private bool TryStartEasyBodyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetFilteredBodyLootCandidates(corpseEquipment).Where(IsEasyWeaponEquipCandidate))
            {
                if (!CanConsiderFilteredLootCandidate(candidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                IEnumerable<BodyGearCandidate> magazineCandidates = GetBodyOperationalMagazineCandidates(corpseEquipment, (Weapon)candidate.Item);
                if (!TryBuildEasyWeaponEquipMove(inventory, followerEquipment, candidate, magazineCandidates, out BodyGearMove? move))
                {
                    continue;
                }

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartBodyGearMove(inventory, move);
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

            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                         containerRoot,
                         "Container.Contents",
                         skipMagazines: false).Where(IsEasyWeaponEquipCandidate))
            {
                if (!CanConsiderFilteredLootCandidate(candidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                IEnumerable<BodyGearCandidate> magazineCandidates = GetContainerOperationalMagazineCandidates(containerRoot, (Weapon)candidate.Item);
                if (!TryBuildEasyWeaponEquipMove(inventory, followerEquipment, candidate, magazineCandidates, out BodyGearMove? move))
                {
                    continue;
                }

                containerLootAttemptedItemIds.Add(candidate.Item.Id);
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

        private bool TryBuildEasyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            out BodyGearMove? move)
        {
            move = null;
            // Phase 3 starts with empty-slot weapon equip only. Replacing an existing primary
            // weapon is intentionally deferred because bot weapon/reload state is cached elsewhere.
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                candidate?.Item is not Weapon weapon ||
                !IsEasyWeaponEquipCandidate(candidate) ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null ||
                !TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.FirstPrimaryWeapon, weapon, out ItemAddress? firstPrimaryAddress))
            {
                return false;
            }

            List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
            BodyGearCandidate? magazineCandidate = FindFirstOperationalMagazineCandidate(
                inventory,
                followerEquipment,
                weapon,
                operationalMagazineCandidates);

            // A primary weapon is only combat-useful if it is already full enough or one compatible
            // loaded mag can move into the vest. Otherwise it falls back to normal cargo handling.
            if (magazineCandidate != null)
            {
                followUps.Add(magazineCandidate);
            }

            if (!IsWeaponLoadedEnoughForPrimary(weapon) && magazineCandidate == null)
            {
                return false;
            }

            if (!TryCreateBodyGearMove(inventory, candidate, firstPrimaryAddress, out BodyGearMove? primaryMove))
            {
                return false;
            }

            move = primaryMove.WithFollowUps(followUps);
            return true;
        }

        private bool TryStartPendingBodyGearSwapFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingBodyGearSwapFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingBodyGearSwapFollowUps.Dequeue();
                if (!TryBuildOperationalMagazineMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                {
                    continue;
                }

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
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
                if (!TryBuildOperationalMagazineMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                {
                    continue;
                }

                containerLootAttemptedItemIds.Add(candidate.Item.Id);
                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryBuildOperationalMagazineMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate?.Item is not MagazineItemClass ||
                string.IsNullOrEmpty(candidate.Item.Id) ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item) ||
                !CanConsiderFilteredLootCandidate(candidate, new HashSet<string>(StringComparer.Ordinal)))
            {
                return false;
            }

            return TryFindOperationalMagazineVestAddress(followerEquipment, candidate.Item, out ItemAddress? address) &&
                   TryCreateBodyGearMove(inventory, candidate, address, out move);
        }

        private static bool ShouldUseFilteredLootEquipmentSlot(BodyGearCandidate candidate)
        {
            return candidate?.Item is Weapon weapon && weapon.GetItemComponent<KnifeComponent>() == null;
        }

        private bool TryCreateBodyGearMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            ItemAddress address,
            out BodyGearMove? move)
        {
            move = null;
            Item item = candidate?.Item;
            if (item == null)
            {
                return false;
            }

            GStruct154<GClass3411> moveResult = InteractionsHandlerClass.Move(item, address, inventory, true);
            if (moveResult.Failed || moveResult.Value.ItemsDestroyRequired || !inventory.CanExecute(moveResult.Value))
            {
                return false;
            }

            move = new BodyGearMove(item, moveResult.Value, candidate.SourceName, candidate.ReportAsLootNothing);
            return true;
        }

        private void StartBodyGearMove(InventoryController inventory, BodyGearMove move)
        {
            bodyLootMoveInProgress = true;
            bodyLootAttemptStartedAt = Time.time;
            inventory.RunNetworkTransaction(move.Operation, new Callback(result => CompleteBodyGearMove(result, move)));
        }

        private void EnqueueBodyGearSwapFollowUps(BodyGearMove move)
        {
            if (move?.FollowUpCandidates == null || move.FollowUpCandidates.Count == 0)
            {
                return;
            }

            foreach (BodyGearCandidate candidate in move.FollowUpCandidates)
            {
                if (candidate?.Item != null &&
                    !string.IsNullOrEmpty(candidate.Item.Id) &&
                    !bodyLootAttemptedItemIds.Contains(candidate.Item.Id))
                {
                    pendingBodyGearSwapFollowUps.Enqueue(candidate);
                }
            }
        }

        private void CompleteBodyGearMove(IResult result, BodyGearMove move)
        {
            try
            {
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                bodyLootNextMoveAt = Time.time + 0.2f;

                if (result?.Succeed == true || IsLootNowInBotInventory(BotOwner?.GetPlayer, move.Item))
                {
                    bodyLootMovesSucceeded++;
                    if (!move.ReportAsLootNothing && !IsDogtagLoot(move.Item))
                    {
                        bodyLootReportedMovesSucceeded++;
                    }

                    InteractableObjects.RegisterLootedWeaponTree(BotOwner, move.Item);
                    EnqueueBodyGearSwapFollowUps(move);

                    if (followerData?.IsSquadMate == true)
                    {
                        InteractableObjects.StoreItem(BotOwner, move.Item);
                    }

                    if (move.Item is Weapon && move.Item.GetItemComponent<KnifeComponent>() == null)
                    {
                        RefreshLootedWeaponPresentation(move.Item);
                        bodyLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Body gear move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeBodyGear move completion failed");
                Modules.Logger.LogError(ex);
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                bodyLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishBodyLootNoMoreMoves()
        {
            if (bodyLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                bodyLootWeaponListDirty = false;
            }

            TryMarkBodyLootSearchedForBoss();

            if (bodyLootReportedMovesSucceeded > 0)
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                ClearBodyLootState("TakeBodyGear:done");
                return;
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.LootNothing, false);
            ClearBodyLootState("TakeBodyGear:noSpace");
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
                bodyLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            StopLootSearchSound();
            bodyLootMoveInProgress = false;
            pendingBodyLootMove = null;
            pendingBodyGearSwapFollowUps.Clear();
            bodyLootReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            bodyLootAttemptStartedAt = 0f;
            pendingBodyLootMoveReadyAt = 0f;
            bodyLootMovesSucceeded = 0;
            bodyLootReportedMovesSucceeded = 0;
            bodyLootGenericSpoken = false;
            bodyLootWeaponListDirty = false;
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
                followerData?.CompleteTakeBodyGear();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

    }
}
