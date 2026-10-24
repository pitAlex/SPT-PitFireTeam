using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private void StripToPermanentEquipment(BotBase teammate, bool keepSecureContainer, bool clearSecureContents = false)
    {
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Inventory.Items ??= [];
        if (teammate.Inventory.Items.Count == 0)
        {
            return;
        }

        // Keep or inject a scabbard knife before stripping. EFT already prevents knife looting, and
        // this gives later spawn/preview validation a stable legal melee slot to work with.
        EnsureFollowerHasPockets(teammate);
        EnsureFollowerHasScabbardKnife(teammate);

        string rootId = GetEquipmentRootId(teammate);
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootId };
        foreach (var preservedItem in teammate.Inventory.Items.Where(item =>
                     !string.IsNullOrWhiteSpace(item?.SlotId) &&
                     IsPermanentTeammateEquipmentSlot(item.SlotId, keepSecureContainer)).ToList())
        {
            // Keep permanent containers anchored, while removing ordinary pocket cargo.
            // Hiring without a kit also empties the secure container; death loss retains its
            // existing behavior. Special-slot trees are preserved separately.
            if (IsPocketsSlotItem(preservedItem)
                || (clearSecureContents && string.Equals(preservedItem.SlotId, nameof(EquipmentSlots.SecuredContainer), StringComparison.OrdinalIgnoreCase)))
            {
                keepIds.Add(preservedItem.Id.ToString());
                continue;
            }

            // Preserve descendants for kept equipment items so attached child items are not orphaned.
            AddItemAndDescendantsToKeepSet(teammate.Inventory.Items, preservedItem.Id.ToString(), keepIds);
        }

        // The filter is the actual loss operation: anything outside the keep set is removed from
        // the teammate profile before the Default snapshot is saved.
        teammate.Inventory.Items = teammate.Inventory.Items
            .Where(item => keepIds.Contains(item.Id.ToString()))
            .ToList();
        teammate.Inventory.Equipment = new MongoId(rootId);
    }

    private static void AddItemAndDescendantsToKeepSet(List<Item> inventoryItems, string itemId, HashSet<string> keepIds)
    {
        if (!keepIds.Add(itemId))
        {
            return;
        }

        foreach (var child in inventoryItems.Where(item =>
                     string.Equals(item.ParentId, itemId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            AddItemAndDescendantsToKeepSet(inventoryItems, child.Id.ToString(), keepIds);
        }
    }

    private static bool IsPocketsSlotItem(Item item)
    {
        return string.Equals(item?.SlotId, nameof(EquipmentSlots.Pockets), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPermanentTeammateEquipmentSlot(string? slotId, bool keepSecureContainer)
    {
        return !string.IsNullOrWhiteSpace(slotId)
            && (slotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
                || slotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                || (keepSecureContainer && slotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase))
                || string.Equals(slotId, nameof(EquipmentSlots.Pockets), StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, nameof(EquipmentSlots.Scabbard), StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, "ArmBand", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slotId, "Armband", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RemoveItemTreesById(List<Item>? inventoryItems, IEnumerable<string>? rootItemIds)
    {
        if (inventoryItems == null || inventoryItems.Count == 0 || rootItemIds == null)
        {
            return false;
        }

        var removeIds = rootItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removeIds.Count == 0)
        {
            return false;
        }

        bool foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (var item in inventoryItems)
            {
                if (item?.Id == null || string.IsNullOrWhiteSpace(item.ParentId) || !removeIds.Contains(item.ParentId))
                {
                    continue;
                }

                if (removeIds.Add(item.Id.ToString()))
                {
                    foundChild = true;
                }
            }
        }

        return inventoryItems.RemoveAll(item => item?.Id != null && removeIds.Contains(item.Id.ToString())) > 0;
    }

}
