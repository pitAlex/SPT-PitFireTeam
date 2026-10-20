using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace pitTeam.Patches
{
    internal sealed class GiveGearPanelPatch : ModulePatch
    {
        private static readonly Dictionary<SimpleStashPanel, Action> Cleanup = new Dictionary<SimpleStashPanel, Action>();
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Show));
        [PatchPostfix]
        private static void Postfix(SimpleStashPanel __instance, CompoundItem item)
        {
            var session = TeammateGearSwap.Current;
            if (session?.Giving != true || !ReferenceEquals(session.Donation, item)) return;
            try
            {
                Restore(__instance);
                __instance._containerName.text = session.Bot.Profile.Nickname;
                __instance._containerNamePanel.SetActive(true);
                var header = (RectTransform)__instance._containerNamePanel.transform;
                var nameRect = __instance._containerName.rectTransform;
                var wrapper = new GameObject("pitFireTeam_GiveItems", typeof(RectTransform), typeof(LayoutElement));
                wrapper.transform.SetParent(header.parent, false);
                wrapper.GetComponent<LayoutElement>().ignoreLayout = true;
                var rect = (RectTransform)wrapper.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                Canvas.WillRenderCanvases positionButton = null;
                Cleanup.Add(__instance, () =>
                {
                    if (positionButton != null) Canvas.willRenderCanvases -= positionButton;
                    if (wrapper != null) UnityEngine.Object.Destroy(wrapper);
                });
                var template = CommonUI.Instance.InventoryScreen._backButton;
                var button = UnityEngine.Object.Instantiate(template, rect, false);
                button.name = "GiveItemsButton";
                button.OnClick.RemoveAllListeners();
                button.SetIcon(null);
                button.SetRawText(pitFireTeam.GetSocialUiText("GiveGearApply"), Mathf.RoundToInt(__instance._containerName.fontSize));
                button._headerLabel.fontStyle |= FontStyles.Bold;
                rect.sizeDelta = new Vector2(Mathf.Max(160f, button._headerLabel.preferredWidth + 48f),
                    Mathf.Max(30f, __instance._containerName.fontSize + 12f));
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.one;
                buttonRect.offsetMin = buttonRect.offsetMax = Vector2.zero;
                buttonRect.localScale = Vector3.one;
                // Match Apply Changes' header-row center, not the small left LOOT label.
                // Reapply after native layout, without joining its horizontal layout group.
                positionButton = () =>
                {
                    if (rect == null || nameRect == null || header == null) return;
                    Vector3 nameCenter = nameRect.TransformPoint(nameRect.rect.center);
                    var row = (RectTransform)header.parent;
                    Vector3 center = row.TransformPoint(row.rect.center);
                    rect.position = new Vector3(center.x, nameCenter.y, nameCenter.z);
                };
                Canvas.willRenderCanvases += positionButton;
                positionButton();
                button.gameObject.SetActive(true);
                button.Interactable = true;
                button._onPointerEnterSound = button._onPointerClickSound = true;
                GearSwapPanelPatch.CreateTransferFeedback(__instance, rect, button._headerLabel, out var progress, out var notice);
                button.OnClick.AddListener(async () =>
                {
                    if (session.Applying) return;
                    button.Interactable = false;
                    button.gameObject.SetActive(false);
                    progress.SetActive(true);
                    notice.SetActive(true);
                    await session.Apply();
                    if (button != null && ReferenceEquals(TeammateGearSwap.Current, session))
                    {
                        progress.SetActive(false);
                        notice.SetActive(false);
                        button.gameObject.SetActive(true);
                        button.Interactable = true;
                    }
                });
            }
            catch (Exception ex)
            {
                pitTeam.Modules.Logger.LogError("[GiveGear] Panel creation failed: " + ex);
                Restore(__instance);
                session.Close();
            }
        }

        internal static void Restore(SimpleStashPanel panel)
        {
            if (!Cleanup.TryGetValue(panel, out var cleanup)) return;
            Cleanup.Remove(panel);
            cleanup();
        }
    }

    internal sealed class GiveGearPanelClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SimpleStashPanel), nameof(SimpleStashPanel.Close));
        [PatchPrefix]
        private static void Prefix(SimpleStashPanel __instance) => GiveGearPanelPatch.Restore(__instance);
    }
}
