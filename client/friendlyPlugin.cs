using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using Newtonsoft.Json;

using pitTeam.Modules;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Localization;
using pitTeam.Utils;
using pitTeam.Patches;

namespace pitTeam
{

    public enum CustomBotRequestType
    {
        TakeLoot = 20,
        Regroup = 30,
        OverThere = 40,
        NeedHelp = 50
    }

    public enum CustomBotDecisions
    {
        SniperSearch = 100,
        CoverToCover = 101,
        EnemySearch = 102,
        MoveToPoint = 103,
        RunToCover = 104,
        GuardToCover = 105,
        FollowBoss = 106,
        attackRetreat = 107,

        DogFight = 108,
    }

    public enum CustomPhrases
    {
        TeamStatus = 10001,
        ViewBackpack = 10002,
    }

    public enum CustomGestures
    {
        OverThere = 220,
    }

    public enum LoadoutManagementMode
    {
        Simple,
        Restricted,
        Immersive,
        Extreme
    }

    public class LanguageOptions
    {
        public string baseSettings { get; set; }
        public string followSettings { get; set; }
        public string combatSettings { get; set; }
        public string inputSettings { get; set; }
        public string miscSettings { get; set; }
        public string testSettings { get; set; }
        public string raidSettings { get; set; }
        public string loadoutManagementSettings { get; set; }
        public string[] equipOptions { get; set; }
        public string[] tacticOptions { get; set; }

        public Dictionary<string, string> statusSound { get; set; }
        public Dictionary<string, string> enemyMarker { get; set; }
        public Dictionary<string, string> scanDistance { get; set; }
        public Dictionary<string, string> enemyRemember { get; set; }
        public Dictionary<string, string> healthMultiplier { get; set; }
        public Dictionary<string, string> pickup { get; set; }
        public Dictionary<string, string> tieredPickup { get; set; }
        public Dictionary<string, string> maximumPickup { get; set; }
        public Dictionary<string, string> recruitPickup { get; set; }
        public Dictionary<string, string> teamEscape { get; set; }
        public Dictionary<string, string> teamEscapeUseAnyExtract { get; set; }

        public Dictionary<string, string> memberTactic { get; set; }
        public Dictionary<string, string> memberEquipment { get; set; }
        public Dictionary<string, string> memberName { get; set; }
        public Dictionary<string, string> memberVoice { get; set; }
        public Dictionary<string, string> memberUniformTop { get; set; }
        public Dictionary<string, string> memberUniformBottom { get; set; }

        public Dictionary<string, string> equipmentLock { get; set; }
        public Dictionary<string, string> loadoutManagement { get; set; }
        public Dictionary<string, string> loadoutManagementSimple { get; set; }
        public Dictionary<string, string> loadoutManagementRestricted { get; set; }
        public Dictionary<string, string> loadoutManagementRestrictedGearMaintenance { get; set; }
        public Dictionary<string, string> loadoutManagementImmersive { get; set; }
        public Dictionary<string, string> loadoutManagementExtreme { get; set; }
        public Dictionary<string, string> npcSendMessage { get; set; }

        [JsonProperty("friendlyPMC")]
        public Dictionary<string, string> pitFireTeam { get; set; }
        public Dictionary<string, string> badGuy { get; set; }
        public Dictionary<string, string> pmcArmbands { get; set; }
        public Dictionary<string, string> englishBear { get; set; }

        public Dictionary<string, string> pingSquad { get; set; }
        public Dictionary<string, string> pingRadioVolume { get; set; }
        public Dictionary<string, string> pingTime { get; set; }
        public Dictionary<string, string> enemyContact { get; set; }
        public Dictionary<string, string> overThere { get; set; }
        public Dictionary<string, string> hideUnsupportedCommands { get; set; }
        public Dictionary<string, string> gestures { get; set; }

        public Dictionary<string, string> botStatus { get; set; }
        public Dictionary<string, string> socialUi { get; set; }

        public Dictionary<string, string> patrolRadius { get; set; }
        public Dictionary<string, string> followDistance { get; set; }
        public Dictionary<string, string> regroupRadius { get; set; }

        public Dictionary<string, string> goToDistance { get; set; }

        public Dictionary<string, string> botTeleport { get; set; }
        public Dictionary<string, string> botHeal { get; set; }

        public Dictionary<string, string> botPrefetch { get; set; }

        public Dictionary<string, string> botGrenades { get; set; }

        public Dictionary<string, string> botTalk { get; set; }

        public Dictionary<string, string> spawnPoint { get; set; }
        public Dictionary<string, string> battleRecorder { get; set; }
        public Dictionary<string, string> battleRecorderSnapshotIntervalMs { get; set; }

        // used only by BE
        public string[] returnItems { get; set; }
        public string[] returnItemsDeath { get; set; }
        public string[] teamEscaped { get; set; }
        public string[] teamSomeEscaped { get; set; }
        public string[] friendlyEscaped { get; set; }
        public string[] deathEscapeMessages { get; set; }
        public Dictionary<string, string> deathEscape { get; set; }
        public string[] traitorKillMessages { get; set; }
        public string[] jerkKillMessages { get; set; }
    }

    [BepInPlugin("xyz.pit.fireteam", "PitAlex-PitFireTeam", "0.8.6")]
    [BepInDependency("xyz.drakia.bigbrain")]
    public class pitFireTeam : BaseUnityPlugin
    {
        public const string SainPluginId = "me.sol.sain";
        public const string SainAddonPluginId = "xyz.pit.fireteam.sainaddon";

        public static bool awaken;

        internal static pitFireTeam Instance { get; private set; }

        public static LanguageOptions optionsLang;

        public static ConfigEntry<int> enemyRemember;

        public static ConfigEntry<int> heatlhMultiplier;

        public static ConfigEntry<int> scanDistance;

        public static ConfigEntry<int> statusSound;
        public static ConfigEntry<bool> enemyMarker;
        public static ConfigEntry<bool> npcSendMessage;
        public static ConfigEntry<bool> pickupEnabled;
        public static ConfigEntry<bool> tieredPickup;
        public static ConfigEntry<int> maximumPickup;
        public static ConfigEntry<bool> recruitPickup;
        public static ConfigEntry<bool> teamEscape;
        public static ConfigEntry<bool> teamEscapeUseAnyExtract;

        public static ConfigEntry<bool> pitFireTeamFLAG;
        public static ConfigEntry<bool> badGuy;

        public static ConfigEntry<bool> englishBear;
        public static ConfigEntry<bool> pmcArmbands;
        public static ConfigEntry<LoadoutManagementMode> loadoutManagementMode;
        public static ConfigEntry<bool> restrictedGearMaintenance;

