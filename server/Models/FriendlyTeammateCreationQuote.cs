using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Models;

public record FriendlyTeammateCreationQuote
{
    public string Token { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public BotBase Teammate { get; set; } = new();
    public int Price { get; set; }
    public int PricingVersion { get; set; }
    public bool WithoutKit { get; set; }
    public string State { get; set; } = "pending";
    public string? MoneyBefore { get; set; }
    public string? MoneyAfter { get; set; }
}

public record FriendlyTeammateCreationPreview
{
    [JsonPropertyName("quoteToken")] public string QuoteToken { get; set; } = string.Empty;
    [JsonPropertyName("aid")] public string Aid { get; set; } = string.Empty;
    [JsonPropertyName("price")] public int Price { get; set; }
    [JsonPropertyName("supportsWithoutKit")] public bool SupportsWithoutKit { get; set; } = true;
}
