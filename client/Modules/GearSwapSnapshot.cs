using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pitTeam.Modules
{
    /// <summary>Canonicalizes descriptor grid enumeration without touching live inventories.</summary>
    internal static class GearSwapSnapshot
    {
        internal static string Normalize(string serialized)
        {
            JToken descriptor = JToken.Parse(serialized);
            NormalizeGrids(descriptor);
            return descriptor.ToString(Formatting.None);
        }

        // Diagnostic only: never canonicalizes or approves a replay. Keep the
        // exact equality guard separate, including ordered slots and cartridges.
        internal static string[] DescribeDifferences(string expected, string actual, int limit = 16)
        {
            var differences = new List<string>();
            Compare(JToken.Parse(expected), JToken.Parse(actual), "$", null, null,
                differences, Math.Max(1, Math.Min(64, limit)));
            if (differences.Count == 0 && expected != actual)
                differences.Add("serializedTextDiffers structuredValuesEqual=true");
            return differences.ToArray();
        }

        private static void Compare(JToken expected, JToken actual, string path, string expectedItem, string actualItem,
            List<string> differences, int limit)
        {
            if (differences.Count >= limit || JToken.DeepEquals(expected, actual)) return;
            if (expected is JObject expectedItemObject) expectedItem = (string)expectedItemObject["Id"] ?? expectedItem;
            if (actual is JObject actualItemObject) actualItem = (string)actualItemObject["Id"] ?? actualItem;
            if (expected is JObject expectedObject && actual is JObject actualObject)
            {
                foreach (string name in expectedObject.Properties().Select(p => p.Name)
                    .Union(actualObject.Properties().Select(p => p.Name)).OrderBy(name => name, StringComparer.Ordinal))
                {
                    Compare(expectedObject[name], actualObject[name], path + "/" + name.Replace("~", "~0").Replace("/", "~1"),
                        expectedItem, actualItem, differences, limit);
                    if (differences.Count >= limit) break;
                }
                return;
            }
            if (expected is JArray expectedArray && actual is JArray actualArray)
            {
                if (expectedArray.Count != actualArray.Count)
                    differences.Add($"path={path}/length expectedItem={expectedItem} actualItem={actualItem} expected={expectedArray.Count} actual={actualArray.Count}");
                for (int i = 0; i < Math.Min(expectedArray.Count, actualArray.Count) && differences.Count < limit; i++)
                    Compare(expectedArray[i], actualArray[i], path + "/" + i, expectedItem, actualItem, differences, limit);
                return;
            }
            differences.Add($"path={path} expectedItem={expectedItem} actualItem={actualItem} expected={DescribeValue(expected)} actual={DescribeValue(actual)}");
        }

        private static string DescribeValue(JToken value)
        {
            string text = value == null ? "<missing>" : value is JContainer ? "<" + value.Type + ">" : value.ToString(Formatting.None);
            return text.Length <= 160 ? text : text.Substring(0, 160) + "...";
        }

        private static void NormalizeGrids(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Name == "Grids" && property.Value is JArray grids)
                    {
                        foreach (JToken grid in grids)
                        {
                            if (!(grid is JObject gridObject) || gridObject["GridNumber"] == null ||
                                !(gridObject["ContainedItems"] is JArray contents))
                                throw new InvalidOperationException("Malformed inventory grid descriptor.");
                            var entries = contents.Select(entry => new
                            {
                                Entry = entry,
                                Id = (entry as JObject)?["Item"]?["Id"]
                            }).ToArray();
                            if (entries.Any(entry => entry.Id?.Type != JTokenType.String || string.IsNullOrEmpty((string)entry.Id)) ||
                                entries.Select(entry => (string)entry.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
                                throw new InvalidOperationException("Missing or duplicate grid item identity.");
                            // Dictionary iteration is not inventory state. Keep every entry's
                            // ID, coordinates, rotation and complete item tree, sorting only
                            // this unordered collection. Cartridge/slot/component arrays stay ordered.
                            gridObject["ContainedItems"] = new JArray(entries
                                .OrderBy(entry => (string)entry.Id, StringComparer.Ordinal).Select(entry => entry.Entry));
                        }
                    }
                    NormalizeGrids(property.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken child in array) NormalizeGrids(child);
            }
        }
    }
}
