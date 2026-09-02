using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatLayer : CustomLayer
    {
        internal const float PostEnemyKeepActiveSeconds = 3f;
        private const float PostCombatFirstAidKeepActiveSeconds = 7f;
        private const float PostCombatSurgeryKeepActiveSeconds = 20f;
        internal const string LingerReason = "linger";
        internal const string LingerExpiredReason = "lingerExpired";

        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions = new HashSet<BotLogicDecision>();
        private static readonly HashSet<string> ActiveFollowerCombatBots = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> PendingForceReleaseRequests = new Dictionary<string, string>(StringComparer.Ordinal);

        private FollowerCombatLogicBase? combatLogic;
        private readonly string brainShortName;

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? currentDecision;
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? lastDecision;
        private bool hadCombatSinceActivation;
        private float lingerUntil;
        private bool lingerArmed;
        private float lingerHardUntil;
        private float medicalKeepActiveStartedAt;
        private bool medicalKeepActiveTimedOut;
        private bool combatLogicResetForInactive;
        private bool retainedLayerRearmPending;

        public FollowerCombatLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            brainShortName = botOwner?.Brain?.BaseBrain?.ShortName() ?? string.Empty;
            combatLogic = CreateCombatLogic(BotOwner, brainShortName);
        }

        public override string GetName()
        {
            return "pitTeam.FollowerCombat";
        }

        public override bool IsActive()
        {
            if (BotOwner == null || combatLogic == null)
            {
                return false;
            }

            if (BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss || BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer)
            {
                return false;
            }

            FollowerRecovery.CheckReloadTimeout(BotOwner);
            if (pitFireTeam.UseSainFollowerCombat)
            {
                return false;
            }

            if (TryConsumeForceReleaseRequest(out string forceReleaseReason))
            {
                CompletePostCombatLinger(forceReleaseReason);
                return false;
            }

            if (lingerArmed && IsLingerExpired() && !HasCurrentLiveGoalEnemy() && !TryKeepActiveForOrderedPush())
            {
                CompletePostCombatLinger(LingerExpiredReason, allowRetainedLayerRearm: true);
                return false;
            }

            if (ShouldYieldPostCombatLingerToRequestCommand())
            {
                CompletePostCombatLinger("lingerYieldRequest");
                return false;
            }

            bool isCombatActive = ShouldTreatCombatAsActive();
            if (isCombatActive)
            {
                hadCombatSinceActivation = true;
                if (HasCurrentLiveGoalEnemy())
                {
                    ClearLinger();
                }

                return true;
            }

            if (ShouldKeepCombatLayerForMedicalWork())
            {
                ClearLinger();
                return true;
            }

            if (!hadCombatSinceActivation)
            {
                return false;
            }

            ArmLingerIfNeeded();
            if (Time.time < lingerUntil)
            {
                return true;
            }

            CompletePostCombatLinger(LingerExpiredReason, allowRetainedLayerRearm: true);
            return false;
        }

        public override void Start()
        {
            base.Start();
            InitializeCombatLayerState("layerStart", "CombatLayer:Start");
        }

        private void InitializeCombatLayerState(string recorderReason, string transitionReason)
        {
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            combatLogicResetForInactive = false;
            retainedLayerRearmPending = false;
            ClearLinger();
            ClearMedicalKeepActive();
            Utils.FollowerMedical.CompletePostCombatFullHeal(BotOwner);
            MarkActive(true);
            NotifyBossCombatLayerStarted(BotOwner);
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            followerData?.CancelTemporaryCombatAggressionOverrideClearDelay();
            followerData?.BeginCombatIndependenceFromPatrol();
            BotOwner?.GetPlayer?.MovementContext?.SetPatrol(false);
            ClearFollowerCommandOnCombatTransition(transitionReason);
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic = CreateCombatLogic(BotOwner, brainShortName);
            combatLogic?.Reset();
            combatLogic?.StartDecision();
            BattleRecorder.RecordCombatLayerState(BotOwner, true, recorderReason);
        }

        public override void Stop()
        {
            BeginPostCombatFullHealIfCombatEnded();
            ReleaseActiveCombatLayerState("layerStop");
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            followerData?.ClearTemporaryCombatAggressionOverrideAfterCombatCooldown();
            followerData?.ClearActiveCombatIndependent();
            followerData?.ClearOrderedPushTargetLock("CombatLayer:Stop");
            ClearFollowerCommandOnCombatTransition("CombatLayer:Stop");
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            combatLogicResetForInactive = false;
            retainedLayerRearmPending = false;
            ClearLinger();
            ClearMedicalKeepActive();
            FollowerContactEnemyRetention.Clear(BotOwner);
            FollowerCombatTargetCommitments.ClearMission(BotOwner, null, "CombatLayer:Stop");
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic?.Reset();
            base.Stop();
        }

        public override Action GetNextAction()
        {
            bool combatActive = ShouldTreatCombatAsActive();
            if (combatActive &&
                retainedLayerRearmPending &&
                IsCurrentBigBrainLayer() &&
                !IsFollowerCombatLayerActive(BotOwner))
            {
                // BigBrain can keep this layer physically selected after linger releases its
                // active state. If a live enemy returns before Stop()/Start(), rebuild the same
                // activation state that Start() would have established before selecting an action.
                InitializeCombatLayerState("layerRetainedRearm", "CombatLayer:RetainedRearm");
                hadCombatSinceActivation = true;
            }

            lastDecision = currentDecision;

            if (combatLogic == null)
            {
                return new Action(
                    typeof(CombatHoldPositionAction),
                    "MissingCombatLogic",
                    new FollowerCombatActionData(BotLogicDecision.holdPosition, "MissingCombatLogic", null));
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision;
            bool keepForMedical = !combatActive && ShouldKeepCombatLayerForMedicalWork();
            if (!combatActive && !keepForMedical)
            {
                // As soon as the live enemy is gone, hand off to the dedicated linger action while the
                // combat layer remains active for release/handoff timing.
                BossPlayers.Instance?.GetFollower(BotOwner)?.ClearOrderedPushTargetLock("CombatLayer:Inactive");
                if (!combatLogicResetForInactive)
                {
                    combatLogic.Reset();
                    combatLogicResetForInactive = true;
                }

                ArmLingerIfNeeded();
                nextDecision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, LingerReason);
            }
            else if (keepForMedical)
            {
                // Medical work discovered during combat must remain in this layer. Handing the
                // bot to patrol while heal/surgery is pending can leave vanilla med nodes stuck.
                combatLogicResetForInactive = false;
                ClearLinger();
                nextDecision = combatLogic.GetMedicalDecision();
                combatLogic.DecisionChanged(currentDecision, nextDecision);
            }
            else
            {
                if (combatLogicResetForInactive)
                {
                    combatLogicResetForInactive = false;
                    combatLogic.StartDecision();
                }

                if (HasCurrentLiveGoalEnemy())
                {
                    ClearLinger();
                }

                nextDecision = combatLogic.GetDecision();
                combatLogic.DecisionChanged(currentDecision, nextDecision);
            }

            currentDecision = nextDecision;
            BattleRecorder.RecordDecisionSelected(BotOwner, lastDecision, nextDecision, combatLogic?.GetCurrentObjectiveName());
            return CreateBigBrainAction(nextDecision);
        }

        public override bool IsCurrentActionEnding()
        {
            if (TryConsumeForceReleaseRequest(out string forceReleaseReason))
            {
                CompletePostCombatLinger(forceReleaseReason);
                return true;
            }

            if (combatLogic == null || currentDecision == null)
            {
                return true;
            }

            bool isHealingAction = IsHealingDecision(currentDecision);

            if (currentDecision.Value.Reason != LingerReason && !ShouldTreatCombatAsActive() && !isHealingAction)
            {
                return true;
            }

            // Linger hold: layer is active but no live enemy. End immediately if combat becomes live
            // again; otherwise end when the linger window expires.
            if (currentDecision.HasValue && currentDecision.Value.Reason == LingerReason)
            {
                if (ShouldYieldPostCombatLingerToRequestCommand())
                {
                    CompletePostCombatLinger("lingerYieldRequest");
                    return true;
                }

                if (ShouldTreatCombatAsActive())
                {
                    if (HasCurrentLiveGoalEnemy())
                    {
                        ClearLinger();
                    }

                    return true;
                }

                if (ShouldKeepCombatLayerForMedicalWork())
                {
                    ClearLinger();
                    return true;
                }

                ArmLingerIfNeeded();
                bool expired = IsLingerExpired();
                if (expired)
                {
                    CompletePostCombatLinger(LingerExpiredReason, allowRetainedLayerRearm: true);
                }

                return expired;
            }

            if (!IsActive() && !isHealingAction)
            {
                return true;
            }

            if (!isHealingAction &&
                IsMovementOrPushDecision(currentDecision.Value.Action) &&
                combatLogic.HasImmediateExplosiveDanger())
            {
                BattleRecorder.RecordDecisionEnd(
                    BotOwner,
                    currentDecision.Value,
                    new AICoreActionEnd("explosiveDanger", true),
                    combatLogic.GetCurrentObjectiveName());
                return true;
            }

            // The concrete logic decides end conditions; it may delegate to shared common logic.
            AICoreActionEnd endResult = combatLogic.ShallEndCurrentDecision(currentDecision.Value);
            if (endResult.Value)
            {
                BattleRecorder.RecordDecisionEnd(BotOwner, currentDecision.Value, endResult, combatLogic.GetCurrentObjectiveName());
            }

            if (endResult.Value &&
                (currentDecision.Value.Action == BotLogicDecision.runToCover ||
                 currentDecision.Value.Action == BotLogicDecision.runToEnemy))
            {
                BotOwner.BotRun.EndMove();
            }

            return endResult.Value;
        }

        private static bool IsMovementOrPushDecision(BotLogicDecision action)
        {
            return action == BotLogicDecision.runToCover ||
                   action == BotLogicDecision.goToPoint ||
                   action == BotLogicDecision.goToPointTactical ||
                   action == BotLogicDecision.attackMoving ||
                   action == BotLogicDecision.attackMovingWithSuppress ||
                   action == BotLogicDecision.runToEnemy ||
                   action == BotLogicDecision.goToEnemy ||
                   action == (BotLogicDecision)CustomBotDecisions.attackRetreat;
        }

        private bool ShouldYieldPostCombatLingerToRequestCommand()
        {
            return (hadCombatSinceActivation || lingerArmed) &&
                   !ShouldTreatCombatAsActive() &&
                   !ShouldKeepCombatLayerForMedicalWork() &&
                   HasPendingRequestLayerCommand();
        }

        private bool HasPendingRequestLayerCommand()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null ||
                !followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out float untilTime))
            {
                return false;
            }

            if (untilTime > 0f && Time.time > untilTime)
            {
                return false;
            }

            return !IsCombatOwnedCommand(command);
        }

        private void ArmLingerIfNeeded()
        {
            if (lingerArmed)
            {
                return;
            }

            lingerUntil = Time.time + PostEnemyKeepActiveSeconds;
            lingerHardUntil = lingerUntil;
            lingerArmed = true;
        }

        private void ClearLinger()
        {
            lingerUntil = 0f;
            lingerHardUntil = 0f;
            lingerArmed = false;
        }

        private void CompletePostCombatLinger(string reason, bool allowRetainedLayerRearm = false)
        {
            Utils.FollowerMedical.BeginPostCombatFullHeal(BotOwner);
            hadCombatSinceActivation = false;
            currentDecision = null;
            lastDecision = null;
            combatLogicResetForInactive = false;
            retainedLayerRearmPending = allowRetainedLayerRearm;
            ClearLinger();
            BossPlayers.Instance?.GetFollower(BotOwner)?.ClearTemporaryCombatAggressionOverrideAfterCombatCooldown();
            ReleaseActiveCombatLayerState(reason);
        }

        private void BeginPostCombatFullHealIfCombatEnded()
        {
            if (hadCombatSinceActivation || lingerArmed || IsHealingDecision(currentDecision))
            {
                Utils.FollowerMedical.BeginPostCombatFullHeal(BotOwner);
            }
        }

        private void ClearMedicalKeepActive()
        {
            medicalKeepActiveStartedAt = 0f;
            medicalKeepActiveTimedOut = false;
        }

        private bool IsLingerExpired()
        {
            if (lingerHardUntil > 0f && Time.time >= lingerHardUntil)
            {
                return true;
            }

            return lingerUntil <= 0f || Time.time >= lingerUntil;
        }

        public static bool IsFollowerCombatLayerActive(BotOwner? botOwner)
        {
            return botOwner != null
                && !string.IsNullOrEmpty(botOwner.ProfileId)
                && ActiveFollowerCombatBots.Contains(botOwner.ProfileId);
        }

        private bool IsCurrentBigBrainLayer()
        {
            try
            {
                return string.Equals(
                    BotOwner?.Brain?.BaseBrain?.CurLayerInfo?.Name(),
                    GetName(),
                    StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[FollowerCombatLayer] Failed to read current layer for {BotOwner?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
                return false;
            }
        }

        internal static bool TryForceReleaseCoreFollowerCombatState(BotOwner? botOwner, string reason)
        {
            if (botOwner == null || string.IsNullOrEmpty(botOwner.ProfileId))
            {
                return false;
            }

            string releaseReason = string.IsNullOrWhiteSpace(reason) ? "forceRelease" : reason;
            bool wasActive = ActiveFollowerCombatBots.Remove(botOwner.ProfileId);
            if (!wasActive)
            {
                return false;
            }

            PendingForceReleaseRequests[botOwner.ProfileId] = releaseReason;
            BattleRecorder.RecordCombatLayerState(botOwner, false, releaseReason);
            NotifyBossCombatLayerReleased(botOwner, releaseReason);
            FollowerRecovery.SoftReset(botOwner);
            return true;
        }

        private bool TryConsumeForceReleaseRequest(out string reason)
        {
            reason = string.Empty;
            string profileId = BotOwner?.ProfileId ?? string.Empty;
            if (string.IsNullOrEmpty(profileId) ||
                !PendingForceReleaseRequests.TryGetValue(profileId, out reason))
            {
                return false;
            }

            PendingForceReleaseRequests.Remove(profileId);
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "forceRelease";
            }

            return true;
        }

        private void ReleaseActiveCombatLayerState(string reason)
        {
            FollowerRecovery.StopShooting(BotOwner);
            if (MarkActive(false))
            {
                BattleRecorder.RecordCombatLayerState(BotOwner, false, reason);
            }

            NotifyBossCombatLayerReleased(BotOwner, reason);
        }

        private static void NotifyBossCombatLayerStarted(BotOwner? botOwner)
        {
            if (botOwner?.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                boss.NotifyFollowerCombatLayerStarted(botOwner);
            }
        }

        private static void NotifyBossCombatLayerReleased(BotOwner? botOwner, string reason)
        {
            if (botOwner?.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                boss.NotifyFollowerCombatLayerReleased(botOwner, reason);
            }
        }

        private bool MarkActive(bool active)
        {
            if (string.IsNullOrEmpty(BotOwner?.ProfileId))
            {
                return false;
            }

            if (active)
            {
                return ActiveFollowerCombatBots.Add(BotOwner.ProfileId);
            }
            else
            {
                return ActiveFollowerCombatBots.Remove(BotOwner.ProfileId);
            }
        }

        private bool HasLiveEnemy()
        {
            return combatLogic?.ShallUseNow() == true;
        }

        private bool HasCurrentLiveGoalEnemy()
        {
            return HasLiveGoalEnemyForFire(BotOwner);
        }

        internal static bool HasLiveGoalEnemyForFire(BotOwner? botOwner)
        {
            EnemyInfo? goalEnemy = botOwner?.Memory?.GoalEnemy;
            return IsGoalEnemyAlive(goalEnemy) &&
                   (botOwner?.Memory?.HaveEnemy == true || goalEnemy!.IsVisible || goalEnemy.CanShoot);
        }

        private bool ShouldTreatCombatAsActive()
        {
            if (FollowerCombatTargetCommitments.TryRestoreMissionIfTemporaryExpired(
                    BotOwner,
                    "combatLayerRestoreMission",
                    out _))
            {
                return true;
            }

            if (FollowerContactEnemyRetention.TryRestore(BotOwner, out _))
            {
                return true;
            }

            if (TryKeepActiveForOrderedPush())
            {
                return true;
            }

            if (HasLiveEnemy())
            {
                return true;
            }

            if (currentDecision.HasValue &&
                IsMovementContinuationDecision(currentDecision.Value.Action) &&
                FollowerCombatCommon.IsNoEnemyThreatCoverReason(currentDecision.Value.Reason) &&
                FollowerAwareness.TryGetRecentThreatLookPoint(BotOwner, out _))
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy != null && IsGoalEnemyAlive(goalEnemy))
            {
                if (IsActiveFollowerSuppressContinuation())
                {
                    return true;
                }

                if (BotOwner.Memory.HaveEnemy)
                {
                    return true;
                }

                if (goalEnemy.IsVisible || goalEnemy.CanShoot)
                {
                    return true;
                }

                if (currentDecision.HasValue && IsMovementContinuationDecision(currentDecision.Value.Action))
                {
                    return true;
                }
            }

            bool recentCombatThreat =
                (hadCombatSinceActivation || currentDecision.HasValue) &&
                FollowerAwareness.WasRecentlyHit(BotOwner);
            return recentCombatThreat ||
                   (BotOwner?.Memory?.IsUnderFire == true &&
                    Time.time - BotOwner.Memory.LastTimeHit <= 2f);
        }

        private bool TryKeepActiveForOrderedPush()
        {
            if (BotOwner == null || !currentDecision.HasValue)
            {
                return false;
            }

            if (!IsOrderedPushMovementContinuation(currentDecision.Value))
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null ||
                followerData.HasOrderedPushCancelRequest ||
                !followerData.TryGetOrderedPushTargetLock(out string targetProfileId, out Vector3 lastKnownPosition))
            {
                return false;
            }

            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (IsGoalEnemyAlive(goalEnemy))
            {
                if (string.Equals(goalEnemy!.ProfileId, targetProfileId, StringComparison.Ordinal))
                {
                    followerData.RefreshOrderedPushTargetLock(goalEnemy);
                    return true;
                }

                if (FollowerCombatTargetCommitments.IsActiveTemporaryTarget(BotOwner, goalEnemy) ||
                    IsImmediateVisibleSelfDefenseThreat(goalEnemy))
                {
                    return true;
                }
            }

            return TryRestoreOrderedPushGoalEnemy(followerData, targetProfileId, lastKnownPosition);
        }

        private bool TryRestoreOrderedPushGoalEnemy(
            BotFollowerPlayer followerData,
            string targetProfileId,
            Vector3 lastKnownPosition)
        {
            if (string.IsNullOrEmpty(targetProfileId))
            {
                return false;
            }

            Player? target = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(targetProfileId);
            if (target?.HealthController?.IsAlive != true)
            {
                followerData.ClearOrderedPushTargetLock("OrderedPushTargetDead");
                return false;
            }

            EnemyInfo? restored = Enemy.MakeEnemy(
                BotOwner,
                target,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (restored == null)
            {
                return false;
            }

            restored.PriorityIndex = 0;
            restored.IgnoreUntilAggression = false;
            restored.SetVisible(restored.IsVisible);
            Vector3 rememberedPosition = IsFinite(lastKnownPosition) && lastKnownPosition.sqrMagnitude > 0.01f
                ? lastKnownPosition
                : target.Position;
            if (IsFinite(rememberedPosition) && rememberedPosition.sqrMagnitude > 0.01f)
            {
                restored.PersonalLastPos = rememberedPosition;
                if (restored.GroupInfo != null)
                {
                    restored.GroupInfo.EnemyLastPosition = rememberedPosition;
                }
            }

            BotOwner.Memory.IsPeace = false;
            using (FollowerGoalEnemyTracker.Begin("FollowerCombatLayer.TryRestoreOrderedPushGoalEnemy", "orderedPushTargetLockRestore"))
            {
                BotOwner.Memory.GoalEnemy = restored;
            }
            followerData.RefreshOrderedPushTargetLock(target);
            return IsGoalEnemyAlive(restored);
        }

        private static bool IsOrderedPushMovementContinuation(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            return IsOrderedPushReason(decision.Reason) &&
                   (IsMovementOrPushDecision(decision.Action) ||
                    decision.Action == BotLogicDecision.search);
        }

        private static bool IsOrderedPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("push.ordered", StringComparison.Ordinal);
        }

        private static bool IsImmediateVisibleSelfDefenseThreat(EnemyInfo goalEnemy)
        {
            return FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(goalEnemy);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsGoalEnemyAlive(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return goalEnemy.Person?.HealthController?.IsAlive == true;
        }

        private bool HasPendingMedicalWork()
        {
            return BotOwner?.Medecine != null &&
                   (BotOwner.Medecine.FirstAid?.Have2Do == true ||
                    BotOwner.Medecine.SurgicalKit?.HaveWork == true ||
                    BotOwner.Medecine.FirstAid?.Using == true ||
                    BotOwner.Medecine.SurgicalKit?.Using == true ||
                    BotOwner.Medecine.Stimulators?.Using == true ||
                    combatLogic?.HasActiveOrPendingHealWork() == true);
        }

        private bool ShouldKeepCombatLayerForMedicalWork()
        {
            if (!HasPendingMedicalWork())
            {
                ClearMedicalKeepActive();
                return false;
            }

            if (!hadCombatSinceActivation && !IsHealingDecision(currentDecision))
            {
                return false;
            }

            // A medical retention window is a one-shot handoff opportunity. If EFT leaves a
            // stale Have2Do/HaveWork flag behind, do not immediately start another identical
            // window and keep the combat layer alive forever. A real layer restart or the
            // pending work clearing resets this latch.
            if (medicalKeepActiveTimedOut)
            {
                return false;
            }

            if (medicalKeepActiveStartedAt <= 0f)
            {
                medicalKeepActiveStartedAt = Time.time;
            }

            float timeout = BotOwner.Medecine?.SurgicalKit?.HaveWork == true ||
                            BotOwner.Medecine?.SurgicalKit?.Using == true
                ? PostCombatSurgeryKeepActiveSeconds
                : PostCombatFirstAidKeepActiveSeconds;
            if (Time.time - medicalKeepActiveStartedAt > timeout)
            {
                medicalKeepActiveStartedAt = 0f;
                medicalKeepActiveTimedOut = true;
                return false;
            }

            return true;
        }

        private static bool IsHealingDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? decision)
        {
            return decision.HasValue && FollowerCombatCommon.IsMedicalDecision(decision.Value);
        }

        private void ClearFollowerCommandOnCombatTransition(string reason)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return;
            }

            if (!followerData.TryGetActiveCommand(out FollowerCommandType command, out _))
            {
                return;
            }

            if ((reason == "CombatLayer:Start" || reason == "CombatLayer:RetainedRearm") &&
                (IsCombatOwnedCommand(command) || command == FollowerCommandType.RegroupNearBoss))
            {
                return;
            }

            if (reason == "CombatLayer:Stop" && !IsCombatOwnedCommand(command))
            {
                return;
            }

            followerData.ClearCommand(reason);
        }

        private static bool IsCombatOwnedCommand(FollowerCommandType command)
        {
            return command == FollowerCommandType.PushEnemy ||
                   command == FollowerCommandType.SuppressEnemy ||
                   command == FollowerCommandType.NeedSniper ||
                   command == FollowerCommandType.CombatComeToBossCover ||
                   command == FollowerCommandType.CombatMoveToPointTactical;
        }

        private static bool IsMovementContinuationDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.goToEnemy ||
                   decision == BotLogicDecision.runToEnemy ||
                   decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == BotLogicDecision.suppressFire ||
                   decision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision == BotLogicDecision.goToPoint ||
                   decision == BotLogicDecision.goToPointTactical ||
                   decision == BotLogicDecision.goToCoverPoint ||
                   decision == BotLogicDecision.goToCoverPointTactical;
        }

        private bool IsActiveFollowerSuppressContinuation()
        {
            if (!currentDecision.HasValue ||
                currentDecision.Value.Action != BotLogicDecision.suppressFire ||
                !FollowerCombatCommon.IsFollowerSuppressReason(currentDecision.Value.Reason))
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null ||
                !followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) ||
                command != FollowerCommandType.SuppressEnemy)
            {
                return FollowerCombatCommon.IsAutonomousSuppressReason(currentDecision.Value.Reason);
            }

            return true;
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.Append(" brain=");
            stringBuilder.Append(brainShortName);
            stringBuilder.Append(" decision=");
            stringBuilder.Append(currentDecision?.Action.ToString() ?? "<none>");
            stringBuilder.Append(" reason=");
            stringBuilder.Append(currentDecision?.Reason ?? "<none>");

            if (BotOwner?.BotFollower?.BossToFollow != null)
            {
                Vector3 bossPosition = FollowerCombatAnchor.GetAnchorPosition(BotOwner);
                float bossNavDistance = Utils.Utils.GetNavDistance(BotOwner.Position, bossPosition);
                stringBuilder.Append(" bossNav=");
                stringBuilder.Append(bossNavDistance.ToString("F1"));
            }
        }

        private static FollowerCombatLogicBase Create(BotOwner botOwner)
        {
            BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(botOwner);
            FollowerCombatTactic tactic = follower?.CombatTactic ?? FollowerCombatTactic.Balanced;
            return tactic switch
            {
                FollowerCombatTactic.Balanced => new FollowerPmcCombatLogic(botOwner),
                // Protector currently uses the default PMC objective until its own objective is implemented.
                FollowerCombatTactic.Protector => new FollowerPmcCombatLogic(botOwner),
                FollowerCombatTactic.Marksman => new FollowerSniperCombatLogic(botOwner),
                _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        private static FollowerCombatLogicBase? CreateCombatLogic(BotOwner botOwner, string shortName)
        {
            if (botOwner == null)
            {
                return null;
            }

            return shortName switch
            {
                "PmcBear" => Create(botOwner),
                "PmcUsec" => Create(botOwner),
                "PMC" => Create(botOwner),
                "ExUsec" => Create(botOwner),
                _ => CreateCombatLogicByRole(botOwner),
            };
        }

        private static FollowerCombatLogicBase? CreateCombatLogicByRole(BotOwner botOwner)
        {
            WildSpawnType role = botOwner.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;

            return role switch
            {
                WildSpawnType.pmcBEAR => Create(botOwner),
                WildSpawnType.pmcUSEC => Create(botOwner),
                WildSpawnType.pmcBot => Create(botOwner),
                WildSpawnType.exUsec => Create(botOwner),
                _ => null,
            };
        }

        private Action CreateBigBrainAction(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            FollowerCombatActionData actionData = new FollowerCombatActionData(decision.Action, decision.Reason, decision.Data);

            if (decision.Action == BotLogicDecision.holdPosition &&
                string.Equals(decision.Reason, LingerReason, StringComparison.Ordinal))
            {
                return new Action(typeof(CombatPostCombatLingerAction), decision.Reason, actionData);
            }

            if (decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                return new Action(typeof(CombatAttackRetreatAction), decision.Reason, actionData);
            }

            switch (decision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return new Action(typeof(CombatHoldPositionAction), decision.Reason, actionData);
                case BotLogicDecision.runToCover:
                    return new Action(typeof(CombatRunToCoverAction), decision.Reason, actionData);
                case BotLogicDecision.attackMoving:
                    return new Action(typeof(CombatAttackMovingAction), decision.Reason, actionData);
                case BotLogicDecision.attackMovingWithSuppress:
                    return new Action(typeof(CombatAttackMovingWithSuppressAction), decision.Reason, actionData);
                case BotLogicDecision.dogFight:
                    return new Action(typeof(CombatDogFightAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromPlace:
                    return new Action(typeof(CombatShootFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromCover:
                    return new Action(typeof(CombatShootFromCoverAction), decision.Reason, actionData);
                case BotLogicDecision.goToEnemy:
                    return new Action(typeof(CombatGoToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.runToEnemy:
                    return new Action(typeof(CombatRunToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.goToPoint:
                    if (FollowerCombatRegroupObjective.IsRunReason(decision.Reason))
                    {
                        return new Action(typeof(CombatRegroupRunAction), decision.Reason, actionData);
                    }

                    return new Action(typeof(CombatGoToPointAction), decision.Reason, actionData);
                case BotLogicDecision.goToPointTactical:
                    return new Action(typeof(CombatGoToPointTacticalAction), decision.Reason, actionData);
                case BotLogicDecision.heal:
                    return new Action(typeof(HealAction), decision.Reason, actionData);
                case BotLogicDecision.healStimulators:
                    return new Action(typeof(HealStimulatorsAction), decision.Reason, actionData);
                case BotLogicDecision.search:
                    BotOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                    return new Action(typeof(CombatSearchAction), decision.Reason, actionData);
                case BotLogicDecision.suppressGrenade:
                    return new Action(typeof(CombatSuppressGrenadeAction), decision.Reason, actionData);
                case BotLogicDecision.suppressFire:
                    return new Action(typeof(CombatSuppressFireAction), decision.Reason, actionData);
                case BotLogicDecision.shootToSmoke:
                    return new Action(typeof(CombatShootToSmokeAction), decision.Reason, actionData);
                case BotLogicDecision.goToCoverPoint:
                    return new Action(typeof(GoToCoverPointAction), decision.Reason, actionData);
                default:
                    if (LoggedUnsupportedDecisions.Add(decision.Action))
                    {
                        Modules.Logger.LogError($"[FollowerCombat] Unsupported decision '{decision.Action}', falling back to hold.");
                    }

                    return new Action(typeof(CombatHoldPositionAction), $"Unsupported:{decision.Action}", actionData);
            }
        }

    }

}
