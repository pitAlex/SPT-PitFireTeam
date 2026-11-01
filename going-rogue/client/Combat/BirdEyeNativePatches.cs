using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Combat;

[HarmonyPatch(typeof(BotFollowerPlayer), nameof(BotFollowerPlayer.Init))]
internal static class BirdEyeFollowerTacticPatch
{
    private static void Prefix(BotFollowerPlayer __instance)
    {
        // Conversion has not entered the registry yet. Normalize before native template
        // capture and command routing, without writing the saved profile's locked tactic.
        if (GameplayModeAddons.Current == GameplayMode.GoingRogue &&
            __instance.GetBoss()?.realPlayer?.IsYourPlayer == true &&
            __instance.GetBot()?.Profile?.Info?.Settings?.Role == WildSpawnType.followerBirdEye)
            __instance.SetCombatTacticFromString("Marksman");
    }
}
