using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Ragfair;
using EFT.UI.Settings;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
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
        private Coroutine settingsScrollRestoreCoroutine;

        private void BuildSettingsPanel()
        {
            if (settingsPanel == null)
            {
                return;
            }

            RectTransform settingsRect = settingsPanel.GetComponent<RectTransform>();
            if (settingsRect == null)
            {
                return;
            }

            float settingsShellHeight = currentRosterShellHeight > 1f
                ? currentRosterShellHeight
                : CalculateRosterShellHeight();

            settingsRect.anchorMin = new Vector2(0.5f, 0.5f);
            settingsRect.anchorMax = new Vector2(0.5f, 0.5f);
            settingsRect.pivot = new Vector2(0.5f, 0.5f);
            settingsRect.sizeDelta = new Vector2(1180f, settingsShellHeight);
            settingsRect.anchoredPosition = new Vector2(0f, -12f);

            for (int index = settingsRect.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsRect.GetChild(index).gameObject);
            }

            settingsScrollRect = null;
            settingsViewport = null;
            settingsContentRoot = null;
            settingsLayoutGroup = null;
            settingsScrollbar = null;
            settingsEntriesBuilt = false;
            StopSettingsScrollRestore();

            CreateScrollableSettingsArea(settingsRect);
            CreateSettingsVersionLabel(settingsRect);
            RebuildSettingsEntries();
        }

        private void CreateSettingsVersionLabel(RectTransform panelRect)
        {
            GameObject labelObject = CreateText("pitFireTeam_SettingsVersionLabel", $"pitFireTeam v{GetPluginVersionText()}", 18f, TextAlignmentOptions.MidlineRight);
            labelObject.transform.SetParent(panelRect, false);
            labelObject.transform.SetAsLastSibling();

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(1f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(1f, 1f);
            labelRect.sizeDelta = new Vector2(320f, 30f);
            labelRect.anchoredPosition = new Vector2(-SettingsViewportSideInset - 28f, -16f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.color = new Color(0.86f, 0.84f, 0.76f, 0.96f);
            label.fontSize = 17f;
            label.fontWeight = FontWeight.Regular;
            label.raycastTarget = false;
        }

        private static string GetPluginVersionText()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
            {
                return "UNKNOWN";
            }

            return version.Revision > 0
                ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private void CreateScrollableSettingsArea(RectTransform panelRect)
        {
            if (TryCreateStockSettingsArea(panelRect))
            {
                return;
            }

            GameObject scrollRootObject = new GameObject("pitFireTeam_SquadControlSettingsScroll", typeof(RectTransform), typeof(ScrollRectNoDrag));
            scrollRootObject.transform.SetParent(panelRect, false);
            RectTransform scrollRoot = scrollRootObject.GetComponent<RectTransform>();
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
            scrollRoot.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRoot, false);
            settingsViewport = viewportObject.GetComponent<RectTransform>();
            settingsViewport.anchorMin = Vector2.zero;
            settingsViewport.anchorMax = Vector2.one;
            settingsViewport.offsetMin = Vector2.zero;
            settingsViewport.offsetMax = new Vector2(-22f, 0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            settingsScrollbar = CreateRosterScrollbar(scrollRoot);
            settingsScrollbar.name = "pitFireTeam_SquadControlSettingsScrollbar";

            GameObject contentObject = new GameObject("pitFireTeam_SquadControlSettingsContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(settingsViewport, false);
            settingsContentRoot = contentObject.GetComponent<RectTransform>();
            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = new Vector2(0f, 0f);

            settingsLayoutGroup = contentObject.GetComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = contentObject.GetComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsScrollRect = scrollRootObject.GetComponent<ScrollRectNoDrag>();
            settingsScrollRect.horizontal = false;
            settingsScrollRect.vertical = true;
            settingsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            settingsScrollRect.scrollSensitivity = 30f;
            settingsScrollRect.inertia = true;
            settingsScrollRect.viewport = settingsViewport;
            settingsScrollRect.content = settingsContentRoot;
            settingsScrollRect.verticalScrollbar = settingsScrollbar;
            settingsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.verticalScrollbarSpacing = 6f;
            settingsScrollRect.AutoZeroing = true;
            settingsScrollRect.Alignment = TextAnchor.UpperLeft;
        }

        private void RebuildSettingsEntries(bool preserveScrollPosition = false)
        {
            if (settingsContentRoot == null)
            {
                return;
            }

            bool raidRestrictedContext = IsRaidRestrictedSettingsContext();
            float targetTopScrollOffset = preserveScrollPosition
                ? GetSettingsTopScrollOffset()
                : 0f;

            CancelShortcutCapture(false);
            ClearLoadoutManagementToggleGroup();
            loadoutManagementToggleSpawners.Clear();
            loadoutManagementFallbackToggles.Clear();
            StopSettingsScrollRestore();
            settingsScrollRect?.StopMovement();

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                GameObject oldEntry = settingsContentRoot.GetChild(index).gameObject;
                oldEntry.SetActive(false);
                Destroy(oldEntry);
            }

            string currentSection = null;
            foreach (SquadSettingEntry setting in BuildSquadSettingsEntries())
            {
                if (!string.Equals(currentSection, setting.SectionTitle, StringComparison.Ordinal))
                {
                    currentSection = setting.SectionTitle;
                    CreateSettingsSectionHeader(currentSection);
                }

                CreateSettingsEntryRow(setting);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsContentRoot);
            if (settingsScrollRect != null)
            {
                SetSettingsTopScrollOffset(targetTopScrollOffset);
                if (preserveScrollPosition)
                {
                    settingsScrollRestoreCoroutine = StartCoroutine(
                        RestoreSettingsScrollPositionAfterLayout(targetTopScrollOffset));
                }
            }

            settingsEntriesBuilt = true;
            settingsEntriesBuiltForRaidRestrictedContext = raidRestrictedContext;
        }

        private float GetSettingsTopScrollOffset()
        {
            if (settingsContentRoot == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, settingsContentRoot.anchoredPosition.y);
        }

        private void SetSettingsTopScrollOffset(float targetTopScrollOffset)
        {
            if (settingsScrollRect == null || settingsContentRoot == null || settingsViewport == null)
            {
                return;
            }

            float maxTopScrollOffset = Mathf.Max(
                0f,
                settingsContentRoot.rect.height - settingsViewport.rect.height);
            Vector2 anchoredPosition = settingsContentRoot.anchoredPosition;
            anchoredPosition.y = Mathf.Clamp(targetTopScrollOffset, 0f, maxTopScrollOffset);
            settingsScrollRect.StopMovement();
            settingsScrollRect.SetContentAnchoredPosition(anchoredPosition);
        }

        private IEnumerator RestoreSettingsScrollPositionAfterLayout(float targetTopScrollOffset)
        {
            yield return null;

            if (settingsScrollRect == null || settingsContentRoot == null)
            {
                settingsScrollRestoreCoroutine = null;
                yield break;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsContentRoot);
            SetSettingsTopScrollOffset(targetTopScrollOffset);
            settingsScrollRestoreCoroutine = null;
        }

        private void StopSettingsScrollRestore()
        {
            if (settingsScrollRestoreCoroutine == null)
            {
                return;
            }

            StopCoroutine(settingsScrollRestoreCoroutine);
            settingsScrollRestoreCoroutine = null;
        }

        private bool SettingsEntriesNeedRebuildForCurrentContext()
        {
            if (settingsContentRoot == null || !settingsEntriesBuilt || settingsContentRoot.childCount == 0)
            {
                return true;
            }

            return settingsEntriesBuiltForRaidRestrictedContext != IsRaidRestrictedSettingsContext();
        }

        private void EnsureSettingsEntriesForCurrentContext()
        {
            if (SettingsEntriesNeedRebuildForCurrentContext())
            {
                RebuildSettingsEntries();
            }
        }

        private void QueueRaidSettingsWarmup()
        {
            if (settingsWarmupCoroutine != null || !SettingsEntriesNeedRebuildForCurrentContext())
            {
                return;
            }

            settingsWarmupCoroutine = StartCoroutine(WarmRaidSettingsEntriesCoroutine());
        }

        private IEnumerator WarmRaidSettingsEntriesCoroutine()
        {
            yield return null;
            settingsWarmupCoroutine = null;

            if (raidSettingsOverlayActive || !IsRaidRestrictedSettingsContext())
            {
                yield break;
            }

            EnsureSettingsEntriesForCurrentContext();
        }

        private IEnumerable<SquadSettingEntry> BuildSquadSettingsEntries()
        {
            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.baseSettings),
                pitFireTeam.spawnPoint,
                pitFireTeam.englishBear,
                pitFireTeam.pingRadioVolume,
                pitFireTeam.pingTime,
                pitFireTeam.statusReportHighlightColor,
                pitFireTeam.statusReportHighlight,
                pitFireTeam.statusReportHealthColoring,
                pitFireTeam.statusReportFullHealthColor,
                pitFireTeam.statusReportMediumHealthColor,
                pitFireTeam.statusReportLowHealthColor,
                pitFireTeam.statusReportAlwaysHighlight,
                pitFireTeam.statusReportShowName,
                pitFireTeam.statusReportShowDistance,
                pitFireTeam.statusReportShowHealth,
                pitFireTeam.statusReportShowTactic,
                pitFireTeam.statusReportShowCombatStatus))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.followSettings),
                pitFireTeam.patrolRadius,
                pitFireTeam.followDistance,
                pitFireTeam.goToDistance))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.combatSettings),
                pitFireTeam.botGrenades,
                pitFireTeam.regroupRadius,
                pitFireTeam.enemyMarker,
                pitFireTeam.enemyMarkerAlertColor,
                pitFireTeam.enemyMarkerVisibleColor,
                pitFireTeam.statusSound,
                pitFireTeam.enemyRemember,
                pitFireTeam.scanDistance,
                pitFireTeam.botTalk))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.raidSettings),
                pitFireTeam.teamEscape,
                pitFireTeam.teamEscapeUseAnyExtract,
                pitFireTeam.pickupEnabled,
                pitFireTeam.tieredPickup,
                pitFireTeam.maximumPickup,
                pitFireTeam.recruitPickup,
                pitFireTeam.npcSendMessage,
                pitFireTeam.pitFireTeamFLAG,
                pitFireTeam.badGuy,
                pitFireTeam.factionHostilities,
                pitFireTeam.pmcArmbands))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.lootingSettings),
                pitFireTeam.lootMinimumPrice,
                pitFireTeam.lootMaximumPrice,
                pitFireTeam.lootFilterFood,
                pitFireTeam.lootFilterMeds,
                pitFireTeam.lootFilterValuables,
                pitFireTeam.lootFilterWeapons,
                pitFireTeam.lootFilterGear,
                pitFireTeam.lootAllowGearSwapping))
            {
                yield return setting;
            }

            if (!IsRaidRestrictedSettingsContext())
            {
                string loadoutSection = pitFireTeam.GetLanguageText(language => language.loadoutManagementSettings);
                yield return new SquadSettingEntry { SectionTitle = loadoutSection, LoadoutMode = LoadoutManagementMode.Simple };
                yield return new SquadSettingEntry { SectionTitle = loadoutSection, LoadoutMode = LoadoutManagementMode.Restricted };
                if (pitFireTeam.loadoutManagementMode?.Value == LoadoutManagementMode.Restricted &&
                    pitFireTeam.restrictedGearMaintenance != null)
                {
                    yield return new SquadSettingEntry
                    {
                        SectionTitle = loadoutSection,
                        Entry = pitFireTeam.restrictedGearMaintenance
                    };
                }

                yield return new SquadSettingEntry { SectionTitle = loadoutSection, LoadoutMode = LoadoutManagementMode.Immersive };
                yield return new SquadSettingEntry { SectionTitle = loadoutSection, LoadoutMode = LoadoutManagementMode.Extreme };
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.inputSettings),
                pitFireTeam.hideUnsupportedCommands,
                pitFireTeam.pingKey,
                pitFireTeam.contactKey,
                pitFireTeam.overThereKey))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                pitFireTeam.GetLanguageText(language => language.miscSettings),
                pitFireTeam.teleportKey,
                pitFireTeam.healKey,
                pitFireTeam.heatlhMultiplier,
                pitFireTeam.botPrefetch))
            {
                yield return setting;
            }
        }

        internal void RefreshLocalizedText()
        {
            EnsureSquadButton();
            EnsureRaidSettingsButton();

            if (screenRoot != null && screenRoot.activeInHierarchy)
            {
                SetStandaloneTitle(GetSocialUiText(raidSettingsOverlayActive ? "SquadControlRaidSettingsTitle" : "SquadControlTitle"));
            }

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.SetRawText(GetSocialUiText("SquadControlBack"), standaloneCloseButton.HeaderSize);
            }

            if (addTeammateButton != null)
            {
                addTeammateButton.SetRawText(GetSocialUiText("AddTeammate"), addTeammateButton.HeaderSize);
            }

            if (emptyRosterLabel != null)
            {
                emptyRosterLabel.text = GetSocialUiText("SquadControlEmptyRoster");
            }

            if (settingsContentRoot != null)
            {
                RebuildSettingsEntries();
            }

            if (rosterGridRoot != null)
            {
                RebuildRosterTiles();
            }
        }

        private static IEnumerable<SquadSettingEntry> BuildSettingsSection(string title, params ConfigEntryBase[] entries)
        {
            return GetSettingsEntries(entries)
                .Select(entry => new SquadSettingEntry
                {
                    SectionTitle = title,
                    Entry = entry
                });
        }

        private static IEnumerable<ConfigEntryBase> GetSettingsEntries(params ConfigEntryBase[] entries)
        {
            return entries.Where(entry => entry != null && !IsBetaHiddenSetting(entry));
        }

        private static bool IsBetaHiddenSetting(ConfigEntryBase entry)
        {
            return false;
        }

        private void CreateSettingsSectionHeader(string title)
        {
            GameObject headerObject = new GameObject($"pitFireTeam_SettingsHeader_{title}", typeof(RectTransform), typeof(LayoutElement));
            headerObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = headerObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsHeaderHeight;
            layout.flexibleWidth = 1f;

            GameObject labelObject = CreateText("Label", title.ToUpperInvariant(), 22f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(headerObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(0f, -6f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.color = new Color(0.92f, 0.82f, 0.63f, 1f);
            label.fontWeight = FontWeight.Bold;
            label.fontSize = 20f;

            GameObject dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(headerObject.transform, false);
            RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.offsetMin = new Vector2(6f, 0f);
            dividerRect.offsetMax = new Vector2(-6f, 1f);

            Image divider = dividerObject.GetComponent<Image>();
            divider.color = new Color(0.54f, 0.45f, 0.29f, 0.45f);
            divider.raycastTarget = false;
        }

        private void CreateSettingsEntryRow(SquadSettingEntry setting)
        {
            if (setting?.LoadoutMode is LoadoutManagementMode loadoutMode)
            {
                CreateLoadoutManagementEntryRow(loadoutMode);
                return;
            }

            CreateSettingsEntryRow(setting?.Entry);
        }

        private void CreateSettingsEntryRow(ConfigEntryBase entry)
        {
            if (entry == null)
            {
                return;
            }

            bool disabledDuringRaid = ShouldDisableSettingDuringRaid(entry);
            GameObject rowObject = new GameObject(
                $"pitFireTeam_Setting_{SanitizeName(entry.Definition.Key)}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            rowObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = rowObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsRowHeight;
            layout.flexibleWidth = 1f;

            Image background = rowObject.GetComponent<Image>();
            background.color = new Color(0.07f, 0.07f, 0.07f, 0.84f);
            background.raycastTarget = true;

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, SettingsRowHeight);

            CreateSettingsRowChrome(rowObject.transform);
            if (IsNestedSettingsEntry(entry))
            {
                background.enabled = false;
                Transform topLine = rowObject.transform.Find("TopLine");
                if (topLine != null)
                {
                    topLine.gameObject.SetActive(false);
                }
            }

            GameObject nameObject = CreateText("Name", GetSettingDisplayName(entry), 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(rowObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(22f, -34f);
            nameRect.offsetMax = new Vector2(-418f, -8f);

            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;
            if (disabledDuringRaid)
            {
                nameLabel.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            }

            GameObject descriptionObject = CreateText("Description", GetSettingDescription(entry), 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(rowObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(22f, 16f);
            descriptionRect.offsetMax = new Vector2(-418f, -38f);

            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = disabledDuringRaid
                ? new Color(0.46f, 0.46f, 0.46f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(rowObject.transform, false);
            controlRect.sizeDelta = new Vector2(360f, 72f);
            controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset + controlRect.sizeDelta.x), 0f);

            if (entry.SettingType == typeof(bool))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.sizeDelta = new Vector2(120f, 72f);
                controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset - 20f), 0f);
                CreateBoolSettingControl(controlRect, entry, !disabledDuringRaid);
                AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
                return;
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                if (IsLootPriceSetting(entry))
                {
                    controlRect.anchorMin = new Vector2(1f, 0.5f);
                    controlRect.anchorMax = new Vector2(1f, 0.5f);
                    controlRect.pivot = new Vector2(1f, 0.5f);
                }

                controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, SettingsSliderVerticalOffset);
                CreateIntSliderSettingControl(controlRect, entry, acceptableRange, !disabledDuringRaid);
                AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
                return;
            }

            if (TryGetHexColorDefault(entry, out string defaultHex))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.sizeDelta = new Vector2(270f, 72f);
                controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, 0f);
                CreateHexColorSettingControl(
                    controlRect,
                    entry as ConfigEntry<string>,
                    defaultHex,
                    !disabledDuringRaid);
                AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
                return;
            }

            if (entry.SettingType == typeof(KeyboardShortcut))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.anchoredPosition = new Vector2(-SettingsShortcutRightInset, 0f);
                CreateShortcutSettingControl(controlRect, entry as ConfigEntry<KeyboardShortcut>, !disabledDuringRaid);
                AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
                return;
            }

            CreateReadOnlySettingControl(controlRect, entry.BoxedValue?.ToString() ?? string.Empty);
            AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
        }

        private static bool IsNestedSettingsEntry(ConfigEntryBase entry)
        {
            return entry == pitFireTeam.restrictedGearMaintenance
                || entry == pitFireTeam.statusReportHealthColoring
                || entry == pitFireTeam.statusReportFullHealthColor
                || entry == pitFireTeam.statusReportMediumHealthColor
                || entry == pitFireTeam.statusReportLowHealthColor
                || entry == pitFireTeam.statusReportAlwaysHighlight;
        }

        private void CreateLoadoutManagementEntryRow(LoadoutManagementMode mode)
        {
            bool disabledDuringRaid = IsRaidActive();
            GameObject rowObject = new GameObject(
                $"pitFireTeam_Setting_LoadoutManagement_{mode}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            rowObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = rowObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsRowHeight;
            layout.flexibleWidth = 1f;

            Image background = rowObject.GetComponent<Image>();
            background.color = new Color(0.07f, 0.07f, 0.07f, 0.84f);
            background.raycastTarget = true;

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, SettingsRowHeight);

            CreateSettingsRowChrome(rowObject.transform);

            Dictionary<string, string> languageEntry = GetLoadoutManagementLanguageEntry(mode);
            string displayName = GetLoadoutManagementDisplayName(mode, languageEntry);
            string description = GetLoadoutManagementDescription(languageEntry);

            GameObject nameObject = CreateText("Name", displayName, 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(rowObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(22f, -34f);
            nameRect.offsetMax = new Vector2(-418f, -8f);

            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;
            if (disabledDuringRaid)
            {
                nameLabel.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            }

            GameObject descriptionObject = CreateText("Description", description, 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(rowObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(22f, 16f);
            descriptionRect.offsetMax = new Vector2(-418f, -38f);

            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = disabledDuringRaid
                ? new Color(0.46f, 0.46f, 0.46f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(rowObject.transform, false);
            controlRect.anchorMin = new Vector2(1f, 0.5f);
            controlRect.anchorMax = new Vector2(1f, 0.5f);
            controlRect.pivot = new Vector2(1f, 0.5f);
            controlRect.sizeDelta = new Vector2(186f, 48f);
            controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, 0f);

            CreateLoadoutManagementRadioControl(controlRect, mode, displayName, !disabledDuringRaid);
            AddRaidDisabledTooltipOverlay(rowObject, disabledDuringRaid);
        }

        private static void CreateSettingsRowChrome(Transform rowTransform)
        {
            GameObject topLineObject = new GameObject("TopLine", typeof(RectTransform), typeof(Image));
            topLineObject.transform.SetParent(rowTransform, false);

            RectTransform topLineRect = topLineObject.GetComponent<RectTransform>();
            topLineRect.anchorMin = new Vector2(0f, 1f);
            topLineRect.anchorMax = new Vector2(1f, 1f);
            topLineRect.pivot = new Vector2(0.5f, 1f);
            topLineRect.offsetMin = new Vector2(12f, -1f);
            topLineRect.offsetMax = new Vector2(-12f, 0f);

            Image topLine = topLineObject.GetComponent<Image>();
            topLine.color = new Color(0.68f, 0.58f, 0.38f, 0.14f);
            topLine.raycastTarget = false;

            GameObject bottomLineObject = new GameObject("BottomLine", typeof(RectTransform), typeof(Image));
            bottomLineObject.transform.SetParent(rowTransform, false);

            RectTransform bottomLineRect = bottomLineObject.GetComponent<RectTransform>();
            bottomLineRect.anchorMin = new Vector2(0f, 0f);
            bottomLineRect.anchorMax = new Vector2(1f, 0f);
            bottomLineRect.pivot = new Vector2(0.5f, 0f);
            bottomLineRect.offsetMin = new Vector2(12f, 0f);
            bottomLineRect.offsetMax = new Vector2(-12f, 1f);

            Image bottomLine = bottomLineObject.GetComponent<Image>();
            bottomLine.color = new Color(0f, 0f, 0f, 0.42f);
            bottomLine.raycastTarget = false;
        }

        private bool ShouldDisableSettingDuringRaid(ConfigEntryBase entry)
        {
            return IsRaidRestrictedSettingsContext() && RequiresRaidRestart(entry);
        }

        private static bool RequiresRaidRestart(ConfigEntryBase entry)
        {
            return entry == pitFireTeam.spawnPoint
                || entry == pitFireTeam.englishBear
                || entry == pitFireTeam.enemyRemember
                || entry == pitFireTeam.heatlhMultiplier
                || entry == pitFireTeam.pitFireTeamFLAG
                || entry == pitFireTeam.badGuy
                || entry == pitFireTeam.factionHostilities
                || entry == pitFireTeam.pmcArmbands
                || entry == pitFireTeam.botPrefetch
                || entry == pitFireTeam.battleRecorderEnabled;
        }

        private bool IsRaidRestrictedSettingsContext()
        {
            if (IsHideoutGameActive())
            {
                return false;
            }

            if (raidSettingsOverlayActive)
            {
                return true;
            }

            if (IsMenuInRaidRestrictedState())
            {
                return true;
            }

            return IsRaidActive();
        }

        private bool IsMenuInRaidRestrictedState()
        {
            try
            {
                if (menuScreen == null)
                {
                    return false;
                }

                return hideScreenButton != null && hideScreenButton.gameObject.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHideoutGameActive()
        {
            try
            {
                return Singleton<AbstractGame>.Instantiated
                    && Singleton<AbstractGame>.Instance is HideoutGame;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRaidActive()
        {
            try
            {
                if (IsHideoutGameActive())
                {
                    return false;
                }

                if (LocalGameCtorPatch.Instance != null)
                {
                    return true;
                }

                return Singleton<AbstractGame>.Instantiated
                    && Singleton<GameWorld>.Instantiated
                    && Singleton<GameWorld>.Instance != null
                    && GamePlayerOwner.MyPlayer != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsGameRaidActive()
        {
            return IsRaidActive();
        }

        private void AddRaidDisabledTooltipOverlay(GameObject rowObject, bool disabledDuringRaid)
        {
            if (!disabledDuringRaid || rowObject == null)
            {
                return;
            }

            GameObject tooltipAreaObject = new GameObject("pitFireTeam_SettingDisabledDuringRaidTooltip", typeof(RectTransform), typeof(Image));
            tooltipAreaObject.transform.SetParent(rowObject.transform, false);

            RectTransform tooltipAreaRect = tooltipAreaObject.GetComponent<RectTransform>();
            tooltipAreaRect.anchorMin = Vector2.zero;
            tooltipAreaRect.anchorMax = Vector2.one;
            tooltipAreaRect.offsetMin = Vector2.zero;
            tooltipAreaRect.offsetMax = Vector2.zero;
            tooltipAreaRect.localScale = Vector3.one;

            Image tooltipAreaImage = tooltipAreaObject.GetComponent<Image>();
            tooltipAreaImage.color = new Color(0f, 0f, 0f, 0f);
            tooltipAreaImage.raycastTarget = true;

            ProfileTooltipHoverController tooltipHover = tooltipAreaObject.AddComponent<ProfileTooltipHoverController>();
            tooltipHover.Configure(GetSocialUiText("SettingsUnavailableDuringRaid"));
        }

        private static void SetSettingsControlInteractable(Transform controlRoot, bool interactable)
        {
            if (controlRoot == null)
            {
                return;
            }

            CanvasGroup canvasGroup = controlRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = controlRoot.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = interactable ? 1f : 0.42f;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;

            foreach (Selectable selectable in controlRoot.GetComponentsInChildren<Selectable>(true))
            {
                selectable.interactable = interactable;
            }

            foreach (TMP_InputField inputField in controlRoot.GetComponentsInChildren<TMP_InputField>(true))
            {
                inputField.readOnly = !interactable;
                inputField.interactable = interactable;
            }
        }

        private void CreateBoolSettingControl(RectTransform parent, ConfigEntryBase entry, bool interactable)
        {
            UpdatableToggle toggle = CloneStockToggle(parent);
            if (toggle == null)
            {
                Toggle fallbackToggle = CreateBasicToggle(parent);
                bool fallbackValue = entry.BoxedValue is bool fallbackBool && fallbackBool;
                fallbackToggle.isOn = fallbackValue;
                SetSettingsControlInteractable(fallbackToggle.transform, interactable);
                if (interactable)
                {
                    fallbackToggle.onValueChanged.AddListener(isOn =>
                    {
                        entry.BoxedValue = isOn;
                        pitFireTeam.Instance?.Config.Save();
                    });
                }
                return;
            }

            bool currentValue = entry.BoxedValue is bool boolValue && boolValue;
            toggle.UpdateValue(currentValue, false, null, null);
            SetSettingsControlInteractable(toggle.transform, interactable);
            toggle.enabled = interactable;
            if (interactable)
            {
                toggle.Bind(isOn =>
                {
                    entry.BoxedValue = isOn;
                    pitFireTeam.Instance?.Config.Save();
                });
            }
        }

        private void ClearLoadoutManagementToggleGroup()
        {
            loadoutManagementToggleGroup = null;
            Transform parent = settingsPanel != null ? settingsPanel.transform : null;
            if (parent == null)
            {
                return;
            }

            Transform existing = parent.Find("pitFireTeam_LoadoutManagementToggleGroup");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }
        }

        private void CreateLoadoutManagementRadioControl(RectTransform parent, LoadoutManagementMode mode, string label, bool interactable)
        {
            Image hoverBackground = CreateLoadoutManagementHoverBackground(parent);
            UIAnimatedToggleSpawner toggle = CloneLoadoutManagementToggle(parent);
            if (toggle == null)
            {
                Toggle fallbackToggle = CreateBasicToggle(parent);
                fallbackToggle.SetIsOnWithoutNotify(pitFireTeam.loadoutManagementMode?.Value == mode);
                loadoutManagementFallbackToggles[mode] = fallbackToggle;
                SetSettingsControlInteractable(fallbackToggle.transform, interactable);
                CreateLoadoutManagementClickOverlay(parent, fallbackToggle.transform as RectTransform, hoverBackground, mode, interactable);
                return;
            }

            RectTransform rect = toggle.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(0f, 42f);
                rect.localScale = Vector3.one * 0.86f;
            }

            CanvasGroup canvasGroup = toggle.GetComponent<CanvasGroup>() ?? toggle.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.alpha = interactable ? 1f : 0.42f;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
            AnimatedToggleCanvasGroupField?.SetValue(toggle, canvasGroup);

            ToggleGroup group = EnsureLoadoutManagementToggleGroup();
            toggle.SpawnableToggle.method_1(group);

            foreach (TextMeshProUGUI text in toggle.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = label.ToUpperInvariant();
                text.overflowMode = TextOverflowModes.Ellipsis;
            }

            toggle.SetActive(true);
            toggle.SpawnableToggle.Interactable = interactable;

            if (toggle.SpawnedObject != null)
            {
                toggle.SpawnedObject.group = group;
                toggle.SpawnedObject.interactable = interactable;
                if (interactable)
                {
                    toggle.SpawnedObject.OnMouseDown += () =>
                    {
                        Modules.Logger.LogInfo($"[UI] Loadout management toggle clicked: {mode}");
                        RequestLoadoutManagementModeChange(mode);
                    };
                    toggle.SpawnedObject.onValueChanged.AddListener(isOn =>
                    {
                        Modules.Logger.LogInfo($"[UI] Loadout management toggle value changed: {mode}={isOn}");
                    });
                }
            }

            toggle.ToggleSilently(pitFireTeam.loadoutManagementMode?.Value == mode);
            loadoutManagementToggleSpawners[mode] = toggle;
            SetSettingsControlInteractable(toggle.transform, interactable);
            CreateLoadoutManagementClickOverlay(parent, toggle.transform as RectTransform, hoverBackground, mode, interactable);
        }

        private void CreateLoadoutManagementClickOverlay(RectTransform parent, RectTransform hoverTarget, Image hoverBackground, LoadoutManagementMode mode, bool interactable)
        {
            GameObject overlayObject = new GameObject("pitFireTeam_LoadoutManagementClickOverlay", typeof(RectTransform), typeof(Image));
            overlayObject.transform.SetParent(parent, false);

            RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayObject.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.001f);
            overlayImage.raycastTarget = interactable;

            LoadoutModeToggleHoverController hoverController = overlayObject.AddComponent<LoadoutModeToggleHoverController>();
            hoverController.Configure(hoverTarget, hoverBackground);
            if (interactable)
            {
                hoverController.OnClick = _ =>
                {
                    Modules.Logger.LogInfo($"[UI] Loadout management toggle clicked: {mode}");
                    RequestLoadoutManagementModeChange(mode);
                };
            }
        }

        private static Image CreateLoadoutManagementHoverBackground(RectTransform parent)
        {
            GameObject backgroundObject = new GameObject("HoverBackground", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(parent, false);

            RectTransform rect = backgroundObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-8f, 5f);
            rect.offsetMax = new Vector2(8f, -5f);
            rect.localScale = Vector3.one;
            backgroundObject.transform.SetAsFirstSibling();

            Image image = backgroundObject.GetComponent<Image>();
            image.color = new Color(0.46f, 0.38f, 0.22f, 0.34f);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private UIAnimatedToggleSpawner CloneLoadoutManagementToggle(RectTransform parent)
        {
            UIAnimatedToggleSpawner template = ResolveLoadoutManagementToggleTemplate();
            if (template == null)
            {
                return null;
            }

            UIAnimatedToggleSpawner toggle = Instantiate(template, parent, false);
            toggle.name = "pitFireTeam_LoadoutManagementModeToggle";
            toggle.gameObject.SetActive(true);
            return toggle;
        }

        private UIAnimatedToggleSpawner ResolveLoadoutManagementToggleTemplate()
        {
            return Resources.FindObjectsOfTypeAll<RagfairScreen>()
                .Select(screen => RagfairAllOffersToggleField?.GetValue(screen) as UIAnimatedToggleSpawner)
                .FirstOrDefault(toggle => toggle != null);
        }

        private ToggleGroup EnsureLoadoutManagementToggleGroup()
        {
            if (loadoutManagementToggleGroup != null)
            {
                return loadoutManagementToggleGroup;
            }

            Transform parent = settingsPanel != null ? settingsPanel.transform : settingsContentRoot;
            GameObject groupObject = new GameObject("pitFireTeam_LoadoutManagementToggleGroup", typeof(RectTransform), typeof(ToggleGroup));
            groupObject.transform.SetParent(parent, false);
            groupObject.SetActive(true);

            loadoutManagementToggleGroup = groupObject.GetComponent<ToggleGroup>();
            loadoutManagementToggleGroup.allowSwitchOff = false;
            return loadoutManagementToggleGroup;
        }

        private void RequestLoadoutManagementModeChange(LoadoutManagementMode mode)
        {
            if (pitFireTeam.loadoutManagementMode == null)
            {
                return;
            }

            LoadoutManagementMode previousMode = pitFireTeam.loadoutManagementMode.Value;
            if (previousMode == mode)
            {
                Modules.Logger.LogInfo($"[UI] Loadout management mode '{mode}' is already selected.");
                return;
            }

            Modules.Logger.LogInfo($"[UI] Loadout management mode change requested: {previousMode} -> {mode}");
            if (previousMode == LoadoutManagementMode.Simple && mode != LoadoutManagementMode.Simple)
            {
                ShowLoadoutManagementConfirmOverlay(mode);
                return;
            }

            ApplyLoadoutManagementModeChange(mode);
        }

        private void ApplyLoadoutManagementModeChange(LoadoutManagementMode mode)
        {
            if (pitFireTeam.loadoutManagementMode == null)
            {
                CloseLoadoutManagementConfirmOverlay();
                return;
            }

            pitFireTeam.loadoutManagementMode.Value = mode;
            pitFireTeam.Instance?.Config.Save();
            Task serverSyncTask = pitFireTeam.SyncServerSettingsNowAsync();
            CloseLoadoutManagementConfirmOverlay();
            RebuildSettingsEntries(preserveScrollPosition: true);
            RefreshRosterPortraitsAfterLoadoutManagementSync(serverSyncTask);
        }

        private void RefreshRosterPortraitsAfterLoadoutManagementSync(Task serverSyncTask)
        {
            if (pitFireTeam.Instance == null)
            {
                RequestRosterRefreshOnNextInject();
                return;
            }

            if (loadoutManagementRosterRefreshCoroutine != null)
            {
                pitFireTeam.Instance.StopCoroutine(loadoutManagementRosterRefreshCoroutine);
                loadoutManagementRosterRefreshCoroutine = null;
            }

            loadoutManagementRosterRefreshCoroutine = pitFireTeam.Instance.StartCoroutine(RefreshRosterPortraitsAfterLoadoutManagementSyncCoroutine(serverSyncTask));
        }

        private IEnumerator RefreshRosterPortraitsAfterLoadoutManagementSyncCoroutine(Task serverSyncTask)
        {
            while (serverSyncTask != null && !serverSyncTask.IsCompleted)
            {
                yield return null;
            }

            loadoutManagementRosterRefreshCoroutine = null;

            if (serverSyncTask != null && serverSyncTask.IsFaulted)
            {
                pitFireTeam.Log.LogWarning("[UI] Loadout management server sync completed with an error before roster portrait refresh.");
            }

            if (rosterGridRoot == null)
            {
                RequestRosterRefreshOnNextInject();
                yield break;
            }

            Modules.Logger.LogInfo("[UI] Refreshing roster portraits after loadout management mode switch.");
            RebuildRosterTiles();
        }

        private void UpdateLoadoutManagementRadioStates()
        {
            LoadoutManagementMode current =
                pitFireTeam.loadoutManagementMode?.Value ??
                pitFireTeam.DefaultLoadoutManagementMode;

            foreach (KeyValuePair<LoadoutManagementMode, UIAnimatedToggleSpawner> pair in loadoutManagementToggleSpawners)
            {
                if (pair.Value != null)
                {
                    pair.Value.ToggleSilently(pair.Key == current);
                }
            }

            foreach (KeyValuePair<LoadoutManagementMode, Toggle> pair in loadoutManagementFallbackToggles)
            {
                if (pair.Value != null)
                {
                    pair.Value.SetIsOnWithoutNotify(pair.Key == current);
                }
            }
        }

        private void ShowLoadoutManagementConfirmOverlay(LoadoutManagementMode mode)
        {
            CloseLoadoutManagementConfirmOverlay(rebuild: false);

            Transform overlayParent = settingsPanel?.transform.parent ?? screenRoot?.transform;
            if (overlayParent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Loadout management confirmation overlay could not open: no overlay parent was available.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_LoadoutManagementConfirmOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_LoadoutManagementConfirmPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(680f, 204f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_LoadoutManagementConfirmHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -28f);
            headerRect.offsetMax = new Vector2(0f, 0f);

            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.06f, 0.06f, 0.06f, 1f);
            headerImage.raycastTarget = true;

            GameObject titleObject = CreateText(
                "pitFireTeam_LoadoutManagementConfirmTitle",
                GetSocialUiText("LoadoutManagementConfirmTitle").ToUpperInvariant(),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateWindowCloseButton(header.transform, "pitFireTeam_LoadoutManagementConfirmCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(() => CloseLoadoutManagementConfirmOverlay());

            GameObject bodyObject = CreateText(
                "pitFireTeam_LoadoutManagementConfirmBody",
                GetSocialUiText("LoadoutManagementConfirmPrompt"),
                24f,
                TextAlignmentOptions.Center);
            RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.SetParent(panel.transform, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(28f, 72f);
            bodyRect.offsetMax = new Vector2(-28f, -42f);

            TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

            DefaultUIButton confirmButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            confirmButton.SetRawText(GetSocialUiText("LoadoutManagementConfirm"), 22);
            confirmButton.OnClick.RemoveAllListeners();
            confirmButton.OnClick.AddListener(() => ApplyLoadoutManagementModeChange(mode));

            loadoutManagementConfirmOverlay = overlayRoot;
            Modules.Logger.LogInfo($"[UI] Loadout management confirmation overlay opened for mode: {mode}");
        }

        private void CloseLoadoutManagementConfirmOverlay(bool rebuild = false)
        {
            if (loadoutManagementConfirmOverlay != null)
            {
                Destroy(loadoutManagementConfirmOverlay);
                loadoutManagementConfirmOverlay = null;
            }

            if (rebuild)
            {
                RebuildSettingsEntries();
            }
        }

        private void CreateIntSliderSettingControl(RectTransform parent, ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange, bool interactable)
        {
            if (IsLootPriceSetting(entry))
            {
                CreateIntNumericInputSettingControl(parent, entry, acceptableRange, interactable);
                return;
            }

            NumberSlider slider = CloneStockNumberSlider(parent);
            if (slider == null)
            {
                RectTransform sliderRoot = new GameObject("SliderRoot", typeof(RectTransform)).GetComponent<RectTransform>();
                sliderRoot.SetParent(parent, false);
                Stretch(sliderRoot);

                Slider fallbackSlider = CreateBasicSlider(sliderRoot, out TextMeshProUGUI valueLabel);
                fallbackSlider.minValue = acceptableRange.MinValue;
                fallbackSlider.maxValue = acceptableRange.MaxValue;
                fallbackSlider.wholeNumbers = true;
                fallbackSlider.value = Convert.ToSingle(entry.BoxedValue);
                valueLabel.text = entry.BoxedValue?.ToString() ?? "0";
                SetSettingsControlInteractable(sliderRoot, interactable);

                if (interactable)
                {
                    fallbackSlider.onValueChanged.AddListener(value =>
                    {
                        int boxed = Mathf.RoundToInt(value);
                        if (Equals(entry.BoxedValue, boxed))
                        {
                            return;
                        }

                        entry.BoxedValue = boxed;
                        valueLabel.text = boxed.ToString();
                        pitFireTeam.Instance?.Config.Save();
                    });
                }
                return;
            }

            slider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            slider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            SetSettingsControlInteractable(slider.transform, interactable);
            slider.enabled = interactable;
            if (interactable)
            {
                slider.Bind(value =>
                {
                    int boxed = Mathf.RoundToInt(value);
                    if (Equals(entry.BoxedValue, boxed))
                    {
                        return;
                    }

                    entry.BoxedValue = boxed;
                    pitFireTeam.Instance?.Config.Save();
                });
            }
        }

        private void CreateIntNumericInputSettingControl(RectTransform parent, ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange, bool interactable)
        {
            TMP_InputField input = CloneStockNumberInput(parent) ?? CreateBasicNumberInput(parent);
            if (input == null)
            {
                CreateReadOnlySettingControl(parent, entry.BoxedValue?.ToString() ?? string.Empty);
                return;
            }

            ConfigureNumberInput(input, entry, acceptableRange, interactable);
        }

        private void CreateHexColorSettingControl(
            RectTransform parent,
            ConfigEntry<string> entry,
            string defaultHex,
            bool interactable)
        {
            if (entry == null)
            {
                CreateReadOnlySettingControl(parent, defaultHex);
                return;
            }

            GameObject swatchBorderObject = new GameObject(
                "pitFireTeam_HexColorSwatchBorder",
                typeof(RectTransform),
                typeof(Image));
            swatchBorderObject.transform.SetParent(parent, false);
            RectTransform swatchBorderRect = swatchBorderObject.GetComponent<RectTransform>();
            swatchBorderRect.anchorMin = new Vector2(1f, 0.5f);
            swatchBorderRect.anchorMax = new Vector2(1f, 0.5f);
            swatchBorderRect.pivot = new Vector2(1f, 0.5f);
            swatchBorderRect.sizeDelta = new Vector2(28f, 28f);
            swatchBorderRect.anchoredPosition = new Vector2(-145f, 0f);

            Image swatchBorder = swatchBorderObject.GetComponent<Image>();
            swatchBorder.color = new Color(0.86f, 0.84f, 0.76f, 0.96f);
            swatchBorder.raycastTarget = false;

            GameObject swatchObject = new GameObject(
                "Color",
                typeof(RectTransform),
                typeof(Image));
            swatchObject.transform.SetParent(swatchBorderObject.transform, false);
            RectTransform swatchRect = swatchObject.GetComponent<RectTransform>();
            Stretch(swatchRect);
            swatchRect.offsetMin = new Vector2(2f, 2f);
            swatchRect.offsetMax = new Vector2(-2f, -2f);

            Image swatch = swatchObject.GetComponent<Image>();
            swatch.raycastTarget = false;

            TMP_InputField input = CloneStockNumberInput(parent) ?? CreateBasicNumberInput(parent);
            if (input == null)
            {
                Destroy(swatchBorderObject);
                CreateReadOnlySettingControl(parent, entry.Value);
                return;
            }

            input.name = "pitFireTeam_HexColorInput";
            RectTransform inputRect = input.transform as RectTransform;
            if (inputRect != null)
            {
                inputRect.anchorMin = new Vector2(1f, 0.5f);
                inputRect.anchorMax = new Vector2(1f, 0.5f);
                inputRect.pivot = new Vector2(1f, 0.5f);
                inputRect.sizeDelta = new Vector2(89f, 38f);
                inputRect.anchoredPosition = new Vector2(-42f, 0f);
                inputRect.localScale = Vector3.one;
            }

            ConfigureHexColorInput(input, entry, defaultHex, swatch, interactable);
        }

        private void ConfigureHexColorInput(
            TMP_InputField input,
            ConfigEntry<string> entry,
            string defaultHex,
            Image swatch,
            bool interactable)
        {
            input.onValueChanged.RemoveAllListeners();
            input.onSelect.RemoveAllListeners();
            input.onDeselect.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onFocusSelectAll = false;
            input.customCaretColor = true;
            input.caretColor = new Color(0.88f, 0.82f, 0.66f, 1f);
            input.selectionColor = new Color(0.78f, 0.72f, 0.58f, 0.62f);
            input.caretWidth = 2;
            input.characterLimit = 7;

            ConfigureNumberInputText(input);
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "#RRGGBB";
            }

            if (!HexColorSetting.TryNormalize(entry.Value, defaultHex, out string currentValue, out Color currentColor))
            {
                currentValue = defaultHex;
                HexColorSetting.TryNormalize(currentValue, defaultHex, out _, out currentColor);
            }

            input.SetTextWithoutNotify(currentValue);
            swatch.color = currentColor;
            SetSettingsControlInteractable(input.transform, interactable);
            if (!interactable)
            {
                return;
            }

            input.onSelect.AddListener(_ => ActivateNumberInput(input));
            input.onValueChanged.AddListener(value =>
            {
                if (HexColorSetting.TryNormalize(value, defaultHex, out _, out Color previewColor))
                {
                    swatch.color = previewColor;
                }
            });
            input.onEndEdit.AddListener(value =>
            {
                if (!HexColorSetting.TryNormalize(value, defaultHex, out string normalized, out Color color))
                {
                    input.SetTextWithoutNotify(currentValue);
                    swatch.color = currentColor;
                    return;
                }

                currentValue = normalized;
                currentColor = color;
                input.SetTextWithoutNotify(normalized);
                swatch.color = color;
                if (string.Equals(entry.Value, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                entry.Value = normalized;
                pitFireTeam.Instance?.Config.Save();
            });
        }

        private static bool IsLootPriceSetting(ConfigEntryBase entry)
        {
            return entry == pitFireTeam.lootMinimumPrice ||
                   entry == pitFireTeam.lootMaximumPrice;
        }

        private static bool TryGetHexColorDefault(ConfigEntryBase entry, out string defaultHex)
        {
            if (entry == pitFireTeam.statusReportHighlightColor)
            {
                defaultHex = StatusReportHighlightColor.DefaultHex;
                return true;
            }

            if (entry == pitFireTeam.statusReportFullHealthColor)
            {
                defaultHex = StatusReportHighlightColor.FullHealthDefaultHex;
                return true;
            }

            if (entry == pitFireTeam.statusReportMediumHealthColor)
            {
                defaultHex = StatusReportHighlightColor.MediumHealthDefaultHex;
                return true;
            }

            if (entry == pitFireTeam.statusReportLowHealthColor)
            {
                defaultHex = StatusReportHighlightColor.LowHealthDefaultHex;
                return true;
            }

            if (entry == pitFireTeam.enemyMarkerAlertColor)
            {
                defaultHex = EnemyMarkerColor.AlertDefaultHex;
                return true;
            }

            if (entry == pitFireTeam.enemyMarkerVisibleColor)
            {
                defaultHex = EnemyMarkerColor.VisibleDefaultHex;
                return true;
            }

            defaultHex = string.Empty;
            return false;
        }

        private TMP_InputField CloneStockNumberInput(RectTransform parent)
        {
            NumberSlider template = ResolveSettingsSliderTemplate();
            TMP_InputField templateInput = template != null
                ? NumberSliderValueInputField?.GetValue(template) as TMP_InputField
                : null;
            if (templateInput == null)
            {
                return null;
            }

            TMP_InputField input = Instantiate(templateInput, parent, false);
            input.name = "pitFireTeam_LootPriceNumberInput";
            input.gameObject.SetActive(true);
            ConfigureNumberInputRect(input.transform as RectTransform, templateInput.transform as RectTransform);
            return input;
        }

        private TMP_InputField CreateBasicNumberInput(RectTransform parent)
        {
            GameObject inputObject = new GameObject("pitFireTeam_LootPriceNumberInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputObject.transform.SetParent(parent, false);

            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            ConfigureNumberInputRect(inputRect, null);

            Image background = inputObject.GetComponent<Image>();
            background.color = Color.black;
            background.raycastTarget = true;

            GameObject textObject = CreateText("Text", string.Empty, 18f, TextAlignmentOptions.MidlineLeft);
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            Stretch(textRect);
            textRect.offsetMin = new Vector2(8f, 0f);
            textRect.offsetMax = new Vector2(-8f, 0f);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;

            GameObject placeholderObject = CreateText("Placeholder", "0", 18f, TextAlignmentOptions.MidlineLeft);
            placeholderObject.transform.SetParent(inputObject.transform, false);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            Stretch(placeholderRect);
            placeholderRect.offsetMin = new Vector2(8f, 0f);
            placeholderRect.offsetMax = new Vector2(-8f, 0f);

            TextMeshProUGUI placeholder = placeholderObject.GetComponent<TextMeshProUGUI>();
            placeholder.color = new Color(0.54f, 0.54f, 0.54f, 0.78f);
            placeholder.enableWordWrapping = false;
            placeholder.raycastTarget = false;

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = background;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = input.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            input.colors = colors;
            ConfigureNumberInputChrome(input);

            return input;
        }

        private static void ConfigureNumberInputRect(RectTransform inputRect, RectTransform templateRect)
        {
            if (inputRect == null)
            {
                return;
            }

            float templateWidth = 0f;
            if (templateRect != null)
            {
                templateWidth = templateRect.rect.width > 1f ? templateRect.rect.width : templateRect.sizeDelta.x;
            }

            float width = Mathf.Max(SettingsLootPriceInputMinWidth, templateWidth * SettingsNumericInputWidthMultiplier);
            inputRect.anchorMin = new Vector2(1f, 0.5f);
            inputRect.anchorMax = new Vector2(1f, 0.5f);
            inputRect.pivot = new Vector2(1f, 0.5f);
            inputRect.sizeDelta = new Vector2(width, Mathf.Max(34f, inputRect.sizeDelta.y));
            inputRect.anchoredPosition = new Vector2(-SettingsLootPriceInputLeftOffset, -SettingsSliderVerticalOffset);
            inputRect.localScale = Vector3.one;
        }

        private void ConfigureNumberInput(TMP_InputField input, ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange, bool interactable)
        {
            input.onValueChanged.RemoveAllListeners();
            input.onSelect.RemoveAllListeners();
            input.onDeselect.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Integer;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onFocusSelectAll = false;
            input.customCaretColor = true;
            input.caretColor = new Color(0.88f, 0.82f, 0.66f, 1f);
            input.selectionColor = new Color(0.78f, 0.72f, 0.58f, 0.62f);
            input.caretWidth = 2;
            input.characterLimit = Mathf.Max(
                acceptableRange.MinValue.ToString().Length,
                acceptableRange.MaxValue.ToString().Length);

            ConfigureNumberInputText(input);

            int currentValue = Convert.ToInt32(entry.BoxedValue);
            input.SetTextWithoutNotify(currentValue.ToString());
            SetSettingsControlInteractable(input.transform, interactable);

            if (!interactable)
            {
                return;
            }

            input.onSelect.AddListener(_ => ActivateNumberInput(input));
            input.onEndEdit.AddListener(value =>
            {
                int boxed = ParseClampedSettingInt(value, currentValue, acceptableRange);
                input.SetTextWithoutNotify(boxed.ToString());
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                pitFireTeam.Instance?.Config.Save();
            });
        }

        private static void ConfigureNumberInputText(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            if (input.targetGraphic != null)
            {
                input.targetGraphic.raycastTarget = true;
            }

            ConfigureNumberInputChrome(input);

            if (input.textViewport != null)
            {
                input.textViewport.offsetMin = new Vector2(8f, 0f);
                input.textViewport.offsetMax = new Vector2(-8f, 0f);
            }

            if (input.textComponent != null)
            {
                input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
                input.textComponent.enableWordWrapping = false;
                input.textComponent.overflowMode = TextOverflowModes.Truncate;
                input.textComponent.raycastTarget = false;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.alignment = TextAlignmentOptions.MidlineLeft;
                placeholder.enableWordWrapping = false;
                placeholder.raycastTarget = false;
            }
        }

        private static void ConfigureNumberInputChrome(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            Image background = input.GetComponent<Image>();
            if (background != null)
            {
                background.color = Color.black;
                background.raycastTarget = true;
                input.targetGraphic = background;
            }

            ColorBlock colors = input.colors;
            colors.normalColor = Color.black;
            colors.highlightedColor = Color.black;
            colors.pressedColor = Color.black;
            colors.selectedColor = Color.black;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.42f);
            input.colors = colors;

            Transform existingBorder = input.transform.Find("pitFireTeam_LootPriceInputBottomBorder");
            RectTransform borderRect = existingBorder as RectTransform;
            Image borderImage = existingBorder != null
                ? existingBorder.GetComponent<Image>()
                : null;

            if (borderRect == null || borderImage == null)
            {
                GameObject borderObject = new GameObject("pitFireTeam_LootPriceInputBottomBorder", typeof(RectTransform), typeof(Image));
                borderObject.transform.SetParent(input.transform, false);
                borderRect = borderObject.GetComponent<RectTransform>();
                borderImage = borderObject.GetComponent<Image>();
            }

            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 0f);
            borderRect.pivot = new Vector2(0.5f, 0f);
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = new Vector2(0f, SettingsLootPriceInputBorderHeight);
            borderRect.localScale = Vector3.one;
            borderRect.SetAsLastSibling();

            borderImage.color = new Color(0.86f, 0.84f, 0.76f, 0.96f);
            borderImage.raycastTarget = false;
        }

        private static void ActivateNumberInput(TMP_InputField input)
        {
            if (input == null || !input.interactable || input.readOnly)
            {
                return;
            }

            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject != input.gameObject &&
                !EventSystem.current.alreadySelecting)
            {
                EventSystem.current.SetSelectedGameObject(input.gameObject);
            }

            input.ActivateInputField();
        }

        private static int ParseClampedSettingInt(string value, int fallback, AcceptableValueRange<int> acceptableRange)
        {
            if (!int.TryParse(value, out int parsed))
            {
                parsed = fallback;
            }

            return Mathf.Clamp(parsed, acceptableRange.MinValue, acceptableRange.MaxValue);
        }

        private void CreateShortcutSettingControl(RectTransform parent, ConfigEntry<KeyboardShortcut> entry, bool interactable)
        {
            if (entry == null)
            {
                CreateReadOnlySettingControl(parent, string.Empty);
                return;
            }

            Button button = CreateActionButton(parent, out TextMeshProUGUI label);
            label.text = FormatShortcut(entry.Value);
            SetSettingsControlInteractable(button.transform, interactable);
            if (interactable)
            {
                button.onClick.AddListener(() =>
                {
                    if (ReferenceEquals(activeShortcutCaptureEntry, entry))
                    {
                        CancelShortcutCapture(true);
                        return;
                    }

                    BeginShortcutCapture(entry, button, label);
                });
            }
        }

        private void CreateReadOnlySettingControl(RectTransform parent, string value)
        {
            GameObject valueObject = CreateText("Value", value, 18f, TextAlignmentOptions.MidlineRight);
            valueObject.transform.SetParent(parent, false);
            RectTransform valueRect = valueObject.GetComponent<RectTransform>();
            Stretch(valueRect);
        }

        private bool TryCreateStockSettingsArea(RectTransform panelRect)
        {
            ScrollRectNoDrag template = ResolveSettingsScrollRectTemplate();
            if (template == null || template.content == null || template.viewport == null)
            {
                return false;
            }

            settingsScrollRect = Instantiate(template, panelRect, false);
            settingsScrollRect.name = "pitFireTeam_SquadControlSettingsScroll";

            RectTransform scrollRect = settingsScrollRect.transform as RectTransform;
            if (scrollRect != null)
            {
                Stretch(scrollRect);
                scrollRect.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
                scrollRect.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);
                scrollRect.localScale = Vector3.one;
            }

            settingsViewport = settingsScrollRect.viewport;
            settingsContentRoot = settingsScrollRect.content;
            settingsScrollbar = settingsScrollRect.verticalScrollbar;

            if (settingsContentRoot == null)
            {
                return false;
            }

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsContentRoot.GetChild(index).gameObject);
            }

            settingsLayoutGroup = settingsContentRoot.GetComponent<VerticalLayoutGroup>() ?? settingsContentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = settingsContentRoot.GetComponent<ContentSizeFitter>() ?? settingsContentRoot.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = Vector2.zero;
            settingsContentRoot.localScale = Vector3.one;

            return true;
        }

        private bool CreateStockSettingsEntryRow(ConfigEntryBase entry)
        {
            if (settingsContentRoot == null)
            {
                return false;
            }

            if (entry.SettingType == typeof(bool))
            {
                return CreateStockToggleRow(entry);
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                return CreateStockSliderRow(entry, acceptableRange);
            }

            return false;
        }

        private bool CreateStockToggleRow(ConfigEntryBase entry)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"pitFireTeam_StockToggleRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            UpdatableToggle clonedToggle = FindComponentByPath<UpdatableToggle>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedToggle == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            clonedToggle.group = null;
            clonedToggle.UpdateValue(entry.BoxedValue is bool boolValue && boolValue, false, null, null);
            clonedToggle.Bind(isOn =>
            {
                entry.BoxedValue = isOn;
                pitFireTeam.Instance?.Config.Save();
            });

            return true;
        }

        private bool CreateStockSliderRow(ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange)
        {
            NumberSlider template = ResolveSettingsSliderTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"pitFireTeam_StockSliderRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            NumberSlider clonedSlider = FindComponentByPath<NumberSlider>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedSlider == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            ConfigureSliderValueInputChrome(NumberSliderValueInputField?.GetValue(clonedSlider) as TMP_InputField);
            clonedSlider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            clonedSlider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            clonedSlider.Bind(value =>
            {
                int boxed = Mathf.RoundToInt(value);
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                pitFireTeam.Instance?.Config.Save();
            });

            return true;
        }

        private void ConfigureStockSettingsRow(RectTransform rowRect, RectTransform templateRect)
        {
            if (rowRect == null)
            {
                return;
            }

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.localScale = Vector3.one;

            LayoutElement layout = rowRect.GetComponent<LayoutElement>() ?? rowRect.gameObject.AddComponent<LayoutElement>();
            float preferredHeight = templateRect != null && templateRect.rect.height > 1f ? templateRect.rect.height : rowRect.rect.height;
            if (preferredHeight < 36f)
            {
                preferredHeight = 48f;
            }

            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = 1f;
        }

        private Transform ResolveSettingsRowTemplateRoot(Component control)
        {
            if (control == null)
            {
                return null;
            }

            Transform controlTransform = control.transform;
            Type controlType = control.GetType();
            Transform candidate = controlTransform;

            for (Transform current = controlTransform.parent; current != null; current = current.parent)
            {
                if (current.GetComponentsInChildren(controlType, true).Length != 1)
                {
                    break;
                }

                candidate = current;
            }

            return candidate;
        }

        private void ApplyStockSettingsLabelText(Transform rowTemplate, Transform clonedRow, Transform controlTransform, string text)
        {
            if (rowTemplate == null || clonedRow == null || controlTransform == null)
            {
                return;
            }

            TextMeshProUGUI[] templateLabels = rowTemplate
                .GetComponentsInChildren<TextMeshProUGUI>(true)
                .Where(label =>
                    label != null &&
                    !string.IsNullOrWhiteSpace(label.text) &&
                    !IsAncestorOf(controlTransform, label.transform))
                .ToArray();

            foreach (TextMeshProUGUI templateLabel in templateLabels)
            {
                TextMeshProUGUI clonedLabel = FindComponentByPath<TextMeshProUGUI>(rowTemplate, clonedRow, templateLabel.transform);
                if (clonedLabel != null)
                {
                    clonedLabel.text = text;
                }
            }
        }

        private UpdatableToggle CloneStockToggle(RectTransform parent)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return null;
            }

            UpdatableToggle toggle = Instantiate(template, parent, false);
            toggle.name = "pitFireTeam_StockToggle";
            RectTransform rect = toggle.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            toggle.gameObject.SetActive(true);
            toggle.group = null;
            HideStockLabelContainers(toggle.transform, null);
            return toggle;
        }

        private NumberSlider CloneStockNumberSlider(RectTransform parent)
        {
            RectTransform templateRoot = ResolveSettingsSliderContainerTemplate();
            if (templateRoot == null)
            {
                return null;
            }

            RectTransform sliderRoot = Instantiate(templateRoot, parent, false);
            NumberSlider slider = sliderRoot.GetComponent<NumberSlider>() ?? sliderRoot.GetComponentInChildren<NumberSlider>(true);
            if (slider == null)
            {
                Destroy(sliderRoot.gameObject);
                return null;
            }

            slider.name = "pitFireTeam_StockNumberSlider";
            if (sliderRoot != null)
            {
                Stretch(sliderRoot);
                sliderRoot.localScale = Vector3.one;
            }

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            ConfigureSliderValueInputChrome(valueInput);
            HideStockLabelContainers(sliderRoot, valueInput?.transform);

            slider.gameObject.SetActive(true);
            return slider;
        }

        private static void ConfigureSliderValueInputChrome(TMP_InputField valueInput)
        {
            if (valueInput == null)
            {
                return;
            }

            Image background = valueInput.GetComponent<Image>();
            if (background != null)
            {
                background.color = Color.clear;
                background.raycastTarget = true;
                valueInput.targetGraphic = background;
            }

            ColorBlock colors = valueInput.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = Color.clear;
            colors.pressedColor = Color.clear;
            colors.selectedColor = Color.clear;
            colors.disabledColor = Color.clear;
            valueInput.colors = colors;
        }

        private void HideStockLabelContainers(Transform root, Transform exemptRoot)
        {
            if (root == null)
            {
                return;
            }

            HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }
        }

        private void HideStockLabelContainer(Transform root, Transform exemptRoot, Transform labelTransform, HashSet<GameObject> hiddenObjects)
        {
            if (root == null || labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && IsAncestorOf(exemptRoot, labelTransform))
            {
                return;
            }

            Transform directChild = ResolveDirectChildContainer(root, labelTransform);
            if (directChild == null || directChild == root || directChild == exemptRoot || (exemptRoot != null && IsAncestorOf(directChild, exemptRoot)))
            {
                return;
            }

            if (hiddenObjects.Add(directChild.gameObject))
            {
                directChild.gameObject.SetActive(false);
            }
        }

        private static Transform ResolveDirectChildContainer(Transform root, Transform descendant)
        {
            if (root == null || descendant == null)
            {
                return null;
            }

            if (ReferenceEquals(root, descendant))
            {
                return root;
            }

            Transform current = descendant;
            while (current != null && current.parent != root)
            {
                current = current.parent;
            }

            return current == null ? descendant : current;
        }

        private Toggle CreateBasicToggle(RectTransform parent)
        {
            GameObject root = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0.5f);
            rootRect.anchorMax = new Vector2(1f, 0.5f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.sizeDelta = new Vector2(92f, 38f);
            rootRect.anchoredPosition = Vector2.zero;

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(root.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            GameObject checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform checkRect = checkmarkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(76f, 24f);
            checkRect.anchoredPosition = Vector2.zero;
            Image checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            toggle.colors = colors;

            return toggle;
        }

        private Slider CreateBasicSlider(RectTransform parent, out TextMeshProUGUI valueLabel)
        {
            valueLabel = CreateText("Value", "0", 18f, TextAlignmentOptions.MidlineRight).GetComponent<TextMeshProUGUI>();
            valueLabel.transform.SetParent(parent, false);
            RectTransform valueRect = valueLabel.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(52f, 26f);
            valueRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject sliderRoot = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderRoot.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderRoot.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.offsetMin = new Vector2(0f, -10f);
            sliderRect.offsetMax = new Vector2(-64f, 10f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 8f);
            backgroundRect.anchoredPosition = Vector2.zero;
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 1f);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            Stretch(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(0f, 0f);
            fillAreaRect.offsetMax = new Vector2(-18f, 0f);

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 8f);
            fillRect.anchoredPosition = Vector2.zero;
            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            GameObject handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleSlideArea.transform.SetParent(sliderRoot.transform, false);
            RectTransform slideAreaRect = handleSlideArea.GetComponent<RectTransform>();
            Stretch(slideAreaRect);
            slideAreaRect.offsetMin = new Vector2(8f, -6f);
            slideAreaRect.offsetMax = new Vector2(-8f, 6f);

            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(handleSlideArea.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.96f, 0.89f, 0.74f, 1f);

            Slider slider = sliderRoot.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            slider.colors = colors;

            return slider;
        }

        private Button CreateActionButton(RectTransform parent, out TextMeshProUGUI label)
        {
            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(232f, 36f);
            buttonRect.anchoredPosition = Vector2.zero;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.52f, 0.43f, 0.27f, 0.88f);
            background.raycastTarget = true;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(buttonObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);

            Image fill = fillObject.GetComponent<Image>();
            fill.color = new Color(0.12f, 0.12f, 0.12f, 0.96f);
            fill.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = fill;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.92f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            button.colors = colors;

            GameObject labelObject = CreateText("Label", string.Empty, 18f, TextAlignmentOptions.Center);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 17f;
            label.fontWeight = FontWeight.SemiBold;
            label.color = new Color(0.93f, 0.88f, 0.77f, 1f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        private void BeginShortcutCapture(ConfigEntry<KeyboardShortcut> entry, Button button, TextMeshProUGUI label)
        {
            CancelShortcutCapture(false);
            activeShortcutCaptureEntry = entry;
            activeShortcutCaptureButton = button;
            activeShortcutCaptureLabel = label;
            activeShortcutCaptureStartFrame = Time.frameCount;
            SeedSuppressedNativeKeys();
            if (activeShortcutCaptureLabel != null)
            {
                activeShortcutCaptureLabel.text = GetSocialUiText("SettingsPressKey");
            }

            if (pitFireTeam.Instance != null)
            {
                shortcutCaptureCoroutine = pitFireTeam.Instance.StartCoroutine(ShortcutCaptureCoroutine());
            }
            else
            {
                CancelShortcutCapture(true);
            }
        }

        private void CancelShortcutCapture(bool refreshLabel)
        {
            ConfigEntry<KeyboardShortcut> capturedEntry = activeShortcutCaptureEntry;
            TextMeshProUGUI capturedLabel = activeShortcutCaptureLabel;

            Coroutine captureCoroutine = shortcutCaptureCoroutine;
            shortcutCaptureCoroutine = null;
            if (captureCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(captureCoroutine);
            }

            activeShortcutCaptureEntry = null;
            activeShortcutCaptureButton = null;
            activeShortcutCaptureLabel = null;
            activeShortcutCaptureStartFrame = -1;
            shortcutCaptureSuppressedKeys.Clear();

            if (refreshLabel && capturedEntry != null && capturedLabel != null)
            {
                capturedLabel.text = FormatShortcut(capturedEntry.Value);
            }
        }

        private void ApplyShortcutCapture(KeyboardShortcut shortcut)
        {
            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            activeShortcutCaptureEntry.Value = shortcut;
            pitFireTeam.Instance?.Config.Save();
            CancelShortcutCapture(true);
        }

        private KeyCode FindPressedMainKey()
        {
            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (IsModifierKey(keyCode))
                {
                    continue;
                }

                if (UnityEngine.Input.GetKeyDown(keyCode))
                {
                    return keyCode;
                }
            }

            return KeyCode.None;
        }

        private static bool IsModifierKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return true;
                default:
                    return false;
            }
        }

        private static KeyCode[] GetPressedModifiers()
        {
            List<KeyCode> modifiers = new List<KeyCode>(3);
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            {
                modifiers.Add(KeyCode.LeftControl);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
            {
                modifiers.Add(KeyCode.LeftShift);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt))
            {
                modifiers.Add(KeyCode.LeftAlt);
            }

            return modifiers.ToArray();
        }

        private string FormatShortcut(KeyboardShortcut shortcut)
        {
            bool hasModifiers = shortcut.Modifiers != null && shortcut.Modifiers.Any();
            if (shortcut.MainKey == KeyCode.None && !hasModifiers)
            {
                return GetSocialUiText("SettingsNotBound");
            }

            shortcutBuilder.Clear();
            if (hasModifiers)
            {
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    if (shortcutBuilder.Length > 0)
                    {
                        shortcutBuilder.Append(" + ");
                    }

                    shortcutBuilder.Append(FormatKeyCode(modifier));
                }
            }

            if (shortcut.MainKey != KeyCode.None)
            {
                if (shortcutBuilder.Length > 0)
                {
                    shortcutBuilder.Append(" + ");
                }

                shortcutBuilder.Append(FormatKeyCode(shortcut.MainKey));
            }

            return shortcutBuilder.ToString();
        }

        private static string FormatKeyCode(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.None:
                    return "None";
                default:
                    return keyCode.ToString();
            }
        }

        private static string GetSettingDisplayName(ConfigEntryBase entry)
        {
            Dictionary<string, string> localized = GetSettingLanguageEntry(entry);
            if (localized != null && localized.TryGetValue("Name", out string name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string key = entry.Definition.Key ?? string.Empty;
            int index = 0;
            while (index < key.Length && (char.IsDigit(key[index]) || char.IsWhiteSpace(key[index])))
            {
                index++;
            }

            string trimmed = key.Substring(index).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? key : trimmed;
        }

        private static string GetSettingDescription(ConfigEntryBase entry)
        {
            Dictionary<string, string> localized = GetSettingLanguageEntry(entry);
            if (localized != null && localized.TryGetValue("Description", out string description))
            {
                return description ?? string.Empty;
            }

            return entry.Description?.Description ?? string.Empty;
        }

        private static Dictionary<string, string> GetSettingLanguageEntry(ConfigEntryBase entry)
        {
            LanguageOptions language = pitFireTeam.optionsLang;
            if (language == null || entry == null)
            {
                return null;
            }

            if (entry == pitFireTeam.scanDistance) return language.scanDistance;
            if (entry == pitFireTeam.patrolRadius) return language.patrolRadius;
            if (entry == pitFireTeam.followDistance) return language.followDistance;
            if (entry == pitFireTeam.regroupRadius) return language.regroupRadius;
            if (entry == pitFireTeam.enemyRemember) return language.enemyRemember;
            if (entry == pitFireTeam.heatlhMultiplier) return language.healthMultiplier;
            if (entry == pitFireTeam.statusSound) return language.statusSound;
            if (entry == pitFireTeam.enemyMarker) return language.enemyMarker;
            if (entry == pitFireTeam.enemyMarkerAlertColor) return language.enemyMarkerAlertColor;
            if (entry == pitFireTeam.enemyMarkerVisibleColor) return language.enemyMarkerVisibleColor;
            if (entry == pitFireTeam.pickupEnabled) return language.pickup;
            if (entry == pitFireTeam.tieredPickup) return language.tieredPickup;
            if (entry == pitFireTeam.maximumPickup) return language.maximumPickup;
            if (entry == pitFireTeam.recruitPickup) return language.recruitPickup;
            if (entry == pitFireTeam.teamEscape) return language.teamEscape;
            if (entry == pitFireTeam.teamEscapeUseAnyExtract) return language.teamEscapeUseAnyExtract;
            if (entry == pitFireTeam.lootMinimumPrice) return language.lootMinimumPrice;
            if (entry == pitFireTeam.lootMaximumPrice) return language.lootMaximumPrice;
            if (entry == pitFireTeam.lootFilterFood) return language.lootFilterFood;
            if (entry == pitFireTeam.lootFilterMeds) return language.lootFilterMeds;
            if (entry == pitFireTeam.lootFilterValuables) return language.lootFilterValuables;
            if (entry == pitFireTeam.lootFilterWeapons) return language.lootFilterWeapons;
            if (entry == pitFireTeam.lootFilterGear) return language.lootFilterGear;
            if (entry == pitFireTeam.lootAllowGearSwapping) return language.lootAllowGearSwapping;
            if (entry == pitFireTeam.npcSendMessage) return language.npcSendMessage;
            if (entry == pitFireTeam.pitFireTeamFLAG) return language.pitFireTeam;
            if (entry == pitFireTeam.badGuy) return language.badGuy;
            if (entry == pitFireTeam.factionHostilities) return language.factionHostilities;
            if (entry == pitFireTeam.pmcArmbands) return language.pmcArmbands;
            if (entry == pitFireTeam.englishBear) return language.englishBear;
            if (entry == pitFireTeam.restrictedGearMaintenance) return language.loadoutManagementRestrictedGearMaintenance;
            if (entry == pitFireTeam.botGrenades) return language.botGrenades;
            if (entry == pitFireTeam.pingKey) return language.pingSquad;
            if (entry == pitFireTeam.pingRadioVolume) return language.pingRadioVolume;
            if (entry == pitFireTeam.pingTime) return language.pingTime;
            if (entry == pitFireTeam.statusReportHighlight) return language.statusReportHighlight;
            if (entry == pitFireTeam.statusReportHighlightColor) return language.statusReportHighlightColor;
            if (entry == pitFireTeam.statusReportHealthColoring) return language.statusReportHealthColoring;
            if (entry == pitFireTeam.statusReportFullHealthColor) return language.statusReportFullHealthColor;
            if (entry == pitFireTeam.statusReportMediumHealthColor) return language.statusReportMediumHealthColor;
            if (entry == pitFireTeam.statusReportLowHealthColor) return language.statusReportLowHealthColor;
            if (entry == pitFireTeam.statusReportAlwaysHighlight) return language.statusReportAlwaysHighlight;
            if (entry == pitFireTeam.statusReportShowName) return language.statusReportShowName;
            if (entry == pitFireTeam.statusReportShowDistance) return language.statusReportShowDistance;
            if (entry == pitFireTeam.statusReportShowHealth) return language.statusReportShowHealth;
            if (entry == pitFireTeam.statusReportShowTactic) return language.statusReportShowTactic;
            if (entry == pitFireTeam.statusReportShowCombatStatus) return language.statusReportShowCombatStatus;
            if (entry == pitFireTeam.contactKey) return language.enemyContact;
            if (entry == pitFireTeam.overThereKey) return language.overThere;
            if (entry == pitFireTeam.hideUnsupportedCommands) return language.hideUnsupportedCommands;
            if (entry == pitFireTeam.teleportKey) return language.botTeleport;
            if (entry == pitFireTeam.healKey) return language.botHeal;
            if (entry == pitFireTeam.botPrefetch) return language.botPrefetch;
            if (entry == pitFireTeam.botTalk) return language.botTalk;
            if (entry == pitFireTeam.spawnPoint) return language.spawnPoint;
            if (entry == pitFireTeam.goToDistance) return language.goToDistance;
            if (entry == pitFireTeam.battleRecorderEnabled) return language.battleRecorder;
            if (entry == pitFireTeam.battleRecorderSnapshotIntervalMs) return language.battleRecorderSnapshotIntervalMs;

            return null;
        }

        private static Dictionary<string, string> GetLoadoutManagementLanguageEntry(LoadoutManagementMode mode)
        {
            LanguageOptions language = pitFireTeam.optionsLang;
            if (language == null)
            {
                return null;
            }

            return mode switch
            {
                LoadoutManagementMode.Restricted => language.loadoutManagementRestricted,
                LoadoutManagementMode.Immersive => language.loadoutManagementImmersive,
                LoadoutManagementMode.Extreme => language.loadoutManagementExtreme,
                _ => language.loadoutManagementSimple,
            };
        }

        private static string GetLoadoutManagementDisplayName(LoadoutManagementMode mode, Dictionary<string, string> languageEntry)
        {
            if (languageEntry != null && languageEntry.TryGetValue("Name", out string name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return mode.ToString();
        }

        private static string GetLoadoutManagementDescription(Dictionary<string, string> languageEntry)
        {
            if (languageEntry != null && languageEntry.TryGetValue("Description", out string description))
            {
                return description ?? string.Empty;
            }

            return string.Empty;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Setting";
            }

            char[] buffer = value.ToCharArray();
            for (int index = 0; index < buffer.Length; index++)
            {
                if (!char.IsLetterOrDigit(buffer[index]))
                {
                    buffer[index] = '_';
                }
            }

            return new string(buffer);
        }

    }
}
