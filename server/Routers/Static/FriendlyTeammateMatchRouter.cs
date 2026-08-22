using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyTeammateMatchRouter(JsonUtil jsonUtil, FriendlyTeammateMatchCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<MatchGroupInviteSendRequest>(
                "/client/match/group/invite/send",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SendGroupInvite(url, info, sessionId, output)
            ),
            new RouteAction<EmptyRequestData>(
                "/client/game/bot/followerdetails",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GetFollowerDetails(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateFollowerProgressBatchRequest>(
                "/client/game/bot/followerprogress",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.PersistFollowerProgress(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateFollowerGenerateRequest>(
                "/client/game/bot/followergenerate",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GenerateFollowerProfile(url, info, sessionId)
            ),
        ]
    ) { }
