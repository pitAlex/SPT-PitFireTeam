namespace pitTeam.Modules
{
    // Native SAIN identity captured before Core conversion changes the bot's settings.
    // These values use the follower aggression anchors; non-anchor native styles
    // use the closest tactical range rather than a personality-independent roll.
    internal static class RecruitCombatAggression
    {
        internal static bool TryMap(string personality, out float aggression)
        {
            switch (personality)
            {
                case "Coward": aggression = 0f; return true;
                case "Timmy": aggression = 20f; return true;
                case "Rat": aggression = 30f; return true;
                case "SnappingTurtle": aggression = 40f; return true;
                case "Normal": aggression = 50f; return true;
                case "Chad": aggression = 70f; return true;
                case "GigaChad":
                case "Wreckless": aggression = 100f; return true;
                default: aggression = 0f; return false;
            }
        }
    }
}
