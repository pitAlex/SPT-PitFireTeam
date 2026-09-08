using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyServerSettingsRequest : IRequestData
{
    public const string DefaultLoadoutManagementMode = "Restricted";

    [JsonPropertyName("pmcArmbands")]
    public bool PmcArmbands { get; set; } = true;

    private string loadoutManagementMode = DefaultLoadoutManagementMode;

    [JsonPropertyName("loadoutManagementMode")]
    public string LoadoutManagementMode
    {
        get => loadoutManagementMode;
        set => loadoutManagementMode = NormalizeLoadoutManagementMode(value);
    }

    public static string NormalizeLoadoutManagementMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "immersive" => "Immersive",
            "extreme" or "realistic" => "Extreme",
            _ => DefaultLoadoutManagementMode,
        };
    }

    [JsonPropertyName("restrictedGearMaintenance")]
    public bool RestrictedGearMaintenance { get; set; } = false;
}
