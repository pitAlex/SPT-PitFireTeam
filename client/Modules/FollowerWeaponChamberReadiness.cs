using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    /// <summary>
    /// Readiness policy for supported <see cref="Weapon.EReloadMode.OnlyBarrel"/> weapons.
    /// Chamber contents are the live loaded state; compatible loose rounds are the reserve.
    /// </summary>
    internal static class FollowerWeaponChamberReadiness
    {
        private const int MinimumReadinessRounds = 8;

        private static readonly EquipmentSlot[] VanillaLooseAmmoSlots =
        {
            EquipmentSlot.Pockets,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };

        internal static bool IsSupportedChamberWeapon(Weapon weapon)
        {
            try
            {
                // Vanilla reload selects Chambers[0] for a single-barrel weapon and the first
                // free chamber for a multi-barrel weapon. Weapon.Apply delegates loose ammo to
                // those chamber slots in both cases. Launchers retain their specialized hands path.
                return weapon?.ReloadMode == Weapon.EReloadMode.OnlyBarrel &&
                       weapon is not EFT.InventoryLogic.GrenadeLauncher &&
                       weapon is not EFT.InventoryLogic.RocketLauncher &&
                       weapon.Chambers?.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon,
            Func<EFT.InventoryLogic.Ammo, bool>? reserveEligibility = null)
        {
            List<EFT.InventoryLogic.Ammo> reserves = GetCompatibleLooseAmmo(
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

        internal static bool IsCompatibleLooseAmmo(Weapon weapon, EFT.InventoryLogic.Ammo ammo)
        {
            if (!IsSupportedChamberWeapon(weapon) ||
                !FollowerWeaponLooseAmmoSupport.IsCompatible(weapon, ammo))
            {
                return false;
            }

            try
            {
                // Slot.CanAccept also checks whether a chamber is occupied. Caliber compatibility
                // therefore remains authoritative when every chamber is already loaded.
                string weaponCaliber = NormalizeCaliber(weapon.AmmoCaliber);
                string ammoCaliber = NormalizeCaliber(ammo.Caliber);
                return !string.IsNullOrEmpty(weaponCaliber) &&
                       string.Equals(weaponCaliber, ammoCaliber, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static int GetLoadedRounds(Weapon weapon)
        {
            return TryReadChamberState(weapon, out _, out int loadedRounds)
                ? loadedRounds
                : 0;
        }

        internal static int GetFreeChamberCount(Weapon weapon)
        {
            if (!TryReadChamberState(weapon, out int capacity, out _))
            {
                return 0;
            }

            try
            {
                // Weapon.Apply can fill an empty slot off-hands. Replacing spent shells is a
                // different vanilla operation and is intentionally not projected here.
                int empty = weapon.Chambers.Count(chamber => chamber?.ContainedItem == null);
                return Math.Min(capacity, Math.Max(0, empty));
            }
            catch
            {
                return 0;
            }
        }

        internal static void RunDeterministicSelfTests()
        {
            ChamberReadinessScenario[] scenarios =
            {
                new ChamberReadinessScenario("CR-01", 2, 2, new[] { 6 }, 8, true, false),
                new ChamberReadinessScenario("CR-02", 2, 2, new[] { 5 }, 7, false, false),
                new ChamberReadinessScenario("CR-03", 2, 0, new[] { 8 }, 8, true, true),
                new ChamberReadinessScenario("CR-04", 2, 1, new[] { 7 }, 8, true, true),
                new ChamberReadinessScenario("CR-05", 1, 1, new[] { 7 }, 8, true, false),
                new ChamberReadinessScenario("CR-06", 1, 1, new[] { 6 }, 7, false, false),
                new ChamberReadinessScenario("CR-07", 1, 0, new[] { 8 }, 8, true, true),
                new ChamberReadinessScenario("CR-08", 5, 5, new[] { 5 }, 10, true, false)
            };

            List<string> failures = new List<string>();
            foreach (ChamberReadinessScenario scenario in scenarios)
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
                Logger.LogInfo(
                    $"[LootCommand][ChamberReadiness] Deterministic formula self-test passed ({scenarios.Length}/{scenarios.Length}).");
                return;
            }

            foreach (string failure in failures)
            {
                pitFireTeam.Log.LogError($"[LootCommand][ChamberReadiness] Formula self-test failed: {failure}");
            }
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateWeaponState(
            Weapon weapon,
            IEnumerable<int> reserveStacks,
            int? loadedRoundsOverride = null)
        {
            if (!TryReadChamberState(weapon, out int capacity, out int loadedRounds))
            {
                return EvaluateFormula(0, 0, reserveStacks, "chamberCapacityUnavailable");
            }

            return EvaluateFormula(
                capacity,
                loadedRoundsOverride ?? loadedRounds,
                reserveStacks,
                "chamberCapacity");
        }

        private static WeaponPrimaryReadinessSnapshot EvaluateFormula(
            int capacity,
            int loadedRounds,
            IEnumerable<int> reserveStacks,
            string referenceReason = "provided")
        {
            int normalizedCapacity = Math.Max(0, capacity);
            int normalizedLoadedRounds = Math.Min(
                normalizedCapacity,
                Math.Max(0, loadedRounds));
            List<int> normalizedReserveStacks = reserveStacks?
                .Where(count => count > 0)
                .Select(count => Math.Max(0, count))
                .ToList() ?? new List<int>();
            int reserveRounds = normalizedReserveStacks.Sum();
            // Two chamber loads are too permissive for low-capacity weapons: a double barrel
            // would otherwise become the combat primary with only four shells. Eight total
            // rounds gives it a small but useful reserve while larger feeds keep the two-load rule.
            int threshold = normalizedCapacity > 0
                ? Math.Max(normalizedCapacity * 2, MinimumReadinessRounds)
                : 0;
            int total = normalizedLoadedRounds + reserveRounds;
            bool primaryReady = normalizedCapacity > 0 && total >= threshold;
            bool requiresLoad = primaryReady && normalizedLoadedRounds < normalizedCapacity;

            string reason = normalizedCapacity <= 0
                ? "chamberCapacityUnavailable"
                : primaryReady && requiresLoad
                ? "readyRequiresChamberLoad"
                : primaryReady
                ? "ready"
                : normalizedLoadedRounds <= 0 && reserveRounds <= 0
                ? "noChamberedOrReserveAmmo"
                : "insufficientUsableRounds";

            return new WeaponPrimaryReadinessSnapshot(
                normalizedCapacity,
                threshold,
                hasInsertedMagazine: normalizedLoadedRounds > 0,
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
                feedKind: "chamberFed");
        }

        private static IEnumerable<EFT.InventoryLogic.Ammo> GetCompatibleLooseAmmo(
            InventoryController inventory,
            Weapon weapon,
            Func<EFT.InventoryLogic.Ammo, bool>? reserveEligibility)
        {
            InventoryEquipment equipment = inventory?.Inventory?.Equipment;
            if (equipment == null || !IsSupportedChamberWeapon(weapon))
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

                foreach (EFT.InventoryLogic.Ammo ammo in snapshot.OfType<EFT.InventoryLogic.Ammo>())
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

        private static bool TryReadChamberState(
            Weapon weapon,
            out int capacity,
            out int loadedRounds)
        {
            capacity = 0;
            loadedRounds = 0;
            try
            {
                if (!IsSupportedChamberWeapon(weapon))
                {
                    return false;
                }

                Slot[] chambers = weapon.Chambers;
                capacity = chambers.Length;
                loadedRounds = chambers.Count(chamber =>
                    chamber?.ContainedItem is EFT.InventoryLogic.Ammo ammo && !ammo.IsUsed);
                return capacity > 0;
            }
            catch
            {
                capacity = 0;
                loadedRounds = 0;
                return false;
            }
        }

        private static string NormalizeCaliber(string caliber)
        {
            return (caliber ?? string.Empty).Replace("Caliber", string.Empty).Trim();
        }

        private readonly struct ChamberReadinessScenario
        {
            public ChamberReadinessScenario(
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
