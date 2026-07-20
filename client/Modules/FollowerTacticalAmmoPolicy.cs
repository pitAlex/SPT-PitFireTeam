using System;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    internal enum TacticalAmmoDecisionKind
    {
        Reject,
        Replenish,
        Upgrade
    }

    internal readonly struct TacticalAmmoDecision
    {
        public TacticalAmmoDecision(
            TacticalAmmoDecisionKind kind,
            string reason,
            int currentRounds,
            int reserveTargetRounds,
            double currentWeightedPenetration,
            int candidatePenetration,
            int candidateRounds,
            double needWeight,
            double powerWeight,
            double opportunityWeight,
            double combinedWeight)
        {
            Kind = kind;
            Reason = reason;
            CurrentRounds = currentRounds;
            ReserveTargetRounds = reserveTargetRounds;
            CurrentWeightedPenetration = currentWeightedPenetration;
            CandidatePenetration = candidatePenetration;
            CandidateRounds = candidateRounds;
            NeedWeight = needWeight;
            PowerWeight = powerWeight;
            OpportunityWeight = opportunityWeight;
            CombinedWeight = combinedWeight;
        }

        public TacticalAmmoDecisionKind Kind { get; }
        public string Reason { get; }
        public int CurrentRounds { get; }
        public int ReserveTargetRounds { get; }
        public double CurrentWeightedPenetration { get; }
        public int CandidatePenetration { get; }
        public int CandidateRounds { get; }
        public double NeedWeight { get; }
        public double PowerWeight { get; }
        public double OpportunityWeight { get; }
        public double CombinedWeight { get; }
        public bool ShouldAcquire => Kind != TacticalAmmoDecisionKind.Reject;

        public string ToDiagnosticString()
        {
            return $"decision={Kind} reason={Reason} currentRounds={CurrentRounds} " +
                   $"reserveTarget={ReserveTargetRounds} currentWeightedPen={CurrentWeightedPenetration:F2} " +
                   $"candidatePen={CandidatePenetration} candidateRounds={CandidateRounds} " +
                   $"needWeight={NeedWeight:F3} powerWeight={PowerWeight:F3} " +
                   $"opportunityWeight={OpportunityWeight:F3} combinedWeight={CombinedWeight:F3}";
        }
    }

    /// <summary>
    /// Balances ammunition shortage against cartridge quality. Once reserve is satisfied, the
    /// same model becomes an opportunity test: a better cartridge must offer enough power across
    /// enough rounds to justify replacing ammunition the follower already carries.
    /// </summary>
    internal static class FollowerTacticalAmmoPolicy
    {
        private const double MinimumCombinedNeedScore = 0.001d;
        private const double MinimumUpgradeOpportunityScore = 0.02d;

        internal static TacticalAmmoDecision Evaluate(
            int currentRounds,
            double currentWeightedPenetration,
            int candidatePenetration,
            int candidateRounds,
            int reserveTargetRounds,
            bool allowUpgrade)
        {
            int normalizedCurrent = Math.Max(0, currentRounds);
            int normalizedCandidateRounds = Math.Max(0, candidateRounds);
            int normalizedReserve = Math.Max(1, reserveTargetRounds);
            int criticalFloor = Math.Max(1, normalizedReserve / 2);
            double normalizedCurrentPenetration = Math.Max(0d, currentWeightedPenetration);
            double needWeight = Clamp01(
                (double)Math.Max(0, normalizedReserve - normalizedCurrent) / normalizedReserve);
            double powerWeight = normalizedCurrent <= 0 || normalizedCurrentPenetration <= 0d
                ? 0d
                : (candidatePenetration - normalizedCurrentPenetration) /
                  Math.Max(1d, normalizedCurrentPenetration);
            double opportunityCoverage = Clamp01(
                (double)normalizedCandidateRounds / normalizedReserve);
            double opportunityWeight = Math.Max(0d, powerWeight) * opportunityCoverage;
            double combinedWeight = needWeight + powerWeight;

            if (normalizedCandidateRounds <= 0)
            {
                return Create(
                    TacticalAmmoDecisionKind.Reject,
                    "candidateEmpty",
                    normalizedCurrent,
                    normalizedReserve,
                    normalizedCurrentPenetration,
                    candidatePenetration,
                    normalizedCandidateRounds,
                    needWeight,
                    powerWeight,
                    opportunityWeight,
                    combinedWeight);
            }

            if (normalizedCurrent <= 0)
            {
                return Create(
                    TacticalAmmoDecisionKind.Replenish,
                    "noCompatibleAmmo",
                    normalizedCurrent,
                    normalizedReserve,
                    normalizedCurrentPenetration,
                    candidatePenetration,
                    normalizedCandidateRounds,
                    needWeight,
                    powerWeight,
                    opportunityWeight,
                    combinedWeight);
            }

            // Below one ordinary magazine, survival pressure wins over cartridge preference.
            if (normalizedCurrent < criticalFloor)
            {
                return Create(
                    TacticalAmmoDecisionKind.Replenish,
                    "criticalShortage",
                    normalizedCurrent,
                    normalizedReserve,
                    normalizedCurrentPenetration,
                    candidatePenetration,
                    normalizedCandidateRounds,
                    needWeight,
                    powerWeight,
                    opportunityWeight,
                    combinedWeight);
            }

            if (normalizedCurrent < normalizedReserve)
            {
                TacticalAmmoDecisionKind kind = combinedWeight >= MinimumCombinedNeedScore
                    ? TacticalAmmoDecisionKind.Replenish
                    : TacticalAmmoDecisionKind.Reject;
                return Create(
                    kind,
                    kind == TacticalAmmoDecisionKind.Replenish
                        ? "needOutweighsQuality"
                        : "downgradeOutweighsNeed",
                    normalizedCurrent,
                    normalizedReserve,
                    normalizedCurrentPenetration,
                    candidatePenetration,
                    normalizedCandidateRounds,
                    needWeight,
                    powerWeight,
                    opportunityWeight,
                    combinedWeight);
            }

            bool worthwhileUpgrade = allowUpgrade &&
                                      powerWeight > 0d &&
                                      opportunityWeight >= MinimumUpgradeOpportunityScore;
            return Create(
                worthwhileUpgrade ? TacticalAmmoDecisionKind.Upgrade : TacticalAmmoDecisionKind.Reject,
                worthwhileUpgrade
                    ? "upgradeOpportunity"
                    : powerWeight <= 0d
                        ? "stockedWithEqualOrBetter"
                        : allowUpgrade
                            ? "upgradeTooSmall"
                            : "upgradeUnavailable",
                normalizedCurrent,
                normalizedReserve,
                normalizedCurrentPenetration,
                candidatePenetration,
                normalizedCandidateRounds,
                needWeight,
                powerWeight,
                opportunityWeight,
                combinedWeight);
        }

        internal static void RunDeterministicSelfTests()
        {
            TacticalAmmoScenario[] scenarios =
            {
                new TacticalAmmoScenario("TA-01", 60, 45d, 35, 30, 60, true, TacticalAmmoDecisionKind.Reject),
                new TacticalAmmoScenario("TA-02", 10, 45d, 35, 30, 60, true, TacticalAmmoDecisionKind.Replenish),
                new TacticalAmmoScenario("TA-03", 30, 45d, 35, 30, 60, true, TacticalAmmoDecisionKind.Replenish),
                new TacticalAmmoScenario("TA-04", 50, 45d, 35, 30, 60, true, TacticalAmmoDecisionKind.Reject),
                new TacticalAmmoScenario("TA-05", 60, 35d, 45, 30, 60, true, TacticalAmmoDecisionKind.Upgrade),
                new TacticalAmmoScenario("TA-06", 120, 45d, 46, 5, 60, true, TacticalAmmoDecisionKind.Reject),
                new TacticalAmmoScenario("TA-07", 60, 24.17d, 35, 30, 60, true, TacticalAmmoDecisionKind.Upgrade),
                new TacticalAmmoScenario("TA-08", 0, 0d, 5, 8, 60, true, TacticalAmmoDecisionKind.Replenish),
                new TacticalAmmoScenario("TA-09", 60, 35d, 45, 30, 60, false, TacticalAmmoDecisionKind.Reject)
            };

            List<string> failures = new List<string>();
            foreach (TacticalAmmoScenario scenario in scenarios)
            {
                TacticalAmmoDecision result = Evaluate(
                    scenario.CurrentRounds,
                    scenario.CurrentWeightedPenetration,
                    scenario.CandidatePenetration,
                    scenario.CandidateRounds,
                    scenario.ReserveTargetRounds,
                    scenario.AllowUpgrade);
                if (result.Kind != scenario.Expected)
                {
                    failures.Add($"{scenario.Id}: expected={scenario.Expected}; actual {result.ToDiagnosticString()}");
                }
            }

            if (failures.Count == 0)
            {
                pitFireTeam.Log.LogInfo(
                    $"[LootCommand][TacticalAmmo] Deterministic policy self-test passed ({scenarios.Length}/{scenarios.Length}).");
                return;
            }

            foreach (string failure in failures)
            {
                pitFireTeam.Log.LogError($"[LootCommand][TacticalAmmo] Policy self-test failed: {failure}");
            }
        }

        private static TacticalAmmoDecision Create(
            TacticalAmmoDecisionKind kind,
            string reason,
            int currentRounds,
            int reserveTargetRounds,
            double currentWeightedPenetration,
            int candidatePenetration,
            int candidateRounds,
            double needWeight,
            double powerWeight,
            double opportunityWeight,
            double combinedWeight)
        {
            return new TacticalAmmoDecision(
                kind,
                reason,
                currentRounds,
                reserveTargetRounds,
                currentWeightedPenetration,
                candidatePenetration,
                candidateRounds,
                needWeight,
                powerWeight,
                opportunityWeight,
                combinedWeight);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0d, Math.Min(1d, value));
        }

        private readonly struct TacticalAmmoScenario
        {
            public TacticalAmmoScenario(
                string id,
                int currentRounds,
                double currentWeightedPenetration,
                int candidatePenetration,
                int candidateRounds,
                int reserveTargetRounds,
                bool allowUpgrade,
                TacticalAmmoDecisionKind expected)
            {
                Id = id;
                CurrentRounds = currentRounds;
                CurrentWeightedPenetration = currentWeightedPenetration;
                CandidatePenetration = candidatePenetration;
                CandidateRounds = candidateRounds;
                ReserveTargetRounds = reserveTargetRounds;
                AllowUpgrade = allowUpgrade;
                Expected = expected;
            }

            public string Id { get; }
            public int CurrentRounds { get; }
            public double CurrentWeightedPenetration { get; }
            public int CandidatePenetration { get; }
            public int CandidateRounds { get; }
            public int ReserveTargetRounds { get; }
            public bool AllowUpgrade { get; }
            public TacticalAmmoDecisionKind Expected { get; }
        }
    }
}
