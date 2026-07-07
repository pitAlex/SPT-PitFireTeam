using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using JsonType;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class FollowerLootPriceService
    {
        private const float MarketPricesRefreshIntervalSeconds = 300f;

        private static bool _marketPricesRequestInFlight;
        private static float _marketPricesUpdatedAt = -MarketPricesRefreshIntervalSeconds;
        private static Dictionary<string, float>? _marketPrices;

        public static void RequestMarketPricesIfNeeded()
        {
            try
            {
                if (_marketPricesRequestInFlight)
                {
                    return;
                }

                if (_marketPrices != null &&
                    Time.realtimeSinceStartup - _marketPricesUpdatedAt <= MarketPricesRefreshIntervalSeconds)
                {
                    return;
                }

                if (!Singleton<ClientApplication<ISession>>.Instantiated)
                {
                    return;
                }

                ISession session = Singleton<ClientApplication<ISession>>.Instance?.GetClientBackEndSession();
                if (session == null)
                {
                    return;
                }

                _marketPricesRequestInFlight = true;
                session.RagfairGetPrices(new Callback<Dictionary<string, float>>(HandleMarketPricesReceived));
            }
            catch (Exception ex)
            {
                _marketPricesRequestInFlight = false;
                Logger.LogInfo($"[LootCommand] Failed to request market prices: {ex.Message}");
            }
        }

        public static bool PassesPriceThreshold(Item item)
        {
            int min = Mathf.Max(0, pitFireTeam.lootMinimumPrice?.Value ?? 0);
            int max = Mathf.Max(0, pitFireTeam.lootMaximumPrice?.Value ?? 0);

            if (min <= 0 && max <= 0)
            {
                return true;
            }

            double price = CalculateItemTreeRoublePrice(item);
            return (min <= 0 || price >= min) &&
                   (max <= 0 || price <= max);
        }

        public static double CalculateItemTreeRoublePrice(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            try
            {
                double total = 0.0;
                foreach (Item treeItem in CollectDeepItemTree(item))
                {
                    if (ShouldIgnorePriceItem(treeItem))
                    {
                        continue;
                    }

                    total += CalculateSingleItemRoublePrice(treeItem);
                }

                return Math.Max(0.0, Math.Floor(total));
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"[LootCommand] Failed to price loot item '{item.TemplateId}': {ex.Message}");
                return 0.0;
            }
        }

        public static int GetBackpackAndPocketFreeArea(InventoryEquipment equipment)
        {
            if (equipment == null)
            {
                return 0;
            }

            return GetFreeArea(equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem) +
                   GetFreeArea(equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem);
        }

        private static void HandleMarketPricesReceived(Result<Dictionary<string, float>> result)
        {
            _marketPricesRequestInFlight = false;
            if (!result.Succeed || result.Value == null)
            {
                Logger.LogInfo("[LootCommand] Failed to load market prices; body/container loot will use handbook fallback prices.");
                return;
            }

            _marketPrices = new Dictionary<string, float>(result.Value, StringComparer.Ordinal);
            _marketPricesUpdatedAt = Time.realtimeSinceStartup;
        }

        private static IEnumerable<Item> CollectDeepItemTree(Item item)
        {
            if (item == null)
            {
                return Enumerable.Empty<Item>();
            }

            List<Item> items = new List<Item>();
            item.GetAllItemsNonAlloc(items, false, true);
            return items;
        }

        private static bool ShouldIgnorePriceItem(Item item)
        {
            return item == null ||
                   item is InventoryEquipment ||
                   item is PocketsItemClass ||
                   item is BuiltInInsertsItemClass ||
                   string.IsNullOrWhiteSpace(item.TemplateId);
        }

        private static double CalculateSingleItemRoublePrice(Item item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TemplateId))
            {
                return 0.0;
            }

            double unitPrice = GetTemplateRoublePrice(item.TemplateId);
            int count = Mathf.Max(1, item.StackObjectsCount);
            return unitPrice * count;
        }

        private static double GetTemplateRoublePrice(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return 0.0;
            }

            if (string.Equals(templateId, GClass3130.ROUBLE_ID.ToString(), StringComparison.Ordinal) ||
                string.Equals(templateId, GClass3130.ROUBLE_STACK_ID.ToString(), StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (_marketPrices != null &&
                _marketPrices.TryGetValue(templateId, out float marketPrice) &&
                marketPrice > 0f)
            {
                return marketPrice;
            }

            try
            {
                if (Singleton<HandbookClass>.Instantiated)
                {
                    return Singleton<HandbookClass>.Instance.GetBasePrice(new MongoID(templateId));
                }
            }
            catch
            {
                // fall through to zero
            }

            return 0.0;
        }

        private static int GetFreeArea(Item item)
        {
            if (item is not SearchableItemItemClass searchable || searchable.Grids == null)
            {
                return 0;
            }

            int freeArea = GetDirectGridFreeArea(searchable);
            foreach (Item child in searchable.GetAllItems())
            {
                if (child != null && child != searchable && child is SearchableItemItemClass nested)
                {
                    freeArea += GetDirectGridFreeArea(nested);
                }
            }

            return freeArea;
        }

        private static int GetDirectGridFreeArea(SearchableItemItemClass searchable)
        {
            if (searchable?.Grids == null)
            {
                return 0;
            }

            int freeArea = 0;
            foreach (StashGridClass grid in searchable.Grids)
            {
                if (grid == null)
                {
                    continue;
                }

                int usedArea = 0;
                foreach (Item child in grid.Items ?? Enumerable.Empty<Item>())
                {
                    usedArea += GetItemArea(child);
                }

                freeArea += Mathf.Max(0, grid.GridHeight * grid.GridWidth - usedArea);
            }

            return freeArea;
        }

        private static int GetItemArea(Item item)
        {
            if (item == null)
            {
                return 0;
            }

            try
            {
                XYCellSizeStruct size = item.CalculateCellSize();
                return Mathf.Max(1, size.X) * Mathf.Max(1, size.Y);
            }
            catch
            {
                return 1;
            }
        }
    }
}
