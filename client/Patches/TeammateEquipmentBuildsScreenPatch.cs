using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Builds;
using EFT.UI.DragAndDrop;
using EFT.UI.Health;
using EFT.UI.Ragfair;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ResultProfile = EFT.OtherPlayerProfile;

namespace pitTeam.Patches
{
    internal static class TeammateEquipmentBuildsScreenFlow
    {
        private static readonly FieldInfo ButtonRawTextField = AccessTools.Field(typeof(DefaultUIButton), "_rawText");
        private static readonly FieldInfo BuildListWeightField = AccessTools.Field(typeof(EquipmentBuildListView), "_weight");
        private static readonly FieldInfo ScreenSelectedBuildField = AccessTools.Field(typeof(EquipmentBuildsScreen), "_currentBuild");
        private static readonly FieldInfo ScreenWeightField = AccessTools.Field(typeof(EquipmentBuildsScreen), "_weight");
        private static readonly FieldInfo EditBuildOnlyAvailableToggleField = AccessTools.Field(typeof(EditBuildScreen), "_onlyAvailableToggle");
        private const string BuyKitRoute = "/singleplayer/pitfireteam/teammate/profile/buy-kit";
        private const float MarketPricesRefreshIntervalSeconds = 300f;
        private const double PrimaryWeaponBaseDiscount = 0.10;
        private const double PrimaryWeaponArmorDiscount = 0.10;
        private const double PrimaryWeaponHelmetDiscount = 0.10;
        private const double PrimaryWeaponSupplyDiscount = 0.10;
        private const double PrimaryWeaponMaxDiscount = 0.40;
        private const double SecondaryWeaponBaseDiscount = 0.10;
        private const double SecondaryWeaponSupplyDiscount = 0.15;
        private const double SecondaryWeaponMaxDiscount = 0.25;
        private const double PistolBaseDiscount = 0.20;
        private const double PistolSupplyDiscount = 0.20;
        private const double PistolMaxDiscount = 0.40;
        private const double ArmorPlateSystemMaxDiscount = 0.20;
        private const double RagfairWeaponOfferSimilarityThreshold = 0.80;
        private const double RagfairWeaponConditionTolerancePercent = 0.5;
        private const int RagfairWeaponOfferPageSize = 50;
        private const double DiscountComparisonTolerance = 0.000001;
        private static bool DisableKitLoadoutDiscountPricing = false;
        private static bool EnableKitLoadoutPricingDiagnostics => pitFireTeam.IsDebugBuild;

        private static string _accountId;
        private static ResultProfile _profile;
        private static EFT.IEftSession _session;
        private static EFT.InventoryLogic.BackEndInventoryController _backendInventoryController;
        private static EquipmentBuildsScreen _activeScreen;
        private static Action _profileBackOverrideAction;
        private static bool _openingFromProfile;
        private static bool _returningToProfile;
        private static bool _customizingBuildsScreen;
        private static bool _marketPricesRequestInFlight;
        private static bool _excludeExistingItems;
        private static float _marketPricesUpdatedAt = -MarketPricesRefreshIntervalSeconds;
        private static Dictionary<string, float> _marketPrices;
        private static Dictionary<string, int> _selectedBuildMissingCounts = new Dictionary<string, int>();
        private static List<Item> _selectedBuildMissingItems = new List<Item>();
        private static Sprite _roublesSprite;
        private static ScreenChromeState _screenChromeState;
        private static GameObject _buyConfirmOverlay;
        private static int _buyConfirmOverlayGeneration;
        private static readonly List<Action> BuyConfirmIconBindings = new List<Action>();
        private static readonly MarketPriceSource BuildMarketPriceSource = new MarketPriceSource();
        private static readonly Dictionary<int, BuildRowState> BuildRowStates = new Dictionary<int, BuildRowState>();
        private static readonly Dictionary<string, TraderWeaponOfferPricingPlan> TraderWeaponOfferPricingPlanCache = new Dictionary<string, TraderWeaponOfferPricingPlan>(StringComparer.Ordinal);
        private static readonly Dictionary<string, RagfairWeaponOfferPricingPlan> RagfairWeaponOfferPricingPlanCache = new Dictionary<string, RagfairWeaponOfferPricingPlan>(StringComparer.Ordinal);
        private static readonly HashSet<string> PendingRagfairWeaponOfferPricingPlanKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedBuildPricingKeys = new HashSet<string>(StringComparer.Ordinal);
        private static string _buildPricingLogPhase = "initial";

        public static bool IsActive => !string.IsNullOrWhiteSpace(_accountId);
        public static bool ShouldCustomizeBuildsScreen => _customizingBuildsScreen && IsActive;

        public static bool ShouldSuppressSearchInteraction(EItemInfoButton button)
        {
            return ShouldCustomizeBuildsScreen
                && (button == EItemInfoButton.EditBuild
                    || button == EItemInfoButton.FilterSearch
                    || button == EItemInfoButton.LinkedSearch
                    || button == EItemInfoButton.NeededSearch);
        }

        private static string GetSocialUiText(string key)
        {
            return pitFireTeam.GetSocialUiText(key);
        }

        public static void CaptureAndSuppressMissingItems(ref IEnumerable<Item> notFoundItems)
        {
            if (!ShouldCustomizeBuildsScreen || notFoundItems == null)
            {
                return;
            }

            List<Item> missingItems = new List<Item>(notFoundItems);
            _selectedBuildMissingItems = missingItems;
            _selectedBuildMissingCounts = SummarizeMissingItems(missingItems);

            // Stock build preview paints missing build items red by feeding this list
            // into GClass3452. The teammate buy screen needs the same availability
            // data for later purchase math, but should not present the stock "you
            // cannot equip this now" warning state.
            notFoundItems = Array.Empty<Item>();
        }

        public static void Open(ResultProfile profile, EFT.IEftSession session, InventoryController inventoryController)
        {
            if (profile == null || session == null || inventoryController == null)
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification(GetSocialUiText("KitLoadoutsOpenFailed"), ENotificationDurationType.Default);
                pitFireTeam.Log.LogWarning("[UI] Buy loadout screen aborted: missing teammate profile, session, or inventory controller.");
                return;
            }

            if (!TryResolveBackendController(session, inventoryController, out EFT.InventoryLogic.BackEndInventoryController backendController))
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification(GetSocialUiText("KitLoadoutsOpenFailed"), ENotificationDurationType.Default);
                pitFireTeam.Log.LogWarning($"[UI] Buy loadout screen aborted: expected backend inventory controller, got '{inventoryController.GetType().Name}'.");
                return;
            }

            _accountId = profile.AccountId;
            _profile = profile;
            _session = session;
            _profileBackOverrideAction = OtherPlayerProfileScreenPatch.ActiveBackOverrideAction;
            _openingFromProfile = true;
            _returningToProfile = false;
            _customizingBuildsScreen = true;
            _excludeExistingItems = false;
            TraderWeaponOfferPricingPlanCache.Clear();
            RagfairWeaponOfferPricingPlanCache.Clear();
            PendingRagfairWeaponOfferPricingPlanKeys.Clear();
            LoggedBuildPricingKeys.Clear();
            _buildPricingLogPhase = _marketPrices != null ? "cached-market" : "initial";

            // Buy mode is a scoped overlay on top of EFT's stock builds screen. The
            // later patches use this state to decide whether they should behave like
            // stock EFT or like the teammate shop.
            OtherPlayerProfileScreenPatch.DisableMenuTaskBarForReturnOverride();
            RequestMarketPrices(session, forceRefresh: false);

            IHealthController healthController = OtherPlayerProfileScreenPatch.CreateProfileHealthController(session.Profile?.Health);
            new EquipmentBuildsScreen.EquipmentBuildsScreenController(session, backendController, healthController, backendController.Inventory.Equipment)
                .ShowScreen(EScreenState.Queued);

