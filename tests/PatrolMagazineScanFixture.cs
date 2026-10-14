using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using pitTeam.Modules;

namespace EFT.InventoryLogic
{
    public class Item { }
    public class Magazine : Item
    {
        public int Count, MaxCount = 30;
        public bool Installed, Compatible = true;
    }
    public class Slot { }
    public class Weapon : Item
    {
        public Slot MagazineSlot = new Slot();
        public Slot GetMagazineSlot() => MagazineSlot;
    }
    public class InventoryController
    {
        public readonly List<Magazine> Items = new List<Magazine>();
        public int Reserve = 60, Checks, Filled;
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
    public class Player { public InventoryController InventoryController = new InventoryController(); }
    public class ReloadState { public bool Reloading; }
    public class WeaponManager { public bool IsWeaponReady = true; public ReloadState Reload = new ReloadState(); }
    public class BotOwner { public Player GetPlayer = new Player(); public WeaponManager WeaponManager = new WeaponManager(); }
}
namespace UnityEngine { public static class Time { public static float time = 100; } }
namespace pitTeam.Modules
{
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
        return PatrolMagazineScanChecks.FillSpares(owner);
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
    private static bool IsInstalledInWeaponTree(Item item) => ((Magazine)item).Installed;
    public static bool FillSpares(BotOwner owner) => TryFillReachableSpareMagazines(owner, new Weapon());
    private static bool TryFillMagazineWithLooseAmmo(BotOwner owner, Magazine magazine)
    {
        var inventory = owner.GetPlayer.InventoryController;
        int rounds = Math.Min(magazine.MaxCount - magazine.Count, inventory.Reserve);
        if (rounds <= 0) return false;
        inventory.Reserve -= rounds;
        magazine.Count += rounds;
        inventory.Filled++;
        return true;
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
        completion.Tick();
        Check(removed.Count == 30 && reloadInventory.Reserve == 574, "Settled reload must fill the removed magazine from reserve");
        Check(inserted.Count == 30 && FollowerOutOfCombatReloadPolicy.Passes == 1, "Completion performs one fill pass and preserves the inserted magazine");
        Check(!completion.reloadingInProgress && completion.Processed == 1 && completion.Cleared == 1 && completion.Returns == 1, "Existing completion, attempt cleanup and return-to-slot must run once");
        Check(completion.nextReloadCheckAt == 102 && completion.nextMagazineFillCheckAt == 130, "Existing completion cooldowns must be retained");
        completion.Tick();
        Check(FollowerOutOfCombatReloadPolicy.Passes == 1 && reloadInventory.Reserve == 574, "Later patrol ticks must not repeat completion refill or consume more reserve");
        return checks;
    }
}
