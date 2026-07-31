using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyServerSettingsRequest : IRequestData
{
    public const string DefaultLoadoutManagementMode = "Restricted";

    [JsonPropertyName("pmcArmbands")]
    public bool PmcArmbands { get; set; } = true;

    [JsonPropertyName("loadoutManagementMode")]
    public string LoadoutManagementMode { get; set; } = DefaultLoadoutManagementMode;

    [JsonPropertyName("restrictedGearMaintenance")]
    public bool RestrictedGearMaintenance { get; set; } = false;
}
