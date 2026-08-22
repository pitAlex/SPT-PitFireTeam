using EFT.InventoryLogic;
using System;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponLooseAmmoSupport
    {
        internal static bool IsCompatible(Weapon weapon, EFT.InventoryLogic.Ammo ammo)
        {
            if (weapon == null ||
                ammo == null ||
                ammo.IsUsed ||
                ammo.StackObjectsCount <= 0 ||
                IsInsideWeaponOrMagazine(ammo))
            {
                return false;
            }

            return IsCartridgeCompatible(weapon, ammo);
        }

        internal static bool IsCartridgeCompatible(Weapon weapon, EFT.InventoryLogic.Ammo ammo)
        {
            if (weapon == null ||
                ammo == null ||
                ammo.IsUsed ||
                ammo.StackObjectsCount <= 0)
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

        internal static bool IsSameCaliber(EFT.InventoryLogic.Ammo left, EFT.InventoryLogic.Ammo right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(
                       NormalizeCaliber(left.Caliber),
                       NormalizeCaliber(right.Caliber),
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsMorePowerful(EFT.InventoryLogic.Ammo candidate, EFT.InventoryLogic.Ammo baseline)
        {
            if (!IsSameCaliber(candidate, baseline))
            {
                return false;
            }

            return ComparePower(candidate, baseline) > 0;
        }

        internal static int ComparePower(EFT.InventoryLogic.Ammo left, EFT.InventoryLogic.Ammo right)
        {
            if (left == null || right == null)
            {
                return left == null ? (right == null ? 0 : -1) : 1;
            }

            // Penetration is the decisive Tarkov upgrade signal. Damage and armor damage provide
            // deterministic tie-breaks without inventing a broad combat-effectiveness formula.
            int comparison = left.PenetrationPower.CompareTo(right.PenetrationPower);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Damage.CompareTo(right.Damage);
            if (comparison != 0)
            {
                return comparison;
            }

            return left.ArmorDamage.CompareTo(right.ArmorDamage);
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
                    .Any(parent => parent is Weapon || parent is EFT.InventoryLogic.Magazine);
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
