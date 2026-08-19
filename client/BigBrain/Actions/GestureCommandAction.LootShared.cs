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
        private static bool TryFindFilteredWeaponCargoEquipmentSlot(
            InventoryEquipment equipment,
            BodyGearCandidate candidate,
            out ItemAddress? address)
        {
            address = null;
            Item item = candidate?.Item;
            if (item == null)
            {
                return false;
            }

            if (item is Weapon holsterWeapon && IsHolsterWeapon(holsterWeapon))
            {
                return TryFindEquipmentSlotAddress(equipment, EquipmentSlot.Holster, item, out address);
            }

            if (item is Weapon weapon && IsShoulderWeaponCandidate(weapon))
            {
                return TryFindEquipmentSlotAddress(equipment, EquipmentSlot.SecondPrimaryWeapon, item, out address);
            }

            return false;
        }

        private static bool TryFindEquipmentSlotAddress(
            InventoryEquipment equipment,
            EquipmentSlot slotName,
            Item item,
            out ItemAddress? address)
        {
            address = null;
            Slot slot = equipment?.GetSlot(slotName);
            if (slot == null || slot.Deleted || slot.ContainedItem != null)
            {
                return false;
            }

            Error error;
            ItemAddress candidateAddress = slot.FindLocationForItem(item, out error);
            if (candidateAddress == null)
            {
                return false;
            }

            address = candidateAddress;
            return true;
        }

        private static bool IsSameLootTree(Item first, Item second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return IsSameLootItem(first, second) ||
                   IsItemInsideRoot(first, second) ||
                   IsItemInsideRoot(second, first);
        }

        private static bool IsItemInsideRoot(Item item, Item root)
        {
            if (item == null || root == null)
            {
                return false;
            }

            if (IsSameLootItem(item, root))
            {
                return true;
            }

            foreach (Item child in SnapshotLootTreeItems(root))
            {
                if (IsSameLootItem(item, child))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Item> SnapshotLootTreeItems(Item root)
        {
            List<Item> snapshot = new List<Item>();
            if (root == null)
            {
                return snapshot;
            }

            try
            {
                foreach (Item item in root.GetAllItems())
                {
                    if (item != null)
                    {
                        snapshot.Add(item);
                    }
                }
            }
            catch (InvalidOperationException ex) when (IsInventoryEnumerationMutation(ex))
            {
                snapshot.Clear();
            }

            return snapshot;
        }

        private static HashSet<string> SnapshotLootTreeItemIds(Item root)
        {
            HashSet<string> itemIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(root?.Id))
            {
                itemIds.Add(root.Id);
            }

            foreach (Item item in SnapshotLootTreeItems(root))
            {
                if (!string.IsNullOrEmpty(item.Id))
                {
                    itemIds.Add(item.Id);
                }
            }

            return itemIds;
        }

        private static bool IsInventoryEnumerationMutation(InvalidOperationException ex)
        {
            return ex.Message?.IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSameLootItem(Item first, Item second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            return !string.IsNullOrEmpty(first?.Id) &&
                   !string.IsNullOrEmpty(second?.Id) &&
                   string.Equals(first.Id, second.Id, StringComparison.Ordinal);
        }

        private static void MarkLootItemTreeSearched(
            IPlayerSearchController searchController,
            Item item,
            HashSet<Item> visited)
        {
            if (item == null || !visited.Add(item))
            {
                return;
            }

            if (!searchController.IsItemKnown(item))
            {
                searchController.SetItemAsKnown(item, false);
            }

            if (item is SearchableItemItemClass searchable)
            {
                searchController.SetItemAsSearched<SearchableItemItemClass>(searchable);
            }

            if (item is not CompoundItem)
            {
                return;
            }

            foreach (Item child in SnapshotLootTreeItems(item))
            {
                MarkLootItemTreeSearched(searchController, child, visited);
            }
        }

        private static int GetBodyGearContentPriority(Item item)
        {
            if (item is Weapon && item.GetItemComponent<KnifeComponent>() == null)
            {
                return 100;
            }

            if (item is ArmorItemClass || item is VestItemClass)
            {
                return 90;
            }

            if (item is HeadwearItemClass || item is HeadphonesItemClass || item is FaceCoverItemClass || item is VisorsItemClass)
            {
                return 80;
            }

            return 10;
        }

        private static bool IsBodyGearCandidateLootable(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.IsSpecialSlotOnly ||
                item is ArmBandItemClass ||
                item.GetItemComponent<KnifeComponent>() != null)
            {
                return false;
            }

            // Respect vanilla lootability/removal flags from the corpse equipment slot. We check the
            // raw component data here because pitFireTeam relaxes UnlootableComponent elsewhere so
            // players can inspect/reorganize teammate gear during raid.
            Slot sourceSlot = item.CurrentAddress?.Container as Slot;
            if (sourceSlot == null || sourceSlot.ParentItem is not InventoryEquipment)
            {
                return true;
            }

            if (IsAlwaysNonLootableEquipmentSlot(sourceSlot))
            {
                return false;
            }

            if (item.TryGetItemComponent<UnlootableComponent>(out UnlootableComponent unlootableComponent) &&
                IsUnlootableFromSlotIgnoringPatch(unlootableComponent, sourceSlot))
            {
                return false;
            }

            if (item.TryGetItemComponent<CantRemoveFromSlotsDuringRaidComponent>(out CantRemoveFromSlotsDuringRaidComponent cantRemoveComponent) &&
                !cantRemoveComponent.CanRemoveFromSlotDuringRaid(sourceSlot.ID))
            {
                return false;
            }

            return true;
        }

        private static bool IsAlwaysNonLootableEquipmentSlot(Slot slot)
        {
            return string.Equals(slot.ID, EquipmentSlot.ArmBand.ToString(), StringComparison.Ordinal) ||
                   string.Equals(slot.ID, EquipmentSlot.Scabbard.ToString(), StringComparison.Ordinal);
        }

        private static bool IsUnlootableFromSlotIgnoringPatch(UnlootableComponent component, Slot slot)
        {
            if (component?.Template == null ||
                slot == null ||
                string.IsNullOrEmpty(component.Template.SlotName) ||
                !slot.ID.Contains(component.Template.SlotName))
            {
                return false;
            }

            if (slot.ParentItem?.Owner is GClass3384 equipmentOwner)
            {
                return component.Template.Side.CheckSide(equipmentOwner.Side);
            }

            return false;
        }

        private static int GetItemArea(Item item)
        {
            try
            {
                XYCellSizeStruct size = item.CalculateCellSize();
                return Mathf.Max(1, size.X) * Mathf.Max(1, size.Y);
            }
            catch
            {
                return 1;
            }
        }

        private bool TryFastForwardLootInventoryTransaction(
            InventoryController inventory,
            BodyGearMove move,
            string context)
        {
            try
            {
                if (inventory is not Player.PlayerInventoryController playerInventory ||
                    playerInventory.Player_0 == null)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][TransactionRecovery] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"context={context} result=unavailable source={move?.SourceName ?? "unknown"}");
                    return false;
                }

                playerInventory.Player_0.FastForwardCurrentOperations();
                Modules.Logger.LogInfo(
                    $"[LootCommand][TransactionRecovery] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"context={context} result=fastForwarded source={move?.SourceName ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(move?.Item)}");
                return true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][TransactionRecovery] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"context={context} result=failed source={move?.SourceName ?? "unknown"} reason={ex.Message}");
                return false;
            }
        }

    }
}
