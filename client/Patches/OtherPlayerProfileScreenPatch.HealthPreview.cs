using EFT;
using EFT.UI;
using EFT.UI.Health;
using EFT.UI.Matchmaker;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static int HealthPreviewGeneration;
        private static bool HealthPaymentBusy;
        private static float HealthPreviewRecoveryPerMinute;
        private static InventoryScreenHealthPanel HealthPreviewPanel;
        private static Coroutine HealthPreviewCoroutine;
        private static Coroutine HealthPreviewHealingCoroutine;
        private static DefaultUIButton HealthPreviewHealButton;
        private static RectTransform HealthPreviewFooter;

        private static void DisplayHealthPreview(OtherPlayerProfileScreen screen, OtherPlayerProfile profile, IEftSession session)
        {
            CloseHealthPreview();
            // Field Upkeep gates new raid injuries on the server. Existing wounds must
            // remain visible and treatable even after the player disables it.
            if (pitFireTeam.Instance != null)
                HealthPreviewCoroutine = pitFireTeam.Instance.StartCoroutine(DisplayHealthPreviewDeferred(screen, profile, session));
        }

        private static IEnumerator DisplayHealthPreviewDeferred(OtherPlayerProfileScreen screen, OtherPlayerProfile profile, IEftSession session)
        {
            yield return null;
            HealthPreviewCoroutine = null;
            if (screen == null || screen != ActiveProfileScreen || profile != ViewedProfile || !screen.gameObject.activeInHierarchy)
                yield break;

            Canvas.ForceUpdateCanvases();
            _ = LoadHealthPreviewAsync(screen, profile, session, HealthPreviewGeneration);
        }

        private const string TeammateHealthRoute = "/singleplayer/pitfireteam/teammate/health";

        private sealed class TeammateTreatmentResponse
        {
            public Profile.HealthInfo health;
            public string quoteToken;
            public int price;
            public JsonType.FlatItem[] insurancePlayerRoubles;
            public Dictionary<string, EFT.TraderData> insurancePlayerTraders;
            public Dictionary<EBodyPart, double> regenerationPerSecond;
            public double loopSeconds;
        }

        private static async Task<TeammateTreatmentResponse> RequestTeammateTreatmentAsync(string suffix, string aid, string token = null, bool syncPayment = false)
        {
            string body = SerializeBody(new { aid, quoteToken = token, syncPayment });
            string json = await Task.Run(() => RequestHandler.PostJson(TeammateHealthRoute + suffix, body));
            var response = DeserializeBodySuccess<TeammateTreatmentResponse>(json)?.data;
            if (response?.health?.BodyParts == null) throw new InvalidOperationException("ProfileHealingFailed");
            return response;
        }

        private static async Task LoadHealthPreviewAsync(OtherPlayerProfileScreen screen, OtherPlayerProfile profile, IEftSession session, int generation)
        {
            try
            {
                var response = await RequestTeammateTreatmentAsync("", profile.AccountId.ToString());
                if (generation != HealthPreviewGeneration || screen == null || screen != ActiveProfileScreen || profile != ViewedProfile)
                    return;
                if (!IsHealthPreviewFull(response.health))
                {
                    ShowHealthPreview(screen, profile, session, response.health, (float)(response.regenerationPerSecond?.Values.Sum() ?? 0) * 60f);
                    if (HealthPreviewPanel != null)
                    {
#if DEBUG
                        float nativeHealth, energy, hydration;
                        EFT.HealthSystem.OfflineHealthController.CalculateRegenRatesPerSecond(out nativeHealth, out energy, out hydration);
                        double teammateHealth = response.regenerationPerSecond?.Values.Sum() ?? 0;
                        Modules.Logger.LogInfo($"[TeammateRecovery] aid={profile.AccountId} playerHpPerHour={nativeHealth * 3600f:F3} teammateHpPerHour={teammateHealth * 3600d:F3}");
                        if (Math.Abs(nativeHealth - teammateHealth) > 0.0001)
                            pitFireTeam.Log.LogWarning("[TeammateRecovery] Player's native rate differs from the server rate; check active hideout bonuses.");
#endif
                        _ = RefreshHealthPreviewAsync(profile, response.health, HealthPreviewPanel, HealthPreviewGeneration, response.loopSeconds);
                    }
                }
            }
            catch (Exception ex) { Modules.Logger.LogError($"[TeammateTreatment] Health load failed: {ex}"); }
        }

        private static async Task RefreshHealthPreviewAsync(OtherPlayerProfile profile, Profile.HealthInfo health,
            InventoryScreenHealthPanel panel, int generation, double loopSeconds)
        {
            try
            {
                while (generation == HealthPreviewGeneration && panel != null && profile == ViewedProfile)
                {
                    await Task.Delay((int)(Math.Max(1, Math.Min(loopSeconds, 30)) * 1000));
                    if (generation != HealthPreviewGeneration || panel == null || profile != ViewedProfile) return;
                    if (HealthPaymentBusy || HealthPreviewHealingCoroutine != null) continue;
                    var response = await RequestTeammateTreatmentAsync("", profile.AccountId.ToString());
                    if (generation != HealthPreviewGeneration || panel == null || HealthPaymentBusy || HealthPreviewHealingCoroutine != null) continue;
                    HealthPreviewRecoveryPerMinute = (float)(response.regenerationPerSecond?.Values.Sum() ?? 0) * 60f;
                    ApplyHealthPreviewValues(health, response.health);
                    panel._parametersPanel.UpdateMainHealthAndStats();
                    if (IsHealthPreviewFull(health))
                    {
                        if (HealthPreviewHealButton != null) HealthPreviewHealButton.Interactable = false;
                        HealthPreviewHealingCoroutine = pitFireTeam.Instance.StartCoroutine(FinishHealthPreviewHealing(health, panel));
                        return;
                    }
                }
            }
            catch (Exception ex) { Modules.Logger.LogError($"[TeammateRecovery] Refresh failed: {ex}"); }
        }

        private static void ApplyHealthPreviewValues(Profile.HealthInfo target, Profile.HealthInfo source)
        {
            foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
            {
                target.BodyParts[part].Health.Current = source.BodyParts[part].Health.Current;
                target.BodyParts[part].Health.Maximum = source.BodyParts[part].Health.Maximum;
            }
        }

        private static void ShowHealthPreview(OtherPlayerProfileScreen screen, OtherPlayerProfile profile, IEftSession session, Profile.HealthInfo health, float recoveryPerMinute)
        {
            var modelWindow = PlayerModelWindowField?.GetValue(screen) as InventoryPlayerModelWithStatsWindow;
            var modelRect = ResolveHealthPreviewImageRect(modelWindow?._playerModelView);
            var sideScreen = Resources.FindObjectsOfTypeAll<MatchMakerSideSelectionScreen>()
                .FirstOrDefault(candidate => candidate != null && candidate._healthPanel != null && candidate._pmcModelView != null);
            var sourceModelRect = ResolveHealthPreviewImageRect(sideScreen?._pmcModelView);
            if (modelRect == null || sourceModelRect == null || sourceModelRect.rect.height <= 0f || sourceModelRect.rect.width <= 0f)
            {
                pitFireTeam.Log.LogWarning($"[UI] Health preview skipped: targetImage={modelRect?.name ?? "missing"}, sourceImage={sourceModelRect?.name ?? "missing"}, sourceSize={sourceModelRect?.rect.size.ToString() ?? "missing"}.");
                return;
            }

            try
            {
                CloseHealthPreview();
                HealthPreviewRecoveryPerMinute = recoveryPerMinute;
                var sourcePanelRect = (RectTransform)sideScreen._healthPanel.transform;
                Vector3 sourceCenter = sourceModelRect.InverseTransformPoint(sourcePanelRect.TransformPoint(sourcePanelRect.rect.center));
                Vector2 centerOffset = (Vector2)sourceCenter - sourceModelRect.rect.center;
                centerOffset = new Vector2(centerOffset.x / sourceModelRect.rect.width, centerOffset.y / sourceModelRect.rect.height);

                // Match EFT's health bars over its PMC model, including the native bar positions.
                HealthPreviewPanel = GameObject.Instantiate(sideScreen._healthPanel, modelRect, false);
                HealthPreviewPanel.name = "pitFireTeam_HealthPreview";
                var panelRect = (RectTransform)HealthPreviewPanel.transform;
                panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
                panelRect.anchoredPosition = new Vector2(centerOffset.x * modelRect.rect.width, centerOffset.y * modelRect.rect.height);
                panelRect.sizeDelta = sourcePanelRect.rect.size;
                // Preserve native screen size; viewport heights can differ between first Show
                // and subsequent visits and must not resize the health widgets.
                Vector3 sourceScale = sourcePanelRect.lossyScale;
                Vector3 targetScale = modelRect.lossyScale;
                panelRect.localScale = new Vector3(sourceScale.x / targetScale.x, sourceScale.y / targetScale.y, 1f);
                panelRect.localRotation = Quaternion.identity;
                var canvasGroup = HealthPreviewPanel.GetComponent<CanvasGroup>() ?? HealthPreviewPanel.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
                HealthPreviewPanel.gameObject.SetActive(true);
                HealthPreviewPanel.Show(CreateProfileHealthController(health, () => HealthPreviewRecoveryPerMinute), null, null);
                HealthPreviewPanel.SetParametersVisibility(false);
                Canvas.ForceUpdateCanvases();
                CreateHealthPreviewFooter(screen, modelWindow, health, profile, session);
            }
            catch (Exception ex)
            {
                CloseHealthPreview();
                Modules.Logger.LogError("[UI] Failed to show temporary teammate health preview.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void CreateHealthPreviewFooter(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow modelWindow, Profile.HealthInfo health, OtherPlayerProfile profile, IEftSession session)
        {
            var template = BackButtonField?.GetValue(screen) as DefaultUIButton;
            var total = HealthPreviewPanel._parametersPanel?._overallHealth;
            var totalRect = HealthPreviewPanel._parametersPanel?._overallHealth?.transform as RectTransform;
            if (template == null || modelWindow._experienceLabel == null || modelWindow._nicknameLabel == null || totalRect == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Health footer skipped: native button, name/EXP labels or total-health widget unavailable.");
                return;
            }

            // The model window has a native layout group. An ignored overlay host prevents
            // it from appending HEAL as another row underneath all profile controls.
            var footerObject = new GameObject("pitFireTeam_HealthPreviewFooter", typeof(RectTransform), typeof(LayoutElement));
            footerObject.GetComponent<LayoutElement>().ignoreLayout = true;
            HealthPreviewFooter = (RectTransform)footerObject.transform;
            HealthPreviewFooter.SetParent(modelWindow.transform, false);
            StretchToFillParent(HealthPreviewFooter);
            Vector2 nativeSize = totalRect.rect.size;
            totalRect.SetParent(HealthPreviewFooter, true);
            totalRect.anchorMin = totalRect.anchorMax = new Vector2(0.5f, 0.5f);
            totalRect.sizeDelta = nativeSize;
            // The side-selection prefab keeps Buff hidden. The native parameter updater
            // supplies positive colour, HP/min formatting, arrow visibility and animation.
            total.transform.Find("Buff")?.gameObject.SetActive(true);
            HealthPreviewPanel._parametersPanel.UpdateMainHealthAndStats();

            HealthPreviewHealButton = CreateOverlayButton(template, HealthPreviewFooter, Vector2.zero, new Vector2(100f, 32f));
            HealthPreviewHealButton.name = "pitFireTeam_HealthPreviewHeal";
            HealthPreviewHealButton.SetRawText(pitFireTeam.GetSocialUiText("ProfileHeal"), 20);
            var buttonRect = (RectTransform)HealthPreviewHealButton.transform;
            HealthPreviewHealButton.OnClick.RemoveAllListeners();
            HealthPreviewHealButton.Interactable = !HealthPaymentBusy;
            var paymentInventory = ActiveProfileInventoryController;
            HealthPreviewHealButton.OnClick.AddListener(() => _ = HealTeammateAsync(profile, session, paymentInventory, health, HealthPreviewPanel));

            Canvas.ForceUpdateCanvases();
            Bounds identityBounds = HealthPreviewTextBounds(HealthPreviewFooter, modelWindow._nicknameLabel);
            identityBounds.Encapsulate(HealthPreviewTextBounds(HealthPreviewFooter, modelWindow._experienceLabel));
            Bounds totalBounds = HealthPreviewTextBounds(HealthPreviewFooter, total._currentValue);
            if (total._maxValue != null)
                totalBounds.Encapsulate(HealthPreviewTextBounds(HealthPreviewFooter, total._maxValue));
            if (total._icon != null)
                totalBounds.Encapsulate(HealthPreviewRectBounds(HealthPreviewFooter, total._icon.rectTransform));

            // Align the visible content, not the native widgets' offset root pivots.
            Vector3 totalDelta = new Vector3(identityBounds.min.x - 88f - totalBounds.max.x,
                identityBounds.center.y - totalBounds.center.y, 0f);
            totalRect.position += HealthPreviewFooter.TransformVector(totalDelta);
            Bounds buttonBounds = HealthPreviewTextBounds(HealthPreviewFooter, HealthPreviewHealButton._headerLabel);
            Vector3 buttonDelta = new Vector3(identityBounds.max.x + 89f - buttonBounds.min.x,
                identityBounds.center.y - buttonBounds.center.y, 0f);
            buttonRect.position += HealthPreviewFooter.TransformVector(buttonDelta);
        }

        private static async Task HealTeammateAsync(OtherPlayerProfile profile, IEftSession session,
            EFT.InventoryLogic.InventoryController inventory, Profile.HealthInfo health, InventoryScreenHealthPanel panel)
        {
            if (HealthPaymentBusy || panel == null || panel != HealthPreviewPanel || HealthPreviewHealingCoroutine != null)
                return;
            HealthPaymentBusy = true;
            if (HealthPreviewHealButton != null) HealthPreviewHealButton.Interactable = false;
            try
            {
                EnsureRepairStashRefreshReady();
                // Resolve refresh capability before the server can charge real money.
                var controller = ResolveActiveProfileInventoryControllerForBackendUpdate(session.Profile, inventory)
                    as EFT.InventoryLogic.OfflineInventoryController;
                if (controller == null) throw new InvalidOperationException("ProfileHealingFailed");
                string aid = profile.AccountId.ToString();
                var quote = await RequestTeammateTreatmentAsync("/quote", aid);
                if (panel == null || panel != HealthPreviewPanel || profile != ViewedProfile) return;
                string prompt = string.Format(GetSocialUiText("ProfileHealingPrompt"), profile.GetCorrectedNickname(), quote.price.ToString("N0"));
                if (!await ItemUiContext.Instance.ShowMessageWindow(out _, prompt, GetSocialUiText("ProfileHealingTitle"), true)) return;
                if (panel == null || panel != HealthPreviewPanel || profile != ViewedProfile) return;
                EnsureRepairStashRefreshReady();
                RepairStashRefreshRequired = true;
                TeammateTreatmentResponse result;
                try { result = await RequestTeammateTreatmentAsync("/heal", aid, quote.quoteToken); }
                catch
                {
                    // An HTTP failure can hide a successful payment. Read the server receipt and
                    // authoritative money before allowing any editor save or another payment.
                    result = await RequestTeammateTreatmentAsync("", aid, syncPayment: true);
                    if (!IsHealthPreviewFull(result.health))
                    {
                        ApplyTreatmentPlayerState(session, controller, result);
                        RepairStashRefreshRequired = false;
                        throw;
                    }
                }
                ApplyTreatmentPlayerState(session, controller, result);
                RepairStashRefreshRequired = false;
                if (panel == null || panel != HealthPreviewPanel) return;
                ApplyHealthPreviewValues(health, result.health);
                panel._parametersPanel.UpdateMainHealthAndStats();
                HealthPreviewHealingCoroutine = pitFireTeam.Instance.StartCoroutine(FinishHealthPreviewHealing(health, panel));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[TeammateTreatment] Payment/healing failed: {ex}");
                string key = RepairStashRefreshRequired ? "LiveStashRefreshFailed"
                    : ex.Message.StartsWith("ProfileHealing", StringComparison.Ordinal) ? ex.Message : "ProfileHealingFailed";
                EFT.Communications.NotificationManager.DisplayWarningNotification(GetSocialUiText(key), EFT.Communications.ENotificationDurationType.Default);
            }
            finally
            {
                HealthPaymentBusy = false;
                if (HealthPreviewHealButton != null && HealthPreviewHealingCoroutine == null)
                    HealthPreviewHealButton.Interactable = !RepairStashRefreshRequired;
            }
        }

        private static void ApplyTreatmentPlayerState(IEftSession session, EFT.InventoryLogic.OfflineInventoryController inventory, TeammateTreatmentResponse response)
        {
            ApplyPlayerRoublePaymentState(session, inventory, new FriendlyTeammateDefaultEquipmentResponse
            {
                insurancePlayerRoubles = response.insurancePlayerRoubles,
                insurancePlayerTraders = response.insurancePlayerTraders
            });
        }

        private static IEnumerator FinishHealthPreviewHealing(Profile.HealthInfo health, InventoryScreenHealthPanel panel)
        {
            BodyPartView[] views = { panel._head, panel._chest, panel._stomach, panel._leftArm, panel._rightArm, panel._leftLeg, panel._rightLeg };
            // Start EFT's own fill animations now; full numeric HP alone does not mean
            // the displayed bars have finished moving.
            foreach (BodyPartView view in views)
                view.Update();
            yield return null;

            while (panel != null && panel == HealthPreviewPanel && IsHealthPreviewFull(health)
                && views.Any(view => view != null && view._healthBar != null && view._healthBar.fillAmount < 1f))
                yield return null;

            HealthPreviewHealingCoroutine = null;
            if (panel == null || panel != HealthPreviewPanel || HealthPreviewFooter == null)
                yield break;

            if (IsHealthPreviewFull(health) && views.All(view => view != null && view._healthBar != null && view._healthBar.fillAmount >= 1f))
            {
                panel.gameObject.SetActive(false);
                HealthPreviewFooter.gameObject.SetActive(false);
            }
            else if (HealthPreviewHealButton != null)
                HealthPreviewHealButton.Interactable = true;
        }

        private static bool IsHealthPreviewFull(Profile.HealthInfo health)
        {
            return EFT.HealthSystem.HealthHelper.RealBodyParts.All(part =>
                health.BodyParts.TryGetValue(part, out Profile.HealthInfo.BodyPartInfo value)
                && value?.Health != null && value.Health.Current >= value.Health.Maximum);
        }

        private static Bounds HealthPreviewTextBounds(RectTransform host, TMPro.TMP_Text text)
        {
            text.ForceMeshUpdate();
            Bounds bounds = text.textBounds;
            Vector3 min = host.InverseTransformPoint(text.transform.TransformPoint(bounds.min));
            Vector3 max = host.InverseTransformPoint(text.transform.TransformPoint(bounds.max));
            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static Bounds HealthPreviewRectBounds(RectTransform host, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Bounds bounds = new Bounds(host.InverseTransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; i++)
                bounds.Encapsulate(host.InverseTransformPoint(corners[i]));
            return bounds;
        }

        private static RectTransform ResolveHealthPreviewImageRect(PlayerModelView modelView)
        {
            // PlayerMVObject is the 3D host. PMCPlayerMV is its ancestor RawImage,
            // and the native PMCs/Button/Health Panel is positioned over that image.
            return modelView == null ? null : modelView.GetComponentsInParent<RawImage>(true)
                .FirstOrDefault()?.rectTransform;
        }

        internal static void CloseHealthPreview()
        {
            HealthPreviewGeneration++;
            HealthPreviewRecoveryPerMinute = 0f;
            if (HealthPreviewCoroutine != null && pitFireTeam.Instance != null)
                pitFireTeam.Instance.StopCoroutine(HealthPreviewCoroutine);
            HealthPreviewCoroutine = null;
            if (HealthPreviewHealingCoroutine != null && pitFireTeam.Instance != null)
                pitFireTeam.Instance.StopCoroutine(HealthPreviewHealingCoroutine);
            HealthPreviewHealingCoroutine = null;
            if (HealthPreviewHealButton != null)
            {
                HealthPreviewHealButton.OnClick.RemoveAllListeners();
                HealthPreviewHealButton = null;
            }
            if (HealthPreviewFooter != null)
            {
                HealthPreviewFooter.gameObject.SetActive(false);
                GameObject.Destroy(HealthPreviewFooter.gameObject);
                HealthPreviewFooter = null;
            }
            if (HealthPreviewPanel != null)
            {
                HealthPreviewPanel.Close();
                GameObject.Destroy(HealthPreviewPanel.gameObject);
                HealthPreviewPanel = null;
            }
        }
    }
}
