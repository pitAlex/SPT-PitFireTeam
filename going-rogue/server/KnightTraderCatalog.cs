using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.GoingRogue.Server;

internal static class KnightTraderCatalog
{
    internal const string IdValue = "67768b19fa281ca31708b187";
    internal const int UnlockLevel = 20;
    internal const string AvatarPath = "/files/trader/avatar/knight.jpg";
    internal static readonly MongoId Id = new(IdValue);

    internal static Trader Create(JsonUtil json, TemplateTable templates)
    {
        var traderBase = Read<TraderBase>(json, "base.json");
        var assort = Read<TraderAssort>(json, "assort.json");
        var assembly = typeof(KnightTraderCatalog).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Resources.traderKnight.weapons.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var weapon = json.Deserialize<WeaponOffer>(reader.ReadToEnd())
                ?? throw new InvalidDataException($"Invalid Knight weapon resource: {resource}");
            var root = weapon.Items[0];
            assort.Items.AddRange(weapon.Items);
            assort.BarterScheme[root.Id] = [[weapon.Price]];
            assort.LoyalLevelItems[root.Id] = weapon.Level;
        }

        // The old loyalty map also names seven quest reward offers absent from this stock.
        // Quest rewards/locks are added with their actual quests, not as orphan trader entries.
        var roots = assort.Items.Where(item => item.ParentId == "hideout").Select(item => item.Id).ToHashSet();
        foreach (var id in assort.LoyalLevelItems.Keys.Where(id => !roots.Contains(id)).ToArray()) assort.LoyalLevelItems.Remove(id);
        // Retain concrete legacy prices/tiers. Modern fields and the access lock are explicit.
        traderBase.UnlockedByDefault = false;
        traderBase.IsAvailableInPVE = true;
        traderBase.NextResupply = 0;
        traderBase.ItemsSell = [];
        traderBase.TransferableItems = new() { Category = [], IdList = [] };
        traderBase.ProhibitedTransferableItems = new() { Category = [], IdList = [] };
        foreach (var tier in traderBase.LoyaltyLevels!) tier.MinLevel = UnlockLevel;
        var trader = new Trader
        {
            Base = traderBase, Assort = assort,
            QuestAssort = new() { ["started"] = [], ["success"] = [], ["fail"] = [] },
            Dialogue = [], Suits = [], Services = []
        };
        Validate(trader, templates);
        return trader;
    }

    private static T Read<T>(JsonUtil json, string name)
    {
        using var stream = typeof(KnightTraderCatalog).Assembly.GetManifestResourceStream(
            "pitFireTeam.GoingRogue.Server.Resources.traderKnight." + name)
            ?? throw new InvalidDataException($"Missing Knight trader resource: {name}");
        using var reader = new StreamReader(stream);
        return json.Deserialize<T>(reader.ReadToEnd()) ?? throw new InvalidDataException($"Invalid Knight trader resource: {name}");
    }

    internal static void Validate(Trader trader, TemplateTable templates)
    {
        if (trader.Base.Id != Id || trader.Base.LoyaltyLevels?.Count != 3)
            throw new InvalidDataException("Invalid Knight trader identity/loyalty levels.");
        var ids = new HashSet<MongoId>();
        foreach (var item in trader.Assort.Items)
        {
            if (!ids.Add(item.Id) || !templates.Items.ContainsKey(item.Template))
                throw new InvalidDataException($"Invalid Knight trader item: {item.Id}/{item.Template}");
        }
        foreach (var item in trader.Assort.Items)
        {
            if (item.ParentId != "hideout")
            {
                if (!ids.Contains(new MongoId(item.ParentId!))) throw new InvalidDataException($"Orphan Knight item: {item.Id}");
                continue;
            }
            if (!trader.Assort.BarterScheme.TryGetValue(item.Id, out var schemes) || schemes.Count == 0 ||
                !trader.Assort.LoyalLevelItems.TryGetValue(item.Id, out var level) || level < 1 || level > 3 ||
                schemes.Any(scheme => scheme.Count == 0 || scheme.Any(price => price.Count is not > 0 || !templates.Items.ContainsKey(price.Template))))
                throw new InvalidDataException($"Invalid Knight offer: {item.Id}");
        }
        foreach (var offers in trader.QuestAssort.Values)
            if (offers.Keys.Any(offer => !ids.Contains(offer))) throw new InvalidDataException("Unknown Knight quest-locked offer.");
    }

    private sealed record WeaponOffer
    {
        [JsonPropertyName("price")] public required BarterScheme Price { get; init; }
        [JsonPropertyName("level")] public int Level { get; init; }
        [JsonPropertyName("item")] public required List<Item> Items { get; init; }
    }
}
