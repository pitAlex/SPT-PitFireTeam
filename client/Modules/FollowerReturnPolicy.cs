using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerReturnPolicy
    {
        // An equipped root and its slotted attachments belong to the kit. Crossing any
        // grid boundary instead means cargo, except registered fast-access weapon magazines
        // handled separately when collecting return IDs.
        internal static bool IsRetainedEquipment(Item item, InventoryEquipment equipment)
        {
            for (int depth = 0; item != null && depth < 64; depth++)
            {
                var address = item.CurrentAddress;
                if (address?.Container is not Slot && address?.Container is not StackSlot) return false;
                Item parent = address.Container.ParentItem;
                if (ReferenceEquals(parent, equipment))
                {
                    return Enum.TryParse(address.Container.ID, out EquipmentSlot slot) &&
                        (slot == EquipmentSlot.FirstPrimaryWeapon || slot == EquipmentSlot.SecondPrimaryWeapon ||
                         slot == EquipmentSlot.Holster || slot == EquipmentSlot.Backpack ||
                         slot == EquipmentSlot.TacticalVest || slot == EquipmentSlot.ArmorVest ||
                         slot == EquipmentSlot.Headwear || slot == EquipmentSlot.Earpiece ||
                         slot == EquipmentSlot.FaceCover || slot == EquipmentSlot.Eyewear);
                }
                item = parent;
            }
            return false;
        }

        internal static List<string> GetReturnIds(bool isSquadMember, InventoryEquipment equipment,
            IEnumerable<string> trackedIds, Func<string, Item> findItem,
            Func<Weapon, Magazine, bool> isRegisteredMagazine = null)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!isSquadMember || equipment == null || trackedIds == null) return result.ToList();
            HashSet<string> kitMagazineIds = GetKitMagazineItemIds(equipment, isRegisteredMagazine);
            foreach (string id in trackedIds)
            {
                Item root = findItem(id);
                if (root == null) continue;
                // A tracked container can become equipped: retain its shell without
                // losing the separately returnable cargo that originally rode inside it.
                foreach (Item item in new[] { root }.Concat(root.GetAllItems()))
                    if (item != null && !kitMagazineIds.Contains(item.Id) && !IsRetainedEquipment(item, equipment))
                        result.Add(item.Id);
            }
            return result.ToList();
        }

        private static HashSet<string> GetKitMagazineItemIds(InventoryEquipment equipment,
            Func<Weapon, Magazine, bool> isRegisteredMagazine)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (isRegisteredMagazine == null) return ids;
            var weapons = new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster }
                .Select(slot => equipment.GetSlot(slot)?.ContainedItem).OfType<Weapon>().ToArray();
            if (weapons.Length == 0) return ids;
            foreach (EquipmentSlot slot in new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Pockets })
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root == null) continue;
                foreach (Magazine magazine in root.GetAllItems().OfType<Magazine>())
                {
                    // Only reload-ready grid contents, not a nested bag or a cargo gun's magazine.
                    // Registration alone is insufficient once a weapon or its spares become cargo.
                    if (magazine.CurrentAddress?.Container is not Grid grid ||
                        !ReferenceEquals(grid.ParentItem, root) ||
                        !weapons.Any(weapon => isRegisteredMagazine(weapon, magazine))) continue;
                    ids.Add(magazine.Id);
                    foreach (Item child in magazine.GetAllItems()) ids.Add(child.Id);
                }
            }
            return ids;
        }
    }
}