        public static ConfigEntry<bool> botPrefetch;

        public static ConfigEntry<bool> botGrenades;

        public static ConfigEntry<int> botTalk;

        public static ConfigEntry<int> patrolRadius;
        public static ConfigEntry<int> followDistance;
        public static ConfigEntry<int> regroupRadius;

        public static ConfigEntry<int> goToDistance;

        public static ConfigEntry<bool> spawnPoint;

        public static ConfigEntry<bool> hideUnsupportedCommands;

        public static ConfigEntry<bool> battleRecorderEnabled;
        public static ConfigEntry<int> battleRecorderSnapshotIntervalMs;
        public static bool IsDebugBuild
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        public static ConfigEntry<KeyboardShortcut> pingKey;
        public static ConfigEntry<int> pingRadioVolume;
        public static ConfigEntry<int> pingTime;

        public static ConfigEntry<KeyboardShortcut> contactKey;
        public static ConfigEntry<KeyboardShortcut> overThereKey;
        public static ConfigEntry<KeyboardShortcut> teleportKey;
        public static ConfigEntry<KeyboardShortcut> healKey;

        public static TarkovApplication application;

        private static Dictionary<ConfigDefinition, string> savedConfigValues;
        private string currentLanguageCode = "en";
        private float nextLanguageCheckTime;

        public static ManualLogSource Log => Instance.Logger;

        public static bool IsSAINInstalled { get; private set; }
        public static bool IsSAINAddonInstalled { get; private set; }

        public static bool UseSainFollowerCombat => IsSAINInstalled && IsSAINAddonInstalled;
        public static bool HasSainRegroupAddon => UseSainFollowerCombat;
        public static bool ShouldDisableSainForFollowers => IsSAINInstalled && !UseSainFollowerCombat;

