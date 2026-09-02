using System;
using EFT;
using pitTeam.Components;

namespace pitTeam.Modules
{
    public static class SainAddonBridge
    {
        private static Func<BotOwner, bool>? _isReadyForPatrolAfterCombat;
        private static Action<BotOwner>? _forceReleaseFollowerCombatState;
        private static Func<BotOwner, bool>? _tryResetFollowerDecisionState;

        public static bool IsFollowerCombatEnabled => pitFireTeam.UseSainFollowerCombat;

        public static bool HasRuntimeCallbacks =>
            _isReadyForPatrolAfterCombat != null &&
            _forceReleaseFollowerCombatState != null &&
            _tryResetFollowerDecisionState != null;

        public static void RegisterRuntimeCallbacks(
            Func<BotOwner, bool> isReadyForPatrolAfterCombat,
            Action<BotOwner> forceReleaseFollowerCombatState,
            Func<BotOwner, bool> tryResetFollowerDecisionState)
        {
            _isReadyForPatrolAfterCombat = isReadyForPatrolAfterCombat;
            _forceReleaseFollowerCombatState = forceReleaseFollowerCombatState;
            _tryResetFollowerDecisionState = tryResetFollowerDecisionState;
        }

        public static void UnregisterRuntimeCallbacks(
            Func<BotOwner, bool> isReadyForPatrolAfterCombat,
            Action<BotOwner> forceReleaseFollowerCombatState,
            Func<BotOwner, bool> tryResetFollowerDecisionState)
        {
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
            if (!IsFollowerCombatEnabled || _isReadyForPatrolAfterCombat == null)
            {
                return false;
            }

            ready = _isReadyForPatrolAfterCombat(botOwner);
            return true;
        }

        public static bool TryForceReleaseFollowerCombatState(BotOwner botOwner)
        {
            if (!IsFollowerCombatEnabled || _forceReleaseFollowerCombatState == null)
            {
                return false;
            }

            _forceReleaseFollowerCombatState(botOwner);
            return true;
        }

        public static bool TryResetDecisionState(BotOwner botOwner)
        {
            if (!IsFollowerCombatEnabled || _tryResetFollowerDecisionState == null)
            {
                return false;
            }

            return _tryResetFollowerDecisionState(botOwner);
        }

        public static void BeginPostCombatFullHeal(BotOwner botOwner)
        {
            Utils.FollowerMedical.BeginPostCombatFullHeal(botOwner);
        }

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
            if (!IsFollowerCombatEnabled)
            {
                return;
            }

            OnBossGroupStaticUpdate?.Invoke(boss);
        }
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
