using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private List<Item> BuildCurrentTeammateKitDeliveryItems(BotBase teammate, bool includeSecureContainer,
        IEnumerable<string>? automaticallySuppliedEquipmentIds)
    {
        var items = teammate.Inventory?.Items;
        if (items == null || items.Count == 0)
        {
            return [];
        }

        string equipmentRootId = GetEquipmentRootId(teammate);
        var automaticIds = (automaticallySuppliedEquipmentIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deliveryItems = new List<Item>();
        foreach (var slotItem in items.Where(item =>
                     item?.ParentId != null
                     && string.Equals(item.ParentId, equipmentRootId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (slotItem?.Id == null || string.IsNullOrWhiteSpace(slotItem.SlotId))
            {
                continue;
            }

            if (IsIgnoredReturnedEquipmentSlot(slotItem.SlotId, includeSecureContainer))
            {
                continue;
            }

            if (IsPocketsSlotItem(slotItem))
            {
                foreach (var pocketChild in items.Where(item =>
                             item?.ParentId != null
                             && string.Equals(item.ParentId, slotItem.Id.ToString(), StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    AddDeliveryItemTree(items, pocketChild, deliveryItems);
                }

                continue;
            }

            AddDeliveryItemTree(items, slotItem, deliveryItems);
        }

        // Filter identities across the complete trees: moving free equipment into a bag
        // must not turn it into a reward. Hoist retained cargo out of an excluded shell.
        deliveryItems.RemoveAll(item => automaticIds.Contains(item.Id.ToString()));
        foreach (var item in deliveryItems.Where(item => item.ParentId != null && automaticIds.Contains(item.ParentId)))
        {
            item.ParentId = null;
            item.SlotId = null;
            item.Location = null;
        }
        return deliveryItems;
    }

    private void AddDeliveryItemTree(List<Item> sourceItems, Item rootItem, List<Item> deliveryItems)
    {
        if (rootItem?.Id == null || IsIgnoredKitRequirementItem(rootItem))
        {
            return;
        }

        var treeIds = GetItemTreeIds(sourceItems, rootItem.Id.ToString());
        var tree = cloner.Clone(sourceItems.Where(item => treeIds.Contains(item.Id.ToString())).ToList())
            ?? sourceItems.Where(item => treeIds.Contains(item.Id.ToString())).ToList();
        if (tree.Count == 0)
        {
            return;
        }

        var deliveryRoot = tree.Single(item => item.Id == rootItem.Id);
        deliveryRoot.ParentId = null;
        deliveryRoot.SlotId = null;
        deliveryRoot.Location = null;
        deliveryItems.Add(deliveryRoot);
        deliveryItems.AddRange(tree.Where(item => item.Id != rootItem.Id));
    }

    private static bool IsIgnoredReturnedEquipmentSlot(string slotId, bool includeSecureContainer)
    {
        return slotId.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
            || (!includeSecureContainer && slotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase));
    }

}
