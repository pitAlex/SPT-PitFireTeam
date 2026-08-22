using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ResultProfile = EFT.OtherPlayerProfile;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void DisplaySkillsPanel(OtherPlayerProfileScreen screen, ResultProfile profile, EFT.IEftSession session)
        {
            if (screen == null || profile?.Skills == null || session?.Profile == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Skills panel skipped: missing screen, profile skills, or session profile.");
                return;
            }

            SkillsScreen template = FindSkillsScreenTemplate();
            if (template == null || !TryPrepareSkillsHost(screen, out RectTransform hostParent))
            {
                pitFireTeam.Log.LogWarning($"[UI] Skills panel skipped: template={(template != null)}.");
                return;
            }

            if (SkillsPanel != null)
            {
                GameObject.Destroy(SkillsPanel.gameObject);
                SkillsPanel = null;
            }

            if (SkillsPanelHost != null)
            {
                GameObject.Destroy(SkillsPanelHost.gameObject);
                SkillsPanelHost = null;
            }

            GameObject hostObject = new GameObject("pitFireTeam_ProfileSkillsHost", typeof(RectTransform));
            hostObject.transform.SetParent(hostParent, false);
            RectTransform hostRect = hostObject.GetComponent<RectTransform>();
            StretchToFillParent(hostRect);
            hostRect.SetAsLastSibling();

            SkillsScreen clone = GameObject.Instantiate(template, hostRect, false);
            clone.name = "pitFireTeam_ProfileSkillsScreen";
            if (clone.transform is RectTransform cloneRect)
            {
                ConfigureInjectedSkillsScreenRect(screen, cloneRect);
            }

            Profile skillsProfile = BuildSkillsProfile(session.Profile, profile.Skills);

            if (!TryInitializeSkillsScreen(clone))
            {
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }

            object healthController = ResolveSkillsHealthController(profile, session);
            if (healthController == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Skills panel skipped: unable to resolve any health controller.");
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }

            try
            {
                SkillsScreenShowMethod?.Invoke(clone, new object[] { skillsProfile, healthController });
                HideDetailedSkillProgressChildren(clone.transform);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to show follower skills panel.");
                Modules.Logger.LogError(ex);
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }

            EFT.UI.UIParent ui = UiField?.GetValue(screen) as EFT.UI.UIParent;
            ui?.AddDisposable(clone);

            SkillsPanelHost = hostRect;
            SkillsPanel = clone;
            pitFireTeam.Log.LogWarning($"[UI] Follower skills panel shown for '{profile.AccountId}'.");
        }

        private static Profile BuildSkillsProfile(Profile sessionProfile, SkillManager sourceSkills)
        {
            Profile skillsProfile = sessionProfile?.Clone();
            if (skillsProfile?.Skills == null || sourceSkills == null)
            {
                return skillsProfile;
            }

            skillsProfile.Skills.ApplyChanges(sourceSkills);
            FilterHiddenSkills(skillsProfile.Skills);
            return skillsProfile;
        }

        private static void FilterHiddenSkills(SkillManager skillManager)
        {
            if (skillManager == null)
            {
                return;
            }

            ReplaceSkillArray(SkillManagerDisplayListField, skillManager);
            ReplaceSkillArray(SkillManagerSkillsField, skillManager);
        }

        private static void ReplaceSkillArray(FieldInfo field, SkillManager skillManager)
        {
            if (field?.GetValue(skillManager) is not EFT.Skill[] skills)
            {
                return;
            }

            EFT.Skill[] filtered = skills
                .Where(skill => skill != null && !skill.Locked && !HiddenFollowerSkills.Contains(skill.Id))
                .ToArray();

            field.SetValue(skillManager, filtered);
        }

        private static void HideDetailedSkillProgressChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == "Progress")
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static object ResolveSkillsHealthController(ResultProfile profile, EFT.IEftSession session)
        {
            try
            {
                if (session?.Profile?.Health != null)
                {
                    return new ProfileSkillsHealthController(session.Profile.Health);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to build session-profile health controller for skills panel.");
                Modules.Logger.LogError(ex);
            }

            try
            {
                return Singleton<GameWorld>.Instantiated
                    ? (object)Singleton<GameWorld>.Instance?.MainPlayer?.ActiveHealthController
                    : null;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to resolve live health controller fallback for skills panel.");
                Modules.Logger.LogError(ex);
                return null;
            }
        }

        private static bool TryInitializeSkillsScreen(SkillsScreen skillsScreen)
        {
            if (skillsScreen == null)
            {
                return false;
            }

            if (SkillsScreenTabsControllerField?.GetValue(skillsScreen) != null)
            {
                return true;
            }

            try
            {
                AccessTools.Method(typeof(SkillsScreen), "Awake")?.Invoke(skillsScreen, null);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to initialize stock SkillsScreen clone.");
                Modules.Logger.LogError(ex);
                return false;
            }

            return SkillsScreenTabsControllerField?.GetValue(skillsScreen) != null;
        }

        private static SkillsScreen FindSkillsScreenTemplate()
        {
            SkillsScreen direct = Resources.FindObjectsOfTypeAll<SkillsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    screen.name != "pitFireTeam_ProfileSkillsScreen" &&
                    screen.transform is RectTransform);
            if (direct != null)
            {
                return direct;
            }

            SkillsAndMasteringScreen skillsAndMastering = Resources.FindObjectsOfTypeAll<SkillsAndMasteringScreen>()
                .FirstOrDefault(screen => screen != null);
            SkillsScreen fromSkillsAndMastering = SkillsAndMasteringSkillsScreenField?.GetValue(skillsAndMastering) as SkillsScreen;
            if (fromSkillsAndMastering?.transform is RectTransform)
            {
                return fromSkillsAndMastering;
            }

            InventoryScreen inventoryScreen = Resources.FindObjectsOfTypeAll<InventoryScreen>()
                .FirstOrDefault(screen => screen != null);
            SkillsAndMasteringScreen inventorySkillsAndMastering = InventorySkillsAndMasteringScreenField?.GetValue(inventoryScreen) as SkillsAndMasteringScreen;
            SkillsScreen fromInventory = SkillsAndMasteringSkillsScreenField?.GetValue(inventorySkillsAndMastering) as SkillsScreen;
            if (fromInventory?.transform is RectTransform)
            {
                return fromInventory;
            }

            pitFireTeam.Log.LogWarning("[UI] Unable to locate a stock SkillsScreen template.");
            return null;
        }

        private static bool TryPrepareSkillsHost(OtherPlayerProfileScreen screen, out RectTransform hostParent)
        {
            hostParent = null;
            if (screen == null)
            {
                return false;
            }

            Transform rightSide = screen.transform.Find("RightSide")
                ?? FindChildRecursive(screen.transform, "RightSide");

            rightSide?.gameObject.SetActive(true);
            hostParent = rightSide as RectTransform;
            return hostParent != null;
        }

        private static void EnsureSkillsScreenOptionsVisible(SkillsScreen skillsScreen)
        {
            if (skillsScreen == null)
            {
                return;
            }

            Transform options = skillsScreen.transform.Find("Options")
                ?? FindChildRecursive(skillsScreen.transform, "Options");
            if (options == null)
            {
                return;
            }

            options.gameObject.SetActive(true);
            if (options is RectTransform optionsRect)
            {
                optionsRect.anchorMin = new Vector2(0f, 1f);
                optionsRect.anchorMax = new Vector2(1f, 1f);
                optionsRect.pivot = new Vector2(0.5f, 1f);
                optionsRect.localScale = Vector3.one;
            }
        }

        private static RectTransform GetSkillsAnchorRect(OtherPlayerProfileScreen screen)
        {
            GameObject[] targets =
            [
                ResolveProfileSectionRoot(screen.transform, (OverallStatsPanelField?.GetValue(screen) as Component)?.transform),
                ResolveProfileSectionRoot(screen.transform, (AchievementsProgressBlockField?.GetValue(screen) as Component)?.transform),
                ResolveProfileSectionRoot(screen.transform, (WeaponsGridLayoutGroupField?.GetValue(screen) as Component)?.transform),
            ];

            foreach (GameObject target in targets)
            {
                if (target?.transform is RectTransform rect)
                {
                    return rect;
                }
            }

            return null;
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.offsetMin = source.offsetMin;
            target.offsetMax = source.offsetMax;
            target.localScale = source.localScale;
            target.localRotation = source.localRotation;
        }

        private static void StretchToFillParent(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = new Vector2(0f, 0f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = Vector2.zero;
            target.sizeDelta = Vector2.zero;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }

        private static void ConfigureInjectedSkillsScreenRect(OtherPlayerProfileScreen screen, RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            float referenceHeight = ResolveReferencePanelHeight(screen);
            float calculatedHeight = referenceHeight > 0f
                ? referenceHeight + SkillsScreenOffset.y
                : target.rect.height + SkillsScreenOffset.y;

            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = SkillsScreenOffset * ResolveUiScaleCompensation(target);
            target.sizeDelta = new Vector2(0f, calculatedHeight);
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }

        private static float ResolveReferencePanelHeight(OtherPlayerProfileScreen screen)
        {
            if (screen == null)
            {
                return 0f;
            }

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                PlayerModelWindowField?.GetValue(screen) as InventoryPlayerModelWithStatsWindow;

            if (TryGetClothingPanel(screen, playerModelWindow, out RectTransform clothingPanel, out _, out Transform parent))
            {
                if (parent is RectTransform parentRect && parentRect.rect.height > 0f)
                {
                    return parentRect.rect.height;
                }

                if (clothingPanel != null && clothingPanel.rect.height > 0f)
                {
                    return clothingPanel.rect.height;
                }
            }

            if (playerModelWindow?.transform is RectTransform playerModelRect && playerModelRect.rect.height > 0f)
            {
                return playerModelRect.rect.height;
            }

            Transform playerModelRoot = screen.transform.Find("PlayerModelWithStats")
                ?? FindChildRecursive(screen.transform, "PlayerModelWithStats");
            if (playerModelRoot is RectTransform rootRect && rootRect.rect.height > 0f)
            {
                return rootRect.rect.height;
            }

            return 0f;
        }
    }
}
