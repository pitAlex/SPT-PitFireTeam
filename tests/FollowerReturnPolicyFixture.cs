using System;
using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using pitTeam.Modules;

namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster, Backpack, TacticalVest,
        ArmorVest, Headwear, Earpiece, FaceCover, Eyewear, Pockets, SecuredContainer }
    public interface IContainer { string ID { get; } Item ParentItem { get; } }
    public class Slot : IContainer { public string ID { get; set; } public Item ParentItem { get; set; } }
    public class StackSlot : IContainer { public string ID { get; set; } public Item ParentItem { get; set; } }
    public class Grid : IContainer { public string ID { get; set; } public Item ParentItem { get; set; } }
    public class ItemAddress { public IContainer Container; }
    public class Item
    {
        public string Id;
        public ItemAddress CurrentAddress;
        public List<Item> Children = new List<Item>();
        public IEnumerable<Item> GetAllItems() => Children.SelectMany(child => new[] { child }.Concat(child.GetAllItems()));
    }
    public class InventoryEquipment : Item { }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static Item Put(Item parent, string id, string slot, bool grid = false, bool stack = false)
    {
        IContainer container = grid ? (IContainer)new Grid { ID = slot, ParentItem = parent } :
            stack ? new StackSlot { ID = slot, ParentItem = parent } : new Slot { ID = slot, ParentItem = parent };
        var item = new Item { Id = id, CurrentAddress = new ItemAddress { Container = container } };
        parent.Children.Add(item);
        return item;
    }
    public static void Main()
    {
        var equipment = new InventoryEquipment { Id = "equipment" };
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            Item item = Put(equipment, slot.ToString(), slot.ToString());
            bool gear = slot != EquipmentSlot.Pockets && slot != EquipmentSlot.SecuredContainer;
            Check(FollowerReturnPolicy.IsRetainedEquipment(item, equipment) == gear, "slot " + slot);
        }
        Item bag = equipment.Children.Single(item => item.Id == "Backpack");
        Item rig = equipment.Children.Single(item => item.Id == "TacticalVest");
        Item gun = equipment.Children.Single(item => item.Id == "FirstPrimaryWeapon");
        Item mag = Put(gun, "seatedMag", "mod_magazine");
        Item rounds = Put(mag, "loadedAmmo", "cartridges", stack: true);
        Item plate = Put(rig, "plate", "mod_plate_front");
        Item cargo = Put(bag, "cargo", "main", grid: true);
        Item looseMag = Put(rig, "looseMag", "main", grid: true);
        Item looseRounds = Put(looseMag, "looseRounds", "cartridges", stack: true);
        Item pocket = Put(equipment.Children.Single(item => item.Id == "Pockets"), "pocket", "main", grid: true);
        foreach (Item item in new[] { mag, rounds, plate })
            Check(FollowerReturnPolicy.IsRetainedEquipment(item, equipment), "attached gear " + item.Id);
        foreach (Item item in new[] { cargo, looseMag, looseRounds, pocket })
            Check(!FollowerReturnPolicy.IsRetainedEquipment(item, equipment), "cargo " + item.Id);
        var all = equipment.GetAllItems().ToDictionary(item => item.Id);
        Func<string, Item> find = id => all.TryGetValue(id, out Item item) ? item : null;
        List<string> tracked = new List<string> { bag.Id, rig.Id, gun.Id, pocket.Id, looseMag.Id, "missing" };
        List<string> returned = FollowerReturnPolicy.GetReturnIds(true, equipment, tracked, find);
        Check(new HashSet<string>(returned).SetEquals(new[] { "cargo", "looseMag", "looseRounds", "pocket" }),
            "equipped container retains shell, cargo expands once");
        Check(returned.Count == returned.Distinct().Count(), "no duplicate child ids");
        Check(!FollowerReturnPolicy.GetReturnIds(false, equipment, tracked, id => throw new Exception("recruit inventory queried")).Any(),
            "membership gate before lookup");
        Check(!FollowerReturnPolicy.GetReturnIds(true, null, tracked, find).Any(), "missing inventory");
        Check(!FollowerReturnPolicy.GetReturnIds(true, equipment, null, find).Any(), "missing tracking");
        bag.CurrentAddress = new ItemAddress { Container = new Grid { ID = "main", ParentItem = rig } };
        Check(FollowerReturnPolicy.GetReturnIds(true, equipment, new[] { bag.Id }, find).Contains(bag.Id), "unequipped bag returns as cargo");
        bag.CurrentAddress = new ItemAddress { Container = new Slot { ID = "Backpack", ParentItem = equipment } };
        Check(!FollowerReturnPolicy.GetReturnIds(true, equipment, new[] { bag.Id }, find).Contains(bag.Id), "restored original bag stays equipped");
        var detached = new Item { Id = "detached" };
        Check(!FollowerReturnPolicy.IsRetainedEquipment(detached, equipment), "detached item");
        detached.CurrentAddress = new ItemAddress { Container = new Slot { ID = "cycle", ParentItem = detached } };
        Check(!FollowerReturnPolicy.IsRetainedEquipment(detached, equipment), "bounded cyclic ancestry");
        Console.WriteLine($"PASS: {checks} follower return policy assertions.");
    }
}
