using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using LiteDB;
using pitTeam.Server.Models;
using pitTeam.Server.Persistence;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

internal static class Program
{
    private static int checks;
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--convert-sqlite-snapshot")
            return TrialDatabaseConversion.Run(args[1], args[2]);
        if (args.Length != 2) throw new ArgumentException("Usage: <SPT runtime root> <legacy profile directory>");
        string runtime = Path.GetFullPath(args[0]);
        string legacy = Path.GetFullPath(args[1]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(runtime, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        return Run(legacy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string legacy)
    {
        string initialDirectory = Environment.CurrentDirectory;
        string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "follower-database", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work);
        try
        {
            StorageConcurrencyTests.Run(work, Check);
            TestDatabase(work);
            TestLegacyAdapter(work, legacy);
            Console.WriteLine($"PASS: {checks} storage checks. Test artifacts: {work}");
            return 0;
        }
        finally { Environment.CurrentDirectory = initialDirectory; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch { checks++; return; }
        throw new Exception("FAIL: expected error: " + message);
    }

    private static void EditDatabase(string path, Action<LiteDatabase> edit)
    {
        using var database = new LiteDatabase(path);
        edit(database);
    }

    private static void TestDatabase(string work)
    {
        const string id = "aaaaaaaaaaaaaaaaaaaaaaaa";
        var database = new TeammateDatabase(Path.Combine(work, "unit"), id);
        Check(database.Initialize(() => new Dictionary<string, string>()) == 0, "empty initialization");
        Check(File.Exists(database.DatabasePath) && database.ReadProfiles().Count == 0, "empty profile database exists");
        database.WriteBatch(new Dictionary<string, string>
        {
            ["10.json"] = "{\"nickname\":\"SecretFollowerName\"}",
            ["10-settings.json"] = "{\"aggression\":30}",
            ["10-equipment.json"] = "[{\"_id\":\"SecretItemId\"}]",
        });
        Check(database.ReadProfiles().Count == 1, "settings/equipment excluded from roster");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(database.DatabasePath)).Contains("SecretFollowerName"), "no profile plaintext");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(database.DatabasePath)).Contains("SecretItemId"), "no equipment plaintext");
        Throws(() => database.WriteBatch(new Dictionary<string, string>
        {
            ["10-settings.json"] = "{\"aggression\":99}", ["failure"] = null!
        }), "failed batch rolls back");
        Check(database.Read("10-settings.json") == "{\"aggression\":30}", "failed batch preserved previous value");

        database.WriteBatch(new Dictionary<string, string> { ["10-settings.json"] = "{\"aggression\":45}" }, backupPrevious: true);
        Check(database.Read("10-settings.json") == "{\"aggression\":45}", "recovery correction saved");
        string backupKey;
        using (var raw = new LiteDatabase(database.DatabasePath))
        {
            backupKey = raw.GetCollection("documents").FindAll()
                .Select(document => document["_id"].AsString).Single(name => name.StartsWith("recovery/"));
        }
        Check(database.Read(backupKey) == "{\"aggression\":30}", "encrypted recovery snapshot retained");

        var reopened = new TeammateDatabase(Path.Combine(work, "unit"), id);
        reopened.Initialize(() => throw new Exception("Must not read legacy JSONs after initialization"));
        Check(reopened.Read("10-settings.json") == "{\"aggression\":45}", "restart persisted edits");
        Check(reopened.DeleteTeammate(10), "delete stored teammate");
        Check(reopened.Read("10-settings.json") == null && reopened.Read("10-equipment.json") == null, "delete removes all active documents");
        reopened.Initialize(() => throw new Exception("Must not resurrect deleted teammate"));
        Check(reopened.ReadProfiles().Count == 0, "deleted teammate stays deleted after initialization");

        Parallel.For(20, 40, aid => reopened.WriteBatch(new Dictionary<string, string> { [$"{aid}.json"] = "{}" }));
        Check(reopened.ReadProfiles().Count == 20, "concurrent writes persisted");
        EditDatabase(reopened.DatabasePath, raw => raw.GetCollection("documents").Update(
            new BsonDocument { ["_id"] = "20.json", ["payload"] = new byte[40] }));
        Throws(() => reopened.ReadProfiles(), "modified ciphertext is rejected");
        Throws(() => reopened.WriteBatch(new Dictionary<string, string> { ["20.json"] = "{}" }), "cannot overwrite corrupted record");
        Throws(() => reopened.Initialize(() => new Dictionary<string, string>()), "corruption does not trigger JSON fallback");

        var large = new TeammateDatabase(Path.Combine(work, "large"), id);
        large.Initialize(() => new Dictionary<string, string>());
        string largeJson = new string('x', 2 * 1024 * 1024);
        large.WriteBatch(new Dictionary<string, string> { ["recruit-requests.json"] = largeJson });
        Check(large.Read("recruit-requests.json") == largeJson, "large recruit payload round trip");
        EditDatabase(large.DatabasePath, raw => raw.UserVersion = 0);
        Check(large.Initialize(() => throw new Exception("Committed import must not run again")) == 0,
            "recover interrupted version update without reimporting JSONs");

        var partial = new TeammateDatabase(Path.Combine(work, "partial"), id);
        Throws(() => partial.Initialize(() => new Dictionary<string, string> { ["10.json"] = "{}", ["bad"] = null! }), "interrupted import");
        Check(partial.ReadProfiles().Count == 0, "failed import has no partial roster");
        Check(partial.Initialize(() => new Dictionary<string, string> { ["10.json"] = "{}" }) == 1, "failed import is retryable");
        const string otherId = "bbbbbbbbbbbbbbbbbbbbbbbb";
        File.Copy(partial.DatabasePath, Path.Combine(work, "partial", otherId + ".db"));
        Throws(() => new TeammateDatabase(Path.Combine(work, "partial"), otherId).Initialize(() => new Dictionary<string, string>()),
            "database tied to owning profile id");
        var brokenSchema = new TeammateDatabase(Path.Combine(work, "broken-schema"), id);
        brokenSchema.Initialize(() => new Dictionary<string, string>());
        EditDatabase(brokenSchema.DatabasePath, raw => raw.DropCollection("documents"));
        Throws(() => brokenSchema.Initialize(() => new Dictionary<string, string>()), "missing schema does not trigger JSON fallback");
        var futureSchema = new TeammateDatabase(Path.Combine(work, "future-schema"), id);
        futureSchema.Initialize(() => new Dictionary<string, string>());
        EditDatabase(futureSchema.DatabasePath, raw => raw.UserVersion = 99);
        Throws(() => futureSchema.Initialize(() => new Dictionary<string, string>()), "unknown schema version fails safely");

        File.Delete(partial.DatabasePath);
        Throws(() => partial.WriteBatch(new Dictionary<string, string> { ["10.json"] = "{}" }), "missing database not recreated during writes");
        Check(!File.Exists(partial.DatabasePath), "failed write did not create database");
    }

    private static void TestLegacyAdapter(string work, string legacy)
    {
        var jsonUtil = new JsonUtil([new SptJsonConverterRegistrator()]);
        var logger = DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>();
        string profileId = Path.GetFileName(legacy.TrimEnd(Path.DirectorySeparatorChar));
        if (profileId.EndsWith(".backup", StringComparison.Ordinal)) profileId = profileId[..^7];
        var session = new MongoId(profileId);
        var originalHashes = Directory.GetFiles(legacy).ToDictionary(path => Path.GetFileName(path)!, Hash);
        string sandbox = Path.Combine(work, "adapter");
        string root = Path.Combine(sandbox, "user", "mods", "pitFireTeam-ServerMod", "Resources", "teammates");
        string importedDirectory = Path.Combine(root, profileId);
        string backupDirectory = importedDirectory + ".backup";
        Directory.CreateDirectory(importedDirectory);
        foreach (string path in Directory.GetFiles(legacy)) File.Copy(path, Path.Combine(importedDirectory, Path.GetFileName(path)));
        // A whole-folder rename must preserve unrelated files and nested recovery backups too.
        Directory.CreateDirectory(Path.Combine(importedDirectory, "recovery"));
        File.WriteAllText(Path.Combine(importedDirectory, "recovery", "note.txt"), "keep this backup");
        Environment.CurrentDirectory = sandbox;
        var storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        storage.InitializeAllProfiles([session]);
        Check(!Directory.Exists(importedDirectory) && Directory.Exists(backupDirectory),
            "successful import renames the original folder to .backup");
        Check(File.ReadAllText(Path.Combine(backupDirectory, "recovery", "note.txt")) == "keep this backup",
            "whole-folder backup preserves unrelated nested files");
        var profiles = storage.ReadProfiles(session);
        Check(profiles.Count == Directory.GetFiles(legacy).Count(path => TeammateDatabase.IsProfileDocument(Path.GetFileName(path))),
            "all actual teammate profiles imported");
        var rawDatabase = new TeammateDatabase(root, profileId);
        foreach (string path in Directory.GetFiles(legacy).Where(path => TeammateDatabase.IsLegacyDocument(Path.GetFileName(path))))
            Check(rawDatabase.Read(Path.GetFileName(path)) == File.ReadAllText(path), "exact imported JSON: " + Path.GetFileName(path));
        foreach (var teammate in profiles)
        {
            string key = $"{teammate.Aid}-settings.json";
            var settings = storage.Read<FriendlyTeammateSettings>(session, key)!;
            Check(settings != null && storage.Read<List<Item>>(session, $"{teammate.Aid}-equipment.json")!.Count > 0, "SPT deserialization");
            settings!.Aggression = 17f;
            storage.Write(session, key, settings);
        }
        var pending = storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json") ?? [];
        storage.Write(session, "recruit-requests.json", pending);
        storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        foreach (var teammate in storage.ReadProfiles(session))
            Check(storage.Read<FriendlyTeammateSettings>(session, $"{teammate.Aid}-settings.json")!.Aggression == 17f, "old JSON did not overwrite edited setting");
        Check(storage.GetAllAccountIds().SetEquals(profiles.Select(profile => profile.Aid!.Value)), "account allocation scans databases");

        // Move only the test copy, after verifying both resolved paths stay under this test root.
        string movedDirectory = Path.Combine(sandbox, "legacy-moved-aside");
        Check(Path.GetFullPath(backupDirectory).StartsWith(work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Path.GetFullPath(movedDirectory).StartsWith(work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "test move contained");
        Directory.Move(backupDirectory, movedDirectory);
        storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        Check(storage.ReadProfiles(session).Count == profiles.Count, "roster works without JSON folder");
        foreach (var teammate in profiles)
            Check(storage.Read<FriendlyTeammateSettings>(session, $"{teammate.Aid}-settings.json")!.Aggression == 17f, "settings work without JSON folder");
        Check(storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Count == pending.Count, "recruit requests work without JSON folder");

        var newTeammate = jsonUtil.Deserialize<BotBase>(jsonUtil.Serialize(profiles[0])!)!;
        newTeammate.Aid = 987654;
        newTeammate.Id = new MongoId("cccccccccccccccccccccccc");
        storage.WriteBatch(session, new Dictionary<string, string>
        {
            ["987654.json"] = jsonUtil.Serialize(newTeammate)!,
            ["987654-settings.json"] = jsonUtil.Serialize(new FriendlyTeammateSettings { AutoJoinEnabled = true })!,
            ["987654-equipment.json"] = jsonUtil.Serialize(newTeammate.Inventory!.Items)!,
        });
        Check(storage.ReadProfiles(session).Count == profiles.Count + 1 && !Directory.Exists(importedDirectory), "new teammate is database-only");
        int deletedAid = profiles[0].Aid!.Value;
        Check(storage.DeleteTeammate(session, deletedAid), "delete migrated teammate");
        Directory.Move(movedDirectory, importedDirectory);
        storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        Check(storage.ReadProfiles(session).All(profile => profile.Aid != deletedAid), "restored JSON cannot resurrect deleted teammate");
        Check(storage.ReadProfiles(session).Any(profile => profile.Aid == 987654), "new teammate retained with restored JSON");
        Check(!Directory.Exists(importedDirectory) && Directory.Exists(backupDirectory),
            "already imported database renames a restored source folder without importing it again");
        Check(storage.Read<FriendlyTeammateSettings>(session, $"{profiles[1].Aid}-settings.json")!.Aggression == 17f,
            "backup rename preserves post-import database edits");

        // Invalid legacy input must block all import, then succeed once that source is repaired.
        const string badId = "dddddddddddddddddddddddd";
        string badDirectory = Path.Combine(root, badId);
        Directory.CreateDirectory(badDirectory);
        File.Copy(Path.Combine(legacy, $"{profiles[0].Aid}.json"), Path.Combine(badDirectory, $"{profiles[0].Aid}.json"));
        File.WriteAllText(Path.Combine(badDirectory, $"{profiles[0].Aid}-settings.json"), "{");
        Throws(() => storage.InitializeProfile(new MongoId(badId)), "malformed sidecar blocks migration");
        Check(Directory.Exists(badDirectory) && !Directory.Exists(badDirectory + ".backup"),
            "failed import leaves the original folder in place");
        const string cleanId = "ffffffffffffffffffffffff";
        storage.InitializeAllProfiles([new MongoId(badId), new MongoId(cleanId)]);
        Check(File.Exists(Path.Combine(root, cleanId + ".db")) && storage.ReadProfiles(new MongoId(cleanId)).Count == 0,
            "a bad legacy profile does not block other profiles at server startup");
        var badDatabase = new TeammateDatabase(root, badId);
        Check(badDatabase.ReadProfiles().Count == 0, "malformed migration has no partial profile");
        File.WriteAllText(Path.Combine(badDirectory, $"{profiles[0].Aid}-settings.json"), "{}");
        storage.InitializeProfile(new MongoId(badId));
        Check(storage.ReadProfiles(new MongoId(badId)).Count == 1, "fixed JSON import retries successfully");
        Check(!Directory.Exists(badDirectory) && Directory.Exists(badDirectory + ".backup"),
            "successful import retry then preserves the source folder as backup");

        // Existing backups, whether a directory or a file, must never be overwritten.
        const string collisionId = "121212121212121212121212";
        string collisionDirectory = Path.Combine(root, collisionId);
        string collisionBackup = collisionDirectory + ".backup";
        Directory.CreateDirectory(collisionDirectory);
        File.Copy(Path.Combine(legacy, $"{profiles[0].Aid}.json"), Path.Combine(collisionDirectory, $"{profiles[0].Aid}.json"));
        File.WriteAllText(collisionBackup, "existing backup");
        storage.InitializeProfile(new MongoId(collisionId));
        Check(Directory.Exists(collisionDirectory) && File.ReadAllText(collisionBackup) == "existing backup",
            "backup-path file collision preserves both original and existing backup");
        Check(storage.ReadProfiles(new MongoId(collisionId)).Count == 1,
            "backup-path collision does not block the committed database");
        string savedCollisionBackup = Path.Combine(sandbox, "existing-backup-file");
        Check(Path.GetFullPath(collisionBackup).StartsWith(work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Path.GetFullPath(savedCollisionBackup).StartsWith(work + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "test backup collision move contained");
        File.Move(collisionBackup, savedCollisionBackup);
        storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        storage.InitializeProfile(new MongoId(collisionId));
        Check(!Directory.Exists(collisionDirectory) && Directory.Exists(collisionBackup),
            "next startup retries backup rename after its destination becomes available");
        Directory.CreateDirectory(collisionDirectory);
        File.WriteAllText(Path.Combine(collisionDirectory, "keep.txt"), "restored folder");
        storage = new FriendlyTeammateStorage(new FileUtil(), jsonUtil, logger);
        storage.InitializeProfile(new MongoId(collisionId));
        Check(File.ReadAllText(Path.Combine(collisionDirectory, "keep.txt")) == "restored folder"
            && File.Exists(Path.Combine(collisionBackup, $"{profiles[0].Aid}.json")),
            "existing backup directory and restored source are both retained");

        const string backupOnlyId = "abababababababababababab";
        string backupOnlyDirectory = Path.Combine(root, backupOnlyId + ".backup");
        Directory.CreateDirectory(backupOnlyDirectory);
        File.Copy(Path.Combine(legacy, $"{profiles[0].Aid}.json"), Path.Combine(backupOnlyDirectory, $"{profiles[0].Aid}.json"));
        storage.InitializeAllProfiles([]);
        Check(!File.Exists(Path.Combine(root, backupOnlyId + ".db")), "backup folders are excluded from profile discovery");
        storage.InitializeProfile(new MongoId(backupOnlyId));
        Check(storage.ReadProfiles(new MongoId(backupOnlyId)).Count == 0
            && Directory.GetFiles(backupOnlyDirectory).Length == 1, "backup contents are never automatically reimported");

        foreach (var entry in originalHashes)
        {
            Check(Hash(Path.Combine(legacy, entry.Key!)) == entry.Value, "live source unchanged");
            Check(Hash(Path.Combine(backupDirectory, entry.Key!)) == entry.Value, "renamed migration backup unchanged");
        }
        Console.WriteLine($"Verified copied live save: {profiles.Count} teammates; {originalHashes.Count} untouched source files.");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

public class TestLogger : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "Error") Console.WriteLine("Expected diagnostic: " + args?[0]);
        return targetMethod.ReturnType == typeof(bool) ? false : null;
    }
}