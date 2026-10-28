using System;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue
{
    // Relative combat confidence only: never changes equipment, skills or damage.
    internal static class GoonCombatConfidence
    {
        internal static float PowerMultiplier(string enemyRole)
        {
            if (string.Equals(enemyRole, "pmcUSEC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enemyRole, "pmcBEAR", StringComparison.OrdinalIgnoreCase)) return 1.5f;
            if (string.Equals(enemyRole, "assault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enemyRole, "assaultGroup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enemyRole, "cursedAssault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enemyRole, "marksman", StringComparison.OrdinalIgnoreCase)) return 2f;
            return 1f;
        }

        internal static bool IsLowThreat(float ownPower, float enemyPower, string enemyRole,
            int knownEnemies, float roleThreat, int weaponThreat)
        {
            if (float.IsNaN(ownPower) || float.IsInfinity(ownPower) || ownPower <= 1f ||
                float.IsNaN(enemyPower) || float.IsInfinity(enemyPower) || enemyPower <= 0f ||
                float.IsNaN(roleThreat) || float.IsInfinity(roleThreat) || knownEnemies < 1 || knownEnemies > 2)
                return false;
            float effectivePower = ownPower * PowerMultiplier(enemyRole);
            if (float.IsInfinity(effectivePower)) return false;
            return FollowerPushRiskPolicy.Threat(FollowerPushRiskPolicy.EquipmentRatio(effectivePower, enemyPower),
                roleThreat, knownEnemies, weaponThreat) <= 0f;
        }
    }
}
