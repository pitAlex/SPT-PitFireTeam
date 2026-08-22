using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using Vector3 = UnityEngine.Vector3;

namespace pitTeam.BigBrain
{
    internal abstract class FollowerCombatLogicBase
    {
        // High-level combat intent. Each objective owns its own decision stack and end logic.
        protected enum CombatObjectiveKind
        {
            Default,
            Regroup,
            OrderedPush,
            Suppression,
            NeedSniper,
            Grenadier,
        }

        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;
        protected readonly FollowerCombatCommon combatCommon;
        protected bool errorLogged;
        protected readonly FollowerCombatObjectiveBase defaultObjective;
        protected readonly FollowerCombatObjectiveBase sniperObjective;
        protected readonly FollowerCombatObjectiveBase regroupObjective;
        protected readonly FollowerCombatOrderedPushObjective orderedPushObjective;
        protected readonly FollowerCombatSuppressionObjective suppressionObjective;
        protected readonly FollowerCombatObjectiveBase needSniperObjective;
        protected readonly FollowerCombatGrenadierObjective grenadierObjective;
        protected CombatObjectiveKind currentObjective = CombatObjectiveKind.Default;
        private CombatObjectiveKind? grenadierResumeObjective;
        private int consumedPushEnemyIssueSequence;

        protected FollowerCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
            combatCommon = new FollowerCombatCommon(botOwner);
            defaultObjective = CreateDefaultObjective(botOwner, combatCommon);
            sniperObjective = CreateSniperObjective(botOwner, combatCommon);
            regroupObjective = CreateRegroupObjective(botOwner, combatCommon);
            orderedPushObjective = CreateOrderedPushObjective(botOwner, combatCommon);
            suppressionObjective = CreateSuppressionObjective(botOwner, combatCommon);
            needSniperObjective = CreateNeedSniperObjective(botOwner, combatCommon);
            grenadierObjective = CreateGrenadierObjective(botOwner, combatCommon);
        }

        public bool ShallUseNow() => combatCommon.HasActiveCombatEnemy();

        public bool HasActiveOrPendingHealWork() => combatCommon.HasActiveOrPendingHealWork();

