using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatOrderedPushObjective : FollowerCombatObjectiveBase
    {
        private const string ReasonPrefix = "objectivePush";
        private const string HealPendingReason = "objectivePush.healPending";
        private const string PressureRecoveryReasonPrefix = "objectivePush.pressureRecovery";
        private const float PressureRecoverySeconds = 3f;

        private readonly FollowerCombatPush combatPush;
        private bool complete;
        private string? targetProfileId;
        private float pressureRecoveryUntil;

        public FollowerCombatOrderedPushObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
            combatPush = new FollowerCombatPush(botOwner, combatCommon);
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            targetProfileId = null;
            pressureRecoveryUntil = 0f;
            combatPush.Reset();
        }

        public void Activate(EnemyInfo goalEnemy)
        {
            Reset();
            targetProfileId = goalEnemy?.ProfileId;
            CombatCommon.ClearInitialDecision();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
        }

        public override void Deactivate()
        {
            Reset();
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
            combatPush.HandleDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!TryGetOrderedTarget(goalEnemy, out EnemyInfo? orderedEnemy, out string rejectReason) ||
                orderedEnemy == null)
            {
                complete = true;
                combatPush.ClearCommittedPush(rejectReason);
                return Hold(rejectReason);
            }

            BossPlayers.Instance?.GetFollower(BotOwner)?.RefreshOrderedPushTargetLock(orderedEnemy);
            ExpirePressureRecoveryIfNeeded();

            if (CombatCommon.TryGetReloadRetreatDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
            {
                return reloadRetreatDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = CombatCommon.TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                return dogFightDecision.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = CombatCommon.InFightLogic();
            if (inFightDecision != null)
            {
                return inFightDecision.Value;
            }

            if (CombatCommon.TryGetCombatLongGunPreparationDecision(
                    orderedEnemy,
                    orderedPush: true,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> longGunPreparation))
            {
                combatPush.ClearCommittedPush("orderedPushWeaponPreparation");
                return longGunPreparation;
            }

            if (CombatCommon.HasInitialDecision)
            {
                return CombatCommon.ConsumeInitialDecision();
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                combatPush.ClearCommittedPush("orderedPushHealPending");
                return Hold("healPending");
            }

            if (TryGetRecoveryDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> recoveryDecision))
            {
                combatPush.ClearCommittedPush("orderedPushRecovery");
                return recoveryDecision;
            }

            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> pressureHoldDecision))
            {
                return pressureHoldDecision;
            }

            if (combatPush.TryGetCommittedPushDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush))
            {
                return committedPush;
            }

            if (combatPush.TryCreateOrderedPushFiringPosition(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> firingPositionDecision))
            {
                return firingPositionDecision;
            }

            return MarkOrderedPushDecision(combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Ordered));
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (FollowerCombatCommon.IsMedicalDecision(currentDecision))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (!TryGetOrderedTarget(
                    BotOwner.Memory?.GoalEnemy,
                    out EnemyInfo? orderedEnemy,
                    out string rejectReason) ||
                orderedEnemy == null)
            {
                complete = true;
                combatPush.ClearCommittedPush(rejectReason);
                return new AICoreActionEndStruct(rejectReason, true);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                if (FollowerCombatCommon.IsWeaponPreparationHoldReason(currentDecision.Reason))
                {
                    return CombatCommon.EndWeaponPreparationHold(currentDecision.Reason);
                }

                if (string.Equals(currentDecision.Reason, HealPendingReason, StringComparison.Ordinal))
                {
                    return EndHealPendingHold(orderedEnemy);
                }

                if (CombatCommon.IsCommittedHolderReason(currentDecision.Reason))
                {
                    return EndOrderedCommittedHold(currentDecision, orderedEnemy);
                }
            }

            if (IsPressureRecoveryReason(currentDecision.Reason))
            {
                return EndPressureRecovery(currentDecision, orderedEnemy);
            }

            if (combatPush.IsPushCommittedDecision(currentDecision))
            {
                AICoreActionEndStruct pushEnd = combatPush.EndCommittedPush(currentDecision);
                if (pushEnd.Value && string.Equals(pushEnd.Reason, "pushUnderFire", StringComparison.Ordinal))
                {
                    ArmPressureRecovery(currentDecision);
                }

                return pushEnd;
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private AICoreActionEndStruct EndHealPendingHold(EnemyInfo orderedEnemy)
        {
            if (!CombatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEndStruct("orderedHealPendingCleared", true);
            }

            if (!CombatCommon.IsHealDecisionRetryBlocked)
            {
                return new AICoreActionEndStruct("orderedHealRetryReady", true);
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                return new AICoreActionEndStruct("orderedHealPendingImmediateThreat", true);
            }

            return FollowerCombatCommon.Continue();
        }

        private AICoreActionEndStruct EndOrderedCommittedHold(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            EnemyInfo orderedEnemy)
        {
            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                CombatCommon.ClearCommittedPosition("orderedRecoveryNeedHeal");
                return new AICoreActionEndStruct("orderedRecoveryNeedHeal", true);
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                CombatCommon.ClearCommittedPosition("orderedRecoveryImmediateThreat");
                return new AICoreActionEndStruct("orderedRecoveryImmediateThreat", true);
            }

            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedHold) &&
                committedHold.Action == currentDecision.Action &&
                string.Equals(committedHold.Reason, currentDecision.Reason, StringComparison.Ordinal))
            {
                return FollowerCombatCommon.Continue();
            }

            return new AICoreActionEndStruct("orderedCommittedHoldReleased", true);
        }

        private bool TryGetOrderedTarget(
            EnemyInfo? currentGoalEnemy,
            out EnemyInfo? orderedEnemy,
            out string rejectReason)
        {
            orderedEnemy = currentGoalEnemy;
            rejectReason = string.Empty;

            if (string.IsNullOrEmpty(targetProfileId))
            {
                rejectReason = "orderedPushMissingTarget";
                return false;
            }

            if (currentGoalEnemy?.Person?.HealthController?.IsAlive == true &&
                string.Equals(currentGoalEnemy.ProfileId, targetProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            if (currentGoalEnemy?.Person?.HealthController?.IsAlive == true &&
                !string.Equals(currentGoalEnemy.ProfileId, targetProfileId, StringComparison.Ordinal) &&
                FollowerCombatTargetCommitments.IsActiveTemporaryTarget(BotOwner, currentGoalEnemy))
            {
                orderedEnemy = currentGoalEnemy;
                return true;
            }

            if (CombatCommon.TryRestoreMissionTargetIfReady("orderedPushRestoreMission", out EnemyInfo? restoredMission) &&
                restoredMission?.Person?.HealthController?.IsAlive == true &&
                string.Equals(restoredMission.ProfileId, targetProfileId, StringComparison.Ordinal))
            {
                orderedEnemy = restoredMission;
                return true;
            }

            if (!CombatCommon.TryForceGoalEnemy(
                    targetProfileId,
                    "orderedPushTarget",
                    out orderedEnemy) ||
                orderedEnemy == null)
            {
                rejectReason = "orderedPushTargetMissingOrDead";
                return false;
            }

            return true;
        }

        private bool TryGetRecoveryDecision(
            EnemyInfo orderedEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            bool pressureRecoveryActive = IsPressureRecoveryActive;
            if (BotOwner.Memory.IsInCover)
            {
                if (!pressureRecoveryActive)
                {
                    return false;
                }

                CombatCommon.HoldFor(Mathf.Max(0.1f, pressureRecoveryUntil - Time.time));
                decision = Hold("pressureRecoverySettle");
                return true;
            }

            bool pressured =
                pressureRecoveryActive ||
                BotOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(BotOwner, 1f) ||
                FollowerAwareness.WasRecentlyDamaged(BotOwner);
            if (!pressured)
            {
                return false;
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                return pressureRecoveryActive &&
                       TryCreatePressureRecoveryFallback(orderedEnemy, out decision);
            }

            if (CombatCommon.HasCommittedPosition(out decision))
            {
                return true;
            }

            if (CombatCommon.HasCommittedCover() && CombatCommon.IsBotInCommittedCover())
            {
                CombatCommon.ArmCommittedRecoveryArrivalHold("orderedPushRecovery");
                return CombatCommon.HasCommittedPosition(out decision);
            }

            bool requireShootLane = orderedEnemy.IsVisible && orderedEnemy.CanShoot;
            if (CombatCommon.TryCommitCombatCover(
                    orderedEnemy,
                    requireShootLane,
                    CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                    out string coverReason,
                    avoidBossFireLane: true,
                    recoveryManeuver: true))
            {
                string orderedCoverReason = coverReason.StartsWith("recovery.", StringComparison.Ordinal)
                    ? coverReason.Substring("recovery.".Length)
                    : coverReason;
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(
                    $"recovery.objectivePush.{orderedCoverReason}");
                return true;
            }

            if (pressureRecoveryActive)
            {
                return TryCreatePressureRecoveryFallback(orderedEnemy, out decision);
            }

            if (orderedEnemy.IsVisible && orderedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "objectivePush.recoveryNoCoverSuppress");
                return true;
            }

            return false;
        }

        private bool TryCreatePressureRecoveryFallback(
            EnemyInfo orderedEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (CombatCommon.TryCreateSuppressDecision(
                    orderedEnemy,
                    PressureRecoveryReasonPrefix + "Suppress",
                    out decision,
                    allowObstructedSuppression: true))
            {
                return true;
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, pressureRecoveryUntil - Time.time));
            decision = Hold("pressureRecoveryThreatHold");
            return true;
        }

        private AICoreActionEndStruct EndPressureRecovery(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            EnemyInfo orderedEnemy)
        {
            if (CombatCommon.HasImmediateExplosiveDanger())
            {
                return new AICoreActionEndStruct("orderedPressureExplosiveDanger", true);
            }

            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEndStruct("orderedPressureNeedHeal", true);
            }

            if ((orderedEnemy.IsVisible && orderedEnemy.CanShoot) ||
                CombatCommon.IsDogFightActive())
            {
                return new AICoreActionEndStruct("orderedPressureFightAvailable", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                AICoreActionEndStruct suppressEnd = CombatCommon.EndSuppressFire(currentDecision.Reason);
                if (suppressEnd.Value &&
                    (string.Equals(suppressEnd.Reason, "enemyMissingOrDead", StringComparison.Ordinal) ||
                     string.Equals(suppressEnd.Reason, "shootImmediately", StringComparison.Ordinal) ||
                     string.Equals(suppressEnd.Reason, "dogFightStarted", StringComparison.Ordinal)))
                {
                    return suppressEnd;
                }
            }

            if (IsPressureRecoveryActive)
            {
                return FollowerCombatCommon.Continue();
            }

            ClearPressureRecovery("elapsed");
            return new AICoreActionEndStruct("orderedPressureRecoveryComplete", true);
        }

        private void ArmPressureRecovery(
            AICoreActionResultStruct<BotLogicDecision, GClass26> interruptedDecision)
        {
            pressureRecoveryUntil = Mathf.Max(
                pressureRecoveryUntil,
                Time.time + PressureRecoverySeconds);
            BattleRecorder.RecordCommitmentEvent(
                BotOwner,
                "orderedPushPressure",
                "beginRecovery",
                "pushUnderFire",
                interruptedDecision,
                untilTime: pressureRecoveryUntil);
        }

        private void ExpirePressureRecoveryIfNeeded()
        {
            if (pressureRecoveryUntil > 0f && Time.time >= pressureRecoveryUntil)
            {
                ClearPressureRecovery("elapsedBeforeDecision");
            }
        }

        private void ClearPressureRecovery(string reason)
        {
            if (pressureRecoveryUntil <= 0f)
            {
                return;
            }

            BattleRecorder.RecordCommitmentEvent(
                BotOwner,
                "orderedPushPressure",
                "clearRecovery",
                reason);
            pressureRecoveryUntil = 0f;
        }

        private bool IsPressureRecoveryActive =>
            pressureRecoveryUntil > 0f && Time.time < pressureRecoveryUntil;

        private static bool IsPressureRecoveryReason(string? reason)
        {
            return reason?.StartsWith(PressureRecoveryReasonPrefix, StringComparison.Ordinal) == true;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> MarkOrderedPushDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null ||
                decision.Reason.StartsWith("push.ordered", StringComparison.Ordinal))
            {
                return decision;
            }

            if (decision.Reason.StartsWith("push.", StringComparison.Ordinal))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    decision.Action,
                    "push.ordered." + decision.Reason.Substring("push.".Length));
            }

            return decision;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
