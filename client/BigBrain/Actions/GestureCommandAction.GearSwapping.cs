using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryStartEasyBodyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetBodyWeaponEquipCandidates(corpseEquipment))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                Weapon candidateWeapon = (Weapon)swapCandidate.Item;
                IEnumerable<BodyGearCandidate> magazineCandidates =
                    GetBodyOperationalMagazineCandidates(corpseEquipment, candidateWeapon);
                IEnumerable<BodyGearCandidate> ammoCandidates =
                    GetBodyWeaponLooseAmmoCandidates(corpseEquipment, candidateWeapon);
                bool builtMove = TryBuildEasyWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    swapCandidate,
                    magazineCandidates,
                    ammoCandidates,
                    out BodyGearMove? move,
                    out bool handledByGearPolicy);

                if (!builtMove)
                {
                    if (handledByGearPolicy)
                    {
                        // The no-fast-access policy is terminal. Do not let ordinary price/category
                        // looting move this weapon somewhere the gear planner explicitly rejected.
                        bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                    }

                    continue;
                }

                // Loading a source magazine into an empty weapon is only a staging move. Keep the
                // weapon eligible so the next planning pass evaluates its new live loaded state
                // through the normal inserted-magazine path.
                if (!move.IsStagingOperation)
                {
                    bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }
                if (TryQueueBodyLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartBodyGearMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartEasyBodyTacticalVestMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            Item vest = corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (vest is not VestItemClass || string.IsNullOrEmpty(vest.Id))
            {
                return false;
            }

            BodyGearCandidate cargoCandidate = new BodyGearCandidate(
                vest,
                EquipmentSlot.TacticalVest,
                "TacticalVest.EquipmentUpgrade",
                2);
            BodyGearCandidate swapCandidate = CreateGearSwapCandidate(cargoCandidate);
            if (!CanConsiderFilteredLootCandidate(swapCandidate, bodyLootAttemptedItemIds) ||
                IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
            {
                return false;
            }

            if (TryBuildTacticalVestEquipMove(inventory, followerEquipment, swapCandidate, out BodyGearMove? equipMove))
            {
                if (ReferenceEquals(equipMove.Item, swapCandidate.Item))
                {
                    bodyLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }

                if (TryQueueBodyLootMoveAfterPickupSuccess(equipMove))
                {
                    return true;
                }

                StartBodyGearMove(inventory, equipMove);
                return true;
            }

            // If the found vest is a real protection upgrade but cannot be safely equipped without
            // disturbing current rig contents, treat it as cargo instead of throwing the old vest.
            if (CanConsiderFilteredLootCandidate(cargoCandidate, bodyLootAttemptedItemIds) &&
                IsPotentialTacticalVestProtectionUpgrade(followerEquipment, vest) &&
                TryBuildFilteredLootMove(inventory, followerEquipment, cargoCandidate, null, null, out BodyGearMove? cargoMove))
            {
                bodyLootAttemptedItemIds.Add(cargoCandidate.Item.Id);
                if (TryQueueBodyLootMoveAfterPickupSuccess(cargoMove))
                {
                    return true;
                }

                StartBodyGearMove(inventory, cargoMove);
                return true;
            }

            return false;
        }

        private bool TryStartEasyContainerWeaponEquipMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetContainerWeaponEquipCandidates(containerRoot))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                Weapon candidateWeapon = (Weapon)swapCandidate.Item;
                IEnumerable<BodyGearCandidate> magazineCandidates =
                    GetContainerOperationalMagazineCandidates(containerRoot, candidateWeapon);
                IEnumerable<BodyGearCandidate> ammoCandidates =
                    GetContainerWeaponLooseAmmoCandidates(containerRoot, candidateWeapon);
                bool builtMove = TryBuildEasyWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    swapCandidate,
                    magazineCandidates,
                    ammoCandidates,
                    out BodyGearMove? move,
                    out bool handledByGearPolicy);

                if (!builtMove)
                {
                    if (handledByGearPolicy)
                    {
                        containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                    }

                    continue;
                }

                if (!move.IsStagingOperation)
                {
                    containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }
                if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartBodySecondaryWeaponPromotionMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryWeaponPromotionChain(
                    inventory,
                    followerEquipment,
                    weapon => GetBodyOperationalMagazineCandidates(corpseEquipment, weapon),
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    out BodyGearMove? move))
            {
                return false;
            }

            bodyLootAttemptedItemIds.Add(move.Item.Id);
            if (TryQueueBodyLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryStartContainerSecondaryWeaponPromotionMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryWeaponPromotionChain(
                    inventory,
                    followerEquipment,
                    weapon => GetContainerOperationalMagazineCandidates(containerRoot, weapon),
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    out BodyGearMove? move))
            {
                return false;
            }

            containerLootAttemptedItemIds.Add(move.Item.Id);
            if (TryQueueContainerLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartContainerLootMove(inventory, move);
            return true;
        }

        private bool TryBuildSecondaryWeaponPromotionChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceMagazineFactory,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            move = null;
            Weapon supportWeapon = followerEquipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem as Weapon;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceMagazineFactory == null ||
                sourceAmmoFactory == null ||
                followerEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null ||
                supportWeapon == null ||
                !IsEasyWeaponEquipCandidate(new BodyGearCandidate(supportWeapon, null, "FollowerSecondPrimary", 0)) ||
                !InteractableObjects.IsLootedWeapon(BotOwner, supportWeapon))
            {
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(supportWeapon))
            {
                IEnumerable<BodyGearCandidate> internalAmmoCandidates = sourceAmmoFactory(supportWeapon)
                    .Where(candidate =>
                        candidate?.Item != null &&
                        !string.IsNullOrEmpty(candidate.Item.Id) &&
                        !attemptedSourceItemIds.Contains(candidate.Item.Id));
                return TryBuildInternalExistingWeaponPromotionChain(
                    inventory,
                    followerEquipment,
                    supportWeapon,
                    internalAmmoCandidates,
                    BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion,
                    "secondaryInternalSourcePromotion",
                    out move);
            }

            if (!HasInsertedMagazine(supportWeapon))
            {
                return false;
            }

            List<BodyGearCandidate> sourceMagazineCandidates = sourceMagazineFactory(supportWeapon)
                .Where(candidate =>
                    candidate?.Item != null &&
                    !string.IsNullOrEmpty(candidate.Item.Id) &&
                    !attemptedSourceItemIds.Contains(candidate.Item.Id))
                .ToList();
            if (sourceMagazineCandidates.Count == 0)
            {
                return false;
            }

            List<BodyGearCandidate> backpackMagazineCandidates =
                GetFollowerBackpackOperationalMagazineCandidates(followerEquipment, supportWeapon).ToList();
            OperationalMagazinePlan magazinePlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                supportWeapon,
                sourceMagazineCandidates.Concat(backpackMagazineCandidates));
            List<BodyGearCandidate> fastAccessCandidates = magazinePlan.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .ToList();
            List<MagazineItemClass> projectedFastAccessMagazines = fastAccessCandidates
                .Select(candidate => candidate.Item)
                .OfType<MagazineItemClass>()
                .ToList();
            WeaponPrimaryReadinessSnapshot projected = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                supportWeapon,
                projectedFastAccessMagazines);

            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(supportWeapon)} evaluation=secondarySourcePromotionProjection " +
                $"sourceMags={sourceMagazineCandidates.Count} backpackMags={backpackMagazineCandidates.Count} " +
                $"plannedFastAccess={fastAccessCandidates.Count} {projected.ToDiagnosticString()}");
            if (!projected.PrimaryReady || projected.RequiresMagazineLoad)
            {
                return false;
            }

            HashSet<string> sourceMagazineIds = new HashSet<string>(
                sourceMagazineCandidates.Select(candidate => candidate.Item.Id),
                StringComparer.Ordinal);
            for (int firstIndex = 0; firstIndex < fastAccessCandidates.Count; firstIndex++)
            {
                BodyGearCandidate firstCandidate = fastAccessCandidates[firstIndex];
                // A later source spare is the trigger. Move it successfully before reorganizing
                // existing backpack cargo or touching the support weapon slot.
                if (!sourceMagazineIds.Contains(firstCandidate.Item.Id) ||
                    !TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        firstCandidate,
                        out BodyGearMove? firstMagazineMove,
                        out _))
                {
                    continue;
                }

                List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                for (int i = 0; i < fastAccessCandidates.Count; i++)
                {
                    if (i != firstIndex)
                    {
                        followUps.Add(fastAccessCandidates[i]);
                    }
                }

                BodyGearCandidate promotionCandidate = CreateGearSwapCandidate(
                        new BodyGearCandidate(
                            supportWeapon,
                            EquipmentSlot.SecondPrimaryWeapon,
                            "FollowerSecondPrimary.SupportWeaponPromotion",
                            0))
                    .WithFollowUpDestination(BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion);
                followUps.Add(promotionCandidate);
                AppendOverflowMagazineAmmoSalvageMarkers(
                    followUps,
                    supportWeapon,
                    magazinePlan.CompatibleLoadedCandidates);
                move = firstMagazineMove.WithFollowUps(followUps, EPhraseTrigger.LootWeapon);
                Modules.Logger.LogInfo(
                    $"[LootCommand] Secondary weapon promotion chain built for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"weapon={DescribeLootDebugItem(supportWeapon)} firstSourceMag={DescribeLootDebugItem(firstCandidate.Item)} " +
                    $"remainingFastAccessMags={fastAccessCandidates.Count - 1}");
                return true;
            }

            return false;
        }

        private bool TryStartBodyBackpackCargoWeaponPromotionMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildBackpackCargoWeaponPromotionChain(
                    inventory,
                    followerEquipment,
                    weapon => GetBodyOperationalMagazineCandidates(corpseEquipment, weapon),
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    out BodyGearMove? move))
            {
                return false;
            }

            bodyLootAttemptedItemIds.Add(move.Item.Id);
            if (TryQueueBodyLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryStartContainerBackpackCargoWeaponPromotionMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildBackpackCargoWeaponPromotionChain(
                    inventory,
                    followerEquipment,
                    weapon => GetContainerOperationalMagazineCandidates(containerRoot, weapon),
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    out BodyGearMove? move))
            {
                return false;
            }

            containerLootAttemptedItemIds.Add(move.Item.Id);
            if (TryQueueContainerLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartContainerLootMove(inventory, move);
            return true;
        }

        private bool TryBuildBackpackCargoWeaponPromotionChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceMagazineFactory,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            move = null;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceMagazineFactory == null ||
                sourceAmmoFactory == null ||
                followerEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null)
            {
                return false;
            }

            foreach (Weapon cargoWeapon in GetPromotableBackpackCargoWeapons(followerEquipment))
            {
                if (FollowerWeaponLooseFeedReadiness.IsSupported(cargoWeapon))
                {
                    IEnumerable<BodyGearCandidate> internalAmmoCandidates = sourceAmmoFactory(cargoWeapon)
                        .Where(candidate =>
                            candidate?.Item != null &&
                            !string.IsNullOrEmpty(candidate.Item.Id) &&
                            !attemptedSourceItemIds.Contains(candidate.Item.Id));
                    if (TryBuildInternalExistingWeaponPromotionChain(
                            inventory,
                            followerEquipment,
                            cargoWeapon,
                            internalAmmoCandidates,
                            BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion,
                            "cargoInternalSourcePromotion",
                            out move))
                    {
                        return true;
                    }

                    continue;
                }

                List<BodyGearCandidate> sourceMagazineCandidates = sourceMagazineFactory(cargoWeapon)
                    .Where(candidate =>
                        candidate?.Item != null &&
                        !string.IsNullOrEmpty(candidate.Item.Id) &&
                        !attemptedSourceItemIds.Contains(candidate.Item.Id))
                    .ToList();
                if (sourceMagazineCandidates.Count == 0)
                {
                    continue;
                }

                List<BodyGearCandidate> backpackMagazineCandidates =
                    GetFollowerBackpackOperationalMagazineCandidates(followerEquipment, cargoWeapon).ToList();
                OperationalMagazinePlan magazinePlan = PlanOperationalMagazineFollowUps(
                    inventory,
                    followerEquipment,
                    cargoWeapon,
                    sourceMagazineCandidates.Concat(backpackMagazineCandidates));
                List<BodyGearCandidate> fastAccessCandidates = magazinePlan.FollowUps
                    .Where(IsOperationalFastAccessFollowUp)
                    .ToList();
                List<MagazineItemClass> projectedFastAccessMagazines = fastAccessCandidates
                    .Select(candidate => candidate.Item)
                    .OfType<MagazineItemClass>()
                    .ToList();
                WeaponPrimaryReadinessSnapshot projected = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                    inventory,
                    cargoWeapon,
                    projectedFastAccessMagazines);
                HashSet<string> sourceMagazineIds = new HashSet<string>(
                    sourceMagazineCandidates.Select(candidate => candidate.Item.Id),
                    StringComparer.Ordinal);

                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(cargoWeapon)} evaluation=cargoPromotionProjection " +
                    $"sourceMags={sourceMagazineCandidates.Count} backpackMags={backpackMagazineCandidates.Count} " +
                    $"plannedFastAccess={fastAccessCandidates.Count} {projected.ToDiagnosticString()}");

                if (!projected.PrimaryReady || projected.RequiresMagazineLoad)
                {
                    continue;
                }

                for (int firstIndex = 0; firstIndex < fastAccessCandidates.Count; firstIndex++)
                {
                    BodyGearCandidate firstCandidate = fastAccessCandidates[firstIndex];
                    if (!sourceMagazineIds.Contains(firstCandidate.Item.Id) ||
                        !TryBuildSupportMagazineFollowUpMove(
                            inventory,
                            followerEquipment,
                            firstCandidate,
                            out BodyGearMove? firstMagazineMove,
                            out string firstMagazineReason))
                    {
                        continue;
                    }

                    List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                    for (int i = 0; i < fastAccessCandidates.Count; i++)
                    {
                        if (i != firstIndex)
                        {
                            followUps.Add(fastAccessCandidates[i]);
                        }
                    }

                    BodyGearCandidate promotionCandidate = CreateGearSwapCandidate(
                            new BodyGearCandidate(
                                cargoWeapon,
                                null,
                                "FollowerBackpack.CargoWeaponPromotion",
                                0))
                        .WithFollowUpDestination(BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion);
                    followUps.Add(promotionCandidate);
                    AppendOverflowMagazineAmmoSalvageMarkers(
                        followUps,
                        cargoWeapon,
                        magazinePlan.CompatibleLoadedCandidates);
                    move = firstMagazineMove.WithFollowUps(followUps, EPhraseTrigger.LootWeapon);
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Cargo weapon promotion chain built for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"weapon={DescribeLootDebugItem(cargoWeapon)} firstSourceMag={DescribeLootDebugItem(firstCandidate.Item)} " +
                        $"remainingFastAccessMags={fastAccessCandidates.Count - 1}");
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<Weapon> GetPromotableBackpackCargoWeapons(InventoryEquipment followerEquipment)
        {
            Item backpack = followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack == null)
            {
                yield break;
            }

            HashSet<string> yieldedWeaponIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Weapon weapon in SnapshotLootTreeItems(backpack).OfType<Weapon>())
            {
                if (weapon == null ||
                    string.IsNullOrEmpty(weapon.Id) ||
                    !yieldedWeaponIds.Add(weapon.Id) ||
                    !IsEasyWeaponEquipCandidate(new BodyGearCandidate(weapon, null, "FollowerBackpack", 0)) ||
                    !HasWeaponFeedForPromotion(weapon) ||
                    !InteractableObjects.IsLootedWeapon(BotOwner, weapon) ||
                    InteractableObjects.IsStrictCargoItem(BotOwner, weapon))
                {
                    continue;
                }

                yield return weapon;
            }
        }

        private bool TryStartEasyContainerTacticalVestMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!pitFireTeam.IsLootGearSwappingEnabled())
            {
                return false;
            }

            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                         containerRoot,
                         "Container.Contents",
                         skipMagazines: false).Where(IsTacticalVestEquipCandidate))
            {
                BodyGearCandidate swapCandidate = CreateGearSwapCandidate(candidate);
                if (!CanConsiderFilteredLootCandidate(swapCandidate, containerLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, swapCandidate.Item))
                {
                    continue;
                }

                if (!TryBuildTacticalVestEquipMove(inventory, followerEquipment, swapCandidate, out BodyGearMove? move))
                {
                    continue;
                }

                if (ReferenceEquals(move.Item, swapCandidate.Item))
                {
                    containerLootAttemptedItemIds.Add(swapCandidate.Item.Id);
                }

                if (TryQueueContainerLootMoveAfterPickupSuccess(move))
                {
                    return true;
                }

                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private static bool IsEasyWeaponEquipCandidate(BodyGearCandidate candidate)
        {
            return candidate?.Item is Weapon weapon && IsShoulderWeaponCandidate(weapon);
        }

        private static bool IsShoulderWeaponCandidate(Weapon weapon)
        {
            if (weapon == null ||
                weapon.GetItemComponent<KnifeComponent>() != null ||
                weapon is PistolItemClass)
            {
                return false;
            }

            // EFT uses WeapClass to distinguish holster revolvers from shoulder-fired
            // revolver mechanisms such as the MTs shotgun, launchers, and custom rifles.
            return weapon is not RevolverItemClass ||
                   !string.Equals(weapon.WeapClass, "pistol", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHolsterWeapon(Weapon weapon)
        {
            return weapon is PistolItemClass ||
                   (weapon is RevolverItemClass &&
                    string.Equals(weapon.WeapClass, "pistol", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<BodyGearCandidate> GetBodyWeaponEquipCandidates(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                yield break;
            }

            HashSet<string> yieldedItemIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (EquipmentSlot slot in BodyGearWeaponSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (TryCreateWeaponEquipCandidate(item, slot, slot.ToString(), 2, yieldedItemIds, out BodyGearCandidate? candidate))
                {
                    yield return candidate;
                }
            }

            // Weapon equip is its own tactical scenario. It must not depend on generic body-loot
            // filtering, where vest/pocket magazines are intentionally skipped as cargo.
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pockets, EquipmentSlot.TacticalVest })
            {
                foreach (BodyGearCandidate candidate in GetWeaponEquipCandidatesFromRoot(
                             corpseEquipment.GetSlot(slot)?.ContainedItem,
                             $"{slot}.WeaponEquip",
                             1,
                             yieldedItemIds))
                {
                    yield return candidate;
                }
            }
        }

        private static IEnumerable<BodyGearCandidate> GetContainerWeaponEquipCandidates(SearchableItemItemClass containerRoot)
        {
            HashSet<string> yieldedItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in GetWeaponEquipCandidatesFromRoot(
                         containerRoot,
                         "Container.WeaponEquip",
                         1,
                         yieldedItemIds))
            {
                yield return candidate;
            }
        }

        private static IEnumerable<BodyGearCandidate> GetWeaponEquipCandidatesFromRoot(
            Item root,
            string sourceName,
            int sourceTier,
            HashSet<string> yieldedItemIds)
        {
            if (root == null)
            {
                yield break;
            }

            if (TryCreateWeaponEquipCandidate(root, null, sourceName, sourceTier, yieldedItemIds, out BodyGearCandidate? rootCandidate))
            {
                yield return rootCandidate;
            }

            foreach (Item item in SnapshotLootTreeItems(root))
            {
                if (ReferenceEquals(item, root))
                {
                    continue;
                }

                if (TryCreateWeaponEquipCandidate(item, null, sourceName, sourceTier, yieldedItemIds, out BodyGearCandidate? candidate))
                {
                    yield return candidate;
                }
            }
        }

        private static bool TryCreateWeaponEquipCandidate(
            Item item,
            EquipmentSlot? sourceSlot,
            string sourceName,
            int sourceTier,
            HashSet<string> yieldedItemIds,
            out BodyGearCandidate? candidate)
        {
            candidate = null;
            if (item == null || string.IsNullOrEmpty(item.Id))
            {
                return false;
            }

            BodyGearCandidate possibleCandidate = new BodyGearCandidate(item, sourceSlot, sourceName, sourceTier);
            if (!IsEasyWeaponEquipCandidate(possibleCandidate) || !yieldedItemIds.Add(item.Id))
            {
                return false;
            }

            candidate = possibleCandidate;
            return true;
        }

        private static bool IsTacticalVestEquipCandidate(BodyGearCandidate candidate)
        {
            return candidate?.Item is VestItemClass;
        }

        private static BodyGearCandidate CreateGearSwapCandidate(BodyGearCandidate candidate)
        {
            // Let the equipment planner inspect this tree without ordinary cargo price/category
            // filters. Weapon policy separately reapplies Pickup Gear to an optional support add;
            // missing-primary acquisition and future true primary swaps remain equipment decisions.
            // Protection checks and executable inventory placement still apply downstream.
            return new BodyGearCandidate(
                item: candidate.Item,
                sourceSlot: candidate.SourceSlot,
                sourceName: candidate.SourceName,
                sourceTier: candidate.SourceTier,
                skipMagazine: candidate.SkipMagazine,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: candidate.BypassBodyGearLootability,
                reportAsLootNothing: candidate.ReportAsLootNothing);
        }

        private bool TryBuildTacticalVestEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                candidate?.Item is not VestItemClass foundVest)
            {
                return false;
            }

            if (TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out move))
            {
                return true;
            }

            if (!CanReplaceOccupiedGearSlot())
            {
                return false;
            }

            Item currentVest = followerEquipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (!IsSafeTacticalVestSwapCandidate(followerEquipment, currentVest, foundVest) ||
                !TryFindBackpackAddressForItem(followerEquipment, currentVest, out ItemAddress? preserveAddress) ||
                !TryCreateBodyGearMove(
                    inventory,
                    new BodyGearCandidate(currentVest, EquipmentSlot.TacticalVest, "TacticalVest.PreserveOld", 0, reportAsLootNothing: true),
                    preserveAddress,
                    out BodyGearMove? preserveMove,
                    storeAsLoot: false))
            {
                return false;
            }

            move = preserveMove.WithFollowUps(new[] { candidate });
            return true;
        }

        private bool TryBuildTacticalVestEquipIntoEmptySlot(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            return TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.TacticalVest, candidate.Item, out ItemAddress? vestAddress) &&
                   TryCreateBodyGearMove(inventory, candidate, vestAddress, out move, storeAsLoot: ShouldReturnGearSwapAsCargo());
        }

        private static bool IsSafeTacticalVestSwapCandidate(InventoryEquipment followerEquipment, Item currentVest, Item foundVest)
        {
            if (followerEquipment == null ||
                currentVest == null ||
                foundVest == null ||
                HasOperationalTacticalVestContents(currentVest))
            {
                return false;
            }

            return IsPotentialTacticalVestProtectionUpgrade(followerEquipment, foundVest);
        }

        private static bool IsPotentialTacticalVestProtectionUpgrade(InventoryEquipment followerEquipment, Item foundVest)
        {
            if (followerEquipment == null ||
                foundVest == null ||
                !TryGetTacticalVestProtectionScore(foundVest, out float foundScore))
            {
                return false;
            }

            Item armorVest = followerEquipment.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem;
            Item currentVest = followerEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            bool currentProtected = TryGetTacticalVestProtectionScore(currentVest, out float currentScore);
            if (!currentProtected && armorVest == null)
            {
                return true;
            }

            return currentProtected && foundScore > currentScore + 25f;
        }

        private static bool HasOperationalTacticalVestContents(Item vest)
        {
            return GetDirectLootChildren(vest).Any(item => item is not ArmorPlateItemClass);
        }

        private static bool TryGetTacticalVestProtectionScore(Item vest, out float score)
        {
            score = 0f;
            if (vest == null)
            {
                return false;
            }

            foreach (ArmorComponent armor in vest.GetItemComponentsInChildren<ArmorComponent>(true))
            {
                RepairableComponent repairable = armor?.Repairable;
                if (repairable == null || repairable.TemplateDurability <= 0 || repairable.Durability <= 1f)
                {
                    continue;
                }

                float durabilityRatio = Mathf.Clamp01(repairable.Durability / repairable.TemplateDurability);
                score = Mathf.Max(score, armor.ArmorClass * 100f + durabilityRatio * 100f);
            }

            return score > 0f;
        }

        private bool TryBuildEasyWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            IEnumerable<BodyGearCandidate>? operationalAmmoCandidates,
            out BodyGearMove? move,
            out bool handledByGearPolicy)
        {
            move = null;
            handledByGearPolicy = false;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                candidate?.Item is not Weapon weapon ||
                !IsEasyWeaponEquipCandidate(candidate))
            {
                return false;
            }

            bool primaryOccupied = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem is Weapon;
            if (primaryOccupied && !pitFireTeam.IsLootGearPickupEnabled())
            {
                // The current occupied-primary phase can only add a support weapon. A future
                // better-primary comparison must run before this support-only gate; until then,
                // Pickup Gear remains authoritative even when second primary is empty.
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation=secondaryAddRejected " +
                    $"destination=Source decisionReason=pickupGearDisabled");
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return TryBuildInternalMagazineWeaponEquipChain(
                    inventory,
                    followerEquipment,
                    candidate,
                    operationalAmmoCandidates,
                    out move,
                    out handledByGearPolicy);
            }

            List<BodyGearCandidate> sourceLooseAmmoCandidates = operationalAmmoCandidates?
                .ToList() ?? new List<BodyGearCandidate>();

            // A working primary makes an empty second-primary slot a real vanilla support role.
            // This is an add, not a replacement: source magazines may join only when they fit in
            // fast access with the inserted-magazine landing reserve still intact.
            if (primaryOccupied)
            {
                if (TryBuildWorkingPrimarySecondaryWeaponEquipChain(
                        inventory,
                        followerEquipment,
                        candidate,
                        operationalMagazineCandidates,
                        out move))
                {
                    move = AppendWeaponLooseAmmoSupportFollowUps(
                        move,
                        followerEquipment,
                        weapon,
                        sourceLooseAmmoCandidates,
                        "newSecondaryWeapon");
                    handledByGearPolicy = true;
                    return true;
                }

                return false;
            }

            // The current primary phase only fills an empty slot. Replacing an existing primary is
            // deferred because vanilla bot weapon/reload state is cached beyond the physical item.
            if (!TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.FirstPrimaryWeapon, weapon, out _))
            {
                return false;
            }

            handledByGearPolicy = true;

            List<BodyGearCandidate> sourceMagazineCandidates =
                operationalMagazineCandidates?.ToList() ?? new List<BodyGearCandidate>();
            OperationalMagazinePlan magazinePlan = SelectNewWeaponMagazinePlan(
                inventory,
                followerEquipment,
                weapon,
                sourceMagazineCandidates);
            LogOperationalMagazinePlan(weapon, magazinePlan);
            LogPrimaryReadinessShadow(inventory, weapon, magazinePlan);

            if (HasInsertedMagazine(weapon))
            {
                WeaponPrimaryReadinessSnapshot projected = EvaluateMagazinePlanProjection(
                    inventory,
                    weapon,
                    magazinePlan);
                bool secondaryOccupied = followerEquipment
                    ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                    ?.ContainedItem != null;
                if (!projected.PrimaryReady && secondaryOccupied)
                {
                    // The gear planner cannot equip this candidate. Leave it untouched so the
                    // ordinary Pickup Gear + price path may still take it as backpack cargo.
                    handledByGearPolicy = false;
                    Modules.Logger.LogInfo(
                        $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(weapon)} evaluation=gearCandidateRejected " +
                        $"destination=OrdinaryCargo decisionReason=secondaryOccupied {projected.ToDiagnosticString()}");
                    return false;
                }

                if (!TryBuildInsertedMagazineWeaponEquipChain(
                        inventory,
                        followerEquipment,
                        candidate,
                        magazinePlan,
                        out move))
                {
                    return false;
                }

                move = AppendWeaponLooseAmmoSupportFollowUps(
                    move,
                    followerEquipment,
                    weapon,
                    sourceLooseAmmoCandidates,
                    "newPrimaryWeapon");
                return true;
            }

            if (TryBuildEmptyWeaponMagazineInsertionMove(
                    inventory,
                    candidate,
                    magazinePlan,
                    out move,
                    out string emptyMagazineReason))
            {
                return true;
            }

            // Insertion is a prerequisite transaction, not a projected equipment decision. If it
            // cannot be built, ordinary Pickup Gear and price still own potential cargo handling.
            handledByGearPolicy = false;
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=gearCandidateRejected " +
                $"destination=OrdinaryCargo decisionReason={emptyMagazineReason}");
            return false;
        }

        private bool TryBuildWorkingPrimarySecondaryWeaponEquipChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            IEnumerable<BodyGearCandidate>? operationalMagazineCandidates,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate?.Item is not Weapon weapon ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon ||
                followerEquipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem != null ||
                !HasInsertedMagazine(weapon))
            {
                return false;
            }

            OperationalMagazinePlan magazinePlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                operationalMagazineCandidates);
            List<BodyGearCandidate> fastAccessCandidates = magazinePlan.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .ToList();
            List<MagazineItemClass> projectedFastAccessMagazines = fastAccessCandidates
                .Select(followUp => followUp.Item)
                .OfType<MagazineItemClass>()
                .ToList();
            WeaponPrimaryReadinessSnapshot projected = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                weapon,
                projectedFastAccessMagazines);

            // Secondary does not need the two-magazine primary threshold. It only needs a real
            // ammunition source: rounds already inserted, an existing compatible fast-access mag,
            // or one of the source magazines this executable chain will move into fast access.
            bool projectedUsable = projected.InsertedRounds > 0 ||
                                   projected.FastAccessMagazineRounds.Any(rounds => rounds > 0);
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=workingPrimarySecondaryProjection " +
                $"plannedFastAccess={fastAccessCandidates.Count} usable={projectedUsable} {projected.ToDiagnosticString()}");
            if (!projectedUsable)
            {
                return false;
            }

            for (int firstIndex = 0; firstIndex < fastAccessCandidates.Count; firstIndex++)
            {
                BodyGearCandidate firstMagazineCandidate = fastAccessCandidates[firstIndex];
                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        firstMagazineCandidate,
                        out BodyGearMove? firstMagazineMove,
                        out string firstMagazineReason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Secondary-equip first move rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"weapon={DescribeLootDebugItem(weapon)} mag={DescribeLootDebugItem(firstMagazineCandidate.Item)} " +
                        $"reason={firstMagazineReason}");
                    continue;
                }

                List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                for (int i = 0; i < fastAccessCandidates.Count; i++)
                {
                    if (i != firstIndex)
                    {
                        followUps.Add(fastAccessCandidates[i]);
                    }
                }

                // Overflow magazines remain loaded at the source. Ammo salvage is reserved for a
                // weapon that settles into FirstPrimaryWeapon, never this support-only branch.
                followUps.Add(candidate.WithFollowUpDestination(BodyGearFollowUpDestination.SecondaryWeaponEquip));
                move = firstMagazineMove.WithFollowUps(
                    followUps,
                    EPhraseTrigger.LootGeneric,
                    continueOnFailure: true);
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation=workingPrimarySecondaryChainBuilt " +
                    $"firstMag={DescribeLootDebugItem(firstMagazineCandidate.Item)} " +
                    $"remainingFastAccessMags={fastAccessCandidates.Count - 1} destination=SecondPrimaryWeapon");
                return true;
            }

            // No source move is needed when the inserted magazine or the follower's existing fast
            // access already makes the support weapon usable.
            if (!TryBuildOperationalSecondaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out _))
            {
                return false;
            }

            return true;
        }

        private bool TryBuildEmptyWeaponMagazineInsertionMove(
            InventoryController inventory,
            BodyGearCandidate weaponCandidate,
            OperationalMagazinePlan sourceMagazinePlan,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "noCompatibleLoadedSourceMagazine";
            if (weaponCandidate?.Item is not Weapon weapon || HasInsertedMagazine(weapon))
            {
                reason = "weaponStateChanged";
                return false;
            }

            Slot magazineSlot;
            try
            {
                magazineSlot = weapon.GetMagazineSlot();
            }
            catch
            {
                reason = "magazineSlotUnavailable";
                return false;
            }

            if (magazineSlot == null)
            {
                reason = "magazineSlotUnavailable";
                return false;
            }

            // Prefer the most-loaded source magazine. We never detach a magazine from another
            // weapon and do not borrow manually supplied follower cargo for this first insertion.
            List<BodyGearCandidate> loadCandidates = sourceMagazinePlan?.CompatibleLoadedCandidates
                .Where(candidate =>
                    candidate?.Item is MagazineItemClass &&
                    !IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                .OrderByDescending(candidate => ((MagazineItemClass)candidate.Item).Count)
                .ThenByDescending(candidate => ((MagazineItemClass)candidate.Item).MaxCount)
                .ToList() ?? new List<BodyGearCandidate>();

            foreach (BodyGearCandidate loadCandidate in loadCandidates)
            {
                MagazineItemClass magazineToLoad = loadCandidate.Item as MagazineItemClass;
                if (magazineToLoad == null)
                {
                    continue;
                }

                BodyGearCandidate loadMoveCandidate = loadCandidate.WithFollowUpDestination(
                    BodyGearFollowUpDestination.LoadMagazineIntoWeapon);
                if (!TryCreateBodyGearMove(
                        inventory,
                        loadMoveCandidate,
                        magazineSlot.CreateItemAddress(),
                        out BodyGearMove? loadMove,
                        storeAsLoot: ShouldReturnGearSwapAsCargo(),
                        successPhrase: EPhraseTrigger.LootWeapon,
                        isStagingOperation: true,
                        stagingWeapon: weapon))
                {
                    reason = "magazineLoadOperationRejected";
                    continue;
                }

                // Stop after the real insertion. The next normal planning pass sees the magazine
                // inside the weapon, applies the established readiness/destination policy, and only
                // afterward salvages ammo from compatible magazines that remain at the source.
                move = loadMove;
                reason = "magazineInsertionStaged";
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation=magazineInsertionMoveBuilt " +
                    $"loadMag={DescribeLootDebugItem(magazineToLoad)} destination=WeaponMagazineSlot");
                return true;
            }

            return false;
        }

        private OperationalMagazinePlan SelectNewWeaponMagazinePlan(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IReadOnlyList<BodyGearCandidate> sourceMagazineCandidates)
        {
            OperationalMagazinePlan sourceOnlyPlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                sourceMagazineCandidates);
            if (!HasInsertedMagazine(weapon))
            {
                return sourceOnlyPlan;
            }

            List<BodyGearCandidate> backpackMagazineCandidates =
                GetFollowerBackpackOperationalMagazineCandidates(followerEquipment, weapon).ToList();
            if (backpackMagazineCandidates.Count == 0)
            {
                return sourceOnlyPlan;
            }

            OperationalMagazinePlan combinedPlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                sourceMagazineCandidates.Concat(backpackMagazineCandidates));
            List<MagazineItemClass> projectedFastAccessMagazines = combinedPlan.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .Select(candidate => candidate.Item)
                .OfType<MagazineItemClass>()
                .ToList();
            WeaponPrimaryReadinessSnapshot projected = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                weapon,
                projectedFastAccessMagazines);
            bool recruitBackpackMagazines = projected.PrimaryReady && !projected.RequiresMagazineLoad;
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=newWeaponBackpackRecruitment " +
                $"sourceMags={sourceMagazineCandidates.Count} backpackMags={backpackMagazineCandidates.Count} " +
                $"decision={(recruitBackpackMagazines ? "moveToFastAccess" : "retainBackpackCargo")} " +
                projected.ToDiagnosticString());

            // Backpack cargo is reorganized only when the complete executable plan makes this
            // weapon a usable primary. Otherwise preserve operational space and leave cargo put.
            return recruitBackpackMagazines ? combinedPlan : sourceOnlyPlan;
        }

        private bool TryBuildInsertedMagazineWeaponEquipChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            OperationalMagazinePlan magazinePlan,
            out BodyGearMove? move)
        {
            move = null;
            // P2 moves only magazines that were proven to fit in vanilla fast access while
            // retaining reload landing space. Backpack candidates remain ordinary cargo loot.
            List<BodyGearCandidate> fastAccessCandidates = magazinePlan.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .ToList();

            List<MagazineItemClass> projectedFastAccessMagazines = fastAccessCandidates
                .Select(followUp => followUp.Item)
                .OfType<MagazineItemClass>()
                .ToList();
            WeaponPrimaryReadinessSnapshot projected = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                candidate.Item as Weapon,
                projectedFastAccessMagazines);
            for (int firstIndex = 0; firstIndex < fastAccessCandidates.Count; firstIndex++)
            {
                BodyGearCandidate firstMagazineCandidate = fastAccessCandidates[firstIndex];
                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        firstMagazineCandidate,
                        out BodyGearMove? firstMagazineMove,
                        out string firstMagazineReason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Inserted-mag first move rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"weapon={DescribeLootDebugItem(candidate.Item)} mag={DescribeLootDebugItem(firstMagazineCandidate.Item)} " +
                        $"reason={firstMagazineReason}");
                    continue;
                }

                List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                for (int i = 0; i < fastAccessCandidates.Count; i++)
                {
                    if (i != firstIndex)
                    {
                        followUps.Add(fastAccessCandidates[i]);
                    }
                }

                // Classify the weapon only after all fast-access transfers settle. Late salvage
                // markers run afterward and independently verify that the result is first primary.
                followUps.Add(candidate.WithFollowUpDestination(BodyGearFollowUpDestination.EvaluateWeaponDestination));
                AppendOverflowMagazineAmmoSalvageMarkers(
                    followUps,
                    candidate.Item as Weapon,
                    magazinePlan.CompatibleLoadedCandidates);
                bool projectedUsablePrimary =
                    projected.PrimaryReady &&
                    !projected.RequiresMagazineLoad &&
                    (projected.InsertedContribution >= projected.Threshold ||
                     FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(
                         followerEquipment,
                         candidate.Item as Weapon));
                move = firstMagazineMove.WithFollowUps(
                    followUps,
                    projectedUsablePrimary ? EPhraseTrigger.LootWeapon : EPhraseTrigger.LootGeneric,
                    continueOnFailure: true);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Inserted-mag live-evaluation chain built for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"weapon={DescribeLootDebugItem(candidate.Item)} firstMag={DescribeLootDebugItem(firstMagazineCandidate.Item)} " +
                    $"remainingFastAccessMags={fastAccessCandidates.Count - 1} " +
                    $"lootCue={(projectedUsablePrimary ? EPhraseTrigger.LootWeapon : EPhraseTrigger.LootGeneric)}");
                return true;
            }

            if (!TryBuildPostTransferWeaponDestinationMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out _,
                    out _))
            {
                return false;
            }

            List<BodyGearCandidate> directSalvageMarkers = new List<BodyGearCandidate>();
            AppendOverflowMagazineAmmoSalvageMarkers(
                directSalvageMarkers,
                candidate.Item as Weapon,
                magazinePlan.CompatibleLoadedCandidates);
            if (directSalvageMarkers.Count > 0)
            {
                move = move.WithFollowUps(directSalvageMarkers);
            }

            return true;
        }

        private static bool CanBuildPotentialPrimaryWeaponCargoPackage(
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate)
        {
            // Package magazines are an exception for a weapon that may later fill an empty
            // primary. Once primary is occupied, another weapon is either the one usable support
            // add handled above or ordinary cargo whose magazines keep their own loot filters.
            return IsEasyWeaponEquipCandidate(candidate) &&
                   followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem == null;
        }

        private bool TryBuildPotentialWeaponCargoChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            OperationalMagazinePlan magazinePlan,
            string evaluation,
            out BodyGearMove? move)
        {
            move = null;
            Item backpack = followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            SearchableItemItemClass simulatedBackpack = CloneSearchableContainer(backpack);
            if (simulatedBackpack == null)
            {
                return TryBuildPotentialWeaponOnlyCargoFallback(
                    inventory,
                    followerEquipment,
                    candidate,
                    evaluation,
                    "noBackpackPackage",
                    out move);
            }

            List<BodyGearCandidate> cargoMagazines = magazinePlan?.CompatibleLoadedCandidates
                .Where(packageCandidate =>
                    packageCandidate?.Item != null &&
                    !string.IsNullOrEmpty(packageCandidate.Item.Id) &&
                    !IsLootNowInBotInventory(BotOwner?.GetPlayer, packageCandidate.Item))
                .GroupBy(packageCandidate => packageCandidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First().WithFollowUpDestination(BodyGearFollowUpDestination.BackpackCargo))
                .ToList() ?? new List<BodyGearCandidate>();

            foreach (BodyGearCandidate cargoMagazine in cargoMagazines)
            {
                if (!TrySimulateContainerAdd(
                        simulatedBackpack,
                        cargoMagazine.Item,
                        out SearchableItemItemClass? nextBackpack))
                {
                    return TryBuildPotentialWeaponOnlyCargoFallback(
                        inventory,
                        followerEquipment,
                        candidate,
                        evaluation,
                        $"packageMagazineDoesNotFit:{cargoMagazine.Item.TemplateId}",
                        out move);
                }

                simulatedBackpack = nextBackpack;
            }

            if (!TrySimulateContainerAdd(simulatedBackpack, candidate.Item, out _))
            {
                return TryBuildPotentialWeaponOnlyCargoFallback(
                    inventory,
                    followerEquipment,
                    candidate,
                    evaluation,
                    "packageWeaponDoesNotFitAfterMagazines",
                    out move);
            }

            BodyGearCandidate weaponCargo = candidate.WithFollowUpDestination(BodyGearFollowUpDestination.BackpackCargo);
            if (cargoMagazines.Count == 0)
            {
                bool builtWeaponCargo = TryBuildBackpackWeaponCargoMove(
                    inventory,
                    followerEquipment,
                    weaponCargo,
                    out move,
                    out string weaponCargoReason);
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(candidate?.Item)} evaluation={evaluation} " +
                    $"destination={(builtWeaponCargo ? "BackpackCargo" : "Source")} decisionReason={weaponCargoReason} packageMags=0");
                return builtWeaponCargo;
            }

            BodyGearCandidate firstMagazine = cargoMagazines[0];
            if (!TryBuildBackpackMagazineCargoMove(
                    inventory,
                    followerEquipment,
                    firstMagazine,
                    out BodyGearMove? firstMagazineMove,
                    out string firstMagazineReason))
            {
                return TryBuildPotentialWeaponOnlyCargoFallback(
                    inventory,
                    followerEquipment,
                    candidate,
                    evaluation,
                    $"firstMagazineMoveRejected:{firstMagazineReason}",
                    out move);
            }

            List<BodyGearCandidate> followUps = cargoMagazines.Skip(1).ToList();
            followUps.Add(weaponCargo);
            // A potential weapon package is still cargo. LootWeapon is reserved for a plan that
            // makes a weapon usable in an equipment slot during this search.
            move = firstMagazineMove.WithFollowUps(followUps, EPhraseTrigger.LootGeneric);
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(candidate?.Item)} evaluation={evaluation} " +
                $"destination=BackpackCargo decisionReason=packageFits packageMags={cargoMagazines.Count}");
            return true;
        }

        private bool TryBuildPotentialWeaponOnlyCargoFallback(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            string evaluation,
            string packageFailure,
            out BodyGearMove? move)
        {
            BodyGearCandidate weaponCargo = candidate.WithFollowUpDestination(BodyGearFollowUpDestination.BackpackCargo);
            bool builtWeaponCargo = TryBuildBackpackWeaponCargoMove(
                inventory,
                followerEquipment,
                weaponCargo,
                out move,
                out string weaponCargoReason);
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(candidate?.Item)} evaluation={evaluation} " +
                $"destination={(builtWeaponCargo ? "BackpackCargo" : "Source")} " +
                $"decisionReason=weaponOnlyFallback packageFailure={packageFailure} weaponResult={weaponCargoReason}");
            return builtWeaponCargo;
        }

        private bool TryBuildBackpackWeaponCargoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            if (candidate?.Item is not Weapon weapon)
            {
                reason = "notWeapon";
                return false;
            }

            if (!TryFindBackpackAddressForItem(followerEquipment, weapon, out ItemAddress? backpackAddress))
            {
                reason = "noBackpackAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    backpackAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                reason = "backpackMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private static bool HasInsertedMagazine(Weapon weapon)
        {
            try
            {
                return weapon?.GetCurrentMagazine() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasWeaponFeedForPromotion(Weapon weapon)
        {
            // Loose-feed weapons have no detachable magazine item. Their live attached feed or
            // chambers can still be completed and promoted by a later looting command.
            return FollowerWeaponLooseFeedReadiness.IsSupported(weapon) ||
                   HasInsertedMagazine(weapon);
        }

        private static bool IsOperationalFastAccessFollowUp(BodyGearCandidate candidate)
        {
            return candidate?.FollowUpDestination == BodyGearFollowUpDestination.OperationalVest ||
                   candidate?.FollowUpDestination == BodyGearFollowUpDestination.OperationalPockets;
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateMagazinePlanProjection(
            InventoryController inventory,
            Weapon weapon,
            OperationalMagazinePlan magazinePlan)
        {
            List<MagazineItemClass> plannedFastAccessMagazines = magazinePlan?.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .Select(candidate => candidate.Item)
                .OfType<MagazineItemClass>()
                .ToList() ?? new List<MagazineItemClass>();
            return FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                weapon,
                plannedFastAccessMagazines);
        }

        private void LogPrimaryReadinessShadow(
            InventoryController inventory,
            Weapon weapon,
            OperationalMagazinePlan magazinePlan)
        {
            WeaponPrimaryReadinessSnapshot actual = FollowerWeaponPrimaryReadiness.EvaluateActual(inventory, weapon);
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=actual {actual.ToDiagnosticString()}");

            List<MagazineItemClass> plannedFastAccessMagazines = magazinePlan?.FollowUps
                .Where(IsOperationalFastAccessFollowUp)
                .Select(candidate => candidate.Item)
                .OfType<MagazineItemClass>()
                .ToList() ?? new List<MagazineItemClass>();
            WeaponPrimaryReadinessSnapshot planned = FollowerWeaponPrimaryReadiness.EvaluatePlannedProjection(
                inventory,
                weapon,
                plannedFastAccessMagazines);
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=plannedProjection " +
                $"projectedMags={plannedFastAccessMagazines.Count} {planned.ToDiagnosticString()}");
        }

        private bool TryBuildPostTransferWeaponDestinationMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string destination,
            out string reason)
        {
            move = null;
            destination = "leftOnSource";
            reason = "weaponMissing";
            if (candidate?.Item is not Weapon weapon)
            {
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return TryBuildInternalPostTransferWeaponDestinationMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out destination,
                    out reason);
            }

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            // A sufficiently loaded high-capacity inserted magazine can sustain the weapon by
            // itself. Otherwise preserve room for that inserted magazine to land during reload.
            bool insertedContributionIsSufficient = actual.InsertedContribution >= actual.Threshold;
            bool hasReloadLandingSpace = insertedContributionIsSufficient ||
                                         FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(
                                             followerEquipment,
                                             weapon);
            string primaryFailure = string.Empty;
            if (actual.PrimaryReady && !actual.RequiresMagazineLoad && hasReloadLandingSpace)
            {
                if (TryBuildPrimaryWeaponEquipMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out move,
                        out string primaryReason))
                {
                    destination = "FirstPrimaryWeapon";
                    reason = "ready";
                    LogPostTransferWeaponDestination(weapon, actual, destination, reason);
                    return true;
                }

                primaryFailure = primaryReason;
            }

            if (TryBuildUnreadyWeaponSupportMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out destination))
            {
                reason = actual.PrimaryReady && !hasReloadLandingSpace
                    ? "reloadLandingSpaceUnavailable"
                    : actual.PrimaryReady
                    ? $"readyPrimaryRejected:{primaryFailure}"
                    : actual.Reason;
                LogPostTransferWeaponDestination(weapon, actual, destination, reason);
                return true;
            }

            bool secondaryOccupied = followerEquipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem != null;
            if (secondaryOccupied)
            {
                destination = "OrdinaryCargo";
            }

            string fallbackReason = secondaryOccupied
                ? "secondaryOccupied;ordinaryCargoFallback"
                : "noFallbackSpace";
            reason = actual.PrimaryReady && !hasReloadLandingSpace
                ? $"reloadLandingSpaceUnavailable;{fallbackReason}"
                : actual.PrimaryReady
                ? $"readyPrimaryRejected:{primaryFailure};{fallbackReason}"
                : $"{actual.Reason};{fallbackReason}";
            LogPostTransferWeaponDestination(weapon, actual, destination, reason);
            return false;
        }

        private void LogPostTransferWeaponDestination(
            Weapon weapon,
            WeaponPrimaryReadinessSnapshot actual,
            string destination,
            string reason)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=postTransfer destination={destination} " +
                   $"decisionReason={reason} {actual.ToDiagnosticString()}");
        }

        private bool TryBuildCargoWeaponPromotionMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            return TryBuildStoredWeaponPromotionMove(
                inventory,
                followerEquipment,
                candidate,
                "cargoPromotion",
                "BackpackCargo",
                out move,
                out reason);
        }

        private bool TryBuildSecondaryWeaponPromotionMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            BotWeaponManager weaponManager = BotOwner?.WeaponManager;
            BotWeaponSelector selector = weaponManager?.Selector;
            Weapon activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            if (candidate?.Item is not Weapon weapon ||
                selector == null ||
                selector.IsChanging ||
                weaponManager.Reload?.Reloading == true ||
                !weaponManager.CanChangeHands() ||
                IsSameLootItem(activeWeapon, weapon))
            {
                move = null;
                reason = "handsBusy";
                return false;
            }

            return TryBuildStoredWeaponPromotionMove(
                inventory,
                followerEquipment,
                candidate,
                "secondarySourcePromotion",
                "SecondPrimaryWeapon",
                out move,
                out reason);
        }

        private bool TryBuildStoredWeaponPromotionMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            string evaluationKind,
            string retainedDestination,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "weaponMissing";
            if (candidate?.Item is not Weapon weapon)
            {
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return TryBuildInternalStoredWeaponPromotionMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    evaluationKind,
                    retainedDestination,
                    out move,
                    out reason);
            }

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            if (!actual.PrimaryReady ||
                actual.RequiresMagazineLoad ||
                !FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(
                    followerEquipment,
                    weapon))
            {
                reason = !actual.PrimaryReady
                    ? actual.Reason
                    : actual.RequiresMagazineLoad
                    ? "requiresMagazineLoad"
                    : "reloadLandingSpaceUnavailable";
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluationKind} " +
                    $"destination={retainedDestination} decisionReason={reason} {actual.ToDiagnosticString()}");
                return false;
            }

            if (!TryBuildPrimaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluationKind} " +
                    $"destination={retainedDestination} decisionReason={reason} {actual.ToDiagnosticString()}");
                return false;
            }

            reason = "ready";
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluationKind} " +
                $"destination=FirstPrimaryWeapon decisionReason={reason} {actual.ToDiagnosticString()}");
            return true;
        }

        private bool TryBuildUnreadyWeaponSupportMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string destination)
        {
            move = null;
            destination = "leftOnSource";
            Item weapon = candidate?.Item;
            if (weapon == null)
            {
                return false;
            }

            // The gear planner owns only the empty support slot. With that slot occupied, the
            // candidate is left for ordinary Pickup Gear cargo evaluation instead of bypassing
            // category and price filters through an automatic backpack move.
            if (TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    weapon,
                    out ItemAddress? secondaryAddress) &&
                TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    secondaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    // With primary empty, this left-shoulder slot is an inert holding state.
                    // The weapon may become usable later, but this search did not make it usable.
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                destination = "SecondPrimaryWeapon";
                return true;
            }

            return false;
        }

        private bool TryBuildOperationalSecondaryWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "weaponMissing";
            if (candidate?.Item is not Weapon weapon || !IsEasyWeaponEquipCandidate(candidate))
            {
                return false;
            }

            if (!pitFireTeam.IsLootGearPickupEnabled())
            {
                reason = "pickupGearDisabled";
                return false;
            }

            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return TryBuildInternalOperationalSecondaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason);
            }

            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                reason = "primaryMissing";
                return false;
            }

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            bool usable = actual.HasInsertedMagazine &&
                          (actual.InsertedRounds > 0 || actual.FastAccessMagazineRounds.Any(rounds => rounds > 0));
            if (!usable)
            {
                reason = actual.HasInsertedMagazine ? "noUsableAmmunition" : "insertedMagazineMissing";
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation=secondaryEquipRejected " +
                    $"destination=Source decisionReason={reason} {actual.ToDiagnosticString()}");
                return false;
            }

            if (!TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    weapon,
                    out ItemAddress? secondaryAddress))
            {
                reason = "secondaryUnavailable";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    secondaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                reason = "secondaryMoveRejected";
                return false;
            }

            reason = "usableSupport";
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation=secondaryEquip " +
                $"destination=SecondPrimaryWeapon decisionReason={reason} {actual.ToDiagnosticString()}");
            return true;
        }

        private bool TryBuildPrimaryWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = string.Empty;

            if (candidate?.Item is not Weapon weapon)
            {
                reason = "notWeapon";
                return false;
            }

            if (!IsEasyWeaponEquipCandidate(candidate))
            {
                reason = "notEasyWeaponCandidate";
                return false;
            }

            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem != null)
            {
                reason = "primaryOccupied";
                return false;
            }

            if (!TryFindEquipmentSlotAddress(followerEquipment, EquipmentSlot.FirstPrimaryWeapon, weapon, out ItemAddress? firstPrimaryAddress))
            {
                reason = "noPrimaryAddress";
                return false;
            }

            if (!TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    firstPrimaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootWeapon,
                    rebindAsPrimaryWeapon: true))
            {
                reason = "primaryMoveRejected";
                return false;
            }

            reason = "ok";
            return true;
        }

        private void QueuePostLootPrimaryWeaponSelection(Weapon weapon, string context)
        {
            if (weapon == null)
            {
                return;
            }

            FollowerLootedPrimaryWeaponBinding.SelectAfterLootCompletion(
                BotOwner,
                weapon,
                context);
        }

        private bool TryStartPendingBodyGearSwapFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingBodyGearSwapFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingBodyGearSwapFollowUps.Dequeue();
                AmmoSalvageFollowUpResult ammoSalvageResult = HandleAmmoSalvageFollowUp(
                    inventory,
                    followerEquipment,
                    candidate,
                    pendingBodyGearSwapFollowUps,
                    bodyLootAttemptedItemIds,
                    bodyContext: true);
                if (ammoSalvageResult == AmmoSalvageFollowUpResult.MoveStarted)
                {
                    return true;
                }

                if (ammoSalvageResult == AmmoSalvageFollowUpResult.Continue)
                {
                    continue;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.WeaponSupportLooseAmmo)
                {
                    if (!TryBuildWeaponLooseAmmoMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            requireWeaponOnFollower: true,
                            out BodyGearMove? looseAmmoMove,
                            out string looseAmmoReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][LooseAmmo] Body follow-up skipped: " +
                            $"reason={looseAmmoReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, looseAmmoMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.InternalAmmoCarry)
                {
                    if (!TryBuildInternalAmmoCarryMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? internalAmmoMove,
                            out string internalAmmoReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][LooseFeedReadiness] Body reserve-ammo follow-up skipped: " +
                            $"reason={internalAmmoReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, internalAmmoMove);
                    return true;
                }

                if (candidate?.Item is VestItemClass)
                {
                    if (!TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out BodyGearMove? vestMove))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, vestMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.SecondaryWeaponEquip)
                {
                    if (!TryBuildOperationalSecondaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? secondaryMove,
                            out string secondaryReason))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body secondary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={secondaryReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, secondaryMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion)
                {
                    if (!TryBuildCargoWeaponPromotionMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? promotionMove,
                            out string promotionReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body cargo weapon promotion retained in backpack for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={promotionReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, promotionMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion)
                {
                    if (!TryBuildSecondaryWeaponPromotionMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? promotionMove,
                            out string promotionReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body secondary weapon promotion retained for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={promotionReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, promotionMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateWeaponDestination)
                {
                    if (!TryBuildPostTransferWeaponDestinationMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? destinationMove,
                            out string destination,
                            out string destinationReason))
                    {
                        bool ordinaryCargoFallback = string.Equals(
                            destination,
                            "OrdinaryCargo",
                            StringComparison.Ordinal);
                        if (ordinaryCargoFallback)
                        {
                            bodyLootAttemptedItemIds.Remove(candidate.Item.Id);
                        }
                        else
                        {
                            bodyLootHadEligibleButNoSpace = true;
                        }

                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body post-transfer weapon destination rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"destination={destination} reason={destinationReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, destinationMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Body primary equip follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} item={DescribeLootDebugItem(candidate.Item)} pendingAfterDequeue={pendingBodyGearSwapFollowUps.Count}");

                    if (!TryBuildPrimaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? primaryMove,
                            out string primaryReason))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body primary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item?.TemplateId ?? "unknown"} reason={primaryReason}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, primaryMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo &&
                    candidate.Item is Weapon)
                {
                    if (!TryBuildBackpackWeaponCargoMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? weaponCargoMove,
                            out string weaponCargoReason))
                    {
                        bodyLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Body weapon cargo follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item.TemplateId} reason={weaponCargoReason}");
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartBodyGearMove(inventory, weaponCargoMove);
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(candidate?.Item)} pendingAfterDequeue={pendingBodyGearSwapFollowUps.Count}");

                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out BodyGearMove? move,
                        out string reason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Body support-mag follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"{candidate?.SourceName ?? "unknown"}:{candidate?.Item?.TemplateId ?? "unknown"} reason={reason}");
                    continue;
                }

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Body follow-up starting move for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)}");
                StartBodyGearMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryStartPendingContainerGearSwapFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            while (pendingContainerGearSwapFollowUps.Count > 0)
            {
                BodyGearCandidate candidate = pendingContainerGearSwapFollowUps.Dequeue();
                AmmoSalvageFollowUpResult ammoSalvageResult = HandleAmmoSalvageFollowUp(
                    inventory,
                    followerEquipment,
                    candidate,
                    pendingContainerGearSwapFollowUps,
                    containerLootAttemptedItemIds,
                    bodyContext: false);
                if (ammoSalvageResult == AmmoSalvageFollowUpResult.MoveStarted)
                {
                    return true;
                }

                if (ammoSalvageResult == AmmoSalvageFollowUpResult.Continue)
                {
                    continue;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.WeaponSupportLooseAmmo)
                {
                    if (!TryBuildWeaponLooseAmmoMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            requireWeaponOnFollower: true,
                            out BodyGearMove? looseAmmoMove,
                            out string looseAmmoReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][LooseAmmo] Container follow-up skipped: " +
                            $"reason={looseAmmoReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, looseAmmoMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.InternalAmmoCarry)
                {
                    if (!TryBuildInternalAmmoCarryMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? internalAmmoMove,
                            out string internalAmmoReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][LooseFeedReadiness] Container reserve-ammo follow-up skipped: " +
                            $"reason={internalAmmoReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, internalAmmoMove);
                    return true;
                }

                if (candidate?.Item is VestItemClass)
                {
                    if (!TryBuildTacticalVestEquipIntoEmptySlot(inventory, followerEquipment, candidate, out BodyGearMove? vestMove))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, vestMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.SecondaryWeaponEquip)
                {
                    if (!TryBuildOperationalSecondaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? secondaryMove,
                            out string secondaryReason))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container secondary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={secondaryReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, secondaryMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateCargoWeaponPromotion)
                {
                    if (!TryBuildCargoWeaponPromotionMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? promotionMove,
                            out string promotionReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container cargo weapon promotion retained in backpack for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={promotionReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, promotionMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateSecondaryWeaponPromotion)
                {
                    if (!TryBuildSecondaryWeaponPromotionMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? promotionMove,
                            out string promotionReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container secondary weapon promotion retained for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"reason={promotionReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, promotionMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.EvaluateWeaponDestination)
                {
                    if (!TryBuildPostTransferWeaponDestinationMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? destinationMove,
                            out string destination,
                            out string destinationReason))
                    {
                        bool ordinaryCargoFallback = string.Equals(
                            destination,
                            "OrdinaryCargo",
                            StringComparison.Ordinal);
                        if (ordinaryCargoFallback)
                        {
                            containerLootAttemptedItemIds.Remove(candidate.Item.Id);
                        }
                        else
                        {
                            containerLootHadEligibleButNoSpace = true;
                        }

                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container post-transfer weapon destination rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"destination={destination} reason={destinationReason} item={DescribeLootDebugItem(candidate?.Item)}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, destinationMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.PrimaryWeaponEquip)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagDebug] Container primary equip follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"source={candidate.SourceName} item={DescribeLootDebugItem(candidate.Item)} pendingAfterDequeue={pendingContainerGearSwapFollowUps.Count}");

                    if (!TryBuildPrimaryWeaponEquipMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? primaryMove,
                            out string primaryReason))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container primary equip follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item?.TemplateId ?? "unknown"} reason={primaryReason}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, primaryMove);
                    return true;
                }

                if (candidate?.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo &&
                    candidate.Item is Weapon)
                {
                    if (!TryBuildBackpackWeaponCargoMove(
                            inventory,
                            followerEquipment,
                            candidate,
                            out BodyGearMove? weaponCargoMove,
                            out string weaponCargoReason))
                    {
                        containerLootHadEligibleButNoSpace = true;
                        Modules.Logger.LogInfo(
                            $"[LootCommand] Container weapon cargo follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                            $"{candidate.SourceName}:{candidate.Item.TemplateId} reason={weaponCargoReason}");
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    StartContainerLootMove(inventory, weaponCargoMove);
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container follow-up dequeued for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                    $"item={DescribeLootDebugItem(candidate?.Item)} pendingAfterDequeue={pendingContainerGearSwapFollowUps.Count}");

                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out BodyGearMove? move,
                        out string reason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand] Container support-mag follow-up rejected for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                        $"{candidate?.SourceName ?? "unknown"}:{candidate?.Item?.TemplateId ?? "unknown"} reason={reason}");
                    continue;
                }

                containerLootAttemptedItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Container follow-up starting move for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"source={candidate.SourceName} dest={candidate.FollowUpDestination} item={DescribeLootDebugItem(candidate.Item)}");
                StartContainerLootMove(inventory, move);
                return true;
            }

            return false;
        }

        private bool TryBuildSupportMagazineFollowUpMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Follow-up build start for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"source={candidate?.SourceName ?? "unknown"} dest={candidate?.FollowUpDestination.ToString() ?? "unknown"} " +
                $"item={DescribeLootDebugItem(candidate?.Item)}");

            if (!TryGetOperationalMagazineCandidate(candidate, out MagazineItemClass? magazine, out string validationReason))
            {
                reason = validationReason;
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up build rejected validation for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"reason={reason} item={DescribeLootDebugItem(candidate?.Item)}");
                return false;
            }

            if (candidate.FollowUpDestination == BodyGearFollowUpDestination.BackpackCargo)
            {
                bool backpackOnly = TryBuildBackpackMagazineCargoMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason);
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up backpack-only result for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"ok={backpackOnly} reason={reason} item={DescribeLootDebugItem(magazine)}");
                return backpackOnly;
            }

            if (TryBuildOperationalMagazineFastAccessMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out string fastAccessReason))
            {
                reason = "ok";
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagDebug] Follow-up fast-access result for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                    $"ok=True destination={candidate.FollowUpDestination} item={DescribeLootDebugItem(magazine)}");
                return true;
            }

            // Do not silently turn a failed operational transfer into backpack support. P2 must
            // evaluate only successful fast-access moves; normal filtered looting owns cargo later.
            reason = $"fastAccess:{fastAccessReason}";
            Modules.Logger.LogInfo(
                $"[LootCommand][MagDebug] Follow-up build rejected placement for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"reason={reason} item={DescribeLootDebugItem(magazine)}");
            return false;
        }

        private void LogOperationalMagazinePlan(Weapon weapon, OperationalMagazinePlan plan)
        {
            if (plan == null)
            {
                return;
            }

            Modules.Logger.LogInfo(
                $"[LootCommand] Support-mag plan for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': " +
                $"weapon={weapon?.TemplateId ?? "unknown"} scanned={plan.ScannedCount} loaded={plan.ValidLoadedCount} " +
                $"queued={plan.FollowUps.Count} vest={plan.OperationalVestCount} pockets={plan.OperationalPocketsCount} " +
                $"fastAccess={plan.OperationalFastAccessCount} rejects={string.Join(",", plan.RejectionReasons)}");
        }

        private static bool ShouldReturnGearSwapAsCargo()
        {
            // Simple/Restricted gear additions are temporary combat upgrades and must return by mail
            // like normal follower cargo. Immersive/Realistic leave them untracked so the escaped
            // teammate's live equipment snapshot can persist the new kit.
            return !pitFireTeam.IsFollowerLoadoutLootableMode();
        }

        private static bool CanReplaceOccupiedGearSlot()
        {
            // Simple/Restricted can add into empty equipment slots, but cannot replace spawned kit.
            // Actual occupied-slot swapping is reserved for the lootable Immersive/Realistic modes.
            return pitFireTeam.IsFollowerLoadoutLootableMode();
        }
    }
}
