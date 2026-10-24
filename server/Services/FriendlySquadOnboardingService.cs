using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlySquadOnboardingService(
    FriendlyTeammateStorage storage,
    FriendlyServerSettingsService settingsService,
    FriendlyTeammateService teammateService,
    JsonUtil jsonUtil)
{
    private const string StateDocument = "squad-onboarding.json";
    private const string DeliveryDocument = "welcome-invitation-delivery.json";
    private const string RequestsDocument = "recruit-requests.json";

    public FriendlySquadOnboardingResponse GetStatus(MongoId sessionId)
    {
        lock (FriendlyTeammateService.CreationLock) return Response(Load(sessionId));
    }

    public FriendlySquadOnboardingResponse Complete(MongoId sessionId, string mode)
    {
        lock (FriendlyTeammateService.CreationLock)
        {
            var state = Load(sessionId);
            if (state.FirstTimeVisit) return Response(state);
            if (mode is not ("GunsForHire" or "Allegiance") || mode != settingsService.LoadSettings().GameplayMode
                || FriendlyModeRequestGate.HasActiveRaid)
                throw new InvalidOperationException("The selected gameplay mode is not ready for onboarding.");

            bool existingSquad = storage.HasTeammatesInEitherMode(sessionId);
            // Generate once, then durably record the exact profile before exposing completion.
            state.GameplayMode = mode;
            state.WelcomeCandidate = existingSquad ? null : teammateService.PrepareWelcomeInvitation(sessionId);
            state.WelcomeState = existingSquad ? "skipped" : "waiting-refresh";
            state.FirstTimeVisit = true;
            Save(sessionId, state);
            return Response(state);
        }
    }

    public FriendlySquadOnboardingResponse ScreenRefreshed(MongoId sessionId)
    {
        lock (FriendlyTeammateService.CreationLock)
        {
            var state = Load(sessionId);
            if (IsActivePending(state) && state.WelcomeState == "waiting-refresh" && !FriendlyModeRequestGate.HasActiveRaid)
            {
                state.InviteDueAtMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2000;
                state.WelcomeState = "scheduled";
                Save(sessionId, state);
            }
            return Response(state);
        }
    }

    public FriendlySquadOnboardingResponse DeliverWelcomeInvitation(MongoId sessionId)
    {
        lock (FriendlyTeammateService.CreationLock)
        {
            var state = Load(sessionId);
            if (!IsActivePending(state) || state.WelcomeState != "scheduled" || FriendlyModeRequestGate.HasActiveRaid
                || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < state.InviteDueAtMilliseconds)
                return Response(state);

            bool allegiance = state.GameplayMode == "Allegiance";
            // This target-mode receipt commits with the inbox. It survives acceptance and
            // decline, closing the cross-database crash window without resurrecting an invite.
            var receipt = storage.ReadModeDocument<FriendlySquadOnboardingState>(sessionId, DeliveryDocument, allegiance);
            if (receipt?.WelcomeState == "delivered")
            {
                state.WelcomeState = "delivered";
            }
            else if (storage.HasTeammatesInEitherMode(sessionId))
            {
                state.WelcomeState = "skipped";
            }
            else
            {
                var candidate = state.WelcomeCandidate ?? throw new InvalidDataException("Welcome candidate is missing.");
                var requests = storage.ReadModeDocument<List<FriendlyRecruitRequestEntry>>(sessionId, RequestsDocument, allegiance) ?? [];
                if (!requests.Any(entry => entry.ProfileId == candidate.ProfileId)) requests.Add(candidate);
                state.WelcomeState = "delivered";
                storage.WriteModeDocuments(sessionId, allegiance, new Dictionary<string, string>
                {
                    [RequestsDocument] = jsonUtil.Serialize(requests)!,
                    [DeliveryDocument] = jsonUtil.Serialize(new FriendlySquadOnboardingState { WelcomeState = "delivered" })!
                });
            }
            state.WelcomeCandidate = null;
            Save(sessionId, state);
            return Response(state);
        }
    }

    private FriendlySquadOnboardingState Load(MongoId sessionId) =>
        storage.ReadSharedDocument<FriendlySquadOnboardingState>(sessionId, StateDocument) ?? new();
    private void Save(MongoId sessionId, FriendlySquadOnboardingState state) =>
        storage.WriteSharedDocument(sessionId, StateDocument, state);
    private bool IsActivePending(FriendlySquadOnboardingState state) =>
        state.FirstTimeVisit && state.GameplayMode == settingsService.LoadSettings().GameplayMode
        && state.WelcomeState is "waiting-refresh" or "scheduled";
    private FriendlySquadOnboardingResponse Response(FriendlySquadOnboardingState state) => new()
    {
        FirstTimeVisit = state.FirstTimeVisit,
        WelcomePending = IsActivePending(state),
        DelayMilliseconds = (int)Math.Clamp(state.InviteDueAtMilliseconds - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0L, 2000L),
        InvitationDelivered = state.WelcomeState == "delivered"
    };
}
