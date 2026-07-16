using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Settings;
using pitTeam.Modules;
using pitTeam.Patches;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayerIcons;
using SPT.Common.Http;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform descendant = FindChildRecursive(child, childName);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private DefaultUIButton CreateOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton button = Instantiate(playerButton, parent, false);
            button.name = "pitFireTeam_OverlayActionButton";
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one * 0.9f;
            }

            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetIcon(null);
            return button;
        }

        private Button CreateWindowCloseButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = new GameObject($"{name}_Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = GetSocialUiText("RenameClose");
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            label.raycastTarget = false;

            return buttonObject.GetComponent<Button>();
        }

        private TraderScreensGroup ResolveTraderScreensGroupTemplate()
        {
            return Resources.FindObjectsOfTypeAll<TraderScreensGroup>()
                .FirstOrDefault(group =>
                    group != null &&
                    TraderCardsContainerField?.GetValue(group) is Transform);
        }

        private SettingsScreen ResolveSettingsScreenTemplate()
        {
            if (cachedSettingsScreenTemplate != null)
            {
                return cachedSettingsScreenTemplate;
            }

            cachedSettingsScreenTemplate = Resources.FindObjectsOfTypeAll<SettingsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    SettingsScreenGameTabField?.GetValue(screen) is GameSettingsTab);
            return cachedSettingsScreenTemplate;
        }

        private ScrollRectNoDrag ResolveSettingsScrollRectTemplate()
        {
            if (cachedSettingsScrollRectTemplate != null)
            {
                return cachedSettingsScrollRectTemplate;
            }

            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            if (gameSettingsTab == null)
            {
                return null;
            }

            cachedSettingsScrollRectTemplate = gameSettingsTab.GetComponentsInChildren<ScrollRectNoDrag>(true)
                .FirstOrDefault(scrollRect =>
                    scrollRect != null &&
                    scrollRect.content != null &&
                    scrollRect.viewport != null);
            return cachedSettingsScrollRectTemplate;
        }

        private GameSettingsTab ResolveGameSettingsTabTemplate()
        {
            if (cachedGameSettingsTabTemplate != null)
            {
                return cachedGameSettingsTabTemplate;
            }

            SettingsScreen settingsScreen = ResolveSettingsScreenTemplate();
            cachedGameSettingsTabTemplate = SettingsScreenGameTabField?.GetValue(settingsScreen) as GameSettingsTab;
            return cachedGameSettingsTabTemplate;
        }

        private UpdatableToggle ResolveSettingsToggleTemplate()
        {
            if (cachedSettingsToggleTemplate != null)
            {
                return cachedSettingsToggleTemplate;
            }

            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            cachedSettingsToggleTemplate = GameSettingsToggleTemplateField?.GetValue(gameSettingsTab) as UpdatableToggle;
            return cachedSettingsToggleTemplate;
        }

        private RectTransform ResolveSettingsSliderContainerTemplate()
        {
            if (cachedSettingsSliderContainerTemplate != null)
            {
                return cachedSettingsSliderContainerTemplate;
            }

            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            if (gameSettingsTab == null)
            {
                return null;
            }

            Transform sliderRoot = gameSettingsTab.transform.Find("Image/Scroll View/Viewport/Other Settings/Scrolls/FOV");
            if (sliderRoot is RectTransform sliderRect)
            {
                cachedSettingsSliderContainerTemplate = sliderRect;
                return cachedSettingsSliderContainerTemplate;
            }

            NumberSlider fallbackSlider = ResolveSettingsSliderTemplate();
            cachedSettingsSliderContainerTemplate = fallbackSlider?.transform as RectTransform;
            return cachedSettingsSliderContainerTemplate;
        }

        private NumberSlider ResolveSettingsSliderTemplate()
        {
            if (cachedSettingsSliderTemplate != null)
            {
                return cachedSettingsSliderTemplate;
            }

            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            cachedSettingsSliderTemplate = GameSettingsSliderTemplateField?.GetValue(gameSettingsTab) as NumberSlider;
            return cachedSettingsSliderTemplate;
        }

        private TradingPlayerPanel ResolveTradingPlayerPanelTemplate()
        {
            return Resources.FindObjectsOfTypeAll<TradingPlayerPanel>()
                .FirstOrDefault(panel =>
                    panel != null &&
                    TradingPlayerPanelIconField?.GetValue(panel) is PlayerIconImage playerIcon &&
                    playerIcon._pmcPreview != null &&
                    playerIcon._progress != null);
        }

        private Scrollbar ResolveVerticalScrollbarTemplate()
        {
            Scrollbar template = Resources.FindObjectsOfTypeAll<ScrollRectNoDrag>()
                .Select(scrollRect => scrollRect?.verticalScrollbar)
                .FirstOrDefault(scrollbar => scrollbar != null && scrollbar.transform is RectTransform);

            if (template != null)
            {
                return template;
            }

            return Resources.FindObjectsOfTypeAll<Scrollbar>()
                .FirstOrDefault(scrollbar => scrollbar != null && scrollbar.direction == Scrollbar.Direction.BottomToTop);
        }

        private static Scrollbar CreateFallbackScrollbar(RectTransform parent)
        {
            GameObject root = new GameObject("pitFireTeam_SquadControlScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(parent, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(12f, 0f);
            rootRect.anchoredPosition = Vector2.zero;

            Image background = root.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.12f);

            GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(root.transform, false);
            RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
            Stretch(slidingRect);
            slidingRect.offsetMin = new Vector2(0f, 4f);
            slidingRect.offsetMax = new Vector2(0f, -4f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.88f, 0.88f, 0.88f, 0.9f);

            Scrollbar scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI ResolveTemplateLabel()
        {
            return HeaderLabelField?.GetValue(playerButton) as TextMeshProUGUI;
        }

        private void ShiftButtonsBelowPlayer(RectTransform playerRect, float verticalStep)
        {
            foreach (RectTransform sibling in playerButton.transform.parent.Cast<Transform>().Select(t => t as RectTransform).Where(t => t != null))
            {
                if (sibling == playerRect || sibling == squadButton.transform)
                {
                    continue;
                }

                if (Mathf.Abs(sibling.anchoredPosition.x - playerRect.anchoredPosition.x) > 8f)
                {
                    continue;
                }

                if (sibling.anchoredPosition.y >= playerRect.anchoredPosition.y - verticalStep * 0.5f)
                {
                    continue;
                }

                if (sibling.name.StartsWith("pitFireTeam_", StringComparison.Ordinal))
                {
                    continue;
                }

                sibling.anchoredPosition -= new Vector2(0f, verticalStep);
            }
        }

        private void CaptureOriginalButtonPositions(RectTransform playerRect)
        {
            foreach (RectTransform sibling in playerButton.transform.parent.Cast<Transform>().Select(t => t as RectTransform).Where(t => t != null))
            {
                if (sibling == null || sibling == squadButton.transform)
                {
                    continue;
                }

                if (Mathf.Abs(sibling.anchoredPosition.x - playerRect.anchoredPosition.x) > 8f)
                {
                    continue;
                }

                if (sibling.name.StartsWith("pitFireTeam_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!originalButtonPositions.ContainsKey(sibling))
                {
                    originalButtonPositions[sibling] = sibling.anchoredPosition;
                }
            }
        }

        private void RestoreOriginalButtonPositions()
        {
            foreach (KeyValuePair<RectTransform, Vector2> pair in originalButtonPositions)
            {
                if (pair.Key != null)
                {
                    pair.Key.anchoredPosition = pair.Value;
                }
            }
        }

        private float ResolveVerticalStep(RectTransform playerRect, RectTransform tradeRect)
        {
            if (tradeRect != null)
            {
                float delta = playerRect.anchoredPosition.y - tradeRect.anchoredPosition.y;
                if (Mathf.Abs(delta) > 1f)
                {
                    return Mathf.Abs(delta);
                }
            }

            return Mathf.Max(80f, playerRect.rect.height + 10f);
        }

        private Sprite LoadSquadIcon()
        {
            if (squadIconSprite != null)
            {
                return squadIconSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "squad.png"),
                Path.Combine(PluginDirectory, "resources", "squad.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "squad.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                pitFireTeam.Log.LogWarning("[UI] Squad Control icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                pitFireTeam.Log.LogWarning($"[UI] Failed to decode Squad Control icon '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_SquadControlIcon";
            squadIconSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            squadIconSprite.name = "pitFireTeam_SquadControlIcon";
            return squadIconSprite;
        }

        private Sprite LoadRosterTileDiagonalSprite()
        {
            if (rosterTileDiagonalSprite != null)
            {
                return rosterTileDiagonalSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "diagonal_lines.png"),
                Path.Combine(PluginDirectory, "resources", "diagonal_lines.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "diagonal_lines.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                pitFireTeam.Log.LogWarning("[UI] Roster tile diagonal overlay image could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                pitFireTeam.Log.LogWarning($"[UI] Failed to decode roster tile diagonal overlay '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_RosterTileDiagonal";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            rosterTileDiagonalSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                200f,
                0u,
                SpriteMeshType.FullRect);
            rosterTileDiagonalSprite.name = "pitFireTeam_RosterTileDiagonal";
            return rosterTileDiagonalSprite;
        }

        private Sprite LoadAutoJoinBadgeSprite()
        {
            if (autoJoinBadgeSprite != null)
            {
                return autoJoinBadgeSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "auto-join.png"),
                Path.Combine(PluginDirectory, "resources", "auto-join.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "auto-join.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", "auto-join.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", "resources", "auto-join.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                pitFireTeam.Log.LogWarning("[UI] Auto-join badge icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                pitFireTeam.Log.LogWarning($"[UI] Failed to decode auto-join badge icon '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_AutoJoinBadge";
            autoJoinBadgeSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            autoJoinBadgeSprite.name = "pitFireTeam_AutoJoinBadge";
            return autoJoinBadgeSprite;
        }

        private Sprite LoadGroupBadgeSprite()
        {
            if (groupBadgeSprite != null)
            {
                return groupBadgeSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "icon_group.png"),
                Path.Combine(PluginDirectory, "resources", "icon_group.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "icon_group.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", "icon_group.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", "resources", "icon_group.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                return null;
            }

            texture.name = "pitFireTeam_GroupBadge";
            groupBadgeSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            groupBadgeSprite.name = "pitFireTeam_GroupBadge";
            return groupBadgeSprite;
        }

        private static string GetSocialUiText(string key)
        {
            return pitFireTeam.GetSocialUiText(key);
        }
    }
}
