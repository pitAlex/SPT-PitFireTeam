using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyEncounterPenaltyRouter(JsonUtil jsonUtil, HttpResponseUtil response, FriendlyEncounterPenaltyService penalties)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>("/singleplayer/pitfireteam/friendly-encounter-penalties",
            (url, info, sessionId, output, cancellationToken) => FriendlyModeRequestGate.Run(() =>
                new ValueTask<string>(response.GetBody(penalties.Get(sessionId)))))
    ]) { }
