using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private enum TreatmentChoice { Cancel, Money, Medkit }
        private static MessageWindow TreatmentChoiceWindow;
        private static EquipItemWindow TreatmentMedkitWindow;

        private static async Task<TreatmentChoice> ChooseTreatmentAsync(OtherPlayerProfile profile, int price)
        {
            var ui = ItemUiContext.Instance;
            var window = UnityEngine.Object.Instantiate(ui._messageWindow, ui._messageWindow.transform.parent, false);
            TreatmentChoiceWindow = window;
            window.name = "pitFireTeam_TreatmentChoice";
            try
            {
                var context = ui.RegisterWindow(window, window.Show(GetSocialUiText("ProfileHealingTitle"),
                    string.Format(GetSocialUiText("ProfileHealingChoicePrompt"), profile.GetCorrectedNickname()), true));
                window._acceptButton.SetRawText(price < 0 ? GetSocialUiText("ProfileHealingMoneyUnavailable")
                    : string.Format(GetSocialUiText("ProfileHealingApplyCost"), price.ToString("N0")), 20);
                window.SetAcceptActive(price >= 0);
                window._cancelButton.SetRawText(GetSocialUiText("ProfileHealingUseMedkit"), 20);
                var choice = TreatmentChoice.Cancel;
                // Escape/X still cancel; only this button changes the selected payment method.
                window._cancelButton.OnClick.RemoveAllListeners();
                window._cancelButton.OnClick.AddListener(() => { choice = TreatmentChoice.Medkit; window.Decline(); });
                bool accepted = await context.WindowResult;
                return accepted && price >= 0 ? TreatmentChoice.Money : choice;
            }
            finally
            {
                if (TreatmentChoiceWindow == window) TreatmentChoiceWindow = null;
                if (window != null) { window.Close(); UnityEngine.Object.Destroy(window.gameObject); }
            }
        }

        private sealed class MedkitSelection : SelectableItemContext.ISelectionContext
        {
            private readonly Action<Item> select;
            public MedkitSelection(Action<Item> select) { this.select = select; }
            public event Action<Item, bool> OnItemSelected { add { } remove { } }
            public bool IsSelected(Item item) => false;
            public bool IsActive(ItemContext context, out string tooltip)
            {
                tooltip = string.Empty;
                return context.Item is MedKit && context.Item.PinLockState != EItemPinLockState.Locked
                    && context.Item.GetItemComponent<MedKitComponent>()?.HpResource > 0;
            }
            public void ToggleSelection(SelectableItemContext context)
            {
                if (IsActive(context, out _)) select(context.Item);
            }
        }

        private static async Task<Item> ChooseTreatmentMedkitAsync(IEftSession session, InventoryController inventory, IEnumerable<string> eligibleIds)
        {
            var ids = new HashSet<string>(eligibleIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var items = session.Profile.Inventory.AllRealPlayerItems.Where(item => ids.Contains(item.Id)
                && item is MedKit && item.GetItemComponent<MedKitComponent>()?.HpResource > 0).ToList();
            var ui = ItemUiContext.Instance;
            var template = ui._equipItemWindow;
            var window = UnityEngine.Object.Instantiate(template, template.transform.parent, false);
            TreatmentMedkitWindow = window;
            window.name = "pitFireTeam_TreatmentMedkits";
            try
            {
                // Use the native Available/search/resource views, with our own selection
                // context. Its normal Show/ToggleSelection would equip/move the chosen item.
                var context = ui.RegisterWindow(window, ((Window<WindowContext>)window).Show());
                window.Caption.text = GetSocialUiText("ProfileHealingMedkitsTitle");
                foreach (Transform child in window._viewsContainer)
                {
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                SetMedkitWindowField(window, "_itemUiContext", ui);
                SetMedkitWindowField(window, "_inventoryController", inventory);
                SetMedkitWindowField(window, "_sills", session.Profile.Skills);
                SetMedkitWindowField(window, "_refreshRequired", false);
                window._inputField.SetTextWithoutNotify(string.Empty);
                SetMedkitWindowField(window, "_filter", string.Empty);
                Item selected = null;
                var selection = new MedkitSelection(item => { selected = item; window.Close(); });
                bool textOnly = false;
                Action render = () =>
                {
                    SetMedkitWindowField(window, "_itemContext", new FirstLevelSelectableItemContext(
                        textOnly ? EItemViewType.SearchMinimal : EItemViewType.Search, selection));
                    window._gridLayoutGroup.cellSize = textOnly ? new Vector2(500, 28) : new Vector2(125, 125);
                    window._gridLayoutGroup.spacing = textOnly ? new Vector2(2, 2) : new Vector2(8, 8);
                    window._gridLayoutGroup.constraintCount = textOnly ? 1 : Mathf.Clamp(items.Count, 3, 6);
                    window._viewTypeImage.transform.localScale = new Vector3(textOnly ? -1 : 1, 1, 1);
                    window._placeHolder.SetActive(items.Count == 0);
                    window.CreateItemViews(items);
                };
                window._toggleViewButton.gameObject.SetActive(true);
                window._toggleViewButton.onClick.RemoveAllListeners();
                window._toggleViewButton.onClick.AddListener(() => { textOnly = !textOnly; render(); });
                render();
                window.WindowTransform.anchorMin = window.WindowTransform.anchorMax = new Vector2(0.5f, 0.5f);
                window.WindowTransform.pivot = new Vector2(0.5f, 0.5f);
                window.WindowTransform.anchoredPosition = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                window.CorrectPosition(default);
                var closed = new TaskCompletionSource<bool>();
                context.OnClose += () => closed.TrySetResult(true);
                await closed.Task;
                return selected;
            }
            finally
            {
                if (TreatmentMedkitWindow == window) TreatmentMedkitWindow = null;
                if (window != null) { window.Close(); UnityEngine.Object.Destroy(window.gameObject); }
            }
        }

        private static void SetMedkitWindowField(EquipItemWindow window, string name, object value)
        {
            var field = AccessTools.Field(typeof(EquipItemWindow), name);
            if (field == null) throw new MissingFieldException(typeof(EquipItemWindow).FullName, name);
            field.SetValue(window, value);
        }

        private static void ApplyTreatmentMedkitState(IEftSession session, OfflineInventoryController inventory, TeammateTreatmentResponse response)
        {
            var player = session.Profile;
            string id = response.usedMedkitId;
            var current = player.Inventory.AllRealPlayerItems.SingleOrDefault(item => item.Id == id);
            if (response.medkitResourceRemaining == null || (current == null && response.medkitItem != null))
                throw CreateLiveStashRefreshException("Treatment medkit is missing from the live player inventory.");
            var updater = new ProfileUpdatesHandler(player, inventory, null, session.RagFair);
            ((IProfileUpdatesHandler)updater).UpdateProfile(new ProfileChanges
            {
                Stash = new StashChangesResponse
                {
                    @new = Array.Empty<JsonType.FlatItem>(),
                    change = response.medkitItem == null ? Array.Empty<JsonType.FlatItem>() : new[] { response.medkitItem },
                    del = CreateDeletedFlatItems(response.medkitItem == null ? new[] { id } : Array.Empty<string>())
                }
            });
            var updated = player.Inventory.AllRealPlayerItems.SingleOrDefault(item => item.Id == id);
            if (response.medkitItem == null ? updated != null : updated?.GetItemComponent<MedKitComponent>() == null
                || Math.Abs(updated.GetItemComponent<MedKitComponent>().HpResource - response.medkitResourceRemaining.Value) > 0.001)
                throw CreateLiveStashRefreshException("Treatment medkit refresh did not match the server-saved resource.");
            Modules.Logger.LogInfo("[UI] Applied verified medkit treatment refresh.");
        }
    }
}