        private void Awake()
        {

            if (!awaken)
            {
                awaken = true;
                Instance = this;
                new Modules.Logger();
                RefreshPluginFlags();
            }

            // initialize follower brain layers
            FollowerLayerRegistry.Init();

            var harmony = new Harmony("xyz.pit.fireteam");

            // bot patches to help with various scenarios while being a follower of the player
            // Temporarily disabled for 4.x stability; revisit once BotsGroup method signatures are remapped.
            new BotGroupAddEnemyPatch().Enable();
            new BotGroupReportEnemyPatch().Enable();
            new BotGroupUsecEnemyPatch().Enable();
            new BotGroupCalcGoalPatch().Enable();
            new BotControllerEnemyPropagationSafetyPatch().Enable();
            new PmcFriendlyFireRetaliationBridgePatch().Enable();

            new BotMemoryDamagePatch().Enable();
            new FollowerGoalEnemyClearRetentionPatch().Enable();
            new ExUsecBrainHitPatch().Enable();

            new BotOwnerIsFolowerPatch().Enable();
            new BotOwnerManualUpdatePatch().Enable();
            new BotOwnerActivatePatch().Enable();
            new SessionLoadBotsEnglishVoicePatch().Enable();
            new LootPatrolActiveLayerListPatch().Enable();
            new LootPatrolDecisionBypassPatch().Enable();
            if (IsSAINInstalled)
                new HostileNonCombatActiveLayerFilterPatch().Enable();
            new AdvAssaultTargetFollowerGuardPatch().Enable();
            new PatrolDataFollowerUpdateGuardPatch().Enable();
            new AvoidDangerFollowerGuardPatch().Enable();
            new FollowerNightVisionActivatePatch().Enable();
            new FollowerNightVisionOnPatch().Enable();
            new FollowerNightVisionOffPatch().Enable();
            new PmcBearCombatLayerSuppressionPatch().Enable();
            new PmcUsecCombatLayerSuppressionPatch().Enable();
            new PmcFlankCombatLayerSuppressionPatch().Enable();

            if (!IsSAINInstalled)
                new FollowerSprintPatch().Enable();
            new FollowerSprintStateDirectionPatch().Enable();

            new AICoreAgentUpdatePatch().Enable();

            // recruit/request patches
            new BotReceiverFollowMeRecruitPatch().Enable();
            new FollowRequestPatch().Enable();
            new HoldRequestPatch().Enable();
            new OpenDoorRequestPatch().Enable();
            new FollowerDoorAutoClosePatch().Enable();
            new FollowerBotRequestTakePatch().Enable();
            new TeammateBackpackInspectionUpdatePatch().Enable();
            new TeammateBackpackChangedContainerPatch().Enable();
            new TeammateBackpackObserverStatePatch().Enable();
            new TeammateBackpackExaminedPatch().Enable();
            new TeammateBackpackKnownItemPatch().Enable();
            new TeammateBackpackSearchedItemPatch().Enable();
            new TeammateBackpackUnknownContentsPatch().Enable();
            new TeammateBackpackSimpleStashLabelPatch().Enable();
            new TeammateCorpseContainersPanelSearchPatch().Enable();
            new TeammateCorpseEquipmentTabSearchPatch().Enable();
            new TeammateCorpseDogtagMovePatch().Enable();
            new TeammateCorpseDogtagRemovePatch().Enable();
            new FollowerBotReceiverHardAimIgnorePatch().Enable();
            new FollowerBotReceiverTiltIgnorePatch().Enable();
            new FollowerBotReceiverPhraseIgnorePatch().Enable();
            new FollowerBotReceiverGestureIgnorePatch().Enable();
            new BotReceiverPhraseOverridePatch().Enable();


            // spawn patches
            new BotsControllerPatch().Enable();
            new BotsControllerStopPatch().Enable();
            new LocalGameCleanupPatch().Enable();

            // Only patch LocalGame ctor here; avoid broad PatchAll side effects at menu/hideout time.
            harmony.CreateClassProcessor(typeof(LocalGameCtorPatch)).Patch();
            new BotsEventsControllerSpawnPatch().Enable();
            new BossSpawnWaveManagerClassPatch().Enable();

            // patch bot equipment to prevent looting companions
            new UnlootableComponentPatch().Enable();
            new ModRaidModdablePatch().Enable();
            new ItemSpecificationPanelPatch().Enable();

            // bot misc patches
            new BotTalkTrySayPatch().Enable();
            new BotTalkSayPatch().Enable();
            new FollowerWeaponTakenAfterDeathPatch().Enable();
            new FollowerWeaponSelectorManualUpdatePatch().Enable();
            new FollowerSupportNoAmmoMainSwitchPolicyPatch().Enable();
            new FollowerHoldLingerReloadSuppressPatch().Enable();
            new FollowerShootFromPlaceCrouchPatch().Enable();
            new FollowerGrenadeAvailabilityPatch().Enable();
            new FollowerGrenadeCooldownPatch().Enable();
            new FollowerGrenadeThrowFinishPatch().Enable();
            new FollowerGrenadeReleasePatch().Enable();
            new FollowerEnemyInfoCorrectionPatch().Enable();
            new FollowerEnemyInfoSetVisibleCorrectionPatch().Enable();
            new BulletImpactPatch().Enable();
            new HearingSensorPatch().Enable();
            new FootstepSoundPatch().Enable();
            new PlayerSayPatch().Enable();
            new PlayerVoicePhraseAvailabilityInitPatch().Enable();
            new PlayerVoicePhraseAvailabilityReplacePatch().Enable();
            new PlayerKilledPatch().Enable();
            new PlayerDeadFallbackPatch().Enable();
            new PlayerShotPatch().Enable();
            new AddTeammateBackButtonPatch().Enable();
            new AddTeammateSideSelectionStateClosePatch().Enable();
            new AddTeammateNicknameFieldEndEditPatch().Enable();
            new AddTeammateNicknameFieldInitPatch().Enable();
            new AddTeammateNicknameFieldStatusPatch().Enable();
            new AddTeammateNicknameValueChangedPatch().Enable();
            new AddTeammateHeadSelectionOptionsPatch().Enable();
            new AddTeammateFinishPatch().Enable();

            // AIBossPlayer class patch
            new AIDataContructPatch().Enable();

            // command/request patches
            new QuickPanelPatch().Enable();
            new QuickPanelUpdateBackpackInteractionPatch().Enable();
            new GestureMenuPatch().Enable();
            new CreatePhraseGroupPatch().Enable();
            new CreateGesturesPatch().Enable();
            new CustomPlayerGestureIntPatch().Enable();
            new CustomPlayerGestureInteractionPatch().Enable();
            new GestureCommandNamePatch().Enable();
            new GestureMenuAvailablePhrasesPatch().Enable();
            new ViewBackpackQuickPanelTextPatch().Enable();
            new ViewBackpackQuickPanelItemTextPatch().Enable();
            new EPhraseTriggerPatch().Enable();
            new PlayPhraseOrGesturePatch().Enable();
            new QuickMumbleStartViewBackpackPatch().Enable();
            new BotReceiverGestureOverridePatch().Enable();
            new RaidStartPatch().Enable();
            new MainMenuControllerPatch().Enable();
            new MainMenuControllerReadyScreenGatePatch().Enable();
            new TarkovApplicationLocalRaidGatePatch().Enable();
            new TarkovApplicationOnlineFallbackPatch().Enable();
            // Compatibility guard: hideout/trader-scene cleanup can null-ref while
            // the teammate flow forces the raid into local mode. Keep this narrow
            // so only that synthetic raid-start unload window is affected.
            new HideoutGameDisposeSyntheticRaidGuardPatch().Enable();
            new MatchmakerPlayerControllerClassAddMemberPatch().Enable();
            new MatchmakerPlayerControllerClassDisbandGroupPatch().Enable();
            new MatchmakerPlayerControllerClassAbortPatch().Enable();
            new MatchmakerPlayerControllerClassLeavePatch().Enable();
            new MatchMakerAcceptScreenPatch().Enable();
            new MatchMakerPlayerPreviewFollowerUiPatch().Enable();
            new ContextInteractionsPlayerRemovePatch().Enable();
            new TransitPointPatch().Enable();
            new MatchMakerSelectionLocationScreenPatch().Enable();
            new SelectSpawnPointPatch().Enable();

            new MatchmakerTimeHasComeShowPatch().Enable();
            new PartyInfoPanelEquipmentHealthPatch().Enable();
            new MenuScreenShowSquadControlPatch().Enable();
            new MenuScreenReconnectVisibilitySquadControlPatch().Enable();
            new MenuScreenMinimizedVisibilitySquadControlPatch().Enable();
            new MatchMakerSideSelectionScreenShowPatch().Enable();
            new MatchMakerSideSelectionScreenTranslateCommandPatch().Enable();
            new MatchMakerSideSelectionScreenClosePatch().Enable();
            new MainMenuControllerOpenSideSelectionGuardPatch().Enable();
            new CurrentScreenTryReturnToRootScreenPatch().Enable();
            new PlayerModelViewShowProfilePatch().Enable();
            new MatchMakerGroupPreviewClosePlayerPatch().Enable();
            new PlayerModelViewShowLastStatePatch().Enable();

            //new ConditionCounterPatch().Enable();

            // social / teammate management patches
            new SocialNetworkClassPatch().Enable();
            new FriendListInvitePlayerPanelPatch().Enable();
            new TeammateContextMenuButtonsPatch().Enable();
            new TeammateGroupContextMenuButtonsPatch().Enable();
            new ChatInvitePlayersPanelRefreshPatch().Enable();
            new ChatCreateDialoguePanelRefreshPatch().Enable();
            new ChatFriendsRequestsPanelRefreshPatch().Enable();
            new FriendRequestProfileViewPatch().Enable();
            new FriendRequestAcceptRefreshPatch().Enable();
            new FriendRequestAcceptAllRefreshPatch().Enable();
            // new SocialNetworkClassSendPatch().Enable();
            // new QuestClassPatch().Enable();

            // set configuration manager
            SetConfiguration();
            BattleRecorder.Initialize();

            new FriendlyDropdownNamePatch().Enable();
            new OtherPlayerProfileScreenPatch().Enable();
            new OtherPlayerProfileScreenClosePatch().Enable();
            new LoadoutEditorUnloadAmmoPatch().Enable();
            new LoadoutEditorRepairContextInteractionPatch().Enable();
            new LoadoutEditorRepairByKitPatch().Enable();
            new LoadoutEditorRepairByTraderPatch().Enable();
            new LoadoutEditorLockContextInteractionPatch().Enable();
            new LoadoutEditorLockExecuteInteractionPatch().Enable();
            new LoadoutEditorLockedContainerOpenPatch().Enable();
            new LoadoutEditorLockedItemMovePatch().Enable();
            new LoadoutEditorLockedDestinationPatch().Enable();
            new TeammateEquipmentBuildsScreenShowPatch().Enable();
            new TeammateEquipmentBuildsScreenVisualPatch().Enable();
            new TeammateEquipmentBuildsScreenSelectionPatch().Enable();
            new TeammateEquipmentBuildsMissingItemsPatch().Enable();
            new TeammateEquipmentBuildsScreenBackButtonPatch().Enable();
            new TeammateEquipmentBuildsScreenAltBackButtonPatch().Enable();
            new TeammateEquipmentBuildsScreenEscapePatch().Enable();
            new TeammateEquipmentBuildsScreenClosePatch().Enable();
            new TeammateEquipmentBuildsScreenBuyButtonPatch().Enable();
            new TeammateEquipmentBuildsSearchContextPatch().Enable();
            new TeammateEquipmentBuildsInspectButtonsPatch().Enable();
            new TeammateEquipmentBuildsListViewPatch().Enable();

            // SAIN/Donuts patches
            if (IsSAINInstalled)
            {
                SAINPatch.PatchSAINIfInstalled(harmony);
            }
        }

