using HarmonyLib;
using pitTeam.Patches;

namespace pitTeam.GoingRogue.Combat
{
    [HarmonyPatch(typeof(BigPipeBotWeaponSelector), nameof(BigPipeBotWeaponSelector.ManualUpdate))]
    internal static class BigPipePlannedWeaponSelectionPatch
    {
        private static bool Prefix(BigPipeBotWeaponSelector __instance)
            => !BigPipeCombatLayer.IsOwned(FollowerWeaponSwitchPolicyRuntime.GetSelectorBotOwner(__instance));
    }
}
