using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateProficiencyRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("proficiency")]
    public FriendlyTeammateProficiencySettings? Proficiency { get; set; }
}
