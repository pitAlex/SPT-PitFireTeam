namespace pitTeam.Server.Models;

public record FriendlyTeammateSettings
{
    public string SelectedLoadoutId { get; set; } = string.Empty;
    public bool AutoJoinEnabled { get; set; }
    public float Aggression { get; set; } = 50f;
    public string CombatTactic { get; set; } = "Rifleman";
    public List<string> OwnedBodyCustomizationIds { get; set; } = [];
    public List<string> OwnedFeetCustomizationIds { get; set; } = [];
}
