using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal partial class GestureCommandAction
    {
        private static float CalculateLootSearchDelaySeconds(int gridCells)
        {
            // Delay scales by sqrt(cells): large containers feel searched without making followers
            // wait as long as the player search mechanic.
            float searchedCells = Mathf.Max(1f, gridCells);
            float delay = LootSearchDelayBaseSeconds + Mathf.Sqrt(searchedCells) * LootSearchDelayPerSqrtCellSeconds;
            return Mathf.Clamp(delay, LootSearchDelayMinSeconds, LootSearchDelayMaxSeconds);
        }

        private static int GetBodyLootSearchGridCells(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                return 0;
            }

            return GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem) +
                   GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem) +
                   GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
        }

        private static int GetSearchableGridCellCount(Item? item)
        {
            if (item is not EFT.InventoryLogic.SearchableItem searchable || searchable.Grids == null)
            {
                return 0;
            }

            int cells = GetDirectGridCellCount(searchable);
            foreach (Item child in searchable.GetAllItems())
            {
                if (child != null && child != searchable && child is EFT.InventoryLogic.SearchableItem nested)
                {
                    cells += GetDirectGridCellCount(nested);
                }
            }

            return cells;
        }

        private static int GetDirectGridCellCount(EFT.InventoryLogic.SearchableItem searchable)
        {
            if (searchable?.Grids == null)
            {
                return 0;
            }

            int cells = 0;
            foreach (EFT.InventoryLogic.Grid grid in searchable.Grids)
            {
                if (grid != null)
                {
                    cells += Mathf.Max(0, grid.GridHeight * grid.GridWidth);
                }
            }

            return cells;
        }

        private static Item? GetBestBodyLootSearchSoundSource(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                return null;
            }

            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets })
            {
                EFT.InventoryLogic.SearchableItem searchable = corpseEquipment.GetSlot(slot)?.ContainedItem as EFT.InventoryLogic.SearchableItem;
                if (searchable != null && !string.IsNullOrWhiteSpace(searchable.SearchSound))
                {
                    return searchable;
                }
            }

            return null;
        }

        private void StartLootSearchSound(Item? soundSource, Vector3 worldPosition)
        {
            StopLootSearchSound();

            try
            {
                if (soundSource is not EFT.InventoryLogic.SearchableItem searchable ||
                    string.IsNullOrWhiteSpace(searchable.SearchSound) ||
                    !Singleton<GUISounds>.Instantiated)
                {
                    PlayLootSearchFallbackSound();
                    return;
                }

                AudioClip clip = Singleton<GUISounds>.Instance.GetLootingClip(searchable.SearchSound);
                if (clip == null)
                {
                    PlayLootSearchFallbackSound();
                    return;
                }

                try
                {
                    BetterAudio audio = Singleton<BetterAudio>.Instance;
                    BetterSource source = audio?.PlayAtPoint(
                        worldPosition,
                        clip,
                        BetterAudio.AudioSourceGroupType.Character,
                        30,
                        1f,
                        EOcclusionTest.None,
                        null,
                        spatialize: true,
                        oneShot: false,
                        autoReleaseSource: false,
                        enabledHighPassFilter: true);

                    if (source != null)
                    {
                        source.Loop = true;
                        activeLootSearchSource = source;
                        return;
                    }
                }
                catch
                {
                    // Fall through to the guaranteed UI audio path below.
                }

                Singleton<GUISounds>.Instance.PlaySound(clip, false, true, 1f);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to play loot search sound: {ex.Message}");
            }
        }

        private void StopLootSearchSound()
        {
            BetterSource source = activeLootSearchSource;
            activeLootSearchSource = null;
            if (source == null)
            {
                return;
            }

            try
            {
                source.Loop = false;
                source.Stop(0f);
                source.Release();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to stop loot search sound: {ex.Message}");
            }
        }

        private static void PlayLootSearchFallbackSound()
        {
            try
            {
                if (Singleton<GUISounds>.Instantiated)
                {
                    EFT.InventoryLogic.Operations.NetworkSearchContentOperation.PlayInstantSearchSound();
                }
            }
            catch
            {
                // best-effort audio only
            }
        }

        private void TryMarkBodyLootSearchedForBoss()
        {
            InventoryEquipment? corpseEquipment = activeBodyLootCorpse?.ItemOwner?.RootItem as InventoryEquipment;
            if (corpseEquipment == null)
            {
                return;
            }

            TryMarkLootTreeSearchedForBoss(corpseEquipment);
        }

        private void TryMarkContainerLootSearchedForBoss()
        {
            EFT.InventoryLogic.SearchableItem? containerRoot = activeLootContainer?.ItemOwner?.Items?.FirstOrDefault() as EFT.InventoryLogic.SearchableItem;
            if (containerRoot == null)
            {
                return;
            }

            TryMarkLootTreeSearchedForBoss(containerRoot);
        }

        private void TryMarkLootTreeSearchedForBoss(Item rootItem)
        {
            IPlayerSearchController? searchController = GamePlayerOwner.MyPlayer?.SearchController;
            if (searchController == null ||
                rootItem == null ||
                IsBossActivelyViewingLootRoot(searchController, rootItem))
            {
                return;
            }

            // Only mark after follower search completes, and never while the player is actively
            // viewing/searching the same tree.
            try
            {
                MarkLootItemTreeSearched(searchController, rootItem, new HashSet<Item>());
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to mark bot-looted tree searched for player: {ex.Message}");
            }
        }

        private static bool IsBossActivelyViewingLootRoot(IPlayerSearchController searchController, Item rootItem)
        {
            try
            {
                if (EFT.UI.Screens.EftScreenManager.Instance.CurrentScreenController is InventoryScreen.InventoryScreenController inventoryScreen &&
                    inventoryScreen.LootItem != null &&
                    IsSameLootTree(inventoryScreen.LootItem, rootItem))
                {
                    return true;
                }
            }
            catch
            {
                // Active search operations below still protect the same-target case if screen
                // state is temporarily unavailable while the loot UI is opening or closing.
            }

            try
            {
                foreach (SearchContentOperation operation in searchController.SearchOperations)
                {
                    if (operation?.Item != null && IsSameLootTree(operation.Item, rootItem))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return true;
            }

            return false;
        }

    }
}
