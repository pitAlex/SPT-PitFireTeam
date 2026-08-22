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
        private static IEnumerable<EquipmentSlot> GetBodyGearEquipmentSlotOrder(BodyGearCandidate candidate)
        {
            // Whole gear recovery only fills empty compatible slots. Swapping or throwing current
            // equipment is handled by later surgical gear-swap phases, not this recovery helper.
            if (candidate.SourceSlot == EquipmentSlot.FirstPrimaryWeapon ||
                candidate.SourceSlot == EquipmentSlot.SecondPrimaryWeapon)
            {
                yield return EquipmentSlot.SecondPrimaryWeapon;
                yield return EquipmentSlot.FirstPrimaryWeapon;
                yield break;
            }

            Item item = candidate.Item;
            if (item is Weapon holsterWeapon && IsHolsterWeapon(holsterWeapon))
            {
                yield return EquipmentSlot.Holster;
                yield break;
            }

            if (item is EFT.InventoryLogic.Armor)
            {
                yield return EquipmentSlot.ArmorVest;
                yield break;
            }

            if (item is EFT.InventoryLogic.Vest)
            {
                yield return EquipmentSlot.TacticalVest;
                yield break;
            }

            if (item is EFT.InventoryLogic.Headwear)
            {
                yield return EquipmentSlot.Headwear;
                yield break;
            }

            if (item is EFT.InventoryLogic.Headphones)
            {
                yield return EquipmentSlot.Earpiece;
                yield break;
            }

            if (item is EFT.InventoryLogic.FaceCover)
            {
                yield return EquipmentSlot.FaceCover;
                yield break;
            }

            if (item is EFT.InventoryLogic.Visors)
            {
                yield return EquipmentSlot.Eyewear;
                yield break;
            }

            if (item is Weapon weapon && IsShoulderWeaponCandidate(weapon))
            {
                yield return EquipmentSlot.SecondPrimaryWeapon;
                yield return EquipmentSlot.FirstPrimaryWeapon;
            }
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetBodyGearCarryContainers(InventoryEquipment equipment, Item item)
        {
            HashSet<EFT.InventoryLogic.IContainer> seen = new HashSet<EFT.InventoryLogic.IContainer>();

            foreach (EquipmentSlot slot in BodyGearCarrySlotOrder)
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root is not EFT.InventoryLogic.SearchableItem searchable)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
                {
                    if (container != null && seen.Add(container))
                    {
                        yield return container;
                    }
                }
            }
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetFilteredLootCarryContainers(InventoryEquipment equipment)
        {
            HashSet<EFT.InventoryLogic.IContainer> seen = new HashSet<EFT.InventoryLogic.IContainer>();

            // Filtered loot cargo may use backpack and pockets. The rig stays reserved for combat
            // magazines and tactical swaps.
            foreach (EquipmentSlot slot in FilteredLootCarrySlotOrder)
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root is not EFT.InventoryLogic.SearchableItem searchable)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
                {
                    if (container != null && seen.Add(container))
                    {
                        yield return container;
                    }
                }
            }
        }

        private static bool TryFindBackpackAddressForItem(
            InventoryEquipment equipment,
            Item item,
            out ItemAddress? address)
        {
            address = null;
            Item backpack = equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack is not EFT.InventoryLogic.SearchableItem searchable)
            {
                return false;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
            {
                if (container != null &&
                    container.TryFindLocationForItem(item, out ItemAddress candidateAddress) &&
                    !object.Equals(item.Parent, candidateAddress))
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetSearchableContainersRecursive(EFT.InventoryLogic.SearchableItem item)
        {
            foreach (EFT.InventoryLogic.IContainer container in item.Containers ?? Enumerable.Empty<EFT.InventoryLogic.IContainer>())
            {
                yield return container;
            }

            foreach (Item child in SnapshotLootTreeItems(item))
            {
                if (child != null && child != item && child is EFT.InventoryLogic.SearchableItem nested)
                {
                    foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(nested))
                    {
                        yield return container;
                    }
                }
            }
        }

    }
}
