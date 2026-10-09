using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerReturnPolicy
    {
        // An equipped root and its slotted attachments belong to the kit. Crossing any
        // grid boundary instead means cargo, even inside an equipped backpack or rig.
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
            IEnumerable<string> trackedIds, Func<string, Item> findItem)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!isSquadMember || equipment == null || trackedIds == null) return result.ToList();
            foreach (string id in trackedIds)
            {
                Item root = findItem(id);
                if (root == null) continue;
                // A tracked container can become equipped: retain its shell without
                // losing the separately returnable cargo that originally rode inside it.
                foreach (Item item in new[] { root }.Concat(root.GetAllItems()))
                    if (item != null && !IsRetainedEquipment(item, equipment)) result.Add(item.Id);
            }
            return result.ToList();
        }
    }
}
