using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Extensions;
using SAIN.Layers;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal sealed class SAINFollowerCombatLayer : SAINLayer
    {
        public static readonly string Name = BuildLayerName("Follower Combat Layer");
        private static readonly HashSet<string> ActiveFollowerCombatBots = new HashSet<string>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> ActiveFollowerCombatSeenAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float ActiveFollowerCombatStaleSeconds = 2f;
        private static readonly System.Type SearchActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.SearchAction");
        private static readonly System.Type RushEnemyActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.RushEnemyAction");
        private static readonly Dictionary<string, float> RegroupCommandLatchUntilByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float RegroupCommandLatchSeconds = 8f;
        private const float SquadDecisionNoEnemyGraceSeconds = 2f;
        private const float SelfActionTransitionGraceSeconds = 1.25f;
        private string? _groupSearchLockEnemyProfileId;
        private bool _currentUsesDefaultBossAction;
        private bool _lastActionUsedDefaultBossAction;
        private ESquadDecision _currentDecision = ESquadDecision.None;
        private ESquadDecision _lastActionDecision = ESquadDecision.None;
        private float _lastEnemySeenAt = -1000f;
        private float _nextRetainedContactRefreshAt;

        public SAINFollowerCombatLayer(BotOwner botOwner, int priority)
            : base(botOwner, priority, Name, ESAINLayer.Squad)
        {
        }

        public override bool IsActive()
        {
            bool active = TryEvaluateFollowerDecision(out _currentDecision);
            if (!active || _currentDecision != ESquadDecision.GroupSearch)
            {
                ReleaseGroupSearchLock();
            }
            MarkActive(active);
            CheckActiveChanged(active);

            return active;
        }

        public override Action GetNextAction()
        {
            _lastActionDecision = _currentDecision;
            _lastActionUsedDefaultBossAction = _currentUsesDefaultBossAction;
            Action nextAction;
            switch (_lastActionDecision)
            {
                case ESquadDecision.Regroup:
                    if (_currentUsesDefaultBossAction)
                        nextAction = new Action(typeof(SAINFollowerCombatDefaultBossAction), "PROTECTDELTA");
                    else
                        nextAction = new Action(typeof(SAINFollowerCombatRegroupAction), $"{_lastActionDecision}");
                    break;

                case ESquadDecision.Suppress:
                    nextAction = new Action(typeof(SAINFollowerCombatSuppressAction), $"{_lastActionDecision}");
                    break;

                case ESquadDecision.Search:
                case ESquadDecision.Help:
                    nextAction = new Action(ResolveSearchActionType(), $"{_lastActionDecision}");
                    break;

                case ESquadDecision.GroupSearch:
                    if (BotOwner?.BotFollower?.BossToFollow is pitAIBossPlayer boss &&
                        Bot?.GoalEnemy?.EnemyPlayer?.ProfileId is string enemyProfileId &&
                        SAINFollowerRuntimeBridge.IsSearchPartyLeader(boss, BotOwner, enemyProfileId))
                    {
                        nextAction = new Action(ResolveSearchActionType(), $"{_lastActionDecision} : Party Leader Search");
                        break;
                    }

                    nextAction = new Action(typeof(SAINFollowerCombatFollowBossSearchAction), $"{_lastActionDecision} : Follow Boss Search");
                    break;

                case ESquadDecision.PushSuppressedEnemy:
                    nextAction = new Action(ResolveRushEnemyActionType(), $"{_lastActionDecision}");
                    break;

                default:
                    nextAction = new Action(typeof(SAINFollowerCombatDefaultBossAction), "PROTECTDELTA");
                    break;
            }

            return nextAction;
        }

        public override void Start()
        {
            base.Start();
            BossPlayers.Instance?.GetFollower(BotOwner)?.BeginCombatIndependenceFromPatrol();
        }

        public override void Stop()
        {
            ReleaseGroupSearchLock();
            SainAddonBridge.BeginPostCombatFullHeal(BotOwner);
            BossPlayers.Instance?.GetFollower(BotOwner)?.ClearActiveCombatIndependent();
            base.Stop();
        }

        public override bool IsCurrentActionEnding()
        {
            if (base.IsCurrentActionEnding())
            {
                if (_lastActionDecision == ESquadDecision.GroupSearch)
                {
                    ReleaseGroupSearchLock();
                }
                return true;
            }

            if (!TryEvaluateFollowerDecision(out ESquadDecision nextDecision))
            {
                if (_lastActionDecision == ESquadDecision.GroupSearch)
                {
                    ReleaseGroupSearchLock();
                }
                return true;
            }

            bool ending = nextDecision != _lastActionDecision ||
                          _currentUsesDefaultBossAction != _lastActionUsedDefaultBossAction;
            if (ending && _lastActionDecision == ESquadDecision.GroupSearch)
            {
                ReleaseGroupSearchLock();
            }

            return ending;
        }
        public static bool IsFollowerCombatLayerActive(BotOwner? botOwner)
        {
            if (botOwner == null || string.IsNullOrEmpty(botOwner.ProfileId))
            {
                return false;
            }

            string profileId = botOwner.ProfileId;
            if (!ActiveFollowerCombatBots.Contains(profileId))
            {
                return false;
            }

            if (!ActiveFollowerCombatSeenAtByBot.TryGetValue(profileId, out float seenAt))
            {
                ActiveFollowerCombatBots.Remove(profileId);
                return false;
            }

            if (Time.time - seenAt > ActiveFollowerCombatStaleSeconds)
            {
                ActiveFollowerCombatBots.Remove(profileId);
                ActiveFollowerCombatSeenAtByBot.Remove(profileId);
                return false;
            }

            return true;
        }

        private bool TryEvaluateFollowerDecision(out ESquadDecision decision)
        {
            decision = ESquadDecision.None;
            _currentUsesDefaultBossAction = false;
            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            if (!GetBotComponent())
            {
                return false;
            }

            BotComponent bot = Bot;
            if (bot == null || !bot.BotActive)
            {
                return false;
            }

            bool refreshedRetainedContact = TryRefreshRetainedContact(bot);

            SAINDecisionClass decisions = bot.Decision;
            if (decisions.CurrentSelfDecision != ESelfActionType.None || decisions.CurrentCombatDecision == ECombatDecision.DogFight)
            {
                return false;
            }

            if (ShouldDeferToSoloSelfAction(bot, decisions))
            {
                return false;
            }

            int knownEnemyCount = bot.EnemyController?.KnownEnemies?.Count ?? 0;
            bool haveEnemy = BotOwner.Memory?.HaveEnemy == true;
            bool haveSainEnemy = bot.GoalEnemy != null || knownEnemyCount > 0;
            bool haveSainCombatContext =
                haveEnemy ||
                haveSainEnemy ||
                refreshedRetainedContact ||
                bot.BotActivation?.BotInCombat == true ||
                bot.ActiveLayer == ESAINLayer.Squad ||
                bot.ActiveLayer == ESAINLayer.Combat;

            if (!haveSainCombatContext)
            {
                return false;
            }

            if (haveSainCombatContext)
            {
                _lastEnemySeenAt = Time.time;
            }

            if (TryHandleRegroupCommand(out decision))
            {
                _currentUsesDefaultBossAction = false;
                return true;
            }

            if (IsTemporaryHoldPositionAggressionActive())
            {
                decision = ESquadDecision.Regroup;
                _currentUsesDefaultBossAction = true;
                return true;
            }

            if (SAINFollowerSquadDecisionCalculator.TryGetDecision(BotOwner, bot, out decision) && decision != ESquadDecision.None)
            {
                if (decision == ESquadDecision.GroupSearch)
                {
                    _groupSearchLockEnemyProfileId = bot.GoalEnemy?.EnemyPlayer?.ProfileId;
                }
                else if (decision == ESquadDecision.Regroup)
                {
                    _currentUsesDefaultBossAction = !FollowerCombatAnchor.IsCombatIndependent(BotOwner);
                }
                return true;
            }

            // Fallback: if SAIN already has a squad decision, run follower layer and map leader/member-sensitive actions to boss/followers.
            if (decisions.CurrentSquadDecision != ESquadDecision.None)
            {
                bool recentEnemyContext = Time.time - _lastEnemySeenAt <= SquadDecisionNoEnemyGraceSeconds;
                if (!haveSainCombatContext && !recentEnemyContext)
                {
                    return false;
                }

                decision = decisions.CurrentSquadDecision;
                _currentUsesDefaultBossAction = decision == ESquadDecision.Regroup &&
                                                !FollowerCombatAnchor.IsCombatIndependent(BotOwner);

                return true;
            }

            return false;
        }

        private bool IsTemporaryHoldPositionAggressionActive()
        {
            return BossPlayers.Instance?.GetFollower(BotOwner)?.IsTemporaryHoldPositionAggressionActive == true;
        }

        private bool TryRefreshRetainedContact(BotComponent bot)
        {
            if (BotOwner == null || bot == null)
            {
                return false;
            }

            if (Time.time < _nextRetainedContactRefreshAt)
            {
                return FollowerContactEnemyRetention.HasActiveRetainedContact(BotOwner);
            }

            _nextRetainedContactRefreshAt = Time.time + 0.5f;
            if (!FollowerContactEnemyRetention.TryGetActiveRetainedEnemy(BotOwner, out Player? enemy, out bool prioritized) ||
                enemy == null)
            {
                return false;
            }

            bool prioritizeAsGoal = prioritized || bot.GoalEnemy == null;
            return SainGoalEnemyBridge.TrySyncEnemyState(BotOwner, enemy, prioritizeAsGoal);
        }

        private void ReleaseGroupSearchLock()
        {
            if (string.IsNullOrEmpty(_groupSearchLockEnemyProfileId) ||
                BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                string.IsNullOrEmpty(BotOwner?.ProfileId))
            {
                _groupSearchLockEnemyProfileId = null;
                return;
            }

            SAINFollowerRuntimeBridge.UnlockSearchPartyLeader(boss, _groupSearchLockEnemyProfileId, BotOwner.ProfileId);
            _groupSearchLockEnemyProfileId = null;
        }

        private bool TryHandleRegroupCommand(out ESquadDecision decision)
        {
            decision = ESquadDecision.None;

            BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(BotOwner);
            bool hasRegroupCommand = follower != null
                && follower.TryGetActiveCommand(out FollowerCommandType command, out _)
                && command == FollowerCommandType.RegroupNearBoss;

            string? botId = BotOwner?.ProfileId;
            if (!hasRegroupCommand || string.IsNullOrEmpty(botId))
            {
                if (!string.IsNullOrEmpty(botId))
                {
                    RegroupCommandLatchUntilByBot.Remove(botId);
                }
                return false;
            }

            bool haveEnemy = BotOwner.Memory?.HaveEnemy == true;
            if (haveEnemy)
            {
                RegroupCommandLatchUntilByBot[botId] = Time.time + RegroupCommandLatchSeconds;
            }
            else if (!RegroupCommandLatchUntilByBot.TryGetValue(botId, out float until) || Time.time > until)
            {
                return false;
            }

            decision = ESquadDecision.Regroup;
            return true;
        }

        private static bool ShouldDeferToSoloSelfAction(BotComponent bot, SAINDecisionClass decisions)
        {
            if (bot == null || decisions == null)
            {
                return false;
            }

            if (decisions.CurrentCombatDecision == ECombatDecision.Retreat)
            {
                return true;
            }

            bool selfProtectiveAction =
                decisions.CurrentSelfDecision == ESelfActionType.Reload ||
                decisions.CurrentSelfDecision == ESelfActionType.FirstAid ||
                decisions.CurrentSelfDecision == ESelfActionType.Surgery;

            bool runtimeProtectiveAction =
                bot.BotOwner?.WeaponManager?.Reload?.Reloading == true ||
                bot.BotOwner?.Medecine?.Using == true ||
                bot.Medical?.Surgery?.SurgeryStarted == true;

            // Plain SeekCover with no self-action context drifts followers away from boss.
            // Keep SAIN solo SeekCover only for protective contexts (reload/med/surgery).
            if (decisions.CurrentCombatDecision == ECombatDecision.SeekCover)
            {
                return selfProtectiveAction || runtimeProtectiveAction;
            }

            if (runtimeProtectiveAction)
            {
                return true;
            }

            if (decisions.PreviousSelfDecision == ESelfActionType.None || decisions.TimeSinceChangeDecision > SelfActionTransitionGraceSeconds)
            {
                return false;
            }

            return decisions.PreviousCombatDecision == ECombatDecision.SeekCover
                || decisions.PreviousCombatDecision == ECombatDecision.Retreat
                || bot.Cover?.CoverInUse != null
                || bot.Cover?.CoverPoint_MovingTo != null;
        }

        private static bool IsMeaningfulCombatDecision(ECombatDecision decision)
        {
            return decision != ECombatDecision.None && decision != ECombatDecision.DebugNoDecision;
        }

        private void MarkActive(bool active)
        {
            string profileId = BotOwner?.ProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (active)
            {
                ActiveFollowerCombatBots.Add(profileId);
                ActiveFollowerCombatSeenAtByBot[profileId] = Time.time;
            }
            else
            {
                ActiveFollowerCombatBots.Remove(profileId);
                ActiveFollowerCombatSeenAtByBot.Remove(profileId);
            }
        }

        private static System.Type ResolveSearchActionType()
        {
            return SearchActionType;
        }

        private static System.Type ResolveRushEnemyActionType()
        {
            return RushEnemyActionType;
        }

        private static System.Type ResolveSainActionType(string fullTypeName)
        {
            System.Reflection.Assembly sainAssembly = typeof(SAINLayer).Assembly;
            return sainAssembly.GetType(fullTypeName, false) ?? typeof(SAINFollowerCombatSuppressAction);
        }
    }
}
