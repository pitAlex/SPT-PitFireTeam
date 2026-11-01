using System;
using pitTeam.Modules;

namespace pitTeam.GoingRogue
{
    internal static class GoonGearPolicy
    {
        private static readonly CompanionGearPolicy Policy = new CompanionGearPolicy(
            keepEquipment: true, allowVestPlateUpgrades: true, secondaryScoreTolerance: 0.10f,
            allowBodyContainerLootCommands: false);

        internal static CompanionGearPolicy ForRole(string role) =>
            (string.Equals(role, "bossKnight", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(role, "followerBigPipe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(role, "followerBirdEye", StringComparison.OrdinalIgnoreCase)) ? Policy : null;
    }
}
