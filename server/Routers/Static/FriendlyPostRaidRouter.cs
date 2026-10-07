using pitTeam.Server.Services;
using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyPostRaidRouter(JsonUtil jsonUtil, FriendlyPostRaidCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<ItemEventRouterRequest>(
                "/client/game/profile/items/moving",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.InsuranceMailClaim(info, sessionId, output))
            ),
            new RouteAction<FollowerInsuranceRaidDisplayRequest>(
                "/singleplayer/pitfireteam/insurance/raid-display",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.InsuranceRaidDisplay(url, info, sessionId))
            ),
            new RouteAction<FollowerInsuranceRaidCompletionRequest>(
                "/singleplayer/pitfireteam/insurance/raid-reports-complete",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.InsuranceReportsComplete(url, info, sessionId))
            ),
            new RouteAction<FriendlyPostRaidReturnItemsRequest>(
                "/singleplayer/returnitems",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.ReturnItems(url, info, sessionId))
            ),
            new RouteAction<FriendlyPostRaidTeamEscapedRequest>(
                "/singleplayer/teamescaped",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.TeamEscaped(url, info, sessionId))
            ),
            new RouteAction<FriendlyRecruitPickupRequest>(
                "/singleplayer/pitfireteam/recruitpickup",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.RecruitPickup(url, info, sessionId))
            ),
            new RouteAction<FriendlyPostRaidKillMessageRequest>(
                "/singleplayer/pitfireteam/postraid/kill-message",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.RecordKillMessage(url, info, sessionId))
            ),
            new RouteAction<FriendlyPostRaidProtectedItemsRequest>(
                "/singleplayer/pitfireteam/postraid/protected-items",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.RegisterProtectedItems(url, info, sessionId))
            ),
            new RouteAction<StartLocalRaidRequestData>(
                "/client/match/local/start",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(async () => { string result = await callbacks.StartLocalRaid(url, info, sessionId, output); FriendlyModeRequestGate.StartRaid(sessionId.ToString()); return result; })
            ),
            new RouteAction<EndLocalRaidRequestData>(
                "/client/match/local/end",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(async () => { string result = await callbacks.EndLocalRaid(url, info, sessionId, output); FriendlyModeRequestGate.EndRaid(sessionId.ToString()); return result; })
            ),
            new RouteAction<FriendlyTeammateDeathEscapeRequest>(
                "/singleplayer/pitfireteam/teammate/raid-outcomes",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.RaidOutcomes(url, info, sessionId))
            ),
            new RouteAction<FriendlyTeammateDeathEscapeRequest>(
                "/singleplayer/pitfireteam/teammate/death-escape",
                async (url, info, sessionId, output, cancellationToken) => await FriendlyModeRequestGate.Run(() => callbacks.DeathEscape(url, info, sessionId))
            ),
        ]
    )
{ }
