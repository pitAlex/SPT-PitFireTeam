using System.Reflection;
using Path = System.IO.Path;
using System.Runtime.CompilerServices;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;

internal static class LauncherLootChecks
{
    private static T Make<T>(Dictionary<string, object?> deps) => (T)typeof(T).GetConstructors().Single().Invoke(
        typeof(T).GetConstructors().Single().GetParameters().Select(p => deps.GetValueOrDefault(p.Name!)).ToArray());
    private static ISptLogger<T> Log<T>() => DispatchProxy.Create<ISptLogger<T>, TestLogger>();

    internal static void Run(string runtime, JsonUtil json, TemplateTable templates, GlobalTable globals,
        ProfileHelper profiles, FriendlyServerSettingsService settings, PmcData player, MongoId session,
        Action<bool, string> check, Action<string> mode)
    {
        string database = Path.Combine(runtime, "SPT_Data/database/locations/tarkovstreets");
        T Read<T>(string name) => json.Deserialize<T>(File.ReadAllText(Path.Combine(database, name + ".json")))!;
        T Config<T>(string name) => json.Deserialize<T>(File.ReadAllText(Path.Combine(runtime, "SPT_Data/configs", name + ".json")))!;
        var data = Read<StaticContainerDetails>("staticContainers");
        var distribution = Read<Dictionary<MongoId, StaticLootDetails>>("staticLoot");
        var source = new Location { StaticContainers = new(() => data), StaticLoot = new(() => distribution), StaticAmmo = Read<Dictionary<string, IEnumerable<StaticAmmoDetails>>>("staticAmmo") };
        var locations = (LocationTable)RuntimeHelpers.GetUninitializedObject(typeof(LocationTable));
        typeof(LocationTable).GetProperty(nameof(LocationTable.TarkovStreets))!.SetValue(locations, source);
        var clone = new FastCloner();
        var random = new RandomUtil(Log<RandomUtil>(), clone);
        var classes = Make<ItemBaseClassService>(new() { ["templateTable"] = templates, ["logger"] = Log<ItemBaseClassService>() });
        var filter = new ItemFilterService(Config<ItemConfig>("item"));
        var helper = Make<ItemHelper>(new() { ["templateTable"] = templates, ["itemBaseClassService"] = classes,
            ["logger"] = Log<ItemHelper>(), ["randomUtil"] = random, ["cloner"] = clone, ["itemFilterService"] = filter });
        var inventory = Make<InventoryHelper>(new() { ["itemHelper"] = helper, ["logger"] = Log<InventoryHelper>(), ["cloner"] = clone });
        var locationConfig = Config<LocationConfig>("location");
        var seasonal = Config<SeasonalEventConfig>("seasonalevents");
        var events = Make<SeasonalEventService>(new() { ["seasonalEventConfig"] = seasonal, ["globalTable"] = globals });
        var presets = new PresetHelper(globals, helper, clone);
        new PresetController(Log<PresetController>(), globals, presets).Initialize();
        var generator = Make<LocationLootGenerator>(new() { ["logger"] = Log<LocationLootGenerator>(), ["locationTable"] = locations,
            ["randomUtil"] = random, ["itemHelper"] = helper, ["presetHelper"] = presets,
            ["seasonalEventService"] = events, ["itemFilterService"] = filter, ["locationConfig"] = locationConfig,
            ["seasonalEventConfig"] = seasonal, ["counterTrackerHelper"] = new CounterTrackerHelper(), ["cloner"] = clone });
        var activity = new ProfileActivityService(new TimeUtil());
        activity.GetProfileActivityRaidData(session).RaidConfiguration = new() { Side = SideType.Pmc };
        var storage = new FriendlyTeammateStorage(new FileUtil(), json, Log<FriendlyTeammateStorage>(), settings);
        KnightQuestLootModule Module() => new(storage, settings, profiles, activity, locations, generator, helper, inventory, clone, json, Log<KnightQuestLootModule>());
        var module = Module();
        module.OnLoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        var previousQuests = player.Quests;
        var previousInventory = player.Inventory;
        try
        {
            player.Quests = [new() { QId = new(KnightQuestPolicy.Thieves), StartTime = 123, Status = QuestStatusEnum.Started, StatusTimers = [] }];
            player.Inventory = new() { Items = [] };
            var all = data.StaticContainers.Select(c => c.Template!.Id!).ToArray();
            check(KnightQuestLootModule.Containers.All(id => all.Count(x => x == id) == 1), "all three exact user-selected bags exist once in installed Streets data");
            var sourceJson = json.Serialize(data);
            var distJson = json.Serialize(distribution);
            var presetJson = json.Serialize(globals.ItemPresets);
            string chosen = module.SelectContainer(session, player, all);
            check(KnightQuestLootModule.Containers.Contains(chosen), "selection comes from the supplied pool");
            check(Module().SelectContainer(session, player, all) == chosen, "saved choice survives a new service instance");
            var otherSession = new MongoId();
            module.SelectContainer(otherSession, player, [KnightQuestLootModule.Containers.First(id => id != chosen)]);
            check(module.SelectContainer(session, player, all) == chosen, "another profile cannot change this player's choice");
            int bagCount = 0;
            foreach (var id in KnightQuestLootModule.Containers)
            {
                storage.WriteModeDocuments(session, KnightQuestLootModule.Mode, new Dictionary<string, string> {
                    [KnightQuestLootModule.Document] = json.Serialize(new KnightQuestLootModule.Placement { QuestStart = 123, Container = id })! });
                foreach (var multiplier in new[] { 1d, 0d })
                {
                    locationConfig.StaticLootMultiplier["tarkovstreets"] = multiplier;
                    // Keep random non-quest loot deterministic while exercising the actual
                    // installed native generator, preset, IDs and grid packing.
                    var bagTpl = data.StaticContainers.First(c => c.Template!.Id == id).Template!.Items!.First().Template;
                    var original = distribution[bagTpl];
                    distribution[bagTpl] = new() { ItemCountDistribution = [new() { Count = 1, RelativeProbability = 1 }],
                        ItemDistribution = [new() { Tpl = new("544fb25a4bdc2dfb738b4567"), RelativeProbability = 1 }] };
                    try
                    {
                        var unrelated = new SpawnpointTemplate { Id = "unrelated", Items = [] };
                        foreach (bool alreadyPresent in new[] { false, true })
                        {
                            var result = new LocationBase { Id = "TarkovStreets", Loot = alreadyPresent ? [unrelated, clone.Clone(data.StaticContainers.First(c => c.Template!.Id == id).Template!)!] : [unrelated] };
                            module.Apply(session, "TarkovStreets", true, result);
                            var bag = result.Loot!.Single(c => c.Id == id);
                            check(bag.IsAlwaysSpawn == true && result.Loot.Count() == 2 && ReferenceEquals(result.Loot.First(), unrelated), "guaranteed chosen bag, no duplicate container or unrelated mutation: " + id + "/" + multiplier);
                            check(bag.Items!.Count(i => i.Template == new MongoId(KnightQuestPolicy.StolenLauncher)) == 1, "exactly one special launcher even with zero loot multiplier");
                            check(bag.Items!.Select(i => i.Id).Distinct().Count() == bag.Items!.Count(), "unique generated inventory identities");
                            check(bag.Items!.Where(i => i.Id.ToString() != bag.Root).All(i => bag.Items!.Any(p => p.Id.ToString() == i.ParentId)), "all generated child parents resolve");
                            check(bag.Items!.Count(i => i.SlotId == "mod_magazine") == 1, "launcher preset attachments present");
                            string after = json.Serialize(result)!;
                            module.Apply(session, "tarkovstreets", true, result);
                            check(json.Serialize(result) == after, "repeat application does not duplicate or reroll launcher");
                            bagCount++;
                        }
                    }
                    finally { distribution[bagTpl] = original; }
                }
            }
            check(bagCount == 12 && json.Serialize(data) == sourceJson && json.Serialize(distribution) == distJson && json.Serialize(globals.ItemPresets) == presetJson,
                "all placement variants preserve shared map loot and weapon presets");
            void Denied(string map = "tarkovstreets", bool generate = true)
            {
                var result = new LocationBase { Id = "TarkovStreets", Loot = [] };
                module.Apply(session, map, generate, result);
                check(result.Loot.Count() == 0, "ineligible request leaves map loot untouched");
            }
            mode("Allegiance"); Denied(); mode("GunsForHire"); Denied(); mode("GoingRogue");
            Denied("woods"); Denied(generate: false);
            activity.GetProfileActivityRaidData(session).RaidConfiguration!.Side = SideType.Savage; Denied();
            activity.GetProfileActivityRaidData(session).RaidConfiguration!.Side = SideType.Pmc;
            foreach (var status in new[] { QuestStatusEnum.AvailableForStart, QuestStatusEnum.AvailableForFinish, QuestStatusEnum.Success, QuestStatusEnum.Fail })
            { player.Quests[0].Status = status; Denied(); }
            player.Quests[0].Status = QuestStatusEnum.Started;
            player.Inventory.Items = [new() { Id = new MongoId(), Template = new(KnightQuestPolicy.StolenLauncher), Upd = new() { SpawnedInSession = true } }]; Denied();
            player.Inventory.Items[0].Upd!.SpawnedInSession = false;
            check(KnightQuestLootModule.NeedsLauncher(player), "non-FIR copy cannot block recovering required FIR item");
            player.Inventory.Items = [];
            player.Quests[0].StartTime++;
            string replacement = module.SelectContainer(session, player, [KnightQuestLootModule.Containers[0]]);
            check(replacement == KnightQuestLootModule.Containers[0] && storage.ReadModeDocument<KnightQuestLootModule.Placement>(session, KnightQuestLootModule.Document, KnightQuestLootModule.Mode)!.QuestStart == 124,
                "new quest attempt writes a fresh scoped selection");
            try { module.SelectContainer(session, player, []); throw new Exception("missing pool accepted"); }
            catch (InvalidDataException) { check(true, "missing container pool fails explicitly instead of spawning elsewhere"); }
        }
        finally
        {
            player.Quests = previousQuests; player.Inventory = previousInventory;
            new Harmony("xyz.pit.fireteam.goingrogue.quest-loot").UnpatchSelf();
        }
    }
}
