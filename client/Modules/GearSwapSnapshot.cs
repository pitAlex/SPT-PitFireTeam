using System;
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
