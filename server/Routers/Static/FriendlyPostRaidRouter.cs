using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyPostRaidRouter(JsonUtil jsonUtil, FriendlyPostRaidCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyPostRaidReturnItemsRequest>(
                "/singleplayer/returnitems",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.ReturnItems(url, info, sessionId)
            ),
            new RouteAction<FriendlyPostRaidTeamEscapedRequest>(
                "/singleplayer/teamescaped",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.TeamEscaped(url, info, sessionId)
            ),
            new RouteAction<FriendlyRecruitPickupRequest>(
                "/singleplayer/pitfireteam/recruitpickup",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.RecruitPickup(url, info, sessionId)
            ),
            new RouteAction<FriendlyPostRaidKillMessageRequest>(
                "/singleplayer/pitfireteam/postraid/kill-message",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.RecordKillMessage(url, info, sessionId)
            ),
            new RouteAction<FriendlyPostRaidProtectedItemsRequest>(
                "/singleplayer/pitfireteam/postraid/protected-items",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.RegisterProtectedItems(url, info, sessionId)
            ),
            new RouteAction<EndLocalRaidRequestData>(
                "/client/match/local/end",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.EndLocalRaid(url, info, sessionId, output)
            ),
            new RouteAction<FriendlyTeammateDeathEscapeRequest>(
                "/singleplayer/pitfireteam/teammate/raid-outcomes",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.RaidOutcomes(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateDeathEscapeRequest>(
                "/singleplayer/pitfireteam/teammate/death-escape",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.DeathEscape(url, info, sessionId)
            ),
        ]
    )
{ }
