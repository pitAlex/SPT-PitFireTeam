using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.Components;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;


namespace pitTeam.Modules
{
    internal class InteractableObjects
    {
        public static InteractableObjects? Instance;

        private Door? _currDoor;
        private Dictionary<string, Door>? _doorsToOpen;

        private LootItem? _lootItem;
        private Vector3? _lootPosition;
        private Components.BotFollowerPlayer? _botToLoot;
        private string? _botToLootProfileId;
        private LootableContainer? _lootContainerTarget;
        private Vector3? _lootContainerPosition;
        private Components.BotFollowerPlayer? _botToContainerLoot;
        private string? _botToContainerLootProfileId;
        private Corpse? _bodyLootTarget;
        private Vector3? _bodyLootPosition;
        private Components.BotFollowerPlayer? _botToBodyLoot;
        private string? _botToBodyLootProfileId;
        private Dictionary<string, LootItem>? _lootItemsByBot;
        private Dictionary<string, Vector3>? _lootPositionsByBot;
        private Dictionary<string, Corpse>? _bodyLootTargetsByBot;
        private Dictionary<string, Vector3>? _bodyLootPositionsByBot;
        private Dictionary<string, LootableContainer>? _lootContainerTargetsByBot;
        private Dictionary<string, Vector3>? _lootContainerPositionsByBot;
        private HashSet<string>? _checkedBodyLootTargetIds;
        private HashSet<string>? _checkedContainerLootTargetIds;

        private bool IsDisposed = false;

        private Dictionary<string, List<string>>? _lootedItems;
        private Dictionary<string, HashSet<string>>? _lootedWeaponIds;
        private Dictionary<string, Dictionary<string, HashSet<string>>>? _lootedWeaponMagazineIds;
        private Dictionary<string, HashSet<string>>? _strictCargoItemIds;
        private List<Item>? _toSendItems;
        private Dictionary<string, Dictionary<string, object>>? _followersWithLoot;

        private Dictionary<string, List<string>>? _followersEquipment;

        private bool _isBossDead = false;
        private static readonly bool EnableBackendItemReturn = true;
        private const string ProtectedRaidItemsRoute = "/singleplayer/pitfireteam/postraid/protected-items";

        private List<Player>? _enemiesSeen;
        private Player? _closestEnemySeen;

        public InteractableObjects()
        {
            if (Instance == null)
            {
                Instance = this;

                _lootedItems = new Dictionary<string, List<string>>();
                _lootedWeaponIds = new Dictionary<string, HashSet<string>>();
                _lootedWeaponMagazineIds = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
                _strictCargoItemIds = new Dictionary<string, HashSet<string>>();
                _toSendItems = new List<Item>();
                _followersWithLoot = new Dictionary<string, Dictionary<string, object>>();
                _doorsToOpen = new Dictionary<string, Door>();

                _enemiesSeen = new List<Player>();

                _followersEquipment = new Dictionary<string, List<string>>();
                _lootItemsByBot = new Dictionary<string, LootItem>(StringComparer.Ordinal);
                _lootPositionsByBot = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                _bodyLootTargetsByBot = new Dictionary<string, Corpse>(StringComparer.Ordinal);
                _bodyLootPositionsByBot = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                _lootContainerTargetsByBot = new Dictionary<string, LootableContainer>(StringComparer.Ordinal);
                _lootContainerPositionsByBot = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                _checkedBodyLootTargetIds = new HashSet<string>(StringComparer.Ordinal);
                _checkedContainerLootTargetIds = new HashSet<string>(StringComparer.Ordinal);

            }

        }
        /** Send items given to followers back to the player */
        private bool SendStoreItems()
        {
            int trackedItemCount = _lootedItems?.Values.Sum(items => items?.Count ?? 0) ?? 0;
            GatherItems();

            if (!EnableBackendItemReturn)
            {
                if (_toSendItems != null && _toSendItems.Count > 0)
                {
                    Logger.LogInfo($"[Loot] BE return disabled. Kept {_toSendItems.Count} tracked follower items local-only.");
                }
                return false;
            }

            if (_toSendItems == null)
            {
                return false;
            }

            if (trackedItemCount > 0 && _toSendItems.Count == 0)
            {
                Logger.LogInfo($"[Loot] Raid-end follower return had {trackedItemCount} tracked item id(s), but no readable return roots were found.");
            }

            Dictionary<string, object>? member = null;
            if (_followersWithLoot != null && _followersWithLoot.Count > 0)
            {
                member = _followersWithLoot.Values.FirstOrDefault();
            }

            // Raid cleanup can unload the request owner immediately after this object is disposed.
            // Send the return payload now so temporary Simple/Restricted gear cannot be stripped
            // from teammate persistence before the mail request has actually reached the server.
            return SendReturnItems(_toSendItems, member, "post-raid returned follower items", synchronous: true);
        }
        /** Gather what items where given to followers and which is still alive to count */
        private void GatherItems()
        {
            var bossPlayers = BossPlayers.Instance.GetBossPlayers();
            if (_toSendItems == null)
            {
                return;
            }

            _toSendItems.Clear();
            List<string> gathered = new List<string>();

            foreach (var player in bossPlayers)
            {
                foreach (var bot in player.Value.Followers)
                {
                    if (!ShouldGatherRaidEndFollowerInventory(bot))
                    {
                        continue;
                    }

                    GatherStoredItemsFromBot(bot, gathered);
                }
            }
        }

        public static void SendDeathEscapeRecoveredGear(IEnumerable<Item> recoveredItems)
        {
            SendReturnItems(recoveredItems, null, "recovered death-escape gear");
        }

        public static List<Item> GetTrackedReturnItemRoots(BotOwner bot)
        {
            List<Item> roots = new List<Item>();
            if (bot?.GetPlayer?.InventoryController == null)
            {
                return roots;
            }

            List<string>? storedItems = GetStoredItems(bot.ProfileId);
            if (storedItems == null || storedItems.Count == 0)
            {
                return roots;
            }

            foreach (string stored in storedItems)
            {
                Item item = FindStoredReturnItem(bot.GetPlayer.InventoryController, stored);
                if (item != null)
                {
                    roots.Add(item);
                }
            }

            // Tracked loot can contain tracked children, for example backpack -> rig -> item.
            // Return/carry simulation must see only the outer recoverable root or it will
            // duplicate nested contents when the mail payload is flattened.
            return RemoveNestedReturnRoots(roots).ToList();
        }

        private void GatherStoredItemsFromBot(BotOwner bot, List<string> gathered)
        {
            if (_toSendItems == null || bot?.GetPlayer?.InventoryController == null)
            {
                return;
            }

            var storedItems = GetStoredItems(bot.ProfileId);
            if (storedItems == null)
            {
                return;
            }

            foreach (var stored in storedItems)
            {
                if (gathered.Contains(stored)) continue;
                Item item = FindStoredReturnItem(bot.GetPlayer.InventoryController, stored);
                if (item == null)
                {
                    Logger.LogInfo(
                        $"[Loot] Could not find tracked follower return item '{stored}' for " +
                        $"'{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' at raid end; botState={bot.BotState}.");
                    continue;
                }

                // If both a container and one of its children are tracked, return the container
                // tree once. Sending overlapping roots to /returnitems duplicates nested rigs/loot.
                if (HasTrackedAncestor(item, storedItems))
                {
                    gathered.Add(stored);
                    continue;
                }

                _toSendItems.Add(item.CloneItem());
                gathered.Add(stored);
            }
        }

