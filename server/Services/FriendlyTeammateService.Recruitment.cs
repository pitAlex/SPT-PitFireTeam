using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private const string RecruitRequestsDocument = "recruit-requests.json";
    private const string RecruitAcceptancesDocument = "accepted-recruits.json";

    internal bool AcceptRecruitInvitation(MongoId sessionId, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return false;
        lock (CreationLock)
        {
            var accepted = storage.Read<Dictionary<string, int>>(sessionId, RecruitAcceptancesDocument) ?? [];
            var pending = storage.Read<List<FriendlyRecruitRequestEntry>>(sessionId, RecruitRequestsDocument) ?? [];
            var request = pending.FirstOrDefault(entry => entry.ProfileId == profileId);
            if (request == null) return accepted.ContainsKey(profileId);
            pending.RemoveAll(entry => entry.ProfileId == profileId);

            // Repair the old save-member/consume-invite window without duplicating captured gear.
            var existing = storage.ReadProfiles(sessionId).FirstOrDefault(profile => profile.Id?.ToString() == profileId);
            if (accepted.ContainsKey(profileId) || existing?.Aid != null)
            {
                if (!accepted.ContainsKey(profileId)) accepted[profileId] = existing!.Aid!.Value;
                storage.WriteBatch(sessionId, AcceptanceDocuments(pending, accepted));
                return true;
            }

            // Reserve the account id before serializing the receipt; creation uses the same lock.
            request.AccountId = GetRecruitAccountIdOrUnique(sessionId, request.AccountId).ToString();
            accepted[profileId] = int.Parse(request.AccountId);
            CreateTeammateFromRecruitCandidate(sessionId, request, AcceptanceDocuments(pending, accepted));
            return true;
        }
    }

    internal bool AcceptAllRecruitInvitations(MongoId sessionId)
    {
        lock (CreationLock)
        {
            var pending = storage.Read<List<FriendlyRecruitRequestEntry>>(sessionId, RecruitRequestsDocument) ?? [];
            if (pending.Count == 0) return false;
            foreach (var request in pending) AcceptRecruitInvitation(sessionId, request.ProfileId);
            return true;
        }
    }

    private Dictionary<string, string> AcceptanceDocuments(List<FriendlyRecruitRequestEntry> pending,
        Dictionary<string, int> accepted) => new()
    {
        [RecruitRequestsDocument] = jsonUtil.Serialize(pending)!,
        [RecruitAcceptancesDocument] = jsonUtil.Serialize(accepted)!
    };
}
