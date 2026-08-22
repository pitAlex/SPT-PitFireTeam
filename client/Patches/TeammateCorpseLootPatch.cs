using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class TeammateCorpseLootGuard
    {
        private static readonly EquipmentSlot[] SearchableCorpseSlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };

        public static bool IsTeammateCorpseEquipment(InventoryEquipment equipment)
        {
            try
            {
                return TeammateCorpseIdentity.IsTeammateCorpseEquipment(equipment);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse equipment owner.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }
        }

        public static void MarkCorpseEquipmentVisible(InventoryEquipment equipment)
        {
            try
            {
                if (!IsTeammateCorpseEquipment(equipment))
                {
                    return;
                }

                IPlayerSearchController searchController = GamePlayerOwner.MyPlayer?.SearchController;
                if (searchController == null)
                {
                    return;
                }

                HashSet<Item> visited = new HashSet<Item>();
                MarkItemKnown(searchController, equipment);

                foreach (EquipmentSlot slotName in SearchableCorpseSlots)
                {
                    MarkItemTreeVisible(searchController, equipment.GetSlot(slotName).ContainedItem, visited);
                }

                foreach (Item item in equipment.GetAllItems())
                {
                    MarkItemTreeVisible(searchController, item, visited);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to mark teammate corpse equipment as searched.");
                pitFireTeam.Log?.LogError(ex);
            }
        }

        public static bool ShouldTreatItemExamined(InventoryController controller, Item item)
        {
            return GamePlayerOwner.MyPlayer?.InventoryController == controller && IsInsideTeammateCorpseEquipment(item);
        }

        public static bool ShouldTreatAddressSearched(ItemAddress address)
        {
            return IsInsideTeammateCorpseEquipment(address);
        }

        public static bool ShouldTreatObservedItemKnown(Item item, ItemAddress address)
        {
            return IsInsideTeammateCorpseEquipment(item) || IsInsideTeammateCorpseEquipment(address);
        }

        public static bool ShouldTreatItemKnown(EFT.PlayerSearchController controller, Item item)
        {
            return IsLocalSearchController(controller) && IsInsideTeammateCorpseEquipment(item);
        }

        public static bool ShouldTreatSearchableSearched(EFT.PlayerSearchController controller, EFT.InventoryLogic.SearchableItem item)
        {
            return IsLocalSearchController(controller) && IsInsideTeammateCorpseEquipment(item);
        }

        public static bool ShouldTreatSearchableContentsKnown(EFT.PlayerSearchController controller, EFT.InventoryLogic.SearchableItem item)
        {
            return IsLocalSearchController(controller) && IsInsideTeammateCorpseEquipment(item);
        }

        private static void MarkItemTreeVisible(IPlayerSearchController searchController, Item item, HashSet<Item> visited)
        {
            if (item == null || !visited.Add(item))
            {
                return;
            }

            MarkItemKnown(searchController, item);
            if (item is EFT.InventoryLogic.SearchableItem searchable)
            {
                searchController.SetItemAsSearched<EFT.InventoryLogic.SearchableItem>(searchable);
            }

            if (item is not CompoundItem)
            {
                return;
            }

            foreach (Item child in item.GetAllItems())
            {
                MarkItemTreeVisible(searchController, child, visited);
            }
        }

        private static void MarkItemKnown(IPlayerSearchController searchController, Item item)
        {
            if (item != null && !searchController.IsItemKnown(item))
            {
                searchController.SetItemAsKnown(item, false);
            }
        }

        private static bool IsInsideTeammateCorpseEquipment(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                if (TeammateCorpseIdentity.IsTeammateCorpseOwner(item.Owner))
                {
                    return true;
                }

                foreach (Item parent in item.GetAllParentItems(false))
                {
                    if (parent is InventoryEquipment equipment && IsTeammateCorpseEquipment(equipment))
                    {
                        return true;
                    }

                    if (TeammateCorpseIdentity.IsTeammateCorpseOwner(parent.Owner))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse item search state.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }

            return false;
        }

        private static bool IsInsideTeammateCorpseEquipment(ItemAddress address)
        {
            if (address == null)
            {
                return false;
            }

            try
            {
                if (TeammateCorpseIdentity.IsTeammateCorpseOwner(address.GetOwnerOrNull()) ||
                    IsInsideTeammateCorpseEquipment(address.Container?.ParentItem))
                {
                    return true;
                }

                foreach (Item parent in address.GetAllParentItems(false))
                {
                    if (parent is InventoryEquipment equipment && IsTeammateCorpseEquipment(equipment))
                    {
                        return true;
                    }

                    if (TeammateCorpseIdentity.IsTeammateCorpseOwner(parent.Owner))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse address search state.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }

            return false;
        }

        private static bool IsLocalSearchController(EFT.PlayerSearchController controller)
        {
            return controller != null &&
                   ReferenceEquals(GamePlayerOwner.MyPlayer?.SearchController, controller);
        }
    }

    internal sealed class TeammateCorpseContainersPanelSearchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContainersPanel), nameof(ContainersPanel.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryEquipment equipment)
        {
            if (TeammateCorpseLootGuard.IsTeammateCorpseEquipment(equipment))
            {
                TeammateCorpseLootGuard.MarkCorpseEquipmentVisible(equipment);
            }
        }
    }

    internal sealed class TeammateCorpseEquipmentTabSearchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentTab), nameof(EquipmentTab.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryEquipment equipment)
        {
            if (TeammateCorpseLootGuard.IsTeammateCorpseEquipment(equipment))
            {
                TeammateCorpseLootGuard.MarkCorpseEquipmentVisible(equipment);
            }
        }
    }
}
