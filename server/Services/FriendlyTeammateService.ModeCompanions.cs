using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private readonly HashSet<string> preparingModeCompanions = new(StringComparer.Ordinal);
    private FriendlyModeCompanion? CompanionPolicy(BotBase teammate) =>
        FriendlyGameplayModeAddons.Policy(settingsService.LoadSettings().GameplayMode, teammate);

    private void EnsureModeCompanions(MongoId sessionId)
    {
        lock (CreationLock)
        {
            string key = settingsService.LoadSettings().GameplayMode + "/" + sessionId;
            // Account-ID allocation reads the roster again; do not recursively seed it.
            if (!preparingModeCompanions.Add(key)) return;
            try
            {
                foreach (var companion in FriendlyGameplayModeAddons.Companions(settingsService.LoadSettings().GameplayMode)
                    .Where(companion => companion.SeedForTesting))
                {
                    string receipt = $"addon-companion-{companion.Role.ToLowerInvariant()}.json";
                    if (storage.Exists(sessionId, receipt)) continue;
                    if (storage.ReadProfiles(sessionId).Any(profile => CompanionPolicy(profile)?.Role == companion.Role))
                    {
                        storage.Write(sessionId, receipt, new { initialized = true });
                        continue;
                    }
                    var player = GetPlayerProfile(sessionId);
                    var teammate = GenerateTeammateBot(sessionId, new BotGenerationDetails
                    {
                        IsPmc = false, Side = "Usec", Role = companion.Role,
                        PlayerLevel = Math.Max(1, player.Info?.Level ?? 1), PlayerName = player.Info?.Nickname,
                        BotCountToGenerate = 1, BotDifficulty = "hard", Location = TeammateGenerationLocation,
                        IsPlayerScav = false, AllPmcsHaveSameNameAsPlayer = false,
                    });
                    teammate.Aid = GetUniqueAccountId(sessionId);
                    NormalizeTeammateProfile(teammate, player);
                    teammate.Info!.Side = "Usec";
                    teammate.Info.Nickname = companion.Nickname;
                    teammate.Info.LowerNickname = companion.Nickname.ToLowerInvariant();
                    companion.PrepareProfile?.Invoke(teammate);
                    var settings = CreateDefaultTeammateSettings(teammate.Customization);
                    var receipts = new Dictionary<string, string> { [receipt] = "{\"initialized\":true}" };
                    string? skillsReceipt = CompanionSkillsReceipt(teammate, companion);
                    if (skillsReceipt != null) receipts[skillsReceipt] = "{\"initialized\":true}";
                    SaveTeammateWithDefaultEquipment(sessionId, teammate, includeSecureContainer: true, settings, receipts);
                }
            }
            finally { preparingModeCompanions.Remove(key); }
        }
    }

    private void RequireCompanionCustomizationAllowed(BotBase teammate)
    {
        if (CompanionPolicy(teammate)?.ProficiencyOnly == true)
            throw new FriendlyTeammateException("CompanionCustomizationLocked");
    }
}
