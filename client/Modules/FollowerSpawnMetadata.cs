using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace pitTeam.Modules
{
    // Validate the wire snapshot before deserialization can substitute default values.
    internal static class FollowerSpawnMetadata
    {
        internal static Dictionary<string, JObject> Read(string json, IEnumerable<string> accounts, bool requireSettings)
        {
            var root = JToken.Parse(json);
            var body = root as JObject;
            if (body?.GetValue("err", StringComparison.OrdinalIgnoreCase)?.Value<int>() is int error && error != 0)
                throw new InvalidOperationException("Follower metadata request was rejected");
            var rows = root as JArray ?? body?.GetValue("data", StringComparison.OrdinalIgnoreCase) as JArray
                ?? throw new InvalidOperationException("Follower metadata array is missing");
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (string account in accounts.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                var matches = rows.OfType<JObject>().Where(row => Text(row, "Aid") == account).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Missing or duplicate follower metadata: " + account);
                var row = matches[0];
                if (string.IsNullOrWhiteSpace(Text(row, "Role"))) throw new InvalidOperationException("Follower role is missing: " + account);
                if (requireSettings)
                {
                    if (string.IsNullOrWhiteSpace(Text(row, "Tactic"))) throw new InvalidOperationException("Follower tactic is missing: " + account);
                    Number(row, "Aggression", 100f);
                    Number(row, "SelfDetermination", 100f);
                    var proficiency = row.GetValue("Proficiency", StringComparison.OrdinalIgnoreCase) as JObject
                        ?? throw new InvalidOperationException("Follower proficiency is missing: " + account);
                    foreach (string key in new[] { "VisionDistance", "VisionSpeed", "Accuracy", "AimSpeed" }) Number(proficiency, key, 200f);
                }
                result.Add(account, row);
            }
            return result;
        }

        internal static string Text(JObject row, string key)
        {
            var value = row.GetValue(key, StringComparison.OrdinalIgnoreCase);
            return value?.Type == JTokenType.String ? value.Value<string>() : null;
        }

        private static void Number(JObject row, string key, float maximum)
        {
            var value = row.GetValue(key, StringComparison.OrdinalIgnoreCase);
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                throw new InvalidOperationException("Follower setting is missing: " + key);
            double number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0 || number > maximum)
                throw new InvalidOperationException("Follower setting is invalid: " + key);
        }
    }
}
