using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    public bool TryGetQuestCompanionProfile(MongoId sessionId, string role, string? memberId,
        int selectedMemberCount, double? healthMultiplier, out BotBase? profile)
    {
        profile = null;
        if (selectedMemberCount != 0 || !string.IsNullOrWhiteSpace(memberId)) return false;
        var mode = settingsService.LoadSettings().GameplayMode;
        var policy = FriendlyGameplayModeAddons.PolicyForRole(mode, role);
        var player = GetPlayerProfile(sessionId);
        var configuration = profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration;
        if (configuration?.Side != SideType.Pmc || policy?.RaidReady != true || policy.GenerateEquipmentEachRaid != true ||
            policy.CanSpawnForQuest?.Invoke(player, configuration?.Location ?? "") != true) return false;
        var generated = GenerateTeammateBot(sessionId, new BotGenerationDetails
        {
            IsPmc = false, Side = player.Info!.Side!, Role = policy.Role,
            PlayerLevel = Math.Max(1, player.Info.Level ?? 1), PlayerName = player.Info.Nickname,
            BotCountToGenerate = 1, BotDifficulty = "hard", IsPlayerScav = false,
            AllPmcsHaveSameNameAsPlayer = false, Location = configuration!.Location!
        });
        if (!HasProperRaidKit(generated) || !string.Equals(generated.Info?.Settings?.Role, policy.Role, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Quest companion generation returned an invalid role/kit");
        generated.Info!.Nickname = policy.Nickname;
        generated.Info.LowerNickname = policy.Nickname.ToLowerInvariant();
        policy.PrepareProfile?.Invoke(generated);
        profile = MergeGeneratedCompanionEquipment(generated, generated, healthMultiplier);
        logger.Info($"[QuestCompanion] generated mode={mode} role={role} profile={profile.Id} location={configuration.Location}");
        return true;
    }
}
