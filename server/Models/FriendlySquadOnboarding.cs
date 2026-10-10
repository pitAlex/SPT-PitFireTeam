using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlySquadOnboardingRequest : IRequestData
{
    [JsonPropertyName("gameplayMode")]
    public string GameplayMode { get; set; } = string.Empty;
}

public record FriendlySquadOnboardingState
{
    [JsonPropertyName("firstTimeVisit")]
    public bool FirstTimeVisit { get; set; }
    public string GameplayMode { get; set; } = string.Empty;
    // waiting-refresh -> scheduled -> delivered; existing squads use skipped.
    public string WelcomeState { get; set; } = string.Empty;
    public long InviteDueAtMilliseconds { get; set; }
    public FriendlyRecruitRequestEntry? WelcomeCandidate { get; set; }
}

public record FriendlySquadOnboardingResponse
{
    [JsonPropertyName("firstTimeVisit")]
    public bool FirstTimeVisit { get; set; }
    [JsonPropertyName("welcomePending")]
    public bool WelcomePending { get; set; }
    [JsonPropertyName("delayMilliseconds")]
    public int DelayMilliseconds { get; set; }
    [JsonPropertyName("invitationDelivered")]
    public bool InvitationDelivered { get; set; }
}
