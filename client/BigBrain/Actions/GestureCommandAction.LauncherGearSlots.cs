using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static List<BodyGearCandidate> OrderLauncherPreferredWeaponCandidates(
            IEnumerable<BodyGearCandidate> candidates)
        {
            // A launcher is the fallback long gun. Stable ordering keeps the source's normal
            // weapon order while ensuring a conventional weapon is planned before a launcher.
            return candidates?
                .OrderBy(candidate =>
                    candidate?.Item is Weapon weapon &&
                    FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon))
                .ToList() ?? new List<BodyGearCandidate>();
        }

        private bool TryBuildGrenadeLauncherSlotNormalizationMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            IReadOnlyList<BodyGearCandidate> sourceCandidates,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            move = null;
            if (inventory == null || followerEquipment == null)
            {
                return false;
            }

            Weapon primary = followerEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            Weapon secondary = followerEquipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
            bool sourceHasLauncher = HasAvailableSourceWeapon(
                sourceCandidates,
                attemptedSourceItemIds,
                requireLauncher: true);
            bool sourceHasConventionalWeapon = HasAvailableSourceWeapon(
                sourceCandidates,
                attemptedSourceItemIds,
                requireLauncher: false);

            if (FollowerCombatCommon.IsGrenadeLauncherWeapon(primary) &&
                secondary == null &&
                sourceHasConventionalWeapon)
            {
                return TryBuildFollowerWeaponSlotMove(
                    inventory,
                    followerEquipment,
                    primary,
                    EquipmentSlot.FirstPrimaryWeapon,
                    EquipmentSlot.SecondPrimaryWeapon,
                    "launcherPrimaryDemotion",
                    rebindAsPrimaryWeapon: false,
                    out move);
            }

            if (primary == null &&
                secondary != null &&
                !FollowerCombatCommon.IsGrenadeLauncherWeapon(secondary) &&
                sourceHasLauncher)
            {
                // The left-shoulder weapon may have been held there only because it was not ready.
                // Vanilla tolerates it as an empty/under-ready primary, which frees the launcher's
                // preferred support slot without discarding either tactical weapon.
                return TryBuildFollowerWeaponSlotMove(
                    inventory,
                    followerEquipment,
                    secondary,
                    EquipmentSlot.SecondPrimaryWeapon,
                    EquipmentSlot.FirstPrimaryWeapon,
                    "conventionalPrimaryPromotionForLauncher",
                    rebindAsPrimaryWeapon: true,
                    out move);
            }

            return false;
        }

        private bool TryBuildFollowerWeaponSlotMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            EquipmentSlot sourceSlot,
            EquipmentSlot destinationSlot,
            string decisionReason,
            bool rebindAsPrimaryWeapon,
            out BodyGearMove? move)
        {
            move = null;
            if (weapon == null ||
                !TryFindEquipmentSlotAddress(followerEquipment, destinationSlot, weapon, out ItemAddress? destinationAddress))
            {
                return false;
            }

            BodyGearCandidate stagingCandidate = new BodyGearCandidate(
                weapon,
                sourceSlot,
                $"FollowerWeaponSlot.{decisionReason}",
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                reportAsLootNothing: true);
            if (!TryCreateBodyGearMove(
                    inventory,
                    stagingCandidate,
                    destinationAddress,
                    out move,
                    storeAsLoot: false,
                    successPhrase: EPhraseTrigger.LootWeapon,
                    rebindAsPrimaryWeapon: rebindAsPrimaryWeapon,
                    isStagingOperation: true,
                    stagingWeapon: weapon))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][LauncherSlots] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} source={sourceSlot} destination={destinationSlot} " +
                    $"decisionReason={decisionReason} result=moveRejected");
                return false;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][LauncherSlots] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} source={sourceSlot} destination={destinationSlot} " +
                $"decisionReason={decisionReason} result=moveBuilt");
            return true;
        }

        private bool ShouldForceConventionalPrimaryForLauncherPreference(
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IReadOnlyList<BodyGearCandidate> sourceCandidates,
            HashSet<string> attemptedSourceItemIds)
        {
            if (candidate?.Item is not Weapon weapon ||
                FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon) ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null)
            {
                return false;
            }

            Weapon secondary = followerEquipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
            return FollowerCombatCommon.IsGrenadeLauncherWeapon(secondary) ||
                   (secondary == null && HasAvailableSourceWeapon(
                       sourceCandidates,
                       attemptedSourceItemIds,
                       requireLauncher: true));
        }

        private bool HasAvailableSourceWeapon(
            IEnumerable<BodyGearCandidate> sourceCandidates,
            HashSet<string> attemptedSourceItemIds,
            bool requireLauncher)
        {
            return sourceCandidates?.Any(candidate =>
                candidate?.Item is Weapon weapon &&
                !string.IsNullOrEmpty(weapon.Id) &&
                !attemptedSourceItemIds.Contains(weapon.Id) &&
                !IsLootNowInBotInventory(BotOwner?.GetPlayer, weapon) &&
                FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon) == requireLauncher) == true;
        }

        private bool TryBuildPreferredGrenadeLauncherSecondaryMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? sourceAmmoCandidates,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate?.Item is not Weapon launcher ||
                !FollowerCombatCommon.IsGrenadeLauncherWeapon(launcher) ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon primary ||
                FollowerCombatCommon.IsGrenadeLauncherWeapon(primary) ||
                !TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    launcher,
                    out ItemAddress? secondaryAddress) ||
                !TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    secondaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    // A launcher added beside an already-working primary is support equipment.
                    // LootWeapon remains reserved for the primary created by a paired slot plan.
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                return false;
            }

            // Slot normalization only chooses the launcher's role. Ammunition remains owned by
            // the shared loose-ammo planner, after any conventional primary package was processed.
            // Its launcher policy rechecks live space for every grenade in vest, pockets,
            // backpack, then secure container order.
            move = AppendWeaponLooseAmmoSupportFollowUps(
                move,
                followerEquipment,
                launcher,
                sourceAmmoCandidates,
                "preferredLauncherSecondary");

            Modules.Logger.LogInfo(
                $"[LootCommand][LauncherSlots] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(launcher)} destination=SecondPrimaryWeapon " +
                $"decisionReason=preferredLauncherSupport result=moveBuilt ammoFollowUps={move.FollowUpCandidates.Count}");
            return true;
        }
    }
}
