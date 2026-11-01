using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateTreatmentRequest : IRequestData
{
    [JsonPropertyName("aid")] public string? Aid { get; set; }
    [JsonPropertyName("quoteToken")] public string? QuoteToken { get; set; }
    [JsonPropertyName("syncPayment")] public bool SyncPayment { get; set; }
}

public record FriendlyTeammateTreatmentResponse : FriendlyTeammateDefaultEquipmentResponse
{
    [JsonPropertyName("health")] public BotBaseHealth Health { get; set; } = new();
    [JsonPropertyName("quoteToken")] public string? QuoteToken { get; set; }
    [JsonPropertyName("price")] public int Price { get; set; }
    [JsonPropertyName("regenerationPerSecond")] public Dictionary<string, double>? RegenerationPerSecond { get; set; }
    [JsonPropertyName("loopSeconds")] public double LoopSeconds { get; set; }
}

// The player JSON and encrypted teammate database cannot share a transaction. Persist this
// intent before saving payment, then commit only teammate HP and the receipt together.
public record FriendlyTeammateTreatmentJournal
{
    public string Token { get; set; } = string.Empty;
    public string Aid { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public DateTime ExpiresUtc { get; set; }
    public int Price { get; set; }
    public string HealthBefore { get; set; } = string.Empty;
    public BotBaseHealth HealthAfter { get; set; } = new();
    public string? PlayerBefore { get; set; }
    public string? PlayerAfter { get; set; }
}