        private void Start()
        {
            RefreshPluginFlags();
        }

        private void OnDestroy()
        {
            if (hideUnsupportedCommands != null)
            {
                hideUnsupportedCommands.SettingChanged -= OnHideUnsupportedCommandsChanged;
            }

            GestureMenuUnsupportedCommandVisibility.ClearTrackedMenus();
            BattleRecorder.Shutdown();
        }

        private static void RefreshPluginFlags()
        {
            IsSAINInstalled = HasPlugin(SainPluginId);
            IsSAINAddonInstalled = HasPlugin(SainAddonPluginId);
        }

        public static bool ShouldUseSainRegroupRoute(bool isCombatRegroupContext)
        {
            return HasSainRegroupAddon && isCombatRegroupContext;
        }

        public static bool ShouldSainRegroupLayerHandle(BotOwner? botOwner)
        {
            return HasSainRegroupAddon && botOwner?.Memory?.HaveEnemy == true;
        }

        private static bool HasPlugin(string pluginId)
        {
            var pluginInfos = BepInEx.Bootstrap.Chainloader.PluginInfos;
            if (pluginInfos.ContainsKey(pluginId))
            {
                return true;
            }

            return pluginInfos.Values.Any(p =>
                p?.Metadata != null &&
                string.Equals(p.Metadata.GUID, pluginId, StringComparison.OrdinalIgnoreCase));
        }


        private void GetLanguage()
        {
            currentLanguageCode = ResolveGameLanguageCode();
            optionsLang = LoadLanguageOptions(currentLanguageCode);
        }

        internal static string GetSocialUiText(string key)
        {
            return GetDictionaryText(optionsLang?.socialUi, EmbeddedEnglishLanguageProvider.Fallback.socialUi, key);
        }

        internal static string GetGestureText(string key)
        {
            return GetDictionaryText(optionsLang?.gestures, EmbeddedEnglishLanguageProvider.Fallback.gestures, key);
        }

        internal static string GetBotStatusText(string key)
        {
            return GetDictionaryText(optionsLang?.botStatus, EmbeddedEnglishLanguageProvider.Fallback.botStatus, key);
        }

        internal static string GetTacticOptionText(int index)
        {
            string localized = GetArrayText(optionsLang?.tacticOptions, index);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return GetArrayText(EmbeddedEnglishLanguageProvider.Fallback.tacticOptions, index) ?? string.Empty;
        }

        internal static string GetLanguageText(Func<LanguageOptions, string> selector)
        {
            string localized = GetLanguageText(optionsLang, selector);
            if (!string.IsNullOrWhiteSpace(localized))
            {
                return localized;
            }

            return GetLanguageText(EmbeddedEnglishLanguageProvider.Fallback, selector) ?? string.Empty;
        }

        private static string GetDictionaryText(Dictionary<string, string> localized, Dictionary<string, string> fallback, string key)
        {
            if (TryGetDictionaryText(localized, key, out string value)
                || TryGetDictionaryText(fallback, key, out value))
            {
                return value;
            }

            return key ?? string.Empty;
        }

