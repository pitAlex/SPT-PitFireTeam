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
        private bool TryBuildPrimaryMagazineTopOffStagingMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            Item sourceRoot,
            Weapon weapon,
            IEnumerable<BodyGearCandidate>? sourceMagazineCandidates,
            IEnumerable<BodyGearCandidate>? sourceAmmoCandidates,
            out BodyGearMove? move)
        {
            move = null;
            if (inventory == null ||
                followerEquipment == null ||
                sourceRoot == null ||
                weapon == null ||
                weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return false;
            }

            MagazineItemClass insertedMagazine = GetCurrentMagazineSafely(weapon);
            if (insertedMagazine != null &&
                insertedMagazine.Count > 0 &&
                !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, insertedMagazine))
            {
                // Filling good rounds around an incompatible inserted load cannot make the weapon
                // usable. Magazine replacement/migration owns that later scenario.
                return false;
            }

            List<BodyGearCandidate> looseAmmo = sourceAmmoCandidates?
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    IsItemInsideRoot(ammo, sourceRoot) &&
                    FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<BodyGearCandidate>();
            if (looseAmmo.Count == 0)
            {
                return false;
            }

            List<BodyGearCandidate> refillableSourceMagazines = sourceMagazineCandidates?
                .GroupBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList() ?? new List<BodyGearCandidate>();
            OperationalMagazinePlan refillPlan = PlanOperationalMagazineFollowUps(
                inventory,
                followerEquipment,
                weapon,
                refillableSourceMagazines,
                allowEmptyCandidates: true);

            List<MagazineTopOffTarget> targets = BuildMagazineTopOffTargets(
                weapon,
                insertedMagazine,
                refillPlan);
            WeaponPrimaryReadinessSnapshot projectedBeforeTopOff = EvaluateMagazinePlanProjection(
                inventory,
                weapon,
                refillPlan);
            if (projectedBeforeTopOff.PrimaryReady)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} result=skipAlreadyReady " +
                    projectedBeforeTopOff.ToDiagnosticString());
                return false;
            }

            int reserveTargetRounds = ResolveMagazineTopOffReserveTargetRounds(
                projectedBeforeTopOff,
                targets);
            List<AmmoItemClass> carriedAmmo = GetFollowerWeaponCartridgeItems(
                    followerEquipment,
                    weapon)
                .ToList();
            foreach (MagazineTopOffTarget target in targets)
            {
                BodyGearCandidate ammoCandidate = SelectMagazineTopOffAmmo(
                    weapon,
                    target.Magazine,
                    looseAmmo,
                    carriedAmmo,
                    reserveTargetRounds,
                    out TacticalAmmoDecision tacticalDecision);
                if (ammoCandidate?.Item is not AmmoItemClass ammo)
                {
                    continue;
                }

                int transferCount = Math.Min(
                    target.Magazine.MaxCount - target.Magazine.Count,
                    ammo.StackObjectsCount);
                if (transferCount <= 0)
                {
                    continue;
                }

                BodyGearCandidate topOffCandidate = ammoCandidate.WithMagazineAmmoTransferContext(
                    BodyGearFollowUpDestination.TopOffWeaponMagazine,
                    weapon,
                    target.Magazine,
                    transferCount);
                if (target.IsInsertedMagazine)
                {
                    if (TryBuildInsertedMagazineTopOffChain(
                            inventory,
                            sourceRoot,
                            weapon,
                            target.Magazine,
                            topOffCandidate,
                            out move,
                            out string insertedReason))
                    {
                        return true;
                    }

                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                        $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(target.Magazine)} " +
                        $"result=skipInserted reason={insertedReason} {tacticalDecision.ToDiagnosticString()}");
                    continue;
                }

                if (TryBuildMagazineTopOffMove(
                        inventory,
                        topOffCandidate,
                        out move,
                        out string topOffReason))
                {
                    return true;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                    $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(target.Magazine)} " +
                    $"ammo={DescribeLooseAmmo(ammo)} result=skip reason={topOffReason} " +
                    tacticalDecision.ToDiagnosticString());
            }

            return false;
        }

        private List<MagazineTopOffTarget> BuildMagazineTopOffTargets(
            Weapon weapon,
            MagazineItemClass insertedMagazine,
            OperationalMagazinePlan refillPlan)
        {
            List<MagazineTopOffTarget> targets = new List<MagazineTopOffTarget>();
            HashSet<string> targetIds = new HashSet<string>(StringComparer.Ordinal);

            AddMagazineTopOffTarget(targets, targetIds, weapon, insertedMagazine, isInsertedMagazine: true);
            // Do not merge found rounds into the follower's pre-raid magazines. Keeping every
            // top-off target in the acquired weapon/source package preserves return bookkeeping in
            // Simple and Restricted modes; existing fast-access mags still count for readiness.
            foreach (BodyGearCandidate candidate in refillPlan.FollowUps.Where(IsOperationalFastAccessFollowUp))
            {
                AddMagazineTopOffTarget(
                    targets,
                    targetIds,
                    weapon,
                    candidate.Item as MagazineItemClass,
                    isInsertedMagazine: false);
            }

            // Complete the fullest useful magazines first. This produces deterministic full-mag
            // states for the existing readiness model and is especially important for five-round
            // magazines, whose partial spares do not combine into a full-mag equivalent.
            return targets
                .OrderByDescending(target =>
                    target.Magazine.MaxCount > 0
                        ? (float)target.Magazine.Count / target.Magazine.MaxCount
                        : 0f)
                .ThenByDescending(target => target.Magazine.Count)
                .ThenByDescending(target => target.IsInsertedMagazine)
                .ThenBy(target => target.Magazine.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static void AddMagazineTopOffTarget(
            ICollection<MagazineTopOffTarget> targets,
            ISet<string> targetIds,
            Weapon weapon,
            MagazineItemClass magazine,
            bool isInsertedMagazine)
        {
            if (magazine == null ||
                string.IsNullOrEmpty(magazine.Id) ||
                !targetIds.Add(magazine.Id) ||
                magazine.MaxCount <= 0 ||
                magazine.Count >= magazine.MaxCount ||
                !IsMagazineCompatibleWithWeapon(weapon, magazine) ||
                (magazine.Count > 0 &&
                 !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine)))
            {
                return;
            }

            targets.Add(new MagazineTopOffTarget(magazine, isInsertedMagazine));
        }

        private static BodyGearCandidate SelectMagazineTopOffAmmo(
            Weapon weapon,
            MagazineItemClass magazine,
            IEnumerable<BodyGearCandidate> candidates,
            IEnumerable<AmmoItemClass> carriedAmmo,
            int reserveTargetRounds,
            out TacticalAmmoDecision selectedDecision)
        {
            selectedDecision = default;
            List<BodyGearCandidate> source = candidates?
                .Where(candidate =>
                    candidate?.Item is AmmoItemClass ammo &&
                    CanTopOffMagazineWithAmmo(weapon, magazine, ammo))
                .ToList() ?? new List<BodyGearCandidate>();
            foreach (BodyGearCandidate candidate in source
                .OrderByDescending(candidate => ((AmmoItemClass)candidate.Item).PenetrationPower)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).Damage)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).ArmorDamage)
                .ThenByDescending(candidate => ((AmmoItemClass)candidate.Item).StackObjectsCount))
            {
                AmmoItemClass ammo = (AmmoItemClass)candidate.Item;
                int availableRounds = source
                    .Where(entry => string.Equals(entry.Item.TemplateId, ammo.TemplateId, StringComparison.Ordinal))
                    .Sum(entry => Math.Max(0, entry.Item.StackObjectsCount));
                TacticalAmmoDecision decision = EvaluateTacticalAmmoCandidate(
                    ammo,
                    carriedAmmo,
                    availableRounds,
                    reserveTargetRounds,
                    allowUpgrade: true);
                if (!decision.ShouldAcquire)
                {
                    Modules.Logger.LogInfo(
                        $"[LootCommand][MagazineTopOff] weapon={DescribeLootDebugItem(weapon)} " +
                        $"ammo={DescribeLooseAmmo(ammo)} result=skipTacticalPolicy " +
                        decision.ToDiagnosticString());
                    continue;
                }

                selectedDecision = decision;
                return candidate;
            }

            return null;
        }

        private static int ResolveMagazineTopOffReserveTargetRounds(
            WeaponPrimaryReadinessSnapshot projected,
            IEnumerable<MagazineTopOffTarget> targets)
        {
            int largestAvailableCapacity = targets?
                .Select(target => Math.Max(0, target?.Magazine?.MaxCount ?? 0))
                .DefaultIfEmpty(0)
                .Max() ?? 0;
            int capacityThreshold = Math.Min(30, largestAvailableCapacity) * 2;
            return Math.Max(projected?.Threshold ?? 0, capacityThreshold);
        }

        private static bool CanTopOffMagazineWithAmmo(
            Weapon weapon,
            MagazineItemClass magazine,
            AmmoItemClass ammo)
        {
            if (weapon == null ||
                magazine == null ||
                ammo == null ||
                magazine.Count >= magazine.MaxCount ||
                !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo) ||
                (magazine.Count > 0 &&
                 !FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, magazine)))
            {
                return false;
            }

            try
            {
                return magazine.CheckCompatibility(ammo) && ammo.CheckAction(null).Succeeded;
            }
            catch
            {
                return false;
            }
        }

        private bool TryBuildInsertedMagazineTopOffChain(
            InventoryController inventory,
            Item sourceRoot,
            Weapon weapon,
            MagazineItemClass magazine,
            BodyGearCandidate topOffCandidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "stagingSpaceUnavailable";
            if (!TryFindSourceMagazineStagingAddress(sourceRoot, magazine, out ItemAddress? stagingAddress))
            {
                return false;
            }

            GStruct154<GClass3411> detachResult = InteractionsHandlerClass.Move(
                magazine,
                stagingAddress,
                inventory,
                true);
            if (detachResult.Failed || detachResult.Value == null)
            {
                reason = $"detachRejected:{DescribeInventoryError(detachResult.Error)}";
                return false;
            }

            BodyGearCandidate restoreCandidate = new BodyGearCandidate(
                magazine,
                null,
                $"{topOffCandidate.SourceName}.MagazineTopOffRestore",
                0,
                bypassPriceThreshold: true,
                bypassCategoryFilter: true,
                bypassBodyGearLootability: true,
                reportAsLootNothing: true,
                followUpDestination: BodyGearFollowUpDestination.RestoreMagazineToWeapon,
                ammoSalvageWeapon: weapon,
                ammoSalvageMagazine: magazine);
            move = new BodyGearMove(
                magazine,
                detachResult.Value,
                $"{topOffCandidate.SourceName}.MagazineTopOffDetach",
                reportAsLootNothing: true,
                followUpCandidates: new[] { topOffCandidate, restoreCandidate },
                storeAsLoot: false,
                successPhrase: EPhraseTrigger.LootGeneric,
                continueFollowUpsOnFailure: true,
                isStagingOperation: true,
                stagingWeapon: weapon,
                stagingMagazine: magazine,
                stagingMagazineRoundsBefore: magazine.Count);
            reason = "ok";
            Modules.Logger.LogInfo(
                $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(magazine)} " +
                $"stage=detach destination={DescribeLootAddress(stagingAddress)}");
            return true;
        }

        private bool TryBuildMagazineTopOffMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidContext";
            if (candidate?.Item is not AmmoItemClass ammo ||
                candidate.AmmoSalvageWeapon is not Weapon weapon ||
                candidate.AmmoSalvageMagazine is not MagazineItemClass magazine ||
                candidate.AmmoSalvageTransferCount <= 0 ||
                !CanTopOffMagazineWithAmmo(weapon, magazine, ammo))
            {
                return false;
            }

            if (IsMagazineInstalledInWeapon(magazine))
            {
                reason = "targetStillInstalled";
                return false;
            }

            int transferCount = Math.Min(
                candidate.AmmoSalvageTransferCount,
                Math.Min(magazine.MaxCount - magazine.Count, ammo.StackObjectsCount));
            if (transferCount <= 0)
            {
                reason = "nothingToTransfer";
                return false;
            }

            GStruct153 applyResult = magazine.Apply(inventory, ammo, transferCount, true);
            if (applyResult.Failed || applyResult.Value == null)
            {
                reason = $"applyRejected:{DescribeInventoryError(applyResult.Error)}";
                return false;
            }

            int roundsBefore = magazine.Count;
            move = new BodyGearMove(
                ammo,
                applyResult.Value,
                $"{candidate.SourceName}.MagazineTopOff",
                reportAsLootNothing: candidate.ReportAsLootNothing,
                storeAsLoot: false,
                successPhrase: EPhraseTrigger.LootGeneric,
                isStagingOperation: true,
                stagingWeapon: weapon,
                stagingMagazine: magazine,
                stagingMagazineRoundsBefore: roundsBefore);
            reason = "ok";
            Modules.Logger.LogInfo(
                $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(magazine)} " +
                $"ammo={DescribeLooseAmmo(ammo)} transfer={transferCount} roundsBefore={roundsBefore} " +
                $"roundsAfterProjected={roundsBefore + transferCount}");
            return true;
        }

        private bool TryBuildRestoreMagazineToWeaponMove(
            InventoryController inventory,
            BodyGearCandidate candidate,
            out BodyGearMove? move,
            out string reason)
        {
            move = null;
            reason = "invalidContext";
            if (candidate?.Item is not MagazineItemClass magazine ||
                candidate.AmmoSalvageWeapon is not Weapon weapon ||
                candidate.AmmoSalvageMagazine is not MagazineItemClass targetMagazine ||
                !IsSameLootItem(magazine, targetMagazine))
            {
                return false;
            }

            if (IsItemInsideRoot(magazine, weapon))
            {
                reason = "alreadyRestored";
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null || magazineSlot.ContainedItem != null)
            {
                reason = magazineSlot == null ? "magazineSlotMissing" : "magazineSlotOccupied";
                return false;
            }

            GStruct154<GClass3411> restoreResult = InteractionsHandlerClass.Move(
                magazine,
                magazineSlot.CreateItemAddress(),
                inventory,
                true);
            if (restoreResult.Failed || restoreResult.Value == null)
            {
                reason = $"restoreRejected:{DescribeInventoryError(restoreResult.Error)}";
                return false;
            }

            move = new BodyGearMove(
                magazine,
                restoreResult.Value,
                candidate.SourceName,
                reportAsLootNothing: true,
                storeAsLoot: false,
                successPhrase: EPhraseTrigger.LootGeneric,
                isStagingOperation: true,
                stagingWeapon: weapon,
                stagingMagazine: magazine,
                stagingMagazineRoundsBefore: magazine.Count,
                terminalOnStagingFailure: false);
            reason = "ok";
            Modules.Logger.LogInfo(
                $"[LootCommand][MagazineTopOff] follower='{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}' " +
                $"weapon={DescribeLootDebugItem(weapon)} target={DescribeLootDebugItem(magazine)} stage=restore");
            return true;
        }

        private static bool TryFindSourceMagazineStagingAddress(
            Item sourceRoot,
            MagazineItemClass magazine,
            out ItemAddress? address)
        {
            address = null;
            if (sourceRoot == null || magazine == null)
            {
                return false;
            }

            HashSet<EFT.InventoryLogic.IContainer> seen = new HashSet<EFT.InventoryLogic.IContainer>();
            IEnumerable<SearchableItemItemClass> searchableItems =
                (sourceRoot is SearchableItemItemClass searchableRoot
                    ? new[] { searchableRoot }
                    : Array.Empty<SearchableItemItemClass>())
                .Concat(SnapshotLootTreeItems(sourceRoot).OfType<SearchableItemItemClass>());
            foreach (SearchableItemItemClass searchable in searchableItems)
            {
                foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
                {
                    if (container == null ||
                        !seen.Add(container) ||
                        !container.TryFindLocationForItem(magazine, out ItemAddress candidateAddress) ||
                        object.Equals(magazine.Parent, candidateAddress))
                    {
                        continue;
                    }

                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private sealed class MagazineTopOffTarget
        {
            public MagazineTopOffTarget(MagazineItemClass magazine, bool isInsertedMagazine)
            {
                Magazine = magazine;
                IsInsertedMagazine = isInsertedMagazine;
            }

            public MagazineItemClass Magazine { get; }
            public bool IsInsertedMagazine { get; }
        }
    }
}
