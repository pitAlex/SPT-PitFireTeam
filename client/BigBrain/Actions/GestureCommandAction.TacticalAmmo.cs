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

        private bool TryBuildPrimaryTacticalAmmoMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Func<Weapon, IEnumerable<BodyGearCandidate>> sourceAmmoFactory,
            HashSet<string> attemptedSourceItemIds,
            out BodyGearMove? move)
        {
            move = null;
            Weapon primary = followerEquipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)
                ?.ContainedItem as Weapon;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                inventory == null ||
                followerEquipment == null ||
                sourceRoot == null ||
                sourceAmmoFactory == null ||
                primary == null ||
                primary.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return false;
            }

            List<BodyGearCandidate> source = sourceAmmoFactory(primary)
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    !string.IsNullOrEmpty(ammo.Id) &&
                    !attemptedSourceItemIds.Contains(ammo.Id) &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(primary, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => CreateTacticalAmmoCandidate(group.First(), primary))
                .ToList();

            // Magazine maintenance is evaluated before strategic source-ammo acquisition. Managed
            // non-Realistic secure-container stacks therefore remain useful top-off supplies instead
            // of making an empty fast-access magazine look "stocked" and blocking the operation.
            if (TryBuildPrimaryMagazineMaintenanceTopOffMove(
                    inventory,
                    followerEquipment,
                    primary,
                    source,
                    out move))
            {
                return true;
            }

            if (source.Count == 0)
            {
                return false;
            }

            List<AmmoItemClass> carried = GetFollowerWeaponCartridgeItems(followerEquipment, primary).ToList();
            int reserveTargetRounds = ResolveWeaponTacticalAmmoReserveTarget(
                inventory,
                primary,
                carried.Concat(source.Select(candidate => (AmmoItemClass)candidate.Item)));
            List<TacticalAmmoSourceEvaluation> evaluations = source
                .Select(candidate =>
                {
                    AmmoItemClass ammo = (AmmoItemClass)candidate.Item;
                    int availableRounds = source
                        .Where(entry => string.Equals(entry.Item.TemplateId, ammo.TemplateId, StringComparison.Ordinal))
                        .Sum(entry => Math.Max(0, entry.Item.StackObjectsCount));
                    return new TacticalAmmoSourceEvaluation(
                        candidate,
                        EvaluateTacticalAmmoCandidate(
                            ammo,
                             carried,
                             availableRounds,
                             reserveTargetRounds,
                             allowUpgrade: false));
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
                    $"weapon={DescribeLootDebugItem(primary)} source={DescribeLooseAmmo(evaluation.Candidate.Item as AmmoItemClass)} " +
                    evaluation.Decision.ToDiagnosticString());
            }

            TacticalAmmoSourceEvaluation selected = evaluations.FirstOrDefault(evaluation =>
                evaluation.Decision.ShouldAcquire);
            if (selected == null)
            {
                foreach (BodyGearCandidate candidate in source)
                {
                    attemptedSourceItemIds.Add(candidate.Item.Id);
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

            attemptedSourceItemIds.Add(selectedAmmo.Id);
            Modules.Logger.LogInfo(
                $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(primary)} source={DescribeLooseAmmo(selectedAmmo)} " +
                $"decision=Replenish result=skip reason={carryReason}");
            return false;
        }

        private static BodyGearCandidate CreateTacticalAmmoCandidate(
            BodyGearCandidate source,
            Weapon weapon)
        {
            return new BodyGearCandidate(
                source.Item,
                source.SourceSlot,
                $"{source.SourceName}.PrimaryTacticalAmmo",
                source.SourceTier,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: false,
                followUpDestination: BodyGearFollowUpDestination.WeaponSupportLooseAmmo,
                weaponSupportWeapon: weapon);
        }

        private static BodyGearCandidate CreatePrimaryMaintenanceAmmoCandidate(
            AmmoItemClass ammo,
            Weapon weapon)
        {
            return new BodyGearCandidate(
                ammo,
                null,
                "FollowerSupply.PrimaryMagazineTopOff",
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true,
                followUpDestination: BodyGearFollowUpDestination.TopOffWeaponMagazine,
                weaponSupportWeapon: weapon);
        }

        private bool TryBuildPrimaryMagazineMaintenanceTopOffMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            IEnumerable<BodyGearCandidate> sourceCandidates,
            out BodyGearMove? move)
        {
            move = null;
            WeaponPrimaryReadinessSnapshot readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                inventory,
                weapon);
            if (readiness?.PrimaryReady == true)
            {
                return false;
            }

            List<BodyGearCandidate> carriedSupply = GetFollowerWeaponLooseAmmoItems(
                    followerEquipment,
                    weapon,
                    includeStrictCargo: true)
                .Select(ammo => CreatePrimaryMaintenanceAmmoCandidate(ammo, weapon))
                .ToList();

            // Simple/Restricted must not merge raid-looted cartridges into protected spawned
            // magazines. Their managed carried supply still performs the same maintenance. In
            // Immersive/Realistic, source rounds may safely become part of the persistent kit.
            IEnumerable<BodyGearCandidate> eligibleSourceSupply = pitFireTeam.IsFollowerLoadoutLootableMode()
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
                if (!TryBuildPrimaryCarriedMagazineLoadMove(
                        inventory,
                        followerEquipment,
                        weapon,
                        supply,
                        out move,
                        out _))
                {
                    continue;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][TacticalAmmo] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} action=maintenanceTopOff " +
                    $"supply={(supply.ReportAsLootNothing ? "carried" : "searchedSource")} " +
                    readiness.ToDiagnosticString());
                return true;
            }

            return false;
        }

        private bool TryBuildPrimaryCarriedMagazineLoadMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Weapon weapon,
            BodyGearCandidate candidate,
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
                    $"weapon={DescribeLootDebugItem(weapon)} action=load target={DescribeLootDebugItem(magazine)} " +
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
