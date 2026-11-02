namespace pitTeam.Shared
{
    // Stable storage identities; addon availability is checked separately at selection.
    public static class GameplayModePolicy
    {
        public const string GunsForHire = "GunsForHire";
        public const string Allegiance = "Allegiance";
        public static string Normalize(string mode) => mode == "GoingRogue" ? Allegiance : mode;
        public static readonly string[] Modes = { GunsForHire, Allegiance };
        public static bool IsKnown(string mode) => mode == GunsForHire || mode == Allegiance;
        public static bool UsesAllegianceRules(string mode) => mode == Allegiance;
        public static string StorageDirectory(string mode)
        {
            switch (mode)
            {
                case GunsForHire: return "teammates";
                case Allegiance: return "teammates-allegiance";
                default: throw new System.ArgumentException("Unknown gameplay mode", nameof(mode));
            }
        }
    }
}
