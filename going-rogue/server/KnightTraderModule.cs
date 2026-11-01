using HarmonyLib;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace pitTeam.GoingRogue.Server;

[Injectable(TypePriority = OnLoadOrder.TraderRegistration)]
public sealed class KnightTraderModule(
    TradersTable traders, TemplateTable templates, LocaleTable locales, JsonUtil json,
    ImageRouter images, TraderConfig traderConfig, RagfairConfig ragfairConfig,
    FriendlyLanguageService language, KnightTraderAccess access, ISptLogger<KnightTraderModule> logger) : IOnLoad
{
    private static KnightTraderAccess? activeAccess;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (traders.ContainsKey(KnightTraderCatalog.Id)) throw new InvalidDataException("Knight trader ID is already registered.");
        var trader = KnightTraderCatalog.Create(json, templates);
        string avatar = Path.Combine(Path.GetDirectoryName(typeof(KnightTraderModule).Assembly.Location)!, "Resources", "avatars", "knight.jpg");
        if (!File.Exists(avatar)) throw new FileNotFoundException("Knight trader avatar is missing.", avatar);
        Install(access);
        traders.Add(KnightTraderCatalog.Id, trader);
        traderConfig.UpdateTime.Add(new() { Name = "Knight", TraderId = KnightTraderCatalog.Id, Seconds = new() { Min = 3600, Max = 7200 } });
        ragfairConfig.Traders[KnightTraderCatalog.Id] = false;
        images.AddRoute(Path.ChangeExtension(KnightTraderCatalog.AvatarPath, null), avatar);
        foreach (var (locale, global) in locales.Global)
        {
            var text = language.GetEditorText(locale);
            global.AddTransformer(values =>
            {
                if (values == null) return values;
                string Text(string key) => text.GetValueOrDefault(key) ?? key;
                foreach (string field in new[] { "Nickname", "FirstName", "FullName" })
                    values[$"{KnightTraderCatalog.IdValue} {field}"] = Text("GoingRogueKnightTraderName");
                values[$"{KnightTraderCatalog.IdValue} Location"] = Text("GoingRogueKnightTraderLocation");
                values[$"{KnightTraderCatalog.IdValue} Description"] = Text("GoingRogueKnightTraderDescription");
                return values;
            });
        }
        logger.Info("Registered Going Rogue Knight trader (level 20 access; legacy loyalty and stock).");
        return Task.CompletedTask;
    }

    internal static void Install(KnightTraderAccess access)
    {
        var harmony = new Harmony("xyz.pit.fireteam.goingrogue.knight-trader");
        try
        {
            Patch(typeof(ProfileHelper), nameof(ProfileHelper.GetCompleteProfile), [typeof(MongoId)], nameof(BeforeProfile));
            Patch(typeof(TraderHelper), nameof(TraderHelper.LevelUp), [typeof(MongoId), typeof(PmcData)], nameof(AfterLevelUp), postfix: true);
            Patch(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort), [typeof(MongoId), typeof(MongoId), typeof(bool)], nameof(BeforeAssort));
            Patch(typeof(TradeHelper), nameof(TradeHelper.BuyItem), [typeof(PmcData), typeof(ProcessBuyTradeRequestData), typeof(MongoId), typeof(bool), typeof(ItemEventRouterResponse)], nameof(BeforeBuy));
            Patch(typeof(TradeHelper), nameof(TradeHelper.SellItem), [typeof(PmcData), typeof(PmcData), typeof(ProcessSellTradeRequestData), typeof(MongoId), typeof(ItemEventRouterResponse)], nameof(BeforeSell));
            activeAccess = access;
        }
        catch { harmony.UnpatchSelf(); activeAccess = null; throw; }

        void Patch(Type type, string name, Type[] arguments, string hook, bool postfix = false)
        {
            var target = type.GetMethod(name, arguments) ?? throw new MissingMethodException(type.FullName, name);
            var patch = new HarmonyMethod(typeof(KnightTraderModule), hook);
            harmony.Patch(target, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
        }
    }

    private static void BeforeProfile(MongoId sessionId) => activeAccess?.Synchronize(sessionId);
    private static void AfterLevelUp(MongoId traderId, PmcData pmcData)
    {
        if (traderId == KnightTraderCatalog.Id) activeAccess?.Apply(pmcData);
    }
    private static bool BeforeAssort(MongoId sessionId, MongoId traderId, ref TraderAssort __result)
    {
        if (traderId != KnightTraderCatalog.Id || activeAccess?.CanTrade(sessionId) == true) return true;
        __result = KnightTraderAccess.EmptyAssort();
        return false;
    }
    private static bool BeforeBuy(ProcessBuyTradeRequestData buyRequestData, MongoId sessionId, ItemEventRouterResponse output) =>
        activeAccess?.Admit(buyRequestData.TransactionId, sessionId, output) ?? buyRequestData.TransactionId != KnightTraderCatalog.Id;
    private static bool BeforeSell(ProcessSellTradeRequestData sellRequest, MongoId sessionId, ItemEventRouterResponse output) =>
        activeAccess?.Admit(sellRequest.TransactionId, sessionId, output) ?? sellRequest.TransactionId != KnightTraderCatalog.Id;
}
