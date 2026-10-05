using EFT.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
        private enum PreviewSquadMode
        {
            GunsForHire,
            Allegiance
        }

        // UI-only selection: no config, server synchronization or gameplay consumer.
        private PreviewSquadMode previewSquadMode = PreviewSquadMode.GunsForHire;
        private readonly List<Action> modeChoiceRefreshers = new List<Action>();

        private void BuildModePanel()
        {
            RectTransform panelRect = modePanel.GetComponent<RectTransform>();
            float shellHeight = currentRosterShellHeight > 1f ? currentRosterShellHeight : CalculateRosterShellHeight();
            panelRect.sizeDelta = new Vector2(1180f, shellHeight);
            panelRect.anchoredPosition = new Vector2(0f, -12f);

            RectTransform titleRect = panelRect.Find("pitFireTeam_SquadControlModePanel_Label") as RectTransform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(40f, -24f);
            titleRect.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

            ToggleGroup group = modePanel.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            modeChoiceRefreshers.Clear();
            float firstRowY = -86f;
            CreateModeChoice(panelRect, group, PreviewSquadMode.GunsForHire, "SquadControlModeGunsForHire", "SquadControlModeGunsForHireDescription", firstRowY);
            CreateModeChoice(panelRect, group, PreviewSquadMode.Allegiance, "SquadControlModeAllegiance", "SquadControlModeAllegianceDescription", firstRowY - SettingsRowHeight - SettingsSpacing);
            RefreshModeChoices();
        }

        private void CreateModeChoice(RectTransform parent, ToggleGroup group, PreviewSquadMode mode, string labelKey, string descriptionKey, float y)
        {
            RectTransform choiceRect = new GameObject($"pitFireTeam_SquadMode_{mode}", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            choiceRect.SetParent(parent, false);
            choiceRect.anchorMin = new Vector2(0f, 1f);
            choiceRect.anchorMax = new Vector2(1f, 1f);
            choiceRect.pivot = new Vector2(0.5f, 1f);
            choiceRect.sizeDelta = new Vector2(-SettingsViewportSideInset * 2f, SettingsRowHeight);
            choiceRect.anchoredPosition = new Vector2(0f, y);
            Image rowBackground = choiceRect.GetComponent<Image>();
            rowBackground.color = new Color(0.07f, 0.07f, 0.07f, 0.84f);
            rowBackground.raycastTarget = true;
            CreateSettingsRowChrome(choiceRect);
            string label = GetSocialUiText(labelKey);

            GameObject nameObject = CreateText("Name", label, 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(choiceRect, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(22f, -34f);
            nameRect.offsetMax = new Vector2(-418f, -8f);
            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;

            GameObject descriptionObject = CreateText("Description", GetSocialUiText(descriptionKey), 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(choiceRect, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = Vector2.zero;
            descriptionRect.anchorMax = Vector2.one;
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(22f, 16f);
            descriptionRect.offsetMax = new Vector2(-418f, -38f);
            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(choiceRect, false);
            controlRect.anchorMin = controlRect.anchorMax = new Vector2(1f, 0.5f);
            controlRect.pivot = new Vector2(1f, 0.5f);
            controlRect.sizeDelta = new Vector2(186f, 48f);
            controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, 0f);

            void SelectMode(bool isOn)
            {
                if (!isOn) return;
                previewSquadMode = mode;
                RefreshModeChoices();
            }

            Image hoverBackground = CreateLoadoutManagementHoverBackground(controlRect);
            RectTransform hoverTarget;
            UIAnimatedToggleSpawner radio = CloneLoadoutManagementToggle(controlRect);
            if (radio != null)
            {
                radio.name = "pitFireTeam_SquadModeRadio";
                RectTransform radioRect = radio.transform as RectTransform;
                radioRect.anchorMin = new Vector2(0f, 0.5f);
                radioRect.anchorMax = new Vector2(1f, 0.5f);
                radioRect.pivot = new Vector2(0.5f, 0.5f);
                radioRect.anchoredPosition = Vector2.zero;
                radioRect.sizeDelta = new Vector2(0f, 42f);
                radioRect.localScale = Vector3.one * 0.86f;
                hoverTarget = radioRect;

                CanvasGroup canvasGroup = radio.GetComponent<CanvasGroup>() ?? radio.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                AnimatedToggleCanvasGroupField?.SetValue(radio, canvasGroup);
                radio.SpawnableToggle.Init(group);
                foreach (TextMeshProUGUI text in radio.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    text.text = label.ToUpperInvariant();
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }
                radio.SetActive(true);
                radio.SpawnableToggle.Interactable = true;
                if (radio.SpawnedObject != null)
                {
                    radio.SpawnedObject.group = group;
                    radio.SpawnedObject.interactable = true;
                    radio.SpawnedObject.onValueChanged.RemoveAllListeners();
                    radio.SpawnedObject.onValueChanged.AddListener(SelectMode);
                }
                modeChoiceRefreshers.Add(() => radio.ToggleSilently(previewSquadMode == mode));
                SetSettingsControlInteractable(radio.transform, true);
            }
            else
            {
                Toggle fallback = CreateBasicToggle(controlRect);
                hoverTarget = fallback.transform as RectTransform;
                fallback.group = group;
                fallback.onValueChanged.AddListener(SelectMode);
                modeChoiceRefreshers.Add(() => fallback.SetIsOnWithoutNotify(previewSquadMode == mode));
            }

            // Reuse the stock-radio click surface so the full choice responds like loadout modes.
            GameObject clickObject = new GameObject("pitFireTeam_SquadModeClickOverlay", typeof(RectTransform), typeof(Image));
            clickObject.transform.SetParent(controlRect, false);
            Stretch(clickObject.GetComponent<RectTransform>());
            Image clickImage = clickObject.GetComponent<Image>();
            clickImage.color = new Color(0f, 0f, 0f, 0.001f);
            clickImage.raycastTarget = true;
            LoadoutModeToggleHoverController hover = clickObject.AddComponent<LoadoutModeToggleHoverController>();
            hover.Configure(hoverTarget, hoverBackground);
            hover.OnClick = _ => SelectMode(true);
        }

        private void RefreshModeChoices()
        {
            foreach (Action refresh in modeChoiceRefreshers)
            {
                refresh();
            }
        }
    }
}
