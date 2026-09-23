using System;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN;
using SAIN.SAINComponent.SubComponents.CoverFinder;
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
            public SAINFollowerRegroupObjective Regroup;
            public SAINFollowerObjectives Objectives;
            public SAINFollowerEngageAttempt EngageAttempt;
            public SAINFollowerRecorder Recorder;
            public SAINFollowerPersonality Personality;
            public SAINFollowerCover Cover;
            public BotComponent Bot;
            public readonly SAINFollowerCombatHandoff Handoff = new SAINFollowerCombatHandoff();
            public bool Prepared;
            public bool Shooter;
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
            SainAddonBridge.RegisterEnemyContactProvider(GetEnemyContact);
            SainCoverSelectionBridge.Register(SelectCover, CoverSelected);
            SainManPersonality.Initialize();
            SainAddonBridge.RegisterPushEnemyHandler(BeginPushObjective);
            SainSquadDecisionBridge.RegisterEnemyPreference(PreferEnemy);
            SainSquadDecisionBridge.Register(GetSquadDecision, TryCombatFallback);
            _enabled = true;
            SainCombatRecorderBridge.Register(CaptureCombat, IsRecordedCombat);
            _nextUpdate = 0f;
            SainAddonBridge.OnBossGroupStaticUpdate += UpdateGroup;
            SainAddonBridge.OnFollowerLifecycleEvent += Lifecycle;
            SainAddonBridge.RegisterRuntimeCallbacks(ReadyForPatrol, Release, Reset, IsReady);
        }

        internal static void Disable()
        {
            _enabled = false;
            SainAddonBridge.UnregisterEnemyContactProvider(GetEnemyContact);
            SainCoverSelectionBridge.Unregister(SelectCover, CoverSelected);
            SainAddonBridge.UnregisterPushEnemyHandler(BeginPushObjective);
            SainSquadDecisionBridge.UnregisterEnemyPreference(PreferEnemy);
            SainSquadDecisionBridge.Unregister(GetSquadDecision, TryCombatFallback);
            SainAddonBridge.UnregisterRuntimeCallbacks(ReadyForPatrol, Release, Reset, IsReady);
            SainAddonBridge.OnBossGroupStaticUpdate -= UpdateGroup;
            SainAddonBridge.OnFollowerLifecycleEvent -= Lifecycle;
            foreach (var follower in BossPlayers.GetFollowers()) Cleanup(follower?.GetBot());
            SainCombatRecorderBridge.Unregister(CaptureCombat, IsRecordedCombat);
        }

        private static SainEnemyContact? GetEnemyContact(BotOwner owner)
        {
            if (!IsReady(owner) || !SainAddonBridge.HasAcceptedGoalEnemy(owner) ||
                !States.TryGetValue(owner, out State state)) return null;
            Enemy enemy = state.Bot.GoalEnemy;
            if (enemy == null || !enemy.WasValid || !enemy.EnemyKnown || !Enemy.IsEnemyActive(enemy) ||
                enemy.EnemyPlayer?.HealthController?.IsAlive != true || !enemy.LastKnownPosition.HasValue)
                return null;

            // Do not select/refresh an enemy or read a hidden target's real position for the UI.
            return new SainEnemyContact(enemy.EnemyProfileId, enemy.LastKnownPosition.Value,
                enemy.IsVisible && enemy.CanShoot ? enemy.EnemyPosition : (UnityEngine.Vector3?)null,
                enemy.TimeSinceSeen);
        }

        private static bool IsReady(BotOwner owner) =>
            _enabled && owner != null && !owner.IsDead && States.TryGetValue(owner, out State state) &&
            state.Prepared && state.Shooter == SainAddonBridge.IsShooterSelected(owner) && state.SoloLayer != null && state.SquadLayer != null && state.Bot != null && !state.Bot.IsDead &&
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
            if (!SainAddonBridge.IsAddonTacticSelected(owner) || owner.IsDead) { Cleanup(owner); return; }
            if (!States.TryGetValue(owner, out State state) || state.SoloLayer == null || state.SquadLayer == null) return;
            try
            {
                bool wasReady = state.Prepared;
                state.Prepared = false;
                if (!SainPlayerSquadBridge.TryGetPlayerLeader(owner, out _) ||
                    !SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot?.Decision == null || bot.Info == null) return;
                if (state.Bot != bot || state.SquadDecisions == null || state.Shooter != SainAddonBridge.IsShooterSelected(owner))
                {
                    if (state.Bot == bot && state.Shooter != SainAddonBridge.IsShooterSelected(owner))
                    {
                        state.SoloLayer?.Stop(); state.SquadLayer?.Stop();
                        bot.Mover.Stop(); bot.Decision.ResetDecisions(false); state.Handoff.Clear();
                    }
                    state.Recorder?.Dispose();
                    state.Recorder = null;
                    state.Objectives?.Clear("nativeStateReplaced");
                    state.Regroup?.Clear("nativeStateReplaced");
                    state.Cover?.Clear();
                    SainMedicalDecisionBridge.Restore(state.Bot);
                    state.Shooter = SainAddonBridge.IsShooterSelected(owner);
                    state.Cover = new SAINFollowerCover(bot);
                    state.Personality = new SAINFollowerPersonality();
                    state.EngageAttempt = new SAINFollowerEngageAttempt(bot);
                    state.Regroup = new SAINFollowerRegroupObjective(bot);
                    state.Objectives = new SAINFollowerObjectives(bot, state.Regroup);
                    state.SquadDecisions = new SAINFollowerSquadDecision(bot);
                }
                state.Bot = bot;
                state.Recorder ??= new SAINFollowerRecorder(bot, state.EngageAttempt, state.Regroup);
                state.Prepared = state.Personality.Apply(bot);
                if (!state.Prepared) SainManPersonality.Restore(owner);
                if (state.Prepared && !wasReady)
                    Modules.Logger.LogInfo($"[SAIN] SainMan combat ready: follower={owner.ProfileId} solo={SAINFollowerSoloCombatLayer.Name} squad={SAINFollowerSquadCombatLayer.Name} personality={bot.Info.Personality}");
            }
            catch (Exception ex)
            {
                state.Prepared = false;
                try { SainManPersonality.Restore(owner); }
                catch (Exception restoreError) { if (!state.ReportedFailure) Modules.Logger.LogError(restoreError.ToString()); }
                if (!state.ReportedFailure)
                {
                    state.ReportedFailure = true;
                    Modules.Logger.LogError($"[SAIN] SainMan initialization failed for {owner.ProfileId}; using core fallback. {ex}");
                }
            }
        }

        private static bool GetSquadDecision(BotOwner owner, Enemy enemy, out ESquadDecision decision)
        {
            decision = ESquadDecision.None;
            if (!States.TryGetValue(owner, out State state) || state.SquadDecisions == null) return false;
            if (!SAINFollowerCombatHandoff.AllowsEnemyCombat(owner)) return true;
            try
            {
                state.Objectives?.Observe();
                state.SquadDecisions.GetDecision(out ESquadDecision result, enemy);
                if (result != ESquadDecision.GroupSearch) state.SquadDecisions.ClearSearchLeader();
                decision = result;
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

        private static bool TryCombatFallback(BotOwner owner, Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self, out ECombatDecision nextSolo, out ESquadDecision nextSquad)
        {
            nextSolo = solo; nextSquad = squad;
            if (!States.TryGetValue(owner, out State state) || state.Regroup == null) return false;
            try
            {
                if (!SAINFollowerCombatHandoff.AllowsDecision(state.Bot, solo, self))
                {
                    nextSolo = ECombatDecision.None;
                    nextSquad = ESquadDecision.None;
                    return true;
                }
                bool handled = state.Objectives.Filter(enemy, solo,
                    squad, self, out ECombatDecision result, out ESquadDecision squadResult);
                nextSolo = result; nextSquad = squadResult;
                return handled;
            }
            catch (Exception ex)
            {
                state.Prepared = false;
                state.Objectives?.Clear("decisionFailed");
                state.Regroup.Clear("decisionFailed");
                if (!state.ReportedFailure)
                {
                    state.ReportedFailure = true;
                    Modules.Logger.LogError($"[SAIN] Automatic regroup decision failed for {owner.ProfileId}; using core fallback. {ex}");
                }
                return false;
            }
        }

        private static bool SelectCover(BotOwner owner, bool sprint, out CoverPoint point)
        {
            point = null;
            return IsReady(owner) && States.TryGetValue(owner, out State state) && state.Cover != null && state.Cover.TrySelect(sprint, out point);
        }

        private static void CoverSelected(BotOwner owner, CoverPoint point) => GetCover(owner)?.Selected(point);

        internal static SAINFollowerCover? GetCover(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Cover : null;

        private static bool BeginPushObjective(BotOwner owner)
        {
            if (!IsReady(owner) || !SainAddonBridge.IsAddonTacticSelected(owner) ||
                !States.TryGetValue(owner, out State state)) return false;
            var follower = BossPlayers.Instance?.GetFollower(owner);
            if (follower == null) return false;
            if (state.Shooter)
            {
                // Core Marksman ignores generic assault push. A handled rejection avoids
                // creating Grunt intent or applying its 100% aggression override.
                follower.ClearCommand("SAIN:MarksmanIgnorePush");
                return true;
            }
            // The addon objective owns this accepted order; core command state stays clear.
            // Native survival work and the existing failed engagement latch are preserved.
            state.Cover?.EndArrivalHold("GoForwardAggression");
            state.Regroup?.Clear("GoForwardAggression");
            follower.ClearCommand("SAIN:GoForwardAggression");
            follower.ClearOrderedPushTargetLock("SAIN:GoForwardAggression");
            follower.SetTemporaryCombatAggressionOverride(100f, "SAIN:GoForwardAggression");
            state.Objectives.Relocation.Clear("GoForwardAggression");
            state.Objectives.SquadSupport.Clear("GoForwardAggression");
            state.Objectives.Push.BeginOrdered();
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
                state.Recorder?.Dispose();
                state.Recorder = null;
                state.Cover?.Clear();
                SainMedicalDecisionBridge.Restore(state.Bot);
                state.Objectives?.Clear("release");
                state.SquadDecisions?.ClearSearchLeader();
                state.EngageAttempt?.Clear("release");
                state.Handoff.Clear();
                state.Regroup?.Clear("release");
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

        internal static SAINFollowerRegroupObjective? GetRegroup(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Regroup : null;

        internal static bool AllowsMedicalContinuation(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) && state.Handoff.AllowsMedicalContinuation(state.Bot);

        internal static bool HasEnteredCombat(BotOwner owner) =>
            owner != null && States.TryGetValue(owner, out State state) && state.Handoff.EnteredCombat;

        internal static SAINFollowerCombatPhase GetCombatPhase(BotOwner owner)
        {
            if (!IsReady(owner) || !States.TryGetValue(owner, out State state)) return SAINFollowerCombatPhase.Released;
            state.Cover?.Observe();
            state.Objectives?.Observe();
            SAINFollowerCombatPhase phase = state.Handoff.Update(state.Bot);
            if (phase != SAINFollowerCombatPhase.Combat)
            {
                // Retain ordered intent during bounded initial binding/contact interruption.
                // This does not activate combat without a living native contact.
                if (state.Objectives?.Push.AwaitingTarget != true)
                { state.Objectives?.Clear("combatEnded"); state.SquadDecisions?.ClearSearchLeader(); state.EngageAttempt?.Clear("combatEnded"); }
                else state.EngageAttempt?.Pause();
                state.Cover?.Clear();
                SainMedicalDecisionBridge.Restore(state.Bot);
            }
            else state.EngageAttempt?.Observe(state.Bot.GoalEnemy);
            state.Recorder?.ObservePhase(phase);
            return phase;
        }

        internal static SAINFollowerRelocationObjective? GetRelocation(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Objectives?.Relocation : null;

        internal static SAINFollowerPushObjective? GetPush(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Objectives?.Push : null;

        internal static BotComponent? GetSearchLeader(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.SquadDecisions?.GetSearchLeader() : null;

        internal static SAINFollowerSquadSupportObjective? GetSquadSupport(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Objectives?.SquadSupport : null;

        internal static SAINFollowerMarksmanObjective? GetMarksman(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.Objectives?.Marksman : null;

        internal static object? GetObjectiveSnapshot(BotOwner owner) =>
            States.TryGetValue(owner, out State state) ? state.Objectives?.Snapshot : null;

        private static Enemy PreferEnemy(BotOwner owner, Enemy native)
        {
            try { return GetSquadSupport(owner)?.PreferEnemy(GetPush(owner)?.PreferEnemy(native) ?? native) ?? native; }
            catch (Exception ex)
            {
                GetPush(owner)?.Clear("targetPreferenceFailed");
                GetSquadSupport(owner)?.Clear("targetPreferenceFailed");
                Modules.Logger.LogError($"[SAIN] Objective target preference failed for {owner.ProfileId}: {ex}");
                return native;
            }
        }

        internal static SAINFollowerEngageAttempt? GetEngageAttempt(BotOwner owner) =>
            IsReady(owner) && States.TryGetValue(owner, out State state) ? state.EngageAttempt : null;

        internal static SAINFollowerRecorder? GetRecorder(BotOwner owner) =>
            _enabled && SainAddonBridge.IsAddonTacticSelected(owner) && States.TryGetValue(owner, out State state) ? state.Recorder : null;

        internal static object? GetPersonalitySnapshot(BotOwner owner) =>
            States.TryGetValue(owner, out State state) ? state.Personality?.Snapshot : null;

        private static SainCombatSnapshot? CaptureCombat(BotOwner owner) => GetRecorder(owner)?.Capture();
        private static bool IsRecordedCombat(BotOwner owner) => IsReady(owner) && GetRecorder(owner)?.Active == true;

        private static bool ReadyForPatrol(BotOwner owner) =>
            States.TryGetValue(owner, out State state) && state.Bot != null &&
            GetCombatPhase(owner) == SAINFollowerCombatPhase.Released &&
            !state.Bot.Decision.HasDecision && BossPlayers.Instance?.GetFollower(owner)?.HasCombatHandoffSignal() == false;

        private static void Release(BotOwner owner)
        {
            if (!States.TryGetValue(owner, out State state) || state.SoloLayer == null || state.SquadLayer == null) return;
            state.Cover?.Clear();
            SainMedicalDecisionBridge.Restore(state.Bot);
            state.Recorder?.ObservePhase(SAINFollowerCombatPhase.Released);
            state.Objectives?.Clear("release");
            state.SquadDecisions?.ClearSearchLeader();
            state.EngageAttempt?.Clear("release");
            state.Handoff.Release(owner);
            state.Regroup?.Clear("release");
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