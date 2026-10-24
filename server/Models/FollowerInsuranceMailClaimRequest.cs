using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public sealed class FollowerInsuranceMailClaimRequest : IRequestData
{
    public List<string> ItemIds { get; set; } = [];
}
