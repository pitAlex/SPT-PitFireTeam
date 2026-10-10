using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Patches
{
    internal sealed class GearSwapPanelPatch : ModulePatch
    {
        private static readonly Dictionary<ComplexStashPanel, Action> Cleanup = new Dictionary<ComplexStashPanel, Action>();
        internal static void Restore(ComplexStashPanel panel)
        {
            if (!Cleanup.TryGetValue(panel, out Action cleanup)) return;
            Cleanup.Remove(panel);
            cleanup();
        }
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ComplexStashPanel), nameof(ComplexStashPanel.Show));
        [PatchPostfix]
        private static void Postfix(ComplexStashPanel __instance, InventoryEquipment equipment)
        {
            var session = TeammateGearSwap.Current;
            if (session == null || !ReferenceEquals(equipment, session.FollowerEquipment)) return;
            try { BuildPanel(__instance, session); }
            catch (Exception ex)
            {
                pitTeam.Modules.Logger.LogError("[SwapGear] Could not create Apply panel: " + ex);
                Restore(__instance);
                session.Close();
            }
        }

        private static void BuildPanel(ComplexStashPanel panel, TeammateGearSwap session)
        {
            Restore(panel);
            panel._containerName.text = session.Bot.Profile.Nickname;
            panel._containerNamePanel.SetActive(true);
            var header = (RectTransform)panel._containerNamePanel.transform;
            var textRect = panel._containerName.rectTransform;
            var equipmentPanel = panel._lootPanel.GetComponentInChildren<EquipmentTab>(true);
            var helmet = equipmentPanel?.GetSlotView(EquipmentSlot.Headwear)?.transform as RectTransform;
            GameObject buttonObject = null;
            Canvas.WillRenderCanvases positionButton = null;
            Cleanup[panel] = () =>
            {
                if (positionButton != null) Canvas.willRenderCanvases -= positionButton;
                if (buttonObject != null) UnityEngine.Object.Destroy(buttonObject);
            };
            float buttonHeight = Mathf.Max(30f, panel._containerName.fontSize + 12f);

            buttonObject = new GameObject("pitFireTeam_SwapGearApply", typeof(RectTransform), typeof(LayoutElement));
            buttonObject.transform.SetParent(header.parent, false);
            buttonObject.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            // Clone the same Back control used by the inventory: its textured hover,
            // text animation and native pointer/click sounds remain single-owned.
            DefaultUIButton template = CommonUI.Instance?.InventoryScreen?._backButton;
            if (template == null) throw new InvalidOperationException("Inventory Back button template is unavailable.");
            DefaultUIButton button = UnityEngine.Object.Instantiate(template, rect, false);
            button.name = "ApplyButton";
            button.OnClick.RemoveAllListeners();
            button.SetIcon(null);
            button.Interactable = true;
            button._onPointerEnterSound = true;
            button._onPointerClickSound = true;
            button.SetRawText(pitFireTeam.GetSocialUiText("SwapGearApply"), Mathf.RoundToInt(panel._containerName.fontSize));
            var label = button._headerLabel;
            label.fontStyle |= FontStyles.Bold;
            float buttonWidth = Mathf.Max(160f, label.preferredWidth + 48f);
            rect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.offsetMin = buttonRect.offsetMax = Vector2.zero;
            buttonRect.localScale = Vector3.one;
            button.gameObject.SetActive(true);

            // Keep the native LOOT/name geometry. Follow the helmet's horizontal center only,
            // so scrolling equipment never moves the action out of the header row.
            positionButton = () =>
            {
                if (rect == null || textRect == null || header == null) return;
                Vector3 nameCenter = textRect.TransformPoint(textRect.rect.center);
                Vector3 center = helmet != null ? helmet.TransformPoint(helmet.rect.center) :
                    ((RectTransform)header.parent).TransformPoint(((RectTransform)header.parent).rect.center);
                rect.position = new Vector3(center.x, nameCenter.y, nameCenter.z);
            };
            Canvas.willRenderCanvases += positionButton;
            positionButton();

            // Reuse the actual item-thumbnail loading prefab, including its artwork/animation.
            GameObject loaderTemplate = panel.GetComponentsInChildren<ItemView>(true)
                .Select(view => view._iconLoader).FirstOrDefault(loader => loader != null)
                ?? Resources.FindObjectsOfTypeAll<ItemView>()
                    .Select(view => view._iconLoader).FirstOrDefault(loader => loader != null)
                ?? Resources.FindObjectsOfTypeAll<ItemIconView>()
                    .Select(view => view.IconLoader).FirstOrDefault(loader => loader != null);
            if (loaderTemplate == null) throw new InvalidOperationException("Item loading indicator template is unavailable.");
            var progress = UnityEngine.Object.Instantiate(loaderTemplate, rect, false);
            progress.name = "TransferProgress";
            progress.SetActive(false);
            var progressRect = (RectTransform)progress.transform;
            progressRect.anchorMin = progressRect.anchorMax = progressRect.pivot = new Vector2(0.5f, 0.5f);
            progressRect.anchoredPosition = Vector2.zero;
            progressRect.sizeDelta = new Vector2(24f, 24f);
            progressRect.localScale = Vector3.one;
            foreach (var graphic in progress.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var notice = new GameObject("TransferNotice", typeof(RectTransform), typeof(Image));
            notice.SetActive(false);
            notice.transform.SetParent(rect, false);
            var noticeRect = (RectTransform)notice.transform;
            noticeRect.anchorMin = noticeRect.anchorMax = new Vector2(0.5f, 0f);
            noticeRect.pivot = new Vector2(0.5f, 1f);
            noticeRect.anchoredPosition = new Vector2(0f, -4f);
            noticeRect.sizeDelta = new Vector2(440f, 48f);
            notice.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.04f, 0.95f);
            notice.GetComponent<Image>().raycastTarget = false;
            var noticeLabel = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            noticeLabel.transform.SetParent(noticeRect, false);
            var noticeText = noticeLabel.GetComponent<TextMeshProUGUI>();
            noticeText.font = label.font;
            noticeText.fontSize = 17f;
            noticeText.color = new Color(0.9f, 0.85f, 0.6f);
            noticeText.alignment = TextAlignmentOptions.Center;
            noticeText.enableWordWrapping = true;
            noticeText.raycastTarget = false;
            noticeText.text = pitFireTeam.GetSocialUiText("SwapGearTransferInProgress");
            noticeText.rectTransform.anchorMin = Vector2.zero;
            noticeText.rectTransform.anchorMax = Vector2.one;
            noticeText.rectTransform.offsetMin = new Vector2(8f, 4f);
            noticeText.rectTransform.offsetMax = new Vector2(-8f, -4f);
            button.OnClick.AddListener(async () =>
            {
                if (session.Applying) return;
                button.Interactable = false;
                button.gameObject.SetActive(false);
                progress.SetActive(true);
                notice.SetActive(true);
                await session.Apply();
                if (ReferenceEquals(TeammateGearSwap.Current, session) && button != null)
                {
                    progress.SetActive(false);
                    notice.SetActive(false);
                    button.gameObject.SetActive(true);
                    button.Interactable = true;
                }
            });
        }
    }

    internal sealed class GearSwapPanelClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ComplexStashPanel), nameof(ComplexStashPanel.Close));
        [PatchPrefix]
        private static void Prefix(ComplexStashPanel __instance) => GearSwapPanelPatch.Restore(__instance);
    }

    internal static class GearSwapInteractions
    {
        internal static bool Allowed(EItemInfoButton action, Item item = null) =>
            !(action == EItemInfoButton.Open && TeammateGearSwap.Current?.IsOpaqueBackpack(item) == true) &&
            (action == EItemInfoButton.Inspect ||
            action == EItemInfoButton.Open || action == EItemInfoButton.Equip || action == EItemInfoButton.Unequip ||
            action == EItemInfoButton.Install || action == EItemInfoButton.Uninstall ||
            action == EItemInfoButton.Load || action == EItemInfoButton.Unload ||
            action == EItemInfoButton.Fold || action == EItemInfoButton.Unfold);
    }
    internal sealed class GearSwapBackpackSlotContentsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SearchableSlotView), nameof(SearchableSlotView.ShowContent));
        [PatchPrefix]
        private static bool Prefix(SearchableSlotView __instance, Item item)
        {
            if (TeammateGearSwap.Current?.IsOpaqueBackpack(item) != true) return true;
            GearSwapBackpackPresentation.CloseContents(__instance._searchableItemView, __instance);
            if (__instance._specSlotsPanel != null) __instance._specSlotsPanel.gameObject.SetActive(false);
            return false;
        }
    }
    internal static class GearSwapBackpackPresentation
    {
        internal static void CloseContents(SearchableItemView contents, SlotView slot = null)
        {
            if (contents == null) return;
            slot = slot ?? contents.GetComponent<SlotView>();
            // UIElement.Close disables the entire GameObject. Native searchable-slot
            // prefabs may share that object with the equipment row. Dispose the contents
            // without hiding the slot/header/drop target; no grids are created here.
            bool containsSlot = slot != null && slot.transform.IsChildOf(contents.transform);
            contents.Close();
            if (containsSlot)
            {
                contents.ShowGameObject();
                slot.ShowGameObject();
            }
        }
    }
    internal sealed class GearSwapBackpackContentsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SearchableItemView), nameof(SearchableItemView.Show));
        [PatchPrefix]
        private static bool Prefix(SearchableItemView __instance, CompoundItem compoundItem)
        {
            if (TeammateGearSwap.Current?.IsOpaqueBackpack(compoundItem) != true) return true;
            GearSwapBackpackPresentation.CloseContents(__instance);
            return false;
        }
    }
    internal sealed class GearSwapBackpackOpenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.OpenItem));
        [PatchPrefix]
        private static bool Prefix(CompoundItem item) => TeammateGearSwap.Current?.IsOpaqueBackpack(item) != true;
    }
    internal sealed class GearSwapArmorSlotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ArmorSlot), nameof(ArmorSlot.CanAcceptRaid));
        [PatchPrefix]
        private static bool Prefix(ArmorSlot __instance, ref InventoryError error, ref bool __result)
        {
            if (TeammateGearSwap.Current?.CanEditPlateSlot(__instance, allowCommit: true) != true) return true;
            error = null;
            __result = true;
            return false;
        }
    }
    internal sealed class GearSwapArmorSlotUiPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.GetModLockedState));
        [PatchPostfix]
        private static void Postfix(Slot slot, ref KeyValuePair<EModLockedState, ModSlotView.TooltipData> __result)
        {
            if (__result.Key != EModLockedState.RaidLock || !(slot.ContainedItem is ArmorPlate) ||
                TeammateGearSwap.Current?.CanEditPlateSlot(slot) != true) return;
            __result = new KeyValuePair<EModLockedState, ModSlotView.TooltipData>(EModLockedState.Unlocked,
                new ModSlotView.TooltipData { ItemName = slot.ContainedItem.Name.Localized(null), Error = string.Empty });
        }
    }
    internal sealed class GearSwapContextPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BaseItemContextInteractions), nameof(BaseItemContextInteractions.IsActive));
        [PatchPostfix]
        private static void Postfix(BaseItemContextInteractions __instance, EItemInfoButton button, ref bool __result)
        {
            if (TeammateGearSwap.Current?.IsDraft(__instance?.Item) == true && !GearSwapInteractions.Allowed(button, __instance.Item)) __result = false;
        }
    }
    internal sealed class GearSwapExecuteInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BaseItemContextInteractions), nameof(BaseItemContextInteractions.ExecuteInteractionInternal));
        [PatchPrefix]
        private static bool Prefix(BaseItemContextInteractions __instance, EItemInfoButton interaction) =>
            TeammateGearSwap.Current?.IsDraft(__instance?.Item) != true || GearSwapInteractions.Allowed(interaction, __instance.Item);
    }
    internal sealed class GearSwapDiscardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.Discard),
            new[] { typeof(Item), typeof(ItemController), typeof(bool), typeof(bool) });
        [PatchPrefix]
        private static bool Prefix(Item item, ref OperationResult<DiscardResult> __result)
        {
            if (TeammateGearSwap.Current?.IsDraft(item) != true) return true;
            __result = new StringError(pitFireTeam.GetSocialUiText("SwapGearActionBlocked"));
            return false;
        }
    }
    internal sealed class GearSwapModifyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.CanModifyItem));
        [PatchPrefix]
        private static bool Prefix(Item item, ref Error error, ref bool __result)
        {
            var session = TeammateGearSwap.Current;
            bool blocked = session != null && (session.IsLiveLocked(item) ||
                (session.IsDraft(item) && (session.Applying || !session.CanEditDraft(item))));
            if (!blocked) return true;
            error = new ItemManipulator.ItemManuallyLockedError(item);
            __result = false;
            return false;
        }
    }
    internal sealed class GearSwapDestinationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.CanTransferTo),
            new[] { typeof(ItemAddress), typeof(ItemController), typeof(Error).MakeByRefType() });
        [PatchPrefix]
        private static bool Prefix(ItemAddress to, ItemController controller, ref Error error, ref bool __result)
        {
            if (!(controller is GearSwapInventoryController draft) ||
                (draft.Session != null && !draft.Session.Applying && draft.Session.CanPlaceDraft(to))) return true;
            error = new ItemManipulator.ItemManuallyLockedError(to.Container.ParentItem);
            __result = false;
            return false;
        }
    }
    internal sealed class GearSwapWeaponUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BotWeaponManager), nameof(BotWeaponManager.ManualUpdate));
        [PatchPrefix]
        private static bool Prefix(BotWeaponManager __instance) => !TeammateGearSwap.Holds(__instance);
    }

    internal sealed class GearSwapHealthUsePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(EFT.HealthSystem.PlayerHealthController),
            nameof(EFT.HealthSystem.PlayerHealthController.ApplyItem),
            typeof(EFT.HealthSystem.PlayerHealthController).GetMethods()
                .Single(m => m.Name == nameof(EFT.HealthSystem.PlayerHealthController.ApplyItem) &&
                    m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.IsGenericType)
                .GetParameters().Select(p => p.ParameterType).ToArray());
        [PatchPrefix]
        private static bool Prefix(Item item, ref bool __result)
        {
            if (TeammateGearSwap.Current?.IsDraft(item) != true) return true;
            __result = false;
            return false;
        }
    }

    internal sealed class GearSwapTabPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(TabGroup), nameof(TabGroup.SelectTab));
        [PatchPrefix]
        private static bool Prefix(Tab tab)
        {
            if (TeammateGearSwap.Current == null) return true;
            var tabs = CommonUI.Instance?.InventoryScreen?._tabDictionary;
            if (tabs == null) return true;
            foreach (var entry in tabs)
                if (ReferenceEquals(entry.Value, tab)) return entry.Key == EInventoryTab.Gear;
            return true;
        }
    }
}
