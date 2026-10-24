using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;

internal static class GearSwapItemCloneChecks
{
    public static int Main()
    {
        int checks = 0;
        Action<bool, string> check = (value, label) => { if (!value) throw new Exception(label); checks++; };
        var source = new Item { State = JObject.Parse(@"{
            'Id':'weapon', 'TemplateId':'rifle', 'StackCount':1, 'SpawnedInSession':true,
            'ActiveCamora':2, 'IsUnderBarrelDeviceActive':true,
            'Malfunction':{'LastMalfunctionTime':12.5,'PlayersWhoKnowMalfType':['player']},
            'ShellsInWeapon':[{'TemplateId':'spent-shell','Index':1}],
            'Components':[{'Durability':37.5}],
            'Slots':[{'SlotNumber':0,'ContainedItem':{'Id':'mag','StackCount':1,
                'StackSlots':[{'ContainedItems':[{'Id':'rounds','StackCount':17}]}]}}]
        }") };
        var clone = TeammateGearSwap.CloneForTest(source);
        check(ItemBinarySerializer.Calls == 1, "Use the shared native descriptor serialization boundary");
        check(ReferenceEquals(ItemBinarySerializer.Search, FullySearchedSearchController.Instance), "Use the same search view as snapshots");
        check(ItemDescriptor.EmptyMaps == 1, "Deserialize with a fresh private identity map");
        check(!ReferenceEquals(source, clone) && !ReferenceEquals(source.State, clone.State), "Clone does not share the live item or descriptor state");
        foreach (string path in new[] { "Id", "TemplateId", "StackCount", "SpawnedInSession", "ActiveCamora",
            "IsUnderBarrelDeviceActive", "Malfunction", "ShellsInWeapon", "Components", "Slots" })
            check(JToken.DeepEquals(source.State[path], clone.State[path]), "Preserve descriptor field: " + path);
        clone.State["Malfunction"]["LastMalfunctionTime"] = 99;
        clone.State["Slots"][0]["ContainedItem"]["Id"] = "changed";
        check((double)source.State["Malfunction"]["LastMalfunctionTime"] == 12.5 &&
            (string)source.State["Slots"][0]["ContainedItem"]["Id"] == "mag", "Draft changes cannot alter source descendants/runtime state");
        var second = TeammateGearSwap.CloneForTest(source);
        check(ItemDescriptor.EmptyMaps == 2 && JToken.DeepEquals(source.State, second.State), "Repeated clones have independent identity maps and no earlier draft changes");
        Console.WriteLine("Gear Swap descriptor clone: " + checks + " assertions passed (controlled serializer doubles, not native roundtrip qualification).");
        return 0;
    }
}

namespace pitTeam.Modules
{
    internal static class TeammateGearSwap
    {
        /* ITEM CLONE */
        internal static Item CloneForTest(Item source) => Clone(source);
    }
}
namespace EFT.InventoryLogic
{
    public sealed class Item { public JObject State; }
}
namespace EFT
{
    public struct MongoID { }
    public sealed class FullySearchedSearchController { public static readonly FullySearchedSearchController Instance = new FullySearchedSearchController(); }
    public sealed class ItemDescriptor
    {
        public JObject State;
        public static int EmptyMaps;
        public Item Deserialize(Dictionary<MongoID, Item> items)
        {
            if (items == null || items.Count != 0) throw new Exception("Identity map must be fresh and private.");
            EmptyMaps++;
            items.Add(new MongoID(), new Item());
            return new Item { State = (JObject)State.DeepClone() };
        }
    }
    public static class ItemBinarySerializer
    {
        public static int Calls;
        public static FullySearchedSearchController Search;
        public static ItemDescriptor SerializeItem(Item item, FullySearchedSearchController search)
        {
            Calls++; Search = search;
            return new ItemDescriptor { State = (JObject)item.State.DeepClone() };
        }
    }
}
