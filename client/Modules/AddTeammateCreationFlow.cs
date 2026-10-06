using Comfort.Common;
using Arena.UI;
using EFT;
using EFT.Communications;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using pitTeam.Patches;

namespace pitTeam.Modules
{
    internal static class AddTeammateCreationFlow
    {
        private const float MinimumReturnLoaderDurationSeconds = 0.1f;

        private static readonly Type SideSelectionScreenType = typeof(AccountSideSelectionScreen<EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController, EEftScreenType>);
        private static readonly System.Reflection.MethodInfo AdvanceStateMethod = AccessTools.Method(SideSelectionScreenType, "ShowAnotherState");
        private static readonly System.Reflection.FieldInfo CurrentStateIndexField = AccessTools.Field(SideSelectionScreenType, "_currentSelectedStateIndex");
        private static readonly System.Reflection.FieldInfo SideSelectionStateField = AccessTools.Field(SideSelectionScreenType, "_sideSelectionState");
        private static readonly System.Reflection.FieldInfo DescriptionCanvasGroupField = AccessTools.Field(typeof(SideSelectionState), "_descriptionCanvasGroup");
        private static readonly System.Reflection.FieldInfo SelectSideNodesField = AccessTools.Field(typeof(SideSelectionState), "_selectSideNodes");
        private static readonly System.Reflection.PropertyInfo PreviewProperty = AccessTools.Property(typeof(SideSelectionState.SelectSideNode), "Preview");
        private static readonly System.Reflection.FieldInfo BackButtonField = AccessTools.Field(SideSelectionScreenType, "_backButton");
        private static readonly System.Reflection.FieldInfo HeadSelectionStateField = AccessTools.Field(SideSelectionScreenType, "_headSelectionState");
        private static readonly System.Reflection.FieldInfo NextButtonField = AccessTools.Field(SideSelectionScreenType, "_nextButton");
        private static readonly System.Reflection.FieldInfo NicknameInputField = AccessTools.Field(typeof(NicknameField), "_inputField");
        private static JsonConverter[] defaultJsonConverters;
        private static EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController activeController;
        private static Coroutine advanceCoroutine;
        private static Coroutine returnCoroutine;
        private static Coroutine submitCoroutine;
        private static bool skipScheduled;
        private static bool submitInProgress;
        private static bool returnInProgress;
        private static bool nicknamePrepared;
        private static bool suppressSkippedSideSelectionModelView;
        private static Button wiredNextButton;
        private static Button wiredBackButton;
        private static UnityAction nextButtonAction;
        private static UnityAction backButtonAction;
        private static Action pendingReturnAction;
        private static FriendlyTeammateCreateRequest resumeSelection;

        public static bool IsActive => activeController != null;
        public static bool SuppressSkippedSideSelectionModelView => suppressSkippedSideSelectionModelView;
        internal static string PreviousHead => resumeSelection?.head;
        internal static string PreviousVoice => resumeSelection?.voice;

        public static void Start(Action onReturn = null)
        {
            if (GameplayModeRuntime.IsAllegiance || GameplayModeRuntime.IsSwitching)
            {
                ShowToast(GetLocalizedSocialUi("SettingsUnavailableInAllegiance"));
                return;
            }
            if (activeController != null)
            {
                ShowToast(GetLocalizedSocialUi("AddTeammateFlowActive"));
                return;
            }

            pendingReturnAction = onReturn;
            resumeSelection = null;
            StartInternal().HandleExceptions();
        }

        internal static void Resume(FriendlyTeammateCreateRequest selection, Action onReturn)
        {
            pendingReturnAction = onReturn;
            resumeSelection = selection;
            StartInternal().HandleExceptions();
        }

        public static bool IsActiveForController(object controller)
        {
            return controller != null && ReferenceEquals(controller, activeController);
        }

