namespace SPTarkov.Server.Core.Models.Eft.Common.Tables;

public record Item(string Id, string Template, string? ParentId, string? SlotId)
{
    public Item() : this("", "", null, null) { }
    public Upd? Upd { get; set; }
}
public record Upd { public int StackObjectsCount { get; set; } = 1; public bool SpawnedInSession { get; set; } }
public record Inventory { public List<Item> Items { get; set; } = []; }
public record BotBaseInventory : Inventory
{
    public SPTarkov.Server.Core.Models.Common.MongoId? Equipment { get; set; }
}
public record BotBase
{
    public int? Aid { get; set; }
    public Info Info { get; set; } = new();
    public BotBaseInventory Inventory { get; set; } = new();
    public object Customization { get; set; } = new();
}
public record Info { public string Nickname { get; set; } = "Candidate"; }
