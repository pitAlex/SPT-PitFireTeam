using pitTeam.Server.Callbacks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyTeammateSocialRouter(JsonUtil jsonUtil, FriendlyTeammateSocialCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<EmptyRequestData>(
                "/client/friend/list",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.MergeFriendList(url, info, sessionId, output)
            ),
            new RouteAction<EmptyRequestData>(
                "/client/friend/request/list/inbox",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.MergeFriendRequestInbox(url, info, sessionId, output)
            ),
            new RouteAction<GetOtherProfileRequest>(
                "/client/profile/view",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.MergeProfileView(url, info, sessionId, output)
            ),
            new RouteAction<AcceptFriendRequestData>(
                "/client/friend/request/accept",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.AcceptFriendRequest(url, info, sessionId, output)
            ),
            new RouteAction<EmptyRequestData>(
                "/client/friend/request/accept-all",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.AcceptAllFriendRequests(url, info, sessionId, output)
            ),
            new RouteAction<DeclineFriendRequestData>(
                "/client/friend/request/decline",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.DeclineFriendRequest(url, info, sessionId, output)
            ),
            new RouteAction<DeleteFriendRequest>(
                "/client/friend/delete",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.DeleteFriend(url, info, sessionId, output)
            ),
        ]
    ) { }
