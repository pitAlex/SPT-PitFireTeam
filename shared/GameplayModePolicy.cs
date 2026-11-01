namespace pitTeam.Shared
{
    // Stable storage identities; addon availability is checked separately at selection.
    public static class GameplayModePolicy
    {
        public const string GunsForHire = "GunsForHire";
        public const string Allegiance = "Allegiance";
        public const string GoingRogue = "GoingRogue";
        public static readonly string[] Modes = { GunsForHire, Allegiance, GoingRogue };
        public static bool IsKnown(string mode) => mode == GunsForHire || mode == Allegiance || mode == GoingRogue;
        public static bool UsesAllegianceRules(string mode) => mode == Allegiance || mode == GoingRogue;
        public static string StorageDirectory(string mode)
        {
            switch (mode)
            {
                case GunsForHire: return "teammates";
                case Allegiance: return "teammates-allegiance";
                case GoingRogue: return "teammates-going-rogue";
                default: throw new System.ArgumentException("Unknown gameplay mode", nameof(mode));
            }
        }
    }
}
