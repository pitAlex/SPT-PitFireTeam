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
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildPrimaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    corpseEquipment,
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    out BodyGearMove? move))
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

        private bool TryStartBodySecondaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    corpseEquipment,
                    weapon => GetBodyWeaponLooseAmmoCandidates(corpseEquipment, weapon),
                    bodyLootAttemptedItemIds,
                    out BodyGearMove? move))
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

        private bool TryStartBodySecondaryTacticalMagazineMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryTacticalMagazineMove(
                    inventory,
                    followerEquipment,
                    (root, weapon) => GetBodyOperationalMagazineCandidates(
                        (InventoryEquipment)root,
                        weapon),
                    corpseEquipment,
                    bodyLootAttemptedItemIds,
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

        private bool TryStartContainerPrimaryTacticalAmmoMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildPrimaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    containerRoot,
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    out BodyGearMove? move))
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

        private bool TryStartContainerSecondaryTacticalAmmoMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryTacticalAmmoMove(
                    inventory,
                    followerEquipment,
                    containerRoot,
                    weapon => GetContainerWeaponLooseAmmoCandidates(containerRoot, weapon),
                    containerLootAttemptedItemIds,
                    out BodyGearMove? move))
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

        private bool TryStartContainerSecondaryTacticalMagazineMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            if (!TryBuildSecondaryTacticalMagazineMove(
                    inventory,
                    followerEquipment,
                    (root, weapon) => GetContainerOperationalMagazineCandidates(
                        (SearchableItemItemClass)root,
                        weapon),
                    containerRoot,
                    containerLootAttemptedItemIds,
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

        private bool TryBuildPrimaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            Weapon primary = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem as Weapon;
            Weapon deferredSecondary = GetEligibleSecondaryAmmoMaintenanceWeapon(followerEquipment);
            return TryBuildEquippedTacticalAmmoMove(
                inventory,
                followerEquipment,
                sourceRoot,
                sourceAmmoFactory,
                attemptedSourceItemIds,
                primary,
                role: "primary",
                deferredWeapon: deferredSecondary,
                fastAccessMagazineEligibility: null,
                carriedAmmoSupplyEligibility: null,
                carriedCartridgeFactory: () => GetFollowerWeaponCartridgeItems(followerEquipment, primary),
                allowSearchedSourceTopOff: pitFireTeam.IsFollowerLoadoutLootableMode(),
                out move);
        }

        private bool TryBuildSecondaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            Weapon secondary = GetEligibleDetachableSecondaryWeapon(followerEquipment);
            bool isLootedSecondary = InteractableObjects.IsLootedWeapon(BotOwner, secondary);
            Func<MagazineItemClass, bool>? magazineEligibility = isLootedSecondary
                ? magazine => InteractableObjects.IsApprovedLootedWeaponMagazine(
                    BotOwner,
                    secondary,
                    magazine)
                : null;

            return TryBuildEquippedTacticalAmmoMove(
                inventory,
                followerEquipment,
                sourceRoot,
                sourceAmmoFactory,
                attemptedSourceItemIds,
                secondary,
                role: "secondary",
                deferredWeapon: null,
                fastAccessMagazineEligibility: magazineEligibility,
                carriedAmmoSupplyEligibility: ammo => IsSecondaryCarriedAmmoSupplyEligible(
                    ammo,
                    magazineEligibility),
                carriedCartridgeFactory: () => GetSecondaryTacticalCartridgeItems(
                    followerEquipment,
                    secondary,
                    magazineEligibility),
                // Source rounds become part of tracked loot when they enter an approved looted
                // secondary magazine, so Simple/Restricted do not touch protected spawn gear.
                allowSearchedSourceTopOff: isLootedSecondary || pitFireTeam.IsFollowerLoadoutLootableMode(),
                out move);
        }

        private bool TryBuildSecondaryTacticalMagazineMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Func<Item, Weapon, IEnumerable<BodyGearCandidate>> sourceMagazineFactory,
            Item sourceRoot,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move,
            out bool hadCompatibleMagazine)
        {
            move = null;
            hadCompatibleMagazine = false;
            Weapon secondary = GetEligibleDetachableSecondaryWeapon(followerEquipment);
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceMagazineFactory == null ||
                sourceRoot == null ||
                secondary == null)
            {
                return false;
            }

            List<BodyGearCandidate> candidates = sourceMagazineFactory(sourceRoot, secondary)
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

            bool isLootedSecondary = InteractableObjects.IsLootedWeapon(BotOwner, secondary);
            Func<MagazineItemClass, bool>? existingMagazineEligibility = isLootedSecondary
                ? magazine => InteractableObjects.IsApprovedLootedWeaponMagazine(
                    BotOwner,
                    secondary,
                    magazine)
                : null;
            MagazineItemClass primaryReloadReserve = GetPrimaryReloadReserveForSecondaryMagazinePlan(
                inventory,
                followerEquipment);
            OperationalMagazinePlan plan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                secondary,
                candidates,
                allowEmptyCandidates: false,
                existingFastAccessMagazineEligibility: existingMagazineEligibility,
                alternateReloadReserveItems: primaryReloadReserve != null
                    ? new[] { primaryReloadReserve }
                    : Array.Empty<MagazineItemClass>());

            foreach (BodyGearCandidate candidate in plan.FollowUps.Where(IsOperationalFastAccessFollowUp))
            {
                if (!TryBuildSupportMagazineFollowUpMove(
                        inventory,
                        followerEquipment,
                        candidate,
                        out move,
                        out string reason))
                {
                    attemptedSourceItemIds.Add(candidate.Item.Id);
                    Modules.Logger.LogInfo(
                        $"[LootCommand][SecondaryMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(secondary)} magazine={DescribeLootDebugItem(candidate.Item)} " +
                        $"result=moveRejected reason={reason}");
                    continue;
                }

                attemptedSourceItemIds.Add(candidate.Item.Id);
                Modules.Logger.LogInfo(
                    $"[LootCommand][SecondaryMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(secondary)} magazine={DescribeLootDebugItem(candidate.Item)} " +
                    $"destination={candidate.FollowUpDestination} primaryReserve={DescribeLootDebugItem(primaryReloadReserve)} " +
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
                $"[LootCommand][SecondaryMagazine] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(secondary)} candidates={candidates.Count} " +
                $"primaryReserve={DescribeLootDebugItem(primaryReloadReserve)} result=noReloadSafeFastAccessSpace");
            return false;
        }

        private MagazineItemClass? GetPrimaryReloadReserveForSecondaryMagazinePlan(
            InventoryController inventory,
            InventoryEquipment followerEquipment)
        {
            Weapon primary = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem as Weapon;
            if (primary == null || primary.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return null;
            }

            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                primary);
            if (readiness?.Threshold > 0 && readiness.InsertedContribution >= readiness.Threshold)
            {
                // A high-capacity inserted magazine that satisfies readiness by itself needs no
                // protected landing slot under the established primary policy.
                return null;
            }

            MagazineItemClass inserted = GetCurrentMagazineSafely(primary);
            if (inserted != null &&
                FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(
                    followerEquipment,
                    inserted))
            {
                return inserted;
            }

            bool isLootedPrimary = InteractableObjects.IsLootedWeapon(BotOwner, primary);
            return GetFastAccessMagazines(followerEquipment)
                .Where(magazine =>
                    magazine.Count > 0 &&
                    (!isLootedPrimary ||
                     InteractableObjects.IsApprovedLootedWeaponMagazine(BotOwner, primary, magazine)) &&
                    IsMagazineCompatibleWithWeapon(primary, magazine) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(primary, magazine) &&
                    FollowerWeaponPrimaryReadiness.HasMagazineReloadLandingSpace(
                        followerEquipment,
                        magazine))
                .OrderByDescending(GetMagazineCellArea)
                .ThenByDescending(GetMagazineLongestSide)
                .ThenByDescending(magazine => magazine.MaxCount)
                .FirstOrDefault();
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
            out BodyGearMove? move)
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

            MarkTacticalAmmoCandidateComplete(selected.Candidate, attemptedSourceItemIds, deferredWeapon);
            Modules.Logger.LogInfo(
                $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"role={role} weapon={DescribeLootDebugItem(weapon)} source={DescribeLooseAmmo(selectedAmmo)} " +
                $"decision=Replenish result=skip reason={carryReason}");
            return false;
        }

        private static Weapon? GetEligibleDetachableSecondaryWeapon(
            InventoryEquipment followerEquipment)
        {
            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                return null;
            }

            Weapon secondary = followerEquipment
                .GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem as Weapon;
            return secondary?.ReloadMode == Weapon.EReloadMode.ExternalMagazine
                ? secondary
                : null;
        }

        private static Weapon? GetEligibleSecondaryAmmoMaintenanceWeapon(
            InventoryEquipment followerEquipment)
        {
            if (followerEquipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem is not Weapon)
            {
                return null;
            }

            Weapon secondary = followerEquipment
                .GetSlot(EquipmentSlot.SecondPrimaryWeapon)
                ?.ContainedItem as Weapon;
            return secondary?.ReloadMode == Weapon.EReloadMode.ExternalMagazine ||
                   FollowerWeaponLooseFeedReadiness.IsSupported(secondary)
                ? secondary
                : null;
        }

        private IEnumerable<AmmoItemClass> GetSecondaryTacticalCartridgeItems(
            InventoryEquipment followerEquipment,
            Weapon secondary,
            Func<MagazineItemClass, bool>? magazineEligibility)
        {
            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);

            // The inserted magazine and chamber belong to the support weapon itself.
            foreach (AmmoItemClass ammo in SnapshotLootTreeItems(secondary).OfType<AmmoItemClass>())
            {
                if (IsCompatibleUniqueAmmo(secondary, ammo, yieldedIds))
                {
                    yield return ammo;
                }
            }

            // Reload-access supply is restricted to the magazines this secondary may actually use.
            foreach (MagazineItemClass magazine in GetFastAccessMagazines(followerEquipment)
                         .Where(magazine => magazineEligibility == null || magazineEligibility(magazine)))
            {
                foreach (AmmoItemClass ammo in SnapshotLootTreeItems(magazine).OfType<AmmoItemClass>())
                {
                    if (IsCompatibleUniqueAmmo(secondary, ammo, yieldedIds))
                    {
                        yield return ammo;
                    }
                }
            }

            foreach (AmmoItemClass ammo in GetFollowerWeaponLooseAmmoItems(
                         followerEquipment,
                         secondary,
                         includeStrictCargo: true))
            {
                if (IsSecondaryCarriedAmmoSupplyEligible(ammo, magazineEligibility) &&
                    IsCompatibleUniqueAmmo(secondary, ammo, yieldedIds))
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

        private static bool IsSecondaryCarriedAmmoSupplyEligible(
            AmmoItemClass ammo,
            Func<MagazineItemClass, bool>? magazineEligibility)
        {
            // Loose stacks are shared supply. Cartridges inside a magazine may be donated only
            // when that magazine belongs to the secondary's permitted reload package.
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

            // Primary owns first refusal. A compatible equipped secondary gets one chance to use
            // a stack that primary left unresolved before ordinary filtered loot is suppressed.
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
