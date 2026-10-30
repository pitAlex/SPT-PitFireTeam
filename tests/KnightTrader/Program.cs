using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Image;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

internal static class Program
{
    private static int checks;
    public static int Main(string[] args)
    {
        string runtime = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(runtime, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, name.Name + ".dll")) : null;
        return Run(runtime);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static T Construct<T>(Dictionary<string, object?> dependencies) => (T)typeof(T).GetConstructors().Single()
        .Invoke(typeof(T).GetConstructors().Single().GetParameters().Select(p => dependencies.GetValueOrDefault(p.Name!)).ToArray());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string runtime)
    {
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates,
            json.Deserialize<Dictionary<MongoId, TemplateItem>>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/database/templates/items.json"))));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Profiles))!.SetValue(templates,
            json.Deserialize<Dictionary<string, ProfileSides>>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/database/templates/profiles.json"))));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Handbook))!.SetValue(templates,
            json.Deserialize<HandbookBase>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/database/templates/handbook.json"))));
        var globals = json.Deserialize<GlobalTable>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/database/globals.json")))!;
        var catalog = KnightTraderCatalog.Create(json, templates);
        Check(catalog.Base.Id == "67768b19fa281ca31708b187" && catalog.Base.Currency == CurrencyType.USD, "legacy identity and USD currency retained");
        Check(catalog.Base.UnlockedByDefault == false && catalog.Base.IsAvailableInPVE, "modern PVE visibility and actual default lock");
        Check(catalog.Base.LoyaltyLevels!.Select(t => (t.MinLevel, t.MinStanding, t.MinSalesSum))
            .SequenceEqual(new[] { ((int?)20, (double?)0.3, (long?)0), (20, 0.5, 0), (20, 1.5, 7000) }), "legacy three loyalty tiers retained");
        Check(catalog.Base.LoyaltyLevels!.All(t => t.BuyPriceCoefficient == 55), "legacy buy coefficient retained");
        Check(catalog.Base.Insurance!.Availability == false && catalog.Base.Repair!.Availability == false && catalog.Base.Medic == false, "no invented services");
        Check(catalog.Assort.Items.Count == 274 && catalog.Assort.Items.Count(i => i.ParentId == "hideout") == 48, "all 32 stock and 16 weapon offers loaded");
        Check(catalog.QuestAssort.Values.All(offers => offers.Count == 0) && catalog.Assort.LoyalLevelItems.Count == 48,
            "absent legacy quest rewards do not create orphan assort/loyalty references");
        Check(catalog.Assort.Items.All(i => templates.Items.ContainsKey(i.Template)), "all stock and weapon attachment templates resolve in installed SPT");
        var copy = KnightTraderCatalog.Create(json, templates);
        catalog.Assort.Items[0].Upd!.StackObjectsCount = 0;
        Check(copy.Assort.Items[0].Upd!.StackObjectsCount > 0, "catalog creates independent stock instances");
        copy.Assort.Items.Add(copy.Assort.Items[0]);
        try { KnightTraderCatalog.Validate(copy, templates); throw new Exception("Duplicate stock accepted"); }
        catch (InvalidDataException) { checks++; }

        var session = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
        var other = new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb");
        var knight = new TraderInfo { Standing = 0.5, SalesSum = 4567, Disabled = false, LoyaltyLevel = 1 };
        var ordinary = new TraderInfo { Standing = 0.77, SalesSum = 123, Unlocked = true, Disabled = false };
        var profile = new PmcData { Id = session, Info = new() { Level = 19, Experience = 0, Side = "Usec" },
            TradersInfo = new() { [KnightTraderCatalog.Id] = knight, [other] = ordinary } };
        foreach (string mode in new[] { "GoingRogue", "Allegiance", "GunsForHire" })
        foreach (int level in new[] { 1, 19, 20, 21, 79 })
        {
            profile.Info.Level = level;
            KnightTraderAccess.Apply(profile, mode);
            Check(knight.Unlocked == (mode == "GoingRogue" && level >= 20), "mode/level access boundary: " + mode + "/" + level);
            Check(knight.Standing == 0.5 && knight.SalesSum == 4567 && ordinary.Unlocked == true && ordinary.Standing == 0.77,
                "access synchronization preserves standing, sales and unrelated traders");
        }
        knight.Disabled = true; KnightTraderAccess.Apply(profile, "GoingRogue");
        Check(knight.Disabled == true, "access lock does not erase native disabled status");
        knight.Disabled = false;

        string previousDirectory = Environment.CurrentDirectory;
        string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "knight-trader", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work);
        Environment.CurrentDirectory = work;
        try
        {
            string mod = Path.Combine(AppContext.BaseDirectory, "user/mods/pitFireTeam-ServerMod/Resources/lang");
            Directory.CreateDirectory(mod);
            foreach (string locale in new[] { "en", "ru", "chs" })
                File.Copy(Path.Combine(previousDirectory, "server/Resources/lang/" + locale + ".json"), Path.Combine(mod, locale + ".json"), true);
            var settings = Construct<FriendlyServerSettingsService>(new() { ["logger"] = DispatchProxy.Create<ISptLogger<FriendlyServerSettingsService>, TestLogger>() });
            void Mode(string value) => settings.SaveAndApply(new() { GameplayMode = value });
            Mode("GoingRogue");
            var save = (SaveServer)RuntimeHelpers.GetUninitializedObject(typeof(SaveServer));
            var full = new SptProfile { ProfileInfo = new() { Edition = "Standard" }, CharacterData = new() { PmcData = profile, ScavData = new() { Id = other } } };
            typeof(SaveServer).GetField("profiles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(save,
                new ConcurrentDictionary<MongoId, SptProfile>([new(session, full)]));
            var profiles = Construct<ProfileHelper>(new() { ["saveServer"] = save, ["templateTable"] = templates, ["globalTable"] = globals, ["cloner"] = new FastCloner() });
            var tradersTable = new TradersTable();
            var traders = Construct<TraderHelper>(new() { ["globalTable"] = globals, ["traderTable"] = tradersTable, ["profileHelper"] = profiles });
            var language = new FriendlyLanguageService(DispatchProxy.Create<ISptLogger<FriendlyLanguageService>, TestLogger>());
            var response = Construct<HttpResponseUtil>(new() { ["jsonUtil"] = json });
            var access = new KnightTraderAccess(settings, profiles, traders, language, response);
            var router = new ImageRouterService();
            var localeTable = new LocaleTable { Global = new() {
                ["en"] = new(() => new GlobalLocaleDictionary()), ["ru"] = new(() => new GlobalLocaleDictionary()),
                ["fr"] = new(() => new GlobalLocaleDictionary()) }, Menu = [], Languages = [] };
            var traderConfig = new TraderConfig { Fence = null! };
            var ragfair = Construct<RagfairConfig>(new()); ragfair.Traders = [];
            var module = new KnightTraderModule(tradersTable, templates, localeTable, json,
                new ImageRouter(new FileUtil(), router, null!), traderConfig, ragfair, language, access,
                DispatchProxy.Create<ISptLogger<KnightTraderModule>, TestLogger>());
            module.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Check(tradersTable.Count == 1 && tradersTable[KnightTraderCatalog.Id].Assort.Items.Count == 274, "real addon registration publishes complete stock");
            Check(traderConfig.UpdateTime.Single().Seconds.Min == 3600 && traderConfig.UpdateTime.Single().Seconds.Max == 7200, "legacy one/two-hour restock interval");
            Check(!ragfair.Traders[KnightTraderCatalog.Id], "Knight stock stays off the flea market");
            Check(File.Exists(router.GetByKey("/files/trader/avatar/knight")), "real native image route resolves deployed avatar");
            Check(localeTable.Global["en"].Value![KnightTraderCatalog.IdValue + " Nickname"] == "Knight" &&
                localeTable.Global["ru"].Value![KnightTraderCatalog.IdValue + " Nickname"] == "Найт" &&
                localeTable.Global["fr"].Value![KnightTraderCatalog.IdValue + " Nickname"] == "Knight", "native trader identity uses central localization and English fallback");
            try { module.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("ID collision accepted"); }
            catch (InvalidDataException) { checks++; }
            void Level(int value)
            {
                profile.Info.Experience = globals.Configuration.Exp.Level.ExperienceTable.Take(value).Sum(x => x.Experience);
                traders.LevelUp(KnightTraderCatalog.Id, profile);
                Check(profile.Info.Level == value, "fixture uses real native XP-to-level calculation: " + value);
            }
            Level(19);
            Check(knight.Unlocked == false, "installed native LevelUp hook locks level 19");
            var cloned = profiles.GetCompleteProfile(session);
            Check(cloned[0].TradersInfo![KnightTraderCatalog.Id].Unlocked == false && cloned[0].TradersInfo![KnightTraderCatalog.Id].LoyaltyLevel == null,
                "real profile projection retains native sanitation and publishes the access lock");
            var nativeAssort = (TraderAssortHelper)RuntimeHelpers.GetUninitializedObject(typeof(TraderAssortHelper));
            Check(nativeAssort.GetAssort(session, KnightTraderCatalog.Id).Items.Count == 0, "actual assort prefix refuses level-19 stock before native providers");
            Check(nativeAssort.GetAssort(session, KnightTraderCatalog.Id, true).Items.Count == 0, "showLockedAssorts cannot bypass trader lock");
            var trade = (TradeHelper)RuntimeHelpers.GetUninitializedObject(typeof(TradeHelper));
            var output = new ItemEventRouterResponse();
            trade.BuyItem(profile, new() { TransactionId = KnightTraderCatalog.Id }, session, false, output);
            Check(output.Warnings?.Count == 1 && output.Warnings[0].Code == BackendErrorCodes.UnknownTradingError, "real buy hook rejects locked trade without inventory providers");
            Check(output.Warnings![0].ErrorMessage == "Knight is only available in Going Rogue from level 20.", "locked trade error uses central localized text");
            output = new();
            trade.SellItem(profile, profile, new() { TransactionId = KnightTraderCatalog.Id }, session, output);
            Check(output.Warnings?.Count == 1 && knight.SalesSum == 4567, "real sell hook rejects locked trade before item/currency changes");
            Check(access.Admit(other, session, new()), "other traders bypass addon admission entirely");
            Level(20);
            Check(knight.Unlocked == true && access.CanTrade(session), "level 20 unlocks without granting standing/sales");
            Check(access.Admit(KnightTraderCatalog.Id, session, new()), "unlocked trade delegates to native execution");
            Check(profiles.GetCompleteProfile(session)[0].TradersInfo![KnightTraderCatalog.Id].Unlocked == true, "profile refresh publishes unlock");
            foreach (var (standing, sales, tier) in new[] { (0.3, 0d, 1), (0.5, 0d, 2), (1.49, 7000d, 2), (1.5, 6999d, 2), (1.5, 7000d, 3) })
            {
                knight.Standing = standing; knight.SalesSum = sales;
                traders.LevelUp(KnightTraderCatalog.Id, profile);
                Check(knight.LoyaltyLevel == tier, "native loyalty tier boundary: " + standing + "/" + sales);
            }
            Mode("Allegiance");
            Check(!access.CanTrade(session) && knight.Unlocked == false, "switching away from Going Rogue relocks access");
            Check(nativeAssort.GetAssort(session, KnightTraderCatalog.Id).Items.Count == 0, "other modes cannot query stock with a stale unlocked UI");
            Mode("GoingRogue");
            Check(access.CanTrade(session) && knight.Standing == 1.5 && knight.SalesSum == 7000, "mode return retains native progression");
            knight.Disabled = true;
            Check(!access.CanTrade(session), "native disabled status still refuses trade above level 20");
            profile.TradersInfo!.Remove(KnightTraderCatalog.Id);
            access.Synchronize(session);
            var initialized = profile.TradersInfo[KnightTraderCatalog.Id];
            Check(initialized.Unlocked == true && initialized.SalesSum == 0 && initialized.Standing == 0,
                "actual native ResetTrader initializes absent Knight without invented standing or trade totals");
            QuestChecks.Run(json, templates, globals, localeTable, router, profiles, settings, access, profile, session, Check, Mode);
        }
        finally
        {
            new Harmony("xyz.pit.fireteam.goingrogue.knight-trader").UnpatchSelf();
            Environment.CurrentDirectory = previousDirectory;
        }
        Console.WriteLine($"PASS: {checks} Knight trader/quest catalog, access and native-hook checks. Synthetic profiles only; no live raid/trade/UI simulation.");
        return 0;
    }
}

public class TestLogger : DispatchProxy { protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.ReturnType == typeof(bool) ? false : null; }
