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
    public class Slot : IContainer { public string ID { get; set; } public Item ParentItem { get; set; } public Item ContainedItem; }
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
    public class Weapon : Item { }
    public class Magazine : Item { }
    public class InventoryEquipment : Item
    {
        public Slot GetSlot(EquipmentSlot slot) => new Slot { ID = slot.ToString(), ParentItem = this,
            ContainedItem = Children.FirstOrDefault(item => item.CurrentAddress?.Container is Slot address &&
                ReferenceEquals(address.ParentItem, this) && address.ID == slot.ToString()) };
    }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static Item Put(Item parent, string id, string slot, bool grid = false, bool stack = false, Item value = null)
    {
        IContainer container = grid ? (IContainer)new Grid { ID = slot, ParentItem = parent } :
            stack ? new StackSlot { ID = slot, ParentItem = parent } : new Slot { ID = slot, ParentItem = parent };
        var item = value ?? new Item();
        item.Id = id;
        item.CurrentAddress = new ItemAddress { Container = container };
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
        VerifyRegisteredMagazines();
        Console.WriteLine($"PASS: {checks} follower return policy assertions.");
    }

    private static void VerifyRegisteredMagazines()
    {
        foreach (EquipmentSlot weaponSlot in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster })
        foreach (string location in new[] { "vest", "pockets", "backpack", "secure", "nested", "special" })
        foreach (bool registered in new[] { false, true })
        foreach (bool loaded in new[] { false, true })
        {
            var equipment = new InventoryEquipment { Id = "equipment" };
            var weapon = (Weapon)Put(equipment, "weapon", weaponSlot.ToString(), value: new Weapon());
            Item rig = Put(equipment, "rig", "TacticalVest");
            Item pockets = Put(equipment, "pockets", "Pockets");
            Item bag = Put(equipment, "bag", "Backpack");
            Item secure = Put(equipment, "secure", "SecuredContainer");
            Item nested = Put(rig, "nestedBag", "main", grid: true);
            Item parent = location == "vest" ? rig : location == "pockets" || location == "special" ? pockets :
                location == "backpack" ? bag : location == "secure" ? secure : nested;
            var magazine = (Magazine)Put(parent, "packageMag", location == "special" ? "SpecialSlot1" : "main",
                grid: location != "special", value: new Magazine());
            Item ammo = loaded ? Put(magazine, "packageRounds", "cartridges", stack: true) : null;
            var unrelated = (Magazine)Put(rig, "cargoMag", "main", grid: true, value: new Magazine());
            Item cargoRounds = Put(unrelated, "cargoRounds", "cartridges", stack: true);
            var all = equipment.GetAllItems().ToDictionary(item => item.Id);
            Func<string, Item> find = id => all.TryGetValue(id, out Item item) ? item : null;
            Func<Weapon, Magazine, bool> approved = (gun, mag) =>
                registered && ReferenceEquals(gun, weapon) && ReferenceEquals(mag, magazine);
            var tracked = new List<string> { rig.Id, magazine.Id, unrelated.Id, cargoRounds.Id };
            if (ammo != null) tracked.Add(ammo.Id);
            string scenario = weaponSlot + "/" + location + "/registered=" + registered + "/loaded=" + loaded;
            List<string> returned = FollowerReturnPolicy.GetReturnIds(true, equipment, tracked, find, approved);
            bool retained = registered && (location == "vest" || location == "pockets");
            Check(returned.Contains(magazine.Id) != retained, "registered magazine classification " + scenario);
            if (loaded) Check(returned.Contains(ammo.Id) != retained, "loaded rounds follow magazine " + scenario);
            Check(returned.Contains(unrelated.Id) && returned.Contains(cargoRounds.Id), "unregistered cargo still returns " + scenario);
            Check(returned.Count == returned.Distinct().Count(), "overlapping tracked trees deduplicate " + scenario);

            // Registration is exact to the retained weapon, not a compatibility/cargo exemption.
            equipment.Children.Remove(weapon);
            weapon.CurrentAddress = new ItemAddress { Container = new Grid { ID = "main", ParentItem = bag } };
            bag.Children.Add(weapon);
            returned = FollowerReturnPolicy.GetReturnIds(true, equipment, tracked, find, approved);
            Check(returned.Contains(magazine.Id) && (ammo == null || returned.Contains(ammo.Id)), "cargo weapon cannot retain spares " + scenario);
            Check(!FollowerReturnPolicy.GetReturnIds(false, equipment, tracked,
                id => throw new Exception("recruit lookup"), (gun, mag) => throw new Exception("recruit registry")).Any(),
                "membership gate precedes magazine classification " + scenario);
        }

        var sharedEquipment = new InventoryEquipment { Id = "shared" };
        var primary = (Weapon)Put(sharedEquipment, "primary", "FirstPrimaryWeapon", value: new Weapon());
        var secondary = (Weapon)Put(sharedEquipment, "secondary", "SecondPrimaryWeapon", value: new Weapon());
        Item sharedRig = Put(sharedEquipment, "sharedRig", "TacticalVest");
        var sharedMag = (Magazine)Put(sharedRig, "sharedMag", "main", grid: true, value: new Magazine());
        var sharedItems = sharedEquipment.GetAllItems().ToDictionary(item => item.Id);
        Check(!FollowerReturnPolicy.GetReturnIds(true, sharedEquipment, new[] { sharedMag.Id }, id => sharedItems[id],
            (gun, mag) => ReferenceEquals(gun, secondary) && ReferenceEquals(mag, sharedMag)).Any(),
            "secondary registration retains its magazine even when primary is not registered");
        Check(FollowerReturnPolicy.GetReturnIds(true, sharedEquipment, new[] { sharedMag.Id }, id => sharedItems[id],
            (gun, mag) => false).Contains(sharedMag.Id), "unregistered compatible cargo is not promoted to kit");
    }
}
