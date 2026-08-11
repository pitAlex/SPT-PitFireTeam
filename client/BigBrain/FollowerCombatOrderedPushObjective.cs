using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatOrderedPushObjective : FollowerCombatObjectiveBase
    {
        private const string ReasonPrefix = "objectivePush";
        private const string HealPendingReason = "objectivePush.healPending";

        private readonly FollowerCombatPush combatPush;
        private bool complete;
        private string? targetProfileId;

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
                if (string.Equals(currentDecision.Reason, HealPendingReason, StringComparison.Ordinal))
                {
                    return EndHealPendingHold(orderedEnemy);
                }

                if (CombatCommon.IsCommittedHolderReason(currentDecision.Reason))
                {
                    return EndOrderedCommittedHold(currentDecision, orderedEnemy);
                }
            }

            if (combatPush.IsPushCommittedDecision(currentDecision))
            {
                return combatPush.EndCommittedPush(currentDecision);
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
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            bool pressured =
                BotOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(BotOwner, 1f) ||
                FollowerAwareness.WasRecentlyDamaged(BotOwner);
            if (!pressured)
            {
                return false;
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                return false;
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

            if (orderedEnemy.IsVisible && orderedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "objectivePush.recoveryNoCoverSuppress");
                return true;
            }

            return false;
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
