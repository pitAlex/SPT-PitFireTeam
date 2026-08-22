using Arena.UI;
using EFT;
using EFT.Communications;
using EFT.UI;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using ResultProfile = EFT.OtherPlayerProfile;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void ConfigureNicknameRenameUi(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow window, ResultProfile profile)
        {
            if (NicknameRenameButton != null && NicknameRenameButton.name == "pitFireTeam_NicknameRenameButton")
            {
                GameObject.Destroy(NicknameRenameButton.gameObject);
                NicknameRenameButton = null;
            }

            OriginalNicknameLabel = NicknameLabelField?.GetValue(window) as CustomTextMeshProUGUI;
            if (OriginalNicknameLabel == null)
            {
                return;
            }

            RectTransform sourceRect = OriginalNicknameLabel.transform as RectTransform;
            if (sourceRect == null)
            {
                return;
            }

            if (OriginalNicknameAnchoredPosition == null)
            {
                OriginalNicknameAnchoredPosition = sourceRect.anchoredPosition;
            }

            sourceRect.anchoredPosition = OriginalNicknameAnchoredPosition.Value;

            DefaultUIButton hideoutButton = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (hideoutButton == null)
            {
                return;
            }

            OriginalHideoutButtonText ??= hideoutButton.HeaderText;
            OriginalHideoutButtonFontSize ??= hideoutButton.HeaderSize;

            HideStockHideoutButtonPanel(hideoutButton);
        }

        private static void CreateEditNameButton(OtherPlayerProfileScreen screen, RectTransform loadoutSelector, Transform parent, ResultProfile profile, int rowOffset = 1)
        {
            if (screen == null || loadoutSelector == null || parent == null || profile == null)
            {
                return;
            }

            if (NicknameRenameButtonRoot != null)
            {
                GameObject.Destroy(NicknameRenameButtonRoot.gameObject);
                NicknameRenameButtonRoot = null;
                NicknameRenameButton = null;
            }

            DefaultUIButton buttonTemplate = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Edit Name button aborted: hideout button template not found.");
                return;
            }

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "pitFireTeam_NameEdit";
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + GetProfileControlRowOffset(loadoutSelector, Mathf.Max(1, rowOffset));
            rowClone.gameObject.SetActive(true);

            Transform upperRoot = rowClone.Find("Upper");
            if (upperRoot != null)
            {
                upperRoot.gameObject.SetActive(false);
            }

            Transform lowerRoot = rowClone.Find("Lower");
            if (lowerRoot != null)
            {
                lowerRoot.gameObject.SetActive(false);
            }

            DefaultUIButton button = GameObject.Instantiate(buttonTemplate, rowClone, false);
            button.name = "pitFireTeam_NicknameRenameButton";
            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetRawText(GetSocialUiText("RenameChange"), 18);
            HideProfileButtonIconContainer(button);
            HideHideoutButtonDecorations(button);
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => ShowRenameOverlay(screen, profile));

            if (button.transform is RectTransform buttonRect && buttonTemplate.transform is RectTransform templateRect)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.sizeDelta = templateRect.sizeDelta;
                buttonRect.localScale = Vector3.one;
            }

            NicknameRenameButtonRoot = rowClone;
            NicknameRenameButton = button;
        }

        private static void HideHideoutButtonDecorations(DefaultUIButton button)
        {
            HiddenRenameButtonDecorations.Clear();

            foreach (Image image in button.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.gameObject == button.gameObject)
                {
                    continue;
                }

                RectTransform rect = image.transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                bool looksLikeSmallIcon = rect.rect.width <= 40f && rect.rect.height <= 40f;
                if (!looksLikeSmallIcon)
                {
                    continue;
                }

                HiddenRenameButtonDecorations.Add(image.gameObject);
                image.gameObject.SetActive(false);
            }
        }

        private static void HideStockHideoutButtonPanel(DefaultUIButton hideoutButton)
        {
            if (hideoutButton == null)
            {
                return;
            }

            Transform panel = FindAncestor(hideoutButton.transform, "HideoutButtonPanel");
            GameObject panelObject = panel != null ? panel.gameObject : hideoutButton.gameObject;

            if (OriginalHideoutButtonPanel == null)
            {
                OriginalHideoutButtonPanel = panelObject;
                OriginalHideoutButtonPanelActive = panelObject.activeSelf;
            }

            panelObject.SetActive(false);
        }

        private static void RestoreStockHideoutButtonPanel()
        {
            if (OriginalHideoutButtonPanel != null && OriginalHideoutButtonPanelActive != null)
            {
                OriginalHideoutButtonPanel.SetActive(OriginalHideoutButtonPanelActive.Value);
            }

            OriginalHideoutButtonPanel = null;
            OriginalHideoutButtonPanelActive = null;
        }

        private static Transform FindAncestor(Transform start, string name)
        {
            Transform current = start;
            while (current != null)
            {
                if (string.Equals(current.name, name, StringComparison.Ordinal))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private static void HideProfileButtonIconContainer(DefaultUIButton button)
        {
            Transform iconContainer = button?.transform.Find("SizeLabel/IconContainer");
            if (iconContainer != null)
            {
                iconContainer.gameObject.SetActive(false);
            }
        }

        internal static void RestoreHideoutButtonVisuals(OtherPlayerProfileScreen screen, ResultProfile profile = null)
        {
            RestoreStockHideoutButtonPanel();

            foreach (GameObject decoration in HiddenRenameButtonDecorations)
            {
                if (decoration != null)
                {
                    decoration.SetActive(true);
                }
            }

            HiddenRenameButtonDecorations.Clear();

            DefaultUIButton hideoutButton = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (hideoutButton == null)
            {
                return;
            }

            hideoutButton.OnClick.RemoveAllListeners();
            if (HideoutButtonHandlerMethod != null)
            {
                hideoutButton.OnClick.AddListener(() => HideoutButtonHandlerMethod.Invoke(screen, null));
            }

            if (!string.IsNullOrWhiteSpace(OriginalHideoutButtonText))
            {
                hideoutButton.SetHeaderText(OriginalHideoutButtonText, OriginalHideoutButtonFontSize ?? hideoutButton.HeaderSize);
            }
            if (profile != null)
            {
                hideoutButton.gameObject.SetActive(profile.HideoutData != null);
            }
        }

        internal static void RestoreProfileRightSideContent(OtherPlayerProfileScreen screen)
        {
            foreach (KeyValuePair<GameObject, bool> pair in HiddenRightSideRoots)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            HiddenRightSideRoots.Clear();
        }

        private static void ShowRenameOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseRenameOverlay();

            if (profile == null || OriginalNicknameLabel == null)
            {
                return;
            }

            DefaultUIButton buttonTemplate = BackButtonField?.GetValue(screen) as DefaultUIButton;
            NicknameField nicknameTemplate = Resources.FindObjectsOfTypeAll<NicknameField>()
                .FirstOrDefault(field =>
                    field != null &&
                    field.gameObject != null &&
                    field.gameObject.scene.IsValid() &&
                    NicknameFieldInputField?.GetValue(field) is TMP_InputField);
            if (buttonTemplate == null || nicknameTemplate == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Teammate rename overlay aborted: template button or NicknameField not found.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_RenameOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screen.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.08f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_RenamePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(620f, 188f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_RenameHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = new GameObject("pitFireTeam_RenameTitle", typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            titleObject.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            CustomTextMeshProUGUI title = titleObject.GetComponent<CustomTextMeshProUGUI>();
            title.text = "\u270E " + GetSocialUiText("RenameTeammateTitle").ToUpperInvariant();
            title.fontSize = 18f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(0.87f, 0.87f, 0.84f, 1f);

            Button closeButton = CreateWindowCloseButton(header.transform);
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(new UnityAction(CloseRenameOverlay));

            GameObject fieldRoot = GameObject.Instantiate(nicknameTemplate.gameObject, panel.transform, false);
            fieldRoot.name = "pitFireTeam_RenameNicknameField";
            RectTransform fieldRect = fieldRoot.transform as RectTransform;
            if (fieldRect != null)
            {
                fieldRect.anchorMin = new Vector2(0f, 1f);
                fieldRect.anchorMax = new Vector2(1f, 1f);
                fieldRect.pivot = new Vector2(0.5f, 1f);
                fieldRect.offsetMin = new Vector2(26f, -118f);
                fieldRect.offsetMax = new Vector2(-26f, -54f);
                fieldRect.localScale = Vector3.one;
            }

            NicknameField nicknameField = fieldRoot.GetComponent<NicknameField>();
            TMP_InputField inputField = NicknameFieldInputField?.GetValue(nicknameField) as TMP_InputField;
            if (nicknameField == null || inputField == null)
            {
                GameObject.Destroy(overlayRoot);
                return;
            }

            string currentNickname = OriginalNicknameLabel.text?.Trim() ?? string.Empty;
            nicknameField.Init(string.Empty);
            inputField.SetTextWithoutNotify(currentNickname);
            inputField.text = currentNickname;
            inputField.interactable = true;
            nicknameField.ValidateEnteredString(currentNickname);
            inputField.textViewport.offsetMin = Vector2.zero;
            inputField.textViewport.offsetMax = Vector2.zero;
            AddTeammateNicknameFieldUi.SetStatusLabelText(
                nicknameField,
                GetSocialUiText("EnterNickname"));

            TMP_Text stockStatusLabel = NicknameFieldStatusLabelField?.GetValue(nicknameField) as TMP_Text;
            if (stockStatusLabel != null)
            {
                stockStatusLabel.gameObject.SetActive(true);
                stockStatusLabel.text = string.Empty;
                stockStatusLabel.color = new Color(0.88f, 0.39f, 0.35f, 1f);
            }

            LocalizedText usedSymbolsLabel = NicknameFieldUsedSymbolsField?.GetValue(nicknameField) as LocalizedText;
            if (usedSymbolsLabel != null)
            {
                usedSymbolsLabel.gameObject.SetActive(false);
            }

            if (inputField.transform is RectTransform inputRect)
            {
                inputRect.offsetMin = Vector2.zero;
                inputRect.offsetMax = Vector2.zero;
            }

            inputField.pointSize = 28f;

            DefaultUIButton saveButton = CreateOverlayButton(buttonTemplate, panel.transform, new Vector2(0f, 12f), new Vector2(180f, 36f));
            saveButton.SetRawText(GetSocialUiText("RenameSave"), 22);
            saveButton.OnClick.RemoveAllListeners();
            saveButton.OnClick.AddListener(() => TryPersistNickname(inputField.text, profile, nicknameField, stockStatusLabel));
            if (saveButton.transform is RectTransform saveRect)
            {
                saveRect.anchorMin = new Vector2(0.5f, 0f);
                saveRect.anchorMax = new Vector2(0.5f, 0f);
                saveRect.pivot = new Vector2(0.5f, 0f);
                saveRect.anchoredPosition = new Vector2(0f, 10f);
                saveRect.localScale = Vector3.one * 0.9f;
            }

            RenameOverlayRoot = overlayRoot;
            RenameOverlayField = nicknameField;

            inputField.ActivateInputField();
            inputField.Select();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            }
        }

        private static DefaultUIButton CreateOverlayButton(DefaultUIButton template, Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton button = GameObject.Instantiate(template, parent, false);
            button.name = $"pitFireTeam_{template.name}";
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one;
            }

            button.gameObject.SetActive(true);
            button.Interactable = true;
            return button;
        }

        private static Button CreateWindowCloseButton(Transform parent)
        {
            GameObject buttonObject = new GameObject("pitFireTeam_RenameCloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = new GameObject("pitFireTeam_RenameCloseLabel", typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            CustomTextMeshProUGUI label = labelObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = GetSocialUiText("RenameClose");
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);

            return buttonObject.GetComponent<Button>();
        }

        private static void TryPersistNickname(string value, ResultProfile profile, NicknameField nicknameField, TMP_Text statusLabel)
        {
            if (ViewedProfile == null || profile == null || nicknameField == null)
            {
                return;
            }

            TMP_InputField inputField = NicknameFieldInputField?.GetValue(nicknameField) as TMP_InputField;
            string normalized = value?.Trim() ?? string.Empty;
            string current = OriginalNicknameLabel?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                AddTeammateNicknameFieldUi.SetStatusLabelText(
                    nicknameField,
                    GetSocialUiText("NicknameTooShort"));
                SetRenameStatus(statusLabel, GetSocialUiText("NicknameTooShort"), true);
                inputField?.ActivateInputField();
                return;
            }

            ENicknameError validationError = nicknameField.ValidationError(normalized);
            if (validationError != ENicknameError.ValidNickname)
            {
                nicknameField.ShowNicknameError(validationError, false);
                string message = validationError == ENicknameError.TooShort
                    ? GetSocialUiText("NicknameTooShort")
                    : validationError.Localized(EStringCase.None);
                if (validationError == ENicknameError.TooShort)
                {
                    AddTeammateNicknameFieldUi.SetStatusLabelText(nicknameField, message);
                }
                SetRenameStatus(statusLabel, message, true);
                inputField?.ActivateInputField();
                return;
            }

            AddTeammateNicknameFieldUi.SetStatusLabelText(
                nicknameField,
                GetSocialUiText("EnterNickname"));

            if (string.Equals(normalized, current, StringComparison.Ordinal))
            {
                CloseRenameOverlay();
                return;
            }

            try
            {
                string responseJson = RequestHandler.PostJson(RenameRoute, SerializeBody(new FriendlyTeammateRenameRequest
                {
                    aid = profile.AccountId,
                    nickname = normalized
                }));
                EnsureBodySuccess(responseJson);

                if (OriginalNicknameLabel != null)
                {
                    OriginalNicknameLabel.text = normalized;
                }

                SocialNetworkClassPatch.RefreshFriendsList();
                MarkSquadRosterDirty(profile?.AccountId);
                CloseRenameOverlay();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate nickname change.");
                Modules.Logger.LogError(ex);

                if (inputField != null)
                {
                    inputField.SetTextWithoutNotify(current);
                    inputField.ActivateInputField();
                }

                SetRenameStatus(statusLabel, GetSocialUiText("RenameFailed"), true);
            }
        }

        private static void SetRenameStatus(TMP_Text statusLabel, string message, bool isError)
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = message;
            statusLabel.color = isError
                ? new Color(0.88f, 0.39f, 0.35f, 1f)
                : new Color(0.62f, 0.62f, 0.6f, 1f);
        }

        internal static void CloseRenameOverlay()
        {
            if (RenameOverlayRoot != null)
            {
                GameObject.Destroy(RenameOverlayRoot);
                RenameOverlayRoot = null;
            }

            RenameOverlayField = null;
        }
    }
}
