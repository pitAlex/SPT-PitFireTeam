using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.GoingRogue.Server;

// Operates only on the isolated, generated raid kit, before its viewing preview is saved.
public sealed class BossMagazineProvisioning(ItemHelper itemHelper, InventoryHelper inventoryHelper)
{
    public void Ensure(BotBase profile)
    {
        var inventory = profile.Inventory ?? throw new InvalidDataException("Missing companion raid inventory");
        var items = inventory.Items ?? throw new InvalidDataException("Missing companion raid items");
        string root = inventory.Equipment?.ToString() ?? throw new InvalidDataException("Missing companion equipment root");
        foreach (string slot in new[] { "FirstPrimaryWeapon", "SecondPrimaryWeapon" })
        {
            var weapon = items.FirstOrDefault(item => item.ParentId == root && item.SlotId == slot);
            if (weapon == null) continue;
            var weaponTemplate = itemHelper.GetItem(weapon.Template).Value
                ?? throw new InvalidDataException($"Missing companion weapon template: {weapon.Template}");
            if (weaponTemplate.Properties?.ReloadMode is not (ReloadMode.ExternalMagazine or ReloadMode.ExternalMagazineWithInternalReloadSupport))
                continue;

            var magazineFilters = weaponTemplate.Properties.Slots?.FirstOrDefault(part => part.Name == "mod_magazine")?
                .Properties?.Filters?.SelectMany(filter => filter.Filter ?? []).ToArray() ?? [];
            bool Fits(Item item) => itemHelper.IsOfBaseclass(item.Template, BaseClasses.MAGAZINE) &&
                magazineFilters.Any(filter => filter == item.Template || itemHelper.IsOfBaseclass(item.Template, filter));
            var containers = items.Where(item => item.ParentId == root && item.SlotId is "TacticalVest" or "Pockets").ToArray();
            bool Carried(Item item) => containers.Any(container => container.Id.ToString() == item.ParentId &&
                (itemHelper.GetItem(container.Template).Value?.Properties?.Grids ?? []).Any(grid => grid.Name == item.SlotId));
            if (items.Any(item => Carried(item) && Fits(item))) continue;

            var inserted = items.FirstOrDefault(item => item.ParentId == weapon.Id.ToString() && item.SlotId == "mod_magazine");
            MongoId magazineTpl = inserted?.Template ?? weaponTemplate.Properties.DefMagType
                ?? throw new InvalidDataException($"Missing companion magazine template: {weapon.Template}");
            var magazineTemplate = itemHelper.GetItem(magazineTpl).Value;
            var spare = new Item { Id = new MongoId(), Template = magazineTpl };
            if (magazineTemplate?.Properties?.ReloadMagType != ReloadMode.ExternalMagazine || !Fits(spare))
                throw new InvalidDataException($"Invalid companion spare magazine: {magazineTpl} for {weapon.Template}");
            var ammoFilters = magazineTemplate.Properties.Cartridges?.SelectMany(part => part.Properties?.Filters ?? [])
                .SelectMany(filter => filter.Filter ?? []).ToHashSet() ?? [];
            MongoId? ammo = items.FirstOrDefault(item =>
                (item.ParentId == inserted?.Id.ToString() || item.ParentId == weapon.Id.ToString()) &&
                ammoFilters.Contains(item.Template))?.Template ?? weaponTemplate.Properties.DefAmmo;
            if (ammo == null || !ammoFilters.Contains(ammo.Value))
                throw new InvalidDataException($"Missing compatible companion magazine ammo: {weapon.Template}");
            var tree = new List<Item> { spare };
            itemHelper.FillMagazineWithCartridge(tree, magazineTemplate, ammo.Value, 1);
            if (tree.Count < 2 || !TryPlace(items, containers, spare))
                throw new InvalidDataException($"Cannot provision spare magazine in companion vest/pockets: role={profile.Info?.Settings?.Role} weapon={weapon.Template} magazine={magazineTpl}");
            items.AddRange(tree);
        }
    }

    private bool TryPlace(List<Item> items, IEnumerable<Item> containers, Item magazine)
    {
        // Use the actual post-generation inventory, not SPT's cached generation grid.
        foreach (var container in containers)
        foreach (var grid in itemHelper.GetItem(container.Template).Value?.Properties?.Grids ?? [])
        {
            if (string.IsNullOrWhiteSpace(grid.Name) || grid.Properties?.CellsH is not > 0 || grid.Properties.CellsV is not > 0)
                continue;
            var filters = grid.Properties.Filters?.ToArray();
            if (filters?.Length > 0 && !filters.Any(filter =>
                    (filter.Filter == null || filter.Filter.Count == 0 || filter.Filter.Any(tpl =>
                        tpl == magazine.Template || itemHelper.IsOfBaseclass(magazine.Template, tpl))) &&
                    !(filter.ExcludedFilter?.Any(tpl => tpl == magazine.Template || itemHelper.IsOfBaseclass(magazine.Template, tpl)) ?? false)))
                continue;
            var gridItems = items.Where(item => item.ParentId != container.Id.ToString() || item.SlotId == grid.Name);
            var map = inventoryHelper.GetContainerMap(grid.Properties.CellsH.Value, grid.Properties.CellsV.Value, gridItems, container.Id);
            if (inventoryHelper.PlaceItemInContainer(map, [magazine], container.Id.ToString(), grid.Name).Success == true)
                return true;
        }
        return false;
    }
}
