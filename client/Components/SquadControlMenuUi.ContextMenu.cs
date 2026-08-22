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
        private void ShowPortraitContextMenu(SquadRosterEntry entry, PointerEventData eventData)
        {
            ClosePortraitContextMenu();

            if (entry == null)
            {
                return;
            }

            Transform overlayParent = rosterPanel?.transform.parent;
            if (overlayParent == null)
            {
                return;
            }

            bool isInGroup = IsAccountInCurrentGroup(entry.AccountId);
            string groupActionLabel = GetSocialUiText(isInGroup ? "SquadControlRemoveFromGroup" : "SquadControlInviteToGroup");
            Action groupAction = isInGroup
                ? (Action)(() => RemoveTeammateFromGroup(entry))
                : () => InviteTeammateToGroup(entry);

            GameObject overlayRoot = new GameObject("pitFireTeam_PortraitContextMenuOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.001f);
            overlayImage.raycastTarget = true;

            Button overlayButton = overlayRoot.GetComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(ClosePortraitContextMenu);

            if (TryCreateStockPortraitContextMenu(overlayRoot.transform, overlayRect, entry, eventData, groupActionLabel, groupAction))
            {
                portraitContextMenuOverlay = overlayRoot;
                return;
            }

            GameObject menuObject = new GameObject("pitFireTeam_PortraitContextMenu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            menuObject.transform.SetParent(overlayRoot.transform, false);
            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0.5f, 0.5f);
            menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.sizeDelta = new Vector2(228f, 0f);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlayRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
            localPoint.x += 10f;
            menuRect.anchoredPosition = ClampContextMenuPosition(overlayRect.rect, localPoint, 228f, 126f);

            Image menuBackground = menuObject.GetComponent<Image>();
            menuBackground.color = new Color(0.045f, 0.045f, 0.045f, 0.98f);
            menuBackground.raycastTarget = true;

            VerticalLayoutGroup layout = menuObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = menuObject.GetComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateContextMenuButton(
                menuObject.transform,
                groupActionLabel,
                groupAction);
            CreateContextMenuButton(
                menuObject.transform,
                GetSocialUiText("SquadControlViewProfile"),
                () =>
                {
                    ClosePortraitContextMenu();
                    OpenProfile(entry.AccountId);
                });
            CreateContextMenuButton(
                menuObject.transform,
                GetSocialUiText(entry.AutoJoinEnabled ? "SquadControlAutoJoinOn" : "SquadControlAutoJoinOff"),
                () => ToggleTeammateAutoJoin(entry));

            portraitContextMenuOverlay = overlayRoot;
        }

        private bool TryCreateStockPortraitContextMenu(
            Transform overlayParent,
            RectTransform overlayRect,
            SquadRosterEntry entry,
            PointerEventData eventData,
            string groupActionLabel,
            Action groupAction)
        {
            if (overlayParent == null || overlayRect == null || entry == null)
            {
                return false;
            }

            SimpleContextMenu template = ResolveMatchmakerContextMenuTemplate();
            if (template == null)
            {
                return false;
            }

            SimpleContextMenu menu = Instantiate(template, overlayParent, false);
            menu.name = "pitFireTeam_PortraitContextMenu";
            menu.gameObject.SetActive(true);
            menu.enabled = false;

            RectTransform menuRect = menu.transform as RectTransform;
            if (menuRect == null)
            {
                Destroy(menu.gameObject);
                return false;
            }

            menuRect.anchorMin = new Vector2(0.5f, 0.5f);
            menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.SetAsLastSibling();

            InteractionButtonsContainer container = SimpleContextMenuButtonsContainerField?.GetValue(menu) as InteractionButtonsContainer;
            SimpleContextMenuButton buttonTemplate = InteractionButtonsContainerTemplateField?.GetValue(container) as SimpleContextMenuButton;
            RectTransform buttonsRoot = InteractionButtonsContainerButtonsRootField?.GetValue(container) as RectTransform;
            if (container == null || buttonTemplate == null || buttonsRoot == null)
            {
                Destroy(menu.gameObject);
                return false;
            }

            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "InviteInGroup",
                groupActionLabel,
                () =>
                {
                    ClosePortraitContextMenu();
                    groupAction?.Invoke();
                });
            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "WatchProfile",
                GetSocialUiText("SquadControlViewProfile"),
                () =>
                {
                    ClosePortraitContextMenu();
                    OpenProfile(entry.AccountId);
                });
            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "AutoJoin",
                GetSocialUiText(entry.AutoJoinEnabled ? "SquadControlAutoJoinOn" : "SquadControlAutoJoinOff"),
                () =>
                {
                    ClosePortraitContextMenu();
                    ToggleTeammateAutoJoin(entry);
                });

            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlayRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
            localPoint.x += 10f;
            Vector2 menuSize = menuRect.rect.size;
            menuRect.anchoredPosition = ClampContextMenuPosition(
                overlayRect.rect,
                localPoint,
                Mathf.Max(menuSize.x, 228f),
                Mathf.Max(menuSize.y, 36f * 3f));

            return true;
        }

        private Vector2 ClampContextMenuPosition(Rect overlayRect, Vector2 desiredPosition, float menuWidth, float menuHeight)
        {
            float minX = -overlayRect.width * 0.5f;
            float maxX = overlayRect.width * 0.5f - menuWidth;
            float minY = -overlayRect.height * 0.5f + menuHeight;
            float maxY = overlayRect.height * 0.5f;

            return new Vector2(
                Mathf.Clamp(desiredPosition.x, minX, maxX),
                Mathf.Clamp(desiredPosition.y, minY, maxY));
        }

        private void CreateStockContextMenuButton(
            InteractionButtonsContainer container,
            SimpleContextMenuButton template,
            RectTransform buttonsRoot,
            string key,
            string label,
            Action onClick)
        {
            if (container == null || template == null || buttonsRoot == null)
            {
                return;
            }

            container.CreateContextButton(
                key,
                label,
                template,
                buttonsRoot,
                null,
                onClick,
                null,
                false,
                false);
        }

        private void CreateContextMenuButton(Transform parent, string label, Action onClick)
        {
            GameObject buttonObject = new GameObject("pitFireTeam_ContextMenuButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 36f;
            layout.flexibleWidth = 1f;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.11f, 0.11f, 0.11f, 1f);
            background.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            GameObject labelObject = CreateText("Label", label, 18f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = new Color(0.92f, 0.92f, 0.90f, 1f);
            text.raycastTarget = false;
        }

        private void ClosePortraitContextMenu()
        {
            if (portraitContextMenuOverlay == null)
            {
                return;
            }

            Destroy(portraitContextMenuOverlay);
            portraitContextMenuOverlay = null;
        }

        private SimpleContextMenu ResolveMatchmakerContextMenuTemplate()
        {
            return Resources.FindObjectsOfTypeAll<PlayersInviteWindow>()
                .Select(window => PlayersInviteWindowContextMenuField?.GetValue(window) as SimpleContextMenu)
                .FirstOrDefault(menu => menu != null && SimpleContextMenuButtonsContainerField?.GetValue(menu) is InteractionButtonsContainer);
        }

    }
}
