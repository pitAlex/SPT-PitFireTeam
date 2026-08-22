using EFT.UI;
using EFT.UI.Screens;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class AddTeammateBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type targetType = typeof(EftAccountSideSelectionScreen).BaseType;
            return AccessTools.Method(targetType, "BackButtonPressedHandler");
        }

        [PatchPrefix]
        private static bool PatchPrefix(object __instance)
        {
            if (!AddTeammateCreationFlow.IsActiveForController(EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController))
            {
                return true;
            }

            AddTeammateCreationFlow.ReturnToMainScreen();
            return false;
        }
    }

    internal class AddTeammateSideSelectionStateClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SideSelectionState), nameof(SideSelectionState.Close));
        }

        [PatchPrefix]
        private static bool PatchPrefix(SideSelectionState __instance)
        {
            if (!AddTeammateCreationFlow.IsActive)
            {
                return true;
            }

            __instance._stateReady = false;
            __instance._compositeDisposable.Dispose();
            if (__instance._stateCanvasGroup != null)
            {
                __instance._stateCanvasGroup.alpha = 0f;
                __instance._stateCanvasGroup.gameObject.SetActive(false);
            }

            return false;
        }
    }
}
