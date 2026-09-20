using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;

// Small grid doubles exercise the production guard without loading Unity. EFT APIs are checked by the client build.
namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { TacticalVest, Pockets, FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
    public class Item { public string Id = Guid.NewGuid().ToString(); public int Width = 1, Height = 1; public ItemAddress CurrentAddress; }
    public class Magazine : Item { public int Count = 60; }
    public class Weapon : Item
    {
        public enum EReloadMode { ExternalMagazine, InternalMagazine }
        public EReloadMode ReloadMode; public Magazine Magazine;
    }
    public class Slot { public Item ContainedItem; }
    public class InventoryEquipment
    {
        public Dictionary<EquipmentSlot, Slot> Slots = new Dictionary<EquipmentSlot, Slot>();
        public Slot GetSlot(EquipmentSlot slot) => Slots.TryGetValue(slot, out var result) ? result : null;
        public void Set(EquipmentSlot slot, Item item) => Slots[slot] = new Slot { ContainedItem = item };
    }
    public class SearchableItem : Item
    {
        public Grid[] Grids;
        public SearchableItem CloneItemWithSameId()
        {
            var copy = new SearchableItem { Id = Id };
            copy.Grids = Grids.Select(grid => new Grid {
                ID = grid.ID, ParentItem = copy, Width = grid.Width, Height = grid.Height,
                Placed = grid.Placed.ToDictionary(pair => new Item { Id = pair.Key.Id, Width = pair.Key.Width, Height = pair.Key.Height }, pair => pair.Value)
            }).ToArray();
            return copy;
        }
    }
    public class ItemAddress { }
    public class LocationInGrid { public int X, Y; public bool Rotated; }
    public class GridItemAddress : ItemAddress { public Grid Grid; public LocationInGrid LocationInGrid; }
    public class Result { public bool Failed; }
    public class Grid
    {
        public string ID; public SearchableItem ParentItem; public int Width, Height;
        public Dictionary<Item, LocationInGrid> Placed = new Dictionary<Item, LocationInGrid>();
        public IEnumerable<Item> Items => Placed.Keys;
        private bool Fits(Item item, LocationInGrid location)
        {
            int w = location.Rotated ? item.Height : item.Width, h = location.Rotated ? item.Width : item.Height;
            if (location.X < 0 || location.Y < 0 || location.X + w > Width || location.Y + h > Height) return false;
            return !Placed.Any(entry => {
                int ew = entry.Value.Rotated ? entry.Key.Height : entry.Key.Width;
                int eh = entry.Value.Rotated ? entry.Key.Width : entry.Key.Height;
                return location.X < entry.Value.X + ew && location.X + w > entry.Value.X &&
                    location.Y < entry.Value.Y + eh && location.Y + h > entry.Value.Y;
            });
        }
        public bool TryFindLocationForItem(Item item, out ItemAddress address)
        {
            foreach (bool rotated in new[] { false, true })
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                var location = new LocationInGrid { X = x, Y = y, Rotated = rotated };
                if (Fits(item, location)) { address = new GridItemAddress { Grid = this, LocationInGrid = location }; return true; }
            }
            address = null; return false;
        }
        public Result Add(Item item, LocationInGrid location, bool simulate)
        {
            if (!Fits(item, location)) return new Result { Failed = true };
            if (!simulate) Placed.Add(item, location);
            return new Result();
        }
        public Result Remove(Item item, bool simulate) => new Result { Failed = !Placed.Remove(item) };
    }
}
namespace pitTeam.Modules { public static class Logger { public static void LogError(string message) => Console.WriteLine(message); } }
namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static Magazine GetCurrentMagazineSafely(Weapon weapon) => weapon.Magazine;
        private static Item ClonePlanningItem(Item item) => new Item { Id = item.Id, Width = item.Width, Height = item.Height };
        private static int assertions;
        private static void Check(bool value, string name)
        {
            assertions++;
            if (!value) throw new Exception(name);
        }
        private static SearchableItem Rig(params int[] heights)
        {
            var rig = new SearchableItem();
            rig.Grids = heights.Select((height, index) => new Grid {
                ID = index.ToString(), ParentItem = rig, Width = 1, Height = height
            }).ToArray();
            return rig;
        }
        private static GridItemAddress At(SearchableItem rig, int index, int x = 0, int y = 0) =>
            new GridItemAddress { Grid = rig.Grids[index], LocationInGrid = new LocationInGrid { X = x, Y = y } };
        private static Weapon Gun(int width, int height) => new Weapon { Magazine = new Magazine { Width = width, Height = height } };
        public static void Main()
        {
            var equipment = new InventoryEquipment();
            var svd = Gun(1, 4);
            equipment.Set(EquipmentSlot.FirstPrimaryWeapon, svd);
            var rig = Rig(4, 2, 2);
            equipment.Set(EquipmentSlot.TacticalVest, rig);
            var loot = new Magazine { Height = 2 };
            Check(!PreservesEquippedMagazineReloadSpace(equipment, loot, At(rig, 0)), "SVD four-cell opening must survive");
            Check(PreservesEquippedMagazineReloadSpace(equipment, loot, At(rig, 1)), "Small slot remains usable");
            Check(rig.Grids.All(grid => !grid.Items.Any()), "Validation must not mutate live grids");
            rig.Grids[1].Add(loot, At(rig, 1).LocationInGrid, false);
            var next = new Magazine { Height = 2 };
            Check(PreservesEquippedMagazineReloadSpace(equipment, next, At(rig, 2)), "Second safe pickup");
            rig.Grids[2].Add(next, At(rig, 2).LocationInGrid, false);
            Check(!PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(rig, 0)), "Consecutive loot cannot consume last opening");
            Check(PreservesEquippedMagazineReloadSpace(equipment, svd.Magazine, At(rig, 0)), "Stowing the inserted magazine itself is allowed");
            svd.Magazine.Count = 0;
            Check(!PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(rig, 0)), "Empty inserted magazine is still protected");
            equipment.Set(EquipmentSlot.SecondPrimaryWeapon, Gun(1, 4));
            var shared = Rig(4, 2);
            equipment.Set(EquipmentSlot.TacticalVest, shared);
            Check(PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(shared, 1)), "Two guns can share one landing opening");
            var square = Gun(2, 2);
            equipment.Set(EquipmentSlot.SecondPrimaryWeapon, square);
            shared.Grids[1].Width = 2;
            Check(!PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(shared, 1)), "Equal-area different shapes remain protected independently");
            equipment.Set(EquipmentSlot.SecondPrimaryWeapon, null);
            equipment.Set(EquipmentSlot.FirstPrimaryWeapon, null);
            equipment.Set(EquipmentSlot.Holster, Gun(1, 2));
            var pockets = Rig(2, 1);
            equipment.Set(EquipmentSlot.TacticalVest, null);
            equipment.Set(EquipmentSlot.Pockets, pockets);
            Check(!PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(pockets, 0)), "Holster and pocket reserves protected");
            Check(PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(pockets, 1)), "Unneeded pocket space remains usable");
            equipment.Set(EquipmentSlot.FirstPrimaryWeapon, svd);
            equipment.Set(EquipmentSlot.Holster, null);
            Check(PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(pockets, 1)), "Preexisting impossible magazine does not block loot");
            Check(PreservesEquippedMagazineReloadSpace(equipment, new Item(), At(Rig(4), 0)), "Backpack or nested cargo unaffected");
            Check(PreservesEquippedMagazineReloadSpace(equipment, new Item(), new ItemAddress()), "Equipment slot and ammo-stack operations unaffected");
            var relocation = Rig(6);
            equipment.Set(EquipmentSlot.Pockets, null);
            equipment.Set(EquipmentSlot.TacticalVest, relocation);
            var carried = new Magazine { Height = 2 };
            relocation.Grids[0].Add(carried, At(relocation, 0).LocationInGrid, false);
            Check(PreservesEquippedMagazineReloadSpace(equipment, carried, At(relocation, 0, 0, 4)), "Relocation credits vacated cells");
            Check(relocation.Grids[0].Placed[carried].Y == 0, "Relocation simulation leaves original position alone");
            Console.WriteLine($"Loot reload reserve fixture passed: {assertions} assertions.");
        }
    }
}