            if (EnableKitLoadoutPricingDiagnostics)
            {
                Modules.Logger.LogInfo($"[UI] Opening teammate equipment builds screen for '{profile.AccountId}'. Kit pricing diagnostics phase='{_buildPricingLogPhase}', marketPricesLoaded={_marketPrices != null}.");
            }
        }

        public static bool ConsumeProfileTransition(Action profileBackOverrideAction)
        {
            if (!_openingFromProfile)
            {
                return false;
            }

            _openingFromProfile = false;
            _profileBackOverrideAction = profileBackOverrideAction ?? _profileBackOverrideAction;
            return true;
        }

        public static bool TryBuildTeammateVisual(EquipmentBuildsScreen screen, EFT.PlayerVisualRepresentation currentVisual, out EFT.PlayerVisualRepresentation teammateVisual)
        {
            teammateVisual = null;
            if (!IsActive || screen == null || _profile == null || currentVisual == null)
            {
                return false;
            }

            teammateVisual = new EFT.PlayerVisualRepresentation(new JsonType.PlayerInfo
            {
                Level = EFT.ProfileInfo.GetLevel(_profile.Info.Experience),
                MemberCategory = _profile.Info.MemberCategory,
                Nickname = _profile.Info.Nickname,
                PrestigeLevel = _profile.Info.PrestigeLevel,
                SelectedMemberCategory = _profile.Info.SelectedMemberCategory,
                Side = _profile.Info.Side
            }, _profile.Customization, currentVisual.Equipment);
            return true;
        }

        public static void ApplyScreenChrome(EquipmentBuildsScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            if (!ShouldCustomizeBuildsScreen)
            {
                // The player character screen also uses EquipmentBuildsScreen. Restore
                // only captured teammate-buy state and otherwise leave stock screens
                // untouched.
                RestoreScreenChrome(screen);
                return;
            }

            _activeScreen = screen;
            CaptureScreenChrome(screen);
            HideScreenBottomPanel(screen.transform);
            HideCurrentWeightValue(screen.transform);
            EnsureExcludeExistingItemsToggle(screen.transform);
            ApplyActionButtonText(screen.transform);
            HideCanEquipIcon(screen.transform);
            ApplySelectedBuildPrice(screen);
        }

        public static void ApplyBuildListRow(EquipmentBuildListView view, EFT.UI.BuildWrapper<EFT.UI.Builds.EquipmentBuild> buildWrapper)
        {
            if (view == null)
            {
                return;
            }

            if (!ShouldCustomizeBuildsScreen)
            {
                RestoreBuildListRow(view);
                return;
            }

            CaptureBuildListRow(view);
            RememberBuildListRow(view, buildWrapper?.Build);
            HideChild(view.transform, "DeleteHolder");
            ApplyRoublesIcon(view.transform);
            ApplyBuildListPrice(view, buildWrapper);
        }

        public static bool HandleBackToProfile()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return false;
            }

            ReturnToProfile();
            return true;
        }

        public static bool HandleBuyRequest()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return false;
            }

            if (!TryCreateBuyQuote(out EquipmentBuildBuyQuote quote))
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification(GetSocialUiText("KitLoadoutPriceFailed"), ENotificationDurationType.Default);
                return true;
            }

            if (!CanAffordBuyQuote(quote))
            {
                ShowNotEnoughResourcesOverlay(quote);
                return true;
            }

            ShowBuyConfirmOverlay(quote);
            return true;
        }

        public static void FinishReturnIfMatches(string accountId)
        {
            if (!_returningToProfile || string.IsNullOrWhiteSpace(accountId) || !string.Equals(accountId, _accountId, StringComparison.Ordinal))
            {
                return;
            }

            ClearReturnState();
        }

        private static bool TryResolveBackendController(EFT.IEftSession session, InventoryController inventoryController, out EFT.InventoryLogic.BackEndInventoryController backendController)
        {
            backendController = inventoryController as EFT.InventoryLogic.BackEndInventoryController;
            if (backendController != null)
            {
                _backendInventoryController = backendController;
                return true;
            }

            if (_backendInventoryController?.Profile != null
                && session?.Profile != null
                && ReferenceEquals(_backendInventoryController.Profile, session.Profile))
            {
                backendController = _backendInventoryController;
                return true;
            }

            return false;
        }

        private static void ReturnToProfile()
        {
            if (_returningToProfile)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_accountId))
            {
                Clear();
                OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
                return;
            }

            _returningToProfile = true;
            OtherPlayerProfileScreenPatch.PrepareReturnOverride(_profileBackOverrideAction);
            ItemUiContext.Instance.ShowPlayerProfileScreen(_accountId, EItemViewType.OtherPlayerProfile)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled || task.Result == null)
                    {
                        pitFireTeam.Log.LogError("[UI] Failed to return from teammate equipment builds screen to teammate profile.");
                        if (task.Exception != null)
                        {
                            pitFireTeam.Log.LogError(task.Exception);
                        }

                        Clear();
                        OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
                    }
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext())
                .HandleExceptions();
        }

        public static void HandleScreenClosed()
        {
            CloseBuyConfirmOverlay();
            RestoreScreenChrome();
            RestoreBuildListRows();

            if (!ShouldCustomizeBuildsScreen)
            {
                return;
            }

            _customizingBuildsScreen = false;
            if (!_returningToProfile)
            {
                ClearReturnState();
                OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
            }
        }

        public static void HideInspectInteractionButtons(ItemSpecificationPanel panel)
        {
            if (!ShouldCustomizeBuildsScreen || panel == null)
            {
                return;
            }

            Transform buttonsPanel = FindChildRecursive(panel.transform, "InteractionButtonsPanel");
            if (buttonsPanel != null)
            {
                buttonsPanel.gameObject.SetActive(false);
            }
        }

        private static void Clear()
        {
            ClearReturnState();
            _customizingBuildsScreen = false;
            _activeScreen = null;
        }

        public static void ClearIfNotOpeningOrReturning()
        {
            if (_openingFromProfile || _returningToProfile)
            {
                return;
            }

            Clear();
        }

        private static void ClearReturnState()
        {
            _accountId = null;
            _profile = null;
            _session = null;
            _profileBackOverrideAction = null;
            _openingFromProfile = false;
            _returningToProfile = false;
            _excludeExistingItems = false;
            _selectedBuildMissingCounts = new Dictionary<string, int>();
            _selectedBuildMissingItems = new List<Item>();
            RagfairWeaponOfferPricingPlanCache.Clear();
            PendingRagfairWeaponOfferPricingPlanKeys.Clear();
            LoggedBuildPricingKeys.Clear();
            _buildPricingLogPhase = "initial";
        }

        private static void CaptureScreenChrome(EquipmentBuildsScreen screen)
        {
            if (_screenChromeState?.Screen == screen)
            {
                return;
            }

            // Capture before hiding/changing anything. EFT reuses this screen for the
            // normal player build flow, so every buy-mode mutation needs a stock value
            // to restore on exit.
            Transform equipButtonTransform = FindEquipButtonTransform(screen.transform);
            DefaultUIButton equipButton = equipButtonTransform?.GetComponent<DefaultUIButton>();
            Transform canEquip = FindCanEquipTransform(screen.transform);

            _screenChromeState = new ScreenChromeState
            {
                Screen = screen,
                EquipButton = equipButton,
                EquipButtonText = equipButton?.HeaderText,
                EquipButtonFontSize = equipButton?.HeaderSize ?? 18,
                EquipButtonRawText = equipButton != null && ButtonRawTextField?.GetValue(equipButton) is bool rawText && rawText,
                EquipButtonLabel = equipButtonTransform?.GetComponentInChildren<TMP_Text>(true),
                EquipButtonLabelText = equipButtonTransform?.GetComponentInChildren<TMP_Text>(true)?.text,
                CanEquip = canEquip,
                CanEquipActive = canEquip?.gameObject.activeSelf,
                ScreenObjects = FindScreenChromeObjects(screen.transform)
            };
        }

        private static void RestoreScreenChrome(EquipmentBuildsScreen screen = null)
        {
            if (_screenChromeState == null)
            {
                return;
            }

            if (screen != null && _screenChromeState.Screen != null && _screenChromeState.Screen != screen)
            {
                return;
            }

            if (_screenChromeState.EquipButton != null)
            {
                _screenChromeState.EquipButton.SetText(
                    _screenChromeState.EquipButtonText ?? "EQUIP",
                    _screenChromeState.EquipButtonFontSize,
                    _screenChromeState.EquipButtonRawText);
            }
            else if (_screenChromeState.EquipButtonLabel != null)
            {
                _screenChromeState.EquipButtonLabel.text = _screenChromeState.EquipButtonLabelText ?? "EQUIP";
            }

            if (_screenChromeState.CanEquip != null && _screenChromeState.CanEquipActive != null)
            {
                _screenChromeState.CanEquip.gameObject.SetActive(_screenChromeState.CanEquipActive.Value);
            }

            if (_screenChromeState.ExcludeExistingItemsToggleRoot != null)
            {
                UnityEngine.Object.Destroy(_screenChromeState.ExcludeExistingItemsToggleRoot);
            }

            if (_screenChromeState.Screen == _activeScreen)
            {
                _activeScreen = null;
            }

            if (_screenChromeState.ScreenObjects != null)
            {
                foreach (KeyValuePair<GameObject, bool> pair in _screenChromeState.ScreenObjects)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetActive(pair.Value);
                    }
                }
            }

            _screenChromeState = null;
        }

        private static void CaptureBuildListRow(EquipmentBuildListView view)
        {
            int instanceId = view.GetInstanceID();
            if (BuildRowStates.ContainsKey(instanceId))
            {
                return;
            }

            Transform deleteHolder = FindChildRecursive(view.transform, "DeleteHolder");
            Image weightIcon = FindChildRecursive(view.transform, "WeightIcon")?.GetComponent<Image>();
            BuildRowStates[instanceId] = new BuildRowState
            {
                DeleteHolder = deleteHolder,
                DeleteHolderActive = deleteHolder?.gameObject.activeSelf,
                WeightIcon = weightIcon,
                WeightIconSprite = weightIcon?.sprite,
                WeightIconEnabled = weightIcon?.enabled,
                WeightIconPreserveAspect = weightIcon?.preserveAspect
            };
        }

        private static void RememberBuildListRow(EquipmentBuildListView view, EFT.UI.Builds.EquipmentBuild build)
        {
            if (view == null)
            {
                return;
            }

            int instanceId = view.GetInstanceID();
            if (BuildRowStates.TryGetValue(instanceId, out BuildRowState state))
            {
                state.View = view;
                state.Build = build;
            }
        }

        private static void RestoreBuildListRow(EquipmentBuildListView view)
        {
            int instanceId = view.GetInstanceID();
            if (!BuildRowStates.TryGetValue(instanceId, out BuildRowState state))
            {
                return;
            }

            RestoreBuildRowState(state);
            BuildRowStates.Remove(instanceId);
        }

        private static void RestoreBuildListRows()
        {
            foreach (BuildRowState state in BuildRowStates.Values)
            {
                RestoreBuildRowState(state);
            }

            BuildRowStates.Clear();
        }

        private static void RestoreBuildRowState(BuildRowState state)
        {
            if (state.DeleteHolder != null && state.DeleteHolderActive != null)
            {
                state.DeleteHolder.gameObject.SetActive(state.DeleteHolderActive.Value);
            }

            if (state.WeightIcon != null)
            {
                state.WeightIcon.sprite = state.WeightIconSprite;
                if (state.WeightIconEnabled != null)
                {
                    state.WeightIcon.enabled = state.WeightIconEnabled.Value;
                }

                if (state.WeightIconPreserveAspect != null)
                {
                    state.WeightIcon.preserveAspect = state.WeightIconPreserveAspect.Value;
                }
            }
        }

        private static void HideScreenBottomPanel(Transform screen)
        {
            HideChild(screen, "BottomPanel");
            HideChild(screen, "Bottom Panel");
            HideChild(screen, "Bottom");
        }

        private static void HideCurrentWeightValue(Transform screen)
        {
            Transform currentWeight = FindCurrentWeightTransform(screen);
            if (currentWeight != null)
            {
                currentWeight.gameObject.SetActive(false);
            }
        }

        private static void EnsureExcludeExistingItemsToggle(Transform screen)
        {
            if (_screenChromeState?.ExcludeExistingItemsToggleRoot != null)
            {
                return;
            }

            Transform buttonTransform = FindEquipButtonTransform(screen);
            RectTransform buttonRect = buttonTransform as RectTransform;
            RectTransform parent = buttonTransform?.parent as RectTransform;
            if (buttonTransform == null || parent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Could not add teammate buy exclude-existing checkbox: missing equip button parent.");
                return;
            }

            Toggle sourceToggle = ResolveEditBuildOnlyAvailableToggleTemplate();
            if (sourceToggle == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Could not add teammate buy exclude-existing checkbox: EditBuildScreen OnlyAvailable toggle template was unavailable.");
                return;
            }

            Toggle clonedToggle = UnityEngine.Object.Instantiate(sourceToggle, parent, false);
            GameObject root = clonedToggle.gameObject;
            root.name = "pitFireTeam_ExcludeExistingItemsToggle";
            root.transform.SetParent(parent, false);
            root.transform.SetSiblingIndex(buttonTransform.GetSiblingIndex() + 1);
            root.SetActive(true);

            RectTransform rootRect = root.GetComponent<RectTransform>() ?? root.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(245f, buttonRect != null && buttonRect.rect.height > 1f ? buttonRect.rect.height : 42f);

            HorizontalOrVerticalLayoutGroup layoutGroup = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            LayoutElement layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.minWidth = 225f;
            layout.preferredWidth = 245f;
            layout.preferredHeight = rootRect.sizeDelta.y;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            if (layoutGroup == null && buttonRect != null)
            {
                rootRect.anchorMin = buttonRect.anchorMin;
                rootRect.anchorMax = buttonRect.anchorMax;
                rootRect.pivot = buttonRect.pivot;
                rootRect.anchoredPosition = buttonRect.anchoredPosition + new Vector2(265f, 0f);
            }

            ConfigureClonedExcludeExistingToggle(clonedToggle);
            clonedToggle.SetIsOnWithoutNotify(false);
            clonedToggle.onValueChanged.AddListener(isOn =>
            {
                _excludeExistingItems = isOn;
                Modules.Logger.LogInfo($"[UI] Teammate buy exclude existing items changed: {isOn}");
                ApplyActionButtonText(screen);
            });

            _screenChromeState.ExcludeExistingItemsToggleRoot = root;
        }

        private static bool TryCreateBuyQuote(out EquipmentBuildBuyQuote quote)
        {
            quote = null;
            EFT.UI.Builds.EquipmentBuild selectedBuild = ScreenSelectedBuildField?.GetValue(_activeScreen) as EFT.UI.Builds.EquipmentBuild;
            if (selectedBuild?.Equipment == null)
            {
                return false;
            }

            KitLoadoutPricingContext pricingContext = CreateKitLoadoutPricingContext(selectedBuild.Equipment);
            int fullKitPrice = CalculateBuildMarketRoublePrice(selectedBuild.Equipment, pricingContext);
            StashOnlyExclusionPlan exclusionPlan = _excludeExistingItems
                ? CreateStashOnlyExclusionPlan(selectedBuild.Equipment, fullKitPrice, pricingContext)
                : StashOnlyExclusionPlan.Empty(fullKitPrice);

            quote = new EquipmentBuildBuyQuote
            {
                Build = selectedBuild,
                BuildName = string.IsNullOrWhiteSpace(selectedBuild.Name) ? "Selected loadout" : selectedBuild.Name,
                FullKitPrice = fullKitPrice,
                MissingOnlyPrice = exclusionPlan.FinalPrice,
                FinalPrice = exclusionPlan.FinalPrice,
                ExcludeExistingItems = _excludeExistingItems,
                CanEquipFromStash = _excludeExistingItems && exclusionPlan.HasAllRequiredItems,
                MissingTemplateCounts = new Dictionary<string, int>(_selectedBuildMissingCounts),
                AvailableStashItems = exclusionPlan.UsedItems,
                BasePurchasedItems = exclusionPlan.PurchasedItems,
                UsedStashItems = exclusionPlan.UsedItems,
                PurchasedItems = exclusionPlan.PurchasedItems
            };
            return true;
        }

        private static bool CanAffordBuyQuote(EquipmentBuildBuyQuote quote)
        {
            if (quote == null)
            {
                return false;
            }

            if (quote.FinalPrice > GetAvailableStashRoubles())
            {
                return false;
            }

            return !quote.ExcludeExistingItems || HasRequiredStashItems(quote.UsedStashItems);
        }

        private static int GetAvailableStashRoubles()
        {
            Inventory inventory = _backendInventoryController?.Inventory;
            CompoundItem stash = inventory?.Stash;
            if (stash == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Item item in CollectDeepItemTree(stash))
            {
                if (item == null || ReferenceEquals(item, stash))
                {
                    continue;
                }

                if (EFT.InventoryLogic.CurrencyUtil.TryGetCurrencyType(new MongoID?(item.TemplateId), out ECurrencyType currencyType)
                    && currencyType == ECurrencyType.RUB
                    && !IsLockedForStashUse(item))
                {
                    total += Mathf.Max(1, item.StackObjectsCount);
                }
            }

            return total;
        }

        private static bool HasRequiredStashItems(IEnumerable<UsedStashItemSummary> requiredItems)
        {
            if (requiredItems == null)
            {
                return true;
            }

            List<UsedStashItemSummary> requestedItems = requiredItems
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.TemplateId) && item.Count > 0)
                .ToList();
            Dictionary<string, int> requestedCounts = requestedItems
                .GroupBy(item => item.TemplateId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Count), StringComparer.Ordinal);
            List<FriendlyTeammateBuyKitUsedItem> exactSelections =
                CreateSafeExactStashSelections(requestedCounts, out Dictionary<string, int> fulfilledCounts);

            foreach (KeyValuePair<string, int> requested in requestedCounts)
            {
                if (!fulfilledCounts.TryGetValue(requested.Key, out int fulfilled) || fulfilled != requested.Value)
                {
                    return false;
                }
            }

            foreach (UsedStashItemSummary requestedItem in requestedItems)
            {
                requestedItem.ExactItems = exactSelections
                    .Where(item => string.Equals(item.templateId, requestedItem.TemplateId, StringComparison.Ordinal))
                    .Select(CloneExactStashItem)
                    .ToList();
            }

            return true;
        }

        private static void ShowBuyConfirmOverlay(EquipmentBuildBuyQuote quote)
        {
            CloseBuyConfirmOverlay();
            quote.IconGeneration = ++_buyConfirmOverlayGeneration;

            Transform overlayParent = _activeScreen?.transform;
            if (overlayParent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Buy confirmation overlay could not open: no overlay parent was available.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_BuyLoadoutConfirmOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;
            _buyConfirmOverlay = overlayRoot;

            GameObject panel = new GameObject("pitFireTeam_BuyLoadoutConfirmPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            bool hasItemizedQuote = quote.ExcludeExistingItems && (quote.UsedStashItems.Count > 0 || quote.PurchasedItems.Count > 0);
            int itemizedLineCount = CountOverlayTextLines(CreateBuyConfirmBodyText(quote));
            float panelHeight = hasItemizedQuote ? Mathf.Clamp(250f + itemizedLineCount * 22f, 340f, 640f) : 224f;
            panelRect.sizeDelta = hasItemizedQuote ? new Vector2(900f, panelHeight) : new Vector2(700f, panelHeight);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_BuyLoadoutConfirmHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutConfirmTitle",
                FormatBuyOverlayTitle(quote),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);
            TextMeshProUGUI titleLabel = titleObject.GetComponent<TextMeshProUGUI>();

            Button closeButton = CreateOverlayCloseButton(header.transform, "pitFireTeam_BuyLoadoutConfirmCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(() =>
            {
                Modules.Logger.LogInfo("[UI] Teammate equipment build buy confirmation closed.");
                CloseBuyConfirmOverlay();
            });

            DefaultUIButton buyButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            Button fallbackBuyButton = null;
            TextMeshProUGUI fallbackBuyButtonLabel = null;
            if (buyButton != null)
            {
                buyButton.SetRawText(GetQuoteActionButtonText(quote), 22);
                buyButton.OnClick.RemoveAllListeners();
                buyButton.OnClick.AddListener(() => ConfirmBuyQuote(quote));
            }
            else
            {
                fallbackBuyButton = CreateFallbackOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f), GetQuoteActionButtonText(quote));
                fallbackBuyButton.onClick.AddListener(() => ConfirmBuyQuote(quote));
                fallbackBuyButtonLabel = fallbackBuyButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (quote.ExcludeExistingItems && quote.AvailableStashItems.Count > 0)
            {
                CreateInteractiveBuyQuoteBody(panel.transform, quote, titleLabel, buyButton, fallbackBuyButton, fallbackBuyButtonLabel);
            }
            else
            {
                string bodyText = CreateBuyConfirmBodyText(quote);
                if (hasItemizedQuote)
                {
                    CreateScrollableOverlayText(panel.transform, "pitFireTeam_BuyLoadoutConfirmBodyScroll", bodyText, 18f, new Vector2(28f, 72f), new Vector2(-28f, -42f), itemizedLineCount);
                }
                else
                {
                    GameObject bodyObject = CreateOverlayText(
                        "pitFireTeam_BuyLoadoutConfirmBody",
                        bodyText,
                        23f,
                        TextAlignmentOptions.Center);
                    RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
                    bodyRect.SetParent(panel.transform, false);
                    bodyRect.anchorMin = new Vector2(0f, 0f);
                    bodyRect.anchorMax = new Vector2(1f, 1f);
                    bodyRect.offsetMin = new Vector2(28f, 72f);
                    bodyRect.offsetMax = new Vector2(-28f, -42f);

                    TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
                    bodyLabel.enableWordWrapping = true;
                    bodyLabel.overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            _buyConfirmOverlay = overlayRoot;
            Modules.Logger.LogInfo($"[UI] Teammate equipment build buy confirmation opened: build='{quote.BuildName}', price={quote.FinalPrice}, excludeExistingItems={quote.ExcludeExistingItems}, stashItemsUsed={quote.UsedStashItems.Count}, missingTemplates={quote.MissingTemplateCounts.Count}.");
        }

        private static void CreateInteractiveBuyQuoteBody(
            Transform parent,
            EquipmentBuildBuyQuote quote,
            TextMeshProUGUI titleLabel,
            DefaultUIButton buyButton,
            Button fallbackBuyButton,
            TextMeshProUGUI fallbackBuyButtonLabel)
        {
            GameObject scrollObject = new GameObject("pitFireTeam_BuyLoadoutInteractiveBodyScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = new Vector2(28f, 72f);
            scrollRectTransform.offsetMax = new Vector2(-28f, -42f);

            GameObject viewportObject = new GameObject("pitFireTeam_BuyLoadoutInteractiveBodyViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            Mask mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentObject = new GameObject("pitFireTeam_BuyLoadoutInteractiveBodyContent", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 35f;

            GameObject promptObject = CreateOverlayText("pitFireTeam_BuyLoadoutInteractivePrompt", "", 20f, TextAlignmentOptions.TopLeft);
            promptObject.transform.SetParent(contentObject.transform, false);
            RectTransform promptRect = promptObject.GetComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0f, 1f);
            promptRect.anchorMax = new Vector2(1f, 1f);
            promptRect.pivot = new Vector2(0.5f, 1f);
            promptRect.offsetMin = new Vector2(0f, -34f);
            promptRect.offsetMax = new Vector2(0f, 0f);
            TextMeshProUGUI promptLabel = promptObject.GetComponent<TextMeshProUGUI>();
            promptLabel.enableWordWrapping = true;
            promptLabel.overflowMode = TextOverflowModes.Overflow;

            GameObject deliveryObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutInteractiveDeliveryNotice",
                GetSocialUiText("KitCurrentGearDeliveryNotice"),
                17f,
                TextAlignmentOptions.TopLeft);
            deliveryObject.transform.SetParent(contentObject.transform, false);
            RectTransform deliveryRect = deliveryObject.GetComponent<RectTransform>();
            deliveryRect.anchorMin = new Vector2(0f, 1f);
            deliveryRect.anchorMax = new Vector2(1f, 1f);
            deliveryRect.pivot = new Vector2(0.5f, 1f);
            deliveryRect.offsetMin = new Vector2(0f, -70f);
            deliveryRect.offsetMax = new Vector2(0f, -38f);

            GameObject stashHeaderObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutInteractiveStashHeader",
                GetSocialUiText("KitItemsTakenFromStash"),
                18f,
                TextAlignmentOptions.TopLeft);
            stashHeaderObject.transform.SetParent(contentObject.transform, false);
            RectTransform stashHeaderRect = stashHeaderObject.GetComponent<RectTransform>();
            stashHeaderRect.anchorMin = new Vector2(0f, 1f);
            stashHeaderRect.anchorMax = new Vector2(1f, 1f);
            stashHeaderRect.pivot = new Vector2(0.5f, 1f);
            stashHeaderRect.offsetMin = new Vector2(0f, -104f);
            stashHeaderRect.offsetMax = new Vector2(0f, -76f);

            RectTransform purchasedItemsContainer = null;
            Toggle templateToggle = ResolveEditBuildOnlyAvailableToggleTemplate();
            var pendingIconLoads = new List<OverlayItemIconRequest>();
            float rowY = -108f;
            foreach (UsedStashItemSummary backingItem in quote.AvailableStashItems.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (backingItem == null)
                {
                    continue;
                }

                Toggle rowToggle = CreateOverlayStashItemRow(contentObject.transform, templateToggle, backingItem, new Vector2(0f, rowY), pendingIconLoads, quote.IconGeneration);
                if (rowToggle == null)
                {
                    continue;
                }

                rowToggle.SetIsOnWithoutNotify(backingItem.Selected);
                rowToggle.onValueChanged.AddListener(isOn =>
                {
                    backingItem.Selected = isOn;
                    RecalculateBuyQuoteSelection(quote);
                    UpdateInteractiveBuyQuoteBody(quote, titleLabel, promptLabel, purchasedItemsContainer, buyButton, fallbackBuyButton, fallbackBuyButtonLabel);
                });
                rowY -= 52f;
            }

            GameObject purchasedObject = new GameObject("pitFireTeam_BuyLoadoutInteractivePurchasedItems", typeof(RectTransform));
            purchasedObject.transform.SetParent(contentObject.transform, false);
            RectTransform purchasedRect = purchasedObject.GetComponent<RectTransform>();
            purchasedRect.anchorMin = new Vector2(0f, 1f);
            purchasedRect.anchorMax = new Vector2(1f, 1f);
            purchasedRect.pivot = new Vector2(0.5f, 1f);
            purchasedRect.offsetMin = new Vector2(0f, rowY - 260f);
            purchasedRect.offsetMax = new Vector2(0f, rowY - 10f);
            purchasedItemsContainer = purchasedRect;

            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(320f, Mathf.Abs(rowY) + 320f));
            UpdateInteractiveBuyQuoteBody(quote, titleLabel, promptLabel, purchasedItemsContainer, buyButton, fallbackBuyButton, fallbackBuyButtonLabel);
            StartOverlayItemIconLoads(pendingIconLoads);
        }

        private static Toggle CreateOverlayStashItemRow(Transform parent, Toggle templateToggle, UsedStashItemSummary item, Vector2 anchoredPosition, List<OverlayItemIconRequest> pendingIconLoads, int iconGeneration)
        {
            if (templateToggle == null)
            {
                return null;
            }

            GameObject rowObject = new GameObject("pitFireTeam_BuyLoadoutStashItemRow", typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(0f, anchoredPosition.y - 48f);
            rowRect.offsetMax = new Vector2(0f, anchoredPosition.y);

            Toggle toggle = UnityEngine.Object.Instantiate(templateToggle, rowObject.transform, false);
            toggle.name = "pitFireTeam_BuyLoadoutStashItemToggle";
            toggle.transform.SetParent(rowObject.transform, false);
            toggle.gameObject.SetActive(true);
            toggle.onValueChanged.RemoveAllListeners();
            toggle.group = null;
            toggle.interactable = true;

            RectTransform rect = toggle.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 0f);
                rect.sizeDelta = new Vector2(34f, 34f);
                rect.localScale = Vector3.one;
            }

            foreach (Selectable selectable in toggle.GetComponentsInChildren<Selectable>(true))
            {
                selectable.interactable = true;
            }

            foreach (TMP_Text label in toggle.GetComponentsInChildren<TMP_Text>(true))
            {
                label.text = string.Empty;
            }

            CreateOverlayItemIcon(rowObject.transform, item.TemplateId, new Vector2(40f, 0f), pendingIconLoads, iconGeneration);
            CreateOverlayItemRowLabel(rowObject.transform, FormatUsedStashItemLine(item), new Vector2(88f, 0f));
            CreateOverlayDivider(rowObject.transform);

            return toggle;
        }

        private static void CreateOverlayPurchaseItemRows(RectTransform container, IEnumerable<UsedStashItemSummary> items, int iconGeneration)
        {
            if (container == null)
            {
                return;
            }

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);
            }

            List<UsedStashItemSummary> displayItems = CreateDisplayItemSummaries(items)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (displayItems.Count == 0)
            {
                container.sizeDelta = new Vector2(0f, 0f);
                RefreshInteractiveContentHeight(container);
                return;
            }

            GameObject headerObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutPurchasedHeader",
                GetSocialUiText("KitItemsPurchased"),
                18f,
                TextAlignmentOptions.TopLeft);
            headerObject.transform.SetParent(container, false);
            RectTransform headerRect = headerObject.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -28f);
            headerRect.offsetMax = new Vector2(0f, 0f);

            float rowY = -36f;
            var pendingIconLoads = new List<OverlayItemIconRequest>();
            foreach (UsedStashItemSummary item in displayItems)
            {
                CreateOverlayPurchaseItemRow(container, item, rowY, pendingIconLoads, iconGeneration);
                rowY -= 52f;
            }

            container.sizeDelta = new Vector2(0f, Mathf.Abs(rowY) + 12f);
            RefreshInteractiveContentHeight(container);
            StartOverlayItemIconLoads(pendingIconLoads);
        }

        private static void RefreshInteractiveContentHeight(RectTransform purchasedItemsContainer)
        {
            if (purchasedItemsContainer?.parent is RectTransform contentRect)
            {
                float purchasedBottom = Mathf.Abs(purchasedItemsContainer.offsetMin.y) + purchasedItemsContainer.sizeDelta.y + 48f;
                contentRect.sizeDelta = new Vector2(0f, Mathf.Max(contentRect.sizeDelta.y, purchasedBottom));
            }
        }

        private static void CreateOverlayPurchaseItemRow(Transform parent, UsedStashItemSummary item, float rowY, List<OverlayItemIconRequest> pendingIconLoads, int iconGeneration)
        {
            GameObject rowObject = new GameObject("pitFireTeam_BuyLoadoutPurchaseItemRow", typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(0f, rowY - 48f);
            rowRect.offsetMax = new Vector2(0f, rowY);

            CreateOverlayItemIcon(rowObject.transform, item.TemplateId, new Vector2(0f, 0f), pendingIconLoads, iconGeneration);
            CreateOverlayItemRowLabel(rowObject.transform, FormatUsedStashItemLine(item), new Vector2(48f, 0f));
            CreateOverlayDivider(rowObject.transform);
        }

        private static void CreateOverlayItemIcon(Transform parent, string templateId, Vector2 anchoredPosition, List<OverlayItemIconRequest> pendingIconLoads, int iconGeneration)
        {
            GameObject frameObject = new GameObject("pitFireTeam_BuyLoadoutItemIconFrame", typeof(RectTransform), typeof(Image));
            frameObject.transform.SetParent(parent, false);
            RectTransform frameRect = frameObject.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0f, 0.5f);
            frameRect.anchorMax = new Vector2(0f, 0.5f);
            frameRect.pivot = new Vector2(0f, 0.5f);
            frameRect.anchoredPosition = anchoredPosition;
            frameRect.sizeDelta = new Vector2(40f, 40f);

            Image frame = frameObject.GetComponent<Image>();
            frame.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            frame.raycastTarget = false;

            GameObject iconObject = new GameObject("pitFireTeam_BuyLoadoutItemIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(frameObject.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(2f, 2f);
            iconRect.offsetMax = new Vector2(-2f, -2f);

            Image image = iconObject.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
            image.enabled = false;
            if (pendingIconLoads != null && !string.IsNullOrWhiteSpace(templateId))
            {
                pendingIconLoads.Add(new OverlayItemIconRequest
                {
                    Image = image,
                    TemplateId = templateId,
                    Generation = iconGeneration
                });
            }
        }

        private static void CreateOverlayItemRowLabel(Transform parent, string text, Vector2 anchoredPosition)
        {
            GameObject labelObject = CreateOverlayText("pitFireTeam_BuyLoadoutItemLabel", text, 17f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(parent, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(anchoredPosition.x, 0f);
            labelRect.offsetMax = new Vector2(0f, 0f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static void CreateOverlayDivider(Transform parent)
        {
            GameObject dividerObject = new GameObject("pitFireTeam_BuyLoadoutItemDivider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(parent, false);
            RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.offsetMin = new Vector2(0f, 0f);
            dividerRect.offsetMax = new Vector2(0f, 1f);

            Image divider = dividerObject.GetComponent<Image>();
            divider.color = new Color(1f, 1f, 1f, 0.18f);
            divider.raycastTarget = false;
        }

        private static void StartOverlayItemIconLoads(IEnumerable<OverlayItemIconRequest> requests)
        {
            if (requests == null)
            {
                return;
            }

            // Rows are created first, then icon work starts as a batch. This avoids
            // interleaving expensive EFT icon generation with layout construction and
            // gives every icon frame/text row a stable target before async callbacks run.
            foreach (OverlayItemIconRequest request in requests)
            {
                AssignItemIcon(request?.Image, request?.TemplateId, request?.Generation ?? 0);
            }
        }

        private static void AssignItemIcon(Image image, string templateId, int iconGeneration)
        {
            if (!IsOverlayIconRequestActive(image, iconGeneration) || string.IsNullOrWhiteSpace(templateId))
            {
                return;
            }

            try
            {
                Item item = Singleton<EFT.ItemFactory>.Instance.CreateItem(MongoID.Generate(true), templateId, null);
                ItemIcon icon = ItemViewFactory.LoadItemIcon(item, 1, false);
                if (IsOverlayIconRequestActive(image, iconGeneration) && icon?.Sprite != null)
                {
                    image.sprite = icon.Sprite;
                    image.enabled = true;
                }

                if (icon?.Changed != null)
                {
                    BuyConfirmIconBindings.Add(icon.Changed.Bind(() =>
                    {
                        if (IsOverlayIconRequestActive(image, iconGeneration) && icon.Sprite != null)
                        {
                            image.sprite = icon.Sprite;
                            image.enabled = true;
                        }
                    }));
                }

                ItemViewFactory.GetItemSpriteAsync(item, 1)
                    .ContinueWith(task =>
                    {
                        AssignLoadedItemIconWithRetry(image, item, templateId, task, retryAttempted: false, iconGeneration);
                    }, TaskScheduler.FromCurrentSynchronizationContext())
                    .HandleExceptions();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to load teammate kit item icon for '{templateId}': {ex.Message}");
                if (IsOverlayIconRequestActive(image, iconGeneration))
                {
                    image.enabled = false;
                }
            }
        }

        private static bool IsOverlayIconRequestActive(Image image, int iconGeneration)
        {
            return image != null
                && _buyConfirmOverlay != null
                && iconGeneration != 0
                && iconGeneration == _buyConfirmOverlayGeneration;
        }

        private static void AssignLoadedItemIconWithRetry(Image image, Item item, string templateId, Task<Sprite> task, bool retryAttempted, int iconGeneration)
        {
            if (!IsOverlayIconRequestActive(image, iconGeneration))
            {
                return;
            }

            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
            {
                image.sprite = task.Result;
                image.enabled = true;
                return;
            }

            if (retryAttempted)
            {
                pitFireTeam.Log.LogWarning($"[UI] Teammate kit item icon retry failed for '{templateId}'.");
                return;
            }

            ItemViewFactory.GetItemSpriteAsync(item, 1)
                .ContinueWith(retryTask =>
                {
                    AssignLoadedItemIconWithRetry(image, item, templateId, retryTask, retryAttempted: true, iconGeneration);
                }, TaskScheduler.FromCurrentSynchronizationContext())
                .HandleExceptions();
        }

        private static void UpdateInteractiveBuyQuoteBody(
            EquipmentBuildBuyQuote quote,
            TextMeshProUGUI titleLabel,
            TextMeshProUGUI promptLabel,
            RectTransform purchasedItemsContainer,
            DefaultUIButton buyButton,
            Button fallbackBuyButton,
            TextMeshProUGUI fallbackBuyButtonLabel)
        {
            string actionText = GetQuoteActionButtonText(quote);
            if (titleLabel != null)
            {
                titleLabel.text = FormatBuyOverlayTitle(quote);
            }

            if (promptLabel != null)
            {
                promptLabel.text = quote.CanEquipFromStash
                    ? string.Empty
                    : string.Format(
                        GetSocialUiText("PurchaseKitPrompt"),
                        quote.BuildName,
                        FormatRoubles(quote.FinalPrice));
            }

            CreateOverlayPurchaseItemRows(purchasedItemsContainer, quote.PurchasedItems, quote.IconGeneration);

            bool canAfford = CanAffordBuyQuote(quote);
            if (buyButton != null)
            {
                buyButton.SetRawText(actionText, 22);
                buyButton.Interactable = canAfford;
            }

            if (fallbackBuyButton != null)
            {
                fallbackBuyButton.interactable = canAfford;
            }

            if (fallbackBuyButtonLabel != null)
            {
                fallbackBuyButtonLabel.text = actionText;
            }
        }

        private static void ShowNotEnoughResourcesOverlay(EquipmentBuildBuyQuote quote)
        {
            CloseBuyConfirmOverlay();

            Transform overlayParent = _activeScreen?.transform;
            if (overlayParent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Not-enough-resources overlay could not open: no overlay parent was available.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 224f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutNotEnoughResourcesTitle",
                GetSocialUiText("BuyKitTitle"),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateOverlayCloseButton(header.transform, "pitFireTeam_BuyLoadoutNotEnoughResourcesCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(CloseBuyConfirmOverlay);

            string bodyText = string.Format(
                GetSocialUiText("NotEnoughResourcesKitPrompt"),
                quote?.BuildName ?? "selected");
            GameObject bodyObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutNotEnoughResourcesBody",
                bodyText,
                23f,
                TextAlignmentOptions.Center);
            RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.SetParent(panel.transform, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(28f, 72f);
            bodyRect.offsetMax = new Vector2(-28f, -42f);

            TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

            string okText = GetSocialUiText("Ok");
            DefaultUIButton okButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            if (okButton != null)
            {
                okButton.SetRawText(okText, 22);
                okButton.OnClick.RemoveAllListeners();
                okButton.OnClick.AddListener(CloseBuyConfirmOverlay);
            }
            else
            {
                Button fallbackButton = CreateFallbackOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f), okText);
                fallbackButton.onClick.AddListener(CloseBuyConfirmOverlay);
            }

            _buyConfirmOverlay = overlayRoot;
            Modules.Logger.LogInfo($"[UI] Teammate equipment build purchase blocked by resources: build='{quote?.BuildName}', price={quote?.FinalPrice ?? 0}, availableRoubles={GetAvailableStashRoubles()}, excludeExistingItems={quote?.ExcludeExistingItems ?? false}.");
        }

        private static void ConfirmBuyQuote(EquipmentBuildBuyQuote quote)
        {
            RecalculateBuyQuoteSelection(quote);
            if (!CanAffordBuyQuote(quote))
            {
                ShowNotEnoughResourcesOverlay(quote);
                return;
            }

            ConfirmBuyQuoteAsync(quote).HandleExceptions();
        }

        private static async Task ConfirmBuyQuoteAsync(EquipmentBuildBuyQuote quote)
        {
            if (quote?.Build?.Equipment == null || string.IsNullOrWhiteSpace(_accountId))
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification(GetSocialUiText("KitLoadoutPurchaseFailed"), ENotificationDurationType.Default);
                return;
            }

            SetBuyScreenBusy(true);
            try
            {
                JsonType.FlatItem[] serializedEquipment = Singleton<EFT.ItemFactory>.Instance.TreeToFlatItems(new Item[] { quote.Build.Equipment });
                if (serializedEquipment == null || serializedEquipment.Length == 0)
                {
                    throw new InvalidOperationException("Selected teammate kit equipment was unavailable for purchase.");
                }

                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    BuyKitRoute,
                    SerializeBody(new FriendlyTeammateBuyKitRequest
                    {
                        aid = _accountId,
                        items = serializedEquipment,
                        price = quote.FinalPrice,
                        useItemsInStash = quote.ExcludeExistingItems,
                        usedItems = quote.UsedStashItems
                            .SelectMany(item => item.ExactItems ?? new List<FriendlyTeammateBuyKitUsedItem>())
                            .Select(CloneExactStashItem)
                            .ToArray()
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateBuyKitResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateBuyKitResponse>(responseJson);

                OtherPlayerProfileScreenPatch.ApplyServerSavedPlayerStash(
                    _session?.Profile,
                    _backendInventoryController,
                    _session?.RagFair,
                    response?.data?.playerStashItems);

                Modules.Logger.LogInfo($"[UI] Teammate equipment build {GetQuoteActionButtonText(quote)} completed: build='{quote.BuildName}', price={quote.FinalPrice}, useItemsInStash={quote.ExcludeExistingItems}.");

                // The server has already saved the teammate's new Default gear here, so the
                // roster portrait can be regenerated from the updated profile when My Squad
                // is injected again.
                Components.SquadControlMenuUi.RequestRosterTileRefreshOnNextInject(_accountId);

                CloseBuyConfirmOverlay();
                ReturnToProfile();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to purchase teammate equipment build.");
                pitFireTeam.Log.LogError(ex);
                EFT.Communications.NotificationManager.DisplayWarningNotification(ex.Message ?? GetSocialUiText("KitLoadoutPurchaseFailed"), ENotificationDurationType.Default);
            }
            finally
            {
                SetBuyScreenBusy(false);
            }
        }

        private static void CloseBuyConfirmOverlay()
        {
            _buyConfirmOverlayGeneration++;
            foreach (Action unsubscribe in BuyConfirmIconBindings.ToList())
            {
                try
                {
                    unsubscribe?.Invoke();
                }
                catch
                {
                    // Best-effort cleanup; stale icon callbacks should never block overlay close.
                }
            }

            BuyConfirmIconBindings.Clear();
            if (_buyConfirmOverlay != null)
            {
                UnityEngine.Object.Destroy(_buyConfirmOverlay);
                _buyConfirmOverlay = null;
            }
        }

        private static void SetBuyScreenBusy(bool busy)
        {
            try
            {
                if (_buyConfirmOverlay != null)
                {
                    CanvasGroup canvasGroup = _buyConfirmOverlay.GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = _buyConfirmOverlay.AddComponent<CanvasGroup>();
                    }

                    canvasGroup.interactable = !busy;
                    canvasGroup.blocksRaycasts = true;
                }

                if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    MonoBehaviourSingleton<PreloaderUI>.Instance.SetLoaderStatus(busy);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to set teammate kit purchase busy state: {ex.Message}");
            }
        }

        private static string SerializeBody(object body)
        {
            return body.ToJson(GetDefaultJsonConverters());
        }

        private static FriendlyTeammateBodyResponse<T> DeserializeBodySuccess<T>(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            FriendlyTeammateBodyResponse<T> body = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<T>>(responseJson);
            if (body != null && body.err != 0)
            {
                throw new InvalidOperationException(body.errmsg ?? "Unknown teammate backend error");
            }

            return body;
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            Type converterClass = typeof(AbstractGame).Assembly
                .GetTypes()
                .First(type => type.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            return Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
        }

        private static DefaultUIButton CreateOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton template = FindEquipButtonTransform(_activeScreen?.transform)?.GetComponent<DefaultUIButton>();
            if (template == null)
            {
                return null;
            }

            DefaultUIButton button = UnityEngine.Object.Instantiate(template, parent, false);
            button.name = "pitFireTeam_BuyLoadoutConfirmActionButton";
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one * 0.9f;
            }

            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetIcon(null);
            return button;
        }

        private static Button CreateFallbackOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size, string text)
        {
            GameObject buttonObject = new GameObject("pitFireTeam_BuyLoadoutConfirmFallbackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.28f, 0.28f, 0.28f, 1f);
            image.raycastTarget = true;

            GameObject labelObject = CreateOverlayText("Label", text, 22f, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonObject.transform, false);
            Stretch(labelRect);

            return buttonObject.GetComponent<Button>();
        }

        private static Button CreateOverlayCloseButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = CreateOverlayText($"{name}_Label", "X", 16f, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonObject.transform, false);
            Stretch(labelRect);

            return buttonObject.GetComponent<Button>();
        }

        private static GameObject CreateOverlayText(string name, string text, float size, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.86f, 0.86f, 0.86f, 1f);
            label.raycastTarget = false;
            return textObject;
        }

        private static void CreateScrollableOverlayText(Transform parent, string name, string text, float size, Vector2 offsetMin, Vector2 offsetMax, int lineCount)
        {
            GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = offsetMin;
            scrollRectTransform.offsetMax = offsetMax;

            GameObject viewportObject = new GameObject($"{name}_Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            Mask mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentObject = CreateOverlayText($"{name}_Content", text, size, TextAlignmentOptions.TopLeft);
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(160f, lineCount * 25f));

            TextMeshProUGUI label = contentObject.GetComponent<TextMeshProUGUI>();
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 35f;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static string FormatRoubles(int amount)
        {
            return string.Format(GetSocialUiText("CurrencyRoubles"), Mathf.Max(0, amount));
        }

        private static string FormatBuyOverlayTitle(EquipmentBuildBuyQuote quote)
        {
            return quote == null
                ? GetSocialUiText("BuyKitTitle")
                : $"{quote.BuildName} - {FormatRoubles(quote.FinalPrice)}";
        }

        private static string CreateBuyConfirmBodyText(EquipmentBuildBuyQuote quote)
        {
            string deliveryText = GetSocialUiText("KitCurrentGearDeliveryNotice");
            if (quote.CanEquipFromStash)
            {
                return $"{CreateUsedStashItemsText(quote)}\n\n{deliveryText}";
            }

            string text = string.Format(
                GetSocialUiText("PurchaseKitPrompt"),
                quote.BuildName,
                FormatRoubles(quote.FinalPrice));
            text = $"{text}\n\n{deliveryText}";
            if (!quote.ExcludeExistingItems || quote.UsedStashItems.Count == 0)
            {
                return quote.ExcludeExistingItems && quote.PurchasedItems.Count > 0
                    ? $"{text}\n\n{CreatePurchasedItemsText(quote)}"
                    : text;
            }

            string usedText = CreateUsedStashItemsText(quote);
            if (quote.PurchasedItems.Count == 0)
            {
                return $"{text}\n\n{usedText}";
            }

            return $"{text}\n\n{usedText}\n\n{CreatePurchasedItemsText(quote)}";
        }

        private static string CreateUsedStashItemsText(EquipmentBuildBuyQuote quote)
        {
            IEnumerable<string> lines = CreateDisplayItemSummaries(quote.UsedStashItems)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatUsedStashItemLine);

            return $"{GetSocialUiText("KitItemsTakenFromStash")}\n{string.Join("\n", lines)}";
        }

        private static string CreatePurchasedItemsText(EquipmentBuildBuyQuote quote)
        {
            IEnumerable<string> lines = CreateDisplayItemSummaries(quote.PurchasedItems)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatUsedStashItemLine);

            return $"{GetSocialUiText("KitItemsPurchased")}\n{string.Join("\n", lines)}";
        }

        private static List<UsedStashItemSummary> CreateDisplayItemSummaries(IEnumerable<UsedStashItemSummary> items)
        {
            if (items == null)
            {
                return new List<UsedStashItemSummary>();
            }

            return items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                .GroupBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new UsedStashItemSummary
                {
                    DisplayName = group.First().DisplayName,
                    TemplateId = group.First().TemplateId,
                    Count = group.Sum(item => item.Count),
                    TotalPrice = group.Sum(item => item.TotalPrice),
                    Selected = group.Any(item => item.Selected)
                })
                .ToList();
        }

        private static void RecalculateBuyQuoteSelection(EquipmentBuildBuyQuote quote)
        {
            if (quote == null)
            {
                return;
            }

            List<UsedStashItemSummary> selectedStashItems = quote.AvailableStashItems
                .Where(item => item?.Selected == true && item.Count > 0)
                .Select(CloneUsedStashItemSummary)
                .ToList();
            List<UsedStashItemSummary> purchasedItems = quote.BasePurchasedItems
                .Select(CloneUsedStashItemSummary)
                .ToList();

            foreach (UsedStashItemSummary unselectedItem in quote.AvailableStashItems.Where(item => item?.Selected != true && item.Count > 0))
            {
                purchasedItems.Add(CloneUsedStashItemSummary(unselectedItem));
            }

            double selectedValue = selectedStashItems.Sum(item => item.TotalPrice);
            quote.UsedStashItems = selectedStashItems;
            quote.PurchasedItems = purchasedItems;
            quote.FinalPrice = Mathf.Max(0, quote.FullKitPrice - Convert.ToInt32(Math.Floor(selectedValue)));
            quote.MissingOnlyPrice = quote.FinalPrice;
            quote.CanEquipFromStash = quote.ExcludeExistingItems && quote.PurchasedItems.Count == 0;
        }

        private static UsedStashItemSummary CloneUsedStashItemSummary(UsedStashItemSummary item)
        {
            return new UsedStashItemSummary
            {
                TemplateId = item.TemplateId,
                DisplayName = item.DisplayName,
                Count = item.Count,
                TotalPrice = item.TotalPrice,
                Selected = item.Selected,
                ExactItems = item.ExactItems?
                    .Select(CloneExactStashItem)
                    .ToList()
                    ?? new List<FriendlyTeammateBuyKitUsedItem>()
            };
        }

        private static FriendlyTeammateBuyKitUsedItem CloneExactStashItem(FriendlyTeammateBuyKitUsedItem item)
        {
            return new FriendlyTeammateBuyKitUsedItem
            {
                itemId = item.itemId,
                templateId = item.templateId,
                count = item.count
            };
        }

        private static int CountOverlayTextLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            return text.Count(character => character == '\n') + 1;
        }

        private static string GetQuoteActionButtonText(EquipmentBuildBuyQuote quote)
        {
            return quote?.CanEquipFromStash == true
                ? GetSocialUiText("EquipKitAction")
                : GetSocialUiText("PurchaseKitAction");
        }

        private static string FormatUsedStashItemLine(UsedStashItemSummary item)
        {
            return item.Count > 1
                ? $"{item.Count} x {item.DisplayName}"
                : item.DisplayName;
        }

        private static Toggle ResolveEditBuildOnlyAvailableToggleTemplate()
        {
            return EditBuildOnlyAvailableToggleField?.GetValue(CommonUI.Instance?.EditBuildScreen) as Toggle
                ?? CommonUI.Instance?.EditBuildScreen?.transform.Find("Toggle Group/OnlyAvailable")?.GetComponent<Toggle>()
                ?? FindChildRecursive(CommonUI.Instance?.EditBuildScreen?.transform, "OnlyAvailable")?.GetComponent<Toggle>();
        }

        private static void ConfigureClonedExcludeExistingToggle(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            // The stock EditBuildScreen toggle may already carry runtime listeners
            // from its original screen. Clear them so the clone only changes the
            // teammate-buy option and never drives weapon-build filtering.
            toggle.onValueChanged.RemoveAllListeners();
            toggle.group = null;
            toggle.interactable = true;

            foreach (Selectable selectable in toggle.GetComponentsInChildren<Selectable>(true))
            {
                selectable.interactable = true;
            }

            foreach (TMP_Text label in toggle.GetComponentsInChildren<TMP_Text>(true))
            {
                label.text = GetSocialUiText("UseItemsInStash");
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        private static void ApplySelectedBuildPrice(EquipmentBuildsScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            EFT.UI.Builds.EquipmentBuild selectedBuild = ScreenSelectedBuildField?.GetValue(screen) as EFT.UI.Builds.EquipmentBuild;
            HealthParameterPanel weightPanel = ScreenWeightField?.GetValue(screen) as HealthParameterPanel;
            if (selectedBuild == null || weightPanel == null)
            {
                return;
            }

            KitLoadoutPricingContext pricingContext = CreateKitLoadoutPricingContext(selectedBuild.Equipment);
            int price = CalculateBuildMarketRoublePrice(selectedBuild.Equipment, pricingContext);
            LogBuildPriceDiagnosticsOnce(selectedBuild, pricingContext, price, "selected");
            weightPanel.SetParameterValue(new ValueStruct
            {
                Current = price,
                Maximum = 0f
            }, -1f, 0, false, true);
            weightPanel.SetWarningColor(false, false);
        }

        private static void ApplyBuildListPrice(EquipmentBuildListView view, EFT.UI.BuildWrapper<EFT.UI.Builds.EquipmentBuild> buildWrapper)
        {
            ApplyBuildListPrice(view, buildWrapper?.Build);
        }

        private static void ApplyBuildListPrice(EquipmentBuildListView view, EFT.UI.Builds.EquipmentBuild build)
        {
            HealthParameterPanel weightPanel = BuildListWeightField?.GetValue(view) as HealthParameterPanel;
            if (weightPanel == null || build == null)
            {
                return;
            }

            KitLoadoutPricingContext pricingContext = CreateKitLoadoutPricingContext(build.Equipment);
            int price = CalculateBuildMarketRoublePrice(build.Equipment, pricingContext);
            LogBuildPriceDiagnosticsOnce(build, pricingContext, price, "list");
            weightPanel.SetParameterValue(new ValueStruct
            {
                Current = price,
                Maximum = 0f
            }, -1f, 0, false, true);
            weightPanel.SetWarningColor(false, false);
        }

        private static int CalculateBuildMarketRoublePrice(InventoryEquipment equipment)
        {
            return CalculateBuildMarketRoublePrice(equipment, CreateKitLoadoutPricingContext(equipment));
        }

        private static int CalculateBuildMarketRoublePrice(InventoryEquipment equipment, KitLoadoutPricingContext pricingContext)
        {
            if (equipment == null)
            {
                return 0;
            }

            double total = 0.0;
            foreach (Item item in CollectEquipmentBuildItems(equipment))
            {
                if (IsIgnoredKitRequirementItem(item))
                {
                    continue;
                }

                total += CalculateKitLoadoutItemMarketRoublePrice(item, pricingContext);
            }

            return Mathf.Max(0, Convert.ToInt32(Math.Floor(total)));
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogBuildPriceDiagnosticsOnce(EFT.UI.Builds.EquipmentBuild build, KitLoadoutPricingContext pricingContext, int finalPrice, string source)
        {
            if (!EnableKitLoadoutPricingDiagnostics || build?.Equipment == null || pricingContext == null)
            {
                return;
            }

            string buildKey = $"{_buildPricingLogPhase}|{CreateBuildPricingLogKey(build)}";
            if (!LoggedBuildPricingKeys.Add(buildKey))
            {
                return;
            }

            string buildName = string.IsNullOrWhiteSpace(build.Name) ? "Selected loadout" : build.Name;
            List<Item> items = CollectEquipmentBuildItems(build.Equipment)
                .Where(item => !IsIgnoredKitRequirementItem(item))
                .ToList();
            double rawTotal = items.Sum(item => Math.Max(0.0, CalculateSingleItemMarketRoublePrice(item)));
            Modules.Logger.LogInfo($"[UI][KitPrice] phase={_buildPricingLogPhase} source={source} build='{buildName}' items={items.Count} rawTotal={Math.Floor(rawTotal)} finalTotal={finalPrice} marketPricesLoaded={_marketPrices != null}");

            foreach (string line in pricingContext.Diagnostics)
            {
                Modules.Logger.LogInfo($"[UI][KitPrice] build='{buildName}' {line}");
            }

            foreach (Item item in items)
            {
                double rawPrice = Math.Max(0.0, CalculateSingleItemMarketRoublePrice(item));
                double finalItemPrice = pricingContext.TryGetItemPricingEntry(item, out KitLoadoutPricingEntry entry)
                    ? entry.Price
                    : rawPrice;
                string rule = entry?.Rule ?? "full value";
                string slotLabel = GetEquipmentSlotLabel(build.Equipment, item);
                Modules.Logger.LogInfo(
                    $"[UI][KitPrice] build='{buildName}' slot={slotLabel} item='{GetItemDisplayName(item)}' tpl={item.TemplateId} raw={Math.Floor(rawPrice)} final={Math.Floor(finalItemPrice)} rule={rule}");
            }
        }

        private static string CreateBuildPricingLogKey(EFT.UI.Builds.EquipmentBuild build)
        {
            if (build?.Equipment == null)
            {
                return string.IsNullOrWhiteSpace(build?.Name) ? "null-build" : build.Name;
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Item item in CollectEquipmentBuildItems(build.Equipment))
            {
                if (IsIgnoredKitRequirementItem(item) || string.IsNullOrWhiteSpace(item?.TemplateId))
                {
                    continue;
                }

                if (counts.TryGetValue(item.TemplateId, out int existing))
                {
                    counts[item.TemplateId] = existing + 1;
                }
                else
                {
                    counts[item.TemplateId] = 1;
                }
            }

            return $"{build.Name}|{string.Join("|", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value}"))}";
        }

        private static string GetEquipmentSlotLabel(InventoryEquipment equipment, Item item)
        {
            if (equipment == null || item == null)
            {
                return "Unknown";
            }

            foreach (Slot slot in equipment.Slots)
            {
                if (slot?.ContainedItem == null)
                {
                    continue;
                }

                if (string.Equals(slot.ContainedItem.Id, item.Id, StringComparison.Ordinal)
                    || IsInsideItemTree(item, slot.ContainedItem))
                {
                    return slot.ID;
                }
            }

            return "Unknown";
        }

        private static StashOnlyExclusionPlan CreateStashOnlyExclusionPlan(InventoryEquipment equipment, int fullKitPrice, KitLoadoutPricingContext pricingContext)
        {
            List<BuildItemRequirement> requirements = CreateBuildItemRequirements(equipment, pricingContext);
            Dictionary<string, int> missingCounts = new Dictionary<string, int>(_selectedBuildMissingCounts, StringComparer.Ordinal);
            Dictionary<string, int> requestedStashCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BuildItemRequirement requirement in requirements)
            {
                missingCounts.TryGetValue(requirement.TemplateId, out int missingCount);
                int stockAvailableCount = Mathf.Max(
                    0,
                    requirement.RequiredCount - Mathf.Min(requirement.RequiredCount, missingCount));
                if (stockAvailableCount > 0)
                {
                    requestedStashCounts[requirement.TemplateId] = stockAvailableCount;
                }
            }

            List<FriendlyTeammateBuyKitUsedItem> exactSelections =
                CreateSafeExactStashSelections(requestedStashCounts, out Dictionary<string, int> fulfilledCounts);
            List<UsedStashItemSummary> usedItems = new List<UsedStashItemSummary>();
            List<UsedStashItemSummary> purchasedItems = new List<UsedStashItemSummary>();
            double usedValue = 0.0;

            foreach (BuildItemRequirement requirement in requirements)
            {
                if (requirement.RequiredCount <= 0)
                {
                    continue;
                }

                fulfilledCounts.TryGetValue(requirement.TemplateId, out int usedCount);
                if (usedCount > 0)
                {
                    usedValue += requirement.TotalPrice * ((double)usedCount / requirement.RequiredCount);
                    usedItems.Add(new UsedStashItemSummary
                    {
                        TemplateId = requirement.TemplateId,
                        DisplayName = requirement.DisplayName,
                        Count = usedCount,
                        TotalPrice = requirement.TotalPrice * ((double)usedCount / requirement.RequiredCount),
                        Selected = true,
                        ExactItems = exactSelections
                            .Where(item => string.Equals(item.templateId, requirement.TemplateId, StringComparison.Ordinal))
                            .Select(CloneExactStashItem)
                            .ToList()
                    });
                }

                int remainingCount = requirement.RequiredCount - usedCount;
                if (remainingCount > 0)
                {
                    purchasedItems.Add(new UsedStashItemSummary
                    {
                        TemplateId = requirement.TemplateId,
                        DisplayName = requirement.DisplayName,
                        Count = remainingCount,
                        TotalPrice = requirement.TotalPrice * ((double)remainingCount / requirement.RequiredCount)
                    });
                }
            }

            int finalPrice = Mathf.Max(0, fullKitPrice - Convert.ToInt32(Math.Floor(usedValue)));
            int requiredUnits = requirements.Sum(requirement => requirement.RequiredCount);
            int usedUnits = usedItems.Sum(item => item.Count);
            int purchasedUnits = purchasedItems.Sum(item => item.Count);
            Modules.Logger.LogInfo($"[UI] Teammate kit stash-use quote scanned {requirements.Count} template(s), {requiredUnits} required item unit(s), {usedUnits} stash-matched unit(s), {purchasedUnits} purchase unit(s), finalPrice={finalPrice}.");
            return new StashOnlyExclusionPlan
            {
                FinalPrice = finalPrice,
                UsedItems = usedItems,
                PurchasedItems = purchasedItems,
                HasAllRequiredItems = requirements.Count > 0 && purchasedItems.Count == 0
            };
        }

        private static List<BuildItemRequirement> CreateBuildItemRequirements(InventoryEquipment equipment)
        {
            return CreateBuildItemRequirements(equipment, CreateKitLoadoutPricingContext(equipment));
        }

        private static List<BuildItemRequirement> CreateBuildItemRequirements(InventoryEquipment equipment, KitLoadoutPricingContext pricingContext)
        {
            Dictionary<string, BuildItemRequirement> requirements = new Dictionary<string, BuildItemRequirement>(StringComparer.Ordinal);
            if (equipment == null)
            {
                return new List<BuildItemRequirement>();
            }

            foreach (Item item in CollectEquipmentBuildItems(equipment))
            {
                AddRequirement(requirements, item, Mathf.Max(1, item.StackObjectsCount), CalculateKitLoadoutItemMarketRoublePrice(item, pricingContext));
            }

            return requirements.Values.ToList();
        }

        private static void AddRequirement(Dictionary<string, BuildItemRequirement> requirements, Item item, int count, double totalPrice)
        {
            if (IsIgnoredKitRequirementItem(item) || string.IsNullOrWhiteSpace(item.TemplateId))
            {
                return;
            }

            if (!requirements.TryGetValue(item.TemplateId, out BuildItemRequirement requirement))
            {
                requirement = new BuildItemRequirement
                {
                    TemplateId = item.TemplateId,
                    DisplayName = GetItemDisplayName(item)
                };
                requirements[item.TemplateId] = requirement;
            }

            requirement.RequiredCount += count;
            requirement.TotalPrice += totalPrice;
        }

        private static bool IsIgnoredEquipmentBuildSlot(Slot slot)
        {
            if (slot == null)
            {
                return false;
            }

            if (string.Equals(slot.ID, EquipmentSlot.Dogtag.ToStringNoBox<EquipmentSlot>(), StringComparison.Ordinal))
            {
                return true;
            }

            return !pitFireTeam.IsFollowerLoadoutRealisticMode()
                && string.Equals(slot.ID, EquipmentSlot.SecuredContainer.ToStringNoBox<EquipmentSlot>(), StringComparison.Ordinal);
        }

        private static bool IsIgnoredKitRequirementItem(Item item)
        {
            // These are structural roots inside EFT's equipment-build tree. Their
            // children are real kit requirements, but the roots themselves are not
            // purchasable gear and should not appear in the teammate kit quote.
            //
            // EFT.InventoryLogic.BuiltInInserts is also skipped: EFT exposes these as child
            // items for armor/helmet stats, but they are integral materials, not
            // player-buyable or swappable kit parts.
            return item == null
                || item is InventoryEquipment
                || item is EFT.InventoryLogic.Pockets
                || item is EFT.InventoryLogic.BuiltInInserts;
        }

        private static List<FriendlyTeammateBuyKitUsedItem> CreateSafeExactStashSelections(
            IReadOnlyDictionary<string, int> requestedCounts,
            out Dictionary<string, int> fulfilledCounts)
        {
            fulfilledCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            Inventory inventory = _backendInventoryController?.Inventory;
            CompoundItem stash = inventory?.Stash;
            if (stash == null || requestedCounts == null || requestedCounts.Count == 0)
            {
                return new List<FriendlyTeammateBuyKitUsedItem>();
            }

            List<Item> allStashItems = CollectDeepItemTree(stash);
            List<Item> candidates = allStashItems
                .Where(item => item != null
                    && !ReferenceEquals(item, stash)
                    && !IsIgnoredKitRequirementItem(item)
                    && !IsLockedForStashUse(item)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.TemplateId))
                .ToList();
            HashSet<string> unsafeContainerIds = new HashSet<string>(StringComparer.Ordinal);

            for (int iteration = 0; iteration <= candidates.Count; iteration++)
            {
                Dictionary<string, int> remainingCounts = requestedCounts
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                Dictionary<string, ExactStashSelection> selectionsById =
                    new Dictionary<string, ExactStashSelection>(StringComparer.Ordinal);

                foreach (Item candidate in candidates)
                {
                    if (unsafeContainerIds.Contains(candidate.Id)
                        || !remainingCounts.TryGetValue(candidate.TemplateId, out int remaining)
                        || remaining <= 0)
                    {
                        continue;
                    }

                    int selectedCount = Mathf.Min(Mathf.Max(1, candidate.StackObjectsCount), remaining);
                    selectionsById[candidate.Id] = new ExactStashSelection
                    {
                        Item = candidate,
                        Count = selectedCount
                    };
                    remainingCounts[candidate.TemplateId] = remaining - selectedCount;
                }

                HashSet<string> newlyUnsafeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (ExactStashSelection selection in selectionsById.Values)
                {
                    if (selection.Count < Mathf.Max(1, selection.Item.StackObjectsCount)
                        && CollectDeepItemTree(selection.Item).Count > 1)
                    {
                        newlyUnsafeIds.Add(selection.Item.Id);
                    }
                }

                foreach (Item descendant in allStashItems)
                {
                    if (descendant == null
                        || ReferenceEquals(descendant, stash)
                        || IsIgnoredKitRequirementItem(descendant))
                    {
                        continue;
                    }

                    foreach (Item parent in descendant.GetAllParentItems())
                    {
                        if (parent == null
                            || ReferenceEquals(parent, stash)
                            || !selectionsById.TryGetValue(parent.Id, out ExactStashSelection parentSelection)
                            || parentSelection.Count < Mathf.Max(1, parent.StackObjectsCount))
                        {
                            continue;
                        }

                        if (!selectionsById.TryGetValue(descendant.Id, out ExactStashSelection descendantSelection)
                            || descendantSelection.Count < Mathf.Max(1, descendant.StackObjectsCount))
                        {
                            newlyUnsafeIds.Add(parent.Id);
                        }
                    }
                }

                if (newlyUnsafeIds.Count == 0)
                {
                    List<FriendlyTeammateBuyKitUsedItem> result = selectionsById.Values
                        .Select(selection => new FriendlyTeammateBuyKitUsedItem
                        {
                            itemId = selection.Item.Id,
                            templateId = selection.Item.TemplateId,
                            count = selection.Count
                        })
                        .ToList();
                    fulfilledCounts = result
                        .GroupBy(item => item.templateId, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Sum(item => item.count), StringComparer.Ordinal);
                    return result;
                }

                int previousUnsafeCount = unsafeContainerIds.Count;
                unsafeContainerIds.UnionWith(newlyUnsafeIds);
                if (unsafeContainerIds.Count == previousUnsafeCount)
                {
                    break;
                }
            }

            return new List<FriendlyTeammateBuyKitUsedItem>();
        }

        private static bool IsLockedForStashUse(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.PinLockState == EItemPinLockState.Locked)
            {
                return true;
            }

            foreach (Item parent in item.GetAllParentItems())
            {
                if (parent?.PinLockState == EItemPinLockState.Locked)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CalculateItemsMarketRoublePrice(IEnumerable<Item> items)
        {
            if (items == null)
            {
                return 0;
            }

            double total = 0.0;
            HashSet<string> pricedItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in items)
            {
                if (item == null)
                {
                    continue;
                }

                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem)
                        || string.IsNullOrWhiteSpace(subItem.Id)
                        || !pricedItemIds.Add(subItem.Id))
                    {
                        continue;
                    }

                    total += CalculateSingleItemMarketRoublePrice(subItem);
                }
            }

            return Mathf.Max(0, Convert.ToInt32(Math.Floor(total)));
        }

        private static double CalculateItemTreeMarketRoublePrice(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            try
            {
                double total = 0.0;
                // Walk each sub-item explicitly instead of pricing the root tree with
                // CalculateBasePriceForAllItems. EFT's tree helper can return zero for
                // nested containers, which is correct for buyout validation but wrong
                // for our visible "what would this kit cost" estimate.
                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem))
                    {
                        continue;
                    }

                    total += CalculateSingleItemMarketRoublePrice(subItem);
                }

                return total;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to calculate market build item price for '{item.Id}': {ex.Message}");
                return 0.0;
            }
        }

        private static double CalculateSingleItemMarketRoublePrice(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            double price = EFT.Trading.PriceCalculator.CalculateBuyoutBasePriceForSingleItem(
                item,
                0,
                BuildMarketPriceSource,
                true);

            return price > 0.0
                ? EFT.Trading.PriceCalculator.ApplyCustomPriceIfNeeded(item, price)
                : 0.0;
        }

        private static double CalculateKitLoadoutItemMarketRoublePrice(Item item, KitLoadoutPricingContext pricingContext)
        {
            if (item == null)
            {
                return 0.0;
            }

            if (pricingContext != null && pricingContext.TryGetItemPriceOverride(item, out double overridePrice))
            {
                return overridePrice;
            }

            return CalculateSingleItemMarketRoublePrice(item);
        }

        private static KitLoadoutPricingContext CreateKitLoadoutPricingContext(InventoryEquipment equipment)
        {
            KitLoadoutPricingContext context = new KitLoadoutPricingContext();
            if (equipment == null)
            {
                return context;
            }

            if (DisableKitLoadoutDiscountPricing)
            {
                context.AddDiagnostic("pricingMode raw-market-only discountsDisabled=True traderPricingDisabled=True");
                return context;
            }

            List<Item> allItems = CollectEquipmentBuildItems(equipment);
            Weapon primaryWeapon = GetEquipmentWeapon(equipment, EquipmentSlot.FirstPrimaryWeapon);
            Weapon secondaryWeapon = GetEquipmentWeapon(equipment, EquipmentSlot.SecondPrimaryWeapon);
            Weapon pistol = GetEquipmentWeapon(equipment, EquipmentSlot.Holster);

            bool hasArmor = HasKitArmor(equipment);
            bool hasHelmet = HasKitHelmet(equipment);
            bool primaryHasSupply = HasPrimaryWeaponSupply(primaryWeapon, allItems);
            bool secondaryHasSupply = HasAnyWeaponSupply(secondaryWeapon, allItems);
            bool pistolHasSupply = HasAnyWeaponSupply(pistol, allItems);

            double primaryDiscount = CalculatePrimaryWeaponDiscount(primaryWeapon, hasArmor, hasHelmet, primaryHasSupply);
            double secondaryDiscount = CalculateSecondaryWeaponDiscount(secondaryWeapon, secondaryHasSupply, primaryDiscount);
            double pistolDiscount = CalculatePistolDiscount(pistol, pistolHasSupply, primaryDiscount, secondaryDiscount);

            context.AddDiagnostic($"primaryFallback discount={FormatPercent(primaryDiscount)} weaponPresent={primaryWeapon != null} armor={hasArmor} helmet={hasHelmet} supply={primaryHasSupply}");
            context.AddDiagnostic($"secondaryFallback discount={FormatPercent(secondaryDiscount)} weaponPresent={secondaryWeapon != null} supply={secondaryHasSupply} primaryAtMax={primaryDiscount + DiscountComparisonTolerance >= PrimaryWeaponMaxDiscount}");
            context.AddDiagnostic($"pistolFallback discount={FormatPercent(pistolDiscount)} weaponPresent={pistol != null} supply={pistolHasSupply} primaryDiscounted={primaryDiscount > 0.0} secondaryDiscounted={secondaryDiscount > 0.0}");

            ApplyWeaponTreePricing(context, primaryWeapon, primaryDiscount, "primary");
            ApplyWeaponTreePricing(context, secondaryWeapon, secondaryDiscount, "secondary");
            ApplyWeaponTreePricing(context, pistol, pistolDiscount, "pistol");
            ApplyArmorPlateSystemPricing(context, equipment.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem, "armorVest");
            ApplyArmorPlateSystemPricing(context, equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem, "tacticalVest");
            return context;
        }

        private static Weapon GetEquipmentWeapon(InventoryEquipment equipment, EquipmentSlot slot)
        {
            return equipment?.GetSlot(slot)?.ContainedItem as Weapon;
        }

        private static double CalculatePrimaryWeaponDiscount(Weapon weapon, bool hasArmor, bool hasHelmet, bool hasSupply)
        {
            if (weapon == null)
            {
                return 0.0;
            }

            if (!hasArmor && !hasHelmet && !hasSupply)
            {
                return 0.0;
            }

            double discount = PrimaryWeaponBaseDiscount;
            if (hasArmor)
            {
                discount += PrimaryWeaponArmorDiscount;
            }

            if (hasHelmet)
            {
                discount += PrimaryWeaponHelmetDiscount;
            }

            if (hasSupply)
            {
                discount += PrimaryWeaponSupplyDiscount;
            }

            return Math.Min(PrimaryWeaponMaxDiscount, discount);
        }

        private static double CalculateSecondaryWeaponDiscount(Weapon weapon, bool hasSupply, double primaryDiscount)
        {
            if (weapon == null || primaryDiscount + DiscountComparisonTolerance < PrimaryWeaponMaxDiscount)
            {
                return 0.0;
            }

            double discount = SecondaryWeaponBaseDiscount;
            if (hasSupply)
            {
                discount += SecondaryWeaponSupplyDiscount;
            }

            return Math.Min(SecondaryWeaponMaxDiscount, discount);
        }

        private static double CalculatePistolDiscount(Weapon weapon, bool hasSupply, double primaryDiscount, double secondaryDiscount)
        {
            if (weapon == null || primaryDiscount <= 0.0 || secondaryDiscount <= 0.0)
            {
                return 0.0;
            }

            double discount = PistolBaseDiscount;
            if (hasSupply)
            {
                discount += PistolSupplyDiscount;
            }

            return Math.Min(PistolMaxDiscount, discount);
        }

        private static bool HasKitArmor(InventoryEquipment equipment)
        {
            if (equipment == null)
            {
                return false;
            }

            if (equipment.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem != null)
            {
                return true;
            }

            Item tacticalVest = equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            return IsArmoredTacticalVest(tacticalVest);
        }

        private static bool HasKitHelmet(InventoryEquipment equipment)
        {
            return equipment?.GetSlot(EquipmentSlot.Headwear)?.ContainedItem is EFT.InventoryLogic.Headwear;
        }

        private static bool IsArmoredTacticalVest(Item item)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                return item.TryGetItemComponent<ArmorHolderComponent>(out _)
                    || item.GetItemComponentsInChildren<ArmorComponent>(true).Any()
                    || item.GetItemComponentsInChildren<CompositeArmorComponent>(true).Any();
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyArmorPlateSystemPricing(KitLoadoutPricingContext context, Item armorItem, string itemLabel)
        {
            if (context == null || armorItem == null)
            {
                return;
            }

            if (!TryGetArmorPlateCoverageDiscount(armorItem, out double discount, out int filledSlots, out int totalSlots))
            {
                context.AddDiagnostic($"{itemLabel}ArmorPricing noPlateSystem");
                return;
            }

            context.AddDiagnostic($"{itemLabel}ArmorPricing filledPlates={filledSlots}/{totalSlots} discount={FormatPercent(discount)}");
            if (discount <= 0.0)
            {
                return;
            }

            List<Item> priceableItems = CollectArmorPlateSystemPriceableItems(armorItem);
            if (priceableItems.Count == 0)
            {
                context.AddDiagnostic($"{itemLabel}ArmorPricing noPriceableItems");
                return;
            }

            ApplyDiscountedItemPricing(context, priceableItems, discount, $"{itemLabel} armor plates {FormatPercent(discount)}");
        }

        private static bool TryGetArmorPlateCoverageDiscount(Item armorItem, out double discount, out int filledSlots, out int totalSlots)
        {
            discount = 0.0;
            filledSlots = 0;
            totalSlots = 0;
            if (armorItem == null)
            {
                return false;
            }

            try
            {
                if (!armorItem.TryGetItemComponent<ArmorHolderComponent>(out ArmorHolderComponent armorHolder)
                    || armorHolder?.ArmorSlots == null)
                {
                    return false;
                }

                List<ArmorSlot> armorSlots = armorHolder.ArmorSlots.Where(slot => slot != null).ToList();
                totalSlots = armorSlots.Count;
                if (totalSlots <= 0)
                {
                    return false;
                }

                filledSlots = armorSlots.Count(slot => slot.ContainedItem != null);
                discount = ArmorPlateSystemMaxDiscount * ((double)filledSlots / totalSlots);
                return true;
            }
            catch
            {
                discount = 0.0;
                filledSlots = 0;
                totalSlots = 0;
                return false;
            }
        }

        private static List<Item> CollectArmorPlateSystemPriceableItems(Item armorItem)
        {
            List<Item> items = new List<Item>();
            if (armorItem == null)
            {
                return items;
            }

            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            if (!IsIgnoredKitRequirementItem(armorItem)
                && !string.IsNullOrWhiteSpace(armorItem.Id)
                && seenItemIds.Add(armorItem.Id))
            {
                items.Add(armorItem);
            }

            void AddArmorSlotTree(Item rootItem)
            {
                foreach (Item item in CollectDeepItemTree(rootItem))
                {
                    if (IsIgnoredKitRequirementItem(item)
                        || string.IsNullOrWhiteSpace(item?.Id)
                        || !seenItemIds.Add(item.Id))
                    {
                        continue;
                    }

                    items.Add(item);
                }
            }

            if (!armorItem.TryGetItemComponent<ArmorHolderComponent>(out ArmorHolderComponent armorHolder)
                || armorHolder?.ArmorSlots == null)
            {
                return items;
            }

            foreach (ArmorSlot armorSlot in armorHolder.ArmorSlots)
            {
                if (armorSlot?.ContainedItem == null)
                {
                    continue;
                }

                AddArmorSlotTree(armorSlot.ContainedItem);
            }

            return items;
        }

        private static bool HasPrimaryWeaponSupply(Weapon weapon, List<Item> allItems)
        {
            if (weapon == null)
            {
                return false;
            }

            return UsesExternalSpareMagazine(weapon)
                ? HasSpareMagazineForWeapon(weapon, allItems)
                : HasCompatibleLooseAmmoForWeapon(weapon, allItems);
        }

        private static bool HasAnyWeaponSupply(Weapon weapon, List<Item> allItems)
        {
            return HasSpareMagazineForWeapon(weapon, allItems)
                || HasCompatibleLooseAmmoForWeapon(weapon, allItems);
        }

        private static bool UsesExternalSpareMagazine(Weapon weapon)
        {
            if (weapon == null)
            {
                return false;
            }

            return weapon.ReloadMode == Weapon.EReloadMode.ExternalMagazine
                || weapon.ReloadMode == Weapon.EReloadMode.ExternalMagazineWithInternalReloadSupport;
        }

        private static bool HasSpareMagazineForWeapon(Weapon weapon, List<Item> allItems)
        {
            if (weapon == null || allItems == null)
            {
                return false;
            }

            Slot magazineSlot = GetWeaponMagazineSlot(weapon);
            EFT.InventoryLogic.Magazine currentMagazine = weapon.GetCurrentMagazine();
            foreach (EFT.InventoryLogic.Magazine magazine in allItems.OfType<EFT.InventoryLogic.Magazine>())
            {
                if (magazine == null || IsInsideItemTree(magazine, weapon))
                {
                    continue;
                }

                if (currentMagazine != null && string.Equals(magazine.TemplateId, currentMagazine.TemplateId, StringComparison.Ordinal))
                {
                    return true;
                }

                if (magazineSlot != null && magazineSlot.CanAccept(magazine))
                {
                    return true;
                }
            }

            return false;
        }

        private static Slot GetWeaponMagazineSlot(Weapon weapon)
        {
            try
            {
                return weapon?.GetMagazineSlot();
            }
            catch
            {
                return null;
            }
        }

        private static bool HasCompatibleLooseAmmoForWeapon(Weapon weapon, List<Item> allItems)
        {
            if (weapon == null || allItems == null)
            {
                return false;
            }

            foreach (EFT.InventoryLogic.Ammo ammo in allItems.OfType<EFT.InventoryLogic.Ammo>())
            {
                if (ammo == null
                    || ammo.StackObjectsCount <= 0
                    || IsInsideItemTree(ammo, weapon))
                {
                    continue;
                }

                if (IsAmmoCompatibleWithWeapon(weapon, ammo))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAmmoCompatibleWithWeapon(Weapon weapon, EFT.InventoryLogic.Ammo ammo)
        {
            if (weapon == null || ammo == null)
            {
                return false;
            }

            try
            {
                if (weapon.Chambers != null)
                {
                    foreach (Slot chamber in weapon.Chambers)
                    {
                        if (chamber != null && chamber.CanAccept(ammo))
                        {
                            return true;
                        }
                    }
                }

                EFT.InventoryLogic.Magazine currentMagazine = weapon.GetCurrentMagazine();
                return currentMagazine?.Cartridges?.Filters?.CheckItemFilter(ammo) == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInsideItemTree(Item item, Item root)
        {
            if (item == null || root == null)
            {
                return false;
            }

            if (string.Equals(item.Id, root.Id, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (Item parent in item.GetAllParentItems())
            {
                if (parent != null && string.Equals(parent.Id, root.Id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyWeaponTreePricing(KitLoadoutPricingContext context, Weapon weapon, double fallbackDiscount, string weaponLabel)
        {
            if (context == null || weapon == null)
            {
                return;
            }

            List<Item> priceableItems = CollectWeaponTreePriceableItems(weapon);
            if (priceableItems.Count == 0)
            {
                return;
            }

            if (TryGetBestRagfairWeaponTreePricingPlan(weapon, priceableItems, out RagfairWeaponOfferPricingPlan ragfairPlan))
            {
                context.AddDiagnostic($"{weaponLabel}RagfairMatch coverage={FormatPercent(ragfairPlan.TargetCoverage)} candidateCoverage={FormatPercent(ragfairPlan.CandidateCoverage)} adjustedPrice={Math.Floor(ragfairPlan.TotalPrice)} offer={ragfairPlan.SourceDescription}");
                ApplyDistributedWeaponTreePrice(context, priceableItems, ragfairPlan.TotalPrice, $"{weaponLabel} ragfair near-match");
                return;
            }

            if (TryGetBestTraderWeaponTreePricingPlan(weapon, priceableItems, out TraderWeaponOfferPricingPlan traderPlan))
            {
                context.AddDiagnostic($"{weaponLabel}TraderOverlap matchedTemplates={traderPlan.MatchedTemplateCounts.Count} scaledPrice={Math.Floor(traderPlan.TotalPrice)} fallbackDiscount={FormatPercent(fallbackDiscount)} offer={traderPlan.SourceDescription}");
                ApplyTraderWeaponOfferPricingPlan(context, priceableItems, fallbackDiscount, traderPlan, weaponLabel);
                return;
            }

            if (fallbackDiscount <= 0.0)
            {
                context.AddDiagnostic($"{weaponLabel}Pricing noTraderOverlap fallbackDiscount=0%");
                return;
            }

            context.AddDiagnostic($"{weaponLabel}Pricing fallbackOnly discount={FormatPercent(fallbackDiscount)}");
            ApplyDiscountedItemPricing(context, priceableItems, fallbackDiscount, $"{weaponLabel} fallback {FormatPercent(fallbackDiscount)}");
        }

        private static bool TryGetBestRagfairWeaponTreePricingPlan(
            Weapon targetWeapon,
            List<Item> targetPriceableItems,
            out RagfairWeaponOfferPricingPlan pricingPlan)
        {
            pricingPlan = null;
            if (targetWeapon == null || targetPriceableItems == null || targetPriceableItems.Count == 0 || _session == null)
            {
                return false;
            }

            Dictionary<string, int> targetCounts = CreateWeaponTreeTemplateCounts(targetWeapon);
            if (targetCounts.Count == 0)
            {
                return false;
            }

            string cacheKey = CreateWeaponTreeTemplateCountsKey(targetCounts);
            if (RagfairWeaponOfferPricingPlanCache.TryGetValue(cacheKey, out RagfairWeaponOfferPricingPlan cachedPlan))
            {
                pricingPlan = cachedPlan;
                return pricingPlan?.HasMatch == true;
            }

            if (PendingRagfairWeaponOfferPricingPlanKeys.Add(cacheKey))
            {
                double targetConditionPercent = GetRagfairRootConditionPercent(targetWeapon);
                RequestRagfairWeaponTreePricingPlanAsync(cacheKey, targetWeapon.TemplateId, targetPriceableItems, targetCounts, targetConditionPercent);
            }

            return false;
        }

        private static async void RequestRagfairWeaponTreePricingPlanAsync(
            string cacheKey,
            string weaponTemplateId,
            List<Item> targetPriceableItems,
            Dictionary<string, int> targetCounts,
            double targetConditionPercent)
        {
            try
            {
                RagfairWeaponOfferPricingPlan plan = await BuildRagfairWeaponOfferPricingPlanAsync(weaponTemplateId, targetPriceableItems, targetCounts, targetConditionPercent);
                RagfairWeaponOfferPricingPlanCache[cacheKey] = plan ?? RagfairWeaponOfferPricingPlan.NoMatch;
                if (EnableKitLoadoutPricingDiagnostics)
                {
                    if (plan?.HasMatch == true)
                    {
                        Modules.Logger.LogInfo($"[UI][KitPrice][Ragfair] weaponTpl={weaponTemplateId} result=match adjustedPrice={Math.Floor(plan.TotalPrice)} coverage={FormatPercent(plan.TargetCoverage)} offer={plan.SourceDescription}");
                    }
                    else
                    {
                        double targetRawPrice = CalculateItemsRawPrice(targetPriceableItems);
                        Modules.Logger.LogInfo($"[UI][KitPrice][Ragfair] weaponTpl={weaponTemplateId} result=noMatch targetItems={targetPriceableItems?.Count ?? 0} targetRaw={Math.Floor(targetRawPrice)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[UI] Ragfair weapon offer pricing failed for '{weaponTemplateId}': {ex.Message}");
                RagfairWeaponOfferPricingPlanCache[cacheKey] = RagfairWeaponOfferPricingPlan.NoMatch;
            }
            finally
            {
                PendingRagfairWeaponOfferPricingPlanKeys.Remove(cacheKey);
                if (EnableKitLoadoutPricingDiagnostics)
                {
                    LoggedBuildPricingKeys.Clear();
                    _buildPricingLogPhase = "ragfair-refresh";
                }

                RefreshActivePriceLabels();
            }
        }

        private static async Task<RagfairWeaponOfferPricingPlan> BuildRagfairWeaponOfferPricingPlanAsync(
            string weaponTemplateId,
            List<Item> targetPriceableItems,
            Dictionary<string, int> targetCounts,
            double targetConditionPercent)
        {
            if (string.IsNullOrWhiteSpace(weaponTemplateId)
                || targetPriceableItems == null
                || targetPriceableItems.Count == 0
                || targetCounts == null
                || targetCounts.Count == 0
                || _session == null)
            {
                return RagfairWeaponOfferPricingPlan.NoMatch;
            }

            string handbookId = ResolveRagfairHandbookIdForItem(weaponTemplateId);
            int conditionFrom = GetRagfairConditionFromPercent(targetConditionPercent);
            Result<OffersList> result = await _session.GetOffers(
                page: 0,
                limit: RagfairWeaponOfferPageSize,
                sortType: (int)ESortType.Price,
                direction: false,
                currency: 1,
                priceFrom: 0,
                priceTo: 0,
                quantityFrom: 0,
                quantityTo: 0,
                conditionFrom: conditionFrom,
                conditionTo: 100,
                oneHourExpiration: false,
                removeBartering: true,
                offerOwnerType: (int)EOfferOwnerType.AnyOwnerType,
                onlyFunctional: true,
                handbookId: handbookId,
                linkedSearchId: string.Empty,
                neededSearchId: string.Empty,
                buildItems: new Dictionary<string, int>(),
                buildCount: 0,
                updateOfferCount: false);

            if (!result.Succeed || result.Value?.offers == null || result.Value.offers.Length == 0)
            {
                if (EnableKitLoadoutPricingDiagnostics)
                {
                    Modules.Logger.LogInfo($"[UI][KitPrice][Ragfair] weaponTpl={weaponTemplateId} handbookId={handbookId} result=emptyOrFailed succeed={result.Succeed} offers={result.Value?.offers?.Length ?? 0}");
                }

                return RagfairWeaponOfferPricingPlan.NoMatch;
            }

            double targetRawPrice = CalculateItemsRawPrice(targetPriceableItems);
            if (targetRawPrice <= 0.0)
            {
                return RagfairWeaponOfferPricingPlan.NoMatch;
            }

            RagfairWeaponOfferPricingPlan bestPlan = null;
            int rejectedEligibility = 0;
            int rejectedPrice = 0;
            int rejectedEmptyTree = 0;
            int rejectedOverlap = 0;
            int rejectedCoverage = 0;
            int rejectedCondition = 0;
            int rejectedRaw = 0;
            foreach (Offer offer in result.Value.offers)
            {
                if (offer?.Item is not Weapon candidateWeapon
                    || !offer.CanBeBought
                    || !offer.OnlyMoney
                    || !string.Equals(candidateWeapon.TemplateId, weaponTemplateId, StringComparison.Ordinal))
                {
                    rejectedEligibility++;
                    continue;
                }

                if (!TryGetRagfairOfferRoublePrice(offer, out double offerPrice) || offerPrice <= 0.0)
                {
                    rejectedPrice++;
                    continue;
                }

                double candidateConditionPercent = GetRagfairRootConditionPercent(candidateWeapon);
                if (targetConditionPercent > 0.0
                    && candidateConditionPercent + RagfairWeaponConditionTolerancePercent < targetConditionPercent)
                {
                    rejectedCondition++;
                    continue;
                }

                List<Item> candidatePriceableItems = CollectWeaponTreePriceableItems(candidateWeapon);
                if (candidatePriceableItems.Count == 0)
                {
                    rejectedEmptyTree++;
                    continue;
                }

                Dictionary<string, int> candidateCounts = CreateWeaponTreeTemplateCounts(candidateWeapon);
                if (!TryCreateOfferOverlapMatch(targetCounts, candidateCounts, weaponTemplateId, out Dictionary<string, int> matchedTemplateCounts))
                {
                    rejectedOverlap++;
                    continue;
                }

                double matchedTargetRawPrice = CalculateMatchedTemplateRawPrice(targetPriceableItems, matchedTemplateCounts);
                if (matchedTargetRawPrice <= 0.0)
                {
                    rejectedRaw++;
                    continue;
                }

                double targetCoverage = matchedTargetRawPrice / targetRawPrice;
                if (targetCoverage + DiscountComparisonTolerance < RagfairWeaponOfferSimilarityThreshold)
                {
                    rejectedCoverage++;
                    continue;
                }

                double candidateRawPrice = CalculateItemsRawPrice(candidatePriceableItems);
                if (candidateRawPrice <= 0.0)
                {
                    rejectedRaw++;
                    continue;
                }

                double matchedCandidateRawPrice = CalculateMatchedTemplateRawPrice(candidatePriceableItems, matchedTemplateCounts);
                if (matchedCandidateRawPrice <= 0.0)
                {
                    rejectedRaw++;
                    continue;
                }

                double candidateCoverage = matchedCandidateRawPrice / candidateRawPrice;
                double unmatchedTargetRawPrice = Math.Max(0.0, targetRawPrice - matchedTargetRawPrice);
                double unmatchedCandidateRawPrice = Math.Max(0.0, candidateRawPrice - matchedCandidateRawPrice);
                double adjustedPrice = Math.Max(0.0, offerPrice - unmatchedCandidateRawPrice + unmatchedTargetRawPrice);

                RagfairWeaponOfferPricingPlan candidatePlan = new RagfairWeaponOfferPricingPlan
                {
                    HasMatch = true,
                    TotalPrice = adjustedPrice,
                    TargetCoverage = targetCoverage,
                    CandidateCoverage = candidateCoverage,
                    SourceDescription = $"{GetItemDisplayName(candidateWeapon)} offerPrice={Math.Floor(offerPrice)} targetCoverage={FormatPercent(targetCoverage)} candidateCoverage={FormatPercent(candidateCoverage)} targetCondition={FormatPercent(targetConditionPercent / 100.0)} candidateCondition={FormatPercent(candidateConditionPercent / 100.0)} deltaAdd={Math.Floor(unmatchedTargetRawPrice)} deltaRemove={Math.Floor(unmatchedCandidateRawPrice)}"
                };

                if (bestPlan == null
                    || candidatePlan.TargetCoverage > bestPlan.TargetCoverage + DiscountComparisonTolerance
                    || (candidatePlan.TargetCoverage.ApproxEquals(bestPlan.TargetCoverage) && candidatePlan.CandidateCoverage > bestPlan.CandidateCoverage + DiscountComparisonTolerance)
                    || (candidatePlan.TargetCoverage.ApproxEquals(bestPlan.TargetCoverage) && candidatePlan.CandidateCoverage.ApproxEquals(bestPlan.CandidateCoverage) && candidatePlan.TotalPrice < bestPlan.TotalPrice))
                {
                    bestPlan = candidatePlan;
                }
            }

            if (bestPlan == null && EnableKitLoadoutPricingDiagnostics)
            {
                Modules.Logger.LogInfo($"[UI][KitPrice][Ragfair] weaponTpl={weaponTemplateId} handbookId={handbookId} conditionFrom={conditionFrom} offers={result.Value.offers.Length} targetRaw={Math.Floor(targetRawPrice)} result=noUsableOffer rejectedEligibility={rejectedEligibility} rejectedPrice={rejectedPrice} rejectedCondition={rejectedCondition} rejectedEmptyTree={rejectedEmptyTree} rejectedOverlap={rejectedOverlap} rejectedCoverage={rejectedCoverage} rejectedRaw={rejectedRaw}");
            }

            return bestPlan ?? RagfairWeaponOfferPricingPlan.NoMatch;
        }

        private static int GetRagfairConditionFromPercent(double targetConditionPercent)
        {
            if (targetConditionPercent <= 0.0)
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.FloorToInt((float)(targetConditionPercent - RagfairWeaponConditionTolerancePercent)),
                0,
                100);
        }

        private static double GetRagfairRootConditionPercent(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            try
            {
                if (item.TryGetItemComponent<RepairableComponent>(out RepairableComponent repairable)
                    && repairable != null
                    && repairable.TemplateDurability > 0)
                {
                    return Math.Max(0.0, Math.Min(100.0, repairable.Durability / repairable.TemplateDurability * 100.0));
                }
            }
            catch
            {
                return 0.0;
            }

            return 0.0;
        }

        private static string ResolveRagfairHandbookIdForItem(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId) || !Singleton<EFT.HandBook.Handbook>.Instantiated)
            {
                return templateId ?? string.Empty;
            }

            try
            {
                EFT.HandBook.HandbookNode node = Singleton<EFT.HandBook.Handbook>.Instance[templateId];
                return !string.IsNullOrWhiteSpace(node?.Data?.Id) ? node.Data.Id : templateId;
            }
            catch
            {
                return templateId;
            }
        }

        private static bool TryGetRagfairOfferRoublePrice(Offer offer, out double price)
        {
            price = 0.0;
            if (offer?.Requirements == null || offer.Requirements.Length != 1 || !offer.OnlyMoney)
            {
                return false;
            }

            IExchangeRequirement requirement = offer.Requirements[0];
            if (requirement == null
                || !string.Equals(requirement.TemplateId, EFT.InventoryLogic.CurrencyUtil.ROUBLE_ID.ToString(), StringComparison.Ordinal)
                    && !string.Equals(requirement.TemplateId, EFT.InventoryLogic.CurrencyUtil.ROUBLE_STACK_ID.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            price = requirement.IntCount;
            return price > 0.0;
        }

        private static List<Item> CollectWeaponTreePriceableItems(Weapon weapon)
        {
            List<Item> items = new List<Item>();
            if (weapon == null)
            {
                return items;
            }

            foreach (Item item in CollectDeepItemTree(weapon))
            {
                if (IsIgnoredKitRequirementItem(item)
                    || item is EFT.InventoryLogic.Ammo
                    || string.IsNullOrWhiteSpace(item?.Id))
                {
                    continue;
                }

                items.Add(item);
            }

            return items;
        }

        private static void ApplyTraderWeaponOfferPricingPlan(
            KitLoadoutPricingContext context,
            List<Item> priceableItems,
            double fallbackDiscount,
            TraderWeaponOfferPricingPlan traderPlan,
            string weaponLabel)
        {
            if (context == null || priceableItems == null || priceableItems.Count == 0 || traderPlan?.MatchedTemplateCounts == null)
            {
                return;
            }

            List<Item> matchedItems = ConsumeMatchedTemplateItems(priceableItems, traderPlan.MatchedTemplateCounts);
            if (matchedItems.Count == 0)
            {
                ApplyDiscountedItemPricing(context, priceableItems, fallbackDiscount, $"{weaponLabel} fallback {FormatPercent(fallbackDiscount)}");
                return;
            }

            ApplyDistributedWeaponTreePrice(context, matchedItems, traderPlan.TotalPrice, $"{weaponLabel} trader overlap");

            HashSet<string> matchedItemIds = new HashSet<string>(
                matchedItems.Where(item => !string.IsNullOrWhiteSpace(item?.Id)).Select(item => item.Id),
                StringComparer.Ordinal);

            if (matchedItemIds.Count == priceableItems.Count)
            {
                return;
            }

            List<Item> unmatchedItems = priceableItems
                .Where(item => item != null && !matchedItemIds.Contains(item.Id))
                .ToList();
            ApplyDiscountedItemPricing(context, unmatchedItems, fallbackDiscount, $"{weaponLabel} fallback {FormatPercent(fallbackDiscount)}");
        }

        private static void ApplyDiscountedItemPricing(KitLoadoutPricingContext context, List<Item> priceableItems, double discount, string rule)
        {
            if (context == null || priceableItems == null || priceableItems.Count == 0 || discount <= 0.0)
            {
                return;
            }

            double multiplier = Math.Max(0.0, 1.0 - discount);
            foreach (Item item in priceableItems)
            {
                context.SetItemPriceOverride(item, CalculateSingleItemMarketRoublePrice(item) * multiplier, rule);
            }
        }

        private static void ApplyDistributedWeaponTreePrice(KitLoadoutPricingContext context, List<Item> priceableItems, double totalPrice, string rule)
        {
            if (context == null || priceableItems == null || priceableItems.Count == 0 || totalPrice <= 0.0)
            {
                return;
            }

            Dictionary<Item, double> rawPrices = new Dictionary<Item, double>();
            double rawTotal = 0.0;
            foreach (Item item in priceableItems)
            {
                double rawPrice = Math.Max(0.0, CalculateSingleItemMarketRoublePrice(item));
                rawPrices[item] = rawPrice;
                rawTotal += rawPrice;
            }

            if (rawTotal <= 0.0)
            {
                context.SetItemPriceOverride(priceableItems[0], totalPrice, rule);
                return;
            }

            double assigned = 0.0;
            for (int i = 0; i < priceableItems.Count; i++)
            {
                Item item = priceableItems[i];
                double itemPrice = i == priceableItems.Count - 1
                    ? Math.Max(0.0, totalPrice - assigned)
                    : totalPrice * (rawPrices[item] / rawTotal);
                assigned += itemPrice;
                context.SetItemPriceOverride(item, itemPrice, rule);
            }
        }

        private static bool TryGetBestTraderWeaponTreePricingPlan(
            Weapon targetWeapon,
            List<Item> targetPriceableItems,
            out TraderWeaponOfferPricingPlan pricingPlan)
        {
            pricingPlan = null;
            if (targetWeapon == null || _session?.Traders == null)
            {
                return false;
            }

            Dictionary<string, int> targetCounts = CreateWeaponTreeTemplateCounts(targetWeapon);
            if (targetCounts.Count == 0)
            {
                return false;
            }

            string cacheKey = CreateWeaponTreeTemplateCountsKey(targetCounts);
            if (TraderWeaponOfferPricingPlanCache.TryGetValue(cacheKey, out TraderWeaponOfferPricingPlan cachedPlan))
            {
                pricingPlan = cachedPlan;
                return pricingPlan?.MatchedTemplateCounts != null;
            }

            try
            {
                TraderWeaponOfferPricingPlan bestPlan = null;
                double bestSavings = 0.0;
                double bestCoverage = 0.0;
                foreach (EFT.Trading.Trader trader in _session.Traders)
                {
                    EFT.Trading.Assortment assortment = trader?.CurrentAssortment;
                    Item rootItem = assortment?.TraderController?.RootItem;
                    if (!IsTraderUsableForExactKitOffer(trader, assortment) || rootItem == null)
                    {
                        continue;
                    }

                    foreach (Weapon candidateWeapon in CollectDeepItemTree(rootItem).OfType<Weapon>())
                    {
                        if (HasParentWeapon(candidateWeapon)
                            || !IsTraderOfferAvailable(trader, assortment, candidateWeapon)
                            || !string.Equals(candidateWeapon.TemplateId, targetWeapon.TemplateId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        List<Item> candidatePriceableItems = CollectWeaponTreePriceableItems(candidateWeapon);
                        Dictionary<string, int> candidateCounts = CreateWeaponTreeTemplateCounts(candidateWeapon);
                        if (!AreTemplateCountsEqual(targetCounts, candidateCounts)
                            || !TryCreateOfferOverlapMatch(targetCounts, candidateCounts, targetWeapon.TemplateId, out Dictionary<string, int> matchedTemplateCounts))
                        {
                            continue;
                        }

                        BarterScheme scheme = assortment.GetSchemeForItem(candidateWeapon);
                        if (!TryCalculateBarterSchemeRoublePrice(scheme, out double offerPrice) || offerPrice <= 0.0)
                        {
                            continue;
                        }

                        double candidateRawPrice = CalculateItemsRawPrice(candidatePriceableItems);
                        if (candidateRawPrice <= 0.0)
                        {
                            continue;
                        }

                        double coverageRawPrice = CalculateMatchedTemplateRawPrice(targetPriceableItems, matchedTemplateCounts);
                        double matchedCandidateRawPrice = CalculateMatchedTemplateRawPrice(candidatePriceableItems, matchedTemplateCounts);
                        if (coverageRawPrice <= 0.0 || matchedCandidateRawPrice <= 0.0)
                        {
                            continue;
                        }

                        double scaledOfferPrice = offerPrice * (matchedCandidateRawPrice / candidateRawPrice);
                        if (scaledOfferPrice >= coverageRawPrice)
                        {
                            continue;
                        }

                        double savings = coverageRawPrice - scaledOfferPrice;
                        if (bestPlan == null
                            || savings > bestSavings
                            || (savings.ApproxEquals(bestSavings) && coverageRawPrice > bestCoverage)
                            || (savings.ApproxEquals(bestSavings) && coverageRawPrice.ApproxEquals(bestCoverage) && scaledOfferPrice < bestPlan.TotalPrice))
                        {
                            bestSavings = savings;
                            bestCoverage = coverageRawPrice;
                            bestPlan = new TraderWeaponOfferPricingPlan
                            {
                                MatchedTemplateCounts = new Dictionary<string, int>(matchedTemplateCounts, StringComparer.Ordinal),
                                TotalPrice = scaledOfferPrice,
                                SourceDescription = $"{trader.Settings.Nickname.Localized(null)}:{GetItemDisplayName(candidateWeapon)} exactMatch matchedRaw={Math.Floor(coverageRawPrice)} candidateRaw={Math.Floor(candidateRawPrice)} scaledOffer={Math.Floor(scaledOfferPrice)}"
                            };
                        }
                    }
                }

                pricingPlan = bestPlan ?? TraderWeaponOfferPricingPlan.NoMatch;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[UI] Exact trader weapon offer pricing failed for '{targetWeapon.Id}': {ex.Message}");
                pricingPlan = TraderWeaponOfferPricingPlan.NoMatch;
                TraderWeaponOfferPricingPlanCache[cacheKey] = pricingPlan;
                return false;
            }

            TraderWeaponOfferPricingPlanCache[cacheKey] = pricingPlan;
            return pricingPlan.MatchedTemplateCounts != null;
        }

        private static bool IsTraderUsableForExactKitOffer(EFT.Trading.Trader trader, EFT.Trading.Assortment assortment)
        {
            return trader?.Info != null
                && trader.Info.Unlocked
                && assortment != null
                && !trader.AssortmentLoading;
        }

        private static bool IsTraderOfferAvailable(EFT.Trading.Trader trader, EFT.Trading.Assortment assortment, Item item)
        {
            if (trader?.Info == null || assortment == null || item == null)
            {
                return false;
            }

            if (assortment.LoyalLevelItems != null
                && assortment.LoyalLevelItems.TryGetValue(item.Id, out int requiredLevel)
                && requiredLevel > trader.Info.LoyaltyLevel)
            {
                return false;
            }

            return assortment.GetSchemeForItem(item) != null;
        }

        private static bool HasParentWeapon(Item item)
        {
            if (item == null)
            {
                return false;
            }

            foreach (Item parent in item.GetAllParentItems())
            {
                if (parent is Weapon)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<string, int> CreateWeaponTreeTemplateCounts(Item weapon)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (weapon == null)
            {
                return counts;
            }

            foreach (Item item in CollectDeepItemTree(weapon))
            {
                if (IsIgnoredKitRequirementItem(item)
                    || item is EFT.InventoryLogic.Ammo
                    || string.IsNullOrWhiteSpace(item?.TemplateId))
                {
                    continue;
                }

                string templateId = item.TemplateId;
                int count = Mathf.Max(1, item.StackObjectsCount);
                if (counts.TryGetValue(templateId, out int existing))
                {
                    counts[templateId] = existing + count;
                }
                else
                {
                    counts[templateId] = count;
                }
            }

            return counts;
        }

        private static string CreateWeaponTreeTemplateCountsKey(Dictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("|", counts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
        }

        private static bool TryCreateOfferOverlapMatch(
            Dictionary<string, int> targetCounts,
            Dictionary<string, int> candidateCounts,
            string rootTemplateId,
            out Dictionary<string, int> matchedTemplateCounts)
        {
            matchedTemplateCounts = null;
            if (targetCounts == null || candidateCounts == null || targetCounts.Count == 0 || candidateCounts.Count == 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rootTemplateId)
                || !targetCounts.ContainsKey(rootTemplateId)
                || !candidateCounts.ContainsKey(rootTemplateId))
            {
                return false;
            }

            Dictionary<string, int> overlap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in candidateCounts)
            {
                if (!targetCounts.TryGetValue(pair.Key, out int targetCount))
                {
                    continue;
                }

                int overlapCount = Math.Min(targetCount, pair.Value);
                if (overlapCount > 0)
                {
                    overlap[pair.Key] = overlapCount;
                }
            }

            if (!overlap.TryGetValue(rootTemplateId, out int rootCount) || rootCount <= 0)
            {
                return false;
            }

            matchedTemplateCounts = overlap;
            return overlap.Count > 0;
        }

        private static double CalculateMatchedTemplateRawPrice(List<Item> targetPriceableItems, Dictionary<string, int> matchedTemplateCounts)
        {
            if (targetPriceableItems == null || matchedTemplateCounts == null || matchedTemplateCounts.Count == 0)
            {
                return 0.0;
            }

            double total = 0.0;
            foreach (Item item in ConsumeMatchedTemplateItems(targetPriceableItems, matchedTemplateCounts))
            {
                total += Math.Max(0.0, CalculateSingleItemMarketRoublePrice(item));
            }

            return total;
        }

        private static double CalculateItemsRawPrice(List<Item> items)
        {
            if (items == null || items.Count == 0)
            {
                return 0.0;
            }

            double total = 0.0;
            foreach (Item item in items)
            {
                total += Math.Max(0.0, CalculateSingleItemMarketRoublePrice(item));
            }

            return total;
        }

        private static List<Item> ConsumeMatchedTemplateItems(List<Item> targetPriceableItems, Dictionary<string, int> matchedTemplateCounts)
        {
            List<Item> matchedItems = new List<Item>();
            if (targetPriceableItems == null || matchedTemplateCounts == null || matchedTemplateCounts.Count == 0)
            {
                return matchedItems;
            }

            Dictionary<string, int> remainingCounts = new Dictionary<string, int>(matchedTemplateCounts, StringComparer.Ordinal);
            foreach (Item item in targetPriceableItems)
            {
                if (item == null
                    || string.IsNullOrWhiteSpace(item.TemplateId)
                    || !remainingCounts.TryGetValue(item.TemplateId, out int remaining)
                    || remaining <= 0)
                {
                    continue;
                }

                matchedItems.Add(item);
                remainingCounts[item.TemplateId] = remaining - 1;
            }

            return matchedItems;
        }

        private static bool AreTemplateCountsEqual(Dictionary<string, int> left, Dictionary<string, int> right)
        {
            if (left == null || right == null || left.Count != right.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, int> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out int rightCount) || rightCount != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryCalculateBarterSchemeRoublePrice(BarterScheme scheme, out double price)
        {
            price = 0.0;
            if (scheme == null || scheme.Count == 0)
            {
                return false;
            }

            double bestPrice = double.MaxValue;
            foreach (BarterVariant variant in scheme)
            {
                if (TryCalculateBarterVariantRoublePrice(variant, out double variantPrice)
                    && variantPrice > 0.0
                    && variantPrice < bestPrice)
                {
                    bestPrice = variantPrice;
                }
            }

            if (bestPrice == double.MaxValue)
            {
                return false;
            }

            price = bestPrice;
            return true;
        }

        private static bool TryCalculateBarterVariantRoublePrice(BarterVariant variant, out double price)
        {
            price = 0.0;
            if (variant == null || variant.Count == 0)
            {
                return false;
            }

            foreach (EFT.BarterTemplate requisite in variant)
            {
                if (requisite == null || string.IsNullOrWhiteSpace(requisite._tpl) || requisite.count <= 0.0)
                {
                    return false;
                }

                double unitPrice = GetTemplateRoubleValue(requisite._tpl);
                if (unitPrice <= 0.0)
                {
                    return false;
                }

                price += unitPrice * requisite.count;
            }

            return price > 0.0;
        }

        private static double GetTemplateRoubleValue(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return 0.0;
            }

            if (string.Equals(templateId, EFT.InventoryLogic.CurrencyUtil.ROUBLE_ID.ToString(), StringComparison.Ordinal)
                || string.Equals(templateId, EFT.InventoryLogic.CurrencyUtil.ROUBLE_STACK_ID.ToString(), StringComparison.Ordinal))
            {
                return 1.0;
            }

            try
            {
                return BuildMarketPriceSource.GetBasePrice(new MongoID(templateId));
            }
            catch
            {
                return 0.0;
            }
        }

        private static string FormatPercent(double value)
        {
            return $"{Math.Round(value * 100.0)}%";
        }

        private static List<Item> CollectDeepItemTree(Item item)
        {
            List<Item> items = new List<Item>();
            if (item == null)
            {
                return items;
            }

            // Equipment builds are full trees: weapons carry mods, rigs carry
            // contents, and armor can carry plate inserts. Keep price, requirement,
            // and stash-match math on the same explicit recursive traversal so the
            // confirmation quote cannot silently ignore nested gear.
            item.GetAllItemsNonAlloc(items, false, true);
            return items;
        }

        private static List<Item> CollectEquipmentBuildItems(InventoryEquipment equipment)
        {
            List<Item> allItems = new List<Item>();
            if (equipment == null)
            {
                return allItems;
            }

            HashSet<string> ignoredItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Slot slot in equipment.Slots)
            {
                if (!IsIgnoredEquipmentBuildSlot(slot) || slot.ContainedItem == null)
                {
                    continue;
                }

                foreach (Item item in CollectDeepItemTree(slot.ContainedItem))
                {
                    if (!string.IsNullOrWhiteSpace(item?.Id))
                    {
                        ignoredItemIds.Add(item.Id);
                    }
                }
            }

            equipment.GetAllItemsFromCollectionNonAlloc(allItems);

            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            List<Item> result = new List<Item>();
            foreach (Item item in allItems)
            {
                if (item == null
                    || ReferenceEquals(item, equipment)
                    || string.IsNullOrWhiteSpace(item.Id)
                    || ignoredItemIds.Contains(item.Id)
                    || !seenItemIds.Add(item.Id))
                {
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private static string GetItemDisplayName(Item item)
        {
            if (item == null)
            {
                return GetSocialUiText("UnknownItem");
            }

            string name = item.Name?.Localized(null);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string shortName = item.ShortName?.Localized(null);
            return !string.IsNullOrWhiteSpace(shortName) ? shortName : item.TemplateId;
        }

        private static void RequestMarketPrices(EFT.IEftSession session, bool forceRefresh)
        {
            if (session == null || _marketPricesRequestInFlight)
            {
                return;
            }

            bool shouldRefresh = forceRefresh
                || _marketPrices == null
                || Time.realtimeSinceStartup - _marketPricesUpdatedAt > MarketPricesRefreshIntervalSeconds;
            if (!shouldRefresh)
            {
                return;
            }

            _marketPricesRequestInFlight = true;
            // This is the same market-facing price table Ragfair uses to update
            // handbook node prices. It is a better visible estimate than Prapor's
            // sell-to-trader value and is cheap enough to cache per screen session.
            session.RagfairGetPrices(new Callback<Dictionary<string, float>>(HandleMarketPricesReceived));
        }

        private static void HandleMarketPricesReceived(Result<Dictionary<string, float>> result)
        {
            _marketPricesRequestInFlight = false;
            if (!result.Succeed || result.Value == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to load Ragfair market prices for teammate build pricing.");
                return;
            }

            _marketPrices = new Dictionary<string, float>(result.Value);
            _marketPricesUpdatedAt = Time.realtimeSinceStartup;
            TraderWeaponOfferPricingPlanCache.Clear();
            RagfairWeaponOfferPricingPlanCache.Clear();
            PendingRagfairWeaponOfferPricingPlanKeys.Clear();
            LoggedBuildPricingKeys.Clear();
            _buildPricingLogPhase = "market-refresh";
            UpdateHandbookPrices(_marketPrices);
            RefreshActivePriceLabels();
        }

        private static void UpdateHandbookPrices(Dictionary<string, float> prices)
        {
            if (prices == null || !Singleton<EFT.HandBook.Handbook>.Instantiated)
            {
                return;
            }

            EFT.HandBook.Handbook handbook = Singleton<EFT.HandBook.Handbook>.Instance;
            foreach (KeyValuePair<string, float> pair in prices)
            {
                EFT.HandBook.HandbookNode node = handbook[pair.Key];
                if (node != null)
                {
                    node.Data.Price = pair.Value;
                }
            }
        }

        private static void RefreshActivePriceLabels()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return;
            }

            if (_activeScreen != null)
            {
                ApplySelectedBuildPrice(_activeScreen);
            }

            foreach (BuildRowState state in BuildRowStates.Values)
            {
                if (state.View != null && state.Build != null)
                {
                    ApplyBuildListPrice(state.View, state.Build);
                }
            }
        }

        private static Dictionary<string, int> SummarizeMissingItems(IEnumerable<Item> missingItems)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in missingItems)
            {
                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem)
                        || string.IsNullOrWhiteSpace(subItem.TemplateId)
                        || !string.IsNullOrWhiteSpace(subItem.Id) && !seenItemIds.Add(subItem.Id))
                    {
                        continue;
                    }

                    int count = Mathf.Max(1, subItem.StackObjectsCount);
                    if (counts.TryGetValue(subItem.TemplateId, out int existing))
                    {
                        counts[subItem.TemplateId] = existing + count;
                    }
                    else
                    {
                        counts[subItem.TemplateId] = count;
                    }
                }
            }

            return counts;
        }

        private static void ApplyActionButtonText(Transform screen)
        {
            string label = GetSocialUiText("PurchaseKitAction");
            if (_excludeExistingItems && TryCreateBuyQuote(out EquipmentBuildBuyQuote quote) && quote.CanEquipFromStash)
            {
                label = GetSocialUiText("EquipKitAction");
            }

            Transform buttonTransform = FindEquipButtonTransform(screen);
            DefaultUIButton button = buttonTransform?.GetComponent<DefaultUIButton>();
            if (button != null)
            {
                button.SetRawText(label, 18);
            }

            TMP_Text text = buttonTransform?.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private static void HideCanEquipIcon(Transform screen)
        {
            Transform canEquip = FindCanEquipTransform(screen);
            if (canEquip != null)
            {
                canEquip.gameObject.SetActive(false);
            }
        }

        private static Transform FindEquipButtonTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/ButtonsPanel/EquipButtonGroup/EquipButton")
                ?? FindChildRecursive(screen, "EquipButton");
        }

        private static Transform FindCanEquipTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/ButtonsPanel/EquipButtonGroup/CanEquip")
                ?? FindChildRecursive(screen, "CanEquip");
        }

        private static Transform FindCurrentWeightTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/AdditionalInfoPanel/WeightPanel/CurrentWeight");
        }

        private static Dictionary<GameObject, bool> FindScreenChromeObjects(Transform screen)
        {
            Dictionary<GameObject, bool> objects = new Dictionary<GameObject, bool>();
            AddOriginalActive(objects, FindChildRecursive(screen, "BottomPanel"));
            AddOriginalActive(objects, FindChildRecursive(screen, "Bottom Panel"));
            AddOriginalActive(objects, FindChildRecursive(screen, "Bottom"));
            AddOriginalActive(objects, FindCurrentWeightTransform(screen));
            return objects;
        }

        private static void AddOriginalActive(Dictionary<GameObject, bool> states, Transform target)
        {
            if (target?.gameObject != null && !states.ContainsKey(target.gameObject))
            {
                states[target.gameObject] = target.gameObject.activeSelf;
            }
        }

        private static void ApplyRoublesIcon(Transform row)
        {
            Transform iconTransform = FindChildRecursive(row, "WeightIcon");
            Image image = iconTransform?.GetComponent<Image>();
            Sprite sprite = LoadRoublesSprite();
            if (image == null || sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.enabled = true;
            image.preserveAspect = true;
        }

        private static Sprite LoadRoublesSprite()
        {
            if (_roublesSprite != null)
            {
                return _roublesSprite;
            }

            string pluginDirectory = Path.GetDirectoryName(typeof(pitFireTeam).Assembly.Location) ?? string.Empty;
            string[] candidates =
            {
                Path.Combine(pluginDirectory, "icon_info_money_roubles_big.png"),
                Path.Combine(pluginDirectory, "resources", "icon_info_money_roubles_big.png"),
                Path.Combine(Directory.GetParent(pluginDirectory)?.FullName ?? pluginDirectory, "resources", "icon_info_money_roubles_big.png")
            };

            string iconPath = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    iconPath = candidates[i];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(iconPath))
            {
                pitFireTeam.Log.LogWarning("[UI] Teammate equipment builds roubles icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                UnityEngine.Object.Destroy(texture);
                pitFireTeam.Log.LogWarning($"[UI] Failed to decode teammate equipment builds roubles icon '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_BuildListRoublesIcon";
            _roublesSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            _roublesSprite.name = "pitFireTeam_BuildListRoublesIcon";
            return _roublesSprite;
        }

        private static void HideChild(Transform root, string childName)
        {
            Transform child = FindChildRecursive(root, childName);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private sealed class ScreenChromeState
        {
            public EquipmentBuildsScreen Screen;
            public DefaultUIButton EquipButton;
            public string EquipButtonText;
            public int EquipButtonFontSize;
            public bool EquipButtonRawText;
            public TMP_Text EquipButtonLabel;
            public string EquipButtonLabelText;
            public Transform CanEquip;
            public bool? CanEquipActive;
            public Dictionary<GameObject, bool> ScreenObjects;
            public GameObject ExcludeExistingItemsToggleRoot;
        }

        private sealed class EquipmentBuildBuyQuote
        {
            public EFT.UI.Builds.EquipmentBuild Build;
            public string BuildName;
            public int FullKitPrice;
            public int MissingOnlyPrice;
            public int FinalPrice;
            public bool ExcludeExistingItems;
            public bool CanEquipFromStash;
            public int IconGeneration;
            public Dictionary<string, int> MissingTemplateCounts;
            public List<UsedStashItemSummary> AvailableStashItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> BasePurchasedItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> UsedStashItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> PurchasedItems = new List<UsedStashItemSummary>();
        }

        private sealed class FriendlyTeammateBuyKitRequest
        {
            public string aid { get; set; }
            public JsonType.FlatItem[] items { get; set; }
            public int price { get; set; }
            public bool useItemsInStash { get; set; }
            public FriendlyTeammateBuyKitUsedItem[] usedItems { get; set; }
        }

        private sealed class FriendlyTeammateBuyKitResponse
        {
            public JsonType.FlatItem[] playerStashItems { get; set; }
        }

        private sealed class FriendlyTeammateBuyKitUsedItem
        {
            public string itemId { get; set; }
            public string templateId { get; set; }
            public int count { get; set; }
        }

        private sealed class StashOnlyExclusionPlan
        {
            public int FinalPrice;
            public bool HasAllRequiredItems;
            public List<UsedStashItemSummary> UsedItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> PurchasedItems = new List<UsedStashItemSummary>();

            public static StashOnlyExclusionPlan Empty(int fullKitPrice)
            {
                return new StashOnlyExclusionPlan
                {
                    FinalPrice = fullKitPrice
                };
            }
        }

        private sealed class BuildItemRequirement
        {
            public string TemplateId;
            public string DisplayName;
            public int RequiredCount;
            public double TotalPrice;
        }

        private sealed class TraderWeaponOfferPricingPlan
        {
            public static readonly TraderWeaponOfferPricingPlan NoMatch = new TraderWeaponOfferPricingPlan();

            public Dictionary<string, int> MatchedTemplateCounts;
            public double TotalPrice;
            public string SourceDescription;
        }

        private sealed class RagfairWeaponOfferPricingPlan
        {
            public static readonly RagfairWeaponOfferPricingPlan NoMatch = new RagfairWeaponOfferPricingPlan();

            public bool HasMatch;
            public double TotalPrice;
            public double TargetCoverage;
            public double CandidateCoverage;
            public string SourceDescription;
        }

        private sealed class KitLoadoutPricingEntry
        {
            public double Price;
            public string Rule;
        }

        private sealed class KitLoadoutPricingContext
        {
            private readonly Dictionary<string, KitLoadoutPricingEntry> _itemPriceOverrides = new Dictionary<string, KitLoadoutPricingEntry>(StringComparer.Ordinal);
            private List<string> diagnostics;
            public List<string> Diagnostics => diagnostics ??= new List<string>();

            public void SetItemPriceOverride(Item item, double price, string rule)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id))
                {
                    return;
                }

                _itemPriceOverrides[item.Id] = new KitLoadoutPricingEntry
                {
                    Price = Math.Max(0.0, price),
                    Rule = rule ?? string.Empty
                };
            }

            public bool TryGetItemPriceOverride(Item item, out double price)
            {
                price = 0.0;
                if (!TryGetItemPricingEntry(item, out KitLoadoutPricingEntry entry))
                {
                    return false;
                }

                price = entry.Price;
                return true;
            }

            public bool TryGetItemPricingEntry(Item item, out KitLoadoutPricingEntry entry)
            {
                entry = null;
                return item != null
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && _itemPriceOverrides.TryGetValue(item.Id, out entry);
            }

            [System.Diagnostics.Conditional("DEBUG")]
            public void AddDiagnostic(string line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Diagnostics.Add(line);
                }
            }
        }

        private sealed class UsedStashItemSummary
        {
            public string TemplateId;
            public string DisplayName;
            public int Count;
            public double TotalPrice;
            public bool Selected;
            public List<FriendlyTeammateBuyKitUsedItem> ExactItems = new List<FriendlyTeammateBuyKitUsedItem>();
        }

        private sealed class ExactStashSelection
        {
            public Item Item;
            public int Count;
        }

        private sealed class OverlayItemIconRequest
        {
            public Image Image;
            public string TemplateId;
            public int Generation;
        }

        private sealed class BuildRowState
        {
            public EquipmentBuildListView View;
            public EFT.UI.Builds.EquipmentBuild Build;
            public Transform DeleteHolder;
            public bool? DeleteHolderActive;
            public Image WeightIcon;
            public Sprite WeightIconSprite;
            public bool? WeightIconEnabled;
            public bool? WeightIconPreserveAspect;
        }

        private sealed class MarketPriceSource : IBasePriceSource
        {
            public double GetBasePrice(MongoID itemId)
            {
                // Prefer the live /client/items/prices table; handbook remains a safe
                // fallback for templates absent from the returned market map.
                if (_marketPrices != null && _marketPrices.TryGetValue(itemId, out float marketPrice) && marketPrice > 0f)
                {
                    return marketPrice;
                }

                if (Singleton<EFT.HandBook.Handbook>.Instantiated)
                {
                    return Singleton<EFT.HandBook.Handbook>.Instance.GetBasePrice(itemId);
                }

                return 0.0;
            }
        }
    }

    internal class TeammateEquipmentBuildsScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "Show", new[] { typeof(EquipmentBuildsScreen.EquipmentBuildsScreenController) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyScreenChrome(__instance);
        }
    }

    internal class TeammateEquipmentBuildsScreenVisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.GetVisualEquipmentState));
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance, ref EFT.PlayerVisualRepresentation __result)
        {
            if (TeammateEquipmentBuildsScreenFlow.TryBuildTeammateVisual(__instance, __result, out EFT.PlayerVisualRepresentation teammateVisual))
            {
                __result = teammateVisual;
            }
        }
    }

    internal class TeammateEquipmentBuildsScreenSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.SelectBuildHandler));
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyScreenChrome(__instance);
        }
    }

    internal class TeammateEquipmentBuildsMissingItemsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.UpdatePreview));
        }

        [PatchPrefix]
        private static void PatchPrefix(ref IEnumerable<Item> notFoundItems)
        {
            TeammateEquipmentBuildsScreenFlow.CaptureAndSuppressMissingItems(ref notFoundItems);
        }
    }

    internal class TeammateEquipmentBuildsScreenBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.CG_Awake));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile();
        }
    }

    internal class TeammateEquipmentBuildsScreenAltBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.CG_Awake1));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile();
        }
    }

    internal class TeammateEquipmentBuildsScreenEscapePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "TranslateCommand");
        }

        [PatchPrefix]
        private static bool PatchPrefix(ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (!command.IsCommand(ECommand.Escape) || !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile())
            {
                return true;
            }

            __result = InputNode.ETranslateResult.BlockAll;
            return false;
        }
    }

    internal class TeammateEquipmentBuildsScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            TeammateEquipmentBuildsScreenFlow.HandleScreenClosed();
        }
    }

    internal class TeammateEquipmentBuildsScreenBuyButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.EquipBuild));
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBuyRequest();
        }
    }

    internal class TeammateEquipmentBuildsSearchContextPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EFT.UI.BaseItemContextInteractions), nameof(EFT.UI.BaseItemContextInteractions.IsActive));
        }

        [PatchPostfix]
        private static void PatchPostfix(EItemInfoButton button, ref bool __result)
        {
            if (TeammateEquipmentBuildsScreenFlow.ShouldSuppressSearchInteraction(button))
            {
                __result = false;
            }
        }
    }

    internal class TeammateEquipmentBuildsInspectButtonsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.InitInteractionButtonsPanel));
        }

        [PatchPostfix]
        private static void PatchPostfix(ItemSpecificationPanel __instance)
        {
            TeammateEquipmentBuildsScreenFlow.HideInspectInteractionButtons(__instance);
        }
    }

    internal class TeammateEquipmentBuildsListViewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EquipmentBuildListView),
                "Show",
                new[] { typeof(EFT.UI.BuildWrapper<EFT.UI.Builds.EquipmentBuild>), typeof(ValueTuple<float, float, float>?) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildListView __instance, EFT.UI.BuildWrapper<EFT.UI.Builds.EquipmentBuild> buildWrapper)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyBuildListRow(__instance, buildWrapper);
        }
    }
}
