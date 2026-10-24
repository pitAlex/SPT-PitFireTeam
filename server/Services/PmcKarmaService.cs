using pitTeam.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

// Callers hold FriendlyModeRequestGate. Karma and receipts live in the player's PMC
// profile, shared across modes; neither follower database nor Fence standing is touched.
[Injectable(InjectionType.Singleton)]
public class PmcKarmaService(SaveServer saveServer, FileUtil fileUtil, JsonUtil jsonUtil)
{
    private const string ReceiptPrefix = "pitFireTeam/pmc-karma/";

    public async ValueTask<PmcKarmaResult> Record(MongoId sessionId, PmcKarmaReport request)
    {
        if (string.IsNullOrWhiteSpace(request.RaidId) || request.RaidId.Length > 128 ||
            request.Kind is not ("kill" or "end")) throw new ArgumentException("Invalid PMC karma report.");
        if (request.Kind == "kill" && (string.IsNullOrWhiteSpace(request.VictimProfileId) || request.VictimProfileId.Length > 128))
            throw new ArgumentException("Missing friendly victim identity.");
        var recruits = (request.ExtractedRecruitIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (recruits.Length > 32 || recruits.Any(id => id.Length > 128)) throw new ArgumentException("Invalid recruited extraction identities.");

        var profile = saveServer.GetProfile(sessionId);
        var pmc = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("PMC profile unavailable.");
        string key = ReceiptPrefix + request.RaidId + "/" + request.Kind +
            (request.Kind == "kill" ? "/" + request.VictimProfileId : string.Empty);
        profile.SptData ??= new Spt();
        profile.SptData.Migrations ??= [];
        if (!profile.SptData.Migrations.TryGetValue(key, out long sound))
        {
            double before = pmc.KarmaValue ?? throw new InvalidOperationException("PMC karma unavailable.");
            if (!double.IsFinite(before)) throw new InvalidOperationException("Invalid PMC karma.");
            double delta = request.Kind == "kill" ? -PmcKarmaPolicy.FriendlyKillLoss :
                PmcKarmaPolicy.RaidGain(recruits.Length, request.Allegiance, request.HadFriendlies,
                    request.KilledFriendly, request.RecruitedAny);
            double after = PmcKarmaPolicy.Apply(before, delta);
            sound = after == before ? 0 : request.Kind == "kill" ? -1 : recruits.Length > 0 ? 1 : 0;
            pmc.KarmaValue = after;
            // This existing profile receipt namespace commits alongside the native value.
            profile.SptData.Migrations[key] = sound;
        }

        if (saveServer.IsProfileInvalidOrUnloadable(sessionId)) throw new IOException("PMC profile is not saveable.");
        await saveServer.SaveProfileAsync(sessionId);
        // SPT caches a save hash before writing. A failed write can make retries skip the
        // file, so never acknowledge or sound a change that exists only in memory.
        var saved = jsonUtil.Deserialize<SptProfile>(fileUtil.ReadFile(Path.Combine("user", "profiles", $"{sessionId}.json")));
        if (saved?.CharacterData?.PmcData?.KarmaValue != pmc.KarmaValue ||
            saved?.SptData?.Migrations?.ContainsKey(key) != true)
            throw new IOException("PMC karma persistence was not confirmed.");
        return new() { KarmaValue = pmc.KarmaValue ?? throw new IOException("PMC karma unavailable after save."), Sound = (int)sound };
    }
}
