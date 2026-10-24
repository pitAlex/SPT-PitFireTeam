using System;
using System.Collections.Generic;
using System.Linq;

internal enum EPlayerSide { Usec, Bear, Savage }
internal enum EquipmentSlot { Backpack }
internal sealed class Profile { internal Info Info = new Info(); internal Inventory Inventory = new Inventory(); }
internal sealed class Info { internal EPlayerSide Side; }
internal sealed class Inventory { internal Equipment Equipment = new Equipment(); }
internal sealed class Equipment
{
    internal readonly Slot Backpack = new Slot();
    internal readonly object OtherKit = new object();
    internal Slot GetSlot(EquipmentSlot slot) => Backpack;
}
internal static class Singleton<T> where T : new() { internal static T Instance = new T(); }
internal static class MongoID { private static int next; internal static string Generate(bool _) => "fresh-" + ++next; }
internal interface IOperationResult { void RollBack(); }
internal sealed class Result
{
    internal bool Failed;
    internal string Error = "Injected native rejection";
    internal IOperationResult Value;
}
internal class Item
{
    internal string Id;
    internal int Width = 1, Height = 1;
    internal object Parent;
    internal Size CalculateCellSize() => new Size { X = Width, Y = Height };
}
internal sealed class Size { internal int X, Y; }
internal sealed class Backpack : Item { internal Grid[] Grids; }
internal sealed class Grid
{
    internal int GridWidth, GridHeight;
    internal bool Reject;
    internal readonly List<Item> Items = new List<Item>();
    internal Address FindLocationForItem(Item item) => Items.Count < GridWidth * GridHeight ? new Address { Grid = this } : null;
}
internal sealed class Address
{
    internal Grid Grid;
    internal Result Add(Item item, bool simulate)
    {
        if (simulate) throw new Exception("Setup must place real profile items");
        if (Grid.Reject) return new Result { Failed = true };
        Grid.Items.Add(item); item.Parent = Grid;
        return new Result();
    }
}
internal sealed class Slot
{
    internal Item ContainedItem;
    internal bool RejectAdd, RejectRemove;
    internal Result Add(Item item, bool simulate)
    {
        if (simulate || ContainedItem != null || RejectAdd) return new Result { Failed = true };
        ContainedItem = item; item.Parent = this;
        return new Result();
    }
    internal Result RemoveItem()
    {
        if (RejectRemove) return new Result { Failed = true };
        var old = ContainedItem; ContainedItem = null; old.Parent = null;
        return new Result { Value = new Removal(this, old) };
    }
    private sealed class Removal : IOperationResult
    {
        private readonly Slot slot;
        private readonly Item item;
        internal Removal(Slot slot, Item item) { this.slot = slot; this.item = item; }
        public void RollBack() { slot.ContainedItem = item; item.Parent = slot; }
    }
}
namespace EFT
{
    internal sealed class ItemFactory
    {
        internal bool WrongBag, BadCargo, RejectCargo;
        internal int Width = 5, Height = 6, Calls;
        internal Item CreateItem(string id, string template, object diff)
        {
            Calls++;
            if (template == "5b44c6ae86f7742d1627baea")
                return WrongBag ? new Item { Id = id } : new Backpack { Id = id,
                    Grids = new[] { new Grid { GridWidth = Width, GridHeight = Height, Reject = RejectCargo } } };
            if (template != "57347c5b245977448d35f6e1") throw new Exception("Unexpected test template");
            return new Item { Id = id, Width = BadCargo ? 2 : 1 };
        }
    }
}
internal static class Fixture
{
    /* BACKPACK SETUP */
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { checks++; return; }
        throw new Exception(message);
    }
    public static void Main()
    {
        var allIds = new HashSet<string>();
        foreach (EPlayerSide side in new[] { EPlayerSide.Usec, EPlayerSide.Bear })
        foreach (bool hasBackpack in new[] { false, true })
        foreach (bool resized in new[] { false, true })
        {
            var factory = Singleton<EFT.ItemFactory>.Instance = new EFT.ItemFactory { Width = resized ? 7 : 5, Height = resized ? 8 : 6 };
            var profile = new Profile(); profile.Info.Side = side;
            var slot = profile.Inventory.Equipment.Backpack;
            var old = new Item { Id = "generated-bag" };
            if (hasBackpack) slot.Add(old, false);
            var kit = profile.Inventory.Equipment.OtherKit;
            int count = PrepareDebugPmcBackpack(profile);
            var bag = slot.ContainedItem as Backpack;
            Check(bag != null && count == factory.Width * factory.Height, "PMC gets a completely filled Beta 2 at actual grid size");
            Check(bag.Grids.Sum(g => g.Items.Count) == count && bag.Grids.All(g => g.Items.All(i => ReferenceEquals(i.Parent, g))), "All filler is loose, addressed cargo");
            Check(ReferenceEquals(kit, profile.Inventory.Equipment.OtherKit), "Other generated equipment is untouched");
            Check(!hasBackpack || old.Parent == null, "Old generated bag is detached before native admission");
            foreach (Item item in bag.Grids.SelectMany(g => g.Items).Concat(new[] { bag }))
                Check(allIds.Add(item.Id), "Repeated debug spawns use unique item IDs");
        }
        var scavFactory = Singleton<EFT.ItemFactory>.Instance = new EFT.ItemFactory();
        var scav = new Profile(); scav.Info.Side = EPlayerSide.Savage;
        var scavBag = new Item(); scav.Inventory.Equipment.Backpack.Add(scavBag, false);
        Check(PrepareDebugPmcBackpack(scav) == 0 && scavFactory.Calls == 0 && ReferenceEquals(scavBag, scav.Inventory.Equipment.Backpack.ContainedItem), "Scavs keep their entire generated inventory");
        Check(PrepareDebugPmcBackpack(null) == 0 && PrepareDebugPmcBackpack(new Profile { Info = null }) == 0, "Missing non-PMC identity does not construct items");
        foreach (string failure in new[] { "bag", "zeroGrid", "cargoSize", "cargoAdd", "slotAdd", "slotRemove", "inventory" })
        {
            var factory = Singleton<EFT.ItemFactory>.Instance = new EFT.ItemFactory { WrongBag = failure == "bag", Width = failure == "zeroGrid" ? 0 : 5,
                BadCargo = failure == "cargoSize", RejectCargo = failure == "cargoAdd" };
            var profile = new Profile(); profile.Info.Side = EPlayerSide.Usec;
            var slot = profile.Inventory.Equipment.Backpack; var original = new Item(); slot.Add(original, false);
            slot.RejectAdd = failure == "slotAdd"; slot.RejectRemove = failure == "slotRemove";
            if (failure == "inventory") profile.Inventory = null;
            Throws(() => PrepareDebugPmcBackpack(profile), "Invalid test setup must fail instead of spawning a random kit");
            Check(ReferenceEquals(slot.ContainedItem, original) && ReferenceEquals(original.Parent, slot), "Failed setup preserves original generated bag");
        }
        Console.WriteLine("PASS: " + checks + " production Debug PMC backpack setup checks; not a live spawn/transfer test.");
    }
}
