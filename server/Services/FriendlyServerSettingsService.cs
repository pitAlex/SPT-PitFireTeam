using System.Text.Json;
using System.Text.Json.Nodes;
using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyServerSettingsService(
    ISptLogger<FriendlyServerSettingsService> logger,
    LostOnDeathConfig lostOnDeathConfig,
    PmcConfig pmcConfig
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FriendlyServerSettingsRequest LoadSettings()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new FriendlyServerSettingsRequest();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FriendlyServerSettingsRequest>(json, JsonOptions)
                ?? new FriendlyServerSettingsRequest();
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to load pitFireTeam server settings: {ex.Message}");
            return new FriendlyServerSettingsRequest();
        }
    }

    public void SaveAndApply(FriendlyServerSettingsRequest settings)
    {
        settings ??= new FriendlyServerSettingsRequest();
        SaveSettings(settings);
        ApplySettings(settings);
    }

    public void ApplyPersistedSettings()
    {
        ApplySettings(LoadSettings());
    }

    public FriendlyLostOnDeathSettingsResponse GetLostOnDeathSettings()
    {
        LostEquipment equipment = lostOnDeathConfig.Equipment;
        bool playerGearProtectedByRaidStatusOverride = IsPlayerGearProtectedBySvmRaidStatusOverride();

        return new FriendlyLostOnDeathSettingsResponse
        {
            PlayerGearProtectedByRaidStatusOverride = playerGearProtectedByRaidStatusOverride,
            Equipment = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArmBand"] = equipment.ArmBand,
                ["Compass"] = equipment.Compass,
                ["Headwear"] = equipment.Headwear,
                ["Earpiece"] = equipment.Earpiece,
                ["FaceCover"] = equipment.FaceCover,
                ["ArmorVest"] = equipment.ArmorVest,
                ["Eyewear"] = equipment.Eyewear,
                ["TacticalVest"] = equipment.TacticalVest,
                ["PocketItems"] = equipment.PocketItems,
                ["Pockets"] = equipment.PocketItems,
                ["Backpack"] = equipment.Backpack,
                ["Holster"] = equipment.Holster,
                ["FirstPrimaryWeapon"] = equipment.FirstPrimaryWeapon,
                ["SecondPrimaryWeapon"] = equipment.SecondPrimaryWeapon,
                ["Scabbard"] = equipment.Scabbard,
                ["SecuredContainer"] = equipment.SecuredContainer,
            },
        };
    }

    private bool IsPlayerGearProtectedBySvmRaidStatusOverride()
    {
        try
        {
            string modsRoot = Path.Combine(AppContext.BaseDirectory, "user", "mods");
            if (!Directory.Exists(modsRoot))
            {
                return false;
            }

            foreach (string svmRoot in Directory.EnumerateDirectories(modsRoot, "*SVM*"))
            {
                string loaderPath = Path.Combine(svmRoot, "Loader", "loader.json");
                string presetsRoot = Path.Combine(svmRoot, "Presets");
                if (!File.Exists(loaderPath) || !Directory.Exists(presetsRoot))
                {
                    continue;
                }

                if (IsSvmPresetProtectingPlayerDeathGear(loaderPath, presetsRoot, out string presetName, out string reason))
                {
                    logger.Info($"Detected SVM player death gear protection from preset '{presetName}' ({reason}); death-escape will skip player gear recovery.");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to inspect SVM raid-status override: {ex.Message}");
        }

        return false;
    }

    private static bool IsSvmPresetProtectingPlayerDeathGear(
        string loaderPath,
        string presetsRoot,
        out string presetName,
        out string reason)
    {
        presetName = string.Empty;
        reason = string.Empty;

        JsonNode? loader = JsonNode.Parse(File.ReadAllText(loaderPath));
        presetName = loader?["CurrentlySelectedPreset"]?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(presetName) ||
            string.Equals(presetName, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string presetPath = Path.Combine(presetsRoot, presetName + ".json");
        if (!File.Exists(presetPath))
        {
            return false;
        }

        JsonNode? root = JsonNode.Parse(File.ReadAllText(presetPath));
        JsonNode? raids = root?["Raids"];
        if (raids == null || !GetBool(raids, "EnableRaids"))
        {
            return false;
        }

        if (GetBool(raids, "SaveGearAfterDeath"))
        {
            reason = "SaveGearAfterDeath=true";
            return true;
        }

        int killedState = GetInt(raids, "OnKilledState", 1);
        // SVM applies this value to KILLED raids before handing the result to SPT.
        // SURVIVED and RUNNER preserve player gear; SVM also uses TRANSIT as an
        // "ignore raid" sentinel, which leaves the pre-raid profile untouched.
        if (killedState == 0 || killedState == 3 || killedState == 5)
        {
            reason = $"OnKilledState={killedState}";
            return true;
        }

        return false;
    }

    private static bool GetBool(JsonNode node, string propertyName)
    {
        return node[propertyName]?.GetValue<bool>() == true;
    }

    private static int GetInt(JsonNode node, string propertyName, int defaultValue)
    {
        return node[propertyName]?.GetValue<int>() ?? defaultValue;
    }

    private void SaveSettings(FriendlyServerSettingsRequest settings)
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to save pitFireTeam server settings: {ex.Message}");
        }
    }

    private void ApplySettings(FriendlyServerSettingsRequest settings)
    {
        try
        {
            pmcConfig.ForceArmband.Enabled = settings.PmcArmbands;

            if (settings.PmcArmbands)
            {
                pmcConfig.ForceArmband.Usec = ItemTpl.ARMBAND_BLUE;
                pmcConfig.ForceArmband.Bear = ItemTpl.ARMBAND_RED;
            }

            logger.Info($"pitFireTeam PMC armband enforcement: {(settings.PmcArmbands ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to configure PMC armbands: {ex.Message}");
        }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            "pitFireTeam-ServerMod",
            "Resources",
            "settings.json");
    }
}
