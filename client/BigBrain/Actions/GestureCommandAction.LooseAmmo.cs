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
            // Ammunition needs to remain usable in raid. The vest is first only when the
            // reload reserve for the follower's active magazines still remains intact.
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
                foreach (EFT.InventoryLogic.Ammo ammo in GetWeaponLooseAmmoItems(root, weapon))
                {
                    yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, $"{slot}.WeaponLooseAmmo");
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetContainerWeaponLooseAmmoCandidates(
            EFT.InventoryLogic.SearchableItem containerRoot,
            Weapon weapon)
        {
            foreach (EFT.InventoryLogic.Ammo ammo in GetWeaponLooseAmmoItems(containerRoot, weapon))
            {
                yield return CreateWeaponLooseAmmoCandidate(ammo, weapon, "Container.WeaponLooseAmmo");
            }
        }

        private static IEnumerable<EFT.InventoryLogic.Ammo> GetWeaponLooseAmmoItems(Item root, Weapon weapon)
        {
            if (root == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EFT.InventoryLogic.Ammo ammo in SnapshotLootTreeItems(root).OfType<EFT.InventoryLogic.Ammo>())
            {
                if (string.IsNullOrEmpty(ammo.Id) ||
                    !yieldedIds.Add(ammo.Id) ||
                    ammo.Parent?.Container is StackSlot or Slot ||
                    !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                {
                    continue;
                }

                yield return ammo;
            }
        }

        private IEnumerable<EFT.InventoryLogic.Ammo> GetFollowerWeaponLooseAmmoItems(
            InventoryEquipment followerEquipment,
            Weapon weapon,
            bool includeStrictCargo)
        {
            if (followerEquipment == null || weapon == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentSlot slot in WeaponLooseAmmoDestinationOrder)
            {
                Item root = followerEquipment.GetSlot(slot)?.ContainedItem;
                foreach (EFT.InventoryLogic.Ammo ammo in SnapshotLootTreeItems(root).OfType<EFT.InventoryLogic.Ammo>())
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

        private IEnumerable<EFT.InventoryLogic.Ammo> GetFollowerWeaponCartridgeItems(
            InventoryEquipment followerEquipment,
            Weapon weapon)
        {
            if (followerEquipment == null || weapon == null)
            {
                yield break;
            }

            // This is an ammunition-supply snapshot, not a reload-access scan. Rounds already in
            // weapons/magazines and manually placed cargo still matter when deciding whether the
            // follower needs a weaker loose stack, even when those items cannot satisfy immediate
            // detachable-magazine readiness by themselves.
            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EFT.InventoryLogic.Ammo ammo in SnapshotLootTreeItems(followerEquipment).OfType<EFT.InventoryLogic.Ammo>())
            {
                if (string.IsNullOrEmpty(ammo.Id) ||
                    !yieldedIds.Add(ammo.Id) ||
                    !FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, ammo))
                {
                    continue;
                }

                yield return ammo;
            }
        }

        private static TacticalAmmoDecision EvaluateTacticalAmmoCandidate(
            EFT.InventoryLogic.Ammo candidate,
            IEnumerable<EFT.InventoryLogic.Ammo> carriedAmmo,
            int candidateAvailableRounds,
            int reserveTargetRounds,
            bool allowUpgrade)
        {
            List<EFT.InventoryLogic.Ammo> compatible = carriedAmmo?
                .Where(ammo =>
                    ammo != null &&
                    ammo.StackObjectsCount > 0 &&
                    FollowerWeaponLooseAmmoSupport.IsSameCaliber(candidate, ammo))
                .ToList() ?? new List<EFT.InventoryLogic.Ammo>();
            int currentRounds = compatible.Sum(ammo => Math.Max(0, ammo.StackObjectsCount));
            double weightedPenetration = currentRounds > 0
                ? compatible.Sum(ammo =>
                      (double)Math.Max(0, ammo.StackObjectsCount) * ammo.PenetrationPower) /
                  currentRounds
                : 0d;
            return FollowerTacticalAmmoPolicy.Evaluate(
                currentRounds,
                weightedPenetration,
                candidate?.PenetrationPower ?? 0,
                candidateAvailableRounds,
                reserveTargetRounds,
                allowUpgrade);
        }

        private int ResolveWeaponTacticalAmmoReserveTarget(
            InventoryController inventory,
            Weapon weapon,
            IEnumerable<EFT.InventoryLogic.Ammo> availableAmmo,
            Func<EFT.InventoryLogic.Magazine, bool>? fastAccessMagazineEligibility = null)
        {
            List<EFT.InventoryLogic.Ammo> available = availableAmmo?
                .Where(ammo => ammo != null)
                .ToList() ?? new List<EFT.InventoryLogic.Ammo>();
            if (FollowerWeaponLooseAmmoSupport.IsShotgun(weapon))
            {
                // Shotgun feed capacities are often tiny compared with their 20-round loose
                // stacks. Tactical stocking therefore follows the established three-stack rule
                // instead of stopping at three internal-magazine capacity equivalents.
                int shotgunStackCapacity = available
                    .Select(ammo => Math.Max(1, ammo.StackMaxSize))
                    .DefaultIfEmpty(20)
                    .Max();
                return shotgunStackCapacity * 3;
            }

            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon,
                internalAmmoEligibility: null,
                fastAccessMagazineEligibility: fastAccessMagazineEligibility);
            if (readiness?.Threshold > 0)
            {
                // Two magazine equivalents make a weapon combat-ready; tactical stocking is a
                // separate policy and continues until a third ordinary magazine is represented.
                return readiness.Threshold + Math.Max(0, readiness.OrdinaryReference);
            }

            List<EFT.InventoryLogic.Magazine> availableMagazines = GetFastAccessMagazines(
                    inventory?.Inventory?.Equipment)
                .Where(magazine =>
                    (fastAccessMagazineEligibility == null ||
                     fastAccessMagazineEligibility(magazine)) &&
                    IsMagazineCompatibleWithWeapon(weapon, magazine))
                .ToList();
            EFT.InventoryLogic.Magazine insertedMagazine = GetCurrentMagazineSafely(weapon);
            if (insertedMagazine != null && IsMagazineCompatibleWithWeapon(weapon, insertedMagazine))
            {
                availableMagazines.Add(insertedMagazine);
            }

            int largestAvailableMagazine = availableMagazines
                .Select(magazine => Math.Max(0, magazine.MaxCount))
                .DefaultIfEmpty(0)
                .Max();
            if (largestAvailableMagazine > 0)
            {
                return Math.Min(30, largestAvailableMagazine) * 2;
            }

            int stackCapacity = available
                .Select(ammo => Math.Max(1, ammo.StackMaxSize))
                .DefaultIfEmpty(1)
                .Max();
            return stackCapacity * 2;
        }

        private static BodyGearCandidate CreateWeaponLooseAmmoCandidate(
            EFT.InventoryLogic.Ammo ammo,
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
            string evaluation,
            IEnumerable<EFT.InventoryLogic.Ammo>? additionalCarriedAmmo = null)
        {
            List<BodyGearCandidate> source = sourceCandidates?
                .Where(candidate =>
                    candidate?.Item is EFT.InventoryLogic.Ammo ammo &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<BodyGearCandidate>();
            if (source.Count == 0)
            {
                return source;
            }

            // Ammunition need is based on every compatible cartridge already carried, including
            // rounds loaded in weapons and magazines. Reload readiness remains stricter elsewhere;
            // this broader snapshot only prevents collecting weaker ammunition unnecessarily.
            List<EFT.InventoryLogic.Ammo> carried = GetFollowerWeaponCartridgeItems(followerEquipment, weapon)
                .Concat(additionalCarriedAmmo ?? Enumerable.Empty<EFT.InventoryLogic.Ammo>())
                .Where(ammo => ammo != null && !string.IsNullOrEmpty(ammo.Id))
                .GroupBy(ammo => ammo.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            InventoryController inventory = BotOwner?.GetPlayer?.InventoryController;
            int reserveTargetRounds = ResolveWeaponTacticalAmmoReserveTarget(
                inventory,
                weapon,
                carried.Concat(source.Select(candidate => (EFT.InventoryLogic.Ammo)candidate.Item)));
            Dictionary<string, int> remainingSourceRoundsByTemplate = source
                .GroupBy(candidate => candidate.Item.TemplateId.ToString(), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(candidate => Math.Max(0, candidate.Item.StackObjectsCount)),
                    StringComparer.Ordinal);
            EFT.InventoryLogic.SearchableItem simulatedSecure = CloneSearchableContainer(
                followerEquipment?.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem);

            List<BodyGearCandidate> accepted = new List<BodyGearCandidate>();
            foreach (BodyGearCandidate candidate in source
                         .OrderByDescending(entry => ((EFT.InventoryLogic.Ammo)entry.Item).PenetrationPower)
                         .ThenByDescending(entry => ((EFT.InventoryLogic.Ammo)entry.Item).Damage)
                         .ThenByDescending(entry => ((EFT.InventoryLogic.Ammo)entry.Item).ArmorDamage))
            {
                EFT.InventoryLogic.Ammo ammo = (EFT.InventoryLogic.Ammo)candidate.Item;
                string ammoTemplateId = ammo.TemplateId.ToString();
                int sourceRoundsOfType = remainingSourceRoundsByTemplate.TryGetValue(
                    ammoTemplateId,
                    out int remainingRounds)
                    ? remainingRounds
                    : ammo.StackObjectsCount;
                TacticalAmmoDecision policyDecision = EvaluateTacticalAmmoCandidate(
                    ammo,
                    carried,
                    sourceRoundsOfType,
                    reserveTargetRounds,
                    allowUpgrade: true);
                bool alreadyCarriesSameTemplate = carried.Any(existing =>
                    string.Equals(
                        existing.TemplateId.ToString(),
                        ammo.TemplateId.ToString(),
                        StringComparison.Ordinal));
                bool fitsSecure = TrySimulateContainerAdd(
                    simulatedSecure,
                    ammo,
                    out EFT.InventoryLogic.SearchableItem? nextSecure);
                bool useSecureOverride = fitsSecure &&
                    FollowerTacticalAmmoPolicy.CanUseSecureStorageOverride(
                        policyDecision,
                        alreadyCarriesSameTemplate);
                TacticalAmmoDecision decision = !policyDecision.ShouldAcquire && useSecureOverride
                    ? AcceptForSecureContainer(policyDecision)
                    : policyDecision;
                Modules.Logger.LogInfo(
                    $"[LootCommand][LooseAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} evaluation={evaluation} ammo={DescribeLooseAmmo(ammo)} " +
                    decision.ToDiagnosticString());
                if (decision.ShouldAcquire)
                {
                    accepted.Add(candidate.WithFollowUpDestination(destination));
                    if (fitsSecure)
                    {
                        simulatedSecure = nextSecure;
                    }

                    // Planning several source stacks is sequential. Count accepted rounds in the
                    // next decision so a large source cannot bypass the reserve/opportunity limit
                    // merely because its stacks have different item ids.
                    carried.Add(ammo);
                    remainingSourceRoundsByTemplate[ammoTemplateId] = Math.Max(
                        0,
                        sourceRoundsOfType - ammo.StackObjectsCount);
                }
            }

            return accepted;
        }

        private static TacticalAmmoDecision AcceptForSecureContainer(
            TacticalAmmoDecision policyDecision)
        {
            return new TacticalAmmoDecision(
                TacticalAmmoDecisionKind.Replenish,
                "secureContainerCapacity",
                policyDecision.CurrentRounds,
                policyDecision.ReserveTargetRounds,
                policyDecision.CurrentWeightedPenetration,
                policyDecision.CandidatePenetration,
                policyDecision.CandidateRounds,
                policyDecision.NeedWeight,
                policyDecision.PowerWeight,
                policyDecision.OpportunityWeight,
                policyDecision.CombinedWeight);
        }

        private BodyGearMove AppendWeaponLooseAmmoSupportFollowUps(
            BodyGearMove move,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceCandidates,
            string evaluation,
            IEnumerable<EFT.InventoryLogic.Ammo>? additionalCarriedAmmo = null)
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
                evaluation,
                additionalCarriedAmmo);
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

        private static IEnumerable<EFT.InventoryLogic.Ammo> GetOperationalMagazineCartridgeItems(
            OperationalMagazinePlan plan)
        {
            if (plan == null)
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in plan.FollowUps.Where(IsOperationalFastAccessFollowUp))
            {
                foreach (EFT.InventoryLogic.Ammo ammo in GetMagazineCartridgeItems(candidate.Item as EFT.InventoryLogic.Magazine))
                {
                    if (!string.IsNullOrEmpty(ammo.Id) && yieldedIds.Add(ammo.Id))
                    {
                        yield return ammo;
                    }
                }
            }
        }

        private static IEnumerable<EFT.InventoryLogic.Ammo> GetMagazineCartridgeItems(EFT.InventoryLogic.Magazine magazine)
        {
            return magazine?.Cartridges?.Items?
                .OfType<EFT.InventoryLogic.Ammo>()
                .Where(ammo => ammo != null && ammo.StackObjectsCount > 0) ??
                Enumerable.Empty<EFT.InventoryLogic.Ammo>();
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
            if (candidate?.Item is not EFT.InventoryLogic.Ammo ammo ||
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

            foreach (EquipmentSlot slot in WeaponLooseAmmoDestinationOrder)
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

        private bool TrySimulateLooseAmmoPlacement(
            EFT.InventoryLogic.Ammo ammo,
            VestReloadReserveSet vestReloadReserves,
            ref EFT.InventoryLogic.SearchableItem? secure,
            ref EFT.InventoryLogic.SearchableItem? pockets,
            ref EFT.InventoryLogic.SearchableItem? backpack,
            ref EFT.InventoryLogic.SearchableItem? vest)
        {
            // Keep the simulated order aligned with real moves. In particular, do not let a
            // capacity check accept secure storage when the rounds could have remained ready for
            // the active weapon in the vest.
            if (TrySimulateFastAccessContainerAdd(vest, ammo, out EFT.InventoryLogic.SearchableItem? nextVest) &&
                CanFitVestReloadReserves(nextVest, vestReloadReserves))
            {
                vest = nextVest;
                return true;
            }

            if (TrySimulateFastAccessContainerAdd(pockets, ammo, out EFT.InventoryLogic.SearchableItem? nextPockets))
            {
                pockets = nextPockets;
                return true;
            }

            if (TrySimulateContainerAdd(backpack, ammo, out EFT.InventoryLogic.SearchableItem? nextBackpack))
            {
                backpack = nextBackpack;
                return true;
            }

            if (TrySimulateContainerAdd(secure, ammo, out EFT.InventoryLogic.SearchableItem? nextSecure))
            {
                secure = nextSecure;
                return true;
            }

            return false;
        }

        private static string DescribeLooseAmmo(EFT.InventoryLogic.Ammo ammo)
        {
            return ammo == null
                ? "none"
                : $"{DescribeLootDebugItem(ammo)}:caliber={ammo.Caliber}:rounds={ammo.StackObjectsCount}:" +
                  $"stackMax={ammo.StackMaxSize}:pen={ammo.PenetrationPower}:damage={ammo.Damage}:armorDamage={ammo.ArmorDamage}";
        }
    }
}
