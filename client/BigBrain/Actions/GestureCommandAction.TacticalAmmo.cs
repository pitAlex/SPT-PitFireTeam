using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private bool TryStartBodyPrimaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment,
            bool allowLooseAmmoCarry)
        {
            if (!TryBuildPrimaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    corpseEquipment,
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    out BodyGearMove? move,
                    allowLooseAmmoCarry))
            {
                return false;
            }

            if (TryQueueBodyLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryStartBodySupportTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment,
            EquipmentSlot supportSlot,
            bool allowLooseAmmoCarry)
        {
            if (!TryBuildSupportTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    corpseEquipment,
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    supportSlot,
                    out BodyGearMove? move,
                    allowLooseAmmoCarry))
            {
                return false;
            }

            if (TryQueueBodyLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryStartBodySupportTacticalMagazineMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment,
            EquipmentSlot supportSlot)
        {
            if (!TryBuildEquippedTacticalMagazineMove(
                    inventory,
                    followerEquipment,
                    (root, weapon) => GetBodyOperationalMagazineCandidates(
                        (InventoryEquipment)root,
                        weapon,
                        includeEmptyForTopOff: true),
                    (root, weapon) => GetBodyWeaponLooseAmmoCandidates(
                        (InventoryEquipment)root,
                        weapon),
                    corpseEquipment,
                    bodyLootAttemptedItemIds,
                    supportSlot,
                    out BodyGearMove? move,
                    out bool hadCompatibleMagazine))
            {
                bodyLootHadEligibleButNoSpace |= hadCompatibleMagazine;
                return false;
            }

            if (TryQueueBodyLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartBodyGearMove(inventory, move);
            return true;
        }

        private bool TryStartBodyPrimaryTacticalMagazineMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            return TryStartBodySupportTacticalMagazineMove(
                inventory,
                corpseEquipment,
                followerEquipment,
                EquipmentSlot.FirstPrimaryWeapon);
        }

        private bool TryStartContainerPrimaryTacticalAmmoMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment,
            bool allowLooseAmmoCarry)
        {
            if (!TryBuildPrimaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    containerRoot,
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    out BodyGearMove? move,
                    allowLooseAmmoCarry))
            {
                return false;
            }

            if (TryQueueContainerLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartContainerLootMove(inventory, move);
            return true;
        }

        private bool TryStartContainerSupportTacticalAmmoMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment,
            EquipmentSlot supportSlot,
            bool allowLooseAmmoCarry)
        {
            if (!TryBuildSupportTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    containerRoot,
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    supportSlot,
                    out BodyGearMove? move,
                    allowLooseAmmoCarry))
            {
                return false;
            }

            if (TryQueueContainerLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartContainerLootMove(inventory, move);
            return true;
        }

        private bool TryStartContainerSupportTacticalMagazineMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment,
            EquipmentSlot supportSlot)
        {
            if (!TryBuildEquippedTacticalMagazineMove(
                    inventory,
                    followerEquipment,
                    (root, weapon) => GetContainerOperationalMagazineCandidates(
                        (SearchableItemItemClass)root,
                        weapon,
                        includeEmptyForTopOff: true),
                    (root, weapon) => GetContainerWeaponLooseAmmoCandidates(
                        (SearchableItemItemClass)root,
                        weapon),
                    containerRoot,
                    containerLootAttemptedItemIds,
                    supportSlot,
                    out BodyGearMove? move,
                    out bool hadCompatibleMagazine))
            {
                containerLootHadEligibleButNoSpace |= hadCompatibleMagazine;
                return false;
            }

            if (TryQueueContainerLootMoveAfterPickupSuccess(move))
            {
                return true;
            }

            StartContainerLootMove(inventory, move);
            return true;
        }

        private bool TryStartContainerPrimaryTacticalMagazineMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            return TryStartContainerSupportTacticalMagazineMove(
                inventory,
                containerRoot,
                followerEquipment,
                EquipmentSlot.FirstPrimaryWeapon);
        }

        private bool TryBuildPrimaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move,
            bool allowLooseAmmoCarry)
        {
            Weapon primary = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem as Weapon;
            Weapon deferredSupport = GetEligibleSupportAmmoMaintenanceWeapon(followerEquipment);
            return TryBuildEquippedTacticalAmmoMove(
                inventory,
                followerEquipment,
                sourceRoot,
                sourceAmmoFactory,
                attemptedSourceItemIds,
                primary,
                role: "primary",
                deferredWeapon: deferredSupport,
                fastAccessMagazineEligibility: null,
                carriedAmmoSupplyEligibility: null,
                carriedCartridgeFactory: () => GetFollowerWeaponCartridgeItems(followerEquipment, primary),
                allowSearchedSourceTopOff: pitFireTeam.IsFollowerLoadoutLootableMode(),
                out move,
                allowLooseAmmoCarry);
        }

        private bool TryBuildSupportTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            EquipmentSlot supportSlot,
            out BodyGearMove? move,
            bool allowLooseAmmoCarry)
        {
            Weapon support = GetEligibleDetachableWeaponForSlot(followerEquipment, supportSlot);
            bool isLootedSupport = InteractableObjects.IsLootedWeapon(BotOwner, support);
            Func<MagazineItemClass, bool>? magazineEligibility = isLootedSupport
                ? magazine => InteractableObjects.IsApprovedLootedWeaponMagazine(
                    BotOwner,
                    support,
                    magazine)
                : null;
            string role = supportSlot == EquipmentSlot.Holster
                ? "holster"
                : "secondary";

            return TryBuildEquippedTacticalAmmoMove(
                inventory,
                followerEquipment,
                sourceRoot,
                sourceAmmoFactory,
                attemptedSourceItemIds,
                support,
                role,
                deferredWeapon: null,
                fastAccessMagazineEligibility: magazineEligibility,
                carriedAmmoSupplyEligibility: ammo => IsSupportCarriedAmmoSupplyEligible(
                    ammo,
                    magazineEligibility),
                carriedCartridgeFactory: () => GetSupportTacticalCartridgeItems(
                    followerEquipment,
                    support,
                    magazineEligibility),
                // Source rounds become part of tracked loot when they enter an approved looted
                // support magazine, so Simple/Restricted do not touch protected spawn gear.
                allowSearchedSourceTopOff: isLootedSupport || pitFireTeam.IsFollowerLoadoutLootableMode(),
                out move,
                allowLooseAmmoCarry);
        }

        private bool TryBuildEquippedTacticalMagazineMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Func<Item, Weapon, IEnumerable<BodyGearCandidate>> sourceMagazineFactory,
            Func<Item, Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            Item sourceRoot,
            HashSet<string> attemptedSourceItemIds,
            EquipmentSlot weaponSlot,
            out BodyGearMove? move,
            out bool hadCompatibleMagazine)
        {
            move = null;
            hadCompatibleMagazine = false;
            Weapon weapon = GetEligibleDetachableWeaponForSlot(followerEquipment, weaponSlot);
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceMagazineFactory == null ||
                sourceAmmoFactory == null ||
                sourceRoot == null ||
                weapon == null)
            {
                return false;
            }

            List<BodyGearCandidate> candidates = sourceMagazineFactory(sourceRoot, weapon)
                .Where(candidate =>
                    candidate?.Item is MagazineItemClass magazine &&
                    !string.IsNullOrEmpty(magazine.Id) &&
                    !attemptedSourceItemIds.Contains(magazine.Id))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            hadCompatibleMagazine = candidates.Count > 0;
            if (!hadCompatibleMagazine)
            {
                return false;
            }

            bool isLootedWeapon = InteractableObjects.IsLootedWeapon(BotOwner, weapon);
            Func<MagazineItemClass, bool>? existingMagazineEligibility = isLootedWeapon
                ? magazine => InteractableObjects.IsApprovedLootedWeaponMagazine(
                    BotOwner,
                    weapon,
                    magazine)
                : null;
            List<MagazineItemClass> alternateReloadReserves = GetAlternateReloadReservesForSupportMagazinePlan(
                inventory,
                followerEquipment,
                weapon);
            List<AmmoItemClass> availableLooseAmmo = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: true)
                .Concat(sourceAmmoFactory(sourceRoot, weapon)
                    .Where(candidate =>
                        candidate?.Item is AmmoItemClass ammo &&
                        !string.IsNullOrEmpty(ammo.Id) &&
                        !attemptedSourceItemIds.Contains(ammo.Id))
                    .Select(candidate => candidate.Item)
                    .OfType<AmmoItemClass>())
                .GroupBy(ammo => ammo.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            bool allowEmptyCandidates = candidates
                .Select(candidate => candidate.Item as MagazineItemClass)
                .Where(magazine => magazine?.Count <= 0)
                .Any(magazine => availableLooseAmmo.Any(ammo =>
                    CanTopOffMagazineWithAmmo(weapon, magazine, ammo)));
            OperationalMagazinePlan plan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                candidates,
                allowEmptyCandidates,
                existingFastAccessMagazineEligibility: existingMagazineEligibility,
                alternateReloadReserveItems: alternateReloadReserves);

            foreach (BodyGearCandidate candidate in plan.FollowUps.Where(IsOperationalFastAccessFollowUp))
            {
                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out move,
                        out string reason,
                        allowEmptyCandidates))
                {
                    attemptedSourceItemIds.Add(candidate.Item.Id);
                    Modules.Logger.LogInfo(
                        $"[LootCommand][SupportMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(weapon)} magazine={DescribeLootDebugItem(candidate.Item)} " +
                        $"result=moveRejected reason={reason}");
                    continue;
                }

                attemptedSourceItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][SupportMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} magazine={DescribeLootDebugItem(candidate.Item)} " +
                    $"destination={candidate.FollowUpDestination} alternateReserves={DescribeReloadReserves(alternateReloadReserves)} " +
                    $"result=movePlanned");
                return true;
            }

            // These magazines were evaluated as tactical support. Overflow remains at the source
            // and must not fall through to ordinary container cargo pickup.
            foreach (BodyGearCandidate candidate in candidates)
            {
                attemptedSourceItemIds.Add(candidate.Item.Id);
            }

            Modules.Logger.LogInfo(
                $"[LootCommand][SupportMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} candidates={candidates.Count} " +
                $"alternateReserves={DescribeReloadReserves(alternateReloadReserves)} result=noReloadSafeFastAccessSpace");
            return false;
        }

        private List<MagazineItemClass> GetAlternateReloadReservesForSupportMagazinePlan(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon plannedSupport)
        {
            List<MagazineItemClass> reserves = new List<MagazineItemClass>();
            foreach (EquipmentSlot slot in new[]
                     {
                         EquipmentSlot.FirstPrimaryWeapon,
                         EquipmentSlot.SecondPrimaryWeapon,
                         EquipmentSlot.Holster
                     })
            {
                Weapon equipped = followerEquipment?.GetSlot(slot)?.ContainedItem as Weapon;
                if (equipped == null || IsSameLootItem(equipped, plannedSupport))
                {
                    continue;
                }

                MagazineItemClass reserve = GetReloadReserveForEquippedWeapon(
                    inventory,
                    followerEquipment,
                    equipped);
                if (reserve != null)
                {
                    reserves.Add(reserve);
                }
            }

            return reserves
                .GroupBy(magazine => magazine.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
        }

        private MagazineItemClass? GetReloadReserveForEquippedWeapon(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon)
        {
            if (weapon == null || weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return null;
            }

            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon);
            if (readiness?.Threshold > 0 && readiness.InsertedContribution >= readiness.Threshold)
            {
                // A high-capacity inserted magazine that satisfies readiness by itself needs no
                // protected landing slot under the established primary policy.
                return null;
            }

            MagazineItemClass inserted = GetCurrentMagazineSafely(weapon);
            if (inserted != null &&
                FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(
                    followerEquipment,
                    inserted))
            {
                return inserted;
            }

            bool isLootedWeapon = InteractableObjects.IsLootedWeapon(BotOwner, weapon);
            return GetFastAccessMagazines(followerEquipment)
                .Where(magazine =>
                    magazine.Count > 0 &&
                    (!isLootedWeapon ||
                     InteractableObjects.IsApprovedLootedWeaponMagazine(BotOwner, weapon, magazine)) &&
                    IsMagazineCompatibleWithWeapon(weapon, magazine) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine) &&
                    FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(
                        followerEquipment,
                        magazine))
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(magazine => magazine.MaxCount)
                .FirstOrDefault();
        }

        private string DescribeReloadReserves(IEnumerable<MagazineItemClass> reserves)
        {
            List<string> descriptions = reserves?
                .Where(magazine => magazine != null)
                .Select(DescribeLootDebugItem)
                .ToList() ?? new List<string>();
            return descriptions.Count == 0 ? "none" : string.Join(" | ", descriptions);
        }

        private bool TryBuildEquippedTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            Weapon weapon,
            string role,
            Weapon? deferredWeapon,
            Func<MagazineItemClass, bool>? fastAccessMagazineEligibility,
            Func<AmmoItemClass, bool>? carriedAmmoSupplyEligibility,
            Func<IEnumerable<AmmoItemClass>> carriedCartridgeFactory,
            bool allowSearchedSourceTopOff,
            out BodyGearMove? move,
            bool allowLooseAmmoCarry)
        {
            move = null;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceRoot == null ||
                sourceAmmoFactory == null ||
                carriedCartridgeFactory == null ||
                weapon == null ||
                weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return false;
            }

            List<BodyGearCandidate> source = sourceAmmoFactory(weapon)
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    !string.IsNullOrEmpty(ammo.Id) &&
                    !attemptedSourceItemIds.Contains(ammo.Id) &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => CreateTacticalAmmoCandidate(group.First(), weapon, role))
                .ToList();

            // Magazine maintenance is evaluated before strategic source-ammo acquisition. Managed
            // non-Realistic secure-container stacks therefore remain useful top-off supplies instead
            // of making an empty fast-access magazine look "stocked" and blocking the operation.
            if (TryBuildEquippedMagazineMaintenanceTopOffMove(
                    inventory,
                    followerEquipment,
                    sourceRoot,
                    weapon,
                    source,
                    role,
                    fastAccessMagazineEligibility,
                    carriedAmmoSupplyEligibility,
                    allowSearchedSourceTopOff,
                    out move))
            {
                return true;
            }

            if (source.Count == 0)
            {
                return false;
            }

            List<AmmoItemClass> carried = carriedCartridgeFactory()
                .Where(ammo => ammo != null && !string.IsNullOrEmpty(ammo.Id))
                .GroupBy(ammo => ammo.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            int reserveTargetRounds = ResolveWeaponTacticalAmmoReserveTarget(
                inventory,
                weapon,
                carried.Concat(source.Select(candidate => (AmmoItemClass)candidate.Item)),
                fastAccessMagazineEligibility);
            List<TacticalAmmoSourceEvaluation> evaluations = source
                .Select(candidate =>
                {
                    AmmoItemClass ammo = (AmmoItemClass)candidate.Item;
                    int availableRounds = source
                        .Where(entry => string.Equals(entry.Item.TemplateId, ammo.TemplateId, StringComparison.Ordinal))
                        .Sum(entry => Math.Max(0, entry.Item.StackObjectsCount));
                    TacticalAmmoDecision policyDecision = EvaluateTacticalAmmoCandidate(
                        ammo,
                        carried,
                        availableRounds,
                        reserveTargetRounds,
                        allowUpgrade: true);
                    bool alreadyCarriesSameTemplate = carried.Any(existing =>
                        string.Equals(
                            existing.TemplateId.ToString(),
                            ammo.TemplateId.ToString(),
                            StringComparison.Ordinal));
                    SearchableItemItemClass secure = CloneSearchableContainer(
                        followerEquipment.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem);
                    bool useSecureOverride = TrySimulateContainerAdd(secure, ammo, out _) &&
                        FollowerTacticalAmmoPolicy.CanUseSecureStorageOverride(
                            policyDecision,
                            alreadyCarriesSameTemplate);
                    TacticalAmmoDecision decision = !policyDecision.ShouldAcquire && useSecureOverride
                        ? AcceptForSecureContainer(policyDecision)
                        : policyDecision;
                    return new TacticalAmmoSourceEvaluation(candidate, decision);
                })
                .OrderByDescending(evaluation => evaluation.Decision.Kind)
                .ThenByDescending(evaluation => ((AmmoItemClass)evaluation.Candidate.Item).PenetrationPower)
                .ThenByDescending(evaluation => ((AmmoItemClass)evaluation.Candidate.Item).Damage)
                .ThenByDescending(evaluation => ((AmmoItemClass)evaluation.Candidate.Item).ArmorDamage)
                .ToList();

            foreach (TacticalAmmoSourceEvaluation evaluation in evaluations)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"role={role} weapon={DescribeLootDebugItem(weapon)} source={DescribeLooseAmmo(evaluation.Candidate.Item as AmmoItemClass)} " +
                    evaluation.Decision.ToDiagnosticString());
            }

            TacticalAmmoSourceEvaluation selected = evaluations.FirstOrDefault(evaluation =>
                evaluation.Decision.ShouldAcquire);
            if (selected == null)
            {
                foreach (BodyGearCandidate candidate in source)
                {
                    MarkTacticalAmmoCandidateComplete(candidate, attemptedSourceItemIds, deferredWeapon);
                }

                return false;
            }

            AmmoItemClass selectedAmmo = selected.Candidate.Item as AmmoItemClass;
            if (TryBuildWeaponLooseAmmoMove(
                    inventory,
                    followerEquipment,
                    selected.Candidate,
                    requireWeaponOnFollower: true,
                    out move,
                    out string carryReason))
            {
                attemptedSourceItemIds.Add(selectedAmmo.Id);
                return true;
            }

            if (!allowLooseAmmoCarry)
            {
                return false;
            }

            MarkTacticalAmmoCandidateComplete(selected.Candidate, attemptedSourceItemIds, deferredWeapon);
            Modules.Logger.LogInfo(
                $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"role={role} weapon={DescribeLootDebugItem(weapon)} source={DescribeLooseAmmo(selectedAmmo)} " +
                $"decision=Replenish result=skip reason={carryReason}");
            return false;
        }

        private static Weapon? GetEligibleDetachableWeaponForSlot(
            InventoryEquipment followerEquipment,
            EquipmentSlot weaponSlot)
        {
            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                return null;
            }

            Weapon weapon = weaponSlot is EquipmentSlot.FirstPrimaryWeapon or
                EquipmentSlot.SecondPrimaryWeapon or EquipmentSlot.Holster
                ? followerEquipment.GetSlot(weaponSlot)?.ContainedItem as Weapon
                : null;
            return weapon?.ReloadMode == Weapon.EReloadMode.ExternalMagazine
                ? weapon
                : null;
        }

        private static Weapon? GetEligibleSupportAmmoMaintenanceWeapon(
            InventoryEquipment followerEquipment,
            EquipmentSlot? supportSlot = null)
        {
            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                return null;
            }

            Weapon support = supportSlot.HasValue
                ? followerEquipment.GetSlot(supportSlot.Value)?.ContainedItem as Weapon
                : GetSingleSupportWeapon(followerEquipment);
            return support?.ReloadMode == Weapon.EReloadMode.ExternalMagazine ||
                   FollowerWeaponLooseFeedReadiness.IsSupported(support)
                ? support
                : null;
        }

        private IEnumerable<AmmoItemClass> GetSupportTacticalCartridgeItems(
            InventoryEquipment followerEquipment,
            Weapon support,
            Func<MagazineItemClass, bool>? magazineEligibility)
        {
            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);

            // The inserted magazine and chamber belong to the support weapon itself.
            foreach (AmmoItemClass ammo in SnapshotLootTreeItems(support).OfType<AmmoItemClass>())
            {
                if (IsCompatibleUniqueAmmo(support, ammo, yieldedIds))
                {
                    yield return ammo;
                }
            }

            // Reload-access supply is restricted to the magazines this support weapon may use.
            foreach (MagazineItemClass magazine in GetFastAccessMagazines(followerEquipment)
                         .Where(magazine => magazineEligibility == null || magazineEligibility(magazine)))
            {
                foreach (AmmoItemClass ammo in SnapshotLootTreeItems(magazine).OfType<AmmoItemClass>())
                {
                    if (IsCompatibleUniqueAmmo(support, ammo, yieldedIds))
                    {
                        yield return ammo;
                    }
                }
            }

            foreach (AmmoItemClass ammo in GetFollowerWeaponLooseAmmoItems(
                         followerEquipment,
                         support,
                         includeStrictCargo: true))
            {
                if (IsSupportCarriedAmmoSupplyEligible(ammo, magazineEligibility) &&
                    IsCompatibleUniqueAmmo(support, ammo, yieldedIds))
                {
                    yield return ammo;
                }
            }
        }

        private static bool IsCompatibleUniqueAmmo(
            Weapon weapon,
            AmmoItemClass ammo,
            HashSet<string> yieldedIds)
        {
            return ammo != null &&
                   !string.IsNullOrEmpty(ammo.Id) &&
                   yieldedIds.Add(ammo.Id) &&
                   FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, ammo);
        }

        private static bool IsSupportCarriedAmmoSupplyEligible(
            AmmoItemClass ammo,
            Func<MagazineItemClass, bool>? magazineEligibility)
        {
            // Loose stacks are shared supply. Cartridges inside a magazine may be donated only
            // when that magazine belongs to the support weapon's permitted reload package.
            return !TryGetMagazineDonor(ammo, out MagazineItemClass? donor) ||
                   magazineEligibility == null ||
                   magazineEligibility(donor);
        }

        private static void MarkTacticalAmmoCandidateComplete(
            BodyGearCandidate candidate,
            HashSet<string> attemptedSourceItemIds,
            Weapon? deferredWeapon)
        {
            if (candidate?.Item is not AmmoItemClass ammo ||
                string.IsNullOrEmpty(ammo.Id))
            {
                return;
            }

            // Primary owns first refusal. The one vanilla support weapon (secondary first,
            // otherwise holster) gets one chance before ordinary filtered loot is suppressed.
            if (deferredWeapon != null &&
                FollowerWeaponLooseAmmoSupport.IsCompatible(deferredWeapon, ammo))
            {
                return;
            }

            attemptedSourceItemIds.Add(ammo.Id);
        }

        private static BodyGearCandidate CreateTacticalAmmoCandidate(
            BodyGearCandidate source,
            Weapon weapon,
            string role)
        {
            return new BodyGearCandidate(
                source.Item,
                source.SourceSlot,
                $"{source.SourceName}.{role}TacticalAmmo",
                source.SourceTier,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: false,
                followUpDestination: BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                weaponSupportWeapon: weapon);
        }

        private static BodyGearCandidate CreateMagazineMaintenanceAmmoCandidate(
            AmmoItemClass ammo,
            Weapon weapon,
            string role)
        {
            return new BodyGearCandidate(
                ammo,
                null,
                $"FollowerSupply.{role}MagazineTopOff",
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true,
                followUpDestination: BodyGearFollowUpDestination.TopOffWeaponMagazine,
                weaponSupportWeapon: weapon);
        }

        private bool TryBuildEquippedMagazineMaintenanceTopOffMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Weapon weapon,
            IEnumerable<BodyGearCandidate> sourceCandidates,
            string role,
            Func<MagazineItemClass, bool>? fastAccessMagazineEligibility,
            Func<AmmoItemClass, bool>? carriedAmmoSupplyEligibility,
            bool allowSearchedSourceTopOff,
            out BodyGearMove? move)
        {
            move = null;
            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon,
                internalAmmoEligibility: null,
                fastAccessMagazineEligibility);
            if (readiness?.PrimaryReady == true)
            {
                return false;
            }

            List<BodyGearCandidate> carriedSupply = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: true)
                .Where(ammo => carriedAmmoSupplyEligibility == null || carriedAmmoSupplyEligibility(ammo))
                .Select(ammo => CreateMagazineMaintenanceAmmoCandidate(ammo, weapon, role))
                .ToList();

            // Callers decide whether searched rounds may enter this weapon's magazines. This keeps
            // protected spawn gear isolated while allowing tracked looted magazines to retain ammo.
            IEnumerable<BodyGearCandidate> eligibleSourceSupply = allowSearchedSourceTopOff
                ? sourceCandidates ?? Enumerable.Empty<BodyGearCandidate>()
                : Enumerable.Empty<BodyGearCandidate>();
            List<BodyGearCandidate> supplies = carriedSupply
                .Concat(eligibleSourceSupply)
                .Where(candidate => candidate?.Item is AmmoItemClass)
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.ReportAsLootNothing ? 0 : 1)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).PenetrationPower)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).Damage)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).ArmorDamage)
                .ThenBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .ToList();

            foreach (BodyGearCandidate supply in supplies)
            {
                MagazineItemClass inserted = GetCurrentMagazineSafely(weapon);
                if (inserted != null &&
                    inserted.Count < inserted.MaxCount &&
                    (fastAccessMagazineEligibility == null || fastAccessMagazineEligibility(inserted)) &&
                    supply.Item is AmmoItemClass insertedAmmo &&
                    CanTopOffMagazineWithAmmo(weapon, inserted, insertedAmmo))
                {
                    int transferCount = Math.Min(
                        inserted.MaxCount - inserted.Count,
                        insertedAmmo.StackObjectsCount);
                    BodyGearCandidate topOffCandidate = supply.WithMagazineAmmoTransferContext(
                        BodyGearFollowUpDestination.TopOffWeaponMagazine,
                        weapon,
                        inserted,
                        transferCount);
                    if (TryBuildInsertedMagazineTopOffChain(
                            inventory,
                            followerEquipment,
                            sourceRoot,
                            weapon,
                            inserted,
                            topOffCandidate,
                            out move,
                            out string insertedReason))
                    {
                        Modules.Logger.LogInfo(
                            $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                            $"role={role} weapon={DescribeLootDebugItem(weapon)} action=maintenanceTopOff " +
                            $"target=inserted supply={(supply.ReportAsLootNothing ? "carried" : "searchedSource")} " +
                            readiness.ToDiagnosticString());
                        return true;
                    }

                    Modules.Logger.LogInfo(
                        $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"role={role} weapon={DescribeLootDebugItem(weapon)} action=maintenanceTopOff " +
                        $"target=inserted result=skip reason={insertedReason}");
                }

                if (!TryBuildEquippedCarriedMagazineLoadMove(
                        inventory,
                        followerEquipment,
                        weapon,
                        supply,
                        role,
                        fastAccessMagazineEligibility,
                        out move,
                        out _))
                {
                    continue;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"role={role} weapon={DescribeLootDebugItem(weapon)} action=maintenanceTopOff " +
                    $"supply={(supply.ReportAsLootNothing ? "carried" : "searchedSource")} " +
                    readiness.ToDiagnosticString());
                return true;
            }

            return false;
        }

        private bool TryBuildEquippedCarriedMagazineLoadMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            BodyGearCandidate candidate,
            string role,
            Func<MagazineItemClass, bool>? fastAccessMagazineEligibility,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "noOperationalMagazineGap";
            if (candidate?.Item is not AmmoItemClass ammo)
            {
                return false;
            }

            foreach (MagazineItemClass magazine in GetFastAccessMagazines(followerEquipment)
                         .Where(magazine =>
                             magazine.Count < magazine.MaxCount &&
                             (fastAccessMagazineEligibility == null ||
                              fastAccessMagazineEligibility(magazine)) &&
                             IsMagazineCompatibleWithWeapon(weapon, magazine) &&
                             CanTopOffMagazineWithAmmo(weapon, magazine, ammo))
                         .OrderByDescending(magazine =>
                             magazine.Cartridges?.Last is AmmoItemClass top &&
                             string.Equals(top.TemplateId, ammo.TemplateId, StringComparison.Ordinal))
                         .ThenByDescending(magazine =>
                             magazine.MaxCount > 0 ? (float)magazine.Count / magazine.MaxCount : 0f)
                         .ThenBy(magazine => magazine.Id, StringComparer.Ordinal))
            {
                int transferCount = Math.Min(
                    magazine.MaxCount - magazine.Count,
                    ammo.StackObjectsCount);
                GStruct153 applyResult = magazine.Apply(inventory, ammo, transferCount, true);
                if (applyResult.Failed || applyResult.Value == null)
                {
                    reason = $"applyRejected:{DescribeInventoryError(applyResult.Error)}";
                    continue;
                }

                move = new BodyGearMove(
                    ammo,
                    applyResult.Value,
                    candidate.SourceName,
                    reportAsLootNothing: candidate.ReportAsLootNothing,
                    storeAsLoot: false,
                    successPhrase: EPhraseTrigger.LootGeneric,
                    isStagingOperation: true,
                    stagingWeapon: weapon,
                    stagingMagazine: magazine,
                    stagingMagazineRoundsBefore: magazine.Count,
                    terminalOnStagingFailure: false,
                    announceStagingLoot: !candidate.ReportAsLootNothing);
                reason = "ok";
                Modules.Logger.LogInfo(
                    $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"role={role} weapon={DescribeLootDebugItem(weapon)} action=load target={DescribeLootDebugItem(magazine)} " +
                    $"source={DescribeLooseAmmo(ammo)} count={transferCount}");
                return true;
            }

            return false;
        }

        private sealed class TacticalAmmoSourceEvaluation
        {
            public TacticalAmmoSourceEvaluation(
                BodyGearCandidate candidate,
                TacticalAmmoDecision decision)
            {
                Candidate = candidate;
                Decision = decision;
            }

            public BodyGearCandidate Candidate { get; }
            public TacticalAmmoDecision Decision { get; }
        }

    }
}
