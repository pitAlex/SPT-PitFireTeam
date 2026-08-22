using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyTeammateMatchCallbacks(
    FriendlyTeammateService teammateService,
    FriendlyPostRaidService postRaidService,
    HttpResponseUtil httpResponseUtil,
    NotificationSendHelper notificationSendHelper,
    MailSendService mailSendService
)
{
    public ValueTask<string> SendGroupInvite(
        string url,
        MatchGroupInviteSendRequest request,
        MongoId sessionId,
        string? previousOutput)
    {
        if (!teammateService.TryGetRaidGroupCharacter(sessionId, request.To, out var teammate, out var rejectionReason))
        {
            if (teammate != null && !string.IsNullOrWhiteSpace(rejectionReason))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);

                    await notificationSendHelper.SendMessageAsync(
                        sessionId,
                        new FriendlyGroupMatchInviteDecline
                        {
                            EventType = NotificationEventType.groupMatchInviteDecline,
                            EventIdentifier = new MongoId(),
                            Aid = teammate.Aid?.ToString(),
                            Nickname = teammate.Info?.Nickname ?? teammate.Aid?.ToString(),
                        }
                    );

                    mailSendService.SendSystemMessageToPlayer(sessionId, rejectionReason, null);
                });

                return new ValueTask<string>(previousOutput ?? httpResponseUtil.GetBody("pitfireteam-teammate-invite"));
            }

            return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            var acceptedTeammate = teammate!;

            await notificationSendHelper.SendMessageAsync(
                sessionId,
                new FriendlyGroupMatchInviteAccept
                {
                    EventType = NotificationEventType.groupMatchInviteAccept,
                    EventIdentifier = new MongoId(),
                    Id = acceptedTeammate.Id,
                    Aid = acceptedTeammate.Aid?.ToString(),
                    Info = acceptedTeammate.Info,
                    VisualRepresentation = acceptedTeammate.VisualRepresentation,
                    IsReady = true,
                }
            );
        });

        return new ValueTask<string>(previousOutput ?? httpResponseUtil.GetBody("pitfireteam-teammate-invite"));
    }

    public ValueTask<string> GenerateFollowerProfile(
        string url,
        FriendlyTeammateFollowerGenerateRequest request,
        MongoId sessionId)
    {
        if (!teammateService.TryGetSpawnProfile(sessionId, request.MemberId, request.Custom?.Health, out var teammate))
        {
            return new ValueTask<string>(httpResponseUtil.GetBody(Array.Empty<object>()));
        }

        string profileId = teammate?.Id.ToString() ?? request.MemberId ?? "unknown";
        HashSet<string> protectedSpawnIds = teammateService.GetProtectedSpawnItemIdsForExtraction(teammate);
        postRaidService.RegisterProtectedRaidItemIds(
            sessionId,
            protectedSpawnIds,
            $"server-generated teammate spawn equipment {profileId}");

        return new ValueTask<string>(httpResponseUtil.GetBody(new[] { teammate }));
    }

    public ValueTask<string> GetFollowerDetails(string url, EmptyRequestData request, MongoId sessionId)
    {
        var details = teammateService.ListFollowerDetails(sessionId);
        return new ValueTask<string>(httpResponseUtil.GetBody(details));
    }

    public ValueTask<string> PersistFollowerProgress(
        string url,
        FriendlyTeammateFollowerProgressBatchRequest request,
        MongoId sessionId)
    {
        teammateService.PersistFollowerProgress(sessionId, request.Entries);
        return new ValueTask<string>(httpResponseUtil.EmptyResponse());
    }
}
