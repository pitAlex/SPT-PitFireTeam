using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json;
using pitTeam.Patches;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using System.Reflection;
using EFT.InputSystem;
using TMPro;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class TeammateHiringPreview
    {
        internal sealed class Quote
        {
            public string quoteToken;
            public string aid;
            public int price;
            public bool supportsWithoutKit;
        }
        private sealed class PurchaseResponse { public JsonType.FlatItem[] playerStashItems; }
        private static Quote quote;
        private static AddTeammateCreationFlow.FriendlyTeammateCreateRequest selection;
        private static Action returnAction;
        private static OtherPlayerProfileScreen screen;
        private static EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController appearanceController;
        private static IEftSession session;
        private static InventoryController inventoryController;
        private static GameObject controls;
        private static DefaultUIButton confirmButton, cancelButton, regenerateButton, noKitButton;
        private static CustomTextMeshProUGUI priceLabel;
        private static PlayerModelView observedModel;
        private static bool busy, leaveToSquad, purchased, previewReady;
        private static Transform modelWindowParent;
        private static RectTransform movedWindow;
        private static int modelWindowSibling;
        private static readonly Dictionary<GameObject, bool> visibility = new Dictionary<GameObject, bool>();
        private static readonly List<RectState> rects = new List<RectState>();

        internal static async Task OpenAsync(Quote prepared, AddTeammateCreationFlow.FriendlyTeammateCreateRequest chosen, Action onReturn)
        {
            quote = prepared;
            selection = chosen;
            returnAction = onReturn;
            leaveToSquad = purchased = busy = false;
            appearanceController = EftScreenManager.Instance.CurrentScreenController
                as EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController;
            SetTransitionLoader(true);
            try
            {
                if (quote == null || string.IsNullOrEmpty(quote.quoteToken) || string.IsNullOrEmpty(quote.aid))
                    throw new InvalidOperationException("Missing teammate quote");
                OtherPlayerProfileScreenPatch.PrepareReturnOverride(OnClosed);
                var controller = await ItemUiContext.Instance.ShowPlayerProfileScreen(quote.aid, EItemViewType.OtherPlayerProfile);
                if (controller == null || screen == null) throw new InvalidOperationException("Unable to open hiring preview");
                RemoveAppearanceFromHistory(controller);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Hiring preview failed: {ex}");
                OtherPlayerProfileScreenPatch.ClearPendingReturnOverride();
                AddTeammateCreationFlow.ShowToast(Text("AddTeammateOpenFailed"));
                await CancelQuoteAsync();
                if (screen != null) { leaveToSquad = false; Close(); }
                else if (AddTeammateCreationFlow.IsActive) AddTeammateCreationFlow.PreviewOpenFailed();
                else
                {
                    await EftScreenManager.Instance.TryReturnToRootScreen();
                    AddTeammateCreationFlow.Resume(chosen, onReturn);
                }
            }
            finally { SetTransitionLoader(false); }
        }

        internal static bool TryShow(OtherPlayerProfileScreen target, OtherPlayerProfile profile, InventoryController controller, IEftSession eftSession)
        {
            if (quote == null || quote.aid != profile.AccountId) return false;
            screen = target;
            session = eftSession;
            inventoryController = controller;
            var modelWindow = Field<InventoryPlayerModelWithStatsWindow>(target, "_playerModelWithStatsWindow");
            var model = Field<PlayerModelView>(modelWindow, "_playerModelView");
            var back = Field<DefaultUIButton>(target, "_backButton");
            var template = Field<CustomTextMeshProUGUI>(modelWindow, "_nicknameLabel");
            var drag = Field<DragTrigger>(modelWindow, "_dragTrigger");
            var rotator = Field<Component>(modelWindow, "_rotator");
            var spinner = Field<ProgressSpinner>(model, "_progressSpinner");
            if (model == null || back == null || template == null) throw new InvalidOperationException("Missing stock preview controls");
            RemoveAppearanceFromHistory(EftScreenManager.Instance.CurrentScreenController
                as OtherPlayerProfileScreen.OtherPlayerProfileScreenController);

            var windowRect = modelWindow.transform as RectTransform;
            movedWindow = windowRect;
            modelWindowParent = windowRect.parent;
            modelWindowSibling = windowRect.GetSiblingIndex();
            rects.Add(new RectState(windowRect));
            windowRect.SetParent(target.transform, false);
            // Rotation input and the loading spinner can be siblings of the native model view.
            // Keep their stock handlers/lifecycle, not just the renderer object.
            KeepOnly(target.transform, new[] { model.transform, back.transform, drag?.transform, rotator?.transform, spinner?.transform }
                .Where(t => t != null).ToArray());
            HideProfileBadges(modelWindow);
            Hide(Field<Component>(target, "_reportPanel"));
            for (Transform current = model.transform; current != target.transform; current = current.parent)
            {
                if (!(current is RectTransform rect)) continue;
                if (rect != windowRect) rects.Add(new RectState(rect));
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f, .5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                if (rect == windowRect) break;
            }
            windowRect.anchorMin = new Vector2(.5f, 0);
            windowRect.anchorMax = new Vector2(.5f, 1);
            windowRect.offsetMin = new Vector2(-300, 200);
            windowRect.offsetMax = new Vector2(300, -140);
            if (drag != null && drag.transform != windowRect && drag.transform != model.transform
                && drag.transform is RectTransform dragRect && dragRect.IsChildOf(windowRect))
            {
                if (!rects.Any(state => state.Target == dragRect)) rects.Add(new RectState(dragRect));
                dragRect.anchorMin = Vector2.zero;
                dragRect.anchorMax = Vector2.one;
                dragRect.offsetMin = dragRect.offsetMax = Vector2.zero;
            }
            Logger.LogInfo($"[UI][Hiring] Preview aid={quote.aid} rotation={drag != null} spinner={spinner != null} loadingComplete={model.LoadingComplete}");

            controls = new GameObject("pitFireTeam_HiringPreview", typeof(RectTransform));
            controls.transform.SetParent(target.transform, false);
            var controlsRect = (RectTransform)controls.transform;
            controlsRect.anchorMin = Vector2.zero;
            controlsRect.anchorMax = Vector2.one;
            controlsRect.offsetMin = controlsRect.offsetMax = Vector2.zero;
            regenerateButton = Button(back, "TeammateHireRegenerate", 0, () => RegenerateAsync().HandleExceptions());
            var regenerateRect = (RectTransform)regenerateButton.transform;
            regenerateRect.anchorMin = regenerateRect.anchorMax = new Vector2(.5f, 1);
            regenerateRect.anchoredPosition = new Vector2(0, -95);
            regenerateRect.sizeDelta = new Vector2(300, 42);
            priceLabel = Label(template, string.Format(Text("TeammateHirePrice"), quote.price.ToString("N0", CultureInfo.CurrentCulture)),
                new Vector2(.5f, 0), new Vector2(0, 195), 28);
            cancelButton = Button(back, "Cancel", -115, () => { leaveToSquad = true; Close(); });
            ((RectTransform)cancelButton.transform).sizeDelta = new Vector2(170, 42);
            confirmButton = Button(back, "AddTeammateConfirm", 115, () => ConfirmAsync().HandleExceptions());
            noKitButton = Button(back, "TeammateHireWithoutKit", 0, () => ConfirmAsync(withoutKit: true).HandleExceptions());
            var noKitRect = (RectTransform)noKitButton.transform;
            noKitRect.anchoredPosition = new Vector2(0, 105);
            noKitRect.sizeDelta = new Vector2(360, 42);
            noKitButton.gameObject.SetActive(quote.supportsWithoutKit);
            ObserveModel(model);
            SetBusy(busy);
            return true;
        }

        private static void HideProfileBadges(InventoryPlayerModelWithStatsWindow window)
        {
            Hide(Field<Component>(window, "_prestigeImage"));
            Hide(Field<Component>(window, "_prestigeHover"));
            Hide(Field<Component>(window, "_playerLevelPanel"));
            var specialIcon = Field<ChatSpecialIcon>(window, "_specialIcon");
            // The nickname can share the special-icon container; hide only its images.
            Hide(Field<Component>(specialIcon, "_icon"));
            Hide(Field<Component>(specialIcon, "_iconPrestige"));
        }

        private static void Hide(Component component)
        {
            if (component == null) return;
            var target = component.gameObject;
            if (!visibility.ContainsKey(target)) visibility[target] = target.activeSelf;
            target.SetActive(false);
        }

        private static void ObserveModel(PlayerModelView model)
        {
            StopObservingModel();
            observedModel = model;
            previewReady = model.LoadingComplete;
            model.LoadingCompletedEvent += OnModelLoaded;
        }

        private static void StopObservingModel()
        {
            if (observedModel != null) observedModel.LoadingCompletedEvent -= OnModelLoaded;
            observedModel = null;
            previewReady = false;
        }

        private static void OnModelLoaded()
        {
            previewReady = true;
            SetBusy(busy);
        }

        private static void KeepOnly(Transform parent, Transform[] keep)
        {
            foreach (Transform child in parent)
            {
                if (keep.Contains(child)) continue;
                if (keep.Any(t => t.IsChildOf(child))) KeepOnly(child, keep);
                else { visibility[child.gameObject] = child.gameObject.activeSelf; child.gameObject.SetActive(false); }
            }
        }

        private static CustomTextMeshProUGUI Label(CustomTextMeshProUGUI template, string value, Vector2 anchor, Vector2 position, float fontSize)
        {
            var label = UnityEngine.Object.Instantiate(template, controls.transform, false);
            label.gameObject.SetActive(true);
            label.text = value;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            var rect = (RectTransform)label.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(700, 55);
            rect.anchoredPosition = position;
            return label;
        }

        private static DefaultUIButton Button(DefaultUIButton template, string key, float x, Action action)
        {
            var button = UnityEngine.Object.Instantiate(template, controls.transform, false);
            button.gameObject.SetActive(true);
            button.SetRawText(Text(key), button.HeaderSize);
            button.SetIcon(null);
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => { if (!busy) action(); });
            button.Interactable = true;
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(260, 42);
            rect.anchoredPosition = new Vector2(x, 150);
            return button;
        }

        private static async Task RegenerateAsync()
        {
            if (busy || purchased || screen == null) return;
            StopObservingModel();
            SetBusy(true);
            priceLabel.gameObject.SetActive(false);
            SetTransitionLoader(true);
            try
            {
                string json = await Task.Run(() => RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/prepare",
                    JsonConvert.SerializeObject(selection)));
                var response = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<Quote>>(json);
                if (response == null || response.err != 0)
                {
                    AddTeammateCreationFlow.ShowToast(Text(response?.errmsg ?? "TeammateHireRegenerateFailed"));
                    return;
                }
                // Prepare replaces the server's pending quote. Never allow purchasing the old
                // display after a failed/ambiguous refresh; Regenerate and Back remain available.
                quote = response.data;
                if (quote == null || string.IsNullOrEmpty(quote.quoteToken) || string.IsNullOrEmpty(quote.aid))
                    throw new InvalidOperationException("Missing regenerated teammate quote");
                var result = await session.GetOtherPlayerProfile(quote.aid);
                if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
                var profile = new OtherPlayerProfile(result.Value);
                if (profile.AccountId != quote.aid) throw new InvalidOperationException("Regenerated profile does not match quote");
                var window = Field<InventoryPlayerModelWithStatsWindow>(screen, "_playerModelWithStatsWindow");
                // Close the window's old model and drag subscriptions before loading its replacement.
                window.Close();
                window.Show(profile, profile.Info.Experience, profile.Info.SelectedMemberCategory,
                    profile.PmcStats.Eft, profile.PlayerVisualRepresentation, null, false);
                AccessTools.Field(typeof(OtherPlayerProfileScreen), "_profile").SetValue(screen, profile);
                // Native Show reactivates some decorations and clothing controls.
                foreach (var entry in visibility) if (entry.Key != null) entry.Key.SetActive(false);
                HideProfileBadges(window);
                priceLabel.text = string.Format(Text("TeammateHirePrice"), quote.price.ToString("N0", CultureInfo.CurrentCulture));
                priceLabel.gameObject.SetActive(true);
                ObserveModel(Field<PlayerModelView>(window, "_playerModelView"));
                Logger.LogInfo($"[UI][Hiring] Regenerated preview aid={quote.aid} price={quote.price}");
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Teammate regeneration failed: {ex}");
                AddTeammateCreationFlow.ShowToast(Text("TeammateHireRegenerateFailed"));
            }
            finally { SetBusy(false); SetTransitionLoader(false); }
        }

        private static async Task ConfirmAsync(bool withoutKit = false)
        {
            if (busy || !previewReady || quote == null || (withoutKit && (purchased || !quote.supportsWithoutKit))) return;
            bool refreshed = false;
            SetBusy(true);
            try
            {
                string json = await Task.Run(() => RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/create",
                    JsonConvert.SerializeObject(new { quoteToken = quote.quoteToken, withoutKit })));
                var response = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<PurchaseResponse>>(json);
                if (response == null || response.err != 0)
                {
                    if (response?.errmsg == "TeammateHireInsufficientFunds")
                    {
                        // Reuse vanilla's modal message window and insufficient-money caption.
                        // The server checks spendable stash roubles before mutating either profile.
                        await ItemUiContext.Instance.ShowMessageWindow(Text("TeammateHireInsufficientFunds"),
                            acceptAction: null, cancelAction: null,
                            caption: "ragfair/Not enough money".Localized(), forceShow: true).WindowResult;
                    }
                    else AddTeammateCreationFlow.ShowToast(Text(response?.errmsg ?? "TeammateHirePurchaseFailed"));
                    return;
                }
                purchased = leaveToSquad = true;
                // A repeated Confirm returns the receipt/stash without charging again, including after refresh failure.
                OtherPlayerProfileScreenPatch.ApplyServerSavedPlayerStash(session.Profile, inventoryController, session.RagFair,
                    response.data.playerStashItems);
                refreshed = true;
                SocialNetworkClassPatch.RefreshFriendsList();
                Components.SquadControlMenuUi.RequestRosterRefreshOnNextInject();
                AddTeammateCreationFlow.ShowToast(string.Format(Text("AddTeammateInProgress"), selection.nickname));
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Teammate confirmation failed: {ex}");
                AddTeammateCreationFlow.ShowToast(Text("TeammateHirePurchaseFailed"));
            }
            finally { SetBusy(false); }
            if (refreshed) Close();
        }

        internal static bool BlockClose(OtherPlayerProfileScreen target) => target == screen && busy;
        private static void SetBusy(bool value)
        {
            busy = value;
            if (confirmButton != null) confirmButton.Interactable = !value && previewReady;
            if (cancelButton != null) cancelButton.Interactable = !value;
            if (regenerateButton != null) regenerateButton.Interactable = !value && !purchased;
            if (noKitButton != null)
            {
                noKitButton.gameObject.SetActive(quote?.supportsWithoutKit == true);
                noKitButton.Interactable = !value && previewReady && !purchased && quote?.supportsWithoutKit == true;
            }
            var back = screen == null ? null : Field<DefaultUIButton>(screen, "_backButton");
            if (back != null) back.Interactable = !value;
        }
        private static void Close() { if (!busy && screen != null) screen.TranslateCommand(ECommand.Escape); }

        internal static void RestoreScreen(OtherPlayerProfileScreen target)
        {
            if (target != screen) return;
            StopObservingModel();
            foreach (var entry in visibility) if (entry.Key != null) entry.Key.SetActive(entry.Value);
            visibility.Clear();
            if (movedWindow != null && modelWindowParent != null)
            {
                movedWindow.SetParent(modelWindowParent, false);
                movedWindow.SetSiblingIndex(modelWindowSibling);
            }
            foreach (var rect in rects) rect.Restore();
            rects.Clear();
            movedWindow = null;
            modelWindowParent = null;
            if (controls != null) UnityEngine.Object.Destroy(controls);
            var back = Field<DefaultUIButton>(target, "_backButton");
            if (back != null) back.Interactable = true;
            controls = null;
            priceLabel = null;
            confirmButton = cancelButton = regenerateButton = noKitButton = null;
            screen = null;
        }

        private static void OnClosed() { FinishCloseAsync().HandleExceptions(); }
        private static async Task FinishCloseAsync()
        {
            var previousSelection = selection;
            var callback = returnAction;
            bool exit = leaveToSquad;
            SetTransitionLoader(true);
            try
            {
                if (!purchased) await CancelQuoteAsync();
                quote = null;
                bool returned = await EftScreenManager.Instance.TryReturnToRootScreen();
                Logger.LogInfo($"[UI][Hiring] Return destination={(exit ? "squad" : "appearance")} rootReturned={returned}");
                if (!returned) throw new InvalidOperationException("Hiring preview could not return to the menu root");
                if (exit)
                {
                    SquadSideSelectionFlow.Deactivate("return-from-hiring-preview");
                    callback?.Invoke();
                }
                else AddTeammateCreationFlow.Resume(previousSelection, callback);
            }
            finally { appearanceController = null; SetTransitionLoader(false); }
        }

        private static void RemoveAppearanceFromHistory(OtherPlayerProfileScreen.OtherPlayerProfileScreenController controller)
        {
            if (controller == null || appearanceController == null || !ReferenceEquals(controller.PreviousScreen, appearanceController)) return;
            // The account-creation controller was closed when opening this preview. Do not let
            // stock Back reopen it before our callback restores a fresh appearance session.
            controller.PreviousScreen = appearanceController.PreviousScreen;
            Logger.LogInfo("[UI][Hiring] Removed closed appearance controller from preview return history.");
        }

        internal static void SetTransitionLoader(bool visible)
        {
            if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                MonoBehaviourSingleton<PreloaderUI>.Instance.SetLoaderStatus(visible);
        }
        private static async Task CancelQuoteAsync()
        {
            string token = quote?.quoteToken;
            if (token == null) return;
            try { await Task.Run(() => RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/cancel", JsonConvert.SerializeObject(new { quoteToken = token }))); }
            catch (Exception ex) { pitFireTeam.Log.LogWarning($"[UI] Could not cancel pending hire; quote will expire: {ex.Message}"); }
        }
        private static string Text(string key) => pitFireTeam.GetSocialUiText(key);
        private static T Field<T>(object target, string name) where T : class => target == null ? null : AccessTools.Field(target.GetType(), name)?.GetValue(target) as T;

        private sealed class RectState
        {
            private readonly RectTransform rect;
            private readonly Vector2 min, max, pivot, size, position;
            internal RectTransform Target => rect;
            internal RectState(RectTransform target) { rect = target; min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot; size = rect.sizeDelta; position = rect.anchoredPosition; }
            internal void Restore() { if (rect == null) return; rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position; }
        }
    }

    internal class TeammateHiringPreviewInputPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(OtherPlayerProfileScreen), "TranslateCommand");
        [PatchPrefix]
        private static bool Prefix(OtherPlayerProfileScreen __instance, ref InputNode.ETranslateResult __result)
        {
            if (!TeammateHiringPreview.BlockClose(__instance)) return true;
            __result = InputNode.ETranslateResult.Block;
            return false;
        }
    }
}
