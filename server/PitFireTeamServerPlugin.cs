using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;
using System.IO;
using pitTeam.Server.Services;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace pitTeam.Server;

public record PitFireTeamServerMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "xyz.pit.fireteam";
    public string Name { get; init; } = "PitFireTeam";
    public string Author { get; init; } = "PitAlex";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("0.10.1");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/pitAlex/SPT-PitFireTeam";
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class PitFireTeamServerPlugin(
    ISptLogger<PitFireTeamServerPlugin> logger,
    TradersTable traders,
    LocaleTable locales,
    FriendlyServerSettingsService settingsService
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCourierTraderRegistered();
        EnsureCourierTraderLocales();
        EnsureCourierAvatarIsServed();
        settingsService.ApplyPersistedSettings();
        return Task.CompletedTask;
    }

    private void EnsureCourierTraderRegistered()
    {
        try
        {
            if (traders.ContainsKey(FriendlyCourierTraderProfile.CourierTraderId))
            {
                return;
            }

            traders[FriendlyCourierTraderProfile.CourierTraderId] = FriendlyCourierTraderProfile.CreateTrader();
            logger.Info($"Registered courier trader '{FriendlyCourierTraderProfile.CourierTraderIdValue}'");
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to register courier trader: {ex.Message}");
        }
    }

    private void EnsureCourierTraderLocales()
    {
        try
        {
            string traderId = FriendlyCourierTraderProfile.CourierTraderIdValue;
            foreach (var (locale, lazyGlobal) in locales.Global)
            {
                lazyGlobal.AddTransformer(localized =>
                {
                    if (localized == null)
                    {
                        return localized;
                    }

                    FriendlyCourierTraderProfile.GetLocalizedIdentity(
                        locale,
                        out string nickname,
                        out string location,
                        out string description);

                    localized[$"{traderId} Nickname"] = nickname;
                    localized[$"{traderId} FirstName"] = nickname;
                    localized[$"{traderId} FullName"] = nickname;
                    localized[$"{traderId} Location"] = location;
                    localized[$"{traderId} Description"] = description;
                    return localized;
                });
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to inject courier trader locale keys: {ex.Message}");
        }
    }

    private void EnsureCourierAvatarIsServed()
    {
        try
        {
            string serverRoot = AppContext.BaseDirectory;
            string sourcePath = Path.Combine(
                serverRoot,
                "user",
                "mods",
                "pitFireTeam-ServerMod",
                "Resources",
                "avatars",
                "courier.png");
            if (!File.Exists(sourcePath))
            {
                logger.Warning($"Courier avatar source missing: {sourcePath}");
                return;
            }

            string targetDirectory = Path.Combine(serverRoot, "user", "sptappdata", "files", "trader", "avatar");
            Directory.CreateDirectory(targetDirectory);

            string targetPath = Path.Combine(targetDirectory, FriendlyCourierTraderProfile.CourierAvatarFileName);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to publish courier avatar: {ex.Message}");
        }
    }

}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class PitFireTeamServerPostLoad(
    ISptLogger<PitFireTeamServerPostLoad> logger,
    FriendlyTeammateService teammateService
) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        teammateService.RecoverDuplicateTeammateItemsForAllProfiles();
        logger.Info("PitFireTeam loaded");
        return Task.CompletedTask;
    }
}
