namespace pitTeam.Server.Models;

public record FriendlyTeammateSettings
{
    public string SelectedLoadoutId { get; set; } = string.Empty;
    public bool AutoJoinEnabled { get; set; }
    public float Aggression { get; set; } = 50f;
    public FriendlyTeammateProficiencySettings Proficiency { get; set; } = new();
    public string CombatTactic { get; set; } = "Rifleman";
    public List<string> OwnedBodyCustomizationIds { get; set; } = [];
    public List<string> OwnedFeetCustomizationIds { get; set; } = [];
}

public record FriendlyTeammateProficiencySettings
{
    public float VisionDistance { get; set; } = 100f;
    public float VisionSpeed { get; set; } = 100f;
    public float AimSpeed { get; set; } = 100f;
    public float Accuracy { get; set; } = 100f;
}
