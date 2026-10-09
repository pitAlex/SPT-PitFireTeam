using System;
using System.Collections.Generic;
using System.Globalization;

namespace pitTeam.Shared
{
    public sealed class FriendlyEncounterPenaltyEntry
    {
        public string Id { get; set; } = string.Empty;
        public long ExpiresAtUnixMs { get; set; }
    }

    public sealed class FriendlyEncounterPenaltyState
    {
        public long ServerNowUnixMs { get; set; }
        public List<FriendlyEncounterPenaltyEntry> Entries { get; set; } = new List<FriendlyEncounterPenaltyEntry>();
    }

    // Real elapsed time determines expiry; only the displayed countdown is quantized.
    public static class FriendlyEncounterPenaltyPolicy
    {
        public const int PointsPerKill = 5;
        public const long DurationMs = 24 * 60 * 60 * 1000L;
        private const long HourMs = 60 * 60 * 1000L;

        public static int GetPoints(IEnumerable<FriendlyEncounterPenaltyEntry> entries, long nowUnixMs, int chanceMultiplier = 1)
        {
            int points = 0;
            foreach (var entry in entries)
                if (entry.ExpiresAtUnixMs > nowUnixMs) points += PointsPerKill;
            return ScalePoints(points, chanceMultiplier);
        }

        public static int ScalePoints(int basePoints, int chanceMultiplier) =>
            basePoints * Math.Max(1, Math.Min(5, chanceMultiplier));

        public static float Apply(float chance, int penaltyPoints) =>
            Math.Max(0f, Math.Min(1f, chance - penaltyPoints / 100f));

        public static string GetCountdown(long expiresAtUnixMs, long nowUnixMs, out bool hours, out long nextUpdateUnixMs)
        {
            long remaining = expiresAtUnixMs - nowUnixMs;
            hours = remaining >= HourMs;
            nextUpdateUnixMs = expiresAtUnixMs;
            if (remaining <= 0) return string.Empty;
            long step = remaining > 2 * HourMs ? HourMs / 2 : hours ? HourMs / 10 : HourMs / 60;
            long buckets = (remaining + step - 1) / step;
            nextUpdateUnixMs = expiresAtUnixMs - (buckets - 1) * step;
            // Cross display-unit/precision boundaries at the real threshold.
            if (remaining > 2 * HourMs) nextUpdateUnixMs = Math.Min(nextUpdateUnixMs, expiresAtUnixMs - 2 * HourMs);
            else if (remaining > HourMs) nextUpdateUnixMs = Math.Min(nextUpdateUnixMs, expiresAtUnixMs - HourMs);
            else if (remaining == HourMs) nextUpdateUnixMs = nowUnixMs + 1;
            return (hours ? buckets * step / (double)HourMs : buckets)
                .ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