        private static Item? FindStoredReturnItem(InventoryController inventoryController, string itemId)
        {
            if (inventoryController?.Inventory?.Equipment == null || string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            foreach (EquipmentSlot slot in GetTrackedReturnSearchSlots())
            {
                Item root = inventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem;
                if (root == null)
                {
                    continue;
                }

                if (root.Id == itemId)
                {
                    return root;
                }

                if (root is CompoundItem compound)
                {
                    foreach (Item child in compound.GetAllItems())
                    {
                        if (child != null && child.Id == itemId)
                        {
                            return child;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<EquipmentSlot> GetTrackedReturnSearchSlots()
        {
            // Weapon-support ammunition can fall back to secure storage and remains an independent
            // tracked return root when it is not consumed by a weapon or magazine.
            yield return EquipmentSlot.SecuredContainer;
            yield return EquipmentSlot.TacticalVest;
            yield return EquipmentSlot.Backpack;
            yield return EquipmentSlot.Pockets;
            yield return EquipmentSlot.FirstPrimaryWeapon;
            yield return EquipmentSlot.SecondPrimaryWeapon;
            yield return EquipmentSlot.Holster;
            yield return EquipmentSlot.ArmorVest;
            yield return EquipmentSlot.Headwear;
            yield return EquipmentSlot.Earpiece;
            yield return EquipmentSlot.FaceCover;
            yield return EquipmentSlot.Eyewear;
        }

        private static IEnumerable<Item> RemoveNestedReturnRoots(IEnumerable<Item> items)
        {
            List<Item> roots = items?.Where(item => item != null).ToList() ?? new List<Item>();
            if (roots.Count <= 1)
            {
                foreach (Item root in roots)
                {
                    yield return root;
                }

                yield break;
            }

            List<HashSet<string>> rootTrees = roots
                .Select(GetItemTreeIds)
                .ToList();

            for (int index = 0; index < roots.Count; index++)
            {
                Item root = roots[index];
                bool coveredByOtherRoot = false;

                for (int otherIndex = 0; otherIndex < roots.Count; otherIndex++)
                {
                    if (index == otherIndex)
                    {
                        continue;
                    }

                    if (string.Equals(roots[otherIndex].Id, root.Id, StringComparison.Ordinal))
                    {
                        coveredByOtherRoot = otherIndex < index;
                        if (coveredByOtherRoot)
                        {
                            break;
                        }

                        continue;
                    }

                    if (rootTrees[otherIndex].Contains(root.Id))
                    {
                        coveredByOtherRoot = true;
                        break;
                    }
                }

                if (!coveredByOtherRoot)
                {
                    yield return root;
                }
            }
        }

        private static bool HasTrackedAncestor(Item item, IEnumerable<string> trackedItemIds)
        {
            if (item == null || trackedItemIds == null)
            {
                return false;
            }

            HashSet<string> tracked = new HashSet<string>(trackedItemIds, StringComparer.Ordinal);
            Item? parent = item.Parent?.Container?.ParentItem;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            while (parent != null)
            {
                if (!visited.Add(parent.Id))
                {
                    return false;
                }

                if (tracked.Contains(parent.Id))
                {
                    return true;
                }

                parent = parent.Parent?.Container?.ParentItem;
            }

            return false;
        }

        private static HashSet<string> GetItemTreeIds(Item item)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (item == null)
            {
                return ids;
            }

            ids.Add(item.Id);
            try
            {
                foreach (Item child in item.GetAllItems())
                {
                    if (child != null)
                    {
                        ids.Add(child.Id);
                    }
                }
            }
            catch
            {
                ids.Add(item.Id);
            }

            return ids;
        }

        // In Simple/Restricted modes teammate gear is lootable during the raid for interaction
        // parity, but those exact item ids must not survive player extraction. The server also
        // derives saved teammate gear from profile JSON; this client route covers live-only
        // movement such as gear handed through the teammate backpack inspection flow.
        private static void RegisterProtectedRaidItemIds(
            IEnumerable<string> itemIds,
            string context,
            bool synchronous = false)
        {
            if (pitFireTeam.IsFollowerLoadoutLootableMode())
            {
                return;
            }

            string[] ids = itemIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            if (ids.Length == 0)
            {
                return;
            }

            string json = JsonConvert.SerializeObject(new
            {
                itemIds = ids,
                context
            });

            void Send()
            {
                try
                {
                    RequestHandler.PostJson(ProtectedRaidItemsRoute, json);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to register protected teammate raid item ids. context='{context}'");
                    Logger.LogError(ex);
                }
            }

            if (synchronous)
            {
                Send();
                return;
            }

            // Spawn equipment is registered early in the raid, so this can be asynchronous.
            // Player-handed items use synchronous registration at the call site to avoid an
            // extraction race if the player leaves immediately after moving the item.
            Task.Run(Send);
        }

        public static void RemoveProtectedRaidItemIds(
            IEnumerable<string> itemIds,
            string context,
            bool synchronous = false)
        {
            if (pitFireTeam.IsFollowerLoadoutLootableMode())
            {
                return;
            }

            string[] ids = itemIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? Array.Empty<string>();
            if (ids.Length == 0)
            {
                return;
            }

            string json = JsonConvert.SerializeObject(new
            {
                removeItemIds = ids,
                context
            });

            void Send()
            {
                try
                {
                    RequestHandler.PostJson(ProtectedRaidItemsRoute, json);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to unregister protected teammate raid item ids. context='{context}'");
                    Logger.LogError(ex);
                }
            }

            if (synchronous)
            {
                Send();
                return;
            }

            Task.Run(Send);
        }

        private static bool SendReturnItems(
            IEnumerable<Item> items,
            Dictionary<string, object>? member,
            string context,
            bool synchronous = false)
        {
            if (!EnableBackendItemReturn || items == null)
            {
                return false;
            }

            try
            {
                List<Item> rootItems = RemoveNestedReturnRoots(items.Where(item => item != null)).ToList();
                if (rootItems.Count == 0)
                {
                    return false;
                }

                foreach (Item root in rootItems)
                {
                    DetachReturnRoot(root);
                }

                JsonType.FlatItem[] flatItems = Singleton<EFT.ItemFactory>.Instance.TreeToFlatItems(rootItems);
                if (flatItems == null || !flatItems.Any())
                {
                    return false;
                }

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
                var defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                string returnItemsJson = new
                {
                    items = flatItems,
                    member,
                }.ToJson(defaultJsonConverters);

                bool Send()
                {
                    try
                    {
                        RequestHandler.PostJson("/singleplayer/returnitems", returnItemsJson);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to send {context}");
                        Logger.LogError(ex);
                        return false;
                    }
                }

                if (synchronous)
                {
                    return Send();
                }

                Task.Run(() => Send());
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to prepare {context}");
                Logger.LogError(ex);
                return false;
            }
        }

        private static void DetachReturnRoot(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                // Return mail accepts a list of root item trees. Recovered items may have been
                // cloned from inside a backpack/rig and still carry the old parent address, which
                // can make nested backpack contents disappear or attach to the wrong mailed root.
                item.CurrentAddress = null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to detach return root '{item.Id}' before mail serialization.");
                Logger.LogError(ex);
            }
        }

        public void Destroy()
        {
            if (IsDisposed) return;


            try
            {
                bool raidTransit = Utils.Utils.FlagGet("RaidTransit");
                if (raidTransit)
                {
                    NpcMessage.SendLostTeammateOutcomes();
                    Logger.LogInfo("[Transit] Skipped post-raid follower item return and escaped loadout persistence; carried follower state will continue into the next map.");
                }
                else
                {
                    bool returnedTrackedItems = SendStoreItems();
                    SendEscapedFollowerDefaultLoadoutOutcomes();
                    NpcMessage.SendLostTeammateOutcomes();

                    if (!returnedTrackedItems)
                    {
                        NpcMessage.NpcSendThankYou();
                    }
                    else
                    {
                        string? id = NpcMessage.GetNpcType("boss");
                        if (id == null) id = NpcMessage.GetNpcType("ally");

                        if (id != null)
                        {
                            NpcMessage.NpcSendThankYou(id);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error sending stored loot");
                Logger.LogError(e);
            }

            if (_lootedItems != null)
            {
                foreach (var stack in _lootedItems)
                {
                    stack.Value.Clear();
                }
            }

            _lootedItems?.Clear();
            _lootedWeaponIds?.Clear();
            _lootedWeaponMagazineIds?.Clear();
            _strictCargoItemIds?.Clear();
            _toSendItems?.Clear();
            _followersWithLoot?.Clear();
            _enemiesSeen?.Clear();

            _followersEquipment?.Clear();
            _lootItemsByBot?.Clear();
            _lootPositionsByBot?.Clear();
            _bodyLootTargetsByBot?.Clear();
            _bodyLootPositionsByBot?.Clear();
            _lootContainerTargetsByBot?.Clear();
            _lootContainerPositionsByBot?.Clear();
            _checkedBodyLootTargetIds?.Clear();
            _checkedContainerLootTargetIds?.Clear();

            _currDoor = null;
            _doorsToOpen?.Clear();

            _lootItem = null;
            _bodyLootTarget = null;
            _lootedItems = null;
            _lootedWeaponIds = null;
            _lootedWeaponMagazineIds = null;
            _strictCargoItemIds = null;

            _enemiesSeen = null;

            _doorsToOpen = null;
            _lootItemsByBot = null;
            _lootPositionsByBot = null;
            _bodyLootTargetsByBot = null;
            _bodyLootPositionsByBot = null;
            _lootContainerTargetsByBot = null;
            _lootContainerPositionsByBot = null;
            _checkedBodyLootTargetIds = null;
            _checkedContainerLootTargetIds = null;

            _isBossDead = false;

            IsDisposed = true;
            Instance = null;
        }

        private void SendEscapedFollowerDefaultLoadoutOutcomes()
        {
            if (_isBossDead || BossPlayers.Instance == null)
            {
                return;
            }

            try
            {
                var entries = new List<object>();
                var seenAids = new HashSet<string>(StringComparer.Ordinal);

                foreach (var boss in BossPlayers.Instance.GetBossPlayers())
                {
                    foreach (var bot in boss.Value.Followers)
                    {
                        if (!ShouldGatherRaidEndFollowerInventory(bot) ||
                            string.IsNullOrWhiteSpace(bot.Profile?.AccountId) ||
                            !seenAids.Add(bot.Profile.AccountId))
                        {
                            continue;
                        }

                        JsonType.FlatItem[] equipmentItems = Singleton<EFT.ItemFactory>.Instance.TreeToFlatItems(
                            new Item[] { bot.GetPlayer.InventoryController.Inventory.Equipment });
                        if (equipmentItems == null || equipmentItems.Length == 0)
                        {
                            continue;
                        }

                        entries.Add(new
                        {
                            Aid = bot.Profile.AccountId,
                            ProfileId = bot.ProfileId ?? string.Empty,
                            Nickname = bot.Profile?.Nickname ?? "Squadmate",
                            Escaped = true,
                            Chance = 1d,
                            ExtractName = string.Empty,
                            Distance = 0d,
                            HealthRatio = CalculateHealthRatio(bot),
                            EquipmentPower = 0d,
                            EnemyAveragePower = 0d,
                            AliveSquadmates = 0,
                            HasSecureMeds = false,
                            EquipmentItems = equipmentItems,
                            TrackedItemIds = GetStoredItems(bot.ProfileId)?.ToArray() ?? Array.Empty<string>()
                        });
                    }
                }

                if (entries.Count == 0)
                {
                    return;
                }

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
                var defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                string json = new
                {
                    Notify = false,
                    Entries = entries
                }.ToJson(defaultJsonConverters);

                Task.Run(() =>
                {
                    try
                    {
                        RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/raid-outcomes", json);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Failed to send escaped teammate loadout outcomes");
                        Logger.LogError(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to prepare escaped teammate loadout outcomes");
                Logger.LogError(ex);
            }
        }

        private static double CalculateHealthRatio(BotOwner bot)
        {
            if (bot?.GetPlayer?.ActiveHealthController == null)
            {
                return 1d;
            }

            float current = 0f;
            float maximum = 0f;
            foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
            {
                try
                {
                    ValueStruct health = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                    current += Mathf.Max(0f, health.Current);
                    maximum += Mathf.Max(0f, health.Maximum);
                }
                catch
                {
                    // Missing body-part data should not block raid-end persistence.
                }
            }

            return maximum > 0f ? Mathf.Clamp01(current / maximum) : 1d;
        }

        private static bool ShouldGatherRaidEndFollowerInventory(BotOwner bot)
        {
            if (bot == null ||
                bot.IsDead ||
                bot.HealthController?.IsAlive != true ||
                bot.GetPlayer?.InventoryController?.Inventory?.Equipment == null)
            {
                return false;
            }

            // BotsController.Stop can move a still-alive follower out of Active before our
            // cleanup runs. Raid-end return and equipment snapshots only require a readable
            // inventory, so do not drop tracked cargo just because BotState has already changed.
            return true;
        }

        public static void Dispose()
        {
            if (Instance != null)
            {
                Instance.Destroy();
                Instance = null;
            }
        }
        /** Set what door the boss wants to open */
        public static void SetCurDoor(Door? door)
        {

            if (Instance != null)
                Instance._currDoor = door;
        }
        public static Door? GetCurDoor()
        {
            if (Instance == null) return null;
            return Instance._currDoor;
        }
        /** Set what loot item the boss wants to be picked up */
        public static void SetCurLootItem(LootItem? item)
        {
            if (Instance != null)
            {
                Instance._lootItem = item;
            }
        }

        public static LootItem? GetCurLootItem()
        {
            if (Instance == null) return null;
            return Instance._lootItem;
        }

        public static void SetCurBodyLootTarget(Corpse? corpse)
        {
            if (Instance != null)
            {
                Instance._bodyLootTarget = corpse;
            }
        }

        public static Corpse? GetCurBodyLootTarget()
        {
            if (Instance == null) return null;
            return Instance._bodyLootTarget;
        }

        public static bool IsBodyLootTargetChecked(Corpse? corpse)
        {
            if (Instance?._checkedBodyLootTargetIds == null ||
                corpse == null ||
                !TryGetBodyLootTargetId(corpse, out string targetId))
            {
                return false;
            }

            return Instance._checkedBodyLootTargetIds.Contains(targetId);
        }

        public static void MarkBodyLootTargetChecked(Corpse? corpse)
        {
            if (Instance?._checkedBodyLootTargetIds == null ||
                corpse == null ||
                !TryGetBodyLootTargetId(corpse, out string targetId))
            {
                return;
            }

            Instance._checkedBodyLootTargetIds.Add(targetId);
        }

        public static void SetCurLootContainerTarget(LootableContainer? container)
        {
            if (Instance != null)
            {
                Instance._lootContainerTarget = container;
            }
        }

        public static LootableContainer? GetCurLootContainerTarget()
        {
            if (Instance == null) return null;
            return Instance._lootContainerTarget;
        }

        public static bool IsContainerLootTargetChecked(LootableContainer? container)
        {
            if (Instance?._checkedContainerLootTargetIds == null ||
                container == null ||
                !TryGetContainerLootTargetId(container, out string targetId))
            {
                return false;
            }

            return Instance._checkedContainerLootTargetIds.Contains(targetId);
        }

        public static void MarkContainerLootTargetChecked(LootableContainer? container)
        {
            if (Instance?._checkedContainerLootTargetIds == null ||
                container == null ||
                !TryGetContainerLootTargetId(container, out string targetId))
            {
                return;
            }

            Instance._checkedContainerLootTargetIds.Add(targetId);
        }

        public static LootItem? GetAssignedLootItem(BotOwner bot)
        {
            if (Instance == null ||
                bot == null ||
                string.IsNullOrEmpty(bot.ProfileId) ||
                Instance._lootItemsByBot == null ||
                !Instance._lootItemsByBot.TryGetValue(bot.ProfileId, out LootItem lootItem))
            {
                return null;
            }

            return lootItem;
        }

        public static Corpse? GetAssignedBodyLootTarget(BotOwner bot)
        {
            if (Instance == null ||
                bot == null ||
                string.IsNullOrEmpty(bot.ProfileId) ||
                Instance._bodyLootTargetsByBot == null ||
                !Instance._bodyLootTargetsByBot.TryGetValue(bot.ProfileId, out Corpse corpse))
            {
                return null;
            }

            return corpse;
        }

        public static LootableContainer? GetAssignedLootContainerTarget(BotOwner bot)
        {
            if (Instance == null ||
                bot == null ||
                string.IsNullOrEmpty(bot.ProfileId) ||
                Instance._lootContainerTargetsByBot == null ||
                !Instance._lootContainerTargetsByBot.TryGetValue(bot.ProfileId, out LootableContainer container))
            {
                return null;
            }

            return container;
        }

        public static bool IsBodyLootTargetReserved(Corpse? corpse, BotOwner? allowedBot = null)
        {
            if (Instance == null || corpse == null)
            {
                return false;
            }

            // Body/container commands can target different objects in parallel, but one object may
            // only have one active follower owner at a time.
            return IsBodyLootTargetReservedByOther(corpse, allowedBot?.ProfileId);
        }

        public static bool IsContainerLootTargetReserved(LootableContainer? container, BotOwner? allowedBot = null)
        {
            if (Instance == null || container == null)
            {
                return false;
            }

            return IsContainerLootTargetReservedByOther(container, allowedBot?.ProfileId);
        }

        public static Vector3 GetLootPosition()
        {
            if (Instance?._lootItem != null && TryGetLootNavPosition(Instance._lootItem, out Vector3 livePosition))
            {
                Instance._lootPosition = livePosition;
                return livePosition;
            }

            return Instance?._lootPosition ?? Vector3.zero;
        }

        public static Vector3 GetLootPosition(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return GetLootPosition();
            }

            LootItem? lootItem = GetAssignedLootItem(bot);
            if (lootItem != null && TryGetLootNavPosition(lootItem, out Vector3 livePosition))
            {
                if (Instance._lootPositionsByBot != null)
                {
                    Instance._lootPositionsByBot[bot.ProfileId] = livePosition;
                }

                return livePosition;
            }

            return Instance._lootPositionsByBot != null &&
                   Instance._lootPositionsByBot.TryGetValue(bot.ProfileId, out Vector3 reservedPosition)
                ? reservedPosition
                : GetLootPosition();
        }

        /** Set what bot is going to pick up the loot */
        public static bool SetTaker(BotOwner bot, LootItem? lootItem = null)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return false;

            var _follower = BossPlayers.Instance.GetFollower(bot);

            if (_follower == null) return false;

            LootItem targetLootItem = lootItem ?? GetAssignedLootItem(bot) ?? Instance._lootItem;
            if (targetLootItem != null)
            {
                try
                {
                    if (lootItem != null)
                    {
                        Instance._lootItem = lootItem;
                    }

                    Collider collider = targetLootItem.GetComponentInChildren<Collider>();
                    if (collider == null)
                    {
                        return false;
                    }

                    Vector3 center = collider.bounds.center;
                    center.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;

                    NavMeshHit navMeshHit;
                    if (!NavMesh.SamplePosition(center, out navMeshHit, 2f, -1))
                    {
                        return false;
                    }

                    Instance._lootPosition = navMeshHit.position;

                    Instance._botToLoot = _follower;
                    Instance._botToLootProfileId = bot.ProfileId;
                    if (Instance._lootItemsByBot != null)
                    {
                        Instance._lootItemsByBot[bot.ProfileId] = targetLootItem;
                    }

                    if (Instance._lootPositionsByBot != null)
                    {
                        Instance._lootPositionsByBot[bot.ProfileId] = navMeshHit.position;
                    }

                    return true;

                }
                catch (Exception ex)
                {
                    Logger.LogError("Could not make bot a Loot Taker");
                    Logger.LogError(ex);
                }
            }

            return false;
        }

        public static bool SetBodyLootTaker(
            BotOwner bot,
            Corpse? corpse = null,
            bool allowAlreadyChecked = false)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return false;

            var follower = BossPlayers.Instance.GetFollower(bot);
            if (follower == null || !follower.CanHandleBodyContainerLootCommands) return false;

            Corpse targetCorpse = corpse ?? GetAssignedBodyLootTarget(bot) ?? Instance._bodyLootTarget;
            if (targetCorpse == null)
            {
                return false;
            }

            // Completed-body history prevents autonomous loot selection from repeating work.
            // Explicit player orders opt out, but can never bypass another follower's reservation.
            if (!allowAlreadyChecked && IsBodyLootTargetChecked(targetCorpse))
            {
                return false;
            }

            if (IsBodyLootTargetReservedByOther(targetCorpse, bot.ProfileId))
            {
                return false;
            }

            try
            {
                if (corpse != null)
                {
                    Instance._bodyLootTarget = corpse;
                }

                if (!TryGetLootNavPosition(targetCorpse, out Vector3 bodyPosition))
                {
                    return false;
                }

                Instance._bodyLootPosition = bodyPosition;
                Instance._botToBodyLoot = follower;
                Instance._botToBodyLootProfileId = bot.ProfileId;
                if (Instance._bodyLootTargetsByBot != null)
                {
                    Instance._bodyLootTargetsByBot[bot.ProfileId] = targetCorpse;
                }

                if (Instance._bodyLootPositionsByBot != null)
                {
                    Instance._bodyLootPositionsByBot[bot.ProfileId] = bodyPosition;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not make bot a Body Loot Taker");
                Logger.LogError(ex);
            }

            return false;
        }

        public static bool SetContainerLootTaker(
            BotOwner bot,
            LootableContainer? container = null,
            bool allowAlreadyChecked = false)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return false;

            var follower = BossPlayers.Instance.GetFollower(bot);
            if (follower == null || !follower.CanHandleBodyContainerLootCommands) return false;

            LootableContainer targetContainer = container ?? GetAssignedLootContainerTarget(bot) ?? Instance._lootContainerTarget;
            if (targetContainer == null)
            {
                return false;
            }

            // Keep completed containers out of autonomous selection while allowing a direct player
            // order to search one again. Active ownership is still checked independently below.
            if (!allowAlreadyChecked && IsContainerLootTargetChecked(targetContainer))
            {
                return false;
            }

            if (IsContainerLootTargetReservedByOther(targetContainer, bot.ProfileId))
            {
                return false;
            }

            try
            {
                if (container != null)
                {
                    Instance._lootContainerTarget = container;
                }

                if (!TryGetLootNavPosition(targetContainer, out Vector3 containerPosition))
                {
                    return false;
                }

                Instance._lootContainerPosition = containerPosition;
                Instance._botToContainerLoot = follower;
                Instance._botToContainerLootProfileId = bot.ProfileId;
                if (Instance._lootContainerTargetsByBot != null)
                {
                    Instance._lootContainerTargetsByBot[bot.ProfileId] = targetContainer;
                }

                if (Instance._lootContainerPositionsByBot != null)
                {
                    Instance._lootContainerPositionsByBot[bot.ProfileId] = containerPosition;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not make bot a Container Loot Taker");
                Logger.LogError(ex);
            }

            return false;
        }

        private static bool IsBodyLootTargetReservedByOther(Corpse targetCorpse, string? allowedProfileId)
        {
            if (Instance?._bodyLootTargetsByBot != null)
            {
                foreach (KeyValuePair<string, Corpse> reservation in Instance._bodyLootTargetsByBot.ToList())
                {
                    if (!IsSameBodyLootTarget(reservation.Value, targetCorpse) ||
                        IsAllowedReservationOwner(reservation.Key, allowedProfileId))
                    {
                        continue;
                    }

                    if (IsActiveLootReservation(reservation.Key, FollowerCommandType.TakeBodyGear))
                    {
                        return true;
                    }

                    RemoveBodyLootReservation(reservation.Key);
                }
            }

            string? legacyOwnerProfileId = Instance?._botToBodyLootProfileId;
            bool legacyOwnerHasMappedTarget =
                !string.IsNullOrEmpty(legacyOwnerProfileId) &&
                Instance?._bodyLootTargetsByBot?.ContainsKey(legacyOwnerProfileId) == true;
            if (!legacyOwnerHasMappedTarget &&
                IsSameBodyLootTarget(Instance?._bodyLootTarget, targetCorpse) &&
                !string.IsNullOrEmpty(legacyOwnerProfileId) &&
                !IsAllowedReservationOwner(legacyOwnerProfileId, allowedProfileId))
            {
                // The global target is also the quick-menu target. Once per-follower maps exist,
                // pairing that mutable target with an older global owner creates a false reservation
                // for the next body. Use this fallback only for an unmapped legacy owner.
                if (IsActiveLootReservation(legacyOwnerProfileId, FollowerCommandType.TakeBodyGear))
                {
                    return true;
                }

                RemoveBodyLootReservation(legacyOwnerProfileId);
            }

            return false;
        }

        private static bool IsContainerLootTargetReservedByOther(LootableContainer targetContainer, string? allowedProfileId)
        {
            if (Instance?._lootContainerTargetsByBot != null)
            {
                foreach (KeyValuePair<string, LootableContainer> reservation in Instance._lootContainerTargetsByBot.ToList())
                {
                    if (!IsSameContainerLootTarget(reservation.Value, targetContainer) ||
                        IsAllowedReservationOwner(reservation.Key, allowedProfileId))
                    {
                        continue;
                    }

                    if (IsActiveLootReservation(reservation.Key, FollowerCommandType.TakeContainerLoot))
                    {
                        return true;
                    }

                    RemoveContainerLootReservation(reservation.Key);
                }
            }

            string? legacyOwnerProfileId = Instance?._botToContainerLootProfileId;
            bool legacyOwnerHasMappedTarget =
                !string.IsNullOrEmpty(legacyOwnerProfileId) &&
                Instance?._lootContainerTargetsByBot?.ContainsKey(legacyOwnerProfileId) == true;
            if (!legacyOwnerHasMappedTarget &&
                IsSameContainerLootTarget(Instance?._lootContainerTarget, targetContainer) &&
                !string.IsNullOrEmpty(legacyOwnerProfileId) &&
                !IsAllowedReservationOwner(legacyOwnerProfileId, allowedProfileId))
            {
                // Keep the same protection as bodies: a menu target refresh must not inherit
                // another follower's legacy global owner while mapped reservations are active.
                if (IsActiveLootReservation(legacyOwnerProfileId, FollowerCommandType.TakeContainerLoot))
                {
                    return true;
                }

                RemoveContainerLootReservation(legacyOwnerProfileId);
            }

            return false;
        }

        private static bool IsAllowedReservationOwner(string ownerProfileId, string? allowedProfileId)
        {
            return !string.IsNullOrEmpty(ownerProfileId) &&
                   !string.IsNullOrEmpty(allowedProfileId) &&
                   string.Equals(ownerProfileId, allowedProfileId, StringComparison.Ordinal);
        }

        private static bool IsActiveLootReservation(string profileId, FollowerCommandType expectedCommand)
        {
            BotFollowerPlayer follower = BossPlayers.GetFollowerByProfileId(profileId);
            BotOwner bot = follower?.GetBot();
            if (follower == null ||
                bot == null ||
                bot.IsDead ||
                bot.BotState != EBotState.Active ||
                bot.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (follower.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                command == expectedCommand)
            {
                return true;
            }

            // Once the follower starts the simulated search, command changes are ignored, so the
            // committed command becomes the authoritative liveness signal for the reservation.
            return follower.IsCommittedLootCommandActive(expectedCommand);
        }

        private static bool IsSameBodyLootTarget(Corpse? first, Corpse? second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (ReferenceEquals(first, second))
            {
                return true;
            }

            Item firstRoot = first.ItemOwner?.RootItem as Item;
            Item secondRoot = second.ItemOwner?.RootItem as Item;
            return !string.IsNullOrEmpty(firstRoot?.Id) &&
                   string.Equals(firstRoot.Id, secondRoot?.Id, StringComparison.Ordinal);
        }

        private static bool TryGetBodyLootTargetId(Corpse corpse, out string targetId)
        {
            targetId = string.Empty;
            Item root = corpse?.ItemOwner?.RootItem as Item;
            if (string.IsNullOrEmpty(root?.Id))
            {
                return false;
            }

            targetId = root.Id;
            return true;
        }

        private static bool IsSameContainerLootTarget(LootableContainer? first, LootableContainer? second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (ReferenceEquals(first, second))
            {
                return true;
            }

            Item firstRoot = first.ItemOwner?.Items?.FirstOrDefault();
            Item secondRoot = second.ItemOwner?.Items?.FirstOrDefault();
            return !string.IsNullOrEmpty(firstRoot?.Id) &&
                   string.Equals(firstRoot.Id, secondRoot?.Id, StringComparison.Ordinal);
        }

        private static bool TryGetContainerLootTargetId(LootableContainer container, out string targetId)
        {
            targetId = string.Empty;
            Item root = container?.ItemOwner?.Items?.FirstOrDefault();
            if (string.IsNullOrEmpty(root?.Id))
            {
                return false;
            }

            targetId = root.Id;
            return true;
        }

        private static void RemoveBodyLootReservation(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId))
            {
                return;
            }

            Instance._bodyLootTargetsByBot?.Remove(profileId);
            Instance._bodyLootPositionsByBot?.Remove(profileId);

            if (string.Equals(Instance._botToBodyLootProfileId, profileId, StringComparison.Ordinal))
            {
                Instance._botToBodyLoot = null;
                Instance._botToBodyLootProfileId = null;
            }
        }

        private static void RemoveContainerLootReservation(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId))
            {
                return;
            }

            Instance._lootContainerTargetsByBot?.Remove(profileId);
            Instance._lootContainerPositionsByBot?.Remove(profileId);

            if (string.Equals(Instance._botToContainerLootProfileId, profileId, StringComparison.Ordinal))
            {
                Instance._botToContainerLoot = null;
                Instance._botToContainerLootProfileId = null;
            }
        }

        internal static bool TryGetLootNavPosition(LootItem lootItem, out Vector3 position)
        {
            position = Vector3.zero;

            if (lootItem == null)
            {
                return false;
            }

            Vector3 samplePoint = lootItem.transform.position;

            try
            {
                Collider collider = lootItem.GetComponentInChildren<Collider>();
                if (collider != null)
                {
                    samplePoint = collider.bounds.center;
                    samplePoint.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;
                }

                if (NavMesh.SamplePosition(samplePoint, out NavMeshHit navMeshHit, 2f, -1))
                {
                    position = navMeshHit.position;
                    return true;
                }
            }
            catch
            {
                // fall back to raw transform position below
            }

            position = lootItem.transform.position;
            return true;
        }

        internal static bool TryGetLootNavPosition(LootableContainer container, out Vector3 position)
        {
            position = Vector3.zero;

            if (container == null)
            {
                return false;
            }

            Vector3 samplePoint = container.transform.position;

            try
            {
                Collider collider = container.GetComponentInChildren<Collider>();
                if (collider != null)
                {
                    samplePoint = collider.bounds.center;
                    samplePoint.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;
                }

                if (NavMesh.SamplePosition(samplePoint, out NavMeshHit navMeshHit, 2f, -1))
                {
                    position = navMeshHit.position;
                    return true;
                }
            }
            catch
            {
                // fall back to raw transform position below
            }

            position = container.transform.position;
            return true;
        }

        public static bool IsTaker(BotOwner bot)
        {
            if (Instance == null || bot == null) return false;
            if (!string.IsNullOrEmpty(bot.ProfileId) &&
                Instance._lootItemsByBot?.ContainsKey(bot.ProfileId) == true)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(Instance._botToLootProfileId) &&
                !string.IsNullOrEmpty(bot.ProfileId) &&
                string.Equals(Instance._botToLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            var _follower = BossPlayers.Instance.GetFollower(bot);
            return _follower != null && _follower == Instance._botToLoot;
        }

        public static bool IsBodyLootTaker(BotOwner bot)
        {
            if (Instance == null || bot == null) return false;
            if (!string.IsNullOrEmpty(bot.ProfileId) &&
                Instance._bodyLootTargetsByBot?.ContainsKey(bot.ProfileId) == true)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(Instance._botToBodyLootProfileId) &&
                !string.IsNullOrEmpty(bot.ProfileId) &&
                string.Equals(Instance._botToBodyLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            var follower = BossPlayers.Instance.GetFollower(bot);
            return follower != null && follower == Instance._botToBodyLoot;
        }

        public static bool IsContainerLootTaker(BotOwner bot)
        {
            if (Instance == null || bot == null) return false;
            if (!string.IsNullOrEmpty(bot.ProfileId) &&
                Instance._lootContainerTargetsByBot?.ContainsKey(bot.ProfileId) == true)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(Instance._botToContainerLootProfileId) &&
                !string.IsNullOrEmpty(bot.ProfileId) &&
                string.Equals(Instance._botToContainerLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            var follower = BossPlayers.Instance.GetFollower(bot);
            return follower != null && follower == Instance._botToContainerLoot;
        }

        public static void RemoveTaker(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;

            Instance._lootItemsByBot?.Remove(bot.ProfileId);
            Instance._lootPositionsByBot?.Remove(bot.ProfileId);

            if (!string.IsNullOrEmpty(Instance._botToLootProfileId) &&
                string.Equals(Instance._botToLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
                return;
            }

            Components.BotFollowerPlayer follower = BossPlayers.Instance.GetFollower(bot);
            if (follower != null && Instance._botToLoot == follower)
            {
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
            }
        }

        public static void RemoveBodyLootTaker(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;

            Instance._bodyLootTargetsByBot?.Remove(bot.ProfileId);
            Instance._bodyLootPositionsByBot?.Remove(bot.ProfileId);

            if (!string.IsNullOrEmpty(Instance._botToBodyLootProfileId) &&
                string.Equals(Instance._botToBodyLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                Instance._botToBodyLoot = null;
                Instance._botToBodyLootProfileId = null;
                return;
            }

            Components.BotFollowerPlayer follower = BossPlayers.Instance.GetFollower(bot);
            if (follower != null && Instance._botToBodyLoot == follower)
            {
                Instance._botToBodyLoot = null;
                Instance._botToBodyLootProfileId = null;
            }
        }

        public static void RemoveContainerLootTaker(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;

            Instance._lootContainerTargetsByBot?.Remove(bot.ProfileId);
            Instance._lootContainerPositionsByBot?.Remove(bot.ProfileId);

            if (!string.IsNullOrEmpty(Instance._botToContainerLootProfileId) &&
                string.Equals(Instance._botToContainerLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                Instance._botToContainerLoot = null;
                Instance._botToContainerLootProfileId = null;
                return;
            }

            Components.BotFollowerPlayer follower = BossPlayers.Instance.GetFollower(bot);
            if (follower != null && Instance._botToContainerLoot == follower)
            {
                Instance._botToContainerLoot = null;
                Instance._botToContainerLootProfileId = null;
            }
        }
        /** Set what bot is going to open the door */
        public static bool SetOpener(BotOwner bot, Door? door = null)
        {
            if (Instance == null || bot == null) return false;
            if (Instance._currDoor != null && Instance._doorsToOpen != null)
            {
                if (!Instance._doorsToOpen.ContainsKey(bot.ProfileId))
                {
                    Instance._doorsToOpen.Add(bot.ProfileId, Instance._currDoor);
                }
                else
                {
                    Instance._doorsToOpen[bot.ProfileId] = door != null ? door : Instance._currDoor;
                }
                return true;
            }
            return false;
        }

        public static bool IsOpener(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null || bot == null) return false;
            return Instance._doorsToOpen.ContainsKey(bot.ProfileId);
        }

        public static void RemoveOpener(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            if (Instance._doorsToOpen.ContainsKey(bot.ProfileId)) Instance._doorsToOpen.Remove(bot.ProfileId);
        }

        public static Door? GetDoorToOpen(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null) return null;
            if (!Instance._doorsToOpen.ContainsKey(bot.ProfileId)) return null;
            return Instance._doorsToOpen[bot.ProfileId];
        }

        public static void ClearCurLootItem()
        {
            if (Instance != null)
            {
                Instance._lootItem = null;
                Instance._lootPosition = null;
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
            }
        }

        public static Vector3 GetBodyLootPosition()
        {
            if (Instance?._bodyLootTarget != null && TryGetLootNavPosition(Instance._bodyLootTarget, out Vector3 livePosition))
            {
                Instance._bodyLootPosition = livePosition;
                return livePosition;
            }

            return Instance?._bodyLootPosition ?? Vector3.zero;
        }

        public static Vector3 GetBodyLootPosition(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return GetBodyLootPosition();
            }

            Corpse? corpse = GetAssignedBodyLootTarget(bot);
            if (corpse != null && TryGetLootNavPosition(corpse, out Vector3 livePosition))
            {
                if (Instance._bodyLootPositionsByBot != null)
                {
                    Instance._bodyLootPositionsByBot[bot.ProfileId] = livePosition;
                }

                return livePosition;
            }

            return Instance._bodyLootPositionsByBot != null &&
                   Instance._bodyLootPositionsByBot.TryGetValue(bot.ProfileId, out Vector3 reservedPosition)
                ? reservedPosition
                : GetBodyLootPosition();
        }

        public static Vector3 GetContainerLootPosition()
        {
            if (Instance?._lootContainerTarget != null && TryGetLootNavPosition(Instance._lootContainerTarget, out Vector3 livePosition))
            {
                Instance._lootContainerPosition = livePosition;
                return livePosition;
            }

            return Instance?._lootContainerPosition ?? Vector3.zero;
        }

        public static Vector3 GetContainerLootPosition(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return GetContainerLootPosition();
            }

            LootableContainer? container = GetAssignedLootContainerTarget(bot);
            if (container != null && TryGetLootNavPosition(container, out Vector3 livePosition))
            {
                if (Instance._lootContainerPositionsByBot != null)
                {
                    Instance._lootContainerPositionsByBot[bot.ProfileId] = livePosition;
                }

                return livePosition;
            }

            return Instance._lootContainerPositionsByBot != null &&
                   Instance._lootContainerPositionsByBot.TryGetValue(bot.ProfileId, out Vector3 reservedPosition)
                ? reservedPosition
                : GetContainerLootPosition();
        }

        public static void ClearCurBodyLootTarget()
        {
            if (Instance != null)
            {
                Instance._bodyLootTarget = null;
                Instance._bodyLootPosition = null;
                Instance._botToBodyLoot = null;
                Instance._botToBodyLootProfileId = null;
            }
        }

        public static void ClearCurLootContainerTarget()
        {
            if (Instance != null)
            {
                Instance._lootContainerTarget = null;
                Instance._lootContainerPosition = null;
                Instance._botToContainerLoot = null;
                Instance._botToContainerLootProfileId = null;
            }
        }
        /** Store the item that was given to a follower */
        public static void StoreItem(BotOwner bot, Item item)
        {
            if (Instance == null || Instance._lootedItems == null || Instance._followersWithLoot == null)
            {
                return;
            }

            if (!Instance._lootedItems.ContainsKey(bot.ProfileId))
            {
                Instance._lootedItems.Add(bot.ProfileId, new List<string>());
                Instance._followersWithLoot.Add(bot.ProfileId, new Dictionary<string, object> {
                    { "_id" , bot.ProfileId  },
                    { "aid" , bot.Profile.AccountId },
                    {
                        "Info" , new Dictionary<string, object>{
                            { "Level", bot.Profile.Info.Level },
                            { "MemberCategory", bot.Profile.Info.MemberCategory },
                            { "Nickname",  bot.Profile.Info.Nickname },
                            { "Side",  bot.Profile.Info.Side },
                        }
                    },
                });
            }

            var list = Instance._lootedItems[bot.ProfileId];
            HashSet<string> treeIds = GetItemTreeIds(item);

            if (TryStoreOnlyReturnableHandledItems(bot, item, treeIds, list))
            {
                return;
            }

            RegisterLootedWeaponTree(bot, item);
            TrackReturnRoot(item, treeIds, list);
            TrackReloadableWeaponMagazines(item, list);
        }

        private static void TrackReloadableWeaponMagazines(Item item, List<string> trackedReturnIds)
        {
            if (item == null || trackedReturnIds == null)
            {
                return;
            }

            foreach (Weapon weapon in GetWeaponTreeItems(item))
            {
                EFT.InventoryLogic.Magazine magazine;
                try
                {
                    magazine = weapon.GetCurrentMagazine();
                }
                catch
                {
                    continue;
                }

                if (magazine == null ||
                    string.IsNullOrWhiteSpace(magazine.Id) ||
                    trackedReturnIds.Contains(magazine.Id))
                {
                    continue;
                }

                // The weapon is the normal return root, but EFT can eject its original magazine
                // into the vest during combat. Keep the magazine id as a fallback root so it is
                // still stripped/returned after reloading; ancestor checks suppress duplicates
                // while the magazine remains seated in the tracked weapon.
                trackedReturnIds.Add(magazine.Id);
            }
        }

        public static void RegisterLootedWeaponTree(BotOwner bot, Item item)
        {
            if (Instance?._lootedWeaponIds == null ||
                bot == null ||
                item == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId))
            {
                return;
            }

            if (!Instance._lootedWeaponIds.TryGetValue(bot.ProfileId, out HashSet<string> weaponIds))
            {
                weaponIds = new HashSet<string>(StringComparer.Ordinal);
                Instance._lootedWeaponIds.Add(bot.ProfileId, weaponIds);
            }

            foreach (Weapon weapon in GetWeaponTreeItems(item))
            {
                if (!string.IsNullOrWhiteSpace(weapon.Id))
                {
                    weaponIds.Add(weapon.Id);
                }

                // The inserted magazine arrives as part of the weapon tree. Preserve that
                // relationship after EFT ejects it during a later reload so it remains an
                // approved magazine for this looted weapon, not an arbitrary spawned spare.
                EFT.InventoryLogic.Magazine insertedMagazine = null;
                try
                {
                    insertedMagazine = weapon.GetCurrentMagazine();
                }
                catch
                {
                    // Some transient weapon trees are incomplete while their move settles.
                }

                RegisterLootedWeaponMagazine(bot, weapon, insertedMagazine);
            }
        }

        public static void RegisterLootedWeaponMagazine(
            BotOwner bot,
            Weapon weapon,
            EFT.InventoryLogic.Magazine magazine)
        {
            if (Instance?._lootedWeaponMagazineIds == null ||
                bot == null ||
                weapon == null ||
                magazine == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId) ||
                string.IsNullOrWhiteSpace(weapon.Id) ||
                string.IsNullOrWhiteSpace(magazine.Id))
            {
                return;
            }

            if (!Instance._lootedWeaponMagazineIds.TryGetValue(
                    bot.ProfileId,
                    out Dictionary<string, HashSet<string>> magazinesByWeapon))
            {
                magazinesByWeapon = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                Instance._lootedWeaponMagazineIds.Add(bot.ProfileId, magazinesByWeapon);
            }

            if (!magazinesByWeapon.TryGetValue(weapon.Id, out HashSet<string> magazineIds))
            {
                magazineIds = new HashSet<string>(StringComparer.Ordinal);
                magazinesByWeapon.Add(weapon.Id, magazineIds);
            }

            if (magazineIds.Add(magazine.Id))
            {
                Logger.LogInfo(
                    $"[LootCommand][WeaponReload] follower='{bot.Profile?.Nickname ?? bot.ProfileId}' " +
                    $"weapon={weapon.TemplateId} magazine={magazine.TemplateId} result=approvedPackageMagazine");
            }
        }

        public static bool IsApprovedLootedWeaponMagazine(
            BotOwner bot,
            Weapon weapon,
            EFT.InventoryLogic.Magazine magazine)
        {
            if (Instance?._lootedWeaponMagazineIds == null ||
                bot == null ||
                weapon == null ||
                magazine == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId) ||
                string.IsNullOrWhiteSpace(weapon.Id) ||
                string.IsNullOrWhiteSpace(magazine.Id))
            {
                return false;
            }

            return Instance._lootedWeaponMagazineIds.TryGetValue(
                       bot.ProfileId,
                       out Dictionary<string, HashSet<string>> magazinesByWeapon) &&
                   magazinesByWeapon.TryGetValue(weapon.Id, out HashSet<string> magazineIds) &&
                   magazineIds.Contains(magazine.Id);
        }

        public static bool IsLootedWeapon(BotOwner bot, Weapon weapon)
        {
            if (Instance?._lootedWeaponIds == null ||
                bot == null ||
                weapon == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId) ||
                string.IsNullOrWhiteSpace(weapon.Id))
            {
                return false;
            }

            return Instance._lootedWeaponIds.TryGetValue(bot.ProfileId, out HashSet<string> weaponIds) &&
                   weaponIds.Contains(weapon.Id);
        }

        public static void RegisterStrictCargoTree(BotOwner bot, Item item)
        {
            if (Instance?._strictCargoItemIds == null ||
                bot == null ||
                item == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId))
            {
                return;
            }

            if (!Instance._strictCargoItemIds.TryGetValue(bot.ProfileId, out HashSet<string> itemIds))
            {
                itemIds = new HashSet<string>(StringComparer.Ordinal);
                Instance._strictCargoItemIds.Add(bot.ProfileId, itemIds);
            }

            // View Backpack is a player-owned cargo transfer, not permission to use the tree as
            // equipment. Record every child independently so one later commanded magazine does
            // not make manually supplied magazines beside it eligible for weapon readiness.
            HashSet<string> treeIds = GetItemTreeIds(item);
            itemIds.UnionWith(treeIds);
            Logger.LogInfo(
                $"[LootCommand][CargoProvenance] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                $"root={item.TemplateId}:{item.Id} classification=strictCargo treeItems={treeIds.Count}");
        }

        public static void ClearStrictCargoTree(BotOwner bot, Item item)
        {
            if (Instance?._strictCargoItemIds == null ||
                bot == null ||
                item == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId) ||
                !Instance._strictCargoItemIds.TryGetValue(bot.ProfileId, out HashSet<string> itemIds))
            {
                return;
            }

            int removed = 0;
            foreach (string itemId in GetItemTreeIds(item))
            {
                if (itemIds.Remove(itemId))
                {
                    removed++;
                }
            }

            if (itemIds.Count == 0)
            {
                Instance._strictCargoItemIds.Remove(bot.ProfileId);
            }

            if (removed > 0)
            {
                Logger.LogInfo(
                    $"[LootCommand][CargoProvenance] follower='{bot.Profile?.Nickname ?? bot.ProfileId ?? "unknown"}' " +
                    $"root={item.TemplateId}:{item.Id} classification=commandAcquired clearedStrictItems={removed}");
            }
        }

        public static bool IsStrictCargoItem(BotOwner bot, Item item)
        {
            if (Instance?._strictCargoItemIds == null ||
                bot == null ||
                item == null ||
                string.IsNullOrWhiteSpace(bot.ProfileId) ||
                string.IsNullOrWhiteSpace(item.Id))
            {
                return false;
            }

            return Instance._strictCargoItemIds.TryGetValue(bot.ProfileId, out HashSet<string> itemIds) &&
                   itemIds.Contains(item.Id);
        }

        public static int RemoveStrictCargoItemIds(string bot, IEnumerable<string> itemIds)
        {
            if (Instance?._strictCargoItemIds == null ||
                string.IsNullOrWhiteSpace(bot) ||
                itemIds == null ||
                !Instance._strictCargoItemIds.TryGetValue(bot, out HashSet<string> strictItemIds))
            {
                return 0;
            }

            int removed = 0;
            foreach (string itemId in itemIds)
            {
                if (strictItemIds.Remove(itemId))
                {
                    removed++;
                }
            }

            if (strictItemIds.Count == 0)
            {
                Instance._strictCargoItemIds.Remove(bot);
            }

            return removed;
        }

        private static IEnumerable<Weapon> GetWeaponTreeItems(Item item)
        {
            if (item == null)
            {
                yield break;
            }

            if (item is Weapon rootWeapon && rootWeapon.GetItemComponent<KnifeComponent>() == null)
            {
                yield return rootWeapon;
            }

            foreach (Item child in item.GetAllItems())
            {
                if (child is Weapon weapon &&
                    !ReferenceEquals(child, item) &&
                    weapon.GetItemComponent<KnifeComponent>() == null)
                {
                    yield return weapon;
                }
            }
        }

        private static void TrackReturnRoot(Item item, HashSet<string> treeIds, List<string> trackedReturnIds)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || trackedReturnIds == null)
            {
                return;
            }

            treeIds ??= GetItemTreeIds(item);

            // Track the largest meaningful root. If a whole backpack/rig is tracked, its
            // children ride inside that one return tree and must not be mailed separately.
            if (HasTrackedAncestor(item, trackedReturnIds))
            {
                return;
            }

            trackedReturnIds.RemoveAll(itemId =>
                !string.Equals(itemId, item.Id, StringComparison.Ordinal) &&
                treeIds.Contains(itemId));

            if (!trackedReturnIds.Contains(item.Id))
            {
                trackedReturnIds.Add(item.Id);
            }
        }

