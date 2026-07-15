using EFT.InventoryLogic;
using System;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponLooseAmmoSupport
    {
        internal static bool IsCompatible(Weapon weapon, AmmoItemClass ammo)
        {
            if (weapon == null ||
                ammo == null ||
                ammo.IsUsed ||
                ammo.StackObjectsCount <= 0 ||
                IsInsideWeaponOrMagazine(ammo))
            {
                return false;
            }

            try
            {
                if (weapon.Chambers?.Any(chamber => chamber?.CanAccept(ammo) == true) == true)
                {
                    return true;
                }

                // Some weapon configurations expose no usable chamber slot while their caliber is
                // still authoritative. Keep this as a fallback, not the primary compatibility test.
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

        internal static bool IsSameCaliber(AmmoItemClass left, AmmoItemClass right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(
                       NormalizeCaliber(left.Caliber),
                       NormalizeCaliber(right.Caliber),
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsMorePowerful(AmmoItemClass candidate, AmmoItemClass baseline)
        {
            if (!IsSameCaliber(candidate, baseline))
            {
                return false;
            }

            // Penetration is the decisive Tarkov upgrade signal. Damage and armor damage provide
            // deterministic tie-breaks without inventing a broad combat-effectiveness formula.
            int comparison = candidate.PenetrationPower.CompareTo(baseline.PenetrationPower);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            comparison = candidate.Damage.CompareTo(baseline.Damage);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            return candidate.ArmorDamage > baseline.ArmorDamage;
        }

        internal static bool IsShotgun(Weapon weapon)
        {
            try
            {
                return weapon?.WeapClass?.IndexOf("shotgun", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInsideWeaponOrMagazine(Item ammo)
        {
            try
            {
                return ammo.GetAllParentItems(false)
                    .Any(parent => parent is Weapon || parent is MagazineItemClass);
            }
            catch
            {
                return true;
            }
        }

        private static string NormalizeCaliber(string caliber)
        {
            return (caliber ?? string.Empty).Replace("Caliber", string.Empty).Trim();
        }
    }
}
