namespace pitTeam.Modules
{
    public enum FollowerLootMode
    {
        Normal,
        LootAndGetWeapon,
        LootAndGetGear,
        GetWeapon,
        GetGear
    }

    // Owned by one accepted command, never by the shared configuration.
    public sealed class FollowerLootRequest
    {
        public FollowerLootMode Mode { get; }
        public bool PriorityPending { get; private set; }
        public bool WantsWeapons => Mode == FollowerLootMode.LootAndGetWeapon || Mode == FollowerLootMode.GetWeapon;
        public bool WantsGear => Mode == FollowerLootMode.LootAndGetGear || Mode == FollowerLootMode.GetGear;
        public bool CategoryOnly => PriorityPending || Mode == FollowerLootMode.GetWeapon || Mode == FollowerLootMode.GetGear;
        public bool AllowsWeaponWork => !CategoryOnly || WantsWeapons;
        public bool AllowsGearWork => !CategoryOnly || WantsGear;

        public FollowerLootRequest(FollowerLootMode mode = FollowerLootMode.Normal)
        {
            Mode = mode;
            PriorityPending = mode == FollowerLootMode.LootAndGetWeapon || mode == FollowerLootMode.LootAndGetGear;
        }

        public bool CompletePriority()
        {
            if (!PriorityPending) return false;
            PriorityPending = false;
            return true;
        }

        public bool AllowsCategory(bool weapon, bool gear)
        {
            return !CategoryOnly || (WantsWeapons && weapon) || (WantsGear && gear);
        }
    }
}
