using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace pitTeam.Server.Services;

// Enforce the setting on generated inventory, including generators that skip SPT's body.
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public sealed class FriendlyPmcArmbandHooks(
    ISptLogger<FriendlyPmcArmbandHooks> logger,
    FriendlyServerSettingsService settingsService) : IOnLoad
{
    private readonly ISptLogger<FriendlyPmcArmbandHooks> log = logger;
    private readonly FriendlyServerSettingsService settings = settingsService;
    private static FriendlyPmcArmbandHooks? current;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var harmony = new Harmony("xyz.pit.fireteam.server.pmc-armbands");
        try
        {
            var target = typeof(BotInventoryGenerator).GetMethod(nameof(BotInventoryGenerator.GenerateInventory),
                [typeof(MongoId), typeof(MongoId), typeof(BotType), typeof(BotGenerationDetails)]);
            if (target == null || target.ReturnType != typeof(BotBaseInventory))
                throw new MissingMethodException(typeof(BotInventoryGenerator).FullName, "GenerateInventory");
            current = this;
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(FriendlyPmcArmbandHooks)
                .GetMethod(nameof(InventoryPostfix), BindingFlags.Static | BindingFlags.NonPublic)!)
                { priority = Priority.Last });
            log.Info($"[PmcArmbands] Generated PMC inventory hook installed; applied setting: {(settings.PmcArmbandsEnabled ? "enabled" : "disabled")}.");
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            current = null;
            log.Error($"[PmcArmbands] Inventory enforcement unavailable; SPT config enforcement remains active: {ex}");
        }
        return Task.CompletedTask;
    }

    private static void InventoryPostfix(BotBaseInventory __result, BotGenerationDetails botGenerationDetails)
    {
        var owner = current;
        if (owner == null) return;
        try
        {
            EnforceInventory(__result, botGenerationDetails, owner.settings.PmcArmbandsEnabled);
        }
        catch (Exception ex)
        {
            owner.log.Warning($"[PmcArmbands] Failed to enforce generated {botGenerationDetails?.Side} armband: {ex.Message}");
        }
    }

    internal static void EnforceInventory(BotBaseInventory? inventory, BotGenerationDetails? details, bool enabled)
    {
        if (!enabled || details is not { IsPmc: true, IsPlayerScav: false }
            || inventory?.Items == null || inventory.Equipment is not { } equipment) return;

        MongoId template;
        if (string.Equals(details.Side, "Usec", StringComparison.OrdinalIgnoreCase)) template = ItemTpl.ARMBAND_BLUE;
        else if (string.Equals(details.Side, "Bear", StringComparison.OrdinalIgnoreCase)) template = ItemTpl.ARMBAND_RED;
        else return;

        var items = inventory.Items;
        // Do not add an orphan if a custom generator returned an incomplete inventory.
        if (!items.Any(item => item.Id == equipment)) return;
        var rootId = equipment.ToString();
        var bands = items.Where(item => string.Equals(item.ParentId, rootId, StringComparison.Ordinal)
            && string.Equals(item.SlotId, "ArmBand", StringComparison.OrdinalIgnoreCase)).ToList();
        if (bands.Count == 1 && bands[0].Template == template) return;

        // Replacing a container in this slot must also remove its complete child tree.
        var removed = bands.Select(item => item.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        bool foundChild;
        do
        {
            foundChild = false;
            foreach (var item in items)
                if (item.ParentId != null && removed.Contains(item.ParentId) && removed.Add(item.Id.ToString()))
                    foundChild = true;
        } while (foundChild);

        var band = new Item
        {
            Id = bands.Count > 0 ? bands[0].Id : new MongoId(),
            Template = template,
            ParentId = rootId,
            SlotId = "ArmBand",
        };
        items.RemoveAll(item => removed.Contains(item.Id.ToString()));
        items.Add(band);
    }
}
