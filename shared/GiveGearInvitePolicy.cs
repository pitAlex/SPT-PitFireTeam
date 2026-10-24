using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Shared
{
    internal static class GiveGearInvitePolicy
    {
        private static readonly HashSet<string> Slots = new HashSet<string>(StringComparer.Ordinal)
        {
            "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster", "ArmorVest", "TacticalVest",
            "Headwear", "Earpiece", "FaceCover", "Eyewear", "Backpack"
        };
        internal static int Apply(int baseChance, IEnumerable<string> acceptedSlots) => Math.Min(100,
            Math.Max(0, baseChance) + 10 * (acceptedSlots ?? Enumerable.Empty<string>())
                .Where(slot => slot != null && Slots.Contains(slot)).Distinct(StringComparer.Ordinal).Count());
    }
}
