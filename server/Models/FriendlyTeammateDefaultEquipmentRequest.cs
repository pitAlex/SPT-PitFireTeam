using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Repair;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateDefaultEquipmentRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("items")]
    public List<Item>? Items { get; set; }

    // Present only for real default commits. This is the staged player stash after the editor's drag/drop
    // operations, and is committed server-side together with teammate equipment.
    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }

    [JsonPropertyName("realItemCommit")]
    public bool RealItemCommit { get; set; }
}

public record FriendlyTeammateDefaultEquipmentResponse
{
    [JsonPropertyName("realItemCommit")]
    public bool RealItemCommit { get; set; }

    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }

    [JsonPropertyName("playerNewStashItems")]
    public List<Item>? PlayerNewStashItems { get; set; }

    [JsonPropertyName("playerChangedStashItems")]
    public List<Item>? PlayerChangedStashItems { get; set; }

    [JsonPropertyName("playerDeletedStashItemIds")]
    public List<string>? PlayerDeletedStashItemIds { get; set; }
}

public record FriendlyTeammateBuyKitRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("items")]
    public List<Item>? Items { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("useItemsInStash")]
    public bool UseItemsInStash { get; set; }

    [JsonPropertyName("usedItems")]
    public List<FriendlyTeammateBuyKitUsedItem>? UsedItems { get; set; }
}

public record FriendlyTeammateBuyKitResponse
{
    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }
}

public record FriendlyTeammateBuyKitUsedItem
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("templateId")]
    public string? TemplateId { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

public record FriendlyTeammateRepairEquipmentRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("repairKitsInfo")]
    public List<RepairKitsInfo>? RepairKitsInfo { get; set; }

    [JsonPropertyName("traderId")]
    public string? TraderId { get; set; }

    [JsonPropertyName("repairCount")]
    public double? RepairCount { get; set; }
}

public record FriendlyTeammateRepairEquipmentResponse
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("durability")]
    public double? Durability { get; set; }

    [JsonPropertyName("maxDurability")]
    public double? MaxDurability { get; set; }

    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }

    [JsonPropertyName("playerNewStashItems")]
    public List<Item>? PlayerNewStashItems { get; set; }

    [JsonPropertyName("playerChangedStashItems")]
    public List<Item>? PlayerChangedStashItems { get; set; }

    [JsonPropertyName("playerDeletedStashItemIds")]
    public List<string>? PlayerDeletedStashItemIds { get; set; }
}
