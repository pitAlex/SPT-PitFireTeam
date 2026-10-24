using EFT;
using System.Collections.Generic;

namespace pitTeam.Utils
{
    // Follower-local admission and failed-use protection, independent of combat episodes.
    internal sealed class FollowerFirstAidTopOffPolicy
    {
        private sealed class Attempt
        {
            public float Health;
            public float Supplies;
            public float RetryAt;
            public int Failures;
            public bool Pending;
        }

        private readonly Dictionary<EBodyPart, Attempt> attempts = new Dictionary<EBodyPart, Attempt>();

        internal static bool NeedsTreatment(EBodyPart part, float current, float maximum, bool fullRecovery)
        {
            if (maximum <= 0f || current <= 0f) return false;
            float missing = maximum - current;
            if (fullRecovery) return missing > 0.5f;
            float threshold = IsVital(part) ? 0.95f : 0.9f;
            return missing >= 2f && current / maximum < threshold;
        }

        internal static bool IsVital(EBodyPart part) => part == EBodyPart.Head || part == EBodyPart.Chest;

        internal void Observe(EBodyPart part, float health, float supplies, float now)
        {
            if (!attempts.TryGetValue(part, out Attempt attempt)) return;
            if (attempt.Pending)
            {
                attempt.Pending = false;
                if (health > attempt.Health + 0.5f) attempt.Failures = 0;
                else { attempt.Failures++; attempt.RetryAt = now + 3f; }
            }
            // Consumption alone cannot rearm a failed part. New damage, recovery by another
            // provider, or replenished usable supplies may legitimately change the outcome.
            if (health < attempt.Health - 1f || health > attempt.Health + 0.5f || supplies > attempt.Supplies + 1f)
            { attempt.Failures = 0; attempt.RetryAt = 0f; attempt.Health = health; attempt.Supplies = supplies; }
        }

        internal bool HasWork(EBodyPart part) =>
            !attempts.TryGetValue(part, out Attempt attempt) || (!attempt.Pending && attempt.Failures < 2);

        internal bool CanAttempt(EBodyPart part, float now) =>
            HasWork(part) && (!attempts.TryGetValue(part, out Attempt attempt) || now >= attempt.RetryAt);

        internal void BeginAttempt(EBodyPart part, float health, float supplies)
        {
            if (!attempts.TryGetValue(part, out Attempt attempt))
            { attempt = new Attempt(); attempts.Add(part, attempt); }
            attempt.Health = health; attempt.Supplies = supplies; attempt.Pending = true;
        }
    }
}
