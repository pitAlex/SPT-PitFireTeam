using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pitTeam.Modules
{
    /// <summary>Failure-only, bounded comparison of the exact serialized Apply snapshots.</summary>
    internal static class GearSwapSnapshotDiff
    {
        internal static IEnumerable<string> Describe(string actual, string expected)
        {
            if (actual == expected) return Array.Empty<string>();
            JToken live = JToken.Parse(actual), draft = JToken.Parse(expected);
            if (JToken.DeepEquals(live, draft))
                return new[] { "parsedValuesEqual=True serializedTextDiffers=True" };
            var differences = new List<string>();
            Compare(live, draft, "$", differences);
            if (differences.Count > 24)
                differences[24] = "additionalDifferencesOmitted=True";
            return differences;
        }

        private static void Compare(JToken actual, JToken expected, string path, List<string> differences)
        {
            if (differences.Count >= 25 || JToken.DeepEquals(actual, expected)) return;
            if (actual is JObject a && expected is JObject e)
            {
                foreach (string name in a.Properties().Select(p => p.Name).Union(e.Properties().Select(p => p.Name)))
                {
                    Compare(a[name], e[name], path + "." + name, differences);
                    if (differences.Count >= 25) break;
                }
                return;
            }
            if (actual is JArray aa && expected is JArray ea)
            {
                for (int i = 0; i < Math.Max(aa.Count, ea.Count) && differences.Count < 25; i++)
                    Compare(i < aa.Count ? aa[i] : null, i < ea.Count ? ea[i] : null,
                        path + "[" + i + "]", differences);
                return;
            }
            differences.Add($"path='{path}' actualItem={Identity(actual)} expectedItem={Identity(expected)} " +
                $"actual={Value(actual)} expected={Value(expected)}");
        }

        private static string Identity(JToken token)
        {
            for (JToken current = token; current != null; current = current.Parent)
            {
                if (!(current is JObject obj)) continue;
                JObject item = obj["Item"] as JObject ?? obj;
                if (item["Id"] != null && item["TemplateId"] != null)
                    return Value(item["Id"]) + "/" + Value(item["TemplateId"]);
            }
            return "none";
        }

        private static string Value(JToken token)
        {
            if (token == null) return "<missing>";
            string value = token.ToString(Formatting.None);
            return value.Length <= 384 ? value : value.Substring(0, 384) + "...";
        }
    }
}
