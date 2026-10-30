using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.GoingRogue.Server;

[Injectable(InjectionType.Singleton)]
public sealed class KnightTraderAccess(
    FriendlyServerSettingsService settings, ProfileHelper profiles, TraderHelper traders,
    FriendlyLanguageService language, HttpResponseUtil response)
{
    internal void Synchronize(MongoId sessionId)
    {
        var profile = profiles.GetPmcProfile(sessionId);
        if (profile?.Info == null || profile.TradersInfo == null) return;
        // Native initialization retains edition defaults; never reset existing standing/sales.
        traders.GetTrader(KnightTraderCatalog.Id, sessionId);
        traders.LevelUp(KnightTraderCatalog.Id, profile);
        Apply(profile);
    }

    internal void Apply(PmcData profile) => Apply(profile, settings.LoadSettings().GameplayMode);

    internal static void Apply(PmcData profile, string mode)
    {
        if (profile.TradersInfo?.TryGetValue(KnightTraderCatalog.Id, out var info) == true)
            info.Unlocked = mode == "GoingRogue" && profile.Info?.Level >= KnightTraderCatalog.UnlockLevel;
    }

    internal bool CanTrade(MongoId sessionId)
    {
        Synchronize(sessionId);
        return profiles.GetPmcProfile(sessionId)?.TradersInfo?.TryGetValue(KnightTraderCatalog.Id, out var info) == true &&
            info.Unlocked == true && info.Disabled != true;
    }

    internal bool Admit(MongoId traderId, MongoId sessionId, ItemEventRouterResponse output)
    {
        if (traderId != KnightTraderCatalog.Id || CanTrade(sessionId)) return true;
        var text = language.GetStringMap(sessionId, "socialUi");
        response.AppendErrorToOutput(output, text.GetValueOrDefault("GoingRogueKnightTraderLocked") ?? "GoingRogueKnightTraderLocked",
            BackendErrorCodes.UnknownTradingError);
        return false;
    }

    internal static TraderAssort EmptyAssort() => new() { Items = [], BarterScheme = [], LoyalLevelItems = [], NextResupply = 0 };
}
