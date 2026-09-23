using HarmonyLib;

namespace pitTeam.SAINAddon
{
    // One owner for all required addon hooks. Partial installation cannot leave patches behind.
    internal static class SAINAddonPatches
    {
        internal const string HarmonyId = "xyz.pit.fireteam.sainaddon";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);
        internal static void Apply()
        {
            try
            {
                SainPlayerSquadBridge.ApplyPatches(Harmony);
                SainSquadDecisionBridge.Apply(Harmony);
                SainCoverSelectionBridge.Apply(Harmony);
                SainMedicalDecisionBridge.Apply(Harmony);
                SainSquadSupportBridge.Apply(Harmony);
                SainContactEnemyBridge.Apply(Harmony);
                SainRegroupFireSafety.Apply(Harmony);
                SainIdleWeaponGuard.Apply(Harmony);
                SainEmergencyWeaponBridge.Apply(Harmony);
            }
            catch { Remove(); throw; }
        }
        internal static void Remove()
        {
            Harmony.UnpatchSelf();
            SainPlayerSquadBridge.Reset();
            SainSquadDecisionBridge.Reset();
            SainCoverSelectionBridge.Reset();
            SainMedicalDecisionBridge.Reset();
            SainEmergencyWeaponBridge.Reset();
            SainSquadSupportBridge.Reset();
        }
    }
}
