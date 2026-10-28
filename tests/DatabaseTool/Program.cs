using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteDB;
using pitTeam.DatabaseTool;
using pitTeam.Server.Persistence;

string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "database-tool", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(work);
string profileId = "0123456789abcdef01234567";
var database = new TeammateDatabase(work, profileId);
database.Initialize(() => new Dictionary<string, string>
{
    ["10.json"] = "{\"_id\":\"111111111111111111111111\",\"aid\":10,\"Info\":{\"Level\":15,\"Nickname\":\"Friend\"}}",
    ["10-settings.json"] = "{\"aggression\":50,\"autoJoin\":true}",
    ["10-equipment.json"] = "[{\"_id\":\"222222222222222222222222\",\"upd\":{\"StackObjectsCount\":30}}]",
    ["friendly-encounter-penalties.json"] = "{\"kills\":[1,2]}"
});
database.WriteBatch(new Dictionary<string, string> { ["10-settings.json"] = "{\"aggression\":60,\"autoJoin\":true}" }, backupPrevious: true);
var initial = database.ReadDocuments();
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
string BytesHash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(database.DatabasePath)));
int Run(string[] args, bool running = false)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    int result = DatabaseToolApplication.Run(args, output, error, () => running);
    if (result != 0) Console.WriteLine("Expected/checked failure: " + error.ToString().Trim());
    return result;
}
string Export(string name, string? document = null)
{
    string file = Path.Combine(work, name + ".json");
    var args = new List<string> { "export", database.DatabasePath, file };
    if (document != null) args.AddRange(["--document", document]);
    Check(Run(args.ToArray()) == 0, "Export succeeds: " + name);
    return file;
}
string initialHash = BytesHash();
Check(Run(["--help"]) == 0 && Run(["unknown"]) != 0, "CLI help and argument validation");
Check(Run(["list", database.DatabasePath]) == 0, "List succeeds");
string export = Export("all");
Check(BytesHash() == initialHash, "List/export do not modify source bytes");
var payload = JsonNode.Parse(File.ReadAllText(export))!.AsObject();
Check(payload["documents"]!.AsArray().Count == 4, "Export includes editable documents only");
Check(!File.ReadAllText(export).Contains("migration/legacy-json-v1") && !File.ReadAllText(export).Contains("recovery/"), "Internal records are not exported");
Check(Run(["export", database.DatabasePath, export]) != 0 && BytesHash() == initialHash, "Export never overwrites existing output or source");
Check(Run(["import", database.DatabasePath, export]) == 0 && BytesHash() == initialHash && Directory.GetFiles(work, "*.bak").Length == 0, "No-op import preserves bytes without backup");
string one = Export("one", "10-settings.json");
var edited = JsonNode.Parse(File.ReadAllText(one))!.AsObject();
edited["documents"]![0]!["value"]!["aggression"] = 75;
File.WriteAllText(one, edited.ToJsonString());
Check(Run(["import", database.DatabasePath, one]) == 0, "Edited import succeeds");
Check(JsonDocument.Parse(database.Read("10-settings.json")!).RootElement.GetProperty("aggression").GetInt32() == 75, "Edited JSON persists and decrypts through production reader");
var after = database.ReadDocuments();
Check(initial.Where(pair => pair.Key != "10-settings.json").All(pair => after[pair.Key] == pair.Value), "Other documents, migration and recovery remain unchanged");
string backup = Directory.GetFiles(work, "*.bak").Single();
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(backup))) == initialHash, "Backup exactly preserves original encrypted bytes");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(database.DatabasePath)).Contains("Nickname"), "Saved database remains encrypted");
string booleanExport = Export("boolean", "10-settings.json");
var booleanData = JsonNode.Parse(File.ReadAllText(booleanExport))!.AsObject();
booleanData["documents"]![0]!["value"]!["autoJoin"] = false;
File.WriteAllText(booleanExport, booleanData.ToJsonString());
Check(Run(["import", database.DatabasePath, booleanExport]) == 0 &&
    !JsonDocument.Parse(database.Read("10-settings.json")!).RootElement.GetProperty("autoJoin").GetBoolean(),
    "Boolean values can be edited without being mistaken for a type change");
