using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Trading;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Trading;

// Filter UI views only. Session traders and native profile/standing data stay registered.
internal static class KnightTraderVisibility
{
    private static WeakReference<TradingScreen> screen;
    private static WeakReference<TraderScreensGroup> traderScreen;
    private static GameplayMode displayedMode;
    private static TradingScreen.ETradingScreenTab displayedTab;

    internal static bool IsVisible(Trader trader, GameplayMode mode) =>
        mode == GameplayMode.GoingRogue || trader == null ||
        !string.Equals(trader.Id, pitTeam.Utils.Props.KnightTrader, StringComparison.Ordinal);

    internal static IEnumerable<Trader> Filter(IEnumerable<Trader> traders, GameplayMode mode) =>
        traders == null || mode == GameplayMode.GoingRogue ? traders : traders.Where(trader => IsVisible(trader, mode)).ToArray();

    internal static void Opened(TradingScreen value, TradingScreen.ETradingScreenTab tab)
    {
        screen = new WeakReference<TradingScreen>(value);
        displayedMode = GameplayModeAddons.Current;
        displayedTab = tab;
    }

    internal static void TraderOpened(TraderScreensGroup value) => traderScreen = new WeakReference<TraderScreensGroup>(value);

    internal static void RefreshMode()
    {
        var mode = GameplayModeAddons.Current;
        if (displayedMode == mode) return;
        displayedMode = mode;
        try
        {
            if (traderScreen != null && traderScreen.TryGetTarget(out var detail) &&
                detail != null && detail.gameObject.activeInHierarchy)
            {
                new TradingScreen.TradersScreenController(detail.Session, detail.HealthController, detail.InventoryController,
                    detail.QuestController, detail.AchievementsController).ShowScreen(EScreenState.Queued);
                return;
            }
            if (screen != null && screen.TryGetTarget(out var current) && current != null &&
                current.gameObject.activeInHierarchy)
                current.ShowScreen(displayedTab);
        }
        catch (Exception ex) { Logger.LogError($"[GoingRogue] Could not refresh trader visibility after mode change: {ex}"); }
    }
}

[HarmonyPatch(typeof(MerchantsList), nameof(MerchantsList.Show))]
internal static class KnightMerchantListVisibilityPatch
{
    private static void Prefix(ref IEnumerable<Trader> __0, ref Trader ____currentTrader)
    {
        var mode = GameplayModeAddons.Current;
        __0 = KnightTraderVisibility.Filter(__0, mode);
        // Native list rendering otherwise tries to reopen the remembered trader and
        // skips every card when that trader is absent from the filtered list.
        if (!KnightTraderVisibility.IsVisible(____currentTrader, mode)) ____currentTrader = null;
    }
}

[HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.TradersList), MethodType.Getter)]
internal static class KnightTraderStripVisibilityPatch
{
    private static void Postfix(ref IEnumerable<Trader> __result) =>
        __result = KnightTraderVisibility.Filter(__result, GameplayModeAddons.Current);
}

[HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.SelectTrader))]
internal static class KnightTraderSelectionVisibilityPatch
{
    private static bool Prefix(TraderScreensGroup __instance, ref Trader __0)
    {
        if (KnightTraderVisibility.IsVisible(__0, GameplayModeAddons.Current)) return true;
        __0 = __instance.TradersList.FirstOrDefault(trader => trader?.Info?.Available == true);
        return __0 != null;
    }
}

[HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.Show))]
internal static class KnightTraderScreenVisibilityPatch
{
    private static void Postfix(TraderScreensGroup __instance) => KnightTraderVisibility.TraderOpened(__instance);
}

[HarmonyPatch(typeof(TradingScreen), nameof(TradingScreen.Show), new[] { typeof(TradingScreen.TradingScreenController) })]
internal static class KnightTradingScreenVisibilityPatch
{
    private static void Postfix(TradingScreen __instance, TradingScreen.TradingScreenController __0) =>
        KnightTraderVisibility.Opened(__instance, __0.Tab);
}
