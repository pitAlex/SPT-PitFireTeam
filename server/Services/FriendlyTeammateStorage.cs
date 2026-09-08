using pitTeam.Server.Models;
using pitTeam.Server.Persistence;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using System.Text.Json;
using Path = System.IO.Path;

namespace pitTeam.Server.Services;

/// <summary>All follower persistence goes through here; loose JSON is read only by the one-time importer.</summary>
// All routes must share the per-profile database cache and its operation locks.
[Injectable(InjectionType.Singleton)]
public class FriendlyTeammateStorage(FileUtil fileUtil, JsonUtil jsonUtil, ISptLogger<FriendlyTeammateStorage> logger)
{
    private readonly object sync = new();
    private readonly Dictionary<string, TeammateDatabase> databases = new(StringComparer.Ordinal);
    private string RootDirectory => Path.Combine(fileUtil.GetModPath("pitFireTeam-ServerMod"), "Resources", "teammates");

    public void InitializeAllProfiles(IEnumerable<MongoId> profileIds)
    {
        foreach (string profileId in profileIds.Select(id => id.ToString()).Concat(GetStoredProfileIds()).Distinct())
        {
            try
            {
                InitializeProfile(new MongoId(profileId));
            }
            catch (Exception ex)
            {
                // Keep other player profiles usable. Access to this profile still fails/retries through
                // GetDatabase; never register an empty replacement or accept a partially imported roster.
                logger.Warning($"Teammate storage startup skipped profile '{profileId}': {ex.Message}");
            }
        }
    }

    public void InitializeProfile(MongoId sessionId) => GetDatabase(sessionId);

    public List<BotBase> ReadProfiles(MongoId sessionId) => GetDatabase(sessionId).ReadProfiles()
        .Select(entry => Deserialize<BotBase>(entry.Value, entry.Key)).ToList();

    public HashSet<int> GetAllAccountIds()
    {
        var result = new HashSet<int>();
        foreach (string profileId in GetStoredProfileIds())
        {
            foreach (var teammate in ReadProfiles(new MongoId(profileId)))
            {
                if (teammate.Aid is > 0) result.Add(teammate.Aid.Value);
            }
        }
        return result;
    }

    public T? Read<T>(MongoId sessionId, string name) where T : class
    {
        string? json = GetDatabase(sessionId).Read(name);
        return json == null ? null : Deserialize<T>(json, name);
    }

    public bool Exists(MongoId sessionId, string name) => GetDatabase(sessionId).Read(name) != null;

    public void Write<T>(MongoId sessionId, string name, T value, bool backupPrevious = false) =>
        GetDatabase(sessionId).WriteBatch(new Dictionary<string, string>
        {
            [name] = jsonUtil.Serialize(value) ?? throw new InvalidDataException($"Unable to serialize teammate document: {name}")
        }, backupPrevious);

    public void WriteBatch(MongoId sessionId, IReadOnlyDictionary<string, string> documents) =>
        GetDatabase(sessionId).WriteBatch(documents);

    public bool DeleteTeammate(MongoId sessionId, int aid) => GetDatabase(sessionId).DeleteTeammate(aid);

    private TeammateDatabase GetDatabase(MongoId sessionId)
    {
        string profileId = sessionId.ToString();
        lock (sync)
        {
            if (databases.TryGetValue(profileId, out var existing)) return existing;
            var database = new TeammateDatabase(RootDirectory, profileId);
            try
            {
                database.Initialize(() => LoadLegacyDocuments(profileId));
                databases.Add(profileId, database);
                TryBackupImportedDirectory(database);
                logger.Info($"Teammate database ready: '{database.DatabasePath}'.");
                return database;
            }
            catch (Exception ex)
            {
                logger.Error($"Unable to initialize teammate database '{database.DatabasePath}': {ex.Message}.");
                throw;
            }
        }
    }

    private void TryBackupImportedDirectory(TeammateDatabase database)
    {
        // DatabasePath is absolute and its profile id is validated. Both paths are siblings
        // inside the teammate storage root; rename the whole folder without changing its files.
        string directory = Path.ChangeExtension(database.DatabasePath, null);
        string backup = directory + ".backup";
        if (!Directory.Exists(directory)) return;
        try
        {
            if (Directory.Exists(backup) || File.Exists(backup))
            {
                logger.Warning($"Teammate backup path already exists: '{backup}'. Original folder retained.");
                return;
            }
            Directory.Move(directory, backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Import is already committed. Keep the database usable and retry on the next startup.
            logger.Warning($"Unable to rename teammate JSON folder '{directory}' to '{backup}': {ex.Message}");
        }
    }

    private IEnumerable<string> GetStoredProfileIds()
    {
        if (!Directory.Exists(RootDirectory)) return [];
        return Directory.GetDirectories(RootDirectory).Select(Path.GetFileName)
            .Concat(Directory.GetFiles(RootDirectory, "*.db").Select(Path.GetFileNameWithoutExtension))
            .Where(name => name != null && TeammateDatabase.IsProfileId(name))
            .Select(name => name!).Distinct().ToArray();
    }

    private IReadOnlyDictionary<string, string> LoadLegacyDocuments(string profileId)
    {
        string directory = Path.Combine(RootDirectory, profileId);
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return documents;
        foreach (string file in Directory.GetFiles(directory).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (!TeammateDatabase.IsLegacyDocument(name)) continue;
            string json = File.ReadAllText(file);
            try
            {
                ValidateLegacyDocument(name, json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Cannot import legacy teammate document '{file}': {ex.Message}", ex);
            }
            documents.Add(name, json);
        }
        return documents;
    }

    private void ValidateLegacyDocument(string name, string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (TeammateDatabase.IsProfileDocument(name))
        {
            var profile = Deserialize<BotBase>(json, name);
            int aid = int.Parse(name[..^5]);
            if (profile.Id == null || profile.Aid != aid || profile.Inventory == null || profile.Info == null)
                throw new InvalidDataException("Missing profile identity/inventory/info or account id does not match the filename.");
        }
        else if (name.EndsWith("-settings.json", StringComparison.OrdinalIgnoreCase))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Settings must be an object.");
            Deserialize<FriendlyTeammateSettings>(json, name);
        }
        else if (name.EndsWith("-equipment.json", StringComparison.OrdinalIgnoreCase))
        {
            Deserialize<List<Item>>(json, name);
        }
        else
        {
            Deserialize<List<FriendlyRecruitRequestEntry>>(json, name);
        }
    }

    private T Deserialize<T>(string json, string name) where T : class =>
        jsonUtil.Deserialize<T>(json) ?? throw new InvalidDataException($"Empty teammate document: {name}");
}