using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.Builds;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Screens;
using EFT.UI.Settings;
using pitTeam.Utils;
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
using UnityEngine.EventSystems;
using UnityEngine.Events;
using dropDownItem = GClass3682;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace pitTeam.Patches
{
    internal class FriendlyProfileDropdownItem : dropDownItem
    {
    }

    internal class FriendlyTeammateBodyResponse<T>
    {
        public int err { get; set; }
        public string errmsg { get; set; }
        public T data { get; set; }
    }

    internal class FriendlyTeammateLoadoutOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class FriendlyTeammateTacticOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class FriendlyTeammateProfileOptions
    {
        public string CurrentLoadoutId { get; set; }
        public string CurrentTactic { get; set; }
        public float Aggression { get; set; } = 50f;
        public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; }
        public List<FriendlyTeammateTacticOption> Tactics { get; set; }
        public FriendlyTeammateProfileRecoveryNotice RecoveryNotice { get; set; }
    }

    internal class FriendlyTeammateProfileRecoveryNotice
    {
        public bool Recovered { get; set; }
        public int RemovedItemCount { get; set; }
        public string Message { get; set; }
    }

    internal class FriendlyTeammateSuitRequest
    {
        public string aid { get; set; }
        public string[] suit { get; set; }
    }

    internal class FriendlyTeammateLoadoutRequest
    {
        public string aid { get; set; }
        public string loadoutId { get; set; }
    }

    internal class FriendlyTeammateRenameRequest
    {
        public string aid { get; set; }
        public string nickname { get; set; }
    }

    internal class FriendlyTeammateAggressionRequest
    {
        public string aid { get; set; }
        public float aggression { get; set; }
    }

    internal class FriendlyTeammateTacticRequest
    {
        public string aid { get; set; }
        public string tactic { get; set; }
    }

    internal sealed class LoadoutEditorHeaderDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;

        private bool _dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = Target != null;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || Target == null)
            {
                return;
            }

            Vector2 delta = eventData.delta;
            Target.offsetMin += delta;
            Target.offsetMax += delta;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }
    }

    internal sealed class ProfileTooltipHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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

    internal class FriendlyDropdownNamePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(GClass3672), "NameLocalizationKey");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3672 __instance, ref string __result)
        {
            if (OtherPlayerProfileScreenPatch.CustomDropdownIds.Contains(__instance.Id))
            {
                __result = __instance.Name;
                return false;
            }

            return true;
        }
    }

    internal partial class OtherPlayerProfileScreenPatch : ModulePatch
    {
        private sealed class ProfileSkillsHealthController : IHealthController
        {
            private readonly Profile.ProfileHealthClass _health;
            private readonly GClass2182 _snapshot;

            public ProfileSkillsHealthController(Profile.ProfileHealthClass health)
            {
                _health = health ?? throw new ArgumentNullException(nameof(health));
                _snapshot = new GClass2182(health);
            }

            public event Action<IEffect> EffectAddedEvent { add { } remove { } }
            public event Action<IEffect> EffectStartedEvent { add { } remove { } }
            public event Action<IEffect> EffectUpdatedEvent { add { } remove { } }
            public event Action<IEffect> EffectResidualEvent { add { } remove { } }
            public event Action<IEffect> EffectRemovedEvent { add { } remove { } }
            public event Action<IEffect> EffectStatusUpdateEvent { add { } remove { } }
            public event Action<EBodyPart, float, DamageInfoStruct> ApplyDamageEvent { add { } remove { } }
            public event Action<EBodyPart, float, DamageInfoStruct> HealthChangedEvent { add { } remove { } }
            public event Action<float> EnergyChangedEvent { add { } remove { } }
            public event Action<float> HydrationChangedEvent { add { } remove { } }
            public event Action<float> TemperatureChangedEvent { add { } remove { } }
            public event Action<EBodyPart, EDamageType> BodyPartDestroyedEvent { add { } remove { } }
            public event Action<EBodyPart, ValueStruct> BodyPartRestoredEvent { add { } remove { } }
            public event Action<EDamageType> DiedEvent { add { } remove { } }
            public event Action<IEffect> HealerDoneEvent { add { } remove { } }
            public event Action<Vector3, float, float> BurnEyesEvent { add { } remove { } }
            public event Action<IPlayerBuff> StimulatorBuffEvent { add { } remove { } }
            public event Action<IPlayerBuff> StimulatorBuffActivationEvent { add { } remove { } }

            public float FallSafeHeight { set { } }
            public bool IsAlive => GetBodyPartHealth(EBodyPart.Common).Current > 0f;
            public float HealthRate => 0f;
            public float EnergyRate => 0f;
            public float HydrationRate => 0f;
            public float TemperatureRate => 0f;
            public float DamageCoeff => 1f;
            public float StaminaCoeff => 1f;
            public int UpdateTime => _health.UpdateTime ?? 0;
            public HealthEffects BodyPartEffects => default;
            public float CarryingWeightAbsoluteModifier => 0f;
            public float CarryingWeightRelativeModifier => 1f;
            public ValueStruct Hydration => _snapshot.Hydration;
            public ValueStruct Energy => _snapshot.Energy;
            public ValueStruct Temperature => _snapshot.Temperature;
            public ValueStruct Poison => _snapshot.Poison;

            public bool IsBodyPartBroken(EBodyPart bodyPart) => false;
            public bool IsBodyPartDestroyed(EBodyPart bodyPart) => GetBodyPartHealth(bodyPart).Current <= 0f;
            public void GetBodyPartsInCriticalCondition(float threshold, out int all, out int vital)
            {
                all = 0;
                vital = 0;
            }

            public void SetEncumbered(bool encumbered) { }
            public void SetOverEncumbered(bool encumbered) { }
            public void AddFatigue() { }
            public void AddImmunityNotificationEffect() { }
            public TEffect FindExistingEffect<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => default;
            public TEffect FindActiveEffect<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => default;
            public IEnumerable<TEffect> FindActiveEffects<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => Enumerable.Empty<TEffect>();
            public IEnumerable<IEffect> GetAllActiveEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public IEnumerable<IEffect> GetAllEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public IEnumerable<IEffect> GetAllResidualEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public GStruct382<EBodyPart> BodyPartsPriority(Item item, bool continuousHealEnabled) => default;
            public bool IsItemForHealing(Item item) => false;
            public IResult HasPartsToApply(Item item) => null;
            public bool CanApplyItem(Item item, EBodyPart bodyPart) => false;
            public bool ApplyItem(Item item, GStruct382<EBodyPart> bodyPart, float? amount = null) => false;
            public bool ApplyItem(Item item, EBodyPart bodyPart, float? amount = null) => false;
            public void CancelApplyingItem() { }
            public void ManualUpdate(float deltaTime) { }
            public void PropagateAllEffects() { }
            public string[] ActiveBuffsNames() => Array.Empty<string>();
            public void DisableMetabolism() { }

            public ValueStruct GetBodyPartHealth(EBodyPart bodyPart, bool rounded = false)
            {
                if (bodyPart == EBodyPart.Common)
                {
                    return _snapshot.GetBodyPartHealth(bodyPart, rounded);
                }

                if (_health.BodyParts != null && _health.BodyParts.TryGetValue(bodyPart, out Profile.ProfileHealthClass.ProfileBodyPartHealthClass part) && part?.Health != null)
                {
                    return new ValueStruct
                    {
                        Current = part.Health.Current,
                        Maximum = part.Health.Maximum
                    };
                }

                return default;
            }
        }

        private const string OptionsRoute = "/singleplayer/pitfireteam/teammate/profile/options";
        private const string SuitRoute = "/singleplayer/pitfireteam/teammate/profile/suit";
        private const string RenameRoute = "/singleplayer/pitfireteam/teammate/profile/rename";
        private const string LoadoutRoute = "/singleplayer/pitfireteam/teammate/profile/loadout";
        private const string DefaultEquipmentRoute = "/singleplayer/pitfireteam/teammate/profile/default-equipment";
        private const string RepairEquipmentRoute = "/singleplayer/pitfireteam/teammate/profile/repair-equipment";
        private const string AggressionRoute = "/singleplayer/pitfireteam/teammate/profile/aggression";
        private const string TacticRoute = "/singleplayer/pitfireteam/teammate/profile/tactic";
        private const string DefaultLoadoutId = "000000000000000000000000";
        private const string DefaultLoadoutName = "Default";

        private static readonly FieldInfo PlayerModelWindowField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
        private static readonly FieldInfo ClothingPanelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_clothingPanel");
        private static readonly FieldInfo NicknameLabelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_nicknameLabel");
        private static readonly FieldInfo NicknameFieldInputField = AccessTools.Field(typeof(NicknameField), "_inputField");
        private static readonly FieldInfo NicknameFieldStatusLabelField = AccessTools.Field(typeof(NicknameField), "_statusLabel");
        private static readonly FieldInfo NicknameFieldUsedSymbolsField = AccessTools.Field(typeof(NicknameField), "_usedSymbolsCount");
        private static readonly FieldInfo BackButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_backButton");
        private static readonly FieldInfo HideoutButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_hideoutButton");
        private static readonly MethodInfo HideoutButtonHandlerMethod = AccessTools.Method(typeof(OtherPlayerProfileScreen), "method_11");
        private static readonly FieldInfo SettingsScreenGameTabField = AccessTools.Field(typeof(SettingsScreen), "_gameSettingsScreen");
        private static readonly FieldInfo GameSettingsSliderTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_fov");
        private static readonly FieldInfo NumberSliderValueInputField = AccessTools.Field(typeof(NumberSlider), "_valueInput");
        private static readonly FieldInfo ReportPanelField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_reportPanel");
        private static readonly FieldInfo OverallStatsPanelField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_overallStatsPanel");
        private static readonly FieldInfo AchievementsProgressBlockField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_achievementsProgressBlock");
        private static readonly FieldInfo AchievementsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_achievementsBlockPlaceholder");
        private static readonly FieldInfo WeaponsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_weaponsBlockPlaceholder");
        private static readonly FieldInfo NonWeaponItemsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_nonWeaponItemsBlockPlaceholder");
        private static readonly FieldInfo WeaponsGridLayoutGroupField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_weaponsGridLayoutGroup");
        private static readonly FieldInfo NonWeaponItemsGridLayoutGroupField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_nonWeaponItemsGridLayoutGroup");
        private static readonly FieldInfo SkillsScreenListTabField = AccessTools.Field(typeof(SkillsScreen), "_listTab");
        private static readonly FieldInfo SkillsScreenThumbsTabField = AccessTools.Field(typeof(SkillsScreen), "_thumbsTab");
        private static readonly FieldInfo SkillsScreenTabsControllerField = AccessTools.Field(typeof(SkillsScreen), "gclass3808_0");
        private static readonly MethodInfo SkillsScreenShowMethod = AccessTools.Method(typeof(SkillsScreen), "Show");
        private static readonly FieldInfo SkillsAndMasteringSkillsScreenField = AccessTools.Field(typeof(SkillsAndMasteringScreen), "_skillsScreen");
        private static readonly FieldInfo InventorySkillsAndMasteringScreenField = AccessTools.Field(typeof(InventoryScreen), "_skillsAndMasteringScreen");
        private static readonly FieldInfo SkillManagerSkillsField = AccessTools.Field(typeof(SkillManager), nameof(SkillManager.Skills));
        private static readonly FieldInfo SkillManagerDisplayListField = AccessTools.Field(typeof(SkillManager), nameof(SkillManager.DisplayList));
        private static readonly FieldInfo UiField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "UI");
        private static readonly FieldInfo UpperDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_upperButtonDropDown");
        private static readonly FieldInfo LowerDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_lowerButtonDropDown");
        private static readonly FieldInfo TransferItemsScreenStashPanelField = AccessTools.Field(typeof(TransferItemsScreen), "_stashPanel");
        private static readonly FieldInfo InventoryScreenItemsPanelField = AccessTools.Field(typeof(InventoryScreen), "_itemsPanel");
        private static readonly FieldInfo ItemsPanelComplexStashPanelField = AccessTools.Field(typeof(ItemsPanel), "_complexStashPanel");
        private static readonly FieldInfo ComplexStashPanelLootPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_lootPanel");
        private static readonly FieldInfo ComplexStashPanelContainersPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_containersPanel");
        private static readonly FieldInfo ComplexStashPanelEquipmentPanelSourceField = AccessTools.Field(typeof(ComplexStashPanel), "_equipmentPanelSource");
        private static readonly FieldInfo ComplexStashPanelComplexPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_complexPanel");
        private static readonly FieldInfo ComplexStashPanelContainerNamePanelField = AccessTools.Field(typeof(ComplexStashPanel), "_containerNamePanel");
        private static readonly FieldInfo ContainersPanelSlotViewsContainerField = AccessTools.Field(typeof(ContainersPanel), "_slotViewsContainer");
        private static readonly FieldInfo ContainersPanelDictionaryField = AccessTools.Field(typeof(ContainersPanel), "dictionary_0");
        private static readonly FieldInfo ContainersPanelDogtagSlotViewField = AccessTools.Field(typeof(ContainersPanel), "slotView_0");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private static readonly string GearIconPath = Path.Combine(PluginDirectory, "gear.png");
        private static readonly string BrainIconPath = Path.Combine(PluginDirectory, "brain.png");
        private static readonly Vector2 SkillsScreenOffset = new Vector2(-38f, -100f);
        private static readonly Vector2 FactionBadgeFollowerOffset = new Vector2(0f, -60f);
        private static readonly EquipmentSlot[] LoadoutEditorContainerSlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack
        };
        private static readonly EquipmentSlot[] LoadoutEditorEquipmentSlots =
        {
            EquipmentSlot.Scabbard,
            EquipmentSlot.Holster,
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Eyewear,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Headwear,
            EquipmentSlot.Earpiece,
            EquipmentSlot.ArmorVest,
            EquipmentSlot.ArmBand,
            EquipmentSlot.Dogtag
        };
        private static readonly HashSet<ESkillId> HiddenFollowerSkills = new HashSet<ESkillId>
        {
            ESkillId.Charisma,
            ESkillId.Attention,
            ESkillId.Intellect,
            ESkillId.Search,
            ESkillId.WeaponTreatment,
            ESkillId.Crafting,
            ESkillId.HideoutManagement
        };

        public static ResultProfile ViewedProfile { get; set; }
        public static OtherPlayerProfileScreen ActiveProfileScreen { get; set; }
        public static InventoryController ActiveProfileInventoryController { get; set; }
        public static GClass3388 ActiveProfileBackendInventoryController { get; set; }
        public static ISession ActiveProfileSession { get; set; }
        public static InventoryPlayerModelWithStatsWindow ActiveProfilePlayerModelWindow { get; set; }
        public static Transform LoadoutSelector { get; set; }
        public static Transform AggressionSelector { get; set; }
        public static DefaultUIButton EditLoadoutButton { get; set; }
        public static Transform EditLoadoutButtonRoot { get; set; }
        public static GameObject LoadoutEditorOverlayRoot { get; set; }
        public static GameObject ProfileRecoveryOverlayRoot { get; set; }
        public static SimpleStashPanel LoadoutEditorStashPanel { get; set; }
        public static ComplexStashPanel LoadoutEditorEquipmentPanel { get; set; }
        public static Profile LoadoutEditorProfile { get; set; }
        public static InventoryController LoadoutEditorInventoryController { get; set; }
        public static ItemContextAbstractClass LoadoutEditorEquipmentContext { get; set; }
        public static FlatItemsDataClass[] LoadoutEditorInitialEquipmentItems { get; set; }
        public static FlatItemsDataClass[] LoadoutEditorInitialStashItems { get; set; }
        private static Dictionary<string, EItemPinLockState> LoadoutEditorOriginalPinLockStates { get; } =
            new Dictionary<string, EItemPinLockState>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Item> LoadoutEditorEquipmentItemsById { get; } =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Item> LoadoutEditorStashItemsById { get; } =
            new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        public static string ActiveTeammateLoadoutId { get; set; }
        public static string ActiveTeammateLoadoutName { get; set; }
        public static string LoadoutEditorSourceLoadoutId { get; set; }
        public static string LoadoutEditorSourceLoadoutName { get; set; }
        public static SkillsScreen SkillsPanel { get; set; }
        public static RectTransform SkillsPanelHost { get; set; }
        public static CustomTextMeshProUGUI OriginalNicknameLabel { get; set; }
        public static DefaultUIButton NicknameRenameButton { get; set; }
        public static Transform NicknameRenameButtonRoot { get; set; }
        public static GameObject RenameOverlayRoot { get; set; }
        public static NicknameField RenameOverlayField { get; set; }
        public static Vector2? OriginalNicknameAnchoredPosition { get; set; }
        public static RectTransform FactionBadgeIconsContainer { get; set; }
        public static Vector2? OriginalFactionBadgeAnchoredPosition { get; set; }
        public static string OriginalHideoutButtonText { get; set; }
        public static int? OriginalHideoutButtonFontSize { get; set; }
        public static GameObject OriginalHideoutButtonPanel { get; set; }
        public static bool? OriginalHideoutButtonPanelActive { get; set; }
        public static List<MongoID> CustomDropdownIds { get; } = new List<MongoID>();
        private static List<GameObject> HiddenRenameButtonDecorations { get; } = new List<GameObject>();
        private static Dictionary<GameObject, bool> HiddenRightSideRoots { get; } = new Dictionary<GameObject, bool>();
        private static Dictionary<RectTransform, Vector2> ProfileScaleOriginalPositions { get; } = new Dictionary<RectTransform, Vector2>();
        private static Coroutine PendingAggressionPersistCoroutine { get; set; }
        private static int PendingAggressionPersistRevision { get; set; }
        internal static Action PendingBackOverrideAction { get; set; }
        internal static Action ActiveBackOverrideAction { get; set; }
        private static bool TaskBarDisabledForReturnOverride { get; set; }

        internal static bool IsLoadoutEditorEquipmentContext(ItemContextAbstractClass context)
        {
            if (context == null || LoadoutEditorEquipmentContext == null)
            {
                return false;
            }

            ItemContextAbstractClass current = context;
            while (current != null)
            {
                if (ReferenceEquals(current, LoadoutEditorEquipmentContext))
                {
                    return true;
                }

                current = current.ItemContextAbstractClass;
            }

            return false;
        }

        internal static bool IsLoadoutEditorEquipmentItem(Item item)
        {
            if (item == null || LoadoutEditorProfile?.Inventory?.Equipment == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            if (IsItemInLoadoutEditorRoot(item, LoadoutEditorProfile.Inventory.Equipment))
            {
                return true;
            }

            RebuildLoadoutEditorItemIndexes();
            return TryGetIndexedLoadoutEditorItem(
                item.Id,
                LoadoutEditorEquipmentItemsById,
                LoadoutEditorProfile.Inventory.Equipment,
                out Item indexedItem)
                && ReferenceEquals(indexedItem, item);
        }

        internal static bool TryGetLoadoutEditorEquipmentItem(string itemId, out Item item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(itemId) || LoadoutEditorProfile?.Inventory?.Equipment == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            if (TryGetIndexedLoadoutEditorItem(
                itemId,
                LoadoutEditorEquipmentItemsById,
                LoadoutEditorProfile.Inventory.Equipment,
                out item))
            {
                return true;
            }

            RebuildLoadoutEditorItemIndexes();
            return TryGetIndexedLoadoutEditorItem(
                itemId,
                LoadoutEditorEquipmentItemsById,
                LoadoutEditorProfile.Inventory.Equipment,
                out item);
        }

        internal static bool TryGetLoadoutEditorItem(string itemId, out Item item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(itemId) || LoadoutEditorProfile?.Inventory == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            if (TryGetLoadoutEditorEquipmentItem(itemId, out item))
            {
                return true;
            }

            if (TryGetIndexedLoadoutEditorItem(
                itemId,
                LoadoutEditorStashItemsById,
                LoadoutEditorProfile.Inventory.Stash,
                out item))
            {
                return true;
            }

            RebuildLoadoutEditorItemIndexes();
            if (TryGetIndexedLoadoutEditorItem(
                    itemId,
                    LoadoutEditorEquipmentItemsById,
                    LoadoutEditorProfile.Inventory.Equipment,
                    out item))
            {
                return true;
            }

            return TryGetIndexedLoadoutEditorItem(
                itemId,
                LoadoutEditorStashItemsById,
                LoadoutEditorProfile.Inventory.Stash,
                out item);
        }

        internal static bool IsLoadoutEditorStashItem(Item item)
        {
            if (IsItemInLoadoutEditorRoot(item, LoadoutEditorProfile?.Inventory?.Stash))
            {
                return true;
            }

            RebuildLoadoutEditorItemIndexes();
            return TryGetIndexedLoadoutEditorItem(
                item?.Id,
                LoadoutEditorStashItemsById,
                LoadoutEditorProfile?.Inventory?.Stash,
                out Item indexedItem)
                && ReferenceEquals(indexedItem, item);
        }

        internal static bool IsLoadoutEditorItem(Item item)
        {
            if (IsItemInLoadoutEditorRoot(item, LoadoutEditorProfile?.Inventory?.Stash)
                || IsItemInLoadoutEditorRoot(item, LoadoutEditorProfile?.Inventory?.Equipment))
            {
                return true;
            }

            string itemId = item?.Id;
            return !string.IsNullOrWhiteSpace(itemId)
                && (LoadoutEditorStashItemsById.ContainsKey(itemId)
                    || LoadoutEditorEquipmentItemsById.ContainsKey(itemId));
        }

        internal static void RebuildLoadoutEditorItemIndexes()
        {
            LoadoutEditorEquipmentItemsById.Clear();
            LoadoutEditorStashItemsById.Clear();

            IndexLoadoutEditorItems(LoadoutEditorProfile?.Inventory?.Equipment, LoadoutEditorEquipmentItemsById);
            IndexLoadoutEditorItems(LoadoutEditorProfile?.Inventory?.Stash, LoadoutEditorStashItemsById);
        }

        internal static void ClearLoadoutEditorItemIndexes()
        {
            LoadoutEditorEquipmentItemsById.Clear();
            LoadoutEditorStashItemsById.Clear();
        }

        internal static bool IsLoadoutEditorPinLockInteraction(EItemInfoButton button)
        {
            return button == EItemInfoButton.SetPin
                || button == EItemInfoButton.SetLock
                || button == EItemInfoButton.SetUnPin
                || button == EItemInfoButton.SetUnLock;
        }

        internal static void CaptureLoadoutEditorOriginalPinLocks(Item root)
        {
            LoadoutEditorOriginalPinLockStates.Clear();
            foreach (Item item in EnumerateItemTree(root))
            {
                LoadoutEditorOriginalPinLockStates[item.Id.ToString()] = item.PinLockState;
            }
        }

        internal static void ApplyLoadoutEditorOriginalPinLocks(Item root)
        {
            if (LoadoutEditorOriginalPinLockStates.Count == 0)
            {
                return;
            }

            foreach (Item item in EnumerateItemTree(root))
            {
                if (LoadoutEditorOriginalPinLockStates.TryGetValue(item.Id.ToString(), out EItemPinLockState state))
                {
                    item.PinLockState = state;
                }
            }
        }

        internal static void ClearLoadoutEditorOriginalPinLocks()
        {
            LoadoutEditorOriginalPinLockStates.Clear();
        }

        internal static bool ShouldBlockLoadoutEditorContainerOpen(Item item)
        {
            return item is CompoundItem
                && TryFindLoadoutEditorLockedItemInPath(item, out _);
        }

        internal static bool TryFindLoadoutEditorLockedItemInPath(Item item, out Item lockedItem)
        {
            lockedItem = null;
            if (item == null || LoadoutEditorOverlayRoot == null || !IsLoadoutEditorItem(item))
            {
                return false;
            }

            if (IsLoadoutEditorLockedItem(item))
            {
                lockedItem = item;
                return true;
            }

            foreach (Item parent in item.GetAllParentItems())
            {
                if (parent == null)
                {
                    continue;
                }

                if (IsLoadoutEditorLockedItem(parent))
                {
                    lockedItem = parent;
                    return true;
                }
            }

            return false;
        }

        internal static bool TryFindLoadoutEditorLockedItemInAddress(ItemAddress address, out Item lockedItem)
        {
            lockedItem = null;
            if (address == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            foreach (Item parent in address.GetAllParentItems())
            {
                if (parent == null || !IsLoadoutEditorItem(parent))
                {
                    continue;
                }

                if (IsLoadoutEditorLockedItem(parent))
                {
                    lockedItem = parent;
                    return true;
                }
            }

            return false;
        }

        private static bool IsLoadoutEditorLockedItem(Item item)
        {
            if (item == null)
            {
                return false;
            }

            return item.PinLockState == EItemPinLockState.Locked
                || (LoadoutEditorOriginalPinLockStates.TryGetValue(item.Id.ToString(), out EItemPinLockState originalState)
                    && originalState == EItemPinLockState.Locked);
        }

        private static bool IsItemInLoadoutEditorRoot(Item item, Item root)
        {
            if (item == null || root == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            if (ReferenceEquals(item, root) || string.Equals(item.Id, root.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (Item parent in item.GetAllParentItems(false))
            {
                if (parent == null)
                {
                    continue;
                }

                if (ReferenceEquals(parent, root) || string.Equals(parent.Id, root.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIndexedLoadoutEditorItem(
            string itemId,
            Dictionary<string, Item> itemsById,
            Item root,
            out Item item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(itemId) || itemsById == null || root == null || LoadoutEditorOverlayRoot == null)
            {
                return false;
            }

            if (!itemsById.TryGetValue(itemId, out Item candidate) || candidate == null)
            {
                return false;
            }

            if (!IsItemInLoadoutEditorRoot(candidate, root))
            {
                return false;
            }

            item = candidate;
            return true;
        }

        private static void IndexLoadoutEditorItems(Item root, Dictionary<string, Item> destination)
        {
            if (root == null || destination == null)
            {
                return;
            }

            foreach (Item item in EnumerateLoadoutEditorItemTree(root))
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id))
                {
                    continue;
                }

                destination[item.Id] = item;
            }
        }

        private static IEnumerable<Item> EnumerateItemTree(Item root)
        {
            return EnumerateLoadoutEditorItemTree(root);
        }

        private static IEnumerable<Item> EnumerateLoadoutEditorItemTree(Item root)
        {
            if (root == null)
            {
                yield break;
            }

            HashSet<string> seenIds = new HashSet<string>(StringComparer.Ordinal);
            Stack<Item> pendingItems = new Stack<Item>();
            pendingItems.Push(root);

            while (pendingItems.Count > 0)
            {
                Item current = pendingItems.Pop();
                if (current == null || !seenIds.Add(current.Id))
                {
                    continue;
                }

                yield return current;

                if (current is GClass3248 collection)
                {
                    foreach (Item item in collection.GetAllItemsFromCollection())
                    {
                        if (item != null)
                        {
                            pendingItems.Push(item);
                        }
                    }
                }

                foreach (RepairableComponent repairable in current.GetItemComponentsInChildren<RepairableComponent>(true))
                {
                    if (repairable?.Item != null)
                    {
                        pendingItems.Push(repairable.Item);
                    }
                }

                if (!current.TryGetItemComponent<ArmorHolderComponent>(out ArmorHolderComponent armorHolder)
                    || armorHolder?.ArmorSlots == null)
                {
                    continue;
                }

                foreach (GClass3125 armorSlot in armorHolder.ArmorSlots)
                {
                    if (armorSlot?.ContainedItem != null)
                    {
                        pendingItems.Push(armorSlot.ContainedItem);
                    }
                }
            }
        }

        private static readonly Dictionary<EMenuType, EStateSwitcher> DisabledSettingsTaskBarButton =
            new Dictionary<EMenuType, EStateSwitcher>
            {
                { EMenuType.Settings, EStateSwitcher.Disabled }
            };
        private static readonly Dictionary<EMenuType, EStateSwitcher> EnabledSettingsTaskBarButton =
            new Dictionary<EMenuType, EStateSwitcher>
            {
                { EMenuType.Settings, EStateSwitcher.Enabled }
            };

        private static void MarkSquadRosterDirty(string accountId = null)
        {
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                Components.SquadControlMenuUi.RequestRosterTileRefreshOnNextInject(accountId);
                return;
            }

            Components.SquadControlMenuUi.RequestRosterRefreshOnNextInject();
        }

        internal static void PrepareReturnOverride(Action callback)
        {
            PendingBackOverrideAction = callback;
        }

        internal static void ClearPendingReturnOverride()
        {
            PendingBackOverrideAction = null;
        }

        internal static void RestoreMenuTaskBarForReturnOverride()
        {
            if (!TaskBarDisabledForReturnOverride)
            {
                return;
            }

            TaskBarDisabledForReturnOverride = false;

            try
            {
                if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    return;
                }

                PreloaderUI preloaderUi = MonoBehaviourSingleton<PreloaderUI>.Instance;
                if (preloaderUi?.MenuTaskBar == null)
                {
                    return;
                }

                preloaderUi.MenuTaskBar.SetButtonsInteractable(true);
                preloaderUi.MenuTaskBar.SetCustomButtonsAvailability(EnabledSettingsTaskBarButton);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to restore bottom navigation buttons after teammate profile: {ex.Message}");
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(OtherPlayerProfileScreen),
                "Show",
                new Type[] { typeof(ResultProfile), typeof(InventoryController), typeof(EItemViewType), typeof(ISession) });
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance, ResultProfile profile, InventoryController inventoryController, EItemViewType viewType, ISession session)
        {
            ConfigureBackOverride(__instance);
            ApplyProfileScaleCompensation(__instance);

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                PlayerModelWindowField?.GetValue(__instance) as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow == null)
            {
                return;
            }

            RestoreProfileRightSideContent(__instance);
            ResetTeammateProfileUi(playerModelWindow);

            if (session?.Profile != null
                && session.Profile.AccountId == profile.AccountId)
            {
                RestoreHideoutButtonVisuals(__instance, profile);
                return;
            }

            RestoreHideoutButtonVisuals(__instance, profile);

            FriendlyTeammateProfileOptions options = TryLoadProfileOptions(profile.AccountId);
            if (options == null)
            {
                return;
            }

            if (options.Loadouts == null || options.Loadouts.Count == 0)
            {
                pitFireTeam.Log.LogWarning($"[UI] Teammate profile patch aborted: no loadout options returned for '{profile.AccountId}'.");
                return;
            }

            if (!TryGetClothingPanel(__instance, playerModelWindow, out RectTransform clothingPanel, out InventoryClothingSelectionPanel clothingSelectionPanel, out Transform parent))
            {
                pitFireTeam.Log.LogWarning("[UI] Teammate profile patch aborted: clothing panel not found on profile screen.");
                return;
            }

            Modules.Logger.LogInfo($"[UI] Applying teammate profile customization UI for '{profile.AccountId}'.");
            ViewedProfile = profile;
            TeammateEquipmentBuildsScreenFlow.FinishReturnIfMatches(profile.AccountId);
            ActiveProfileScreen = __instance;
            ActiveProfileInventoryController = inventoryController;
            RememberActiveBackendInventoryController(session, inventoryController);
            ActiveProfileSession = session;
            ActiveProfilePlayerModelWindow = playerModelWindow;
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            playerModelWindow.OnCustomizationChanged += PlayerModelWithStatsWindow_OnCustomizationChanged;

            HideProfileActions(__instance);
            ClearProfileRightSideContent(__instance);
            ConfigureNicknameRenameUi(__instance, playerModelWindow, profile);
            MoveFactionBadgeForFollowerProfile(__instance, playerModelWindow);

            clothingPanel.gameObject.SetActive(true);
            DisplayClothingOptions(profile.PlayerVisualRepresentation, playerModelWindow, inventoryController, clothingSelectionPanel);

            AddViewListClass ui = UiField?.GetValue(__instance) as AddViewListClass;
            if (ui == null)
            {
                return;
            }

            if (LoadoutSelector != null)
            {
                GameObject.Destroy(LoadoutSelector.gameObject);
                LoadoutSelector = null;
            }

            if (AggressionSelector != null)
            {
                GameObject.Destroy(AggressionSelector.gameObject);
                AggressionSelector = null;
            }

            StopPendingAggressionPersist();

            RectTransform clone = GameObject.Instantiate(clothingPanel, parent, true);
            clone.name = "pitFireTeam_LoadoutSelector";
            clone.anchoredPosition = clothingPanel.anchoredPosition + GetProfileControlRowOffset(clothingPanel, 1);

            InventoryClothingSelectionPanel loadoutPanel = clone.GetComponent<InventoryClothingSelectionPanel>();
            if (loadoutPanel == null)
            {
                GameObject.Destroy(clone.gameObject);
                return;
            }

            ui.AddDisposable(loadoutPanel);
            LoadoutSelector = clone;
            try
            {
                ConfigureLoadoutPanel(loadoutPanel, clothingSelectionPanel);
                DisplayLoadoutOptions(__instance, profile, inventoryController, session, loadoutPanel, playerModelWindow, options, replaceLoadoutDropdown: false);
                ApplyLoadoutPanelLayout(loadoutPanel, clothingSelectionPanel);
                if (pitFireTeam.IsFollowerLoadoutRealTransferMode())
                {
                    ReplaceLoadoutDropdownWithEditButton(__instance, profile, loadoutPanel);
                }

                CreateAggressionSliderRow(__instance, clone, parent, profile, options);
                CreateEditLoadoutButton(__instance, clone, parent, profile, 2);
                CreateEditNameButton(__instance, clone, parent, profile, 3);
                DisplaySkillsPanel(__instance, profile, session);
                ShowProfileRecoveryOverlay(__instance, options.RecoveryNotice);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to apply teammate profile elements.");
                Modules.Logger.LogError(ex);
            }
        }

        private static RectTransform CreateAggressionSliderRow(
            OtherPlayerProfileScreen screen,
            RectTransform loadoutSelector,
            Transform parent,
            ResultProfile profile,
            FriendlyTeammateProfileOptions options)
        {
            if (screen == null || loadoutSelector == null || parent == null || profile == null || options == null)
            {
                return null;
            }

            if (AggressionSelector != null)
            {
                GameObject.Destroy(AggressionSelector.gameObject);
                AggressionSelector = null;
            }

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "pitFireTeam_AggressionRow";
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + GetProfileControlRowOffset(loadoutSelector, 1);
            rowClone.gameObject.SetActive(true);

            Transform upperRoot = rowClone.Find("Upper");
            if (upperRoot != null)
            {
                upperRoot.gameObject.SetActive(false);
            }

            Transform lowerRoot = rowClone.Find("Lower");
            if (lowerRoot != null)
            {
                lowerRoot.gameObject.SetActive(false);
            }

            float aggressionValue = Mathf.Clamp(options.Aggression, 0f, 100f);

            CreateAggressionRowContent(rowClone, profile, aggressionValue, true);
            AggressionSelector = rowClone;
            return rowClone;
        }

        private static bool IsMarksmanTactic(string tactic)
        {
            return string.Equals(tactic, "marksman", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetAggressionRowMarksmanState(bool isMarksman)
        {
            if (AggressionSelector == null)
            {
                return;
            }

            CanvasGroup rowCanvasGroup = AggressionSelector.GetComponent<CanvasGroup>() ?? AggressionSelector.gameObject.AddComponent<CanvasGroup>();
            rowCanvasGroup.alpha = 1f;

            CustomTextMeshProUGUI label = AggressionSelector.Find("pitFireTeam_AggressionLabel")?.GetComponent<CustomTextMeshProUGUI>();
            if (label != null)
            {
                label.color = Color.white;
            }

            NumberSlider slider = AggressionSelector.GetComponentsInChildren<NumberSlider>(true)
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, "pitFireTeam_ProfileAggressionSlider", StringComparison.Ordinal));
            if (slider != null)
            {
                slider.enabled = true;
                slider.UpdateValue(isMarksman ? 30f : 50f, false, 0f, 100f);

                Slider stockSlider = slider.GetComponentInChildren<Slider>(true);
                if (stockSlider != null)
                {
                    stockSlider.interactable = true;
                }

                TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
                if (valueInput != null)
                {
                    ConfigureSliderValueInputChrome(valueInput);
                    valueInput.readOnly = false;
                    valueInput.interactable = true;
                }
            }

            Transform existingTooltip = AggressionSelector.Find("pitFireTeam_AggressionDisabledTooltip");
            if (existingTooltip != null)
            {
                GameObject.Destroy(existingTooltip.gameObject);
            }
        }

        private static void CreateAggressionRowContent(RectTransform rowRoot, ResultProfile profile, float aggressionValue, bool interactable)
        {
            if (rowRoot == null || profile == null)
            {
                return;
            }

            CanvasGroup rowCanvasGroup = rowRoot.GetComponent<CanvasGroup>() ?? rowRoot.gameObject.AddComponent<CanvasGroup>();
            rowCanvasGroup.alpha = 1f;

            CustomTextMeshProUGUI label = CreateAggressionLabel(
                "pitFireTeam_AggressionLabel",
                rowRoot,
                GetSocialUiText("ProfileAggression"),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(44f, 0f),
                new Vector2(190f, 28f),
                18f,
                TextAlignmentOptions.MidlineLeft);

            NumberSlider slider = CloneStockNumberSlider(rowRoot);
            if (slider == null)
            {
                return;
            }

            RectTransform sliderRoot = slider.transform as RectTransform;
            if (sliderRoot != null)
            {
                float sliderHeight = sliderRoot.sizeDelta.y > 0f ? sliderRoot.sizeDelta.y : 36f;
                sliderRoot.anchorMin = new Vector2(0f, 0.5f);
                sliderRoot.anchorMax = new Vector2(0f, 0.5f);
                sliderRoot.pivot = new Vector2(0f, 0.5f);
                sliderRoot.anchoredPosition = new Vector2(243f, 42f);
                sliderRoot.sizeDelta = new Vector2(300f, sliderHeight);
                sliderRoot.localScale = Vector3.one;
            }

            Transform captionRoot = sliderRoot?.Find("Caption");
            if (captionRoot != null)
            {
                GameObject.Destroy(captionRoot.gameObject);
            }

            slider.Show(0f, 100f, "0");
            slider.UpdateValue(Mathf.Round(aggressionValue), false, 0f, 100f);

            Slider stockSlider = slider.GetComponentInChildren<Slider>(true);
            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            ConfigureSliderValueInputChrome(valueInput);
            if (stockSlider != null)
            {
                stockSlider.interactable = interactable;
            }

            slider.enabled = interactable;

            if (valueInput != null)
            {
                valueInput.readOnly = !interactable;
                valueInput.interactable = interactable;
            }
            if (!interactable)
            {
                Color disabledColor = new Color(0.62f, 0.62f, 0.62f, 1f);
                if (label != null)
                {
                    label.color = disabledColor;
                }
            }

            slider.Bind(value =>
            {
                int roundedValue = Mathf.RoundToInt(value);
                if (interactable)
                {
                    ScheduleAggressionPersist(profile.AccountId, roundedValue);
                }
            });
        }

        private static CustomTextMeshProUGUI CreateAggressionLabel(
            string name,
            RectTransform parent,
            string text,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            CustomTextMeshProUGUI label = labelObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = new Color(0.87f, 0.87f, 0.84f, 1f);
            label.raycastTarget = false;
            return label;
        }

        private static SettingsScreen ResolveSettingsScreenTemplate()
        {
            return Resources.FindObjectsOfTypeAll<SettingsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    SettingsScreenGameTabField?.GetValue(screen) is GameSettingsTab);
        }

        private static GameSettingsTab ResolveGameSettingsTabTemplate()
        {
            SettingsScreen settingsScreen = ResolveSettingsScreenTemplate();
            return SettingsScreenGameTabField?.GetValue(settingsScreen) as GameSettingsTab;
        }

        private static RectTransform ResolveSettingsSliderContainerTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            if (gameSettingsTab == null)
            {
                return null;
            }

            Transform sliderRoot = gameSettingsTab.transform.Find("Image/Scroll View/Viewport/Other Settings/Scrolls/FOV");
            if (sliderRoot is RectTransform sliderRect)
            {
                return sliderRect;
            }

            NumberSlider fallbackSlider = ResolveSettingsSliderTemplate();
            return fallbackSlider?.transform as RectTransform;
        }

        private static NumberSlider ResolveSettingsSliderTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            return GameSettingsSliderTemplateField?.GetValue(gameSettingsTab) as NumberSlider;
        }

        private static NumberSlider CloneStockNumberSlider(RectTransform parent)
        {
            RectTransform templateRoot = ResolveSettingsSliderContainerTemplate();
            if (templateRoot == null)
            {
                return null;
            }

            RectTransform sliderRoot = GameObject.Instantiate(templateRoot, parent, false);
            NumberSlider slider = sliderRoot.GetComponent<NumberSlider>() ?? sliderRoot.GetComponentInChildren<NumberSlider>(true);
            if (slider == null)
            {
                GameObject.Destroy(sliderRoot.gameObject);
                return null;
            }

            slider.name = "pitFireTeam_ProfileAggressionSlider";
            sliderRoot.localScale = Vector3.one;

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            ConfigureSliderValueInputChrome(valueInput);
            HideStockLabelContainers(sliderRoot, valueInput?.transform);

            slider.gameObject.SetActive(true);
            return slider;
        }

        private static void ConfigureSliderValueInputChrome(TMP_InputField valueInput)
        {
            if (valueInput == null)
            {
                return;
            }

            Image background = valueInput.GetComponent<Image>();
            if (background != null)
            {
                background.color = Color.clear;
                background.raycastTarget = true;
                valueInput.targetGraphic = background;
            }

            ColorBlock colors = valueInput.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = Color.clear;
            colors.pressedColor = Color.clear;
            colors.selectedColor = Color.clear;
            colors.disabledColor = Color.clear;
            valueInput.colors = colors;
        }

        private static void HideStockLabelContainers(Transform root, Transform exemptRoot)
        {
            if (root == null)
            {
                return;
            }

            HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                DestroyStockLabelObject(exemptRoot, label?.transform);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                DestroyStockLabelObject(exemptRoot, label?.transform);
            }
        }

        private static void HideStockLabelContainer(Transform root, Transform exemptRoot, Transform labelTransform, HashSet<GameObject> hiddenObjects)
        {
            if (root == null || labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && (labelTransform == exemptRoot || labelTransform.IsChildOf(exemptRoot)))
            {
                return;
            }

            Transform current = labelTransform;
            while (current != null && current != root)
            {
                if (current.TryGetComponent(out LayoutElement _))
                {
                    if (hiddenObjects.Add(current.gameObject))
                    {
                        current.gameObject.SetActive(false);
                    }

                    return;
                }

                current = current.parent;
            }
        }

        private static void DestroyStockLabelObject(Transform exemptRoot, Transform labelTransform)
        {
            if (labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && (labelTransform == exemptRoot || labelTransform.IsChildOf(exemptRoot)))
            {
                return;
            }

            GameObject.Destroy(labelTransform.gameObject);
        }

        private static void ScheduleAggressionPersist(string accountId, int aggression)
        {
            StopPendingAggressionPersist();

            if (string.IsNullOrWhiteSpace(accountId) || pitFireTeam.Instance == null)
            {
                return;
            }

            int revision = ++PendingAggressionPersistRevision;
            PendingAggressionPersistCoroutine = pitFireTeam.Instance.StartCoroutine(
                PersistAggressionDelayed(accountId, aggression, revision));
        }

        internal static void StopPendingAggressionPersist()
        {
            if (PendingAggressionPersistCoroutine != null && pitFireTeam.Instance != null)
            {
                pitFireTeam.Instance.StopCoroutine(PendingAggressionPersistCoroutine);
            }

            PendingAggressionPersistCoroutine = null;
        }

        private static System.Collections.IEnumerator PersistAggressionDelayed(string accountId, int aggression, int revision)
        {
            yield return new WaitForSecondsRealtime(0.35f);

            if (revision != PendingAggressionPersistRevision)
            {
                yield break;
            }

            PendingAggressionPersistCoroutine = null;

            try
            {
                string responseJson = RequestHandler.PostJson(AggressionRoute, SerializeBody(new FriendlyTeammateAggressionRequest
                {
                    aid = accountId,
                    aggression = aggression
                }));
                EnsureBodySuccess(responseJson);
                MarkSquadRosterDirty(accountId);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate aggression change.");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool IsDefaultTacticSelection(string tactic)
        {
            return string.IsNullOrWhiteSpace(tactic)
                || string.Equals(tactic, "default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tactic, "rifleman", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tactic, "balanced", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetTacticDisplayName(string tactic)
        {
            if (string.IsNullOrWhiteSpace(tactic))
            {
                return GetSocialUiText("ProfileTactic");
            }

            if (IsDefaultTacticSelection(tactic))
            {
                return GetSocialUiText("ProfileTactic");
            }

            if (string.Equals(tactic, "marksman", StringComparison.OrdinalIgnoreCase))
            {
                return GetSocialUiText("ProfileTacticMarksman");
            }

            if (string.Equals(tactic, "protector", StringComparison.OrdinalIgnoreCase))
            {
                return GetSocialUiText("ProfileTacticProtector");
            }

            return tactic;
        }

        private static void CreateEditLoadoutButton(OtherPlayerProfileScreen screen, RectTransform loadoutSelector, Transform parent, ResultProfile profile, int rowOffset = 1)
        {
            if (screen == null || loadoutSelector == null || parent == null || profile == null)
            {
                return;
            }

            if (EditLoadoutButton != null)
            {
                if (EditLoadoutButtonRoot != null)
                {
                    GameObject.Destroy(EditLoadoutButtonRoot.gameObject);
                    EditLoadoutButtonRoot = null;
                }

                EditLoadoutButton = null;
            }

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "pitFireTeam_LoadoutEdit";
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + GetProfileControlRowOffset(loadoutSelector, Mathf.Max(1, rowOffset));
            rowClone.gameObject.SetActive(true);

            Transform upperRoot = rowClone.Find("Upper");
            if (upperRoot != null)
            {
                upperRoot.gameObject.SetActive(false);
            }

            Transform lowerRoot = rowClone.Find("Lower");
            if (lowerRoot != null)
            {
                lowerRoot.gameObject.SetActive(false);
            }

            DefaultUIButton buttonTemplate = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                GameObject.Destroy(rowClone.gameObject);
                pitFireTeam.Log.LogWarning("[UI] Edit Loadout button aborted: hideout button template not found.");
                return;
            }

            DefaultUIButton button = GameObject.Instantiate(buttonTemplate, rowClone, false);
            if (button == null)
            {
                GameObject.Destroy(rowClone.gameObject);
                pitFireTeam.Log.LogWarning("[UI] Edit Loadout button aborted: cloned hideout button not found.");
                return;
            }

            button.name = "pitFireTeam_EditLoadoutButton";
            button.gameObject.SetActive(true);
            button.Interactable = true;
            bool realTransferMode = pitFireTeam.IsFollowerLoadoutRealTransferMode();
            button.SetRawText(
                realTransferMode
                    ? GetSocialUiText("BuyGearLoadout")
                    : GetSocialUiText("EditLoadout"),
                18);
            HideProfileButtonIconContainer(button);
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() =>
            {
                if (realTransferMode)
                {
                    TeammateEquipmentBuildsScreenFlow.Open(profile, session: ActiveProfileSession, inventoryController: ActiveProfileInventoryController);
                    return;
                }

                ShowLoadoutEditorOverlay(screen, profile);
            });

            if (button.transform is RectTransform buttonRect && buttonTemplate.transform is RectTransform templateRect)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.sizeDelta = templateRect.sizeDelta;
                buttonRect.localScale = Vector3.one;
            }

            EditLoadoutButtonRoot = rowClone;
            EditLoadoutButton = button;
        }

        internal static IHealthController CreateProfileHealthController(Profile.ProfileHealthClass health)
        {
            return new ProfileSkillsHealthController(health ?? new Profile.ProfileHealthClass());
        }

        private static Vector2 GetProfileControlRowOffset(RectTransform reference, int rowOffset)
        {
            return new Vector2(0f, -72f * Mathf.Max(1, rowOffset) * ResolveUiScaleCompensation(reference));
        }

        private static void ApplyProfileScaleCompensation(OtherPlayerProfileScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            float compensation = ResolveUiScaleCompensation(screen);
            DefaultUIButton backButton = BackButtonField?.GetValue(screen) as DefaultUIButton;
            ApplyProfileScaleCompensation(backButton?.transform as RectTransform, compensation);
            ApplyProfileScaleCompensation(screen.transform.Find("Background") as RectTransform, compensation);
        }

        private static void ApplyProfileScaleCompensation(RectTransform rect, float compensation)
        {
            if (rect == null)
            {
                return;
            }

            if (!ProfileScaleOriginalPositions.TryGetValue(rect, out Vector2 originalPosition))
            {
                originalPosition = rect.anchoredPosition;
                ProfileScaleOriginalPositions[rect] = originalPosition;
            }

            rect.anchoredPosition = new Vector2(originalPosition.x, originalPosition.y * compensation);
        }

        private static float ResolveUiScaleCompensation(Component reference)
        {
            Canvas canvas = reference != null ? reference.GetComponentInParent<Canvas>() : null;
            CanvasScaler scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler == null || scaler.scaleFactor <= 0.001f || GClass3825.Float_0 <= 0.001f)
            {
                return 1f;
            }

            return Mathf.Clamp(GClass3825.Float_0 / scaler.scaleFactor, 0.5f, 2f);
        }

        private static void ConfigureBackOverride(OtherPlayerProfileScreen screen)
        {
            if (screen == null)
            {
                PendingBackOverrideAction = null;
                ActiveBackOverrideAction = null;
                return;
            }

            Action callback = PendingBackOverrideAction;
            PendingBackOverrideAction = null;
            ActiveBackOverrideAction = callback;

            if (callback == null)
            {
                return;
            }

            DisableMenuTaskBarForReturnOverride();
        }

        internal static void DisableMenuTaskBarForReturnOverride()
        {
            if (TaskBarDisabledForReturnOverride)
            {
                return;
            }

            try
            {
                if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    return;
                }

                PreloaderUI preloaderUi = MonoBehaviourSingleton<PreloaderUI>.Instance;
                if (preloaderUi?.MenuTaskBar == null)
                {
                    return;
                }

                if (!preloaderUi.MenuTaskBar.gameObject.activeSelf)
                {
                    return;
                }

                TaskBarDisabledForReturnOverride = true;
                preloaderUi.MenuTaskBar.SetButtonsInteractable(false, "Taskbar/Unavailable");
                preloaderUi.MenuTaskBar.SetCustomButtonsAvailability(DisabledSettingsTaskBarButton);
            }
            catch (Exception ex)
            {
                TaskBarDisabledForReturnOverride = false;
                pitFireTeam.Log.LogWarning($"[UI] Failed to disable bottom navigation buttons for teammate profile: {ex.Message}");
            }
        }

        private static bool TryGetClothingPanel(
            OtherPlayerProfileScreen screen,
            InventoryPlayerModelWithStatsWindow playerModelWindow,
            out RectTransform clothingPanel,
            out InventoryClothingSelectionPanel clothingSelectionPanel,
            out Transform parent)
        {
            clothingPanel = null;
            clothingSelectionPanel = null;
            parent = null;

            clothingSelectionPanel = ClothingPanelField?.GetValue(playerModelWindow) as InventoryClothingSelectionPanel;
            if (clothingSelectionPanel == null)
            {
                Transform playerModelTransform = playerModelWindow.transform;
                Transform hierarchyPanel = playerModelTransform.Find("ClothingPanel")
                    ?? playerModelTransform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen?.transform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen?.transform.Find("ClothingPanel");

                if (hierarchyPanel == null)
                {
                    hierarchyPanel = FindChildRecursive(playerModelTransform, "ClothingPanel")
                        ?? FindChildRecursive(screen?.transform, "ClothingPanel");
                }

                clothingSelectionPanel = hierarchyPanel?.GetComponent<InventoryClothingSelectionPanel>();
            }

            clothingPanel = clothingSelectionPanel?.transform as RectTransform;
            parent = clothingPanel?.parent;
            return clothingPanel != null && clothingSelectionPanel != null && parent != null;
        }

        private static void ResetTeammateProfileUi(InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            CloseProfileRecoveryOverlay();
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            ViewedProfile = null;
            ActiveProfileScreen = null;
            ActiveProfileInventoryController = null;
            ActiveProfileSession = null;
            ActiveProfilePlayerModelWindow = null;
            ActiveTeammateLoadoutId = null;
            ActiveTeammateLoadoutName = null;
            RestoreFactionBadgePosition();

            if (LoadoutSelector != null)
            {
                GameObject.Destroy(LoadoutSelector.gameObject);
                LoadoutSelector = null;
            }

            if (EditLoadoutButtonRoot != null)
            {
                GameObject.Destroy(EditLoadoutButtonRoot.gameObject);
                EditLoadoutButtonRoot = null;
            }

            EditLoadoutButton = null;

            if (NicknameRenameButtonRoot != null)
            {
                GameObject.Destroy(NicknameRenameButtonRoot.gameObject);
                NicknameRenameButtonRoot = null;
            }

            NicknameRenameButton = null;

            if (TryGetClothingPanel(null, playerModelWindow, out RectTransform clothingPanel, out _, out _)
                && clothingPanel != null)
            {
                clothingPanel.gameObject.SetActive(false);
            }
        }

        private static void MoveFactionBadgeForFollowerProfile(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            RectTransform iconsContainer = FindFactionBadgeIconsContainer(screen, playerModelWindow);
            if (iconsContainer == null)
            {
                return;
            }

            if (!ReferenceEquals(FactionBadgeIconsContainer, iconsContainer))
            {
                RestoreFactionBadgePosition();
                FactionBadgeIconsContainer = iconsContainer;
                OriginalFactionBadgeAnchoredPosition = iconsContainer.anchoredPosition;
            }

            if (OriginalFactionBadgeAnchoredPosition == null)
            {
                OriginalFactionBadgeAnchoredPosition = iconsContainer.anchoredPosition;
            }

            iconsContainer.anchoredPosition = OriginalFactionBadgeAnchoredPosition.Value + FactionBadgeFollowerOffset;
        }

        public static void RestoreFactionBadgePosition()
        {
            if (FactionBadgeIconsContainer != null && OriginalFactionBadgeAnchoredPosition != null)
            {
                FactionBadgeIconsContainer.anchoredPosition = OriginalFactionBadgeAnchoredPosition.Value;
            }

            FactionBadgeIconsContainer = null;
            OriginalFactionBadgeAnchoredPosition = null;
        }

        private static RectTransform FindFactionBadgeIconsContainer(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            Transform iconsContainer = playerModelWindow?.transform.Find("CharacterPanel/PlayerModelView/IconsContainer")
                ?? screen?.transform.Find("PlayerModelWithStats/CharacterPanel/PlayerModelView/IconsContainer")
                ?? FindChildRecursive(playerModelWindow?.transform, "IconsContainer")
                ?? FindChildRecursive(screen?.transform, "IconsContainer");

            return iconsContainer as RectTransform;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
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

        public static void PlayerModelWithStatsWindow_OnCustomizationChanged(dropDownItem suit)
        {
            if (ViewedProfile == null)
            {
                return;
            }

            try
            {
                string body = ViewedProfile.Customization[EBodyModelPart.Body];
                string feet = ViewedProfile.Customization[EBodyModelPart.Feet];
                string responseJson = RequestHandler.PostJson(SuitRoute, SerializeBody(new FriendlyTeammateSuitRequest
                {
                    aid = ViewedProfile.AccountId,
                    suit = new string[] { body, feet }
                }));
                EnsureBodySuccess(responseJson);
                MarkSquadRosterDirty(ViewedProfile?.AccountId);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate suit change.");
                Modules.Logger.LogError(ex);
            }
        }

    }

    internal class OtherPlayerProfileScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(OtherPlayerProfileScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance)
        {
            Action callback = OtherPlayerProfileScreenPatch.ActiveBackOverrideAction;
            OtherPlayerProfileScreenPatch.ActiveBackOverrideAction = null;
            OtherPlayerProfileScreenPatch.PendingBackOverrideAction = null;
            bool transitioningToBuildsScreen = TeammateEquipmentBuildsScreenFlow.ConsumeProfileTransition(callback);
            if (!transitioningToBuildsScreen)
            {
                TeammateEquipmentBuildsScreenFlow.ClearIfNotOpeningOrReturning();
                OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
            }

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow")?.GetValue(__instance)
                as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow != null)
            {
                playerModelWindow.OnCustomizationChanged -= OtherPlayerProfileScreenPatch.PlayerModelWithStatsWindow_OnCustomizationChanged;
            }

            OtherPlayerProfileScreenPatch.CloseLoadoutEditorOverlay();
            OtherPlayerProfileScreenPatch.ViewedProfile = null;
            OtherPlayerProfileScreenPatch.ActiveProfileScreen = null;
            OtherPlayerProfileScreenPatch.ActiveProfileInventoryController = null;
            OtherPlayerProfileScreenPatch.ActiveProfileSession = null;
            OtherPlayerProfileScreenPatch.ActiveProfilePlayerModelWindow = null;
            OtherPlayerProfileScreenPatch.ActiveTeammateLoadoutId = null;
            OtherPlayerProfileScreenPatch.ActiveTeammateLoadoutName = null;
            OtherPlayerProfileScreenPatch.CustomDropdownIds.Clear();
            OtherPlayerProfileScreenPatch.StopPendingAggressionPersist();
            OtherPlayerProfileScreenPatch.CloseRenameOverlay();
            OtherPlayerProfileScreenPatch.RestoreFactionBadgePosition();

            if (OtherPlayerProfileScreenPatch.OriginalNicknameLabel != null)
            {
                if (OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition != null
                    && OtherPlayerProfileScreenPatch.OriginalNicknameLabel.transform is RectTransform labelRect)
                {
                    labelRect.anchoredPosition = OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition.Value;
                }

                OtherPlayerProfileScreenPatch.OriginalNicknameLabel = null;
                OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition = null;
            }

            if (OtherPlayerProfileScreenPatch.NicknameRenameButton != null)
            {
                if (OtherPlayerProfileScreenPatch.NicknameRenameButton.name == "pitFireTeam_NicknameRenameButton")
                {
                    if (OtherPlayerProfileScreenPatch.NicknameRenameButtonRoot != null)
                    {
                        GameObject.Destroy(OtherPlayerProfileScreenPatch.NicknameRenameButtonRoot.gameObject);
                    }
                    else
                    {
                        GameObject.Destroy(OtherPlayerProfileScreenPatch.NicknameRenameButton.gameObject);
                    }
                }

                OtherPlayerProfileScreenPatch.NicknameRenameButtonRoot = null;
                OtherPlayerProfileScreenPatch.NicknameRenameButton = null;
            }

            if (OtherPlayerProfileScreenPatch.EditLoadoutButton != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.EditLoadoutButton.gameObject);
                OtherPlayerProfileScreenPatch.EditLoadoutButton = null;
            }

            OtherPlayerProfileScreenPatch.RestoreHideoutButtonVisuals(__instance);
            OtherPlayerProfileScreenPatch.RestoreProfileRightSideContent(__instance);

            if (OtherPlayerProfileScreenPatch.LoadoutSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.LoadoutSelector.gameObject);
                OtherPlayerProfileScreenPatch.LoadoutSelector = null;
            }

            if (OtherPlayerProfileScreenPatch.AggressionSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.AggressionSelector.gameObject);
                OtherPlayerProfileScreenPatch.AggressionSelector = null;
            }

            if (OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot.gameObject);
                OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot = null;
            }

            if (OtherPlayerProfileScreenPatch.SkillsPanel != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.SkillsPanel.gameObject);
                OtherPlayerProfileScreenPatch.SkillsPanel = null;
            }

            if (OtherPlayerProfileScreenPatch.SkillsPanelHost != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.SkillsPanelHost.gameObject);
                OtherPlayerProfileScreenPatch.SkillsPanelHost = null;
            }

            if (transitioningToBuildsScreen || callback == null)
            {
                return;
            }

            if (pitFireTeam.Instance == null)
            {
                callback();
                return;
            }

            pitFireTeam.Instance.StartCoroutine(InvokeBackOverrideNextFrame(callback));
        }

        private static System.Collections.IEnumerator InvokeBackOverrideNextFrame(Action callback)
        {
            yield return null;
            callback?.Invoke();
        }
    }
}
