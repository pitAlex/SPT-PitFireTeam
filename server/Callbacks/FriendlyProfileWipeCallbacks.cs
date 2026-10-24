using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Launcher;
using SPTarkov.Server.Core.Models.Spt.Launcher;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyProfileWipeCallbacks(SaveServer saveServer, FriendlyTeammateService teammateService,
    JsonUtil jsonUtil, ISptLogger<FriendlyProfileWipeCallbacks> logger)
{
    public ValueTask<string> AfterWipe(RegisterData request, string? output)
    {
        if (string.IsNullOrWhiteSpace(output)
            || jsonUtil.Deserialize<LauncherV2WipeResponse>(output)?.Response != true)
            return new ValueTask<string>(output ?? string.Empty);

        // Launcher routes identify the account by username, not by the HTTP session/cookie.
        var profile = saveServer.GetProfiles().SingleOrDefault(entry => entry.Value.ProfileInfo?.Username == request.Username);
        if (string.IsNullOrWhiteSpace(request.Username) || profile.Key.IsEmpty || profile.Value?.ProfileInfo?.IsWiped != true)
            throw new InvalidOperationException("Cannot resolve the account accepted for the launcher profile wipe.");

        teammateService.WipeProfile(profile.Key);
        logger.Info($"Launcher profile wipe cleared both squads and first-visit state for '{profile.Key}'.");
        return new ValueTask<string>(output);
    }
}
