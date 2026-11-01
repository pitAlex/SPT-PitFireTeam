namespace pitTeam.Server.Models;

// Deliberately no effect, resource or maximum-health update fields in the raid-end contract.
public record FriendlyTeammateRaidHealth
{
    public Dictionary<string, FriendlyTeammateRaidBodyPartHealth>? BodyParts { get; set; }
}

public record FriendlyTeammateRaidBodyPartHealth
{
    public double Current { get; set; }
    public double SpawnMaximum { get; set; }
}
