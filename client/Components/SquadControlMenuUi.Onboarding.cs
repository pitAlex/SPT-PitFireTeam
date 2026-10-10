using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;
using pitTeam.Patches;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
        private const string OnboardingRoute = "/singleplayer/pitfireteam/squad-onboarding";
        private static readonly Dictionary<GameplayMode, Sprite> modeCardSprites = new Dictionary<GameplayMode, Sprite>();
        private GameObject onboardingPanel;
        private Transform squadVisitHost;
        private int squadVisitVersion;
        private bool onboardingVisible;
        private bool onboardingBusy;
        private bool rosterLoadSucceeded;
        private int welcomeDeliveryVisit = -1;

        internal async Task<bool> PrepareSquadVisitAsync(Transform host)
        {
            squadVisitHost = host;
            int version = ++squadVisitVersion;
            onboardingVisible = true;
            EnsureScreen();
            HideSquadPanels();
            CreateOnboardingPanel(host);
            ShowOnboardingMessage("SquadOnboardingLoading", false);
            try
            {
                JObject state = await SendOnboardingAsync(string.Empty);
                if (!IsSquadVisitCurrent(version)) return true;
                if (state["firstTimeVisit"]?.Value<bool>() != true)
                {
                    BuildOnboardingCards();
                    return true;
                }
                onboardingVisible = false;
                DestroyOnboardingPanel();
                return false;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Squad onboarding could not load: " + ex);
                if (IsSquadVisitCurrent(version)) ShowOnboardingMessage("SquadOnboardingFailed", true);
                return true;
            }
        }

        private void HideSquadPanels()
        {
            rosterPanel?.SetActive(false);
            modePanel?.SetActive(false);
            settingsPanel?.SetActive(false);
            ClosePortraitContextMenu();
            CloseRemoveConfirmOverlay();
            CancelPortraitQueue();
        }

        private void CreateOnboardingPanel(Transform host)
        {
            DestroyOnboardingPanel();
            onboardingPanel = new GameObject("pitFireTeam_SquadOnboarding", typeof(RectTransform));
            RectTransform rect = onboardingPanel.GetComponent<RectTransform>();
            rect.SetParent(host, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(820f, 535f);
            rect.anchoredPosition = new Vector2(0f, -12f);
            if (host is RectTransform hostRect && hostRect.rect.height > 1f)
            {
                float fit = Mathf.Min(1f, Mathf.Max(0.5f, (hostRect.rect.height - 230f) / 535f),
                    Mathf.Max(0.5f, (hostRect.rect.width - 100f) / 820f));
                rect.localScale = new Vector3(fit, fit, 1f);
            }
        }

        private void ClearOnboardingContent()
        {
            if (onboardingPanel == null) return;
            foreach (Transform child in onboardingPanel.transform)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private void ShowOnboardingMessage(string key, bool retry)
        {
            if (onboardingPanel == null) return;
            ClearOnboardingContent();
            GameObject label = CreateText("Status", GetSocialUiText(key), 22f, TextAlignmentOptions.Center);
            label.transform.SetParent(onboardingPanel.transform, false);
            Stretch(label.GetComponent<RectTransform>());
            if (!retry) return;
            RectTransform buttonRect = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
            buttonRect.SetParent(onboardingPanel.transform, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(180f, 40f);
            buttonRect.anchoredPosition = new Vector2(0f, -65f);
            buttonRect.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 1f);
            GameObject retryLabel = CreateText("Label", GetSocialUiText("SquadOnboardingRetry"), 20f, TextAlignmentOptions.Center);
            retryLabel.transform.SetParent(buttonRect, false);
            Stretch(retryLabel.GetComponent<RectTransform>());
            buttonRect.GetComponent<Button>().onClick.AddListener(() => MatchMakerSideSelectionScreenShowPatch.RefreshSquadScreen(squadVisitHost));
        }

        private void BuildOnboardingCards()
        {
            ClearOnboardingContent();
            CreateModeCard(GameplayMode.GunsForHire, -210f, "SquadControlModeGunsForHire", "SquadControlModeGunsForHireDescription");
            CreateModeCard(GameplayMode.Allegiance, 210f, "SquadControlModeAllegiance", "SquadControlModeAllegianceDescription");
        }

        private void CreateModeCard(GameplayMode mode, float x, string nameKey, string descriptionKey)
        {
            GameObject card = new GameObject("pitFireTeam_ModeCard_" + mode, typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = card.GetComponent<RectTransform>();
            rect.SetParent(onboardingPanel.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(RosterTileWidth * 2f, RosterTileHeight * 2.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            Image background = card.GetComponent<Image>();
            Color normal = new Color(0.045f, 0.045f, 0.045f, 0.97f);
            background.color = normal;

            GameObject corner = new GameObject("BackgroundOverlay", typeof(RectTransform), typeof(Image));
            RectTransform cornerRect = corner.GetComponent<RectTransform>();
            cornerRect.SetParent(rect, false);
            cornerRect.anchorMin = cornerRect.anchorMax = cornerRect.pivot = new Vector2(0f, 1f);
            cornerRect.sizeDelta = new Vector2(116f, 145f);
            Image cornerImage = corner.GetComponent<Image>();
            cornerImage.sprite = LoadRosterTileDiagonalSprite();
            cornerImage.color = cornerImage.sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.06f);
            cornerImage.raycastTarget = false;

            RectTransform portrait = new GameObject("PortraitRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            portrait.SetParent(rect, false);
            portrait.anchorMin = portrait.anchorMax = portrait.pivot = new Vector2(0.5f, 1f);
            portrait.sizeDelta = new Vector2(284f, 355f);
            portrait.anchoredPosition = new Vector2(0f, -35f);

            // Match roster portraits: the outline must sit behind the image it frames.
            CreatePortraitBorder(portrait);
            GameObject artwork = new GameObject("PortraitArtwork", typeof(RectTransform), typeof(Image));
            artwork.transform.SetParent(portrait, false);
            Stretch(artwork.GetComponent<RectTransform>());
            Image image = artwork.GetComponent<Image>();
            image.sprite = LoadModeCardSprite(mode);
            image.preserveAspect = true;
            image.raycastTarget = false;

            TextMeshProUGUI name = CreateRosterNameLabel(rect, GetSocialUiText(nameKey));
            RectTransform nameRect = name.GetComponent<RectTransform>();
            nameRect.offsetMin = new Vector2(32f, 35f);
            nameRect.offsetMax = new Vector2(-32f, 130f);
            name.fontSize = 32f;
            name.alignment = TextAlignmentOptions.Bottom;
            card.AddComponent<RosterTileHoverController>().Configure(background, name, normal,
                new Color(0.6235f, 0.6157f, 0.5647f, 1f), new Color(0.16f, 0.16f, 0.16f, 0.99f));
            card.AddComponent<TooltipHoverController>().Configure(GetSocialUiText(descriptionKey));
            Button button = card.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = background;
            // No portrait/context controller is attached; Button only accepts left click.
            button.onClick.AddListener(() => CompleteOnboarding(mode));
        }

        private Sprite LoadModeCardSprite(GameplayMode mode)
        {
            if (modeCardSprites.TryGetValue(mode, out Sprite sprite) && sprite != null) return sprite;
            string filename = mode == GameplayMode.Allegiance ? "allegiance-card.png" : "guns-for-hire-card.png";
            string path = new[] { Path.Combine(PluginDirectory, filename), Path.Combine(PluginDirectory, "resources", filename),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", filename) }.FirstOrDefault(File.Exists);
            if (path == null) throw new FileNotFoundException("Mode card artwork is missing.", filename);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(texture);
                throw new InvalidDataException("Mode card artwork could not be decoded: " + filename);
            }
            texture.name = "pitFireTeam_ModeCard_" + mode;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            modeCardSprites[mode] = sprite;
            return sprite;
        }

        private async void CompleteOnboarding(GameplayMode mode)
        {
            if (onboardingBusy || !onboardingVisible) return;
            onboardingBusy = true;
            int version = squadVisitVersion;
            foreach (Button button in onboardingPanel.GetComponentsInChildren<Button>()) button.interactable = false;
            try
            {
                await ApplyGameplayModeAsync(mode);
                JObject state = await SendOnboardingAsync("/complete", new { gameplayMode = mode.ToString() });
                if (state["firstTimeVisit"]?.Value<bool>() != true) throw new InvalidOperationException("Onboarding did not complete.");
                if (!IsSquadVisitCurrent(version)) return;
                RequestRosterRefreshNowOrNextInject();
                RebuildSettingsEntries();
                RefreshModeChoices();
                MatchMakerSideSelectionScreenShowPatch.RefreshSquadScreen(squadVisitHost);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Initial gameplay mode selection failed: " + ex);
                AddTeammateCreationFlow.ShowToast(GetSocialUiText("SquadOnboardingFailed"));
                if (IsSquadVisitCurrent(version)) BuildOnboardingCards();
            }
            finally { onboardingBusy = false; }
        }

        private static async Task<JObject> SendOnboardingAsync(string suffix, object body = null)
        {
            string response = await Task.Run(() => suffix.Length == 0 ? RequestHandler.GetJson(OnboardingRoute)
                : RequestHandler.PostJson(OnboardingRoute + suffix, JsonConvert.SerializeObject(body ?? new { })));
            JObject root = JObject.Parse(response);
            if (root["err"]?.Value<int>() != 0) throw new InvalidOperationException(root["errmsg"]?.ToString());
            return root["data"] as JObject ?? throw new InvalidDataException("Invalid onboarding response.");
        }

        private bool IsSquadVisitCurrent(int version) => this != null && version == squadVisitVersion
            && squadVisitHost != null && squadVisitHost.gameObject.activeInHierarchy && SquadSideSelectionFlow.SquadModeActive;

        private async void NotifySquadScreenRefreshed()
        {
            int version = squadVisitVersion;
            if (onboardingVisible || !rosterLoadSucceeded || !IsSquadVisitCurrent(version) || welcomeDeliveryVisit == version) return;
            welcomeDeliveryVisit = version;
            try
            {
                // Allow layout and the restored normal panel to render before starting the timer.
                await Task.Yield();
                if (!IsSquadVisitCurrent(version)) return;
                JObject state = await SendOnboardingAsync("/refreshed");
                while (state["welcomePending"]?.Value<bool>() == true)
                {
                    await Task.Delay(Math.Max(50, state["delayMilliseconds"]?.Value<int>() ?? 2000));
                    if (!IsSquadVisitCurrent(version)) return;
                    state = await SendOnboardingAsync("/deliver");
                }
                if (IsSquadVisitCurrent(version) && state["invitationDelivered"]?.Value<bool>() == true)
                    SocialNetworkClassPatch.RefreshFriendsList(true);
            }
            catch (Exception ex)
            {
                welcomeDeliveryVisit = -1;
                pitFireTeam.Log.LogError("[UI] Welcome invitation delivery remains pending: " + ex);
            }
        }

        private void DestroyOnboardingPanel()
        {
            if (onboardingPanel != null)
            {
                onboardingPanel.SetActive(false);
                Destroy(onboardingPanel);
                onboardingPanel = null;
            }
        }

        private void CancelSquadVisit()
        {
            squadVisitVersion++;
            squadVisitHost = null;
            onboardingVisible = false;
            DestroyOnboardingPanel();
        }
    }
}
