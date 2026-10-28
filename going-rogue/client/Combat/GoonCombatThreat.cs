using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.GoingRogue.Combat
{
    internal static class GoonCombatThreat
    {
        internal static bool IsLowThreat(BotOwner owner, EnemyInfo enemy, FollowerCombatCommon common)
        {
            if (enemy == null || !common.HasActiveCombatEnemy(enemy)) return false;
            var role = enemy.Person?.Profile?.Info?.Settings?.Role;
            if (!role.HasValue || GoonCombatConfidence.PowerMultiplier(role.Value.ToString()) == 1f)
                return common.IsEnemyLowThreat(enemy, ignoreEquip: false, maximumEnemies: 2f);

            if (Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(enemy) ||
                !FollowerEnemyTracking.TryGetKnownPosition(enemy, out _, out _) ||
                common.ShouldBlockProactiveAutoPushForWeaponThreat(enemy)) return false;

            return GoonCombatConfidence.IsLowThreat(owner.AIData?.PowerOfEquipment ?? 0f,
                enemy.Person?.AIData?.PowerOfEquipment ?? 0f, role.Value.ToString(),
                pitAIBossPlayer.CountKnownAdvanceEnemies(owner, enemy),
                FollowerCombatRiflemanEngagement.GetCombatRoleThreatMultiplier(role.Value),
                (int)common.GetAutoPushWeaponThreatPolicy(enemy));
        }
    }
}
