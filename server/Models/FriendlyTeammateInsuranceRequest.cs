using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateInsuranceRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }
    [JsonPropertyName("traderId")]
    public string? TraderId { get; set; }
    [JsonPropertyName("itemIds")]
    public List<string> ItemIds { get; set; } = [];
    [JsonPropertyName("quotedTotal")]
    public int QuotedTotal { get; set; }
    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; }
}
