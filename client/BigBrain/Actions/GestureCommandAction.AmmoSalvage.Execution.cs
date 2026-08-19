using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using System;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryBuildAmmoSalvageMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidTransfer";
            if (candidate?.Item is not AmmoItemClass ammo ||
                candidate.AmmoSalvageMagazine is not MagazineItemClass magazine ||
                candidate.AmmoSalvageWeapon is not Weapon weapon ||
                ammo.StackObjectsCount <= 0 ||
                !IsItemInsideRoot(ammo, magazine))
            {
                return false;
            }

            // Marker planning and execution are separated by real EFT transactions. Recheck the
            // adoption boundary for every seed/fill step so queued work cannot consume source ammo
            // after the weapon has left FirstPrimaryWeapon.
            if (!IsWeaponEquippedAsPrimaryForAmmoSalvage(followerEquipment, weapon))
            {
                reason = "weaponNotPrimary";
                return false;
            }

            if (!IsSameLootItem(magazine.Cartridges.Last, ammo))
            {
                reason = "sourceStackNotOnTop";
                return false;
            }

            if (candidate.FollowUpDestination == BodyGearFollowUpDestination.SalvagedAmmoStackTransfer)
            {
                return TryBuildAmmoStackConsolidationMove(
                    inventory,
                    candidate,
                    ammo,
                    magazine,
                    out move,
                    out reason);
            }

            if (candidate.FollowUpDestination == BodyGearFollowUpDestination.SalvagedAmmoVest &&
                !CanPlaceAmmoInVestWithReloadReserve(followerEquipment, ammo))
            {
                reason = "vestReloadReserveChanged";
                return false;
            }

            if (!TryGetAmmoSalvageEquipmentSlot(candidate.FollowUpDestination, out EquipmentSlot destinationSlot) ||
                !TryFindDirectEquipmentContainerAddress(
                    followerEquipment,
                    destinationSlot,
                    ammo,
                    out ItemAddress? destinationAddress))
            {
                reason = "destinationNoLongerFits";
                return false;
            }

            return TryBuildAmmoStackSeedMove(
                inventory,
                candidate,
                ammo,
                magazine,
                destinationAddress,
                out move,
                out reason);
        }

        private bool TryBuildAmmoStackSeedMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            AmmoItemClass source,
            MagazineItemClass magazine,
            ItemAddress destinationAddress,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidStackSeed";
            int requestedCount = candidate.AmmoSalvageTransferCount;
            if (requestedCount <= 0 || requestedCount > source.StackObjectsCount)
            {
                return false;
            }

            // Vanilla unloading creates or selects a loose stack and transfers one cartridge at a
            // time. Seed one round through the same ammo API, then fill that stack in a follow-up.
            GStruct154<GInterface433> seedResult = AmmoItemClass.ApplyToAddress(
                source,
                destinationAddress,
                1,
                inventory,
                true);
            if (seedResult.Failed)
            {
                reason = $"seedRejected:{DescribeInventoryError(seedResult.Error)}";
                return false;
            }

            if (seedResult.Value is not GInterface424 operation)
            {
                reason = "seedOperationUnsupported";
                return false;
            }

            AmmoItemClass seedTarget = operation.ResultItem as AmmoItemClass;
            if (seedTarget == null)
            {
                reason = "seedResultMissing";
                return false;
            }

            BodyGearCandidate[] followUps = requestedCount > 1
                ? new[]
                {
                    candidate.WithAmmoSalvageContext(
                        BodyGearFollowUpDestination.SalvagedAmmoStackTransfer,
                        candidate.AmmoSalvageWeapon,
                        magazine,
                        seedTarget,
                        requestedCount - 1)
                }
                : Array.Empty<BodyGearCandidate>();
            bool createdLooseStack = !string.Equals(source.Id, seedTarget.Id, StringComparison.Ordinal);
            move = new BodyGearMove(
                seedTarget,
                operation,
                candidate.SourceName,
                reportAsLootNothing: true,
                followUpCandidates: followUps,
                storeAsLoot: ShouldReturnGearSwapAsCargo(),
                successPhrase: EPhraseTrigger.LootGeneric,
                ammoSalvageMagazineId: magazine.Id,
                resolveResultItemById: createdLooseStack,
                prependFollowUps: followUps.Length > 0,
                ammoSalvageReplacementSourceId: createdLooseStack ? source.Id : null,
                useVanillaAmmoTransaction: true);
            Modules.Logger.LogInfo(
                $"[LootCommand][AmmoSalvage] seed planned source={DescribeLootDebugItem(source)} " +
                $"destination={candidate.FollowUpDestination} requested={requestedCount} " +
                $"seed={seedTarget.StackObjectsCount} fillFollowUp={Math.Max(0, requestedCount - 1)}");
            reason = "ok";
            return true;
        }

        private bool TryBuildAmmoStackConsolidationMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            AmmoItemClass source,
            MagazineItemClass magazine,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidStackTransfer";
            AmmoItemClass target = ResolveAmmoSalvageTargetStack(
                inventory,
                candidate.AmmoSalvageTargetStack);
            int transferCount = candidate.AmmoSalvageTransferCount;
            if (target == null ||
                transferCount <= 0 ||
                transferCount > source.StackObjectsCount ||
                transferCount > target.StackMaxSize - target.StackObjectsCount ||
                !string.Equals(source.TemplateId, target.TemplateId, StringComparison.Ordinal) ||
                source.IsUsed != target.IsUsed ||
                !IsLootNowInBotInventory(BotOwner?.GetPlayer, target))
            {
                return false;
            }

            GStruct154<GInterface433> transferResult = AmmoItemClass.ApplyToAmmo(
                source,
                target,
                transferCount,
                inventory,
                true);
            if (transferResult.Failed)
            {
                reason = $"ammoTransferRejected:{DescribeInventoryError(transferResult.Error)}";
                return false;
            }

            if (transferResult.Value is not GInterface424 operation)
            {
                reason = "ammoTransferOperationUnsupported";
                return false;
            }

            // The destination stack was already moved and return-tracked. Consolidation changes
            // only its count, so do not register the soon-to-be-consumed source group as a root.
            move = new BodyGearMove(
                source,
                operation,
                candidate.SourceName,
                reportAsLootNothing: true,
                storeAsLoot: false,
                successPhrase: EPhraseTrigger.LootGeneric,
                ammoSalvageMagazineId: magazine.Id,
                useVanillaAmmoTransaction: true);
            Modules.Logger.LogInfo(
                $"[LootCommand][AmmoSalvage] stack fill planned source={DescribeLootDebugItem(source)} " +
                $"target={DescribeLootDebugItem(target)} count={transferCount}");
            reason = "ok";
            return true;
        }

        private bool CanPlaceAmmoInVestWithReloadReserve(
            InventoryEquipment equipment,
            AmmoItemClass ammo)
        {
            SearchableItemItemClass vest = CloneEquipmentContainer(equipment, EquipmentSlot.TacticalVest);
            if (!TrySimulateFastAccessContainerAdd(vest, ammo, out SearchableItemItemClass? nextVest))
            {
                return false;
            }

            VestReloadReserveSet reserves = FindVestReloadReserves(equipment, vest);
            return CanFitVestReloadReserves(nextVest, reserves);
        }

        private static void RunBodyGearMoveTransaction(
            InventoryController inventory,
            BodyGearMove move,
            Callback callback)
        {
            if (move?.UseDirectAmmoLoadTransaction == true)
            {
                try
                {
                    // EFT's own LoadMagazine path converts ApplyWithoutRestrictions directly and
                    // submits it without the generic CanExecute gate. Cartridge StackSlot sources
                    // require that same path when consolidating one magazine into another.
                    BaseInventoryOperationClass operation =
                        inventory.ConvertOperationResultToOperation(move.Operation);
                    inventory.vmethod_1(operation, callback);
                }
                catch (Exception ex)
                {
                    callback.Fail($"Direct ammo-load transaction failed: {ex.Message}");
                }

                return;
            }

            if (move?.UseVanillaAmmoTransaction != true)
            {
                inventory.RunNetworkTransaction(move.Operation, callback);
                return;
            }

            if (move.Operation is not GInterface433 ammoOperation)
            {
                callback.Fail("Ammo salvage operation does not implement the EFT ammo contract");
                return;
            }

            // EFT's magazine-unload path intentionally bypasses the generic RunNetworkTransaction
            // CanExecute gate. Internal cartridge StackSlot items fail that generic item-move check,
            // so vanilla wraps the ammo result and submits the converted ammo operation directly.
            BaseInventoryOperationClass networkOperation = inventory.ConvertOperationResultToOperation(
                new GClass3420(ammoOperation));
            inventory.vmethod_1(networkOperation, callback);
        }

        private static bool TryFindDirectEquipmentContainerAddress(
            InventoryEquipment equipment,
            EquipmentSlot slot,
            Item item,
            out ItemAddress? address)
        {
            address = null;
            if (equipment?.GetSlot(slot)?.ContainedItem is not SearchableItemItemClass container ||
                container.Grids == null ||
                item == null)
            {
                return false;
            }

            foreach (StashGridClass grid in OrderFastAccessGridsByBestFit(container.Grids, item))
            {
                if (grid != null &&
                    grid.TryFindLocationForItem(item, out ItemAddress candidateAddress) &&
                    !object.Equals(item.Parent, candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetAmmoSalvageEquipmentSlot(
            BodyGearFollowUpDestination destination,
            out EquipmentSlot slot)
        {
            switch (destination)
            {
                case BodyGearFollowUpDestination.SalvagedAmmoSecuredContainer:
                    slot = EquipmentSlot.SecuredContainer;
                    return true;
                case BodyGearFollowUpDestination.SalvagedAmmoPockets:
                    slot = EquipmentSlot.Pockets;
                    return true;
                case BodyGearFollowUpDestination.SalvagedAmmoBackpack:
                    slot = EquipmentSlot.Backpack;
                    return true;
                case BodyGearFollowUpDestination.SalvagedAmmoVest:
                    slot = EquipmentSlot.TacticalVest;
                    return true;
                default:
                    slot = default;
                    return false;
            }
        }

        private Item ResolveCompletedBodyGearMoveItem(BodyGearMove move, bool logMissingResult)
        {
            if (move?.ResolveResultItemById != true || string.IsNullOrEmpty(move.Item?.Id))
            {
                return move?.Item;
            }

            InventoryController inventory = BotOwner?.GetPlayer?.InventoryController;
            if (inventory != null && inventory.TryFindItem(move.Item.Id, out Item liveItem))
            {
                return liveItem;
            }

            if (logMissingResult)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][AmmoSalvage] loose-stack seed could not be resolved in follower inventory: " +
                    $"follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' itemId={move.Item.Id}");
            }

            return move.Item;
        }
    }
}
