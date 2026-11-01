using Comfort.Common;
using EFT;
using EFT.Customization;
using EFT.UI;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

namespace pitTeam.Patches
{
    // The row owns its subscription and the dropdown's detached popup, including on rebuild.
    internal sealed class ProfileVoiceDropdownLifetime : MonoBehaviour
    {
        public UIParent Disposables { get; } = new UIParent();

        private void OnDestroy() => Disposables.Dispose();
    }

    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void ConfigureVoiceDropdown(OtherPlayerProfileScreen screen, RectTransform row,
            DefaultUIButton nameButton, OtherPlayerProfile profile)
        {
            try
            {
                CustomizationSolver solver = Singleton<CustomizationSolver>.Instance;
                if (solver == null || !profile.Customization.TryGetValue(EBodyModelPart.Voice, out MongoID currentVoice))
                {
                    return;
                }

                // Use all loaded voice templates, including Scav/boss and unrestricted voices.
                var voices = solver._voices.Values.Where(voice => voice != null).OrderBy(voice => voice.Name).ToList();

                if (voices.Count == 0)
                {
                    return;
                }

                InventoryClothingSelectionPanel panel = row.GetComponent<InventoryClothingSelectionPanel>();
                DropDownBox dropdown = LowerDropdownField?.GetValue(panel) as DropDownBox;
                DropDownBox upperDropdown = UpperDropdownField?.GetValue(panel) as DropDownBox;
                if (dropdown == null || upperDropdown?.transform is not RectTransform leftRect)
                {
                    return;
                }

                int appliedIndex = voices.FindIndex(voice => voice.Id == currentVoice);
                string[] labels = voices.Select(voice => voice.NameLocalizationKey.Localized(null)).ToArray();
                // Unknown legacy voices must not be silently changed just by opening the screen.
                if (appliedIndex < 0)
                {
                    voices.Insert(0, null);
                    labels = new[] { currentVoice.ToString() }.Concat(labels).ToArray();
                    appliedIndex = 0;
                }

                Transform left = row.Find("Upper");
                foreach (Transform child in left)
                {
                    child.gameObject.SetActive(false);
                }
                left.gameObject.SetActive(true);
                nameButton.transform.SetParent(left, false);
                RectTransform buttonRect = (RectTransform)nameButton.transform;
                buttonRect.anchorMin = leftRect.anchorMin;
                buttonRect.anchorMax = leftRect.anchorMax;
                buttonRect.pivot = leftRect.pivot;
                buttonRect.sizeDelta = leftRect.sizeDelta;
                buttonRect.anchoredPosition = leftRect.anchoredPosition;
                buttonRect.localScale = Vector3.one;

                row.Find("Lower")?.gameObject.SetActive(true);
                ReplaceDropdownIcon(row, "Lower/Icon", Path.Combine(PluginDirectory, "voice.png"));
                Image voiceIcon = row.Find("Lower/Icon")?.GetComponent<Image>();
                if (voiceIcon != null)
                {
                    // Reuse the native icon frame, tint and aspect handling of the other selectors.
                    Image tacticIcon = LoadoutSelector?.Find("Lower/Icon")?.GetComponent<Image>();
                    if (tacticIcon != null) voiceIcon.color = tacticIcon.color;
                    voiceIcon.raycastTarget = false;
                }
                dropdown.gameObject.SetActive(true);
                dropdown.Show(labels);
                dropdown.UpdateValue(appliedIndex, false);
                dropdown.Interactable = true;
                var lifetime = row.gameObject.AddComponent<ProfileVoiceDropdownLifetime>();
                lifetime.Disposables.AddDisposable(dropdown);
                lifetime.Disposables.SubscribeEvent<int>(dropdown.OnValueChanged, index =>
                {
                    if (row == null || !row.gameObject.activeInHierarchy || ActiveProfileScreen != screen
                        || index < 0 || index >= voices.Count || index == appliedIndex)
                    {
                        return;
                    }

                    if (voices[index] == null)
                    {
                        dropdown.UpdateValue(appliedIndex, false);
                        return;
                    }

                    CustomizationPlayerVoice selected = voices[index];
                    dropdown.Interactable = false;
                    try
                    {
                        string response = RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/profile/voice",
                            SerializeBody(new { aid = profile.AccountId, voice = selected.Id.ToString() }));
                        // A missing/malformed response is not a confirmed save.
                        if (string.IsNullOrWhiteSpace(response) || (int?)JObject.Parse(response)["err"] != 0)
                        {
                            throw new InvalidOperationException("Teammate voice save was not confirmed.");
                        }
                        profile.Customization[EBodyModelPart.Voice] = selected.Id;
                        appliedIndex = index;
                        MarkSquadRosterDirty(profile.AccountId);
                    }
                    catch (Exception ex)
                    {
                        dropdown.UpdateValue(appliedIndex, false);
                        Modules.Logger.LogError("[UI] Failed to persist teammate voice change.");
                        Modules.Logger.LogError(ex);
                        EFT.Communications.NotificationManager.DisplayWarningNotification(
                            GetSocialUiText("VoiceChangeFailed"), EFT.Communications.ENotificationDurationType.Default);
                        return;
                    }
                    finally
                    {
                        if (dropdown != null) dropdown.Interactable = true;
                    }

                    // Preview failure must not undo a confirmed save.
                    new VoicePreviewSpeaker().PlayVoice(selected.Name).HandleExceptions();
                });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to create teammate voice dropdown.");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
