using EFT.InventoryLogic;
using System;
using System.Linq;

namespace pitTeam.Modules
{
    /// <summary>
    /// Separates physical magazine fit from combat usability. Some magazine families fit weapons
    /// of different calibers, so every loaded cartridge must also be valid for the weapon.
    /// </summary>
    internal static class FollowerWeaponMagazineCompatibility
    {
        internal static bool IsMechanicallyCompatible(Weapon weapon, MagazineItemClass magazine)
        {
            if (weapon == null || magazine == null)
            {
                return false;
            }

            try
            {
                MagazineItemClass currentMagazine = weapon.GetCurrentMagazine();
                if (currentMagazine != null &&
                    string.Equals(currentMagazine.TemplateId, magazine.TemplateId, StringComparison.Ordinal))
                {
                    return true;
                }

                Slot magazineSlot = weapon.GetMagazineSlot();
                return magazineSlot != null && magazineSlot.CanAccept(magazine);
            }
            catch
            {
                return false;
            }
        }

        internal static bool AreLoadedCartridgesCompatible(Weapon weapon, MagazineItemClass magazine)
        {
            if (weapon == null || magazine == null)
            {
                return false;
            }

            if (magazine.Count <= 0)
            {
                return true;
            }

            try
            {
                int inspectedRounds = 0;
                foreach (AmmoItemClass ammo in magazine.Cartridges.Items
                             .OfType<AmmoItemClass>()
                             .Where(ammo => ammo != null && ammo.StackObjectsCount > 0)
                             .ToArray())
                {
                    if (!FollowerWeaponLooseAmmoSupport.IsCartridgeCompatible(weapon, ammo))
                    {
                        return false;
                    }

                    inspectedRounds += ammo.StackObjectsCount;
                }

                // Fail closed when EFT reports loaded rounds that could not be inspected.
                return inspectedRounds >= magazine.Count;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsOperational(Weapon weapon, MagazineItemClass magazine)
        {
            return magazine?.Count > 0 &&
                   IsMechanicallyCompatible(weapon, magazine) &&
                   AreLoadedCartridgesCompatible(weapon, magazine);
        }
    }
}
