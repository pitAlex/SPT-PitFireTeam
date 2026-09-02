using EFT.UI;
using EFT.UI.Settings;
using pitTeam.Modules;
using SPT.Common.Http;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ResultProfile = EFT.OtherPlayerProfile;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private const string ProficiencyRoute = "/singleplayer/pitfireteam/teammate/profile/proficiency";

        private static DefaultUIButton ProficiencyButton { get; set; }
        private static Transform ProficiencyButtonRoot { get; set; }
        private static GameObject ProficiencyOverlayRoot { get; set; }
        private static NumberSlider ProficiencyAggressionSlider { get; set; }
        private static NumberSlider ProficiencyVisionSlider { get; set; }
        private static NumberSlider ProficiencyPrecisionSlider { get; set; }
        private static NumberSlider ProficiencyReactionSlider { get; set; }
        private static float ActiveProfileAggression { get; set; } = 50f;
        private static string ActiveProfileTactic { get; set; } = "Rifleman";
        private static FollowerProficiencyModifierValues ActiveProfileProficiency { get; set; } =
            new FollowerProficiencyModifierValues();
        private static Coroutine PendingProficiencyPersistCoroutine { get; set; }
        private static int PendingProficiencyPersistRevision { get; set; }

        private static void CreateProficiencyButton(
            OtherPlayerProfileScreen screen,
            RectTransform loadoutSelector,
            Transform parent,
            ResultProfile profile,
            FriendlyTeammateProfileOptions options)
        {
            ResetProficiencyUi();

            if (screen == null || loadoutSelector == null || parent == null || profile == null || options == null)
            {
                return;
            }

            ActiveProfileAggression = Mathf.Clamp(options.Aggression, 0f, 100f);
            ActiveProfileTactic = string.IsNullOrWhiteSpace(options.CurrentTactic)
                ? "Rifleman"
                : options.CurrentTactic;
            ActiveProfileProficiency = (options.Proficiency ?? new FollowerProficiencyModifierValues()).Clone();

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "pitFireTeam_ProficiencyButtonRow";
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + GetProfileControlRowOffset(loadoutSelector, 1);
            rowClone.gameObject.SetActive(true);

            SetProfileSelectorSectionActive(rowClone, "Upper", false);
            SetProfileSelectorSectionActive(rowClone, "Lower", false);

            DefaultUIButton buttonTemplate = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                GameObject.Destroy(rowClone.gameObject);
                pitFireTeam.Log.LogWarning("[UI] Proficiency button aborted: hideout button template not found.");
                return;
            }

            DefaultUIButton button = GameObject.Instantiate(buttonTemplate, rowClone, false);
            button.name = "pitFireTeam_AdjustProficiencyButton";
            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetRawText(GetSocialUiText("ProfileAdjustProficiency").ToUpperInvariant(), 18);
            HideProfileButtonIconContainer(button);
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => ShowProficiencyOverlay(screen, profile));

            if (button.transform is RectTransform buttonRect && buttonTemplate.transform is RectTransform templateRect)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.sizeDelta = templateRect.sizeDelta;
                buttonRect.localScale = Vector3.one;
            }

            ProficiencyButtonRoot = rowClone;
            ProficiencyButton = button;
        }

        private static void ShowProficiencyOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseProficiencyOverlay();

            if (screen == null || profile == null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_ProficiencyOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screen.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_ProficiencyPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(720f, 430f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.86f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_ProficiencyHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -36f);
            headerRect.offsetMax = Vector2.zero;

            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.055f, 0.055f, 0.055f, 0.95f);
            headerImage.raycastTarget = true;
            AddLoadoutEditorHeaderDivider(header.transform, "TopDivider", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), Vector2.zero);
            AddLoadoutEditorHeaderDivider(header.transform, "BottomDivider", Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));

            ProfileOverlayHeaderDragHandle dragHandle = header.AddComponent<ProfileOverlayHeaderDragHandle>();
            dragHandle.Target = panelRect;

            CreateOverlayText(
                "pitFireTeam_ProficiencyTitle",
                header.transform,
                new Vector2(18f, 0f),
                new Vector2(-54f, 0f),
                TextAlignmentOptions.MidlineLeft,
                GetProficiencyEditorTitle(profile),
                20f,
                new Color(0.87f, 0.87f, 0.84f, 1f));

            Button closeButton = CreateWindowCloseButton(header.transform);
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-6f, 0f);
            }

            closeButton.onClick.AddListener(CloseProficiencyOverlay);

            RectTransform aggressionRow = CreateProficiencySliderRow(
                panel.transform,
                "pitFireTeam_ProficiencyAggressionRow",
                50f);
            CreateAggressionRowContent(aggressionRow, profile, ActiveProfileAggression, true);

            ProficiencyVisionSlider = CreateProficiencyModifierSlider(
                panel.transform,
                "pitFireTeam_ProficiencyVisionRow",
                "ProfileVision",
                -12f,
                ActiveProfileProficiency.GetVisionPercent(),
                ActiveProfileProficiency.SetVisionPercent,
                profile.AccountId);
            ProficiencyPrecisionSlider = CreateProficiencyModifierSlider(
                panel.transform,
                "pitFireTeam_ProficiencyPrecisionRow",
                "ProfilePrecision",
                -74f,
                ActiveProfileProficiency.GetPrecisionPercent(),
                ActiveProfileProficiency.SetPrecisionPercent,
                profile.AccountId);
            ProficiencyReactionSlider = CreateProficiencyModifierSlider(
                panel.transform,
                "pitFireTeam_ProficiencyReactionRow",
                "ProfileReaction",
                -136f,
                ActiveProfileProficiency.GetReactionPercent(),
                ActiveProfileProficiency.SetReactionPercent,
                profile.AccountId);

            DefaultUIButton buttonTemplate = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate != null)
            {
                DefaultUIButton resetButton = CreateOverlayButton(
                    buttonTemplate,
                    panel.transform,
                    Vector2.zero,
                    new Vector2(180f, 36f));
                resetButton.name = "pitFireTeam_ProficiencyResetButton";
                resetButton.SetRawText(GetSocialUiText("ProfileResetProficiency").ToUpperInvariant(), 22);
                HideProfileButtonIconContainer(resetButton);
                resetButton.OnClick.RemoveAllListeners();
                resetButton.OnClick.AddListener(() => ResetProficiencyValues(profile));
                if (resetButton.transform is RectTransform resetRect)
                {
                    resetRect.anchorMin = new Vector2(0.5f, 0f);
                    resetRect.anchorMax = new Vector2(0.5f, 0f);
                    resetRect.pivot = new Vector2(0.5f, 0f);
                    resetRect.anchoredPosition = new Vector2(0f, 12f);
                    resetRect.localScale = Vector3.one * 0.9f;
                }
            }

            ProficiencyOverlayRoot = overlayRoot;
        }

        private static string GetProficiencyEditorTitle(ResultProfile profile)
        {
            return string.Format(
                GetSocialUiText("AdjustProficiencyTitleWithName"),
                profile?.Info?.Nickname ?? string.Empty);
        }

        private static RectTransform CreateProficiencySliderRow(Transform parent, string name, float anchoredY)
        {
            GameObject rowObject = new GameObject(name, typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = new Vector2(0f, anchoredY);
            rowRect.sizeDelta = new Vector2(620f, 56f);
            rowRect.localScale = Vector3.one;
            return rowRect;
        }

        private static NumberSlider CreateProficiencyModifierSlider(
            Transform parent,
            string rowName,
            string labelKey,
            float anchoredY,
            float currentValue,
            Action<float> updateValue,
            string accountId)
        {
            RectTransform rowRoot = CreateProficiencySliderRow(parent, rowName, anchoredY);
            CreateAggressionLabel(
                $"{rowName}_Label",
                rowRoot,
                GetSocialUiText(labelKey),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(44f, 0f),
                new Vector2(190f, 28f),
                18f,
                TextAlignmentOptions.MidlineLeft);

            NumberSlider slider = CloneStockNumberSlider(rowRoot);
            if (slider == null)
            {
                return null;
            }

            slider.name = $"{rowName}_Slider";
            RectTransform sliderRoot = slider.transform as RectTransform;
            if (sliderRoot != null)
            {
                float sliderHeight = sliderRoot.sizeDelta.y > 0f ? sliderRoot.sizeDelta.y : 36f;
                sliderRoot.anchorMin = new Vector2(0f, 0.5f);
                sliderRoot.anchorMax = new Vector2(0f, 0.5f);
                sliderRoot.pivot = new Vector2(0f, 0.5f);
                sliderRoot.anchoredPosition = new Vector2(243f, 42f);
                sliderRoot.sizeDelta = new Vector2(300f, sliderHeight);
                sliderRoot.localScale = Vector3.one;
            }

            Transform captionRoot = sliderRoot?.Find("Caption");
            if (captionRoot != null)
            {
                GameObject.Destroy(captionRoot.gameObject);
            }

            slider.Show(
                FollowerProficiencyModifierValues.MinimumPercent,
                FollowerProficiencyModifierValues.MaximumPercent,
                "0");
            slider.UpdateValue(
                FollowerProficiencyModifierValues.NormalizePercent(currentValue),
                false,
                FollowerProficiencyModifierValues.MinimumPercent,
                FollowerProficiencyModifierValues.MaximumPercent);

            Slider stockSlider = slider.GetComponentInChildren<Slider>(true);
            if (stockSlider != null)
            {
                stockSlider.interactable = true;
            }

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            ConfigureSliderValueInputChrome(valueInput);
            if (valueInput != null)
            {
                valueInput.readOnly = false;
                valueInput.interactable = true;
            }

            slider.enabled = true;
            slider.Bind(value =>
            {
                float roundedValue = Mathf.Round(value);
                updateValue?.Invoke(roundedValue);
                ScheduleProficiencyPersist(accountId, ActiveProfileProficiency);
            });
            return slider;
        }

        private static void ResetProficiencyValues(ResultProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            SetProficiencyAggressionForTactic(IsMarksmanTactic(ActiveProfileTactic));
            ActiveProfileProficiency.SetVisionPercent(FollowerProficiencyModifierValues.DefaultPercent);
            ActiveProfileProficiency.SetPrecisionPercent(FollowerProficiencyModifierValues.DefaultPercent);
            ActiveProfileProficiency.SetReactionPercent(FollowerProficiencyModifierValues.DefaultPercent);

            UpdateProficiencySlider(ProficiencyVisionSlider, ActiveProfileProficiency.GetVisionPercent());
            UpdateProficiencySlider(ProficiencyPrecisionSlider, ActiveProfileProficiency.GetPrecisionPercent());
            UpdateProficiencySlider(ProficiencyReactionSlider, ActiveProfileProficiency.GetReactionPercent());

            ScheduleAggressionPersist(profile.AccountId, Mathf.RoundToInt(ActiveProfileAggression));
            ScheduleProficiencyPersist(profile.AccountId, ActiveProfileProficiency);
        }

        private static void UpdateProficiencySlider(NumberSlider slider, float value)
        {
            if (slider == null)
            {
                return;
            }

            slider.UpdateValue(
                FollowerProficiencyModifierValues.NormalizePercent(value),
                false,
                FollowerProficiencyModifierValues.MinimumPercent,
                FollowerProficiencyModifierValues.MaximumPercent);
        }

        private static void ScheduleProficiencyPersist(
            string accountId,
            FollowerProficiencyModifierValues proficiency)
        {
            StopPendingProficiencyPersist();
            if (string.IsNullOrWhiteSpace(accountId) || pitFireTeam.Instance == null)
            {
                return;
            }

            int revision = ++PendingProficiencyPersistRevision;
            PendingProficiencyPersistCoroutine = pitFireTeam.Instance.StartCoroutine(
                PersistProficiencyDelayed(accountId, proficiency.Clone(), revision));
        }

        private static void StopPendingProficiencyPersist()
        {
            if (PendingProficiencyPersistCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(PendingProficiencyPersistCoroutine);
            }

            PendingProficiencyPersistCoroutine = null;
        }

        private static System.Collections.IEnumerator PersistProficiencyDelayed(
            string accountId,
            FollowerProficiencyModifierValues proficiency,
            int revision)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (revision != PendingProficiencyPersistRevision)
            {
                yield break;
            }

            PendingProficiencyPersistCoroutine = null;
            try
            {
                string responseJson = RequestHandler.PostJson(
                    ProficiencyRoute,
                    SerializeBody(new FriendlyTeammateProficiencyRequest
                    {
                        aid = accountId,
                        proficiency = proficiency
                    }));
                EnsureBodySuccess(responseJson);
                MarkSquadRosterDirty(accountId);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate proficiency change.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void SetProfileSelectorSectionActive(RectTransform root, string childName, bool active)
        {
            Transform child = root?.Find(childName);
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }

        internal static void CloseProficiencyOverlay()
        {
            if (ProficiencyOverlayRoot != null)
            {
                GameObject.Destroy(ProficiencyOverlayRoot);
                ProficiencyOverlayRoot = null;
            }

            ProficiencyAggressionSlider = null;
            ProficiencyVisionSlider = null;
            ProficiencyPrecisionSlider = null;
            ProficiencyReactionSlider = null;
        }

        internal static void ResetProficiencyUi()
        {
            CloseProficiencyOverlay();

            if (ProficiencyButtonRoot != null)
            {
                GameObject.Destroy(ProficiencyButtonRoot.gameObject);
                ProficiencyButtonRoot = null;
            }

            ProficiencyButton = null;
            ActiveProfileAggression = 50f;
            ActiveProfileTactic = "Rifleman";
            ActiveProfileProficiency = new FollowerProficiencyModifierValues();
        }
    }
}
