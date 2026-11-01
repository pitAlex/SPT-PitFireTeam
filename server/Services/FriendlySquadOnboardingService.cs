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
    private const string GoingRogue = pitTeam.Shared.GameplayModePolicy.GoingRogue;

    public FriendlySquadOnboardingResponse GetStatus(MongoId sessionId)
    {
        lock (FriendlyTeammateService.CreationLock)
        {
            var state = Load(sessionId);
            if (state.FirstTimeVisit && !state.GoingRogueChoiceCompleted && FriendlyGameplayModeAddons.IsAvailable(GoingRogue))
            {
                string current = settingsService.LoadSettings().GameplayMode;
                // Profiles already using the addon have made this choice. Preserve
                // a pending offer during a lost-completion-response recovery.
                if (current == GoingRogue && state.AddonChoiceFromMode == null)
                {
                    state.GoingRogueChoiceCompleted = true;
                    Save(sessionId, state);
                }
                else if (current != GoingRogue && state.AddonChoiceFromMode != current)
                {
                    state.AddonChoiceFromMode = current;
                    Save(sessionId, state);
                }
            }
            return Response(state);
        }
    }

    public FriendlySquadOnboardingResponse Complete(MongoId sessionId, string mode, IReadOnlyCollection<string>? shownModes = null)
    {
        lock (FriendlyTeammateService.CreationLock)
        {
            var state = Load(sessionId);
            if (state.FirstTimeVisit)
            {
                if (state.GoingRogueChoiceCompleted || state.AddonChoiceFromMode == null || shownModes == null) return Response(state);
                if (!FriendlyGameplayModeAddons.IsAvailable(GoingRogue) || !FriendlyGameplayModeAddons.IsAvailable(mode) ||
                    (mode != state.AddonChoiceFromMode && mode != GoingRogue) ||
                    mode != settingsService.LoadSettings().GameplayMode || FriendlyModeRequestGate.HasActiveRaid ||
                    shownModes?.Contains(GoingRogue) != true)
                    throw new InvalidOperationException("The addon gameplay choice is not ready.");
                state.GoingRogueChoiceCompleted = true;
                state.AddonChoiceFromMode = null;
                Save(sessionId, state);
                return Response(state);
            }
            if (!FriendlyGameplayModeAddons.IsAvailable(mode) || mode != settingsService.LoadSettings().GameplayMode
                || FriendlyModeRequestGate.HasActiveRaid)
                throw new InvalidOperationException("The selected gameplay mode is not ready for onboarding.");

            if (FriendlyGameplayModeAddons.Companions(mode).Any(companion => companion.SeedForTesting))
                teammateService.ListTeammates(sessionId); // Prepare addon companions before the welcome decision.
            bool existingSquad = storage.HasTeammatesInEitherMode(sessionId);
            // Generate once, then durably record the exact profile before exposing completion.
            state.GameplayMode = mode;
            state.WelcomeCandidate = existingSquad ? null : teammateService.PrepareWelcomeInvitation(sessionId);
            state.WelcomeState = existingSquad ? "skipped" : "waiting-refresh";
            state.FirstTimeVisit = true;
            // Only acknowledge the addon if both sides could actually offer it.
            state.GoingRogueChoiceCompleted = FriendlyGameplayModeAddons.IsAvailable(GoingRogue) &&
                (mode == GoingRogue || shownModes?.Contains(GoingRogue) == true);
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

            string mode = state.GameplayMode;
            // This target-mode receipt commits with the inbox. It survives acceptance and
            // decline, closing the cross-database crash window without resurrecting an invite.
            var receipt = storage.ReadModeDocument<FriendlySquadOnboardingState>(sessionId, DeliveryDocument, mode);
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
                var requests = storage.ReadModeDocument<List<FriendlyRecruitRequestEntry>>(sessionId, RequestsDocument, mode) ?? [];
                if (!requests.Any(entry => entry.ProfileId == candidate.ProfileId)) requests.Add(candidate);
                state.WelcomeState = "delivered";
                storage.WriteModeDocuments(sessionId, mode, new Dictionary<string, string>
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
        InvitationDelivered = state.WelcomeState == "delivered",
        ModeChoices = !state.FirstTimeVisit
            ? pitTeam.Shared.GameplayModePolicy.Modes.Where(FriendlyGameplayModeAddons.IsAvailable).ToArray()
            : HasAddonChoice(state) ? new[] { state.AddonChoiceFromMode!, GoingRogue } : [],
        AddonChoiceRequired = HasAddonChoice(state)
    };
    private static bool HasAddonChoice(FriendlySquadOnboardingState state) => state.FirstTimeVisit &&
        !state.GoingRogueChoiceCompleted && state.AddonChoiceFromMode != null && FriendlyGameplayModeAddons.IsAvailable(GoingRogue);
}
