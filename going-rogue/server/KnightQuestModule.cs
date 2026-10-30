using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace pitTeam.GoingRogue.Server;

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public sealed class KnightQuestModule(CustomQuestService quests, TemplateTable templates, LocaleTable locales,
    ImageRouter images, JsonUtil json, KnightTraderAccess access, FriendlyServerSettingsService settings,
    EventOutputHolder outputs, ISptLogger<KnightQuestModule> logger,
    CustomItemService items, GlobalTable globals) : IOnLoad
{
    private static KnightQuestModule? active;
    private bool IsGoingRogue => settings.LoadSettings().GameplayMode == "GoingRogue";
    internal static bool CanSpawnKnight(BotBase player, string location) =>
        player.Info?.Level >= KnightTraderCatalog.UnlockLevel &&
        player.Quests?.Any(quest => quest.Status == QuestStatusEnum.Started &&
            KnightQuestPolicy.UsesMap(quest.QId.ToString(), location)) == true &&
        player.TradersInfo?.TryGetValue(KnightTraderCatalog.Id, out var trader) == true && trader.Disabled != true;
    internal static Dictionary<MongoId, Quest> ReadQuests(JsonUtil json) => Read<Dictionary<MongoId, Quest>>(json, "KnightQuests.json");
    internal static Dictionary<string, Dictionary<string, string>> ReadLocales(JsonUtil json) =>
        Read<Dictionary<string, Dictionary<string, string>>>(json, "KnightQuestLocales.json");

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = ReadQuests(json);
        var translations = ReadLocales(json);
        if (definitions.Count != 3 || definitions.Keys.Any(id => !KnightQuestPolicy.Owns(id.ToString())) ||
            definitions.Values.Any(q => q.TraderId != KnightTraderCatalog.Id || q.Image != KnightQuestPolicy.ImageFor(q.Id.ToString())) ||
            definitions.Keys.Any(templates.Quests.ContainsKey))
            throw new InvalidDataException("Invalid or duplicate Knight quest definitions.");
        var artwork = definitions.Values.Select(q => q.Image).Distinct().ToDictionary(route => route,
            route => Path.Combine(Path.GetDirectoryName(typeof(KnightQuestModule).Assembly.Location)!, "Resources", "quests", Path.GetFileName(route)));
        foreach (var path in artwork.Values)
            if (!File.Exists(path)) throw new FileNotFoundException("Knight quest image is missing.", path);
        KnightQuestItemCatalog.Register(items, templates, globals, locales, json);
        // Native SPT registration and native global-locale fallback; no external quest loader.
        var text = locales.Global.Keys.ToDictionary(locale => locale, locale =>
        {
            var merged = new Dictionary<string, string>(translations["en"]);
            if (translations.TryGetValue(locale, out var translated))
                foreach (var entry in translated) merged[entry.Key] = entry.Value;
            return merged;
        });
        foreach (var definition in definitions.Values)
        {
            var result = quests.CreateQuest(new() { NewQuest = definition, Locales = text });
            if (!result.Success) throw new InvalidDataException(string.Join("; ", result.Errors));
        }
        foreach (var (route, path) in artwork) images.AddRoute(Path.ChangeExtension(route, null), path);
        Install();
        logger.Info("Registered Knight quests 1–3 with native objectives, rewards and quest images. Quest 3 container placement is not configured yet.");
        return Task.CompletedTask;
    }

    private void Install()
    {
        var harmony = new Harmony("xyz.pit.fireteam.goingrogue.knight-quests");
        try
        {
            var list = typeof(QuestHelper).GetMethod(nameof(QuestHelper.GetClientQuests), [typeof(MongoId)])
                ?? throw new MissingMethodException(nameof(QuestHelper.GetClientQuests));
            harmony.Patch(list, postfix: new HarmonyMethod(typeof(KnightQuestModule), nameof(AfterList)));
            Patch(nameof(QuestController.AcceptQuest), [typeof(PmcData), typeof(AcceptQuestRequestData), typeof(MongoId)], nameof(BeforeAccept));
            Patch(nameof(QuestController.CompleteQuest), [typeof(PmcData), typeof(CompleteQuestRequestData), typeof(MongoId)], nameof(BeforeComplete));
            Patch(nameof(QuestController.HandoverQuest), [typeof(PmcData), typeof(HandoverQuestRequestData), typeof(MongoId)], nameof(BeforeHandover));
            active = this;
        }
        catch { harmony.UnpatchSelf(); active = null; throw; }
        void Patch(string name, Type[] args, string hook)
        {
            var target = typeof(QuestController).GetMethod(name, args) ?? throw new MissingMethodException(name);
            var patch = new HarmonyMethod(typeof(KnightQuestModule), hook);
            harmony.Patch(target, prefix: patch);
        }
    }

    internal static T Read<T>(JsonUtil json, string resource)
    {
        using var stream = typeof(KnightQuestModule).Assembly.GetManifestResourceStream("pitFireTeam.GoingRogue.Server.Resources.quests." + resource)
            ?? throw new InvalidDataException("Missing Knight quest resource: " + resource);
        using var reader = new StreamReader(stream);
        return json.Deserialize<T>(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid Knight quest resource: " + resource);
    }
    private static void AfterList(ref List<Quest> __result)
    {
        if (active?.IsGoingRogue != true)
            __result = __result.Where(q => !KnightQuestPolicy.Owns(q.Id.ToString())).ToList();
    }
    private bool Admit(MongoId quest, MongoId session, ref ItemEventRouterResponse result)
    {
        if (!KnightQuestPolicy.Owns(quest.ToString()) || access.CanTrade(session)) return true;
        result = outputs.GetOutput(session);
        access.Admit(KnightTraderCatalog.Id, session, result);
        return false;
    }
    private static bool BeforeAccept(AcceptQuestRequestData acceptedQuest, MongoId sessionID, ref ItemEventRouterResponse __result) =>
        active?.Admit(acceptedQuest.QuestId, sessionID, ref __result) ?? !KnightQuestPolicy.Owns(acceptedQuest.QuestId.ToString());
    private static bool BeforeComplete(CompleteQuestRequestData request, MongoId sessionId, ref ItemEventRouterResponse __result) =>
        active?.Admit(request.QuestId, sessionId, ref __result) ?? !KnightQuestPolicy.Owns(request.QuestId.ToString());
    private static bool BeforeHandover(HandoverQuestRequestData request, MongoId sessionID, ref ItemEventRouterResponse __result) =>
        active?.Admit(request.QuestId, sessionID, ref __result) ?? !KnightQuestPolicy.Owns(request.QuestId.ToString());
}
