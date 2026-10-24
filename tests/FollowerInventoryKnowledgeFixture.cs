using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using JetBrains.Annotations;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using HarmonyLib;

namespace JetBrains.Annotations { public class NotNullAttribute : Attribute { } }
namespace HarmonyLib
{
    public class PatchPostfixAttribute : Attribute { }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name, Type[] args) => type.GetMethod(name, args);
    }
}
namespace SPT.Reflection.Patching { public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); } }
namespace EFT.InventoryLogic
{
    public class Item
    {
        public string TemplateId;
        public ItemAddress Parent;
        public bool ExaminedByDefault;
    }
    public class Ammo : Item { }
    public class InventoryEquipment : Item { }
    public class Container { public Item ParentItem; }
    public class Slot : Container { }
    public class ItemAddress { public Container Container; }
    public class Inventory { public InventoryEquipment Equipment = new InventoryEquipment(); }
    public class Profile
    {
        public string ProfileId;
        public Dictionary<string, bool> Encyclopedia = new Dictionary<string, bool>();
    }
    public class ItemController { public virtual bool Examined(Item item) => false; }
    public class InventoryController : ItemController
    {
        public Profile Profile = new Profile();
        public Inventory Inventory = new Inventory();
        public bool UsePatch = true;
        public override bool Examined(Item item)
        {
            bool result = item.ExaminedByDefault || Profile.Encyclopedia.ContainsKey(item.TemplateId);
            if (UsePatch) pitTeam.Patches.TeammateBackpackExaminedPatch.PatchPostfix(this, item, ref result);
            return result;
        }
    }
    public class Error { }
    public class CannotApplyItemError : Error { public CannotApplyItemError(Item item, Item target) { } }
    public class ItemNotExaminedError : Error { public ItemNotExaminedError(Item item) { } }
    public class FilterError : Error { }
    public struct OperationResult
    {
        public Error Error;
        public bool Succeeded => Error == null;
        public static implicit operator OperationResult(Error error) => new OperationResult { Error = error };
    }
    public class Magazine : Item
    {
        public bool Compatible = true;
        public class LoadInsertedMagazineError : Error { public LoadInsertedMagazineError(Magazine mag, Ammo ammo) { } }
        /* NATIVE_APPLY */
        public OperationResult ApplyWithoutRestrictions(ItemController controller, Ammo ammo, int count, bool simulate)
        {
            return Compatible ? new OperationResult() : new OperationResult { Error = new FilterError() };
        }
    }
}
namespace pitTeam.Modules
{
    public class Player { public bool IsAI = true; public InventoryController InventoryController = new InventoryController(); }
    public class Bot { public Player GetPlayer = new Player(); }
    public class Follower { public Bot Bot = new Bot(); public Bot GetBot() => Bot; }
    public static class BossPlayers
    {
        public static Dictionary<string, Follower> Followers = new Dictionary<string, Follower>();
        public static Follower GetFollowerByProfileId(string id) => id != null && Followers.TryGetValue(id, out var follower) ? follower : null;
    }
    public static class TeammateBackpackInspection
    {
        public static bool Active;
        public static bool ShouldTreatItemExamined(InventoryController controller, Item item) => Active;
    }
    public static class TeammateCorpseLootGuard
    {
        public static bool Active;
        public static bool ShouldTreatItemExamined(InventoryController controller, Item item) => Active;
    }
    public static class TeammateGearSwap
    {
        public static bool Active;
        public static bool TreatCommitItemKnown(Item item) => Active;
    }
}
/* KNOWLEDGE_HELPER */
namespace pitTeam.Patches { /* EXAMINATION_PATCH */ }
public static class FollowerInventoryKnowledgeChecks
{
    private static int count;
    private static void Check(bool result, string message) { count++; if (!result) throw new Exception(message); }
    private static void Carry(InventoryController inventory, Item item)
    {
        item.Parent = new ItemAddress { Container = new Container { ParentItem = inventory.Inventory.Equipment } };
    }
    public static int Run()
    {
        var follower = new Follower();
        var inventory = follower.Bot.GetPlayer.InventoryController;
        inventory.Profile.ProfileId = "Volk";
        BossPlayers.Followers.Add("Volk", follower);
        var ammo = new Ammo { TemplateId = "Igolnik" };
        var magazine = new Magazine { TemplateId = "PMAG", ExaminedByDefault = true };
        Carry(inventory, ammo);
        Carry(inventory, magazine);
        inventory.UsePatch = false;
        Check(magazine.ApplyItem(inventory, ammo, 22, true).Error is ItemNotExaminedError, "Native guard must reproduce Volk's unexamined reserve rejection");
        inventory.UsePatch = true;
        Check(magazine.ApplyItem(inventory, ammo, 22, true).Succeeded, "Follower examination must admit the same own-ammo transfer");
        Check(inventory.Profile.Encyclopedia.Count == 0, "Follower examination must not mutate encyclopedia state");
        magazine.Parent.Container = new Slot { ParentItem = inventory.Inventory.Equipment };
        Check(magazine.ApplyItem(inventory, ammo, 22, true).Error is Magazine.LoadInsertedMagazineError, "Installed-magazine restriction must remain native");
        Carry(inventory, magazine);
        magazine.Compatible = false;
        Check(magazine.ApplyItem(inventory, ammo, 22, true).Error is FilterError, "Ammo compatibility rejection must remain intact");
        var acquired = new Item { TemplateId = "newItem" };
        Check(!FollowerInventoryKnowledge.ShouldTreatItemExamined(inventory, acquired), "Detached/world items must stay unknown");
        Carry(inventory, acquired);
        Check(inventory.Examined(acquired), "Newly acquired non-ammo items must immediately be examined");
        var nested = new Ammo { TemplateId = "nestedAmmo", Parent = new ItemAddress { Container = new Container { ParentItem = acquired } } };
        Check(inventory.Examined(nested), "Nested carried contents must be examined");
        acquired.Parent = null;
        Check(!inventory.Examined(acquired) && !inventory.Examined(nested), "Leaving the follower equipment tree must end the override");
        var ordinary = new InventoryController();
        ordinary.Profile.ProfileId = "ordinaryBot";
        var foreign = new Item { TemplateId = "foreignItem" };
        Carry(ordinary, foreign);
        Check(!inventory.Examined(foreign), "Other inventories must not become known to a follower");
        Check(!ordinary.Examined(foreign), "Ordinary bot examination must remain unchanged");
        var human = new InventoryController();
        human.Profile.ProfileId = "Volk";
        var humanItem = new Item { TemplateId = "humanItem" };
        Carry(human, humanItem);
        Check(!human.Examined(humanItem) && human.Profile.Encyclopedia.Count == 0, "A human/menu controller sharing the profile ID must remain unchanged");
        follower.Bot.GetPlayer.IsAI = false;
        Check(!inventory.Examined(ammo), "A human must never receive the follower override");
        follower.Bot.GetPlayer.IsAI = true;
        Check(!FollowerInventoryKnowledge.ShouldTreatItemExamined(null, ammo) && !FollowerInventoryKnowledge.ShouldTreatItemExamined(inventory, null), "Missing contexts must preserve native results");
        BossPlayers.Followers.Clear();
        Check(!inventory.Examined(ammo), "Dismissed followers must lose the override");
        ammo.ExaminedByDefault = true;
        Check(inventory.Examined(ammo), "Native known results must be preserved");
        ammo.ExaminedByDefault = false;
        TeammateBackpackInspection.Active = true;
        Check(human.Examined(humanItem), "Existing backpack UI examination must remain functional");
        TeammateBackpackInspection.Active = false;
        TeammateCorpseLootGuard.Active = true;
        Check(human.Examined(humanItem), "Existing corpse examination must remain functional");
        TeammateCorpseLootGuard.Active = false;
        TeammateGearSwap.Active = true;
        Check(human.Examined(humanItem), "Existing Swap Gear examination must remain functional");
        return count;
    }
}
