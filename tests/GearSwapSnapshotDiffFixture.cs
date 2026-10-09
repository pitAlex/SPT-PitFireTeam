using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;

internal static class GearSwapSnapshotDiffChecks
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
            const string actual = "{\"Id\":\"weapon\",\"TemplateId\":\"gun\",\"Components\":[{\"Durability\":80}]}";
            string expected = actual.Replace(":80", ":79");
            var difference = GearSwapSnapshotDiff.Describe(actual, expected).ToArray();
            Check(difference.Length == 1, "One changed field produces one record");
            Check(difference[0].Contains("$.Components[0].Durability"), "Records exact component path");
            Check(difference[0].Contains("actual=80 expected=79"), "Records actual and expected values");
            Check(difference[0].Contains("actualItem=\"weapon\"/\"gun\""), "Records owning item identity");
            Check(!GearSwapSnapshotDiff.Describe(actual, actual).Any(), "Equal snapshots are silent");
            difference = GearSwapSnapshotDiff.Describe("{\"Id\":1,\"StackCount\":2}", "{\"StackCount\":2,\"Id\":1}").ToArray();
            Check(difference.Single().Contains("parsedValuesEqual=True"), "Identifies serialization-only differences");
            difference = GearSwapSnapshotDiff.Describe("{\"X\":null}", "{}").ToArray();
            Check(difference.Single().Contains("actual=null expected=<missing>"), "Distinguishes null from missing field");
            difference = GearSwapSnapshotDiff.Describe("[1,2]", "[2,1]").ToArray();
            Check(difference.Length == 2 && difference[0].Contains("$[0]"), "Array order differences remain visible");
            difference = GearSwapSnapshotDiff.Describe("[1]", "[1,2]").ToArray();
            Check(difference.Single().Contains("actual=<missing> expected=2"), "Reports missing array entries");
            difference = GearSwapSnapshotDiff.Describe("{\"Item\":{\"Id\":\"mag\",\"TemplateId\":\"magazine\"},\"X\":1}",
                "{\"Item\":{\"Id\":\"mag\",\"TemplateId\":\"magazine\"},\"X\":0}").ToArray();
            Check(difference.Single().Contains("actualItem=\"mag\"/\"magazine\""), "Grid location differences identify the contained item");
            difference = GearSwapSnapshotDiff.Describe(JsonConvert.SerializeObject(Enumerable.Repeat(1,100)),
                JsonConvert.SerializeObject(Enumerable.Repeat(2,100))).ToArray();
            Check(difference.Length == 25 && difference.Last().Contains("Omitted=True"), "Bounds mismatch output");
            difference = GearSwapSnapshotDiff.Describe(JsonConvert.SerializeObject(new string('a',1000)), "0").ToArray();
            Check(difference.Single().Length < 600, "Bounds individual values");
            Console.WriteLine("Gear Swap snapshot differences: " + checks + " assertions passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
