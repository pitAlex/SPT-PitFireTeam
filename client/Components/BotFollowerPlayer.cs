using BepInEx.Bootstrap;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using pitTeam.Modules;
using DrakiaXYZ.BigBrain.Brains;
using pitTeam.BigBrain;
using pitTeam.Patches;

namespace pitTeam.Components
{
    public enum FollowerCommandType
    {
        None = 0,
        // Persistent hold consumed by FollowerRequestLayer/GestureCommandAction.
        HoldPosition = 1,
        // Out-of-combat point movement from There/GoForward.
        MoveToPoint = 2,
        // Short boss-close movement from Come With Me.
        ComeCloser = 3,
        // Ordered regroup near boss; combat may route this through combat objectives.
        RegroupNearBoss = 4,
        // Situational request-layer commands.
        TakeLootItem = 5,
        OpenDoor = 6,
        // Commanded corpse recovery. This is cargo collection, not equipment replacement:
        // the action may use empty compatible slots, but it must not swap the follower's kit.
        TakeBodyGear = 7,
        // Combat objective commands.
        PushEnemy = 8,
        SuppressEnemy = 9,
        NeedSniper = 10,
        // Combat gesture movement commands.
        CombatComeToBossCover = 11,
        CombatMoveToPointTactical = 12,
        // Commanded searchable-container looting through backpack/pocket cargo space.
        TakeContainerLoot = 13
    }

    public enum FollowerCombatTactic
    {
        Balanced = 0,
        Marksman = 1,
        Protector = 2,
    }

    public class BotFollowerPlayer
    {
        protected BotOwner _bot;
        protected pitAIBossPlayer _player;

        protected BotSettingsInGameModif settingModif;

        protected bool _IsSquadMate = false;

        protected string _grouId = "";
        protected string _teamId = "";

        protected List<AICoreLayer<BotLogicDecision>>? vanillaLayers;

        public bool IsSquadMate
        {
            get
            {
                return _IsSquadMate;
            }
        }

        public bool CanHandleBodyContainerLootCommands
        {
            get
            {
                if (!_IsSquadMate)
                {
                    return false;
                }

                string profileId = _bot?.ProfileId ?? _bot?.Profile?.ProfileId ?? _bot?.Profile?.Id ?? string.Empty;
                string accountId = _bot?.Profile?.AccountId ?? string.Empty;

                // Body/container looting is reserved for saved teammates that were generated
                // through the raid squad flow. Recruited/picked-up allies can still be followers,
                // but they should not satisfy these command assignments.
                return IsSpawnedSquadMemberId(profileId) ||
                       IsSpawnedSquadMemberId(accountId) ||
                       FollowerTransitStateCache.IsTransitSpawnProfile(profileId);
            }
        }

        private static bool IsSpawnedSquadMemberId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return pitTeam.Utils.SpawnHelper.spawnMemberIds.Contains(id) ||
                   pitTeam.Utils.SpawnHelper.spawnMemberIdsScav.Contains(id);
        }


        protected WildSpawnType _botRole;
        protected bool _canPatrol = false;
        private bool _combatIndependent;
        private bool _combatIndependentRequested;
        private bool _combatRegroupUsesBossAnchor;
        private bool _patrolLeaderSectorAnchorSet;
        private Vector3 _patrolLeaderSectorAnchor;
        private bool _peaceChangeHooked = false;
        private bool _manualUpdateHooked = false;
        private bool _lootedWeaponPromotionInProgress;
        private float _nextLootedWeaponPromotionCheckAt;
        private Vector3 _teleportGraceTarget;
        private float _teleportGraceUntil;
        private const float TeleportGraceSeconds = 0.45f;
        private const float TeleportReteleportDistance = 1.5f;
        private const float LootedWeaponPromotionCheckInterval = 0.75f;
        private const float TemporaryCombatAggressionClearDelaySeconds = 2f;
        private const float TemporaryCombatAggressionRecentEnemySeconds = 3f;
        private const float TemporaryCombatAggressionGroupEnemySeconds = 5f;
        private FollowerCommandType _activeCommand = FollowerCommandType.None;
        private Vector3 _commandTarget;
        private float _commandUntilTime;
        private bool _tightRegroupRequested;
        private int _moveToPointIssueSequence;
        private int _pushEnemyIssueSequence;
        private bool _suppressEnemyRequiresLauncher;
        private bool _suppressEnemyForceWeapon;
        private bool _suppressEnemyUseAutomaticSecondary;
        private bool _orderedPushCancelRequested;
        private string? _orderedPushCancelReason;
        private bool _orderedPushTargetLockActive;
        private string? _orderedPushTargetProfileId;
        private Vector3 _orderedPushTargetPosition;
        private bool _holdPositionShouldCrouch = true;
        private bool _resumeHoldAfterComeCloser;
        private bool _resumeHoldAfterTakeLoot;
        private bool _resumeHoldAfterTakeLootCrouch;
        private bool _postLootMoveToPoint;
        private bool _resumeHoldAfterPostLootMove;
        private bool _resumeHoldAfterPostLootMoveCrouch;
        private FollowerCommandType _committedLootCommand = FollowerCommandType.None;
        private float _commandLookPauseUntil;
        private Vector3 _commandLookOverridePoint;
        private float _commandLookOverrideUntil;
        private string? _knownEnemyProfileId;
        private float _knownEnemySince;
        private bool _knownEnemyLatched;
        private const float KnownEnemyAcquireHoldSeconds = 0.5f;

        private static Type? _sainEnableType;
        private static MethodInfo? _getSainByBotOwnerMethod;
        private static MethodInfo? _getSainByProfileMethod;
        private static bool _sainAddonPatrolBridgeErrorLogged;
        private float _combatAggression = 50f;
        private bool _temporaryCombatAggressionOverrideActive;
        private float _temporaryCombatAggressionOverride;
        private float _temporaryCombatAggressionClearAfter;
        private FollowerCombatTactic _combatTactic = FollowerCombatTactic.Balanced;
        private bool _backpackInspectionActive;
        private float? _pickupIndependence01;
        private string? _initialHolsterWeaponId;
        private bool _initialHolsterWeaponCaptured;
        public bool CanPatrol
        {
            get
            {
                return _canPatrol;
            }
        }

        public bool CombatIndependent
        {
            get
            {
                return _combatIndependent;
            }
        }

        public bool CombatRegroupUsesBossAnchor
        {
            get
            {
                return _combatRegroupUsesBossAnchor;
            }
        }

        public float CombatAggression
        {
            get
            {
                return _combatAggression;
            }
            set
            {
                _combatAggression = Mathf.Clamp(value, 0f, 100f);
            }
        }

        public float EffectiveCombatAggression
        {
            get
            {
                UpdateTemporaryCombatAggressionClearDelay();
                return _temporaryCombatAggressionOverrideActive
                    ? _temporaryCombatAggressionOverride
                    : _combatAggression;
            }
        }

        public bool IsTemporaryCombatAggressionOverrideActive
        {
            get
            {
                UpdateTemporaryCombatAggressionClearDelay();
                return _temporaryCombatAggressionOverrideActive;
            }
        }

        public bool IsTemporaryHoldPositionAggressionActive
        {
            get
            {
                UpdateTemporaryCombatAggressionClearDelay();
                return _temporaryCombatAggressionOverrideActive && _temporaryCombatAggressionOverride <= 0.01f;
            }
        }

        public bool IsBackpackInspectionActive
        {
            get
            {
                return _backpackInspectionActive;
            }
        }

        public bool IsInitialHolsterWeapon(Weapon weapon)
        {
            return weapon != null &&
                   !string.IsNullOrEmpty(_initialHolsterWeaponId) &&
                   string.Equals(weapon.Id, _initialHolsterWeaponId, StringComparison.Ordinal);
        }

        public float PickupIndependence01
        {
            get
            {
                if (_IsSquadMate)
                {
                    return 0f;
                }

                _pickupIndependence01 ??= CalculatePickupIndependence01();
                return _pickupIndependence01.Value;
            }
        }

        public float BossProtectionWillingness01
        {
            get
            {
                if (_IsSquadMate)
                {
                    return 1f;
                }

                int followerLevel = Mathf.Max(1, _bot?.Profile?.Info?.Level ?? 1);
                int playerLevel = Mathf.Max(1, _player?.realPlayer?.Profile?.Info?.Level ?? 1);
                return PickupFollowerPersonality.CalculateBossProtectionWillingness01(
                    followerLevel,
                    playerLevel,
                    PickupIndependence01);
            }
        }

        public FollowerCombatTactic CombatTactic
        {
            get
            {
                return _combatTactic;
            }
            set
            {
                _combatTactic = value;
            }
        }

        public BotFollowerPlayer(BotOwner bot, pitAIBossPlayer player, bool isSquad = false, WildSpawnType botRole = WildSpawnType.assault)
        {
            _bot = bot;
            _player = player;
            _botRole = botRole == WildSpawnType.assault ? _bot.Profile.Info.Settings.Role : botRole;

            _IsSquadMate = isSquad;
            CaptureInitialHolsterWeapon(finalAttempt: false);

            settingModif = new BotSettingsInGameModif(1f, 1f, 1f, 0.9f, 1f, 1f, 1f, 1f, 1f);

            if (player.realPlayer.Side != EPlayerSide.Savage) NpcMessage.AddNpc(bot, isSquad);

        }

        private void CaptureInitialHolsterWeapon(bool finalAttempt)
        {
            if (_initialHolsterWeaponCaptured)
            {
                return;
            }

            try
            {
                InventoryEquipment equipment = _bot?.GetPlayer?.InventoryController?.Inventory?.Equipment;
                if (equipment == null)
                {
                    return;
                }

                Weapon initialHolster = equipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
                if (initialHolster != null)
                {
                    _initialHolsterWeaponId = initialHolster.Id;
                    _initialHolsterWeaponCaptured = true;
                    return;
                }

                // An empty constructor-time slot may still be inventory assembly. Only Init can
                // finalize that the follower genuinely spawned without a holster weapon.
                _initialHolsterWeaponCaptured = finalAttempt;
            }
            catch
            {
                // Init performs the final retry if the player inventory is still being assembled.
            }
        }

        private float CalculatePickupIndependence01()
        {
            string seed =
                _bot?.ProfileId ??
                _bot?.Profile?.AccountId ??
                _bot?.Profile?.Nickname ??
                _bot?.name ??
                string.Empty;
            return PickupFollowerPersonality.CalculateIndependence01(seed);
        }

