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
        // Loose item pickup is a direct "pick this up" command, not the body/container looting
        // scenario. It stays separate from loot-search voice, delay, and filtering rules.
        private void HandleTakeLootItem()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                ClearTakeLootState(guardFailureReason ?? "TakeLoot:invalidState");
                return;
            }

            activeLootItem ??= InteractableObjects.GetAssignedLootItem(BotOwner) ?? InteractableObjects.GetCurLootItem();
            if (activeLootItem == null)
            {
                ClearTakeLootState("TakeLoot:itemMissing");
                return;
            }

            if (TryFinishTransferredLoot("TakeLoot:detectedInInventory"))
            {
                return;
            }

            // Keep the selected loot item pinned for this command so brief quick-panel target changes
            // don't drop execution ownership mid-pickup.
            if (InteractableObjects.GetCurLootItem() == null)
            {
                InteractableObjects.SetCurLootItem(activeLootItem);
            }

            Vector3 lootPosition;
            try
            {
                lootPosition = InteractableObjects.GetLootPosition(BotOwner);
            }
            catch
            {
                ClearTakeLootState("TakeLoot:missingLootPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, lootPosition);
            if (distance > 1.75f)
            {
                lootPickupReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(lootPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeLootItem.transform.position);

            if (lootPickupInProgress)
            {
                if (TryFinishTransferredLoot("TakeLoot:detectedInInventoryDuringPickup"))
                {
                    return;
                }

                if (lootPickupAttemptStartedAt > 0f && Time.time - lootPickupAttemptStartedAt > 3f)
                {
                    StopLootPickupState(BotOwner?.GetPlayer);
                    lootPickupInProgress = false;
                    lootPickupAttemptStartedAt = 0f;
                    lootPickupReadyAt = Time.time + 0.35f;
                }

                return;
            }

            if (lootPickupReadyAt <= 0f)
            {
                lootPickupReadyAt = Time.time + 0.35f;
                return;
            }

            if (Time.time < lootPickupReadyAt)
            {
                return;
            }

            StartLootPickup(activeLootItem);
        }

        private bool CanContinueLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeLoot:botInvalid";
                return false;
            }

            if (!InteractableObjects.IsTaker(BotOwner))
            {
                // Taker ownership can be cleared transiently; try to recover once before aborting.
                if (!InteractableObjects.SetTaker(BotOwner) || !InteractableObjects.IsTaker(BotOwner))
                {
                    reason = "TakeLoot:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeLoot:enemy";
                return false;
            }

            return true;
        }

        private bool TryGetLootExecutionContext(
            LootItem? lootItem,
            out Player? botPlayer,
            out InventoryController? inventory,
            out Item? rootItem,
            out string reason)
        {
            botPlayer = null;
            inventory = null;
            rootItem = null;

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeLoot:invalidState";
                return false;
            }

            if (lootItem == null || lootItem.gameObject == null)
            {
                reason = "TakeLoot:itemMissing";
                return false;
            }

            TraderControllerClass? itemOwner = lootItem.ItemOwner;
            rootItem = itemOwner?.RootItem;
            if (rootItem == null)
            {
                reason = "TakeLoot:itemNull";
                return false;
            }

            botPlayer = BotOwner.GetPlayer;
            inventory = botPlayer?.InventoryController;
            if (botPlayer == null || inventory == null)
            {
                reason = "TakeLoot:noInventory";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void StartLootPickup(LootItem lootItem)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? inventory, out Item? rootItem, out string reason))
                {
                    ClearTakeLootState(reason);
                    return;
                }

                var pickupResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                    rootItem,
                    inventory,
                    inventory.Inventory.Equipment.ToEnumerable<InventoryEquipment>(),
                    InteractionsHandlerClass.EMoveItemOrder.PickUp,
                    true);

                if (!pickupResult.Succeeded)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    ClearTakeLootState("TakeLoot:noSpace");
                    return;
                }

                if (!inventory.CanExecute(pickupResult.Value))
                {
                    ClearTakeLootState("TakeLoot:cannotExecute");
                    return;
                }

                lootPickupInProgress = true;
                lootPickupAttemptStartedAt = Time.time;
                botPlayer.SaveInteractionRayInfo();
                try
                {
                    botPlayer.CurrentManagedState.Pickup(true, () => ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupResult.Value));
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("TakeLoot pickup animation failed; falling back to direct inventory transaction");
                    Modules.Logger.LogError(ex);
                    StopLootPickupState(botPlayer);
                    ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupResult.Value);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot start failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:startException");
            }
        }

        private void ExecuteLootPickupTransaction(LootItem lootItem, Item rootItem, InventoryController inventory, GInterface424 pickupAction)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? currentInventory, out Item? currentRootItem, out string reason))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState(reason);
                    return;
                }

                if (!ReferenceEquals(currentInventory, inventory) || !ReferenceEquals(currentRootItem, rootItem))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState("TakeLoot:itemChanged");
                    return;
                }

                if (pickupAction is GInterface427 moveAction)
                {
                    ItemAddress currentAddress = rootItem.CurrentAddress;
                    if (currentAddress == null || !moveAction.From.Equals(currentAddress))
                    {
                        StopLootPickupState(botPlayer);
                        ClearTakeLootState("TakeLoot:itemMoved");
                        return;
                    }
                }

                inventory.RunNetworkTransaction(pickupAction, new Callback(result => CompleteLootPickup(result, botPlayer, rootItem)));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot transaction failed");
                Modules.Logger.LogError(ex);
                StopLootPickupState(BotOwner?.GetPlayer);
                ClearTakeLootState("TakeLoot:transactionException");
            }
        }

        private void CompleteLootPickup(IResult result, Player? botPlayer, Item rootItem)
        {
            try
            {
                if (result?.Succeed == true || IsLootNowInBotInventory(botPlayer, rootItem))
                {
                    FinishLootPickupSuccess(botPlayer, rootItem, "TakeLoot:done");
                    return;
                }

                BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                ClearTakeLootState("TakeLoot:transactionFailed");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot completion failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:completionException");
            }
            finally
            {
                StopLootPickupState(botPlayer);
            }
        }

        private bool TryFinishTransferredLoot(string reason)
        {
            Item? rootItem = activeLootItem?.ItemOwner?.RootItem;
            Player? botPlayer = BotOwner?.GetPlayer;
            if (rootItem == null || !IsLootNowInBotInventory(botPlayer, rootItem))
            {
                return false;
            }

            FinishLootPickupSuccess(botPlayer, rootItem, reason);
            return true;
        }

        private void FinishLootPickupSuccess(Player? botPlayer, Item rootItem, string reason)
        {
            botPlayer?.UpdateInteractionCast();
            InteractableObjects.RegisterLootedWeaponTree(BotOwner, rootItem);

            if (followerData?.IsSquadMate == true)
            {
                InteractableObjects.StoreItem(BotOwner, rootItem);
            }

            if (rootItem is Weapon weapon && rootItem.GetItemComponent<KnifeComponent>() == null)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();

                Weapon slottedPrimary = BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                    ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
                if (IsSameLootItem(slottedPrimary, weapon))
                {
                    // EFT's loose-item pickup can physically fill an empty primary slot without
                    // rebuilding the bot's spawn-time weapon info. Use the same rebind as commanded
                    // body/container equip so the carried weapon becomes a real combat primary.
                    RebindLootedPrimaryWeapon(weapon);
                }
            }

            ClearTakeLootState(reason);
        }

        private void RefreshLootedWeaponPresentation(Item item)
        {
            if (item is not Weapon || item.GetItemComponent<KnifeComponent>() != null)
            {
                return;
            }

            try
            {
                BotOwner?.WeaponManager?.UpdateWeaponsList();
                ForceRefreshLootedItemIcon(item);
                RefreshLootedWeaponWorldModel(item);
                item.RaiseRefreshEvent(true, true);

                // EFT icon generation can still be resolving right after a bot-side move transaction.
                Utils.Utils.SetTimeout(() =>
                {
                    ForceRefreshLootedItemIcon(item);
                    RefreshLootedWeaponWorldModel(item);
                    item.RaiseRefreshEvent(true, true);
                }, 250);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to refresh looted weapon presentation: {ex.Message}");
            }
        }

        private void RefreshLootedWeaponWorldModel(Item item)
        {
            try
            {
                // Bot-side move transactions can leave the slot view holding the old assembled model.
                // Replaying the child-change path forces EFT to rebuild the equipped weapon from the
                // current tree, which prevents loose mags/mods appearing as floating world parts.
                item.ChildrenChanged.Invoke(item);

                PlayerBody.EquipmentSlotClass? slotView = BotOwner?.GetPlayer?.PlayerBody?.GetSlotViewByItem(item);
                if (slotView == null)
                {
                    return;
                }

                slotView.method_3();
                slotView.method_0();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to refresh looted weapon world model: {ex.Message}");
            }
        }

        private static void ForceRefreshLootedItemIcon(Item item)
        {
            try
            {
                ItemViewFactory.LoadItemIcon(item, 1, true);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to regenerate looted item icon: {ex.Message}");
            }
        }

        private static bool IsDogtagLoot(Item item)
        {
            return item?.GetItemComponent<DogtagComponent>() != null;
        }

        private bool TryBeginLootPickupSuccessLead(BodyGearMove move, ref bool alreadySpoken)
        {
            if (alreadySpoken ||
                move == null ||
                move.ReportAsLootNothing ||
                IsDogtagLoot(move.Item))
            {
                return false;
            }

            alreadySpoken = true;
            SayLootPhrase(move.SuccessPhrase);
            return true;
        }

        private bool TryQueueBodyLootMoveAfterPickupSuccess(BodyGearMove move)
        {
            if (!TryBeginLootPickupSuccessLead(move, ref bodyLootSuccessSpoken))
            {
                return false;
            }

            pendingBodyLootMove = move;
            pendingBodyLootMoveReadyAt = Time.time + LootPickupSuccessLeadSeconds;
            bodyLootNextMoveAt = pendingBodyLootMoveReadyAt;
            return true;
        }

        private bool TryQueueContainerLootMoveAfterPickupSuccess(BodyGearMove move)
        {
            if (!TryBeginLootPickupSuccessLead(move, ref containerLootSuccessSpoken))
            {
                return false;
            }

            pendingContainerLootMove = move;
            pendingContainerLootMoveReadyAt = Time.time + LootPickupSuccessLeadSeconds;
            containerLootNextMoveAt = pendingContainerLootMoveReadyAt;
            return true;
        }

        private void SayLootPhrase(EPhraseTrigger trigger)
        {
            try
            {
                BotOwner?.BotTalk?.DropNextSayPeriod();
                BotOwner?.BotTalk?.Say(trigger, true);
            }
            catch
            {
                // Voice feedback should never block a completed inventory transaction.
            }
        }

        private bool IsLootNowInBotInventory(Player? botPlayer, Item rootItem)
        {
            InventoryController? inventory = botPlayer?.InventoryController ?? BotOwner?.GetPlayer?.InventoryController;
            if (inventory == null || rootItem == null || string.IsNullOrEmpty(rootItem.Id))
            {
                return false;
            }

            return inventory.TryFindItem(rootItem.Id, out Item foundItem) &&
                   ReferenceEquals(foundItem, rootItem);
        }

        private static void StopLootPickupState(Player? botPlayer)
        {
            try
            {
                if (botPlayer == null)
                {
                    return;
                }

                if (botPlayer.MovementContext != null)
                {
                    botPlayer.MovementContext.PickupAction = null;
                }

                if (botPlayer.CurrentManagedState is PickupStateClass pickupState)
                {
                    pickupState.Pickup(false, null);
                }
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        private void CleanupLootInteraction(string reason)
        {
            if (!lootPickupInProgress &&
                lootPickupReadyAt <= 0f &&
                lootPickupAttemptStartedAt <= 0f &&
                activeLootItem == null &&
                BotOwner?.GetPlayer?.CurrentManagedState is not PickupStateClass)
            {
                return;
            }

            StopLootPickupState(BotOwner?.GetPlayer);
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;

            if (BotOwner != null)
            {
                InteractableObjects.RemoveTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurLootItem();
        }

        private void ClearTakeLootState(string reason)
        {
            if (!string.Equals(reason, "TakeLoot:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:detectedInInventory", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:detectedInInventoryDuringPickup", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Take loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupLootInteraction(reason);
            if (IsTakeLootSuccess(reason))
            {
                followerData?.CompleteTakeLootItem();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

        private static bool IsTakeLootSuccess(string reason)
        {
            return string.Equals(reason, "TakeLoot:done", StringComparison.Ordinal) ||
                   string.Equals(reason, "TakeLoot:detectedInInventory", StringComparison.Ordinal) ||
                   string.Equals(reason, "TakeLoot:detectedInInventoryDuringPickup", StringComparison.Ordinal);
        }

    }
}
