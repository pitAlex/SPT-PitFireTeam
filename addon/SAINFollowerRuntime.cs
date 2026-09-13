using System;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerRuntime
    {
        private sealed class State
        {
            public SAINFollowerSoloCombatLayer SoloLayer;
            public SAINFollowerSquadCombatLayer SquadLayer;
            public SAINFollowerSquadDecision SquadDecisions;
            public BotComponent Bot;
            public bool Prepared;
            public bool ReportedFailure;
        }
        private static readonly ConditionalWeakTable<BotOwner, State> States = new ConditionalWeakTable<BotOwner, State>();
        private static bool _enabled;
        private static float _nextUpdate;

        internal static void RegisterSoloLayer(BotOwner owner, SAINFollowerSoloCombatLayer layer)
        {
            var state = States.GetValue(owner, _ => new State());
            state.SoloLayer = layer;
            state.Prepared = false;
        }

        internal static void RegisterSquadLayer(BotOwner owner, SAINFollowerSquadCombatLayer layer)
        {
            var state = States.GetValue(owner, _ => new State());
            state.SquadLayer = layer;
            state.Prepared = false;
        }

        internal static void Enable()
        {
            SainManPersonality.Initialize();
            SainSquadDecisionBridge.Register(GetSquadDecision);
            _enabled = true;
            _nextUpdate = 0f;
            SainAddonBridge.OnBossGroupStaticUpdate += UpdateGroup;
            SainAddonBridge.OnFollowerLifecycleEvent += Lifecycle;
            SainAddonBridge.RegisterRuntimeCallbacks(ReadyForPatrol, Release, Reset, IsReady);
        }

        internal static void Disable()
        {
            _enabled = false;
            SainSquadDecisionBridge.Unregister(GetSquadDecision);
            SainAddonBridge.UnregisterRuntimeCallbacks(ReadyForPatrol, Release, Reset, IsReady);
            SainAddonBridge.OnBossGroupStaticUpdate -= UpdateGroup;
            SainAddonBridge.OnFollowerLifecycleEvent -= Lifecycle;
            foreach (var follower in BossPlayers.GetFollowers()) Cleanup(follower?.GetBot());
        }

        private static bool IsReady(BotOwner owner) =>
            _enabled && owner != null && !owner.IsDead && States.TryGetValue(owner, out State state) &&
            state.Prepared && state.SoloLayer != null && state.SquadLayer != null && state.Bot != null && !state.Bot.IsDead &&
            SainPlayerSquadBridge.TryGetPlayerLeader(owner, out _);

        private static void UpdateGroup(pitAIBossPlayer boss)
        {
            if (!_enabled || UnityEngine.Time.time < _nextUpdate) return;
            _nextUpdate = UnityEngine.Time.time + 0.5f;
            foreach (var follower in BossPlayers.GetFollowers()) Prepare(follower?.GetBot());
        }

        private static void Prepare(BotOwner owner)
        {
            if (owner == null) return;
            if (!SainAddonBridge.IsSainManSelected(owner) || owner.IsDead) { Cleanup(owner); return; }
            if (!States.TryGetValue(owner, out State state) || state.SoloLayer == null || state.SquadLayer == null) return;
            try
            {
                bool wasReady = state.Prepared;
                state.Prepared = false;
                if (!SainPlayerSquadBridge.TryGetPlayerLeader(owner, out _) ||
                    !SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot?.Decision == null || bot.Info == null) return;
                if (state.Bot != bot || state.SquadDecisions == null)
                    state.SquadDecisions = new SAINFollowerSquadDecision(bot);
                state.Bot = bot;
                state.Prepared = SainManPersonality.ApplyChad(owner, bot.Info, SAINPlugin.LoadedPreset);
                if (state.Prepared && !wasReady)
                    Modules.Logger.LogInfo($"[SAIN] SainMan combat ready: follower={owner.ProfileId} solo={SAINFollowerSoloCombatLayer.Name} squad={SAINFollowerSquadCombatLayer.Name} personality=Chad");
            }
            catch (Exception ex)
            {
                state.Prepared = false;
                if (!state.ReportedFailure)
                {
                    state.ReportedFailure = true;
                    Modules.Logger.LogError($"[SAIN] SainMan initialization failed for {owner.ProfileId}; using core fallback. {ex}");
                }
            }
        }

        private static bool GetSquadDecision(BotOwner owner, object enemy, out int decision)
        {
            decision = (int)ESquadDecision.None;
            if (!States.TryGetValue(owner, out State state) || state.SquadDecisions == null) return false;
            try
            {
                state.SquadDecisions.GetDecision(out ESquadDecision result, enemy as Enemy);
                decision = (int)result;
            }
            catch (Exception ex)
            {
                state.Prepared = false;
                if (!state.ReportedFailure)
                {
                    state.ReportedFailure = true;
                    Modules.Logger.LogError($"[SAIN] Player squad decision failed for {owner.ProfileId}; using core fallback. {ex}");
                }
            }
            // Handled None deliberately falls through to SAIN's native solo provider, not its AI-leader squad provider.
            return true;
        }

        private static void Lifecycle(BotOwner owner, FollowerLifecycleEvent kind)
        {
            if (kind == FollowerLifecycleEvent.OnRecruited) Prepare(owner);
            else Cleanup(owner);
        }

        private static void Cleanup(BotOwner owner)
        {
            if (owner == null) return;
            if (States.TryGetValue(owner, out State state))
            {
                bool wasPrepared = state.Prepared;
                state.Prepared = false;
                if (wasPrepared && !owner.IsDead)
                {
                    try { state.SoloLayer?.Stop(); state.SquadLayer?.Stop(); state.Bot?.Mover?.Stop(); state.Bot?.Decision?.ResetDecisions(false); }
                    catch (Exception ex) { Modules.Logger.LogError($"[SAIN] Combat cleanup failed for {owner.ProfileId}: {ex}"); }
                }
            }
            try { SainManPersonality.Restore(owner); }
            catch (Exception ex) { Modules.Logger.LogError($"[SAIN] Personality restore failed for {owner.ProfileId}: {ex}"); }
        }

        private static bool ReadyForPatrol(BotOwner owner) =>
            States.TryGetValue(owner, out State state) && state.Bot != null &&
            !state.Bot.Decision.HasDecision && BossPlayers.Instance?.GetFollower(owner)?.HasCombatHandoffSignal() == false;

        private static void Release(BotOwner owner)
        {
            if (!States.TryGetValue(owner, out State state) || state.SoloLayer == null || state.SquadLayer == null) return;
            state.SoloLayer?.Stop(); state.SquadLayer?.Stop();
            state.Bot?.Mover?.Stop();
            state.Bot?.Decision?.ResetDecisions(false);
        }

        private static bool Reset(BotOwner owner)
        {
            if (!States.TryGetValue(owner, out State state) || state.Bot?.Decision == null) return false;
            state.Bot.Decision.ResetDecisions(false);
            return true;
        }
    }
}