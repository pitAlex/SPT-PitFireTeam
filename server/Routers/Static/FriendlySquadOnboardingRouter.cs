using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlySquadOnboardingRouter(JsonUtil jsonUtil, HttpResponseUtil http,
    FriendlySquadOnboardingService onboarding, ISptLogger<FriendlySquadOnboardingRouter> logger)
    : StaticRouter(jsonUtil,
    [
        new RouteAction<EmptyRequestData>("/singleplayer/pitfireteam/squad-onboarding",
            (url, info, sessionId, output, cancellationToken) => Run(sessionId, () => onboarding.GetStatus(sessionId), http, logger)),
        new RouteAction<FriendlySquadOnboardingRequest>("/singleplayer/pitfireteam/squad-onboarding/complete",
            (url, info, sessionId, output, cancellationToken) => Run(sessionId, () => onboarding.Complete(sessionId, info.GameplayMode), http, logger)),
        new RouteAction<EmptyRequestData>("/singleplayer/pitfireteam/squad-onboarding/refreshed",
            (url, info, sessionId, output, cancellationToken) => ScreenRefreshed(sessionId, onboarding, http, logger)),
        new RouteAction<EmptyRequestData>("/singleplayer/pitfireteam/squad-onboarding/deliver",
            (url, info, sessionId, output, cancellationToken) => Run(sessionId, () => onboarding.DeliverWelcomeInvitation(sessionId), http, logger))
    ])
{
    private static async ValueTask<string> ScreenRefreshed(MongoId sessionId, FriendlySquadOnboardingService onboarding,
        HttpResponseUtil http, ISptLogger<FriendlySquadOnboardingRouter> logger)
    {
        FriendlySquadOnboardingResponse? state = null;
        string output = await Run(sessionId, () => state = onboarding.ScreenRefreshed(sessionId), http, logger);
        if (state?.WelcomePending == true)
            _ = DeliverAfterDelay(sessionId, state.DelayMilliseconds, onboarding, http, logger);
        return output;
    }

    private static async Task DeliverAfterDelay(MongoId sessionId, int delay, FriendlySquadOnboardingService onboarding,
        HttpResponseUtil http, ISptLogger<FriendlySquadOnboardingRouter> logger)
    {
        try
        {
            // Never hold the mode/roster gate during the delay. The durable intent and
            // target-mode receipt make concurrent timers and restart recovery harmless.
            await Task.Delay(Math.Max(0, delay));
            await Run(sessionId, () => onboarding.DeliverWelcomeInvitation(sessionId), http, logger);
        }
        catch (Exception ex) { logger.Warning($"Welcome invitation remains pending for '{sessionId}': {ex}"); }
    }

    private static ValueTask<string> Run(MongoId sessionId, Func<FriendlySquadOnboardingResponse> action,
        HttpResponseUtil http, ISptLogger<FriendlySquadOnboardingRouter> logger) => FriendlyModeRequestGate.Run(() =>
    {
        try { return new ValueTask<string>(http.GetBody(action())); }
        catch (Exception ex)
        {
            logger.Warning($"Squad onboarding failed for '{sessionId}': {ex}");
            return new ValueTask<string>(http.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: "SquadOnboardingFailed"));
        }
    });
}
