using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using pitTeam.GoingRogue.Trading;
using pitTeam.Modules;

internal static class VisibilityChecks
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static void Main()
    {
        var harmony = new Harmony("pitTeam.fixture.knight.visibility");
        harmony.PatchAll(typeof(KnightTraderVisibility).Assembly);
        var knight = new Trader { Id = pitTeam.Utils.Props.KnightTrader, Info = new TraderInfo { Available = false } };
        var ordinary = new Trader { Id = "prapor" };
        var locked = new Trader { Id = "another-custom-trader", Info = new TraderInfo { Available = false } };
        var registered = new List<Trader> { locked, knight, ordinary };
        var list = new MerchantsList();
        var details = new TraderScreensGroup { Registered = registered };
        foreach (GameplayMode mode in Enum.GetValues(typeof(GameplayMode)))
        {
            GameplayModeAddons.Current = mode;
            list.Show(registered);
            Check(list.Displayed.Contains(knight) == (mode == GameplayMode.GoingRogue), "merchant visibility follows mode: " + mode);
            Check(details.TradersList.Contains(knight) == (mode == GameplayMode.GoingRogue), "navigation strip follows mode: " + mode);
            Check(list.Displayed.Contains(ordinary) && list.Displayed.Contains(locked), "other traders keep native visibility: " + mode);
            Check(registered.Count == 3 && registered[1] == knight && !knight.Info.Available, "registered trader and lock remain untouched: " + mode);
        }
        GameplayModeAddons.Current = GameplayMode.Allegiance;
        list.Remember(knight); list.Show(registered);
        Check(list.Remembered == null, "remembered hidden Knight cannot suppress rendering of all other cards");
        list.Remember(ordinary); list.Show(registered);
        Check(list.Remembered == ordinary, "ordinary remembered trader is preserved");
        details.SelectTrader(knight);
        Check(details.Selected == ordinary, "hidden selection falls back to an available visible trader");
        details.SelectTrader(locked);
        Check(details.Selected == locked, "other trader selections retain native behavior");
        Check(KnightTraderVisibility.Filter(null, GameplayMode.Allegiance) == null, "null list retains native handling");
        Check(ReferenceEquals(KnightTraderVisibility.Filter(registered, GameplayMode.GoingRogue), registered), "Going Rogue retains the native list reference");

        var screen = new TradingScreen();
        screen.Show(new TradingScreen.TradingScreenController { Tab = TradingScreen.ETradingScreenTab.FleaMarket });
        GameplayModeAddons.Current = GameplayMode.GoingRogue;
        KnightTraderVisibility.RefreshMode();
        Check(screen.Refreshes == 1 && screen.LastTab == TradingScreen.ETradingScreenTab.FleaMarket, "open flea tab survives mode refresh");
        KnightTraderVisibility.RefreshMode();
        Check(screen.Refreshes == 1, "unchanged mode does not rebuild the UI");
        screen.gameObject.activeInHierarchy = false;
        GameplayModeAddons.Current = GameplayMode.GunsForHire;
        KnightTraderVisibility.RefreshMode();
        Check(screen.Refreshes == 1, "closed screen is not reopened");

        GameplayModeAddons.Current = GameplayMode.GoingRogue;
        KnightTraderVisibility.RefreshMode();
        details.gameObject.activeInHierarchy = true;
        details.Show();
        GameplayModeAddons.Current = GameplayMode.Allegiance;
        KnightTraderVisibility.RefreshMode();
        Check(TradingScreen.TradersScreenController.Opened == 1, "open trader details refresh through the native traders controller");
        Check(registered.Count == 3 && registered.Contains(knight), "detail refresh never unregisters Knight");
        details.gameObject.activeInHierarchy = false;
        screen.gameObject.activeInHierarchy = true;
        screen.ThrowOnRefresh = true;
        GameplayModeAddons.Current = GameplayMode.GoingRogue;
        KnightTraderVisibility.RefreshMode();
        Check(Logger.Errors.Count == 1, "UI transition failure is contained and diagnosed");
        KnightTraderVisibility.RefreshMode();
        Check(Logger.Errors.Count == 1, "failed refresh does not create a polling error loop");
        harmony.UnpatchSelf();
        GameplayModeAddons.Current = GameplayMode.Allegiance;
        list.Show(registered);
        Check(list.Displayed.Contains(knight), "removing the addon restores the untouched native trader list");
        Console.WriteLine("PASS: " + checks + " production Knight trader visibility/Harmony/mode-refresh checks; native UI rendering remains a game check.");
    }
}
namespace pitTeam.Modules
{
    public enum GameplayMode { GunsForHire, Allegiance, GoingRogue }
    public static class GameplayModeAddons { public static GameplayMode Current; }
    public static class Logger { public static List<string> Errors = new List<string>(); public static void LogError(string text) => Errors.Add(text); }
}
namespace pitTeam.Utils { public static class Props { public const string KnightTrader = "67768b19fa281ca31708b187"; } }
namespace EFT.Trading
{
    public class Trader { public string Id; public TraderInfo Info = new TraderInfo(); }
    public class TraderInfo { public bool Available = true; }
}
namespace EFT.UI.Screens { public enum EScreenState { Queued } }
namespace EFT.UI
{
    public class GameObject { public bool activeInHierarchy; }
    public class MerchantsList
    {
        private Trader _currentTrader;
        public Trader Remembered => _currentTrader;
        public void Remember(Trader trader) { _currentTrader = trader; }
        public List<Trader> Displayed;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Show(IEnumerable<Trader> tradersList) => Displayed = tradersList.ToList();
    }
    public class TraderScreensGroup
    {
        public GameObject gameObject = new GameObject();
        public IEnumerable<Trader> Registered;
        public Trader Selected;
        public object Session, HealthController, InventoryController, QuestController, AchievementsController;
        public IEnumerable<Trader> TradersList { [MethodImpl(MethodImplOptions.NoInlining)] get { return Registered; } }
        [MethodImpl(MethodImplOptions.NoInlining)] public void SelectTrader(Trader nextSelected) { Selected = nextSelected; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Show() { }
    }
    public class TradingScreen
    {
        public enum ETradingScreenTab { Merchants, FleaMarket }
        public GameObject gameObject = new GameObject();
        public int Refreshes;
        public bool ThrowOnRefresh;
        public ETradingScreenTab LastTab;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Show(TradingScreenController controller) { gameObject.activeInHierarchy = true; }
        public void ShowScreen(ETradingScreenTab tab) { if (ThrowOnRefresh) throw new Exception("fixture transition failure"); Refreshes++; LastTab = tab; }
        public class TradingScreenController { public ETradingScreenTab Tab; }
        public class TradersScreenController
        {
            public static int Opened;
            public TradersScreenController(object session, object health, object inventory, object quests, object achievements) { }
            public void ShowScreen(Screens.EScreenState state) { Opened++; }
        }
    }
}
