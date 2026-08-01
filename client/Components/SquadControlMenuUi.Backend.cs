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
        private void InviteTeammateToGroup(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            if (!entry.HasProperRaidKit)
            {
                string message = $"Cannot add {entry.Nickname} to the raid group without a proper kit.";
                AddTeammateCreationFlow.ShowToast(message);
                Modules.Logger.LogInfo($"[UI] Blocked group invite for teammate '{entry.AccountId}': missing primary or pistol weapon.");
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
            {
                pitFireTeam.Log.LogWarning($"[UI] Could not invite teammate '{entry.AccountId}': matchmaker controller unavailable.");
                return;
            }

            if (!groupRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            if (controller.IsInGroup(entry.AccountId))
            {
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                return;
            }

            TeammateAutoJoinRuntime.ClearSuppression(entry.AccountId);
            SetPortraitLoading(entry.AccountId, true);

            try
            {
                controller.SendInvite(entry.AccountId, true, null);
            }
            catch (Exception ex)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                pitFireTeam.Log.LogError($"[UI] Failed to send group invite for teammate '{entry.AccountId}'.");
                pitFireTeam.Log.LogError(ex);
                return;
            }

            if (pitFireTeam.Instance == null)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                return;
            }

            pitFireTeam.Instance.StartCoroutine(WaitForInviteResolutionCoroutine(entry.AccountId, entry.Nickname));
        }

        private void RemoveTeammateFromGroup(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
            {
                pitFireTeam.Log.LogWarning($"[UI] Could not remove teammate '{entry.AccountId}' from the group: matchmaker controller unavailable.");
                return;
            }

            if (!groupRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            if (!controller.IsInGroup(entry.AccountId))
            {
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                return;
            }

            SetPortraitLoading(entry.AccountId, true);

            bool confirmationShown;
            try
            {
                confirmationShown = TryShowStockRemovePlayerConfirmation(controller, entry.AccountId, entry.Nickname);
            }
            catch (Exception ex)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                pitFireTeam.Log.LogError($"[UI] Failed to show the remove-group confirmation for teammate '{entry.AccountId}'.");
                pitFireTeam.Log.LogError(ex);
                return;
            }

            if (!confirmationShown)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                pitFireTeam.Log.LogWarning($"[UI] Stock remove-player interaction was not available for teammate '{entry.AccountId}'.");
            }
        }

        private IEnumerator WaitForInviteResolutionCoroutine(string accountId, string nickname)
        {
            const float timeoutSeconds = 15f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            bool inviteObserved = false;
            bool joinedGroup = false;
            bool inviteClearedWithoutJoin = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) && controller != null)
                {
                    if (controller.IsInGroup(accountId))
                    {
                        joinedGroup = true;
                        break;
                    }

                    bool invitePending = controller.IsInvitedByMe(accountId);
                    if (invitePending)
                    {
                        inviteObserved = true;
                    }
                    else if (inviteObserved)
                    {
                        inviteClearedWithoutJoin = true;
                        break;
                    }
                }

                yield return null;
            }

            SetPortraitLoading(accountId, false);
            groupRequestsInFlight.Remove(accountId);
            SyncGroupBadgesFromKnownState();

            if (joinedGroup)
            {
                string successTemplate = GetSocialUiText("SquadControlInviteAcceptedToast");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            // A cleared invite that did not join is EFT's normal decline path. Let the stock
            // decline notification and any server-provided reason carry the user-facing message.
            if (inviteClearedWithoutJoin)
            {
                yield break;
            }

            string failureTemplate = GetSocialUiText("SquadControlInvitePendingFailedToast");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private IEnumerator WaitForGroupRemovalCoroutine(string accountId, string nickname)
        {
            const float timeoutSeconds = 10f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            bool removedFromGroup = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) && controller != null)
                {
                    if (!controller.IsInGroup(accountId))
                    {
                        removedFromGroup = true;
                        break;
                    }
                }

                yield return null;
            }

            SetPortraitLoading(accountId, false);
            groupRequestsInFlight.Remove(accountId);
            SyncGroupBadgesFromKnownState();

            if (removedFromGroup)
            {
                string successTemplate = GetSocialUiText("SquadControlRemovedFromGroupToast");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            ShowGroupRemovalFailure(nickname);
        }

        private bool TryShowStockRemovePlayerConfirmation(
            MatchmakerPlayerControllerClass controller,
            string accountId,
            string nickname)
        {
            if (controller == null || string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            GroupPlayerDataClass groupPlayer = controller.GroupPlayers?
                .FirstOrDefault(player => player?.AccountId == accountId);
            if (groupPlayer == null)
            {
                pitFireTeam.Log.LogWarning($"[UI] Could not resolve live group player data for teammate '{accountId}'.");
                return false;
            }

            ContextInteractionsClass contextInteractions = controller.GetContextInteractions(groupPlayer, true, true);
            if (contextInteractions == null)
            {
                pitFireTeam.Log.LogWarning($"[UI] Could not build stock context interactions for teammate '{accountId}'.");
                return false;
            }

            if (!contextInteractions.IsInteractionAvailable(ERaidPlayerButton.RemovePlayer).Succeed)
            {
                return false;
            }

            ItemUiContext itemUiContext = ItemUiContext.Instance;
            if (itemUiContext == null)
            {
                pitFireTeam.Log.LogWarning($"[UI] Could not show the remove-player confirmation for teammate '{accountId}': item UI context unavailable.");
                return false;
            }

            string confirmationTemplate = GetSocialUiText("SquadControlRemoveFromGroupConfirm");
            itemUiContext.ShowMessageWindow(
                string.Format(confirmationTemplate, nickname ?? string.Empty),
                () => BeginConfirmedGroupRemoval(contextInteractions, accountId, nickname),
                () => CancelGroupRemoval(accountId),
                null,
                0f,
                false,
                TextAlignmentOptions.Center);
            return true;
        }

        private void BeginConfirmedGroupRemoval(
            ContextInteractionsClass contextInteractions,
            string accountId,
            string nickname)
        {
            if (contextInteractions?.GroupPlayerDataClass == null ||
                !string.Equals(contextInteractions.GroupPlayerDataClass.AccountId, accountId, StringComparison.Ordinal))
            {
                CompleteGroupRemovalRequest(accountId);
                ShowGroupRemovalFailure(nickname);
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) ||
                controller == null ||
                !controller.IsInGroup(accountId))
            {
                CompleteGroupRemovalRequest(accountId);
                ShowGroupRemovalFailure(nickname);
                return;
            }

            try
            {
                contextInteractions.method_21();
            }
            catch (Exception ex)
            {
                CompleteGroupRemovalRequest(accountId);
                pitFireTeam.Log.LogError($"[UI] Failed to remove teammate '{accountId}' from the group.");
                pitFireTeam.Log.LogError(ex);
                ShowGroupRemovalFailure(nickname);
                return;
            }

            if (pitFireTeam.Instance == null)
            {
                CompleteGroupRemovalRequest(accountId);
                ShowGroupRemovalFailure(nickname);
                return;
            }

            pitFireTeam.Instance.StartCoroutine(WaitForGroupRemovalCoroutine(accountId, nickname));
        }

        private void CancelGroupRemoval(string accountId)
        {
            CompleteGroupRemovalRequest(accountId);
        }

        private void CompleteGroupRemovalRequest(string accountId)
        {
            SetPortraitLoading(accountId, false);
            groupRequestsInFlight.Remove(accountId);
            SyncGroupBadgesFromKnownState();
        }

        private static void ShowGroupRemovalFailure(string nickname)
        {
            string failureTemplate = GetSocialUiText("SquadControlRemoveFromGroupFailedToast");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private void ToggleTeammateAutoJoin(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            bool nextState = !entry.AutoJoinEnabled;
            if (!autoJoinRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            SetPortraitLoading(entry.AccountId, true);
            pitFireTeam.Instance.StartCoroutine(ToggleTeammateAutoJoinCoroutine(entry, nextState));
        }

        private IEnumerator ToggleTeammateAutoJoinCoroutine(SquadRosterEntry entry, bool nextState)
        {
            string accountId = entry?.AccountId ?? string.Empty;
            string nickname = entry?.Nickname ?? string.Empty;
            Task<string> requestTask = null;
            string requestBody = JsonConvert.SerializeObject(new
            {
                aid = accountId,
                enabled = nextState
            });

            try
            {
                requestTask = Task.Run(() => RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/autojoin", requestBody));
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Failed to start auto join toggle request for teammate '{accountId}'.");
                pitFireTeam.Log.LogError(ex);
            }

            if (requestTask != null)
            {
                yield return new WaitUntil(() => requestTask.IsCompleted);
            }

            bool success = false;
            string backendError = null;

            if (requestTask == null)
            {
                backendError = "Request did not start.";
            }
            else if (requestTask.IsFaulted)
            {
                backendError = requestTask.Exception?.GetBaseException().Message;
                pitFireTeam.Log.LogError($"[UI] Failed to toggle auto join for teammate '{accountId}'.");
                pitFireTeam.Log.LogError(requestTask.Exception);
            }
            else
            {
                success = TryValidateBackendSuccess(requestTask.Result, out backendError);
                if (!success && !string.IsNullOrWhiteSpace(backendError))
                {
                    pitFireTeam.Log.LogWarning($"[UI] Auto join toggle rejected for teammate '{accountId}': {backendError}");
                }
            }

            SetPortraitLoading(accountId, false);
            autoJoinRequestsInFlight.Remove(accountId);

            if (success)
            {
                if (nextState)
                {
                    TeammateAutoJoinRuntime.ClearSuppression(accountId);
                }
                else
                {
                    TeammateAutoJoinRuntime.MarkSuppressed(accountId);
                    if (!IsAccountInCurrentGroup(accountId))
                    {
                        MainMenuControllerPatch.GroupPlayers.RemoveFirst(player => player?.AccountId == accountId);
                    }
                }

                entry.AutoJoinEnabled = nextState;
                UpdateAutoJoinBadge(accountId, nextState);
                string successTemplate = GetSocialUiText(nextState ? "SquadControlAutoJoinEnabledToast" : "SquadControlAutoJoinDisabledToast");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            string failureTemplate = GetSocialUiText(nextState ? "SquadControlAutoJoinEnableFailedToast" : "SquadControlAutoJoinDisableFailedToast");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private static bool TryValidateBackendSuccess(string responseJson, out string backendError)
        {
            backendError = null;

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return true;
            }

            try
            {
                BackendBodyResponse response = JsonConvert.DeserializeObject<BackendBodyResponse>(responseJson);
                if (response == null)
                {
                    backendError = "Backend returned an empty response body.";
                    return false;
                }

                if (response.err != 0)
                {
                    backendError = string.IsNullOrWhiteSpace(response.errmsg)
                        ? $"Backend returned err={response.err}."
                        : response.errmsg;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                backendError = $"Failed to parse backend response: {ex.Message}";
                return false;
            }
        }

        private bool TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller)
        {
            controller = null;

            try
            {
                if (pitFireTeam.application == null)
                {
                    pitFireTeam.application = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                }

                if (pitFireTeam.application == null)
                {
                    return false;
                }

                controller = pitFireTeam.application.MatchmakerPlayerControllerClass;
                return controller != null;
            }
            catch
            {
                controller = null;
                return false;
            }
        }

        private void EnsureGroupBadgeEventLogging()
        {
            try
            {
                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
                {
                    DetachGroupBadgeEventLogging();
                    return;
                }

                if (ReferenceEquals(subscribedGroupBadgeLogController, controller))
                {
                    return;
                }

                DetachGroupBadgeEventLogging();
                subscribedGroupBadgeLogController = controller;

                if (controller.GroupPlayers != null)
                {
                    unsubscribeGroupBadgeLog = controller.GroupPlayers.ItemsChanged.Subscribe(LogGroupBadgeItemsChanged);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI][GroupBadge] Failed to subscribe to GroupPlayers.ItemsChanged.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void DetachGroupBadgeEventLogging()
        {
            try
            {
                if (unsubscribeGroupBadgeLog != null)
                {
                    unsubscribeGroupBadgeLog();
                    unsubscribeGroupBadgeLog = null;
                }

                subscribedGroupBadgeLogController = null;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI][GroupBadge] Failed to detach GroupPlayers.ItemsChanged subscription.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void LogGroupBadgeItemsChanged()
        {
            try
            {
                SyncGroupBadgesFromKnownState();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI][GroupBadge] Failed while logging GroupPlayers.ItemsChanged.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void SyncGroupBadgesFromKnownState()
        {
            if (!SyncGroupBadgesFromCurrentGroup())
            {
                SyncGroupBadgesFromOpeningSnapshot();
            }
        }

        private bool SyncGroupBadgesFromCurrentGroup()
        {
            try
            {
                if (rosterGridRoot == null || !rosterGridRoot.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller?.GroupPlayers == null)
                {
                    return false;
                }

                HashSet<string> groupedIds = controller.GroupPlayers
                    .Select(player => player?.AccountId)
                    .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
                    .Select(accountId => accountId!)
                    .ToHashSet(StringComparer.Ordinal);

                ApplyGroupBadgeVisibility(groupedIds);
                return true;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI][GroupBadge] Failed to sync badge visibility from current group.");
                pitFireTeam.Log.LogError(ex);
                return false;
            }
        }

        private void SyncGroupBadgesFromOpeningSnapshot()
        {
            try
            {
                if (rosterGridRoot == null || !rosterGridRoot.gameObject.activeInHierarchy)
                {
                    return;
                }

                HashSet<string> groupedIds = Modules.SquadSideSelectionFlow.OpeningGroupAccountIds
                    .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
                    .ToHashSet(StringComparer.Ordinal);

                ApplyGroupBadgeVisibility(groupedIds);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI][GroupBadge] Failed to sync badge visibility from opening snapshot.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void ApplyGroupBadgeVisibility(HashSet<string> groupedIds)
        {
            if (rosterGridRoot == null)
            {
                return;
            }

            const string prefix = "pitFireTeam_RosterTile_";
            for (int i = 0; i < rosterGridRoot.childCount; i++)
            {
                Transform tile = rosterGridRoot.GetChild(i);
                if (tile == null || !tile.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string accountId = tile.name.Substring(prefix.Length);
                UpdateGroupBadge(accountId, groupedIds != null && groupedIds.Contains(accountId));
            }
        }

        private bool IsAccountInCurrentGroup(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            try
            {
                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
                {
                    return false;
                }

                return controller.IsInGroup(accountId)
                    || (controller.GroupPlayers != null && controller.GroupPlayers.Any(player => player?.AccountId == accountId));
            }
            catch
            {
                return false;
            }
        }

    }
}
