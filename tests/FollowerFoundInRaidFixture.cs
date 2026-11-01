using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using pitTeam.Patches;

namespace EFT.InventoryLogic
{
    public enum EquipmentSlot { Dogtag }
    public class Item
    {
        public string Id;
        public bool SpawnedInSession;
        public List<Item> Children = new List<Item>();
    }
    public class Slot { public Item ContainedItem; }
    public class InventoryEquipment
    {
        public Item Backpack, Weapon;
        public Slot Dogtag = new Slot();
        public Slot GetSlot(EquipmentSlot slot) => Dogtag;
    }
    public class Inventory { public InventoryEquipment Equipment = new InventoryEquipment(); }
}
namespace EFT
{
    public class Profile { public Inventory Inventory = new Inventory(); }
    public class BotOwner { public Profile Profile = new Profile(); public bool Pending, Registered; }
}
namespace pitTeam.Modules
{
    public static class BossPlayers
    {
        // Models the existing pending-spawn admission as well as registered followers.
        public static bool IsFollower(BotOwner bot) => bot.Pending || bot.Registered;
    }
}
namespace SPT.Reflection.Patching
{
    public class PatchPrefix : Attribute { }
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static Type TypeByName(string name) => typeof(SPT.Custom.CustomAI.PmcFoundInRaidEquipment);
        public static MethodInfo Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters);
    }
}
namespace SPT.Custom.CustomAI
{
    public class PmcFoundInRaidEquipment
    {
        public void ConfigurePMCFindInRaidStatus(BotOwner bot) { }
    }
}
static class FollowerFoundInRaidFixture
{
    static int checks;
    static readonly MethodInfo Prefix = typeof(FollowerFoundInRaidPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static bool Run(BotOwner bot) => (bool)Prefix.Invoke(null, new object[] { bot });
    static Item Cargo(string id, bool fir) => new Item { Id = id, SpawnedInSession = fir };
    static void Main()
    {
        Check(Run(null), "null actor does not claim native ownership");
        Check(Run(new BotOwner { Profile = null }), "invalid profile leaves native behavior unchanged");
        foreach (bool pending in new[] { false, true })
        foreach (bool registered in new[] { false, true })
        foreach (bool fir in new[] { false, true })
        {
            var bot = new BotOwner { Pending = pending, Registered = registered };
            var equipment = bot.Profile.Inventory.Equipment;
            equipment.Backpack = Cargo("backpack", fir);
            equipment.Weapon = Cargo("primary", fir);
            equipment.Dogtag.ContainedItem = Cargo("tag", false);
            var item = Cargo("stash-cargo", fir);
            var nestedBag = Cargo("nested-bag", !fir);
            var nested = Cargo("nested-cargo", fir);
            nestedBag.Children.Add(nested);
            equipment.Backpack.Children.Add(item);
            equipment.Backpack.Children.Add(nestedBag);
            bool nativeRuns = Run(bot);
            Check(nativeRuns == !(pending || registered), "only Core-owned follower activations skip SPT rewrite");
            Check(item.SpawnedInSession == fir && nested.SpawnedInSession == fir && nestedBag.SpawnedInSession == !fir,
                "stash/nested cargo flags preserved, including pre-registration");
            Check(equipment.Backpack.SpawnedInSession == fir && equipment.Weapon.SpawnedInSession == fir,
                "worn equipment flags remain intact");
            Check(equipment.Dogtag.ContainedItem.SpawnedInSession == (pending || registered), "SPT dogtag rule retained only for intercepted actors");
            Check(ReferenceEquals(equipment.Backpack.Children[0], item) && equipment.Backpack.Children.Count == 2,
                "no items cloned, moved or removed");
        }

        var transit = new BotOwner { Pending = true };
        transit.Profile.Inventory.Equipment.Backpack = Cargo("bag", false);
        var brought = Cargo("brought-in", false);
        var found = Cargo("looted-before-transit", true);
        transit.Profile.Inventory.Equipment.Backpack.Children.Add(brought);
        transit.Profile.Inventory.Equipment.Backpack.Children.Add(found);
        Check(!Run(transit) && !brought.SpawnedInSession && found.SpawnedInSession,
            "transit retains mixed provenance without promoting or clearing cargo");
        var newlyFound = Cargo("looted-after-spawn", true);
        transit.Profile.Inventory.Equipment.Backpack.Children.Add(newlyFound);
        Check(newlyFound.SpawnedInSession, "no ongoing inventory flag normalization");
        Check(!Run(transit) && newlyFound.SpawnedInSession, "repeat brain activation preserves genuine acquired loot");

        var missingTag = new BotOwner { Pending = true };
        missingTag.Profile.Inventory.Equipment.Dogtag = null;
        Check(!Run(missingTag), "tag-less follower still preserves inventory");
        var target = (MethodInfo)typeof(FollowerFoundInRaidPatch).GetMethod("GetTargetMethod", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(new FollowerFoundInRaidPatch(), null);
        Check(target.Name == "ConfigurePMCFindInRaidStatus" && target.GetParameters()[0].ParameterType == typeof(BotOwner),
            "production patch selects only the SPT FIR routine");
        Console.WriteLine("Follower FIR: " + checks + " production-prefix checks passed (controlled native doubles).");
    }
}
