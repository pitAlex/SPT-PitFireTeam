using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

internal static partial class Program
{
    private static int checks;
    public static int Main(string[] args)
    {
        string runtime = Path.GetFullPath(args.Single());
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(runtime, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, name.Name + ".dll")) : null;
        return Run(runtime);
    }

    private static T Construct<T>(Dictionary<string, object?> dependencies) => (T)typeof(T).GetConstructors().Single()
        .Invoke(typeof(T).GetConstructors().Single().GetParameters().Select(p => dependencies.GetValueOrDefault(p.Name!)).ToArray());
    private static object? Private(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Logger<T>() => DispatchProxy.Create<T, TestLogger>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); checks++; }
    private static void Refuses(Action action, string key)
    {
        try { action(); }
        catch (FriendlyTeammateException ex) { Check(ex.Message == key, "expected " + key + ", got " + ex.Message); return; }
        throw new Exception("Expected rejection: " + key);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string runtime)
    {
        string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "teammate-treatment", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(work, "user", "profiles"));
        string previousDirectory = Environment.CurrentDirectory;
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        var globals = json.Deserialize<GlobalTable>(File.ReadAllText(Path.Combine(runtime, "SPT_Data", "database", "globals.json")))!;
        var therapist = json.Deserialize<TraderBase>(File.ReadAllText(Path.Combine(runtime, "SPT_Data", "database", "traders", Traders.THERAPIST.ToString(), "base.json")))!;
        var config = json.Deserialize<CoreConfig>(File.ReadAllText(Path.Combine(runtime, "SPT_Data", "configs", "core.json")))!;
        var healthConfig = json.Deserialize<HealthConfig>(File.ReadAllText(Path.Combine(runtime, "SPT_Data", "configs", "health.json")))!;
        var settings = Construct<FriendlyServerSettingsService>(new() { ["logger"] = Logger<ISptLogger<FriendlyServerSettingsService>>() });
        var storage = new FriendlyTeammateStorage(new FileUtil(), json, Logger<ISptLogger<FriendlyTeammateStorage>>(), settings);
        var save = Construct<SaveServer>(new()
        {
            ["fileUtil"] = new FileUtil(), ["saveLoadRouters"] = Array.Empty<SPTarkov.Server.Core.DI.SaveLoadRouter>(),
            ["jsonUtil"] = json, ["hashUtil"] = new HashUtil(null!), ["coreConfig"] = config, ["logger"] = Logger<ISptLogger<SaveServer>>()
        });
        var helper = Construct<ProfileHelper>(new() { ["saveServer"] = save });
        var traders = new TradersTable { [Traders.THERAPIST] = new() { Base = therapist, Assort = null!, Dialogue = null!, QuestAssort = null! } };
        var traderHelper = Construct<TraderHelper>(new() { ["globalTable"] = globals, ["traderTable"] = traders });
        var cloner = new FastCloner();
        var language = new FriendlyLanguageService(Logger<ISptLogger<FriendlyLanguageService>>());
        var service = Construct<FriendlyTeammateService>(new()
        {
            ["storage"] = storage, ["jsonUtil"] = json, ["profileHelper"] = helper, ["saveServer"] = save,
            ["fileUtil"] = new FileUtil(), ["globalTable"] = globals, ["traderHelper"] = traderHelper,
            ["cloner"] = cloner, ["settingsService"] = settings, ["logger"] = Logger<ISptLogger<FriendlyTeammateService>>(),
            ["healthConfig"] = healthConfig, ["languageService"] = language,
            ["teammateInsuranceService"] = Construct<FriendlyTeammateInsuranceService>(new() { ["settingsService"] = settings })
        });
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "user/mods/pitFireTeam-ServerMod/Resources/settings.json");
        byte[]? oldSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        try
        {
            Environment.CurrentDirectory = work;
            settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
            var session = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
            storage.InitializeProfile(session);
            var health = new BotBaseHealth { BodyParts = new(), UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
                health.BodyParts[part] = new() { Health = new() { Current = 20, Maximum = 60, Minimum = 0 } };
            var teammate = new BotBase { Aid = 123, Id = new MongoId(), Health = health, Inventory = new() { Items = [] } };
            storage.Write(session, "123.json", teammate);
            storage.Write(session, "124.json", new BotBase { Aid = 124, Health = cloner.Clone(health) });
            string other = json.Serialize(storage.Read<BotBase>(session, "124.json"))!;
            var stashId = new MongoId();
            var money = new Item { Id = new MongoId(), Template = Money.ROUBLES, ParentId = stashId.ToString(), SlotId = "hideout", Upd = new() { StackObjectsCount = 100000 } };
            var player = new PmcData
            {
                Info = new() { Side = "Usec", Level = 33, Experience = 1454315 }, Health = cloner.Clone(health),
                Inventory = new() { Stash = stashId, Items = [new() { Id = stashId, Template = new MongoId() }, money] },
                TradersInfo = new() { [Traders.THERAPIST] = new() { Unlocked = true, LoyaltyLevel = 2, SalesSum = 200000, Standing = 0.4 } },
                Skills = new() { Common = [] }
            };
            var full = new SptProfile { ProfileInfo = new(), CharacterData = new() { PmcData = player } };
            TestRecovery(player, globals, cloner);
            var profiles = (ConcurrentDictionary<MongoId, SptProfile>)typeof(SaveServer).GetField("profiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(save)!;
            profiles[session] = full;
            save.SaveProfileAsync(session).GetAwaiter().GetResult();
            string playerHealth = json.Serialize(player.Health)!;
            var quote = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            Check(quote.Price > 0 && quote.QuoteToken != null, "server quotes paid HP using native tables");
            Check(money.Upd.StackObjectsCount == 100000 && FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280,
                "quoting has no currency or HP side effects");
            int expected = FriendlyTeammateTreatmentPricing.Calculate(280, 5, 80, 2, 10, 0.005f, 0.005f, false);
            Check(expected == 1046, "native loyalty plus Charisma formula and final ceiling");
            Check(FriendlyTeammateTreatmentPricing.Calculate(280, 5, 80, 4, 51, 0.005f, 0.005f, true) == 0, "native free treatment eligibility overrides price");
            Refuses(() => service.HealTeammate(session, new() { Aid = "124", QuoteToken = quote.QuoteToken }), "ProfileHealingQuoteExpired");
            var request = new FriendlyTeammateTreatmentRequest { Aid = "123", QuoteToken = quote.QuoteToken };
            var paid = service.HealTeammate(session, request);
            int remaining = (int)player.Inventory!.Items!.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount!;
            Check(remaining == 100000 - quote.Price && FriendlyTeammateTreatmentPricing.MissingHealth(paid.Health) == 0, "one charge restores real saved teammate HP");
            Check(player.TradersInfo[Traders.THERAPIST].SalesSum == 200000 + quote.Price, "payment counts toward Therapist turnover");
            service.HealTeammate(session, request);
            Check((int)player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount! == remaining, "duplicate completed token cannot charge twice");
            Check(json.Serialize(player.Health) == playerHealth && json.Serialize(storage.Read<BotBase>(session, "124.json")) == other, "player health and other teammate stay unchanged");
            var saved = json.Deserialize<SptProfile>(File.ReadAllText(Path.Combine("user", "profiles", session + ".json")))!;
            Check(saved.CharacterData!.PmcData!.Inventory!.Items!.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount == remaining,
                "rouble deduction is saved to player JSON");

            // Simulate restart after player save but before the teammate/receipt batch.
            var journal = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
            storage.Write(session, "123.json", teammate);
            journal.State = "paying";
            storage.Write(session, "pending-treatment.json", journal);
            service.RecoverTeammateTreatment(session);
            Check(storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!.State == "complete"
                && FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 0,
                "charged intent recovers teammate HP and receipt together without another debit");

            storage.Write(session, "123.json", teammate);
            var next = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount = 0;
            Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = next.QuoteToken }), "ProfileHealingInsufficientFunds");
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280, "insufficient funds never grants HP");
            var pending = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
            pending.State = "paying";
            pending.PlayerBefore = (string)Private(service, "TreatmentPlayerSignature", player)!;
            pending.PlayerAfter = "never-saved-payment";
            storage.Write(session, "pending-treatment.json", pending);
            service.RecoverTeammateTreatment(session);
            Check(storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!.State == "pending", "unpaid intent returns to pending without treatment");
            pending.PlayerBefore = "conflicting-player-state";
            storage.Write(session, "pending-treatment.json", pending);
            Refuses(() => service.RecoverTeammateTreatment(session), "ProfileHealingRecoveryFailed");
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280, "conflicting recovery fails closed");
            pending.State = "pending";
            pending.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
            storage.Write(session, "pending-treatment.json", pending);
            Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = pending.Token }), "ProfileHealingQuoteExpired");

            player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount = 100000;
            player.Info.Level = 33;
            var stale = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            var changed = cloner.Clone(teammate)!;
            changed.Health!.BodyParts!["Head"].Health!.Current = 21;
            storage.Write(session, "123.json", changed);
            Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = stale.QuoteToken }), "ProfileHealingQuoteExpired");
            storage.Write(session, "123.json", teammate);

            player.Info.Level = 1;
            globals.Configuration.Health.HealPrice.TrialLevels = 5;
            globals.Configuration.Health.HealPrice.TrialRaids = 30;
            var trial = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            Check(trial.Price == 0, "free-treatment eligibility belongs to the paying player");
            service.HealTeammate(session, new() { Aid = "123", QuoteToken = trial.QuoteToken });
            Check(player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount == 100000
                && FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 0,
                "free treatment restores saved teammate HP without taking roubles");

            // Force a native player-file write failure after journaling the payment.
            storage.Write(session, "123.json", teammate);
            player.Info.Level = 33;
            var failing = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            string playerFile = Path.Combine("user", "profiles", session + ".json");
            save.SaveProfileAsync(session).GetAwaiter().GetResult();
            File.Move(playerFile, playerFile + ".before-failure");
            Directory.CreateDirectory(playerFile);
            Refuses(() => service.HealTeammate(session, new() { Aid = "123", QuoteToken = failing.QuoteToken }), "ProfileHealingRecoveryFailed");
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 280,
                "failed player save never grants healing");
            Directory.Delete(playerFile);
            File.Move(playerFile + ".before-failure", playerFile);
            Refuses(() => service.RecoverTeammateTreatment(session), "ProfileHealingRecoveryFailed");
            Check(storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!.State == "paying",
                "native cached save hash cannot falsely complete treatment after a failed write");
            ((ConcurrentDictionary<MongoId, string>)typeof(SaveServer).GetField("saveMd5", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(save)!).Clear();
            service.RecoverTeammateTreatment(session);
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(storage.Read<BotBase>(session, "123.json")!.Health) == 0
                && player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount == 100000 - failing.Price,
                "recovery after resetting the native save cache grants HP after exactly one verified debit");

            storage.Write(session, "123.json", teammate);
            var recoveringQuote = service.GetTeammateHealth(session, new() { Aid = "123" }, true);
            var recoveryJournal = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
            var injury = json.Deserialize<BotBaseHealth>(recoveryJournal.HealthBefore)!;
            injury.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
            recoveryJournal.HealthBefore = json.Serialize(injury)!;
            storage.Write(session, "pending-treatment.json", recoveryJournal);
            var injuredProfile = cloner.Clone(teammate)!;
            injuredProfile.Health = cloner.Clone(injury);
            for (double tick = injury.UpdateTime.Value + 3; tick <= injury.UpdateTime.Value + 60; tick += 3)
                FriendlyTeammateRegeneration.Advance(injuredProfile.Health!, FriendlyTeammateRegeneration.Rates(player, globals), 3, tick);
            storage.Write(session, "123.json", injuredProfile);
            var recoveredRead = service.GetTeammateHealth(session, new() { Aid = "123" }, false);
            Check(recoveredRead.InsurancePlayerRoubles == null && recoveredRead.PlayerStashItems == null,
                "recovery poll carries no full stash or payment snapshot");
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(recoveredRead.Health) < 280, "closed-profile elapsed recovery is actually saved");
            int fundsBeforeRecovery = (int)player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount!;
            service.HealTeammate(session, new() { Aid = "123", QuoteToken = recoveringQuote.QuoteToken });
            var completedRecovery = storage.Read<FriendlyTeammateTreatmentJournal>(session, "pending-treatment.json")!;
            Check(completedRecovery.Price < recoveringQuote.Price && completedRecovery.State == "complete",
                "a recovery poll between quote and payment remains valid and lowers the charged price");
            Check(player.Inventory.Items.Single(i => i.Template == Money.ROUBLES).Upd!.StackObjectsCount == fundsBeforeRecovery - completedRecovery.Price,
                "payment charges the recovered HP cost rather than the stale maximum quote");
            health.BodyParts["LeftLeg"].Effects = new() { ["Fracture"] = new() };
            Refuses(() => FriendlyTeammateTreatmentPricing.MissingHealth(health), "ProfileHealingUnavailable");
            TestRaidHealth(service, storage, settings, json, session, player, healthConfig);
            Console.WriteLine($"PASS: {checks} paid teammate healing checks. {work}");
            return 0;
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            if (oldSettings != null) File.WriteAllBytes(settingsPath, oldSettings);
        }
    }

    private static void TestRecovery(PmcData player, GlobalTable globals, FastCloner cloner)
    {
        var originalBonuses = player.Bonuses;
        var originalSkills = player.Skills!.Common;
        double originalBoost = globals.Configuration.SkillsSettings.HideoutManagement.SkillBoostPercent;
        try
        {
            player.Bonuses = [];
            player.Skills.Common = [];
            var baseRates = FriendlyTeammateRegeneration.Rates(player, globals);
            double loop = FriendlyTeammateRegeneration.LoopSeconds(globals);
            float nativeHeadLoop = (float)globals.Configuration.Health.Effects.Regeneration.BodyHealth.Head.Value * (float)loop / 60f;
            Check(Math.Abs(baseRates["Head"] - nativeHeadLoop / loop) < 1e-9, "base rate mirrors native per-part per-loop float arithmetic");
            Check(baseRates["Chest"] != baseRates["Head"], "body parts retain their distinct native rates");
            var bonusId = new MongoId();
            player.Bonuses = [new() { Id = bonusId, Type = BonusType.HealthRegeneration, IsPassive = true, Value = 100 },
                new() { Id = bonusId, Type = BonusType.HealthRegeneration, IsPassive = true, Value = 100 }];
            player.Skills.Common = [new() { Id = SkillTypes.HideoutManagement, Progress = 2000 }];
            globals.Configuration.SkillsSettings.HideoutManagement.SkillBoostPercent = 0.5;
            var boosted = FriendlyTeammateRegeneration.Rates(player, globals);
            Check(Math.Abs(boosted["Head"] / baseRates["Head"] - 2.1) < 1e-6,
                "positive combined hideout bonus gets player Hideout Management boost and duplicate IDs count once");
            player.Bonuses = [new() { Id = bonusId, Type = BonusType.HealthRegeneration, IsPassive = true, Value = -10 }];
            Check(Math.Abs(FriendlyTeammateRegeneration.Rates(player, globals)["Head"] / baseRates["Head"] - 0.9) < 1e-6,
                "negative combined bonus is not amplified by Hideout Management");
            player.Bonuses[0].IsPassive = false;
            Check(FriendlyTeammateRegeneration.Rates(player, globals)["Head"] == baseRates["Head"], "unpowered non-passive hideout bonus is excluded");
            var hp = cloner.Clone(player.Health)!;
            hp.UpdateTime = 1000;
            Check(!FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, 1000 + loop - 0.1), "sub-loop time cannot grant an early recovery tick");
            Check(FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, 1000 + loop * 2 + 0.5)
                && Math.Abs(hp.BodyParts!["Head"].Health!.Current!.Value - (20 + baseRates["Head"] * loop * 2)) < 1e-9,
                "elapsed recovery grants exactly completed native loops");
            Check(hp.UpdateTime == 1000 + loop * 2, "checkpoint retains the unfinished-loop remainder");
            var once = cloner.Clone(player.Health)!;
            once.UpdateTime = 1000;
            FriendlyTeammateRegeneration.Advance(once, baseRates, loop, 1000 + loop * 10);
            for (int tick = 3; tick <= 10; tick++) FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, 1000 + loop * tick);
            Check(Math.Abs(once.BodyParts!["Head"].Health!.Current!.Value - hp.BodyParts!["Head"].Health!.Current!.Value) < 1e-9,
                "reopening catch-up and repeated online polls grant the same elapsed healing");
            double saved = hp.BodyParts!["Head"].Health!.Current!.Value;
            Check(!FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, hp.UpdateTime!.Value)
                && hp.BodyParts["Head"].Health!.Current == saved, "repeated snapshot cannot heal the same time twice");
            hp.UpdateTime = 0;
            FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, 1000);
            Check(hp.UpdateTime == 1000 && hp.BodyParts["Head"].Health!.Current == saved, "legacy zero timestamps initialize without granting decades of recovery");
            hp.UpdateTime = 2000;
            FriendlyTeammateRegeneration.Advance(hp, baseRates, loop, 1000);
            Check(hp.UpdateTime == 1000 && hp.BodyParts["Head"].Health!.Current == saved, "future checkpoint clamps safely without granting HP");
            FriendlyTeammateRegeneration.Advance(hp, boosted, loop, 1000000);
            Check(FriendlyTeammateTreatmentPricing.MissingHealth(hp) == 0, "long elapsed recovery caps every body part at its own maximum");
            hp.BodyParts["Head"].Health!.Current = 20;
            hp.BodyParts["Head"].Effects = new() { ["HeavyBleeding"] = new() };
            FriendlyTeammateRegeneration.Advance(hp, boosted, loop, 2000000);
            Check(hp.BodyParts["Head"].Health!.Current == 20 && hp.BodyParts["Head"].Effects!.ContainsKey("HeavyBleeding"),
                "unsupported effects are neither healed through nor silently removed");
        }
        finally
        {
            player.Bonuses = originalBonuses;
            player.Skills.Common = originalSkills;
            globals.Configuration.SkillsSettings.HideoutManagement.SkillBoostPercent = originalBoost;
        }
    }
}

public class TestLogger : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.ReturnType == typeof(bool) ? false : null;
}
