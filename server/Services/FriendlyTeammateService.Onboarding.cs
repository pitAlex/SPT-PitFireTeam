using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    internal FriendlyRecruitRequestEntry PrepareWelcomeInvitation(MongoId sessionId)
    {
        var player = GetPlayerProfile(sessionId);
        var teammate = GenerateTeammateBot(sessionId, new BotGenerationDetails
        {
            IsPmc = true,
            Side = player.Info!.Side!,
            Role = GetPmcRole(player.Info.Side),
            PlayerLevel = 1,
            PlayerName = player.Info.Nickname,
            BotRelativeLevelDeltaMax = 0,
            BotRelativeLevelDeltaMin = 0,
            BotCountToGenerate = 1,
            BotDifficulty = "hard",
            Location = TeammateGenerationLocation,
            LocationSpecificPmcLevelOverride = new MinMax<int> { Min = 1, Max = 1 },
            IsPlayerScav = false,
            AllPmcsHaveSameNameAsPlayer = false
        });
        NormalizeTeammateProfile(teammate, player);
        teammate.Aid = GetUniqueAccountId(sessionId);
        teammate.Info!.Nickname = EnsureUniqueRecruitNickname(sessionId, teammate.Info.Nickname!);
        teammate.Info.LowerNickname = teammate.Info.Nickname.ToLowerInvariant();
        NormalizeWelcomeProfile(teammate);
        var request = new FriendlyRecruitRequestEntry
        {
            IsWelcomeTeammate = true,
            ProfileId = NormalizeRequiredValue(teammate.Id?.ToString(), "profileId"),
            AccountId = teammate.Aid.ToString()!,
            Nickname = NormalizeRequiredValue(teammate.Info.Nickname, "nickname"),
            Level = 1,
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

    private static void NormalizeWelcomeProfile(BotBase teammate)
    {
        teammate.Info ??= new();
        teammate.Stats ??= new();
        teammate.Stats.Eft ??= new();
        teammate.Info.Level = 1;
        teammate.Info.Experience = 0;
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
