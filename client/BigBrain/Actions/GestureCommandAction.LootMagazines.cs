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
        private IEnumerable<BodyGearCandidate> GetBodyOperationalMagazineCandidates(InventoryEquipment corpseEquipment, Weapon weapon)
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
                foreach (MagazineItemClass magazine in GetOperationalMagazineItems(root, weapon))
                {
                    yield return new BodyGearCandidate(
                        magazine,
                        null,
                        $"{slot}.WeaponSupportMagazine",
                        0);
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerOperationalMagazineCandidates(SearchableItemItemClass containerRoot, Weapon weapon)
        {
            foreach (MagazineItemClass magazine in GetOperationalMagazineItems(containerRoot, weapon))
            {
                yield return new BodyGearCandidate(
                    magazine,
                    null,
                    "Container.WeaponSupportMagazine",
                    0);
            }
        }

        private static IEnumerable<MagazineItemClass> GetOperationalMagazineItems(Item root, Weapon weapon)
        {
            if (root == null || weapon == null)
            {
                yield break;
            }

            foreach (Item item in root.GetAllItems())
            {
                if (item is not MagazineItemClass magazine ||
                    magazine.Count <= 0 ||
                    IsItemInsideRoot(magazine, weapon))
                {
                    continue;
                }

                if (IsMagazineCompatibleWithWeapon(weapon, magazine))
                {
                    yield return magazine;
                }
            }
        }

        private BodyGearCandidate? FindFirstOperationalMagazineCandidate(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? candidates)
        {
            if (inventory == null || followerEquipment == null || weapon == null || candidates == null)
            {
                return null;
            }

            // A support mag must be both eligible loot and physically placeable in the follower's
            // vest, because combat reload logic does not search the backpack for this weapon.
            foreach (BodyGearCandidate candidate in candidates)
            {
                if (candidate?.Item is not MagazineItemClass magazine ||
                    string.IsNullOrEmpty(magazine.Id) ||
                    !CanConsiderFilteredLootCandidate(candidate, new HashSet<string>(StringComparer.Ordinal)) ||
                    !TryFindOperationalMagazineVestAddress(followerEquipment, magazine, out ItemAddress? address))
                {
                    continue;
                }

                GStruct154<GClass3411> moveResult = InteractionsHandlerClass.Move(magazine, address, inventory, true);
                if (!moveResult.Failed && !moveResult.Value.ItemsDestroyRequired && inventory.CanExecute(moveResult.Value))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsWeaponLoadedEnoughForPrimary(Weapon weapon)
        {
            if (weapon == null)
            {
                return false;
            }

            try
            {
                MagazineItemClass magazine = weapon.GetCurrentMagazine();
                int maxCount = magazine?.MaxCount ?? weapon.GetMaxMagazineCount();
                int currentCount = magazine?.Count ?? weapon.GetCurrentMagazineCount();
                return maxCount > 0 && currentCount >= maxCount;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMagazineCompatibleWithWeapon(Weapon weapon, MagazineItemClass magazine)
        {
            if (weapon == null || magazine == null)
            {
                return false;
            }

            try
            {
                MagazineItemClass currentMagazine = weapon.GetCurrentMagazine();
                if (currentMagazine != null &&
                    string.Equals(currentMagazine.TemplateId, magazine.TemplateId, StringComparison.Ordinal))
                {
                    return true;
                }

                Slot magazineSlot = weapon.GetMagazineSlot();
                return magazineSlot != null && magazineSlot.CanAccept(magazine);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindOperationalMagazineVestAddress(
            InventoryEquipment followerEquipment,
            Item magazine,
            out ItemAddress? address)
        {
            address = null;
            Item tacticalVest = followerEquipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (tacticalVest is not SearchableItemItemClass searchable)
            {
                return false;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
            {
                if (container != null &&
                    container.TryFindLocationForItem(magazine, out ItemAddress candidateAddress) &&
                    !magazine.Parent.Equals(candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindBodyGearEquipmentSlot(
            InventoryEquipment equipment,
            BodyGearCandidate candidate,
            out ItemAddress? address)
        {
            address = null;

            if (candidate.Item is BackpackItemClass)
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
