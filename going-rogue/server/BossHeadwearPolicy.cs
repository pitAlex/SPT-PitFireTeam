using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace pitTeam.GoingRogue.Server;

// Runs on fresh companion raid kits, after replacement generators such as APBS.
// Halloween can replace headwear and suppress the other three head slots.
public sealed class BossHeadwearPolicy(BotTable bots, BotConfig config, BotInventoryGenerator generator,
    BotHelper botHelper, ItemHelper itemHelper)
{
    private static readonly EquipmentSlots[] HeadSlots =
        [EquipmentSlots.FaceCover, EquipmentSlots.Headwear, EquipmentSlots.Earpiece, EquipmentSlots.Eyewear];

    internal static bool NeedsRestore(BotBase profile)
    {
        string role = profile.Info?.Settings?.Role?.ToLowerInvariant() ?? "";
        return role is "bossknight" or "followerbigpipe" or "followerbirdeye" &&
            profile.Inventory?.Equipment is { } root && profile.Inventory.Items?.Any(item =>
                item.ParentId == root.ToString() && item.SlotId == nameof(EquipmentSlots.Headwear) &&
                item.Template == ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET) == true;
    }

    public void Ensure(BotBase profile)
    {
        if (!NeedsRestore(profile)) return;
        string role = profile.Info!.Settings!.Role!.ToLowerInvariant();
        if (!bots.Types.TryGetValue(role, out var template) || template == null ||
            !config.Equipment.TryGetValue(role, out var filters) || filters == null)
            throw new InvalidDataException($"Native companion head equipment policy is missing: {role}");
        Restore(profile, template, filters, generator.GenerateEquipment, botHelper, itemHelper);
    }

    internal static void Restore(BotBase profile, BotType template, EquipmentFilters filters,
        Func<GenerateEquipmentProperties, bool> generate, BotHelper botHelper, ItemHelper itemHelper)
    {
        if (!NeedsRestore(profile)) return;
        var cloner = new FastCloner();
        // Native generation removes rejected choices from its pools. Keep all
        // mutations local, including the inventory, until restoration succeeds.
        var native = cloner.Clone(template)!;
        var equipmentFilters = cloner.Clone(filters)!;
        var inventory = cloner.Clone(profile.Inventory)!;
        var items = inventory.Items!;
        string root = inventory.Equipment!.Value.ToString();
        var pumpkin = items.Single(item => item.ParentId == root && item.SlotId == nameof(EquipmentSlots.Headwear));
        var removed = itemHelper.FindAndReturnChildrenByAssort(pumpkin.Id, items).Select(item => item.Id).ToHashSet();
        removed.Add(pumpkin.Id);
        items.RemoveAll(item => removed.Contains(item.Id));
        string role = profile.Info!.Settings!.Role!.ToLowerInvariant();
        int level = profile.Info.Level ?? 1;
        MongoId botId = profile.Id ?? throw new InvalidDataException("Missing companion profile ID");
        foreach (var slot in HeadSlots)
        {
            if (items.Any(item => item.ParentId == root && item.SlotId == slot.ToString()) ||
                native.BotInventory?.Equipment?.TryGetValue(slot, out var pool) != true || pool == null) continue;
            pool.Remove(ItemTpl.HEADWEAR_JACKOLANTERN_TACTICAL_PUMPKIN_HELMET);
            generate(new GenerateEquipmentProperties
            {
                BotId = botId, RootEquipmentSlot = slot, RootEquipmentPool = pool,
                ModPool = native.BotInventory.Mods, SpawnChances = native.BotChances,
                BotData = new() { Role = role, EquipmentRole = role, Level = level },
                Inventory = inventory, BotEquipmentConfig = equipmentFilters,
                RandomisationDetails = botHelper.GetBotRandomizationDetails(level, equipmentFilters),
                GeneratingPlayerLevel = level
            });
        }
        profile.Inventory = inventory;
    }
}
