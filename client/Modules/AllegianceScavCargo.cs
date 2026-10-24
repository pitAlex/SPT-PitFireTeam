using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace pitTeam.Modules
{
    // Field Scavs never own a saved teammate profile. Return commanded loot carried
    // by living, current companions when their human leader successfully extracts.
    internal static class AllegianceScavCargo
    {
        private sealed class Cargo
        {
            internal BotOwner Bot;
            internal readonly HashSet<string> StartingIds = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> GivenIds = new HashSet<string>(StringComparer.Ordinal);
            internal readonly Dictionary<string, int> GivenUnits = new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly Dictionary<string, int> NativeUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        private static readonly Dictionary<string, Cargo> Recruits = new Dictionary<string, Cargo>(StringComparer.Ordinal);
        private static bool captured;

        internal static void Reset() { Recruits.Clear(); captured = false; }

        internal static void OnRecruited(BotOwner bot, IPlayer leader)
        {
            var human = Singleton<GameWorld>.Instance?.MainPlayer;
            // Player Scavs use native recruitment, not the PMC-only encounter lottery.
            if (!AllegiancePmcFriendship.IsRegularScav(bot) || human == null ||
                leader?.IsYourPlayer != true || leader.ProfileId != human.ProfileId ||
                (human.Side != EPlayerSide.Savage && (!GameplayModeRuntime.IsAllegiance ||
                    !AllegiancePmcFriendship.IsSelectedFriendly(bot, leader))) ||
                Recruits.ContainsKey(bot.ProfileId)) return;
            var follower = BossPlayers.GetFollowerByProfileId(bot.ProfileId);
            if (follower?.IsSquadMate != false || follower.GetBoss()?.realPlayer?.ProfileId != leader.ProfileId) return;
            var cargo = new Cargo { Bot = bot };
            foreach (var item in Inventory(bot)) cargo.StartingIds.Add(item.Id);
            Recruits.Add(bot.ProfileId, cargo);
        }

        private static IEnumerable<Item> Inventory(BotOwner bot)
        {
            var equipment = bot?.GetPlayer?.InventoryController?.Inventory?.Equipment;
            return equipment == null ? Enumerable.Empty<Item>() : equipment.GetAllItems();
        }

        internal static void TrackGiven(BotOwner bot, Item item)
        {
            if (bot == null || item == null || !Recruits.TryGetValue(bot.ProfileId, out var cargo)) return;
            foreach (var child in new[] { item }.Concat(item.GetAllItems()))
                if (!cargo.StartingIds.Contains(child.Id) && cargo.GivenIds.Add(child.Id))
                {
                    cargo.GivenUnits[child.Id] = child.StackObjectsCount;
                    cargo.NativeUnits[child.Id] = 0;
                }
        }

        // Give Gear may split ammunition while planning a magazine package. Preserve the
        // source ownership rather than treating every newly generated cartridge ID as loot.
        internal static IEnumerable<string> GetPureGivenIds(BotOwner bot) => bot != null &&
            Recruits.TryGetValue(bot.ProfileId, out var cargo) ? cargo.GivenIds.Where(id =>
                cargo.NativeUnits.TryGetValue(id, out int native) && native == 0).ToArray() : new string[0];

        internal static bool HasMixedStack(BotOwner bot, Item item) => bot != null && item != null &&
            Recruits.TryGetValue(bot.ProfileId, out var cargo) &&
            cargo.GivenUnits.TryGetValue(item.Id, out int given) && given > 0 &&
            cargo.NativeUnits.TryGetValue(item.Id, out int native) && native > 0;

        // Native split children and borrowed Restricted kit are usable but never return cargo.
        internal static void RegisterNonReturnableIds(BotOwner bot, IEnumerable<string> ids)
        {
            if (bot == null || !Recruits.TryGetValue(bot.ProfileId, out var cargo)) return;
            var excluded = ids.ToArray();
            cargo.StartingIds.UnionWith(excluded);
            RemoveGiven(bot, excluded);
        }

        // Stack merges keep the recipient's existing ID. Record only the added units,
        // including a gift merged into the Scav's native stack; never return that native share.
        internal static void TrackStackChange(BotOwner bot, Item item, int before)
        {
            if (bot == null || item == null || !Recruits.TryGetValue(bot.ProfileId, out var cargo)) return;
            int delta = item.StackObjectsCount - before;
            if (!cargo.GivenUnits.TryGetValue(item.Id, out int given))
            {
                if (delta <= 0) return;
                cargo.NativeUnits[item.Id] = before;
                cargo.GivenIds.Add(item.Id);
            }
            cargo.GivenUnits[item.Id] = Math.Max(0, given + delta);
            if (delta < -given) cargo.NativeUnits[item.Id] = Math.Max(0, cargo.NativeUnits[item.Id] + delta + given);
        }

        internal static void RemoveGiven(BotOwner bot, IEnumerable<string> ids)
        {
            if (bot != null && Recruits.TryGetValue(bot.ProfileId, out var cargo))
                foreach (string id in ids) { cargo.GivenIds.Remove(id); cargo.GivenUnits.Remove(id); cargo.NativeUnits.Remove(id); }
        }

        internal static Dictionary<string, int> CaptureCommandedLoot(BotOwner bot)
        {
            if (bot == null || !Recruits.ContainsKey(bot.ProfileId)) return null;
            return Inventory(bot).ToDictionary(item => item.Id, item => item.StackObjectsCount, StringComparer.Ordinal);
        }

        internal static void TrackMagazineRefill(BotOwner bot, Item magazine, Dictionary<string, int> before, bool givenSource)
        {
            if (before == null || magazine == null || bot == null || !Recruits.TryGetValue(bot.ProfileId, out var cargo)) return;
            if (givenSource) { TrackCommandedLoot(bot, magazine, before); return; }
            foreach (Item item in magazine.GetAllItems())
            {
                if (!before.TryGetValue(item.Id, out int units))
                { RegisterNonReturnableIds(bot, new[] { (string)item.Id }); continue; }
                if (cargo.GivenUnits.ContainsKey(item.Id))
                    cargo.NativeUnits[item.Id] += Math.Max(0, item.StackObjectsCount - units);
            }
        }

        internal static void TrackCommandedLoot(BotOwner bot, Item item, Dictionary<string, int> before)
        {
            if (bot == null || item == null || !Recruits.TryGetValue(bot.ProfileId, out var cargo)) return;
            var live = Inventory(bot).ToDictionary(value => value.Id, StringComparer.Ordinal);
            foreach (var child in new[] { item }.Concat(item.GetAllItems()).GroupBy(value => value.Id).Select(group => group.First()))
            {
                if (!live.TryGetValue(child.Id, out var carried)) continue;
                if (before != null && before.TryGetValue(child.Id, out int units))
                    TrackStackChange(bot, carried, units);
                else if (!cargo.StartingIds.Contains(child.Id) && cargo.GivenIds.Add(child.Id))
                {
                    cargo.GivenUnits[child.Id] = carried.StackObjectsCount;
                    cargo.NativeUnits[child.Id] = 0;
                }
            }
        }

        private static bool IsCurrentCompanion(Cargo cargo, string profileId)
        {
            var bot = cargo.Bot;
            if (bot == null || bot.IsDead || bot.GetPlayer?.HealthController?.IsAlive != true) return false;
            var follower = BossPlayers.GetFollowerByProfileId(bot.ProfileId);
            return follower?.IsSquadMate == false && follower.GetBoss()?.realPlayer?.ProfileId == profileId;
        }

        internal static void EndRaid(string profileId, ExitStatus status, string exitName)
        {
            var world = Singleton<GameWorld>.Instance;
            var human = world?.MainPlayer;
            if (captured || human == null || (human.Side != EPlayerSide.Savage && !GameplayModeRuntime.IsAllegiance) ||
                AllegianceScavRelations.TrustBroken || human.ProfileId != profileId || human.HealthController?.IsAlive != true ||
                (status != ExitStatus.Survived && status != ExitStatus.Runner && status != ExitStatus.Transit) ||
                string.IsNullOrWhiteSpace(exitName)) return;
            captured = true;

            // Include an inspection still open at extraction, before follower/UI teardown.
            TeammateBackpackInspection.TrackBackpackItemChanges();
            var present = Recruits.Values.Where(c => IsCurrentCompanion(c, profileId)).ToArray();
            if (present.Length == 0) return;

            // Serialize the real inventory, then filter its flat copy. Never detach or mutate
            // live items and never include a Scav's kit or items carried out by the player.
            var allItems = new JArray();
            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
            var converters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = converters });
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cargo in present)
            {
                var roots = cargo.Bot.GetPlayer.InventoryController.Inventory.Equipment;
                var live = Inventory(cargo.Bot).ToDictionary(item => item.Id, StringComparer.Ordinal);
                var flat = Singleton<ItemFactory>.Instance.TreeToFlatItems(new Item[] { roots });
                foreach (var token in JArray.FromObject(flat, serializer))
                {
                    string id = token["_id"]?.Value<string>();
                    if (id == null || !cargo.GivenIds.Contains(id) || !live.TryGetValue(id, out var item) ||
                        InteractableObjects.IsProtectedFollowerEquipment(item)) continue;
                    int units = Math.Min(cargo.GivenUnits[id], Math.Max(0, item.StackObjectsCount - cargo.NativeUnits[id]));
                    if (units <= 0 || !ids.Add(id)) continue;
                    var upd = token["upd"] as JObject ?? new JObject();
                    upd["StackObjectsCount"] = units;
                    token["upd"] = upd;
                    allItems.Add(token);
                }
            }
            string serverId = FollowerInsuranceRaidReports.CurrentServerId;
            if (string.IsNullOrWhiteSpace(serverId)) throw new InvalidOperationException("Raid identity unavailable for Scav cargo.");
            var packet = new JObject
            {
                ["serverId"] = serverId, ["exitName"] = exitName,
                // Recruited Scavs stay behind at transit; settle this map, never respawn them.
                ["transit"] = status == ExitStatus.Transit,
                ["scavProfileIds"] = JArray.FromObject(present.Select(c => c.Bot.ProfileId)),
                ["items"] = allItems
            };
            FollowerInsuranceRaidReports.Prepare(packet.ToString(Formatting.None))
                .Send("/singleplayer/pitfireteam/scav-cargo");
        }
    }
}
