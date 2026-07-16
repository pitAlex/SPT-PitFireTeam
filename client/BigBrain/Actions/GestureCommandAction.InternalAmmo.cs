using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        // This pipeline began with attached internal magazines. It now deliberately also owns
        // supported chamber-fed weapons because both use the same tactical contract: load from
        // loose rounds, carry only protected reserves, then classify from settled live state.
        private bool TryBuildInternalMagazineWeaponEquipChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate weaponCandidate,
            IEnumerable<BodyGearCandidate>? sourceAmmoCandidates,
            out BodyGearMove? move,
            out bool handledByGearPolicy)
        {
            move = null;
            handledByGearPolicy = false;
            if (weaponCandidate?.Item is not Weapon weapon ||
                !FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return false;
            }

            bool primaryOccupied = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem != null;
            bool secondaryOccupied = followerEquipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem != null;
            if ((primaryOccupied && secondaryOccupied) ||
                (!primaryOccupied && !TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.FirstPrimaryWeapon,
                    weapon,
                    out _)))
            {
                // With no equipment destination, ordinary Pickup Gear and price own cargo.
                return false;
            }

            handledByGearPolicy = true;
            List<BodyGearCandidate> sourceAmmo = SelectWeaponLooseAmmoSupportCandidates(
                followerEquipment,
                weapon,
                sourceAmmoCandidates,
                BodyGearFollowUpDestination.InternalAmmoCarry,
                "newInternalWeapon");
            List<AmmoItemClass> followerAmmo = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: false)
                .ToList();

            TrySelectInternalAmmoLoad(
                weapon,
                sourceAmmo,
                followerAmmo,
                out BodyGearCandidate? loadCandidate,
                out int loadCount);
            InternalAmmoPlan plan = PlanInternalAmmoCarry(
                followerEquipment,
                weapon,
                sourceAmmo,
                followerAmmo,
                loadCandidate?.Item?.Id,
                loadCount);
            LogInternalAmmoPlan(weapon, plan, "newWeaponProjection");

            if (plan.Projected.InsertedRounds <= 0 && plan.Projected.TotalContribution <= 0)
            {
                handledByGearPolicy = false;
                return false;
            }

            if (!primaryOccupied && !plan.Projected.PrimaryReady && secondaryOccupied)
            {
                handledByGearPolicy = false;
                return false;
            }

            BodyGearFollowUpDestination finalDestination = primaryOccupied
                ? BodyGearFollowUpDestination.SecondaryWeaponEquip
                : BodyGearFollowUpDestination.EvaluateWeaponDestination;
            BodyGearCandidate finalCandidate = weaponCandidate.WithFollowUpDestination(finalDestination);
            EPhraseTrigger cue = !primaryOccupied && plan.Projected.PrimaryReady
                ? EPhraseTrigger.LootWeapon
                : EPhraseTrigger.LootGeneric;

            if (loadCandidate != null && loadCount > 0)
            {
                if (!TryBuildInternalMagazineLoadMove(
                        inventory,
                        weapon,
                        loadCandidate,
                        loadCount,
                        cue,
                        out BodyGearMove? loadMove,
                        out string loadReason))
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][LooseFeedReadiness] load rejected weapon={DescribeLootDebugItem(weapon)} " +
                        $"ammo={DescribeLootDebugItem(loadCandidate.Item)} reason={loadReason}");
                    return false;
                }

                // A load can split or replace the source stack. Re-enter normal planning after it
                // settles so reserve candidates come from the live item tree, never stale refs.
                move = loadMove;
                return true;
            }

            if (TryBuildFirstInternalAmmoCarryMove(
                    inventory,
                    followerEquipment,
                    plan.AcceptedSourceAmmo,
                    finalCandidate,
                    cue,
                    out move))
            {
                return true;
            }

            return primaryOccupied
                ? TryBuildOperationalSecondaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    weaponCandidate,
                    out move,
                    out _)
                : TryBuildPostTransferWeaponDestinationMove(
                    inventory,
                    followerEquipment,
                    weaponCandidate,
                    out move,
                    out _,
                    out _);
        }

        private bool TryBuildInternalExistingWeaponPromotionChain(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceAmmoCandidates,
            BodyGearFollowUpDestination promotionDestination,
            string evaluation,
            out BodyGearMove? move)
        {
            move = null;
            List<BodyGearCandidate> sourceAmmo = SelectWeaponLooseAmmoSupportCandidates(
                followerEquipment,
                weapon,
                sourceAmmoCandidates,
                BodyGearFollowUpDestination.InternalAmmoCarry,
                evaluation);
            if (sourceAmmo.Count == 0)
            {
                return false;
            }

            List<AmmoItemClass> followerAmmo = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: false)
                .ToList();
            TrySelectInternalAmmoLoad(
                weapon,
                sourceAmmo,
                followerAmmo,
                out BodyGearCandidate? loadCandidate,
                out int loadCount);
            InternalAmmoPlan plan = PlanInternalAmmoCarry(
                followerEquipment,
                weapon,
                sourceAmmo,
                followerAmmo,
                loadCandidate?.Item?.Id,
                loadCount);
            LogInternalAmmoPlan(weapon, plan, evaluation);
            if (!plan.Projected.PrimaryReady)
            {
                return false;
            }

            BodyGearCandidate promotionCandidate = CreateGearSwapCandidate(
                    new BodyGearCandidate(weapon, null, evaluation, 0))
                .WithFollowUpDestination(promotionDestination);

            if (loadCandidate != null && loadCount > 0)
            {
                if (!TryBuildInternalMagazineLoadMove(
                        inventory,
                        weapon,
                        loadCandidate,
                        loadCount,
                        EPhraseTrigger.LootWeapon,
                        out BodyGearMove? loadMove,
                        out _))
                {
                    return false;
                }

                // The load may consume or replace its source stack. Only the weapon marker is
                // safe to retain; if more source ammo is still needed, the next planning pass
                // rebuilds those moves from live state before promotion.
                move = loadMove.WithFollowUps(
                    new[] { promotionCandidate },
                    EPhraseTrigger.LootWeapon);
                return true;
            }

            return TryBuildFirstInternalAmmoCarryMove(
                inventory,
                followerEquipment,
                plan.AcceptedSourceAmmo,
                promotionCandidate,
                EPhraseTrigger.LootWeapon,
                out move);
        }

        private InternalAmmoPlan PlanInternalAmmoCarry(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IReadOnlyList<BodyGearCandidate> sourceAmmo,
            IReadOnlyList<AmmoItemClass> followerAmmo,
            string? consumedAmmoId,
            int consumedRounds)
        {
            SearchableItemItemClass simulatedSecure = CloneSearchableContainer(
                followerEquipment?.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem);
            SearchableItemItemClass simulatedPockets = CloneSearchableContainer(
                followerEquipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem);
            SearchableItemItemClass simulatedBackpack = CloneSearchableContainer(
                followerEquipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem);
            SearchableItemItemClass simulatedVest = CloneSearchableContainer(
                followerEquipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
            VestReloadReserveSet vestReloadReserves = FindVestReloadReserves(
                followerEquipment,
                simulatedVest);
            List<int> reserveStacks = new List<int>();
            foreach (AmmoItemClass ammo in followerAmmo ?? Array.Empty<AmmoItemClass>())
            {
                int effectiveCount = GetEffectiveInternalAmmoCount(ammo, consumedAmmoId, consumedRounds);
                if (effectiveCount > 0)
                {
                    reserveStacks.Add(effectiveCount);
                }
            }

            InternalAmmoPlan plan = new InternalAmmoPlan();
            foreach (BodyGearCandidate candidate in sourceAmmo ?? Array.Empty<BodyGearCandidate>())
            {
                if (candidate?.Item is not AmmoItemClass ammo ||
                    !FollowerWeaponLooseFeedReadiness.IsCompatibleLooseAmmo(weapon, ammo))
                {
                    continue;
                }

                int effectiveCount = GetEffectiveInternalAmmoCount(ammo, consumedAmmoId, consumedRounds);
                if (effectiveCount <= 0)
                {
                    continue;
                }

                if (!TrySimulateWeaponLooseAmmoPlacement(
                        ammo,
                        vestReloadReserves,
                        ref simulatedSecure,
                        ref simulatedPockets,
                        ref simulatedBackpack,
                        ref simulatedVest))
                {
                    plan.RejectedNoSpace++;
                    continue;
                }

                plan.AcceptedSourceAmmo.Add(
                    candidate.WithFollowUpDestination(BodyGearFollowUpDestination.InternalAmmoCarry));
                reserveStacks.Add(effectiveCount);
            }

            int projectedLoaded = FollowerWeaponLooseFeedReadiness.GetLoadedRounds(weapon) +
                                  Math.Max(0, consumedRounds);
            plan.Projected = FollowerWeaponLooseFeedReadiness.EvaluateProjected(
                weapon,
                reserveStacks,
                projectedLoaded);
            return plan;
        }

        private static int GetEffectiveInternalAmmoCount(
            AmmoItemClass ammo,
            string? consumedAmmoId,
            int consumedRounds)
        {
            int count = Math.Max(0, ammo?.StackObjectsCount ?? 0);
            if (!string.IsNullOrEmpty(consumedAmmoId) &&
                string.Equals(ammo?.Id, consumedAmmoId, StringComparison.Ordinal))
            {
                count -= Math.Max(0, consumedRounds);
            }

            return Math.Max(0, count);
        }

        private static void TrySelectInternalAmmoLoad(
            Weapon weapon,
            IReadOnlyList<BodyGearCandidate> sourceAmmo,
            IReadOnlyList<AmmoItemClass> followerAmmo,
            out BodyGearCandidate? loadCandidate,
            out int loadCount)
        {
            loadCandidate = null;
            loadCount = 0;
            int free;
            if (FollowerWeaponChamberReadiness.IsSupportedChamberWeapon(weapon))
            {
                free = FollowerWeaponChamberReadiness.GetFreeChamberCount(weapon);
            }
            else
            {
                MagazineItemClass internalMagazine;
                try
                {
                    internalMagazine = weapon?.GetCurrentMagazine();
                }
                catch
                {
                    return;
                }

                free = Math.Max(0, (internalMagazine?.MaxCount ?? 0) - (internalMagazine?.Count ?? 0));
            }

            if (free <= 0)
            {
                return;
            }

            loadCandidate = sourceAmmo?
                .Where(candidate => candidate?.Item is AmmoItemClass ammo &&
                    FollowerWeaponLooseFeedReadiness.IsCompatibleLooseAmmo(weapon, ammo))
                .OrderByDescending(candidate => ((AmmoItemClass)candidate.Item).StackObjectsCount)
                .FirstOrDefault();
            if (loadCandidate == null)
            {
                AmmoItemClass carriedAmmo = followerAmmo?
                    .Where(ammo => FollowerWeaponLooseFeedReadiness.IsCompatibleLooseAmmo(weapon, ammo))
                    .OrderByDescending(ammo => ammo.StackObjectsCount)
                    .FirstOrDefault();
                if (carriedAmmo != null)
                {
                    loadCandidate = CreateWeaponLooseAmmoCandidate(
                        carriedAmmo,
                        weapon,
                        "Follower.LooseFeedWeaponAmmo")
                        .WithFollowUpDestination(BodyGearFollowUpDestination.InternalAmmoCarry);
                }
            }

            if (loadCandidate?.Item is AmmoItemClass selectedAmmo)
            {
                // Weapon.Apply fills one chamber per off-hands transaction. Replanning after each
                // settled shell keeps chamber count and split-stack references authoritative.
                loadCount = FollowerWeaponChamberReadiness.IsSupportedChamberWeapon(weapon)
                    ? Math.Min(1, selectedAmmo.StackObjectsCount)
                    : Math.Min(free, selectedAmmo.StackObjectsCount);
            }
        }

        private bool TryBuildInternalMagazineLoadMove(
            InventoryController inventory,
            Weapon weapon,
            BodyGearCandidate ammoCandidate,
            int loadCount,
            EPhraseTrigger successPhrase,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidLoad";
            if (ammoCandidate?.Item is not AmmoItemClass ammo || loadCount <= 0)
            {
                return false;
            }

            int loadedBefore = FollowerWeaponLooseFeedReadiness.GetLoadedRounds(weapon);
            GStruct153 loadResult;
            int plannedLoadCount;
            if (FollowerWeaponChamberReadiness.IsSupportedChamberWeapon(weapon))
            {
                if (FollowerWeaponChamberReadiness.GetFreeChamberCount(weapon) <= 0)
                {
                    reason = "emptyChamberUnavailable";
                    return false;
                }

                plannedLoadCount = 1;
                // This is the same off-hands operation used by vanilla
                // TraderControllerClass.LoadMultiBarrelWeapon.
                loadResult = weapon.Apply(inventory, ammo, plannedLoadCount, true);
            }
            else
            {
                MagazineItemClass internalMagazine;
                try
                {
                    internalMagazine = weapon.GetCurrentMagazine();
                }
                catch
                {
                    reason = "internalMagazineUnavailable";
                    return false;
                }

                plannedLoadCount = loadCount;
                loadResult = internalMagazine.ApplyWithoutRestrictions(
                    inventory,
                    ammo,
                    plannedLoadCount,
                    true);
            }
            if (loadResult.Failed)
            {
                reason = $"applyRejected:{loadResult.Error}";
                return false;
            }

            if (loadResult.Value == null || !inventory.CanExecute(loadResult.Value))
            {
                reason = "operationCannotExecute";
                return false;
            }

            move = new BodyGearMove(
                ammo,
                loadResult.Value,
                ammoCandidate.SourceName,
                reportAsLootNothing: true,
                storeAsLoot: false,
                successPhrase: successPhrase,
                isStagingOperation: true,
                stagingWeapon: weapon,
                stagingWeaponLoadedRoundsBefore: loadedBefore);
            Modules.Logger.LogInfo(
                $"[LootCommand][LooseFeedReadiness] load planned weapon={DescribeLootDebugItem(weapon)} " +
                $"ammo={DescribeLootDebugItem(ammo)} count={plannedLoadCount} loadedBefore={loadedBefore}");
            reason = "ok";
            return true;
        }

        private bool TryBuildFirstInternalAmmoCarryMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            IReadOnlyList<BodyGearCandidate> ammoCandidates,
            BodyGearCandidate finalCandidate,
            EPhraseTrigger successPhrase,
            out BodyGearMove? move)
        {
            move = null;
            for (int firstIndex = 0; firstIndex < (ammoCandidates?.Count ?? 0); firstIndex++)
            {
                if (!TryBuildInternalAmmoCarryMove(
                        inventory,
                        followerEquipment,
                        ammoCandidates[firstIndex],
                        out BodyGearMove? firstMove,
                        out _))
                {
                    continue;
                }

                List<BodyGearCandidate> followUps = new List<BodyGearCandidate>();
                for (int i = 0; i < ammoCandidates.Count; i++)
                {
                    if (i != firstIndex)
                    {
                        followUps.Add(ammoCandidates[i]);
                    }
                }

                followUps.Add(finalCandidate);
                move = firstMove.WithFollowUps(followUps, successPhrase, continueOnFailure: true);
                return true;
            }

            return false;
        }

        private bool TryBuildInternalAmmoCarryMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidAmmo";
            return TryBuildWeaponLooseAmmoMove(
                inventory,
                followerEquipment,
                candidate,
                requireWeaponOnFollower: false,
                out move,
                out reason);
        }

        private WeaponPrimaryReadinessSnapshot EvaluateActualWeaponReadiness(
            InventoryController inventory,
            Weapon weapon)
        {
            return FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon,
                ammo => !InteractableObjects.IsStrictCargoItem(BotOwner, ammo));
        }

        private bool TryBuildInternalPostTransferWeaponDestinationMove(
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

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            if (actual.PrimaryReady &&
                !actual.RequiresMagazineLoad &&
                TryBuildPrimaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out string primaryReason))
            {
                destination = "FirstPrimaryWeapon";
                reason = "ready";
                LogInternalWeaponDestination(weapon, actual, destination, reason);
                return true;
            }

            if (TryBuildUnreadyWeaponSupportMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out destination))
            {
                reason = actual.RequiresMagazineLoad
                    ? "looseFeedLoadRequired"
                    : actual.Reason;
                LogInternalWeaponDestination(weapon, actual, destination, reason);
                return true;
            }

            bool secondaryOccupied = followerEquipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem != null;
            destination = secondaryOccupied ? "OrdinaryCargo" : "leftOnSource";
            reason = secondaryOccupied
                ? $"{actual.Reason};secondaryOccupied;ordinaryCargoFallback"
                : $"{actual.Reason};noFallbackSpace";
            LogInternalWeaponDestination(weapon, actual, destination, reason);
            return false;
        }

        private bool TryBuildInternalOperationalSecondaryWeaponEquipMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "weaponMissing";
            if (candidate?.Item is not Weapon weapon ||
                followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                return false;
            }

            if (!pitFireTeam.IsLootGearPickupEnabled())
            {
                reason = "pickupGearDisabled";
                return false;
            }

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            if (actual.InsertedRounds <= 0)
            {
                reason = "noLoadedAmmunition";
                return false;
            }

            if (!TryFindEquipmentSlotAddress(
                    followerEquipment,
                    EquipmentSlot.SecondPrimaryWeapon,
                    weapon,
                    out ItemAddress? secondaryAddress) ||
                !TryCreateBodyGearMove(
                    inventory,
                    candidate,
                    secondaryAddress,
                    out move,
                    storeAsLoot: ShouldReturnGearSwapAsCargo(),
                    successPhrase: EPhraseTrigger.LootGeneric))
            {
                reason = "secondaryUnavailable";
                return false;
            }

            reason = "usableLooseFeedSupport";
            LogInternalWeaponDestination(weapon, actual, "SecondPrimaryWeapon", reason);
            return true;
        }

        private bool TryBuildInternalStoredWeaponPromotionMove(
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

            WeaponPrimaryReadinessSnapshot actual = EvaluateActualWeaponReadiness(inventory, weapon);
            if (!actual.PrimaryReady || actual.RequiresMagazineLoad)
            {
                reason = actual.RequiresMagazineLoad ? "looseFeedLoadRequired" : actual.Reason;
                LogInternalWeaponDestination(weapon, actual, retainedDestination, reason, evaluationKind);
                return false;
            }

            if (!TryBuildPrimaryWeaponEquipMove(
                    inventory,
                    followerEquipment,
                    candidate,
                    out move,
                    out reason))
            {
                LogInternalWeaponDestination(weapon, actual, retainedDestination, reason, evaluationKind);
                return false;
            }

            reason = "ready";
            LogInternalWeaponDestination(weapon, actual, "FirstPrimaryWeapon", reason, evaluationKind);
            return true;
        }

        private void LogInternalAmmoPlan(Weapon weapon, InternalAmmoPlan plan, string evaluation)
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][LooseFeedReadiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluation} " +
                $"plannedSourceStacks={plan.AcceptedSourceAmmo.Count} rejectedNoSpace={plan.RejectedNoSpace} " +
                plan.Projected.ToDiagnosticString());
        }

        private void LogInternalWeaponDestination(
            Weapon weapon,
            WeaponPrimaryReadinessSnapshot readiness,
            string destination,
            string reason,
            string evaluation = "postTransfer")
        {
            Modules.Logger.LogInfo(
                $"[LootCommand][LooseFeedReadiness] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluation} destination={destination} " +
                $"decisionReason={reason} {readiness.ToDiagnosticString()}");
        }

        private sealed class InternalAmmoPlan
        {
            public List<BodyGearCandidate> AcceptedSourceAmmo { get; } = new List<BodyGearCandidate>();
            public int RejectedNoSpace { get; set; }
            public WeaponPrimaryReadinessSnapshot Projected { get; set; } = null!;
        }
    }
}
