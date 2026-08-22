using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryStartEasyBodyHolsterWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!CanAddHolsterWeapon(followerEquipment))
            {
                return false;
            }

            foreach (BodyGearCandidate sourceCandidate in GetBodyWeaponEquipCandidates(
                         corpseEquipment,
                         IsHolsterWeapon))
            {
                BodyGearCandidate candidate = CreateGearSwapCandidate(sourceCandidate);
                if (!CanConsiderFilteredLootCandidate(candidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                Weapon weapon = candidate.Item as Weapon;
                IEnumerable<BodyGearCandidate> magazines = GetBodyOperationalMagazineCandidates(
                    corpseEquipment,
                    weapon,
                    includeEmptyForTopOff: true);
                List<BodyGearCandidate> looseAmmo = GetBodyWeaponLooseAmmoCandidates(
                        corpseEquipment,
                        weapon)
                    .ToList();
                bool built = TryBuildPhaseOneHolsterWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    magazines,
                    looseAmmo,
                    out BodyGearMove? move,
                    out OperationalMagazinePlan? magazinePlan);
                if (!built)
                {
                    continue;
                }

                if (!move.IsStagingOperation)
                {
                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                }

                if (magazinePlan != null)
                {
                    move = AppendWeaponLooseAmmoSupportFollowUps(
                        move,
                        followerEquipment,
                        weapon,
                        looseAmmo,
                        "newHolsterWeapon",
                        GetOperationalMagazineCartridgeItems(magazinePlan));
                }

                if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartBodyGearMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartEasyContainerHolsterWeaponEquipMove(
            InventoryController inventory,
            EFT.InventoryLogic.SearchableItem containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!CanAddHolsterWeapon(followerEquipment))
            {
                return false;
            }

            foreach (BodyGearCandidate sourceCandidate in GetContainerWeaponEquipCandidates(
                         containerRoot,
                         IsHolsterWeapon))
            {
                BodyGearCandidate candidate = CreateGearSwapCandidate(sourceCandidate);
                if (!CanConsiderFilteredLootCandidate(candidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                Weapon weapon = candidate.Item as Weapon;
                IEnumerable<BodyGearCandidate> magazines = GetContainerOperationalMagazineCandidates(
                    containerRoot,
                    weapon,
                    includeEmptyForTopOff: true);
                List<BodyGearCandidate> looseAmmo = GetContainerWeaponLooseAmmoCandidates(
                        containerRoot,
                        weapon)
                    .ToList();
                bool built = TryBuildPhaseOneHolsterWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    magazines,
                    looseAmmo,
                    out BodyGearMove? move,
                    out OperationalMagazinePlan? magazinePlan);
                if (!built)
                {
                    continue;
                }

                if (!move.IsStagingOperation)
                {
                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                }

                if (magazinePlan != null)
                {
                    move = AppendWeaponLooseAmmoSupportFollowUps(
                        move,
                        followerEquipment,
                        weapon,
                        looseAmmo,
                        "newHolsterWeapon",
                        GetOperationalMagazineCartridgeItems(magazinePlan));
                }

                if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryBuildPhaseOneHolsterWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate> operationalMagazineCandidates,
            IEnumerable<BodyGearCandidate> looseAmmoCandidates,
            out BodyGearMove? move,
            out OperationalMagazinePlan? magazinePlan)
        {
            move = null;
            magazinePlan = null;
            if (!CanAddHolsterWeapon(followerEquipment) ||
                candidate?.Item is not Weapon weapon ||
                !IsHolsterWeapon(weapon))
            {
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return TryBuildInternalMagazineWeaponEquipChain(
                    inventory,
                    followerEquipment,
                    candidate,
                    looseAmmoCandidates,
                    EquipmentSlot.Holster,
                    out move,
                    out _);
            }

            return TryBuildWorkingPrimarySupportWeaponEquipChain(
                inventory,
                followerEquipment,
                candidate,
                operationalMagazineCandidates,
                EquipmentSlot.Holster,
                out move,
                out magazinePlan);
        }

        private static bool CanAddHolsterWeapon(InventoryEquipment followerEquipment)
        {
            // The holster is an independent physical slot. A shoulder support weapon keeps
            // vanilla's preferred support role, but it does not prevent adding a usable pistol.
            return pitFireTeam.IsLootGearSwappingEnabled() &&
                   pitFireTeam.IsLootWeaponPickupEnabled() &&
                   followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is Weapon &&
                   followerEquipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem == null;
        }

        private static Weapon? GetSingleSupportWeapon(InventoryEquipment followerEquipment)
        {
            // Vanilla gives second primary ownership of the support role. Phase 1 follows that
            // ordering and considers holster only when no shoulder support weapon exists.
            Weapon secondary = followerEquipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem as Weapon;
            if (secondary != null)
            {
                return secondary;
            }

            Weapon holster = followerEquipment?.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
            return IsHolsterWeapon(holster) ? holster : null;
        }
    }
}
