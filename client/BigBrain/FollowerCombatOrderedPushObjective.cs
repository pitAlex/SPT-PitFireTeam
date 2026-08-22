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
        private const float SainRetainedCloseSearchDistance = 15f;
        private const float SainRetainedSearchHoldSeconds = 1.25f;

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
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
            combatPush.HandleDecisionChanged(nextDecision);

            if (CombatCommon.ShouldCommitMovementDecision(
                    nextDecision,
                    combatPush.IsPushCommittedDecision(nextDecision)))
            {
                CombatCommon.CommitMovement(nextDecision);
            }
            else if (!CombatCommon.IsSameCommittedMovement(nextDecision))
            {
                CombatCommon.ClearCommittedMovement();
            }
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo goalEnemy)
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
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> reloadRetreatDecision))
            {
                return reloadRetreatDecision;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? dogFightDecision = CombatCommon.TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                return dogFightDecision.Value;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? inFightDecision = CombatCommon.InFightLogic();
            if (inFightDecision != null)
            {
                return inFightDecision.Value;
            }

            if (CombatCommon.TryGetCombatLongGunPreparationDecision(
                    orderedEnemy,
                    orderedPush: true,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> longGunPreparation))
            {
                combatPush.ClearCommittedPush("orderedPushWeaponPreparation");
                return longGunPreparation;
            }

            if (CombatCommon.HasInitialDecision)
            {
                return CombatCommon.ConsumeInitialDecision();
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? healDecision = CombatCommon.TryGetNeedHealDecision();
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
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> recoveryDecision))
            {
                combatPush.ClearCommittedPush("orderedPushRecovery");
                return recoveryDecision;
            }

            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> pressureHoldDecision))
            {
                return pressureHoldDecision;
            }

            // With the external SAIN mod installed, EFT's mirror can retain only a memory-only
            // target while SAIN still owns an exact last-known point. At close range, investigate
            // that point instead of advancing against the hidden live transform. The objective and
            // ordered kill target remain active throughout the bounded search/hold phase.
            if (TryGetSainRetainedCloseSearchDecision(
                    orderedEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> retainedSearchDecision))
            {
                combatPush.ClearCommittedPush("orderedPushSainRetainedSearch");
                return retainedSearchDecision;
            }

            if (combatPush.TryGetCommittedPushDecision(
                    orderedEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedPush))
            {
                return committedPush;
            }

            return combatPush.CreateOrderedPushDecision(orderedEnemy);
        }

        public override AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
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
                return new AICoreActionEnd(rejectReason, true);
            }

            if (currentDecision.Action == BotLogicDecision.shootFromPlace &&
                CombatCommon.TryPrepareExposedFireRecoveryBreak(
                    currentDecision,
                    out AICoreActionEnd recoveryBreak))
            {
                return recoveryBreak;
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

            if (TryPrepareSainRetainedSearchBreak(
                    currentDecision,
                    orderedEnemy,
                    out AICoreActionEnd retainedSearchBreak))
            {
                return retainedSearchBreak;
            }

            if (combatPush.IsPushCommittedDecision(currentDecision))
            {
                AICoreActionEnd pushEnd = combatPush.EndCommittedPush(currentDecision);
                if (pushEnd.Value && string.Equals(pushEnd.Reason, "pushUnderFire", StringComparison.Ordinal))
                {
                    ArmPressureRecovery(currentDecision);
                }

                return pushEnd;
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private bool TryGetSainRetainedCloseSearchDecision(
            EnemyInfo orderedEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (!CombatCommon.TryCreateSainRetainedCloseMemorySearchDecision(
                    orderedEnemy,
                    SainRetainedCloseSearchDistance,
                    "push.ordered.memorySearch",
                    out decision))
            {
                return false;
            }

            if (decision.Action == BotLogicDecision.holdPosition)
            {
                CombatCommon.HoldFor(SainRetainedSearchHoldSeconds);
            }

            return true;
        }

        private bool TryPrepareSainRetainedSearchBreak(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision,
            EnemyInfo orderedEnemy,
            out AICoreActionEnd end)
        {
            end = FollowerCombatCommon.Continue();
            if ((currentDecision.Action != BotLogicDecision.runToEnemy &&
                 currentDecision.Action != BotLogicDecision.goToEnemy) ||
                currentDecision.Reason?.StartsWith("push.ordered.", StringComparison.Ordinal) != true ||
                !TryGetSainRetainedCloseSearchDecision(
                    orderedEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision) ||
                nextDecision.Action != BotLogicDecision.search ||
                !CombatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "orderedPushSainRetainedSearch",
                    nextDecision))
            {
                return false;
            }

            combatPush.ClearCommittedPush("orderedPushSainRetainedSearch");
            end = new AICoreActionEnd("orderedPushSainRetainedSearch", true);
            return true;
        }

        private AICoreActionEnd EndHealPendingHold(EnemyInfo orderedEnemy)
        {
            if (!CombatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEnd("orderedHealPendingCleared", true);
            }

            if (!CombatCommon.IsHealDecisionRetryBlocked)
            {
                return new AICoreActionEnd("orderedHealRetryReady", true);
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                return new AICoreActionEnd("orderedHealPendingImmediateThreat", true);
            }

            return FollowerCombatCommon.Continue();
        }

        private AICoreActionEnd EndOrderedCommittedHold(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision,
            EnemyInfo orderedEnemy)
        {
            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                CombatCommon.ClearCommittedPosition("orderedRecoveryNeedHeal");
                return new AICoreActionEnd("orderedRecoveryNeedHeal", true);
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                CombatCommon.ClearCommittedPosition("orderedRecoveryImmediateThreat");
                return new AICoreActionEnd("orderedRecoveryImmediateThreat", true);
            }

            bool committedHoldTimerActive = CombatCommon.IsCommittedHolderTimerActive();
            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedHold) &&
                committedHold.Action == currentDecision.Action &&
                string.Equals(committedHold.Reason, currentDecision.Reason, StringComparison.Ordinal))
            {
                return FollowerCombatCommon.Continue();
            }

            if (!committedHoldTimerActive)
            {
                CombatCommon.BlockCommittedPushCoverForReplan(currentDecision.Reason);
            }

            return new AICoreActionEnd("orderedCommittedHoldReleased", true);
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
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
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
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.suppressFire,
                    "objectivePush.recoveryNoCoverSuppress");
                return true;
            }

            return false;
        }

        private bool TryCreatePressureRecoveryFallback(
            EnemyInfo orderedEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
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

        private AICoreActionEnd EndPressureRecovery(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision,
            EnemyInfo orderedEnemy)
        {
            if (CombatCommon.HasImmediateExplosiveDanger())
            {
                return new AICoreActionEnd("orderedPressureExplosiveDanger", true);
            }

            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEnd("orderedPressureNeedHeal", true);
            }

            if ((orderedEnemy.IsVisible && orderedEnemy.CanShoot) ||
                CombatCommon.IsDogFightActive())
            {
                return new AICoreActionEnd("orderedPressureFightAvailable", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                AICoreActionEnd suppressEnd = CombatCommon.EndSuppressFire(currentDecision.Reason);
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
            return new AICoreActionEnd("orderedPressureRecoveryComplete", true);
        }

        private void ArmPressureRecovery(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> interruptedDecision)
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

        private static AICoreActionResult<BotLogicDecision, CoreActionResultParams> Hold(string suffix)
        {
            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
