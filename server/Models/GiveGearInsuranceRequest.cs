using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record GiveGearInsuranceRequest : IRequestData
{
    public string? InsuranceServerId { get; set; }
    public string? InsuranceReportId { get; set; }
    public List<string> ItemIds { get; set; } = [];
}
