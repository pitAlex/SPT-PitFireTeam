using SPTarkov.Server.Core.Models.Enums;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;
using System;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyTeammateSocialCallbacks(
    FriendlyTeammateService teammateService,
    FriendlyRecruitService recruitService,
    FriendlySquadOnboardingService onboarding,
    HttpResponseUtil httpResponseUtil,
    JsonUtil jsonUtil
)
{
    public ValueTask<string> MergeFriendList(string url, EmptyRequestData _, MongoId sessionId, string? previousOutput)
    {
        var body = DeserializeBody<GetFriendListDataResponse>(previousOutput);
        var response = body?.Data ?? new GetFriendListDataResponse
        {
            Friends = [],
            Ignore = [],
            InIgnoreList = [],
        };

        response.Friends ??= [];
        foreach (var teammate in teammateService.ListTeammateDialogs(sessionId))
        {
            if (response.Friends.Any(existing =>
                existing.Id == teammate.Id ||
                (existing.Aid != 0 && teammate.Aid != 0 && existing.Aid == teammate.Aid)))
            {
                continue;
            }

            response.Friends.Add(teammate);
        }

        return new ValueTask<string>(httpResponseUtil.GetBody(response, body?.Err ?? 0, body?.ErrMsg));
    }

    public ValueTask<string> MergeFriendRequestInbox(string url, EmptyRequestData _, MongoId sessionId, string? previousOutput)
    {
        // Recover a due welcome delivery after restart or a lost client response.
        onboarding.DeliverWelcomeInvitation(sessionId);
        var body = DeserializeBody<List<FriendlySocialFriendRequestEntry>>(previousOutput);
        var response = body?.Data ?? [];

        foreach (var request in recruitService.ListRecruitFriendRequests(sessionId))
        {
            if (response.Any(existing => string.Equals(existing.From, request.From, StringComparison.Ordinal)))
            {
                continue;
            }

            response.Add(request);
        }

        return new ValueTask<string>(httpResponseUtil.GetBody(response, body?.Err ?? 0, body?.ErrMsg));
    }

    public ValueTask<string> MergeProfileView(string url, GetOtherProfileRequest request, MongoId sessionId, string? previousOutput)
    {
        var body = DeserializeBody<GetOtherProfileResponse>(previousOutput);

        if (teammateService.TryGetPendingTeammateProfile(sessionId, request.AccountId, out var pendingProfile))
            return new ValueTask<string>(httpResponseUtil.GetBody(pendingProfile));

        if (teammateService.TryGetTeammateProfile(sessionId, request.AccountId, out var teammateProfile)
            && teammateProfile != null)
        {
            return new ValueTask<string>(httpResponseUtil.GetBody(teammateProfile));
        }

        if (recruitService.TryGetRecruitProfile(sessionId, request.AccountId, out var recruitProfile)
            && recruitProfile != null)
        {
            return new ValueTask<string>(httpResponseUtil.GetBody(recruitProfile));
        }

        if (int.TryParse(request.AccountId, out var requestedAid) && body?.Data?.Aid == requestedAid)
        {
            return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
        }

        return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
    }

    public ValueTask<string> DeleteFriend(string url, DeleteFriendRequest request, MongoId sessionId, string? previousOutput)
    {
        try
        {
            teammateService.DeleteTeammateByProfileId(sessionId, request.FriendId);
            return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
        }
        catch (Exception ex)
        {
            return new(httpResponseUtil.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError,
                errmsg: ex is FriendlyTeammateException ? ex.Message : "TeammateDeleteFailed"));
        }
    }

    public ValueTask<string> AcceptFriendRequest(string url, AcceptFriendRequestData request, MongoId sessionId, string? previousOutput)
    {
        if (recruitService.AcceptRecruitRequest(sessionId, request.ProfileId))
        {
            return new ValueTask<string>(httpResponseUtil.NullResponse());
        }

        return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
    }

    public ValueTask<string> AcceptAllFriendRequests(string url, EmptyRequestData request, MongoId sessionId, string? previousOutput)
    {
        recruitService.AcceptAllRecruitRequests(sessionId);
        return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
    }

    public ValueTask<string> DeclineFriendRequest(string url, DeclineFriendRequestData request, MongoId sessionId, string? previousOutput)
    {
        if (recruitService.DeclineRecruitRequest(sessionId, request.ProfileId))
        {
            return new ValueTask<string>(httpResponseUtil.NullResponse());
        }

        return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
    }

    private FriendlyTeammateBodyResponse<T>? DeserializeBody<T>(string? previousOutput)
    {
        if (string.IsNullOrWhiteSpace(previousOutput))
        {
            return null;
        }

        return jsonUtil.Deserialize<FriendlyTeammateBodyResponse<T>>(previousOutput);
    }
}