        public static void ReturnToMainScreen()
        {
            if (pendingReturnAction != null)
            {
                if (pitFireTeam.Instance == null)
                {
                    EFT.UI.Screens.EftScreenManager.Instance.TryReturnToRootScreen().HandleExceptions();
                    InvokePendingReturnAction();
                    return;
                }

                if (returnCoroutine != null)
                {
                    pitFireTeam.Instance.StopCoroutine(returnCoroutine);
                }

                returnInProgress = true;
                returnCoroutine = pitFireTeam.Instance.StartCoroutine(ReturnToPendingScreen());
                return;
            }

            if (pitFireTeam.Instance == null)
            {
                EFT.UI.Screens.EftScreenManager.Instance.TryReturnToRootScreen().HandleExceptions();
                InvokePendingReturnAction();
                return;
            }

            if (returnCoroutine != null)
            {
                pitFireTeam.Instance.StopCoroutine(returnCoroutine);
            }

            returnInProgress = true;
            returnCoroutine = pitFireTeam.Instance.StartCoroutine(ReturnToMainScreenWithOverlay());
        }

        public static void RefreshSubmitButton()
        {
            if (!MonoBehaviourSingleton<LoginUI>.Instantiated)
            {
                return;
            }

            ConfigureHeadSelectionUi(MonoBehaviourSingleton<LoginUI>.Instance.SideSelectionScreen);
        }

        public static bool TryCompleteFromCurrentScreen()
        {
            if (submitInProgress)
            {
                return false;
            }

            if (!MonoBehaviourSingleton<LoginUI>.Instantiated)
            {
                return false;
            }

            EftAccountSideSelectionScreen screen = MonoBehaviourSingleton<LoginUI>.Instance.SideSelectionScreen;
            if (screen == null)
            {
                return false;
            }

            HeadSelectionState headSelectionState = HeadSelectionStateField?.GetValue(screen) as HeadSelectionState;
            if (headSelectionState == null || headSelectionState._profileData == null)
            {
                return false;
            }

            string nickname = GetNicknameText(headSelectionState);
            ENicknameError error = headSelectionState._nicknameField != null
                ? headSelectionState._nicknameField.ValidationError(nickname)
                : ENicknameError.InvalidNickname;

            if (error != ENicknameError.ValidNickname)
            {
                headSelectionState._nicknameField?.ShowNicknameError(error, false);
                ConfigureHeadSelectionUi(screen);
                return false;
            }

            headSelectionState._profileData.Nickname = nickname;

            FriendlyTeammateCreateRequest payload = new FriendlyTeammateCreateRequest
            {
                nickname = headSelectionState._profileData.Nickname,
                voice = headSelectionState._profileData.VoiceId,
                head = headSelectionState._profileData.HeadId
            };

            string json = JsonConvert.SerializeObject(payload);
            Logger.LogInfo($"[UI] Add teammate selection complete: {json}");

            submitInProgress = true;
            ConfigureHeadSelectionUi(screen);

            if (submitCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(submitCoroutine);
            }

            submitCoroutine = pitFireTeam.Instance.StartCoroutine(SubmitTeammateCoroutine(payload));
            return true;
        }

