using pitTeam.Server.Callbacks;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Launcher;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyProfileWipeRouter(JsonUtil jsonUtil, FriendlyProfileWipeCallbacks callbacks)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<RegisterData>("/launcher/v2/wipe",
            (url, info, sessionId, output, cancellationToken) => FriendlyModeRequestGate.Run(() => callbacks.AfterWipe(info, output)))
    ]) { }
