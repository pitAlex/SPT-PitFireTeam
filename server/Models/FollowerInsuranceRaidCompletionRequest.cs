using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FollowerInsuranceRaidCompletionRequest : IRequestData
{
    public string? InsuranceServerId { get; set; }
    public List<string>? ReportIds { get; set; }
    public bool Failed { get; set; }
}
