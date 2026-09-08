using System.Globalization;
using System.Text.Json;
using LiteDB;
using pitTeam.Server.Persistence;

internal static class TrialDatabaseConversion
{
    // Offline test/deployment utility only. No SQLite library is linked into the server.
    public static int Run(string snapshotPath, string outputDirectory)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        var root = snapshot.RootElement;
        string profileId = root.GetProperty("profileId").GetString()!;
        if (!TeammateDatabase.IsProfileId(profileId) || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported SQLite trial snapshot.");
        var records = root.GetProperty("records").EnumerateObject()
            .ToDictionary(entry => entry.Name, entry => Convert.FromBase64String(entry.Value.GetString()!));
        if (!records.ContainsKey("migration/legacy-json-v1"))
            throw new InvalidDataException("Trial snapshot has no completed JSON migration marker.");
        outputDirectory = Path.GetFullPath(outputDirectory);
        var store = new TeammateDatabase(outputDirectory, profileId);
        if (File.Exists(store.DatabasePath)) throw new IOException("Conversion output already exists; source and existing output were not changed.");
        Directory.CreateDirectory(outputDirectory);
        using (var database = new LiteDatabase(new ConnectionString
        {
            Filename = store.DatabasePath,
            Collation = new Collation(CultureInfo.InvariantCulture.LCID, CompareOptions.Ordinal),
        }))
        {
            database.BeginTrans();
            try
            {
                var collection = database.GetCollection("documents");
                foreach (var record in records)
                    collection.Insert(new BsonDocument { ["_id"] = record.Key, ["payload"] = record.Value });
                database.Commit();
            }
            catch { database.Rollback(); throw; }
            database.UserVersion = 1;
        }
        // Authenticate every record using the production reader. No JSON import is permitted.
        store.Initialize(() => throw new InvalidDataException("Conversion unexpectedly requested legacy JSON."));
        using (var database = new LiteDatabase(store.DatabasePath))
        {
            var actual = database.GetCollection("documents").FindAll()
                .ToDictionary(document => document["_id"].AsString, document => document["payload"].AsBinary);
            if (actual.Count != records.Count || records.Any(entry => !actual.TryGetValue(entry.Key, out var value) || !entry.Value.SequenceEqual(value)))
                throw new InvalidDataException("Converted records differ from the SQLite snapshot.");
        }
        Console.WriteLine($"Converted and verified {records.Count} encrypted records, including {store.ReadProfiles().Count} teammates: {store.DatabasePath}");
        return 0;
    }
}
