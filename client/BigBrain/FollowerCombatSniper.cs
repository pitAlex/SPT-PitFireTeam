using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatSniper
    {
        private const float RepositionCooldownSeconds = 4f;
        private const float RegroupSameLevelTolerance = 1.75f;
        private const float MarksmanDefaultAutoSearchAggression = 0.3f;
        private const float FireSupportSettleSeconds = 2.5f;
        private const string FireSupportHoldReason = "sniper.fireSupportHold";
        private const string NoActionHoldReason = "sniper.noActionHold";
        private const string PositionHoldReason = "sniper.positionHold";
        private const string SupportPositionHoldReason = "sniper.FireSupport.positionHold";
        private const float HoldOpportunityScanIntervalSeconds = 0.75f;
        private const float SupportHoldTimeoutSeconds = 10f;
        private const float RepositionHoldTimeoutSeconds = 10f;
        private const float NoActionFallbackCooldownSeconds = 1f;
        private const float FiringPositionCooldownSeconds = 4f;
        private const float MarksmanCloseSearchClusterRadius = 35f;
        private const float MarksmanCloseSearchMinEnemyDistance = 16f;
        private const float MarksmanRiflemanDeferMaxNavDelta = 8f;
        private const float MarksmanRiflemanDeferMaxFollowerNavDistance = 18f;
        private const float MarksmanTeamSearchAutoMaxEnemyDistance = 35f;
        private const float RegroupFiringOpportunityRecentSeenSeconds = 1.5f;
        private const float IndirectThreatRecentHitSeconds = 3f;
        private const float IndirectThreatSuppressMaxDistance = 260f;
        private const string CloseWeaponPrepareHoldReason = "sniper.closeWeaponPrepare";
        private const float CloseWeaponPrepareRetryCooldownSeconds = 1f;

        private readonly CommittedCoverPhaseState repositionPhase = new CommittedCoverPhaseState();
        private readonly CommittedCoverPhaseState supportPhase = new CommittedCoverPhaseState();
        private readonly FiringPositionArrivalState firingPositionArrival = new FiringPositionArrivalState();
        private readonly BotOwner BotOwner;
        private readonly FollowerCombatCommon CombatCommon;
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? currentEndSourceDecision;
        private float noActionFallbackUntil;
        private float nextFiringPositionAllowedTime;
        private float closeWeaponPrepareUntil;
        private float closeWeaponPrepareRetryUntil;
        private string closeWeaponPrepareEnemyProfileId = string.Empty;
        private bool closeWeaponPreparationPending;
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? preparedCloseSearchDecision;
        private Vector3 preparedCloseSearchPoint;
        private float closeSearchRetryUntil;

        public FollowerCombatSniper(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            BotOwner = botOwner;
            CombatCommon = combatCommon;
        }

        public void Reset()
        {
            CombatCommon.ResetCommittedCover();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedGrenade();
            repositionPhase.Reset();
            supportPhase.Reset();
            currentEndSourceDecision = null;
            firingPositionArrival.Reset();
            noActionFallbackUntil = 0f;
            nextFiringPositionAllowedTime = 0f;
            ClearCloseWeaponPreparation();
            closeWeaponPrepareRetryUntil = 0f;
            closeSearchRetryUntil = 0f;
            CombatCommon.ResetRecoveryNoCoverCommitment();
            if (!CombatCommon.HasActiveCombatEnemy())
            {
                CombatCommon.TrySwitchBackToPrimaryFromAutomaticMarksmanSupport();
            }
        }

        public void DecisionChanged(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            if (!firingPositionArrival.Owns(nextDecision.Reason))
            {
                firingPositionArrival.Reset();
            }
            else if (firingPositionArrival.IsAdjusting &&
                firingPositionArrival.MatchesEnemy(BotOwner.Memory.GoalEnemy?.ProfileId) &&
                (BotOwner.GoToSomePointData.HaveTarget() != true ||
                 (BotOwner.GoToSomePointData.Point - firingPositionArrival.AdjustmentPoint).sqrMagnitude > 0.01f))
            {
                // The prepared successor owns this exact point even if the outgoing action
                // refreshed its old destination between end selection and the action handoff.
                BotOwner.GoToSomePointData.SetPoint(firingPositionArrival.AdjustmentPoint);
            }

            if (!string.Equals(nextDecision.Reason, CloseWeaponPrepareHoldReason, StringComparison.Ordinal))
            {
                ClearCloseWeaponPreparation();
            }

            ApplyMarksmanWeaponPolicy(BotOwner.Memory.GoalEnemy, nextDecision);
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
            CombatCommon.UpdateRecoveryNoCoverCommitment(nextDecision);
            if (FollowerCombatCommon.IsMovementDecision(nextDecision) &&
                IsAutomaticSupportIntentReason(nextDecision.Reason))
            {
                CombatCommon.ClearCommittedPosition("sniperAutomaticSearchStarted");
                // Tactical-point search owns its own destination, not the previous sniper cover.
                if (nextDecision.Action == BotLogicDecision.goToPointTactical)
                {
                    CombatCommon.ClearCommittedCover("sniperAutomaticSearchStarted");
                }
            }
            UpdateMarksmanCommittedHolderPhase(nextDecision);
            if (IsSniperCoverHoldReason(nextDecision.Reason))
            {
                CombatCommon.TryRenewCommittedPositionHold(nextDecision, RepositionHoldTimeoutSeconds);
            }

            if (CombatCommon.ShouldCommitMovementDecision(nextDecision, false))
            {
                CombatCommon.CommitMovement(nextDecision);
            }
            else if (!CombatCommon.IsSameCommittedMovement(nextDecision))
            {
                CombatCommon.ClearCommittedMovement();
            }
        }

        public void StartDecision()
        {
            PrepareStartDecision();
        }

        public void PrepareStartDecision()
        {
            PrepareMarksmanStartDecision();
        }

        private void PrepareMarksmanStartDecision()
        {
            CombatCommon.ClearInitialDecision();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close)
            {
                if (ShouldDeferCloseAutoToNearbyRifleman(goalEnemy))
                {
                    return;
                }

                if (ShouldUseOffensiveAutoSearch(goalEnemy))
                {
                    if (TryCreateSafeCloseSearchDecision(
                            goalEnemy,
                            "sniper.startCloseSearch",
                            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> closeSearch))
                    {
                        CombatCommon.SetInitialDecision(closeSearch);
                        return;
                    }
                }


                AICoreActionResult<BotLogicDecision, CoreActionResultParams>? suppressDecision =
                    CombatCommon.TryGetAllyEngagementSupportDecision(true);
                if (suppressDecision != null)
                {
                    CombatCommon.SetInitialDecision(suppressDecision.Value);
                    return;
                }

                if (TryCreateCloseSuppressMove(goalEnemy, "sniper.startCloseSuppress", out AICoreActionResult<BotLogicDecision, CoreActionResultParams> closeSuppress))
                {
                    CombatCommon.SetInitialDecision(closeSuppress);
                }

                return;
            }

            if (CombatCommon.TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? startCover, out _, out _) &&
                CombatCommon.IsCoverUsable(startCover))
            {
                CombatCommon.AssignCover(startCover);
                BotLogicDecision moveAction = CombatCommon.SelectCommittedCoverMoveAction(goalEnemy);
                CombatCommon.SetInitialDecision(new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    moveAction,
                    FollowerCombatCommon.CreateMovementReason("sniper.startPosition", moveAction)));
            }
        }

        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo goalEnemy)
        {
            ClearAggressiveRequests();

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                ClearRegroupCommand();
                return Regroup(goalEnemy, isExplicitOrder: true);
            }

            // Marksman follows the same shared commitment model as default, but its fresh
            // planning branches prefer firing/support positions over assault pressure.
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? preFight = TryGetMarksmanPreFightDecision(goalEnemy);
            if (preFight != null)
            {
                return preFight.Value;
            }

            if (CombatCommon.TryGetCommittedGrenadeDecision(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedGrenade))
            {
                return committedGrenade;
            }

            if (CombatCommon.HasInitialDecision)
            {
                AICoreActionResult<BotLogicDecision, CoreActionResultParams> startDecision = CombatCommon.ConsumeInitialDecision();
                return startDecision;
            }

            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return Regroup(goalEnemy);
            }

            CombatCommon.RefreshShootCover();
            CombatCommon.ValidateCommittedCover();

            if (CombatCommon.TryGetReloadRetreatDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> reloadRetreat))
            {
                CombatCommon.ClearInitialDecision();
                return reloadRetreat;
            }

            if (TryContinueFiringPositionArrival(goalEnemy, out var arrivalDecision))
            {
                return arrivalDecision;
            }

            // Close-quarter handling is marksman policy: secondary weapon and compact movement,
            // while explicit support orders are owned by separate combat objectives.
            if (!ShouldDeferCloseAutoToNearbyRifleman(goalEnemy) &&
                TryGetCloseQuarterDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> closeQuarter))
            {
                return closeQuarter;
            }

            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> recover))
            {
                return recover;
            }

            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedPosition,
                    deferCombatBreaks: true))
            {
                return committedPosition;
            }

            if (CombatCommon.TryGetCommittedMovementDecision(
                    goalEnemy,
                    HasExplicitRegroupOrder(),
                    false,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committedMovement))
            {
                return committedMovement;
            }

            if (CombatCommon.TryActivateFollowerGrenade(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> grenadeDecision))
            {
                return grenadeDecision;
            }

            // Boss-under-attack support should preempt active reposition travel/hold.
            if (repositionPhase.IsActive &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
            }

            if (TryGetActiveCommittedTravelDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> activeTravel))
            {
                return activeTravel;
            }

            if (TryGetPushSupportDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> pushSupport))
            {
                return pushSupport;
            }

            if (TryGetSniperSupportDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> support))
            {
                return support;
            }

            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> bossSupport))
            {
                return bossSupport;
            }

            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> visible))
            {
                return visible;
            }

            if (TryGetIndirectThreatPressureDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> threatPressure))
            {
                return threatPressure;
            }

            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> committed))
            {
                return committed;
            }

            // Regroup distance is a next-decision choice, never an active-action interrupt. Once a
            // hold or movement has completed and no stronger local commitment remains, choose the
            // regroup objective before inventing another firing-position route.
            if (ShouldRegroupForBossDistance())
            {
                return Regroup(goalEnemy);
            }

            if (TryGetRepositionDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> reposition))
            {

                return reposition;
            }

            return Regroup(goalEnemy);
        }

        /// <summary>
        /// Handles the marksman-only close-quarter branch: automatic secondary first, then compact pressure.
        /// </summary>
        private bool TryGetCloseQuarterDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;

            bool closeEnoughForSecondary = goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
            bool offensiveSearchAllowed = ShouldUseOffensiveAutoSearch(goalEnemy);
            if (!closeEnoughForSecondary && !offensiveSearchAllowed)
            {
                return false;
            }

            // Face-to-face contact should favor immediate fire with the current weapon.
            if (closeEnoughForSecondary && goalEnemy.IsVisible &&
                goalEnemy.CanShoot
            )
            {
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromPlace,
                    "sniper.closeImmediateShoot");
                return true;
            }
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? dogFight = closeEnoughForSecondary
                ? CombatCommon.TryGetDogFightDecision()
                : null;
            if (dogFight != null)
            {
                if (dogFight.Value.Action == BotLogicDecision.dogFight)
                {
                    // Dogfight aiming can be unstable for marksman at close breakouts. Prefer a
                    // stable immediate shot decision here and let close-quarter policy continue next tick.
                    decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.closeImmediateShoot");
                    return true;
                }

                decision = dogFight.Value;
                return true;
            }


            if (offensiveSearchAllowed)
            {
                if (TryCreateSafeCloseSearchDecision(
                        goalEnemy,
                        "sniper.closeSearch",
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> searchDecision))
                {
                    decision = searchDecision;
                    return true;
                }
            }

            if (!closeEnoughForSecondary ||
                !TryCreateCloseSuppressMove(goalEnemy, "sniper.closeAutoSuppress", out decision))
            {
                return false;
            }

            return true;
        }

        private bool ShouldUseOffensiveAutoSearch(EnemyInfo goalEnemy)
        {
            if (CombatCommon.IsTemporaryHoldPositionAggressionActive() ||
                !CombatCommon.HasAutomaticCloseCombatWeaponAvailable())
            {
                return false;
            }

            if (CombatCommon.ShouldBlockProactiveAutoPushForWeaponThreat(goalEnemy) ||
                CombatCommon.ShouldUseCautiousWeaponThreatStyle(goalEnemy))
            {
                return false;
            }

            if (Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy))
            {
                return false;
            }

            float aggression = CombatCommon.GetAggression01();
            if (aggression <= 0.01f)
            {
                return false;
            }

            if (!IsWithinMarksmanAutoSearchDistance(goalEnemy, aggression))
            {
                return false;
            }

            return CombatCommon.IsSafeCloseSearchTarget(goalEnemy, aggression, MarksmanCloseSearchClusterRadius);
        }

        private bool IsMarksmanCloseSearchDestinationSafe(
            EnemyInfo goalEnemy,
            Vector3 target)
        {
            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor) || !IsFinite(target) ||
                (target - BotOwner.Position).sqrMagnitude <= 2f * 2f ||
                !Utils.Utils.TryGetCompletePathDistance(BotOwner.Position, target, out float navDistance) ||
                !IsFinite(navDistance) || navDistance > 90f)
            {
                return false;
            }

            enemyAnchor.y = target.y;
            return (target - enemyAnchor).sqrMagnitude >=
                   MarksmanCloseSearchMinEnemyDistance * MarksmanCloseSearchMinEnemyDistance;
        }

        private bool TryCreateSafeCloseSearchDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < closeSearchRetryUntil)
            {
                return false;
            }

            // Select a real, distinct route before drawing the support weapon. Otherwise a failed
            // search can repeatedly draw automatic support and immediately return to sniper hold.
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? searchDecision =
                CombatCommon.EnemyCoverSearch(reason, weakEnemy: false, avoidBossFireLane: true);
            if (!searchDecision.HasValue ||
                searchDecision.Value.Action != BotLogicDecision.goToPointTactical ||
                BotOwner.GoToSomePointData?.HaveTarget() != true ||
                !IsMarksmanCloseSearchDestinationSafe(goalEnemy, BotOwner.GoToSomePointData.Point))
            {
                closeSearchRetryUntil = Time.time + FiringPositionCooldownSeconds;
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "Sniper", "engagementRejected", "noSafeAutomaticSearchDestination");
                return false;
            }

            Vector3 searchPoint = BotOwner.GoToSomePointData.Point;
            if (!TryPrepareAutomaticCloseWeapon(goalEnemy, out decision, out bool weaponReady))
            {
                return false;
            }

            if (!weaponReady)
            {
                preparedCloseSearchDecision = searchDecision;
                preparedCloseSearchPoint = searchPoint;
                return true;
            }

            decision = searchDecision.Value;
            return true;
        }

        private bool TryPrepareAutomaticCloseWeapon(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
            out bool weaponReady)
        {
            decision = default;
            weaponReady = CombatCommon.IsAutomaticCloseCombatWeaponReady();
            if (weaponReady)
            {
                ClearCloseWeaponPreparation();
                return true;
            }

            if (Time.time < closeWeaponPrepareRetryUntil)
            {
                return false;
            }

            // Only an accepted switch request starts the bounded preparation hold. An unrelated
            // reload/hands transition must not be mistaken for a switch owned by this tactic.
            if (!CombatCommon.TryRequestAutomaticSupportForCloseCombat())
            {
                BlockCloseWeaponPreparationRetry();
                return false;
            }

            BeginCloseWeaponPreparation(goalEnemy);
            closeWeaponPreparationPending = true;
            CombatCommon.HoldFor(0.25f);
            decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                BotLogicDecision.holdPosition,
                CloseWeaponPrepareHoldReason);
            return true;
        }

        private void BeginCloseWeaponPreparation(EnemyInfo goalEnemy)
        {
            string enemyProfileId = goalEnemy.ProfileId ?? string.Empty;
            if (string.Equals(closeWeaponPrepareEnemyProfileId, enemyProfileId, StringComparison.Ordinal) &&
                closeWeaponPrepareUntil > Time.time)
            {
                return;
            }

            closeWeaponPrepareEnemyProfileId = enemyProfileId;
            closeWeaponPrepareUntil = Time.time + FollowerCombatCommon.SupportWeaponPrepareTimeoutSeconds;
        }

        private void BlockCloseWeaponPreparationRetry()
        {
            closeWeaponPrepareRetryUntil = Time.time + CloseWeaponPrepareRetryCooldownSeconds;
            ClearCloseWeaponPreparation();
        }

        private void ClearCloseWeaponPreparation()
        {
            closeWeaponPrepareUntil = 0f;
            closeWeaponPrepareEnemyProfileId = string.Empty;
            closeWeaponPreparationPending = false;
            preparedCloseSearchDecision = null;
            preparedCloseSearchPoint = Vector3.zero;
        }

        private bool IsWithinMarksmanAutoSearchDistance(EnemyInfo goalEnemy, float aggression)
        {
            Enemy.EnemyDistance distance = Enemy.Distance(goalEnemy);
            if (aggression <= MarksmanDefaultAutoSearchAggression + 0.01f)
            {
                return distance <= Enemy.EnemyDistance.Close;
            }

            Enemy.EnemyDistance maxDistance = CombatCommon.GetMaxPushDistance(
                aggression,
                FollowerCombatTactic.Balanced);
            return distance <= maxDistance;
        }

        /// <summary>
        /// Marksman still uses the shared prefight gates, but a generic immediate-shoot handoff
        /// must not steal ownership from close-quarter secondary logic mid-fight.
        /// </summary>
        private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetMarksmanPreFightDecision(EnemyInfo goalEnemy)
        {
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? preFight = CombatCommon.PreFightLogic();
            if (preFight == null)
            {
                return null;
            }

            if (preFight.Value.Action == BotLogicDecision.shootFromPlace &&
                string.Equals(preFight.Value.Reason, "ShootImmediately", StringComparison.Ordinal) &&
                ShouldPreserveCloseQuarterWeaponFlow(goalEnemy))
            {
                return null;
            }

            return preFight;
        }

        /// <summary>
        /// If marksman close-quarter logic should own the frame, do not let generic shootFromPlace
        /// re-evaluation interrupt it and risk a mid-fight weapon swap.
        /// </summary>
        private bool ShouldPreserveCloseQuarterWeaponFlow(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (CombatCommon.IsCurrentWeaponAutomatic())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Preserves a close-weapon selection across its handoff. The preparation branch owns its
        /// one asynchronous request; decision changes never reissue that request during movement.
        /// </summary>
        private void ApplyMarksmanWeaponPolicy(
            EnemyInfo? goalEnemy,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (goalEnemy == null)
            {
                return;
            }

            if (ShouldUseCloseIntentSecondary(decision))
            {
                return;
            }

            if (ShouldSwitchBackToPrimaryForSniperDecision(goalEnemy, decision))
            {
                TrySwitchToPrimaryForSniperDecision();
            }
        }

        private static bool ShouldUseCloseIntentSecondary(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            return IsAutomaticSupportIntentReason(decision.Reason);
        }

        private static bool IsCloseIntentDecisionReason(string? reason)
        {
            return reason != null &&
                   (reason.StartsWith("sniper.startClose", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.closeSearch", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal));
        }

        internal static bool IsAutomaticSupportIntentReason(string? reason)
        {
            const string coverHoldPrefix = "committedCoverHold.";
            const string pointHoldPrefix = "committedPositionHold.";
            if (reason?.StartsWith(coverHoldPrefix, StringComparison.Ordinal) == true)
            {
                reason = reason.Substring(coverHoldPrefix.Length);
            }
            else if (reason?.StartsWith(pointHoldPrefix, StringComparison.Ordinal) == true)
            {
                reason = reason.Substring(pointHoldPrefix.Length);
            }

            return IsCloseIntentDecisionReason(reason) ||
                   string.Equals(reason, "sniper.closeImmediateShoot", StringComparison.Ordinal) ||
                   string.Equals(reason, CloseWeaponPrepareHoldReason, StringComparison.Ordinal) ||
                   FollowerCombatSuppressionObjective.IsAutomaticSupportIntentReason(reason);
        }

        private bool CanUseCloseIntentSecondary(EnemyInfo goalEnemy)
        {
            return CanUseAutomaticSupportForCloseThreat(BotOwner, goalEnemy);
        }

        internal static bool CanUseAutomaticSupportForCloseThreat(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            return FollowerCombatCommon.HasActiveCombatEnemy(botOwner, goalEnemy) &&
                   goalEnemy != null &&
                   goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
        }

        private void TrySwitchToPrimaryForSniperDecision()
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                !selector.IsChanging)
            {
                selector.ChangeToMain();
            }
        }

        private bool ShouldSwitchBackToPrimaryForSniperDecision(
            EnemyInfo goalEnemy,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null || selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
            {
                return false;
            }

            if (!CombatCommon.IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary())
            {
                return false;
            }

            if (CanUseCloseIntentSecondary(goalEnemy))
            {
                return false;
            }

            if (IsCloseIntentDecisionReason(decision.Reason))
            {
                return true;
            }

            return IsMarksmanPrimaryRangeDecision(decision.Reason) ||
                   CombatCommon.IsCommittedCoverRetreatingFromEnemy(goalEnemy);
        }

        private static bool IsMarksmanPrimaryRangeDecision(string? reason)
        {
            if (reason == null)
            {
                return false;
            }

            return reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.NeedSniper", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.protectBossShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.coverHold", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.recoverCover", StringComparison.Ordinal) ||
                   string.Equals(reason, FireSupportHoldReason, StringComparison.Ordinal) ||
                   string.Equals(reason, SupportPositionHoldReason, StringComparison.Ordinal) ||
                   string.Equals(reason, NoActionHoldReason, StringComparison.Ordinal) ||
                   string.Equals(reason, PositionHoldReason, StringComparison.Ordinal);
        }

        /// <summary>
        /// Close marksman suppression is movement, so only emit it after a real destination was committed.
        /// </summary>
        private bool TryCreateCloseSuppressMove(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (!TryPrepareAutomaticCloseWeapon(goalEnemy, out decision, out bool weaponReady))
            {
                return false;
            }

            if (!weaponReady)
            {
                return true;
            }

            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    reason,
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: true))
            {
                return false;
            }

            // The committed cover already has the mutated reason stored.
            // Just use the stored values directly.
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            return true;
        }

        private bool ShouldDeferCloseAutoToNearbyRifleman(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.Followers == null)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            float marksmanToEnemyNav = Utils.Utils.GetNavDistance(BotOwner.Position, enemyAnchor);
            if (!IsFinite(marksmanToEnemyNav))
            {
                marksmanToEnemyNav = Vector3.Distance(BotOwner.Position, enemyAnchor);
            }

            foreach (BotOwner follower in boss.Followers)
            {
                if (follower == null ||
                    follower == BotOwner ||
                    follower.IsDead ||
                    follower.BotState != EBotState.Active)
                {
                    continue;
                }

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null || followerData.CoreCombatTactic != FollowerCombatTactic.Balanced)
                {
                    continue;
                }

                float navToMarksman = Utils.Utils.GetNavDistance(follower.Position, BotOwner.Position);
                if (!IsFinite(navToMarksman))
                {
                    navToMarksman = Vector3.Distance(follower.Position, BotOwner.Position);
                }

                if (navToMarksman > MarksmanRiflemanDeferMaxFollowerNavDistance)
                {
                    continue;
                }

                float riflemanToEnemyNav = Utils.Utils.GetNavDistance(follower.Position, enemyAnchor);
                if (!IsFinite(riflemanToEnemyNav))
                {
                    riflemanToEnemyNav = Vector3.Distance(follower.Position, enemyAnchor);
                }

                if (riflemanToEnemyNav <= marksmanToEnemyNav + MarksmanRiflemanDeferMaxNavDelta)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Marksman visible-contact policy: shoot if possible, otherwise commit a firing position.
        /// </summary>
        private bool TryGetVisibleDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                CombatCommon.ExtendCommittedCover();
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover,
                    "sniper.shootFromCover");
                return true;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediate = CombatCommon.TryGetImmediateShootDecision("sniper.immediateShoot");
            if (immediate != null)
            {
                if (ShouldPreserveCloseQuarterWeaponFlow(goalEnemy))
                {
                    return false;
                }

                decision = immediate.Value;
                return true;
            }

            if (CombatCommon.ShouldBreakAdvanceForImmediateFire() &&
                goalEnemy.IsVisible &&
                goalEnemy.CanShoot)
            {
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromPlace,
                    "sniper.visibleStableShoot");
                return true;
            }

            string reason = BotOwner.Memory.IsInCover ? "sniper.relocate" : "sniper.coverMove";
            if (CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    reason,
                    out string committedReason,
                    preferPointToShoot: true,
                    preferInbetween: !BotOwner.Memory.IsInCover,
                    enforceMarksmanPositionPolicy: true))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(committedReason);
                return true;
            }

            return false;
        }

        /// <summary>
        /// When a marksman is being pressured by an indirectly tracked enemy, do not idle in
        /// no-action hold. Use the shared suppress/reposition primitives instead of inventing
        /// a marksman-only blind-fire action.
        /// </summary>
        private bool TryGetIndirectThreatPressureDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!ShouldRespondToIndirectThreatPressure(goalEnemy))
            {
                return false;
            }

            if (CombatCommon.TryCreateSoftObstructedSuppressDecision(
                    goalEnemy,
                    "autoSuppress.sniper.indirectThreat",
                    out decision))
            {
                return true;
            }

            return TryGetRepositionDecision(goalEnemy, out decision);
        }

        private bool ShouldRespondToIndirectThreatPressure(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy.IsVisible ||
                goalEnemy.Distance > IndirectThreatSuppressMaxDistance ||
                !CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            return BotOwner.Memory.IsUnderFire ||
                   FollowerCombatCommon.WasHitRecently(BotOwner, IndirectThreatRecentHitSeconds) ||
                   FollowerAwareness.WasRecentlyDamaged(BotOwner) ||
                   FollowerAwareness.WasRecentlyThreatened(BotOwner);
        }

        /// <summary>
        /// Recovery is tactic-neutral in intent: if the marksman is exposed and hurt/pressured, move to cover.
        /// </summary>
        private bool TryGetRecoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
            bool forceRecovery = false,
            bool ignoreCommittedPosition = false)
        {
            decision = default;
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            bool needCover =
                forceRecovery ||
                CombatCommon.HasRecoveryPressure(1f) ||
                CombatCommon.IsFollowerCriticallyWounded() ||
                CombatCommon.IsEnemyActivelyThreateningMe(goalEnemy, 18f, 0.75f);

            if (!needCover &&
                !goalEnemy.IsVisible &&
                Time.time - goalEnemy.PersonalLastSeenTime < 2f &&
                !CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                needCover = true;
            }

            if (!needCover)
            {
                return false;
            }

            if (CombatCommon.TryGetCommittedRecoveryDecision(
                    goalEnemy,
                    out decision,
                    ignoreCommittedPosition))
            {
                return true;
            }

            decision = CombatCommon.CreateNoCoverRecoveryDecision(goalEnemy);
            return true;
        }

        /// <summary>
        /// Marksman support uses ally contact only as a firing-position cue, not as a rush/push trigger.
        /// </summary>
        private bool TryGetSniperSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
            bool allowActiveSupportPhase = false)
        {
            decision = default;
            if (supportPhase.IsActive && !allowActiveSupportPhase)
            {
                return false;
            }

            if (goalEnemy.IsVisible || BotOwner.Memory.IsUnderFire)
            {
                if (goalEnemy.CanShoot || BotOwner.Memory.IsUnderFire)
                {
                    return false;
                }
            }

            if (!CombatCommon.TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition))
            {
                return false;
            }

            if (!CombatCommon.TrySelectPreferredSupportEnemy(
                    supportEnemyProfileId,
                    supportEnemyPosition,
                    out EnemyInfo? promotedEnemy,
                    preferBackline: true))
            {
                return false;
            }

            if (!CombatCommon.HasActiveCombatEnemy(promotedEnemy))
            {
                return false;
            }

            if (!CombatCommon.TryCommitSupportFiringCover(
                    promotedEnemy,
                    "sniper.FireSupport",
                    out string coverReason,
                    preferBackline: true,
                    enforceMarksmanPositionPolicy: true))
            {
                if (!CombatCommon.TryCreateSupportFiringPositionDecision(
                        promotedEnemy,
                        supportEnemyPosition,
                        "sniper.FireSupport.position",
                        out decision,
                        preferBackline: true,
                        enforceMarksmanPositionPolicy: true,
                        allowForwardPositions: false,
                        allowBattlefieldPositions: true,
                        maxNavDistance: 90f))
                {
                    return false;
                }

                supportPhase.BeginTravel();
                return true;
            }

            supportPhase.BeginTravel();

            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            return true;
        }

        private void UpdateMarksmanCommittedHolderPhase(AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            if (nextDecision.Action != BotLogicDecision.holdPosition)
            {
                return;
            }

            if (IsSupportCommittedHoldReason(nextDecision.Reason))
            {
                if (!supportPhase.IsActive)
                {
                    supportPhase.BeginTravel();
                }

                if (supportPhase.PromoteToHoldOnArrival())
                {
                    supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                }

                return;
            }

            if (string.Equals(nextDecision.Reason, SupportPositionHoldReason, StringComparison.Ordinal))
            {
                if (!supportPhase.IsActive)
                {
                    supportPhase.BeginTravel();
                }

                if (supportPhase.PromoteToHoldOnArrival())
                {
                    supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                }

                return;
            }

            if (IsRepositionCommittedHoldReason(nextDecision.Reason) ||
                string.Equals(nextDecision.Reason, "sniper.coverHold", StringComparison.Ordinal))
            {
                if (!repositionPhase.IsActive)
                {
                    repositionPhase.BeginTravel();
                }

                if (repositionPhase.PromoteToHoldOnArrival())
                {
                    repositionPhase.StartCooldown(RepositionCooldownSeconds);
                    repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
                }
            }

            if (string.Equals(nextDecision.Reason, PositionHoldReason, StringComparison.Ordinal) ||
                string.Equals(nextDecision.Reason, NoActionHoldReason, StringComparison.Ordinal))
            {
                if (!repositionPhase.IsActive)
                {
                    repositionPhase.BeginTravel();
                }

                if (repositionPhase.PromoteToHoldOnArrival())
                {
                    float maxHoldSeconds = string.Equals(nextDecision.Reason, NoActionHoldReason, StringComparison.Ordinal)
                        ? NoActionFallbackCooldownSeconds
                        : RepositionHoldTimeoutSeconds;
                    repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, maxHoldSeconds);
                }
            }
        }

        private bool TryPrepareBreakDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
            bool beginSupportTravel,
            bool beginRepositionTravel)
        {
            if (!currentEndSourceDecision.HasValue ||
                !CombatCommon.TryPrepareDecisionTransition(
                    currentEndSourceDecision.Value,
                    "marksmanPreparedBreak",
                    decision))
            {
                return false;
            }

            repositionPhase.Clear();
            supportPhase.Clear();

            if (beginSupportTravel)
            {
                supportPhase.BeginTravel();
            }

            if (beginRepositionTravel)
            {
                repositionPhase.BeginTravel();
            }

            return true;
        }

        private bool TryPrepareImmediateShotBreak(
            EnemyInfo goalEnemy,
            string endReason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? shotDecision = null;
            if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                shotDecision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover,
                    "sniper.preparedCoverShot");
            }
            else
            {
                shotDecision = CombatCommon.TryGetImmediateShootDecision("sniper.preparedImmediateShoot");
            }

            if (!shotDecision.HasValue)
            {
                return false;
            }

            if (!TryPrepareBreakDecision(shotDecision.Value, false, false))
            {
                return false;
            }

            end = new AICoreActionEnd(endReason, true);
            return true;
        }

        private bool TryPrepareCloseMovementFightBreak(
            EnemyInfo goalEnemy,
            string endReason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot)
            {
                return false;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams> fightDecision =
                new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromPlace,
                    "sniper.closeImmediateShoot");
            if (!TryPrepareBreakDecision(fightDecision, false, false))
            {
                return false;
            }

            end = new AICoreActionEnd(endReason, true);
            return true;
        }

        private bool TryPreparePressureRecoveryBreak(
            EnemyInfo goalEnemy,
            string endReason,
            out AICoreActionEnd end)
        {
            end = default;
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            // Destination proximity is not protection. If EFT still says the bot is exposed, the
            // arrived point failed its recovery purpose and must not be recycled as the successor.
            if (CombatCommon.IsAtCommittedCoverArrival())
            {
                CombatCommon.BlockCommittedRecoveryCover("marksmanExposedAtCommittedCover");
                CombatCommon.ResetCommittedCover();
                CombatCommon.ClearCommittedPosition("marksmanExposedAtCommittedCover");
                CombatCommon.ClearCommittedMovement("marksmanExposedAtCommittedCover");
            }

            if (!TryGetRecoverDecision(
                    goalEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> recoveryDecision,
                    forceRecovery: true,
                    ignoreCommittedPosition: true))
            {
                return false;
            }

            if (!TryPrepareBreakDecision(recoveryDecision, false, false))
            {
                return false;
            }

            CombatCommon.ClearCommittedPosition("marksmanPressureRecovery");
            end = new AICoreActionEnd(endReason, true);
            return true;
        }

        private bool TryPreparePushSupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!TryGetPushSupportDecision(
                    goalEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
                    allowActiveSupportPhase: true))
            {
                return false;
            }

            if (!TryPrepareBreakDecision(decision, true, false))
            {
                return false;
            }

            end = new AICoreActionEnd(reason, true);
            return true;
        }

        private bool TryPrepareBossSupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision))
            {
                return false;
            }

            if (!TryPrepareBreakDecision(decision, true, false))
            {
                return false;
            }

            end = new AICoreActionEnd(reason, true);
            return true;
        }

        private bool TryPrepareAllySupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!TryGetSniperSupportDecision(
                    goalEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
                    allowActiveSupportPhase: true))
            {
                return false;
            }

            if (!TryPrepareBreakDecision(decision, true, false))
            {
                return false;
            }

            end = new AICoreActionEnd(reason, true);
            return true;
        }

        private bool TryPrepareSupportRefreshBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEnd end)
        {
            end = default;
            if (!TryGetSupportHoldOpportunityDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision))
            {
                return false;
            }

            if (!TryPrepareBreakDecision(decision, true, false))
            {
                return false;
            }

            end = new AICoreActionEnd(reason, true);
            return true;
        }

        private bool TryGetPushSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
            bool allowActiveSupportPhase = false)
        {
            decision = default;
            if (supportPhase.IsActive && !allowActiveSupportPhase)
            {
                return false;
            }

            if (!TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent))
            {
                return false;
            }

            // Marksman consumes the same push event as Rifleman helpers, but its support policy is
            // stricter: prefer current shot/cover, then support cover/firing position, not assault.
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!IsSameEnemy(goalEnemy, pushEvent.EnemyProfileId))
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                CombatCommon.ExtendCommittedCover();
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover,
                    "sniper.pushSupportCurrentCover");
                return true;
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediate =
                CombatCommon.TryGetImmediateShootDecision("sniper.pushSupportImmediateShoot");
            if (immediate != null)
            {
                decision = immediate.Value;
                return true;
            }

            if (pushEvent.IsSearchPush &&
                goalEnemy.Distance <= MarksmanTeamSearchAutoMaxEnemyDistance &&
                (BotOwner.Position - pushEvent.Owner.Position).sqrMagnitude <= 20f * 20f)
            {
                if (CombatCommon.TryCreateTeamSearchSupportDecision(
                        pushEvent,
                        goalEnemy,
                        "sniper.closeSearch.teamSupport",
                        out decision))
                {
                    return true;
                }
            }

            if (!CombatCommon.TryCommitMarksmanSupportCover(
                    goalEnemy,
                    pushEvent.Owner.Position,
                    pushEvent.EnemyPosition,
                    pushEvent.Destination,
                    "sniper.FireSupport.push",
                    out string coverReason))
            {
                Vector3 supportAnchor = IsFinite(pushEvent.EnemyPosition)
                    ? pushEvent.EnemyPosition
                    : pushEvent.Destination;
                if (!CombatCommon.TryCreateSupportFiringPositionDecision(
                        goalEnemy,
                        supportAnchor,
                        "sniper.FireSupport.pushPosition",
                        out decision,
                        preferBackline: true,
                        enforceMarksmanPositionPolicy: true,
                        allowForwardPositions: false,
                        allowBattlefieldPositions: true,
                        maxNavDistance: 90f))
                {
                    return false;
                }

                supportPhase.BeginTravel();
                return true;
            }

            supportPhase.BeginTravel();
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            return true;
        }

        private static bool IsSameEnemy(EnemyInfo goalEnemy, string enemyProfileId)
        {
            return !string.IsNullOrEmpty(enemyProfileId) &&
                   string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Keeps an existing committed firing position sticky once chosen.
        /// </summary>
        private bool TryGetCommittedCoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!CombatCommon.HasCommittedCover())
            {
                repositionPhase.Clear();
                supportPhase.Clear();
                return false;
            }

            if (CombatCommon.IsBotInCommittedCover())
            {
                if (repositionPhase.PromoteToHoldOnArrival())
                {
                    repositionPhase.StartCooldown(RepositionCooldownSeconds);
                    repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
                }

                bool supportArrived = supportPhase.PromoteToHoldOnArrival();
                if (supportArrived)
                {
                    supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                }

                if (supportPhase.IsHolding)
                {
                    if (HasImmediateShotFromCurrentCover(goalEnemy))
                    {
                        CombatCommon.ExtendCommittedCover();
                        decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                            BotLogicDecision.shootFromCover,
                            "sniper.committedFire");
                        return true;
                    }

                    if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (TryGetSupportHoldOpportunityDecision(goalEnemy, out decision))
                    {
                        return true;
                    }

                    if (IsSupportHoldExpired())
                    {
                        if (TryGetIdleEngagementDecision(goalEnemy, out decision))
                        {
                            return true;
                        }

                        supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                    }

                    if (supportArrived)
                    {
                        CombatCommon.HoldFor(FireSupportSettleSeconds);
                    }

                    decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                        BotLogicDecision.holdPosition,
                        FireSupportHoldReason);
                    return true;
                }

                if (HasImmediateShotFromCurrentCover(goalEnemy))
                {
                    CombatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                        BotLogicDecision.shootFromCover,
                        "sniper.committedFire");
                    return true;
                }

                if (goalEnemy.IsVisible)
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                // Reposition cover is sticky: do not re-evaluate to new covers just because
                // enemy memory or boss position moved while the follower is settled.
                if (repositionPhase.IsHolding)
                {
                    if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (TryGetRepositionHoldOpportunityDecision(goalEnemy, out decision))
                    {
                        return true;
                    }

                    if (IsRepositionHoldExpired())
                    {
                        if (TryGetIdleEngagementDecision(goalEnemy, out decision))
                        {
                            return true;
                        }

                        repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
                    }

                    CombatCommon.HoldCoverForMaxDuration();
                    decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                        BotLogicDecision.holdPosition,
                        "sniper.coverHold");
                    return true;
                }

                // Unseen break-outs should stay marksman-safe: allow relocalization/support/regroup
                // exits, but do not reuse default advance-pressure behavior for sniper tactic.
                if (CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy) &&
                    CanScanRepositionHold())
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                CombatCommon.HoldCoverForMaxDuration();
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.holdPosition,
                    "sniper.coverHold");
                return true;
            }

            CombatCommon.AssignCommittedCover();
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        /// <summary>
        /// With no active visible shot, look for a better firing position instead of default pressure.
        /// </summary>
        private bool TryGetRepositionDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (CombatCommon.IsAtCommittedCoverArrival())
            {
                if (!IsRepositionCooldownActive() &&
                    TryGetRepositionHoldOpportunityDecision(goalEnemy, out decision))
                {
                    return true;
                }

                return TryGetRepositionArrivalHold(out decision);
            }

            if (IsRepositionCooldownActive())
            {
                return false;
            }

            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.reposition",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: false,
                    enforceMarksmanPositionPolicy: true))
            {
                if (!TryCreateOwnFiringPositionDecision(goalEnemy, "sniper.position", out decision))
                {
                    return false;
                }

                repositionPhase.StartCooldown(FiringPositionCooldownSeconds);
                repositionPhase.BeginTravel();
                return true;
            }

            if (CombatCommon.IsAtCommittedCoverArrival())
            {
                return TryGetRepositionArrivalHold(out decision);
            }

            repositionPhase.BeginTravel();

            // The committed cover already has the action and mutated reason stored.
            // Just use the stored values instead of recomputing them.
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        private bool TryGetRepositionArrivalHold(
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            // Reusing an arrived cover is a hold, never a new travel phase. In particular, indirect
            // threat pressure must not bypass arrival promotion and recreate the completed move.
            CombatCommon.ClearCommittedMovement("sniperRepositionAlreadyArrived");
            CombatCommon.ArmCommittedArrivalHold("sniper.reposition");
            if (!CombatCommon.HasCommittedPosition(out decision, deferCombatBreaks: true))
            {
                return false;
            }

            return true;
        }

        private bool TryCreateOwnFiringPositionDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < nextFiringPositionAllowedTime)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            nextFiringPositionAllowedTime = Time.time + FiringPositionCooldownSeconds;
            if (!CombatCommon.TryCreateSupportFiringPositionDecision(
                    goalEnemy,
                    enemyAnchor,
                    reason,
                    out decision,
                    preferBackline: true,
                    enforceMarksmanPositionPolicy: true,
                    allowForwardPositions: false,
                    allowBattlefieldPositions: true,
                    maxNavDistance: 90f,
                    minDisplacement: 2f))
            {
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "Sniper", "engagementRejected",
                    CombatCommon.LastSupportFiringPositionRejectReason ?? "noFiringPosition");
                return false;
            }

            return true;
        }

        public AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            currentEndSourceDecision = currentDecision;

            // Explicit regroup commands should interrupt any ongoing action immediately.
            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                firingPositionArrival.Reset();
                return new AICoreActionEnd("sniperExplicitRegroup", true);
            }

            if (IsFiringPositionArrivalDecision(currentDecision.Reason))
            {
                return EndFiringPositionArrival(currentDecision);
            }

            if (IsFiringPositionArrivalTravel(currentDecision))
            {
                AICoreActionEnd travelEnd = currentDecision.Action == BotLogicDecision.runToCover
                    ? EndMarksmanCommittedRunToCover(currentDecision.Reason)
                    : CombatCommon.EndGoToPoint(endWhenEnemyVisibleShootable:
                        currentDecision.Action == BotLogicDecision.goToPointTactical ||
                        ShouldBreakMarksmanPositionMoveForVisibleThreat());
                if (travelEnd.Value && (IsMarksmanArrivalEnd(travelEnd.Reason) ||
                    string.Equals(travelEnd.Reason, "arrivedAtPoint", StringComparison.Ordinal)))
                {
                    return BeginFiringPositionArrival(currentDecision);
                }
                return travelEnd;
            }

            if (FollowerCombatCommon.IsRecoveryManeuverReason(currentDecision.Reason) &&
                FollowerCombatCommon.IsMovementDecision(currentDecision))
            {
                return EndMarksmanRecoveryMovement(currentDecision);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return EndHoldPosition(currentDecision);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire &&
                FollowerCombatCommon.IsRecoveryNoCoverReason(currentDecision.Reason))
            {
                return CombatCommon.EndRecoveryNoCoverSuppress(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.goToPoint &&
                IsMarksmanPositionMoveReason(currentDecision.Reason))
            {
                return EndMarksmanPositionMove(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.runToCover &&
                IsMarksmanCommittedTravelReason(currentDecision.Reason))
            {
                return EndMarksmanCommittedRunToCover(currentDecision.Reason);
            }

            if ((currentDecision.Action == BotLogicDecision.attackMoving ||
                 currentDecision.Action == BotLogicDecision.attackMovingWithSuppress) &&
                IsMarksmanCommittedTravelReason(currentDecision.Reason))
            {
                return EndMarksmanCommittedAttackMoving(currentDecision.Reason);
            }

            if (currentDecision.Reason != null &&
                currentDecision.Reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal))
            {
                if (!CombatCommon.HasActiveCombatEnemy())
                {
                    return new AICoreActionEnd("sniperCloseAutoNoEnemy", true);
                }
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private static bool IsFiringPositionArrivalDecision(string? reason)
        {
            return reason == "sniper.position.arrival.adjust" || reason == "sniper.position.arrival.wait" ||
                   reason == "sniper.closeSearch.arrival.adjust" || reason == "sniper.closeSearch.arrival.wait";
        }

        private static bool IsFiringPositionArrivalTravel(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            string? reason = decision.Reason;
            if (reason == null || IsFiringPositionArrivalDecision(reason) ||
                reason.StartsWith("sniper.NeedSniper", StringComparison.Ordinal)) return false;

            if (decision.Action == BotLogicDecision.goToPointTactical)
                return reason == "sniper.closeSearch" || reason == "sniper.startCloseSearch";
            if (decision.Action == BotLogicDecision.goToPoint)
                return IsMarksmanPositionMoveReason(reason);

            return decision.Action == BotLogicDecision.runToCover &&
                (reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                 reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                 reason.StartsWith("sniper.startPosition", StringComparison.Ordinal) ||
                 reason.StartsWith("sniper.coverMove", StringComparison.Ordinal) ||
                 reason.StartsWith("sniper.relocate", StringComparison.Ordinal));
        }

        private bool TryGetFiringPositionArrivalShot(
            string reason,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover, reason);
                return true;
            }
            var immediate = CombatCommon.TryGetImmediateShootDecision(reason);
            decision = immediate.GetValueOrDefault();
            return immediate.HasValue;
        }

        private bool TryPreparePendingMedicalBreak(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            if (FollowerCombatCommon.IsMedicalDecision(currentDecision) ||
                currentDecision.Reason == FollowerCombatCommon.HealRetryHoldReason ||
                !(BotOwner.Medecine?.FirstAid.Have2Do == true ||
                  BotOwner.Medecine?.SurgicalKit.HaveWork == true ||
                  BotOwner.Medecine?.FirstAid.Using == true ||
                  BotOwner.Medecine?.SurgicalKit.Using == true)) return false;

            // Keep the shared medical planner's contact/retry policy and its concrete destination.
            var medical = CombatCommon.TryGetNeedHealDecision();
            if (!medical.HasValue || !TryPrepareBreakDecision(medical.Value, false, false)) return false;
            CombatCommon.ClearCommittedPosition("sniperHoldMedicalWork");
            firingPositionArrival.Reset();
            return true;
        }

        private AICoreActionEnd BeginFiringPositionArrival(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> source)
        {
            EnemyInfo? enemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(enemy) || enemy == null)
                return new AICoreActionEnd("arrivalEnemyInvalid", true);
            if (TryPreparePendingMedicalBreak(source))
                return new AICoreActionEnd("sniperArrivalMedicalWork", true);

            bool automatic = IsAutomaticSupportIntentReason(source.Reason);
            if (!firingPositionArrival.Begin(BotOwner.Position, enemy.ProfileId, source.Reason,
                    automatic ? "sniper.closeSearch.arrival" : "sniper.position.arrival"))
                return FollowerCombatCommon.Continue();

            if (TryPrepareFiringPositionArrivalShot(out AICoreActionEnd shotEnd)) return shotEnd;

            ClearFiringPositionArrivalCommitments("sniperArrivalRecheck");
            if (CombatCommon.TryCreateLocalFiringPositionAdjustment(enemy, firingPositionArrival.Origin,
                    automatic, firingPositionArrival.MoveReason, out var adjustment) &&
                firingPositionArrival.TryAdjust(BotOwner.GoToSomePointData.Point) &&
                TryPrepareBreakDecision(adjustment, false, false))
            {
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "marksman", "arrivalAdjustmentPrepared",
                    $"source={source.Reason} origin={firingPositionArrival.Origin} point={firingPositionArrival.AdjustmentPoint}");
                return new AICoreActionEnd("sniperArrivalAdjustPrepared", true);
            }

            BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "marksman", "arrivalAdjustmentRejected",
                $"source={source.Reason} origin={firingPositionArrival.Origin} reason={CombatCommon.LastSupportFiringPositionRejectReason}");
            return PrepareFiringPositionArrivalWait();
        }

        private bool TryPrepareFiringPositionArrivalShot(out AICoreActionEnd end)
        {
            end = default;
            if (!TryGetFiringPositionArrivalShot(firingPositionArrival.ShotReason, out var shot) ||
                !TryPrepareBreakDecision(shot, false, false)) return false;

            CombatCommon.ClearCommittedPosition("sniperArrivalShot");
            CombatCommon.ClearCommittedMovement("sniperArrivalShot");
            firingPositionArrival.Reset();
            end = new AICoreActionEnd("sniperArrivalShotPrepared", true);
            return true;
        }

        private bool TryContinueFiringPositionArrival(
            EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!firingPositionArrival.IsActive && !firingPositionArrival.IsReleased) return false;
            if (!CombatCommon.HasActiveCombatEnemy(enemy) || !firingPositionArrival.MatchesEnemy(enemy.ProfileId))
            {
                ClearFiringPositionArrivalCommitments("sniperArrivalEnemyChanged");
                firingPositionArrival.Reset();
                return false;
            }
            if (firingPositionArrival.IsActive)
            {
                decision = GetFiringPositionArrivalDecision();
                return true;
            }

            // The bounded attempt is over. Real fire/recovery, then distance regroup, precede an
            // opportunistic close search that could otherwise recycle the just-failed position.
            string shotReason = firingPositionArrival.ShotReason;
            firingPositionArrival.Reset();
            if (TryGetFiringPositionArrivalShot(shotReason, out decision)) return true;
            if (TryGetRecoverDecision(enemy, out decision)) return true;
            if (!ShouldRegroupForBossDistance()) return false;
            decision = Regroup(enemy);
            return true;
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetFiringPositionArrivalDecision()
        {
            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                firingPositionArrival.IsAdjusting ? BotLogicDecision.goToPoint : BotLogicDecision.holdPosition,
                firingPositionArrival.IsAdjusting ? firingPositionArrival.MoveReason : firingPositionArrival.WaitReason);
        }

        private AICoreActionEnd PrepareFiringPositionArrivalWait()
        {
            if (!firingPositionArrival.IsWaiting)
                firingPositionArrival.BeginWait(Time.time, UnityEngine.Random.Range(1.5f, 2.5f));
            var wait = GetFiringPositionArrivalDecision();
            if (!TryPrepareBreakDecision(wait, false, false)) return FollowerCombatCommon.Continue();

            ClearFiringPositionArrivalCommitments("sniperArrivalWait");
            float remaining = Mathf.Max(0f, firingPositionArrival.WaitUntil - Time.time);
            CombatCommon.SetCommittedPosition(BotOwner.Position, wait, remaining);
            CombatCommon.HoldFor(remaining);
            BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "marksman", "arrivalWaitPrepared",
                $"source={firingPositionArrival.SourceReason} origin={firingPositionArrival.Origin} until={firingPositionArrival.WaitUntil}");
            return new AICoreActionEnd("sniperArrivalWaitPrepared", true);
        }

        private AICoreActionEnd EndFiringPositionArrival(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            EnemyInfo? enemy = BotOwner.Memory.GoalEnemy;
            if (!firingPositionArrival.Owns(current.Reason) || !CombatCommon.HasActiveCombatEnemy(enemy) ||
                enemy == null || !firingPositionArrival.MatchesEnemy(enemy.ProfileId) ||
                CombatCommon.HasActiveCombatGestureOrder())
            {
                ClearFiringPositionArrivalCommitments("sniperArrivalInterrupted");
                firingPositionArrival.Reset();
                return new AICoreActionEnd("sniperArrivalInterrupted", true);
            }
            if (TryPreparePendingMedicalBreak(current))
                return new AICoreActionEnd("sniperArrivalMedicalWork", true);
            if (TryPrepareFiringPositionArrivalShot(out AICoreActionEnd shotEnd)) return shotEnd;

            if ((BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f)) &&
                TryPreparePressureRecoveryBreak(enemy, "sniperArrivalRecoveryPrepared", out AICoreActionEnd recoveryEnd))
            {
                firingPositionArrival.Reset();
                return recoveryEnd;
            }
            if (ShouldBreakForBossUnderAttack(enemy) &&
                TryGetBossUnderAttackDecision(enemy, out var support) &&
                TryPrepareBreakDecision(support, false, false))
            {
                CombatCommon.ClearCommittedPosition("sniperArrivalBossSupport");
                firingPositionArrival.Reset();
                return new AICoreActionEnd("sniperArrivalBossSupportPrepared", true);
            }

            if (firingPositionArrival.IsAdjusting)
            {
                AICoreActionEnd moveEnd = CombatCommon.EndLocalFiringPositionAdjustment(firingPositionArrival.AdjustmentPoint);
                if (!moveEnd.Value) return moveEnd;
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "marksman", "arrivalAdjustmentEnded", moveEnd.Reason);
                // Reached, blocked, or invalidated: spend the wait, never search again here.
                return PrepareFiringPositionArrivalWait();
            }

            if (firingPositionArrival.TryRelease(Time.time))
            {
                ClearFiringPositionArrivalCommitments("sniperArrivalReleased");
                closeSearchRetryUntil = Mathf.Max(closeSearchRetryUntil, Time.time + FiringPositionCooldownSeconds);
                nextFiringPositionAllowedTime = Mathf.Max(nextFiringPositionAllowedTime, Time.time + FiringPositionCooldownSeconds);
                BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "marksman", "arrivalReleased",
                    $"source={firingPositionArrival.SourceReason} origin={firingPositionArrival.Origin}");
                return new AICoreActionEnd("sniperArrivalWaitExpired", true);
            }
            return FollowerCombatCommon.Continue();
        }

        private void ClearFiringPositionArrivalCommitments(string reason)
        {
            CombatCommon.ClearCommittedCover(reason);
            CombatCommon.ClearCommittedPosition(reason);
            CombatCommon.ClearCommittedMovement(reason);
            repositionPhase.Clear();
            supportPhase.Clear();
        }

        private AICoreActionEnd EndMarksmanRecoveryMovement(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            AICoreActionEnd end = CombatCommon.ShallEndCurrentDecision(currentDecision);
            bool closeContactBreak = end.Value &&
                (string.Equals(end.Reason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal) ||
                 string.Equals(end.Reason, "retreatPointBlankVisibleThreat", StringComparison.Ordinal));
            if (closeContactBreak)
            {
                if (goalEnemy != null &&
                    TryPrepareCloseMovementFightBreak(
                        goalEnemy,
                        "marksmanRecoveryCloseFight",
                        out AICoreActionEnd closeFightBreak))
                {
                    CombatCommon.ClearCommittedMovement("marksmanRecoveryCloseFight");
                    CombatCommon.ClearCommittedCover("marksmanRecoveryCloseFight");
                    return closeFightBreak;
                }

                // Do not drop a valid recovery move into a decision gap at contact distance. The
                // movement ends only when a concrete stationary-fire successor is prepared.
                return FollowerCombatCommon.Continue();
            }

            if (end.Value &&
                CombatCommon.HasCommittedCover() &&
                !CombatCommon.IsBotInCommittedCover() &&
                (goalEnemy == null || Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose) &&
                (string.Equals(end.Reason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal) ||
                 string.Equals(end.Reason, "stableImmediateFire", StringComparison.Ordinal) ||
                 string.Equals(end.Reason, "stationary", StringComparison.Ordinal)))
            {
                return FollowerCombatCommon.Continue();
            }

            if (end.Value)
            {
                CombatCommon.ClearCommittedMovement();
            }

            return end;
        }

        private bool TryGetActiveCommittedTravelDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;

            bool activeTravel =
                (repositionPhase.IsPendingArrival || supportPhase.IsPendingArrival) &&
                CombatCommon.HasCommittedCover() &&
                !CombatCommon.IsBotInCommittedCover();
            if (!activeTravel)
            {
                return false;
            }

            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            if (ShouldYieldCommittedTravelForImmediateCombat(goalEnemy, decision.Action))
            {
                decision = default;
                return false;
            }

            CombatCommon.AssignCommittedCover();
            return true;
        }

        private bool ShouldYieldCommittedTravelForImmediateCombat(
            EnemyInfo goalEnemy,
            BotLogicDecision moveAction)
        {
            if (moveAction == BotLogicDecision.runToCover)
            {
                return CombatCommon.ShouldBreakRunToCoverForImmediateFire();
            }

            if (moveAction == BotLogicDecision.attackMoving ||
                moveAction == BotLogicDecision.attackMovingWithSuppress ||
                moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                return CombatCommon.ShouldBreakAdvanceForImmediateFire();
            }

            return goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose;
        }

        private AICoreActionEnd EndMarksmanCommittedRunToCover(string? reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            AICoreActionEnd end = CombatCommon.EndRunToCover(reason);
            if (end.Value &&
                string.Equals(end.Reason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal))
            {
                if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                    goalEnemy == null ||
                    !TryPrepareCloseMovementFightBreak(
                        goalEnemy,
                        "marksmanCoverRunCloseFightPrepared",
                        out AICoreActionEnd preparedFightBreak))
                {
                    return FollowerCombatCommon.Continue();
                }

                CombatCommon.ClearCommittedMovement();
                CombatCommon.ClearCommittedCover("marksmanCoverRunCloseFightPrepared");
                return preparedFightBreak;
            }

            if (end.Value)
            {
                CombatCommon.ClearCommittedMovement();
            }

            return end;
        }

        private AICoreActionEnd EndMarksmanCommittedAttackMoving(string? reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("marksmanCoverMoveNoEnemy", true);
            }

            AICoreActionEnd end = CombatCommon.EndRunToCover(reason);
            if (end.Value &&
                string.Equals(end.Reason, "visibleCloseFireBreakCoverMove", StringComparison.Ordinal))
            {
                if (!TryPrepareCloseMovementFightBreak(
                        goalEnemy,
                        "marksmanCoverMoveCloseFightPrepared",
                        out AICoreActionEnd preparedFightBreak))
                {
                    return FollowerCombatCommon.Continue();
                }

                CombatCommon.ClearCommittedMovement();
                CombatCommon.ClearCommittedCover("marksmanCoverMoveCloseFightPrepared");
                return preparedFightBreak;
            }

            if (end.Value)
            {
                CombatCommon.ClearCommittedMovement();

                if (IsMarksmanArrivalEnd(end.Reason))
                {
                    if (CombatCommon.ShouldBreakAdvanceForImmediateFire())
                    {
                        return new AICoreActionEnd("marksmanCoverMoveShotReady", true);
                    }

                    ArmMarksmanTravelArrivalHold(reason);
                }
            }

            return end;
        }

        private AICoreActionEnd EndMarksmanPositionMove(string? reason)
        {
            AICoreActionEnd end = CombatCommon.EndGoToPoint(
                endWhenEnemyVisibleShootable: ShouldBreakMarksmanPositionMoveForVisibleThreat());
            if (!end.Value)
            {
                return end;
            }

            if (string.Equals(end.Reason, "arrivedAtPoint", StringComparison.Ordinal))
            {
                if (CombatCommon.ShouldBreakAdvanceForImmediateFire())
                {
                    CombatCommon.ClearCommittedPosition();
                    return new AICoreActionEnd("marksmanPositionShotReady", true);
                }

                ArmMarksmanTravelArrivalHold(reason);
                return new AICoreActionEnd("marksmanPositionArrived", true);
            }

            return end;
        }

        private void ArmMarksmanTravelArrivalHold(string? reason)
        {
            bool supportPosition = IsMarksmanSupportPositionReason(reason);
            string holdReason = supportPosition ? SupportPositionHoldReason : PositionHoldReason;
            CombatCommon.SetCommittedPosition(
                BotOwner.Position,
                new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, holdReason),
                supportPosition ? FireSupportSettleSeconds : RepositionHoldTimeoutSeconds);

            if (supportPosition)
            {
                supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                return;
            }

            repositionPhase.StartCooldown(RepositionCooldownSeconds);
            repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
        }

        private static bool IsMarksmanArrivalEnd(string? reason)
        {
            return string.Equals(reason, "alreadyInCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "arrivedCommittedCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "arrivedCoverPoint", StringComparison.Ordinal);
        }

        private bool ShouldBreakMarksmanPositionMoveForVisibleThreat()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot)
            {
                return false;
            }

            return goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() ||
                   BotOwner.Memory.IsUnderFire ||
                   FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f);
        }

        private AICoreActionEnd EndHoldPosition(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            string? reason = currentDecision.Reason;
            if (CombatCommon.HasActiveCombatGestureOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("sniperCombatGestureBreakHold", true);
            }

            if (TryPreparePendingMedicalBreak(currentDecision))
            {
                return new AICoreActionEnd("sniperHoldMedicalWork", true);
            }

            if (string.Equals(reason, FollowerCombatCommon.HealRetryHoldReason, StringComparison.Ordinal))
            {
                return EndMedicalRetryHold(currentDecision);
            }

            if (string.Equals(reason, FireSupportHoldReason, StringComparison.Ordinal))
            {
                return EndFireSupportHoldPosition();
            }

            if (string.Equals(reason, NoActionHoldReason, StringComparison.Ordinal))
            {
                return EndNoActionHold();
            }

            if (string.Equals(reason, SupportPositionHoldReason, StringComparison.Ordinal))
            {
                return EndFireSupportPositionHold();
            }

            if (string.Equals(reason, PositionHoldReason, StringComparison.Ordinal))
            {
                return EndMarksmanPositionHold();
            }

            if (string.Equals(reason, FollowerCombatCommon.RecoveryNoCoverThreatHoldReason, StringComparison.Ordinal))
            {
                return CombatCommon.EndRecoveryNoCoverThreatHold();
            }

            if (string.Equals(reason, CloseWeaponPrepareHoldReason, StringComparison.Ordinal))
            {
                return EndCloseWeaponPreparationHold();
            }

            if (CombatCommon.IsCommittedHolderReason(reason) &&
                FollowerCombatCommon.IsRecoveryManeuverReason(reason))
            {
                EnemyInfo? recoveryEnemy = BotOwner.Memory.GoalEnemy;
                if (!CombatCommon.HasActiveCombatEnemy(recoveryEnemy))
                {
                    CombatCommon.ClearCommittedPosition("sniperRecoveryHoldNoEnemy");
                    return new AICoreActionEnd("sniperRecoveryHoldNoEnemy", true);
                }

                if (TryPrepareImmediateShotBreak(recoveryEnemy!, "sniperRecoveryHoldShotReady", out var recoveryShot))
                {
                    return recoveryShot;
                }

                if (CombatCommon.WasHitAfterCommittedPosition() &&
                    TryPreparePressureRecoveryBreak(recoveryEnemy!, "sniperRecoveryHoldNewHit", out var newHitRecovery))
                {
                    return newHitRecovery;
                }

                // Keep the bounded arrival grace even when IsInCover is late. Expiry or leaving
                // the actual arrival envelope releases it for a fresh recovery decision.
                return CombatCommon.EndCommittedPositionHold(currentDecision, deferCombatBreaks: true);
            }

            if (!string.IsNullOrEmpty(reason) && reason.Contains("regroupNotNeeded"))
            {
                if (HasExplicitRegroupOrder())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEnd("sniperRegroupHoldExplicitRegroup", true);
                }

                if (ShouldRegroupForBossDistance())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEnd("sniperRegroupNowNeeded", true);
                }

                return CombatCommon.EndBaseHoldPosition(reason);
            }

            bool isHoldingInCover = IsSniperCoverHoldReason(reason) ||
                                    BotOwner.Memory.IsInCover ||
                                    CombatCommon.IsBotInCommittedCover();

            if (!isHoldingInCover)
            {
                return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
            }

            CombatCommon.ValidateCommittedCover();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEnd("sniperCoverHoldNoEnemy", true);
            }

            // Break for visible pressure only after retaining a concrete fire or recovery successor.
            if (goalEnemy.IsVisible)
            {
                if (TryPrepareImmediateShotBreak(
                        goalEnemy,
                        "sniperCoverHoldShotReady",
                        out AICoreActionEnd shotBreak))
                {
                    return shotBreak;
                }

                if (TryPreparePressureRecoveryBreak(
                        goalEnemy,
                        "sniperCoverHoldVisibleRecovery",
                        out AICoreActionEnd visibleRecoveryBreak))
                {
                    return visibleRecoveryBreak;
                }

                if (IsRepositionHoldExpired())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEnd("sniperCoverHoldVisibleExpired", true);
                }

                return FollowerCombatCommon.Continue();
            }

            // Damage pressure follows the same transactional handoff; a failed scan keeps the hold.
            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                if (TryPrepareImmediateShotBreak(
                        goalEnemy,
                        "sniperCoverHoldUnderFireShot",
                        out AICoreActionEnd underFireShotBreak))
                {
                    return underFireShotBreak;
                }

                if (TryPreparePressureRecoveryBreak(
                        goalEnemy,
                        "sniperCoverHoldUnderFireRecovery",
                        out AICoreActionEnd underFireRecoveryBreak))
                {
                    return underFireRecoveryBreak;
                }

                if (IsRepositionHoldExpired())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEnd("sniperCoverHoldUnderFireExpired", true);
                }

                return FollowerCombatCommon.Continue();
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("sniperCoverHoldExplicitRegroup", true);
            }

            // Priority 1: keep scanning for better shooting opportunities while waiting in cover.
            if (CanScanRepositionHold() && ShouldRescanShootingPosition(goalEnemy))
            {
                MarkRepositionHoldScanned();
            }

            // Break when a better shooting spot appears than the current committed hold point.
            if (CanScanRepositionHold() && HasNewShootingSpotOpportunity())
            {
                MarkRepositionHoldScanned();
                if (!TryGetRepositionHoldOpportunityDecision(
                        goalEnemy,
                        out AICoreActionResult<BotLogicDecision, CoreActionResultParams> repositionDecision))
                {
                    return FollowerCombatCommon.Continue();
                }

                if (!TryPrepareBreakDecision(repositionDecision, false, true))
                {
                    return FollowerCombatCommon.Continue();
                }

                return new AICoreActionEnd("sniperCoverHoldNewShootSpot", true);
            }

            // Priority 2: boss-under-attack only breaks when support opportunity is real
            // (shoot from current cover or bossward support cover exists).
            if (CanScanRepositionHold() &&
                TryPreparePushSupportBreak(
                    goalEnemy,
                    "sniperCoverHoldPushSupport",
                    out AICoreActionEnd pushSupportBreak))
            {
                MarkRepositionHoldScanned();
                return pushSupportBreak;
            }

            if (CanScanRepositionHold() &&
                ShouldBreakForBossSupportOpportunity(goalEnemy) &&
                TryPrepareBossSupportBreak(
                    goalEnemy,
                    "sniperCoverHoldBossUnderAttack",
                    out AICoreActionEnd bossSupportBreak))
            {
                MarkRepositionHoldScanned();
                return bossSupportBreak;
            }

            // If an ally starts a real engagement while marksman is holding in cover, break hold
            // and re-evaluate support. Boss-under-attack path still runs first and stays prioritized.
            if (CanScanRepositionHold() &&
                TryPrepareAllySupportBreak(
                    goalEnemy,
                    "sniperCoverHoldAllySupport",
                    out AICoreActionEnd allySupportBreak))
            {
                MarkRepositionHoldScanned();
                return allySupportBreak;
            }

            // Priority 3: when too far from boss, break hold so regroup objective can take over.
            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("sniperCoverHoldBossObjective", true);
            }

            if (IsRepositionHoldExpired())
            {
                // Expiry is permission to seek a different firing position, not permission to clear
                // the current cover and immediately recommit it. End only with an atomically prepared
                // replacement; otherwise retain this hold and retry after another bounded lifecycle.
                if (TryPrepareIdleEngagementBreak(goalEnemy))
                {
                    return new AICoreActionEnd("sniperCoverHoldExpiredReposition", true);
                }

                repositionPhase.StartCooldown(RepositionCooldownSeconds);
                repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
                CombatCommon.TryRenewCommittedPositionHold(currentDecision, RepositionHoldTimeoutSeconds);
                CombatCommon.HoldCoverForMaxDuration();
                return FollowerCombatCommon.Continue();
            }

            if (CombatCommon.IsCommittedHolderReason(reason))
            {
                return CombatCommon.EndCommittedPositionHold(currentDecision, deferCombatBreaks: true);
            }

            // Note: baseHoldPosition end-timeout uses EndBaseHoldPosition which respects EFT-level hold gates.
            // For tactical control, consider calling CombatCommon.HoldCoverForMaxDuration() during hold entry
            // to apply marksman-aware hold durations (10-18s based on aggression).
            return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
        }

        private AICoreActionEnd EndMedicalRetryHold(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) || goalEnemy == null)
            {
                return new AICoreActionEnd("sniperMedicalRetryCombatEnded", true);
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? dogFight =
                CombatCommon.TryGetDogFightDecision();
            if (dogFight != null &&
                CombatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "sniperMedicalRetryDogFight",
                    dogFight.Value))
            {
                return new AICoreActionEnd("sniperMedicalRetryDogFight", true);
            }

            // Pressure recovery is a fallback only after a due medical retry has had its turn.
            if (!CombatCommon.IsHealDecisionRetryBlocked)
            {
                AICoreActionResult<BotLogicDecision, CoreActionResultParams>? healDecision =
                    CombatCommon.TryGetNeedHealDecision();
                if (healDecision != null &&
                    CombatCommon.TryPrepareDecisionTransition(
                        currentDecision,
                        "sniperMedicalRetryReady",
                        healDecision.Value))
                {
                    return new AICoreActionEnd("sniperMedicalRetryReady", true);
                }
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediateShoot =
                CombatCommon.TryGetImmediateShootDecision("sniper.medicalRetryImmediateShoot");
            if (immediateShoot != null &&
                CombatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "sniperMedicalRetryImmediateShoot",
                    immediateShoot.Value))
            {
                return new AICoreActionEnd("sniperMedicalRetryImmediateShoot", true);
            }

            if (TryGetRecoverDecision(
                    goalEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> recoveryDecision) &&
                CombatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "sniperMedicalRetryDangerRecovery",
                    recoveryDecision))
            {
                return new AICoreActionEnd("sniperMedicalRetryDangerRecovery", true);
            }

            if (TryGetBossUnderAttackDecision(
                    goalEnemy,
                    out AICoreActionResult<BotLogicDecision, CoreActionResultParams> bossSupportDecision) &&
                CombatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "sniperMedicalRetryBossSupport",
                    bossSupportDecision))
            {
                return new AICoreActionEnd("sniperMedicalRetryBossSupport", true);
            }

            if (CombatCommon.IsHealDecisionRetryBlocked)
            {
                return FollowerCombatCommon.Continue();
            }

            if (!CombatCommon.HasActiveOrPendingHealWork())
            {
                return new AICoreActionEnd("sniperMedicalRetryCleared", true);
            }

            return FollowerCombatCommon.Continue();
        }

        private AICoreActionEnd EndCloseWeaponPreparationHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                (goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() &&
                 (!preparedCloseSearchDecision.HasValue || !ShouldUseOffensiveAutoSearch(goalEnemy))))
            {
                ClearCloseWeaponPreparation();
                return new AICoreActionEnd("sniperCloseWeaponPrepareNoCloseEnemy", true);
            }

            string enemyProfileId = goalEnemy.ProfileId ?? string.Empty;
            if (!string.Equals(closeWeaponPrepareEnemyProfileId, enemyProfileId, StringComparison.Ordinal))
            {
                ClearCloseWeaponPreparation();
                return new AICoreActionEnd("sniperCloseWeaponPrepareThreatChanged", true);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                AICoreActionResult<BotLogicDecision, CoreActionResultParams> fightDecision =
                    new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.closeImmediateShoot");
                if (!TryPrepareBreakDecision(fightDecision, false, false))
                {
                    return FollowerCombatCommon.Continue();
                }

                ClearCloseWeaponPreparation();
                return new AICoreActionEnd("sniperCloseWeaponPrepareContact", true);
            }

            if (CombatCommon.IsAutomaticCloseCombatWeaponReady())
            {
                if (preparedCloseSearchDecision.HasValue)
                {
                    if (!ShouldUseOffensiveAutoSearch(goalEnemy) || ShouldDeferCloseAutoToNearbyRifleman(goalEnemy) ||
                        !IsMarksmanCloseSearchDestinationSafe(goalEnemy, preparedCloseSearchPoint))
                    {
                        closeSearchRetryUntil = Time.time + FiringPositionCooldownSeconds;
                        ClearCloseWeaponPreparation();
                        return new AICoreActionEnd("sniperCloseSearchInvalidated", true);
                    }

                    BotOwner.GoToSomePointData.SetPoint(preparedCloseSearchPoint);
                    if (!TryPrepareBreakDecision(preparedCloseSearchDecision.Value, false, false))
                    {
                        return FollowerCombatCommon.Continue();
                    }
                }

                ClearCloseWeaponPreparation();
                return new AICoreActionEnd("sniperCloseWeaponReady", true);
            }

            if (!closeWeaponPreparationPending || Time.time >= closeWeaponPrepareUntil)
            {
                BlockCloseWeaponPreparationRetry();
                return new AICoreActionEnd("sniperCloseWeaponPrepareFailed", true);
            }

            CombatCommon.HoldFor(Mathf.Min(0.25f, closeWeaponPrepareUntil - Time.time));
            return FollowerCombatCommon.Continue();
        }

        private static bool IsSniperCoverHoldReason(string? reason)
        {
            return string.Equals(reason, "sniper.coverHold", StringComparison.Ordinal) ||
                   IsSupportCommittedHoldReason(reason) ||
                   IsRepositionCommittedHoldReason(reason);
        }

        private static bool IsSupportCommittedHoldReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("committedCoverHold.sniper.FireSupport", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.NeedSniper", StringComparison.Ordinal));
        }

        private static bool IsRepositionCommittedHoldReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("committedCoverHold.sniper.reposition", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.startPosition", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.coverMove", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.relocate", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.recoverCover", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.startCloseSuppress", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.sniper.closeAutoSuppress", StringComparison.Ordinal));
        }

        private static bool IsMarksmanPositionMoveReason(string? reason)
        {
            return IsMarksmanSupportPositionReason(reason) ||
                   (!string.IsNullOrEmpty(reason) &&
                    reason.StartsWith("sniper.position.", StringComparison.Ordinal));
        }

        private static bool IsMarksmanSupportPositionReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.NeedSniper", StringComparison.Ordinal)) &&
                   reason.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private AICoreActionEnd EndFireSupportHoldPosition()
        {
            CombatCommon.ValidateCommittedCover();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                supportPhase.Clear();
                return new AICoreActionEnd("fireSupportHoldNoEnemy", true);
            }

            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                if (TryPrepareImmediateShotBreak(
                        goalEnemy,
                        "fireSupportHoldUnderFireShot",
                        out AICoreActionEnd underFireShotBreak))
                {
                    return underFireShotBreak;
                }

                if (TryPreparePressureRecoveryBreak(
                        goalEnemy,
                        "fireSupportHoldUnderFireRecovery",
                        out AICoreActionEnd underFireRecoveryBreak))
                {
                    return underFireRecoveryBreak;
                }

                if (IsSupportHoldExpired())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEnd("fireSupportHoldUnderFireExpired", true);
                }

                return FollowerCombatCommon.Continue();
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("fireSupportHoldExplicitRegroup", true);
            }

            if (TryPrepareImmediateShotBreak(
                    goalEnemy,
                    "fireSupportHoldShotReady",
                    out AICoreActionEnd shotBreak))
            {
                return shotBreak;
            }

            if (CanScanSupportHold() &&
                TryPreparePushSupportBreak(
                    goalEnemy,
                    "fireSupportHoldPushSupport",
                    out AICoreActionEnd pushSupportBreak))
            {
                MarkSupportHoldScanned();
                return pushSupportBreak;
            }

            if (CanScanSupportHold() &&
                ShouldBreakForBossSupportOpportunity(goalEnemy) &&
                TryPrepareBossSupportBreak(
                    goalEnemy,
                    "fireSupportHoldBossUnderAttack",
                    out AICoreActionEnd bossSupportBreak))
            {
                MarkSupportHoldScanned();
                return bossSupportBreak;
            }

            if (CanScanSupportHold() &&
                TryPrepareAllySupportBreak(
                    goalEnemy,
                    "fireSupportHoldAllySupport",
                    out AICoreActionEnd allySupportBreak))
            {
                MarkSupportHoldScanned();
                return allySupportBreak;
            }

            if (CanScanSupportHold() &&
                TryPrepareSupportRefreshBreak(
                    goalEnemy,
                    "fireSupportHoldRefresh",
                    out AICoreActionEnd supportRefreshBreak))
            {
                MarkSupportHoldScanned();
                return supportRefreshBreak;
            }

            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEnd("fireSupportHoldBossObjective", true);
            }

            if (IsSupportHoldExpired())
            {
                if (TryPrepareIdleEngagementBreak(goalEnemy))
                {
                    return new AICoreActionEnd("fireSupportHoldEngagement", true);
                }

                supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
            }

            CombatCommon.HoldFor(FireSupportSettleSeconds);
            return FollowerCombatCommon.Continue();
        }

        private AICoreActionEnd EndFireSupportPositionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                supportPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEnd("fireSupportPositionNoEnemy", true);
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediateShot =
                CombatCommon.TryGetImmediateShootDecision("sniper.positionImmediateShoot");
            if (immediateShot.HasValue)
            {
                if (!TryPrepareBreakDecision(immediateShot.Value, false, false))
                {
                    return FollowerCombatCommon.Continue();
                }

                supportPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEnd("fireSupportPositionShotReady", true);
            }

            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                if (TryPreparePressureRecoveryBreak(
                        goalEnemy,
                        "fireSupportPositionPressureRecovery",
                        out AICoreActionEnd recoveryBreak))
                {
                    supportPhase.Clear();
                    return recoveryBreak;
                }

                return FollowerCombatCommon.Continue();
            }

            if (IsSupportHoldExpired())
            {
                if (TryPrepareIdleEngagementBreak(goalEnemy))
                {
                    return new AICoreActionEnd("fireSupportPositionEngagement", true);
                }

                supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
            }

            CombatCommon.HoldFor(FireSupportSettleSeconds);
            return default;
        }

        private AICoreActionEnd EndMarksmanPositionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEnd("marksmanPositionNoEnemy", true);
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediateShot =
                CombatCommon.TryGetImmediateShootDecision("sniper.positionImmediateShoot");
            if (immediateShot.HasValue)
            {
                if (!TryPrepareBreakDecision(immediateShot.Value, false, false))
                {
                    return FollowerCombatCommon.Continue();
                }

                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEnd("marksmanPositionShotReady", true);
            }

            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                if (TryPreparePressureRecoveryBreak(
                        goalEnemy,
                        "marksmanPositionPressureRecovery",
                        out AICoreActionEnd recoveryBreak))
                {
                    repositionPhase.Clear();
                    return recoveryBreak;
                }

                return FollowerCombatCommon.Continue();
            }

            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEnd("marksmanPositionBossObjective", true);
            }

            if (IsRepositionHoldExpired())
            {
                if (TryPrepareIdleEngagementBreak(goalEnemy))
                {
                    return new AICoreActionEnd("marksmanPositionEngagement", true);
                }

                repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
            }

            CombatCommon.HoldFor(FireSupportSettleSeconds);
            return default;
        }

        private AICoreActionEnd EndNoActionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                repositionPhase.Clear();
                return new AICoreActionEnd("sniperNoActionNoEnemy", true);
            }

            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? immediateShot =
                CombatCommon.TryGetImmediateShootDecision("sniper.noActionImmediateShoot");
            if (immediateShot.HasValue)
            {
                if (!TryPrepareBreakDecision(immediateShot.Value, false, false))
                {
                    return FollowerCombatCommon.Continue();
                }

                repositionPhase.Clear();
                return new AICoreActionEnd("sniperNoActionShotReady", true);
            }

            if (TryGetIndirectThreatPressureDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> threatPressure))
            {
                bool beginRepositionTravel = threatPressure.Action != BotLogicDecision.suppressFire;
                if (!TryPrepareBreakDecision(threatPressure, false, beginRepositionTravel))
                {
                    return FollowerCombatCommon.Continue();
                }

                return new AICoreActionEnd("sniperNoActionThreatPressure", true);
            }

            if (HasExplicitRegroupOrder() || ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                repositionPhase.Clear();
                return new AICoreActionEnd("sniperNoActionBossObjective", true);
            }

            if (Time.time >= noActionFallbackUntil || IsRepositionHoldExpired())
            {
                if (TryPrepareIdleEngagementBreak(goalEnemy))
                {
                    return new AICoreActionEnd("sniperNoActionEngagement", true);
                }

                noActionFallbackUntil = Time.time + FiringPositionCooldownSeconds;
                repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, FiringPositionCooldownSeconds);
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, noActionFallbackUntil - Time.time));
            return default;
        }


        private bool HasNewShootingSpotOpportunity()
        {
            CustomNavigationPoint? pointToShoot = CombatCommon.PointToShoot;
            if (!CombatCommon.IsCoverUsable(pointToShoot))
            {
                return false;
            }

            CustomNavigationPoint? currentCover = BotOwner.Memory?.CurCustomCoverPoint;
            if (currentCover == null)
            {
                return true;
            }

            return pointToShoot == null || pointToShoot.Id != currentCover.Id;
        }

        private static bool IsMarksmanCommittedTravelReason(string? reason)
        {
            if (reason == null)
            {
                return false;
            }

            return reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.NeedSniper", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.protectBossShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.startPosition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.coverMove", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.relocate", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.recoverCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.startCloseSuppress", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.closeAutoSuppress", StringComparison.Ordinal);
        }

        private bool HasImmediateShotFromCurrentCover(EnemyInfo goalEnemy)
        {
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return true;
            }

            return CombatCommon.TryRaiseForStandingCoverShot(out _);
        }

        private bool ShouldBreakForBossSupportOpportunity(EnemyInfo goalEnemy)
        {
            if (!ShouldBreakForBossUnderAttack(goalEnemy))
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return true;
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            return CombatCommon.TryFindCoverTowardBoss(
                goalEnemy,
                bossPosition,
                CombatDistanceConfiguration.Instance.GetBossSupportShootCoverRadius(),
                requireShootLane: true,
                requireHideFromEnemy: false,
                out _);
        }

        private bool ShouldBreakForAllyEngagementSupportOpportunity()
        {
            return CombatCommon.TryGetAllyEngagementSupportDecision() != null;
        }

        private bool ShouldBreakForPushSupportOpportunity()
        {
            return CombatCommon.TryGetActivePushEventForCurrentEnemy(out _);
        }

        private bool TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent)
        {
            return CombatCommon.TryGetActivePushEventForCurrentEnemy(out pushEvent);
        }

        private bool ShouldRescanShootingPosition(EnemyInfo goalEnemy)
        {
            if (repositionPhase.IsHolding)
            {
                return false;
            }

            if (!CombatCommon.IsCommittedCoverLockExpired)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                if (goalEnemy.CanShoot && CombatCommon.CanShootFromCurrentCover(out _))
                {
                    return true;
                }

                return true;
            }

            return CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy);
        }

        private bool ShouldBreakForBossUnderAttack(EnemyInfo goalEnemy)
        {
            return CombatCommon.ShouldBreakForBossUnderAttack(goalEnemy);
        }

        private bool ShouldBreakCommittedCoverForBossObjective(EnemyInfo goalEnemy, bool allowLockedBreak = false)
        {
            return CombatCommon.ShouldBreakCommittedCoverForBossObjective(
                goalEnemy,
                ShouldRegroupForBossDistance(),
                hasImmediateShot: HasImmediateShotFromCurrentCover(goalEnemy),
                allowMovingCommittedCoverBreak: allowLockedBreak);
        }

        private bool IsRepositionCooldownActive()
        {
            return repositionPhase.IsCooldownActive;
        }

        private bool CanScanSupportHold()
        {
            return supportPhase.CanScan;
        }

        private void MarkSupportHoldScanned()
        {
            supportPhase.MarkScanned(HoldOpportunityScanIntervalSeconds);
        }

        private bool IsSupportHoldExpired()
        {
            return supportPhase.IsHoldExpired;
        }

        private bool CanScanRepositionHold()
        {
            return repositionPhase.CanScan;
        }

        private void MarkRepositionHoldScanned()
        {
            repositionPhase.MarkScanned(HoldOpportunityScanIntervalSeconds);
        }

        private bool IsRepositionHoldExpired()
        {
            return repositionPhase.IsHoldExpired;
        }

        private bool TryGetSupportHoldOpportunityDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            int? previousCoverId = CombatCommon.CommittedCoverId;
            if (!CombatCommon.TryCommitSupportFiringCover(
                    goalEnemy,
                    "sniper.FireSupport.refresh",
                    out string coverReason,
                    preferBackline: true,
                    enforceMarksmanPositionPolicy: true) ||
                CombatCommon.CommittedCoverId == previousCoverId)
            {
                if (!CombatCommon.TryCreateSupportFiringPositionDecision(
                    goalEnemy,
                    FollowerCombatCommon.GetEnemyAnchor(goalEnemy),
                    "sniper.FireSupport.refreshPosition",
                    out decision,
                    preferBackline: true,
                    enforceMarksmanPositionPolicy: true,
                    allowForwardPositions: false,
                    allowBattlefieldPositions: true,
                    maxNavDistance: 90f))
                {
                    return false;
                }

                supportPhase.BeginTravel();
                return true;
            }

            supportPhase.BeginTravel();
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            return true;
        }

        private bool TryGetRepositionHoldOpportunityDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            if (!CombatCommon.TryReplaceCommittedFiringCover(
                    goalEnemy,
                    CombatCommon.PointToShoot,
                    "sniper.reposition.refresh",
                    out decision,
                    enforceMarksmanPositionPolicy: true))
            {
                return false;
            }

            repositionPhase.BeginTravel();
            return true;
        }

        private bool TryGetIdleEngagementDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!BotOwner.Memory.IsUnderFire && !FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) &&
                !ShouldDeferCloseAutoToNearbyRifleman(goalEnemy) && ShouldUseOffensiveAutoSearch(goalEnemy) &&
                TryCreateSafeCloseSearchDecision(goalEnemy, "sniper.closeSearch", out decision))
            {
                return true;
            }

            if (TryGetRepositionHoldOpportunityDecision(goalEnemy, out decision))
            {
                return true;
            }

            // A valid current cover is not proof of a useful firing position. Search the wider
            // candidate set without first releasing it; only an accepted distinct route replaces it.
            if (!TryCreateOwnFiringPositionDecision(goalEnemy, "sniper.position.reengage", out decision))
            {
                return false;
            }

            CombatCommon.ClearCommittedCover("sniperEngagementReposition");
            CombatCommon.ClearCommittedPosition("sniperEngagementReposition");
            repositionPhase.BeginTravel();
            return true;
        }

        private bool TryPrepareIdleEngagementBreak(EnemyInfo goalEnemy)
        {
            if (!TryGetIdleEngagementDecision(goalEnemy, out var decision) ||
                !TryPrepareBreakDecision(decision, false,
                    FollowerCombatCommon.IsMovementDecision(decision) && !IsAutomaticSupportIntentReason(decision.Reason)))
            {
                return false;
            }

            CombatCommon.ClearCommittedPosition("sniperHoldEngagement");
            BattleRecorder.RecordObjectiveDiagnostic(BotOwner, "Sniper", "engagementPrepared", decision.Reason);
            return true;
        }

        private void ClearCommittedCoverAndRepositionState()
        {
            CombatCommon.ClearCommittedCover();
            CombatCommon.ClearCommittedPosition();
            repositionPhase.Clear();
            supportPhase.Clear();
        }

        /// <summary>
        /// Computes whether the follower should switch to the regroup objective based on live boss
        /// nav distance. Regroup itself decides whether that becomes forward, backward, or lateral movement.
        /// </summary>
        private bool ShouldRegroupForBossDistance()
        {
            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float navDistance = CombatCommon.GetBossNavDistance(bossPosition);
            float directDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            if (CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directDistance, navDistance))
            {
                return false;
            }

            float followerBossDistance = FollowerCombatCommon.GetSafeRegroupDistance(navDistance, directDistance);
            float regroupTriggerDistance = CombatDistanceConfiguration.Instance.GetRegroupNeededDistanceMarksman(BotOwner);
            if (followerBossDistance <= regroupTriggerDistance)
            {
                return false;
            }

            if (CombatCommon.ShouldDeferAutonomousRegroupAfterRecentFight(
                    BotOwner.Memory.GoalEnemy,
                    followerBossDistance,
                    regroupTriggerDistance))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Guards against NaN or infinity vectors from stale game data.
        /// </summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> Regroup(
            EnemyInfo? goalEnemy = null,
            bool isExplicitOrder = false)
        {
            // Explicit player regroup commands must always activate regroup, bypassing distance checks.
            // Autonomous regroup decisions only activate when far enough from the boss.
            if (!isExplicitOrder && !ShouldRegroupForBossDistance())
            {
                return CreateNoActionFallback();
            }

            if (!isExplicitOrder &&
                CombatCommon.HasActiveCombatEnemy(goalEnemy) &&
                ShouldDeferAutonomousRegroupForFiringOpportunity(goalEnemy))
            {
                if (TryGetVisibleDecision(goalEnemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> visibleDecision))
                {
                    return visibleDecision;
                }

                return CreateNoActionFallback();
            }

            return CombatCommon.CreateRegroupObjectiveDecision();
        }

        private bool ShouldDeferAutonomousRegroupForFiringOpportunity(EnemyInfo goalEnemy)
        {
            if (ShouldBreakForBossUnderAttack(goalEnemy) || IsRegroupDistanceExtreme())
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if ((supportPhase.IsActive || repositionPhase.IsActive) &&
                Time.time - goalEnemy.PersonalSeenTime <= RegroupFiringOpportunityRecentSeenSeconds)
            {
                return true;
            }

            return false;
        }

        private bool IsRegroupDistanceExtreme()
        {
            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float navDistance = CombatCommon.GetBossNavDistance(bossPosition);
            float directDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            if (CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directDistance, navDistance))
            {
                return false;
            }

            float followerBossDistance = FollowerCombatCommon.GetSafeRegroupDistance(navDistance, directDistance);
            return CombatCommon.IsAutonomousRegroupDistanceExtreme(
                followerBossDistance,
                CombatDistanceConfiguration.Instance.GetRegroupNeededDistanceMarksman(BotOwner));
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateNoActionFallback()
        {
            if (Time.time >= noActionFallbackUntil)
            {
                noActionFallbackUntil = Time.time + NoActionFallbackCooldownSeconds;
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, noActionFallbackUntil - Time.time));
            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                BotLogicDecision.holdPosition,
                NoActionHoldReason);
        }

        private bool HasExplicitRegroupOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            return followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        private bool ClearRegroupCommand()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            if (followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                command == FollowerCommandType.RegroupNearBoss)
            {
                followerData.ClearCommand("Marksman:ignoreRegroup");
                return true;
            }

            return false;
        }

        private void ClearAggressiveRequests()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                (command == FollowerCommandType.PushEnemy ||
                 command == FollowerCommandType.SuppressEnemy))
            {
                followerData.ClearCommand(command == FollowerCommandType.PushEnemy
                    ? "Marksman:ignorePush"
                    : "Marksman:ignoreSuppress");
            }

            BotRequest? request = BotOwner.BotRequestController?.CurRequest;
            if (request == null)
            {
                return;
            }

            if (request.BotRequestType != BotRequestType.attackClose &&
                request.BotRequestType != BotRequestType.suppressionFire &&
                request.BotRequestType != BotRequestType.throwGrenade &&
                request.BotRequestType != BotRequestType.throwGrenadeFromPlace)
            {
                return;
            }

            request.Complete();
            if (BotOwner.BotRequestController != null)
            {
                BotOwner.BotRequestController.CurRequest = null;
            }
        }

        private bool TryGetBossUnderAttackDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (FollowerCombatAnchor.IsCombatIndependent(BotOwner))
            {
                return false;
            }

            // If the marksman already has a clean personal shot, taking it is the fastest support.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            AIBossPlayerLogic? bossLogic = boss.GetBossLogic();
            if (bossLogic == null || !bossLogic.IsHitted)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!CombatCommon.TryUseSupportGoalEnemy(bossEnemy, "sniper.bossUnderAttack", out EnemyInfo? prioritizedEnemy) ||
                !CombatCommon.HasActiveCombatEnemy(prioritizedEnemy))
            {
                return false;
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (CombatCommon.TryFindCoverTowardBoss(
                    prioritizedEnemy,
                    bossPosition,
                    CombatDistanceConfiguration.Instance.GetBossSupportShootCoverRadius(),
                    requireShootLane: true,
                    requireHideFromEnemy: false,
                    keepBehindBoss: true,
                    out CustomNavigationPoint? supportCover))
            {
                if (CombatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, supportCover, "sniper.protectBossShootCover"))
                {
                    supportPhase.BeginTravel();
                    decision = CombatCommon.CreateCommittedCoverMoveDecision();
                    return true;
                }
            }

            decision = Regroup(prioritizedEnemy);
            return true;
        }
    }
}
