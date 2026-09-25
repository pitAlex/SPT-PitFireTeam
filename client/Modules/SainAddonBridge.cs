using System;
using EFT;
using UnityEngine;
using pitTeam.Components;

namespace pitTeam.Modules
{
    public static class SainAddonBridge
    {
        private static Func<BotOwner, object?>? _getSquadSnapshot;
        public static bool HasSquadProvider => _getSquadSnapshot != null;
        public static void RegisterSquadSnapshot(Func<BotOwner, object?> provider) => _getSquadSnapshot = provider;
        public static void UnregisterSquadSnapshot(Func<BotOwner, object?> provider)
        {
            if (_getSquadSnapshot == provider) _getSquadSnapshot = null;
        }
        public static object? GetSquadSnapshot(BotOwner owner) => _getSquadSnapshot?.Invoke(owner);
        private static Func<BotOwner, SainEnemyContact?>? _getEnemyContact;
        private static bool _reportedContactFailure;

        public static void RegisterEnemyContactProvider(Func<BotOwner, SainEnemyContact?> provider)
        {
            _getEnemyContact = provider;
            _reportedContactFailure = false;
        }

        public static void UnregisterEnemyContactProvider(Func<BotOwner, SainEnemyContact?> provider)
        {
            if (_getEnemyContact == provider) _getEnemyContact = null;
        }

        // Handled with no contact is authoritative: never fall back to an EFT goal's live position.
        public static bool TryGetEnemyContact(BotOwner owner, out SainEnemyContact? contact)
        {
            contact = null;
            if (!IsFollowerCombatEnabled(owner)) return false;
            try { contact = _getEnemyContact?.Invoke(owner); }
            catch (Exception ex)
            {
                if (!_reportedContactFailure)
                {
                    _reportedContactFailure = true;
                    Logger.LogError($"[SAIN] Status contact read failed: {ex}");
                }
            }
            return true;
        }

        private static Func<BotOwner, bool>? _isReadyForCombat;
        public static bool IsUsingMedical(BotOwner owner) => Utils.FollowerMedical.IsUsingMedical(owner);
        public static bool HasAcceptedGoalEnemy(BotOwner owner) =>
            BotFollowerPlayer.IsEnemyInfoAlive(owner?.Memory?.GoalEnemy);
        public static void EndPostCombatFullHeal(BotOwner owner) => Utils.FollowerMedical.CompletePostCombatFullHeal(owner);
        public static bool IsCombatReady(BotOwner owner) => _isReadyForCombat?.Invoke(owner) == true;
        private static Func<BotOwner, bool>? _isReadyForPatrolAfterCombat;
        private static Action<BotOwner>? _forceReleaseFollowerCombatState;
        private static Func<BotOwner, bool>? _tryResetFollowerDecisionState;

        public static bool IsSainManSelected(BotOwner botOwner) =>
            pitFireTeam.IsSainManTacticAvailable && botOwner != null &&
            BossPlayers.Instance?.GetFollower(botOwner)?.CombatTactic == FollowerCombatTactic.SainMan;

        public static bool IsAddonTacticSelected(BotOwner botOwner) =>
            pitFireTeam.IsSainManTacticAvailable && botOwner != null &&
            BossPlayers.Instance?.GetFollower(botOwner) is { } follower &&
            FollowerCombatTactics.UsesSainCombat(follower.CombatTactic);

        public static bool IsShooterSelected(BotOwner botOwner) =>
            BossPlayers.Instance?.GetFollower(botOwner)?.CombatTactic == FollowerCombatTactic.SAINShooter;

        public static bool IsFollowerCombatEnabled(BotOwner botOwner) => pitFireTeam.UseSainFollowerCombat(botOwner);

        public static bool HasRuntimeCallbacks =>
            _isReadyForPatrolAfterCombat != null &&
            _forceReleaseFollowerCombatState != null &&
            _tryResetFollowerDecisionState != null && _isReadyForCombat != null;

        public static void RegisterRuntimeCallbacks(
            Func<BotOwner, bool> isReadyForPatrolAfterCombat,
            Action<BotOwner> forceReleaseFollowerCombatState,
            Func<BotOwner, bool> tryResetFollowerDecisionState,
            Func<BotOwner, bool> isReadyForCombat)
        {
            _isReadyForPatrolAfterCombat = isReadyForPatrolAfterCombat;
            _forceReleaseFollowerCombatState = forceReleaseFollowerCombatState;
            _tryResetFollowerDecisionState = tryResetFollowerDecisionState;
            _isReadyForCombat = isReadyForCombat;
        }

        public static void UnregisterRuntimeCallbacks(
            Func<BotOwner, bool> isReadyForPatrolAfterCombat,
            Action<BotOwner> forceReleaseFollowerCombatState,
            Func<BotOwner, bool> tryResetFollowerDecisionState,
            Func<BotOwner, bool> isReadyForCombat)
        {
            if (_isReadyForCombat == isReadyForCombat) _isReadyForCombat = null;
            if (_isReadyForPatrolAfterCombat == isReadyForPatrolAfterCombat)
            {
                _isReadyForPatrolAfterCombat = null;
            }

            if (_forceReleaseFollowerCombatState == forceReleaseFollowerCombatState)
            {
                _forceReleaseFollowerCombatState = null;
            }

            if (_tryResetFollowerDecisionState == tryResetFollowerDecisionState)
            {
                _tryResetFollowerDecisionState = null;
            }
        }

