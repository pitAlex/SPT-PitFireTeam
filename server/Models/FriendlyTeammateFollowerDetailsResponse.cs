namespace pitTeam.Server.Models;

public record FriendlyTeammateFollowerDetailsResponse
{
    public string Aid { get; set; } = string.Empty;

    public string Tactic { get; set; } = string.Empty;

    public float Aggression { get; set; }

    public FriendlyTeammateProficiencySettings Proficiency { get; set; } = new();

    public string Equipment { get; set; } = string.Empty;

    public string Voice { get; set; } = string.Empty;

    public string Head { get; set; } = string.Empty;
}
