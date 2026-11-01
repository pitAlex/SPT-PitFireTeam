#nullable enable annotations
using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using pitTeam.Modules;
using Modules = pitTeam.Modules;

namespace EFT.InventoryLogic
{
    public class Item
    {
        public string Id = Guid.NewGuid().ToString(), TemplateId = "nativeTemplate";
        public Address Parent = new Address();
        public List<Item> ParentItems = new List<Item>();
        public virtual IEnumerable<Item> GetAllParentItems() => ParentItems;
    }
    public class Container { public Item ParentItem; }
    public class Address { public Container Container = new Container(); }
    public class Filter { public bool CheckItemFilter(Ammo ammo) => true; }
    public class Cartridges { public Filter Filters = new Filter(); }
    public class Ammo : Item
    {
        public InventoryController Inventory;
        public int StackObjectsCount => Inventory.Reserve;
        public ApplyResult CheckAction(object action) => new ApplyResult();
    }
    public class Magazine : Item
    {
        public int Count, MaxCount = 30;
        public bool Installed, Compatible = true;
        public Cartridges Cartridges = new Cartridges();
        public override IEnumerable<Item> GetAllParentItems()
        {
            if (Installed) yield return new Weapon();
            foreach (Item parent in ParentItems) yield return parent;
        }
        public ApplyResult Apply(InventoryController inventory, Ammo ammo, int rounds, bool simulate)
        {
            inventory.Applies++;
            if (Installed) throw new Exception("Packing an installed magazine is illegal");
            return new ApplyResult { Magazine = this, Ammo = ammo, Rounds = rounds };
        }
    }
    public class Slot { public Item ContainedItem; }
    public enum EquipmentSlot { FirstPrimaryWeapon, SecondPrimaryWeapon, Holster }
    public class Equipment
    {
        public Slot Primary = new Slot();
        public Slot GetSlot(EquipmentSlot slot) => slot == EquipmentSlot.FirstPrimaryWeapon ? Primary : new Slot();
    }
    public class Weapon : Item
    {
        public enum EReloadMode { ExternalMagazine, InternalMagazine }
        public EReloadMode ReloadMode;
        public Magazine Inserted;
        public Slot MagazineSlot = new Slot();
        public Slot GetMagazineSlot() => MagazineSlot;
        public Magazine GetCurrentMagazine() => Inserted;
    }
    public class ApplyResult
    {
        public bool Failed; public bool Succeeded => !Failed; public object Error;
        public Magazine Magazine; public Ammo Ammo; public int Rounds;
    }
    public class InventoryController
    {
        public readonly List<Magazine> Items = new List<Magazine>();
        public int Reserve = 60, Checks, Filled, Applies;
        public InventoryController Inventory => this;
        public Equipment Equipment = new Equipment();
        public bool Examined(Item item) => true;
        public void GetAcceptableItemsNonAlloc<T>(object slots, List<T> result, Predicate<T> predicate, object ignored) where T : Item
        {
            T ammo = new Ammo { Inventory = this } as T;
            if (ammo != null && predicate(ammo)) result.Add(ammo);
        }
        public void TryRunNetworkTransaction(ApplyResult result, Comfort.Common.Callback callback)
        {
            if (result.Magazine.Installed) throw new Exception("Magazine was inserted before transaction");
            if (result.Rounds > Reserve) throw new Exception("Refill invented ammunition");
            Reserve -= result.Rounds; result.Magazine.Count += result.Rounds; Filled++;
            callback(new Comfort.Common.Completion { Succeed = true });
        }
        // EFT walks the live container.Items collection before invoking the predicate.
        public void GetReachableItemsOfTypeNonAlloc<T>(IList<T> result, Predicate<T> predicate) where T : Item
        {
            foreach (Magazine item in Items)
            {
                T typed = item as T;
                if (typed != null && (predicate == null || predicate(typed))) result.Add(typed);
            }
        }
    }
    public struct Result { public bool Succeeded; }
    public static class ItemManipulator
    {
        public static Result CheckMoveIgnoringTargetItem(Item item, Slot slot, InventoryController controller)
        {
            var magazine = (Magazine)item;
            controller.Checks++;
            // Native simulated Move removes and restores the candidate in its source grid.
            // Rollback preserves contents but changes the live collection's version.
            controller.Items.Remove(magazine);
            controller.Items.Add(magazine);
            return new Result { Succeeded = magazine.Compatible };
        }
    }
}
namespace EFT
{
    public class Player
    {
        public InventoryController InventoryController = new InventoryController();
        public class FirearmController { public bool ReloadOperation; public bool IsInReloadOperation() => ReloadOperation; }
    }
    public class ReloadState { public bool Reloading; }
    public class WeaponManager { public bool IsWeaponReady = true; public ReloadState Reload = new ReloadState(); public Player.FirearmController ShootController = new Player.FirearmController(); }
    public class BotOwner { public Player GetPlayer = new Player(); public WeaponManager WeaponManager = new WeaponManager(); }
}
public class BotReload { public static object _availableEquipmentSlots = new object(); }
namespace Comfort.Common
{
    public class Completion { public bool Succeed; }
    public delegate void Callback(Completion completion);
}
namespace UnityEngine { public static class Time { public static float time = 100; } }
namespace pitTeam.Modules
{
    public class FollowerWeaponSupplyBudget
    {
        public bool Owns(Weapon weapon, Magazine magazine) => true;
        public int Fill(Magazine magazine, Ammo ammo) => Math.Min(magazine.MaxCount - magazine.Count, ammo.StackObjectsCount);
    }
    public class TeammateGearSwap { public static TeammateGearSwap Current; public BotOwner Bot; }
    public static class InteractableObjects { public static bool IsLootedWeapon(BotOwner bot, Weapon weapon) => false; }
    public static class AllegianceScavCargo
    {
        public static object CaptureCommandedLoot(BotOwner bot) => new object();
        public static HashSet<string> GetPureGivenIds(BotOwner bot) => new HashSet<string>();
        public static void TrackMagazineRefill(BotOwner bot, Magazine mag, object before, bool given) { }
    }
    public static class Logger { public static void LogError(string message) { throw new Exception(message); } }
    public static class BattleRecorder
    {
        public static void RecordAmmoRefill(BotOwner owner, string stage, string reason, Func<object> details) { }
    }
}
public static class FollowerOutOfCombatReloadPolicy
{
    public static int Passes;
    public static bool TryFillCarriedWeaponMagazines(BotOwner owner)
    {
        Passes++;
        return PatrolMagazineScanChecks.FillCarried(owner);
    }
}
public class ReloadCompletionChecks
{
    private const float OutOfCombatReloadSlotCooldown = 2, OutOfCombatReloadFullCycleCooldown = 30;
    public BotOwner BotOwner = new BotOwner();
    public bool reloadingInProgress = true;
    private string reloadingWeaponId = "originalRifle";
    public float nextReloadCheckAt, nextMagazineFillCheckAt;
    public int Processed, Cleared, Returns;
    private void MarkReloadWeaponProcessed(string id) { Processed++; }
    private void ClearOutOfCombatReloadAttemptBudget(string id) { Cleared++; }
    private void TryCompleteReturnAfterTopOffSwitch(object selector) { Returns++; }
    public void Tick()
    {
        var selector = new object();
        /* RELOAD_COMPLETION */
    }
}
public static class PatrolMagazineScanChecks
{
    /* SPARE_FILL */
    /* CARRIED_FILL */
    /* REFILL */
    /* CAN_TOP_OFF */
    private static readonly EquipmentSlot[] WeaponSlotsToTopOff = { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster };
    private static bool IsLauncherWeapon(Weapon weapon) => false;
    private static int GetCurrentLoadedCount(Weapon weapon) => weapon.Inserted?.Count ?? 0;
    private static int GetCurrentCapacity(Weapon weapon, Magazine magazine) => magazine?.MaxCount ?? 0;
    private static bool HasCompatibleLooseAmmo(BotOwner bot, Weapon weapon, Magazine magazine) => bot.GetPlayer.InventoryController.Reserve > 0;
    private static bool HasBetterMagazine(BotOwner bot, Weapon weapon) => bot.GetPlayer.InventoryController.Items.Exists(mag => !mag.Installed && mag.Compatible && mag.Count > GetCurrentLoadedCount(weapon));
    private static FollowerWeaponSupplyBudget BuildRefillBudget(BotOwner bot) => new FollowerWeaponSupplyBudget();
    /* INSTALLED */
    public static bool FillSpares(BotOwner owner) => TryFillReachableSpareMagazines(owner, new Weapon());
    public static bool FillCarried(BotOwner owner)
    {
        var inventory = owner.GetPlayer.InventoryController;
        if (inventory.Equipment.Primary.ContainedItem == null) inventory.Equipment.Primary.ContainedItem = new Weapon();
        return TryFillCarriedWeaponMagazines(owner);
    }
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    public static int Run()
    {
        Check(!TryFillReachableSpareMagazines(null, new Weapon()), "Missing bot must be skipped");
        var owner = new BotOwner();
        var inventory = owner.GetPlayer.InventoryController;
        Check(!TryFillReachableSpareMagazines(owner, null), "Missing weapon must be skipped");
        Check(!TryFillReachableSpareMagazines(owner, new Weapon { MagazineSlot = null }), "Missing magazine slot must be skipped");
        Check(!TryFillReachableSpareMagazines(owner, new Weapon()), "Empty inventory must be skipped");
        var partial = new Magazine { Count = 11 };
        var empty = new Magazine();
        var incompatible = new Magazine { Count = 1, Compatible = false };
        var full = new Magazine { Count = 30 };
        var installed = new Magazine { Count = 5, Installed = true };
        inventory.Items.AddRange(new[] { partial, incompatible, full, installed, empty });
        Check(TryFillReachableSpareMagazines(owner, new Weapon()), "Compatible spares should fill despite simulated move rollback");
        Check(partial.Count == 30 && empty.Count == 30, "Every compatible spare must be processed");
        Check(inventory.Reserve == 11 && inventory.Filled == 2, "Only missing rounds must be consumed");
        Check(incompatible.Count == 1 && full.Count == 30 && installed.Count == 5, "Incompatible, full and installed magazines must remain unchanged");
        Check(inventory.Checks == 3 && inventory.Items.Count == 5, "Compatibility runs only for loose partial candidates and preserves inventory contents");
        inventory.Reserve = 0;
        partial.Count = 22;
        Check(!TryFillReachableSpareMagazines(owner, new Weapon()) && partial.Count == 22, "No reserve must not invent rounds");
        inventory.Reserve = 3;
        Check(TryFillReachableSpareMagazines(owner, new Weapon()) && partial.Count == 25 && inventory.Reserve == 0, "Limited reserve must partially fill a magazine");

        var completion = new ReloadCompletionChecks();
        var removed = new Magazine { Count = 12 };
        var inserted = new Magazine { Count = 30, Installed = true };
        var reloadInventory = completion.BotOwner.GetPlayer.InventoryController;
        reloadInventory.Reserve = 592;
        reloadInventory.Items.AddRange(new[] { inserted, removed });
        completion.BotOwner.WeaponManager.Reload.Reloading = true;
        completion.Tick();
        Check(FollowerOutOfCombatReloadPolicy.Passes == 0 && completion.Processed == 0, "Active reload must retain ownership without filling");
        completion.BotOwner.WeaponManager.Reload.Reloading = false;
        completion.BotOwner.WeaponManager.IsWeaponReady = false;
        completion.Tick();
        Check(FollowerOutOfCombatReloadPolicy.Passes == 0 && removed.Count == 12, "Unsettled hands must not refill the removed magazine");
        completion.BotOwner.WeaponManager.IsWeaponReady = true;
        completion.BotOwner.WeaponManager.ShootController.ReloadOperation = true;
        completion.Tick();
        Check(FollowerOutOfCombatReloadPolicy.Passes == 0 && completion.Processed == 0, "Native watchdog clearing Reloading must not finish a live hands reload or start inventory packing");
        completion.BotOwner.WeaponManager.ShootController.ReloadOperation = false;
        completion.Tick();
        Check(removed.Count == 30 && reloadInventory.Reserve == 574, "Settled reload must fill the removed magazine from reserve");
        Check(inserted.Count == 30 && FollowerOutOfCombatReloadPolicy.Passes == 1, "Completion performs one fill pass and preserves the inserted magazine");
        Check(!completion.reloadingInProgress && completion.Processed == 1 && completion.Cleared == 1 && completion.Returns == 1, "Existing completion, attempt cleanup and return-to-slot must run once");
        Check(completion.nextReloadCheckAt == 102 && completion.nextMagazineFillCheckAt == 130, "Existing completion cooldowns must be retained");
        completion.Tick();
        Check(FollowerOutOfCombatReloadPolicy.Passes == 1 && reloadInventory.Reserve == 574, "Later patrol ticks must not repeat completion refill or consume more reserve");

        var rifle = new Weapon { Inserted = new Magazine { Installed = true, Count = 0 } };
        var guarded = new BotOwner(); var guardInventory = guarded.GetPlayer.InventoryController;
        guardInventory.Equipment.Primary.ContainedItem = rifle;
        guardInventory.Items.Add(rifle.Inserted);
        Check(!TryFillMagazineWithLooseAmmo(guarded, rifle.Inserted, rifle), "Direct native refill callbacks cannot pack an inserted magazine");
        var nested = new Magazine(); nested.ParentItems.Add(new Item()); nested.ParentItems.Add(rifle);
        Check(!TryFillMagazineWithLooseAmmo(guarded, nested, rifle), "A magazine nested anywhere under a weapon remains protected");
        Check(!TryFillCarriedWeaponMagazines(guarded) && guardInventory.Applies == 0 && guardInventory.Reserve == 60,
            "Carried-weapon pass never submits an installed magazine to Apply");
        Check(!CanTopOffWeapon(guarded, rifle), "Loose ammo without a spare is not an executable detachable-magazine reload");
        rifle.ReloadMode = Weapon.EReloadMode.InternalMagazine;
        Check(CanTopOffWeapon(guarded, rifle), "Internal-feed weapons retain their native loose-ammunition reload");
        rifle.ReloadMode = Weapon.EReloadMode.ExternalMagazine;
        var spare = new Magazine { Count = 7 };
        guardInventory.Items.Add(spare);
        Check(TryFillCarriedWeaponMagazines(guarded) && spare.Count == 30 && rifle.Inserted.Count == 0 && guardInventory.Reserve == 37,
            "An empty rifle stays untouched while its loose spare is prepared with finite ammunition");
        Check(CanTopOffWeapon(guarded, rifle), "A prepared spare enables the ordinary magazine swap");
        var ejected = rifle.Inserted; ejected.Installed = false; spare.Installed = true; rifle.Inserted = spare;
        Check(TryFillCarriedWeaponMagazines(guarded) && ejected.Count == 30 && spare.Count == 30 && guardInventory.Reserve == 7,
            "After native ejection the old magazine can be packed legally without touching the newly inserted spare");
        return checks;
    }
}