        public static bool TryIsReadyForPatrolAfterCombat(BotOwner botOwner, out bool ready)
        {
            ready = false;
            if (!IsFollowerCombatEnabled(botOwner) || _isReadyForPatrolAfterCombat == null)
            {
                return false;
            }

            ready = _isReadyForPatrolAfterCombat(botOwner);
            return true;
        }

        public static bool TryForceReleaseFollowerCombatState(BotOwner botOwner)
        {
            if (!IsFollowerCombatEnabled(botOwner) || _forceReleaseFollowerCombatState == null)
            {
                return false;
            }

            _forceReleaseFollowerCombatState(botOwner);
            return true;
        }

        public static bool TryResetDecisionState(BotOwner botOwner)
        {
            if (!IsFollowerCombatEnabled(botOwner) || _tryResetFollowerDecisionState == null)
            {
                return false;
            }

            return _tryResetFollowerDecisionState(botOwner);
        }

        public static void BeginPostCombatFullHeal(BotOwner botOwner)
        {
            Utils.FollowerMedical.BeginPostCombatFullHeal(botOwner);
        }

        private static Func<BotOwner, bool>? _tryPushEnemy;
        private static Func<BotOwner, SainPushOrder, bool>? _tryDirectedPushEnemy;
        public static void RegisterPushEnemyHandler(Func<BotOwner, bool> handler) => _tryPushEnemy = handler;
        public static void RegisterDirectedPushEnemyHandler(Func<BotOwner, SainPushOrder, bool> handler) => _tryDirectedPushEnemy = handler;
        public static void UnregisterPushEnemyHandler(Func<BotOwner, bool> handler)
        {
            if (_tryPushEnemy == handler) _tryPushEnemy = null;
        }
        public static void UnregisterDirectedPushEnemyHandler(Func<BotOwner, SainPushOrder, bool> handler)
        {
            if (_tryDirectedPushEnemy == handler) _tryDirectedPushEnemy = null;
        }

        // Called after core command acceptance; the ready addon owns its interpretation.
        public static bool TryPushEnemy(BotOwner owner) =>
            IsFollowerCombatEnabled(owner) && _tryPushEnemy?.Invoke(owner) == true;
        public static bool TryPushEnemy(BotOwner owner, SainPushOrder order) =>
            IsFollowerCombatEnabled(owner) && _tryDirectedPushEnemy?.Invoke(owner, order) == true;

        // Generic event that addon can hook into for follower lifecycle changes.
        public static event Action<BotOwner, FollowerLifecycleEvent>? OnFollowerLifecycleEvent;

        // Boss-group static update event so addon-owned SAIN sync can run on shared group context.
        public static event Action<pitAIBossPlayer>? OnBossGroupStaticUpdate;

        /// <summary>
        /// Raise the follower lifecycle event (called from core plugin paths).
        /// </summary>
        public static void RaiseFollowerLifecycleEvent(BotOwner bot, FollowerLifecycleEvent eventType)
        {
            OnFollowerLifecycleEvent?.Invoke(bot, eventType);
        }

        /// <summary>
        /// Raise the boss-group static update event (called from core boss-group paths).
        /// </summary>
        public static void RaiseBossGroupStaticUpdate(pitAIBossPlayer boss)
        {
            if (!pitFireTeam.IsSainFollowerCombatAvailable && !SainAddonBridge.HasSquadProvider)
            {
                return;
            }

            OnBossGroupStaticUpdate?.Invoke(boss);
        }
    }

    // One command-time remembered point; no native SAIN references cross into core.
    public readonly struct SainPushOrder(string enemyProfileId, Vector3 lastKnownPosition)
    {
        public string EnemyProfileId { get; } = enemyProfileId;
        public Vector3 LastKnownPosition { get; } = lastKnownPosition;
    }

    // Passive status-report data; no native SAIN references cross into core.
    public readonly struct SainEnemyContact
    {
        public SainEnemyContact(string profileId, Vector3 lastKnownPosition, Vector3? visiblePosition, float timeSinceSeen)
        {
            ProfileId = profileId;
            LastKnownPosition = lastKnownPosition;
            VisiblePosition = visiblePosition;
            TimeSinceSeen = timeSinceSeen;
        }

        public string ProfileId { get; }
        public Vector3 LastKnownPosition { get; }
        public Vector3? VisiblePosition { get; }
        public float TimeSinceSeen { get; }
    }

    /// <summary>
    /// Follower lifecycle events that addons can subscribe to for custom cleanup/setup.
    /// </summary>
    public enum FollowerLifecycleEvent
    {
        /// <summary>Fired when a bot is recruited as a follower (after Init).</summary>
        OnRecruited,

        /// <summary>Fired when a follower is dismissed/converted back to regular bot.</summary>
        OnDismiss,

        /// <summary>Fired when raid cleanup occurs (all followers cleared).</summary>
        OnRaidEnd,
    }
}