        private static async Task StartInternal()
        {
            try
            {
                TarkovApplication app = ResolveApplication();
                EFT.IEftSession session = app?.Session;
                if (session == null)
                {
                    ShowToast(GetLocalizedSocialUi("AddTeammateOpenFailed"));
                    pitFireTeam.Log.LogError("[UI] Could not start add teammate flow: session is not available.");
                    return;
                }

                EPlayerSide playerSide = session.Profile?.Info?.Side ?? EPlayerSide.Usec;
                if (playerSide != EPlayerSide.Bear && playerSide != EPlayerSide.Usec)
                {
                    ShowToast(GetLocalizedSocialUi("AddTeammateUnsupportedSide"));
                    pitFireTeam.Log.LogWarning($"[UI] Add teammate flow aborted because current side is {playerSide}.");
                    return;
                }

                if (MonoBehaviourSingleton<LoginUI>.Instantiated)
                {
                    MonoBehaviourSingleton<LoginUI>.Instance.gameObject.SetActive(true);
                }

                await EnsureDefaultProfilesLoaded();
                if (EFT.CreateProfileOperation._bearProfile == null || EFT.CreateProfileOperation._usecProfile == null)
                {
                    ShowToast(GetLocalizedSocialUi("AddTeammateOpenFailed"));
                    pitFireTeam.Log.LogError("[UI] Could not start add teammate flow: default preview profiles are missing.");
                    return;
                }

                EFT.CreateProfileOperation.PreliminaryProfileData profileData = new EFT.CreateProfileOperation.PreliminaryProfileData
                {
                    Side = playerSide,
                    Nickname = resumeSelection?.nickname,
                    HeadId = resumeSelection?.head,
                    VoiceId = resumeSelection?.voice
                };

                var controller = new EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController(
                    EFT.CreateProfileOperation._bearProfile,
                    EFT.CreateProfileOperation._usecProfile,
                    profileData,
                    session.SessionMode,
                    false,
                    string.Empty);

                controller.OnNickNameSubmitted += HandleNicknameSubmitted;
                controller.OnShowNextScreen += HandleFlowCompleted;
                controller.OnShow += HandleScreenShown;
                controller.OnClose += CleanupActiveFlow;

                activeController = controller;
                skipScheduled = false;
                nicknamePrepared = false;
                suppressSkippedSideSelectionModelView = true;
                controller.ShowScreen(EScreenState.Queued);
            }
            catch (Exception ex)
            {
                CleanupActiveFlow();
                ShowToast(GetLocalizedSocialUi("AddTeammateOpenFailed"));
                pitFireTeam.Log.LogError("[UI] Failed to open add teammate creation flow.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static async Task EnsureDefaultProfilesLoaded()
        {
            if (EFT.CreateProfileOperation._bearProfile == null)
            {
                EFT.CreateProfileOperation._bearProfile = await EFT.CreateProfileOperation.LoadProfile("DefaultBearProfile");
            }

            if (EFT.CreateProfileOperation._usecProfile == null)
            {
                EFT.CreateProfileOperation._usecProfile = await EFT.CreateProfileOperation.LoadProfile("DefaultUsecProfile");
            }
        }

        private static void HandleNicknameSubmitted(string nickname)
        {
            activeController?.ShowNicknameError(ENicknameError.ValidNickname);
        }

        private static void HandleScreenShown()
        {
            if (skipScheduled || activeController == null || pitFireTeam.Instance == null)
            {
                return;
            }

            skipScheduled = true;
            advanceCoroutine = pitFireTeam.Instance.StartCoroutine(AdvanceToHeadSelection());
        }

        private static IEnumerator AdvanceToHeadSelection()
        {
            int attemptsRemaining = 60;
            while (attemptsRemaining-- > 0 && activeController != null)
            {
                if (MonoBehaviourSingleton<LoginUI>.Instantiated)
                {
                    EftAccountSideSelectionScreen screen = MonoBehaviourSingleton<LoginUI>.Instance.SideSelectionScreen;
                    if (screen != null)
                    {
                        int currentStateIndex = CurrentStateIndexField?.GetValue(screen) as int? ?? -1;
                        if (currentStateIndex == 0)
                        {
                            try
                            {
                                Task advanceTask = AdvanceStateMethod?.Invoke(screen, new object[] { 1 }) as Task;
                                advanceTask?.HandleExceptions();
                                HideSkippedSideSelectionVisuals(screen);
                            }
                            catch (Exception ex)
                            {
                                pitFireTeam.Log.LogError("[UI] Failed to skip side selection for add teammate flow.");
                                pitFireTeam.Log.LogError(ex);
                            }
                            break;
                        }

                        if (currentStateIndex == 1)
                        {
                            HideSkippedSideSelectionVisuals(screen);
                            ConfigureHeadSelectionUi(screen);
                            break;
                        }
                    }
                }

                yield return null;
            }

            advanceCoroutine = null;
        }

        private static void HandleFlowCompleted()
        {
            TryCompleteFromCurrentScreen();
        }

        private static void CleanupActiveFlow()
        {
            if (activeController != null)
            {
                activeController.OnNickNameSubmitted -= HandleNicknameSubmitted;
                activeController.OnShowNextScreen -= HandleFlowCompleted;
                activeController.OnShow -= HandleScreenShown;
                activeController.OnClose -= CleanupActiveFlow;
            }

            if (advanceCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(advanceCoroutine);
            }

            if (returnCoroutine != null && pitFireTeam.Instance != null && !returnInProgress)
            {
                pitFireTeam.Instance.StopCoroutine(returnCoroutine);
            }

            if (submitCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(submitCoroutine);
                TeammateHiringPreview.SetTransitionLoader(false);
            }

            advanceCoroutine = null;
            returnCoroutine = null;
            submitCoroutine = null;
            activeController = null;
            skipScheduled = false;
            submitInProgress = false;
            nicknamePrepared = false;
            suppressSkippedSideSelectionModelView = false;
            wiredNextButton = null;
            wiredBackButton = null;
            nextButtonAction = null;
            backButtonAction = null;
            if (returnCoroutine == null && !returnInProgress)
            {
                pendingReturnAction = null;
            }
        }

        private static TarkovApplication ResolveApplication()
        {
            if (pitFireTeam.application != null)
            {
                return pitFireTeam.application;
            }

            try
            {
                pitFireTeam.application = ClientAppUtils.GetMainApp();
            }
            catch
            {
            }

            if (pitFireTeam.application == null && Singleton<ClientApplication<EFT.IEftSession>>.Instantiated)
            {
                pitFireTeam.application = Singleton<ClientApplication<EFT.IEftSession>>.Instance as TarkovApplication;
            }

            return pitFireTeam.application;
        }

        internal static void ShowToast(string message)
        {
            EFT.Communications.NotificationManager.DisplayMessageNotification(
                message,
                ENotificationDurationType.Default,
                ENotificationIconType.Default,
                null);
        }

        public static string GetLocalizedSocialUi(string key)
        {
            return pitFireTeam.GetSocialUiText(key);
        }

        private static IEnumerator ReturnToMainScreenWithOverlay()
        {
            PreloaderUI preloaderUi = MonoBehaviourSingleton<PreloaderUI>.Instantiated
                ? MonoBehaviourSingleton<PreloaderUI>.Instance
                : null;

            preloaderUi?.SetLoaderStatus(true);
            Task<bool> returnTask = EFT.UI.Screens.EftScreenManager.Instance.TryReturnToRootScreen();
            float elapsed = 0f;

            while (!returnTask.IsCompleted || elapsed < MinimumReturnLoaderDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (returnTask.IsFaulted)
            {
                pitFireTeam.Log.LogError("[UI] Failed while returning from add teammate flow to root screen.");
                pitFireTeam.Log.LogError(returnTask.Exception);
            }

            InvokePendingReturnAction();
            yield return null;

            if (preloaderUi != null)
            {
                preloaderUi.SetLoaderStatus(false);
            }

            returnCoroutine = null;
            returnInProgress = false;
        }

        private static IEnumerator ReturnToPendingScreen()
        {
            EftAccountSideSelectionScreen.EftAccountSideSelectionScreenController controllerToClose = activeController;
            Task<bool> returnTask = controllerToClose != null
                ? controllerToClose.CloseSelf(true)
                : EFT.UI.Screens.EftScreenManager.Instance.TryReturnToRootScreen();

            while (!returnTask.IsCompleted)
            {
                yield return null;
            }

            if (returnTask.IsFaulted)
            {
                pitFireTeam.Log.LogError("[UI] Failed while returning from add teammate flow to pending screen.");
                pitFireTeam.Log.LogError(returnTask.Exception?.GetBaseException()?.Message ?? "Unknown close failure.");
            }

            InvokePendingReturnAction();

            returnCoroutine = null;
            returnInProgress = false;
        }

        private static void HideSkippedSideSelectionVisuals(EftAccountSideSelectionScreen screen)
        {
            try
            {
                SideSelectionState sideSelectionState = SideSelectionStateField?.GetValue(screen) as SideSelectionState;
                if (sideSelectionState == null)
                {
                    return;
                }

                Component descriptionCanvas = DescriptionCanvasGroupField?.GetValue(sideSelectionState) as Component;
                if (descriptionCanvas != null)
                {
                    descriptionCanvas.gameObject.SetActive(false);
                }

                if (SelectSideNodesField?.GetValue(sideSelectionState) is IEnumerable nodes)
                {
                    foreach (object node in nodes)
                    {
                        PlayerProfilePreview preview = PreviewProperty?.GetValue(node) as PlayerProfilePreview;
                        if (preview != null)
                        {
                            preview.gameObject.SetActive(false);
                        }
                    }
                }

                DefaultUIButton backButton = BackButtonField?.GetValue(screen) as DefaultUIButton;
                Button buttonComponent = backButton != null ? backButton.GetComponent<Button>() : null;
                if (buttonComponent != null)
                {
                    Navigation navigation = buttonComponent.navigation;
                    navigation.mode = Navigation.Mode.None;
                    buttonComponent.navigation = navigation;
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to hide skipped side selection visuals.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static void ConfigureHeadSelectionUi(EftAccountSideSelectionScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            try
            {
                HeadSelectionState headSelectionState = HeadSelectionStateField?.GetValue(screen) as HeadSelectionState;
                DefaultUIButton nextButton = NextButtonField?.GetValue(screen) as DefaultUIButton;
                if (headSelectionState == null || nextButton == null)
                {
                    return;
                }

                suppressSkippedSideSelectionModelView = false;
                PrepareNicknameField(headSelectionState);
                headSelectionState.StateReady = true;
                nextButton.gameObject.SetActive(true);
                nextButton.SetRawText(GetLocalizedSocialUi("AddTeammateNext"), nextButton.HeaderSize);
                bool nicknameValid = headSelectionState._nicknameField != null &&
                    headSelectionState._nicknameField.ValidationError(GetNicknameText(headSelectionState)) == ENicknameError.ValidNickname;
                nextButton.Interactable = !submitInProgress && nicknameValid;

                WireFlowButtons(screen, nextButton);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to configure head selection UI for add teammate flow.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static void PrepareNicknameField(HeadSelectionState headSelectionState)
        {
            if (nicknamePrepared || headSelectionState == null)
            {
                return;
            }

            if (headSelectionState._profileData != null)
            {
                headSelectionState._profileData.Nickname = resumeSelection?.nickname ?? string.Empty;
            }

            TMP_InputField inputField = headSelectionState._nicknameField != null
                ? NicknameInputField?.GetValue(headSelectionState._nicknameField) as TMP_InputField
                : null;

            if (inputField != null)
            {
                inputField.interactable = true;
                inputField.readOnly = false;
                inputField.SetTextWithoutNotify(resumeSelection?.nickname ?? string.Empty);
                inputField.Select();
                inputField.ActivateInputField();
            }

            headSelectionState._nicknameField?.ShowNicknameError(ENicknameError.ValidNickname, false);
            nicknamePrepared = true;
        }

        private static string GetNicknameText(HeadSelectionState headSelectionState)
        {
            if (headSelectionState?._nicknameField == null)
            {
                return string.Empty;
            }

            TMP_InputField inputField = NicknameInputField?.GetValue(headSelectionState._nicknameField) as TMP_InputField;
            return inputField?.text ?? headSelectionState._profileData?.Nickname ?? string.Empty;
        }

        private static void WireFlowButtons(EftAccountSideSelectionScreen screen, DefaultUIButton nextButton)
        {
            Button next = nextButton.GetComponent<Button>();
            if (next != null && !ReferenceEquals(wiredNextButton, next))
            {
                if (wiredNextButton != null && nextButtonAction != null)
                {
                    wiredNextButton.onClick.RemoveListener(nextButtonAction);
                }

                next.onClick.RemoveAllListeners();
                nextButtonAction = OnNextButtonPressed;
                next.onClick.AddListener(nextButtonAction);
                wiredNextButton = next;
            }

            DefaultUIButton backButton = BackButtonField?.GetValue(screen) as DefaultUIButton;
            Button back = backButton != null ? backButton.GetComponent<Button>() : null;
            if (back != null && !ReferenceEquals(wiredBackButton, back))
            {
                if (wiredBackButton != null && backButtonAction != null)
                {
                    wiredBackButton.onClick.RemoveListener(backButtonAction);
                }

                back.onClick.RemoveAllListeners();
                backButtonAction = ReturnToMainScreen;
                back.onClick.AddListener(backButtonAction);
                wiredBackButton = back;
            }

        }

        private static void OnNextButtonPressed()
        {
            TryCompleteFromCurrentScreen();
        }

        private static IEnumerator SubmitTeammateCoroutine(FriendlyTeammateCreateRequest payload)
        {
            TeammateHiringPreview.SetTransitionLoader(true);
            Task<string> requestTask = Task.Run(() => RequestHandler.PostJson(
                "/singleplayer/pitfireteam/teammate/prepare",
                SerializeRequest(payload)));

            while (!requestTask.IsCompleted)
            {
                yield return null;
            }

            submitCoroutine = null;

            if (requestTask.IsFaulted)
            {
                TeammateHiringPreview.SetTransitionLoader(false);
                submitInProgress = false;
                pitFireTeam.Log.LogError("[UI] Failed to create teammate in backend.");
                pitFireTeam.Log.LogError(requestTask.Exception);
                ShowToast(GetLocalizedSocialUi("AddTeammateCreateFailed"));
                RefreshSubmitButton();
                yield break;
            }

            try
            {
                HandleCreateTeammateResponse(payload, requestTask.Result);
            }
            catch (Exception ex)
            {
                TeammateHiringPreview.SetTransitionLoader(false);
                submitInProgress = false;
                pitFireTeam.Log.LogError("[UI] Failed to process teammate create response.");
                pitFireTeam.Log.LogError(ex);
                ShowToast(GetLocalizedSocialUi("AddTeammateCreateFailed"));
                RefreshSubmitButton();
            }
        }

        private static void HandleCreateTeammateResponse(FriendlyTeammateCreateRequest payload, string responseJson)
        {
            FriendlyTeammateBodyResponse<TeammateHiringPreview.Quote> response = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<TeammateHiringPreview.Quote>>(responseJson);
            if (response == null || response.err != 0)
            {
                TeammateHiringPreview.SetTransitionLoader(false);
                string backendError = response?.errmsg;
                ShowToast(string.IsNullOrEmpty(backendError) ? GetLocalizedSocialUi("AddTeammateCreateFailed") : GetLocalizedSocialUi(backendError));
                submitInProgress = false;
                RefreshSubmitButton();
                return;
            }
            Action onReturn = pendingReturnAction;
            TeammateHiringPreview.OpenAsync(response.data, payload, onReturn).HandleExceptions();
        }

        internal static void PreviewOpenFailed()
        {
            submitInProgress = false;
            RefreshSubmitButton();
        }

        private static void InvokePendingReturnAction()
        {
            Action callback = pendingReturnAction;
            pendingReturnAction = null;
            if (callback == null)
            {
                return;
            }

            try
            {
                // Returning from the add-teammate flow should reopen squad side-selection
                // on the first back press. Clear squad-mode guard before invoking callback.
                SquadSideSelectionFlow.Deactivate("return-from-add-teammate");
                callback();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to invoke add teammate return action.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private static string SerializeRequest(object payload)
        {
            JsonConverter[] converters = GetDefaultJsonConverters();
            if (converters != null)
            {
                return payload.ToJson(converters);
            }

            return JsonConvert.SerializeObject(payload);
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            if (defaultJsonConverters != null)
            {
                return defaultJsonConverters;
            }

            try
            {
                Type converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public) != null);

                defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to resolve default JSON converters for add teammate request.");
                pitFireTeam.Log.LogError(ex);
            }

            return defaultJsonConverters;
        }

        internal sealed class FriendlyTeammateCreateRequest
        {
            public string nickname;
            public string voice;
            public string head;
        }

    }
}
