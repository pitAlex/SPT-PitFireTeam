using System;

namespace pitTeam.Modules
{
    // Optional modes supply immutable restrictions; Core owns inventory transactions.
    public sealed class CompanionGearPolicy
    {
        public bool KeepEquipment { get; }
        public bool AllowVestPlateUpgrades { get; }
        public float SecondaryScoreTolerance { get; }
        public bool AllowBodyContainerLootCommands { get; }

        public CompanionGearPolicy(bool keepEquipment, bool allowVestPlateUpgrades,
            float secondaryScoreTolerance)
            : this(keepEquipment, allowVestPlateUpgrades, secondaryScoreTolerance, true) { }

        public CompanionGearPolicy(bool keepEquipment, bool allowVestPlateUpgrades,
            float secondaryScoreTolerance, bool allowBodyContainerLootCommands)
        {
            if (float.IsNaN(secondaryScoreTolerance) || secondaryScoreTolerance < 0f || secondaryScoreTolerance > 1f)
                throw new ArgumentOutOfRangeException(nameof(secondaryScoreTolerance));
            KeepEquipment = keepEquipment;
            AllowVestPlateUpgrades = allowVestPlateUpgrades;
            SecondaryScoreTolerance = secondaryScoreTolerance;
            AllowBodyContainerLootCommands = allowBodyContainerLootCommands;
        }

        public bool AllowsSecondaryScore(float primary, float candidate) =>
            primary > 0f && !float.IsInfinity(primary) && !float.IsInfinity(candidate) &&
            Math.Abs(candidate - primary) <= primary * SecondaryScoreTolerance;
    }
}
