using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    internal FriendlyRecruitRequestEntry PrepareWelcomeInvitation(MongoId sessionId)
    {
        var player = GetPlayerProfile(sessionId);
        var generation = CreateWelcomeGenerationDetails(player);
        var teammate = GenerateTeammateBot(sessionId, generation);
        NormalizeTeammateProfile(teammate, player);
        teammate.Aid = GetUniqueAccountId(sessionId);
        teammate.Info!.Nickname = EnsureUniqueRecruitNickname(sessionId, teammate.Info.Nickname!);
        teammate.Info.LowerNickname = teammate.Info.Nickname.ToLowerInvariant();
        NormalizeWelcomeProfile(teammate, generation.PlayerLevel ?? 1);
        var request = new FriendlyRecruitRequestEntry
        {
            IsWelcomeTeammate = true,
            ProfileId = NormalizeRequiredValue(teammate.Id?.ToString(), "profileId"),
            AccountId = teammate.Aid.ToString()!,
            Nickname = NormalizeRequiredValue(teammate.Info.Nickname, "nickname"),
            Level = teammate.Info.Level ?? 1,
            Side = teammate.Info.Side!,
            Voice = NormalizeRequiredValue(teammate.Customization!.Voice, "voice"),
            Head = NormalizeRequiredValue(teammate.Customization.Head, "head"),
            ProfileJson = jsonUtil.Serialize(teammate)!,
            Aggression = 50f,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        CaptureRecruitmentGearPrice(request);
        return request;
    }

    private BotGenerationDetails CreateWelcomeGenerationDetails(PmcData player)
    {
        int level = Math.Max(1, player.Info?.Level ?? 1);
        return new BotGenerationDetails
        {
            IsPmc = true,
            Side = player.Info!.Side!,
            Role = GetPmcRole(player.Info.Side),
            PlayerLevel = level,
            PlayerName = player.Info.Nickname,
            BotRelativeLevelDeltaMax = 0,
            BotRelativeLevelDeltaMin = 0,
            BotCountToGenerate = 1,
            BotDifficulty = "hard",
            Location = TeammateGenerationLocation,
            LocationSpecificPmcLevelOverride = new MinMax<int> { Min = level, Max = level },
            IsPlayerScav = false,
            AllPmcsHaveSameNameAsPlayer = false
        };
    }

    private void NormalizeWelcomeProfile(BotBase teammate, int level)
    {
        teammate.Info ??= new();
        teammate.Stats ??= new();
        teammate.Stats.Eft ??= new();
        teammate.Info.Level = Math.Max(1, level);
        teammate.Info.Experience = profileHelper.GetExperience(teammate.Info.Level.Value) ?? 0;
        teammate.Skills ??= new Skills { Common = [] };
        foreach (var skill in teammate.Skills.Common ?? [])
        {
            if (skill == null) continue;
            skill.Progress = 0;
            skill.PointsEarnedDuringSession = 0;
        }
        teammate.Skills.Mastering = [];
        teammate.Skills.Points = 0;
        teammate.Stats.Eft.TotalInGameTime = 0;
        teammate.Stats.Eft.OverallCounters = new OverallCounters { Items = [] };
    }
}
