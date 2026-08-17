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

                if (rootItem is Weapon supportWeapon &&
                    IsShoulderWeaponCandidate(supportWeapon) &&
                    TryPrepareLoosePickupWeaponFromBackpack(
                        supportWeapon,
                        inventory,
                        inventory.Inventory.Equipment))
                {
                    return;
                }

                if (!TryBuildLootPickupOperation(
                        rootItem,
                        inventory,
                        out GInterface424? pickupOperation,
                        out string pickupFailureReason))
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    ClearTakeLootState($"TakeLoot:{pickupFailureReason}");
                    return;
                }

                lootPickupInProgress = true;
                lootPickupAttemptStartedAt = Time.time;
                botPlayer.SaveInteractionRayInfo();
                try
                {
                    botPlayer.CurrentManagedState.Pickup(true, () => ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupOperation));
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("TakeLoot pickup animation failed; falling back to direct inventory transaction");
                    Modules.Logger.LogError(ex);
                    StopLootPickupState(botPlayer);
                    ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupOperation);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot start failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:startException");
            }
        }

        private bool TryBuildLootPickupOperation(
            Item rootItem,
            InventoryController inventory,
            out GInterface424? operation,
            out string failureReason)
        {
            operation = null;
            failureReason = "noSpace";
            if (rootItem == null || inventory?.Inventory?.Equipment == null)
            {
                failureReason = "noInventory";
                return false;
            }

            if (rootItem is Weapon weapon && IsShoulderWeaponCandidate(weapon))
            {
                // A direct player order owns its physical destination even when automatic gear
                // swapping is disabled. The shoulder used must truthfully communicate whether the
                // bot considers this weapon usable or merely held for later.
                return TryBuildLooseWeaponPickupOperation(
                    weapon,
                    inventory,
                    inventory.Inventory.Equipment,
                    out operation,
                    out failureReason);
            }

            if (rootItem is MagazineItemClass commandedMagazine &&
                pitFireTeam.IsLootGearSwappingEnabled() &&
                TryResolveCommandedMagazineOwner(
                    inventory.Inventory.Equipment,
                    commandedMagazine,
                    out _,
                    out _))
            {
                // Compatible weapon magazines may never consume the landing space needed to
                // eject an equipped magazine during reload. Failure here must not fall through
                // to EFT's unrestricted placement search.
                return TryBuildReloadSafeCommandedMagazinePickup(
                    commandedMagazine,
                    inventory,
                    inventory.Inventory.Equipment,
                    out operation,
                    out failureReason);
            }

            GStruct154<GInterface424> pickupResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                rootItem,
                inventory,
                inventory.Inventory.Equipment.ToEnumerable<InventoryEquipment>(),
                InteractionsHandlerClass.EMoveItemOrder.PickUp,
                true);
            if (!pickupResult.Succeeded)
            {
                return false;
            }

            if (!inventory.CanExecute(pickupResult.Value))
            {
                failureReason = "cannotExecute";
                return false;
            }

            operation = pickupResult.Value;
            return true;
        }

        private bool TryBuildLooseWeaponPickupOperation(
            Weapon weapon,
            InventoryController inventory,
            InventoryEquipment equipment,
            out GInterface424? operation,
            out string failureReason)
        {
            operation = null;
            failureReason = "noSafeWeaponDestination";
            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon,
                ammo => !InteractableObjects.IsStrictCargoItem(BotOwner, ammo));
            bool insertedAloneIsSufficient = readiness.InsertedContribution >= readiness.Threshold;
            bool hasReloadLandingSpace = insertedAloneIsSufficient ||
                                         FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(
                                             equipment,
                                             weapon);

            // Right shoulder is reserved for a weapon the bot will actually register and use.
            if (readiness.PrimaryReady &&
                !readiness.RequiresMagazineLoad &&
                hasReloadLandingSpace &&
                TryBuildLooseWeaponPickupMove(
                    weapon,
                    inventory,
                    equipment,
                    EquipmentSlot.FirstPrimaryWeapon,
                    out operation))
            {
                LogLooseWeaponPickupDestination(weapon, readiness, "FirstPrimaryWeapon", "ready");
                return true;
            }

            // Left shoulder is the visible holding state for a weapon that is not yet primary-ready.
            if (TryBuildLooseWeaponPickupMove(
                    weapon,
                    inventory,
                    equipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    out operation))
            {
                LogLooseWeaponPickupDestination(
                    weapon,
                    readiness,
                    "SecondPrimaryWeapon",
                    readiness.PrimaryReady ? "primaryUnavailable" : readiness.Reason);
                return true;
            }

            if (TryFindBackpackAddressForItem(equipment, weapon, out ItemAddress? backpackAddress) &&
                TryBuildLootPickupMove(weapon, backpackAddress, inventory, out operation))
            {
                LogLooseWeaponPickupDestination(
                    weapon,
                    readiness,
                    "BackpackCargo",
                    readiness.PrimaryReady ? "primaryAndSecondaryUnavailable" : readiness.Reason);
                return true;
            }

            // If no honest fallback exists, a reasonably loaded weapon may use the right shoulder
            // as a last resort. A physical first-primary weapon must then be registered as such;
            // otherwise the bot's visible equipment would disagree with its combat weapon state.
            if (HasSafeForcedPrimaryMagazine(readiness, out int minimumSafeRounds) &&
                TryBuildLooseWeaponPickupMove(
                    weapon,
                    inventory,
                    equipment,
                    EquipmentSlot.FirstPrimaryWeapon,
                    out operation))
            {
                LogLooseWeaponPickupDestination(
                    weapon,
                    readiness,
                    "FirstPrimaryWeapon",
                    $"forcedPrimaryFallback;minimumSafeRounds={minimumSafeRounds}");
                return true;
            }

            LogLooseWeaponPickupDestination(
                weapon,
                readiness,
                "Source",
                $"noSafeDestination;minimumSafeRounds={GetForcedPrimaryMinimumSafeRounds(readiness)}");
            return false;
        }

        private static bool TryBuildLooseWeaponPickupMove(
            Weapon weapon,
            InventoryController inventory,
            InventoryEquipment equipment,
            EquipmentSlot destination,
            out GInterface424? operation)
        {
            operation = null;
            return TryFindEquipmentSlotAddress(equipment, destination, weapon, out ItemAddress? address) &&
                   TryBuildLootPickupMove(weapon, address, inventory, out operation);
        }

        private static bool TryBuildLootPickupMove(
            Item item,
            ItemAddress address,
            InventoryController inventory,
            out GInterface424? operation)
        {
            operation = null;
            GStruct154<GClass3411> moveResult = InteractionsHandlerClass.Move(item, address, inventory, true);
            if (moveResult.Failed ||
                moveResult.Value.ItemsDestroyRequired ||
                !inventory.CanExecute(moveResult.Value))
            {
                return false;
            }

            operation = moveResult.Value;
            return true;
        }

        private static bool HasSafeForcedPrimaryMagazine(
            WeaponPrimaryReadinessSnapshot readiness,
            out int minimumSafeRounds)
        {
            minimumSafeRounds = GetForcedPrimaryMinimumSafeRounds(readiness);
            return readiness?.HasInsertedMagazine == true &&
                   readiness.InsertedRounds >= minimumSafeRounds;
        }

        private static int GetForcedPrimaryMinimumSafeRounds(WeaponPrimaryReadinessSnapshot readiness)
        {
            if (readiness == null || !readiness.HasInsertedMagazine)
            {
                return int.MaxValue;
            }

            int insertedCapacity = Math.Max(0, readiness.InsertedCapacity);
            int ordinaryReference = Math.Max(0, readiness.OrdinaryReference);
            int usableBasis = insertedCapacity > 0 && ordinaryReference > 0
                ? Math.Min(insertedCapacity, ordinaryReference)
                : Math.Max(insertedCapacity, ordinaryReference);
            return Math.Max(1, (usableBasis + 1) / 2);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void LogLooseWeaponPickupDestination(
            Weapon weapon,
            WeaponPrimaryReadinessSnapshot readiness,
            string destination,
            string reason)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=loosePickup destination={destination} " +
                $"decisionReason={reason} {readiness?.ToDiagnosticString() ?? "readinessMissing"}");
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
                    StopLootPickupState(botPlayer);
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

            StopLootPickupState(botPlayer);
            FinishLootPickupSuccess(botPlayer, rootItem, reason);
            return true;
        }

        private void FinishLootPickupSuccess(Player? botPlayer, Item rootItem, string reason)
        {
            botPlayer?.UpdateInteractionCast();
            InteractableObjects.ClearStrictCargoTree(BotOwner, rootItem);
            InteractableObjects.RegisterLootedWeaponTree(BotOwner, rootItem);
            RegisterCommandedLooseMagazineForEquippedWeapon(rootItem);

            Weapon? primaryWeaponToBind = null;

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
                    primaryWeaponToBind = weapon;
                }
            }

            ClearTakeLootState(reason);
            if (primaryWeaponToBind != null)
            {
                QueueLoosePickupPrimaryWeaponBinding(primaryWeaponToBind);
            }
        }

        private void RegisterCommandedLooseMagazineForEquippedWeapon(Item item)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                item is not MagazineItemClass magazine ||
                magazine.Parent == null)
            {
                return;
            }

            InventoryEquipment equipment = BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment;
            Item vest = equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            Item pockets = equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem;
            if (!IsItemInsideRoot(magazine, vest) && !IsItemInsideRoot(magazine, pockets))
            {
                // A commanded magazine that lands in the backpack remains cargo until a later
                // command can prove a reload-safe fast-access move for it.
                return;
            }

            if (!TryResolveCommandedMagazineOwner(equipment, magazine, out Weapon? weapon, out EquipmentSlot slot))
            {
                return;
            }

            InteractableObjects.RegisterLootedWeaponMagazine(BotOwner, weapon, magazine);
            Modules.Logger.LogInfo(
                $"[LootCommand][WeaponReload] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} magazine={DescribeLootDebugItem(magazine)} " +
                $"result=commandedMagazineAssigned slot={slot}");
        }

        private static bool TryResolveCommandedMagazineOwner(
            InventoryEquipment equipment,
            MagazineItemClass magazine,
            out Weapon? weapon,
            out EquipmentSlot slot)
        {
            weapon = null;
            slot = EquipmentSlot.FirstPrimaryWeapon;
            if (equipment == null || magazine == null)
            {
                return false;
            }

            foreach (EquipmentSlot candidateSlot in new[]
                     {
                         EquipmentSlot.FirstPrimaryWeapon,
                         EquipmentSlot.SecondPrimaryWeapon
                     })
            {
                Weapon candidateWeapon = equipment.GetSlot(candidateSlot)?.ContainedItem as Weapon;
                if (candidateWeapon == null ||
                    candidateWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine ||
                    !IsMagazineCompatibleWithWeapon(candidateWeapon, magazine) ||
                    (magazine.Count > 0 &&
                     !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(candidateWeapon, magazine)))
                {
                    continue;
                }

                // First primary deliberately wins when both equipped weapons accept the same mag.
                weapon = candidateWeapon;
                slot = candidateSlot;
                return true;
            }

            return false;
        }

        private static bool TryBuildReloadSafeCommandedMagazinePickup(
            MagazineItemClass magazine,
            InventoryController inventory,
            InventoryEquipment equipment,
            out GInterface424? operation,
            out string failureReason)
        {
            operation = null;
            failureReason = "noReloadSafeSpace";

            SearchableItemItemClass simulatedVest = CloneSearchableContainer(
                equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
            SearchableItemItemClass simulatedPockets = CloneSearchableContainer(
                equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem);
            List<MagazineItemClass> reloadReserves = GetCommandedMagazineReloadReserve(equipment);

            if (TrySimulateFastAccessAddWithReserves(
                    simulatedVest,
                    simulatedPockets,
                    magazine,
                    reloadReserves,
                    out _,
                    out _,
                    out BodyGearFollowUpDestination destination))
            {
                ItemAddress? fastAccessAddress;
                bool foundAddress = destination == BodyGearFollowUpDestination.OperationalVest
                    ? TryFindOperationalMagazineVestAddress(equipment, magazine, out fastAccessAddress)
                    : TryFindOperationalMagazinePocketsAddress(equipment, magazine, out fastAccessAddress);
                if (foundAddress && TryBuildLootPickupMove(magazine, fastAccessAddress, inventory, out operation))
                {
                    failureReason = "ok";
                    return true;
                }
            }

            // A commanded magazine that would consume reload landing space remains useful cargo.
            if (TryFindBackpackAddressForItem(equipment, magazine, out ItemAddress? backpackAddress) &&
                TryBuildLootPickupMove(magazine, backpackAddress, inventory, out operation))
            {
                failureReason = "ok";
                return true;
            }

            return false;
        }

        private static List<MagazineItemClass> GetCommandedMagazineReloadReserve(InventoryEquipment equipment)
        {
            List<MagazineItemClass> reserves = new List<MagazineItemClass>();
            foreach (EquipmentSlot slot in new[]
                     {
                         EquipmentSlot.FirstPrimaryWeapon,
                         EquipmentSlot.SecondPrimaryWeapon
                     })
            {
                Weapon weapon = equipment?.GetSlot(slot)?.ContainedItem as Weapon;
                if (weapon?.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
                {
                    continue;
                }

                MagazineItemClass inserted = GetCurrentMagazineSafely(weapon);
                if (inserted != null)
                {
                    reserves.Add(inserted);
                }
            }

            // One shared landing opening is sufficient because weapon reloads are sequential.
            return NormalizeReloadReserveItems(reserves)
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(magazine => magazine.MaxCount)
                .Take(1)
                .ToList();
        }

        private void QueueLoosePickupPrimaryWeaponBinding(Weapon weapon)
        {
            FollowerLootedPrimaryWeaponBinding.SelectAfterLootCompletion(
                BotOwner,
                weapon,
                "loosePickup");
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
                (move.IsStagingOperation && !move.AnnounceStagingLoot) ||
                (move.ReportAsLootNothing && move.SuccessPhrase != EPhraseTrigger.LootWeapon) ||
                IsDogtagLoot(move.Item))
            {
                return false;
            }

            // A usable-weapon plan may begin with a support-ammo move that is intentionally
            // excluded from the final loot count. The plan still needs its LootWeapon cue before
            // those transactions begin; ReportAsLootNothing only silences standalone hidden loot.
            alreadySpoken = true;
            SayLootPhrase(move.SuccessPhrase);
            Modules.Logger.LogInfo(
                $"[LootCommand][Voice] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"cue={move.SuccessPhrase} result=spoken");
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

                botPlayer.CurrentManagedState?.Pickup(false, null);
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
            ResetLoosePickupBackpackSupport();

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
                if (!TryBeginPostLootMove(FollowerCommandType.TakeLootItem))
                {
                    followerData?.CompleteTakeLootItem();
                }
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
