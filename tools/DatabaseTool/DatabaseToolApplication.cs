using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using pitTeam.Server.Persistence;

namespace pitTeam.DatabaseTool;

public sealed record DatabaseExport
{
    public string Format { get; init; } = "pitFireTeam-database-export";
    public int FormatVersion { get; init; } = 1;
    public required string ProfileId { get; init; }
    public required string SourceFingerprint { get; init; }
    public required List<ExportDocument> Documents { get; init; }
}

public sealed record ExportDocument
{
    public required string Name { get; init; }
    public required string OriginalSha256 { get; init; }
    public required JsonElement Value { get; init; }
}

public static class DatabaseToolApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Help = """
        pitFireTeam Database Tool
        Close Tarkov and the SPT server before using this tool.

          pitFireTeam.DatabaseTool list <profileId.db>
          pitFireTeam.DatabaseTool export <profileId.db> <output.json> [--document <name>]
          pitFireTeam.DatabaseTool import <profileId.db> <edited.json>

        Export creates a new readable JSON file. Edit only the document 'value' fields.
        Import updates existing documents only, keeps internal records, and creates an
        encrypted .bak beside the database before replacing it. Stale exports are rejected.
        No game installation, server DLL or manually supplied encryption key is required.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error, Func<bool>? gameRunning = null)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                output.WriteLine(Help);
                return 0;
            }
            string command = args[0];
            if (!(command == "list" && args.Length == 2 || command == "import" && args.Length == 3 ||
                command == "export" && (args.Length == 3 || args.Length == 5 && args[3] == "--document")))
                throw new ArgumentException("Invalid arguments. Run with --help for usage.");
            if ((gameRunning ?? IsGameRunning)()) throw new IOException("Close Tarkov and the SPT server before opening the database.");

            using var snapshot = new OfflineSnapshot(args[1], writable: command == "import");
            snapshot.Store.Initialize(() => throw new InvalidDataException("This database must be initialized by the mod before it can be edited."));
            var documents = snapshot.Store.ReadDocuments();
            if (!documents.ContainsKey("migration/legacy-json-v1"))
                throw new InvalidDataException("This database has no completed migration marker. Open it with the mod first.");
            var editable = documents.Where(pair => IsEditable(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            if (command == "list")
            {
                foreach (var document in editable) output.WriteLine(document.Key);
                output.WriteLine($"{editable.Length} editable documents. Database unchanged.");
            }
            else if (command == "export")
            {
                if (args.Length == 5)
                {
                    editable = editable.Where(pair => pair.Key == args[4]).ToArray();
                    if (editable.Length == 0) throw new ArgumentException("The requested document does not exist or is internal.");
                }
                var export = new DatabaseExport
                {
                    ProfileId = snapshot.ProfileId, SourceFingerprint = Fingerprint(documents),
                    Documents = editable.Select(pair => new ExportDocument
                    {
                        Name = pair.Key, OriginalSha256 = Hash(pair.Value), Value = ParseValue(pair.Value)
                    }).ToList()
                };
                // Serialize/validate before creating output; never overwrite another export or a database.
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);
                using var file = new FileStream(Path.GetFullPath(args[2]), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(json);
                output.WriteLine($"Exported {export.Documents.Count} documents to {Path.GetFullPath(args[2])}. Database unchanged.");
            }
            else
            {
                string json = File.ReadAllText(args[2]);
                var root = ParseValue(json);
                EnsureUniqueProperties(root);
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out _) || !root.TryGetProperty("formatVersion", out _))
                    throw new InvalidDataException("Select a JSON export created by this utility.");
                var import = JsonSerializer.Deserialize<DatabaseExport>(json, JsonOptions)
                    ?? throw new InvalidDataException("The export is empty.");
                if (import.Format != "pitFireTeam-database-export" || import.FormatVersion != 1 ||
                    import.ProfileId != snapshot.ProfileId)
                    throw new InvalidDataException("Wrong export format, version or player profile.");
                if (import.SourceFingerprint != Fingerprint(documents))
                    throw new InvalidDataException("The database changed since this export, or it belongs to another mode/database. Export again before editing.");
                if (import.Documents == null || import.Documents.Count == 0) throw new InvalidDataException("No documents were supplied.");
                var changes = new Dictionary<string, string>(StringComparer.Ordinal);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var document in import.Documents)
                {
                    if (document == null || !names.Add(document.Name) || !IsEditable(document.Name) ||
                        !documents.TryGetValue(document.Name, out string? original))
                        throw new InvalidDataException("Import contains duplicate, internal or nonexistent document names.");
                    if (document.OriginalSha256 != Hash(original)) throw new InvalidDataException($"Original hash mismatch: {document.Name}");
                    var originalValue = ParseValue(original);
                    ValidateEdit(document.Name, originalValue, document.Value);
                    if (Normalize(originalValue) != Normalize(document.Value)) changes.Add(document.Name, document.Value.GetRawText());
                }
                if (changes.Count == 0) output.WriteLine("No changes. Database unchanged; no backup needed.");
                else
                {
                    snapshot.Store.WriteBatch(changes);
                    var verified = snapshot.Store.ReadDocuments();
                    if (verified.Count != documents.Count || documents.Any(pair =>
                        verified[pair.Key] != (changes.TryGetValue(pair.Key, out string? changed) ? changed : pair.Value)))
                        throw new InvalidDataException("Staged database verification failed. Original database unchanged.");
                    string backup = snapshot.Commit();
                    output.WriteLine($"Imported {changes.Count} changed documents. Backup: {backup}");
                }
            }
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static bool IsEditable(string name) => !string.IsNullOrWhiteSpace(name) &&
        name.EndsWith(".json", StringComparison.Ordinal) &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsGameRunning()
    {
        bool running = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { running |= process.ProcessName.Equals("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase) ||
                                 process.ProcessName.StartsWith("SPT.Server", StringComparison.OrdinalIgnoreCase); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return running;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Fingerprint(Dictionary<string, string> documents) => Hash(JsonSerializer.Serialize(
        documents.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new[] { pair.Key, Hash(pair.Value) })));
    private static string Normalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var child in value.EnumerateArray()) WriteCanonical(writer, child);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
    private static JsonElement ParseValue(string json)
    {
        using var value = JsonDocument.Parse(json);
        return value.RootElement.Clone();
    }

    private static void EnsureUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property: " + property.Name);
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) EnsureUniqueProperties(child);
    }

    private static void ValidateEdit(string name, JsonElement original, JsonElement edited)
    {
        if (edited.ValueKind != original.ValueKind || edited.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidDataException("Document must keep its original object/array structure: " + name);
        if (original.ValueKind == JsonValueKind.Object)
            foreach (var property in original.EnumerateObject())
            {
                if (!edited.TryGetProperty(property.Name, out var after)) throw new InvalidDataException($"Missing original field in {name}: {property.Name}");
                if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    bool bothBoolean = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                       after.ValueKind is JsonValueKind.True or JsonValueKind.False;
                    if (property.Value.ValueKind != after.ValueKind && !bothBoolean) throw new InvalidDataException($"Changed field type in {name}: {property.Name}");
                    if (after.ValueKind == JsonValueKind.Object) ValidateEdit(name + "/" + property.Name, property.Value, after);
                }
            }
        if (TeammateDatabase.IsProfileDocument(name))
            foreach (string key in new[] { "_id", "aid" })
                if (original.TryGetProperty(key, out var before) &&
                    (!edited.TryGetProperty(key, out var after) || Normalize(before) != Normalize(after)))
                    throw new InvalidDataException("Teammate identity cannot be changed: " + name);
    }
}

internal sealed class OfflineSnapshot : IDisposable
{
    private readonly FileStream source;
    private readonly string path;
    private readonly string workspace;
    private bool committed;
    internal string ProfileId { get; }
    internal TeammateDatabase Store { get; }

    internal OfflineSnapshot(string databasePath, bool writable)
    {
        path = Path.GetFullPath(databasePath);
        ProfileId = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (Path.GetExtension(path) != ".db" || !TeammateDatabase.IsProfileId(ProfileId))
            throw new ArgumentException("Select the original 24-character player profile ID .db file; keep its filename unchanged.");
        string log = Path.Combine(Path.GetDirectoryName(path)!, ProfileId + "-log.db");
        if (File.Exists(log) && new FileInfo(log).Length > 0)
            throw new IOException("An uncheckpointed LiteDB log exists. Close SPT cleanly first; do not remove the log file.");
        // Deny concurrent writes; permit the native atomic replacement to read/rename the old file.
        source = new FileStream(path, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read | FileShare.Delete);
        workspace = Path.Combine(Path.GetDirectoryName(path)!, ".pitFireTeam-edit-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workspace);
            Store = new TeammateDatabase(workspace, ProfileId);
            using (var copy = new FileStream(Store.DatabasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) source.CopyTo(copy);
        }
        catch { Dispose(); throw; }
    }

    internal string Commit()
    {
        if (committed) throw new InvalidOperationException("Database already committed.");
        string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "." + Guid.NewGuid().ToString("N")[..8] + ".bak";
        File.Replace(Store.DatabasePath, path, backup);
        committed = true;
        return backup;
    }

    public void Dispose()
    {
        source?.Dispose();
        try { if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true); }
        catch (IOException) { } // Cleanup must not turn a committed import into a reported failure.
        catch (UnauthorizedAccessException) { }
    }
}
