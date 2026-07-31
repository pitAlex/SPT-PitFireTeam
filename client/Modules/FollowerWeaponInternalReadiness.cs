using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponInternalReadiness
    {
        private static readonly EquipmentSlot[] VanillaLooseAmmoSlots =
        {
            EquipmentSlot.Pockets,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };

        internal static bool IsInternalMagazineWeapon(Weapon weapon)
        {
            try
            {
                return weapon?.ReloadMode == Weapon.EReloadMode.InternalMagazine;
            }
            catch
            {
                return false;
            }
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon,
            Func<AmmoItemClass, bool>? reserveEligibility = null)
        {
            List<AmmoItemClass> reserves = GetCompatibleLooseAmmo(
                    inventory,
                    weapon,
                    reserveEligibility)
                .ToList();
            return EvaluateWeaponState(weapon, reserves.Select(ammo => ammo.StackObjectsCount));
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateProjected(
            Weapon weapon,
            IEnumerable<int> projectedReserveStacks,
            int? loadedRoundsOverride = null)
        {
            return EvaluateWeaponState(weapon, projectedReserveStacks, loadedRoundsOverride);
        }

        internal static bool IsCompatibleLooseAmmo(Weapon weapon, AmmoItemClass ammo)
        {
            if (!IsInternalMagazineWeapon(weapon) ||
                !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
            {
                return false;
            }

            MagazineItemClass internalMagazine;
            try
            {
                internalMagazine = weapon.GetCurrentMagazine();
                if (internalMagazine == null || !internalMagazine.CheckCompatibility(ammo))
                {
                    return false;
                }

                // RevolverItemClass feeds directly from its cylinder. The M32 and similar
                // shoulder-fired revolvers do not expose a separate chamber slot that accepts
                // loose ammunition, so cylinder compatibility is the final feed check.
                if (weapon is RevolverItemClass)
                {
                    return true;
                }

                return weapon.Chambers != null &&
                       weapon.Chambers.Any(chamber => chamber?.CanAccept(ammo) == true);
            }
            catch
            {
                return false;
            }
        }

        internal static int GetLoadedRounds(Weapon weapon)
        {
            if (!TryReadInternalState(weapon, out _, out int magazineRounds, out int chamberRounds))
            {
                return 0;
            }

            return magazineRounds + chamberRounds;
        }

        internal static void RunDeterministicSelfTests()
        {
            InternalReadinessScenario[] scenarios =
            {
                new InternalReadinessScenario("IR-01", 8, 8, new[] { 8 }, 16, true, false),
                new InternalReadinessScenario("IR-02", 8, 8, new[] { 7 }, 15, false, false),
                new InternalReadinessScenario("IR-03", 8, 0, new[] { 8, 8 }, 16, true, true),
                new InternalReadinessScenario("IR-04", 8, 9, new[] { 7 }, 16, true, false),
                new InternalReadinessScenario("IR-05", 5, 5, new[] { 5 }, 10, true, false)
            };

            List<string> failures = new List<string>();
            foreach (InternalReadinessScenario scenario in scenarios)
            {
                WeaponPrimaryReadinessSnapshot result = EvaluateFormula(
                    scenario.Capacity,
                    scenario.LoadedRounds,
                    scenario.ReserveStacks);
                if (result.TotalContribution != scenario.ExpectedTotal ||
                    result.PrimaryReady != scenario.ExpectedReady ||
                    result.RequiresMagazineLoad != scenario.ExpectedRequiresLoad)
                {
                    failures.Add(
                        $"{scenario.Id}: expected total={scenario.ExpectedTotal} ready={scenario.ExpectedReady} " +
                        $"load={scenario.ExpectedRequiresLoad}; actual {result.ToDiagnosticString()}");
                }
            }

            if (failures.Count == 0)
            {
                pitFireTeam.Log.LogInfo(
                    $"[LootCommand][InternalReadiness] Deterministic formula self-test passed ({scenarios.Length}/{scenarios.Length}).");
                return;
            }

            foreach (string failure in failures)
            {
                pitFireTeam.Log.LogError($"[LootCommand][InternalReadiness] Formula self-test failed: {failure}");
            }
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateWeaponState(
            Weapon weapon,
            IEnumerable<int> reserveStacks,
            int? loadedRoundsOverride = null)
        {
            if (!TryReadInternalState(weapon, out int capacity, out int magazineRounds, out int chamberRounds))
            {
                return EvaluateFormula(0, 0, reserveStacks, "internalMagazineUnavailable");
            }

            int loadedRounds = loadedRoundsOverride ?? magazineRounds + chamberRounds;
            return EvaluateFormula(capacity, loadedRounds, reserveStacks, "internalMagazineCapacity");
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateFormula(
            int capacity,
            int loadedRounds,
            IEnumerable<int> reserveStacks,
            string referenceReason = "provided")
        {
            int normalizedCapacity = Math.Max(0, capacity);
            int normalizedLoadedRounds = Math.Max(0, loadedRounds);
            List<int> normalizedReserveStacks = reserveStacks?
                .Where(count => count > 0)
                .Select(count => Math.Max(0, count))
                .ToList() ?? new List<int>();
            int reserveRounds = normalizedReserveStacks.Sum();
            int threshold = normalizedCapacity > 0 ? normalizedCapacity * 2 : 0;
            int total = normalizedLoadedRounds + reserveRounds;
            bool primaryReady = normalizedCapacity > 0 && total >= threshold;
            bool requiresLoad = primaryReady && normalizedLoadedRounds <= 0;

            string reason = normalizedCapacity <= 0
                ? "internalCapacityUnavailable"
                : primaryReady && requiresLoad
                ? "readyRequiresInternalLoad"
                : primaryReady
                ? "ready"
                : normalizedLoadedRounds <= 0 && reserveRounds <= 0
                ? "noLoadedOrReserveAmmo"
                : "insufficientUsableRounds";

            // Reuse the common readiness snapshot so primary binding and later promotion have one
            // contract. For internal feeds, the spare-round list represents loose reserve stacks.
            return new WeaponPrimaryReadinessSnapshot(
                normalizedCapacity,
                threshold,
                hasInsertedMagazine: normalizedCapacity > 0,
                insertedRounds: normalizedLoadedRounds,
                insertedCapacity: normalizedCapacity,
                insertedContribution: normalizedLoadedRounds,
                fastAccessMagazineRounds: normalizedReserveStacks,
                fastAccessContribution: reserveRounds,
                totalContribution: total,
                primaryReady: primaryReady,
                requiresMagazineLoad: requiresLoad,
                reason: reason,
                availableMagazineCount: normalizedReserveStacks.Count,
                referenceReason: referenceReason,
                feedKind: "internalMagazine");
        }

        private static IEnumerable<AmmoItemClass> GetCompatibleLooseAmmo(
            InventoryController inventory,
            Weapon weapon,
            Func<AmmoItemClass, bool>? reserveEligibility)
        {
            InventoryEquipment equipment = inventory?.Inventory?.Equipment;
            if (equipment == null || !IsInternalMagazineWeapon(weapon))
            {
                yield break;
            }

            HashSet<string> yieldedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentSlot slot in VanillaLooseAmmoSlots)
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root == null)
                {
                    continue;
                }

                List<Item> snapshot;
                try
                {
                    snapshot = root.GetAllItems().Where(item => item != null).ToList();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                foreach (AmmoItemClass ammo in snapshot.OfType<AmmoItemClass>())
                {
                    if (string.IsNullOrEmpty(ammo.Id) ||
                        !yieldedIds.Add(ammo.Id) ||
                        reserveEligibility?.Invoke(ammo) == false ||
                        !IsCompatibleLooseAmmo(weapon, ammo))
                    {
                        continue;
                    }

                    yield return ammo;
                }
            }
        }

        private static bool TryReadInternalState(
            Weapon weapon,
            out int capacity,
            out int magazineRounds,
            out int chamberRounds)
        {
            capacity = 0;
            magazineRounds = 0;
            chamberRounds = 0;
            try
            {
                if (!IsInternalMagazineWeapon(weapon))
                {
                    return false;
                }

                MagazineItemClass internalMagazine = weapon.GetCurrentMagazine();
                if (internalMagazine == null || internalMagazine.MaxCount <= 0)
                {
                    return false;
                }

                capacity = internalMagazine.MaxCount;
                magazineRounds = Math.Max(0, internalMagazine.Count);
                chamberRounds = Math.Max(0, weapon.ChamberAmmoCount);
                return true;
            }
            catch
            {
                capacity = 0;
                magazineRounds = 0;
                chamberRounds = 0;
                return false;
            }
        }

        private readonly struct InternalReadinessScenario
        {
            public InternalReadinessScenario(
                string id,
                int capacity,
                int loadedRounds,
                int[] reserveStacks,
                int expectedTotal,
                bool expectedReady,
                bool expectedRequiresLoad)
            {
                Id = id;
                Capacity = capacity;
                LoadedRounds = loadedRounds;
                ReserveStacks = reserveStacks;
                ExpectedTotal = expectedTotal;
                ExpectedReady = expectedReady;
                ExpectedRequiresLoad = expectedRequiresLoad;
            }

            public string Id { get; }
            public int Capacity { get; }
            public int LoadedRounds { get; }
            public int[] ReserveStacks { get; }
            public int ExpectedTotal { get; }
            public bool ExpectedReady { get; }
            public bool ExpectedRequiresLoad { get; }
        }
    }
}
