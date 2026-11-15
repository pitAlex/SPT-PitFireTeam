using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyPostRaidProtectedItemsRequest : IRequestData
{
    [JsonPropertyName("itemIds")]
    public List<string>? ItemIds { get; set; }

    [JsonPropertyName("removeItemIds")]
    public List<string>? RemoveItemIds { get; set; }

    [JsonPropertyName("droppingCompanionProfileId")]
    public string? DroppingCompanionProfileId { get; set; }

    [JsonPropertyName("droppedCompanionItemIds")]
    public List<string>? DroppedCompanionItemIds { get; set; }

    [JsonPropertyName("context")]
    public string? Context { get; set; }
}
