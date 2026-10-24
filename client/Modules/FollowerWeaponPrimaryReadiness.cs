using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponPrimaryReadiness
    {
        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon,
            Func<EFT.InventoryLogic.Ammo, bool>? internalAmmoEligibility = null,
            Func<EFT.InventoryLogic.Magazine, bool>? fastAccessMagazineEligibility = null)
        {
            if (FollowerWeaponLooseFeedReadiness.IsSupported(weapon))
            {
                return FollowerWeaponLooseFeedReadiness.EvaluateActual(
                    inventory,
                    weapon,
                    internalAmmoEligibility);
            }

            return EvaluateInventoryState(inventory, weapon, null, fastAccessMagazineEligibility);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluatePlannedProjection(
            InventoryController inventory,
            Weapon weapon,
            IEnumerable<EFT.InventoryLogic.Magazine> projectedFastAccessMagazines)
        {
            return EvaluateInventoryState(inventory, weapon, projectedFastAccessMagazines, null);
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

            EFT.InventoryLogic.Magazine insertedMagazine;
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
            EFT.InventoryLogic.Magazine magazine)
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
            string caliber,
            bool hasInsertedMagazine,
            int insertedRounds,
            int insertedCapacity,
            IEnumerable<int> compatibleFastAccessRounds,
            int availableMagazineCount = 0,
            string referenceReason = "provided",
            int chamberRounds = 0)
        {
            int normalizedInsertedRounds = hasInsertedMagazine ? Math.Max(0, insertedRounds) : 0;
            int normalizedInsertedCapacity = hasInsertedMagazine ? Math.Max(0, insertedCapacity) : 0;
            int threshold = FollowerWeaponAmmoReadinessPolicy.GetRequiredRounds(caliber);
            int insertedContribution = normalizedInsertedRounds + Math.Max(0, chamberRounds);

            List<int> spareRounds = compatibleFastAccessRounds?
                .Where(rounds => rounds > 0)
                .Select(rounds => Math.Max(0, rounds))
                .ToList() ?? new List<int>();
            int spareContribution = spareRounds.Sum();
            int totalContribution = insertedContribution + spareContribution;
            bool primaryReady = threshold > 0 && totalContribution >= threshold;
            bool requiresMagazineLoad = primaryReady && !hasInsertedMagazine;

            string reason;
            if (threshold <= 0)
            {
                reason = "unsupportedCaliber";
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
                $"caliber={caliber};{referenceReason}",
                chamberRounds: Math.Max(0, chamberRounds));
        }

        internal static void RunDeterministicSelfTests()
        {
            ReadinessScenario[] scenarios =
            {
                new ReadinessScenario("WP-01", "Caliber556x45NATO", true, 60, 60, Array.Empty<int>(), 60, true, false),
                new ReadinessScenario("WP-02", "Caliber556x45NATO", true, 15, 30, new[] { 30 }, 45, false, false),
                new ReadinessScenario("WP-03", "Caliber556x45NATO", true, 30, 30, new[] { 30 }, 60, true, false),
                new ReadinessScenario("WP-04", "Caliber9x39", true, 8, 20, new[] { 9 }, 17, false, false),
                new ReadinessScenario("WP-05", "Caliber9x39", true, 20, 20, new[] { 20 }, 40, true, false),
                new ReadinessScenario("WP-06", "Caliber9x19PARA", true, 19, 30, new[] { 20, 20 }, 59, false, false),
                new ReadinessScenario("WP-07", "Caliber9x19PARA", true, 19, 30, new[] { 20, 20, 20 }, 79, true, false),
                new ReadinessScenario("WP-08", "Caliber762x51", false, 0, 0, new[] { 15, 15 }, 30, true, true),
                new ReadinessScenario("WP-09", "Caliber762x54R", true, 4, 5, new[] { 5 }, 9, false, false),
                new ReadinessScenario("WP-10", "Caliber86x70", true, 5, 5, new[] { 5, 5, 5 }, 20, true, false),
                new ReadinessScenario("WP-11", "Caliber12g", true, 2, 5, new[] { 5, 5 }, 12, true, false),
                new ReadinessScenario("WP-12", "Caliber366TKM", true, 10, 10, new[] { 10, 10, 10 }, 40, true, false),
                new ReadinessScenario("WP-13", "custom", true, 59, 100, Array.Empty<int>(), 59, false, false),
                new ReadinessScenario("WP-14", "Caliber40x46", true, 60, 60, Array.Empty<int>(), 60, false, false)
            };

            List<string> failures = new List<string>();
            foreach (ReadinessScenario scenario in scenarios)
            {
                WeaponPrimaryReadinessSnapshot result = EvaluateFormula(
                    scenario.Caliber,
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
            IEnumerable<EFT.InventoryLogic.Magazine>? projectedFastAccessMagazines,
            Func<EFT.InventoryLogic.Magazine, bool>? fastAccessMagazineEligibility)
        {
            if (weapon == null)
            {
                return EvaluateFormula(string.Empty, false, 0, 0, Array.Empty<int>(), 0, "weaponMissing");
            }

            Slot magazineSlot;
            EFT.InventoryLogic.Magazine insertedMagazine;
            try
            {
                magazineSlot = weapon.GetMagazineSlot();
                insertedMagazine = weapon.GetCurrentMagazine();
            }
            catch (Exception ex)
            {
                return EvaluateFormula(string.Empty, false, 0, 0, Array.Empty<int>(), 0, $"weaponRead:{ex.Message}");
            }

            List<EFT.InventoryLogic.Magazine> compatibleMagazines = new List<EFT.InventoryLogic.Magazine>();
            HashSet<string> includedMagazineIds = new HashSet<string>(StringComparer.Ordinal);
            string referenceReason = "usableActualRounds";

            if (inventory != null && magazineSlot != null)
            {
                try
                {
                    List<EFT.InventoryLogic.Magazine> reachableMagazines = new List<EFT.InventoryLogic.Magazine>();
                    inventory.GetReachableItemsOfTypeNonAlloc<EFT.InventoryLogic.Magazine>(reachableMagazines, null);
                    foreach (EFT.InventoryLogic.Magazine magazine in reachableMagazines.ToArray())
                    {
                        if (fastAccessMagazineEligibility != null &&
                            !fastAccessMagazineEligibility(magazine))
                        {
                            continue;
                        }

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
                foreach (EFT.InventoryLogic.Magazine magazine in projectedFastAccessMagazines.ToArray())
                {
                    if (fastAccessMagazineEligibility != null &&
                        !fastAccessMagazineEligibility(magazine))
                    {
                        continue;
                    }

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
            int availableMagazineCount = compatibleMagazines.Count +
                                         (insertedAmmoCompatible && insertedMagazine?.Count > 0 ? 1 : 0);
            int chamberRounds = weapon.Chambers?.Count(chamber =>
                chamber?.ContainedItem is EFT.InventoryLogic.Ammo ammo && !ammo.IsUsed &&
                FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, ammo)) ?? 0;

            WeaponPrimaryReadinessSnapshot result = EvaluateFormula(
                weapon.AmmoCaliber,
                insertedMagazine != null,
                insertedAmmoCompatible ? insertedMagazine?.Count ?? 0 : 0,
                insertedMagazine?.MaxCount ?? 0,
                compatibleMagazines.Select(magazine => magazine.Count),
                availableMagazineCount,
                referenceReason,
                chamberRounds);

            if (insertedAmmoCompatible)
            {
                return result;
            }

            // Compatible spares cannot make a weapon usable while a wrong-caliber magazine is
            // still installed. A later explicit magazine-swap phase may resolve that state.
            return new WeaponPrimaryReadinessSnapshot(
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

        private static bool CanFitClonedItem(Item containerItem, Item item)
        {
            try
            {
                EFT.InventoryLogic.SearchableItem containerClone = containerItem?.CloneItem() as EFT.InventoryLogic.SearchableItem;
                if (containerClone?.Grids == null || item == null)
                {
                    return false;
                }

                containerClone.CurrentAddress = null;
                foreach (EFT.InventoryLogic.Grid grid in containerClone.Grids)
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
            EFT.InventoryLogic.Magazine insertedMagazine,
            EFT.InventoryLogic.Magazine candidate,
            HashSet<string> includedMagazineIds,
            List<EFT.InventoryLogic.Magazine> compatibleMagazines,
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

        private readonly struct ReadinessScenario
        {
            public ReadinessScenario(
                string id,
                string caliber,
                bool hasInsertedMagazine,
                int insertedRounds,
                int insertedCapacity,
                int[] fastAccessRounds,
                int expectedContribution,
                bool expectedReady,
                bool expectedRequiresLoad)
            {
                Id = id;
                Caliber = caliber;
                HasInsertedMagazine = hasInsertedMagazine;
                InsertedRounds = insertedRounds;
                InsertedCapacity = insertedCapacity;
                FastAccessRounds = fastAccessRounds;
                ExpectedContribution = expectedContribution;
                ExpectedReady = expectedReady;
                ExpectedRequiresLoad = expectedRequiresLoad;
            }

            public string Id { get; }
            public string Caliber { get; }
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
            string feedKind = "detachableMagazine",
            int chamberRounds = 0)
        {
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
            ChamberRounds = chamberRounds;
        }

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
        public int ChamberRounds { get; }

        public string ToDiagnosticString()
        {
            if (string.Equals(FeedKind, "internalMagazine", StringComparison.Ordinal) ||
                string.Equals(FeedKind, "chamberFed", StringComparison.Ordinal))
            {
                return $"feed={FeedKind} capacity={InsertedCapacity} threshold={Threshold} " +
                       $"loaded={InsertedRounds} reserveStacks={FastAccessMagazineRounds.Count} " +
                       $"reserveRounds={FastAccessContribution} total={TotalContribution} " +
                       $"primaryReady={PrimaryReady} requiresFeedLoad={RequiresMagazineLoad} " +
                       $"reason={Reason} referenceSource={ReferenceReason}";
            }

            string inserted = HasInsertedMagazine ? $"{InsertedRounds}/{InsertedCapacity}" : "none";
            return $"feed={FeedKind} threshold={Threshold} inserted={inserted} chamberRounds={ChamberRounds} " +
                   $"insertedContribution={InsertedContribution} fastAccessMags={FastAccessMagazineRounds.Count} " +
                   $"fastAccessRounds={FastAccessContribution} total={TotalContribution} " +
                   $"primaryReady={PrimaryReady} requiresMagazineLoad={RequiresMagazineLoad} reason={Reason} " +
                   $"availableMags={AvailableMagazineCount} referenceSource={ReferenceReason}";
        }
    }
}
