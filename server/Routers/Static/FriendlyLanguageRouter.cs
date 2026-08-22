using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyLanguageRouter(JsonUtil jsonUtil, FriendlyLanguageCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyLanguageRequest>(
                "/singleplayer/pitfireteam/lang",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Get(url, info, sessionId)
            ),
            new RouteAction<FriendlyLanguageRequest>(
                "/singleplayer/pitlang",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Get(url, info, sessionId)
            ),
        ]
    )
{ }
