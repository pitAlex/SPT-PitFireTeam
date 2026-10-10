using SPTarkov.Server.Core.Models.Common;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    // Called under the route gate after SPT accepts the launcher's account wipe.
    public void WipeProfile(MongoId sessionId)
    {
        lock (CreationLock)
        {
            storage.WipeProfile(sessionId);
            foreach (string key in profileRecoveryNotices.Keys
                .Where(key => key.StartsWith(sessionId + ":", StringComparison.OrdinalIgnoreCase)).ToArray())
                profileRecoveryNotices.Remove(key);
            foreach (string mode in new[] { "GunsForHire", "Allegiance" })
            {
                string key = mode + "/" + sessionId;
                startupRecoveryNotices.Remove(key);
                duplicateRecoveryCheckedSessions.Remove(key);
            }
            FriendlyModeRequestGate.EndRaid(sessionId.ToString());
        }
    }
}
