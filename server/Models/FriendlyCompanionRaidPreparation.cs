using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyCompanionRaidPreparationRequest : IRequestData
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("members")] public List<string> Members { get; set; } = [];
    [JsonPropertyName("memberRevisions")] public Dictionary<string, string> MemberRevisions { get; set; } = new(StringComparer.Ordinal);
    [JsonPropertyName("raidConfiguration")] public GetRaidConfigurationRequestData? RaidConfiguration { get; set; }
}

public record FriendlyCompanionRaidPreparationResponse(string Token, IReadOnlyList<string> Members);
