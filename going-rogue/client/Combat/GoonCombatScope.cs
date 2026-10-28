using System;

namespace pitTeam.GoingRogue.Combat
{
    internal static class GoonCombatScope
    {
        internal static bool Matches(string expectedRole, string expectedBrain, string mode, string role, string brain,
            bool registeredFollower, bool livingActiveBot, bool humanLeader) =>
            string.Equals(mode, "GoingRogue", StringComparison.Ordinal) &&
            string.Equals(role, expectedRole, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(brain, expectedBrain, StringComparison.Ordinal) &&
            registeredFollower && livingActiveBot && humanLeader;
    }
}
