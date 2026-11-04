using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.GoingRogue;

// Shared client/server rules. Only native Success counts as completed.
internal static class RogueAlliancePolicy
{
    internal static readonly string[] Quests = {
        "6775d9957e2dbcb3bd0a02c7", "6775da597e2dbcb3bd0a02c9", "677687f2fa281ca31708b17a",
        "67768936fa281ca31708b17c", "6776899afa281ca31708b17e", "677689ebfa281ca31708b180", "67768a41fa281ca31708b182"
    };
    internal static int Completed(Func<string, bool> success)
    {
        int count = 0;
        foreach (string quest in Quests) if (success(quest)) count++;
        return count;
    }
    // Callers supply completion objectives only, never failure/prerequisite conditions.
    internal static bool HostileKill(string conditionType, IEnumerable<string> roles) =>
        string.Equals(conditionType, "Kills", StringComparison.OrdinalIgnoreCase) &&
        roles?.Any(IsAlly) == true;
    internal static bool IsGoon(string role) => string.Equals(role, "bossKnight", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "followerBigPipe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "followerBirdEye", StringComparison.OrdinalIgnoreCase);
    internal static bool IsExUsec(string role) => string.Equals(role, "exUsec", StringComparison.OrdinalIgnoreCase);
    internal static bool IsAlly(string role) => IsGoon(role) || IsExUsec(role);
    internal static float Acceptance(int completed, double standing, bool goonEscort)
    {
        if (completed < Quests.Length || double.IsNaN(standing) || standing < RequiredStanding("exUsec")) return 0f;
        if (goonEscort) return 1f;
        return (float)(0.5d + 0.2d * Math.Min(1d, standing - 1d));
    }
    internal static double RequiredStanding(string role) => IsExUsec(role) ? 1d :
        IsGoon(role) && !string.Equals(role, "bossKnight", StringComparison.OrdinalIgnoreCase) ? 0.5d : 0d;
    internal static bool CanDeploy(string role, double standing, bool conflict) => !IsAlly(role) ||
        !conflict && !double.IsNaN(standing) && standing >= RequiredStanding(role);
    internal static double Penalty(string role) => IsExUsec(role) ? 0.025d : IsGoon(role) ? 0.1d : 0d;
    internal static double AfterPenalty(double standing, string role) => Math.Max(0.1d, standing - Penalty(role));
}
