using pitTeam.Server.Services;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class PmcKarmaRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse, PmcKarmaService karma)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<PmcKarmaRequest>("/singleplayer/pitfireteam/pmc-karma",
            async (url, info, sessionId, output, cancellationToken) =>
                await FriendlyModeRequestGate.Run(async () => httpResponse.GetBody(await karma.Record(sessionId, info))))
    ])
{ }