        private static bool TryStoreOnlyReturnableHandledItems(
            BotOwner bot,
            Item item,
            HashSet<string> treeIds,
            List<string> trackedReturnIds)
        {
            if (pitFireTeam.IsFollowerLoadoutLootableMode())
            {
                return false;
            }

            HashSet<string> protectedFollowerGearIds = GetProtectedFollowerEquipmentIds();
            if (protectedFollowerGearIds.Count == 0 || !treeIds.Overlaps(protectedFollowerGearIds))
            {
                return false;
            }

            // Simple/Restricted teammate spawn gear may be moved around in raid for interaction
            // parity, but it must not become return-mail cargo. If a handled tree mixes protected
            // gear and unrelated cargo, split clean non-protected children back out for return
            // tracking instead of mailing the protected parent.
            HashSet<string> protectedHandledIds = treeIds
                .Where(itemId => protectedFollowerGearIds.Contains(itemId))
                .ToHashSet(StringComparer.Ordinal);

            RegisterProtectedRaidItemIds(
                protectedHandledIds,
                "protected follower handled item",
                synchronous: true);

            if (!protectedFollowerGearIds.Contains(item.Id))
            {
                TrackReturnRoot(item, treeIds, trackedReturnIds);
                return true;
            }

            List<Item> returnableRoots = new List<Item>();
            CollectReturnableRootsExcludingProtected(item, protectedFollowerGearIds, returnableRoots);

            foreach (Item returnableRoot in returnableRoots)
            {
                if (returnableRoot == null || string.IsNullOrWhiteSpace(returnableRoot.Id))
                {
                    continue;
                }

                TrackReturnRoot(returnableRoot, GetItemTreeIds(returnableRoot), trackedReturnIds);
            }

            if (returnableRoots.Count == 0)
            {
                Logger.LogInfo(
                    $"[Loot] Skipped protected follower gear return for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}': {item.TemplateId}");
            }

            return true;
        }

