using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Ws;

namespace pitTeam.Server.Models;

public record FriendlyGroupMatchInviteDecline : WsNotificationEvent
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("Nickname")]
    public string? Nickname { get; set; }
}

public record FriendlyGroupMatchInviteAccept : WsNotificationEvent
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("Info")]
    public CharacterInfo? Info { get; set; }

    [JsonPropertyName("PlayerVisualRepresentation")]
    public PlayerVisualRepresentation? VisualRepresentation { get; set; }

    [JsonPropertyName("IsReady")]
    public bool? IsReady { get; set; }
}