        private static bool TryGetDictionaryText(Dictionary<string, string> source, string key, out string value)
        {
            value = null;
            if (source == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return source.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
        }

        private static string GetArrayText(string[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length && !string.IsNullOrWhiteSpace(values[index])
                ? values[index]
                : null;
        }

        private static string GetLanguageText(LanguageOptions language, Func<LanguageOptions, string> selector)
        {
            if (language == null || selector == null)
            {
                return null;
            }

            try
            {
                return selector(language);
            }
            catch
            {
                return null;
            }
        }

        private static LanguageOptions LoadLanguageOptions(string languageCode)
        {
            LanguageOptions fallback = EmbeddedEnglishLanguageProvider.Create();

            try
            {
                string embeddedEnglishJson = JsonConvert.SerializeObject(fallback);
                string requestBody = JsonConvert.SerializeObject(new { locale = languageCode, englishJson = embeddedEnglishJson });
                string responseJson = RequestHandler.PostJson("/singleplayer/pitfireteam/lang", requestBody);
                LanguageOptions loaded = JsonConvert.DeserializeObject<LanguageOptions>(responseJson);
                return ApplyLanguageFallback(loaded, fallback);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"Failed to load pitFireTeam language '{languageCode}', using English fallback: {ex.Message}");
                return fallback;
            }
        }

        private static LanguageOptions ApplyLanguageFallback(LanguageOptions loaded, LanguageOptions fallback)
        {
            loaded ??= new LanguageOptions();

            foreach (PropertyInfo property in typeof(LanguageOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                object current = property.GetValue(loaded);
                object defaultValue = property.GetValue(fallback);

                if (current == null)
                {
                    property.SetValue(loaded, defaultValue);
                    continue;
                }

                if (current is System.Collections.IDictionary currentDictionary
                    && defaultValue is System.Collections.IDictionary defaultDictionary)
                {
                    foreach (object key in defaultDictionary.Keys)
                    {
                        if (!currentDictionary.Contains(key))
                        {
                            currentDictionary[key] = defaultDictionary[key];
                        }
                    }
                    continue;
                }

                if (current is Array currentArray && currentArray.Length == 0)
                {
                    property.SetValue(loaded, defaultValue);
                    continue;
                }

                if (current is string currentString && string.IsNullOrWhiteSpace(currentString))
                {
                    property.SetValue(loaded, defaultValue);
                }
            }

            return loaded;
        }

        private static string ResolveGameLanguageCode()
        {
            try
            {
                if (Singleton<SharedGameSettingsClass>.Instantiated)
                {
                    string gameLanguage = Singleton<SharedGameSettingsClass>.Instance?.Game?.Settings?.Language?.Value;
                    if (!string.IsNullOrWhiteSpace(gameLanguage))
                    {
                        return NormalizeLanguageCode(gameLanguage);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"Failed to read game language setting: {ex.Message}");
            }

            return "en";
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return "en";
            }

            string normalized = languageCode.Trim().ToLowerInvariant();
            int separatorIndex = normalized.IndexOfAny(new[] { '-', '_' });
            if (separatorIndex > 0)
            {
                normalized = normalized.Substring(0, separatorIndex);
            }

            return normalized switch
            {
                "de" => "ge",
                _ => normalized
            };
        }

        private void CheckLanguageSettingChanged()
        {
            if (Time.realtimeSinceStartup < nextLanguageCheckTime)
            {
                return;
            }

            nextLanguageCheckTime = Time.realtimeSinceStartup + 1f;
            string observedLanguage = ResolveGameLanguageCode();
            if (string.Equals(observedLanguage, currentLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentLanguageCode = observedLanguage;
            optionsLang = LoadLanguageOptions(currentLanguageCode);
            SquadControlMenuUi.FindInstance()?.RefreshLocalizedText();
        }
        private void ConfigSet()
        {
            Config.SaveOnConfigSet = false;

            savedConfigValues = new Dictionary<ConfigDefinition, string>();

            var orphanedEntries = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries").GetValue(Config) as Dictionary<ConfigDefinition, string>;
            orphanedEntries.ExecuteForEach(it =>
            {
                savedConfigValues.Add(it.Key, it.Value);
            });

            scanDistance = Config.Bind("", "01 ScanDistance", 140, new ConfigDescription(optionsLang.scanDistance["Description"], new AcceptableValueRange<int>(50, 300), CreateConfigAttributes(-100, false, optionsLang.scanDistance)));

            patrolRadius = Config.Bind("", "02 PatrolRadius", 50, new ConfigDescription(optionsLang.patrolRadius["Description"], new AcceptableValueRange<int>(30, 100), CreateConfigAttributes(-200, false, optionsLang.patrolRadius)));

            followDistance = Config.Bind("", "02 FollowDistance", 12, new ConfigDescription(optionsLang.followDistance["Description"], new AcceptableValueRange<int>(8, 30), CreateConfigAttributes(-201, false, optionsLang.followDistance)));

            enemyRemember = Config.Bind("", "03 EnemyRemember", 20, new ConfigDescription(optionsLang.enemyRemember["Description"], new AcceptableValueRange<int>(5, 60), CreateConfigAttributes(-300, false, optionsLang.enemyRemember)));

            heatlhMultiplier = Config.Bind("", "04 HealthMultiplier", 1, new ConfigDescription(optionsLang.healthMultiplier["Description"], new AcceptableValueRange<int>(1, 10), CreateConfigAttributes(-400, false, optionsLang.healthMultiplier)));

            statusSound = Config.Bind("", "05 StatusSound", 100, new ConfigDescription(optionsLang.statusSound["Description"], new AcceptableValueRange<int>(0, 100), CreateConfigAttributes(-500, false, optionsLang.statusSound)));

            enemyMarker = Config.Bind("", "06 EnemyMarker", true, new ConfigDescription(optionsLang.enemyMarker["Description"], null, CreateConfigAttributes(-600, false, optionsLang.enemyMarker)));

            pickupEnabled = Config.Bind("", "07 Pickup", true, new ConfigDescription(optionsLang.pickup["Description"], null, CreateConfigAttributes(-700, false, optionsLang.pickup)));

            tieredPickup = Config.Bind("", "08 TieredPickup", true, new ConfigDescription(optionsLang.tieredPickup["Description"], null, CreateConfigAttributes(-800, false, optionsLang.tieredPickup)));

            maximumPickup = Config.Bind("", "09 MaximumPickup", 2, new ConfigDescription(optionsLang.maximumPickup["Description"], new AcceptableValueRange<int>(0, 10), CreateConfigAttributes(-900, false, optionsLang.maximumPickup)));

            recruitPickup = Config.Bind("", "10 RecruitPickup", true, new ConfigDescription(optionsLang.recruitPickup["Description"], null, CreateConfigAttributes(-1000, false, optionsLang.recruitPickup)));

            teamEscape = Config.Bind("", "10 TeamEscape", true, new ConfigDescription(optionsLang.teamEscape["Description"], null, CreateConfigAttributes(-1001, false, optionsLang.teamEscape)));

            teamEscapeUseAnyExtract = Config.Bind("", "10 TeamEscapeUseAnyExtract", true, new ConfigDescription(optionsLang.teamEscapeUseAnyExtract["Description"], null, CreateConfigAttributes(-1002, false, optionsLang.teamEscapeUseAnyExtract)));

            npcSendMessage = Config.Bind("", "11 NpcSendMessage", true, new ConfigDescription(optionsLang.npcSendMessage["Description"], null, CreateConfigAttributes(-1003, false, optionsLang.npcSendMessage)));

            pitFireTeamFLAG = Config.Bind("", "12 PitFireTeam", true, new ConfigDescription(optionsLang.pitFireTeam["Description"], null, CreateConfigAttributes(-1004, false, optionsLang.pitFireTeam)));

            badGuy = Config.Bind("", "13 BadGuy", false, new ConfigDescription(optionsLang.badGuy["Description"], null, CreateConfigAttributes(-1005, false, optionsLang.badGuy)));

            englishBear = Config.Bind("", "14 EnglishBear", true, new ConfigDescription(optionsLang.englishBear["Description"], null, CreateConfigAttributes(-1006, false, optionsLang.englishBear)));

            pmcArmbands = Config.Bind("", "14 PmcArmbands", true, new ConfigDescription(optionsLang.pmcArmbands["Description"], null, CreateConfigAttributes(-1007, false, optionsLang.pmcArmbands)));
            pmcArmbands.SettingChanged += (_, _) => SyncServerSettings();

            loadoutManagementMode = Config.Bind("", "14 LoadoutManagement", LoadoutManagementMode.Simple, new ConfigDescription(optionsLang.loadoutManagement["Description"], null, CreateConfigAttributes(-1005, false, optionsLang.loadoutManagement)));

            restrictedGearMaintenance = Config.Bind("", "14 LoadoutManagementRestrictedGearMaintenance", false, new ConfigDescription(optionsLang.loadoutManagementRestrictedGearMaintenance["Description"], null, CreateConfigAttributes(-1005, false, optionsLang.loadoutManagementRestrictedGearMaintenance)));
            restrictedGearMaintenance.SettingChanged += (_, _) => SyncServerSettings();

            botGrenades = Config.Bind("", "15 BotGrenades", true, new ConfigDescription(optionsLang.botGrenades["Description"], null, CreateConfigAttributes(-1005, false, optionsLang.botGrenades)));

            regroupRadius = Config.Bind("", "15 RegroupRadius", 18, new ConfigDescription(optionsLang.regroupRadius["Description"], new AcceptableValueRange<int>(10, 38), CreateConfigAttributes(-1005, false, optionsLang.regroupRadius)));

            pingKey = Config.Bind("", "16 PingSquad", new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.pingSquad["Description"], null, CreateConfigAttributes(-1006, false, optionsLang.pingSquad)));

            pingRadioVolume = Config.Bind("", "17 PingRadioVolume", 50, new ConfigDescription(optionsLang.pingRadioVolume["Description"], new AcceptableValueRange<int>(0, 100), CreateConfigAttributes(-1007, false, optionsLang.pingRadioVolume)));

            pingTime = Config.Bind("", "18 PingTime", 5, new ConfigDescription(optionsLang.pingTime["Description"], new AcceptableValueRange<int>(5, 30), CreateConfigAttributes(-1008, false, optionsLang.pingTime)));

            contactKey = Config.Bind("", "19 EnemyContact", new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.enemyContact["Description"], null, CreateConfigAttributes(-1009, false, optionsLang.enemyContact)));

            overThereKey = Config.Bind("", "20 OverThere", new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.overThere["Description"], null, CreateConfigAttributes(-1010, false, optionsLang.overThere)));

            teleportKey = Config.Bind("", "21 BotTeleport", new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.botTeleport["Description"], null, CreateConfigAttributes(-1011, false, optionsLang.botTeleport)));
            healKey = Config.Bind("", "22 BotHeal", new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.botHeal["Description"], null, CreateConfigAttributes(-1012, false, optionsLang.botHeal)));

