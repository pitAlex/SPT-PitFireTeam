using pitTeam.Server.Models;
using pitTeam.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace pitTeam.Server.Services;

// Route callers hold FriendlyModeRequestGate: read/write stays in one mode database.
[Injectable(InjectionType.Singleton)]
public class FriendlyEncounterPenaltyService(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings)
{
    private const string Document = "friendly-encounter-penalties.json";

    public FriendlyEncounterPenaltyState Get(MongoId sessionId, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!settings.LoadSettings().IsAllegiance) return new() { ServerNowUnixMs = now };
        var state = storage.Read<FriendlyEncounterPenaltyState>(sessionId, Document) ?? new();
        if (state.Entries.RemoveAll(entry => entry.ExpiresAtUnixMs <= now) > 0)
            storage.Write(sessionId, Document, state);
        state.ServerNowUnixMs = now;
        return state;
    }

    public FriendlyEncounterPenaltyState Record(MongoId sessionId, FriendlyPostRaidKillMessageRequest request, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var state = Get(sessionId, now);
        if (!settings.LoadSettings().IsAllegiance || request.MessageKind != "traitor") return state;
        if (string.IsNullOrWhiteSpace(request.RaidId) || request.RaidId.Length > 128 ||
            string.IsNullOrWhiteSpace(request.VictimProfileId) || request.VictimProfileId.Length > 128 || request.KilledAtUnixMs <= 0)
            throw new ArgumentException("Invalid recruited-follower kill identity.");
        string id = request.RaidId + "/" + request.VictimProfileId;
        long expires = Math.Min(now, request.KilledAtUnixMs) + FriendlyEncounterPenaltyPolicy.DurationMs;
        if (expires > now && !state.Entries.Any(entry => entry.Id == id))
        {
            state.Entries.Add(new() { Id = id, ExpiresAtUnixMs = expires });
            storage.Write(sessionId, Document, state);
        }
        return state;
    }
}
