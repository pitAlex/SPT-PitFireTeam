using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Image;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

internal static class QuestChecks
{
    internal static void Run(JsonUtil json, TemplateTable templates, GlobalTable globals, LocaleTable locales, ImageRouterService images,
        ProfileHelper profiles, FriendlyServerSettingsService settings, KnightTraderAccess access, PmcData profile,
        MongoId session, Action<bool, string> check, Action<string> mode)
    {
        var definitions = KnightQuestModule.ReadQuests(json);
        var questPlayer = new BotBase
        {
            Info = new() { Level = 20 },
            TradersInfo = new() { [KnightTraderCatalog.Id] = new() { Disabled = false } },
            Quests = [new() { QId = new(KnightQuestPolicy.Competition), StartTime = 1, Status = QuestStatusEnum.Started, StatusTimers = [] }]
        };
        foreach (string map in new[] { "woods", "shoreline", "LIGHTHOUSE" })
            check(KnightQuestModule.CanSpawnKnight(questPlayer, map), "active first quest authorizes native Knight on objective map " + map);
        foreach (string map in new[] { "bigmap", "factory4_day", "" })
            check(!KnightQuestModule.CanSpawnKnight(questPlayer, map), "first quest cannot spawn Knight on non-objective map " + map);
        foreach (var status in new[] { QuestStatusEnum.AvailableForStart, QuestStatusEnum.AvailableForFinish, QuestStatusEnum.Success, QuestStatusEnum.Fail })
        {
            questPlayer.Quests[0].Status = status;
            check(!KnightQuestModule.CanSpawnKnight(questPlayer, "woods"), "unstarted/finished/failed task cannot spawn Knight: " + status);
        }
        questPlayer.Quests[0].Status = QuestStatusEnum.Started;
        questPlayer.TradersInfo[KnightTraderCatalog.Id].Disabled = true;
        check(!KnightQuestModule.CanSpawnKnight(questPlayer, "woods"), "disabled Knight trader denies quest spawn");
        questPlayer.TradersInfo[KnightTraderCatalog.Id].Disabled = false;
        questPlayer.Info.Level = 19;
        check(!KnightQuestModule.CanSpawnKnight(questPlayer, "woods"), "level 20 remains a server admission requirement");
        questPlayer.Info.Level = 20;
        questPlayer.Quests[0].QId = new(KnightQuestPolicy.MyLand);
        check(KnightQuestModule.CanSpawnKnight(questPlayer, "bigmap") && !KnightQuestModule.CanSpawnKnight(questPlayer, "woods"), "second quest authorizes Customs only");
        check(definitions.Count == 3 && definitions.ContainsKey(new(KnightQuestPolicy.Competition)) && definitions.ContainsKey(new(KnightQuestPolicy.MyLand)) &&
            definitions.ContainsKey(new(KnightQuestPolicy.Thieves)), "all three exact legacy quest IDs retained");
        var first = definitions[new(KnightQuestPolicy.Competition)];
        var second = definitions[new(KnightQuestPolicy.MyLand)];
        var third = definitions[new(KnightQuestPolicy.Thieves)];
        questPlayer.Quests[0].QId = third.Id;
        check(!KnightQuestModule.CanSpawnKnight(questPlayer, "TarkovStreets") && !KnightQuestPolicy.UsesMap(KnightQuestPolicy.Thieves, "TarkovStreets"),
            "retrieval task never triggers a quest companion or proximity requirement");
        check(third.Conditions.AvailableForStart!.First().Value == 20 &&
            third.Conditions.AvailableForStart!.Single(c => c.ConditionType == "Quest").AvailableAfter == 0 &&
            json.Serialize(third.Conditions.AvailableForStart)!.Contains(KnightQuestPolicy.MyLand), "third task requires My Land success and level 20 without a delay");
        check(third.Conditions.AvailableForFinish!.Select(c => c.ConditionType).SequenceEqual(new[] { "FindItem", "HandoverItem" }) &&
            third.Conditions.AvailableForFinish!.All(c => c.Value == 1 && c.OnlyFoundInRaid == true) &&
            third.Conditions.AvailableForFinish!.All(c => json.Serialize(c)!.Contains(KnightQuestPolicy.StolenLauncher)),
            "native third task requires finding and handing over one exact special launcher Found in Raid");
        check(third.Conditions.Fail!.Count == 0 && third.Rewards!["Success"].Count == 3 &&
            json.Serialize(third.Rewards)!.Contains("15000") && json.Serialize(third.Rewards)!.Contains("2000"),
            "third task retains native rewards and no kill/death failure condition");
        check(first.Conditions.AvailableForStart!.Single().Value == 20 && second.Conditions.AvailableForStart!.First().Value == 20, "level 20 entry requirements retained");
        check(first.Conditions.AvailableForFinish!.Select(c => c.Value).SequenceEqual(new double?[] { 5, 5, 5 }), "first quest requires five player kills on each map");
        check(first.Conditions.AvailableForFinish!.All(c => c.Counter!.Conditions!.Any(x => x.ConditionType == "Location")), "all three native map filters retained");
        var prerequisite = second.Conditions.AvailableForStart!.Single(c => c.ConditionType == "Quest");
        check(prerequisite.AvailableAfter == 3600 && json.Serialize(prerequisite)!.Contains(KnightQuestPolicy.Competition), "second task retains first completion and one-hour delay");
        check(second.Conditions.AvailableForFinish!.Single().Value == 8 &&
            json.Serialize(second.Conditions.AvailableForFinish)!.Contains("prapor_27_1"), "My Land uses eight kills in native Scav Base zone");
        check(first.Restartable && second.Restartable && first.Conditions.Fail!.Count == 1 && second.Conditions.Fail!.Count == 1, "both native tasks retain restartable failure counters");
        check(first.Rewards!["Success"].Count == 2 && second.Rewards!["Success"].Count == 3 &&
            json.Serialize(first.Rewards)!.Contains("32500") && json.Serialize(second.Rewards)!.Contains("24000") && json.Serialize(second.Rewards)!.Contains("5696686a4bdc2da3298b456a"), "legacy XP, standing and USD rewards deserialize natively");
        check(definitions.Values.All(q => q.Image == KnightQuestPolicy.ImageFor(q.Id.ToString()) && q.ProgressSource == "eft" && q.AcceptanceAndFinishingSource == "eft"), "native EFT progression and per-quest artwork configured");
        check(first.Image != second.Image && second.Image == KnightQuestPolicy.MyLandImage, "My Land uses its stronghold artwork independently of the first quest");

        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Quests))!.SetValue(templates, new Dictionary<MongoId, Quest>());
        var output = new ItemEventRouterResponse();
        var holder = (EventOutputHolder)RuntimeHelpers.GetUninitializedObject(typeof(EventOutputHolder));
        typeof(EventOutputHolder).GetField("_outputStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(holder,
            new Dictionary<MongoId, ItemEventRouterResponse> { [session] = output });
        var service = new CustomQuestService(templates, locales, null!, Activator.CreateInstance<QuestConfig>());
        var pmc = Activator.CreateInstance<PmcConfig>(); pmc.GlobalLootBlacklist = [];
        var modCache = new ModItemCacheService(DispatchProxy.Create<ISptLogger<ModItemCacheService>, TestLogger>(),
            [new SptMod { Directory = AppContext.BaseDirectory, ModMetadata = new GoingRogueMetadata(), Assemblies = [typeof(KnightQuestModule).Assembly] }]);
        var customItems = new CustomItemService(DispatchProxy.Create<ISptLogger<CustomItemService>, TestLogger>(), templates, locales, null!, null!, pmc,
            new ItemBaseClassService(DispatchProxy.Create<ISptLogger<ItemBaseClassService>, TestLogger>(), templates, null!),
            modCache, new FastCloner());
        var baseLauncher = json.Serialize(templates.Items[new(KnightQuestItemCatalog.NativeLauncher)]);
        var module = new KnightQuestModule(service, templates, locales, new ImageRouter(new FileUtil(), images, null!), json,
            access, settings, holder, DispatchProxy.Create<ISptLogger<KnightQuestModule>, TestLogger>(), customItems, globals);
        module.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            check(templates.Quests.Count == 3, "real native quest service registers all three tasks");
            var launcher = templates.Items[new(KnightQuestPolicy.StolenLauncher)];
            check(launcher.Properties!.QuestItem == true && launcher.Properties.IsUngivable == true && launcher.Properties.IsUnbuyable == true &&
                launcher.Properties.Width == 4 && launcher.Properties.Height == 1, "native item clone retains the legacy special quest item flags and size");
            check(json.Serialize(templates.Items[new(KnightQuestItemCatalog.NativeLauncher)]) == baseLauncher &&
                !ReferenceEquals(launcher.Properties, templates.Items[new(KnightQuestItemCatalog.NativeLauncher)].Properties), "custom launcher never mutates the ordinary M32A1");
            check(pmc.GlobalLootBlacklist.Contains(new(KnightQuestPolicy.StolenLauncher)) &&
                templates.Handbook.Items!.Any(i => i.Id == new MongoId(KnightQuestPolicy.StolenLauncher)), "quest launcher is registered through native item/cache/handbook services without ordinary PMC loot");
            check(modCache.GetCachedItemIdsFromMod("xyz.pit.fireteam.goingrogue").Contains(new(KnightQuestPolicy.StolenLauncher)),
                "native mod-item recovery cache owns the special launcher");
            var preset = globals.ItemPresets.Values.Single(p => p.Encyclopedia == new MongoId(KnightQuestPolicy.StolenLauncher));
            check(preset.Items.Count == 5 && preset.Items.All(i => templates.Items.ContainsKey(i.Template)), "legacy launcher preset and all attachments resolve against installed SPT");
            check(File.Exists(images.GetByKey("/files/quest/icon/knightcompetitionquest")), "native image route resolves new quest artwork");
            var imageRequest = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            imageRequest.Request.Path = KnightQuestPolicy.Image;
            check(new ImageRouter(new FileUtil(), images, null!).CanHandle(imageRequest), "native case-normalized image routing handles the quest's original mixed-case URL");
            imageRequest.Request.Path = second.Image;
            check(new ImageRouter(new FileUtil(), images, null!).CanHandle(imageRequest) &&
                File.Exists(images.GetByKey("/files/quest/icon/knightmylandquest")), "native image routing resolves My Land stronghold artwork");
            check(images.GetByKey("/files/quest/icon/knightmylandquest") != images.GetByKey("/files/quest/icon/knightcompetitionquest"), "quest image routes resolve different files");
            imageRequest.Request.Path = third.Image;
            check(new ImageRouter(new FileUtil(), images, null!).CanHandle(imageRequest) && File.Exists(images.GetByKey("/files/quest/icon/knightthieves")), "native route resolves the third quest's original artwork");
            check(KnightQuestModule.ReadLocales(json)["en"][second.Description].Contains("we'll make it ours"), "deployed dialogue describes taking the stronghold");
            check(locales.Global["en"].Value![first.Name] == "Who let the dogs out?" && locales.Global["fr"].Value![first.Name] == "Who let the dogs out?", "quest locales use embedded English fallback");
            check(locales.Global["ru"].Value![first.Name] != "Who let the dogs out?", "existing Russian quest translation retained");
            try { module.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("duplicate quests accepted"); }
            catch (InvalidDataException) { check(templates.Quests.Count == 3, "duplicate preflight does not overwrite quest data"); }

            var questController = (QuestController)RuntimeHelpers.GetUninitializedObject(typeof(QuestController));
            foreach (string selected in new[] { "Allegiance", "GunsForHire" })
            {
                mode(selected);
                output.Warnings = [];
                check(ReferenceEquals(output, questController.AcceptQuest(profile, new() { QuestId = first.Id }, session)) && output.Warnings.Count == 1,
                    "real native accept hook rejects wrong mode: " + selected);
                output.Warnings = [];
                check(ReferenceEquals(output, questController.CompleteQuest(profile, new() { QuestId = second.Id }, session)) && output.Warnings.Count == 1,
                    "real native completion hook rejects wrong mode before rewards: " + selected);
                output.Warnings = [];
                check(ReferenceEquals(output, questController.HandoverQuest(profile, new() { QuestId = third.Id }, session)) && output.Warnings.Count == 1,
                    "real native third-task handover rejects wrong mode before touching items: " + selected);
                output.Warnings = [];
                check(ReferenceEquals(output, questController.AcceptQuest(profile, new() { QuestId = third.Id }, session)) && output.Warnings.Count == 1,
                    "third task cannot be accepted outside Going Rogue: " + selected);
                output.Warnings = [];
                check(ReferenceEquals(output, questController.CompleteQuest(profile, new() { QuestId = third.Id }, session)) && output.Warnings.Count == 1,
                    "third task cannot grant rewards outside Going Rogue: " + selected);
            }
            var previous = profile.Quests;
            try
            {
                profile.Quests = definitions.Values.Select(q => new QuestStatus { QId = q.Id, StartTime = 1, Status = QuestStatusEnum.Started, StatusTimers = [] }).ToList();
                var helperConstructor = typeof(QuestHelper).GetConstructors().Single();
                var deps = new Dictionary<string, object?> { ["templateTable"] = templates, ["profileHelper"] = profiles,
                    ["cloner"] = new FastCloner(), ["rewardHelper"] = RuntimeHelpers.GetUninitializedObject(typeof(RewardHelper)) };
                var helper = (QuestHelper)helperConstructor.Invoke(helperConstructor.GetParameters().Select(p => deps.GetValueOrDefault(p.Name!)).ToArray());
                check(helper.GetClientQuests(session).Count == 0 && profile.Quests.Count == 3, "native quest listing hides accepted tasks outside mode without deleting saved progress");
                mode("GoingRogue");
                check(helper.GetClientQuests(session).Count == 3, "native quest listing restores tasks on mode return");
            }
            finally { profile.Quests = previous; }
        }
        finally { new Harmony("xyz.pit.fireteam.goingrogue.knight-quests").UnpatchSelf(); }
    }
}
