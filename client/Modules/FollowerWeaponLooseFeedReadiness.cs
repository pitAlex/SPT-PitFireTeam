using EFT.InventoryLogic;
using System;
using System.Collections.Generic;

namespace pitTeam.Modules
{
    /// <summary>
    /// Shared dispatch for weapons whose readiness is loaded directly from loose ammunition.
    /// Detachable-magazine weapons stay on <see cref="FollowerWeaponPrimaryReadiness"/>.
    /// </summary>
    internal static class FollowerWeaponLooseFeedReadiness
    {
        internal static bool IsSupported(Weapon weapon)
        {
            return FollowerWeaponInternalReadiness.IsInternalMagazineWeapon(weapon) ||
                   FollowerWeaponChamberReadiness.IsSupportedChamberWeapon(weapon);
        }

        internal static bool IsCompatibleLooseAmmo(Weapon weapon, EFT.InventoryLogic.Ammo ammo)
        {
            if (FollowerWeaponInternalReadiness.IsInternalMagazineWeapon(weapon))
            {
                return FollowerWeaponInternalReadiness.IsCompatibleLooseAmmo(weapon, ammo);
            }

            return FollowerWeaponChamberReadiness.IsCompatibleLooseAmmo(weapon, ammo);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateActual(
            InventoryController inventory,
            Weapon weapon,
            Func<EFT.InventoryLogic.Ammo, bool>? reserveEligibility = null)
        {
            if (FollowerWeaponInternalReadiness.IsInternalMagazineWeapon(weapon))
            {
                return FollowerWeaponInternalReadiness.EvaluateActual(
                    inventory,
                    weapon,
                    reserveEligibility);
            }

            return FollowerWeaponChamberReadiness.EvaluateActual(
                inventory,
                weapon,
                reserveEligibility);
        }

        internal static WeaponPrimaryReadinessSnapshot EvaluateProjected(
            Weapon weapon,
            IEnumerable<int> projectedReserveStacks,
            int? loadedRoundsOverride = null)
        {
            if (FollowerWeaponInternalReadiness.IsInternalMagazineWeapon(weapon))
            {
                return FollowerWeaponInternalReadiness.EvaluateProjected(
                    weapon,
                    projectedReserveStacks,
                    loadedRoundsOverride);
            }

            return FollowerWeaponChamberReadiness.EvaluateProjected(
                weapon,
                projectedReserveStacks,
                loadedRoundsOverride);
        }

        internal static int GetLoadedRounds(Weapon weapon)
        {
            if (FollowerWeaponInternalReadiness.IsInternalMagazineWeapon(weapon))
            {
                return FollowerWeaponInternalReadiness.GetLoadedRounds(weapon);
            }

            return FollowerWeaponChamberReadiness.GetLoadedRounds(weapon);
        }
    }
}
