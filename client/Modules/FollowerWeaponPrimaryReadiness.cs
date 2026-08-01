using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponPrimaryReadiness
    {
        private const int MaximumOrdinaryMagazineReference = 30;
        private const int TinyMagazineReference = 5;

        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon,
            Func<AmmoItemClass, bool>? internalAmmoEligibility = null)
        {
            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return FollowerWeaponLooseFeedReadiness.EvaluateActual(
                    inventory,
                    weapon,
                    internalAmmoEligibility);
            }

            return EvaluateInventoryState(inventory, weapon, null);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluatePlannedProjection(
            InventoryController inventory,
            Weapon weapon,
            IEnumerable<MagazineItemClass> projectedFastAccessMagazines)
        {
            return EvaluateInventoryState(inventory, weapon, projectedFastAccessMagazines);
        }

        internal static bool HasInsertedMagazineReloadLandingSpace(
            InventoryEquipment equipment,
            Weapon weapon)
        {
            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                // Internal and chamber feeds never eject a detachable magazine into fast access.
                return true;
            }

            MagazineItemClass insertedMagazine;
            try
            {
                insertedMagazine = weapon?.GetCurrentMagazine();
            }
            catch
            {
                return false;
            }

            return insertedMagazine == null || HasMagazineReloadLandingSpace(equipment, insertedMagazine);
        }

        internal static bool HasMagazineReloadLandingSpace(
            InventoryEquipment equipment,
            MagazineItemClass magazine)
        {
            if (magazine == null)
            {
                return true;
            }

            // A usable primary must be able to eject its current magazine before loading a spare.
            // Vanilla reload treats tactical vest and pockets as fast access, so either may supply
            // the reserved landing space.
            return CanFitClonedItem(
                       equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem,
                       magazine) ||
                   CanFitClonedItem(
                       equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem,
                       magazine);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateFormula(
            int ordinaryReference,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity,
            IEnumerable<int> compatibleFastAccessRounds,
            int availableMagazineCount = 0,
            string referenceReason = "provided")
        {
            int reference = Math.Max(0, ordinaryReference);
            int normalizedInsertedRounds = hasInsertedMagazine ? Math.Max(0, insertedRounds) : 0;
            int normalizedInsertedCapacity = hasInsertedMagazine ? Math.Max(0, insertedCapacity) : 0;
            bool usesTinyMagazineReserve = reference == TinyMagazineReference;
            int threshold = reference > 0 ? reference * 2 : 0;
            int insertedContribution = CalculateInsertedContribution(
                reference,
                hasInsertedMagazine,
                normalizedInsertedRounds,
                normalizedInsertedCapacity,
                usesTinyMagazineReserve);

            List<int> spareRounds = compatibleFastAccessRounds?
                .Where(rounds => rounds > 0)
                .Select(rounds => Math.Max(0, rounds))
                .ToList() ?? new List<int>();
            int spareContribution = usesTinyMagazineReserve
                ? spareRounds.Count(rounds => rounds >= reference) * reference
                : spareRounds.Sum();
            int totalContribution = insertedContribution + spareContribution;
            bool primaryReady = reference > 0 && totalContribution >= threshold;
            bool requiresMagazineLoad = primaryReady && !hasInsertedMagazine;

            string reason;
            if (reference <= 0)
            {
                reason = "ordinaryReferenceUnavailable";
                primaryReady = false;
                requiresMagazineLoad = false;
            }
            else if (primaryReady && requiresMagazineLoad)
            {
                reason = "readyRequiresMagazineLoad";
            }
            else if (primaryReady)
            {
                reason = "ready";
            }
            else if (!hasInsertedMagazine && spareRounds.Count == 0)
            {
                reason = "noInsertedMagazineOrFastAccessSpare";
            }
            else if (usesTinyMagazineReserve)
            {
                reason = "tinyMagazineNeedsTwoFullEquivalents";
            }
            else
            {
                reason = "insufficientUsableRounds";
            }

            return new WeaponPrimaryReadinessSnapshot(
                reference,
                threshold,
                hasInsertedMagazine,
                normalizedInsertedRounds,
                normalizedInsertedCapacity,
                insertedContribution,
                spareRounds,
                spareContribution,
                totalContribution,
                primaryReady,
                requiresMagazineLoad,
                reason,
                availableMagazineCount,
                referenceReason);
        }

        internal static void RunDeterministicSelfTests()
        {
            ReadinessScenario[] scenarios =
            {
                new ReadinessScenario("WP-01", 30, true, 60, 60, Array.Empty<int>(), 60, true, false),
                new ReadinessScenario("WP-02", 30, true, 45, 45, Array.Empty<int>(), 45, false, false),
                new ReadinessScenario("WP-03", 30, true, 30, 30, new[] { 30 }, 60, true, false),
                new ReadinessScenario("WP-04", 30, true, 20, 30, new[] { 30 }, 60, true, false),
                new ReadinessScenario("WP-05", 30, true, 15, 30, new[] { 30 }, 60, true, false),
                new ReadinessScenario("WP-06", 30, true, 14, 30, new[] { 30 }, 44, false, false),
                new ReadinessScenario("WP-07", 30, true, 14, 30, new[] { 30, 16 }, 60, true, false),
                new ReadinessScenario("WP-08", 30, false, 0, 0, new[] { 30, 30 }, 60, true, true),
                new ReadinessScenario("WP-09", 30, false, 0, 0, new[] { 20, 20, 20 }, 60, true, true),
                new ReadinessScenario("WP-10", 30, true, 30, 30, Array.Empty<int>(), 30, false, false),
                new ReadinessScenario("WP-11", 30, true, 14, 30, new[] { 30, 16 }, 60, true, false),
                new ReadinessScenario("WP-12", 24, true, 12, 24, new[] { 24 }, 48, true, false),
                new ReadinessScenario("WP-13", 24, true, 11, 24, new[] { 24 }, 35, false, false),
                new ReadinessScenario("WP-14", 30, true, 30, 60, Array.Empty<int>(), 30, false, false),
                new ReadinessScenario("WP-15", 30, true, 31, 60, new[] { 29 }, 60, true, false),
                new ReadinessScenario("WP-16", 5, true, 2, 5, new[] { 5, 5 }, 10, true, false),
                new ReadinessScenario("WP-17", 5, true, 3, 5, new[] { 5, 5 }, 10, true, false),
                new ReadinessScenario("WP-18", 5, true, 4, 5, new[] { 5 }, 10, true, false),
                new ReadinessScenario("WP-19", 5, true, 5, 5, new[] { 5 }, 10, true, false),
                new ReadinessScenario("WP-20", 5, true, 5, 5, new[] { 4 }, 5, false, false),
                new ReadinessScenario("WP-21", 5, true, 3, 5, new[] { 5 }, 5, false, false)
            };

            List<string> failures = new List<string>();
            foreach (ReadinessScenario scenario in scenarios)
            {
                WeaponPrimaryReadinessSnapshot result = EvaluateFormula(
                    scenario.OrdinaryReference,
                    scenario.HasInsertedMagazine,
                    scenario.InsertedRounds,
                    scenario.InsertedCapacity,
                    scenario.FastAccessRounds);

                if (result.TotalContribution != scenario.ExpectedContribution ||
                    result.PrimaryReady != scenario.ExpectedReady ||
                    result.RequiresMagazineLoad != scenario.ExpectedRequiresLoad)
                {
                    failures.Add(
                        $"{scenario.Id}: expected total={scenario.ExpectedContribution} ready={scenario.ExpectedReady} " +
                        $"load={scenario.ExpectedRequiresLoad}; actual {result.ToDiagnosticString()}");
                }
            }

            if (failures.Count == 0)
            {
                Logger.LogInfo(
                    $"[LootCommand][Readiness] Deterministic formula self-test passed ({scenarios.Length}/{scenarios.Length}).");
                FollowerWeaponInternalReadiness.RunDeterministicSelfTests();
                FollowerWeaponChamberReadiness.RunDeterministicSelfTests();
                FollowerTacticalAmmoPolicy.RunDeterministicSelfTests();
                return;
            }

            foreach (string failure in failures)
            {
                pitFireTeam.Log.LogError($"[LootCommand][Readiness] Formula self-test failed: {failure}");
            }

            FollowerWeaponInternalReadiness.RunDeterministicSelfTests();
            FollowerWeaponChamberReadiness.RunDeterministicSelfTests();
            FollowerTacticalAmmoPolicy.RunDeterministicSelfTests();
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateInventoryState(
            InventoryController inventory,
            Weapon weapon,
            IEnumerable<MagazineItemClass>? projectedFastAccessMagazines)
        {
            if (weapon == null)
            {
                return EvaluateFormula(0, false, 0, 0, Array.Empty<int>(), 0, "weaponMissing");
            }

            Slot magazineSlot;
            MagazineItemClass insertedMagazine;
            try
            {
                magazineSlot = weapon.GetMagazineSlot();
                insertedMagazine = weapon.GetCurrentMagazine();
            }
            catch (Exception ex)
            {
                return EvaluateFormula(0, false, 0, 0, Array.Empty<int>(), 0, $"weaponRead:{ex.Message}");
            }

            List<MagazineItemClass> compatibleMagazines = new List<MagazineItemClass>();
            HashSet<string> includedMagazineIds = new HashSet<string>(StringComparer.Ordinal);
            string referenceReason = "availableLoadedMagazines";

            if (inventory != null && magazineSlot != null)
            {
                try
                {
                    List<MagazineItemClass> reachableMagazines = new List<MagazineItemClass>();
                    inventory.GetReachableItemsOfTypeNonAlloc<MagazineItemClass>(reachableMagazines, null);
                    foreach (MagazineItemClass magazine in reachableMagazines.ToArray())
                    {
                        TryAddCompatibleFastAccessMagazine(
                            weapon,
                            magazineSlot,
                            insertedMagazine,
                            magazine,
                            includedMagazineIds,
                            compatibleMagazines);
                    }
                }
                catch (Exception ex)
                {
                    referenceReason = $"{referenceReason};fastAccessScan:{ex.Message}";
                }
            }

            if (projectedFastAccessMagazines != null && magazineSlot != null)
            {
                foreach (MagazineItemClass magazine in projectedFastAccessMagazines.ToArray())
                {
                    TryAddCompatibleFastAccessMagazine(
                        weapon,
                        magazineSlot,
                        insertedMagazine,
                        magazine,
                        includedMagazineIds,
                        compatibleMagazines,
                        excludeInstalledMagazines: false);
                }
            }

            bool insertedAmmoCompatible = insertedMagazine == null ||
                                          FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(
                                              weapon,
                                              insertedMagazine);
            int availableReference = ResolveAvailableOrdinaryReference(
                insertedAmmoCompatible ? insertedMagazine : null,
                compatibleMagazines,
                out int availableMagazineCount);
            if (availableReference <= 0)
            {
                referenceReason = $"{referenceReason};noneAvailable";
            }

            WeaponPrimaryReadinessSnapshot result = EvaluateFormula(
                availableReference,
                insertedMagazine != null,
                insertedAmmoCompatible ? insertedMagazine?.Count ?? 0 : 0,
                insertedMagazine?.MaxCount ?? 0,
                compatibleMagazines.Select(magazine => magazine.Count),
                availableMagazineCount,
                referenceReason);

            if (insertedAmmoCompatible)
            {
                return result;
            }

            // Compatible spares cannot make a weapon usable while a wrong-caliber magazine is
            // still installed. A later explicit magazine-swap phase may resolve that state.
            return new WeaponPrimaryReadinessSnapshot(
                result.OrdinaryReference,
                result.Threshold,
                hasInsertedMagazine: true,
                insertedRounds: 0,
                insertedCapacity: result.InsertedCapacity,
                insertedContribution: 0,
                result.FastAccessMagazineRounds,
                result.FastAccessContribution,
                totalContribution: result.FastAccessContribution,
                primaryReady: false,
                requiresMagazineLoad: false,
                reason: "insertedAmmoIncompatible",
                result.AvailableMagazineCount,
                referenceReason: $"{result.ReferenceReason};insertedRounds={insertedMagazine?.Count ?? 0}");
        }

        private static int CalculateInsertedContribution(
            int ordinaryReference,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity,
            bool usesTinyMagazineReserve)
        {
            if (!hasInsertedMagazine || insertedRounds <= 0)
            {
                return 0;
            }

            // Tiny magazines are counted as whole magazine states. A nearly full inserted
            // magazine (4/5 or 5/5) is one full equivalent; lower states require two full
            // spares instead of combining partial rounds into an artificial full magazine.
            if (usesTinyMagazineReserve)
            {
                return insertedRounds >= 4 ? ordinaryReference : 0;
            }

            bool atLeastHalfFull = insertedCapacity > 0 && insertedRounds * 2 >= insertedCapacity;
            return atLeastHalfFull
                ? Math.Max(ordinaryReference, insertedRounds)
                : insertedRounds;
        }

        private static bool CanFitClonedItem(Item containerItem, Item item)
        {
            try
            {
                SearchableItemItemClass containerClone = containerItem?.CloneItem() as SearchableItemItemClass;
                if (containerClone?.Grids == null || item == null)
                {
                    return false;
                }

                containerClone.CurrentAddress = null;
                foreach (StashGridClass grid in containerClone.Grids)
                {
                    Item itemClone = item.CloneItem();
                    if (itemClone == null)
                    {
                        continue;
                    }

                    itemClone.CurrentAddress = null;
                    if (grid?.AddAnywhere(itemClone, EErrorHandlingType.Ignore).Succeeded == true)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // If fit cannot be proven, do not promote the weapon into an unusable primary.
            }

            return false;
        }

        private static void TryAddCompatibleFastAccessMagazine(
            Weapon weapon,
            Slot magazineSlot,
            MagazineItemClass insertedMagazine,
            MagazineItemClass candidate,
            HashSet<string> includedMagazineIds,
            List<MagazineItemClass> compatibleMagazines,
            bool excludeInstalledMagazines = true)
        {
            if (candidate == null ||
                candidate.Count <= 0 ||
                string.IsNullOrEmpty(candidate.Id) ||
                string.Equals(candidate.Id, insertedMagazine?.Id, StringComparison.Ordinal) ||
                !includedMagazineIds.Add(candidate.Id))
            {
                return;
            }

            if (excludeInstalledMagazines && IsInstalledInWeaponTree(candidate))
            {
                return;
            }

            try
            {
                if (magazineSlot.CanAccept(candidate) &&
                    FollowerWeaponMagazineCompatibility.AreLoadedCartridgesCompatible(weapon, candidate))
                {
                    compatibleMagazines.Add(candidate);
                }
            }
            catch
            {
                // A candidate that cannot be proven compatible contributes nothing.
            }
        }

        private static bool IsInstalledInWeaponTree(Item item)
        {
            try
            {
                foreach (Item parent in item.GetAllParentItems(false))
                {
                    if (parent is Weapon)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static int ResolveAvailableOrdinaryReference(
            MagazineItemClass insertedMagazine,
            IEnumerable<MagazineItemClass> compatibleMagazines,
            out int availableMagazineCount)
        {
            int largestAvailableCapacity = 0;
            availableMagazineCount = 0;

            if (insertedMagazine?.Count > 0)
            {
                largestAvailableCapacity = Math.Max(0, insertedMagazine.MaxCount);
                availableMagazineCount++;
            }

            if (compatibleMagazines != null)
            {
                foreach (MagazineItemClass magazine in compatibleMagazines)
                {
                    if (magazine?.Count <= 0)
                    {
                        continue;
                    }

                    largestAvailableCapacity = Math.Max(
                        largestAvailableCapacity,
                        Math.Max(0, magazine.MaxCount));
                    availableMagazineCount++;
                }
            }

            return Math.Min(MaximumOrdinaryMagazineReference, largestAvailableCapacity);
        }

        private readonly struct ReadinessScenario
        {
            public ReadinessScenario(
                string id,
                int ordinaryReference,
                bool hasInsertedMagazine,
                int insertedRounds,
                int insertedCapacity,
                int[] fastAccessRounds,
                int expectedContribution,
                bool expectedReady,
                bool expectedRequiresLoad)
            {
                Id = id;
                OrdinaryReference = ordinaryReference;
                HasInsertedMagazine = hasInsertedMagazine;
                InsertedRounds = insertedRounds;
                InsertedCapacity = insertedCapacity;
                FastAccessRounds = fastAccessRounds;
                ExpectedContribution = expectedContribution;
                ExpectedReady = expectedReady;
                ExpectedRequiresLoad = expectedRequiresLoad;
            }

            public string Id { get; }
            public int OrdinaryReference { get; }
            public bool HasInsertedMagazine { get; }
            public int InsertedRounds { get; }
            public int InsertedCapacity { get; }
            public int[] FastAccessRounds { get; }
            public int ExpectedContribution { get; }
            public bool ExpectedReady { get; }
            public bool ExpectedRequiresLoad { get; }
        }
    }

    internal sealed class WeaponPrimaryReadinessSnapshot
    {
        public WeaponPrimaryReadinessSnapshot(
            int ordinaryReference,
            int threshold,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity,
            int insertedContribution,
            IReadOnlyList<int> fastAccessMagazineRounds,
            int fastAccessContribution,
            int totalContribution,
            bool primaryReady,
            bool requiresMagazineLoad,
            string reason,
            int availableMagazineCount,
            string referenceReason,
            string feedKind = "detachableMagazine")
        {
            OrdinaryReference = ordinaryReference;
            Threshold = threshold;
            HasInsertedMagazine = hasInsertedMagazine;
            InsertedRounds = insertedRounds;
            InsertedCapacity = insertedCapacity;
            InsertedContribution = insertedContribution;
            FastAccessMagazineRounds = fastAccessMagazineRounds;
            FastAccessContribution = fastAccessContribution;
            TotalContribution = totalContribution;
            PrimaryReady = primaryReady;
            RequiresMagazineLoad = requiresMagazineLoad;
            Reason = reason;
            AvailableMagazineCount = availableMagazineCount;
            ReferenceReason = referenceReason;
            FeedKind = feedKind;
        }

        public int OrdinaryReference { get; }
        public int Threshold { get; }
        public bool HasInsertedMagazine { get; }
        public int InsertedRounds { get; }
        public int InsertedCapacity { get; }
        public int InsertedContribution { get; }
        public IReadOnlyList<int> FastAccessMagazineRounds { get; }
        public int FastAccessContribution { get; }
        public int TotalContribution { get; }
        public bool PrimaryReady { get; }
        public bool RequiresMagazineLoad { get; }
        public string Reason { get; }
        public int AvailableMagazineCount { get; }
        public string ReferenceReason { get; }
        public string FeedKind { get; }

        public string ToDiagnosticString()
        {
            if (string.Equals(FeedKind, "internalMagazine", StringComparison.Ordinal) ||
                string.Equals(FeedKind, "chamberFed", StringComparison.Ordinal))
            {
                return $"feed={FeedKind} capacity={OrdinaryReference} threshold={Threshold} " +
                       $"loaded={InsertedRounds} reserveStacks={FastAccessMagazineRounds.Count} " +
                       $"reserveRounds={FastAccessContribution} total={TotalContribution} " +
                       $"primaryReady={PrimaryReady} requiresFeedLoad={RequiresMagazineLoad} " +
                       $"reason={Reason} referenceSource={ReferenceReason}";
            }

            string inserted = HasInsertedMagazine ? $"{InsertedRounds}/{InsertedCapacity}" : "none";
            return $"feed={FeedKind} reference={OrdinaryReference} threshold={Threshold} inserted={inserted} " +
                   $"insertedContribution={InsertedContribution} fastAccessMags={FastAccessMagazineRounds.Count} " +
                   $"fastAccessRounds={FastAccessContribution} total={TotalContribution} " +
                   $"primaryReady={PrimaryReady} requiresMagazineLoad={RequiresMagazineLoad} reason={Reason} " +
                   $"availableMags={AvailableMagazineCount} referenceSource={ReferenceReason}";
        }
    }
}
