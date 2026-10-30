using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;

internal static class GearSwapSnapshotChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }
    private static JObject Grid(params JObject[] items) => new JObject
    {
        ["GridNumber"] = 0,
        ["ContainedItems"] = new JArray(items)
    };
    private static JObject Entry(string id, int x, int y) => new JObject
    {
        ["Item"] = new JObject { ["Id"] = id, ["TemplateId"] = id + "-template", ["StackCount"] = 1,
            ["Components"] = new JArray(new JObject { ["Durability"] = 80 }), ["SpawnedInSession"] = false },
        ["X"] = x, ["Y"] = y, ["Rotation"] = 0,
        ["Location"] = new JObject { ["x"] = x, ["y"] = y, ["r"] = 0 }
    };
    private static string Canonical(JToken token) => GearSwapSnapshot.Normalize(token.ToString(Formatting.None));
    private static void Invalid(JToken token, string message)
    {
        bool rejected = false;
        try { Canonical(token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }
    private static void CheckGridComparison()
    {
        // Same failure shape as DocYen's helmet/headset exchange: native dictionary
        // holes put the removed helmet before unrelated ammo/dogtags, unlike the clone.
        var original = new JObject { ["Id"] = "backpack", ["Grids"] = new JArray(
            Grid(Entry("removed-helmet", 1, 3), Entry("ammo", 1, 0), Entry("dogtag", 2, 9))) };
        var reordered = (JObject)original.DeepClone();
        var entries = (JArray)reordered["Grids"][0]["ContainedItems"];
        reordered["Grids"][0]["ContainedItems"] = new JArray(entries.Reverse());
        string originalJson = original.ToString(Formatting.None);
        Check(originalJson != reordered.ToString(Formatting.None), "Reproduces raw replay snapshot mismatch");
        Check(Canonical(original) == Canonical(reordered), "Grid enumeration order does not change inventory state");
        Check(original.ToString(Formatting.None) == originalJson, "Normalization does not mutate its source");
        Check(GearSwapSnapshot.Normalize(Canonical(original)) == Canonical(original), "Normalization is idempotent");

        foreach (string path in new[] { "Item.Id", "Item.TemplateId", "Item.StackCount", "Item.SpawnedInSession",
            "Item.Components[0].Durability", "X", "Y", "Rotation", "Location.x", "Location.y", "Location.r" })
        {
            var changed = (JObject)reordered.DeepClone();
            JToken field = changed["Grids"][0]["ContainedItems"][0].SelectToken(path);
            field.Replace(field.Type == JTokenType.String ? (JToken)new JValue("different-item") :
                field.Type == JTokenType.Boolean ? new JValue(true) : new JValue(99));
            Check(Canonical(original) != Canonical(changed), "Retains real state difference: " + path);
        }
        var removed = (JObject)reordered.DeepClone();
        ((JArray)removed["Grids"][0]["ContainedItems"]).RemoveAt(0);
        Check(Canonical(original) != Canonical(removed), "Missing items are rejected");
        var added = (JObject)reordered.DeepClone();
        ((JArray)added["Grids"][0]["ContainedItems"]).Add(Entry("extra", 4, 0));
        Check(Canonical(original) != Canonical(added), "Extra items are rejected");

        var nestedA = new JObject { ["Grids"] = new JArray(Grid(new JObject
            { ["Item"] = original, ["X"] = 0, ["Y"] = 0 })) };
        var nestedB = new JObject { ["Grids"] = new JArray(Grid(new JObject
            { ["Item"] = reordered, ["X"] = 0, ["Y"] = 0 })) };
        Check(Canonical(nestedA) == Canonical(nestedB), "Nested backpack grids are normalized");
        nestedB["Grids"][0]["ContainedItems"][0]["X"] = 2;
        Check(Canonical(nestedA) != Canonical(nestedB), "Parent-container position still matters");
        var differentGrid = (JObject)reordered.DeepClone();
        differentGrid["Grids"][0]["GridNumber"] = 1;
        Check(Canonical(original) != Canonical(differentGrid), "Grid identity still matters");

        foreach (string arrayName in new[] { "StackSlots", "Slots", "Components", "ContainedItems" })
        {
            var orderedA = new JObject { [arrayName] = new JArray(Entry("ammo-a", 0, 0), Entry("ammo-b", 0, 0)) };
            var orderedB = new JObject { [arrayName] = new JArray(Entry("ammo-b", 0, 0), Entry("ammo-a", 0, 0)) };
            Check(Canonical(orderedA) != Canonical(orderedB), "Preserves non-grid array order: " + arrayName);
        }
        var duplicates = new JObject { ["Grids"] = new JArray(Grid(Entry("duplicate", 0, 0), Entry("duplicate", 1, 0))) };
        Invalid(duplicates, "Duplicate IDs fail closed rather than being collapsed");
        var missingId = new JObject { ["Grids"] = new JArray(Grid(new JObject { ["Item"] = new JObject() })) };
        Invalid(missingId, "Missing item identity fails closed");
        Check(Canonical(new JObject { ["Grids"] = new JArray(Grid()) }).Contains("\"ContainedItems\":[]"), "Empty grids remain valid");
        var nullField = new JObject { ["Grids"] = null };
        Check(Canonical(nullField) != Canonical(new JObject()), "Null and absent fields remain distinct");
    }
    public static int Main()
    {
        try
        {
            CheckGridComparison();
            CheckDifferenceDiagnostics();
            Console.WriteLine("Gear Swap snapshot normalization: " + checks + " assertions passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckDifferenceDiagnostics()
    {
        var before = new JObject { ["Id"]="knight-equipment", ["Slots"]=new JArray(new JObject
        { ["SlotNumber"]=3, ["ContainedItem"]=new JObject { ["Id"]="vest", ["Slots"]=new JArray(new JObject
          { ["ContainedItem"]=new JObject { ["Id"]="plate", ["Components"]=new JArray(new JObject { ["Durability"]=40 }) } }) } }) };
        string expected=before.ToString(Formatting.None);
        var changed=(JObject)before.DeepClone();changed.SelectToken("Slots[0].ContainedItem.Slots[0].ContainedItem.Components[0].Durability").Replace(39);
        string actual=changed.ToString(Formatting.None);
        var differences=GearSwapSnapshot.DescribeDifferences(expected,actual);
        Check(differences.Length==1&&differences[0].Contains("expectedItem=plate actualItem=plate")&&
            differences[0].Contains("/Components/0/Durability")&&differences[0].Contains("expected=40 actual=39"),
            "Replay diagnostics identify nested plate, exact field and both values");
        Check(expected!=actual&&Canonical(before)!=Canonical(changed),"Diagnostics do not approve or normalize away a real mismatch");
        Check(before.ToString(Formatting.None)==expected&&changed.ToString(Formatting.None)==actual,"Difference reporting leaves descriptors untouched");
        Check(GearSwapSnapshot.DescribeDifferences(expected,expected).Length==0,"Matching snapshots emit no differences");
        var a=new JObject { ["Id"]="rifle", ["StackSlots"]=new JArray(1,2), ["Malfunction"]=new JObject { ["LastShotTime"]=10 } };
        var b=(JObject)a.DeepClone();b["StackSlots"]=new JArray(2,1);b["Malfunction"]["LastShotTime"]=11;
        Check(GearSwapSnapshot.DescribeDifferences(a.ToString(Formatting.None),b.ToString(Formatting.None)).Length==3,
            "Ordered cartridge and unrelated weapon runtime differences remain visible");
        Check(GearSwapSnapshot.DescribeDifferences(a.ToString(Formatting.None),b.ToString(Formatting.None),1).Length==1,"Difference output obeys its bound");
        Check(GearSwapSnapshot.DescribeDifferences(a.ToString(Formatting.None),b.ToString(Formatting.None),0).Length==1,"A zero requested bound still reports one useful difference");
        var longA=new JObject { ["Id"]="item", ["Value"]=new string('a',1000) };
        var longB=new JObject { ["Id"]="item", ["Value"]=new string('b',1000) };
        Check(GearSwapSnapshot.DescribeDifferences(longA.ToString(),longB.ToString()).Single().Length<430,"Scalar values cannot flood the diagnostic log");
        var reordered=new JObject { ["Slots"]=before["Slots"].DeepClone(), ["Id"]="knight-equipment" };
        Check(GearSwapSnapshot.DescribeDifferences(expected,reordered.ToString(Formatting.None)).Single().Contains("structuredValuesEqual=true"),
            "Text-only serialization differences are distinguished without weakening equality");
        Check(GearSwapSnapshot.DescribeDifferences("{\"Value\":null}","{}").Single().Contains("actual=<missing>"),"Missing and null values remain distinct");
        Check(GearSwapSnapshot.DescribeDifferences("{\"Slots\":[1,2]}","{\"Slots\":[1]}").Single().Contains("/Slots/length"),"Missing item/slot array lengths remain visible");
    }
}
