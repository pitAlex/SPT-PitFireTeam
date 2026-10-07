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

    // Original IDs for loss exclusion when a client path clones return trees.
    [JsonPropertyName("insuranceSourceItemIdsByRoot")]
    public Dictionary<string, List<string>>? InsuranceSourceItemIdsByRoot { get; set; }

    // Exact original ID for each cloned item ID in the delivered flat tree.
    [JsonPropertyName("insuranceSourceItemIdByReturnId")]
    public Dictionary<string, string>? InsuranceSourceItemIdByReturnId { get; set; }

    [JsonPropertyName("member")]
    public FriendlyPostRaidMember? Member { get; set; }
}
