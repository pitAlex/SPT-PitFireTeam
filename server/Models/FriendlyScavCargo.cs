using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public sealed class FriendlyScavCargoRequest : IRequestData
{
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    [JsonPropertyName("exitName")] public string? ExitName { get; set; }
    [JsonPropertyName("transit")] public bool Transit { get; set; }
    [JsonPropertyName("scavProfileIds")] public List<string> ScavProfileIds { get; set; } = [];
    [JsonPropertyName("items")] public List<Item> Items { get; set; } = [];
    public string? InsuranceReportId { get; set; }
}

public sealed class FriendlyScavCargoLedger
{
    public string? ServerId { get; set; }
    public bool Allegiance { get; set; }
    public FriendlyScavCargoRequest? Cargo { get; set; }
    public string? MessageId { get; set; }
    public string? State { get; set; }
    public List<string> DeliveredIds { get; set; } = [];
    public bool NativeCoopGift { get; set; }
    public int? ExpectedMailItemCount { get; set; }
}
