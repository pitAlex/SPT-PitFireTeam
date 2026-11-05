using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    public bool TryGetQuestCompanionProfile(MongoId sessionId, string role, string? memberId,
        int selectedMemberCount, double? healthMultiplier, out BotBase? profile, string? preparationToken = null)
    {
        profile = null;
        if (selectedMemberCount != 0 || !string.IsNullOrWhiteSpace(memberId)) return false;
        var configuration = profileActivityService.GetProfileActivityRaidData(sessionId).RaidConfiguration;
        var policy = EligibleQuestCompanion(sessionId, role, configuration);
        if (policy == null) return false;
        if (!string.IsNullOrWhiteSpace(preparationToken))
            profile = PreparedQuestCompanionForSpawn(sessionId, policy.Role, configuration!, healthMultiplier, preparationToken);
        else profile = GenerateQuestCompanion(sessionId, policy, configuration!, healthMultiplier);
        return true;
    }

    private BotBase PreparedQuestCompanionForSpawn(MongoId sessionId, string role,
        GetRaidConfigurationRequestData configuration, double? healthMultiplier, string preparationToken)
    {
        if (!preparedCompanionRaids.TryGetValue(sessionId.ToString(), out var prepared) || prepared.Token != preparationToken ||
            prepared.ModeGeneration != FriendlyModeRequestGate.Generation)
            throw new InvalidOperationException("The displayed quest companion is unavailable; refusing to reroll it");
        var reserved = prepared.QuestCompanions.GetValueOrDefault(role) ??
            (prepared.QuestCompanion?.Info?.Settings?.Role == role ? prepared.QuestCompanion : null);
        if (reserved == null) throw new InvalidOperationException("The displayed quest companion is unavailable; refusing to reroll it");
        var reservedConfiguration = jsonUtil.Deserialize<GetRaidConfigurationRequestData>(prepared.Configuration)!;
        if (reservedConfiguration.Location != configuration.Location || reservedConfiguration.TimeVariant != configuration.TimeVariant)
            throw new InvalidOperationException("Quest companion raid configuration changed");
        var profile = cloner.Clone(reserved)!;
        ApplyTemporaryHealthMultiplier(profile, healthMultiplier);
        logger.Info($"[QuestCompanion] spawn prepared token={preparationToken} role={role} profile={profile.Id}");
        return profile;
    }

    private FriendlyModeCompanion? EligibleQuestCompanion(MongoId sessionId, string role, GetRaidConfigurationRequestData? configuration)
    {
        var policy = FriendlyGameplayModeAddons.PolicyForRole(settingsService.LoadSettings().GameplayMode, role);
        return configuration?.Side == SideType.Pmc && policy?.RaidReady == true && policy.GenerateEquipmentEachRaid &&
            policy.CanSpawnForQuest?.Invoke(GetPlayerProfile(sessionId), configuration.Location ?? "") == true ? policy : null;
    }

    private BotBase GenerateQuestCompanion(MongoId sessionId, FriendlyModeCompanion policy,
        GetRaidConfigurationRequestData configuration, double? healthMultiplier)
    {
        var player = GetPlayerProfile(sessionId);
        var generated = GenerateTeammateBot(sessionId, new BotGenerationDetails
        {
            IsPmc = false, Side = player.Info!.Side!, Role = policy.Role,
            PlayerLevel = Math.Max(1, player.Info.Level ?? 1), PlayerName = player.Info.Nickname,
            BotCountToGenerate = 1, BotDifficulty = "hard", IsPlayerScav = false,
            AllPmcsHaveSameNameAsPlayer = false, Location = configuration!.Location!
        }, configuration);
        if (!HasProperRaidKit(generated) || !string.Equals(generated.Info?.Settings?.Role, policy.Role, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Quest companion generation returned an invalid role/kit");
        generated.Info!.Nickname = policy.Nickname;
        generated.Info.LowerNickname = policy.Nickname.ToLowerInvariant();
        generated.Aid = GetUniqueAccountId(sessionId);
        policy.PrepareProfile?.Invoke(generated);
        var profile = MergeGeneratedCompanionEquipment(generated, generated, healthMultiplier);
        logger.Info($"[QuestCompanion] generated mode={settingsService.LoadSettings().GameplayMode} role={policy.Role} profile={profile.Id} location={configuration.Location}");
        return profile;
    }
}
