// Small SPT API doubles for policy fixtures. The server build separately checks the real 4.1.0 API.
namespace SPTarkov.Server.Core.Models.Eft.Common.Tables
{
    public record Item
    {
        public string Id { get; set; } = "";
        public string Template { get; set; } = "";
        public string? ParentId { get; set; }
        public string? SlotId { get; set; }
        public object? Location { get; set; }
        public int Rounds { get; set; }
    }
    public record Location(int X, int Y, bool Rotated = false);
    public record Template(TemplateProperties? Properties);
    public record TemplateProperties(List<Grid>? Grids, int Width = 1, int Height = 1);
    public record Grid(string? Name, GridProperties? Properties);
    public record GridProperties(int? CellsH, int? CellsV, List<GridFilter>? Filters = null);
    public record GridFilter(HashSet<string>? Filter = null, HashSet<string>? ExcludedFilter = null);
}
namespace SPTarkov.Server.Core.Helpers.Items
{
    using SPTarkov.Server.Core.Models.Eft.Common.Tables;
    public class ItemHelper
    {
        public Dictionary<string, Template> Templates { get; } = new();
        public KeyValuePair<bool, Template?> GetItem(string tpl) => new(Templates.ContainsKey(tpl), Templates.GetValueOrDefault(tpl));
        public bool IsOfBaseclass(string tpl, string parent) => parent == "magazine" && tpl.StartsWith("mag");
    }
}
namespace SPTarkov.Server.Core.Helpers.Profile
{
    using SPTarkov.Server.Core.Models.Eft.Common.Tables;
    using SPTarkov.Server.Core.Helpers.Items;
    public class InventoryHelper(ItemHelper items)
    {
        public record Placement(bool? Success);
        public int[,] GetContainerMap(int width, int height, IEnumerable<Item> inventory, string parent)
        {
            int[,] map = new int[height, width];
            foreach (var item in inventory.Where(i => i.ParentId == parent))
            {
                var loc = (Location)item.Location!;
                var props = items.GetItem(item.Template).Value!.Properties!;
                int w = loc.Rotated ? props.Height : props.Width;
                int h = loc.Rotated ? props.Width : props.Height;
                for (int y = loc.Y; y < loc.Y + h; y++)
                    for (int x = loc.X; x < loc.X + w; x++) map[y, x] = 1;
            }
            return map;
        }
        public Placement PlaceItemInContainer(int[,] map, List<Item> tree, string parent, string slot)
        {
            var item = tree[0];
            var props = items.GetItem(item.Template).Value!.Properties!;
            foreach (bool rotate in new[] { false, true })
            {
                int w = rotate ? props.Height : props.Width, h = rotate ? props.Width : props.Height;
                for (int y = 0; y <= map.GetLength(0) - h; y++)
                for (int x = 0; x <= map.GetLength(1) - w; x++)
                {
                    bool free = true;
                    for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++) free &= map[y + dy, x + dx] == 0;
                    if (!free) continue;
                    item.ParentId = parent; item.SlotId = slot; item.Location = new Location(x, y, rotate);
                    return new(true);
                }
            }
            return new(false);
        }
    }
}