        public virtual void Init()
        {
            // ORBIT registers its own agent/squad runtime when its layer is constructed. Claim the bot
            // before resetting brain layers so ORBIT cannot keep ticking or re-register this follower.
            OrbitCompatibility.ClaimFollower(_bot);

            // Constructor-time player inventory is normally ready, but follower initialization is
            // the last safe retry before raid commands can alter equipment provenance.
            CaptureInitialHolsterWeapon(finalAttempt: true);
            _canPatrol = false;
            BaseBrain baseBrain = _bot.Brain.BaseBrain;
            if (baseBrain == null)
            {
                Modules.Logger.LogError("BaseBrain is null for " + _bot.Profile.Nickname);
                return;
            }

            // force current layer to trigger end decision
            try
            {
                ResetBrainForFollower(baseBrain);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Error while trying to deactivate vanilla layers");
                Modules.Logger.LogError(ex);
            }


            if (baseBrain != null)
            {
                // deactive current layer to prevent conflicts with our layers, if any
                if (baseBrain.CurLayerInfo != null && baseBrain.CurLayerInfo.IsActive)
                {
                    string name = baseBrain.CurLayerInfo.Name();
                    _bot.Brain.Agent.Deactivate(name);
                    baseBrain.CurLayerInfo.IsActive = false;
                }
            }

            // remove layers that might conflict with our logic (like vanilla follow or loot patrol layers)
            RemoveConflictingLayers(_bot.Brain.BaseBrain, _bot.Brain.Agent);
            SuppressThirdPartyPeaceBehaviors();

            ForceEndCurrentDecision(_bot);
            HookPeaceChange();
            HookManualUpdate();



            // bot might be following someone, reset that
            if (_bot.BotFollower.HaveBoss)
            {
                _bot.BotFollower.BossToFollow.RemoveFollower(_bot);
                _bot.BotFollower.BossToFollow = null;
            }
            // bot might have request going on, dispose it
            if (_bot.BotRequestController.CurRequest != null)
            {
                _bot.BotRequestController.CurRequest.Complete();
                _bot.BotRequestController.CurRequest = null;
            }
            // bot might have an enemy in his mind, clear it
            if (_bot.Memory.HaveEnemy)
            {
                _bot.Memory.DeleteInfoAboutEnemy(_bot.Memory.GoalEnemy.Person);
            }

            // remove looting brain, if present
            /*if (Chainloader.PluginInfos.ContainsKey("me.skwizzy.lootingbots"))
            {
                Type lootingBrain = Type.GetType("LootingBots.Patch.Components.LootingBrain, skwizzy.LootingBots");

                if (lootingBrain != null)
                {
                    if (_bot.GetPlayer.gameObject.TryGetComponent(lootingBrain, out Component component))
                    {
                        Modules.Logger.LogInfo("Looting brain found, removing it");
                        UnityEngine.Object.Destroy(component);
                    }
                }
                else
                {
                    Modules.Logger.LogInfo("Looting brain not found");
                }
            }*/

            // followers should not be questing, so stop it (if questing mod is present)
            /* Type questingBrain = Type.GetType("SPTQuestingBots.Components.BotObjectiveManager, SPTQuestingBots");
             if (questingBrain != null && _bot.GetPlayer.gameObject.TryGetComponent(questingBrain, out var questingComponent))
             {
                 try
                 {
                     MethodInfo stopMethod = questingBrain.GetMethod("StopQuesting", BindingFlags.Public | BindingFlags.Instance);
                     stopMethod?.Invoke(questingComponent, null);
                     Modules.Logger.LogInfo("Questing stopped for " + _bot.Profile.Nickname);
                 }
                 catch (Exception ex)
                 {
                     Modules.Logger.LogError("Failed to stop questing");
                     Modules.Logger.LogError(ex);
                 }
             }*/

            /// reset bot animation stances
            _bot.GetPlayer.MovementContext.SetPatrol(false);
            _bot.Tilt.Stop();

            // add special follower settings
            SetFollowerSettings(_bot);
            // Ensure newly-converted followers can acquire enemies immediately.
            // Recruited bots can carry over a peaceful state from prior AI context.
            _bot.Memory.IsPeace = false;
            // let the bot talk
            _bot.BotTalk.SetSilence(0f);
            _bot.GetPlayer.BeingHitAction -= OnBeingHit;
            _bot.GetPlayer.BeingHitAction += OnBeingHit;
            // force bot to turn off light
            if (_bot.BotLight != null && _bot.BotLight.IsEnable) _bot.BotLight.TurnOff(false, true);
            // make bot follower of player
            _player.AddFollower(_bot);

            // On conversion, immediately face the player to avoid initial upward stare until next steering update.
            if (_player?.realPlayer != null)
            {
                Vector3 lookPoint = _player.realPlayer.Transform.position + Vector3.up * 1.2f;
                _bot.Steering.LookToPoint(lookPoint);
            }


            bool isPickedUp = !_IsSquadMate && (_player.bossGroup == null || _player.bossGroup.Id != _bot.BotsGroup.Id);

            // make bot join the player's group
            if (_player.bossGroup != null)
            {
                Dictionary<IPlayer, EnemyInfo>? enemyInfos = _bot.EnemiesController?.EnemyInfos;
                BotsGroup currentGroup = _bot.BotsGroup;

                // clear the player's followers from being enemies to the bot
                _player.Followers.ForEach(bt =>
                {
                    if (enemyInfos != null && enemyInfos.TryGetValue(bt, out _))
                    {
                        enemyInfos.Remove(bt);
                    }
                });
                // clear the player from being an enemy to the bot
                if (enemyInfos != null && enemyInfos.TryGetValue(_player.realPlayer, out _))
                {
                    enemyInfos.Remove(_player.realPlayer);
                }
                // add the bot to the player's group, if not already (PickUp case here with spawn)
                if (currentGroup != null && currentGroup.Id != _player.bossGroup.Id)
                {
                    // - bot is some kind of boss of a group, we have to change that
                    if (_bot.Boss.HaveFollowers() && currentGroup.BossGroup != null && _bot.Boss.Followers.Count >= 1)
                    {
                        foreach (BotOwner follower in _bot.Boss.Followers)
                        {
                            follower.BotFollower.BossToFollow = null;
                        }
                    }

                    _bot.BotsGroup.RemoveAlly(_bot);
                    _bot.BotsGroup = _player.bossGroup;
                    RefreshSainEnemyListAfterGroupReassign();

                    // - ensure the bot is not marked as enemy already by the others
                    if (_bot.GetPlayer != null)
                    {
                        _player.bossGroup.RemoveEnemy(_bot.GetPlayer);
                    }

                    var botEnemies = enemyInfos?.ToList();
                    if (botEnemies == null)
                    {
                        botEnemies = new List<KeyValuePair<IPlayer, EnemyInfo>>();
                    }
                    foreach (var item in botEnemies)
                    {
                        _bot.Memory.DeleteInfoAboutEnemy(item.Key);
                    }

                    isPickedUp = true;

                    _player.bossGroup.AddMember(_bot, false);
                    Utils.Enemy.ForceIgnoreUntilAggressionOff(_player.bossGroup);
                    foreach (var en in _player.bossGroup.Enemies)
                    {
                        _bot.Memory.AddEnemy(en.Key, en.Value, false);
                    }
                    Utils.Enemy.ForceIgnoreUntilAggressionOff(_bot);
                }
            }
            // if there is no group yet, make one and group the player with the bot (PickUp case here without spawn)
            else
            {
                isPickedUp = true;

                _bot.BotsGroup.RemoveAlly(_bot);

                _bot.Settings.GetEnemyBotTypes().RemoveAll(x => Utils.Props.friendlyBotTypes.Contains(x));
                _bot.Settings.GetFriendlyBotTypes().AddRange(Utils.Props.friendlyBotTypes);

                BotZone zone = _bot.BotsController.BotSpawner.GetClosestZone(_bot.GetPlayer.Transform.position, out var zoneDist);

                List<BotOwner> activeEnemies = new List<BotOwner>();
                foreach (BotOwner item2 in _bot.BotsController.BotSpawner.GetBotEnemiesList(_bot))
                {
                    if (!Utils.Props.friendlyBotTypes.Contains(item2.Profile.Info.Settings.Role))
                        activeEnemies.Add(item2);
                }

                BotsGroup group = new BotsGroupPlayer(zone, _bot.BotsController.BotGame, _bot, activeEnemies, _bot.BotsController.BotSpawner._deadBodiesController, _bot.BotsController.BotSpawner._allPlayers, _player);

                _bot.BotsGroup = group;
                RefreshSainEnemyListAfterGroupReassign();

                // - go through the enemy filtering process
                var groupEnemies = _bot.BotsGroup.Enemies;
                var botEnemies = _bot.EnemiesController.EnemyInfos.ToList();
                foreach (var item in botEnemies)
                {
                    _bot.Memory.DeleteInfoAboutEnemy(item.Key);
                }

                group.AddMember(_bot, false);
                BossPlayers.AddGroupToBoss(_player, group);
                Utils.Enemy.ForceIgnoreUntilAggressionOff(group);
                Utils.Enemy.ForceIgnoreUntilAggressionOff(_bot);

                using (FollowerGoalEnemyTracker.Begin("BotFollowerPlayer.SetFollowerGroup", "groupReassignClear"))
                {
                    _bot.Memory.GoalEnemy = null;
                }
            }

            if (isPickedUp)
            {
                // ensure bot sees the player's group as his group
                var _groupRequestController = _bot.BotRequestController._groupRequestController;
                if (_groupRequestController != null)
                {
                    _groupRequestController.OnAddRequest -= _bot.BotRequestController.OnAddRequest;
                }

                _bot.Memory._botsGroup = _bot.BotsGroup;
                _bot.BotRequestController._groupRequestController = null;
                ResetPickupFollowerRuntimeState();
            }

            EnsureSainBossAndFollowersFriendly();

            var _bots = _bot.BotsController.BotSpawner._bots;
            var _rougeTypes = Utils.Props.BossFollowersType.ToList();
            _rougeTypes.Add(WildSpawnType.exUsec);

            bool friendsWithRogue = !Utils.Props.BossFollowersType.Contains(_botRole) && Utils.Utils.PlayerHasKnightQuest(_player.realPlayer.Profile);

            if (friendsWithRogue)
            {

                _bot.Settings.GetEnemyBotTypes().RemoveAll(x => _rougeTypes.Contains(x));
                _bot.Settings.GetFriendlyBotTypes().AddRange(_rougeTypes);
            }

            foreach (var item in _bots.BotOwners)
            {
                if (BossPlayers.IsFollower(item) || item.IsDead) continue;

                var itemRole = item.Profile.Info.Settings.Role;
                // ensure the new member sees zombies as enemies
                if (Utils.Props.ZombieTypes.Contains(itemRole))
                {
                    _bot.BotsGroup.AddEnemy(item, EBotEnemyCause.addPlayerToBoss);
                }
                // ensure Rogues see the new member as ally and vice versa
                else if (friendsWithRogue)
                {
                    if (!_rougeTypes.Contains(itemRole)) continue;

                    _bot.BotsGroup.RemoveEnemy(item);
                    _bot.BotsGroup.AddNeutral(item);
                    _bot.BotsGroup.AddAlly(item.GetPlayer);

                    _bot.Memory.DeleteInfoAboutEnemy(item);

                    item.BotsGroup.RemoveEnemy(_bot);
                    item.BotsGroup.AddNeutral(_bot);
                    item.BotsGroup.AddAlly(_bot.GetPlayer);

                    item.Memory.DeleteInfoAboutEnemy(_bot);
                }
            }

            // apply the settings modifier
            _bot.Settings.Current.Apply(settingModif);

            Utils.Utils.SetTimeout(() =>
            {
                // TURN OFF THE FLASHLIGHT!
                if (_bot.BotLight != null && _bot.BotLight.IsEnable)
                {
                    _bot.BotLight.TurnOff(false, false);
                }
            }, 300);

            // - ensure weapon is in auto mode
            if (_bot.WeaponManager.ShootController.Item != null && _bot.WeaponManager.ShootController.Item.WeapFireType.Contains(Weapon.EFireMode.fullauto))
                _bot.WeaponManager.ShootController.ChangeFireMode(Weapon.EFireMode.fullauto);

            Modules.Logger.LogInfo($"Bot {_bot.Profile.Nickname} is now a follower of {_player.Player().Profile.Nickname} (IsSquadMate={_IsSquadMate}, IsPeace={_bot.Memory.IsPeace})");
        }

