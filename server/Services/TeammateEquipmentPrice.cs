using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

// Traverses attachment slots only. Grid contents and ammunition never enter the quote.
internal static class TeammateEquipmentPrice
{
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase)
    {
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "Headwear", "FaceCover",
        "Eyewear", "Earpiece", "TacticalVest", "ArmorVest", "Backpack"
    };

    internal static int Calculate(IEnumerable<Item> inventory, string equipmentId,
        Func<Item, IEnumerable<string>> attachmentSlots, Func<Item, bool> excluded,
        Func<Item, bool> isPlate, Func<Item, double> unitPrice)
    {
        var items = inventory.ToList();
        var children = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double total = 0;

        void Add(Item item, string rootSlot)
        {
            if (excluded(item) || !seen.Add(item.Id.ToString())) return;
            double price = unitPrice(item);
            if (!double.IsFinite(price) || price <= 0) throw new InvalidOperationException($"No equipment price for {item.Template}");
            total += price; // Equipment and installed parts are individual items, never cargo stacks.
            if (rootSlot.Equals("Backpack", StringComparison.OrdinalIgnoreCase)) return;
            var slots = attachmentSlots(item).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var child in children[item.Id.ToString()])
            {
                if (child.SlotId == null || !slots.Contains(child.SlotId)) continue;
                if ((rootSlot.Equals("TacticalVest", StringComparison.OrdinalIgnoreCase)
                    || rootSlot.Equals("ArmorVest", StringComparison.OrdinalIgnoreCase)) && !isPlate(child)) continue;
                Add(child, rootSlot);
            }
        }

        foreach (var item in children[equipmentId])
            if (item.SlotId != null && Roots.Contains(item.SlotId)) Add(item, item.SlotId);
        if (!double.IsFinite(total) || total > int.MaxValue) throw new InvalidOperationException("Equipment price exceeds supported range");
        return checked((int)Math.Ceiling(total));
    }
}
