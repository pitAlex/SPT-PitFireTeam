namespace pitTeam.Server.Models;

public record FriendlyTeammateDeletionJournal
{
    public int Aid { get; set; }
    public string State { get; set; } = "pending";
    public int Price { get; set; }
    public string MoneyBefore { get; set; } = string.Empty;
    public string MoneyAfter { get; set; } = string.Empty;
}