        protected virtual void SetFollowerSettings(BotOwner bot)
        {

            bool isGoon = Utils.Props.BossFollowersType.Contains(_botRole);
            // increase bot's power
            BotSettings settings = Singleton<BotSettingsController>.Instance.GetSettings(BotDifficulty.hard, _botRole, _bot.BotsController.IsPvE);

            // - hardcode some settings to make the bot more efficient
            settings.FileSettings.Move.REACH_DIST = 1.5f;
            settings.FileSettings.Move.REACH_DIST_COVER = 2f;
            settings.FileSettings.Move.REACH_DIST_RUN = 1f;
            settings.FileSettings.Boss.BIG_PIPE_ARTILLERY_COUNT = 1;
            settings.FileSettings.Mind.PART_PERCENT_TO_HEAL = 0.75f;
            settings.FileSettings.Mind.DIST_TO_ENEMY_YO_CAN_HEAL = 15f;

            settings.FileSettings.Mind.DIST_TO_STOP_RUN_ENEMY = 15f;
            settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = pitFireTeam.enemyRemember.Value;
            settings.FileSettings.Mind.TIME_TO_FIND_ENEMY = 10f;
            settings.FileSettings.Mind.ATTACK_IMMEDIATLY_CHANCE_0_100 = 50f;
            settings.FileSettings.Mind.CHANCE_TO_RUN_CAUSE_DAMAGE_0_100 = 50f;


            settings.FileSettings.Mind.CAN_TALK = true;
            settings.FileSettings.Mind.TALK_WITH_QUERY = true;
            bot.BotTalk._canSay = true;
            // - fix missing phrases (bug appeared in 0.16)
            if (!bot.BotTalk._priority.Any(x => x.Key == EPhraseTrigger.Ready))
                bot.BotTalk._priority.Add(EPhraseTrigger.Ready, 140f);
            if (!bot.BotTalk._priority.Any(x => x.Key == EPhraseTrigger.Going))
                bot.BotTalk._priority.Add(EPhraseTrigger.Going, 141f);
            if (!bot.BotTalk._priority.Any(x => x.Key == EPhraseTrigger.DontKnow))
                bot.BotTalk._priority.Add(EPhraseTrigger.DontKnow, 142f);

            settings.FileSettings.Mind.CAN_STAND_BY = false;
            settings.FileSettings.Mind.CAN_TAKE_ANY_ITEM = true;
            settings.FileSettings.Mind.CAN_TAKE_ITEMS = true;
            settings.FileSettings.Mind.CAN_THROW_REQUESTS = true;
            settings.FileSettings.Mind.CAN_DROP_ITEMS = false; // prevent bot from dropping items randomly
            settings.FileSettings.Mind.CAN_USE_MEDS = true;
            settings.FileSettings.Mind.MEDS_ONLY_SAFE_CONTAINER = false;
            settings.FileSettings.Mind.SURGE_KIT_ONLY_SAFE_CONTAINER = false;
            settings.FileSettings.Mind.GROUP_ANY_PHRASE_DELAY = 2f;
            settings.FileSettings.Mind.GROUP_EXACTLY_PHRASE_DELAY = 1f;
            settings.FileSettings.Mind.GROUP_EXACTLY_PHRASE_DELAY_MAX = 1f;

            if (_player.realPlayer.Side != EPlayerSide.Savage)
            {
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_PMC_PLAYERS = false;
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_SAVAGE_PLAYERS = true;
            }
            else
            {
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_SAVAGE_PLAYERS = false;
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_PMC_PLAYERS = true;
            }

            settings.FileSettings.Mind.CHANCE_FUCK_YOU_ON_CONTACT_100 = 0;
            settings.FileSettings.Mind.REVENGE_TO_GROUP = true;

            EPlayerSide playerSide = _player.Player().Side;

            // force follower loyality
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_SAVAGE = playerSide == EPlayerSide.Savage;
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_BEAR = playerSide == EPlayerSide.Bear;
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_USEC = playerSide == EPlayerSide.Usec;
            settings.FileSettings.Mind.CAN_EXECUTE_REQUESTS = true;

            settings.FileSettings.Mind.FRIEND_AGR_KILL = 0.000001f;
            settings.FileSettings.Mind.FRIEND_DEAD_AGR_LOW = -0.000001f;
            settings.FileSettings.Mind.REVENGE_FOR_SAVAGE_PLAYERS = false;

            // follower can turn enemy to anyone and cares only for the boss
            settings.GetWarnBotTypes().Clear();
            settings.FileSettings.Mind.WARN_BOT_TYPES = new WildSpawnType[] { };
            settings.FileSettings.Mind.REVENGE_BOT_TYPES = new WildSpawnType[] { };
            List<WildSpawnType> friendList = settings.GetFriendlyBotTypes();
            friendList.AddRange(Utils.Props.friendlyBotTypes);
            settings.FileSettings.Mind.FRIENDLY_BOT_TYPES = Utils.Props.friendlyBotTypes.ToArray();

            // opposing sides are always enemies
            if (playerSide == EPlayerSide.Bear)
                settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

            else if (playerSide == EPlayerSide.Usec)
                settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

            if (playerSide != EPlayerSide.Savage)
            {
                settings.FileSettings.Mind.DEFAULT_SAVAGE_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                // - bad guy is hostile to everyone    
                if (Utils.Utils.FlagGet("isBadGuy"))
                {
                    settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                    settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

                    List<WildSpawnType> enemyList = settings.GetEnemyBotTypes();

                    if (!enemyList.Contains(WildSpawnType.pmcUSEC))
                        enemyList.Add(WildSpawnType.pmcUSEC);

                    if (!enemyList.Contains(WildSpawnType.pmcBEAR))
                        enemyList.Add(WildSpawnType.pmcBEAR);
                }
            }
            // ensure scav followers are hostile to PMCs
            else
            {
                settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
            }

            settings.FileSettings.Mind.BULLET_FEEL_CLOSE_SDIST = 2f * 2f;
            settings.FileSettings.Mind.CHANCE_FUCK_YOU_ON_CONTACT_100 = 0;
            settings.FileSettings.Mind.DIST_TO_ENEMY_SPOTTED_ON_HIT = 20;
            settings.FileSettings.Mind.DOG_FIGHT_IN = 3f;
            settings.FileSettings.Mind.DOG_FIGHT_OUT = 20f;
            settings.FileSettings.Mind.SHOOT_INSTEAD_DOG_FIGHT = 1f;
            settings.FileSettings.Mind.MIN_DAMAGE_SCARE = 20f;
            settings.FileSettings.Mind.PROTECT_DELTA_HEAL_SEC = 5f;

            settings.FileSettings.Patrol.PICKUP_ITEMS_TO_BACKPACK_OR_CONTAINER = true;
            settings.FileSettings.Patrol.CHANCE_TO_PLAY_VOICE_WHEN_CLOSE = 50;
            settings.FileSettings.Patrol.CHANCE_TO_PLAY_GESTURE_WHEN_CLOSE = 100;
            settings.FileSettings.Patrol.CAN_PEACEFUL_LOOK = true;
            settings.FileSettings.Patrol.FRIEND_SEARCH_SEC = 60;
            settings.FileSettings.Patrol.FOLLOWER_START_MOVE_DELAY = 0.5f;
            settings.FileSettings.Patrol.CAN_FRIENDLY_TILT = true;
            settings.FileSettings.Patrol.VISION_DIST_COEF_PEACE = 1f;

            settings.FileSettings.Boss.SHALL_WARN = false;
            settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER = 0f;

            settings.FileSettings.Look.MINIMUM_VISIBLE_DIST = 15f;

            // Keep follower grenade capability hard-disabled by default.
            // Runtime code opens a short throw window only for explicit follower grenade actions.
            settings.FileSettings.Core.CanGrenade = false;
            settings.FileSettings.Core.CanRun = true;

            settings.FileSettings.Cover.CHECK_CLOSEST_FRIEND = true;
            settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE = 1;
            settings.FileSettings.Cover.HIDE_TO_COVER_TIME = 5;
            settings.FileSettings.Cover.HITS_TO_LEAVE_COVER = 2;
            settings.FileSettings.Cover.HITS_TO_LEAVE_COVER_UNKNOWN = 2;
            settings.FileSettings.Cover.TIME_TO_MOVE_TO_COVER = 15;
            settings.FileSettings.Cover.RETURN_TO_ATTACK_AFTER_AMBUSH_MIN = 20;
            settings.FileSettings.Cover.RETURN_TO_ATTACK_AFTER_AMBUSH_MAX = 50;
            settings.FileSettings.Cover.SPOTTED_GRENADE_RADIUS = 24f;
            settings.FileSettings.Cover.SPOTTED_GRENADE_TIME = 7;

            // - faster aiming sett
            settings.FileSettings.Aiming.COEF_IF_MOVE = 1f;
            settings.FileSettings.Aiming.BOTTOM_COEF = 0.05f;
            settings.FileSettings.Aiming.COEF_FROM_COVER = 0.3f;
            settings.FileSettings.Aiming.PANIC_COEF = 1f;
            if (!isGoon)
            {
                settings.FileSettings.Aiming.MAX_AIMING_UPGRADE_BY_TIME = 0.15f;
            }

            // - improved shooting settings
            settings.FileSettings.Aiming.SHPERE_FRIENDY_FIRE_SIZE = 0.5f;
            settings.FileSettings.Aiming.AIMING_TYPE = 6; // the head is a priority
            if (!isGoon)
            {
                settings.FileSettings.Aiming.ANY_PART_SHOOT_TIME = 15f;
                settings.FileSettings.Aiming.ANYTIME_LIGHT_WHEN_AIM_100 = 50;
                settings.FileSettings.Aiming.BAD_SHOOTS_MAX = 2;
                settings.FileSettings.Aiming.BAD_SHOOTS_MIN = 1;
                settings.FileSettings.Aiming.FIRST_CONTACT_ADD_CHANCE_100 = 20f;
            }
            // - hit disturbance settings
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_DELAY_SEC = 0.2f;
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_MAX_ANG = 10f;
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_MIN_ANG = 2f;
            settings.FileSettings.Aiming.DAMAGE_PANIC_TIME = 10f;
            if (!isGoon)
            {
                settings.FileSettings.Aiming.DAMAGE_TO_DISCARD_AIM_0_100 = 30f;
            }


            settings.FileSettings.Look.CAN_USE_LIGHT = true;
            settings.FileSettings.Look.NIGHT_VISION_ON = 100.0f;
            settings.FileSettings.Look.NIGHT_VISION_OFF = 110.0f;
            settings.FileSettings.Look.NIGHT_VISION_DIST = 160.0f;
            settings.FileSettings.Look.VISIBLE_ANG_NIGHTVISION = 120f;
            settings.FileSettings.Look.LOOK_THROUGH_PERIOD_BY_HIT = 5f;
            settings.FileSettings.Look.LightOnVisionDistance = 40.0f;
            settings.FileSettings.Look.LOOK_LAST_POSENEMY_IF_NO_DANGER_SEC = 25f;
            settings.FileSettings.Look.VISIBLE_ANG_LIGHT = 45.0f;
            settings.FileSettings.Look.VISIBLE_DISNACE_WITH_LIGHT = 65.0f;


            settings.FileSettings.Look.GOAL_TO_FULL_DISSAPEAR = 1.1f;
            settings.FileSettings.Look.GOAL_TO_FULL_DISSAPEAR_GREEN = 2f;
            //settings.FileSettings.Look.LOOK_THROUGH_GRASS = true;
            settings.FileSettings.Look.MAX_VISION_GRASS_METERS = 1.0f;
            settings.FileSettings.Look.NO_GREEN_DIST = 4.0f;
            settings.FileSettings.Look.NO_GRASS_DIST = 5.0f;

            settings.FileSettings.Look.CHECK_HEAD_ANY_DIST = true;
            settings.FileSettings.Look.MIDDLE_DIST_CAN_SHOOT_HEAD = true;

            settings.FileSettings.Hearing.DISPERSION_COEF = 1.6f;
            settings.FileSettings.Hearing.CLOSE_DIST = 7f;
            settings.FileSettings.Hearing.FAR_DIST = 35f;

            settings.FileSettings.Cover.SIT_DOWN_WHEN_HOLDING = true;

            settings.FileSettings.Shoot.WAIT_NEXT_SINGLE_SHOT = 0.1f;
            settings.FileSettings.Shoot.WAIT_NEXT_SINGLE_SHOT_LONG_MAX = 1.8f;
            settings.FileSettings.Shoot.NEXT_SINGLE_SHOT_PAUSE = 3f;

            settings.FileSettings.Boss.EFFECT_REGENERATION_PER_MIN = 40f;

            settings.FileSettings.Grenade.IGNORE_SMOKE_GRENADE = true;
            settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = false;
            settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = false;
            settings.FileSettings.Grenade.NO_RUN_FROM_AI_GRENADES = false;

            bot.Settings = settings;

            bot.ENEMY_LOOK_AT_ME = Mathf.Cos(settings.FileSettings.Mind.ENEMY_LOOK_AT_ME_ANG * 0.017453292f);
            bot.GetPlayer.ActiveHealthController.SetDamageCoeff(settings.FileSettings.Core.DamageCoeff);

            // counter SAIN
            bot.LookSensor.ShootFromEyes = true;
            bot.Settings.FileSettings.Look.SHOOT_FROM_EYES = true;

            // - friendly bot never gets tired
            bot.GetPlayer.Physical.Stamina.ForceMode = true;
            bot.GetPlayer.Physical.HandsStamina.ForceMode = true;
            // - need no food
            bot.GetPlayer.HealthController.DisableMetabolism();
            // - have followers share the same groupId as the player
            _grouId = bot.GetPlayer.Profile.Info.GroupId;
            _teamId = bot.GetPlayer.Profile.Info.TeamId;
            bot.GetPlayer.Profile.Info.GroupId = _player.realPlayer.GroupId;
            bot.GetPlayer.Profile.Info.TeamId = _player.realPlayer.Profile.Info.TeamId;

            bot.Tactic.AggressionChange(1f);

            // - take on the new vision values
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Core.VisibleAngle * 0.017453292f));
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE_LIGHT").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Look.VISIBLE_ANG_LIGHT * 0.017453292f));
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE_NIGHTVISION").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Look.VISIBLE_ANG_NIGHTVISION * 0.017453292f));

            _bot.LookSensor.ManualUpdate();

            // force bosses to not warn the follower (these will be exUsec and the Goons)
            if (_player.realPlayer.Side != EPlayerSide.Savage && Utils.Utils.PlayerHasKnightQuest(_player.realPlayer.Profile))
            {
                var field = typeof(FenceLoyaltyLevel).GetField("HostileBosses", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    FieldInfo attrField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
                    attrField?.SetValue(field, field.Attributes & ~FieldAttributes.InitOnly);

                    field.SetValue(_bot.Profile.FenceInfo.FenceLoyalty, false);
                }
            }

            // SAIN plugin keeps its own ForgetEnemyTime property (private setter) that drives
            // EnemyKnownChecker independently of the vanilla TIME_TO_FORGOR_ABOUT_ENEMY_SEC field.
            // Override it here so both paths use the player-configured value.
            // This must run after bot.Settings = settings above, and is safe to call before SAIN
            // AddFollower postfix fires since GetSainBot falls back gracefully when SAIN is absent.
            if (pitFireTeam.IsSAINInstalled)
            {
                TryOverrideSainForgetEnemyTime(bot, pitFireTeam.enemyRemember.Value);
            }
        }

        private static void TryOverrideSainForgetEnemyTime(BotOwner bot, float forgetSeconds)
        {
            if (bot == null || forgetSeconds <= 0f) return;

            try
            {
                object sainBot = GetSainBot(bot);
                if (sainBot == null) return;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object info = null;
                PropertyInfo infoProp = sainBot.GetType().GetProperty("Info", flags);
                if (infoProp?.CanRead == true) info = infoProp.GetValue(sainBot);
                if (info == null)
                {
                    FieldInfo infoField = sainBot.GetType().GetField("Info", flags);
                    if (infoField != null) info = infoField.GetValue(sainBot);
                }
                if (info == null) return;

                // Override the auto-property backing field; private setter prevents direct set.
                FieldInfo backingField = info.GetType().GetField("<ForgetEnemyTime>k__BackingField", flags);
                if (backingField != null)
                {
                    backingField.SetValue(info, forgetSeconds);
                }
                else
                {
                    Modules.Logger.LogInfo("[SAIN] Could not find ForgetEnemyTime backing field — SAIN forget time may not match config.");
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed to override SAINBotInfoClass.ForgetEnemyTime");
                Modules.Logger.LogError(ex);
            }
        }

        public bool IsBot(BotOwner bot)
        {
            return _bot == null ? false : bot.ProfileId == _bot.ProfileId;
        }

        public BotOwner GetBot()
        {
            return _bot;
        }

        public pitAIBossPlayer GetBoss()
        {
            if (_bot == null) return null;
            return _player;
        }

        public void BeginTeleportGrace(Vector3 target)
        {
            _teleportGraceTarget = target;
            _teleportGraceUntil = Time.time + TeleportGraceSeconds;
        }

        public virtual void Dismiss(bool warnPlayer = false)
        {
            if (_bot == null) return;
            try
            {
                if (_bot.IsDead || _bot.BotState != EBotState.Active) return;

                // Prevent stale enemy refs from surviving the follower->regular bot handoff.
                ClearInvalidEnemyState();

                // - ensure bot no longer follows the player
                if (_bot.BotFollower.HaveBoss)
                {
                    _bot.BotFollower.BossToFollow.RemoveFollower(_bot);
                    _bot.BotFollower.BossToFollow = null;

                }
                // - bot might have request going on, dispose it
                if (_bot.BotRequestController.CurRequest != null)
                {
                    _bot.BotRequestController.CurRequest.Complete();
                }

                _bot.GetPlayer.Physical.Stamina.ForceMode = false;
                _bot.GetPlayer.Physical.HandsStamina.ForceMode = false;

                // -- bot needs a new group
                BotZone zone = _bot.BotsController.BotSpawner.GetClosestZone(_bot.GetPlayer.Transform.position, out var zoneDist);
                BotsGroup group = _bot.BotsController.BotSpawner.GetGroupAndSetEnemies(_bot, zone);

                _bot.BotsGroup = group;

                if (_bot.BotRequestController._groupRequestController != null)
                {
                    _bot.BotRequestController._groupRequestController.OnAddRequest -= _bot.BotRequestController.OnAddRequest;
                }

                _bot.Memory._botsGroup = group;
                _bot.BotRequestController._groupRequestController = null;

                _bot.GetPlayer.Profile.Info.GroupId = _grouId;
                _bot.GetPlayer.Profile.Info.TeamId = _teamId;

                _bot.BotsGroup.AddMember(_bot, false);
                Utils.Enemy.ForceIgnoreUntilAggressionOff(_bot.BotsGroup);
                Utils.Enemy.ForceIgnoreUntilAggressionOff(_bot);

                _bot.Memory.IsPeace = !warnPlayer;

                // make player and his followers enemies of the bot
                if (warnPlayer)
                {
                    _player.Followers.ForEach(fl =>
                    {
                        if (_bot.EnemiesController.EnemyInfos.TryGetValue(fl.GetPlayer, out var info))
                        {
                            _bot.Memory.DeleteInfoAboutEnemy(fl.GetPlayer);
                        }
                        _bot.BotsGroup.AddEnemy(fl.GetPlayer, EBotEnemyCause.addPlayer);
                    });

                    if (_bot.EnemiesController.EnemyInfos.TryGetValue(_player.Player(), out var plinfo))
                    {
                        _bot.Memory.DeleteInfoAboutEnemy(_player.Player());
                    }

                    _bot.BotsGroup.CheckAndAddEnemy(_player.Player());
                    _bot.BotsGroup.Enemies.ExecuteForEach((key, value) =>
                    {
                        value.IsHaveSeen = key.ProfileId == _player.Player().ProfileId || BossPlayers.GetFollowers().Find(fl => fl.GetBot().ProfileId == key.ProfileId) != null;
                        _bot.Memory.AddEnemy(key, value, false);

                        if (key.ProfileId == _player.Player().ProfileId && _bot.EnemiesController.EnemyInfos.TryGetValue(_player.Player(), out var eninfo))
                        {
                            using (FollowerGoalEnemyTracker.Begin("BotFollowerPlayer.Dismiss", "warnPlayer"))
                            {
                                _bot.Memory.GoalEnemy = eninfo;
                            }
                            Modules.Logger.LogInfo("Make player the enemy");
                        }
                    });
                }

            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("Error on dismiss for a follower: " + ex.Message);
                Modules.Logger.LogInfo(ex.StackTrace);
            }
            finally
            {
                // Fire lifecycle event for addon integration (cleanup, cache removal, etc).
                Modules.SainAddonBridge.RaiseFollowerLifecycleEvent(_bot, FollowerLifecycleEvent.OnDismiss);

                if (_bot?.GetPlayer != null)
                {
                    _bot.GetPlayer.BeingHitAction -= OnBeingHit;
                }

                if (_bot != null)
                {
                    InteractableObjects.ClearStoredItems(_bot.ProfileId);
                    InteractableObjects.RemoveTaker(_bot);
                    NpcMessage.RemoveNpc(_bot.ProfileId, _bot.HealthController?.IsAlive == false);
                }

                ClearCommand("Dismiss:finally");
                UnhookManualUpdate();
                UnhookPeaceChange();
            }
            // @TODO : see what else can be reverted
        }

        private void ClearInvalidEnemyState()
        {
            if (_bot == null) return;

            // Clear current/last enemy pointers first to avoid group decision sorting stale EnemyInfo.
            using (FollowerGoalEnemyTracker.Begin("BotFollowerPlayer.ClearInvalidEnemyState", "clearInvalidEnemyState"))
            {
                _bot.Memory.GoalEnemy = null;
            }
            _bot.Memory.LastEnemy = null;

            if (_bot.EnemiesController?.EnemyInfos != null)
            {
                foreach (IPlayer enemy in _bot.EnemiesController.EnemyInfos.Keys.ToList())
                {
                    if (!IsValidEnemyRef(enemy))
                    {
                        _bot.Memory.DeleteInfoAboutEnemy(enemy);
                    }
                }
            }

            if (_bot.BotsGroup?.Enemies != null)
            {
                foreach (IPlayer enemy in _bot.BotsGroup.Enemies.Keys.ToList())
                {
                    if (!IsValidEnemyRef(enemy))
                    {
                        _bot.BotsGroup.RemoveEnemy(enemy);
                    }
                }
            }
        }

        private static bool IsValidEnemyRef(IPlayer enemy)
        {
            if (enemy == null) return false;

            try
            {
                var _ = enemy.Position;
            }
            catch
            {
                return false;
            }

            if (enemy.IsAI && enemy.AIData?.BotOwner?.GetPlayer == null)
            {
                return false;
            }

            return true;
        }

        public void SetCanPatrol(bool value)
        {
            // Persistent out-of-combat On Your Own mode. Combat derives a separate
            // independence flag from this at combat start, then combat commands can
            // change only the combat flag without dropping patrol intent.
            _canPatrol = value;
            if (!value)
            {
                ClearPatrolLeaderSectorAnchor();
                ReleasePatrolMovementState("SetCanPatrol:false");
            }
        }

        public bool TryGetPatrolLeaderSectorAnchor(out Vector3 anchor)
        {
            anchor = _patrolLeaderSectorAnchor;
            return _patrolLeaderSectorAnchorSet;
        }

        public void SetPatrolLeaderSectorAnchor(Vector3 anchor)
        {
            _patrolLeaderSectorAnchor = anchor;
            _patrolLeaderSectorAnchorSet = true;
        }

        public void ClearPatrolLeaderSectorAnchor()
        {
            _patrolLeaderSectorAnchor = Vector3.zero;
            _patrolLeaderSectorAnchorSet = false;
        }

        private void ReleasePatrolMovementState(string reason)
        {
            try
            {
                if (_bot == null || _bot.IsDead || _bot.BotState != EBotState.Active)
                {
                    return;
                }

                _bot.GetPlayer?.MovementContext?.SetPatrol(false);
                _bot.Tilt?.Stop();
                if (_bot.Mover != null)
                {
                    _bot.Mover.Pause = false;
                    if (_bot.Mover.Sprinting)
                    {
                        _bot.Mover.Sprint(false, false);
                    }
                    _bot.Mover.SetTargetMoveSpeed(1f);
                }

                if (_bot.BotRequestController?.CurRequest != null)
                {
                    _bot.BotRequestController.CurRequest.Complete();
                    _bot.BotRequestController.CurRequest = null;
                }

                _bot.PatrollingData?.Pause();
                _bot.GoToSomePointData?.SetPoint(_bot.Position);
                _bot.GoToSomePointData?.UpdateToGo(false);
                _bot.StopMove();
                ForceEndCurrentDecision(_bot);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Patrol] Failed to release patrol movement state for {_bot?.Profile?.Nickname} reason={reason}");
                Modules.Logger.LogError(ex);
            }
        }

        public void BeginCombatIndependenceFromPatrol()
        {
            _combatIndependent = _canPatrol || _combatIndependentRequested;
            _combatRegroupUsesBossAnchor = false;
        }

        public void SetCombatIndependent(bool value)
        {
            _combatIndependentRequested = value;
            _combatIndependent = value;
            _combatRegroupUsesBossAnchor = false;
        }

        public void ClearActiveCombatIndependent()
        {
            _combatIndependent = false;
            _combatRegroupUsesBossAnchor = false;
        }

        public void ClearCombatIndependent()
        {
            _combatIndependent = false;
            _combatIndependentRequested = false;
            _combatRegroupUsesBossAnchor = false;
        }

        public void SetCombatRegroupBossAnchor(bool value)
        {
            _combatRegroupUsesBossAnchor = value;
        }

        public void SetBackpackInspectionActive(bool active)
        {
            _backpackInspectionActive = active;
        }

        public void SetHoldPosition(float duration, bool crouch = true)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            ClearVanillaRequestState(null, nameof(SetHoldPosition));
            _activeCommand = FollowerCommandType.HoldPosition;
            _commandUntilTime = float.PositiveInfinity;
            _commandTarget = Vector3.zero;
            _holdPositionShouldCrouch = crouch;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            ResetPostLootMoveState();
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetHoldPosition));
        }

        public void SetMoveToPoint(Vector3 target, float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            SetMoveToPointState(target, nameof(SetMoveToPoint), refreshBrain: true);
        }

        private void SetMoveToPointState(Vector3 target, string source, bool refreshBrain)
        {
            unchecked
            {
                _moveToPointIssueSequence++;
            }

            _activeCommand = FollowerCommandType.MoveToPoint;
            _commandTarget = target;
            _commandUntilTime = float.PositiveInfinity;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            ResetPostLootMoveState();
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, source);
            if (refreshBrain &&
                !HasKnownEnemy() &&
                !FollowerCombatLayer.TryForceReleaseCoreFollowerCombatState(_bot, nameof(SetMoveToPoint)))
            {
                Utils.FollowerRecovery.SoftReset(_bot);
            }
        }

        public bool TrySetPostLootMoveToPoint(FollowerCommandType completedLootCommand, Vector3 target)
        {
            if (ShouldIgnoreCommandSet() ||
                _activeCommand != completedLootCommand ||
                (completedLootCommand != FollowerCommandType.TakeLootItem &&
                 completedLootCommand != FollowerCommandType.TakeBodyGear &&
                 completedLootCommand != FollowerCommandType.TakeContainerLoot))
            {
                return false;
            }

            bool resumeHold = _resumeHoldAfterTakeLoot;
            bool resumeHoldCrouch = _resumeHoldAfterTakeLootCrouch;
            SetMoveToPointState(target, nameof(TrySetPostLootMoveToPoint), refreshBrain: false);
            _postLootMoveToPoint = true;
            _resumeHoldAfterPostLootMove = resumeHold;
            _resumeHoldAfterPostLootMoveCrouch = resumeHoldCrouch;
            return true;
        }

        public void SetComeCloser(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            FollowerCommandType previous = _activeCommand;
            if (_activeCommand == FollowerCommandType.HoldPosition)
            {
                _resumeHoldAfterComeCloser = true;
            }
            else if (_activeCommand != FollowerCommandType.ComeCloser)
            {
                _resumeHoldAfterComeCloser = false;
            }

            _activeCommand = FollowerCommandType.ComeCloser;
            _commandUntilTime = _resumeHoldAfterComeCloser
                ? float.PositiveInfinity
                : Time.time + Mathf.Max(2f, duration);
            _commandTarget = Vector3.zero;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetComeCloser));
        }

        public void SetRegroup(float duration, bool tightRegroup = false)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.RegroupNearBoss)
            {
                ClearCommand($"SetRegroup:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.RegroupNearBoss;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(2f, duration);
            _tightRegroupRequested = tightRegroup;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetRegroup));
        }

        public void SetTakeLootItem(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand == FollowerCommandType.HoldPosition)
            {
                _resumeHoldAfterTakeLoot = true;
                _resumeHoldAfterTakeLootCrouch = _holdPositionShouldCrouch;
            }
            else if (_activeCommand != FollowerCommandType.TakeLootItem)
            {
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
            }

            if (_activeCommand != FollowerCommandType.None &&
                _activeCommand != FollowerCommandType.TakeLootItem &&
                _activeCommand != FollowerCommandType.HoldPosition)
            {
                ClearCommand($"SetTakeLootItem:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.TakeLootItem;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(6f, duration);
            _resumeHoldAfterComeCloser = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetTakeLootItem));
        }

        public void SetTakeBodyGear(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand == FollowerCommandType.HoldPosition)
            {
                _resumeHoldAfterTakeLoot = true;
                _resumeHoldAfterTakeLootCrouch = _holdPositionShouldCrouch;
            }
            else if (_activeCommand != FollowerCommandType.TakeBodyGear)
            {
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
            }

            if (_activeCommand != FollowerCommandType.None &&
                _activeCommand != FollowerCommandType.TakeBodyGear &&
                _activeCommand != FollowerCommandType.HoldPosition)
            {
                ClearCommand($"SetTakeBodyGear:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.TakeBodyGear;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(12f, duration);
            _resumeHoldAfterComeCloser = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetTakeBodyGear));
        }

        public void SetTakeContainerLoot(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand == FollowerCommandType.HoldPosition)
            {
                _resumeHoldAfterTakeLoot = true;
                _resumeHoldAfterTakeLootCrouch = _holdPositionShouldCrouch;
            }
            else if (_activeCommand != FollowerCommandType.TakeContainerLoot)
            {
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
            }

            if (_activeCommand != FollowerCommandType.None &&
                _activeCommand != FollowerCommandType.TakeContainerLoot &&
                _activeCommand != FollowerCommandType.HoldPosition)
            {
                ClearCommand($"SetTakeContainerLoot:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.TakeContainerLoot;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(12f, duration);
            _resumeHoldAfterComeCloser = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetTakeContainerLoot));
        }

        public void SetOpenDoor(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.OpenDoor)
            {
                ClearCommand($"SetOpenDoor:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.OpenDoor;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(6f, duration);
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetOpenDoor));
        }

        public void SetPushEnemy(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            unchecked
            {
                _pushEnemyIssueSequence++;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.PushEnemy)
            {
                ClearCommand($"SetPushEnemy:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.PushEnemy;
            _commandTarget = Vector3.zero;
            _commandUntilTime = float.PositiveInfinity;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetPushEnemy));
        }

        public void SetSuppressEnemy(float duration)
        {
            SetSuppressEnemy(duration, Vector3.zero);
        }

        public void SetSuppressEnemy(float duration, Vector3 orderTarget)
        {
            SetSuppressEnemy(duration, orderTarget, requireLauncher: false);
        }

        public void SetSuppressEnemy(float duration, Vector3 orderTarget, bool requireLauncher)
        {
            SetSuppressEnemy(duration, orderTarget, requireLauncher, forceWeapon: false);
        }

        public void SetSuppressEnemy(float duration, Vector3 orderTarget, bool requireLauncher, bool forceWeapon)
        {
            SetSuppressEnemy(duration, orderTarget, requireLauncher, forceWeapon, useAutomaticSecondary: false);
        }

        public void SetSuppressEnemy(
            float duration,
            Vector3 orderTarget,
            bool requireLauncher,
            bool forceWeapon,
            bool useAutomaticSecondary)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.SuppressEnemy)
            {
                ClearCommand($"SetSuppressEnemy:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.SuppressEnemy;
            _commandTarget = orderTarget;
            _commandUntilTime = Time.time + Mathf.Max(4f, duration);
            _suppressEnemyRequiresLauncher = requireLauncher;
            _suppressEnemyForceWeapon = forceWeapon;
            _suppressEnemyUseAutomaticSecondary = useAutomaticSecondary;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetSuppressEnemy));
        }

        public void SetNeedSniper(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.NeedSniper)
            {
                ClearCommand($"SetNeedSniper:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.NeedSniper;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(4f, duration);
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetNeedSniper));
        }

        public void SetCombatComeToBossCover(float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.CombatComeToBossCover)
            {
                ClearCommand($"SetCombatComeToBossCover:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.CombatComeToBossCover;
            _commandTarget = Vector3.zero;
            _commandUntilTime = Time.time + Mathf.Max(4f, duration);
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetCombatComeToBossCover));
        }

        public void SetCombatMoveToPointTactical(Vector3 target, float duration)
        {
            if (ShouldIgnoreCommandSet())
            {
                return;
            }

            if (_activeCommand != FollowerCommandType.None && _activeCommand != FollowerCommandType.CombatMoveToPointTactical)
            {
                ClearCommand($"SetCombatMoveToPointTactical:replace({_activeCommand})");
            }

            _activeCommand = FollowerCommandType.CombatMoveToPointTactical;
            _commandTarget = target;
            _commandUntilTime = Time.time + Mathf.Max(4f, duration);
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(SetCombatMoveToPointTactical));
        }

        public void SetTemporaryCombatAggressionOverride(
            float aggression,
            string source = nameof(SetTemporaryCombatAggressionOverride))
        {
            bool previousActive = _temporaryCombatAggressionOverrideActive;
            float previousAggression = _temporaryCombatAggressionOverride;
            ClearVanillaRequestState(null, nameof(SetTemporaryCombatAggressionOverride));
            _temporaryCombatAggressionOverride = Mathf.Clamp(aggression, 0f, 100f);
            _temporaryCombatAggressionOverrideActive = true;
            _temporaryCombatAggressionClearAfter = 0f;
            BattleRecorder.RecordCombatAggressionOverride(
                this,
                "set",
                source,
                previousActive,
                previousAggression,
                _temporaryCombatAggressionOverrideActive,
                _temporaryCombatAggressionOverride,
                _temporaryCombatAggressionClearAfter);
        }

        public void ClearTemporaryCombatAggressionOverride(
            string source = nameof(ClearTemporaryCombatAggressionOverride))
        {
            bool previousActive = _temporaryCombatAggressionOverrideActive;
            float previousAggression = _temporaryCombatAggressionOverride;
            float previousClearAfter = _temporaryCombatAggressionClearAfter;
            _temporaryCombatAggressionOverrideActive = false;
            _temporaryCombatAggressionOverride = 0f;
            _temporaryCombatAggressionClearAfter = 0f;
            if (previousActive ||
                !Mathf.Approximately(previousAggression, 0f) ||
                previousClearAfter > 0f)
            {
                BattleRecorder.RecordCombatAggressionOverride(
                    this,
                    "clear",
                    source,
                    previousActive,
                    previousAggression,
                    _temporaryCombatAggressionOverrideActive,
                    _temporaryCombatAggressionOverride,
                    _temporaryCombatAggressionClearAfter);
            }
        }

        public void ClearTemporaryCombatAggressionOverrideAfterCombatCooldown()
        {
            if (!_temporaryCombatAggressionOverrideActive)
            {
                _temporaryCombatAggressionClearAfter = 0f;
                return;
            }

            float clearAt = Time.time + TemporaryCombatAggressionClearDelaySeconds;
            _temporaryCombatAggressionClearAfter = Mathf.Max(_temporaryCombatAggressionClearAfter, clearAt);
        }

        public void CancelTemporaryCombatAggressionOverrideClearDelay()
        {
            _temporaryCombatAggressionClearAfter = 0f;
        }

        private void UpdateTemporaryCombatAggressionClearDelay()
        {
            if (_temporaryCombatAggressionClearAfter <= 0f)
            {
                return;
            }

            if (Time.time < _temporaryCombatAggressionClearAfter)
            {
                return;
            }

            if (_bot == null || _bot.IsDead || _bot.BotState != EBotState.Active)
            {
                ClearTemporaryCombatAggressionOverride("combatCooldownBotInactive");
                return;
            }

            if (!IsSafelyOutOfCombat(_bot))
            {
                _temporaryCombatAggressionClearAfter = Time.time + TemporaryCombatAggressionClearDelaySeconds;
                return;
            }

            ClearTemporaryCombatAggressionOverride("combatCooldownElapsed");
        }

        public void CompleteComeCloser()
        {
            if (_activeCommand != FollowerCommandType.ComeCloser)
            {
                return;
            }

            if (_resumeHoldAfterComeCloser)
            {
                _activeCommand = FollowerCommandType.HoldPosition;
                _commandTarget = Vector3.zero;
                _commandUntilTime = float.PositiveInfinity;
                _resumeHoldAfterComeCloser = false;
                BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(CompleteComeCloser));
                return;
            }

            ClearCommand("CompleteComeCloser");
        }

        public bool IsComeCloserFromHold()
        {
            return _activeCommand == FollowerCommandType.ComeCloser && _resumeHoldAfterComeCloser;
        }

        public void CompleteMoveToPoint(string reason)
        {
            if (_activeCommand != FollowerCommandType.MoveToPoint)
            {
                return;
            }

            if (_postLootMoveToPoint && _resumeHoldAfterPostLootMove)
            {
                bool crouch = _resumeHoldAfterPostLootMoveCrouch;
                ResetPostLootMoveState();
                _activeCommand = FollowerCommandType.HoldPosition;
                _commandTarget = Vector3.zero;
                _commandUntilTime = float.PositiveInfinity;
                _holdPositionShouldCrouch = crouch;
                BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(CompleteMoveToPoint));
                return;
            }

            ClearCommand(reason);
        }

        private void ResetPostLootMoveState()
        {
            _postLootMoveToPoint = false;
            _resumeHoldAfterPostLootMove = false;
            _resumeHoldAfterPostLootMoveCrouch = false;
        }

        public void CompleteTakeLootItem()
        {
            if (_activeCommand != FollowerCommandType.TakeLootItem)
            {
                return;
            }

            if (_resumeHoldAfterTakeLoot)
            {
                _activeCommand = FollowerCommandType.HoldPosition;
                _commandTarget = Vector3.zero;
                _commandUntilTime = float.PositiveInfinity;
                _holdPositionShouldCrouch = _resumeHoldAfterTakeLootCrouch;
                _resumeHoldAfterComeCloser = false;
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
                BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(CompleteTakeLootItem));
                return;
            }

            ClearCommand("CompleteTakeLootItem");
        }

        public void CompleteTakeBodyGear()
        {
            if (_activeCommand != FollowerCommandType.TakeBodyGear)
            {
                return;
            }

            if (_resumeHoldAfterTakeLoot)
            {
                _activeCommand = FollowerCommandType.HoldPosition;
                _commandTarget = Vector3.zero;
                _commandUntilTime = float.PositiveInfinity;
                _holdPositionShouldCrouch = _resumeHoldAfterTakeLootCrouch;
                _resumeHoldAfterComeCloser = false;
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
                BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(CompleteTakeBodyGear));
                return;
            }

            ClearCommand("CompleteTakeBodyGear");
        }

        public void CompleteTakeContainerLoot()
        {
            if (_activeCommand != FollowerCommandType.TakeContainerLoot)
            {
                return;
            }

            if (_resumeHoldAfterTakeLoot)
            {
                _activeCommand = FollowerCommandType.HoldPosition;
                _commandTarget = Vector3.zero;
                _commandUntilTime = float.PositiveInfinity;
                _holdPositionShouldCrouch = _resumeHoldAfterTakeLootCrouch;
                _resumeHoldAfterComeCloser = false;
                _resumeHoldAfterTakeLoot = false;
                _resumeHoldAfterTakeLootCrouch = false;
                BattleRecorder.RecordCommandSet(this, _activeCommand, _commandTarget, _commandUntilTime, nameof(CompleteTakeContainerLoot));
                return;
            }

            ClearCommand("CompleteTakeContainerLoot");
        }

        public bool ShouldCrouchForHoldPosition()
        {
            return _holdPositionShouldCrouch;
        }

        public bool IsCommittedLootCommandActive()
        {
            return _committedLootCommand != FollowerCommandType.None;
        }

        public bool IsCommittedLootCommandActive(FollowerCommandType command)
        {
            return _committedLootCommand == command;
        }

        public bool IsLootOrPickupCommandActive()
        {
            return _activeCommand == FollowerCommandType.TakeLootItem ||
                   _activeCommand == FollowerCommandType.TakeBodyGear ||
                   _activeCommand == FollowerCommandType.TakeContainerLoot;
        }

        public void BeginCommittedLootCommand(FollowerCommandType command)
        {
            if (command == FollowerCommandType.TakeBodyGear ||
                command == FollowerCommandType.TakeContainerLoot)
            {
                _committedLootCommand = command;
            }
        }

        public void EndCommittedLootCommand(FollowerCommandType command)
        {
            if (_committedLootCommand == command)
            {
                _committedLootCommand = FollowerCommandType.None;
            }
        }

        private bool ShouldIgnoreCommandSet()
        {
            return _committedLootCommand != FollowerCommandType.None;
        }

        public void PauseCommandLookRandom(float duration)
        {
            _commandLookPauseUntil = Mathf.Max(_commandLookPauseUntil, Time.time + Mathf.Max(0f, duration));
        }

        public bool IsCommandLookRandomPaused()
        {
            return Time.time < _commandLookPauseUntil;
        }

        public void SetCommandLookOverride(Vector3 point, float duration)
        {
            if (duration <= 0f)
            {
                return;
            }

            _commandLookOverridePoint = point;
            _commandLookOverrideUntil = Time.time + duration;
        }

        public void ClearCommandLookOverride()
        {
            _commandLookPauseUntil = 0f;
            _commandLookOverridePoint = Vector3.zero;
            _commandLookOverrideUntil = 0f;
        }

        public bool TryGetCommandLookOverride(out Vector3 point)
        {
            if (Time.time < _commandLookOverrideUntil)
            {
                point = _commandLookOverridePoint;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        public static bool TryGetCommandLookOverride(BotOwner bot, out Vector3 point)
        {
            point = Vector3.zero;
            if (bot == null)
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(bot);
            return followerData?.TryGetCommandLookOverride(out point) == true;
        }

        public static bool TrySetCommandLookOverride(BotOwner bot, Vector3 point, float duration, bool applyImmediateLook = true)
        {
            if (bot == null)
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(bot);
            if (followerData == null)
            {
                return false;
            }

            if (applyImmediateLook)
            {
                bot.Steering.LookToPoint(point);
            }

            followerData.SetCommandLookOverride(point, duration);
            return true;
        }

        public static bool TryApplyCommandLookOverride(BotOwner bot)
        {
            if (!TryGetCommandLookOverride(bot, out Vector3 point))
            {
                return false;
            }

            bot.Steering.LookToPoint(point);
            return true;
        }

        public bool TryGetActiveCommand(out FollowerCommandType command, out Vector3 target)
        {
            if (IsHealingOrHealDecision())
            {
                if (_activeCommand == FollowerCommandType.PushEnemy)
                {
                    command = FollowerCommandType.None;
                    target = Vector3.zero;
                    return false;
                }

                if (_activeCommand == FollowerCommandType.CombatComeToBossCover ||
                    _activeCommand == FollowerCommandType.CombatMoveToPointTactical)
                {
                    if (Time.time > _commandUntilTime)
                    {
                        ClearCommand("TryGetActiveCommand:timeout");
                        command = FollowerCommandType.None;
                        target = Vector3.zero;
                        return false;
                    }

                    command = _activeCommand;
                    target = _commandTarget;
                    return true;
                }

                if (_activeCommand != FollowerCommandType.None)
                {
                    ClearCommand("TryGetActiveCommand:healing");
                }
            }

            if (_activeCommand != FollowerCommandType.None && Time.time > _commandUntilTime)
            {
                ClearCommand("TryGetActiveCommand:timeout");
            }

            command = _activeCommand;
            target = _commandTarget;
            return command != FollowerCommandType.None;
        }

        public bool ClearQueuedPushEnemyWhileHealing(string reason)
        {
            if (_activeCommand != FollowerCommandType.PushEnemy || !IsHealingOrHealDecision())
            {
                return false;
            }

            ClearCommand(reason);
            return true;
        }

        private bool IsHealingOrHealDecision()
        {
            if (_bot == null)
            {
                return false;
            }

            bool isUsingHeal = _bot.Medecine?.FirstAid?.Using == true ||
                               _bot.Medecine?.SurgicalKit?.Using == true;
            BotLogicDecision currentDecision = _bot.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return isUsingHeal ||
                   currentDecision == BotLogicDecision.heal ||
                   currentDecision == BotLogicDecision.healStimulators;
        }

        public bool TryPeekActiveCommand(out FollowerCommandType command, out Vector3 target, out float untilTime)
        {
            command = _activeCommand;
            target = _commandTarget;
            untilTime = _commandUntilTime;
            return command != FollowerCommandType.None;
        }

        public int MoveToPointIssueSequence => _moveToPointIssueSequence;

        public int PushEnemyIssueSequence => _pushEnemyIssueSequence;

        public bool TightRegroupRequested =>
            _activeCommand == FollowerCommandType.RegroupNearBoss && _tightRegroupRequested;

        public bool SuppressEnemyRequiresLauncher =>
            _activeCommand == FollowerCommandType.SuppressEnemy && _suppressEnemyRequiresLauncher;

        public bool SuppressEnemyForceWeapon =>
            _activeCommand == FollowerCommandType.SuppressEnemy && _suppressEnemyForceWeapon;

        public bool SuppressEnemyUseAutomaticSecondary =>
            _activeCommand == FollowerCommandType.SuppressEnemy && _suppressEnemyUseAutomaticSecondary;

        public void RequestOrderedPushCancel(string reason)
        {
            _orderedPushCancelRequested = true;
            _orderedPushCancelReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            if (_activeCommand == FollowerCommandType.PushEnemy)
            {
                ClearCommand($"OrderedPushCancel:{_orderedPushCancelReason}");
            }
        }

        public bool HasOrderedPushCancelRequest => _orderedPushCancelRequested;

        public bool TryConsumeOrderedPushCancelRequest(out string reason)
        {
            reason = _orderedPushCancelReason ?? "unspecified";
            if (!_orderedPushCancelRequested)
            {
                return false;
            }

            _orderedPushCancelRequested = false;
            _orderedPushCancelReason = null;
            return true;
        }

        public bool HasOrderedPushTargetLock =>
            _orderedPushTargetLockActive &&
            !string.IsNullOrEmpty(_orderedPushTargetProfileId);

        public bool TryGetOrderedPushTargetLock(out string profileId, out Vector3 lastKnownPosition)
        {
            profileId = _orderedPushTargetProfileId ?? string.Empty;
            lastKnownPosition = _orderedPushTargetPosition;
            return HasOrderedPushTargetLock;
        }

        public void ActivateOrderedPushTargetLock(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                ClearOrderedPushTargetLock("activateMissingTarget");
                return;
            }

            _orderedPushTargetLockActive = true;
            _orderedPushTargetProfileId = goalEnemy.ProfileId;
            _orderedPushTargetPosition = GetOrderedPushTargetPosition(goalEnemy);
            FollowerCombatTargetCommitments.SetMission(
                _bot,
                goalEnemy,
                FollowerCombatTargetMissionKind.OrderedPush,
                "activateOrderedPushTargetLock");
        }

        public void RefreshOrderedPushTargetLock(EnemyInfo? goalEnemy)
        {
            if (!HasOrderedPushTargetLock ||
                goalEnemy == null ||
                !string.Equals(goalEnemy.ProfileId, _orderedPushTargetProfileId, StringComparison.Ordinal))
            {
                return;
            }

            Vector3 position = GetOrderedPushTargetPosition(goalEnemy);
            if (IsFinite(position) && position.sqrMagnitude > 0.01f)
            {
                _orderedPushTargetPosition = position;
            }

            FollowerCombatTargetCommitments.RefreshMission(
                _bot,
                goalEnemy,
                FollowerCombatTargetMissionKind.OrderedPush,
                "refreshOrderedPushTargetLock");
        }

        public void RefreshOrderedPushTargetLock(Player target)
        {
            if (!HasOrderedPushTargetLock ||
                target == null ||
                !string.Equals(target.ProfileId, _orderedPushTargetProfileId, StringComparison.Ordinal))
            {
                return;
            }

            if (IsFinite(target.Position) && target.Position.sqrMagnitude > 0.01f)
            {
                _orderedPushTargetPosition = target.Position;
            }

            FollowerCombatTargetCommitments.RefreshMission(
                _bot,
                target,
                FollowerCombatTargetMissionKind.OrderedPush,
                "refreshOrderedPushTargetLock");
        }

        public void ClearOrderedPushTargetLock(string reason = "unspecified")
        {
            _orderedPushTargetLockActive = false;
            _orderedPushTargetProfileId = null;
            _orderedPushTargetPosition = Vector3.zero;
            FollowerCombatTargetCommitments.ClearMission(
                _bot,
                FollowerCombatTargetMissionKind.OrderedPush,
                reason);
        }

        private static Vector3 GetOrderedPushTargetPosition(EnemyInfo goalEnemy)
        {
            if (IsFinite(goalEnemy.CurrPosition) && goalEnemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return goalEnemy.CurrPosition;
            }

            if (IsFinite(goalEnemy.EnemyLastPositionReal) && goalEnemy.EnemyLastPositionReal.sqrMagnitude > 0.01f)
            {
                return goalEnemy.EnemyLastPositionReal;
            }

            if (IsFinite(goalEnemy.PersonalLastPos) && goalEnemy.PersonalLastPos.sqrMagnitude > 0.01f)
            {
                return goalEnemy.PersonalLastPos;
            }

            return goalEnemy.Person?.Position ?? Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void SetCombatTacticFromString(string? tactic)
        {
            _combatTactic = ParseCombatTactic(tactic);
        }

        public static FollowerCombatTactic ParseCombatTactic(string? tactic)
        {
            if (string.IsNullOrWhiteSpace(tactic))
            {
                return FollowerCombatTactic.Balanced;
            }

            switch (tactic.Trim().ToLowerInvariant())
            {
                case "marksman":
                    return FollowerCombatTactic.Marksman;

                case "guard":
                case "protector":
                case "holder":
                case "assist":
                case "support":
                    return FollowerCombatTactic.Protector;

                case "default":
                case "rifleman":
                case "balanced":
                case "pusher":
                default:
                    return FollowerCombatTactic.Balanced;
            }
        }

        public bool HasKnownEnemy()
        {
            BotOwner owner = _bot;
            if (owner?.Memory == null || owner.Memory.HaveEnemy != true)
            {
                _knownEnemyProfileId = null;
                _knownEnemySince = 0f;
                _knownEnemyLatched = false;
                return false;
            }

            EnemyInfo goalEnemy = owner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                _knownEnemyProfileId = null;
                _knownEnemySince = 0f;
                _knownEnemyLatched = false;
                return false;
            }

            string? goalProfileId = goalEnemy.ProfileId;
            if (string.IsNullOrEmpty(goalProfileId))
            {
                _knownEnemyProfileId = null;
                _knownEnemySince = 0f;
                _knownEnemyLatched = false;
                return false;
            }

            if (!string.Equals(_knownEnemyProfileId, goalProfileId, StringComparison.Ordinal))
            {
                _knownEnemyProfileId = goalProfileId;
                _knownEnemySince = Time.time;
                _knownEnemyLatched = false;
            }

            if (_knownEnemyLatched)
            {
                return true;
            }

            if (HasLineOfSightToGoalEnemy(owner, goalEnemy))
            {
                _knownEnemyLatched = true;
                return true;
            }

            return Time.time - _knownEnemySince >= KnownEnemyAcquireHoldSeconds;
        }

        public bool IsBotActivelyEngaging(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || _bot?.Memory?.GoalEnemy == null)
            {
                return false;
            }

            EnemyInfo goalEnemy = _bot.Memory.GoalEnemy;
            return !string.IsNullOrEmpty(goalEnemy.ProfileId) &&
                   string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal) &&
                   ((goalEnemy.IsVisible && goalEnemy.CanShoot) ||
                    _bot.Memory.IsUnderFire ||
                    _bot.DogFight?.DogFightState > BotDogFightStatus.none);
        }

        private static bool HasLineOfSightToGoalEnemy(BotOwner owner, EnemyInfo goalEnemy)
        {
            try
            {
                if (owner?.GetPlayer?.PlayerBones?.WeaponRoot == null)
                {
                    return false;
                }

                Player enemyPlayer = goalEnemy.Person as Player;
                if ((enemyPlayer == null || enemyPlayer.HealthController?.IsAlive != true) && !string.IsNullOrEmpty(goalEnemy.ProfileId))
                {
                    enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                }

                if (enemyPlayer?.MainParts == null || enemyPlayer.HealthController?.IsAlive != true)
                {
                    return false;
                }

                bool hasHead = enemyPlayer.MainParts.TryGetValue(BodyPartType.head, out var headPart);
                bool hasBody = enemyPlayer.MainParts.TryGetValue(BodyPartType.body, out var bodyPart);
                if (!hasHead && !hasBody)
                {
                    return false;
                }

                Vector3 firePos = owner.GetPlayer.PlayerBones.WeaponRoot.position;
                LayerMask mask = owner.LookSensor?.Mask ?? LayersMaskController.HighPolyWithTerrainMask;

                if (headPart != null &&
                    Utils.Utils.CanShootToTarget(new ShootToPoint(headPart.Position, 1f), firePos, mask, false))
                {
                    return true;
                }

                if (bodyPart != null &&
                    Utils.Utils.CanShootToTarget(new ShootToPoint(bodyPart.Position, 1f), firePos, mask, false))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public bool IsReadyForPatrolAfterCombat()
        {
            BotOwner owner = _bot;
            if (!IsSafelyOutOfCombat(owner))
            {
                return false;
            }

            if (!pitFireTeam.UseSainFollowerCombat)
            {
                ClearTemporaryCombatAggressionOverrideAfterCombatCooldown();
                return true;
            }

            bool bridgeReady = false;
            bool bridgeAvailable = false;

            try
            {
                bridgeAvailable = SainAddonBridge.HasRuntimeCallbacks;
                if (SainAddonBridge.TryIsReadyForPatrolAfterCombat(owner, out bool ready))
                {
                    bridgeReady = ready;
                }
            }
            catch
            {
                // Bridge errors are handled below with explicit logging and strict fallback behavior.
            }

            if (bridgeReady)
            {
                ClearTemporaryCombatAggressionOverrideAfterCombatCooldown();
                return true;
            }

            if (!bridgeAvailable && !_sainAddonPatrolBridgeErrorLogged)
            {
                _sainAddonPatrolBridgeErrorLogged = true;
                Modules.Logger.LogError("[SAIN] Patrol readiness bridge is unavailable. Ensure pitFireTeam SAIN addon is present and loaded.");
            }

            // With fixed SAIN/addon target, fail closed if bridge is unavailable.
            return false;
        }

        public bool HasCombatHandoffSignal()
        {
            return !IsSafelyOutOfCombat(_bot);
        }

        public void ClearVanillaRequestState(Player requester = null, string reason = "unspecified")
        {
            try
            {
                if (_bot == null)
                {
                    return;
                }

                if (_bot.BotRequestController?.CurRequest != null)
                {
                    _bot.BotRequestController.CurRequest.Complete();
                    _bot.BotRequestController.CurRequest = null;
                }

                Player requesterPlayer = requester ?? GetBoss()?.realPlayer;
                if (requesterPlayer != null)
                {
                    _bot.BotsGroup?.RequestsController?.RemoveAllRequestByRequester(requesterPlayer);
                }

                _bot.BotRequestController?.ResetTimer();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Follower Command] Failed to clear vanilla request state for {_bot?.Profile?.Nickname} reason={reason}");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool IsSafelyOutOfCombat(BotOwner owner)
        {
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active)
            {
                return false;
            }

            if (HasActiveCombatSignal(owner))
            {
                return false;
            }

            if (HasRecentGroupCombatSignal(owner))
            {
                return false;
            }

            if (HasSquadmateCombatSignal(owner))
            {
                return false;
            }

            return true;
        }

        private static bool HasActiveCombatSignal(BotOwner owner)
        {
            if (owner?.Memory == null)
            {
                return false;
            }

            EnemyInfo goalEnemy = owner.Memory.GoalEnemy;
            if (owner.Memory.HaveEnemy && IsEnemyInfoAlive(goalEnemy))
            {
                return true;
            }

            if (owner.Memory.IsUnderFire && Time.time - owner.Memory.LastTimeHit <= 2f)
            {
                return true;
            }

            var infos = owner.EnemiesController?.EnemyInfos;
            if (infos == null)
            {
                return false;
            }

            foreach (var kv in infos)
            {
                EnemyInfo info = kv.Value;
                if (info == null || !IsEnemyInfoAlive(info))
                {
                    continue;
                }

                if (info.IsVisible ||
                    info.CanShoot ||
                    Time.time - info.PersonalLastSeenTime <= TemporaryCombatAggressionRecentEnemySeconds)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRecentGroupCombatSignal(BotOwner owner)
        {
            if (owner?.BotsGroup?.Enemies == null ||
                owner.BotsGroup.EnemyLastSeenTimeReal <= 0f ||
                Time.time - owner.BotsGroup.EnemyLastSeenTimeReal > TemporaryCombatAggressionGroupEnemySeconds)
            {
                return false;
            }

            foreach (IPlayer enemy in owner.BotsGroup.Enemies.Keys)
            {
                if (IsLiveEnemyPlayer(enemy))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSquadmateCombatSignal(BotOwner owner)
        {
            BotFollowerPlayer self = BossPlayers.Instance?.GetFollower(owner);
            pitAIBossPlayer boss = self?.GetBoss();
            string bossProfileId = boss?.realPlayer?.ProfileId;
            if (string.IsNullOrEmpty(bossProfileId))
            {
                return false;
            }

            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowersByBoss(bossProfileId))
            {
                BotOwner squadmate = follower?.GetBot();
                if (squadmate == null ||
                    squadmate == owner ||
                    squadmate.IsDead ||
                    squadmate.BotState != EBotState.Active ||
                    squadmate.GetPlayer?.HealthController?.IsAlive != true)
                {
                    continue;
                }

                if (HasActiveCombatSignal(squadmate) || HasRecentGroupCombatSignal(squadmate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEnemyInfoAlive(EnemyInfo info)
        {
            if (info == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(info.ProfileId))
            {
                Player alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(info.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return info.Person?.HealthController?.IsAlive == true;
        }

        private static bool IsLiveEnemyPlayer(IPlayer enemy)
        {
            if (enemy == null)
            {
                return false;
            }

            Player player = enemy as Player;
            if (player?.HealthController?.IsAlive == true)
            {
                return true;
            }

            BotOwner bot = enemy.AIData?.BotOwner;
            return bot != null &&
                   !bot.IsDead &&
                   bot.BotState == EBotState.Active &&
                   bot.GetPlayer?.HealthController?.IsAlive == true;
        }

        public void ClearCommand(string reason = "unspecified")
        {
            if (_committedLootCommand != FollowerCommandType.None &&
                ShouldIgnoreCommittedLootClear(reason))
            {
                return;
            }

            FollowerCommandType previousCommand = _activeCommand;
            Vector3 previousTarget = _commandTarget;
            float previousUntilTime = _commandUntilTime;

            if (_bot != null)
            {
                if (_activeCommand == FollowerCommandType.TakeLootItem)
                {
                    InteractableObjects.RemoveTaker(_bot);
                }

                if (_activeCommand == FollowerCommandType.TakeBodyGear)
                {
                    InteractableObjects.RemoveBodyLootTaker(_bot);
                }

                if (_activeCommand == FollowerCommandType.TakeContainerLoot)
                {
                    InteractableObjects.RemoveContainerLootTaker(_bot);
                }

                if (_activeCommand == FollowerCommandType.OpenDoor)
                {
                    InteractableObjects.RemoveOpener(_bot);
                }
            }
            _activeCommand = FollowerCommandType.None;
            _commandTarget = Vector3.zero;
            _commandUntilTime = 0f;
            _tightRegroupRequested = false;
            _suppressEnemyRequiresLauncher = false;
            _suppressEnemyForceWeapon = false;
            _suppressEnemyUseAutomaticSecondary = false;
            _holdPositionShouldCrouch = true;
            _resumeHoldAfterComeCloser = false;
            _resumeHoldAfterTakeLoot = false;
            _resumeHoldAfterTakeLootCrouch = false;
            ResetPostLootMoveState();
            _committedLootCommand = FollowerCommandType.None;
            ClearCommandLookOverride();

            BattleRecorder.RecordCommandCleared(this, previousCommand, previousTarget, previousUntilTime, reason);
        }

        private static bool ShouldIgnoreCommittedLootClear(string reason)
        {
            return reason.StartsWith("ClearFollowerCommands", StringComparison.Ordinal) ||
                   reason.StartsWith("OnYourOwn:", StringComparison.Ordinal) ||
                   reason.StartsWith("Attention:", StringComparison.Ordinal);
        }

        private void ResetPickupFollowerRuntimeState()
        {
            if (_bot == null)
            {
                return;
            }

            try
            {
                _knownEnemyProfileId = null;
                _knownEnemySince = 0f;
                _knownEnemyLatched = false;

                ClearCommand("PickupReset");
                _bot.Memory.IsPeace = false;
                using (FollowerGoalEnemyTracker.Begin("BotFollowerPlayer.ResetPickupFollowerRuntimeState", "pickupReset"))
                {
                    _bot.Memory.GoalEnemy = null;
                }
                _bot.Memory.LastEnemy = null;
                _bot.Memory?.GoalTarget?.Clear();

                _bot.StopMove();
                _bot.Mover.Pause = false;
                if (_bot.Mover.Sprinting)
                {
                    _bot.Mover.Sprint(false, false);
                }

                if (pitFireTeam.UseSainFollowerCombat)
                {
                    SainAddonBridge.TryResetDecisionState(_bot);
                    SainAddonBridge.TryForceReleaseFollowerCombatState(_bot);
                }

                Modules.Logger.LogInfo($"[Follower Pickup Reset] follower={_bot.Profile?.Nickname ?? _bot.name}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Follower Pickup Reset] failed follower={_bot?.Profile?.Nickname ?? _bot?.name}");
                Modules.Logger.LogError(ex);
            }
        }

        private void ResetBrainForFollower(BaseBrain baseBrain)
        {
            if (baseBrain == null) return;

            // Deactivate current layer if possible
            var currentLayer = baseBrain.CurLayerInfo;
            if (currentLayer != null && currentLayer.IsActive)
            {
                string name = currentLayer.Name();
                _bot.Brain.Agent.Deactivate(name);
                currentLayer.IsActive = false;
            }

            // Remove peaceful/patrol style layers
            RemoveBrainLayer(baseBrain, layer =>
            {
                if (layer == null) return false;
                string name = layer.Name();
                return name.Contains("Utility") || name.Contains("Patrol") || name == "PtrlBirdEye";
            });

            // Force decision to end so new layer can take over
            if (currentLayer is BaseLogicLayer simpleLayer)
            {
                simpleLayer.CalcActionNextFrame();
            }
            else if (currentLayer is BaseLogicLayerSimple baseLayer)
            {
                baseLayer._nextFrameDropAction = true;
            }
        }

        private void RemoveBrainLayer(BaseBrain baseBrain, Func<AICoreLayer<BotLogicDecision>, bool> shouldRemove)
        {
            try
            {
                if (baseBrain == null) return;

                var dictField = AccessTools.Field(typeof(AICoreStrategy<BotLogicDecision>), "_layers");
                var layers = dictField?.GetValue(baseBrain) as Dictionary<int, AICoreLayer<BotLogicDecision>>;
                if (layers == null) return;

                var toRemove = new List<int>();
                foreach (var kvp in layers)
                {
                    if (shouldRemove(kvp.Value))
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                var brainHelpersType = AccessTools.TypeByName("DrakiaXYZ.BigBrain.Internal.BrainHelpers");
                var removeLayerForBot = brainHelpersType != null
                    ? AccessTools.Method(brainHelpersType, "RemoveLayerForBot", new[] { typeof(BotOwner), typeof(string) })
                    : null;

                foreach (var index in toRemove)
                {
                    if (layers.TryGetValue(index, out var layer) && layer != null)
                    {
                        string name = layer.Name();
                        if (removeLayerForBot != null)
                        {
                            removeLayerForBot.Invoke(null, new object[] { _bot, name });
                        }
                        else
                        {
                            _bot.Brain.Agent.Deactivate(name);
                            layer.IsActive = false;
                            baseBrain._activeLayers.Remove(layer);
                            layers.Remove(index);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Error while removing brain layer");
                Modules.Logger.LogError(ex);
            }
        }
        /** Ensure vanilla patrolling is not conflicting with our layers */
        private void HookPeaceChange()
        {
            if (_peaceChangeHooked) return;
            if (_bot?.Memory == null) return;
            _bot.Memory.OnPeaceChange += OnPeaceChange;
            _peaceChangeHooked = true;
        }

        private void UnhookPeaceChange()
        {
            if (!_peaceChangeHooked) return;
            if (_bot?.Memory == null) return;
            _bot.Memory.OnPeaceChange -= OnPeaceChange;
            _peaceChangeHooked = false;
        }

        private void HookManualUpdate()
        {
            if (_manualUpdateHooked) return;
            if (_bot?.ProfileId == null) return;
            BotOwnerManualUpdatePatch.BotOwnerUpdate[_bot.ProfileId] = OnBotOwnerManualUpdate;
            _manualUpdateHooked = true;
        }

        private void UnhookManualUpdate()
        {
            if (!_manualUpdateHooked) return;
            if (_bot?.ProfileId != null &&
                BotOwnerManualUpdatePatch.BotOwnerUpdate.TryGetValue(_bot.ProfileId, out var cb) &&
                cb == OnBotOwnerManualUpdate)
            {
                BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(_bot.ProfileId);
            }
            _manualUpdateHooked = false;
        }

        private void OnPeaceChange(bool isPeace)
        {
            _bot?.PatrollingData?.Pause();
        }

        private void OnBotOwnerManualUpdate(BotOwner owner)
        {
            try
            {
                if (owner == null || owner != _bot) return;

                Utils.FollowerMedical.UpdateMedicalHandsWatchdog(owner);
                UpdateTemporaryCombatAggressionClearDelay();
                TryPromoteReadyLootedWeapon(owner);

                if (_teleportGraceUntil > Time.time)
                {
                    owner.Mover?.Stop();
                    owner.StopMove();
                    owner.GoToSomePointData?.SetPoint(_teleportGraceTarget);
                    owner.PatrollingData?.Pause();

                    if ((owner.Position - _teleportGraceTarget).sqrMagnitude > TeleportReteleportDistance * TeleportReteleportDistance)
                    {
                        owner.GetPlayer?.Teleport(_teleportGraceTarget);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Exception in BotFollowerPlayer manual update combat debounce");
                Modules.Logger.LogError(ex);
            }
        }

        private void TryPromoteReadyLootedWeapon(BotOwner owner)
        {
            if (_lootedWeaponPromotionInProgress ||
                Time.time < _nextLootedWeaponPromotionCheckAt)
            {
                return;
            }

            _nextLootedWeaponPromotionCheckAt = Time.time + LootedWeaponPromotionCheckInterval;
            if (!pitFireTeam.IsLootGearSwappingEnabled() ||
                _backpackInspectionActive ||
                owner == null ||
                owner.IsDead ||
                owner.BotState != EBotState.Active ||
                owner.Memory?.HaveEnemy == true ||
                _teleportGraceUntil > Time.time ||
                IsLootOrPickupCommandActive())
            {
                return;
            }

            InventoryController inventory = owner.GetPlayer?.InventoryController;
            InventoryEquipment equipment = inventory?.Inventory?.Equipment;
            Slot primarySlot = equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon);
            if (inventory == null ||
                primarySlot == null ||
                primarySlot.Deleted ||
                primarySlot.ContainedItem != null)
            {
                return;
            }

            BotWeaponManager weaponManager = owner.WeaponManager;
            BotWeaponSelector selector = weaponManager?.Selector;
            Weapon activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            if (weaponManager == null ||
                selector == null ||
                selector.IsChanging ||
                weaponManager.Reload?.Reloading == true ||
                !weaponManager.CanChangeHands())
            {
                return;
            }

            if (!TryFindReadyLootedWeaponPromotionCandidate(
                    owner,
                    inventory,
                    equipment,
                    activeWeapon,
                    out Weapon weapon,
                    out WeaponPrimaryReadinessSnapshot readiness,
                    out string evaluation))
            {
                return;
            }

            Error locationError;
            ItemAddress primaryAddress = primarySlot.FindLocationForItem(weapon, out locationError);
            if (primaryAddress == null)
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{owner.Profile?.Nickname ?? owner.ProfileId ?? "unknown"}' " +
                    $"weapon={weapon.TemplateId} evaluation={evaluation} " +
                    $"destination={DescribePromotionSource(evaluation)} decisionReason=noPrimaryAddress {readiness.ToDiagnosticString()}");
                return;
            }

            Diz.LanguageExtensions.OperationResult<EFT.InventoryLogic.MoveResult> moveResult =
                EFT.InventoryLogic.ItemManipulator.Move(weapon, primaryAddress, inventory, true);
            if (moveResult.Failed ||
                moveResult.Value.ItemsDestroyRequired ||
                !inventory.CanExecute(moveResult.Value))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand][Readiness] follower='{owner.Profile?.Nickname ?? owner.ProfileId ?? "unknown"}' " +
                    $"weapon={weapon.TemplateId} evaluation={evaluation} " +
                    $"destination={DescribePromotionSource(evaluation)} decisionReason=moveRejected {readiness.ToDiagnosticString()}");
                return;
            }

            _lootedWeaponPromotionInProgress = true;
            Modules.Logger.LogInfo(
                $"[LootCommand][Readiness] follower='{owner.Profile?.Nickname ?? owner.ProfileId ?? "unknown"}' " +
                $"weapon={weapon.TemplateId} evaluation={evaluation} " +
                $"destination=FirstPrimaryWeapon decisionReason=ready {readiness.ToDiagnosticString()}");
            inventory.RunNetworkTransaction(
                moveResult.Value,
                new Callback(result => CompleteLootedWeaponPromotion(owner, weapon, evaluation, result)));
        }

        private bool TryFindReadyLootedWeaponPromotionCandidate(
            BotOwner owner,
            InventoryController inventory,
            InventoryEquipment equipment,
            Weapon activeWeapon,
            out Weapon weapon,
            out WeaponPrimaryReadinessSnapshot readiness,
            out string evaluation)
        {
            weapon = equipment?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
            readiness = null;
            evaluation = "secondaryPromotion";
            if (IsTrackedPrimaryCandidate(owner, weapon))
            {
                readiness = FollowerWeaponPrimaryReadiness.EvaluateActual(
                    inventory,
                    weapon,
                    ammo => !InteractableObjects.IsStrictCargoItem(owner, ammo));
                if (IsPrimaryReadyWithReloadLandingSpace(equipment, weapon, readiness))
                {
                    // The tracked support weapon keeps priority. Wait until it leaves the hands
                    // rather than promoting an unrelated cargo weapon around it.
                    return !IsSameItem(activeWeapon, weapon);
                }
            }

            Item backpack = equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (backpack == null)
            {
                weapon = null;
                readiness = null;
                return false;
            }

            List<(Weapon Weapon, WeaponPrimaryReadinessSnapshot Readiness)> readyCargo = backpack
                .GetAllItems()
                .OfType<Weapon>()
                .Where(candidate => IsTrackedPrimaryCandidate(owner, candidate))
                .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(candidate => (
                    Weapon: candidate,
                    Readiness: FollowerWeaponPrimaryReadiness.EvaluateActual(
                        inventory,
                        candidate,
                        ammo => !InteractableObjects.IsStrictCargoItem(owner, ammo))))
                .Where(candidate => IsPrimaryReadyWithReloadLandingSpace(
                    equipment,
                    candidate.Weapon,
                    candidate.Readiness))
                .Take(2)
                .ToList();

            // Weapon comparison is deliberately deferred. Promote only when inventory state
            // identifies one unambiguous ready cargo weapon.
            if (readyCargo.Count != 1)
            {
                weapon = null;
                readiness = null;
                return false;
            }

            weapon = readyCargo[0].Weapon;
            readiness = readyCargo[0].Readiness;
            evaluation = "cargoFastAccessPromotion";
            return true;
        }

        private static bool IsTrackedPrimaryCandidate(BotOwner owner, Weapon weapon)
        {
            return weapon != null &&
                   !string.IsNullOrEmpty(weapon.Id) &&
                   weapon.GetItemComponent<KnifeComponent>() == null &&
                   weapon is not EFT.InventoryLogic.Pistol &&
                   weapon is not EFT.InventoryLogic.Revolver &&
                   InteractableObjects.IsLootedWeapon(owner, weapon) &&
                   !InteractableObjects.IsStrictCargoItem(owner, weapon);
        }

        private static bool IsPrimaryReadyWithReloadLandingSpace(
            InventoryEquipment equipment,
            Weapon weapon,
            WeaponPrimaryReadinessSnapshot readiness)
        {
            if (readiness == null || !readiness.PrimaryReady || readiness.RequiresMagazineLoad)
            {
                return false;
            }

            return readiness.InsertedContribution >= readiness.Threshold ||
                   FollowerWeaponPrimaryReadiness.HasInsertedMagazineReloadLandingSpace(equipment, weapon);
        }

        private static string DescribePromotionSource(string evaluation)
        {
            return string.Equals(evaluation, "cargoFastAccessPromotion", StringComparison.Ordinal)
                ? "BackpackCargo"
                : "SecondPrimaryWeapon";
        }

        private void CompleteLootedWeaponPromotion(
            BotOwner owner,
            Weapon weapon,
            string evaluation,
            IResult result)
        {
            _lootedWeaponPromotionInProgress = false;
            _nextLootedWeaponPromotionCheckAt = Time.time + LootedWeaponPromotionCheckInterval;

            Weapon slottedPrimary = owner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            if (result?.Succeed != true && !IsSameItem(slottedPrimary, weapon))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Looted weapon promotion failed for " +
                    $"'{owner?.Profile?.Nickname ?? owner?.ProfileId ?? "unknown"}': " +
                    $"{weapon?.TemplateId ?? "unknown"}:{evaluation}");
                return;
            }

            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active)
            {
                return;
            }

            owner?.WeaponManager?.UpdateWeaponsList();
            FollowerLootedPrimaryWeaponBinding.RebindAndSelect(
                owner,
                weapon,
                evaluation);
        }

        private static bool IsSameItem(Item first, Item second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            return !string.IsNullOrEmpty(first?.Id) &&
                   !string.IsNullOrEmpty(second?.Id) &&
                   string.Equals(first.Id, second.Id, StringComparison.Ordinal);
        }

        private static object GetSainBot(BotOwner owner)
        {
            if (owner == null) return null;

            _sainEnableType ??=
                AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");
            if (_sainEnableType == null) return null;

            if (_getSainByBotOwnerMethod == null && _getSainByProfileMethod == null)
            {
                foreach (MethodInfo method in _sainEnableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name != "GetSAIN") continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                    {
                        _getSainByBotOwnerMethod = method;
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                    {
                        _getSainByProfileMethod = method;
                    }
                }
            }

            if (_getSainByBotOwnerMethod != null)
            {
                return _getSainByBotOwnerMethod.Invoke(null, new object[] { owner });
            }

            if (_getSainByProfileMethod != null && !string.IsNullOrEmpty(owner.ProfileId))
            {
                object[] args = { owner.ProfileId, null };
                bool found = _getSainByProfileMethod.Invoke(null, args) is bool result && result;
                if (found)
                {
                    return args[1];
                }
            }

            return null;
        }

        private void EnsureSainBossAndFollowersFriendly()
        {
            if (!pitFireTeam.UseSainFollowerCombat) return;
            if (_bot == null || _player?.realPlayer == null) return;

            try
            {
                List<IPlayer> friendlyPlayers = [_player.realPlayer];

                if (_player.Followers != null)
                {
                    friendlyPlayers.AddRange(_player.Followers);
                }

                HashSet<string> protectedIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (IPlayer friend in friendlyPlayers)
                {
                    if (friend == null) continue;
                    if (string.IsNullOrEmpty(friend.ProfileId)) continue;
                    if (friend.ProfileId == _bot.ProfileId) continue;

                    protectedIds.Add(friend.ProfileId);
                }

                if (protectedIds.Count == 0) return;

                object sainBot = GetSainBot(_bot);
                if (sainBot == null) return;

                object? enemyController =
                    AccessTools.Property(sainBot.GetType(), "EnemyController")?.GetValue(sainBot) ??
                    AccessTools.Field(sainBot.GetType(), "EnemyController")?.GetValue(sainBot);
                if (enemyController == null) return;

                Type enemyControllerType = enemyController.GetType();
                MethodInfo removeEnemyMethod = enemyControllerType.GetMethod("RemoveEnemy", new[] { typeof(string) });
                MethodInfo clearEnemyMethod = enemyControllerType.GetMethod("ClearEnemy", Type.EmptyTypes);

                if (removeEnemyMethod != null)
                {
                    foreach (string profileId in protectedIds)
                    {
                        removeEnemyMethod.Invoke(enemyController, new object[] { profileId });
                    }
                }

                object? goalEnemy =
                    AccessTools.Property(enemyControllerType, "GoalEnemy")?.GetValue(enemyController) ??
                    AccessTools.Field(enemyControllerType, "_goalEnemy")?.GetValue(enemyController);
                string? goalEnemyId = goalEnemy != null
                    ? AccessTools.Property(goalEnemy.GetType(), "EnemyProfileId")?.GetValue(goalEnemy) as string
                    : null;
                if (!string.IsNullOrEmpty(goalEnemyId) && protectedIds.Contains(goalEnemyId))
                {
                    clearEnemyMethod?.Invoke(enemyController, Array.Empty<object>());
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] failed to sync friendly enemy state for recruited follower={_bot?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
            }
        }

        private void OnBeingHit(EFT.Ballistics.DamageInfo arg1, EBodyPart arg2, float arg3)
        {
            if (_activeCommand == FollowerCommandType.PushEnemy)
            {
                return;
            }

            ClearCommand("OnBeingHit");
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F1},{v.y:F1},{v.z:F1})";
        }

        private void ForceEndCurrentDecision(BotOwner bot)
        {
            if (bot == null) return;

            var brain = bot?.Brain?.BaseBrain;
            var agent = bot?.Brain?.Agent;
            if (brain == null || agent == null) return;

            string layer = brain.CurLayerInfo?.Name() ?? "<null>";
            string node = agent.GetActiveNodeName() ?? "<null>";

            EnsureFollowerLayerPresent(brain);

            if (bot.BotRequestController?.CurRequest != null)
            {
                bot.BotRequestController.CurRequest.Complete();
                bot.BotRequestController.CurRequest = null;
            }

            bot.PatrollingData?.Pause();

            if (brain.CurLayerInfo is BaseLogicLayer simpleLayer)
            {
                simpleLayer.CalcActionNextFrame(BotLogicDecision.holdPosition);
            }
            else if (brain.CurLayerInfo is BaseLogicLayerSimple baseLayer)
            {
                baseLayer._nextFrameDropAction = true;
            }

            bot.StopMove();
            bot.GoToSomePointData?.SetPoint(bot.Position);

            ClearActiveLayerPointers(brain, agent);
            brain.CalcActionNextFrame();
        }

        private void RemoveConflictingLayers(BaseBrain brain, AICoreAgent<BotLogicDecision> agent)
        {
            if (brain == null || agent == null) return;

            string[] blockedExact =
            {
                "Exfiltration",
                "SAIN : Extract",
                "SAIN : Squad Layer",
                "Follow Player",
                "Looting",
                "OrbitBrainLayer",
                "BotMind_Looting",
                "BotMind_Questing"
            };

            var toRemove = new List<int>();
            foreach (var kvp in brain._layers)
            {
                var layer = kvp.Value;
                if (layer == null) continue;

                string name = layer.Name();
                if (string.IsNullOrEmpty(name)) continue;

                bool exactMatch = blockedExact.Contains(name);
                bool orbitLayerMatch = IsNamedLayer(layer, name, "OrbitBrainLayer");
                bool containsMatch =
                    name.IndexOf("Exfil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Extract", StringComparison.OrdinalIgnoreCase) >= 0;

                if (exactMatch || orbitLayerMatch || containsMatch)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int index in toRemove)
            {
                if (!brain._layers.TryGetValue(index, out var layer) || layer == null) continue;

                agent.Deactivate(layer.Name());
                brain.DeactivateLayer(index);
                layer.IsActive = false;

                brain._activeLayers.Remove(layer);
                //brain._layers.Remove(index);
            }
        }

        private static bool IsNamedLayer(AICoreLayer<BotLogicDecision> layer, string layerName, string expectedName)
        {
            if (string.Equals(layerName, expectedName, StringComparison.OrdinalIgnoreCase) ||
                layerName.EndsWith("." + expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Type type = layer.GetType();
            return string.Equals(type.Name, expectedName, StringComparison.OrdinalIgnoreCase) ||
                   (type.FullName?.EndsWith("." + expectedName, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void SuppressThirdPartyPeaceBehaviors()
        {
            SuppressLootingBotsLooting();
        }

        private void SuppressLootingBotsLooting()
        {
            if (!Chainloader.PluginInfos.ContainsKey("me.skwizzy.lootingbots"))
            {
                return;
            }

            try
            {
                Type externalType = Type.GetType("LootingBots.External, skwizzy.LootingBots");
                if (externalType == null)
                {
                    Modules.Logger.LogInfo("LootingBots External type not found while suppressing follower looting");
                    return;
                }

                MethodInfo preventMethod = AccessTools.Method(externalType, "PreventBotFromLooting");
                if (preventMethod == null)
                {
                    Modules.Logger.LogInfo("LootingBots PreventBotFromLooting method not found while suppressing follower looting");
                    return;
                }

                preventMethod.Invoke(null, new object[] { _bot, 36000f });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to suppress LootingBots looting for follower " + _bot?.Profile?.Nickname);
                Modules.Logger.LogError(ex);
            }
        }

        private void EnsureFollowerLayerPresent(BaseBrain brain)
        {
            if (brain == null) return;

            foreach (var kvp in brain._layers)
            {
                if (kvp.Value != null && kvp.Value.Name() == "pitTeam.FollowerPatrol")
                {
                    if (!brain.IsLayerActive(kvp.Value))
                    {
                        brain.ActivateLayer(kvp.Key);
                    }
                    return;
                }
            }

            var customEntry = BrainManager.CustomLayersReadOnly
                .FirstOrDefault(x => x.Value.customLayerType.FullName == "pitTeam.BigBrain.FollowerPatrolLayer");

            if (customEntry.Value == null) return;

            var wrapperType = AccessTools.TypeByName("DrakiaXYZ.BigBrain.Internal.CustomLayerWrapper");
            if (wrapperType == null) return;

            object wrapper = Activator.CreateInstance(
                wrapperType,
                new object[] { customEntry.Value.customLayerType, _bot, customEntry.Value.customLayerPriority }
            );
            if (wrapper == null) return;

            AccessTools
                .Method(typeof(AICoreStrategy<BotLogicDecision>), nameof(AICoreStrategy<BotLogicDecision>.TryAddLayer))
                ?.Invoke(brain, new object[] { customEntry.Key, wrapper, true });
        }

        private void ClearActiveLayerPointers(BaseBrain brain, AICoreAgent<BotLogicDecision> agent)
        {
            try
            {
                var activeLayerField = AccessTools.Field(typeof(AICoreStrategy<BotLogicDecision>), "aICoreLayer");
                activeLayerField?.SetValue(brain, null);

                var agentActiveLayerField = AccessTools.Field(typeof(AICoreAgent<BotLogicDecision>), "_lastActiveLayer");
                agentActiveLayerField?.SetValue(agent, null);

                agent.UsingLayer = string.Empty;

                var lastResultField = AccessTools.Field(typeof(AICoreAgent<BotLogicDecision>), "_lastResult");
                if (lastResultField != null)
                {
                    var defaultResult = default(AICoreActionResult<BotLogicDecision, CoreActionResultParams>);
                    lastResultField.SetValue(agent, defaultResult);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to clear active layer pointers");
                Modules.Logger.LogError(ex);
            }
        }

        private void RefreshSainEnemyListAfterGroupReassign()
        {
            if (!pitFireTeam.UseSainFollowerCombat) return;
            if (_bot == null) return;

            try
            {
                Type sainEnableType =
                    AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                    AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");
                if (sainEnableType == null)
                {
                    Modules.Logger.LogInfo($"[SAIN] EnemyList refresh skipped (SAINEnableClass not found) follower={_bot.Profile?.Nickname}");
                    return;
                }

                object? sainBot = null;

                MethodInfo? getSainByBotOwner = null;
                MethodInfo? getSainByProfile = null;
                foreach (MethodInfo method in sainEnableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name != "GetSAIN") continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                    {
                        getSainByBotOwner = method;
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                    {
                        getSainByProfile = method;
                    }
                }

                if (getSainByBotOwner != null)
                {
                    sainBot = getSainByBotOwner.Invoke(null, new object[] { _bot });
                }
                else
                {
                    if (getSainByProfile != null)
                    {
                        object[] args = { _bot.ProfileId, null };
                        bool found = false;
                        object invokeResult = getSainByProfile.Invoke(null, args);
                        if (invokeResult is bool boolResult)
                        {
                            found = boolResult;
                        }

                        if (found)
                        {
                            sainBot = args[1];
                        }
                    }
                }

                if (sainBot == null)
                {
                    Modules.Logger.LogInfo($"[SAIN] EnemyList refresh skipped (SAIN bot null) follower={_bot.Profile?.Nickname}");
                    return;
                }

                Type sainBotType = sainBot.GetType();
                object enemyController =
                    AccessTools.Property(sainBotType, "EnemyController")?.GetValue(sainBot) ??
                    AccessTools.Field(sainBotType, "EnemyController")?.GetValue(sainBot);
                if (enemyController == null)
                {
                    Modules.Logger.LogInfo($"[SAIN] EnemyList refresh skipped (EnemyController null) follower={_bot.Profile?.Nickname}");
                    return;
                }

                Type enemyControllerType = enemyController.GetType();
                object listController =
                    AccessTools.Field(enemyControllerType, "_listController")?.GetValue(enemyController) ??
                    AccessTools.Field(enemyControllerType, "listController")?.GetValue(enemyController) ??
                    AccessTools.Property(enemyControllerType, "EnemyListController")?.GetValue(enemyController);
                if (listController == null)
                {
                    Modules.Logger.LogInfo($"[SAIN] EnemyList refresh skipped (EnemyListController null) follower={_bot.Profile?.Nickname}");
                    return;
                }

                Type listControllerType = listController.GetType();
                MethodInfo dispose = AccessTools.Method(listControllerType, "Dispose");
                MethodInfo init = AccessTools.Method(listControllerType, "Init");
                if (dispose == null || init == null)
                {
                    Modules.Logger.LogInfo($"[SAIN] EnemyList refresh skipped (Dispose/Init missing) follower={_bot.Profile?.Nickname}");
                    return;
                }

                dispose.Invoke(listController, Array.Empty<object>());
                init.Invoke(listController, Array.Empty<object>());
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] EnemyList refresh failed follower={_bot?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
            }
        }

    }
}
