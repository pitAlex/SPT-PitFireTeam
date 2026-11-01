using System.Text.Json.Serialization;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.GoingRogue.Server;

public sealed record KnightTraderSessionState(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("unlocked")] bool Unlocked,
    [property: JsonPropertyName("disabled")] bool Disabled);

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public sealed class KnightSessionRouter(JsonUtil json, HttpResponseUtil http, KnightTraderAccess access)
    : StaticRouter(json,
    [
        new RouteAction<EmptyRequestData>("/singleplayer/going-rogue/knight/session",
            (url, info, sessionId, output, cancellationToken) => FriendlyModeRequestGate.Run(() =>
                new ValueTask<string>(http.GetBody(access.SessionState(sessionId)))))
    ]);
