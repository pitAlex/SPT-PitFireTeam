namespace pitTeam.Server.Models;

public record FriendlyTeammateProfileOptionsResponse
{
    public string CurrentLoadoutId { get; set; } = string.Empty;

    public string CurrentTactic { get; set; } = string.Empty;

    public float Aggression { get; set; } = 50f;

    public FriendlyTeammateProficiencySettings Proficiency { get; set; } = new();

    public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; } = [];

    public List<FriendlyTeammateTacticOption> Tactics { get; set; } = [];

    public List<string> OwnedBodyCustomizationIds { get; set; } = [];

    public List<string> OwnedFeetCustomizationIds { get; set; } = [];

    public FriendlyTeammateProfileRecoveryNotice? RecoveryNotice { get; set; }
}

public record FriendlyTeammateLoadoutOption
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public record FriendlyTeammateTacticOption
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public record FriendlyTeammateProfileRecoveryNotice
{
    public bool Recovered { get; set; }

    public int RemovedItemCount { get; set; }

    public string Message { get; set; } = string.Empty;
}

public record FriendlyTeammateStartupRecoveryNotice
{
    public bool Recovered { get; set; }

    public int RemovedItemCount { get; set; }

    public List<string> TeammateNames { get; set; } = [];

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
