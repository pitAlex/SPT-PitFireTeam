using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.Builds;
using EFT.Communications;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void ShowLoadoutEditorOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseLoadoutEditorOverlay();

            if (screen == null || profile == null || ActiveProfileSession?.Profile?.Inventory?.Stash == null)
            {
                return;
            }

            LoadoutEditorSourceLoadoutId = ActiveTeammateLoadoutId;
            LoadoutEditorSourceLoadoutName = ActiveTeammateLoadoutName;

            DefaultUIButton buttonTemplate = BackButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Loadout editor overlay aborted: template button not found.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_LoadoutEditorOverlay", typeof(RectTransform), typeof(Image));
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

            GameObject panel = new GameObject("pitFireTeam_LoadoutEditorPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = new Vector2(235f, 100f);
            panelRect.offsetMax = new Vector2(-235f, -100f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.86f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_LoadoutEditorHeader", typeof(RectTransform), typeof(Image));
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

            LoadoutEditorHeaderDragHandle dragHandle = header.AddComponent<LoadoutEditorHeaderDragHandle>();
            dragHandle.Target = panelRect;

            CreateOverlayText(
                "pitFireTeam_LoadoutEditorTitle",
                header.transform,
                new Vector2(18f, 0f),
                new Vector2(-54f, 0f),
                TextAlignmentOptions.MidlineLeft,
                GetLoadoutEditorTitle(profile),
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

            closeButton.onClick.AddListener(new UnityAction(CloseLoadoutEditorOverlay));

            CreateOverlayText(
                "pitFireTeam_LoadoutEditorSubtitle",
                panel.transform,
                new Vector2(28f, -62f),
                new Vector2(-28f, -98f),
                TextAlignmentOptions.MidlineLeft,
                string.Format(
                    pitFireTeam.IsFollowerLoadoutRealTransferMode()
                        ? GetSocialUiText("EditLoadoutSubtitleReal")
                        : GetSocialUiText("EditLoadoutSubtitle"),
                    profile.Info?.Nickname ?? "teammate"),
                17f,
                new Color(0.67f, 0.67f, 0.64f, 1f));

            RectTransform leftSection = CreateLoadoutEditorSection(
                panel.transform,
                "pitFireTeam_PlayerStashSection",
                GetSocialUiText("PlayerStash"),
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(16f, 64f),
                new Vector2(-2f, -104f));

            RectTransform rightSection = CreateLoadoutEditorSection(
                panel.transform,
                "pitFireTeam_BotInventorySection",
                GetSocialUiText("BotInventory"),
                new Vector2(0.5f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 64f),
                new Vector2(-10f, -104f));

            TryBuildLoadoutEditorPanels(profile, leftSection, rightSection);

            DefaultUIButton cancelButton = CreateOverlayButton(buttonTemplate, panel.transform, Vector2.zero, new Vector2(180f, 36f));
            cancelButton.name = "pitFireTeam_LoadoutEditorCancelButton";
            cancelButton.SetRawText(GetSocialUiText("Cancel"), 20);
            cancelButton.OnClick.RemoveAllListeners();
            cancelButton.OnClick.AddListener(CloseLoadoutEditorOverlay);
            if (cancelButton.transform is RectTransform cancelRect)
            {
                cancelRect.anchorMin = new Vector2(1f, 0f);
                cancelRect.anchorMax = new Vector2(1f, 0f);
                cancelRect.pivot = new Vector2(1f, 0f);
                cancelRect.anchoredPosition = new Vector2(-212f, 18f);
                cancelRect.localScale = Vector3.one * 0.9f;
            }

            DefaultUIButton doneButton = CreateOverlayButton(buttonTemplate, panel.transform, Vector2.zero, new Vector2(180f, 36f));
            doneButton.name = "pitFireTeam_LoadoutEditorDoneButton";
            doneButton.SetRawText(GetSocialUiText("Save"), 20);
            doneButton.OnClick.RemoveAllListeners();
            doneButton.OnClick.AddListener(async () =>
            {
                await CommitLoadoutEditorPresetFromUiAsync(profile);
            });
            if (doneButton.transform is RectTransform doneRect)
            {
                doneRect.anchorMin = new Vector2(1f, 0f);
                doneRect.anchorMax = new Vector2(1f, 0f);
                doneRect.pivot = new Vector2(1f, 0f);
                doneRect.anchoredPosition = new Vector2(-24f, 18f);
                doneRect.localScale = Vector3.one * 0.9f;
            }

            LoadoutEditorOverlayRoot = overlayRoot;
        }

        private static RectTransform CreateLoadoutEditorSection(
            Transform parent,
            string name,
            string title,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            GameObject section = new GameObject(name, typeof(RectTransform), typeof(Image));
            section.transform.SetParent(parent, false);
            RectTransform sectionRect = section.GetComponent<RectTransform>();
            sectionRect.anchorMin = anchorMin;
            sectionRect.anchorMax = anchorMax;
            sectionRect.offsetMin = offsetMin;
            sectionRect.offsetMax = offsetMax;
            sectionRect.localScale = Vector3.one;

            Image sectionImage = section.GetComponent<Image>();
            sectionImage.color = new Color(0f, 0f, 0f, 0.46f);
            sectionImage.raycastTarget = true;

            GameObject contentRoot = new GameObject($"{name}_Content", typeof(RectTransform));
            contentRoot.transform.SetParent(section.transform, false);
            RectTransform contentRect = contentRoot.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(8f, 10f);
            contentRect.offsetMax = new Vector2(-8f, -10f);
            contentRect.localScale = Vector3.one;
            return contentRect;
        }

        private static void AddLoadoutEditorHeaderDivider(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            GameObject dividerObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(parent, false);
            RectTransform rect = dividerObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Image image = dividerObject.GetComponent<Image>();
            image.color = new Color(0.72f, 0.68f, 0.58f, 0.18f);
            image.raycastTarget = false;
        }

        private static string GetLoadoutEditorTitle(ResultProfile profile)
        {
            string displayName = GetLoadoutEditorTitleTargetName(profile);
            string titleFormat = GetSocialUiText("EditLoadoutTitleWithName");
            return string.Format(titleFormat, displayName);
        }

        private static string GetLoadoutEditorTitleTargetName(ResultProfile profile)
        {
            bool simpleMode = !pitFireTeam.IsFollowerLoadoutRealTransferMode();
            if (simpleMode
                && !IsDefaultLoadoutEditorSelection()
                && !string.IsNullOrWhiteSpace(LoadoutEditorSourceLoadoutName))
            {
                return LoadoutEditorSourceLoadoutName;
            }

            return profile?.Info?.Nickname ?? "teammate";
        }

        private static void CreateLoadoutEditorFallbackText(Transform parent, string name, string body)
        {
            CreateOverlayText(
                name,
                parent,
                new Vector2(12f, 12f),
                new Vector2(-12f, -12f),
                TextAlignmentOptions.Center,
                body,
                19f,
                new Color(0.58f, 0.58f, 0.56f, 1f));
        }

        private static void TryBuildLoadoutEditorPanels(ResultProfile profile, RectTransform leftSection, RectTransform rightSection)
        {
            if (profile?.Equipment == null
                || ActiveProfileSession?.Profile?.Inventory?.Stash == null
                || ActiveProfileInventoryController == null)
            {
                const string missingReason = "missing profile equipment, stash, or inventory controller";
                pitFireTeam.Log.LogWarning("[UI] Loadout editor inventory build aborted: missing profile equipment, stash, or inventory controller.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "pitFireTeam_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder"), missingReason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "pitFireTeam_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder"), missingReason));
                return;
            }

            ItemUiContext itemUiContext = ItemUiContext.Instance;
            if (itemUiContext == null)
            {
                const string reason = "ItemUiContext.Instance is null";
                pitFireTeam.Log.LogWarning("[UI] Loadout editor inventory build aborted: ItemUiContext.Instance is null.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "pitFireTeam_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder"), reason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "pitFireTeam_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder"), reason));
                return;
            }

            if (!TryCreateLoadoutEditorProfile(profile, out Profile editorProfile, out InventoryController editorInventoryController))
            {
                const string reason = "failed to create local editor inventory";
                pitFireTeam.Log.LogWarning("[UI] Loadout editor inventory build aborted: failed to create local editor inventory.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "pitFireTeam_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder"), reason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "pitFireTeam_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder"), reason));
                return;
            }

            LoadoutEditorProfile = editorProfile;
            LoadoutEditorInventoryController = editorInventoryController;
            RebuildLoadoutEditorItemIndexes();

            itemUiContext.Configure(
                editorInventoryController,
                editorProfile,
                ActiveProfileSession,
                ActiveProfileSession?.InsuranceCompany,
                null,
                null,
                new CompoundItem[] { editorProfile.Inventory.Stash },
                EItemUiContextType.TransferItemsScreen,
                ECursorResult.ShowCursor,
                null,
                editorProfile.Inventory.Equipment,
                null);

            TryBuildLoadoutEditorStashPanel(leftSection, editorProfile, editorInventoryController);
            TryBuildLoadoutEditorFollowerPanel(profile, rightSection, itemUiContext, editorProfile, editorInventoryController);
        }

        private static bool TryCreateLoadoutEditorProfile(ResultProfile profile, out Profile editorProfile, out InventoryController editorInventoryController)
        {
            editorProfile = null;
            editorInventoryController = null;

            try
            {
                bool preserveRealItemIds = IsRealDefaultLoadoutEditorCommit();
                if (preserveRealItemIds)
                {
                    CaptureLoadoutEditorOriginalPinLocks(ActiveProfileSession?.Profile?.Inventory?.Stash);
                }
                else
                {
                    ClearLoadoutEditorOriginalPinLocks();
                }

                Profile baseProfile = ActiveProfileSession?.Profile?.Clone();
                InventoryEquipment editorEquipment = ResolveLoadoutEditorSourceEquipment(profile);
                if (baseProfile?.Inventory == null || editorEquipment == null)
                {
                    return false;
                }

                // Player stash ids are preserved only for real default commits, where Done describes actual
                // ownership movement between the player's stash and teammate gear.
                Item editorStash = preserveRealItemIds
                    ? baseProfile.Inventory.Stash.CloneItemWithSameId()
                    : baseProfile.Inventory.Stash.CloneItem(null);

                EFTInventoryClass inventoryDescriptor = new EFTInventoryClass(baseProfile.Inventory, GClass2240.Instance)
                {
                    Equipment = EFTItemSerializerClass.SerializeItem(editorEquipment, null),
                    Stash = EFTItemSerializerClass.SerializeItem(editorStash, null)
                };

                baseProfile.Inventory = inventoryDescriptor.ToInventory();
                if (preserveRealItemIds)
                {
                    ApplyLoadoutEditorOriginalPinLocks(baseProfile.Inventory?.Stash);
                }

                editorProfile = baseProfile;
                editorInventoryController = new InventoryController(editorProfile, false);
                CaptureLoadoutEditorInitialState(editorProfile, preserveRealItemIds);
                return editorProfile.Inventory?.Equipment != null && editorProfile.Inventory.Stash != null;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to create local teammate loadout editor profile.");
                pitFireTeam.Log.LogError(ex);
                editorProfile = null;
                editorInventoryController = null;
                return false;
            }
        }

        private static InventoryEquipment ResolveLoadoutEditorSourceEquipment(ResultProfile profile)
        {
            InventoryEquipment sourceEquipment = TryGetCustomBuildById(LoadoutEditorSourceLoadoutId)?.Equipment ?? profile?.Equipment;
            // Real default editing stages the teammate's existing item ids so Done can describe
            // actual ownership movement. Clone-only modes avoid aliasing live profile items.
            InventoryEquipment clonedEquipment = IsRealDefaultLoadoutEditorCommit()
                ? sourceEquipment?.CloneItemWithSameId() as InventoryEquipment
                : sourceEquipment?.CloneItem(null) as InventoryEquipment;
            if (clonedEquipment == null)
            {
                return null;
            }

            SanitizeLoadoutEditorEquipment(clonedEquipment);
            return clonedEquipment;
        }

        private static void SanitizeLoadoutEditorEquipment(InventoryEquipment equipment)
        {
            if (equipment == null)
            {
                return;
            }

            if (!pitFireTeam.IsFollowerLoadoutRealisticMode())
            {
                RemoveLoadoutEditorSlotItem(equipment, EquipmentSlot.SecuredContainer);
            }
        }

        private static void RemoveLoadoutEditorSlotItem(InventoryEquipment equipment, EquipmentSlot slot)
        {
            try
            {
                equipment.GetSlot(slot)?.RemoveItemWithoutRestrictions();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Failed to sanitize loadout editor slot '{slot}'.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static void TryBuildLoadoutEditorStashPanel(
            RectTransform leftSection,
            Profile editorProfile,
            InventoryController editorInventoryController)
        {
            try
            {
                SimpleStashPanel stashTemplate = ResolveLoadoutEditorStashTemplate();
                StashItemClass fakeStash = editorProfile?.Inventory?.Stash;
                if (stashTemplate == null || fakeStash == null)
                {
                    throw new InvalidOperationException($"stashTemplate={(stashTemplate != null)}, fakeStash={(fakeStash != null)}");
                }

                ItemContextAbstractClass stashContext = new GClass3459(
                    fakeStash,
                    GClass3459.EItemType.Inventory,
                    editorInventoryController.Inventory.FavoriteItemsStorage,
                    false);

                SimpleStashPanel stashPanel = GameObject.Instantiate(stashTemplate, leftSection, false);
                stashPanel.name = "pitFireTeam_LoadoutEditorStashPanel";
                if (stashPanel.transform is RectTransform stashRect)
                {
                    StretchToFillParent(stashRect);
                }

                stashPanel.Show(
                    fakeStash,
                    editorInventoryController,
                    stashContext,
                    false,
                    null,
                    SimpleStashPanel.EStashSearchAvailability.Unavailable,
                    null,
                    ItemsPanel.EItemsTab.Gear);

                LoadoutEditorStashPanel = stashPanel;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to build cloned stash panel.");
                pitFireTeam.Log.LogError(ex);
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "pitFireTeam_PlayerStashFallback",
                    string.Format(
                        GetSocialUiText("PlayerStashPlaceholder"),
                        ex.GetType().Name + ": " + ex.Message));
            }
        }

        private static void TryBuildLoadoutEditorFollowerPanel(
            ResultProfile profile,
            RectTransform rightSection,
            ItemUiContext itemUiContext,
            Profile editorProfile,
            InventoryController editorInventoryController)
        {
            try
            {
                ComplexStashPanel equipmentTemplate = ResolveLoadoutEditorEquipmentTemplate();
                InventoryEquipment equipmentView = editorProfile?.Inventory?.Equipment;
                if (equipmentTemplate == null || equipmentView == null || editorInventoryController == null)
                {
                    throw new InvalidOperationException($"equipmentTemplate={(equipmentTemplate != null)}, equipmentView={(equipmentView != null)}, followerController={(editorInventoryController != null)}");
                }

                ItemContextAbstractClass equipmentContext = new LoadoutEditorEquipmentRootContext(EItemViewType.InventoryDuringMatching);
                LoadoutEditorEquipmentContext = equipmentContext;

                ComplexStashPanel equipmentPanelRoot = GameObject.Instantiate(equipmentTemplate, rightSection, false);
                equipmentPanelRoot.name = "pitFireTeam_LoadoutEditorEquipmentPanel";
                if (equipmentPanelRoot.transform is RectTransform equipmentRect)
                {
                    StretchToFillParent(equipmentRect);
                }

                ShowLoadoutEditorEquipmentPanel(
                    equipmentPanelRoot,
                    equipmentContext,
                    equipmentView,
                    editorInventoryController,
                    profile.Info?.Nickname ?? "teammate",
                    profile.Skills ?? ActiveProfileSession.Profile.Skills,
                    ActiveProfileSession.InsuranceCompany,
                    itemUiContext);

                LoadoutEditorEquipmentPanel = equipmentPanelRoot;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to build cloned follower inventory.");
                pitFireTeam.Log.LogError(ex);
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "pitFireTeam_BotInventoryFallback",
                    string.Format(
                        GetSocialUiText("BotInventoryPlaceholder"),
                        ex.GetType().Name + ": " + ex.Message));
            }
        }

        private static SimpleStashPanel ResolveLoadoutEditorStashTemplate()
        {
            SimpleStashPanel screenTemplate = TransferItemsScreenStashPanelField?.GetValue(CommonUI.Instance?.TransferItemsScreen) as SimpleStashPanel;
            if (screenTemplate != null)
            {
                return screenTemplate;
            }

            return Resources.FindObjectsOfTypeAll<SimpleStashPanel>()
                .FirstOrDefault(panel => panel != null && !panel.name.StartsWith("pitFireTeam_", StringComparison.Ordinal));
        }

        private static ComplexStashPanel ResolveLoadoutEditorEquipmentTemplate()
        {
            ItemsPanel itemsPanel = InventoryScreenItemsPanelField?.GetValue(CommonUI.Instance?.InventoryScreen) as ItemsPanel;
            ComplexStashPanel screenTemplate = ItemsPanelComplexStashPanelField?.GetValue(itemsPanel) as ComplexStashPanel;
            if (screenTemplate != null)
            {
                return screenTemplate;
            }

            return Resources.FindObjectsOfTypeAll<ComplexStashPanel>()
                .FirstOrDefault(panel => panel != null && !panel.name.StartsWith("pitFireTeam_", StringComparison.Ordinal));
        }

        private static void ShowLoadoutEditorEquipmentPanel(
            ComplexStashPanel panelRoot,
            ItemContextAbstractClass equipmentContext,
            InventoryEquipment equipmentView,
            InventoryController followerInventoryController,
            string followerName,
            SkillManager skills,
            InsuranceCompanyClass insurance,
            ItemUiContext itemUiContext)
        {
            if (panelRoot == null || equipmentView == null || followerInventoryController == null)
            {
                throw new InvalidOperationException("equipment panel root, equipment view, or follower inventory controller was null");
            }

            panelRoot.Show(
                followerInventoryController,
                equipmentContext,
                equipmentView,
                skills,
                insurance,
                itemUiContext);

            string headerTitle = !string.IsNullOrWhiteSpace(LoadoutEditorSourceLoadoutName)
                ? LoadoutEditorSourceLoadoutName
                : followerName;

            if (!pitFireTeam.IsFollowerLoadoutRealisticMode())
            {
                HideLoadoutEditorContainerSlot(panelRoot, EquipmentSlot.SecuredContainer);
            }
            SetLoadoutEditorEquipmentHeader(panelRoot.transform, headerTitle);
            HideLoadoutEditorEquipmentHeaderIcon(panelRoot.transform);
            HideLoadoutEditorCharacterGearImage(panelRoot.transform);
        }

        private static void HideLoadoutEditorContainerSlot(ComplexStashPanel panelRoot, EquipmentSlot slot)
        {
            if (panelRoot == null)
            {
                return;
            }

            try
            {
                ContainersPanel containersPanel = ComplexStashPanelContainersPanelField?.GetValue(panelRoot) as ContainersPanel;
                if (containersPanel == null)
                {
                    return;
                }

                IDictionary<EquipmentSlot, SlotView> slotViews =
                    ContainersPanelDictionaryField?.GetValue(containersPanel) as IDictionary<EquipmentSlot, SlotView>;
                if (slotViews == null || !slotViews.TryGetValue(slot, out SlotView slotView) || slotView == null)
                {
                    return;
                }

                slotView.Close();
                GameObject.Destroy(slotView.gameObject);
                slotViews.Remove(slot);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Failed to hide loadout editor container slot '{slot}'.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static void SetLoadoutEditorEquipmentHeader(Transform panelRoot, string headerTitle)
        {
            if (panelRoot == null || string.IsNullOrWhiteSpace(headerTitle))
            {
                return;
            }

            Transform disabledTextTransform = panelRoot.Find("Header/Text/DisabledText");
            if (disabledTextTransform == null)
            {
                disabledTextTransform = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "DisabledText", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Text", StringComparison.Ordinal));
            }

            Transform headerTextTransform = panelRoot.Find("Header/Text");
            if (headerTextTransform == null)
            {
                headerTextTransform = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "Text", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Header", StringComparison.Ordinal));
            }

            if (headerTextTransform != null)
            {
                LocalizedText headerLocalizedText = headerTextTransform.GetComponent<LocalizedText>();
                if (headerLocalizedText != null)
                {
                    headerLocalizedText.enabled = false;
                }
            }

            if (disabledTextTransform != null)
            {
                LocalizedText disabledLocalizedText = disabledTextTransform.GetComponent<LocalizedText>();
                if (disabledLocalizedText != null)
                {
                    disabledLocalizedText.enabled = false;
                }

                TMP_Text disabledText = disabledTextTransform.GetComponent<TMP_Text>();
                if (disabledText != null)
                {
                    disabledText.text = headerTitle;
                    return;
                }
            }

            if (headerTextTransform != null)
            {
                TMP_Text headerText = headerTextTransform.GetComponent<TMP_Text>();
                if (headerText != null)
                {
                    headerText.text = headerTitle;
                }
            }
        }

        private static void HideLoadoutEditorEquipmentHeaderIcon(Transform panelRoot)
        {
            if (panelRoot == null)
            {
                return;
            }

            Transform headerIcon = panelRoot.Find("Header/Icon");
            if (headerIcon == null)
            {
                headerIcon = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "Icon", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Header", StringComparison.Ordinal));
            }

            if (headerIcon != null)
            {
                headerIcon.gameObject.SetActive(false);
            }
        }

        private static void HideLoadoutEditorCharacterGearImage(Transform equipmentTabRoot)
        {
            if (equipmentTabRoot == null)
            {
                return;
            }

            Transform characterGear = equipmentTabRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform =>
                    transform != null &&
                    (string.Equals(transform.name, "CharacterGear", StringComparison.Ordinal) ||
                     string.Equals(transform.name, "ChracterGear", StringComparison.Ordinal)));

            if (characterGear == null)
            {
                return;
            }

            Image[] gearImages = characterGear.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < gearImages.Length; i++)
            {
                if (gearImages[i] != null)
                {
                    gearImages[i].enabled = false;
                }
            }
        }

        private static CustomTextMeshProUGUI CreateOverlayText(
            string name,
            Transform parent,
            Vector2 offsetMin,
            Vector2 offsetMax,
            TextAlignmentOptions alignment,
            string text,
            float fontSize,
            Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = offsetMin;
            textRect.offsetMax = offsetMax;
            textRect.localScale = Vector3.one;

            CustomTextMeshProUGUI label = textObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.enableWordWrapping = true;
            return label;
        }

        internal static void CloseLoadoutEditorOverlay()
        {
            CloseLoadoutEditorChildWindows();

            if (LoadoutEditorEquipmentPanel != null)
            {
                LoadoutEditorEquipmentPanel.Close();
                LoadoutEditorEquipmentPanel.UnConfigure();
                LoadoutEditorEquipmentPanel = null;
            }

            if (LoadoutEditorStashPanel != null)
            {
                LoadoutEditorStashPanel.Close();
                LoadoutEditorStashPanel = null;
            }

            if (LoadoutEditorEquipmentContext != null && ItemUiContext.Instance != null)
            {
                ItemUiContext.Instance.UnregisterView(LoadoutEditorEquipmentContext);
                LoadoutEditorEquipmentContext = null;
            }

            if (LoadoutEditorOverlayRoot != null)
            {
                GameObject.Destroy(LoadoutEditorOverlayRoot);
                LoadoutEditorOverlayRoot = null;
            }

            LoadoutEditorProfile = null;
            LoadoutEditorInventoryController = null;
            LoadoutEditorSourceLoadoutId = null;
            LoadoutEditorSourceLoadoutName = null;
            LoadoutEditorInitialEquipmentItems = null;
            LoadoutEditorInitialStashItems = null;
            ClearLoadoutEditorOriginalPinLocks();
            ClearLoadoutEditorItemIndexes();

            RestoreProfileItemUiContext();
        }

        private static void ShowProfileRecoveryOverlay(
            OtherPlayerProfileScreen screen,
            FriendlyTeammateProfileRecoveryNotice notice)
        {
            if (screen == null || notice == null || !notice.Recovered)
            {
                return;
            }

            CloseProfileRecoveryOverlay();

            DefaultUIButton buttonTemplate = BackButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Profile recovery overlay skipped: template button not found.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_ProfileRecoveryOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screen.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.58f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_ProfileRecoveryPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(680f, 224f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_ProfileRecoveryHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -34f);
            headerRect.offsetMax = Vector2.zero;

            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.055f, 0.055f, 0.055f, 0.95f);
            headerImage.raycastTarget = true;

            CreateOverlayText(
                "pitFireTeam_ProfileRecoveryTitle",
                header.transform,
                new Vector2(18f, 0f),
                new Vector2(-54f, 0f),
                TextAlignmentOptions.MidlineLeft,
                GetSocialUiText("ProfileRecoveredTitle").ToUpperInvariant(),
                19f,
                new Color(0.87f, 0.87f, 0.84f, 1f));

            Button closeButton = CreateWindowCloseButton(header.transform);
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-6f, 0f);
            }

            closeButton.onClick.AddListener(new UnityAction(CloseProfileRecoveryOverlay));

            string body = string.IsNullOrWhiteSpace(notice.Message)
                ? GetSocialUiText("ProfileRecoveredBody")
                : notice.Message;

            CreateOverlayText(
                "pitFireTeam_ProfileRecoveryBody",
                panel.transform,
                new Vector2(34f, 70f),
                new Vector2(-34f, -48f),
                TextAlignmentOptions.Center,
                body,
                22f,
                new Color(0.88f, 0.88f, 0.84f, 1f));

            DefaultUIButton okButton = CreateOverlayButton(buttonTemplate, panel.transform, new Vector2(250f, 18f), new Vector2(180f, 36f));
            okButton.name = "pitFireTeam_ProfileRecoveryOkButton";
            okButton.SetRawText(GetSocialUiText("Ok"), 20);
            okButton.OnClick.RemoveAllListeners();
            okButton.OnClick.AddListener(CloseProfileRecoveryOverlay);

            ProfileRecoveryOverlayRoot = overlayRoot;
        }

        private static void CloseProfileRecoveryOverlay()
        {
            if (ProfileRecoveryOverlayRoot == null)
            {
                return;
            }

            GameObject.Destroy(ProfileRecoveryOverlayRoot);
            ProfileRecoveryOverlayRoot = null;
        }

        private static void CloseLoadoutEditorChildWindows()
        {
            try
            {
                GClass3752.RequestGlobalClose();
                ItemUiContext.Instance?.method_11();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to close loadout editor child windows: {ex.Message}");
            }
        }

        private static void RestoreProfileItemUiContext()
        {
            InventoryController restoreController = ResolveActiveProfileInventoryControllerForBackendUpdate(
                ActiveProfileSession?.Profile,
                ActiveProfileInventoryController);
            if (restoreController == null || ActiveProfileSession == null || ItemUiContext.Instance == null)
            {
                return;
            }

            try
            {
                Profile profileClone = ActiveProfileSession.Profile?.Clone();
                if (profileClone == null)
                {
                    return;
                }

                if (ViewedProfile?.Skills != null)
                {
                    profileClone.Skills = ViewedProfile.Skills;
                }

                ItemUiContext.Instance.Configure(
                    restoreController,
                    profileClone,
                    ActiveProfileSession,
                    null,
                    null,
                    null,
                    null,
                    EItemUiContextType.Hideout,
                    ECursorResult.ShowCursor,
                    null,
                    null,
                    null);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to restore teammate profile ItemUiContext after closing loadout editor.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static async Task CommitLoadoutEditorPresetFromUiAsync(ResultProfile profile)
        {
            try
            {
                CloseLoadoutEditorChildWindows();
                await SaveLoadoutEditorPresetAsync(profile);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to commit teammate loadout editor changes.");
                pitFireTeam.Log.LogError(ex);
                NotificationManagerClass.DisplayWarningNotification(
                    ex.Message ?? GetSocialUiText("LoadoutEditorSaveFailed"),
                    ENotificationDurationType.Default);
            }
        }

        private static async Task SaveLoadoutEditorPresetAsync(ResultProfile profile)
        {
            if (profile == null
                || LoadoutEditorProfile?.Inventory?.Equipment == null
                || ItemUiContext.Instance == null)
            {
                NotificationManagerClass.DisplayWarningNotification(
                    GetSocialUiText("LoadoutEditorSaveFailed"),
                    ENotificationDurationType.Default);
                return;
            }

            if (IsDefaultLoadoutEditorSelection())
            {
                await SaveLoadoutEditorDefaultEquipmentAsync(profile);
                return;
            }

            if (ActiveProfileSession?.EquipmentBuildsStorage == null)
            {
                NotificationManagerClass.DisplayWarningNotification(
                    GetSocialUiText("LoadoutEditorSaveFailed"),
                    ENotificationDurationType.Default);
                return;
            }

            string initialName = string.IsNullOrWhiteSpace(LoadoutEditorSourceLoadoutName)
                ? ActiveProfileSession.EquipmentBuildsStorage.LastEquippedPresetName
                : LoadoutEditorSourceLoadoutName;

            GClass3838 nameDialog = ItemUiContext.Instance.ShowEditBuildNameWindow(
                initialName ?? string.Empty,
                "EquipmentBuild/SetNameWindowCaption".Localized(null),
                "EquipmentBuild/SetNameWindowPlaceholder".Localized(null));

            try
            {
                while (true)
                {
                    string enteredName;
                    try
                    {
                        enteredName = await nameDialog.AcceptResult;
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(enteredName))
                    {
                        NotificationManagerClass.DisplayWarningNotification(GetSocialUiText("NameCannotBeEmpty"), ENotificationDurationType.Default);
                        continue;
                    }

                    enteredName = enteredName.Trim();
                    EquipmentBuildsStorageClass buildsStorage = ActiveProfileSession.EquipmentBuildsStorage;
                    GClass3953 originalBuild = TryGetCustomBuildById(LoadoutEditorSourceLoadoutId);
                    GClass3953 existingBuildByName = buildsStorage.FindCustomBuildByName(enteredName);
                    MongoID targetBuildId;

                    if (existingBuildByName != null
                        && (originalBuild == null || existingBuildByName.Id != originalBuild.Id))
                    {
                        bool replaceExisting = await ShowReplaceBuildPromptAsync();
                        if (!replaceExisting)
                        {
                            continue;
                        }

                        targetBuildId = existingBuildByName.Id;
                    }
                    else if (originalBuild != null && string.Equals(originalBuild.Name, enteredName, StringComparison.Ordinal))
                    {
                        targetBuildId = originalBuild.Id;
                    }
                    else
                    {
                        targetBuildId = new MongoID(ActiveProfileSession.Profile);
                    }

                    GClass3953 editedBuild = new GClass3953(
                        targetBuildId,
                        enteredName,
                        CreateSanitizedLoadoutEditorSaveEquipment(),
                        EEquipmentBuildType.Custom);

                    var saveResult = await buildsStorage.SaveBuild(editedBuild);
                    if (saveResult.Failed)
                    {
                        NotificationManagerClass.DisplayWarningNotification(saveResult.Error ?? GetSocialUiText("SaveEquipmentPresetFailed"), ENotificationDurationType.Default);
                        continue;
                    }

                    await PersistTeammateLoadoutSelectionAsync(profile.AccountId, editedBuild.Id);
                    ActiveTeammateLoadoutId = editedBuild.Id;
                    ActiveTeammateLoadoutName = editedBuild.Name;
                    NotificationManagerClass.DisplayMessageNotification(
                        string.Format("EquipmentBuilds/PresetSaved".Localized(null), editedBuild.Name),
                        ENotificationDurationType.Default,
                        ENotificationIconType.Default,
                        null);

                    CloseLoadoutEditorOverlay();
                    RefreshCurrentTeammateLoadoutSelector(profile);
                    MarkSquadRosterDirty(profile.AccountId);
                    return;
                }
            }
            finally
            {
                nameDialog.Close();
            }
        }

        private static bool IsDefaultLoadoutEditorSelection()
        {
            return string.Equals(LoadoutEditorSourceLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task SaveLoadoutEditorDefaultEquipmentAsync(ResultProfile profile)
        {
            bool realItemCommit = IsRealDefaultLoadoutEditorCommit();
            SetLoadoutEditorBusy(realItemCommit);

            try
            {
                FlatItemsDataClass[] serializedEquipment = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(
                    new Item[] { CreateSanitizedLoadoutEditorSaveEquipment() });
                if (serializedEquipment == null || serializedEquipment.Length == 0)
                {
                    throw new InvalidOperationException("Loadout editor default equipment was unavailable for save.");
                }

                FlatItemsDataClass[] serializedPlayerStash = null;
                if (realItemCommit)
                {
                    // The editor is a staged inventory. Sending both sides lets the server commit the final
                    // ownership state atomically instead of trusting client-side drag events one by one.
                    serializedPlayerStash = CreateLoadoutEditorSaveStashItems();
                    if (!HasLoadoutEditorRealChanges(serializedEquipment, serializedPlayerStash))
                    {
                        pitFireTeam.Log.LogInfo("[UI] No real loadout editor changes detected; closing without teammate default commit.");
                        CloseLoadoutEditorOverlay();
                        RefreshCurrentTeammateLoadoutSelector(profile);
                        return;
                    }

                    pitFireTeam.Log.LogInfo("[UI] Prepared server-authoritative real loadout commit.");
                }

                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    DefaultEquipmentRoute,
                    SerializeBody(new FriendlyTeammateDefaultEquipmentRequest
                    {
                        aid = profile.AccountId,
                        items = serializedEquipment,
                        playerStashItems = serializedPlayerStash,
                        realItemCommit = realItemCommit
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateDefaultEquipmentResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateDefaultEquipmentResponse>(responseJson);

                if (realItemCommit)
                {
                    // The server save is authoritative. This reconciles the currently open client
                    // profile with the server-saved stash snapshot so a restart is not needed.
                    ApplyServerDefaultEquipmentPlayerStashChanges(response?.data);
                }

                ActiveTeammateLoadoutId = DefaultLoadoutId;
                ActiveTeammateLoadoutName = DefaultLoadoutName;

                CloseLoadoutEditorOverlay();
                RefreshCurrentTeammateLoadoutSelector(profile);
                MarkSquadRosterDirty(profile.AccountId);

            }
            finally
            {
                SetLoadoutEditorBusy(false);
            }
        }

        private static void SetLoadoutEditorBusy(bool busy)
        {
            try
            {
                if (LoadoutEditorOverlayRoot != null)
                {
                    CanvasGroup canvasGroup = LoadoutEditorOverlayRoot.GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = LoadoutEditorOverlayRoot.AddComponent<CanvasGroup>();
                    }

                    canvasGroup.interactable = !busy;
                    canvasGroup.blocksRaycasts = true;
                }

                if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    MonoBehaviourSingleton<PreloaderUI>.Instance.SetLoaderStatus(busy);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to set loadout editor busy state: {ex.Message}");
            }
        }

        internal static void ApplyServerSavedPlayerStash(FlatItemsDataClass[] savedStashItems)
        {
            ApplyServerSavedPlayerStash(
                ActiveProfileSession?.Profile,
                ResolveActiveProfileInventoryControllerForBackendUpdate(
                    ActiveProfileSession?.Profile,
                    ActiveProfileInventoryController),
                ActiveProfileSession?.RagFair,
                savedStashItems);
        }

        internal static void ApplyServerSavedPlayerStash(
            Profile activeProfile,
            InventoryController activeInventoryController,
            RagFairClass ragFair,
            FlatItemsDataClass[] savedStashItems)
        {
            if (savedStashItems == null || savedStashItems.Length == 0)
            {
                throw new InvalidOperationException("Server did not return a saved player stash for live refresh.");
            }

            if (activeProfile?.Inventory?.Stash == null)
            {
                throw new InvalidOperationException("Active player profile was unavailable for live stash refresh.");
            }

            FlatItemsDataClass[] liveStashItems = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(
                new Item[] { activeProfile.Inventory.Stash });
            if (liveStashItems == null || liveStashItems.Length == 0)
            {
                throw new InvalidOperationException("Active player stash was unavailable for live refresh.");
            }

            if (activeInventoryController is GClass3388 profileInventoryController)
            {
                try
                {
                    GClass2337 delta = BuildPlayerStashRefreshDelta(liveStashItems, savedStashItems);
                    int newCount = delta.@new?.Length ?? 0;
                    int changeCount = delta.change?.Length ?? 0;
                    int delCount = delta.del?.Length ?? 0;
                    if (newCount == 0 && changeCount == 0 && delCount == 0)
                    {
                        pitFireTeam.Log.LogInfo("[UI] Live player stash already matched server-saved loadout commit.");
                        return;
                    }

                    var updater = new GClass2331(
                        activeProfile,
                        profileInventoryController,
                        null,
                        ragFair);
                    updater.UpdateProfile(new ProfileChangesPocoClass { Stash = delta });

                    pitFireTeam.Log.LogInfo($"[UI] Applied live player stash refresh for real loadout commit: new={newCount}, change={changeCount}, del={delCount}.");
                    return;
                }
                catch (Exception ex)
                {
                    pitFireTeam.Log.LogWarning($"[UI] Backend-style live stash refresh failed; applying server-saved stash snapshot directly. {ex.Message}");
                }
            }
            else
            {
                pitFireTeam.Log.LogWarning("[UI] Backend inventory controller was unavailable; applying server-saved stash snapshot directly.");
            }

            ApplyServerSavedPlayerStashSnapshot(activeProfile, activeInventoryController, savedStashItems);
        }

        private static void ApplyServerSavedPlayerStashSnapshot(
            Profile activeProfile,
            InventoryController activeInventoryController,
            FlatItemsDataClass[] savedStashItems)
        {
            ItemFactoryClass.GStruct181 tree = Singleton<ItemFactoryClass>.Instance.FlatItemsToTree(savedStashItems, false, null);
            string stashRootId = savedStashItems[0]._id.ToString();
            if (!tree.Items.TryGetValue(stashRootId, out Item savedRoot) || !(savedRoot is StashItemClass savedStash))
            {
                throw new InvalidOperationException("Server-saved player stash root was unavailable for live refresh.");
            }

            activeProfile.Inventory.Stash = savedStash;
            if (activeInventoryController?.Inventory != null)
            {
                activeInventoryController.Inventory.Stash = savedStash;
            }

            foreach (Item item in EnumerateLoadoutEditorItemTree(savedStash))
            {
                item?.RaiseRefreshEvent(true, true);
            }

            pitFireTeam.Log.LogInfo("[UI] Applied live player stash refresh from server-saved snapshot.");
        }

        private static void RememberActiveBackendInventoryController(ISession session, InventoryController inventoryController)
        {
            if (inventoryController is GClass3388 backendController
                && session?.Profile != null
                && ReferenceEquals(backendController.Profile, session.Profile))
            {
                ActiveProfileBackendInventoryController = backendController;
            }
        }

        private static InventoryController ResolveActiveProfileInventoryControllerForBackendUpdate(
            Profile activeProfile,
            InventoryController preferredController)
        {
            if (preferredController is GClass3388)
            {
                return preferredController;
            }

            if (activeProfile != null
                && ActiveProfileBackendInventoryController?.Profile != null
                && ReferenceEquals(ActiveProfileBackendInventoryController.Profile, activeProfile))
            {
                return ActiveProfileBackendInventoryController;
            }

            return preferredController;
        }

        internal static bool CanRepairLoadoutEditorEquipmentItem(Item item)
        {
            return ViewedProfile != null
                && IsDefaultLoadoutEditorSelection()
                && IsLoadoutEditorEquipmentItem(item)
                && item.GetItemComponentsInChildren<RepairableComponent>(true).Any();
        }

        private static bool IsLoadoutEditorEquipmentItemSavedToTeammateProfile(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (IsRealDefaultLoadoutEditorCommit() && LoadoutEditorInitialEquipmentItems != null)
            {
                return LoadoutEditorInitialEquipmentItems.Any(candidate =>
                    candidate?._id != null
                    && string.Equals(candidate._id.ToString(), item.Id, StringComparison.Ordinal));
            }

            if (ViewedProfile?.Equipment == null)
            {
                return false;
            }

            return EnumerateLoadoutEditorItemTree(ViewedProfile.Equipment)
                .Any(candidate => candidate != null && string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
        }

        internal static async Task<IResult> RepairLoadoutEditorEquipmentWithKitAsync(RepairItem[] repairKitsInfo, Item itemToRepair)
        {
            if (!CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return new FailedResult("This teammate loadout item cannot be repaired from the editor", 0);
            }

            SetLoadoutEditorBusy(true);
            try
            {
                IResult prepareResult = await PrepareLoadoutEditorRepairTargetAsync(itemToRepair.Id);
                if (prepareResult.Failed)
                {
                    return prepareResult;
                }

                if (!TryGetLoadoutEditorEquipmentItem(itemToRepair.Id, out itemToRepair))
                {
                    return new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0);
                }

                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    RepairEquipmentRoute,
                    SerializeBody(new FriendlyTeammateRepairEquipmentRequest
                    {
                        aid = ViewedProfile.AccountId,
                        target = itemToRepair.Id,
                        repairKitsInfo = repairKitsInfo
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateRepairEquipmentResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateRepairEquipmentResponse>(responseJson);
                FriendlyTeammateRepairEquipmentResponse data = response?.data;
                if (data == null)
                {
                    return new FailedResult("The teammate repair response was empty", 0);
                }

                ApplyLoadoutEditorRepairResult(itemToRepair, data);
                ApplyServerRepairPlayerStashChanges(data);
                ApplyServerRepairLoadoutEditorStashChanges(data);
                CaptureLoadoutEditorInitialState(LoadoutEditorProfile, IsRealDefaultLoadoutEditorCommit());
                SyncLoadoutEditorRepairKitsFromActiveProfile(repairKitsInfo);
                MarkSquadRosterDirty(ViewedProfile.AccountId);
                pitFireTeam.Log.LogInfo($"[UI] Repaired teammate loadout item '{itemToRepair.Id}' through teammate repair route.");
                return SuccessfulResult.New;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to repair teammate loadout equipment.");
                pitFireTeam.Log.LogError(ex);
                return new FailedResult("Failed to repair teammate loadout item", 0);
            }
            finally
            {
                SetLoadoutEditorBusy(false);
            }
        }

        internal static async Task<IResult> RepairLoadoutEditorEquipmentWithTraderAsync(string traderId, RepairItem repairItem, Item itemToRepair)
        {
            if (repairItem == null || !CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return new FailedResult("This teammate loadout item cannot be repaired from the editor", 0);
            }

            SetLoadoutEditorBusy(true);
            try
            {
                IResult prepareResult = await PrepareLoadoutEditorRepairTargetAsync(itemToRepair.Id);
                if (prepareResult.Failed)
                {
                    return prepareResult;
                }

                if (!TryGetLoadoutEditorEquipmentItem(itemToRepair.Id, out itemToRepair))
                {
                    return new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0);
                }

                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    RepairEquipmentRoute,
                    SerializeBody(new FriendlyTeammateRepairEquipmentRequest
                    {
                        aid = ViewedProfile.AccountId,
                        target = itemToRepair.Id,
                        traderId = traderId,
                        repairCount = repairItem.Count
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateRepairEquipmentResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateRepairEquipmentResponse>(responseJson);
                FriendlyTeammateRepairEquipmentResponse data = response?.data;
                if (data == null)
                {
                    return new FailedResult("The teammate trader repair response was empty", 0);
                }

                ApplyLoadoutEditorRepairResult(itemToRepair, data);
                ApplyServerRepairPlayerStashChanges(data);
                ApplyServerRepairLoadoutEditorStashChanges(data);
                CaptureLoadoutEditorInitialState(LoadoutEditorProfile, IsRealDefaultLoadoutEditorCommit());
                MarkSquadRosterDirty(ViewedProfile.AccountId);
                pitFireTeam.Log.LogInfo($"[UI] Repaired teammate loadout item '{itemToRepair.Id}' through teammate trader repair route.");
                return SuccessfulResult.New;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to repair teammate loadout equipment with trader.");
                pitFireTeam.Log.LogError(ex);
                return new FailedResult("Failed to repair teammate loadout item", 0);
            }
            finally
            {
                SetLoadoutEditorBusy(false);
            }
        }

        private static async Task<IResult> PrepareLoadoutEditorRepairTargetAsync(string targetItemId)
        {
            if (!TryGetLoadoutEditorEquipmentItem(targetItemId, out Item itemToRepair)
                || !CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0);
            }

            if (!ShouldCommitLoadoutEditorBeforeRepair(itemToRepair))
            {
                return SuccessfulResult.New;
            }

            IResult commitResult = await CommitLoadoutEditorStateBeforeRepairAsync(ViewedProfile);
            if (commitResult.Failed)
            {
                return commitResult;
            }

            if (!TryGetLoadoutEditorEquipmentItem(targetItemId, out Item savedItem)
                || !CanRepairLoadoutEditorEquipmentItem(savedItem)
                || !IsLoadoutEditorEquipmentItemSavedToTeammateProfile(savedItem))
            {
                return new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0);
            }

            return SuccessfulResult.New;
        }

        private static bool ShouldCommitLoadoutEditorBeforeRepair(Item item)
        {
            return !IsLoadoutEditorEquipmentItemSavedToTeammateProfile(item)
                || HasPendingLoadoutEditorRealChanges();
        }

        private static async Task<IResult> CommitLoadoutEditorStateBeforeRepairAsync(ResultProfile profile)
        {
            if (profile == null || LoadoutEditorProfile?.Inventory?.Equipment == null)
            {
                return new FailedResult(GetSocialUiText("LoadoutEditorSaveFailed"), 0);
            }

            try
            {
                bool realItemCommit = IsRealDefaultLoadoutEditorCommit();
                FlatItemsDataClass[] serializedEquipment = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(
                    new Item[] { CreateSanitizedLoadoutEditorSaveEquipment() });
                if (serializedEquipment == null || serializedEquipment.Length == 0)
                {
                    return new FailedResult("Loadout editor default equipment was unavailable for save.", 0);
                }

                FlatItemsDataClass[] serializedPlayerStash = realItemCommit
                    ? CreateLoadoutEditorSaveStashItems()
                    : null;

                pitFireTeam.Log.LogInfo("[UI] Saving pending teammate loadout editor changes before repair.");
                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    DefaultEquipmentRoute,
                    SerializeBody(new FriendlyTeammateDefaultEquipmentRequest
                    {
                        aid = profile.AccountId,
                        items = serializedEquipment,
                        playerStashItems = serializedPlayerStash,
                        realItemCommit = realItemCommit
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateDefaultEquipmentResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateDefaultEquipmentResponse>(responseJson);

                if (realItemCommit)
                {
                    bool appliedDelta = ApplyServerDefaultEquipmentPlayerStashChanges(response?.data);
                    if (appliedDelta)
                    {
                        CaptureLoadoutEditorInitialState(LoadoutEditorProfile, realItemCommit: true);
                    }
                    else
                    {
                        ApplyServerSavedLoadoutEditorStash(response?.data?.playerStashItems);
                    }
                }
                else
                {
                    CaptureLoadoutEditorInitialState(LoadoutEditorProfile, realItemCommit: false);
                }

                ActiveTeammateLoadoutId = DefaultLoadoutId;
                ActiveTeammateLoadoutName = DefaultLoadoutName;
                RefreshCurrentTeammateLoadoutSelector(profile);
                MarkSquadRosterDirty(profile.AccountId);
                return SuccessfulResult.New;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to save pending teammate loadout editor changes before repair.");
                pitFireTeam.Log.LogError(ex);
                return new FailedResult(ex.Message ?? GetSocialUiText("LoadoutEditorSaveFailed"), 0);
            }
        }

        private static void ApplyLoadoutEditorRepairResult(Item itemToRepair, FriendlyTeammateRepairEquipmentResponse response)
        {
            RepairableComponent repairable = itemToRepair.GetItemComponent<RepairableComponent>();
            if (repairable == null || !response.durability.HasValue || !response.maxDurability.HasValue)
            {
                return;
            }

            repairable.MaxDurability = (float)response.maxDurability.Value;
            repairable.Durability = Math.Min(repairable.MaxDurability, (float)response.durability.Value);
            itemToRepair.UpdateAttributes();
            itemToRepair.RaiseRefreshEvent(true, true);
        }

        private static bool ApplyServerDefaultEquipmentPlayerStashChanges(FriendlyTeammateDefaultEquipmentResponse response)
        {
            if (TryApplyPlayerStashDelta(
                    response?.playerNewStashItems,
                    response?.playerChangedStashItems,
                    response?.playerDeletedStashItemIds,
                    "real loadout commit"))
            {
                return true;
            }

            if (response?.playerStashItems != null && response.playerStashItems.Length > 0)
            {
                ApplyServerSavedPlayerStash(response.playerStashItems);
            }

            return false;
        }

        private static void ApplyServerRepairPlayerStashChanges(FriendlyTeammateRepairEquipmentResponse response)
        {
            if (TryApplyPlayerStashDelta(
                    response?.playerNewStashItems,
                    response?.playerChangedStashItems,
                    response?.playerDeletedStashItemIds,
                    "teammate repair"))
            {
                return;
            }

            if (response?.playerStashItems != null && response.playerStashItems.Length > 0)
            {
                ApplyServerSavedPlayerStash(response.playerStashItems);
            }
        }

        private static bool TryApplyPlayerStashDelta(
            FlatItemsDataClass[] newItems,
            FlatItemsDataClass[] changedItems,
            string[] deletedItemIds,
            string reason)
        {
            if (newItems == null && changedItems == null && deletedItemIds == null)
            {
                return false;
            }

            newItems ??= Array.Empty<FlatItemsDataClass>();
            changedItems ??= Array.Empty<FlatItemsDataClass>();
            FlatItemsDataClass[] deletedItems = CreateDeletedFlatItems(deletedItemIds);
            if (newItems.Length == 0 && changedItems.Length == 0 && deletedItems.Length == 0)
            {
                return true;
            }

            Profile activeProfile = ActiveProfileSession?.Profile;
            InventoryController inventoryController = ResolveActiveProfileInventoryControllerForBackendUpdate(
                activeProfile,
                ActiveProfileInventoryController);
            if (activeProfile == null || !(inventoryController is GClass3388 profileInventoryController))
            {
                return false;
            }

            try
            {
                var updater = new GClass2331(
                    activeProfile,
                    profileInventoryController,
                    null,
                    ActiveProfileSession?.RagFair);
                updater.UpdateProfile(new ProfileChangesPocoClass
                {
                    Stash = new GClass2337
                    {
                        @new = newItems,
                        change = changedItems,
                        del = deletedItems
                    }
                });

                pitFireTeam.Log.LogInfo($"[UI] Applied {reason} player stash delta: new={newItems.Length}, change={changedItems.Length}, del={deletedItems.Length}.");
                return true;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] {reason} stash delta refresh failed; falling back to full refresh when available. {ex.Message}");
                return false;
            }
        }

        private static FlatItemsDataClass[] CreateDeletedFlatItems(IEnumerable<string> itemIds)
        {
            if (itemIds == null)
            {
                return Array.Empty<FlatItemsDataClass>();
            }

            List<FlatItemsDataClass> deletedItems = new List<FlatItemsDataClass>();
            foreach (string itemId in itemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                try
                {
                    deletedItems.Add(new FlatItemsDataClass { _id = new MongoID(itemId) });
                }
                catch (Exception ex)
                {
                    pitFireTeam.Log.LogWarning($"[UI] Ignoring invalid teammate repair deleted stash item id '{itemId}': {ex.Message}");
                }
            }

            return deletedItems.ToArray();
        }

        private static void ApplyServerRepairLoadoutEditorStashChanges(FriendlyTeammateRepairEquipmentResponse response)
        {
            if (response == null || !IsRealDefaultLoadoutEditorCommit() || LoadoutEditorProfile?.Inventory == null)
            {
                return;
            }

            FlatItemsDataClass[] newItems = response.playerNewStashItems ?? Array.Empty<FlatItemsDataClass>();
            FlatItemsDataClass[] changedItems = response.playerChangedStashItems ?? Array.Empty<FlatItemsDataClass>();
            string[] deletedItemIds = response.playerDeletedStashItemIds ?? Array.Empty<string>();
            if (newItems.Length > 0)
            {
                if (response.playerStashItems != null && response.playerStashItems.Length > 0)
                {
                    ApplyServerSavedLoadoutEditorStash(response.playerStashItems);
                }
                else
                {
                    pitFireTeam.Log.LogWarning("[UI] Teammate repair returned new player stash items without a full stash fallback for editor refresh.");
                }

                return;
            }

            try
            {
                foreach (FlatItemsDataClass changedItem in changedItems)
                {
                    string changedId = changedItem?._id.ToString();
                    if (string.IsNullOrWhiteSpace(changedId)
                        || changedItem.upd == null
                        || !LoadoutEditorStashItemsById.TryGetValue(changedId, out Item editorItem)
                        || editorItem == null)
                    {
                        continue;
                    }

                    changedItem.upd.ParseJsonTo(typeof(Item), editorItem, Array.Empty<Newtonsoft.Json.JsonConverter>());
                    editorItem.RaiseRefreshEvent(false, true);
                }

                foreach (string deletedItemId in deletedItemIds)
                {
                    if (string.IsNullOrWhiteSpace(deletedItemId)
                        || !LoadoutEditorStashItemsById.TryGetValue(deletedItemId, out Item editorItem)
                        || editorItem == null)
                    {
                        continue;
                    }

                    var removeResult = InteractionsHandlerClass.DiscardWithoutRestrictions(editorItem, LoadoutEditorInventoryController);
                    if (removeResult.Succeeded)
                    {
                        removeResult.Value.RaiseEvents(LoadoutEditorInventoryController, CommandStatus.Begin);
                        removeResult.Value.RaiseEvents(LoadoutEditorInventoryController, CommandStatus.Succeed);
                    }
                    else
                    {
                        pitFireTeam.Log.LogWarning($"[UI] Failed to remove consumed repair stash item '{deletedItemId}' from loadout editor stash: {removeResult.Error}");
                    }
                }

                RebuildLoadoutEditorItemIndexes();
                CaptureLoadoutEditorInitialState(LoadoutEditorProfile, realItemCommit: true);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Teammate repair editor stash delta refresh failed; falling back to full refresh when available. {ex.Message}");
                if (response.playerStashItems != null && response.playerStashItems.Length > 0)
                {
                    ApplyServerSavedLoadoutEditorStash(response.playerStashItems);
                }
            }
        }

        private static void ApplyServerSavedLoadoutEditorStash(FlatItemsDataClass[] savedStashItems)
        {
            if (savedStashItems == null
                || savedStashItems.Length == 0
                || LoadoutEditorProfile?.Inventory == null
                || !IsRealDefaultLoadoutEditorCommit())
            {
                return;
            }

            try
            {
                FlatItemsDataClass[] editorStashItems = RemoveLoadoutEditorEquipmentItemsFromSavedStash(savedStashItems);
                ItemFactoryClass.GStruct181 tree = Singleton<ItemFactoryClass>.Instance.FlatItemsToTree(editorStashItems, false, null);
                string stashRootId = editorStashItems[0]._id.ToString();
                if (!tree.Items.TryGetValue(stashRootId, out Item savedRoot) || !(savedRoot is StashItemClass savedStash))
                {
                    throw new InvalidOperationException("Server-saved player stash root was unavailable for loadout editor refresh.");
                }

                ApplyLoadoutEditorOriginalPinLocks(savedStash);
                LoadoutEditorProfile.Inventory.Stash = savedStash;
                RebuildLoadoutEditorItemIndexes();
                CaptureLoadoutEditorInitialState(LoadoutEditorProfile, realItemCommit: true);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to refresh loadout editor staged stash after teammate repair.");
                pitFireTeam.Log.LogError(ex);
                throw;
            }
        }

        private static FlatItemsDataClass[] RemoveLoadoutEditorEquipmentItemsFromSavedStash(FlatItemsDataClass[] savedStashItems)
        {
            InventoryEquipment equipment = LoadoutEditorProfile?.Inventory?.Equipment;
            if (equipment == null || savedStashItems == null || savedStashItems.Length == 0)
            {
                return savedStashItems;
            }

            HashSet<string> equipmentIds = EnumerateLoadoutEditorItemTree(equipment)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string stashRootId = savedStashItems[0]._id.ToString();
            return savedStashItems
                .Where(item => item?._id != null
                    && (string.Equals(item._id.ToString(), stashRootId, StringComparison.OrdinalIgnoreCase)
                        || !equipmentIds.Contains(item._id.ToString())))
                .ToArray();
        }

        private static void SyncLoadoutEditorRepairKitsFromActiveProfile(RepairItem[] repairKitsInfo)
        {
            if (repairKitsInfo == null || repairKitsInfo.Length == 0 || LoadoutEditorProfile?.Inventory == null)
            {
                return;
            }

            foreach (RepairItem repairKitInfo in repairKitsInfo)
            {
                if (repairKitInfo == null || string.IsNullOrWhiteSpace(repairKitInfo.Id))
                {
                    continue;
                }

                RepairKitsItemClass activeRepairKit = ActiveProfileSession?.Profile?.Inventory?
                    .GetPlayerItems()
                    .OfType<RepairKitsItemClass>()
                    .FirstOrDefault(item => string.Equals(item.Id, repairKitInfo.Id, StringComparison.Ordinal));
                RepairKitsItemClass editorRepairKit = LoadoutEditorProfile.Inventory
                    .GetPlayerItems()
                    .OfType<RepairKitsItemClass>()
                    .FirstOrDefault(item => string.Equals(item.Id, repairKitInfo.Id, StringComparison.Ordinal));
                if (editorRepairKit == null)
                {
                    continue;
                }

                if (activeRepairKit?.RepairKitComponent == null)
                {
                    editorRepairKit.RaiseRefreshEvent(true, true);
                    continue;
                }

                editorRepairKit.RepairKitComponent.Resource = activeRepairKit.RepairKitComponent.Resource;
                editorRepairKit.UpdateAttributes();
                editorRepairKit.RaiseRefreshEvent(true, true);
            }
        }

        // Builds the same shape of item delta the stock backend item-event route returns. Parent/slot/location
        // changes are expressed as delete + add because EFT's "change" path only updates the upd block.
        private static GClass2337 BuildPlayerStashRefreshDelta(FlatItemsDataClass[] currentItems, FlatItemsDataClass[] savedItems)
        {
            Dictionary<string, FlatItemsDataClass> currentById = ToFlatItemDictionary(currentItems);
            Dictionary<string, FlatItemsDataClass> savedById = ToFlatItemDictionary(savedItems);
            HashSet<string> stashRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (currentItems.Length > 0)
            {
                stashRootIds.Add(currentItems[0]._id.ToString());
            }

            if (savedItems.Length > 0)
            {
                stashRootIds.Add(savedItems[0]._id.ToString());
            }

            HashSet<string> deleteCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> addCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FlatItemsDataClass> changedItems = new List<FlatItemsDataClass>();

            // First pass: live items that disappeared, moved, or only changed upd data.
            foreach (KeyValuePair<string, FlatItemsDataClass> entry in currentById)
            {
                if (stashRootIds.Contains(entry.Key))
                {
                    continue;
                }

                if (!savedById.TryGetValue(entry.Key, out FlatItemsDataClass savedItem))
                {
                    deleteCandidates.Add(entry.Key);
                    continue;
                }

                if (PlacementChanged(entry.Value, savedItem))
                {
                    deleteCandidates.Add(entry.Key);
                    addCandidates.Add(entry.Key);
                    continue;
                }

                if (!JsonTokenEquals(entry.Value.upd, savedItem.upd))
                {
                    changedItems.Add(savedItem);
                }
            }

            // Second pass: items present in the saved stash but absent from the live profile.
            foreach (KeyValuePair<string, FlatItemsDataClass> entry in savedById)
            {
                if (stashRootIds.Contains(entry.Key))
                {
                    continue;
                }

                if (!currentById.ContainsKey(entry.Key))
                {
                    addCandidates.Add(entry.Key);
                }
            }

            HashSet<string> expandedAddCandidates = ExpandAddCandidatesWithAddressableParents(
                addCandidates,
                currentById,
                savedById,
                deleteCandidates,
                stashRootIds);

            List<string> deleteRoots = ReduceToRootCandidates(deleteCandidates, currentById);
            List<string> addRoots = ReduceToRootCandidates(expandedAddCandidates, savedById);
            // If a whole container tree is being replaced, its children are covered by the new tree and must
            // not also be sent as independent upd-only changes.
            changedItems = changedItems
                .Where(item => !HasAncestorInSet(item, deleteRoots, savedById))
                .ToList();

            List<FlatItemsDataClass> newItems = ExpandFlatItemRoots(addRoots, savedById);
            FlatItemsDataClass[] deletedItems = deleteRoots
                .Select(id => new FlatItemsDataClass { _id = currentById[id]._id })
                .ToArray();

            return new GClass2337
            {
                @new = newItems.ToArray(),
                change = changedItems.ToArray(),
                del = deletedItems
            };
        }

        private static Dictionary<string, FlatItemsDataClass> ToFlatItemDictionary(IEnumerable<FlatItemsDataClass> items)
        {
            Dictionary<string, FlatItemsDataClass> result = new Dictionary<string, FlatItemsDataClass>(StringComparer.OrdinalIgnoreCase);
            foreach (FlatItemsDataClass item in items)
            {
                if (item?._id == null)
                {
                    continue;
                }

                result[item._id.ToString()] = item;
            }

            return result;
        }

        private static HashSet<string> ExpandAddCandidatesWithAddressableParents(
            HashSet<string> addCandidates,
            Dictionary<string, FlatItemsDataClass> currentById,
            Dictionary<string, FlatItemsDataClass> savedById,
            HashSet<string> deleteCandidates,
            HashSet<string> stashRootIds)
        {
            HashSet<string> expanded = new HashSet<string>(addCandidates, StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in addCandidates.ToArray())
            {
                if (!savedById.TryGetValue(candidate, out FlatItemsDataClass candidateItem))
                {
                    continue;
                }

                string parentId = GetParentId(candidateItem);
                string topNestedParentId = null;
                while (!string.IsNullOrWhiteSpace(parentId))
                {
                    if (stashRootIds.Contains(parentId))
                    {
                        break;
                    }

                    if (!savedById.TryGetValue(parentId, out FlatItemsDataClass parent))
                    {
                        break;
                    }

                    topNestedParentId = parentId;
                    expanded.Add(parentId);
                    parentId = GetParentId(parent);
                }

                // EFT's backend updater can add a top-level item tree into an existing container,
                // but it cannot address a nested live container as the target for a standalone new item.
                if (!string.IsNullOrWhiteSpace(topNestedParentId) && currentById.ContainsKey(topNestedParentId))
                {
                    deleteCandidates.Add(topNestedParentId);
                    expanded.Add(topNestedParentId);
                }
            }

            return expanded;
        }

        private static bool HasAncestorInSet(
            FlatItemsDataClass item,
            IEnumerable<string> ancestorIds,
            Dictionary<string, FlatItemsDataClass> sourceById)
        {
            HashSet<string> ancestorSet = new HashSet<string>(ancestorIds, StringComparer.OrdinalIgnoreCase);
            string parentId = GetParentId(item);
            while (!string.IsNullOrWhiteSpace(parentId))
            {
                if (ancestorSet.Contains(parentId))
                {
                    return true;
                }

                if (!sourceById.TryGetValue(parentId, out FlatItemsDataClass parent))
                {
                    return false;
                }

                parentId = GetParentId(parent);
            }

            return false;
        }

        private static List<string> ReduceToRootCandidates(HashSet<string> candidates, Dictionary<string, FlatItemsDataClass> sourceById)
        {
            List<string> roots = new List<string>();
            foreach (string candidate in candidates)
            {
                string parentId = GetParentId(sourceById[candidate]);
                bool parentIsCandidate = false;
                while (!string.IsNullOrWhiteSpace(parentId))
                {
                    if (candidates.Contains(parentId))
                    {
                        parentIsCandidate = true;
                        break;
                    }

                    if (!sourceById.TryGetValue(parentId, out FlatItemsDataClass parent))
                    {
                        break;
                    }

                    parentId = GetParentId(parent);
                }

                if (!parentIsCandidate)
                {
                    roots.Add(candidate);
                }
            }

            return roots;
        }

        private static List<FlatItemsDataClass> ExpandFlatItemRoots(IEnumerable<string> rootIds, Dictionary<string, FlatItemsDataClass> sourceById)
        {
            Dictionary<string, List<string>> childrenByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, FlatItemsDataClass> entry in sourceById)
            {
                string parentId = GetParentId(entry.Value);
                if (string.IsNullOrWhiteSpace(parentId))
                {
                    continue;
                }

                if (!childrenByParent.TryGetValue(parentId, out List<string> children))
                {
                    children = new List<string>();
                    childrenByParent[parentId] = children;
                }

                children.Add(entry.Key);
            }

            List<FlatItemsDataClass> result = new List<FlatItemsDataClass>();
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootId in rootIds)
            {
                AddFlatItemTree(rootId, sourceById, childrenByParent, added, result);
            }

            return result;
        }

        private static void AddFlatItemTree(
            string itemId,
            Dictionary<string, FlatItemsDataClass> sourceById,
            Dictionary<string, List<string>> childrenByParent,
            HashSet<string> added,
            List<FlatItemsDataClass> result)
        {
            if (!added.Add(itemId) || !sourceById.TryGetValue(itemId, out FlatItemsDataClass item))
            {
                return;
            }

            result.Add(item);
            if (!childrenByParent.TryGetValue(itemId, out List<string> children))
            {
                return;
            }

            foreach (string childId in children)
            {
                AddFlatItemTree(childId, sourceById, childrenByParent, added, result);
            }
        }

        private static bool PlacementChanged(FlatItemsDataClass current, FlatItemsDataClass saved)
        {
            return !string.Equals(current._tpl.ToString(), saved._tpl.ToString(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(GetParentId(current), GetParentId(saved), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(current.slotId, saved.slotId, StringComparison.Ordinal)
                || !JsonTokenEquals(current.location, saved.location);
        }

        private static string GetParentId(FlatItemsDataClass item)
        {
            return item?.parentId?.ToString();
        }

        private static bool JsonTokenEquals(GClass846 left, GClass846 right)
        {
            Newtonsoft.Json.Linq.JToken leftToken = left?.JToken;
            Newtonsoft.Json.Linq.JToken rightToken = right?.JToken;
            if (IsNullOrEmptyJson(leftToken) && IsNullOrEmptyJson(rightToken))
            {
                return true;
            }

            return Newtonsoft.Json.Linq.JToken.DeepEquals(leftToken, rightToken);
        }

        private static bool IsNullOrEmptyJson(Newtonsoft.Json.Linq.JToken token)
        {
            return token == null
                || token.Type == Newtonsoft.Json.Linq.JTokenType.Null
                || (token.Type == Newtonsoft.Json.Linq.JTokenType.Object && !token.HasValues);
        }

        private static InventoryEquipment CreateSanitizedLoadoutEditorSaveEquipment()
        {
            InventoryEquipment sanitizedEquipment = IsRealDefaultLoadoutEditorCommit()
                ? LoadoutEditorProfile?.Inventory?.Equipment?.CloneItemWithSameId() as InventoryEquipment
                : LoadoutEditorProfile?.Inventory?.Equipment?.CloneItem(null) as InventoryEquipment;
            if (sanitizedEquipment == null)
            {
                throw new InvalidOperationException("Loadout editor equipment was unavailable for save.");
            }

            SanitizeLoadoutEditorEquipment(sanitizedEquipment);
            return sanitizedEquipment;
        }

        private static bool IsRealDefaultLoadoutEditorCommit()
        {
            return pitFireTeam.IsFollowerLoadoutRealTransferMode() && IsDefaultLoadoutEditorSelection();
        }

        private static FlatItemsDataClass[] CreateLoadoutEditorSaveStashItems()
        {
            Item stash = LoadoutEditorProfile?.Inventory?.Stash;
            if (stash == null)
            {
                throw new InvalidOperationException("Loadout editor player stash was unavailable for real item save.");
            }

            FlatItemsDataClass[] serializedStash = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { stash });
            if (serializedStash == null || serializedStash.Length == 0)
            {
                throw new InvalidOperationException("Loadout editor player stash was unavailable for real item save.");
            }

            FlatItemsDataClass[] sanitizedStash = RemoveLoadoutEditorEquipmentItemsFromSavedStash(serializedStash);
            if (sanitizedStash == null || sanitizedStash.Length == 0)
            {
                throw new InvalidOperationException("Loadout editor player stash was unavailable after real item save sanitization.");
            }

            return sanitizedStash;
        }

        private static void CaptureLoadoutEditorInitialState(Profile editorProfile, bool realItemCommit)
        {
            if (!realItemCommit)
            {
                LoadoutEditorInitialEquipmentItems = null;
                LoadoutEditorInitialStashItems = null;
                return;
            }

            InventoryEquipment equipment = editorProfile?.Inventory?.Equipment;
            Item stash = editorProfile?.Inventory?.Stash;
            LoadoutEditorInitialEquipmentItems = equipment == null
                ? null
                : Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { equipment });
            LoadoutEditorInitialStashItems = stash == null
                ? null
                : Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { stash });
        }

        private static bool HasLoadoutEditorRealChanges(FlatItemsDataClass[] currentEquipment, FlatItemsDataClass[] currentStash)
        {
            if (LoadoutEditorInitialEquipmentItems == null || LoadoutEditorInitialStashItems == null)
            {
                return true;
            }

            return !FlatItemArraysEqual(LoadoutEditorInitialEquipmentItems, currentEquipment)
                || !FlatItemArraysEqual(LoadoutEditorInitialStashItems, currentStash);
        }

        private static bool HasPendingLoadoutEditorRealChanges()
        {
            if (!IsRealDefaultLoadoutEditorCommit())
            {
                return false;
            }

            try
            {
                InventoryEquipment equipment = LoadoutEditorProfile?.Inventory?.Equipment;
                Item stash = LoadoutEditorProfile?.Inventory?.Stash;
                if (equipment == null || stash == null)
                {
                    return true;
                }

                FlatItemsDataClass[] currentEquipment = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { equipment });
                FlatItemsDataClass[] currentStash = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { stash });
                return HasLoadoutEditorRealChanges(currentEquipment, currentStash);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Unable to compare loadout editor state before repair: {ex.Message}");
                return true;
            }
        }

        private static bool FlatItemArraysEqual(FlatItemsDataClass[] left, FlatItemsDataClass[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            Dictionary<string, FlatItemsDataClass> leftById = ToFlatItemDictionary(left);
            Dictionary<string, FlatItemsDataClass> rightById = ToFlatItemDictionary(right);
            if (leftById.Count != rightById.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, FlatItemsDataClass> entry in leftById)
            {
                if (!rightById.TryGetValue(entry.Key, out FlatItemsDataClass rightItem)
                    || !FlatItemStateEquals(entry.Value, rightItem))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FlatItemStateEquals(FlatItemsDataClass left, FlatItemsDataClass right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            return string.Equals(left._id.ToString(), right._id.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(left._tpl.ToString(), right._tpl.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetParentId(left), GetParentId(right), StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.slotId, right.slotId, StringComparison.Ordinal)
                && JsonTokenEquals(left.location, right.location)
                && JsonTokenEquals(left.upd, right.upd);
        }

        private static GClass3953 TryGetCustomBuildById(string buildId)
        {
            if (string.IsNullOrWhiteSpace(buildId) || ActiveProfileSession?.EquipmentBuildsStorage?.EquipmentBuilds == null)
            {
                return null;
            }

            foreach (KeyValuePair<MongoID, GClass3953> entry in ActiveProfileSession.EquipmentBuildsStorage.EquipmentBuilds)
            {
                if (entry.Value?.BuildType != EEquipmentBuildType.Custom)
                {
                    continue;
                }

                if (string.Equals(entry.Key.ToString(), buildId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private sealed class FriendlyTeammateDefaultEquipmentRequest
        {
            public string aid { get; set; }
            public FlatItemsDataClass[] items { get; set; }
            public FlatItemsDataClass[] playerStashItems { get; set; }
            public bool realItemCommit { get; set; }
        }

        private sealed class FriendlyTeammateDefaultEquipmentResponse
        {
            public bool realItemCommit { get; set; }
            public FlatItemsDataClass[] playerStashItems { get; set; }
            public FlatItemsDataClass[] playerNewStashItems { get; set; }
            public FlatItemsDataClass[] playerChangedStashItems { get; set; }
            public string[] playerDeletedStashItemIds { get; set; }
        }

        private sealed class FriendlyTeammateRepairEquipmentRequest
        {
            public string aid { get; set; }
            public string target { get; set; }
            public RepairItem[] repairKitsInfo { get; set; }
            public string traderId { get; set; }
            public float? repairCount { get; set; }
        }

        private sealed class FriendlyTeammateRepairEquipmentResponse
        {
            public string itemId { get; set; }
            public double? durability { get; set; }
            public double? maxDurability { get; set; }
            public FlatItemsDataClass[] playerStashItems { get; set; }
            public FlatItemsDataClass[] playerNewStashItems { get; set; }
            public FlatItemsDataClass[] playerChangedStashItems { get; set; }
            public string[] playerDeletedStashItemIds { get; set; }
        }

        private static async Task<bool> ShowReplaceBuildPromptAsync()
        {
            if (ItemUiContext.Instance == null)
            {
                return false;
            }

            GClass3834 messageWindow;
            try
            {
                return await ItemUiContext.Instance.ShowMessageWindow(
                    out messageWindow,
                    "EquipmentBuild/ReplaceMessage".Localized(null),
                    null,
                    false);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        private static async Task PersistTeammateLoadoutSelectionAsync(string teammateAccountId, MongoID loadoutId)
        {
            string persistedLoadoutId = loadoutId.ToString();
            if (string.IsNullOrWhiteSpace(teammateAccountId) || string.IsNullOrWhiteSpace(persistedLoadoutId))
            {
                return;
            }

            string responseJson = await Task.Run(() => RequestHandler.PostJson(
                LoadoutRoute,
                SerializeBody(new FriendlyTeammateLoadoutRequest
                {
                    aid = teammateAccountId,
                    loadoutId = persistedLoadoutId
                })));

            EnsureBodySuccess(responseJson);
        }

        private static void RefreshCurrentTeammateLoadoutSelector(ResultProfile profile)
        {
            if (profile == null
                || LoadoutSelector == null
                || ActiveProfileInventoryController == null
                || ActiveProfileSession == null
                || ActiveProfileScreen == null
                || ActiveProfilePlayerModelWindow == null)
            {
                return;
            }

            InventoryClothingSelectionPanel loadoutPanel = LoadoutSelector.GetComponent<InventoryClothingSelectionPanel>();
            FriendlyTeammateProfileOptions options = TryLoadProfileOptions(profile.AccountId);
            if (loadoutPanel == null || options == null)
            {
                return;
            }

            DisplayLoadoutOptions(
                ActiveProfileScreen,
                profile,
                ActiveProfileInventoryController,
                ActiveProfileSession,
                loadoutPanel,
                ActiveProfilePlayerModelWindow,
                options);

            RefreshPlayerVisualization(
                profile,
                ActiveProfileInventoryController,
                ActiveProfileSession,
                ActiveProfilePlayerModelWindow);
        }
    }
}
