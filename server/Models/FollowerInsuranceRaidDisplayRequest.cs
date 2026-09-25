using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

// Read-only, exact-raid policy snapshot for in-raid item icons.
public record FollowerInsuranceRaidDisplayRequest : IRequestData
{
    public string? InsuranceServerId { get; set; }
}
