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
        public bool WeaponSelectionInitialized { get; private set; }
        public bool SelectiveWeapons { get; private set; }
        public string SelectedLongGunId { get; private set; }
        public string SelectedPistolId { get; private set; }
        public bool SelectedLongGunCanEquip { get; private set; }

        public void InitializeWeaponSelection(bool pickupWeaponsEnabled, string primaryId, string secondaryId,
            string pistolId, bool shoulderSlotAvailable, bool holsterAvailable)
        {
            if (WeaponSelectionInitialized) return;
            WeaponSelectionInitialized = true;
            SelectiveWeapons = WantsWeapons && !pickupWeaponsEnabled;
            if (!SelectiveWeapons) return;
            SelectedLongGunId = !string.IsNullOrEmpty(primaryId) ? primaryId : secondaryId;
            SelectedPistolId = holsterAvailable ? pistolId : null;
            SelectedLongGunCanEquip = shoulderSlotAvailable;
        }

        public bool AllowsSelectedWeapon(string itemId, bool isGun, bool isWeaponLoot)
        {
            if (!SelectiveWeapons || !isWeaponLoot) return true;
            return isGun && !string.IsNullOrEmpty(itemId) &&
                (itemId == SelectedLongGunId || itemId == SelectedPistolId);
        }

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
