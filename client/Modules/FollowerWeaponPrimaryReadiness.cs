using Comfort.Common;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponPrimaryReadiness
    {
        private const int MaximumOrdinaryMagazineReference = 30;
        private static readonly object ReferenceCacheLock = new object();
        private static readonly Dictionary<string, OrdinaryReferenceResolution> ReferenceCache =
            new Dictionary<string, OrdinaryReferenceResolution>(StringComparer.Ordinal);

        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon)
        {
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
            MagazineItemClass insertedMagazine;
            try
            {
                insertedMagazine = weapon?.GetCurrentMagazine();
            }
            catch
            {
                return false;
            }

            if (insertedMagazine == null)
            {
                return true;
            }

            // A usable primary must be able to eject its current magazine before loading a spare.
            // Vanilla reload treats tactical vest and pockets as fast access, so either may supply
            // the reserved landing space.
            return CanFitClonedItem(
                       equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem,
                       insertedMagazine) ||
                   CanFitClonedItem(
                       equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem,
                       insertedMagazine);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateFormula(
            int ordinaryReference,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity,
            IEnumerable<int> compatibleFastAccessRounds,
            int supportedMagazineTemplateCount = 0,
            string referenceReason = "provided")
        {
            int reference = Math.Max(0, ordinaryReference);
            int threshold = reference > 0 ? reference * 2 : 0;
            int normalizedInsertedRounds = hasInsertedMagazine ? Math.Max(0, insertedRounds) : 0;
            int normalizedInsertedCapacity = hasInsertedMagazine ? Math.Max(0, insertedCapacity) : 0;
            int insertedContribution = CalculateInsertedContribution(
                reference,
                hasInsertedMagazine,
                normalizedInsertedRounds,
                normalizedInsertedCapacity);

            List<int> spareRounds = compatibleFastAccessRounds?
                .Where(rounds => rounds > 0)
                .Select(rounds => Math.Max(0, rounds))
                .ToList() ?? new List<int>();
            int spareContribution = spareRounds.Sum();
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
                supportedMagazineTemplateCount,
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
                new ReadinessScenario("WP-15", 30, true, 31, 60, new[] { 29 }, 60, true, false)
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
                pitFireTeam.Log.LogInfo(
                    $"[LootCommand][Readiness] Deterministic formula self-test passed ({scenarios.Length}/{scenarios.Length}).");
                return;
            }

            foreach (string failure in failures)
            {
                pitFireTeam.Log.LogError($"[LootCommand][Readiness] Formula self-test failed: {failure}");
            }
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

            OrdinaryReferenceResolution reference = ResolveOrdinaryReference(weapon, magazineSlot);
            List<int> compatibleRounds = new List<int>();
            HashSet<string> includedMagazineIds = new HashSet<string>(StringComparer.Ordinal);

            if (inventory != null && magazineSlot != null)
            {
                try
                {
                    List<MagazineItemClass> reachableMagazines = new List<MagazineItemClass>();
                    inventory.GetReachableItemsOfTypeNonAlloc<MagazineItemClass>(reachableMagazines, null);
                    foreach (MagazineItemClass magazine in reachableMagazines.ToArray())
                    {
                        TryAddCompatibleFastAccessMagazine(
                            magazineSlot,
                            insertedMagazine,
                            magazine,
                            includedMagazineIds,
                            compatibleRounds);
                    }
                }
                catch (Exception ex)
                {
                    reference = reference.WithReason($"{reference.Reason};fastAccessScan:{ex.Message}");
                }
            }

            if (projectedFastAccessMagazines != null && magazineSlot != null)
            {
                foreach (MagazineItemClass magazine in projectedFastAccessMagazines.ToArray())
                {
                    TryAddCompatibleFastAccessMagazine(
                        magazineSlot,
                        insertedMagazine,
                        magazine,
                        includedMagazineIds,
                        compatibleRounds,
                        excludeInstalledMagazines: false);
                }
            }

            return EvaluateFormula(
                reference.OrdinaryReference,
                insertedMagazine != null,
                insertedMagazine?.Count ?? 0,
                insertedMagazine?.MaxCount ?? 0,
                compatibleRounds,
                reference.SupportedMagazineTemplateCount,
                reference.Reason);
        }

        private static int CalculateInsertedContribution(
            int ordinaryReference,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity)
        {
            if (!hasInsertedMagazine || insertedRounds <= 0)
            {
                return 0;
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
            Slot magazineSlot,
            MagazineItemClass insertedMagazine,
            MagazineItemClass candidate,
            HashSet<string> includedMagazineIds,
            List<int> compatibleRounds,
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
                if (magazineSlot.CanAccept(candidate))
                {
                    compatibleRounds.Add(candidate.Count);
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

        private static OrdinaryReferenceResolution ResolveOrdinaryReference(Weapon weapon, Slot magazineSlot)
        {
            if (magazineSlot == null)
            {
                return new OrdinaryReferenceResolution(0, 0, "magazineSlotMissing");
            }

            string cacheKey = BuildMagazineSlotCacheKey(weapon, magazineSlot);
            lock (ReferenceCacheLock)
            {
                if (ReferenceCache.TryGetValue(cacheKey, out OrdinaryReferenceResolution cached))
                {
                    return cached.WithReason("cache");
                }
            }

            int maximumReference = 0;
            int supportedTemplateCount = 0;
            string reason = "templates";
            try
            {
                ItemFactoryClass itemFactory = Singleton<ItemFactoryClass>.Instance;
                if (itemFactory?.ItemTemplates == null)
                {
                    return new OrdinaryReferenceResolution(0, 0, "itemTemplatesUnavailable");
                }

                foreach (var templateEntry in itemFactory.ItemTemplates)
                {
                    if (templateEntry.Value is not MagazineTemplateClass)
                    {
                        continue;
                    }

                    try
                    {
                        Item item = itemFactory.CreateItem(
                            $"pft-readiness-{templateEntry.Key}",
                            templateEntry.Key.ToString(),
                            null);
                        if (item is not MagazineItemClass magazine || !magazineSlot.CanAccept(magazine))
                        {
                            continue;
                        }

                        supportedTemplateCount++;
                        maximumReference = Math.Max(
                            maximumReference,
                            Math.Min(MaximumOrdinaryMagazineReference, Math.Max(0, magazine.MaxCount)));
                    }
                    catch
                    {
                        // Individual malformed or special-use templates must not abort the resolver.
                    }
                }
            }
            catch (Exception ex)
            {
                reason = $"templateScan:{ex.Message}";
            }

            OrdinaryReferenceResolution resolution = new OrdinaryReferenceResolution(
                maximumReference,
                supportedTemplateCount,
                maximumReference > 0 ? reason : $"{reason};noCompatibleMagazineTemplates");
            lock (ReferenceCacheLock)
            {
                ReferenceCache[cacheKey] = resolution;
            }

            return resolution;
        }

        private static string BuildMagazineSlotCacheKey(Weapon weapon, Slot magazineSlot)
        {
            StringBuilder key = new StringBuilder();
            key.Append(weapon?.TemplateId.ToString() ?? "weaponMissing");
            key.Append('|');
            key.Append(magazineSlot?.ParentItem?.TemplateId.ToString() ?? "slotParentMissing");

            if (magazineSlot?.Filters == null)
            {
                return key.ToString();
            }

            foreach (ItemFilter filter in magazineSlot.Filters)
            {
                key.Append("|+");
                if (filter?.Filter != null)
                {
                    foreach (var accepted in filter.Filter)
                    {
                        key.Append(accepted.ToString());
                        key.Append(',');
                    }
                }

                key.Append("|-");
                if (filter?.ExcludedFilter != null)
                {
                    foreach (var excluded in filter.ExcludedFilter)
                    {
                        key.Append(excluded.ToString());
                        key.Append(',');
                    }
                }
            }

            return key.ToString();
        }

        private readonly struct OrdinaryReferenceResolution
        {
            public OrdinaryReferenceResolution(int ordinaryReference, int supportedMagazineTemplateCount, string reason)
            {
                OrdinaryReference = ordinaryReference;
                SupportedMagazineTemplateCount = supportedMagazineTemplateCount;
                Reason = reason ?? "unknown";
            }

            public int OrdinaryReference { get; }
            public int SupportedMagazineTemplateCount { get; }
            public string Reason { get; }

            public OrdinaryReferenceResolution WithReason(string reason)
            {
                return new OrdinaryReferenceResolution(
                    OrdinaryReference,
                    SupportedMagazineTemplateCount,
                    reason);
            }
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
            int supportedMagazineTemplateCount,
            string referenceReason)
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
            SupportedMagazineTemplateCount = supportedMagazineTemplateCount;
            ReferenceReason = referenceReason;
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
        public int SupportedMagazineTemplateCount { get; }
        public string ReferenceReason { get; }

        public string ToDiagnosticString()
        {
            string inserted = HasInsertedMagazine ? $"{InsertedRounds}/{InsertedCapacity}" : "none";
            return $"reference={OrdinaryReference} threshold={Threshold} inserted={inserted} " +
                   $"insertedContribution={InsertedContribution} fastAccessMags={FastAccessMagazineRounds.Count} " +
                   $"fastAccessRounds={FastAccessContribution} total={TotalContribution} " +
                   $"primaryReady={PrimaryReady} requiresMagazineLoad={RequiresMagazineLoad} reason={Reason} " +
                   $"supportedTemplates={SupportedMagazineTemplateCount} referenceSource={ReferenceReason}";
        }
    }
}
