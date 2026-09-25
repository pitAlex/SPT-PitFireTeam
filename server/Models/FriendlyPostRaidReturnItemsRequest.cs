using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyPostRaidReturnItemsRequest : IRequestData
{
    public string? InsuranceServerId { get; set; }
    public string? InsuranceReportId { get; set; }
    [JsonPropertyName("items")]
    public List<Item>? Items { get; set; }

    // Passive original-ID evidence for client paths that clone return trees with new IDs.
    [JsonPropertyName("insuranceSourceItemIdsByRoot")]
    public Dictionary<string, List<string>>? InsuranceSourceItemIdsByRoot { get; set; }

    [JsonPropertyName("member")]
    public FriendlyPostRaidMember? Member { get; set; }
}
