using System.Text.Json;
using pitTeam.Server.Models;
using pitTeam.Server.Persistence;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace SPTarkov.Server.Core.Models.Common
{
    public record MongoId(string Id)
    {
        public MongoId() : this(Guid.NewGuid().ToString("N")[..24]) { }
        public override string ToString() => Id;
        public static implicit operator string(MongoId value) => value.Id;
    }
}
namespace SPTarkov.Server.Core.Models.Eft.Common { public record PmcData { public Inventory Inventory { get; set; } = new(); } }
namespace SPTarkov.Server.Core.Models.Eft.Profile { public record GetOtherProfileResponse; }
namespace SPTarkov.Server.Core.Models.Utils { public interface IRequestData; }
namespace SPTarkov.Server.Core.Models.Enums
{
    public enum EquipmentSlots { Pockets, Scabbard, SecuredContainer }
    public static class BaseClasses { public const string AMMO = "ammo", BUILT_IN_INSERTS = "insert", ARMOR_PLATE = "plate"; }
}
namespace pitTeam.Server.Constants
{
    public static class FriendlyItemTemplateIds
    {
        public static class Currency { public const string Roubles = "roubles"; }
        public static class Weapon { public const string HiringBayonet = "5bffdc370db834001d23eca8"; }
    }
}
namespace pitTeam.Server.Models
{
    public record FriendlyRecruitRequestEntry { public string ProfileJson { get; set; } = ""; public int? RecruitmentGearPrice { get; set; } }
    public record FixtureTeammateSettings { public int? RecruitmentGearPrice { get; set; } }
    public record FriendlyTeammateBuyKitResponse { public List<Item>? PlayerStashItems { get; set; } }
    public sealed class FriendlyTeammateException(string message) : Exception(message);
}
namespace pitTeam.Server.Services
{
    public partial class FriendlyTeammateService
    {
        public readonly FixtureStorage storage;
        public readonly FixtureSave saveServer;
        public readonly FixtureSettings settingsService = new();
        public readonly FixtureItemHelper itemHelper = new();
        public readonly FixtureRagfairConfig ragfairConfig = new();
        public readonly FixtureHideoutTable hideoutTable = new();
        public readonly FixtureTraderHelper traderHelper = new();
        public Func<Item, double> PriceLookup() => CreateHiringUnitPriceLookup();
        private readonly FixtureClone cloner = new();
        private readonly FixtureJson jsonUtil = new();
        private readonly FixtureLog logger = new();
        public PmcData Player { get; } = new();
        public bool FailTeammateSave;
        public bool DuplicateNickname;
        public int GenerationCount;
        public List<Item>? GeneratedItems;
        public FriendlyTeammateService(string directory)
        {
            storage = new(directory);
            saveServer = new(Player);
            Player.Inventory.Items.Add(new("money", "roubles", "stash", "grid") { Upd = new() { StackObjectsCount = 1000 } });
        }
        private BotBase GenerateNewTeammate(MongoId _, FriendlyTeammateCreateRequest request)
        {
            GenerationCount++;
            return new() { Aid = 122 + GenerationCount, Info = new() { Nickname = request.Nickname! }, Inventory = new() { Items = cloner.Clone(GeneratedItems) ?? [new("gun", "weapon", "equipment", "FirstPrimaryWeapon")] } };
        }
        private static string GetEquipmentRootId(BotBase _) => "equipment";
        private static void EnsureFollowerHasPockets(BotBase bot)
        {
            if (!bot.Inventory.Items.Any(i => i.SlotId == "Pockets"))
                bot.Inventory.Items.Add(new("pockets", "pockets", "equipment", "Pockets"));
        }
        private static void EnsureFollowerHasScabbardKnife(BotBase bot)
        {
            if (!bot.Inventory.Items.Any(i => i.SlotId == "Scabbard"))
                bot.Inventory.Items.Add(new("knife", "knife", "equipment", "Scabbard"));
        }
        private static string NormalizeLoadoutManagementMode(string mode) => mode;
        private static bool IsExtremeLoadoutManagementMode(string mode) => mode == "Extreme";
        private static GetOtherProfileResponse ToOtherProfileResponse(BotBase _) => new();
        private PmcData GetPlayerProfile(MongoId _) => Player;
        private FixtureTeammateSettings GetTeammateSettings(MongoId session, BotBase bot) =>
            storage.Read<FixtureTeammateSettings>(session, $"{bot.Aid}-settings.json") ?? new();
        private bool TryDeserializeRecruitProfile(FriendlyRecruitRequestEntry candidate, out BotBase profile)
        {
            profile = string.IsNullOrEmpty(candidate.ProfileJson) ? null! : JsonSerializer.Deserialize<BotBase>(candidate.ProfileJson)!;
            return profile != null;
        }
        private static List<Item> GetPlayerStashItems(PmcData player) => player.Inventory.Items;
        private void EnsureNicknameIsUnique(MongoId _, string name) { if (DuplicateNickname) throw new FriendlyTeammateException("duplicate"); }
        private static object CreateDefaultTeammateSettings(object _) => new();
        private void SaveTeammateWithDefaultEquipment(MongoId id, BotBase bot, bool extreme, object settings, IReadOnlyDictionary<string, string> extra)
        {
            if (FailTeammateSave) throw new IOException("Injected teammate storage failure");
            var documents = extra.ToDictionary();
            documents.Add($"{bot.Aid}.json", JsonSerializer.Serialize(bot));
            documents.Add($"{bot.Aid}-equipment.json", JsonSerializer.Serialize(bot.Inventory.Items));
            storage.Database.WriteBatch(documents);
        }
        private static void DeductRoublesFromPlayerStash(PmcData player, int amount)
        {
            var money = player.Inventory.Items.Single(i => i.Template == "roubles");
            if (money.Upd!.StackObjectsCount < amount) throw new FriendlyTeammateException("insufficient");
            money.Upd.StackObjectsCount -= amount;
        }
    }
    public sealed class FixtureStorage
    {
        public bool FailDeletion;
        public TeammateDatabase Database { get; }
        public FixtureStorage(string directory) { Database = new(directory, "eeeeeeeeeeeeeeeeeeeeeeee"); Database.Initialize(() => new Dictionary<string, string>()); }
        public T? Read<T>(MongoId _, string name) => Database.Read(name) is string json ? JsonSerializer.Deserialize<T>(json) : default;
        public void Write<T>(MongoId _, string name, T value) => Database.WriteBatch(new Dictionary<string, string> { [name] = JsonSerializer.Serialize(value) });
        public bool Exists(MongoId _, string name) => Database.Read(name) != null;
        public List<BotBase> ReadProfiles(MongoId _) => Database.ReadProfiles().Values.Select(json => JsonSerializer.Deserialize<BotBase>(json)!).ToList();
        public bool DeleteTeammate(MongoId _, int aid, IReadOnlyDictionary<string, string>? receipt = null)
        {
            if (FailDeletion) throw new IOException("Injected deletion save failure");
            return Database.DeleteTeammate(aid, receipt);
        }
    }
    public sealed class FixtureSave(PmcData player)
    {
        public bool FailNext;
        public int Saves;
        public bool IsProfileInvalidOrUnloadable(MongoId _) => false;
        public Task<long> SaveProfileAsync(MongoId _)
        {
            Saves++;
            if (FailNext) { FailNext = false; throw new IOException("Injected player save failure"); }
            if (player.Inventory.Items.Count == 0) throw new InvalidOperationException("Missing player inventory");
            return Task.FromResult(1L);
        }
    }
    public sealed class FixtureSettings { public string LoadoutManagementMode { get; set; } = "Restricted"; public FixtureSettings LoadSettings() => this; }
    public sealed class FixtureItemHelper
    {
        public Dictionary<string, double> Handbook = new();
        public Dictionary<string, double?> Market = new();
        public Dictionary<string, string> Parents = new();
        public KeyValuePair<string, FixtureTemplate> GetItem(string template) => new(template, new());
        public bool IsOfBaseclass(string template, string parent) => template == parent || Parents.GetValueOrDefault(template) == parent;
        public double? GetDynamicItemPrice(string template) => Market.TryGetValue(template, out var value) ? value : 100;
        public double GetStaticItemPrice(string template) => Handbook.GetValueOrDefault(template, 50);
    }
    public sealed class FixtureRagfairConfig { public FixtureDynamic Dynamic { get; } = new(); }
    public sealed class FixtureDynamic { public FixtureGeneratedPrices GenerateBaseFleaPrices { get; } = new(); }
    public sealed class FixtureGeneratedPrices
    {
        public bool UseHandbookPrice = true, UseHideoutCraftMultiplier, PreventPriceBeingBelowTraderBuyPrice;
        public double PriceMultiplier = 2, HideoutCraftMultiplier;
        public Dictionary<string, double> ItemTplMultiplierOverride = new(), ItemTypeMultiplierOverride = new();
    }
    public sealed class FixtureHideoutTable { public FixtureProduction Production { get; } = new(); }
    public sealed class FixtureProduction { public List<FixtureRecipe> Recipes { get; } = []; }
    public sealed class FixtureRecipe { public List<FixtureRequirement> Requirements { get; } = []; }
    public sealed class FixtureRequirement { public string Type { get; set; } = "Item"; public string TemplateId { get; set; } = ""; }
    public sealed class FixtureTraderHelper { public double Floor; public double GetHighestSellToTraderPrice(string _) => Floor; }
    public sealed class FixtureTemplate { public FixtureProperties Properties { get; } = new(); }
    public sealed class FixtureProperties { public List<FixtureSlot> Slots { get; } = []; }
    public record FixtureSlot(string Name);
    public sealed class FixtureClone { public T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!; }
    public sealed class FixtureJson { public string Serialize<T>(T value) => JsonSerializer.Serialize(value); }
    public sealed class FixtureLog { public void Info(string _) {} public void Warning(string _) {} }
}
