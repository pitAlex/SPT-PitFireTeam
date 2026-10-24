using System;
using System.Collections.Generic;

namespace pitTeam.Shared
{
    public static class PmcKarmaPolicy
    {
        public const double FriendlyKillLoss = 0.025;
        public const double RecruitExtractionGain = 0.02;
        public const double PeacefulRaidGain = 0.005;

        public static double Apply(double karma, double delta) =>
            Math.Round(Math.Max(0d, Math.Min(1d, karma + delta)), 6);

        public static double RaidGain(int escapedRecruits, bool allegiance, bool hadFriendlies,
            bool killedFriendly, bool recruitedAny) => escapedRecruits > 0
                ? escapedRecruits * RecruitExtractionGain
                : allegiance && hadFriendlies && !killedFriendly && !recruitedAny ? PeacefulRaidGain : 0d;
    }

    public class PmcKarmaReport
    {
        public string RaidId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string VictimProfileId { get; set; } = string.Empty;
        public bool Allegiance { get; set; }
        public bool HadFriendlies { get; set; }
        public bool KilledFriendly { get; set; }
        public bool RecruitedAny { get; set; }
        public List<string> ExtractedRecruitIds { get; set; } = new List<string>();
    }

    public sealed class PmcKarmaResult
    {
        public double KarmaValue { get; set; }
        // -1 negative sound, 0 silent, +1 positive sound. Replays return the same decision.
        public int Sound { get; set; }
    }
}
