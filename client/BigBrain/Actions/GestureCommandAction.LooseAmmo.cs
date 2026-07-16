using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static readonly EquipmentSlot[] WeaponLooseAmmoDestinationOrder =
        {
            EquipmentSlot.SecuredContainer,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.TacticalVest
        };

        private static readonly EquipmentSlot[] LauncherLooseAmmoDestinationOrder =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };

        private IEnumerable<BodyGearCandidate> GetBodyWeaponLooseAmmoCandidates(
            InventoryEquipment corpseEquipment,
            Weapon weapon)
        {
            if (corpseEquipment == null || weapon == null)
            {
                yield break;
            }

            foreach (EquipmentSlot slot in new[]
                     {
                         EquipmentSlot.TacticalVest,
                         EquipmentSlot.Pockets,
                         EquipmentSlot.Backpack
                     })
            {
                Item root = corpseEquipment.GetSlot(slot)?.ContainedItem;
                foreach (AmmoItemClass ammo in GetWeaponLooseAmmoItems(root, weapon))
                {
                    yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, $"{slot}.WeaponLooseAmmo");
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerWeaponLooseAmmoCandidates(
            SearchableItemItemClass containerRoot,
            Weapon weapon)
        {
            foreach (AmmoItemClass ammo in GetWeaponLooseAmmoItems(containerRoot, weapon))
            {
                yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, "Container.WeaponLooseAmmo");
            }
        }

        private static IEnumerable<AmmoItemClass> GetWeaponLooseAmmoItems(Item root, Weapon weapon)
        {
            if (root == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AmmoItemClass ammo in SnapshotLootTreeItems(root).OfType<AmmoItemClass>())
            {
                if (string.IsNullOrEmpty(ammo.Id) ||
                    !yieldedIds.Add(ammo.Id) ||
                    !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                {
                    continue;
                }

                yield return ammo;
            }
        }

        private IEnumerable<AmmoItemClass> GetFollowerWeaponLooseAmmoItems(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            bool includeStrictCargo)
        {
            if (followerEquipment == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            EquipmentSlot[] destinationOrder = FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon)
                ? LauncherLooseAmmoDestinationOrder
                : WeaponLooseAmmoDestinationOrder;
            foreach (EquipmentSlot slot in destinationOrder)
            {
                Item root = followerEquipment.GetSlot(slot)?.ContainedItem;
                foreach (AmmoItemClass ammo in SnapshotLootTreeItems(root).OfType<AmmoItemClass>())
                {
                    if (string.IsNullOrEmpty(ammo.Id) ||
                        !yieldedIds.Add(ammo.Id) ||
                        (!includeStrictCargo && InteractableObjects.IsStrictCargoItem(BotOwner, ammo)) ||
                        !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                    {
                        continue;
                    }

                    yield return ammo;
                }
            }
        }

        private static BodyGearCandidate CreateWeaponLooseAmmoCandidate(
            AmmoItemClass ammo,
            Weapon weapon,
            string sourceName)
        {
            return new BodyGearCandidate(
                ammo,
                null,
                sourceName,
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true,
                followUpDestination: BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                weaponSupportWeapon: weapon);
        }

        private List<BodyGearCandidate> SelectWeaponLooseAmmoSupportCandidates(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceCandidates,
            BodyGearFollowUpDestination destination,
            string evaluation)
        {
            List<BodyGearCandidate> source = sourceCandidates?
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<BodyGearCandidate>();
            if (source.Count == 0)
            {
                return source;
            }

            // Saturation intentionally counts all compatible loose rounds on the follower,
            // including manually placed strict cargo. Those rounds remain excluded from readiness,
            // but they still mean a non-Realistic follower does not need more of the same quality.
            List<AmmoItemClass> carried = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: true)
                .ToList();
            int carriedRounds = carried.Sum(ammo => Math.Max(0, ammo.StackObjectsCount));
            int stackCapacity = carried
                .Concat(source.Select(candidate => (AmmoItemClass)candidate.Item))
                .Select(ammo => Math.Max(1, ammo.StackMaxSize))
                .DefaultIfEmpty(1)
                .Max();
            int saturationRounds = stackCapacity *
                                   (FollowerWeaponLooseAmmoSupport.IsShotgun(weapon) ? 3 : 2);
            bool realistic = pitFireTeam.IsFollowerLoadoutRealisticMode();
            bool alreadySaturated = carriedRounds >= saturationRounds;

            List<BodyGearCandidate> accepted = new List<BodyGearCandidate>();
            foreach (BodyGearCandidate candidate in source)
            {
                AmmoItemClass ammo = (AmmoItemClass)candidate.Item;
                AmmoItemClass bestSameCaliber = carried
                    .Where(existing => FollowerWeaponLooseAmmoSupport.IsSameCaliber(ammo, existing))
                    .OrderByDescending(existing => existing.PenetrationPower)
                    .ThenByDescending(existing => existing.Damage)
                    .ThenByDescending(existing => existing.ArmorDamage)
                    .FirstOrDefault();
                bool betterAmmo = bestSameCaliber == null ||
                                  FollowerWeaponLooseAmmoSupport.IsMorePowerful(ammo, bestSameCaliber);
                bool shouldTake = realistic || !alreadySaturated || betterAmmo;
                Modules.Logger.LogInfo(
                    $"[LootCommand][LooseAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluation} ammo={DescribeLooseAmmo(ammo)} " +
                    $"carriedRounds={carriedRounds} saturationRounds={saturationRounds} mixedStacks={carried.Count} " +
                    $"realistic={realistic} betterSameCaliber={betterAmmo} decision={(shouldTake ? "take" : "skipSaturated")}");
                if (shouldTake)
                {
                    accepted.Add(candidate.WithFollowUpDestination(destination));
                }
            }

            return accepted;
        }

        private BodyGearMove AppendWeaponLooseAmmoSupportFollowUps(
            BodyGearMove move,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceCandidates,
            string evaluation)
        {
            if (move == null)
            {
                return move;
            }

            List<BodyGearCandidate> looseAmmo = SelectWeaponLooseAmmoSupportCandidates(
                followerEquipment,
                weapon,
                sourceCandidates,
                BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                evaluation);
            if (looseAmmo.Count == 0)
            {
                return move;
            }

            List<BodyGearCandidate> followUps = move.FollowUpCandidates.ToList();
            followUps.AddRange(looseAmmo);
            return move.WithFollowUps(
                followUps,
                move.SuccessPhrase,
                move.ContinueFollowUpsOnFailure);
        }

        private bool TryBuildWeaponLooseAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            bool requireWeaponOnFollower,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidAmmo";
            if (candidate?.Item is not AmmoItemClass ammo ||
                candidate.WeaponSupportWeapon is not Weapon weapon ||
                ammo.StackObjectsCount <= 0 ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, ammo) ||
                !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
            {
                return false;
            }

            if (requireWeaponOnFollower && !IsLootNowInBotInventory(BotOwner?.GetPlayer, weapon))
            {
                reason = "weaponNotAcquired";
                return false;
            }

            EquipmentSlot[] destinationOrder = FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon)
                ? LauncherLooseAmmoDestinationOrder
                : WeaponLooseAmmoDestinationOrder;
            foreach (EquipmentSlot slot in destinationOrder)
            {
                if (slot == EquipmentSlot.TacticalVest &&
                    !CanPlaceAmmoInVestWithReloadReserve(followerEquipment, ammo))
                {
                    continue;
                }

                if (!TryFindDirectEquipmentContainerAddress(
                        followerEquipment,
                        slot,
                        ammo,
                        out ItemAddress? address) ||
                    !TryCreateBodyGearMove(
                        inventory,
                        candidate,
                        address,
                        out move,
                        storeAsLoot: ShouldReturnGearSwapAsCargo(),
                        successPhrase: EPhraseTrigger.LootGeneric))
                {
                    continue;
                }

                reason = $"ok:{slot}";
                Modules.Logger.LogInfo(
                    $"[LootCommand][LooseAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} ammo={DescribeLooseAmmo(ammo)} destination={slot}");
                return true;
            }

            reason = "noProtectedDestinationSpace";
            return false;
        }

        private bool TrySimulateWeaponLooseAmmoPlacement(
            Weapon weapon,
            AmmoItemClass ammo,
            VestReloadReserveSet vestReloadReserves,
            ref SearchableItemItemClass? secure,
            ref SearchableItemItemClass? pockets,
            ref SearchableItemItemClass? backpack,
            ref SearchableItemItemClass? vest)
        {
            if (FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon))
            {
                // Launcher rounds must remain reload-accessible whenever possible. Each simulated
                // placement mutates its cloned container, so later one-round grenades naturally
                // spill from vest to pockets to backpack before secure storage is considered.
                if (TrySimulateContainerAdd(vest, ammo, out SearchableItemItemClass? nextLauncherVest) &&
                    CanFitVestReloadReserves(nextLauncherVest, vestReloadReserves))
                {
                    vest = nextLauncherVest;
                    return true;
                }

                if (TrySimulateContainerAdd(pockets, ammo, out SearchableItemItemClass? nextLauncherPockets))
                {
                    pockets = nextLauncherPockets;
                    return true;
                }

                if (TrySimulateContainerAdd(backpack, ammo, out SearchableItemItemClass? nextLauncherBackpack))
                {
                    backpack = nextLauncherBackpack;
                    return true;
                }

                if (TrySimulateContainerAdd(secure, ammo, out SearchableItemItemClass? nextLauncherSecure))
                {
                    secure = nextLauncherSecure;
                    return true;
                }

                return false;
            }

            if (TrySimulateContainerAdd(secure, ammo, out SearchableItemItemClass? nextSecure))
            {
                secure = nextSecure;
                return true;
            }

            if (TrySimulateContainerAdd(pockets, ammo, out SearchableItemItemClass? nextPockets))
            {
                pockets = nextPockets;
                return true;
            }

            if (TrySimulateContainerAdd(backpack, ammo, out SearchableItemItemClass? nextBackpack))
            {
                backpack = nextBackpack;
                return true;
            }

            if (TrySimulateContainerAdd(vest, ammo, out SearchableItemItemClass? nextVest) &&
                CanFitVestReloadReserves(nextVest, vestReloadReserves))
            {
                vest = nextVest;
                return true;
            }

            return false;
        }

        private static string DescribeLooseAmmo(AmmoItemClass ammo)
        {
            return ammo == null
                ? "none"
                : $"{DescribeLootDebugItem(ammo)}:caliber={ammo.Caliber}:rounds={ammo.StackObjectsCount}:" +
                  $"stackMax={ammo.StackMaxSize}:pen={ammo.PenetrationPower}:damage={ammo.Damage}:armorDamage={ammo.ArmorDamage}";
        }
    }
}
