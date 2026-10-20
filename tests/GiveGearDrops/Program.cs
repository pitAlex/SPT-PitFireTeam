using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.Modules;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main()
    {
        foreach (var fault in new[] { "none", "prefab-first", "prefab-second", "create-first", "create-second", "partial-second", "destroy-observer" })
        {
            var world = new GameWorld { Failure = fault }; Singleton<GameWorld>.Instance = world;
            var factory = new ObjectsFactory { Failure = fault }; Singleton<ObjectsFactory>.Instance = factory;
            var bot = new Player(); var origin = new Container();
            Item first = new Item("first"), second = new Item("second"); origin.Add(first); origin.Add(second);
            try
            {
                var batch = GiveGearWorldDrops.Prepare(bot, new[] { first, second });
                Check(fault == "none", "Fault must stop drop preparation");
                Check(origin.Items.Count == 0 && world.LootList.Count == 2, "Each overflow root has exactly one prepared world owner");
                Check(world.LootList.Cast<LootItem>().All(l => !l.gameObject.Active) && world.Publications == 0,
                    "Prepared drops stay inactive with no throw callbacks before commit");
                Check(first.CurrentAddress.Owner != origin && second.CurrentAddress.Owner != origin, "World ownership replaces temporary escrow ownership");
                batch.Commit();
                Check(world.LootList.Cast<LootItem>().All(l => l.gameObject.Active) && world.Publications == 2,
                    "Commit exposes each actual world item once");
                Check(bot.InventoryController.Events == 4 && origin.Items.Count == 0, "Native removal events publish without recreating escrow items");
                batch.Commit();
                Check(bot.InventoryController.Events == 4 && world.Publications == 2, "Repeated Commit does not republish ownership events");
                bool denied = false;
                try { batch.Rollback(); } catch (InvalidOperationException) { denied = true; }
                Check(denied && origin.Items.Count == 0 && world.LootList.Count == 2, "Committed loot cannot be restored into a second owner");
            }
            catch (InvalidOperationException)
            {
                Check(fault != "none", "Successful path must not throw");
                Check(origin.Items.SequenceEqual(new[] { first, second }) || origin.Items.ToHashSet().SetEquals(new[] { first, second }),
                    "Preparation failure restores every exact original item");
                Check(first.CurrentAddress.Owner == origin && second.CurrentAddress.Owner == origin, "Failed world owners cannot leave addresses attached to dead loot");
                Check(world.LootList.Count == 0 && world.ItemOwners.Count == 0 && world.LootItems.Count == 0,
                    "Rollback clears every native world registry, including partial initialization");
                Check(world.Publications == 0 && bot.InventoryController.Events == 0, "Failed preparation emits no committed inventory/throw events");
            }
        }
        Console.WriteLine($"PASS: {checks} production Give Gear world-drop preparation/rollback checks.");
    }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace UnityEngine
{
    public struct Vector3 { public static Vector3 up => new(); public static Vector3 operator +(Vector3 a, Vector3 b) => new(); public static Vector3 operator *(Vector3 a, float n) => new(); }
    public struct Quaternion { public static Quaternion identity => new(); }
    public class BoxCollider { }
    public class Transform { public void SetPositionAndRotation(Vector3 _, Quaternion rotation) { } }
    public class RigidBody { public Vector3 velocity; }
    public class GameObject
    {
        public bool Active; public LootItem Loot;
        public void SetActive(bool active) => Active = active;
        public T GetComponent<T>() where T : class => Loot as T;
    }
    public static class Object { public static void Destroy(GameObject value) => value.SetActive(false); }
}
namespace EFT.InventoryLogic
{
    public class ItemAddress { public object Owner; }
    public class Item { public string Id, ShortName; public ItemAddress CurrentAddress; public Item(string id) { Id = ShortName = id; } }
    public class Container
    {
        public List<Item> Items = new();
        public void Add(Item item) { if (item.CurrentAddress != null) throw new Exception("Already owned"); Items.Add(item); item.CurrentAddress = new() { Owner = this }; }
    }
    public class ItemController { public ItemController(Item item, string id, string name, bool localize) { item.CurrentAddress = new() { Owner = this }; } }
    public class InventoryController { public int Events; }
    public enum CommandStatus { Begin, Succeed }
    public interface IOperationResult { void RollBack(); void RaiseEvents(InventoryController controller, CommandStatus status); }
    public interface IPossibleDestroyResult { bool ItemsDestroyRequired { get; } }
    public class DiscardResult : IOperationResult
    {
        public Container From; public Item Item;
        public void RollBack() => From.Add(Item);
        public void RaiseEvents(InventoryController controller, CommandStatus _) => controller.Events++;
    }
    public class Result { public bool Failed; public object Error; public DiscardResult Value; }
    public static class ItemManipulator
    {
        public static Result Discard(Item item, InventoryController _, bool simulate)
        {
            var from = (Container)item.CurrentAddress.Owner;
            from.Items.Remove(item); item.CurrentAddress = null;
            return new() { Value = new() { From = from, Item = item } };
        }
    }
}
namespace EFT
{
    public class Player
    {
        public bool Destroyed; public Vector3 Position; public InventoryController InventoryController = new();
        public static bool GetVisibleToCamera(Player _) => true;
    }
    public class ObjectsFactory
    {
        public string Failure;
        public GameObject CreateLootPrefab(Item item, bool visible, object unused)
        {
            if (Failure == "prefab-" + item.Id) return null;
            return new();
        }
    }
    public class GameWorld
    {
        public string Failure; public List<object> LootList = new(); public HashSet<object> ItemOwners = new(); public Dictionary<int, LootItem> LootItems = new();
        public int Publications;
        public Action<LootItem> OnThrowItem;
        public GameWorld() { OnThrowItem = _ => Publications++; }
        public LootItem CreateLootWithRigidbody(GameObject prefab, Item item, string name, bool random, object valid, out BoxCollider collider,
            bool sync, bool validate, float delay)
        {
            collider = new();
            if (Failure == "create-" + item.Id || Failure == "destroy-observer" && item.Id == "second") throw new InvalidOperationException("Creation failed");
            var loot = new LootItem { gameObject = prefab, ItemOwner = item.CurrentAddress.Owner, NetId = item.Id == "first" ? 1 : 2 };
            prefab.Loot = loot; LootList.Add(loot); ItemOwners.Add(loot.ItemOwner); LootItems.Add(loot.NetId, loot);
            if (Failure == "partial-" + item.Id) throw new InvalidOperationException("Failed after registration");
            return loot;
        }
        public void DestroyLoot(LootItem loot)
        {
            if (Failure == "destroy-observer") throw new InvalidOperationException("Observer failed");
            LootList.Remove(loot); ItemOwners.Remove(loot.ItemOwner); LootItems.Remove(loot.NetId); loot.Kill();
        }
    }
}
namespace EFT.Interactive
{
    public class LootItem
    {
        public GameObject gameObject; public object ItemOwner; public int NetId; public Player LastOwner;
        public Transform transform = new(); public RigidBody RigidBody = new();
        public void CacheParameters() { } public int GetNetId() => NetId; public void Kill() => gameObject.SetActive(false);
    }
}
namespace pitTeam.Modules { public static class Logger { public static void LogError(object _) { } } }
