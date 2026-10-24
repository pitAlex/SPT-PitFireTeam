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
namespace SPTarkov.Server.Core.Models.Eft.Common
{
    public record PmcData { public Inventory Inventory { get; set; } = new(); public List<FixturePolicy>? InsuredItems { get; set; } = []; }
    public record FixturePolicy { public MongoId? ItemId { get; set; } }
}
namespace SPTarkov.Server.Core.Models.Eft.Profile
{
    public record GetOtherProfileResponse;
    public record SptProfile
    {
        public FixtureCharacters? CharacterData { get; set; }
        public Spt? SptData { get; set; }
        public List<Insurance>? InsuranceList { get; set; } = [];
        public Dictionary<string, Dialogue>? DialogueRecords { get; set; }
    }
    public record Spt { public Dictionary<string, long>? Migrations { get; set; } }
    public record Insurance { public List<Item>? Items { get; set; } }
    public record Dialogue
    {
        public MongoId Id { get; set; } = new();
        public SPTarkov.Server.Core.Models.Enums.MessageType? Type { get; set; }
        public bool? Pinned { get; set; }
        public int? New { get; set; }
        public int? AttachmentsNew { get; set; }
        public List<Message>? Messages { get; set; }
    }
    public record Message
    {
        public MongoId Id { get; set; } = new();
        public MongoId UserId { get; set; } = new();
        public SPTarkov.Server.Core.Models.Enums.MessageType? MessageType { get; set; }
        public long? DateTime { get; set; }
        public string? Text { get; set; }
        public bool? HasRewards { get; set; }
        public bool? RewardCollected { get; set; }
        public long? MaxStorageTime { get; set; }
        public MessageItems? Items { get; set; }
    }
    public record MessageItems { public MongoId? Stash { get; set; } public List<Item>? Data { get; set; } }
    public record FixtureCharacters { public PmcData? PmcData { get; set; } }
}
namespace SPTarkov.Server.Core.Models.Utils { public interface IRequestData; }
namespace SPTarkov.Server.Core.Models.Enums
{
    public enum EquipmentSlots { Pockets, Scabbard, SecuredContainer }
    public enum MessageType { NpcTraderMessage = 2 }
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
    public record FriendlyRecruitRequestEntry { public string ProfileId { get; set; } = ""; public string AccountId { get; set; } = ""; public string ProfileJson { get; set; } = ""; public int? RecruitmentGearPrice { get; set; } }
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
        public readonly FixtureFile fileUtil;
        public readonly FixtureSettings settingsService = new();
        public readonly FixtureItemHelper itemHelper = new();
        public readonly FixtureRagfairConfig ragfairConfig = new();
        public readonly FixtureHideoutTable hideoutTable = new();
        public readonly FixtureTraderHelper traderHelper = new();
        public readonly FixtureProfileHelper profileHelper;
        public readonly FixtureLanguage languageService = new();
        public readonly FixtureDiagnostics insuranceDiagnostics = new();
        public readonly FixtureNotifier notifierHelper = new();
        public readonly FixtureNotifications notificationSendHelper = new();
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
            saveServer = new(Player, directory);
            profileHelper = new(saveServer);
            fileUtil = new(saveServer);
            Player.Inventory.Items.Add(new("money", "roubles", "stash", "grid") { Upd = new() { StackObjectsCount = 1000 } });
            saveServer.InitializeDisk();
        }
        private BotBase GenerateNewTeammate(MongoId _, FriendlyTeammateCreateRequest request)
        {
            GenerationCount++;
            return new() { Aid = 122 + GenerationCount, Info = new() { Nickname = request.Nickname! }, Inventory = new() { Items = cloner.Clone(GeneratedItems) ?? [new("gun", "weapon", "equipment", "FirstPrimaryWeapon")] } };
        }
        public int RecruitCreations;
        public int FailRecruitCreationOn;
        public int GetRecruitAccountIdOrUnique(MongoId session, string? value)
        {
            if (int.TryParse(value, out int aid) && !storage.Exists(session, $"{aid}.json")) return aid;
            int next = 800;
            while (storage.Exists(session, $"{next}.json")) next++;
            return next;
        }
        public void CreateTeammateFromRecruitCandidate(MongoId session, FriendlyRecruitRequestEntry candidate,
            IReadOnlyDictionary<string, string> additionalDocuments)
        {
            RecruitCreations++;
            if (RecruitCreations == FailRecruitCreationOn) throw new IOException("Injected acceptance failure");
            int aid = int.Parse(candidate.AccountId);
            var documents = additionalDocuments.ToDictionary();
            documents[$"{aid}.json"] = JsonSerializer.Serialize(new BotBase { Id = new(candidate.ProfileId), Aid = aid });
            documents[$"{aid}-settings.json"] = JsonSerializer.Serialize(new FixtureTeammateSettings { RecruitmentGearPrice = candidate.RecruitmentGearPrice });
            documents[$"{aid}-equipment.json"] = JsonSerializer.Serialize(new[] { new Item(candidate.ProfileId+"-gun", "weapon", "equipment", "FirstPrimaryWeapon") });
            storage.Database.WriteBatch(documents);
        }
        private static string GetEquipmentRootId(BotBase _) => "equipment";
        private List<BotBase> LoadTeammates(MongoId session) => storage.ReadProfiles(session);
        private static HashSet<string> GetItemTreeIds(List<Item> source, string id)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddItemAndDescendantsToKeepSet(source, id, ids);
            return ids;
        }
        private static bool IsIgnoredKitRequirementItem(Item item) => string.IsNullOrWhiteSpace(item.ParentId)
            || string.Equals(item.SlotId, "Dogtag", StringComparison.OrdinalIgnoreCase) || IsPocketsSlotItem(item);
        private bool IsCurrentLoadoutManagementModeExtreme() => IsExtremeLoadoutManagementMode(settingsService.LoadoutManagementMode);
        private static string GetLanguageValue(Dictionary<string,string> values, string key, string fallback) => values.GetValueOrDefault(key, fallback);
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
        public bool FailDeliveryCompletion;
        public TeammateDatabase Database { get; }
        public FixtureStorage(string directory) { Database = new(directory, "eeeeeeeeeeeeeeeeeeeeeeee"); Database.Initialize(() => new Dictionary<string, string>()); }
        public T? Read<T>(MongoId _, string name) => Database.Read(name) is string json ? JsonSerializer.Deserialize<T>(json) : default;
        public void Write<T>(MongoId _, string name, T value)
        {
            if (FailDeliveryCompletion && value is FriendlyTeammateDeletionJournal { State: "complete", Delivery: not null })
                throw new IOException("Injected courier receipt failure");
            Database.WriteBatch(new Dictionary<string, string> { [name] = JsonSerializer.Serialize(value) });
        }
        public void WriteBatch(MongoId _, IReadOnlyDictionary<string, string> documents) => Database.WriteBatch(documents);
        public bool Exists(MongoId _, string name) => Database.Read(name) != null;
        public List<BotBase> ReadProfiles(MongoId _) => Database.ReadProfiles().Values.Select(json => JsonSerializer.Deserialize<BotBase>(json)!).ToList();
        public bool DeleteTeammate(MongoId _, int aid, IReadOnlyDictionary<string, string>? receipt = null)
        {
            if (FailDeletion) throw new IOException("Injected deletion save failure");
            return Database.DeleteTeammate(aid, receipt);
        }
    }
    public sealed class FixtureSave(PmcData player, string directory)
    {
        public SptProfile Profile { get; } = new() { CharacterData = new() { PmcData = player } };
        public bool FailNext, SkipCachedWrites;
        public HashSet<int> FailOnSaves = [];
        public int Saves;
        public string? Cached;
        public string SavePath => Path.Combine(directory, "player.json");
        public void InitializeDisk() => File.WriteAllText(SavePath, Serialize());
        private string Serialize() => JsonSerializer.Serialize(Profile);
        public SptProfile DiskProfile => JsonSerializer.Deserialize<SptProfile>(File.ReadAllText(SavePath))!;
        public int DiskMoney => JsonSerializer.Deserialize<SptProfile>(File.ReadAllText(SavePath))!.CharacterData!.PmcData!.Inventory.Items
            .Where(item => item.Template == "roubles").Sum(item => item.Upd!.StackObjectsCount);
        public void Restart()
        {
            var saved = DiskProfile;
            player.Inventory = saved.CharacterData!.PmcData!.Inventory;
            player.InsuredItems = saved.CharacterData.PmcData.InsuredItems;
            Profile.SptData = saved.SptData;
            Profile.DialogueRecords = saved.DialogueRecords;
            Profile.InsuranceList = saved.InsuranceList;
            Cached = null;
        }
        public bool IsProfileInvalidOrUnloadable(MongoId _) => false;
        public Task<long> SaveProfileAsync(MongoId _)
        {
            Saves++;
            string current = Serialize();
            bool skip = SkipCachedWrites && Cached == current;
            Cached = current; // Match SPT's hash update before the attempted write.
            if (FailNext || FailOnSaves.Remove(Saves)) { FailNext = false; throw new IOException("Injected player save failure"); }
            if (!skip) File.WriteAllText(SavePath, current);
            return Task.FromResult(1L);
        }
    }
    public sealed class FixtureFile(FixtureSave save)
    {
        public string ReadFile(string _) => File.ReadAllText(save.SavePath);
    }
    public sealed class FixtureSettings { public bool IsAllegiance { get; set; } public string LoadoutManagementMode { get; set; } = "Restricted"; public FixtureSettings LoadSettings() => this; }
    public sealed class FixtureItemHelper
    {
        public Dictionary<string, double> Handbook = new();
        public Dictionary<string, double?> Market = new();
        public Dictionary<string, string> Parents = new();
        public KeyValuePair<string, FixtureTemplate> GetItem(string template) => new(template, new());
        public bool IsOfBaseclass(string template, string parent) => template == parent || Parents.GetValueOrDefault(template) == parent;
        public double? GetDynamicItemPrice(string template) => Market.TryGetValue(template, out var value) ? value : 100;
        public double GetStaticItemPrice(string template) => Handbook.GetValueOrDefault(template, 50);
        public IEnumerable<Item> ReplaceIDs(List<Item> items, PmcData? _)
        {
            var map = items.ToDictionary(item => item.Id, _ => new MongoId().ToString());
            return items.Select(item => item with { Id = map[item.Id], ParentId = item.ParentId != null && map.TryGetValue(item.ParentId, out var parent) ? parent : item.ParentId });
        }
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
    public sealed class FixtureJson { public string Serialize<T>(T value) => JsonSerializer.Serialize(value); public T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value); }
    public sealed class FixtureLog { public void Info(string _) {} public void Warning(string _) {} }
    public static class FriendlyCourierTraderProfile { public static readonly MongoId CourierTraderId = new("dddddddddddddddddddddddd"); }
    public sealed class FixtureProfileHelper(FixtureSave save) { public SptProfile GetFullProfile(MongoId _) => save.Profile; }
    public sealed class FixtureLanguage
    {
        public Dictionary<string, string> GetStringMap(MongoId _, string __) => new() { ["RemovedTeammateEquipmentDelivery"] = "Equipment from the removed teammate is ready for pickup." };
    }
    public sealed class FixtureDiagnostics
    {
        public bool FailNext;
        public HashSet<string> CourierIds = [];
        public void ObserveMemberRemovalCourier(MongoId _, IEnumerable<string> ids)
        {
            if (FailNext) { FailNext = false; throw new IOException("Injected insurance evidence failure"); }
            CourierIds.UnionWith(ids);
        }
    }
    public sealed class FixtureNotifier { public Message CreateNewMessageNotification(Message message) => message; }
    public sealed class FixtureNotifications
    {
        public bool FailNext;
        public List<Message> Messages = [];
        public Task SendMessageAsync(MongoId _, Message message)
        {
            if (FailNext) { FailNext = false; return Task.FromException(new IOException("Injected websocket failure")); }
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
