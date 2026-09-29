using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

internal static class RestrictedMagazineRecovery
{
    // The caller supplies a filtered, detached raid snapshot and commits it only on success.
    // Recover the magazine shell, never its pre-raid cartridges: upkeep still consumes ammo.
    internal static bool TryRestore(
        IEnumerable<Item> savedItems,
        List<Item> raidItems,
        Func<Item, bool> isMagazine,
        Func<Item, Item> cloneItem,
        Func<List<Item>, Item, bool> tryPlace,
        out int restored,
        out string? unplacedId)
    {
        restored = 0;
        unplacedId = null;
        var presentIds = raidItems.Select(item => item.Id).ToHashSet();
        foreach (var saved in savedItems.Where(isMagazine))
        {
            if (presentIds.Contains(saved.Id))
            {
                continue;
            }

            var magazine = cloneItem(saved);
            if (!tryPlace(raidItems, magazine))
            {
                unplacedId = saved.Id.ToString();
                return false;
            }

            raidItems.Add(magazine);
            presentIds.Add(magazine.Id);
            restored++;
        }

        return true;
    }

    internal static bool TryPlace(List<Item> inventoryItems, Item magazine, ItemHelper itemHelper, InventoryHelper inventoryHelper)
    {
        // Never displace a surviving magazine that was inserted during a reload.
        var originalParent = inventoryItems.FirstOrDefault(item => item.Id.ToString() == magazine.ParentId);
        if (originalParent != null
            && string.Equals(magazine.SlotId, "mod_magazine", StringComparison.OrdinalIgnoreCase)
            && !inventoryItems.Any(item => item.ParentId == magazine.ParentId
                && string.Equals(item.SlotId, magazine.SlotId, StringComparison.OrdinalIgnoreCase)))
        {
            magazine.Location = null;
            return true;
        }

        // Prefer its original carry container, then worn vest/pockets/backpack. Map each
        // grid separately: GetContainerMap otherwise combines every grid of the rig.
        var containers = inventoryItems.Where(item =>
                item == originalParent
                || item.ParentId == inventoryItems[0].Id.ToString()
                    && (item.SlotId == "TacticalVest" || item.SlotId == "Pockets" || item.SlotId == "Backpack"))
            .OrderByDescending(item => item == originalParent);
        foreach (var container in containers)
        {
            foreach (var grid in itemHelper.GetItem(container.Template).Value?.Properties?.Grids ?? [])
            {
                if (string.IsNullOrWhiteSpace(grid.Name)
                    || grid.Properties?.CellsH is not > 0 || grid.Properties.CellsV is not > 0)
                {
                    continue;
                }

                var filters = grid.Properties.Filters?.ToList();
                if (filters?.Count > 0 && !filters.Any(filter =>
                        (filter.Filter == null || filter.Filter.Count == 0
                            || filter.Filter.Any(tpl => tpl == magazine.Template || itemHelper.IsOfBaseclass(magazine.Template, tpl)))
                        && !(filter.ExcludedFilter?.Any(tpl => tpl == magazine.Template || itemHelper.IsOfBaseclass(magazine.Template, tpl)) ?? false)))
                {
                    continue;
                }

                var gridItems = inventoryItems.Where(item => item.ParentId != container.Id.ToString()
                    || string.Equals(item.SlotId, grid.Name, StringComparison.OrdinalIgnoreCase));
                int[,] map = inventoryHelper.GetContainerMap(
                    grid.Properties.CellsH.Value, grid.Properties.CellsV.Value, gridItems, container.Id);
                if (inventoryHelper.PlaceItemInContainer(map, [magazine], container.Id.ToString(), grid.Name).Success == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

}
