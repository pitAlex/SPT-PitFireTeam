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
        private void RebuildRosterTiles()
        {
            if (rosterGridRoot == null)
            {
                return;
            }

            CloseRemoveConfirmOverlay();
            ClosePortraitContextMenu();
            CancelPortraitQueue();
            portraitLoadingIndicators.Clear();
            autoJoinRequestsInFlight.Clear();
            groupRequestsInFlight.Clear();
            rosterBuildVersion++;

            for (int i = rosterGridRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(rosterGridRoot.GetChild(i).gameObject);
            }

            List<SquadRosterEntry> entries = BuildRosterEntries().ToList();
            UpdateRosterGridLayout(entries.Count);

            foreach (SquadRosterEntry entry in entries)
            {
                CreateRosterTile(rosterGridRoot, entry, rosterBuildVersion);
            }

            UpdateEmptyRosterState(entries.Count == 0);

            if (rosterGridRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterGridRoot);
            }

            if (rosterContentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterContentRoot);
            }

            if (rosterPanel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterPanel.GetComponent<RectTransform>());
            }

            if (rosterScrollRect != null)
            {
                rosterScrollRect.verticalNormalizedPosition = 1f;
            }

            SyncGroupBadgesFromKnownState();
        }

        private void RefreshRosterTiles(ICollection<string> accountIds)
        {
            if (rosterGridRoot == null || accountIds == null || accountIds.Count == 0)
            {
                return;
            }

            Dictionary<string, SquadRosterEntry> entriesByAccountId = BuildRosterEntries()
                .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToDictionary(entry => entry.AccountId, StringComparer.Ordinal);

            bool changed = false;
            foreach (string accountId in accountIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal))
            {
                if (!entriesByAccountId.TryGetValue(accountId, out SquadRosterEntry entry))
                {
                    continue;
                }

                if (RefreshRosterTile(entry))
                {
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rosterGridRoot);

            if (rosterContentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterContentRoot);
            }

            if (rosterPanel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterPanel.GetComponent<RectTransform>());
            }

            SyncGroupBadgesFromKnownState();
        }

        private bool RefreshRosterTile(SquadRosterEntry entry)
        {
            if (rosterGridRoot == null || entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return false;
            }

            Transform existingTile = rosterGridRoot.Find($"pitFireTeam_RosterTile_{entry.AccountId}");
            if (existingTile == null)
            {
                return false;
            }

            int siblingIndex = existingTile.GetSiblingIndex();
            Destroy(existingTile.gameObject);

            // Capture the new tile directly — Destroy() is deferred, so Find() by name would
            // match the still-alive old tile first, leaving the new tile at the end every time.
            GameObject createdTile = CreateRosterTile(rosterGridRoot, entry, ++rosterBuildVersion);
            if (createdTile == null)
            {
                return false;
            }

            createdTile.transform.SetSiblingIndex(siblingIndex);
            return true;
        }

        private IEnumerable<SquadRosterEntry> BuildRosterEntries()
        {
            List<SquadRosterEntry> entries = new List<SquadRosterEntry>();

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    JToken root = JToken.Parse(response);
                    JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                    if (dataToken is JArray teammates)
                    {
                        foreach (JToken teammate in teammates)
                        {
                            string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString() ?? string.Empty;
                            string socialMemberId = teammate?["Id"]?.ToString() ?? teammate?["id"]?.ToString() ?? string.Empty;
                            string nickname = teammate?["Info"]?["Nickname"]?.ToString() ?? teammate?["info"]?["nickname"]?.ToString() ?? string.Empty;
                            int level = ParseLevel(teammate?["Info"]?["Level"]?.ToString() ?? teammate?["info"]?["level"]?.ToString());
                            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(nickname))
                            {
                                continue;
                            }

                            entries.Add(new SquadRosterEntry
                            {
                                AccountId = accountId,
                                SocialMemberId = socialMemberId,
                                Nickname = nickname,
                                Level = level,
                                AutoJoinEnabled = ParseBool(teammate?["AutoJoinEnabled"]?.ToString() ?? teammate?["autoJoinEnabled"]?.ToString()),
                                HasProperRaidKit = ParseBool(teammate?["HasProperRaidKit"]?.ToString() ?? teammate?["hasProperRaidKit"]?.ToString(), defaultValue: true)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to build Squad Control roster.");
                pitFireTeam.Log.LogError(ex);
            }

            return entries
                .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
                .Select(group => group.First());
        }

        private static int ParseLevel(string value)
        {
            return int.TryParse(value, out int level) ? Mathf.Max(1, level) : 1;
        }

        private static bool ParseBool(string value, bool defaultValue = false)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
        }

        private void CreateAddTeammateButton(RectTransform rosterRect, DefaultUIButton buttonTemplate = null)
        {
            buttonTemplate ??= playerButton;
            if (rosterRect == null || buttonTemplate == null || playerButton == null)
            {
                return;
            }

            if (addTeammateButton != null)
            {
                Destroy(addTeammateButton.gameObject);
                addTeammateButton = null;
            }

            addTeammateButton = Instantiate(buttonTemplate, rosterRect, false);
            addTeammateButton.name = "pitFireTeam_SquadControlAddTeammateButton";
            addTeammateButton.SetRawText(GetSocialUiText("AddTeammate"), playerButton.HeaderSize);
            addTeammateButton.SetIcon(null);
            addTeammateButton.Interactable = true;
            addTeammateButton.OnClick.RemoveAllListeners();
            addTeammateButton.OnClick.AddListener(OnAddTeammateClicked);

            RectTransform buttonRect = addTeammateButton.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.sizeDelta = new Vector2(320f, playerButton.transform is RectTransform playerRect ? playerRect.sizeDelta.y : 54f);
                buttonRect.anchoredPosition = Vector2.zero;
            }
        }

        private void OnAddTeammateClicked()
        {
            AddTeammateCreationFlow.Start(SquadSideSelectionFlow.Open);
        }

        private void CreateEmptyRosterLabel(RectTransform rosterRect)
        {
            if (rosterRect == null)
            {
                return;
            }

            if (emptyRosterLabel != null)
            {
                Destroy(emptyRosterLabel.gameObject);
                emptyRosterLabel = null;
            }

            GameObject textObject = CreateText(
                "pitFireTeam_EmptyRosterLabel",
                GetSocialUiText("SquadControlEmptyRoster"),
                24f,
                TextAlignmentOptions.Center);
            textObject.transform.SetParent(rosterRect, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(760f, 90f);
            textRect.anchoredPosition = Vector2.zero;

            emptyRosterLabel = textObject.GetComponent<TextMeshProUGUI>();
            emptyRosterLabel.enableWordWrapping = true;
            emptyRosterLabel.overflowMode = TextOverflowModes.Ellipsis;
            emptyRosterLabel.gameObject.SetActive(false);
        }

        private void UpdateEmptyRosterState(bool isEmpty)
        {
            if (emptyRosterLabel == null)
            {
                return;
            }

            emptyRosterLabel.text = GetSocialUiText("SquadControlEmptyRoster");
            emptyRosterLabel.gameObject.SetActive(isEmpty);

            UpdateRosterPanelLayout(isEmpty);
        }

        private GameObject CreateRosterTile(RectTransform parent, SquadRosterEntry entry, int buildVersion)
        {
            GameObject tileObject = new GameObject(
                $"pitFireTeam_RosterTile_{entry.AccountId}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                typeof(Button));
            tileObject.transform.SetParent(parent, false);

            RectTransform tileRect = tileObject.GetComponent<RectTransform>();
            tileRect.sizeDelta = new Vector2(RosterTileWidth, RosterTileHeight);

            LayoutElement layoutElement = tileObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = RosterTileWidth;
            layoutElement.preferredHeight = RosterTileHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            Image tileBackground = tileObject.GetComponent<Image>();
            Color normalColor = new Color(0.045f, 0.045f, 0.045f, 0.97f);
            Color hoverColor = new Color(0.6235f, 0.6157f, 0.5647f, 1f);
            Color pressedColor = new Color(0.16f, 0.16f, 0.16f, 0.99f);
            tileBackground.color = normalColor;

            GameObject tileOverlayObject = new GameObject("BackgroundOverlay", typeof(RectTransform), typeof(Image));
            tileOverlayObject.transform.SetParent(tileRect, false);
            RectTransform tileOverlayRect = tileOverlayObject.GetComponent<RectTransform>();
            tileOverlayRect.anchorMin = new Vector2(0f, 1f);
            tileOverlayRect.anchorMax = new Vector2(0f, 1f);
            tileOverlayRect.pivot = new Vector2(0f, 1f);
            tileOverlayRect.sizeDelta = new Vector2(58f, 58f);
            tileOverlayRect.anchoredPosition = Vector2.zero;

            Image tileOverlay = tileOverlayObject.GetComponent<Image>();
            Sprite diagonalSprite = LoadRosterTileDiagonalSprite();
            if (diagonalSprite != null)
            {
                tileOverlay.sprite = diagonalSprite;
                tileOverlay.type = Image.Type.Simple;
                tileOverlay.color = Color.white;
            }
            else
            {
                tileOverlay.color = new Color(1f, 1f, 1f, 0.06f);
            }

            tileOverlay.raycastTarget = false;

            Button button = tileObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = tileBackground;
            button.onClick.AddListener(() => OpenProfile(entry.AccountId));

            TextMeshProUGUI nameLabel = CreateRosterNameLabel(tileRect, entry.Nickname);

            RosterTileHoverController hoverController = tileObject.AddComponent<RosterTileHoverController>();
            hoverController.Configure(tileBackground, nameLabel, normalColor, hoverColor, pressedColor);

            PortraitContextClickController tileContextController = tileObject.AddComponent<PortraitContextClickController>();
            tileContextController.OnRightClick = eventData => ShowPortraitContextMenu(entry, eventData);

            RectTransform portraitHost = CreatePortraitHost(tileRect, entry.Level, out PlayerIconImage iconImage);
            RegisterPortraitLoadingIndicator(entry.AccountId, iconImage?._progress);
            AttachPortraitProfileTrigger(portraitHost, entry);
            CreateRemoveButton(tileRect, entry);
            CreateAutoJoinBadge(tileRect, entry);
            CreateGroupBadge(tileRect, entry.AccountId);
            EnqueuePortrait(entry.AccountId, iconImage, portraitHost, buildVersion);
            return tileObject;
        }

        private void CreateRemoveButton(RectTransform tileRect, SquadRosterEntry entry)
        {
            GameObject buttonObject = new GameObject("pitFireTeam_RemoveButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(tileRect, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(24f, 22f);
            rect.localPosition = new Vector3(95f, 107f, 0f);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.88f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(() => ShowRemoveConfirmOverlay(entry));

            TooltipHoverController tooltipHover = buttonObject.AddComponent<TooltipHoverController>();
            tooltipHover.Configure(GetSocialUiText("SquadControlDeleteTooltip"));

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.localPosition = new Vector3(-12f, -10f, 0f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = GetSocialUiText("RenameClose");
            label.fontSize = 16f;
            label.fontWeight = FontWeight.Medium;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            label.raycastTarget = false;
        }

        private void CreateAutoJoinBadge(RectTransform tileRect, SquadRosterEntry entry)
        {
            if (tileRect == null || entry == null)
            {
                return;
            }

            Sprite badgeSprite = LoadAutoJoinBadgeSprite();
            if (badgeSprite == null)
            {
                return;
            }

            GameObject badgeObject = new GameObject("pitFireTeam_AutoJoinBadge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(tileRect, false);

            RectTransform rect = badgeObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(22f, 22f);
            rect.localPosition = new Vector3(95f, -107f, 0f);

            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = badgeSprite;
            badgeImage.type = Image.Type.Simple;
            badgeImage.preserveAspect = true;
            badgeImage.color = new Color(0.92f, 0.92f, 0.9f, 0.95f);
            badgeImage.raycastTarget = true;

            TooltipHoverController tooltipHover = badgeObject.AddComponent<TooltipHoverController>();
            tooltipHover.Configure(GetSocialUiText("SquadControlAutoJoinTooltip"));

            badgeObject.SetActive(entry.AutoJoinEnabled);
        }

        private void UpdateAutoJoinBadge(string accountId, bool enabled)
        {
            if (rosterGridRoot == null || string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }

            Transform tile = rosterGridRoot.Find($"pitFireTeam_RosterTile_{accountId}");
            if (tile == null)
            {
                return;
            }

            Transform badge = tile.Find("pitFireTeam_AutoJoinBadge");
            if (badge != null)
            {
                badge.gameObject.SetActive(enabled);
            }
        }

        private void CreateGroupBadge(RectTransform tileRect, string accountId)
        {
            if (tileRect == null)
            {
                return;
            }

            Sprite badgeSprite = LoadGroupBadgeSprite();
            if (badgeSprite == null)
            {
                return;
            }

            GameObject badgeObject = new GameObject("pitFireTeam_GroupBadge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(tileRect, false);

            RectTransform rect = badgeObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(20f, 20f);
            rect.anchoredPosition = new Vector2(1f, 3f);

            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = badgeSprite;
            badgeImage.type = Image.Type.Simple;
            badgeImage.preserveAspect = true;
            badgeImage.color = new Color(0.92f, 0.92f, 0.9f, 0.95f);
            badgeImage.raycastTarget = true;

            TooltipHoverController tooltipHover = badgeObject.AddComponent<TooltipHoverController>();
            tooltipHover.Configure(GetSocialUiText("SquadControlInGroupTooltip"));

            bool startsActive = IsAccountInCurrentGroup(accountId)
                || Modules.SquadSideSelectionFlow.IsAccountInOpeningGroupSnapshot(accountId);
            badgeObject.SetActive(startsActive);
        }

        private void UpdateGroupBadge(string accountId, bool enabled)
        {
            if (rosterGridRoot == null || string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }

            Transform tile = rosterGridRoot.Find($"pitFireTeam_RosterTile_{accountId}");
            if (tile == null)
            {
                return;
            }

            Transform badge = tile.Find("pitFireTeam_GroupBadge");
            if (badge != null)
            {
                badge.gameObject.SetActive(enabled);
            }
        }

        private void ShowRemoveConfirmOverlay(SquadRosterEntry entry)
        {
            CloseRemoveConfirmOverlay();

            Transform? overlayParent = rosterPanel?.transform.parent;
            if (overlayParent == null || entry == null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_RemoveOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.12f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_RemovePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 188f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_RemoveHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = CreateText(
                "pitFireTeam_RemoveTitle",
                GetSocialUiText("RemoveTeammateTitle").ToUpperInvariant(),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateWindowCloseButton(header.transform, "pitFireTeam_RemoveCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(CloseRemoveConfirmOverlay);

            GameObject bodyObject = CreateText(
                "pitFireTeam_RemoveBody",
                string.Format(
                    GetSocialUiText("RemoveTeammatePrompt"),
                    entry.Nickname),
                24f,
                TextAlignmentOptions.Center);
            RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.SetParent(panel.transform, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(28f, 68f);
            bodyRect.offsetMax = new Vector2(-28f, -42f);

            TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

            DefaultUIButton removeButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            removeButton.SetRawText(GetSocialUiText("RemoveTeammateConfirm"), 22);
            removeButton.OnClick.RemoveAllListeners();
            removeButton.OnClick.AddListener(() => RemoveTeammate(entry));

            removeConfirmOverlay = overlayRoot;
        }

        private void RemoveTeammate(SquadRosterEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                IChatInteractions chatInteractions = ItemUiContext.Instance?.Session;
                SocialNetworkClass socialNetwork = chatInteractions?.SocialNetwork;
                if (socialNetwork?.FriendsList == null)
                {
                    pitFireTeam.Log.LogError("[UI] Failed to delete teammate: social network is unavailable.");
                    return;
                }

                UpdatableChatMember member = socialNetwork.FriendsList
                    .FirstOrDefault(candidate =>
                        candidate != null &&
                        (!string.IsNullOrWhiteSpace(candidate.AccountId)
                            ? string.Equals(candidate.AccountId, entry.AccountId, StringComparison.Ordinal)
                            : string.Equals(candidate.Id, entry.SocialMemberId, StringComparison.Ordinal)));

                if (member == null)
                {
                    pitFireTeam.Log.LogError($"[UI] Failed to delete teammate '{entry.AccountId}': social member was not found.");
                    return;
                }

                socialNetwork.RemoveFromFriendsList(member, new Callback(_ =>
                {
                    CloseRemoveConfirmOverlay();
                    SocialNetworkClassPatch.RefreshFriendsList();
                    RebuildRosterTiles();
                }));
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Failed to delete teammate '{entry.AccountId}'.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void CloseRemoveConfirmOverlay()
        {
            if (removeConfirmOverlay == null)
            {
                return;
            }

            Destroy(removeConfirmOverlay);
            removeConfirmOverlay = null;
        }

        private RectTransform CreatePortraitHost(RectTransform tileRect, int level, out PlayerIconImage iconImage)
        {
            RectTransform portraitRoot = new GameObject("PortraitRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            portraitRoot.SetParent(tileRect, false);
            portraitRoot.anchorMin = new Vector2(0.5f, 1f);
            portraitRoot.anchorMax = new Vector2(0.5f, 1f);
            portraitRoot.pivot = new Vector2(0.5f, 1f);
            portraitRoot.sizeDelta = new Vector2(142f, 142f);
            portraitRoot.anchoredPosition = new Vector2(0f, -14f);

            CreatePortraitBorder(portraitRoot);

            if (TryCreateStockPortrait(portraitRoot, level, out iconImage))
            {
                return portraitRoot;
            }

            RectTransform fallbackRoot = new GameObject("PortraitFallback", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            fallbackRoot.SetParent(portraitRoot, false);
            Stretch(fallbackRoot);

            Image preview = fallbackRoot.GetComponent<Image>();
            preview.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            preview.preserveAspect = true;
            preview.sprite = LoadSquadIcon();

            GameObject progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(fallbackRoot, false);
            RectTransform progressRect = progress.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0.5f, 0.5f);
            progressRect.anchorMax = new Vector2(0.5f, 0.5f);
            progressRect.pivot = new Vector2(0.5f, 0.5f);
            progressRect.sizeDelta = new Vector2(28f, 28f);
            progress.SetActive(false);

            Image progressImage = progress.GetComponent<Image>();
            progressImage.color = new Color(0.9f, 0.9f, 0.88f, 0.85f);
            progressImage.sprite = LoadSquadIcon();

            iconImage = new PlayerIconImage
            {
                _pmcPreview = preview,
                _progress = progress
            };

            return portraitRoot;
        }

        private static void CreatePortraitBorder(RectTransform portraitRoot)
        {
            if (portraitRoot == null)
            {
                return;
            }

            GameObject borderObject = new GameObject("pitFireTeam_PortraitBorder", typeof(RectTransform), typeof(Image), typeof(Outline));
            borderObject.transform.SetParent(portraitRoot, false);
            borderObject.transform.SetAsLastSibling();

            RectTransform borderRect = borderObject.GetComponent<RectTransform>();
            Stretch(borderRect);

            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.color = new Color(0f, 0f, 0f, 0f);
            borderImage.raycastTarget = false;

            Outline outline = borderObject.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        private bool TryCreateStockPortrait(RectTransform parent, int level, out PlayerIconImage iconImage)
        {
            iconImage = null;

            TradingPlayerPanel template = ResolveTradingPlayerPanelTemplate();
            PlayerIconImage templateIcon = TradingPlayerPanelIconField?.GetValue(template) as PlayerIconImage;
            if (templateIcon?._pmcPreview == null || templateIcon._progress == null)
            {
                return false;
            }

            Transform templateRoot = FindPortraitTemplateRoot(templateIcon);
            if (templateRoot == null)
            {
                return false;
            }

            GameObject clonedRoot = Instantiate(templateRoot.gameObject, parent, false);
            clonedRoot.name = "Portrait";
            RectTransform clonedRect = clonedRoot.transform as RectTransform;
            if (clonedRect != null)
            {
                Stretch(clonedRect);
            }

            Image previewClone = FindComponentByPath<Image>(templateRoot, clonedRoot.transform, templateIcon._pmcPreview.transform);
            GameObject progressClone = FindTransformByPath(templateRoot, clonedRoot.transform, templateIcon._progress.transform)?.gameObject;
            if (previewClone == null || progressClone == null)
            {
                Destroy(clonedRoot);
                return false;
            }

            iconImage = new PlayerIconImage
            {
                _pmcPreview = previewClone,
                _progress = progressClone
            };

            ReplaceStockPortraitChrome(clonedRoot.transform, level);
            ResetStockPortraitToLoadingState(iconImage);

            return true;
        }

        private static void ResetStockPortraitToLoadingState(PlayerIconImage iconImage)
        {
            if (iconImage?._pmcPreview == null || iconImage._progress == null)
            {
                return;
            }

            iconImage._pmcPreview.sprite = null;
            iconImage._pmcPreview.enabled = false;
            iconImage._pmcPreview.preserveAspect = true;
            iconImage._progress.SetActive(true);
        }

        private void ReplaceStockPortraitChrome(Transform clonedRoot, int level)
        {
            if (clonedRoot == null)
            {
                return;
            }

            Transform background = clonedRoot.Find("Background");
            if (background != null)
            {
                background.gameObject.SetActive(true);
            }

            SetNamedPortraitChildActive(clonedRoot, "Shadow_L", false);
            SetNamedPortraitChildActive(clonedRoot, "Shadow_R", false);

            Transform levelRoot = clonedRoot.Find("Level");
            if (levelRoot != null)
            {
                //
                TextMeshProUGUI? label = levelRoot.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = level.ToString("D2");
                }
                else
                {
                    levelRoot.gameObject.SetActive(false);

                    CreatePortraitLevelBadge(clonedRoot, level);
                }
            }

            RemoveStockPortraitBorderChrome(clonedRoot);
            CreatePortraitBackground(clonedRoot);
        }

        private static void SetNamedPortraitChildActive(Transform root, string childName, bool isActive)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                Transform child = descendants[i];
                if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(isActive);
                }
            }
        }

        private static void RemoveStockPortraitBorderChrome(Transform clonedRoot)
        {
            if (clonedRoot == null)
            {
                return;
            }

            Image[] images = clonedRoot.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                {
                    continue;
                }

                string name = image.name;
                if (name.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("border", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.gameObject.SetActive(false);
                }
            }
        }

        private static void CreatePortraitBackground(Transform clonedRoot)
        {
            GameObject backgroundObject = new GameObject("pitFireTeam_PortraitBackground", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(clonedRoot, false);
            backgroundObject.transform.SetAsFirstSibling();

            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);

            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.color = new Color(0.14f, 0.15f, 0.16f, 1f);
            backgroundImage.raycastTarget = false;
        }

        private void CreatePortraitLevelBadge(Transform clonedRoot, int level)
        {
            GameObject badgeObject = new GameObject("pitFireTeam_LevelBadge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(clonedRoot, false);

            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(44f, 36f);
            badgeRect.anchoredPosition = new Vector2(0f, 0f);

            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.color = new Color(0f, 0f, 0f, 0f);
            badgeImage.raycastTarget = false;

            GameObject labelObject = new GameObject("LevelLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(badgeObject.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.localPosition = new Vector3(17f, -14f, 0f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = level.ToString("D2");
            label.fontSize = 18f;
            label.fontWeight = FontWeight.SemiBold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            label.raycastTarget = false;
        }

        private Transform FindPortraitTemplateRoot(PlayerIconImage templateIcon)
        {
            Transform previewTransform = templateIcon._pmcPreview.transform;
            Transform progressTransform = templateIcon._progress.transform;
            if (previewTransform == null || progressTransform == null)
            {
                return null;
            }

            HashSet<Transform> previewAncestors = new HashSet<Transform>();
            for (Transform current = previewTransform; current != null; current = current.parent)
            {
                previewAncestors.Add(current);
            }

            for (Transform current = progressTransform; current != null; current = current.parent)
            {
                if (previewAncestors.Contains(current))
                {
                    return current;
                }
            }

            return previewTransform.parent;
        }

        private static T FindComponentByPath<T>(Transform templateRoot, Transform clonedRoot, Transform target) where T : Component
        {
            Transform clone = FindTransformByPath(templateRoot, clonedRoot, target);
            return clone != null ? clone.GetComponent<T>() : null;
        }

        private static Transform FindTransformByPath(Transform templateRoot, Transform clonedRoot, Transform target)
        {
            if (templateRoot == null || clonedRoot == null || target == null)
            {
                return null;
            }

            List<int> path = new List<int>();
            for (Transform current = target; current != null && current != templateRoot; current = current.parent)
            {
                path.Add(current.GetSiblingIndex());
            }

            if (target != templateRoot && (target.parent == null || !IsAncestorOf(templateRoot, target)))
            {
                return null;
            }

            Transform resolved = clonedRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                int index = path[i];
                if (index < 0 || index >= resolved.childCount)
                {
                    return null;
                }

                resolved = resolved.GetChild(index);
            }

            return resolved;
        }

        private static bool IsAncestorOf(Transform ancestor, Transform target)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        private TextMeshProUGUI CreateRosterNameLabel(RectTransform tileRect, string nickname)
        {
            GameObject textObject = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(tileRect, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0f);
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.offsetMin = new Vector2(16f, 14f);
            textRect.offsetMax = new Vector2(-16f, 66f);

            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = nickname;
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.Bottom;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = new Color(0.86f, 0.86f, 0.84f, 1f);
            return label;
        }

        private void AttachPortraitProfileTrigger(RectTransform portraitHost, SquadRosterEntry entry)
        {
            if (portraitHost == null || entry == null)
            {
                return;
            }

            GameObject triggerObject = new GameObject("pitFireTeam_PortraitProfileTrigger", typeof(RectTransform), typeof(Image));
            triggerObject.transform.SetParent(portraitHost, false);

            RectTransform triggerRect = triggerObject.GetComponent<RectTransform>();
            Stretch(triggerRect);
            triggerRect.SetAsLastSibling();

            Image triggerImage = triggerObject.GetComponent<Image>();
            triggerImage.color = new Color(0f, 0f, 0f, 0.001f);
            triggerImage.raycastTarget = true;

            PortraitContextClickController controller = triggerObject.AddComponent<PortraitContextClickController>();
            controller.OnLeftClick = _ => OpenProfile(entry.AccountId);
            controller.OnRightClick = eventData => ShowPortraitContextMenu(entry, eventData);
        }

        private void RegisterPortraitLoadingIndicator(string accountId, GameObject progressObject)
        {
            if (string.IsNullOrWhiteSpace(accountId) || progressObject == null)
            {
                return;
            }

            portraitLoadingIndicators[accountId] = progressObject;
            progressObject.SetActive(false);
        }

        private void SetPortraitLoading(string accountId, bool loading)
        {
            if (TryGetPortraitLoadingIndicator(accountId, out GameObject progressObject))
            {
                progressObject.SetActive(loading);
            }
        }

        private bool TryGetPortraitLoadingIndicator(string accountId, out GameObject progressObject)
        {
            progressObject = null;

            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            if (portraitLoadingIndicators.TryGetValue(accountId, out progressObject) && progressObject != null)
            {
                return true;
            }

            Transform tile = rosterGridRoot?.Find($"pitFireTeam_RosterTile_{accountId}");
            Transform progressTransform = FindChildRecursive(tile, "Progress");
            if (progressTransform == null)
            {
                return false;
            }

            progressObject = progressTransform.gameObject;
            portraitLoadingIndicators[accountId] = progressObject;
            return true;
        }

        private void EnqueuePortrait(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)
        {
            _portraitQueue.Enqueue((accountId, iconImage, portraitRoot, buildVersion));

            if (_portraitQueueCoroutine == null)
            {
                _portraitQueueCoroutine = pitFireTeam.Instance.StartCoroutine(DrainPortraitQueueCoroutine());
            }
        }

        private void CancelPortraitQueue()
        {
            _portraitQueue.Clear();

            if (_portraitQueueCoroutine != null)
            {
                pitFireTeam.Instance.StopCoroutine(_portraitQueueCoroutine);
                _portraitQueueCoroutine = null;
            }
        }

        private IEnumerator DrainPortraitQueueCoroutine()
        {
            while (_portraitQueue.Count > 0)
            {
                var (accountId, iconImage, portraitRoot, buildVersion) = _portraitQueue.Dequeue();
                yield return LoadTeammatePortraitCoroutine(accountId, iconImage, portraitRoot, buildVersion);
                yield return new WaitForSeconds(0.3f);
            }

            _portraitQueueCoroutine = null;
        }

        private IEnumerator LoadTeammatePortraitCoroutine(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)
        {
            if (string.IsNullOrWhiteSpace(accountId) || iconImage == null || ItemUiContext.Instance?.Session == null)
            {
                yield break;
            }

            var profileTask = ItemUiContext.Instance.Session.GetOtherPlayerProfile(accountId);
            while (!profileTask.IsCompleted)
            {
                yield return null;
            }

            if (buildVersion != rosterBuildVersion || portraitRoot == null || iconImage._pmcPreview == null)
            {
                yield break;
            }

            if (profileTask.IsFaulted || profileTask.Result.Failed || profileTask.Result.Value == null)
            {
                yield break;
            }

            try
            {
                GClass1416 profile = new GClass1416(profileTask.Result.Value);
                iconImage.SetPresetIcon(profile.Customization, profile.Equipment);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError($"[UI] Failed to load teammate portrait for '{accountId}'.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void OpenProfile(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId) || ItemUiContext.Instance == null)
            {
                return;
            }

            try
            {
                OtherPlayerProfileScreenPatch.PrepareReturnOverride(ReturnFromProfileToSquadControl);
                Task<OtherPlayerProfileScreen.GClass3883> task = ItemUiContext.Instance.ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile);
                task.ContinueWith(
                    continuation =>
                    {
                        if (continuation.IsFaulted)
                        {
                            OtherPlayerProfileScreenPatch.ClearPendingReturnOverride();
                            pitFireTeam.Log.LogError($"[UI] Failed to open profile for '{accountId}'.");
                            pitFireTeam.Log.LogError(continuation.Exception);
                        }
                    },
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                OtherPlayerProfileScreenPatch.ClearPendingReturnOverride();
                pitFireTeam.Log.LogError($"[UI] Failed to start profile open for '{accountId}'.");
                pitFireTeam.Log.LogError(ex);
            }
        }

        private void CreateScrollableRosterArea(RectTransform shellRect)
        {
            if (shellRect == null)
            {
                return;
            }

            GameObject scrollRootObject = new GameObject("pitFireTeam_SquadControlRosterScroll", typeof(RectTransform), typeof(ScrollRectNoDrag));
            scrollRootObject.transform.SetParent(shellRect, false);
            RectTransform scrollRoot = scrollRootObject.GetComponent<RectTransform>();
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(18f, RosterViewportBottomInset);
            scrollRoot.offsetMax = new Vector2(-18f, -RosterViewportTopInset);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRoot, false);
            rosterViewport = viewportObject.GetComponent<RectTransform>();
            rosterViewport.anchorMin = Vector2.zero;
            rosterViewport.anchorMax = Vector2.one;
            rosterViewport.offsetMin = Vector2.zero;
            rosterViewport.offsetMax = new Vector2(-22f, 0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            rosterScrollbar = CreateRosterScrollbar(scrollRoot);

            GameObject contentObject = new GameObject("pitFireTeam_SquadControlRosterContent", typeof(RectTransform));
            contentObject.transform.SetParent(rosterViewport, false);
            rosterContentRoot = contentObject.GetComponent<RectTransform>();
            rosterContentRoot.anchorMin = new Vector2(0f, 1f);
            rosterContentRoot.anchorMax = new Vector2(1f, 1f);
            rosterContentRoot.pivot = new Vector2(0.5f, 1f);
            rosterContentRoot.anchoredPosition = Vector2.zero;
            rosterContentRoot.sizeDelta = new Vector2(0f, 0f);

            GameObject gridObject = new GameObject("pitFireTeam_SquadControlRosterGrid", typeof(RectTransform));
            gridObject.transform.SetParent(rosterContentRoot, false);
            rosterGridRoot = gridObject.GetComponent<RectTransform>();
            ConfigureRosterContentContainer(rosterGridRoot);

            rosterScrollRect = scrollRootObject.GetComponent<ScrollRectNoDrag>();
            rosterScrollRect.horizontal = false;
            rosterScrollRect.vertical = true;
            rosterScrollRect.movementType = ScrollRect.MovementType.Clamped;
            rosterScrollRect.scrollSensitivity = 30f;
            rosterScrollRect.inertia = true;
            rosterScrollRect.viewport = rosterViewport;
            rosterScrollRect.content = rosterContentRoot;
            rosterScrollRect.verticalScrollbar = rosterScrollbar;
            rosterScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            rosterScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            rosterScrollRect.verticalScrollbarSpacing = 6f;
            rosterScrollRect.AutoZeroing = true;
            rosterScrollRect.Alignment = TextAnchor.UpperCenter;
        }

        private Scrollbar CreateRosterScrollbar(RectTransform parent)
        {
            Scrollbar template = ResolveVerticalScrollbarTemplate();
            Scrollbar scrollbar;

            if (template != null)
            {
                scrollbar = Instantiate(template, parent, false);
                scrollbar.name = "pitFireTeam_SquadControlScrollbar";
            }
            else
            {
                scrollbar = CreateFallbackScrollbar(parent);
            }

            if (scrollbar.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(12f, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            scrollbar.gameObject.SetActive(true);
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            return scrollbar;
        }

        private void UpdateRosterGridLayout(int entryCount)
        {
            if (rosterGridLayout == null)
            {
                return;
            }

            int columnCount = Mathf.Clamp(entryCount, 1, 5);
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(entryCount / 5f));
            float contentHeight = CalculateRowsHeight(rowCount) - RosterViewportPadding;
            float viewportHeight = Mathf.Max(0f, rosterViewport != null && rosterViewport.rect.height > 1f
                ? rosterViewport.rect.height
                : currentRosterShellHeight - 20f);
            float wrapperHeight = Mathf.Max(viewportHeight, contentHeight);
            float gridOffsetY = contentHeight < wrapperHeight ? (wrapperHeight - contentHeight) * 0.5f : 0f;

            rosterGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rosterGridLayout.constraintCount = columnCount;

            if (rosterContentRoot != null)
            {
                rosterContentRoot.sizeDelta = new Vector2(0f, wrapperHeight);
            }

            if (rosterGridRoot != null)
            {
                rosterGridRoot.sizeDelta = new Vector2(0f, contentHeight);
                rosterGridRoot.anchoredPosition = new Vector2(0f, -gridOffsetY);
            }
        }

        private void UpdateRosterPanelLayout(bool isEmpty)
        {
            float buttonHeight = addTeammateButton?.transform is RectTransform addButtonSource
                ? addButtonSource.sizeDelta.y
                : 54f;

            if (stockCardsContainer != null)
            {
                float verticalScaleCompensation = ResolveUiScaleCompensation(rosterPanelRect);
                float shellCenterY = isEmpty ? 54f : (RosterShellToButtonGap + buttonHeight) * 0.5f;
                stockCardsContainer.anchoredPosition = new Vector2(0f, (shellCenterY + RosterBlockVerticalOffset) * verticalScaleCompensation);
            }

            if (addTeammateButton?.transform is RectTransform addButtonRect)
            {
                float verticalScaleCompensation = ResolveUiScaleCompensation(rosterPanelRect);
                float buttonCenterY = isEmpty
                    ? EmptyRosterButtonCenterY
                    : -(currentRosterShellHeight + RosterShellToButtonGap) * 0.5f;
                addButtonRect.anchoredPosition = new Vector2(0f, (buttonCenterY + RosterBlockVerticalOffset) * verticalScaleCompensation);
            }

            if (emptyRosterLabel?.transform is RectTransform emptyLabelRect)
            {
                float verticalScaleCompensation = ResolveUiScaleCompensation(rosterPanelRect);
                float emptyLabelY = EmptyRosterButtonCenterY + EmptyRosterLabelSpacing;
                emptyLabelRect.anchoredPosition = new Vector2(0f, (emptyLabelY + RosterBlockVerticalOffset) * verticalScaleCompensation);
            }

            if (rosterPanelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterPanelRect);
            }
        }

        private static float CalculateRosterShellHeight()
        {
            float screenHeight = Mathf.Max(720f, Screen.height);
            float targetHeight = screenHeight * RosterHeightScreenRatio;
            int visibleRowCount = CalculateVisibleRosterRowCount(targetHeight);
            return CalculateRowsHeight(visibleRowCount) + RosterViewportTopInset + RosterViewportBottomInset;
        }

        private static int CalculateVisibleRosterRowCount(float targetHeight)
        {
            float rowUnit = RosterTileHeight + RosterTileSpacing;
            float usableHeight = Mathf.Max(0f, targetHeight - RosterViewportTopInset - RosterViewportBottomInset - RosterViewportPadding + RosterTileSpacing);
            int visibleRows = Mathf.FloorToInt(usableHeight / rowUnit);
            return Mathf.Max(2, visibleRows);
        }

        private static float CalculateRowsHeight(int rowCount)
        {
            int rows = Mathf.Max(1, rowCount);
            return RosterTileHeight * rows + RosterTileSpacing * (rows - 1) + RosterViewportPadding;
        }

        private static float ResolveUiScaleCompensation(RectTransform reference)
        {
            Canvas canvas = reference != null ? reference.GetComponentInParent<Canvas>() : null;
            CanvasScaler scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler == null || scaler.scaleFactor <= 0.001f || GClass3825.Float_0 <= 0.001f)
            {
                return 1f;
            }

            return Mathf.Clamp(GClass3825.Float_0 / scaler.scaleFactor, 0.5f, 2f);
        }

        // Sequential portrait load queue — one SetPresetIcon per entry, processed in order.
        private static readonly Queue<(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)> _portraitQueue
            = new Queue<(string, PlayerIconImage, RectTransform, int)>();
        private static Coroutine _portraitQueueCoroutine;

        private void PrepareRosterShellContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            if (container.GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup horizontalLayout)
            {
                horizontalLayout.enabled = false;
            }

            if (container.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup verticalLayout)
            {
                verticalLayout.enabled = false;
            }

            if (container.GetComponent<GridLayoutGroup>() is GridLayoutGroup gridLayout)
            {
                gridLayout.enabled = false;
            }

            if (container.GetComponent<ContentSizeFitter>() is ContentSizeFitter sizeFitter)
            {
                sizeFitter.enabled = false;
            }
        }

        private void ConfigureRosterContentContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            rosterGridLayout = container.GetComponent<GridLayoutGroup>() ?? container.gameObject.AddComponent<GridLayoutGroup>();
            rosterGridLayout.enabled = true;
            rosterGridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            rosterGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            rosterGridLayout.cellSize = new Vector2(RosterTileWidth, RosterTileHeight);
            rosterGridLayout.spacing = new Vector2(RosterTileSpacing, RosterTileSpacing);
            rosterGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rosterGridLayout.constraintCount = 5;
            rosterGridLayout.childAlignment = TextAnchor.UpperCenter;
            rosterGridLayout.padding = new RectOffset(0, 0, 0, 0);

            ContentSizeFitter sizeFitter = container.GetComponent<ContentSizeFitter>() ?? container.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            container.anchorMin = new Vector2(0.5f, 1f);
            container.anchorMax = new Vector2(0.5f, 1f);
            container.pivot = new Vector2(0.5f, 1f);
            container.anchoredPosition = Vector2.zero;
            container.sizeDelta = Vector2.zero;
            container.localScale = Vector3.one;
        }

    }
}
