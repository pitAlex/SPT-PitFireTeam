using System.Text.Json.Nodes;
using pitTeam.Server.Persistence;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace pitTeam.GoingRogue.Server;

// Retired mode data is recovery material, never an active third roster.
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 3)]
public sealed class LegacyGoonMigration(FileUtil files, ISptLogger<LegacyGoonMigration> logger) : IOnLoad
{
    internal const string Receipt = "migration/going-rogue-companions-v1";
    private static readonly string[] Roles = ["bossKnight", "followerBigPipe", "followerBirdEye"];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        string resources = Path.Combine(files.GetModPath("pitFireTeam-ServerMod"), "Resources");
        string legacy = Path.Combine(resources, "teammates-going-rogue");
        if (!Directory.Exists(legacy)) return Task.CompletedTask;
        foreach (string path in Directory.GetFiles(legacy, "*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string profile = Path.GetFileNameWithoutExtension(path);
            if (!TeammateDatabase.IsProfileId(profile)) continue;
            // A conflict is explicit: do not silently create a replacement companion.
            int count = Migrate(resources, profile);
            if (count > 0) logger.Info($"[GoingRogue] Migrated {count} companion(s) into Allegiance for {profile}.");
        }
        return Task.CompletedTask;
    }

    public static int Migrate(string resources, string profile)
    {
        var source = new TeammateDatabase(Path.Combine(resources, "teammates-going-rogue"), profile);
        if (!File.Exists(source.DatabasePath)) return 0;
        var target = new TeammateDatabase(Path.Combine(resources, "teammates-allegiance"), profile);
        target.Initialize(() => new Dictionary<string, string>());
        if (target.Read(Receipt) != null) return 0;
        var documents = Plan(source.ReadDocuments(), target.ReadDocuments());
        if (documents.Count == 0) return 0;
        string backup = target.DatabasePath + ".before-going-rogue-" + Guid.NewGuid().ToString("N") + ".bak";
        File.Copy(target.DatabasePath, backup);
        target.WriteBatch(documents);
        foreach (var (name, value) in documents)
            if (target.Read(name) != value) throw new InvalidDataException("Companion migration verification failed: " + name);
        return documents.Keys.Count(TeammateDatabase.IsProfileDocument);
    }

    internal static Dictionary<string, string> Plan(IReadOnlyDictionary<string, string> source, IReadOnlyDictionary<string, string> target)
    {
        if (target.ContainsKey(Receipt)) return [];
        string? Role(string json) => JsonNode.Parse(json)?["Info"]?["Settings"]?["Role"]?.GetValue<string>();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var migratingRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, json) in source.Where(entry => TeammateDatabase.IsProfileDocument(entry.Key)))
        {
            string? role = Role(json);
            if (!Roles.Contains(role, StringComparer.OrdinalIgnoreCase)) continue;
            string aid = name[..^5];
            var profile = JsonNode.Parse(json)!;
            if (profile["aid"]?.ToString() != aid || !migratingRoles.Add(role!))
                throw new InvalidDataException("Invalid or duplicate legacy companion identity: " + name);
            var itemIds = (profile["Inventory"]?["items"] as JsonArray ?? [])
                .Select(item => item?["_id"]?.ToString()).Where(id => id != null).ToHashSet();
            foreach (var other in target.Where(entry => TeammateDatabase.IsProfileDocument(entry.Key) && entry.Key != name))
            {
                var otherProfile = JsonNode.Parse(other.Value)!;
                if (profile["_id"] != null && profile["_id"]!.ToString() == otherProfile["_id"]?.ToString() ||
                    (otherProfile["Inventory"]?["items"] as JsonArray ?? []).Any(item => itemIds.Contains(item?["_id"]?.ToString())))
                    throw new InvalidDataException("Companion profile/item identity collision with " + other.Key);
            }
            if (target.Any(entry => TeammateDatabase.IsProfileDocument(entry.Key) && entry.Key != name &&
                string.Equals(Role(entry.Value), role, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Allegiance already has a different {role}; companion migration requires reconciliation.");
            foreach (var entry in source.Where(entry => entry.Key == name || entry.Key.StartsWith(aid + "-", StringComparison.Ordinal) ||
                entry.Key == $"addon-companion-{role!.ToLowerInvariant()}.json" ||
                entry.Key == $"addon-quest-companion-{role!.ToLowerInvariant()}.json"))
            {
                if (target.TryGetValue(entry.Key, out var existing) && existing != entry.Value)
                    throw new InvalidDataException("Allegiance migration document collision: " + entry.Key);
                result[entry.Key] = entry.Value;
            }
        }
        // Preserve the chosen quest bag, without importing penalties, normal recruits or old inboxes.
        const string placement = "knight-launcher-placement.json";
        if (source.TryGetValue(placement, out var selected))
        {
            if (target.TryGetValue(placement, out var existing) && existing != selected)
                throw new InvalidDataException("Allegiance already has a different launcher placement.");
            result[placement] = selected;
        }
        result[Receipt] = "{\"completed\":true}";
        return result;
    }
}
