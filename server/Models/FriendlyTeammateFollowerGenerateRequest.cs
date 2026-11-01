using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateFollowerGenerateRequest : IRequestData
{
    [JsonPropertyName("QuestCompanionRole")]
    public string? QuestCompanionRole { get; set; }

    [JsonPropertyName("SelectedMemberCount")]
    public int SelectedMemberCount { get; set; }

    [JsonPropertyName("PreparationToken")]
    public string? PreparationToken { get; set; }

    [JsonPropertyName("MemberId")]
    public string? MemberId { get; set; }

    [JsonPropertyName("ScavId")]
    public string? ScavId { get; set; }

    [JsonPropertyName("Custom")]
    public FriendlyTeammateFollowerGenerateCustomization? Custom { get; set; }
}

public record FriendlyTeammateFollowerGenerateCustomization
{
    [JsonPropertyName("Health")]
    public double? Health { get; set; }

    [JsonPropertyName("English")]
    public bool? English { get; set; }
}
