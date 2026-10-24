using System.Text.Json.Serialization;

namespace pitTeam.Server.Models;

public record FriendlyTeammateDeleteResponse
{
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("playerStashItems")]
    public List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>? PlayerStashItems { get; set; }
}