        public static bool IsProtectedFollowerEquipment(Item item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || pitFireTeam.IsFollowerLoadoutLootableMode())
            {
                return false;
            }

            HashSet<string> protectedFollowerGearIds = GetProtectedFollowerEquipmentIds();
            return protectedFollowerGearIds.Contains(item.Id);
        }

        private static HashSet<string> GetProtectedFollowerEquipmentIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (Instance?._followersEquipment == null)
            {
                return ids;
            }

            foreach (List<string> followerGearIds in Instance._followersEquipment.Values)
            {
                if (followerGearIds == null)
                {
                    continue;
                }

                foreach (string itemId in followerGearIds)
                {
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        ids.Add(itemId);
                    }
                }
            }

            return ids;
        }

        private static void CollectReturnableRootsExcludingProtected(
            Item item,
            HashSet<string> protectedIds,
            List<Item> returnableRoots)
        {
            if (item == null || protectedIds == null || protectedIds.Count == 0)
            {
                return;
            }

            HashSet<string> itemTreeIds = GetItemTreeIds(item);
            bool itemIsProtected = protectedIds.Contains(item.Id);
            bool subtreeContainsProtected = itemTreeIds.Overlaps(protectedIds);

            if (!itemIsProtected && !subtreeContainsProtected)
            {
                returnableRoots.Add(item);
                return;
            }

            if (item is not CompoundItem compound)
            {
                return;
            }

            foreach (Item child in GetDirectChildren(compound))
            {
                CollectReturnableRootsExcludingProtected(child, protectedIds, returnableRoots);
            }
        }

        private static IEnumerable<Item> GetDirectChildren(CompoundItem item)
        {
            foreach (Item child in item.GetAllItems())
            {
                if (child == null || ReferenceEquals(child, item))
                {
                    continue;
                }

                if (ReferenceEquals(child.Parent?.Container?.ParentItem, item))
                {
                    yield return child;
                }
            }
        }

        public static void RemoveStoredItem(string bot, string itemId)
        {
            if (Instance?._lootedItems != null && Instance._lootedItems.ContainsKey(bot))
            {
                var list = Instance._lootedItems[bot];
                if (list.Contains(itemId))
                {
                    list.Remove(itemId);
                }
            }

            if (Instance?._lootedWeaponIds != null &&
                Instance._lootedWeaponIds.TryGetValue(bot, out HashSet<string> weaponIds))
            {
                weaponIds.Remove(itemId);
            }

            if (Instance?._lootedWeaponMagazineIds != null &&
                Instance._lootedWeaponMagazineIds.TryGetValue(
                    bot,
                    out Dictionary<string, HashSet<string>> magazinesByWeapon))
            {
                magazinesByWeapon.Remove(itemId);
                foreach (HashSet<string> magazineIds in magazinesByWeapon.Values)
                {
                    magazineIds.Remove(itemId);
                }
            }

            RemoveStrictCargoItemIds(bot, new[] { itemId });
        }

        public static List<string>? GetStoredItems(string bot)
        {
            if (Instance?._lootedItems != null && Instance._lootedItems.ContainsKey(bot))
            {
                return Instance._lootedItems[bot];
            }

            return null;
        }

        public static void ClearStoredItems(string bot)
        {
            if (Instance == null || Instance._isBossDead || Instance._lootedItems == null || Instance._followersWithLoot == null) return;

            if (Instance._lootedItems.ContainsKey(bot))
            {
                Instance._lootedItems.Remove(bot);
                Instance._followersWithLoot.Remove(bot);
            }

            Instance._lootedWeaponIds?.Remove(bot);
            Instance._lootedWeaponMagazineIds?.Remove(bot);
            Instance._strictCargoItemIds?.Remove(bot);
        }

        /** Store what enemies the player might have seen during "CONTACT" phrase */
        public static void CheckSeenEnemies(IPlayer player)
        {
            if (Instance == null || player == null) return;
            if (Instance._enemiesSeen == null) return;
            if (player.Transform == null) return;

            Instance._closestEnemySeen = null;
            Instance._enemiesSeen.Clear();

            pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);

            if (boss == null || boss.bossGroup == null) return;

            float scanDistance = pitFireTeam.scanDistance.Value;

            Vector3 playerPosition = player.Transform.position;
            Vector3 playerLookDirection = player.LookDirection;
            float sphereRadius = scanDistance / 2;
            float sphereDistance = scanDistance / 2;

            RaycastHit[] hits = new RaycastHit[20];
            Ray visionRay = new Ray(playerPosition, playerLookDirection);
            int numHits = Physics.SphereCastNonAlloc(
                    visionRay,
                    sphereRadius,
                    hits,
                    sphereDistance,
                    LayersMaskController.PlayerMask
                );

            // get all enemies the boss might have seen
            for (int i = 0; i < numHits; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider != null && hit.collider.gameObject != null)
                {
                    var enemy = hit.collider.gameObject.GetComponent<Player>();
                    if (enemy != null)
                    {
                        if (boss.Followers.Find(fl => fl.ProfileId == enemy.ProfileId) != null) continue;

                        if (player.ProfileId == enemy.ProfileId) continue;

                        bool isenemy = boss.bossGroup.IsEnemy(enemy);

                        if (!isenemy && boss.bossGroup.IsPlayerEnemy(enemy))
                        {
                            isenemy = true;
                        }

                        if (isenemy)
                        {
                            BotOwner enemyBot = enemy.GetComponent<BotOwner>();
                            if (enemyBot != null)
                            {
                                // who ever has the play in sights is the enemy
                                if (enemyBot.Memory.GoalEnemy != null && enemyBot.Memory.GoalEnemy.ProfileId == player.ProfileId)
                                {
                                    isenemy = true;
                                }
                                else
                                {
                                    var bossAllies = Utils.Props.BossFollowersType.ToList();
                                    bossAllies.Add(WildSpawnType.exUsec);
                                    // do not mark as enemy an ally
                                    if (
                                        enemy.Side == player.Side && new EPlayerSide[] { EPlayerSide.Bear, EPlayerSide.Usec }.Contains(enemy.Side) &&
                                        Utils.Utils.FlagGet("pitFireTeam") && !Utils.Utils.FlagGet("isBadGuy") &&
                                        !enemyBot.BotsGroup.IsEnemy(player)
                                       )
                                    {
                                        isenemy = false;
                                    }
                                    // do not mark as enemy a boss allly
                                    else if (
                                        !enemyBot.BotsGroup.IsEnemy(player) &&
                                        bossAllies.Contains(enemy.Profile.Info.Settings.Role) &&
                                        Utils.Utils.PlayerHasKnightQuest(player.Profile)
                                    )
                                    {
                                        isenemy = false;
                                    }
                                }
                            }
                        }

                        if (isenemy)
                        {
                            if (player.PlayerBones?.WeaponRoot == null) continue;
                            if (enemy.MainParts == null) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.head, out var headPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.body, out var bodyPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.leftArm, out var leftArmPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.rightArm, out var rightArmPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.leftLeg, out var leftLegPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.rightLeg, out var rightLegPart)) continue;

                            Vector3 firePos = player.PlayerBones.WeaponRoot.position;
                            // - we check if any part of the enemy is visible to the player
                            if (
                                Utils.Utils.CanShootToTarget(new ShootToPoint(headPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootToPoint(bodyPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootToPoint(leftArmPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootToPoint(rightArmPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootToPoint(leftLegPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootToPoint(rightLegPart.Position, 1), firePos, LayersMaskController.HighPolyWithTerrainMask, false)
                            )
                            {
                                Instance._enemiesSeen.Add(enemy);
                            }
                        }
                    }
                }
            }

            float dist = Mathf.Infinity;
            Player? closest = null;
            foreach (var item in Instance._enemiesSeen)
            {
                float edist = Vector3.Distance(playerPosition, item.Position);
                if (edist < dist)
                {
                    dist = edist;
                    closest = item;
                }
            }

            if (closest != null)
            {
                Instance._closestEnemySeen = closest;
            }
        }
        /** Get all enemies the player might have seen during "CONTACT" phrase */
        public static List<Player> GetSeenEnemies()
        {
            if (Instance == null || Instance._enemiesSeen == null) return new List<Player>();
            return Instance._enemiesSeen;

        }
        /** Get the closest enemy the player might have seen during "CONTACT" phrase */
        public static Player GetClosestSeenEnemy()
        {
            if (Instance == null) return null;
            return Instance._closestEnemySeen;
        }

        public static void BossIsDead()
        {
            if (Instance == null) return;
            Instance._isBossDead = true;
        }

        public static bool IsBossDead()
        {
            if (Instance == null) return false;
            return Instance._isBossDead;
        }


        public static void StoreEquipment(Profile profile)
        {
            if (Instance == null || Instance._followersEquipment == null)
            {
                return;
            }

            if (!Instance._followersEquipment.ContainsKey(profile.ProfileId))
            {
                if (FollowerTransitStateCache.TryConsumeProtectedEquipmentIds(profile, out List<string> carriedProtectedItems))
                {
                    Instance._followersEquipment.Add(profile.ProfileId, carriedProtectedItems);
                    RestoreTransitTrackedReturnItems(profile);
                    Logger.LogInfo(
                        $"[Transit] Restored carried follower protected-equipment set for '{profile.Nickname ?? profile.ProfileId}' " +
                        $"protectedEquipmentIds={carriedProtectedItems.Count}.");
                    return;
                }

                HashSet<string> items = new HashSet<string>(StringComparer.Ordinal);
                foreach (EquipmentSlot slotType in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    if (
                        slotType == EquipmentSlot.Dogtag ||
                        slotType == EquipmentSlot.ArmBand ||
                        slotType == EquipmentSlot.Scabbard
                    ) continue;

                    Slot botSlot = profile.Inventory.Equipment.GetSlot(slotType);

                    if (botSlot.IsSpecial) continue;

                    Item contained = botSlot.ContainedItem;

                    if (contained != null)
                    {
                        items.UnionWith(GetItemTreeIds(contained));
                    }
                }

                if (items.Count > 0)
                {
                    List<string> protectedItems = items.ToList();
                    Instance._followersEquipment.Add(profile.ProfileId, protectedItems);
                    // Keep the spawn kit locally for in-raid item-removal patches. Server-side
                    // extraction protection is registered when the spawn profile is generated,
                    // because the backend already owns that prepared equipment graph.
                }
            }
        }

        private static void RestoreTransitTrackedReturnItems(Profile profile)
        {
            if (Instance?._lootedItems == null ||
                Instance._followersWithLoot == null ||
                profile == null ||
                string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                return;
            }

            if (!FollowerTransitStateCache.TryConsumeTrackedReturnItemIds(profile, out List<string> trackedReturnItemIds) ||
                trackedReturnItemIds == null ||
                trackedReturnItemIds.Count == 0)
            {
                return;
            }

            if (!Instance._lootedItems.TryGetValue(profile.ProfileId, out List<string> storedItems))
            {
                storedItems = new List<string>();
                Instance._lootedItems.Add(profile.ProfileId, storedItems);
            }

            foreach (string itemId in trackedReturnItemIds)
            {
                if (!string.IsNullOrWhiteSpace(itemId) && !storedItems.Contains(itemId))
                {
                    storedItems.Add(itemId);
                }
            }

            if (!Instance._followersWithLoot.ContainsKey(profile.ProfileId))
            {
                Instance._followersWithLoot.Add(profile.ProfileId, new Dictionary<string, object> {
                    { "_id" , profile.ProfileId  },
                    { "aid" , profile.AccountId },
                    {
                        "Info" , new Dictionary<string, object>{
                            { "Level", profile.Info.Level },
                            { "MemberCategory", profile.Info.MemberCategory },
                            { "Nickname",  profile.Info.Nickname },
                            { "Side",  profile.Info.Side },
                        }
                    },
                });
            }

            Logger.LogInfo(
                $"[Transit] Restored carried follower return-item tracking for '{profile.Nickname ?? profile.ProfileId}' " +
                $"trackedReturnItemIds={trackedReturnItemIds.Count}.");
        }


        public static Dictionary<string, List<string>> GetStoredEquipment()
        {
            if (Instance?._followersEquipment == null) return new Dictionary<string, List<string>>();
            return Instance._followersEquipment;
        }
    }
}
