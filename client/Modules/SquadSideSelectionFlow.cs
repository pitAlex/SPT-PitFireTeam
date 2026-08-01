using Comfort.Common;
using EFT;
using EFT.UI.Matchmaker;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class SquadSideSelectionFlow
    {
        private static readonly FieldInfo AppMenuControllerField =
            AccessTools.Field(typeof(TarkovApplication), "mainMenuControllerClass");

        private static readonly MethodInfo OpenSideSelectionMethod =
            AccessTools.Method(typeof(MainMenuControllerClass), "method_44");

        private const string AlphaLabelPath = "Preloader UI/BottomPanel/Content/UpperPart/AlphaLabel";

        public static bool SquadModeActive { get; private set; }
        public static bool SuppressPlayerModelViewShow { get; private set; }
        public static bool IsOpeningSquadModeScreen { get; private set; }
        public static IReadOnlyCollection<string> OpeningGroupAccountIds => openingGroupAccountIds;

        private static GameObject alphaLabelObject;
        private static bool? alphaLabelWasActive;
        private static HashSet<string> openingGroupAccountIds = new HashSet<string>(StringComparer.Ordinal);

        public static void Open()
        {
            if (SquadModeActive)
            {
                if (HasActiveSquadSideSelectionScreen())
                {
                    return;
                }

                Deactivate("stale-squad-screen-open");
            }

            if (AppMenuControllerField == null || OpenSideSelectionMethod == null)
            {
                pitFireTeam.Log.LogWarning("[SquadFlow] MainMenuControllerClass reflection not available — cannot open side selection screen.");
                return;
            }

            TarkovApplication app = ResolveApp();
            if (app == null)
            {
                pitFireTeam.Log.LogWarning("[SquadFlow] TarkovApplication not available.");
                return;
            }

            object menuController = AppMenuControllerField.GetValue(app);
            if (menuController == null)
            {
                pitFireTeam.Log.LogWarning("[SquadFlow] MainMenuControllerClass instance is null.");
                return;
            }

            SquadModeActive = true;
            IsOpeningSquadModeScreen = true;
            CaptureOpeningGroupSnapshot(app);
            HideSquadScreenAlphaLabel();

            try
            {
                OpenSideSelectionMethod.Invoke(menuController, null);
            }
            catch (Exception ex)
            {
                ShowSquadScreenAlphaLabel();
                SquadModeActive = false;
                openingGroupAccountIds.Clear();
                pitFireTeam.Log.LogError("[SquadFlow] Failed to open MatchMakerSideSelectionScreen.");
                pitFireTeam.Log.LogError(ex);
            }
            finally
            {
                IsOpeningSquadModeScreen = false;
            }
        }

        public static void OnScreenClosed()
        {
            SuppressPlayerModelViewShow = false;
            ShowSquadScreenAlphaLabel();
        }

        public static void Deactivate(string reason = null)
        {
            SuppressPlayerModelViewShow = false;
            SquadModeActive = false;
            IsOpeningSquadModeScreen = false;
            ShowSquadScreenAlphaLabel();
            openingGroupAccountIds.Clear();

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Logger.LogInfo($"[SquadFlow] Squad side-selection mode disabled: {reason}");
            }
        }

        public static void BeginPlayerModelViewSuppression()
        {
            SuppressPlayerModelViewShow = true;
        }

        public static void EndPlayerModelViewSuppression()
        {
            SuppressPlayerModelViewShow = false;
        }

        private static void HideSquadScreenAlphaLabel()
        {
            GameObject target = ResolveAlphaLabelObject();
            if (target == null)
            {
                return;
            }

            alphaLabelWasActive ??= target.activeSelf;
            target.SetActive(false);
        }

        private static void ShowSquadScreenAlphaLabel()
        {
            if (alphaLabelObject == null || !alphaLabelWasActive.HasValue)
            {
                return;
            }

            alphaLabelObject.SetActive(alphaLabelWasActive.Value);
            alphaLabelWasActive = null;
        }

        private static GameObject ResolveAlphaLabelObject()
        {
            if (alphaLabelObject != null)
            {
                return alphaLabelObject;
            }

            if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)
            {
                return null;
            }

            Transform transform = MonoBehaviourSingleton<PreloaderUI>.Instance.transform.Find(AlphaLabelPath);
            if (transform == null)
            {
                return null;
            }

            alphaLabelObject = transform.gameObject;
            return alphaLabelObject;
        }

        private static TarkovApplication ResolveApp()
        {
            if (pitFireTeam.application != null)
            {
                return pitFireTeam.application;
            }

            try
            {
                pitFireTeam.application = ClientAppUtils.GetMainApp();
            }
            catch { }

            return pitFireTeam.application;
        }

        private static bool HasActiveSquadSideSelectionScreen()
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<MatchMakerSideSelectionScreen>()
                    .Any(screen => screen != null && screen.gameObject != null && screen.gameObject.activeInHierarchy);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning("[SquadFlow] Failed to verify active squad side-selection screen.");
                pitFireTeam.Log.LogError(ex);
                return false;
            }
        }

        private static void CaptureOpeningGroupSnapshot(TarkovApplication app)
        {
            openingGroupAccountIds.Clear();

            MatchmakerPlayerControllerClass controller = app?.MatchmakerPlayerControllerClass;
            if (controller?.GroupPlayers == null)
            {
                return;
            }

            foreach (string accountId in controller.GroupPlayers
                         .Select(player => player?.AccountId)
                         .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
                         .Select(accountId => accountId!))
            {
                openingGroupAccountIds.Add(accountId);
            }
        }

        public static bool IsAccountInOpeningGroupSnapshot(string accountId)
        {
            return !string.IsNullOrWhiteSpace(accountId) && openingGroupAccountIds.Contains(accountId);
        }
    }
}
