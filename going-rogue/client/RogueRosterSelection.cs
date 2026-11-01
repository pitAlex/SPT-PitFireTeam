using System;
using System.Collections.Generic;

namespace pitTeam.GoingRogue;

internal static class RogueRosterSelection
{
    private static readonly HashSet<string> Roles = new(StringComparer.OrdinalIgnoreCase)
        { "bossKnight", "followerBigPipe", "followerBirdEye", "exUsec" };
    internal static bool IsRole(string role) => role != null && Roles.Contains(role);

    internal static bool IsGoonRole(string role) => IsRole(role) && !string.Equals(role, "exUsec", StringComparison.OrdinalIgnoreCase);

}
