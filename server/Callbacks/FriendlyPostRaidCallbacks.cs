using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyPostRaidCallbacks(
    HttpResponseUtil httpResponse,
    FriendlyPostRaidService postRaidService,
    FriendlyRecruitService recruitService,
    FollowerInsuranceRaidDiagnostics insuranceDiagnostics,
    FriendlyTeammateService teammateService
)
{
    public ValueTask<string> ReturnItems(string url, FriendlyPostRaidReturnItemsRequest request, MongoId sessionId)
    {
        postRaidService.HandleReturnItems(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> TeamEscaped(string url, FriendlyPostRaidTeamEscapedRequest request, MongoId sessionId)
    {
        postRaidService.HandleTeamEscaped(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> RecruitPickup(string url, FriendlyRecruitPickupRequest request, MongoId sessionId)
    {
        recruitService.QueueRecruitPickups(sessionId, request.Candidates);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> RecordKillMessage(string url, FriendlyPostRaidKillMessageRequest request, MongoId sessionId)
    {
        postRaidService.RecordKillMessage(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> RegisterProtectedItems(string url, FriendlyPostRaidProtectedItemsRequest request, MongoId sessionId)
    {
        postRaidService.RegisterProtectedRaidItems(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> StartLocalRaid(string url, StartLocalRaidRequestData request, MongoId sessionId, string? output)
    {
        insuranceDiagnostics.BeginRaid(sessionId, request, output);
        return new ValueTask<string>(output ?? httpResponse.NullResponse());
    }

    public ValueTask<string> EndLocalRaid(string url, EndLocalRaidRequestData request, MongoId sessionId, string? output)
    {
        postRaidService.RemoveProtectedTeammateItemsFromExtractedProfile(sessionId, request);
        postRaidService.HandleEndLocalRaidKillMessages(sessionId, request);
        insuranceDiagnostics.EndRaid(sessionId, request);
        return new ValueTask<string>(output ?? httpResponse.NullResponse());
    }

    public ValueTask<string> RaidOutcomes(string url, FriendlyTeammateDeathEscapeRequest request, MongoId sessionId)
    {
        request ??= new FriendlyTeammateDeathEscapeRequest();

        if (request.ResolveOnly)
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.ResolveRaidOutcomes(request.Entries)));
        }

        List<FriendlyTeammateDeathEscapeEntry> entries = request.Entries ?? [];
        if (entries.Any(entry => entry?.RollEscape == true))
        {
            entries = teammateService.ResolveRaidOutcomes(entries).Entries;
        }

        FriendlyTeammateDeathEscapeSummary summary = teammateService.PersistDeathEscapeOutcomes(sessionId, entries);
        insuranceDiagnostics.ObserveOutcomes(sessionId, entries, request.InsuranceServerId, request.InsuranceReportId);
        if (request.Notify)
        {
            postRaidService.HandleDeathEscapeSummary(sessionId, summary);
        }

        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> DeathEscape(string url, FriendlyTeammateDeathEscapeRequest request, MongoId sessionId)
    {
        return RaidOutcomes(url, request, sessionId);
    }

    public ValueTask<string> InsuranceReportsComplete(string url, FollowerInsuranceRaidCompletionRequest request, MongoId sessionId)
    {
        insuranceDiagnostics.CompleteReports(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> InsuranceRaidDisplay(string url, FollowerInsuranceRaidDisplayRequest request, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(
            insuranceDiagnostics.GetDisplayPolicies(sessionId, request?.InsuranceServerId)));
    }
}
