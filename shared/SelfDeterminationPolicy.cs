using System;

namespace pitTeam.Shared
{
    // Pure calibration shared by persistence, Core and the optional addon.
    public static class SelfDeterminationPolicy
    {
        public const float Default = 50f;
        public static float Normalize(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? Default : Math.Max(0f, Math.Min(100f, value));
        public static float BossInfluence(float value) => 2f * (1f - Normalize(value) / 100f);
        public static bool AllowsAutomaticPosition(float value, float currentBossDistanceSqr, float destinationBossDistanceSqr) =>
            Normalize(value) > 0f || destinationBossDistanceSqr <= 100f || destinationBossDistanceSqr < currentBossDistanceSqr;
        public static float Trigger(float value, float baseline)
        {
            float d = Normalize(value);
            return d <= 50f ? 10f + (baseline - 10f) * d / 50f : baseline * (1f + (d - 50f) / 50f);
        }
        public static float Completion(float value, float trigger, float orderedDistance, bool marksman)
        {
            float d = Normalize(value), midpoint = marksman ? orderedDistance : Math.Min(10f, orderedDistance);
            float preferred = d <= 50f ? 6f + (midpoint - 6f) * d / 50f
                : midpoint + (orderedDistance - midpoint) * (d - 50f) / 50f;
            return Math.Max(2f, Math.Min(preferred, trigger - 2f));
        }
    }
}
