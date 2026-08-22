using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class MatchMakerSelectionLocationScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.ShowInternal));
        }
        [PatchPostfix]
        private static void PatchPostfix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {
        }
    }
}