string reordered = Export("reordered", "10-settings.json");
var reorderedData = JsonNode.Parse(File.ReadAllText(reordered))!.AsObject();
var oldValue = reorderedData["documents"]![0]!["value"]!.AsObject();
reorderedData["documents"]![0]!["value"] = new JsonObject(oldValue.Reverse().Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
File.WriteAllText(reordered, reorderedData.ToJsonString());
string reorderHash = BytesHash();
int reorderBackups = Directory.GetFiles(work, "*.bak").Length;
Check(Run(["import", database.DatabasePath, reordered]) == 0 && BytesHash() == reorderHash && Directory.GetFiles(work, "*.bak").Length == reorderBackups,
    "Property reordering is a no-op and does not rewrite the database");

void RejectedEdit(string name, Action<JsonObject> edit)
{
    string file = Export(name);
    var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
    edit(node);
    File.WriteAllText(file, node.ToJsonString());
    string before = BytesHash();
    int backups = Directory.GetFiles(work, "*.bak").Length;
    Check(Run(["import", database.DatabasePath, file]) != 0 && BytesHash() == before && Directory.GetFiles(work, "*.bak").Length == backups,
        "Rejected import is non-destructive: " + name);
}
RejectedEdit("profile", node => node["profileId"] = "abcdef0123456789abcdef01");
RejectedEdit("version", node => node["formatVersion"] = 99);
RejectedEdit("internal", node => node["documents"]![0]!["name"] = "migration/legacy-json-v1");
RejectedEdit("nonexistent", node => node["documents"]![0]!["name"] = "new.json");
RejectedEdit("duplicate-doc", node => node["documents"]!.AsArray().Add(node["documents"]![0]!.DeepClone()));
RejectedEdit("identity", node => node["documents"]!.AsArray().Single(doc => doc!["name"]!.GetValue<string>() == "10.json")!["value"]!["aid"] = 999);
RejectedEdit("type", node => node["documents"]!.AsArray().Single(doc => doc!["name"]!.GetValue<string>() == "10.json")!["value"]!["Info"]!["Level"] = "not a number");
RejectedEdit("missing-field", node => node["documents"]!.AsArray().Single(doc => doc!["name"]!.GetValue<string>() == "10.json")!["value"]!["Info"]!.AsObject().Remove("Level"));
RejectedEdit("hash", node => node["documents"]![0]!["originalSha256"] = "wrong");
RejectedEdit("valid-before-invalid", node =>
{
    node["documents"]!.AsArray().Single(doc => doc!["name"]!.GetValue<string>() == "10-settings.json")!["value"]!["aggression"] = 99;
    node["documents"]!.AsArray().Single(doc => doc!["name"]!.GetValue<string>() == "10.json")!["value"]!["aid"] = 999;
});
RejectedEdit("missing-format", node => node.Remove("format"));

string stale = Export("stale");
database.WriteBatch(new Dictionary<string, string> { ["10-settings.json"] = "{\"aggression\":20,\"autoJoin\":true}" });
string changedHash = BytesHash();
Check(Run(["import", database.DatabasePath, stale]) != 0 && BytesHash() == changedHash, "Stale export cannot overwrite newer state");
Check(Run(["list", database.DatabasePath], running: true) != 0 && BytesHash() == changedHash, "Running-game check refuses database access");
using (var locked = new FileStream(database.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    Check(Run(["list", database.DatabasePath]) != 0, "Open database is refused");
string log = Path.Combine(work, profileId + "-log.db");
File.WriteAllBytes(log, [1]);
Check(Run(["list", database.DatabasePath]) != 0 && BytesHash() == changedHash, "Pending LiteDB log is refused without modification");
File.Delete(log);
string invalid = Path.Combine(work, "invalid.json");
File.WriteAllText(invalid, "{invalid");
Check(Run(["import", database.DatabasePath, invalid]) != 0 && BytesHash() == changedHash, "Malformed JSON leaves original untouched");
string duplicate = Export("duplicate-property", "10-settings.json");
File.WriteAllText(duplicate, File.ReadAllText(duplicate).Replace("\"aggression\": 20", "\"aggression\": 20, \"Aggression\": 30"));
Check(Run(["import", database.DatabasePath, duplicate]) != 0 && BytesHash() == changedHash, "Ambiguous duplicate JSON properties are refused");
Check(!Directory.GetDirectories(work, ".pitFireTeam-edit-*").Any(), "Temporary encrypted workspaces are cleaned up");

string damagedFolder = Path.Combine(work, "damaged");
Directory.CreateDirectory(damagedFolder);
string damagedFile = Path.Combine(damagedFolder, profileId + ".db");
File.Copy(database.DatabasePath, damagedFile);
using (var raw = new LiteDatabase(damagedFile))
{
    var records = raw.GetCollection("documents");
    var record = records.FindById("10-settings.json");
    byte[] payloadBytes = record["payload"].AsBinary;
    payloadBytes[^1] ^= 1;
    record["payload"] = payloadBytes;
    records.Update(record);
}
string damagedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(damagedFile)));
Check(Run(["export", damagedFile, Path.Combine(work, "damaged-export.json")]) != 0 &&
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(damagedFile))) == damagedHash && !File.Exists(Path.Combine(work, "damaged-export.json")),
    "Authentication failure does not modify or export the damaged database");
string missingFile = Path.Combine(work, "abcdef0123456789abcdef01.db");
Check(Run(["list", missingFile]) != 0 && !File.Exists(missingFile), "Missing databases are never created or initialized");
Console.WriteLine($"PASS: {checks} database utility checks. Artifacts: {work}");
