using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private FollowerLootRequest ActiveLootRequest => followerData?.LootRequest;
        // Selective requests build only the chosen weapon packages in the ordinary candidate loop.
        // Do not spend their one-shot override on unrelated equipped-weapon maintenance/promotions.
        private bool AllowsRequestedWeaponWork => (ActiveLootRequest?.AllowsWeaponWork ?? true) &&
            ActiveLootRequest?.SelectiveWeapons != true;
        private bool AllowsRequestedGearWork => ActiveLootRequest?.AllowsGearWork ?? true;
        private bool IsRequestedWeaponPickupEnabled() =>
            ActiveLootRequest?.WantsWeapons == true || pitFireTeam.IsLootWeaponPickupEnabled();

        private IEnumerable<BodyGearCandidate> OrderRequestedLoot(IEnumerable<BodyGearCandidate> candidates)
        {
            if (ActiveLootRequest?.SelectiveWeapons == true)
            {
                return candidates.OrderBy(candidate => candidate.Item?.Id == ActiveLootRequest.SelectedLongGunId ? 0 :
                    candidate.Item?.Id == ActiveLootRequest.SelectedPistolId ? 1 : 2);
            }
            // Stable ordering retains whole wearable roots before their fallback contents.
            return ActiveLootRequest?.WantsWeapons == true
                ? candidates.OrderBy(candidate => candidate.Item is Weapon ? 0 : 1)
                : candidates;
        }

        private void InitializeBodyWeaponSelection(InventoryEquipment corpse, InventoryEquipment follower)
        {
            if (ActiveLootRequest == null || ActiveLootRequest.WeaponSelectionInitialized) return;
            var primary = corpse.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            var secondary = corpse.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
            var pistol = corpse.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
            ActiveLootRequest.InitializeWeaponSelection(pitFireTeam.IsLootWeaponPickupEnabled(),
                primary?.Id, secondary?.Id, pistol?.Id,
                follower.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem == null ||
                follower.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem == null,
                follower.GetSlot(EquipmentSlot.Holster)?.ContainedItem == null);
            Modules.Logger.LogInfo($"[LootCommand][WeaponSelection] follower='{BotOwner?.Profile?.Nickname}' " +
                $"selective={ActiveLootRequest.SelectiveWeapons} longGun={ActiveLootRequest.SelectedLongGunId ?? "none"} " +
                $"pistol={ActiveLootRequest.SelectedPistolId ?? "none"} equipLongGun={ActiveLootRequest.SelectedLongGunCanEquip}");
        }
    }
}
