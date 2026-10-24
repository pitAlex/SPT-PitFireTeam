using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyPostRaidKillMessageRequest : IRequestData
{
    [JsonPropertyName("victimProfileId")]
    public string? VictimProfileId { get; set; }

    [JsonPropertyName("victimAccountId")]
    public string? VictimAccountId { get; set; }

    [JsonPropertyName("messageKind")]
    public string? MessageKind { get; set; }

    [JsonPropertyName("messageText")]
    public string? MessageText { get; set; }

    [JsonPropertyName("raidId")]
    public string? RaidId { get; set; }

    [JsonPropertyName("killedAtUnixMs")]
    public long KilledAtUnixMs { get; set; }
}
