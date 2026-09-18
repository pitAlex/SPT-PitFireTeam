using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private FollowerLootRequest ActiveLootRequest => followerData?.LootRequest;
        private bool AllowsRequestedWeaponWork => ActiveLootRequest?.AllowsWeaponWork ?? true;
        private bool AllowsRequestedGearWork => ActiveLootRequest?.AllowsGearWork ?? true;
        private bool IsRequestedWeaponPickupEnabled() =>
            ActiveLootRequest?.WantsWeapons == true || pitFireTeam.IsLootWeaponPickupEnabled();

        private IEnumerable<BodyGearCandidate> OrderRequestedLoot(IEnumerable<BodyGearCandidate> candidates)
        {
            // Stable ordering retains whole wearable roots before their fallback contents.
            return ActiveLootRequest?.WantsWeapons == true
                ? candidates.OrderBy(candidate => candidate.Item is Weapon ? 0 : 1)
                : candidates;
        }
    }
}
