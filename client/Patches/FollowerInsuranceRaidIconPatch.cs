using System;
using System.Reflection;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // Only the icon/border: stock InsuranceCompany must continue to own actual player policies.
    internal sealed class FollowerInsuranceRaidIconPatch : ModulePatch
    {
        private static bool warningLogged;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(GridItemView), nameof(GridItemView.EnableInsuranceIcons), new[] { typeof(bool) });

        [PatchPostfix]
        private static void PatchPostfix(GridItemView __instance)
        {
            try
            {
                if (!FollowerInsuranceRaidDisplay.IsInsured(__instance?.Item?.Id)) return;
                __instance.InsuredItemBorder?.SetActive(true);
                __instance.InsuredIcon?.SetActive(true);
            }
            catch (Exception ex)
            {
                if (warningLogged) return;
                warningLogged = true;
                pitFireTeam.Log.LogWarning($"[FollowerInsurance:RaidIcon] Unable to display follower shield: {ex}");
            }
        }
    }
}
