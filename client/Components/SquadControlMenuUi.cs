using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Ragfair;
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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi : MonoBehaviour
    {
        private const string SquadButtonName = "pitFireTeam_SquadControlButton";
        private const string ScreenRootName = "pitFireTeam_SquadControlScreen";
        private const float RosterTileWidth = 190f;
        private const float RosterTileHeight = 214f;
        private const float RosterTileSpacing = 18f;
        private const float RosterViewportPadding = 20f;
        private const float RosterViewportTopInset = 44f;
        private const float RosterViewportBottomInset = 26f;
        private const float RosterHeightScreenRatio = 0.54f;
        private const float RosterShellToButtonGap = -14f;
        private const float RosterBlockVerticalOffset = -24f;
        private const float EmptyRosterButtonCenterY = -36f;
        private const float EmptyRosterLabelSpacing = 92f;
        private const float SettingsViewportTopInset = 62f;
        private const float SettingsViewportBottomInset = 28f;
        private const float SettingsViewportSideInset = 24f;
        private const float SettingsRowHeight = 104f;
        private const float SettingsHeaderHeight = 36f;
        private const float SettingsSpacing = 12f;
        private const float SettingsControlRightInset = 52f;
        private const float SettingsShortcutRightInset = 128f;
        private const float SettingsSliderVerticalOffset = 36f;
        private const float SettingsNumericInputWidthMultiplier = 1.4f;
        private const float SettingsLootPriceInputMinWidth = 156f;
        private const float SettingsLootPriceInputLeftOffset = 40f;
        private const float SettingsLootPriceInputBorderHeight = 1f;
        private const float RaidOverlayBackButtonYOffset = 50f;

        private static readonly FieldInfo HeaderLabelField = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static readonly FieldInfo TraderCardsContainerField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardsContainer");
        private static readonly FieldInfo TradingPlayerPanelIconField = AccessTools.Field(typeof(TradingPlayerPanel), "_playerIconImage");
        private static readonly FieldInfo SettingsScreenGameTabField = AccessTools.Field(typeof(SettingsScreen), "_gameSettingsScreen");
        private static readonly FieldInfo GameSettingsToggleTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_enableBlockInvites");
        private static readonly FieldInfo GameSettingsSliderTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_fov");
        private static readonly FieldInfo NumberSliderValueInputField = AccessTools.Field(typeof(NumberSlider), "_valueInput");
        private static readonly FieldInfo RagfairAllOffersToggleField = AccessTools.Field(typeof(RagfairScreen), "_allOffersToggle");
        private static readonly FieldInfo AnimatedToggleCanvasGroupField = AccessTools.Field(typeof(UIAnimatedToggleSpawner), "_canvasGroup");
        private static readonly ShortcutKeyCandidate[] ShortcutKeyCandidates =
        {
            new ShortcutKeyCandidate(KeyCode.Mouse0),
            new ShortcutKeyCandidate(KeyCode.Mouse1),
            new ShortcutKeyCandidate(KeyCode.Mouse2),
            new ShortcutKeyCandidate(KeyCode.Mouse3),
            new ShortcutKeyCandidate(KeyCode.Mouse4),
            new ShortcutKeyCandidate(KeyCode.A),
            new ShortcutKeyCandidate(KeyCode.B),
            new ShortcutKeyCandidate(KeyCode.C),
            new ShortcutKeyCandidate(KeyCode.D),
            new ShortcutKeyCandidate(KeyCode.E),
            new ShortcutKeyCandidate(KeyCode.F),
            new ShortcutKeyCandidate(KeyCode.G),
            new ShortcutKeyCandidate(KeyCode.H),
            new ShortcutKeyCandidate(KeyCode.I),
            new ShortcutKeyCandidate(KeyCode.J),
            new ShortcutKeyCandidate(KeyCode.K),
            new ShortcutKeyCandidate(KeyCode.L),
            new ShortcutKeyCandidate(KeyCode.M),
            new ShortcutKeyCandidate(KeyCode.N),
            new ShortcutKeyCandidate(KeyCode.O),
            new ShortcutKeyCandidate(KeyCode.P),
            new ShortcutKeyCandidate(KeyCode.Q),
            new ShortcutKeyCandidate(KeyCode.R),
            new ShortcutKeyCandidate(KeyCode.S),
            new ShortcutKeyCandidate(KeyCode.T),
            new ShortcutKeyCandidate(KeyCode.U),
            new ShortcutKeyCandidate(KeyCode.V),
            new ShortcutKeyCandidate(KeyCode.W),
            new ShortcutKeyCandidate(KeyCode.X),
            new ShortcutKeyCandidate(KeyCode.Y),
            new ShortcutKeyCandidate(KeyCode.Z),
            new ShortcutKeyCandidate(KeyCode.Alpha0),
            new ShortcutKeyCandidate(KeyCode.Alpha1),
            new ShortcutKeyCandidate(KeyCode.Alpha2),
            new ShortcutKeyCandidate(KeyCode.Alpha3),
            new ShortcutKeyCandidate(KeyCode.Alpha4),
            new ShortcutKeyCandidate(KeyCode.Alpha5),
            new ShortcutKeyCandidate(KeyCode.Alpha6),
            new ShortcutKeyCandidate(KeyCode.Alpha7),
            new ShortcutKeyCandidate(KeyCode.Alpha8),
            new ShortcutKeyCandidate(KeyCode.Alpha9),
            new ShortcutKeyCandidate(KeyCode.F1),
            new ShortcutKeyCandidate(KeyCode.F2),
            new ShortcutKeyCandidate(KeyCode.F3),
            new ShortcutKeyCandidate(KeyCode.F4),
            new ShortcutKeyCandidate(KeyCode.F5),
            new ShortcutKeyCandidate(KeyCode.F6),
            new ShortcutKeyCandidate(KeyCode.F7),
            new ShortcutKeyCandidate(KeyCode.F8),
            new ShortcutKeyCandidate(KeyCode.F9),
            new ShortcutKeyCandidate(KeyCode.F10),
            new ShortcutKeyCandidate(KeyCode.F11),
            new ShortcutKeyCandidate(KeyCode.F12),
            new ShortcutKeyCandidate(KeyCode.Keypad0),
            new ShortcutKeyCandidate(KeyCode.Keypad1),
            new ShortcutKeyCandidate(KeyCode.Keypad2),
            new ShortcutKeyCandidate(KeyCode.Keypad3),
            new ShortcutKeyCandidate(KeyCode.Keypad4),
            new ShortcutKeyCandidate(KeyCode.Keypad5),
            new ShortcutKeyCandidate(KeyCode.Keypad6),
            new ShortcutKeyCandidate(KeyCode.Keypad7),
            new ShortcutKeyCandidate(KeyCode.Keypad8),
            new ShortcutKeyCandidate(KeyCode.Keypad9),
            new ShortcutKeyCandidate(KeyCode.Space),
            new ShortcutKeyCandidate(KeyCode.Tab),
            new ShortcutKeyCandidate(KeyCode.Return),
            new ShortcutKeyCandidate(KeyCode.Insert),
            new ShortcutKeyCandidate(KeyCode.Delete),
            new ShortcutKeyCandidate(KeyCode.Home),
            new ShortcutKeyCandidate(KeyCode.End),
            new ShortcutKeyCandidate(KeyCode.PageUp),
            new ShortcutKeyCandidate(KeyCode.PageDown),
            new ShortcutKeyCandidate(KeyCode.UpArrow),
            new ShortcutKeyCandidate(KeyCode.DownArrow),
            new ShortcutKeyCandidate(KeyCode.LeftArrow),
            new ShortcutKeyCandidate(KeyCode.RightArrow),
            new ShortcutKeyCandidate(KeyCode.LeftBracket),
            new ShortcutKeyCandidate(KeyCode.RightBracket),
            new ShortcutKeyCandidate(KeyCode.Backslash),
            new ShortcutKeyCandidate(KeyCode.Semicolon),
            new ShortcutKeyCandidate(KeyCode.Quote),
            new ShortcutKeyCandidate(KeyCode.Comma),
            new ShortcutKeyCandidate(KeyCode.Period),
            new ShortcutKeyCandidate(KeyCode.Slash),
            new ShortcutKeyCandidate(KeyCode.BackQuote),
            new ShortcutKeyCandidate(KeyCode.Minus),
            new ShortcutKeyCandidate(KeyCode.Equals),
            new ShortcutKeyCandidate(KeyCode.KeypadDivide),
            new ShortcutKeyCandidate(KeyCode.KeypadMultiply),
            new ShortcutKeyCandidate(KeyCode.KeypadMinus),
            new ShortcutKeyCandidate(KeyCode.KeypadPlus),
            new ShortcutKeyCandidate(KeyCode.KeypadPeriod),
            new ShortcutKeyCandidate(KeyCode.LeftControl),
            new ShortcutKeyCandidate(KeyCode.RightControl),
            new ShortcutKeyCandidate(KeyCode.LeftShift),
            new ShortcutKeyCandidate(KeyCode.RightShift),
            new ShortcutKeyCandidate(KeyCode.LeftAlt),
            new ShortcutKeyCandidate(KeyCode.RightAlt),
        };

        private readonly struct ShortcutKeyCandidate
        {
            public readonly KeyCode KeyCode;

            public ShortcutKeyCandidate(KeyCode keyCode)
            {
                KeyCode = keyCode;
            }
        }
        private static readonly FieldInfo PlayersInviteWindowContextMenuField = AccessTools.Field(typeof(PlayersInviteWindow), "_contextMenu");
        private static readonly FieldInfo SimpleContextMenuButtonsContainerField = AccessTools.Field(typeof(SimpleContextMenu), "_interactionButtonsContainer");
        private static readonly FieldInfo InteractionButtonsContainerTemplateField = AccessTools.Field(typeof(InteractionButtonsContainer), "_buttonTemplate");
        private static readonly FieldInfo InteractionButtonsContainerButtonsRootField = AccessTools.Field(typeof(InteractionButtonsContainer), "_buttonsContainer");
        private static readonly FieldInfo MatchmakerBackButtonField = AccessTools.Field(typeof(MatchMakerSideSelectionScreen), "_backButton");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static Sprite squadIconSprite;
        private static Sprite rosterTileDiagonalSprite;
        private static Sprite autoJoinBadgeSprite;
        private static Sprite groupBadgeSprite;
        private const float RaidSettingsButtonGap = 10f;

        private MenuScreen menuScreen;
        private DefaultUIButton playerButton;
        private DefaultUIButton tradeButton;
        private DefaultUIButton hideScreenButton;
        private DefaultUIButton squadButton;
        private DefaultUIButton raidSettingsButton;
        private GameObject screenRoot;
        private TextMeshProUGUI titleLabel;
        private DefaultUIButton standaloneCloseButton;
        private RectTransform stockCardsContainer;
        private RectTransform rosterPanelRect;
        private ScrollRectNoDrag rosterScrollRect;
        private RectTransform rosterViewport;
        private RectTransform rosterContentRoot;
        private RectTransform rosterGridRoot;
        private GridLayoutGroup rosterGridLayout;
        private Scrollbar rosterScrollbar;
        private ScrollRectNoDrag settingsScrollRect;
        private RectTransform settingsViewport;
        private RectTransform settingsContentRoot;
        private VerticalLayoutGroup settingsLayoutGroup;
        private Scrollbar settingsScrollbar;
        private GameObject rosterPanel;
        private GameObject settingsPanel;
        private DefaultUIButton addTeammateButton;
        private TextMeshProUGUI emptyRosterLabel;
        private GameObject removeConfirmOverlay;
        private GameObject loadoutManagementConfirmOverlay;
        private GameObject portraitContextMenuOverlay;
        private float currentRosterShellHeight;
        private Button activeShortcutCaptureButton;
        private TextMeshProUGUI activeShortcutCaptureLabel;
        private ConfigEntry<KeyboardShortcut> activeShortcutCaptureEntry;
        private int activeShortcutCaptureStartFrame;
        private Coroutine shortcutCaptureCoroutine;
        private readonly HashSet<KeyCode> shortcutCaptureSuppressedKeys = new HashSet<KeyCode>();
        private readonly Dictionary<RectTransform, Vector2> originalButtonPositions = new Dictionary<RectTransform, Vector2>();
        private readonly Dictionary<string, GameObject> portraitLoadingIndicators = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly HashSet<string> autoJoinRequestsInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> groupRequestsInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder shortcutBuilder = new StringBuilder();
        private int rosterBuildVersion;
        private static bool forceRosterRefreshOnNextInject;
        private static readonly HashSet<string> pendingTileRefreshAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private bool raidSettingsOverlayActive;
        private MatchmakerPlayerControllerClass subscribedGroupBadgeLogController;
        private Action unsubscribeGroupBadgeLog;
        private ToggleGroup loadoutManagementToggleGroup;
        private Coroutine loadoutManagementRosterRefreshCoroutine;
        private readonly Dictionary<LoadoutManagementMode, UIAnimatedToggleSpawner> loadoutManagementToggleSpawners = new Dictionary<LoadoutManagementMode, UIAnimatedToggleSpawner>();
        private readonly Dictionary<LoadoutManagementMode, Toggle> loadoutManagementFallbackToggles = new Dictionary<LoadoutManagementMode, Toggle>();

        internal static void RequestRosterRefreshOnNextInject()
        {
            forceRosterRefreshOnNextInject = true;
            pendingTileRefreshAccountIds.Clear();
        }

        internal static void RequestRosterRefreshNowOrNextInject()
        {
            RequestRosterRefreshOnNextInject();

            SquadControlMenuUi instance = FindInstance();
            if (instance?.rosterGridRoot == null || instance.rosterPanel == null || !instance.rosterPanel.activeInHierarchy)
            {
                return;
            }

            forceRosterRefreshOnNextInject = false;
            pendingTileRefreshAccountIds.Clear();
            instance.RebuildRosterTiles();
        }

        internal static void RequestRosterTileRefreshOnNextInject(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }

            if (forceRosterRefreshOnNextInject)
            {
                return;
            }

            pendingTileRefreshAccountIds.Add(accountId);
        }

        private sealed class SquadRosterEntry
        {
            public string AccountId { get; set; } = string.Empty;
            public string SocialMemberId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public int Level { get; set; }
            public bool AutoJoinEnabled { get; set; }
            public bool HasProperRaidKit { get; set; } = true;
        }

        private sealed class SquadSettingEntry
        {
            public string SectionTitle { get; set; } = string.Empty;
            public ConfigEntryBase Entry { get; set; }
            public LoadoutManagementMode? LoadoutMode { get; set; }
        }

        private sealed class BackendBodyResponse
        {
            public int err;
            public string errmsg;
            public object data;
        }

        private sealed class RosterTileHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private Image background;
            private TextMeshProUGUI nameLabel;
            private Color normalColor;
            private Color hoverColor;
            private Color pressedColor;
            private Color normalTextColor;
            private Color hoverTextColor;
            private bool isHovered;

            public void Configure(Image target, TextMeshProUGUI label, Color normal, Color hover, Color pressed)
            {
                background = target;
                nameLabel = label;
                normalColor = normal;
                hoverColor = hover;
                pressedColor = pressed;
                normalTextColor = nameLabel != null ? nameLabel.color : default;
                hoverTextColor = new Color(1f - normalTextColor.r, 1f - normalTextColor.g, 1f - normalTextColor.b, normalTextColor.a);
                Apply(normalColor);
                ApplyText(normalTextColor);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (isHovered)
                {
                    return;
                }

                isHovered = true;
                GUISounds guiSounds = Singleton<GUISounds>.Instance;
                if (guiSounds != null)
                {
                    guiSounds.PlayUISound(EUISoundType.ButtonOver);
                }

                Apply(hoverColor);
                ApplyText(hoverTextColor);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                Apply(normalColor);
                ApplyText(normalTextColor);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(isHovered ? hoverColor : normalColor);
                ApplyText(isHovered ? hoverTextColor : normalTextColor);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverColor : normalColor);
                ApplyText(isHovered ? hoverTextColor : normalTextColor);
            }

            private void Apply(Color color)
            {
                if (background != null)
                {
                    background.color = color;
                }
            }

            private void ApplyText(Color color)
            {
                if (nameLabel != null)
                {
                    nameLabel.color = color;
                }
            }
        }

        internal sealed class TabHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private RectTransform rectTransform;
            private Vector3 normalScale = Vector3.one;
            private Vector3 hoverScale = new Vector3(1.025f, 1.025f, 1f);
            private Vector3 pressedScale = new Vector3(0.99f, 0.99f, 1f);
            private bool isHovered;

            public void Configure(RectTransform target)
            {
                rectTransform = target;
                if (rectTransform != null)
                {
                    normalScale = rectTransform.localScale;
                }
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                isHovered = true;
                Apply(hoverScale);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                Apply(normalScale);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(pressedScale);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverScale : normalScale);
            }

            private void Apply(Vector3 scale)
            {
                if (rectTransform != null)
                {
                    rectTransform.localScale = scale;
                }
            }
        }

        internal sealed class LoadoutModeToggleHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
        {
            private RectTransform rectTransform;
            private Image hoverBackground;
            private Vector3 normalScale = Vector3.one;
            private Vector3 hoverScale = Vector3.one;
            private Vector3 pressedScale = Vector3.one;
            private bool isHovered;

            public Action<PointerEventData> OnClick { get; set; }

            public void Configure(RectTransform target, Image background)
            {
                rectTransform = target;
                hoverBackground = background;
                if (rectTransform != null)
                {
                    normalScale = rectTransform.localScale;
                    hoverScale = new Vector3(normalScale.x * 1.012f, normalScale.y * 1.012f, normalScale.z);
                    pressedScale = new Vector3(normalScale.x * 0.996f, normalScale.y * 0.996f, normalScale.z);
                }

                SetHoverBackground(false);
                Apply(normalScale);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                isHovered = true;
                SetHoverBackground(true);
                Apply(hoverScale);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                SetHoverBackground(false);
                Apply(normalScale);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(pressedScale);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverScale : normalScale);
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    OnClick?.Invoke(eventData);
                }
            }

            private void SetHoverBackground(bool visible)
            {
                if (hoverBackground != null)
                {
                    hoverBackground.enabled = visible;
                }
            }

            private void Apply(Vector3 scale)
            {
                if (rectTransform != null)
                {
                    rectTransform.localScale = scale;
                }
            }
        }

        public static SquadControlMenuUi GetOrCreate(MenuScreen menuScreen)
        {
            SquadControlMenuUi ui = menuScreen.GetComponent<SquadControlMenuUi>();
            if (ui == null)
            {
                ui = menuScreen.gameObject.AddComponent<SquadControlMenuUi>();
            }

            return ui;
        }

        internal static void ReturnFromProfileToSquadControl()
        {
            SquadSideSelectionFlow.Open();
        }

        private sealed class PortraitContextClickController : MonoBehaviour, IPointerClickHandler
        {
            public Action<PointerEventData> OnLeftClick { get; set; }
            public Action<PointerEventData> OnRightClick { get; set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    OnLeftClick?.Invoke(eventData);
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    OnRightClick?.Invoke(eventData);
                }
            }
        }

        private sealed class TooltipHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private string tooltipText;
            private Vector2 tooltipOffset = new Vector2(5f, 7f);

            public void Configure(string text, Vector2? offset = null)
            {
                tooltipText = text ?? string.Empty;
                tooltipOffset = offset ?? new Vector2(5f, 7f);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (string.IsNullOrWhiteSpace(tooltipText))
                {
                    return;
                }

                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Show(tooltipText, tooltipOffset, 0f, null);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Close();
            }

            private void OnDisable()
            {
                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Close();
            }
        }

        public void Initialize(MenuScreen screen, DefaultUIButton sourcePlayerButton, DefaultUIButton sourceTradeButton, DefaultUIButton sourceHideScreenButton)
        {
            DetachGroupBadgeEventLogging();

            menuScreen = screen;
            playerButton = sourcePlayerButton;
            tradeButton = sourceTradeButton;
            hideScreenButton = sourceHideScreenButton;

            if (playerButton == null)
            {
                return;
            }

            EnsureSquadButton();
            EnsureRaidSettingsButton();
            EnsureScreen();
            raidSettingsOverlayActive = false;
            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.gameObject.SetActive(false);
            }

            EnsureGroupBadgeEventLogging();
            SyncButtonVisibility();
        }

        private void OnDisable()
        {
            DetachGroupBadgeEventLogging();
        }

        private void OnDestroy()
        {
            DetachGroupBadgeEventLogging();
        }

        public void SyncButtonVisibility()
        {
            if (playerButton != null && squadButton != null)
            {
                bool visible = playerButton.gameObject.activeSelf;
                squadButton.gameObject.SetActive(visible);
            }

            if (raidSettingsButton != null)
            {
                bool showRaidSettingsButton = hideScreenButton != null
                    && hideScreenButton.gameObject.activeSelf
                    && !raidSettingsOverlayActive;
                raidSettingsButton.gameObject.SetActive(showRaidSettingsButton);
            }
        }

        private void Update()
        {
            if (raidSettingsOverlayActive && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                HideRaidSettingsOverlay();
                return;
            }

            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            PollShortcutCapture();
        }

        private IEnumerator ShortcutCaptureCoroutine()
        {
            yield return null;

            while (activeShortcutCaptureEntry != null)
            {
                PollShortcutCapture();
                yield return null;
            }

            shortcutCaptureCoroutine = null;
        }

        private void PollShortcutCapture()
        {
            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            if (Time.frameCount == activeShortcutCaptureStartFrame)
            {
                return;
            }

            if (!IsShortcutCaptureTargetActive())
            {
                CancelShortcutCapture(true);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CancelShortcutCapture(true);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace) || UnityEngine.Input.GetKeyDown(KeyCode.Delete))
            {
                ApplyShortcutCapture(new KeyboardShortcut(KeyCode.None));
                return;
            }

            KeyCode mainKey = FindPressedMainKey();
            if (mainKey == KeyCode.None)
            {
                mainKey = FindPressedNativeMainKey();
            }

            if (mainKey == KeyCode.None)
            {
                return;
            }

            KeyCode[] modifiers = GetPressedModifiers();
            if (modifiers.Length == 0)
            {
                modifiers = GetPressedNativeModifiers();
            }
            ApplyShortcutCapture(new KeyboardShortcut(mainKey, modifiers));
        }

        private void OnGUI()
        {
            if (activeShortcutCaptureEntry == null || Event.current == null || Event.current.type != EventType.KeyDown)
            {
                return;
            }

            if (Time.frameCount == activeShortcutCaptureStartFrame)
            {
                return;
            }

            KeyCode keyCode = Event.current.keyCode;
            if (keyCode == KeyCode.None)
            {
                return;
            }

            if (keyCode == KeyCode.Escape)
            {
                CancelShortcutCapture(true);
                Event.current.Use();
                return;
            }

            if (keyCode == KeyCode.Backspace || keyCode == KeyCode.Delete)
            {
                ApplyShortcutCapture(new KeyboardShortcut(KeyCode.None));
                Event.current.Use();
                return;
            }

            if (IsModifierKey(keyCode))
            {
                return;
            }

            ApplyShortcutCapture(new KeyboardShortcut(keyCode, GetPressedModifiers()));
            Event.current.Use();
        }

        private bool IsShortcutCaptureTargetActive()
        {
            if (activeShortcutCaptureButton != null && activeShortcutCaptureButton.gameObject.activeInHierarchy)
            {
                return true;
            }

            bool labelActive = activeShortcutCaptureLabel != null && activeShortcutCaptureLabel.gameObject.activeInHierarchy;
            return labelActive;
        }

        private static string FormatModifierLog(KeyCode[] modifiers)
        {
            return modifiers == null || modifiers.Length == 0
                ? "<none>"
                : string.Join("+", modifiers.Select(modifier => modifier.ToString()).ToArray());
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        private KeyCode FindPressedNativeMainKey()
        {
            if (IsNativeKeyDown(KeyCode.Escape))
            {
                CancelShortcutCapture(true);
                return KeyCode.None;
            }

            if (IsNativeKeyDown(KeyCode.Backspace) || IsNativeKeyDown(KeyCode.Delete))
            {
                ApplyShortcutCapture(new KeyboardShortcut(KeyCode.None));
                return KeyCode.None;
            }

            foreach (ShortcutKeyCandidate candidate in ShortcutKeyCandidates)
            {
                bool down = IsNativeKeyDown(candidate.KeyCode);
                if (!down)
                {
                    shortcutCaptureSuppressedKeys.Remove(candidate.KeyCode);
                    continue;
                }

                if (IsModifierKey(candidate.KeyCode) || shortcutCaptureSuppressedKeys.Contains(candidate.KeyCode))
                {
                    continue;
                }

                return candidate.KeyCode;
            }

            return KeyCode.None;
        }

        private void SeedSuppressedNativeKeys()
        {
            shortcutCaptureSuppressedKeys.Clear();
            foreach (ShortcutKeyCandidate candidate in ShortcutKeyCandidates)
            {
                if (IsNativeKeyDown(candidate.KeyCode))
                {
                    shortcutCaptureSuppressedKeys.Add(candidate.KeyCode);
                }
            }
        }

        private static bool IsNativeKeyDown(KeyCode keyCode)
        {
            int virtualKey = GetVirtualKey(keyCode);
            return virtualKey != 0 && (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        private static KeyCode[] GetPressedNativeModifiers()
        {
            List<KeyCode> modifiers = new List<KeyCode>(3);
            if (IsNativeKeyDown(KeyCode.LeftControl) || IsNativeKeyDown(KeyCode.RightControl))
            {
                modifiers.Add(KeyCode.LeftControl);
            }

            if (IsNativeKeyDown(KeyCode.LeftShift) || IsNativeKeyDown(KeyCode.RightShift))
            {
                modifiers.Add(KeyCode.LeftShift);
            }

            if (IsNativeKeyDown(KeyCode.LeftAlt) || IsNativeKeyDown(KeyCode.RightAlt))
            {
                modifiers.Add(KeyCode.LeftAlt);
            }

            return modifiers.ToArray();
        }

        private static int GetVirtualKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.Mouse0: return 0x01;
                case KeyCode.Mouse1: return 0x02;
                case KeyCode.Mouse2: return 0x04;
                case KeyCode.Mouse3: return 0x05;
                case KeyCode.Mouse4: return 0x06;
                case KeyCode.Backspace: return 0x08;
                case KeyCode.Tab: return 0x09;
                case KeyCode.Return: return 0x0D;
                case KeyCode.Escape: return 0x1B;
                case KeyCode.Space: return 0x20;
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.End: return 0x23;
                case KeyCode.Home: return 0x24;
                case KeyCode.LeftArrow: return 0x25;
                case KeyCode.UpArrow: return 0x26;
                case KeyCode.RightArrow: return 0x27;
                case KeyCode.DownArrow: return 0x28;
                case KeyCode.Insert: return 0x2D;
                case KeyCode.Delete: return 0x2E;
                case KeyCode.LeftShift: return 0xA0;
                case KeyCode.RightShift: return 0xA1;
                case KeyCode.LeftControl: return 0xA2;
                case KeyCode.RightControl: return 0xA3;
                case KeyCode.LeftAlt: return 0xA4;
                case KeyCode.RightAlt: return 0xA5;
                case KeyCode.KeypadMultiply: return 0x6A;
                case KeyCode.KeypadPlus: return 0x6B;
                case KeyCode.KeypadMinus: return 0x6D;
                case KeyCode.KeypadPeriod: return 0x6E;
                case KeyCode.KeypadDivide: return 0x6F;
                case KeyCode.Semicolon: return 0xBA;
                case KeyCode.Equals: return 0xBB;
                case KeyCode.Comma: return 0xBC;
                case KeyCode.Minus: return 0xBD;
                case KeyCode.Period: return 0xBE;
                case KeyCode.Slash: return 0xBF;
                case KeyCode.BackQuote: return 0xC0;
                case KeyCode.LeftBracket: return 0xDB;
                case KeyCode.Backslash: return 0xDC;
                case KeyCode.RightBracket: return 0xDD;
                case KeyCode.Quote: return 0xDE;
            }

            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            {
                return 0x30 + (keyCode - KeyCode.Alpha0);
            }

            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            {
                return 0x41 + (keyCode - KeyCode.A);
            }

            if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
            {
                return 0x70 + (keyCode - KeyCode.F1);
            }

            if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
            {
                return 0x60 + (keyCode - KeyCode.Keypad0);
            }

            return 0;
        }

        private void EnsureSquadButton()
        {
            if (squadButton == null)
            {
                Transform existing = playerButton.transform.parent.Find(SquadButtonName);
                if (existing != null)
                {
                    squadButton = existing.GetComponent<DefaultUIButton>();
                }
            }

            if (squadButton == null)
            {
                squadButton = Instantiate(playerButton, playerButton.transform.parent, false);
                squadButton.name = SquadButtonName;
                squadButton.transform.SetSiblingIndex(playerButton.transform.GetSiblingIndex() + 1);
            }

            RectTransform playerRect = playerButton.transform as RectTransform;
            RectTransform tradeRect = tradeButton != null ? tradeButton.transform as RectTransform : null;
            RectTransform squadRect = squadButton.transform as RectTransform;

            if (playerRect != null && squadRect != null)
            {
                CaptureOriginalButtonPositions(playerRect);
                RestoreOriginalButtonPositions();

                float verticalStep = ResolveVerticalStep(playerRect, tradeRect) * 0.82f;
                squadRect.anchorMin = playerRect.anchorMin;
                squadRect.anchorMax = playerRect.anchorMax;
                squadRect.pivot = playerRect.pivot;
                squadRect.sizeDelta = playerRect.sizeDelta;
                squadRect.anchoredPosition = playerRect.anchoredPosition + new Vector2(0f, -verticalStep - 10f);

                ShiftButtonsBelowPlayer(playerRect, verticalStep);

                if (tradeRect != null)
                {
                    tradeRect.anchoredPosition -= new Vector2(0f, 4f);
                }
            }

            squadButton.SetRawText(GetSocialUiText("SquadControlButton"), playerButton.HeaderSize);
            squadButton.SetIcon(LoadSquadIcon());
            squadButton.OnClick.RemoveAllListeners();
            squadButton.OnClick.AddListener(Modules.SquadSideSelectionFlow.Open);
        }

        private void EnsureRaidSettingsButton()
        {
            if (hideScreenButton == null)
            {
                if (raidSettingsButton != null)
                {
                    raidSettingsButton.gameObject.SetActive(false);
                }

                return;
            }

            if (raidSettingsButton == null)
            {
                Transform existing = hideScreenButton.transform.parent.Find("pitFireTeam_RaidSettingsButton");
                if (existing != null)
                {
                    raidSettingsButton = existing.GetComponent<DefaultUIButton>();
                }
            }

            if (raidSettingsButton == null)
            {
                raidSettingsButton = Instantiate(hideScreenButton, hideScreenButton.transform.parent, false);
                raidSettingsButton.name = "pitFireTeam_RaidSettingsButton";
            }

            if (hideScreenButton.transform is RectTransform resumeRect
                && raidSettingsButton.transform is RectTransform raidRect)
            {
                raidRect.anchorMin = resumeRect.anchorMin;
                raidRect.anchorMax = resumeRect.anchorMax;
                raidRect.pivot = resumeRect.pivot;
                raidRect.sizeDelta = resumeRect.sizeDelta;
                raidRect.anchoredPosition = resumeRect.anchoredPosition - new Vector2(0f, resumeRect.rect.height + RaidSettingsButtonGap - 5f);
                raidRect.localScale = resumeRect.localScale;
            }

            raidSettingsButton.transform.SetSiblingIndex(hideScreenButton.transform.GetSiblingIndex() + 1);
            raidSettingsButton.SetRawText(GetSocialUiText("SquadControlRaidSettingsButton"), hideScreenButton.HeaderSize);
            raidSettingsButton.SetIcon(null);
            raidSettingsButton.OnClick.RemoveAllListeners();
            raidSettingsButton.OnClick.AddListener(ShowRaidSettingsOverlay);
        }

        internal static SquadControlMenuUi FindInstance()
        {
            return Resources.FindObjectsOfTypeAll<SquadControlMenuUi>().FirstOrDefault(ui => ui != null);
        }

        internal void InjectPanelsIntoScreen(Transform newParent)
        {
            EnsureScreen();
            EnsureGroupBadgeEventLogging();
            if (screenRoot == null)
            {
                return;
            }

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            if (rosterPanel != null && rosterPanel.transform.parent == rootRect)
            {
                rosterPanel.transform.SetParent(newParent, false);
            }

            if (settingsPanel != null && settingsPanel.transform.parent == rootRect)
            {
                settingsPanel.transform.SetParent(newParent, false);
            }

            ShowTab(true);

            // Rebuild roster only on first open or when explicitly requested by add-teammate flow.
            bool shouldForceRosterRefresh = forceRosterRefreshOnNextInject;
            forceRosterRefreshOnNextInject = false;
            List<string> pendingTileRefreshIds = pendingTileRefreshAccountIds.ToList();
            pendingTileRefreshAccountIds.Clear();
            bool needsRoster = rosterGridRoot != null && (rosterGridRoot.childCount == 0 || shouldForceRosterRefresh);
            bool needsSettings = settingsContentRoot != null && settingsContentRoot.childCount == 0;

            if (!needsRoster && rosterGridRoot != null)
            {
                SyncGroupBadgesFromKnownState();

                if (pendingTileRefreshIds.Count > 0)
                {
                    RefreshRosterTiles(pendingTileRefreshIds);
                }
            }

            if (shouldForceRosterRefresh && needsRoster)
            {
                RebuildRosterTiles();
                needsRoster = false;
            }

            if (needsRoster || needsSettings)
            {
                pitFireTeam.Instance.StartCoroutine(RebuildAfterTransitionCoroutine(needsRoster, needsSettings));
            }
        }

        private IEnumerator RebuildAfterTransitionCoroutine(bool roster, bool settings)
        {
            // Wait for the matchmaker screen open animation to finish before doing any heavy work.
            // Time-based is more reliable than frame-counting across different frame rates.
            yield return new WaitForSeconds(1.2f);

            if (settings) RebuildSettingsEntries();

            if (roster) RebuildRosterTiles();
        }

        internal void RetractPanels()
        {
            if (screenRoot == null)
            {
                return;
            }

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            if (rosterPanel != null && rosterPanel.transform.parent != rootRect)
            {
                rosterPanel.transform.SetParent(rootRect, false);
            }

            if (settingsPanel != null && settingsPanel.transform.parent != rootRect)
            {
                settingsPanel.transform.SetParent(rootRect, false);
            }
        }

        private void EnsureScreen()
        {
            if (screenRoot == null)
            {
                Transform existing = menuScreen.transform.Find(ScreenRootName);
                if (existing != null)
                {
                    screenRoot = existing.gameObject;
                }
            }

            if (screenRoot != null)
            {
                return;
            }

            screenRoot = new GameObject(ScreenRootName, typeof(RectTransform), typeof(Image));
            screenRoot.transform.SetParent(menuScreen.transform, false);

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image background = screenRoot.GetComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.06f, 1f);
            background.raycastTarget = true;

            CreateScreenChrome();
            screenRoot.SetActive(false);
        }

        private void CreateScreenChrome()
        {
            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            CreateHeader(rootRect);

            if (!TryCreateStockTraderChrome(rootRect))
            {
                rosterPanel = CreateFallbackContentPanel("pitFireTeam_SquadControlRosterPanel", GetSocialUiText("SquadControlRosterTab"));
                settingsPanel = CreateFallbackContentPanel("pitFireTeam_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab"));
                BuildSettingsPanel();
            }

            ShowTab(true);
        }

        private void CreateHeader(RectTransform rootRect)
        {
            GameObject titleObject = CreateText(
                "pitFireTeam_SquadControlTitle",
                GetSocialUiText("SquadControlTitle"),
                44,
                TextAlignmentOptions.Center);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(rootRect, false);
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(600f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, -48f);

            titleLabel = titleObject.GetComponent<TextMeshProUGUI>();
            EnsureStandaloneCloseButton();
        }

        private void EnsureStandaloneCloseButton()
        {
            if (standaloneCloseButton != null && standaloneCloseButton.transform.parent == screenRoot.transform)
            {
                return;
            }

            DefaultUIButton closeButton = ResolveOverlayBackButtonTemplate();
            if (closeButton == null)
            {
                return;
            }

            closeButton.transform.SetParent(screenRoot.transform, false);
            closeButton.name = "pitFireTeam_SquadControlStandaloneClose";
            closeButton.gameObject.SetActive(false);
            standaloneCloseButton = closeButton;
        }

        private DefaultUIButton ResolveOverlayBackButtonTemplate()
        {
            MatchMakerSideSelectionScreen sideSelectionScreen = Resources.FindObjectsOfTypeAll<MatchMakerSideSelectionScreen>()
                .FirstOrDefault(screen => screen != null);
            DefaultUIButton template = sideSelectionScreen?.transform.Find("ScreenDefaultButtons/BackButton")?.GetComponent<DefaultUIButton>()
                ?? MatchmakerBackButtonField?.GetValue(sideSelectionScreen) as DefaultUIButton;
            if (template == null)
            {
                return null;
            }

            DefaultUIButton clone = Instantiate(template, screenRoot.transform, false);
            clone.gameObject.SetActive(true);
            clone.Interactable = true;

            if (template.transform is RectTransform templateRect && clone.transform is RectTransform cloneRect)
            {
                cloneRect.anchorMin = new Vector2(0.5f, 0f);
                cloneRect.anchorMax = new Vector2(0.5f, 0f);
                cloneRect.pivot = new Vector2(0.5f, 0f);
                cloneRect.sizeDelta = templateRect.sizeDelta;
                cloneRect.anchoredPosition = new Vector2(0f, RaidOverlayBackButtonYOffset);
                cloneRect.localScale = templateRect.localScale;
            }

            return clone;
        }

        private bool TryCreateStockTraderChrome(RectTransform rootRect)
        {
            TraderScreensGroup templateGroup = ResolveTraderScreensGroupTemplate();
            if (templateGroup == null)
            {
                return false;
            }

            Transform cardsContainerTemplate = TraderCardsContainerField?.GetValue(templateGroup) as Transform;

            if (cardsContainerTemplate == null)
            {
                return false;
            }

            stockCardsContainer = Instantiate(cardsContainerTemplate as RectTransform, rootRect, false);
            stockCardsContainer.name = "pitFireTeam_SquadControlCardsContainer";
            float rosterShellHeight = CalculateRosterShellHeight();
            currentRosterShellHeight = rosterShellHeight;
            stockCardsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            stockCardsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            stockCardsContainer.pivot = new Vector2(0.5f, 0.5f);
            stockCardsContainer.sizeDelta = new Vector2(1180f, rosterShellHeight);
            stockCardsContainer.anchoredPosition = new Vector2(0f, -12f);
            PrepareRosterShellContainer(stockCardsContainer);

            for (int i = stockCardsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(stockCardsContainer.GetChild(i).gameObject);
            }

            rosterPanel = new GameObject("pitFireTeam_SquadControlRosterPanel", typeof(RectTransform));
            rosterPanel.transform.SetParent(rootRect, false);
            RectTransform rosterRect = rosterPanel.GetComponent<RectTransform>();
            rosterPanelRect = rosterRect;
            rosterRect.anchorMin = new Vector2(0.5f, 0.5f);
            rosterRect.anchorMax = new Vector2(0.5f, 0.5f);
            rosterRect.pivot = new Vector2(0.5f, 0.5f);
            rosterRect.sizeDelta = new Vector2(1260f, rosterShellHeight + 180f);
            rosterRect.anchoredPosition = new Vector2(0f, 8f);

            stockCardsContainer.SetParent(rosterRect, false);
            stockCardsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            stockCardsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            stockCardsContainer.pivot = new Vector2(0.5f, 0.5f);
            stockCardsContainer.sizeDelta = new Vector2(1180f, rosterShellHeight);
            CreateScrollableRosterArea(stockCardsContainer);
            CreateEmptyRosterLabel(rosterRect);
            CreateAddTeammateButton(rosterRect);
            UpdateRosterPanelLayout(false);
            RebuildRosterTiles();

            settingsPanel = CreateFallbackContentPanel("pitFireTeam_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab"));
            BuildSettingsPanel();
            return true;
        }

        internal void ShowTab(bool showRoster)
        {
            if (rosterPanel != null)
            {
                rosterPanel.SetActive(showRoster);
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(!showRoster);
            }

            if (showRoster)
            {
                CancelShortcutCapture(false);
            }
        }

        internal void ShowRaidSettingsOverlay()
        {
            EnsureScreen();
            if (screenRoot == null)
            {
                return;
            }

            EnsureStandaloneCloseButton();

            RetractPanels();
            raidSettingsOverlayActive = true;
            screenRoot.SetActive(true);
            SetStandaloneTitle(GetSocialUiText("SquadControlRaidSettingsTitle"));
            ShowTab(false);

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.SetRawText(GetSocialUiText("SquadControlBack"), standaloneCloseButton.HeaderSize);
                standaloneCloseButton.SetIcon(null);
                standaloneCloseButton.OnClick.RemoveAllListeners();
                standaloneCloseButton.OnClick.AddListener(HideRaidSettingsOverlay);
                standaloneCloseButton.Interactable = true;
                standaloneCloseButton.transform.SetAsLastSibling();
                standaloneCloseButton.gameObject.SetActive(true);
            }

            if (settingsContentRoot != null)
            {
                RebuildSettingsEntries();
            }

            SyncButtonVisibility();
        }

        internal void HideRaidSettingsOverlay()
        {
            raidSettingsOverlayActive = false;

            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.gameObject.SetActive(false);
            }

            SetStandaloneTitle(GetSocialUiText("SquadControlTitle"));
            CancelShortcutCapture(false);
            SyncButtonVisibility();
        }

        private void SetStandaloneTitle(string title)
        {
            if (titleLabel == null)
            {
                return;
            }

            titleLabel.text = title;
        }

        private GameObject CreateFallbackContentPanel(string name, string label)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(screenRoot.transform, false);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1180f, 320f);
            panelRect.anchoredPosition = Vector2.zero;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = string.Equals(name, "pitFireTeam_SquadControlSettingsPanel", StringComparison.Ordinal)
                ? new Color(0f, 0f, 0f, 0.702f)
                : new Color(0.09f, 0.11f, 0.12f, 0.94f);

            GameObject labelObject = CreateText($"{name}_Label", label, 30, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(panelRect, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(420f, 48f);
            labelRect.anchoredPosition = Vector2.zero;

            return panel;
        }

        private GameObject CreateText(string name, string text, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            TextMeshProUGUI textLabel = textObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                textLabel.font = templateLabel.font;
                textLabel.fontSharedMaterial = templateLabel.fontSharedMaterial;
                textLabel.color = templateLabel.color;
            }
            else
            {
                textLabel.color = Color.white;
            }

            textLabel.text = text;
            textLabel.fontSize = fontSize;
            textLabel.alignment = alignment;
            textLabel.enableWordWrapping = false;
            return textObject;
        }

    }
}
