using System;
using pitTeam.Modules;

namespace pitTeam.GoingRogue
{
    internal static class GoonCommandPolicy
    {
        private static readonly CompanionCommandPolicy Assault = new CompanionCommandPolicy(true, PushChance,
            GoonCombatConfidence.PowerMultiplier, onYourOwnAggression: 70f);
        private static readonly CompanionCommandPolicy Sniper = new CompanionCommandPolicy(true, null, null, onYourOwnAggression: 70f);
        private static readonly CompanionCommandPolicy IndependentPipe = new CompanionCommandPolicy(true,
            (_, _, _) => 1f, GoonCombatConfidence.PowerMultiplier, onYourOwnAggression: 70f);

        internal static CompanionCommandPolicy ForRole(string role)
            => ForRole(role, knightPresent: true);

        internal static CompanionCommandPolicy ForRole(string role, bool knightPresent)
        {
            if (string.Equals(role, "bossKnight", StringComparison.OrdinalIgnoreCase)) return Assault;
            if (string.Equals(role, "followerBigPipe", StringComparison.OrdinalIgnoreCase))
                return knightPresent ? Assault : IndependentPipe;
            return string.Equals(role, "followerBirdEye", StringComparison.OrdinalIgnoreCase) ? Sniper : null;
        }

        private static float PushChance(float ownEquipment, float enemyEquipment, float threat)
        {
            if (float.IsNaN(ownEquipment) || float.IsInfinity(ownEquipment) ||
                float.IsNaN(enemyEquipment) || float.IsInfinity(enemyEquipment) ||
                float.IsNaN(threat) || float.IsInfinity(threat)) return 0f;

            // Confident assault bosses: 70-100% before positive shared threat reduces willingness.
            // Unknown gear is neutral. Character level, aggression and pickup personality are absent.
            float confidence = ownEquipment > 0f && enemyEquipment > 0f
                ? Math.Max(0f, Math.Min(1f, (ownEquipment / enemyEquipment - 0.55f) / 0.9f)) : 0.5f;
            return Math.Max(0.1f, Math.Min(1f, 0.7f + 0.3f * confidence - Math.Max(0f, threat) * 0.02f));
        }
    }
}
