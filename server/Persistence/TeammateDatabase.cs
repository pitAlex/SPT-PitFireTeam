using LiteDB;
using System.Globalization;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.Security.Cryptography;
using System.Text;

namespace pitTeam.Server.Persistence;

/// <summary>
/// Profile-local encrypted JSON documents. The embedded key discourages casual editing; it is
/// not a security boundary against the machine owner. Keep the v1 key stable across builds/platforms.
/// </summary>
public sealed class TeammateDatabase
{
    private const int SchemaVersion = 1;
    private const string ImportKey = "migration/legacy-json-v1";
    private const string KeyMaterial = "pitFireTeam/followers/v1/90aa379ef47b4d36b257ed00c8fc3ebf/8ca24bf176e542cab4ed96bd9b623345";
    private readonly object sync = new();
    private readonly byte[] key;
    private readonly string profileId;
    public string DatabasePath { get; }

    public TeammateDatabase(string rootDirectory, string profileId)
    {
        if (!IsProfileId(profileId))
            throw new ArgumentException("Invalid teammate database profile id.", nameof(profileId));
        this.profileId = profileId.ToLowerInvariant();
        DatabasePath = Path.GetFullPath(Path.Combine(rootDirectory, this.profileId + ".db"));
        key = SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial + "/" + this.profileId));
    }

    public static bool IsProfileId(string value) => value.Length == 24 && value.All(char.IsAsciiHexDigit);

    public static bool IsProfileDocument(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(name[..^5], out int aid) && aid > 0;

    public static bool IsLegacyDocument(string name)
    {
        if (name == "recruit-requests.json" || IsProfileDocument(name)) return true;
        foreach (string suffix in new[] { "-settings.json", "-equipment.json" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name[..^suffix.Length], out int aid) && aid > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Creates the empty DB before reading legacy files. Validated documents and the completion
    /// marker commit together. Failure leaves the JSONs intact and the migration retryable.
    /// After commit legacy files are never consulted again, even when a teammate is deleted.
    /// </summary>
    public int Initialize(Func<IReadOnlyDictionary<string, string>> loadLegacyDocuments)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            using var database = Open(create: true);
            int version = database.UserVersion;
            if (version != 0 && version != SchemaVersion)
                throw new InvalidDataException($"Unsupported teammate database version {version}: {DatabasePath}");
            if (version == SchemaVersion && !database.CollectionExists("documents"))
                throw new InvalidDataException($"Teammate database document collection is missing: {DatabasePath}");

            var collection = database.GetCollection("documents");
            int imported = InTransaction(database, () =>
            {
                // Never replace a damaged database or fall back to stale JSONs.
                var existing = ReadAll(collection);
                if (existing.ContainsKey(ImportKey)) return 0;
                if (version != 0 || existing.Count != 0)
                    throw new InvalidDataException($"Teammate database migration marker is missing: {DatabasePath}");

                var documents = loadLegacyDocuments();
                foreach (var document in documents)
                {
                    Write(collection, document.Key, document.Value);
                    if (Read(collection, document.Key) != document.Value)
                        throw new InvalidDataException($"Teammate import verification failed: {document.Key}");
                }
                // Retain source hashes as provenance for the preserved JSON backup.
                var hashes = documents.ToDictionary(entry => entry.Key,
                    entry => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Value))));
                Write(collection, ImportKey, JsonSerializer.Serialize(hashes));
                return documents.Count;
            });
            // LiteDB pragmas are outside document transactions. A crash here is recoverable:
            // version 0 with a valid committed import marker only finishes this version update.
            if (version == 0) database.UserVersion = SchemaVersion;
            return imported;
        }
    }

    public string? Read(string name)
    {
        lock (sync)
        {
            using var database = Open();
            return Read(database.GetCollection("documents"), name);
        }
    }

    public Dictionary<string, string> ReadProfiles()
    {
        lock (sync)
        {
            using var database = Open();
            return ReadAll(database.GetCollection("documents"), IsProfileDocument);
        }
    }

    public void WriteBatch(IReadOnlyDictionary<string, string> documents, bool backupPrevious = false)
    {
        lock (sync)
        {
            using var database = Open();
            var collection = database.GetCollection("documents");
            InTransaction(database, () =>
            {
                foreach (var document in documents)
                {
                    // Authenticate the prior value before overwriting it, including recovery writes.
                    string? previous = Read(collection, document.Key);
                    if (backupPrevious && previous != null)
                        Write(collection, $"recovery/{Guid.NewGuid():N}/{document.Key}", previous);
                    Write(collection, document.Key, document.Value);
                }
                return true;
            });
        }
    }

    public bool DeleteTeammate(int aid)
    {
        lock (sync)
        {
            using var database = Open();
            var collection = database.GetCollection("documents");
            return InTransaction(database, () =>
            {
                if (Read(collection, $"{aid}.json") == null) return false;
                collection.Delete($"{aid}.json");
                collection.Delete($"{aid}-settings.json");
                collection.Delete($"{aid}-equipment.json");
                return true;
            });
        }
    }

    private LiteDatabase Open(bool create = false)
    {
        if (!File.Exists(DatabasePath))
        {
            if (!create) throw new FileNotFoundException("Teammate database is missing.", DatabasePath);
        }
        else
        {
            // The local SQLite trial must be converted offline. Never treat it as an empty LiteDB
            // store and reimport old JSONs over changes made during that trial.
            using var stream = File.OpenRead(DatabasePath);
            Span<byte> header = stackalloc byte[16];
            if (stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8))
                throw new InvalidDataException($"SQLite trial database requires offline LiteDB conversion: {DatabasePath}");
        }

        return new LiteDatabase(new ConnectionString
        {
            Filename = DatabasePath,
            Connection = ConnectionType.Direct,
            Collation = new Collation(CultureInfo.InvariantCulture.LCID, CompareOptions.Ordinal),
        });
    }

    private static T InTransaction<T>(LiteDatabase database, Func<T> action)
    {
        database.BeginTrans();
        try
        {
            T result = action();
            database.Commit();
            return result;
        }
        catch
        {
            database.Rollback();
            throw;
        }
    }

    private Dictionary<string, string> ReadAll(
        ILiteCollection<BsonDocument> collection, Func<string, bool>? filter = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in collection.FindAll())
        {
            string name = document["_id"].AsString;
            if (filter == null || filter(name)) result.Add(name, Unprotect(name, GetPayload(document)));
        }
        return result;
    }

    private string? Read(ILiteCollection<BsonDocument> collection, string name)
    {
        var document = collection.FindById(name);
        return document == null ? null : Unprotect(name, GetPayload(document));
    }

    private static byte[] GetPayload(BsonDocument document) =>
        document["payload"].IsBinary ? document["payload"].AsBinary
            : throw new InvalidDataException($"Teammate database payload is missing: {document["_id"]}");

    private void Write(ILiteCollection<BsonDocument> collection, string name, string json) =>
        collection.Upsert(new BsonDocument { ["_id"] = name, ["payload"] = Protect(name, json) });

    private byte[] Protect(string name, string json)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(json);
        // v1: version (1), random nonce (12), authentication tag (16), ciphertext.
        byte[] payload = new byte[29 + plaintext.Length];
        payload[0] = 1;
        RandomNumberGenerator.Fill(payload.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(payload.AsSpan(1, 12), plaintext, payload.AsSpan(29), payload.AsSpan(13, 16),
            Encoding.UTF8.GetBytes(profileId + "/" + name));
        return payload;
    }

    private string Unprotect(string name, byte[] payload)
    {
        try
        {
            if (payload.Length < 29 || payload[0] != 1) throw new CryptographicException();
            byte[] plaintext = new byte[payload.Length - 29];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload.AsSpan(1, 12), payload.AsSpan(29), payload.AsSpan(13, 16), plaintext,
                Encoding.UTF8.GetBytes(profileId + "/" + name));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException($"Teammate database record failed integrity validation: {DatabasePath}, {name}", ex);
        }
    }
}