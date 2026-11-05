using System.Reflection;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace pitTeam.GoingRogue.Server;

// Per-player placement; never change the shared location table or forced-loot pool.
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 2)]
public sealed class KnightQuestLootModule(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
    ProfileHelper profiles, ProfileActivityService activity, LocationTable locations, LocationLootGenerator generator,
    ItemHelper items, InventoryHelper inventory, ICloner cloner, JsonUtil json, ISptLogger<KnightQuestLootModule> logger) : IOnLoad
{
    internal const string Document = "knight-launcher-placement.json";
    internal const string Mode = "Allegiance";
    internal static readonly string[] Containers = [
        "container_City_SE_02_DesignStuff_00005",
        "container_City_SE_02_DesignStuff_00002",
        "container_City_SE_02_DesignStuff_00004"
    ];
    private static KnightQuestLootModule? active;
    private readonly FriendlyServerSettingsService settingsService = settings;
    private readonly LocationTable locationTable = locations;
    private readonly ISptLogger<KnightQuestLootModule> log = logger;
    private readonly object sync = new();
    private static readonly MethodInfo GenerateContainer = AccessTools.Method(typeof(LocationLootGenerator), "AddLootToContainer",
        [typeof(StaticContainerData), typeof(IEnumerable<StaticForced>), typeof(Dictionary<MongoId, StaticLootDetails>),
         typeof(Dictionary<string, IEnumerable<StaticAmmoDetails>>), typeof(string)])
        ?? throw new MissingMethodException("LocationLootGenerator.AddLootToContainer");

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var harmony = new Harmony("xyz.pit.fireteam.goingrogue.quest-loot");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest),
                [typeof(PmcData), typeof(AcceptQuestRequestData), typeof(MongoId)]),
                postfix: new HarmonyMethod(typeof(KnightQuestLootModule), nameof(AfterAccept)));
            harmony.Patch(AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot),
                [typeof(MongoId), typeof(string), typeof(bool)]),
                postfix: new HarmonyMethod(typeof(KnightQuestLootModule), nameof(AfterLocation)));
            active = this;
        }
        catch { harmony.UnpatchSelf(); active = null; throw; }
        return Task.CompletedTask;
    }

    internal static bool NeedsLauncher(PmcData? player) =>
        player?.Quests?.Any(q => q.QId == new MongoId(KnightQuestPolicy.Thieves) && q.Status == QuestStatusEnum.Started) == true &&
        player.Inventory?.Items?.Any(i => i.Template == new MongoId(KnightQuestPolicy.StolenLauncher) && i.Upd?.SpawnedInSession == true) != true;

    internal string SelectContainer(MongoId session, PmcData player, IReadOnlyCollection<string> available)
    {
        lock (sync)
        {
            var quest = player.Quests!.Single(q => q.QId == new MongoId(KnightQuestPolicy.Thieves));
            var saved = storage.ReadModeDocument<Placement>(session, Document, Mode);
            if (saved?.QuestStart == quest.StartTime && Containers.Contains(saved.Container) && available.Contains(saved.Container))
                return saved.Container;
            var pool = Containers.Where(available.Contains).ToArray();
            if (pool.Length == 0) throw new InvalidDataException("None of the configured Streets launcher containers exist.");
            saved = new Placement { QuestStart = quest.StartTime, Container = pool[Random.Shared.Next(pool.Length)] };
            storage.WriteModeDocuments(session, Mode, new Dictionary<string, string> { [Document] = json.Serialize(saved)! });
            log.Info($"[KnightQuestLoot] Selected {saved.Container} for profile {session}.");
            return saved.Container;
        }
    }

    internal void Apply(MongoId session, string map, bool generateLoot, LocationBase result)
    {
        if (!generateLoot || !string.Equals(map, "tarkovstreets", StringComparison.OrdinalIgnoreCase) ||
            settingsService.LoadSettings().GameplayMode != Mode || activity.GetProfileActivityRaidData(session)?.RaidConfiguration?.Side != SideType.Pmc)
            return;
        var player = profiles.GetPmcProfile(session);
        if (!NeedsLauncher(player)) return;
        if (result.Loot?.SelectMany(point => point.Items ?? []).Any(i => i.Template == new MongoId(KnightQuestPolicy.StolenLauncher)) == true)
            return; // Repeated processing cannot add a second copy.
        var location = locationTable.GetLocation("tarkovstreets") ?? throw new InvalidDataException("Streets location is missing.");
        var data = location.StaticContainers!.Value!;
        var available = data.StaticContainers.Where(c => c.Template?.IsContainer == true).Select(c => c.Template!.Id!).ToArray();
        var selected = SelectContainer(session, player!, available);
        var template = data.StaticContainers.Single(c => c.Template?.Id == selected);
        var forced = new[] { new StaticForced { ContainerId = selected, ItemTpl = new(KnightQuestPolicy.StolenLauncher) } }
            .Concat((data.StaticForced ?? []).Where(f => f.ContainerId == selected && f.ItemTpl != new MongoId(KnightQuestPolicy.StolenLauncher))).ToArray();
        // Native forced items are packed before random loot. Only this container is
        // regenerated, and all inputs are copies so third-party hooks cannot mutate DB data.
        var generated = (StaticContainerData)GenerateContainer.Invoke(generator,
            [cloner.Clone(template), forced, cloner.Clone(location.StaticLoot!.Value!), cloner.Clone(location.StaticAmmo), "tarkovstreets"])!;
        var bag = generated.Template!;
        EnsureLauncher(bag);
        var launcher = bag.Items!.Single(i => i.Template == new MongoId(KnightQuestPolicy.StolenLauncher));
        foreach (var item in bag.Items!.Cast<Item>().GetItemWithChildren(launcher.Id))
        {
            item.Upd ??= new();
            item.Upd.SpawnedInSession = true;
        }
        bag.IsAlwaysSpawn = true;
        result.Loot = (result.Loot ?? []).Where(point => point.Id != selected).Append(bag).ToList();
        log.Info($"[KnightQuestLoot] Placed launcher in {selected} for profile {session}.");
    }

    // Native AddLootToContainer returns early on a zero-item roll (including a
    // zero loot multiplier), before processing forced loot. Cover that case too.
    internal void EnsureLauncher(SpawnpointTemplate bag)
    {
        int count = bag.Items?.Count(i => i.Template == new MongoId(KnightQuestPolicy.StolenLauncher)) ?? 0;
        if (count == 1) return;
        if (count > 1) throw new InvalidDataException("Native loot returned duplicate quest launchers.");
        var preset = KnightQuestModule.Read<Preset>(json, "KnightLauncherPreset.json");
        var tree = items.ReplaceIDs(preset.Items, null).ToList();
        var root = bag.Items!.Single(i => i.Id.ToString() == bag.Root);
        var grid = items.GetItem(root.Template).Value!.Properties!.Grids!.Single();
        var map = inventory.GetContainerMap(grid.Properties!.CellsH!.Value, grid.Properties.CellsV!.Value, bag.Items!, root.Id);
        if (inventory.PlaceItemInContainer(map, tree, root.Id.ToString(), grid.Name!).Success != true)
            throw new InvalidDataException($"Launcher does not fit selected container {bag.Id}; original raid loot retained.");
        foreach (var item in tree) { item.Upd ??= new(); item.Upd.SpawnedInSession = true; }
        bag.Items = bag.Items!.Concat(tree.Select(item => item.ToLootItem())).ToList();
    }

    private static void AfterAccept(PmcData pmcData, AcceptQuestRequestData acceptedQuest, MongoId sessionID)
    {
        if (active == null || acceptedQuest.QuestId != new MongoId(KnightQuestPolicy.Thieves) ||
            active.settingsService.LoadSettings().GameplayMode != Mode || !NeedsLauncher(pmcData)) return;
        try
        {
            var available = active.locationTable.GetLocation("tarkovstreets")!.StaticContainers!.Value!.StaticContainers
                .Where(c => c.Template?.IsContainer == true).Select(c => c.Template!.Id!).ToArray();
            active.SelectContainer(sessionID, pmcData, available);
        }
        catch (Exception ex) { active.log.Error($"[KnightQuestLoot] Could not save launcher placement: {ex.Message}"); }
    }

    private static void AfterLocation(MongoId sessionId, string name, bool generateLoot, LocationBase __result)
    {
        try { active?.Apply(sessionId, name, generateLoot, __result); }
        catch (Exception ex) { active?.log.Error($"[KnightQuestLoot] Launcher placement failed; original raid loot retained: {ex}"); }
    }

    public sealed class Placement
    {
        public double QuestStart { get; set; }
        public string Container { get; set; } = "";
    }
}
