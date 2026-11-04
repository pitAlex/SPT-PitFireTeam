using System.Text.Json.Serialization;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.GoingRogue.Server;

public sealed record RogueStandingReport(
    [property: JsonPropertyName("raidId")] string RaidId,
    [property: JsonPropertyName("victimId")] string VictimId,
    [property: JsonPropertyName("role")] string Role) : IRequestData;

[Injectable(InjectionType.Singleton)]
public sealed class RogueStandingService(SaveServer saves, FriendlyServerSettingsService settings, FileUtil files, JsonUtil json,
    FriendlyTeammateStorage storage)
{
    public async ValueTask<double> Record(MongoId sessionId, RogueStandingReport report)
    {
        if (settings.LoadSettings().GameplayMode != "Allegiance" || !Guid.TryParseExact(report.RaidId, "N", out _) ||
            string.IsNullOrWhiteSpace(report.VictimId) || report.VictimId.Length > 128 || !RogueAlliancePolicy.IsAlly(report.Role))
            throw new ArgumentException("Invalid Rogue standing report.");
        var profile = saves.GetProfile(sessionId);
        var player = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("PMC profile unavailable.");
        if (player.Quests?.Any(q => q.QId.ToString() == RogueAlliancePolicy.Quests[0] && q.Status == QuestStatusEnum.Success) != true ||
            player.TradersInfo?.TryGetValue(KnightTraderCatalog.Id, out var info) != true || info == null || info.Disabled == true)
            throw new InvalidOperationException("Rogue alliance is not unlocked.");
        if (storage.ReadProfiles(sessionId).Any(member => member.Id.ToString() == report.VictimId))
            return info.Standing ?? 0d;
        string key = "pitFireTeam/rogue-standing/" + report.RaidId + "/" + report.VictimId;
        profile.SptData ??= new Spt();
        profile.SptData.Migrations ??= [];
        if (!profile.SptData.Migrations.ContainsKey(key))
        {
            double before = info.Standing ?? 0d;
            if (!double.IsFinite(before)) throw new InvalidOperationException("Invalid Knight standing.");
            info.Standing = RogueAlliancePolicy.AfterPenalty(before, report.Role);
            profile.SptData.Migrations[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        if (saves.IsProfileInvalidOrUnloadable(sessionId)) throw new IOException("PMC profile is not saveable.");
        await saves.SaveProfileAsync(sessionId);
        var saved = json.Deserialize<SptProfile>(files.ReadFile(Path.Combine("user", "profiles", $"{sessionId}.json")));
        if (saved?.CharacterData?.PmcData?.TradersInfo?.GetValueOrDefault(KnightTraderCatalog.Id)?.Standing != info.Standing ||
            saved?.SptData?.Migrations?.ContainsKey(key) != true) throw new IOException("Knight standing persistence was not confirmed.");
        return info.Standing ?? 0d;
    }
}

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class RogueStandingRouter(JsonUtil json, HttpResponseUtil http, RogueStandingService standing)
    : StaticRouter(json, [new RouteAction<RogueStandingReport>("/singleplayer/going-rogue/knight/standing",
        (url, request, sessionId, output, cancellation) => FriendlyModeRequestGate.Run(async () =>
            http.GetBody(new { standing = await standing.Record(sessionId, request) }))) ]);
