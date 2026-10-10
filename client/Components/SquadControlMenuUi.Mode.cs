using pitTeam.Modules;
using pitTeam.Patches;
using EFT;
using System.Linq;
using EFT.UI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
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
            CreateModeChoice(panelRect, group, GameplayMode.GunsForHire, "SquadControlModeGunsForHire", "SquadControlModeGunsForHireDescription", firstRowY);
            CreateModeChoice(panelRect, group, GameplayMode.Allegiance, "SquadControlModeAllegiance", "SquadControlModeAllegianceDescription", firstRowY - SettingsRowHeight - SettingsSpacing);
            RefreshModeChoices();
        }

        private void CreateModeChoice(RectTransform parent, ToggleGroup group, GameplayMode mode, string labelKey, string descriptionKey, float y)
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
            nameRect.offsetMin = new Vector2(60f, -34f);
            nameRect.offsetMax = new Vector2(-22f, -8f);
            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;

            GameObject descriptionObject = CreateText("Description", GetSocialUiText(descriptionKey), 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(choiceRect, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = Vector2.zero;
            descriptionRect.anchorMax = Vector2.one;
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(60f, 16f);
            descriptionRect.offsetMax = new Vector2(-22f, -38f);
            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(choiceRect, false);
            controlRect.anchorMin = controlRect.anchorMax = new Vector2(0f, 1f);
            controlRect.pivot = new Vector2(0f, 1f);
            controlRect.sizeDelta = new Vector2(28f, 28f);
            controlRect.anchoredPosition = new Vector2(20f, -8f);

            Toggle radio = CreateLocationRadioControl(controlRect, string.Empty, true, group,
                GameplayModeRuntime.Current == mode, () => ChangeGameplayMode(mode));
            choiceRect.gameObject.AddComponent<LocationRadioClickController>().Radio = radio;
            modeChoiceRefreshers.Add(() => SetLocationRadioSelected(radio, GameplayModeRuntime.Current == mode));
        }

        private async void ChangeGameplayMode(GameplayMode next)
        {
            if (GameplayModeRuntime.IsSwitching || next == GameplayModeRuntime.Current) { RefreshModeChoices(); return; }
            if (IsRaidActive()) { AddTeammateCreationFlow.ShowToast(GetSocialUiText("SettingsUnavailableDuringRaid")); RefreshModeChoices(); return; }
            GameObject blocker = new GameObject("pitFireTeam_ModeSwitchBusy", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(modePanel.transform.parent, false);
            Stretch(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            CancelPortraitQueue();
            try
            {
                await ApplyGameplayModeAsync(next);
                RebuildRosterTiles();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Gameplay mode switch failed: {ex}");
                AddTeammateCreationFlow.ShowToast(GetSocialUiText("GameplayModeSwitchFailed"));
            }
            finally
            {
                if (blocker != null) Destroy(blocker);
                if (this != null)
                {
                    if (addTeammateButton != null) addTeammateButton.gameObject.SetActive(!GameplayModeRuntime.IsAllegiance);
                    RebuildSettingsEntries();
                    RefreshModeChoices();
                    NotifySquadScreenRefreshed();
                }
            }
        }

        private async Task ApplyGameplayModeAsync(GameplayMode next)
        {
            if (IsRaidActive() || GameplayModeRuntime.IsSwitching)
                throw new InvalidOperationException("Gameplay mode selection is unavailable.");
            if (next == GameplayModeRuntime.Current)
            {
                // Initial Guns for Hire selection still needs server confirmation.
                await GameplayModeRuntime.SynchronizeAsync();
                return;
            }
            var outgoingMembers = MainMenuControllerPatch.GroupPlayers.Where(player => player != null).Select(player => player.AccountId).ToArray();
            await GameplayModeRuntime.ChangeAsync(next);
            welcomeDeliveryVisit = -1;
            foreach (string id in outgoingMembers)
            {
                MainMenuControllerPatch.GroupPlayers.RemoveFirst(player => player?.AccountId == id);
                if (TryGetMatchmakerController(out var controller))
                {
                    controller.GroupPlayers.RemoveFirst(player => player?.AccountId == id);
                    if (controller.GroupPlayers.Count <= 1) controller.Group?.RemoveOwner();
                }
            }
            TeammateAutoJoinRuntime.ClearAllSuppression();
            SquadSideSelectionFlow.ClearOpeningGroupSnapshot();
            SocialNetworkClassPatch.RefreshFriendsList(true);
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
