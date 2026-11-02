using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;

static class GearSwapReplayScopeFixture
{
    static int checks;
    static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    static JObject Item(string id) => new JObject { ["Id"] = id, ["TemplateId"] = id + "-tpl", ["StackCount"] = 1, ["SpawnedInSession"] = false };
    static JObject Plate(string id) { var item = Item(id); item["Components"] = new JArray(new JObject { ["Durability"] = 50, ["MaxDurability"] = 50 }); return item; }
    static JObject Slot(int number, JObject item) => new JObject { ["SlotNumber"] = number, ["ContainedItem"] = item.DeepClone() };
    static JObject Grid(JObject item, int x = 0) => new JObject { ["GridNumber"] = 0, ["ContainedItems"] = new JArray(new JObject {
        ["Item"] = item.DeepClone(), ["X"] = x, ["Y"] = 0, ["Rotation"] = 0 }) };
    static string State(Dictionary<string, JObject> items, string id) => GearSwapSnapshot.Normalize(items[id].ToString(Formatting.None));
    static Dictionary<string, JObject> Clone(Dictionary<string, JObject> items)
    {
        var result = new Dictionary<string, JObject>(); foreach (var pair in items) result.Add(pair.Key, (JObject)pair.Value.DeepClone()); return result;
    }
    static string Capture(GearSwapReplayScope scope, Dictionary<string, JObject> items, Dictionary<string, string> locations) =>
        scope.Snapshot(id => State(items, id), id => locations[id]);
    static void Main()
    {
        var plate = Plate("new-plate"); var oldPlate = Plate("old-plate");
        var magazine = Item("M200-mag"); magazine["StackSlots"] = new JArray(new JObject { ["SlotNumber"] = 0, ["ContainedItems"] = new JArray() });
        var weapon = Item("rifle"); weapon["Slots"] = new JArray(Slot(1, magazine));
        var vest = Item("vest"); vest["Slots"] = new JArray(Slot(1, plate));
        var offer = Item("offer"); offer["Grids"] = new JArray(new JObject { ["GridNumber"] = 0, ["ContainedItems"] = new JArray() });
        var overflow = Item("overflow"); overflow["Grids"] = new JArray(Grid(oldPlate));
        var equipment = Item("equipment"); equipment["Slots"] = new JArray(Slot(0, weapon), Slot(1, vest));
        var expected = new Dictionary<string, JObject> { ["new-plate"] = plate, ["old-plate"] = oldPlate, ["vest"] = vest,
            ["offer"] = offer, ["overflow"] = overflow, ["equipment"] = equipment, ["rifle"] = weapon };
        var addresses = new Dictionary<string, string> { ["new-plate"] = "vest:front", ["old-plate"] = "overflow:0,0", ["rifle"] = "equipment:primary" };
        var scope = new GearSwapReplayScope();
        scope.Include(new[] { "old-plate" }, new[] { "overflow" }, _ => "vest");
        scope.Include(new[] { "new-plate" }, new[] { "vest" }, _ => "offer");
        var actual = Clone(expected);
        // The observed mismatch: a magazine cartridge-container array disappears in the draft.
        expected["equipment"]["Slots"][0]["ContainedItem"]["Slots"][0]["ContainedItem"]["StackSlots"] = null;
        expected["rifle"]["Slots"][0]["ContainedItem"]["StackSlots"] = null;
        Check(State(expected,"equipment") != State(actual,"equipment"), "full inventory equality reproduces Birdeye's rejection");
        Check(Capture(scope,expected,addresses) == Capture(scope,actual,addresses), "plate-only accepted edits ignore untouched firearm clone representation");
        var extraSibling = Item("unrelated-mag"); extraSibling["StackSlots"] = new JArray(1,2);
        expected["vest"]["Grids"] = new JArray(Grid(extraSibling)); actual = Clone(expected);
        actual["vest"]["Grids"][0]["ContainedItems"][0]["Item"]["StackSlots"] = null;
        Check(Capture(scope,expected,addresses) == Capture(scope,actual,addresses), "affected vest layout does not compare unrelated mag internals");
        foreach (string property in new[] { "Id", "TemplateId", "StackCount", "SpawnedInSession", "Components" })
        {
            actual = Clone(expected); actual["new-plate"][property] = property == "Components" ? (JToken)new JArray(new JObject { ["Durability"] = 1 }) : new JValue("changed");
            Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "plate data cannot be ignored: " + property);
        }
        foreach (string id in new[] { "new-plate", "old-plate" })
        {
            var moved = new Dictionary<string,string>(addresses); moved[id] = "wrong-owner:wrong-slot";
            Check(Capture(scope,expected,addresses) != Capture(scope,expected,moved), "both incoming and displaced plate addresses validated: " + id);
        }
        actual = Clone(expected); actual["vest"]["Slots"][0]["ContainedItem"] = oldPlate.DeepClone();
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "failed equip rejected even if item data matches");
        actual = Clone(expected); actual["vest"]["Slots"][0]["SlotNumber"] = 2;
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "wrong plate slot rejected");
        actual = Clone(expected); actual["overflow"]["Grids"][0]["ContainedItems"] = new JArray();
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "missing displaced plate rejected");
        actual = Clone(expected); actual["offer"]["Grids"] = new JArray(Grid(plate));
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "plate left in source rejected");
        actual = Clone(expected); actual["overflow"]["Grids"][0]["ContainedItems"][0]["X"] = 2;
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "drop-escrow grid position preserved");
        actual = Clone(expected); actual["vest"]["Grids"][0]["ContainedItems"][0]["Item"]["Id"] = "unexpected-item";
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "untouched sibling identity must remain in affected container");

        // Whole weapons/containers need complete subtree validation when actually exchanged.
        scope.Include(new[] { "rifle" }, new[] { "equipment" }, _ => "equipment");
        actual = Clone(expected); actual["rifle"]["Slots"][0]["ContainedItem"]["StackSlots"] = new JArray(1,2);
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "changed weapon requires its full magazine state");
        actual = Clone(expected); actual["rifle"]["Malfunction"] = new JObject { ["Malfunction"] = 1 };
        Check(Capture(scope,expected,addresses) != Capture(scope,actual,addresses), "exchanged weapon malfunction state still protected");
        var bagScope = new GearSwapReplayScope(); bagScope.Include(new[] { "vest" }, new[] { "equipment" }, _ => "equipment");
        var bagAddress = new Dictionary<string,string> { ["vest"] = "equipment:rig" };
        actual = Clone(expected); actual["vest"]["Grids"][0]["ContainedItems"][0]["Item"]["StackSlots"] = new JArray(2,1);
        Check(Capture(bagScope,expected,bagAddress) != Capture(bagScope,actual,bagAddress), "whole vest exchange protects nested ammunition order");
        var same = new GearSwapReplayScope();
        same.Include(new[] { "new-plate", "old-plate", "new-plate" }, new[] { "vest", "overflow", "offer" }, id => id == "old-plate" ? "vest" : "offer");
        var plateScope = new GearSwapReplayScope();
        plateScope.Include(new[] { "old-plate", "new-plate" }, new[] { "overflow", "vest", "offer" }, id => id == "old-plate" ? "vest" : "offer");
        Check(Capture(same,expected,addresses) == Capture(plateScope,expected,addresses), "scope is deterministic across repeated moves and unordered edits");
        bool missing = false;
        try { var broken = Clone(expected); broken.Remove("old-plate"); Capture(plateScope,broken,addresses); } catch(KeyNotFoundException) { missing = true; }
        Check(missing, "missing affected item fails closed");
        actual = Clone(expected);
        var replayAddresses = new Dictionary<string,string>(addresses);
        bool rolledBack = false;
        try
        {
            GearSwapTransaction.Execute(new Func<Action>[] { () =>
            {
                replayAddresses["new-plate"] = "wrong-slot";
                return () => { replayAddresses["new-plate"] = addresses["new-plate"]; rolledBack = true; };
            } }, undo => undo(), () =>
            {
                if (Capture(plateScope,expected,addresses) != Capture(plateScope,actual,replayAddresses))
                    throw new InvalidOperationException("Wrong plate placement");
            });
        }
        catch (InvalidOperationException) { }
        Check(rolledBack && replayAddresses["new-plate"] == addresses["new-plate"], "scope validation failure triggers the existing transaction rollback");
        Console.WriteLine("Gear Swap replay scope: " + checks + " assertions passed.");
    }
}
