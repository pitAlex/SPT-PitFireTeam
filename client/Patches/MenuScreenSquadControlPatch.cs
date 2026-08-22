using EFT;
using EFT.UI;
using pitTeam.Components;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class MenuScreenShowSquadControlPatch : ModulePatch
    {
        private static readonly FieldInfo PlayerButtonField = AccessTools.Field(typeof(MenuScreen), "_playerButton");
        private static readonly FieldInfo TradeButtonField = AccessTools.Field(typeof(MenuScreen), "_tradeButton");
        private static readonly FieldInfo HideScreenButtonField = AccessTools.Field(typeof(MenuScreen), "_hideScreenButton");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(MenuScreen),
                "Show",
                new Type[] { typeof(Profile), typeof(EFT.UI.Matchmaker.MatchmakerPlayersController), typeof(ESessionMode) });
        }

        [PatchPostfix]
        private static void PatchPostfix(MenuScreen __instance)
        {
            DefaultUIButton playerButton = PlayerButtonField?.GetValue(__instance) as DefaultUIButton;
            DefaultUIButton tradeButton = TradeButtonField?.GetValue(__instance) as DefaultUIButton;
            DefaultUIButton hideScreenButton = HideScreenButtonField?.GetValue(__instance) as DefaultUIButton;
            if (playerButton == null)
            {
                return;
            }

            SquadControlMenuUi.GetOrCreate(__instance).Initialize(__instance, playerButton, tradeButton, hideScreenButton);
        }
    }

    internal class MenuScreenReconnectVisibilitySquadControlPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.ChangeToReconnectionAvailable));
        }

        [PatchPostfix]
        private static void PatchPostfix(MenuScreen __instance)
        {
            __instance.GetComponent<SquadControlMenuUi>()?.SyncButtonVisibility();
        }
    }

    internal class MenuScreenMinimizedVisibilitySquadControlPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.ChangeScreenInGameStatus));
        }

        [PatchPostfix]
        private static void PatchPostfix(MenuScreen __instance)
        {
            __instance.GetComponent<SquadControlMenuUi>()?.SyncButtonVisibility();
        }
    }
}