        public bool HasImmediateExplosiveDanger() => combatCommon.HasImmediateExplosiveDanger();

        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetMedicalDecision()
        {
            combatCommon.BeginCoverEvaluationCycle();
            combatCommon.RepairGoalEnemyMemory();
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? healDecision = combatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "medicalHold");
        }

        public virtual void Reset()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            followerData?.SetCombatRegroupBossAnchor(false);
            followerData?.ClearOrderedPushTargetLock("CombatLogic:Reset");
            FollowerCombatTargetCommitments.ClearMission(BotOwner, null, "CombatLogic:Reset");
            combatCommon.Reset();
            defaultObjective.Reset();
            sniperObjective.Reset();
            regroupObjective.Reset();
            orderedPushObjective.Reset();
            suppressionObjective.Reset();
            needSniperObjective.Reset();
            grenadierObjective.Reset();
            currentObjective = CombatObjectiveKind.Default;
            grenadierResumeObjective = null;
            consumedPushEnemyIssueSequence = 0;
        }

        public virtual AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision()
        {
            combatCommon.BeginCoverEvaluationCycle();
            combatCommon.RepairGoalEnemyMemory();
            combatCommon.TryRestoreMissionTargetIfReady("combatDecisionRestoreMission", out _);
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                combatCommon.ClearDecisionTransition();
                if (combatCommon.TryGetTargetHandoffScanDecision(
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> targetHandoffDecision))
                {
                    return targetHandoffDecision;
                }

                if (combatCommon.TryGetNoEnemyThreatCoverDecision(
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> noEnemyThreatDecision))
                {
                    return noEnemyThreatDecision;
                }

                return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "nullEnemy");
            }

            combatCommon.ClearTargetHandoffScan("goalEnemyAvailable");

            FollowerEnemyInfoCorrection.CorrectDistanceOnly(BotOwner, goalEnemy);

            try
            {
                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
                if (TryConsumeCombatGestureCommand(followerData, goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> commandDecision))
                {
                    combatCommon.ClearDecisionTransition();
                    return commandDecision;
                }

                // End conditions may release an action only after preparing its concrete successor.
                // Consume that one-shot handoff before ordinary objective routing can select the old
                // action again; target changes and explicit combat commands invalidate the handoff.
                if (combatCommon.TryConsumePreparedDecisionTransition(
                        goalEnemy,
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> transitionDecision))
                {
                    return transitionDecision;
                }

                RefreshObjective(goalEnemy);
                if (TryActivateFirstPrimaryGrenadier(goalEnemy))
                {
                    return grenadierObjective.GetDecision(goalEnemy);
                }

                if (currentObjective != CombatObjectiveKind.Grenadier &&
                    combatCommon.TryCreatePendingLauncherPrimaryFallbackDecision(
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> fallbackDecision))
                {
                    return fallbackDecision;
                }

                if (currentObjective != CombatObjectiveKind.Grenadier &&
                    combatCommon.TryCreatePendingFirstPrimaryLauncherHolsterFallbackDecision(
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> holsterFallbackDecision))
                {
                    return holsterFallbackDecision;
                }

                AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision = GetCurrentObjective().GetDecision(goalEnemy);
                // Default combat can request an objective switch without leaking a fake action to the layer.
                // When that happens, activate regroup immediately and return regroup's first real decision.
                if (currentObjective != CombatObjectiveKind.Regroup &&
                    FollowerCombatRegroupObjective.IsRegroupActivationReason(decision.Reason))
                {
                    ActivateRegroupObjective();
                    return regroupObjective.GetDecision(goalEnemy);
                }

                if (currentObjective != CombatObjectiveKind.Grenadier &&
                    FollowerCombatGrenadierObjective.IsAutonomousActivationReason(decision.Reason))
                {
                    ActivateGrenadierObjective(ordered: false, "activateGrenadierAuto");
                    return grenadierObjective.GetDecision(goalEnemy);
                }

                return decision;
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    Logger.LogError(ex);
                    errorLogged = true;
                    return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "errorLogged");
                }

                return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "errorLogged2");
            }
        }

        public virtual AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            combatCommon.BeginCoverEvaluationCycle();
            combatCommon.TryApplyPendingLauncherPrimaryFallback(currentDecision);

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;

            // Target handoff is a shared transient hold, independent of whichever tactic/objective
            // happened to own the killed target. Route its bounded scanner before tactic-specific
            // cover-hold rules can interpret the null GoalEnemy as an immediate failure.
            if (currentDecision.Action == BotLogicDecision.holdPosition &&
                FollowerCombatCommon.IsTargetHandoffScanReason(currentDecision.Reason))
            {
                return combatCommon.EndBaseHoldPosition(currentDecision.Reason ?? string.Empty);
            }

            // GoalEnemy can disappear between clustered contacts while incoming fire is still
            // concrete. This recovery is shared survival work, not ordered-objective completion,
            // but a renewed push order must still be able to restart the ordered mission.
            if (FollowerCombatCommon.IsNoEnemyThreatCoverReason(currentDecision.Reason))
            {
                if (goalEnemy != null &&
                    ShouldConsumePushCommand(followerData, goalEnemy) &&
                    CanInterruptForOrderedPushOrder(currentDecision))
                {
                    return new AICoreActionEnd("objectivePushOrder", true);
                }

                if (currentDecision.Action == BotLogicDecision.holdPosition &&
                    !combatCommon.HasActiveCombatGestureOrder() &&
                    combatCommon.IsCommittedHolderReason(currentDecision.Reason) &&
                    combatCommon.HasCommittedPosition(
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedHold) &&
                    committedHold.Action == currentDecision.Action &&
                    string.Equals(committedHold.Reason, currentDecision.Reason, StringComparison.Ordinal))
                {
                    return FollowerCombatCommon.Continue();
                }

                return currentDecision.Action == BotLogicDecision.holdPosition
                    ? combatCommon.EndBaseHoldPosition(currentDecision.Reason ?? string.Empty)
                    : combatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (combatCommon.TryRestoreMissionTargetIfReady("combatEndRestoreMission", out EnemyInfo? restoredMission))
            {
                goalEnemy = restoredMission;
            }
            FollowerEnemyInfoCorrection.CorrectDistanceOnly(BotOwner, goalEnemy);

            if (currentObjective == CombatObjectiveKind.OrderedPush &&
                followerData?.HasOrderedPushCancelRequest == true)
            {
                return new AICoreActionEnd("orderedPushCancelRequested", true);
            }

            if (goalEnemy != null &&
                HasActiveCombatGestureOrder(followerData) &&
                CanInterruptForCombatGestureOrder(currentDecision))
            {
                return new AICoreActionEnd("combatGestureBreakMovement", true);
            }

            if (currentObjective != CombatObjectiveKind.Suppression &&
                goalEnemy != null &&
                ShouldConsumeSuppressCommand(followerData, goalEnemy))
            {
                if (!CanSatisfySuppressionOrder(followerData))
                {
                    followerData?.ClearCommand("CombatObjective:RejectSuppressionWeapon");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                if (!CanInterruptForSuppressionOrder(currentDecision))
                {
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
                {
                    followerData?.ClearCommand("CombatObjective:RejectSuppression");
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                return new AICoreActionEnd("objectiveSuppressionOrder", true);
            }

            if ((currentObjective != CombatObjectiveKind.OrderedPush || HasRenewedOrderedPushOrder(followerData)) &&
                goalEnemy != null &&
                ShouldConsumePushCommand(followerData, goalEnemy) &&
                CanInterruptForOrderedPushOrder(currentDecision))
            {
                return new AICoreActionEnd("objectivePushOrder", true);
            }

            if (currentObjective != CombatObjectiveKind.NeedSniper &&
                goalEnemy != null &&
                ShouldConsumeNeedSniperCommand(followerData, goalEnemy))
            {
                if (ShouldRejectNeedSniperObjective(goalEnemy))
                {
                    followerData?.ClearCommand("CombatObjective:RejectNeedSniper");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return FollowerCombatCommon.Continue();
                }

                return new AICoreActionEnd("objectiveNeedSniperOrder", true);
            }

            if (ShouldConsumeRegroupCommand(followerData) &&
                CanInterruptForRegroupOrder(currentDecision))
            {
                return new AICoreActionEnd("objectiveRegroupOrder", true);
            }

            // Objective ownership is stateful, not encoded in the action reason. Regroup may emit
            // shared interrupt actions such as heal/dogFight, and those still need to return to regroup.
            return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
        }

        public virtual void DecisionChanged(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            // Same ownership rule as end logic: the active objective owns even shared-reason actions.
            GetCurrentObjective().DecisionChanged(prevDecision, nextDecision);
        }

        public string GetCurrentObjectiveName()
        {
            return GetCurrentObjective().GetType().Name;
        }

        public virtual void StartDecision()
        {
            combatCommon.RepairGoalEnemyMemory();
            ActivatePrimaryObjectiveForStart();
            GetCurrentObjective().StartDecision();
        }

        protected virtual FollowerCombatObjectiveBase CreateDefaultObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatDefaultObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateSniperObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatSniperObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateRegroupObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatRegroupObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatOrderedPushObjective CreateOrderedPushObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatOrderedPushObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatSuppressionObjective CreateSuppressionObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatSuppressionObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateNeedSniperObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatNeedSniperObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatGrenadierObjective CreateGrenadierObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatGrenadierObjective(botOwner, combatCommon);
        }

        protected virtual bool ShouldConsumeRegroupCommand(BotFollowerPlayer? followerData)
        {
            // RegroupNearBoss is only a trigger for combat objective selection.
            // Once consumed, combat runs from objective state rather than command polling.
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        protected virtual bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool ShouldConsumePushCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool ShouldConsumeSuppressCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool CanInterruptForSuppressionOrder(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            return !combatCommon.IsInFight(currentDecision.Action) &&
                   !FollowerCombatCommon.IsMedicalDecision(currentDecision);
        }

        protected virtual bool CanInterruptForRegroupOrder(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            if (FollowerCombatCommon.IsMedicalDecision(currentDecision) ||
                currentDecision.Action == BotLogicDecision.dogFight)
            {
                return false;
            }

            return FollowerCombatCommon.IsMovementDecision(currentDecision) ||
                   !combatCommon.IsInFight(currentDecision.Action);
        }

        protected virtual bool CanInterruptForOrderedPushOrder(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            if (IsActiveGrenadierLauncherFire(currentDecision))
            {
                return false;
            }

            return !FollowerCombatCommon.IsMedicalDecision(currentDecision) &&
                   currentDecision.Action != BotLogicDecision.dogFight;
        }

        private bool HasRenewedOrderedPushOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                   command == FollowerCommandType.PushEnemy &&
                   followerData.PushEnemyIssueSequence != consumedPushEnemyIssueSequence;
        }

        private bool IsActiveGrenadierLauncherFire(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            return currentObjective == CombatObjectiveKind.Grenadier &&
                   currentDecision.Action == BotLogicDecision.shootFromPlace &&
                   FollowerCombatCommon.IsGrenadeLauncherCombatReason(currentDecision.Reason);
        }

        protected virtual bool ShouldReturnToPrimaryObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            // Regroup is temporary combat intent: finish it when it completes, combat ends, or
            // an explicit combat order arrives and should hand control back to the tactic's primary stack.
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   regroupObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromSuppressionObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   suppressionObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromOrderedPushObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   ShouldConsumeRegroupCommand(followerData) ||
                   orderedPushObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromNeedSniperObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   needSniperObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromGrenadierObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   ShouldConsumeRegroupCommand(followerData) ||
                   grenadierObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        private bool TryConsumeCombatGestureCommand(
            BotFollowerPlayer? followerData,
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (followerData == null ||
                !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                return false;
            }

            if (command == FollowerCommandType.CombatComeToBossCover)
            {
                if (!combatCommon.TryCreateBossCoverAttackMovingDecision(
                        goalEnemy,
                        CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                        "command.comeWithMeBossCover",
                        out decision))
                {
                    followerData.ClearCommand("CombatCommand:NoBossCover");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return false;
                }

                followerData.ClearCommand("CombatCommand:ConsumeComeWithMeCover");
                ActivatePrimaryObjective();
                return true;
            }

            if (command == FollowerCommandType.CombatMoveToPointTactical)
            {
                if (!combatCommon.TryCreateBossCommandTacticalPointDecision(
                        target,
                        "command.thereTactical",
                        out decision))
                {
                    followerData.ClearCommand("CombatCommand:InvalidTacticalPoint");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return false;
                }

                followerData.ClearCommand("CombatCommand:ConsumeThereTactical");
                ActivatePrimaryObjective();
                return true;
            }

            return false;
        }

        private bool CanInterruptForCombatGestureOrder(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            if (!FollowerCombatCommon.IsMovementDecision(currentDecision))
            {
                return false;
            }

            string? reason = currentDecision.Reason;
            if (!string.IsNullOrEmpty(reason) &&
                reason.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return !IsActiveGrenadierLauncherFire(currentDecision);
        }

        private void RefreshObjective(EnemyInfo goalEnemy)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData?.TryConsumeOrderedPushCancelRequest(out string cancelReason) == true)
            {
                if (currentObjective == CombatObjectiveKind.OrderedPush)
                {
                    ActivatePrimaryObjective($"orderedPushCancel:{cancelReason}");
                }

                return;
            }

            if (ShouldConsumePushCommand(followerData, goalEnemy))
            {
                ActivateOrderedPushObjective(followerData!, goalEnemy);
                return;
            }

            if (ShouldConsumeRegroupCommand(followerData))
            {
                ActivateRegroupObjective(followerData!);
                return;
            }

            if (ShouldConsumeSuppressCommand(followerData, goalEnemy))
            {
                ActivateSuppressionObjective(followerData!, goalEnemy);
                return;
            }

            if (ShouldConsumeNeedSniperCommand(followerData, goalEnemy))
            {
                ActivateNeedSniperObjective(followerData!, goalEnemy);
                return;
            }

            if (currentObjective == CombatObjectiveKind.Regroup && ShouldReturnToPrimaryObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.Suppression && ShouldReturnFromSuppressionObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.OrderedPush && ShouldReturnFromOrderedPushObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.NeedSniper && ShouldReturnFromNeedSniperObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.Grenadier && ShouldReturnFromGrenadierObjective(followerData, goalEnemy))
            {
                ReturnFromGrenadierObjective(followerData, goalEnemy);
            }
        }

        private void ActivateRegroupObjective(BotFollowerPlayer followerData)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:Regroup");
            combatCommon.ClearCommittedPushDecision("CombatObjective:Regroup");
            followerData.ClearCommand("CombatObjective:ConsumeRegroup");
            ActivateRegroupObjective(forceReset: true, "activateRegroupOrder");
            followerData.SetCombatRegroupBossAnchor(true);
        }

        private void ActivateOrderedPushObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectPush");
                return;
            }

            DeactivateRegroupForObjectiveSwitch(followerData);
            consumedPushEnemyIssueSequence = followerData.PushEnemyIssueSequence;
            followerData.ClearCommand("CombatObjective:ConsumePush");
            DeactivateGrenadierForObjectiveSwitch("switch.orderedPush");
            followerData.ActivateOrderedPushTargetLock(goalEnemy);
            orderedPushObjective.Activate(goalEnemy);
            currentObjective = CombatObjectiveKind.OrderedPush;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateOrderedPush");
        }

        private void ActivateRegroupObjective(bool forceReset = false, string reason = "activateRegroup")
        {
            if (currentObjective == CombatObjectiveKind.Regroup && !forceReset)
            {
                return;
            }

            // Activate resets regroup-local state so every new regroup order starts fresh from the
            // follower's current combat geometry instead of reusing stale bossward targets.
            DeactivateGrenadierForObjectiveSwitch(reason);
            regroupObjective.Activate();
            currentObjective = CombatObjectiveKind.Regroup;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        private void ActivateSuppressionObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:Suppression");
            combatCommon.ClearCommittedPushDecision("CombatObjective:Suppression");
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectSuppression");
                return;
            }

            if (!CanSatisfySuppressionOrder(followerData))
            {
                followerData.ClearCommand("CombatObjective:RejectSuppressionWeapon");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            DeactivateRegroupForObjectiveSwitch(followerData);
            Vector3 suppressTarget = Vector3.zero;
            bool suppressRequiresLauncher = false;
            bool suppressForceWeapon = false;
            bool suppressUseAutomaticSecondary = false;
            if (followerData.TryPeekActiveCommand(out FollowerCommandType command, out Vector3 target, out _) &&
                command == FollowerCommandType.SuppressEnemy)
            {
                suppressTarget = target;
                suppressRequiresLauncher = followerData.SuppressEnemyRequiresLauncher;
                suppressForceWeapon = followerData.SuppressEnemyForceWeapon;
                suppressUseAutomaticSecondary = followerData.SuppressEnemyUseAutomaticSecondary;
            }

            followerData.ClearCommand("CombatObjective:ConsumeSuppression");
            DeactivateGrenadierForObjectiveSwitch("switch.suppressionOrder");
            bool launcherSuppressCooldownActive =
                combatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: true, out _);
            if (launcherSuppressCooldownActive)
            {
                combatCommon.RecordGrenadeLauncherSuppressCooldownSkip(
                    ordered: true,
                    suppressRequiresLauncher ? "orderedSuppressRequiresLauncher" : "orderedSuppress");
                suppressRequiresLauncher = false;
                suppressForceWeapon = true;
            }

            if (!launcherSuppressCooldownActive &&
                (suppressRequiresLauncher ||
                 (!suppressForceWeapon &&
                  !suppressUseAutomaticSecondary &&
                  combatCommon.HasUsableEquippedGrenadeLauncher())))
            {
                combatCommon.SetOrderedSuppressTarget(suppressTarget);
                ActivateGrenadierObjective(ordered: true, "activateGrenadierSuppression");
                return;
            }

            if (currentObjective != CombatObjectiveKind.Suppression)
            {
                suppressionObjective.Activate(suppressRequiresLauncher, suppressForceWeapon, suppressUseAutomaticSecondary);
                currentObjective = CombatObjectiveKind.Suppression;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateSuppression");
            }
            else
            {
                suppressionObjective.Activate(suppressRequiresLauncher, suppressForceWeapon, suppressUseAutomaticSecondary);
            }

            combatCommon.SetOrderedSuppressTarget(suppressTarget);
        }

        private bool CanSatisfySuppressionOrder(BotFollowerPlayer? followerData)
        {
            if (followerData?.SuppressEnemyRequiresLauncher == true)
            {
                return combatCommon.HasUsableEquippedGrenadeLauncher();
            }

            if (combatCommon.CanCurrentWeaponSuppressOrUseGrenadeLauncher())
            {
                return true;
            }

            return followerData?.SuppressEnemyUseAutomaticSecondary == true &&
                   combatCommon.HasLoadedAutomaticSecondaryForPush();
        }

        private void ActivateNeedSniperObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:NeedSniper");
            combatCommon.ClearCommittedPushDecision("CombatObjective:NeedSniper");
            if (ShouldRejectNeedSniperObjective(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectNeedSniper");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            DeactivateRegroupForObjectiveSwitch(followerData);
            followerData.ClearCommand("CombatObjective:ConsumeNeedSniper");
            DeactivateGrenadierForObjectiveSwitch("switch.needSniper");
            if (currentObjective != CombatObjectiveKind.NeedSniper)
            {
                needSniperObjective.Activate();
                currentObjective = CombatObjectiveKind.NeedSniper;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateNeedSniper");
            }
        }

        private void ActivateGrenadierObjective(
            bool ordered,
            string reason,
            CombatObjectiveKind? resumeObjective = null)
        {
            DeactivateRegroupForObjectiveSwitch(BossPlayers.Instance?.GetFollower(BotOwner));
            grenadierObjective.Activate(ordered);
            grenadierResumeObjective = resumeObjective;
            currentObjective = CombatObjectiveKind.Grenadier;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        private void DeactivateGrenadierForObjectiveSwitch(string reason)
        {
            if (currentObjective == CombatObjectiveKind.Grenadier)
            {
                grenadierObjective.DeactivateForObjectiveSwitch(reason);
            }

            grenadierResumeObjective = null;
        }

        private void DeactivateRegroupForObjectiveSwitch(BotFollowerPlayer? followerData)
        {
            if (currentObjective != CombatObjectiveKind.Regroup)
            {
                return;
            }

            regroupObjective.Deactivate();
            followerData?.SetCombatRegroupBossAnchor(false);
        }

        private bool TryActivateFirstPrimaryGrenadier(EnemyInfo goalEnemy)
        {
            if (currentObjective != CombatObjectiveKind.Default &&
                currentObjective != CombatObjectiveKind.OrderedPush)
            {
                return false;
            }

            if (!FollowerCombatCommon.HasUsableFirstPrimaryGrenadeLauncher(BotOwner))
            {
                if (FollowerCombatCommon.IsFirstPrimaryGrenadeLauncherSelectedOrActive(BotOwner))
                {
                    combatCommon.RequestFirstPrimaryLauncherHolsterFallback(
                        "primaryLauncherUnavailable");
                }

                return false;
            }

            // A close fight may begin before Grenadier ever owns a decision. Request the same
            // loaded-pistol fallback used when an active launcher opportunity collapses at close
            // range; the pending-fallback router below will hold until the hands switch settles.
            if (combatCommon.IsFirstPrimaryLauncherTargetTooCloseForCombat(goalEnemy))
            {
                combatCommon.RequestFirstPrimaryLauncherHolsterFallback("primaryLauncherTargetTooClose");
                return false;
            }

            if (combatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: false, out _))
            {
                // A launcher equipped as the only primary cannot service ordinary rifle decisions
                // while its next safe explosive opportunity is cooling down. Use the loaded pistol
                // rather than leaving the action guard to hold the bot motionless with the launcher.
                if (FollowerCombatCommon.IsFirstPrimaryGrenadeLauncherSelectedOrActive(BotOwner))
                {
                    combatCommon.RequestFirstPrimaryLauncherHolsterFallback(
                        "primaryLauncherOpportunityCooldown");
                }

                return false;
            }

            if (!combatCommon.HasAutonomousGrenadeLauncherTarget(goalEnemy, out string? rejectReason))
            {
                if (FollowerCombatCommon.IsFirstPrimaryGrenadeLauncherSelectedOrActive(BotOwner))
                {
                    combatCommon.RequestFirstPrimaryLauncherHolsterFallback(
                        $"primaryLauncherNoOpportunity.{rejectReason ?? "unknown"}");
                }

                return false;
            }

            CombatObjectiveKind? resumeObjective = currentObjective == CombatObjectiveKind.OrderedPush
                ? CombatObjectiveKind.OrderedPush
                : null;
            ActivateGrenadierObjective(
                ordered: false,
                "activatePrimaryGrenadier",
                resumeObjective);
            return true;
        }

        private void ReturnFromGrenadierObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            bool resumeOrderedPush =
                grenadierResumeObjective == CombatObjectiveKind.OrderedPush &&
                grenadierObjective.IsComplete &&
                !orderedPushObjective.IsComplete &&
                combatCommon.HasActiveCombatEnemy(goalEnemy) &&
                !HasActiveCombatGestureOrder(followerData) &&
                !HasActiveSuppressOrder(followerData) &&
                !HasActiveNeedSniperOrder(followerData) &&
                !ShouldConsumeRegroupCommand(followerData);

            if (!resumeOrderedPush)
            {
                ActivatePrimaryObjective();
                return;
            }

            grenadierObjective.DeactivateForObjectiveSwitch("resumeOrderedPush");
            grenadierResumeObjective = null;
            currentObjective = CombatObjectiveKind.OrderedPush;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "resumeOrderedPush");
        }

        private bool ShouldRejectNeedSniperObjective(EnemyInfo goalEnemy)
        {
            return combatCommon.HasActiveOrPendingHealWork() ||
                   BotOwner.Memory.IsUnderFire ||
                   FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) ||
                   (goalEnemy.IsVisible &&
                    goalEnemy.CanShoot &&
                    goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance());
        }

        protected void ActivatePrimaryObjectiveForStart()
        {
            grenadierResumeObjective = null;
            currentObjective = CombatObjectiveKind.Default;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "combatStart");
        }

        private void ActivatePrimaryObjective(string reason = "returnPrimary")
        {
            if (currentObjective == CombatObjectiveKind.Default)
            {
                return;
            }

            if (currentObjective == CombatObjectiveKind.OrderedPush)
            {
                BossPlayers.Instance?.GetFollower(BotOwner)?.ClearOrderedPushTargetLock($"CombatObjective:{reason}");
            }

            regroupObjective.Deactivate();
            orderedPushObjective.Deactivate();
            suppressionObjective.Deactivate();
            needSniperObjective.Deactivate();
            grenadierObjective.Deactivate();
            grenadierResumeObjective = null;
            BossPlayers.Instance?.GetFollower(BotOwner)?.SetCombatRegroupBossAnchor(false);
            // Re-enter tactic combat with clean local primary-objective state, but do not call
            // StartDecision() here or the bot would incorrectly get a fresh combat opener.
            GetObjective().Activate();
            currentObjective = CombatObjectiveKind.Default;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        protected FollowerCombatObjectiveBase GetCurrentObjective()
        {
            return currentObjective switch
            {
                CombatObjectiveKind.Regroup => regroupObjective,
                CombatObjectiveKind.OrderedPush => orderedPushObjective,
                CombatObjectiveKind.Suppression => suppressionObjective,
                CombatObjectiveKind.NeedSniper => needSniperObjective,
                CombatObjectiveKind.Grenadier => grenadierObjective,
                _ => GetObjective(),
            };
        }

        protected abstract FollowerCombatObjectiveBase GetObjective();

        private static bool HasActivePushOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.PushEnemy;
        }

        private static bool HasActiveSuppressOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }

        private static bool HasActiveNeedSniperOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.NeedSniper;
        }

        private static bool HasActiveCombatGestureOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   (command == FollowerCommandType.CombatComeToBossCover ||
                    command == FollowerCommandType.CombatMoveToPointTactical);
        }
    }
}