            botPrefetch = Config.Bind("", "23 BotPrefetch", true, new ConfigDescription(optionsLang.botPrefetch["Description"], null, CreateConfigAttributes(-1013, false, optionsLang.botPrefetch)));

            botTalk = Config.Bind("", "24 BotTalk", 100, new ConfigDescription(optionsLang.botTalk["Description"], new AcceptableValueRange<int>(0, 100), CreateConfigAttributes(-1014, false, optionsLang.botTalk)));

            spawnPoint = Config.Bind("", "25 SpawnPoint", true, new ConfigDescription(optionsLang.spawnPoint["Description"], null, CreateConfigAttributes(-1015, false, optionsLang.spawnPoint)));

            goToDistance = Config.Bind("", "26 GoToDistance", 50, new ConfigDescription(optionsLang.goToDistance["Description"], new AcceptableValueRange<int>(10, 150), CreateConfigAttributes(-1016, false, optionsLang.goToDistance)));

            hideUnsupportedCommands = Config.Bind("", "HideUnSupportedCommands", false, new ConfigDescription(optionsLang.hideUnsupportedCommands["Description"], null, CreateConfigAttributes(-1017, false, optionsLang.hideUnsupportedCommands)));
            hideUnsupportedCommands.SettingChanged += OnHideUnsupportedCommandsChanged;

            bool showBattleRecorderSettings = IsDebugBuild;
            battleRecorderEnabled = Config.Bind("Miscellaneous", "27 BattleRecorder", false, new ConfigDescription(optionsLang.battleRecorder["Description"], null, CreateConfigAttributes(-9998, showBattleRecorderSettings, optionsLang.battleRecorder)));

            battleRecorderSnapshotIntervalMs = Config.Bind("Miscellaneous", "28 BattleRecorderSnapshotIntervalMs", 200, new ConfigDescription(optionsLang.battleRecorderSnapshotIntervalMs["Description"], new AcceptableValueRange<int>(50, 1000), CreateConfigAttributes(-9999, showBattleRecorderSettings, optionsLang.battleRecorderSnapshotIntervalMs)));


            Config.SaveOnConfigSet = true;
            Config.Save();
            SyncServerSettings();
        }

        private void OnHideUnsupportedCommandsChanged(object sender, EventArgs e)
        {
            GestureMenuUnsupportedCommandVisibility.RefreshTrackedMenus();
        }

        internal static void SyncServerSettingsNow()
        {
            SyncServerSettings();
        }

        internal static Task SyncServerSettingsNowAsync()
        {
            return SyncServerSettingsAsync();
        }

        internal static bool IsFollowerLoadoutLootableMode()
        {
            LoadoutManagementMode mode = loadoutManagementMode?.Value ?? LoadoutManagementMode.Simple;
            return mode == LoadoutManagementMode.Immersive || mode == LoadoutManagementMode.Extreme;
        }

        internal static bool IsFollowerLoadoutRealTransferMode()
        {
            LoadoutManagementMode mode = loadoutManagementMode?.Value ?? LoadoutManagementMode.Simple;
            return mode == LoadoutManagementMode.Restricted
                || mode == LoadoutManagementMode.Immersive
                || mode == LoadoutManagementMode.Extreme;
        }

        internal static bool IsFollowerLoadoutRealisticMode()
        {
            return (loadoutManagementMode?.Value ?? LoadoutManagementMode.Simple) == LoadoutManagementMode.Extreme;
        }

        private static void SyncServerSettings()
        {
            _ = SyncServerSettingsAsync();
        }

