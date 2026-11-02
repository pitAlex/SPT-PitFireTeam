using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;
using SPT.Common.Http;

namespace pitTeam.GoingRogue.Trading;

internal static class KnightSessionRefresh
{
    private static readonly FieldInfo MenuField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

    internal static async Task RefreshAsync()
    {
        if (!Singleton<ClientApplication<IEftSession>>.Instantiated) return;
        var app = Singleton<ClientApplication<IEftSession>>.Instance;
        var session = app?.GetClientBackEndSession();
        var profile = session?.Profile;
        var menu = app is TarkovApplication tarkov ? MenuField?.GetValue(tarkov) as MainMenuShowOperation : null;
        var book = menu?.QuestController?.Quests;
        // Startup synchronization precedes creation of the lobby controllers; the
        // normal login profile owns that case. Live mode changes have this book.
        if (profile == null) return;
        if (book == null) throw new InvalidOperationException("The lobby quest book is unavailable for Knight refresh.");
        if (book.ProfileId != profile.Id) throw new InvalidOperationException("Knight refresh quest profile does not match the session.");
        var mode = GameplayModeAddons.Current;
        var body = JObject.Parse(await Task.Run(() => RequestHandler.GetJson("/singleplayer/going-rogue/knight/session")));
        var state = body["data"];
        if ((int?)body["err"] != 0 || (string)state?["mode"] != mode.ToString() ||
            state?["unlocked"]?.Type != JTokenType.Boolean || state?["disabled"]?.Type != JTokenType.Boolean)
            throw new InvalidOperationException("Knight access refresh was not acknowledged for the active mode.");
        var templates = mode == GameplayMode.Allegiance ? await session.RequestQuestsTemplates(true) : null;
        if (GameplayModeAddons.Current != mode || !ReferenceEquals(app.GetClientBackEndSession(), session) || !ReferenceEquals(session.Profile, profile) ||
            !ReferenceEquals(menu.QuestController?.Quests, book))
            throw new InvalidOperationException("Knight refresh session changed while loading.");
        if (!profile.TradersInfo.TryGetValue(pitTeam.Utils.Props.KnightTrader, out var info))
            throw new InvalidOperationException("Registered Knight trader is missing from the session profile.");
        // Native merge retains existing quest instances, statuses and counters.
        if (templates != null) book.AddTemplates(templates.Where(template => Quests.KnightQuestPolicy.Owns(template.Id)));
        void Apply(Profile.TraderInfo target)
        {
            target.SetDisabled((bool)state["disabled"]);
            target.SetUnlocked((bool)state["unlocked"]);
            target.UpdateLevel();
        }
        Apply(info);
        var trader = session.Traders.FirstOrDefault(value => value.Id == pitTeam.Utils.Props.KnightTrader);
        if (trader?.Info != null && !ReferenceEquals(trader.Info, info)) Apply(trader.Info);
        Logger.LogInfo($"[GoingRogue] Refreshed Knight session mode={mode} unlocked={info.Unlocked} disabled={info.Disabled}");
    }
}
