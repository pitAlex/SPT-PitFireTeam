using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public static class FriendlyTeammateRaidHealthPolicy
{
    public static bool Apply(BotBaseHealth? saved, FriendlyTeammateRaidHealth? raid, bool escaped,
        double blackedMultiplier, double deathMultiplier, double now)
    {
        // Missing/partial payloads from older clients or failed snapshots must not replace HP.
        foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
            if (saved?.BodyParts?.GetValueOrDefault(part)?.Health?.Maximum is not double maximum
                || !double.IsFinite(maximum) || maximum <= 0
                || raid?.BodyParts?.GetValueOrDefault(part) is not { } incoming
                || !double.IsFinite(incoming.Current) || !double.IsFinite(incoming.SpawnMaximum) || incoming.SpawnMaximum <= 0)
                return false;
        if (!double.IsFinite(blackedMultiplier) || !double.IsFinite(deathMultiplier)) return false;

        foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
        {
            var target = saved!.BodyParts![part];
            var value = raid!.BodyParts![part];
            double fraction = escaped ? Math.Clamp(value.Current / value.SpawnMaximum, 0, 1) : Math.Clamp(deathMultiplier, 0, 1);
            if (escaped && fraction == 0) fraction = Math.Clamp(blackedMultiplier, 0, 1);
            target.Health!.Current = target.Health.Maximum!.Value * fraction;
            // The user's policy: retain HP only. No bleeding, fracture, destroyed-part,
            // exhaustion, pain, stimulant or other persistent effect crosses the raid boundary.
            target.Effects = null;
        }
        saved!.UpdateTime = now;
        return true;
    }
}