        private static Task SyncServerSettingsAsync()
        {
            try
            {
                string requestBody = JsonConvert.SerializeObject(new
                {
                    pmcArmbands = pmcArmbands?.Value ?? true,
                    loadoutManagementMode = (loadoutManagementMode?.Value ?? LoadoutManagementMode.Simple).ToString(),
                    restrictedGearMaintenance = restrictedGearMaintenance?.Value ?? false
                });
                return Task.Run(() =>
                {
                    try
                    {
                        RequestHandler.PostJson("/singleplayer/pitfireteam/settings", requestBody);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogInfo($"Failed to sync pitFireTeam server settings: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"Failed to sync pitFireTeam server settings: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        private static ConfigurationManagerAttributes CreateConfigAttributes(int order, bool browsable, Dictionary<string, string> languageEntry)
        {
            string displayName = null;
            languageEntry?.TryGetValue("Name", out displayName);
            return new ConfigurationManagerAttributes
            {
                Order = order,
                Browsable = browsable,
                DispName = displayName
            };
        }
        private void _BotTeleport()
        {
            Modules.Logger.LogInfo("Followers teleport");

            if (GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            string id = GamePlayerOwner.MyPlayer.ProfileId;

            if (BossPlayers.Instance != null)
            {
                var followers = BossPlayers.GetFollowersByBoss(id);
                Vector3 position = GamePlayerOwner.MyPlayer.Transform.position;
                List<Vector3> reservedSpots = new List<Vector3> { position };
                int followerIndex = 0;
                foreach (var follower in followers)
                {
                    if (follower != null && follower.GetBot().HealthController.IsAlive && !follower.GetBot().DoorOpener.Interacting)
                    {
                        BotOwner bot = follower.GetBot();
                        Vector3 target = FindTeleportSpot(position, reservedSpots, followerIndex);
                        followerIndex++;

                        follower.BeginTeleportGrace(target);
                        bot.Mover.Stop();
                        bot.GetPlayer.Teleport(target);
                        reservedSpots.Add(target);
                    }
                }
            }
        }

        private static Vector3 FindTeleportSpot(Vector3 playerPosition, List<Vector3> reservedSpots, int index)
        {
            const float minDistanceToPlayer = 2f;
            const float minDistanceBetweenFollowers = 1.6f;
            const float sampleRadius = 1.4f;
            const float baseRadius = 2.25f;
            const float ringStep = 1.4f;

            float seedAngle = UnityEngine.Random.Range(0f, 360f);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                int ring = attempt / 8;
                float radius = baseRadius + ring * ringStep;
                float angle = seedAngle + (index * 67f) + (attempt * 45f);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 candidate = playerPosition + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * radius;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                {
                    if ((hit.position - playerPosition).sqrMagnitude < minDistanceToPlayer * minDistanceToPlayer)
                    {
                        continue;
                    }

                    bool overlaps = false;
                    foreach (Vector3 spot in reservedSpots)
                    {
                        if ((hit.position - spot).sqrMagnitude < minDistanceBetweenFollowers * minDistanceBetweenFollowers)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (!overlaps)
                    {
                        return hit.position;
                    }
                }
            }

            // Fallback near player if no valid spread point is found.
            if (NavMesh.SamplePosition(playerPosition, out NavMeshHit fallbackHit, 1.2f, NavMesh.AllAreas))
            {
                return fallbackHit.position;
            }
            return playerPosition;
        }

        private void _BotHeal()
        {
            Modules.Logger.LogInfo("Followers fix heal");

            if (GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            string id = GamePlayerOwner.MyPlayer.ProfileId;

            if (BossPlayers.Instance != null)
            {
                var followers = BossPlayers.GetFollowersByBoss(id);

                foreach (var follower in followers)
                {
                    var bot = follower.GetBot();

                    if (follower != null && bot.HealthController.IsAlive)
                    {
                        global::pitTeam.Utils.FollowerMedical.CancelAllHealing(bot, recoverDestroyedSurgeryParts: true);
                        global::pitTeam.Utils.FollowerMedical.ForceHeal(bot);
                    }
                }
            }

        }

        private void SetConfiguration()
        {
            // - get config language
            GetLanguage();
            // - set config
            ConfigSet();
            RegisterDebugConsoleCommands();
        }

        private void RegisterDebugConsoleCommands()
        {
            try
            {
                Type consoleType = AccessTools.TypeByName("EFT.UI.ConsoleScreen")
                    ?? AccessTools.TypeByName("ConsoleScreen");
                if (consoleType == null)
                {
                    Modules.Logger.LogError("ConsoleScreen type not found; debug spawn commands not registered.");
                    return;
                }

                FieldInfo processorField = AccessTools.Field(consoleType, "Processor");
                object processor = processorField?.GetValue(null);
                if (processor == null)
                {
                    PropertyInfo processorProperty = AccessTools.Property(consoleType, "Processor");
                    processor = processorProperty?.GetValue(null);
                }
                if (processor == null)
                {
                    Modules.Logger.LogError("ConsoleScreen.Processor not found; debug spawn commands not registered.");
                    return;
                }

                MethodInfo registerWithDescription = null;
                MethodInfo registerSimple = null;
                MethodInfo[] registerCandidates = processor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo method in registerCandidates)
                {
                    if (!string.Equals(method.Name, "RegisterCommand", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 3
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(Action)
                        && parameters[2].ParameterType == typeof(string))
                    {
                        registerWithDescription = method;
                    }
                    else if (parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(Action))
                    {
                        registerSimple = method;
                    }
                }

                if (registerWithDescription != null)
                {
                    RegisterDebugConsoleCommand(
                        processor,
                        registerWithDescription,
                        "fs_spawnfollower",
                        SpawnFollowerConsoleCommand,
                        "Spawn a follower bot near the player");
                    RegisterDebugConsoleCommand(
                        processor,
                        registerWithDescription,
                        "fs_spawnscav",
                        SpawnScavConsoleCommand,
                        "Spawn a scav enemy at the point you are looking at");
                    Modules.Logger.LogInfo("Registered console command: fs_spawnfollower");
                    Modules.Logger.LogInfo("Registered console command: fs_spawnscav");
                    return;
                }

                if (registerSimple != null)
                {
                    RegisterDebugConsoleCommand(
                        processor,
                        registerSimple,
                        "fs_spawnfollower",
                        SpawnFollowerConsoleCommand,
                        null);
                    RegisterDebugConsoleCommand(
                        processor,
                        registerSimple,
                        "fs_spawnscav",
                        SpawnScavConsoleCommand,
                        null);
                    Modules.Logger.LogInfo("Registered console command: fs_spawnfollower");
                    Modules.Logger.LogInfo("Registered console command: fs_spawnscav");
                    return;
                }

                Modules.Logger.LogError("RegisterCommand overload not found; debug spawn commands not registered.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to register debug spawn console commands");
                Modules.Logger.LogError(ex);
            }
        }

        private static void RegisterDebugConsoleCommand(
            object processor,
            MethodInfo registerMethod,
            string command,
            Action action,
            string description)
        {
            ParameterInfo[] parameters = registerMethod.GetParameters();
            if (parameters.Length == 3)
            {
                registerMethod.Invoke(processor, new object[] { command, action, description ?? string.Empty });
                return;
            }

            registerMethod.Invoke(processor, new object[] { command, action });
        }

        private static void SpawnFollowerConsoleCommand()
        {
            _ = SpawnFollowerConsoleCommandAsync();
        }

        private static async Task SpawnFollowerConsoleCommandAsync()
        {
            if (!Singleton<AbstractGame>.Instantiated || Singleton<GameWorld>.Instance == null || GamePlayerOwner.MyPlayer == null)
            {
                DebugConsoleLog("fs_spawnfollower: this command can only be used in-raid.", true);
                return;
            }

            Player player = GamePlayerOwner.MyPlayer;
            EPlayerSide spawnSide = player.Side;

            if (BossPlayers.Instance == null)
            {
                DebugConsoleLog("fs_spawnfollower: boss system is not initialized.", true);
                return;
            }

            pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);
            if (boss == null)
            {
                if (BotsControllerPatch.Controller == null)
                {
                    DebugConsoleLog("fs_spawnfollower: bots controller is not ready.", true);
                    return;
                }

                boss = BossPlayers.AddPlayerAsBoss(player, BotsControllerPatch.Controller);
                if (boss == null)
                {
                    DebugConsoleLog("fs_spawnfollower: failed to initialize player boss.", true);
                    return;
                }
            }

            if (BotsControllerPatch.Instance == null)
            {
                DebugConsoleLog("fs_spawnfollower: bots patch instance is missing.", true);
                return;
            }

            try
            {
                string spawnFailureReason = null;
                bool result = await BotsControllerPatch.Instance.SpawnDebugFollower(
                    boss,
                    spawnSide,
                    reason => spawnFailureReason = reason
                );
                if (result)
                {
                    DebugConsoleLog($"fs_spawnfollower: spawn requested ({spawnSide}).", false);
                }
                else
                {
                    string details = string.IsNullOrEmpty(spawnFailureReason) ? "unknown reason" : spawnFailureReason;
                    DebugConsoleLog($"fs_spawnfollower: spawn request failed ({details}).", true);
                }
            }
            catch (Exception ex)
            {
                DebugConsoleLog($"fs_spawnfollower: exception: {ex.Message}", true);
                Modules.Logger.LogError(ex);
            }
        }

        private static void SpawnScavConsoleCommand()
        {
            _ = SpawnScavConsoleCommandAsync();
        }

        private static async Task SpawnScavConsoleCommandAsync()
        {
            if (!Singleton<AbstractGame>.Instantiated || Singleton<GameWorld>.Instance == null || GamePlayerOwner.MyPlayer == null)
            {
                DebugConsoleLog("fs_spawnscav: this command can only be used in-raid.", true);
                return;
            }

            if (BotsControllerPatch.Instance == null || BotsControllerPatch.Controller == null)
            {
                DebugConsoleLog("fs_spawnscav: bots controller is not ready.", true);
                return;
            }

            Player player = GamePlayerOwner.MyPlayer;
            if (!TryGetDebugScavSpawnPoint(player, out Vector3 spawnPoint, out string failureReason))
            {
                DebugConsoleLog($"fs_spawnscav: no valid spawn point ({failureReason}).", true);
                return;
            }

            try
            {
                string spawnFailureReason = null;
                bool result = await BotsControllerPatch.Instance.SpawnDebugScavEnemy(
                    spawnPoint,
                    reason => spawnFailureReason = reason);

                if (result)
                {
                    DebugConsoleLog($"fs_spawnscav: spawn requested at {FormatVector(spawnPoint)}.", false);
                }
                else
                {
                    string details = string.IsNullOrEmpty(spawnFailureReason) ? "unknown reason" : spawnFailureReason;
                    DebugConsoleLog($"fs_spawnscav: spawn request failed ({details}).", true);
                }
            }
            catch (Exception ex)
            {
                DebugConsoleLog($"fs_spawnscav: exception: {ex.Message}", true);
                Modules.Logger.LogError(ex);
            }
        }

        private static bool TryGetDebugScavSpawnPoint(Player player, out Vector3 spawnPoint, out string failureReason)
        {
            const float maxRayDistance = 150f;
            const float fallbackForwardDistance = 25f;
            float[] sampleRadii = { 1.5f, 3f, 6f, 10f };

            spawnPoint = Vector3.zero;
            failureReason = string.Empty;
            if (player == null)
            {
                failureReason = "player null";
                return false;
            }

            Vector3 rayOrigin = player.PlayerBones?.WeaponRoot?.position ?? (player.Position + Vector3.up * 1.5f);
            Vector3 rayDirection = player.LookDirection.sqrMagnitude > 0.001f
                ? player.LookDirection.normalized
                : player.Transform.forward;

            Vector3 rawTarget;
            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, maxRayDistance, LayerMaskClass.HighPolyWithTerrainMask))
            {
                rawTarget = hit.point;
            }
            else
            {
                Vector3 flatDirection = rayDirection;
                flatDirection.y = 0f;
                if (flatDirection.sqrMagnitude <= 0.001f)
                {
                    flatDirection = player.Transform.forward;
                    flatDirection.y = 0f;
                }

                rawTarget = player.Position + flatDirection.normalized * fallbackForwardDistance;
            }

            foreach (float radius in sampleRadii)
            {
                if (NavMesh.SamplePosition(rawTarget, out NavMeshHit navHit, radius, NavMesh.AllAreas))
                {
                    spawnPoint = navHit.position;
                    return true;
                }
            }

            failureReason = "look target is not near navmesh";
            return false;
        }

        private static string FormatVector(Vector3 value)
        {
            return $"{value.x:0.0},{value.y:0.0},{value.z:0.0}";
        }

        private static void DebugConsoleLog(string message, bool isError)
        {
            try
            {
                Type consoleType = AccessTools.TypeByName("EFT.UI.ConsoleScreen")
                    ?? AccessTools.TypeByName("ConsoleScreen");
                if (consoleType != null)
                {
                    MethodInfo logMethod = AccessTools.Method(consoleType, isError ? "LogError" : "Log", new[] { typeof(string) });
                    logMethod?.Invoke(null, new object[] { message });
                }
            }
            catch
            {
                // ignore console reflection errors
            }

            if (isError) Modules.Logger.LogError(message);
            else Modules.Logger.LogInfo(message);
        }

        void Update()
        {
            CheckLanguageSettingChanged();

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                return;
            }

            if (GamePlayerOwner.MyPlayer == null || GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            if (pingKey.Value.IsUp() || contactKey.Value.IsUp() || overThereKey.Value.IsUp())
            {

                string id = GamePlayerOwner.MyPlayer.ProfileId;

                if (BossPlayers.Instance != null && PingTeamates.Instance != null)
                {
                    var boss = BossPlayers.Instance.GetBossPlayer(id);
                    if (boss != null)
                    {
                        if (pingKey.Value.IsUp())
                            boss.PhraseSaid(new BotEventHandler.GClass692
                            {
                                phrase = (EPhraseTrigger)CustomPhrases.TeamStatus,
                                PlayerRequester = boss.realPlayer
                            });
                        else if (overThereKey.Value.IsUp())
                            BossGestureCommandRouter.TryPlayOverThereGesture(boss.realPlayer);
                        else
                            boss.realPlayer.Say(EPhraseTrigger.OnRepeatedContact, true);
                    }
                }
            }

            else if (teleportKey.Value.IsUp())
            {
                _BotTeleport();
            }

            else if (healKey.Value.IsUp())
            {
                _BotHeal();
            }

        }
    }
}
