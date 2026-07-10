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
        private const float CloseIntentRecentSeenSeconds = 2.25f;
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

        private readonly CommittedCoverPhaseState repositionPhase = new CommittedCoverPhaseState();
        private readonly CommittedCoverPhaseState supportPhase = new CommittedCoverPhaseState();
        private readonly BotOwner BotOwner;
        private readonly FollowerCombatCommon CombatCommon;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedBreakDecision;
        private float noActionFallbackUntil;
        private float nextFiringPositionAllowedTime;

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
            preparedBreakDecision = null;
            noActionFallbackUntil = 0f;
            nextFiringPositionAllowedTime = 0f;
            if (!CombatCommon.HasActiveCombatEnemy())
            {
                CombatCommon.TrySwitchBackToPrimaryFromAutomaticSecondary();
            }
        }

        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            ApplyMarksmanWeaponPolicy(BotOwner.Memory.GoalEnemy, nextDecision);
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
            UpdateMarksmanCommittedHolderPhase(nextDecision);

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
                    CombatCommon.SetInitialDecision(CombatCommon.EnemySearch("sniper.startCloseSearch"));
                    return;
                }


                AICoreActionResultStruct<BotLogicDecision, GClass26>? suppressDecision =
                    CombatCommon.TryGetAllyEngagementSupportDecision(true);
                if (suppressDecision != null)
                {
                    CombatCommon.SetInitialDecision(suppressDecision.Value);
                    return;
                }

                if (TryCreateCloseSuppressMove(goalEnemy, "sniper.startCloseSuppress", out AICoreActionResultStruct<BotLogicDecision, GClass26> closeSuppress))
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
                CombatCommon.SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    moveAction,
                    FollowerCombatCommon.CreateMovementReason("sniper.startPosition", moveAction)));
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
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
            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = TryGetMarksmanPreFightDecision(goalEnemy);
            if (preFight != null)
            {
                return preFight.Value;
            }

            if (CombatCommon.TryGetCommittedGrenadeDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedGrenade))
            {
                return committedGrenade;
            }

            if (CombatCommon.HasInitialDecision)
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26> startDecision = CombatCommon.ConsumeInitialDecision();
                return startDecision;
            }

            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return Regroup(goalEnemy);
            }

            CombatCommon.RefreshShootCover();
            CombatCommon.ValidateCommittedCover();

            if (CombatCommon.TryGetReloadRetreatDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreat))
            {
                CombatCommon.ClearInitialDecision();
                return reloadRetreat;
            }

            // Close-quarter handling is marksman policy: secondary weapon and compact movement,
            // while explicit support orders are owned by separate combat objectives.
            if (!ShouldDeferCloseAutoToNearbyRifleman(goalEnemy) &&
                TryGetCloseQuarterDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> closeQuarter))
            {
                return closeQuarter;
            }

            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recover))
            {
                return recover;
            }

            if (CombatCommon.HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPosition))
            {
                return committedPosition;
            }

            if (CombatCommon.TryGetCommittedMovementDecision(
                    goalEnemy,
                    HasExplicitRegroupOrder(),
                    false,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedMovement))
            {
                return committedMovement;
            }

            if (TryConsumePreparedBreakDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> preparedDecision))
            {
                return preparedDecision;
            }

            if (CombatCommon.TryActivateFollowerGrenade(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> grenadeDecision))
            {
                return grenadeDecision;
            }

            // Boss-under-attack support should preempt active reposition travel/hold.
            if (repositionPhase.IsActive &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
            }

            if (TryGetActiveCommittedTravelDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> activeTravel))
            {
                return activeTravel;
            }

            if (TryGetPushSupportDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> pushSupport))
            {
                return pushSupport;
            }

            if (TryGetSniperSupportDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> support))
            {
                return support;
            }

            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> bossSupport))
            {
                return bossSupport;
            }

            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visible))
            {
                return visible;
            }

            if (TryGetIndirectThreatPressureDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> threatPressure))
            {
                return threatPressure;
            }

            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committed))
            {
                return committed;
            }

            if (TryGetRepositionDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reposition))
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            bool closeEnoughForSecondary = goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
            if (!closeEnoughForSecondary)
            {
                return false;
            }

            // Face-to-face contact should favor immediate fire with the current weapon.
            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot
            )
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "sniper.closeImmediateShoot");
                return true;
            }
            else if (
                !BotOwner.Memory.GoalEnemy.HaveSeen ||
                Time.time - BotOwner.Memory.GoalEnemy.PersonalLastSeenTime > 1.5f
            )
            {
                CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat();
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                if (dogFight.Value.Action == BotLogicDecision.dogFight)
                {
                    // Dogfight aiming can be unstable for marksman at close breakouts. Prefer a
                    // stable immediate shot decision here and let close-quarter policy continue next tick.
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.closeImmediateShoot");
                    return true;
                }

                decision = dogFight.Value;
                return true;
            }


            if (ShouldUseOffensiveAutoSearch(goalEnemy))
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26> searchDecision = CombatCommon.EnemySearch("sniper.closeSearch");
                if (IsMarksmanCloseSearchDestinationSafe(goalEnemy, searchDecision))
                {
                    decision = searchDecision;
                    return true;
                }
            }

            if (!TryCreateCloseSuppressMove(goalEnemy, "sniper.closeAutoSuppress", out decision))
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
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Action != BotLogicDecision.goToPointTactical &&
                decision.Action != BotLogicDecision.goToPoint)
            {
                return true;
            }

            if (BotOwner.GoToSomePointData?.HaveTarget() != true)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 target = BotOwner.GoToSomePointData.Point;
            if (!IsFinite(enemyAnchor) || !IsFinite(target))
            {
                return false;
            }

            enemyAnchor.y = target.y;
            return (target - enemyAnchor).sqrMagnitude >=
                   MarksmanCloseSearchMinEnemyDistance * MarksmanCloseSearchMinEnemyDistance;
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
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetMarksmanPreFightDecision(EnemyInfo goalEnemy)
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = CombatCommon.PreFightLogic();
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
        /// Re-asserts marksman close-quarter weapon policy on decision handoff so generic action
        /// transitions do not flip the bot back to the sniper rifle right as close combat starts.
        /// </summary>
        private void ApplyMarksmanWeaponPolicy(
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (goalEnemy == null)
            {
                return;
            }

            if (ShouldUseCloseIntentSecondary(decision))
            {
                TryApplyCloseIntentSecondary(goalEnemy);
                return;
            }

            if (ShouldSwitchBackToPrimaryForSniperDecision(goalEnemy, decision))
            {
                TrySwitchToPrimaryForSniperDecision();
            }
        }

        private static bool ShouldUseCloseIntentSecondary(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return IsCloseIntentDecisionReason(decision.Reason);
        }

        private static bool IsCloseIntentDecisionReason(string? reason)
        {
            return reason != null &&
                   (reason.StartsWith("sniper.startClose", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.closeSearch", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal));
        }

        private bool CanUseCloseIntentSecondary(EnemyInfo goalEnemy, bool distanceIgnore = false)
        {
            if (goalEnemy == null || (!distanceIgnore && goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance()))
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalSeenTime <= CloseIntentRecentSeenSeconds;
        }

        private void TryApplyCloseIntentSecondary(EnemyInfo goalEnemy, bool distanceIgnore = false)
        {
            if (!CanUseCloseIntentSecondary(goalEnemy, distanceIgnore))
            {
                return;
            }

            CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat();
        }

        private void TrySwitchToPrimaryForSniperDecision()
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                selector.TryChangeToMain();
            }
        }

        private bool ShouldSwitchBackToPrimaryForSniperDecision(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null || selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
            {
                return false;
            }

            if (!CombatCommon.IsUsingAutomaticSecondaryOverNonAutomaticPrimary())
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
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
                if (followerData == null || followerData.CombatTactic != FollowerCombatTactic.Balanced)
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                CombatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "sniper.shootFromCover");
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? immediate = CombatCommon.TryGetImmediateShootDecision("sniper.immediateShoot");
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
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            bool needCover =
                BotOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(BotOwner, 1f) ||
                CombatCommon.IsFollowerCriticallyWounded();
            if (!needCover)
            {
                return false;
            }

            if (CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.recoverCover",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: true))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "sniper.recoverNoCover");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Marksman support uses ally contact only as a firing-position cue, not as a rush/push trigger.
        /// </summary>
        private bool TryGetSniperSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
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

        private bool TryConsumePreparedBreakDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!preparedBreakDecision.HasValue)
            {
                return false;
            }

            decision = preparedBreakDecision.Value;
            preparedBreakDecision = null;
            return true;
        }

        private void UpdateMarksmanCommittedHolderPhase(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
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

            if (IsRepositionCommittedHoldReason(nextDecision.Reason))
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
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool beginSupportTravel,
            bool beginRepositionTravel)
        {
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

            preparedBreakDecision = decision;
            return true;
        }

        private bool TryPreparePushSupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = default;
            if (!TryGetPushSupportDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
                    allowActiveSupportPhase: true))
            {
                return false;
            }

            TryPrepareBreakDecision(decision, true, false);
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryPrepareBossSupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = default;
            if (!TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> decision))
            {
                return false;
            }

            TryPrepareBreakDecision(decision, true, false);
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryPrepareAllySupportBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = default;
            if (!TryGetSniperSupportDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
                    allowActiveSupportPhase: true))
            {
                return false;
            }

            TryPrepareBreakDecision(decision, true, false);
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryPrepareSupportRefreshBreak(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionEndStruct end)
        {
            end = default;
            if (!TryGetSupportHoldOpportunityDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> decision))
            {
                return false;
            }

            TryPrepareBreakDecision(decision, true, false);
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryGetPushSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
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
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "sniper.pushSupportCurrentCover");
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? immediate =
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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

                    if (ShouldReleaseSupportHoldForOpportunity(goalEnemy) || IsSupportHoldExpired())
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (supportArrived)
                    {
                        CombatCommon.HoldFor(FireSupportSettleSeconds);
                    }

                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        FireSupportHoldReason);
                    return true;
                }

                if (HasImmediateShotFromCurrentCover(goalEnemy))
                {
                    CombatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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

                    if (ShouldReleaseRepositionHoldForOpportunity(goalEnemy) || IsRepositionHoldExpired())
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    CombatCommon.HoldCoverForMaxDuration();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
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

            repositionPhase.BeginTravel();

            // The committed cover already has the action and mutated reason stored.
            // Just use the stored values instead of recomputing them.
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        private bool TryCreateOwnFiringPositionDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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

            if (!CombatCommon.TryCreateSupportFiringPositionDecision(
                    goalEnemy,
                    enemyAnchor,
                    reason,
                    out decision,
                    preferBackline: true,
                    enforceMarksmanPositionPolicy: true,
                    allowForwardPositions: false,
                    allowBattlefieldPositions: true,
                    maxNavDistance: 90f))
            {
                return false;
            }

            nextFiringPositionAllowedTime = Time.time + FiringPositionCooldownSeconds;
            return true;
        }

        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            // Explicit regroup commands should interrupt any ongoing action immediately.
            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperExplicitRegroup", true);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return EndHoldPosition(currentDecision.Reason);
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
                    return new AICoreActionEndStruct("sniperCloseAutoNoEnemy", true);
                }
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private bool TryGetActiveCommittedTravelDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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

        private AICoreActionEndStruct EndMarksmanCommittedRunToCover(string? reason)
        {
            AICoreActionEndStruct end = CombatCommon.EndRunToCover(reason);
            if (end.Value)
            {
                CombatCommon.ClearCommittedMovement();
            }

            return end;
        }

        private AICoreActionEndStruct EndMarksmanCommittedAttackMoving(string? reason)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("marksmanCoverMoveNoEnemy", true);
            }

            AICoreActionEndStruct end = CombatCommon.EndRunToCover(reason);
            if (end.Value)
            {
                CombatCommon.ClearCommittedMovement();

                if (IsMarksmanArrivalEnd(end.Reason))
                {
                    if (CombatCommon.ShouldBreakAdvanceForImmediateFire())
                    {
                        return new AICoreActionEndStruct("marksmanCoverMoveShotReady", true);
                    }

                    ArmMarksmanTravelArrivalHold(reason);
                }
            }

            return end;
        }

        private AICoreActionEndStruct EndMarksmanPositionMove(string? reason)
        {
            AICoreActionEndStruct end = CombatCommon.EndGoToPoint(
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
                    return new AICoreActionEndStruct("marksmanPositionShotReady", true);
                }

                bool supportPosition = IsMarksmanSupportPositionReason(reason);
                string holdReason = supportPosition ? SupportPositionHoldReason : PositionHoldReason;
                CombatCommon.SetCommittedPosition(
                    BotOwner.Position,
                    new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason),
                    supportPosition ? FireSupportSettleSeconds : RepositionHoldTimeoutSeconds);

                if (supportPosition)
                {
                    supportPhase.BeginHoldLifecycle(FireSupportSettleSeconds, SupportHoldTimeoutSeconds);
                }
                else
                {
                    repositionPhase.StartCooldown(RepositionCooldownSeconds);
                    repositionPhase.BeginHoldLifecycle(FireSupportSettleSeconds, RepositionHoldTimeoutSeconds);
                }

                return new AICoreActionEndStruct("marksmanPositionArrived", true);
            }

            return end;
        }

        private void ArmMarksmanTravelArrivalHold(string? reason)
        {
            bool supportPosition = IsMarksmanSupportPositionReason(reason);
            string holdReason = supportPosition ? SupportPositionHoldReason : PositionHoldReason;
            CombatCommon.SetCommittedPosition(
                BotOwner.Position,
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason),
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

        private AICoreActionEndStruct EndHoldPosition(string? reason)
        {
            if (CombatCommon.HasActiveCombatGestureOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCombatGestureBreakHold", true);
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

            if (!string.IsNullOrEmpty(reason) && reason.Contains("regroupNotNeeded"))
            {
                if (HasExplicitRegroupOrder())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEndStruct("sniperRegroupHoldExplicitRegroup", true);
                }

                if (ShouldRegroupForBossDistance())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEndStruct("sniperRegroupNowNeeded", true);
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
                return new AICoreActionEndStruct("sniperCoverHoldNoEnemy", true);
            }

            // Break immediately when active combat pressure returns.
            if (goalEnemy.IsVisible)
            {
                if (CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
                {
                    return new AICoreActionEndStruct("sniperCoverHoldShotReady", true);
                }

                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldVisibleEnemy", true);
            }

            // If damage pressure resumes, stop waiting in hold and re-enter combat routing.
            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldUnderFire", true);
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldExplicitRegroup", true);
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
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> repositionDecision))
                {
                    return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
                }

                TryPrepareBreakDecision(repositionDecision, false, true);
                return new AICoreActionEndStruct("sniperCoverHoldNewShootSpot", true);
            }

            // Priority 2: boss-under-attack only breaks when support opportunity is real
            // (shoot from current cover or bossward support cover exists).
            if (CanScanRepositionHold() &&
                TryPreparePushSupportBreak(
                    goalEnemy,
                    "sniperCoverHoldPushSupport",
                    out AICoreActionEndStruct pushSupportBreak))
            {
                MarkRepositionHoldScanned();
                return pushSupportBreak;
            }

            if (CanScanRepositionHold() &&
                ShouldBreakForBossSupportOpportunity(goalEnemy) &&
                TryPrepareBossSupportBreak(
                    goalEnemy,
                    "sniperCoverHoldBossUnderAttack",
                    out AICoreActionEndStruct bossSupportBreak))
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
                    out AICoreActionEndStruct allySupportBreak))
            {
                MarkRepositionHoldScanned();
                return allySupportBreak;
            }

            if (IsRepositionHoldExpired())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldExpired", true);
            }

            // Priority 3: when too far from boss, break hold so regroup objective can take over.
            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldBossObjective", true);
            }

            // Note: baseHoldPosition end-timeout uses EndBaseHoldPosition which respects EFT-level hold gates.
            // For tactical control, consider calling CombatCommon.HoldCoverForMaxDuration() during hold entry
            // to apply marksman-aware hold durations (10-18s based on aggression).
            return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
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

        private AICoreActionEndStruct EndFireSupportHoldPosition()
        {
            CombatCommon.ValidateCommittedCover();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                supportPhase.Clear();
                return new AICoreActionEndStruct("fireSupportHoldNoEnemy", true);
            }

            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldUnderFire", true);
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldExplicitRegroup", true);
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return new AICoreActionEndStruct("fireSupportHoldShotReady", true);
            }

            if (CombatCommon.TryRaiseForStandingCoverShot(out _))
            {
                return new AICoreActionEndStruct("fireSupportHoldStandingShotReady", true);
            }

            if (CanScanSupportHold() &&
                TryPreparePushSupportBreak(
                    goalEnemy,
                    "fireSupportHoldPushSupport",
                    out AICoreActionEndStruct pushSupportBreak))
            {
                MarkSupportHoldScanned();
                return pushSupportBreak;
            }

            if (CanScanSupportHold() &&
                ShouldBreakForBossSupportOpportunity(goalEnemy) &&
                TryPrepareBossSupportBreak(
                    goalEnemy,
                    "fireSupportHoldBossUnderAttack",
                    out AICoreActionEndStruct bossSupportBreak))
            {
                MarkSupportHoldScanned();
                return bossSupportBreak;
            }

            if (CanScanSupportHold() &&
                TryPrepareAllySupportBreak(
                    goalEnemy,
                    "fireSupportHoldAllySupport",
                    out AICoreActionEndStruct allySupportBreak))
            {
                MarkSupportHoldScanned();
                return allySupportBreak;
            }

            if (CanScanSupportHold() &&
                TryPrepareSupportRefreshBreak(
                    goalEnemy,
                    "fireSupportHoldRefresh",
                    out AICoreActionEndStruct supportRefreshBreak))
            {
                MarkSupportHoldScanned();
                return supportRefreshBreak;
            }

            if (ShouldReleaseSupportHoldForOpportunity(goalEnemy) || IsSupportHoldExpired())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldExpired", true);
            }

            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldBossObjective", true);
            }

            AICoreActionEndStruct baseEnd = CombatCommon.EndBaseHoldPosition(FireSupportHoldReason);
            if (baseEnd.Value)
            {
                ClearCommittedCoverAndRepositionState();
            }

            return baseEnd;
        }

        private AICoreActionEndStruct EndFireSupportPositionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                supportPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("fireSupportPositionNoEnemy", true);
            }

            if (CombatCommon.TryGetImmediateShootDecision("sniper.positionImmediateShoot") != null)
            {
                supportPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("fireSupportPositionShotReady", true);
            }

            if (IsSupportHoldExpired())
            {
                supportPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("fireSupportPositionExpired", true);
            }

            CombatCommon.HoldFor(FireSupportSettleSeconds);
            return default;
        }

        private AICoreActionEndStruct EndMarksmanPositionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("marksmanPositionNoEnemy", true);
            }

            if (CombatCommon.TryGetImmediateShootDecision("sniper.positionImmediateShoot") != null)
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("marksmanPositionShotReady", true);
            }

            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("marksmanPositionBossObjective", true);
            }

            if (IsRepositionHoldExpired())
            {
                repositionPhase.Clear();
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("marksmanPositionExpired", true);
            }

            CombatCommon.HoldFor(FireSupportSettleSeconds);
            return default;
        }

        private AICoreActionEndStruct EndNoActionHold()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                repositionPhase.Clear();
                return new AICoreActionEndStruct("sniperNoActionNoEnemy", true);
            }

            if (CombatCommon.TryGetImmediateShootDecision("sniper.noActionImmediateShoot") != null)
            {
                repositionPhase.Clear();
                return new AICoreActionEndStruct("sniperNoActionShotReady", true);
            }

            if (TryGetIndirectThreatPressureDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> threatPressure))
            {
                bool beginRepositionTravel = threatPressure.Action != BotLogicDecision.suppressFire;
                TryPrepareBreakDecision(threatPressure, false, beginRepositionTravel);
                return new AICoreActionEndStruct("sniperNoActionThreatPressure", true);
            }

            if (HasExplicitRegroupOrder() || ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                repositionPhase.Clear();
                return new AICoreActionEndStruct("sniperNoActionBossObjective", true);
            }

            if (Time.time >= noActionFallbackUntil || IsRepositionHoldExpired())
            {
                repositionPhase.Clear();
                return new AICoreActionEndStruct("sniperNoActionExpired", true);
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            int? previousCoverId = CombatCommon.CommittedCoverId;
            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.reposition.refresh",
                    out _,
                    preferPointToShoot: true,
                    preferInbetween: false,
                    enforceMarksmanPositionPolicy: true) ||
                CombatCommon.CommittedCoverId == previousCoverId)
            {
                return false;
            }

            repositionPhase.BeginTravel();
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        private bool ShouldReleaseSupportHoldForOpportunity(EnemyInfo goalEnemy)
        {
            return CanScanSupportHold() &&
                   ShouldUseOffensiveAutoSearch(goalEnemy);
        }

        private bool ShouldReleaseRepositionHoldForOpportunity(EnemyInfo goalEnemy)
        {
            return CanScanRepositionHold() &&
                   ShouldUseOffensiveAutoSearch(goalEnemy);
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

            float followerBossDistance = GetSafeRegroupDistance(navDistance, directDistance);
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

        private AICoreActionResultStruct<BotLogicDecision, GClass26> Regroup(
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
                if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision))
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

            float followerBossDistance = GetSafeRegroupDistance(navDistance, directDistance);
            return CombatCommon.IsAutonomousRegroupDistanceExtreme(
                followerBossDistance,
                CombatDistanceConfiguration.Instance.GetRegroupNeededDistanceMarksman(BotOwner));
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateNoActionFallback()
        {
            if (Time.time >= noActionFallbackUntil)
            {
                noActionFallbackUntil = Time.time + NoActionFallbackCooldownSeconds;
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, noActionFallbackUntil - Time.time));
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
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

        private static float GetSafeRegroupDistance(float navDistance, float directDistance)
        {
            bool navValid = !float.IsNaN(navDistance) && !float.IsInfinity(navDistance) && navDistance > 0.1f;
            if (!navValid)
            {
                return directDistance;
            }

            // Use the conservative larger value so bad/short nav samples cannot mark far bots as "already regrouped".
            return Mathf.Max(navDistance, directDistance);
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
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
