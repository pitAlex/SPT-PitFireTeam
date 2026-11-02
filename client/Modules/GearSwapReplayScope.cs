using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pitTeam.Modules
{
    /// <summary>Post-replay state required by the accepted edits, independent of unrelated clone state.</summary>
    internal sealed class GearSwapReplayScope
    {
        private readonly HashSet<string> _items = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _containers = new HashSet<string>(StringComparer.Ordinal);

        // Capture sources before each live operation; later edits may move an item again.
        // Destination parents include temporary escrow, swaps and newly created split items.
        internal void Include(IEnumerable<string> items, IEnumerable<string> destinations, Func<string, string> parent)
        {
            foreach (string id in items)
            {
                _items.Add(id);
                string source = parent(id);
                if (source != null) _containers.Add(source);
            }
            _containers.UnionWith(destinations);
        }

        internal string Snapshot(Func<string, string> itemState, Func<string, string> address)
        {
            return new JObject
            {
                ["Items"] = new JArray(_items.OrderBy(id => id, StringComparer.Ordinal).Select(id => new JObject
                {
                    ["Id"] = id,
                    ["Address"] = address(id),
                    // Changed/moved items retain their entire subtree, not only identity.
                    ["State"] = JToken.Parse(itemState(id))
                })),
                ["Containers"] = new JArray(_containers.OrderBy(id => id, StringComparer.Ordinal).Select(id =>
                    Layout(itemState(id))))
            }.ToString(Formatting.None);
        }

        private static JObject Layout(string serialized)
        {
            var item = JObject.Parse(serialized);
            var layout = new JObject { ["Id"] = item["Id"], ["TemplateId"] = item["TemplateId"] };
            foreach (string name in new[] { "Slots", "StackSlots", "Grids" })
                if (item.Property(name) != null) layout[name] = item[name].DeepClone();
            // Container membership, slot numbers, cartridge order and grid coordinates are
            // relevant; state inside an untouched sibling item is not part of this exchange.
            foreach (JObject descendant in layout.Descendants().OfType<JObject>().Where(obj =>
                obj["Id"] != null && obj["TemplateId"] != null).ToArray())
            {
                if (descendant.Parent != null)
                    descendant.Replace(new JObject { ["Id"] = descendant["Id"] });
            }
            return layout;
        }
    }
}
