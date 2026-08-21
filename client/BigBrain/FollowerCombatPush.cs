using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    /// <summary>
    /// Shared push planner and committed-push lifecycle. Tactics ask this class to build a
    /// pressure plan; common stores the committed decision; the tactic router decides when
    /// to ask for or honor that committed push.
    /// </summary>
    internal sealed class FollowerCombatPush
    {
        public enum PushActivationSource
        {
            Automatic,
            Ordered
        }

        private const string PushReasonPrefix = "push.";
        private const float RunToEnemyNonSprintGraceSeconds = 0.75f;
        private const float RunToEnemyNoSprintBlockSeconds = 3f;
        private const float NoPushHoldSeconds = 1.25f;
        private const string OrderedProvisionalAdvanceReason = "push.ordered.provisionalAdvance";
        private const string OrderedForwardShootCoverReason = "push.ordered.forwardShootCover";
        private const float OrderedForwardCoverScanInterval = 2f;
        private const float OrderedForwardCoverMaxNavDistance = 30f;
        private const float OrderedForwardCoverMinProgress = 2f;
        private const float OrderedForwardCoverMinDot = 0.2f;
#if DEBUG
        private const float MemoryOnlyAutoPushBlockDiagnosticInterval = 1f;
#endif
        private const int AutoPushMinMagazineAmmo = 10;
        private const int StandardAutoPushMagazineCapacity = 30;
        private const int PrecisionRifleAutoPushMagazineCapacity = 20;
        private const int ShotgunAutoPushMinMagazineAmmo = 6;
        private const float ShotgunAutoPushMaxEnemyDistance = 20f;
        private const float CautiousPushRoleThreatMultiplier = 1.1f;
        private const float CautiousPushEnemyClusterCount = 2f;

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private float runToEnemyNonSprintSince;
        private float committedPushActionableVisibleSince;
        private Vector3 stalledPushLastPosition;
        private float stalledPushSince;
        private float nextOrderedForwardCoverScanAt;
#if DEBUG
        private float nextMemoryOnlyAutoPushBlockLogAt;
#endif

        public FollowerCombatPush(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
        }

        public void Reset()
        {
            ClearCommittedPush("reset");
        }

        public void HandleDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            // Push is intentionally latched by reason, not by caller. Default can use it as
            // assault pressure while marksman can use the same commit mechanics for support
            // positioning without becoming a default rusher.
            if (IsPushCommittedDecision(nextDecision))
            {
                CommitPush(nextDecision);
                if (IsOrderedProvisionalAdvance(nextDecision))
                {
                    nextOrderedForwardCoverScanAt = Time.time + OrderedForwardCoverScanInterval;
                }
            }
            else if (combatCommon.IsCurrentGoalTemporaryEngagementTarget())
            {
                return;
            }
            else
            {
                ClearCommittedPush("decisionChanged");
            }
        }

        public bool HasCommittedPush()
        {
            return combatCommon.HasCommittedPushDecision();
        }

        public bool TryGetCommittedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasCommittedPush())
            {
                return false;
            }

            if (combatCommon.IsCommittedPushPausedByTemporaryTarget(goalEnemy))
            {
                return false;
            }

            if (ShouldInterruptCommittedPush(goalEnemy, out _))
            {
                ClearCommittedPush("committedPushInterrupted");
                return false;
            }

            return combatCommon.TryGetCommittedPushDecision(goalEnemy, out decision);
        }

        public AICoreActionEndStruct EndCommittedPush(AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (combatCommon.IsCommittedPushPausedByTemporaryTarget(goalEnemy))
            {
                return new AICoreActionEndStruct("pushTemporaryTarget", true);
            }

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) &&
                !combatCommon.TryRestoreMissionTargetIfReady("pushEndRestoreMission", out goalEnemy) &&
                !combatCommon.TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                ClearCommittedPush("pushEnemyMissingOrDead");
                return new AICoreActionEndStruct("pushEnemyMissingOrDead", true);
            }

            if (goalEnemy == null)
            {
                ClearCommittedPush("pushEnemyMissingOrDead");
                return new AICoreActionEndStruct("pushEnemyMissingOrDead", true);
            }

            combatCommon.RefreshCommittedPushEnemyRetention();

            if (ShouldInterruptCommittedPush(goalEnemy, out string interruptReason))
            {
                ClearCommittedPush(interruptReason);
                return new AICoreActionEndStruct(interruptReason, true);
            }

            if (TryPrepareOrderedClosePushTransition(currentDecision, goalEnemy, out AICoreActionEndStruct closeTransition))
            {
                return closeTransition;
            }

            if (TryPrepareOrderedForwardCoverTransition(currentDecision, goalEnemy, out AICoreActionEndStruct coverTransition))
            {
                return coverTransition;
            }

            if (currentDecision.Action == BotLogicDecision.runToEnemy &&
                !combatCommon.CanSprintForCombatMovement())
            {
                combatCommon.BlockRunToEnemy(RunToEnemyNoSprintBlockSeconds);
                ClearCommittedPush("pushRunCannotSprint");
                return new AICoreActionEndStruct("pushRunCannotSprint", true);
            }

            if (currentDecision.Action == BotLogicDecision.runToEnemy &&
                ShouldEndRunToEnemyBecauseNotSprinting())
            {
                combatCommon.BlockRunToEnemy(RunToEnemyNoSprintBlockSeconds);
                ClearCommittedPush("pushRunNotSprinting");
                return new AICoreActionEndStruct("pushRunNotSprinting", true);
            }

            if (!IsOrderedProvisionalAdvance(currentDecision) &&
                ShouldPrepareStalledPushFallback(goalEnemy, currentDecision, out string stalledReason))
            {
                ClearCommittedPush(stalledReason);
                return new AICoreActionEndStruct(stalledReason, true);
            }

            AICoreActionEndStruct endResult = currentDecision.Action switch
            {
                BotLogicDecision.runToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.goToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.runToCover => combatCommon.EndRunToCover(currentDecision.Reason),
                BotLogicDecision.goToPoint => combatCommon.EndGoToPoint(),
                BotLogicDecision.goToPointTactical => combatCommon.EndTacticalPoint(),
                BotLogicDecision.attackMoving => combatCommon.EndAttackMoving(),
                BotLogicDecision.attackMovingWithSuppress => combatCommon.EndAttackMovingWithSuppress(),
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => combatCommon.EndAttackRetreat(currentDecision.Reason),
                _ => combatCommon.ShallEndCurrentDecision(currentDecision),
            };

            if (endResult.Value)
            {
                ClearCommittedPush(endResult.Reason);
            }

            return endResult;
        }

        public void ClearCommittedPush(string? reason = null)
        {
            ReleasePushEvent(reason ?? "clearPush");
            combatCommon.ClearCommittedPushDecision(reason);
            runToEnemyNonSprintSince = 0f;
            committedPushActionableVisibleSince = 0f;
            stalledPushLastPosition = Vector3.zero;
            stalledPushSince = 0f;
            nextOrderedForwardCoverScanAt = 0f;
        }

        public bool IsPushCommittedDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (!IsPushReason(decision.Reason))
            {
                return IsStartWeakEnemyPushReason(decision.Reason);
            }

            return decision.Action == BotLogicDecision.runToEnemy ||
                   decision.Action == BotLogicDecision.goToEnemy ||
                   decision.Action == BotLogicDecision.runToCover ||
                   decision.Action == BotLogicDecision.attackMoving ||
                   decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision.Action == BotLogicDecision.goToPoint ||
                   decision.Action == BotLogicDecision.goToPointTactical ||
                   decision.Action == BotLogicDecision.search;
        }

        public static bool IsPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith(PushReasonPrefix, StringComparison.Ordinal);
        }

        public static bool IsStartWeakEnemyPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("startWeakEnemyPush", StringComparison.Ordinal);
        }

        /// <summary>
        /// Ported from old plugin EngageEnemy intent: decide push movement style after the
        /// caller has already chosen automatic or ordered push activation.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> EngageEnemy(
            PushActivationSource source,
            bool enemyLowThreat = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "engageNoEnemy");
            }

            bool pushOrdered = source == PushActivationSource.Ordered;
            if (!pushOrdered &&
                Utils.Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy))
            {
                RecordMemoryOnlyAutoPushBlocked(goalEnemy, "engageEnemy");
                return CreateMemoryOnlyAutoSearchDecision(goalEnemy);
            }

            if (!pushOrdered && combatCommon.HasActiveGrenadeLauncherSuppressNearCurrentEnemy())
            {
                return CreateNoPushDecision(goalEnemy, "launcherSuppress");
            }

            if (!pushOrdered && !combatCommon.HasPushReadyLongGun())
            {
                return CreateNoPushDecision(goalEnemy, "longGunAmmo");
            }

            if (!pushOrdered &&
                IsEnemyMarksman(goalEnemy) &&
                TryCreateMarksmanFightDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> marksmanFight))
            {
                return marksmanFight;
            }

            bool enemyVisible = goalEnemy.IsVisible;
            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(goalEnemy);
            float enemiesAtLocation = enemyLowThreat && !pushOrdered || string.IsNullOrEmpty(goalEnemy.ProfileId)
                ? 1f
                : Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition);
            bool cautiousPush = ShouldUseCautiousPushStyle(goalEnemy, pushOrdered, enemyLowThreat, enemiesAtLocation);
            cautiousPush |= combatCommon.ShouldUseCautiousWeaponThreatStyle(goalEnemy);

            if (!pushOrdered &&
                combatCommon.ShouldBlockProactiveAutoPushForWeaponThreat(goalEnemy))
            {
                if (TryCreateLowAmmoCoveredPush(goalEnemy, distanceToEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> weaponThreatDecision))
                {
                    return weaponThreatDecision;
                }

                return CreateNoPushDecision(goalEnemy, "weaponThreat");
            }

            if (!pushOrdered &&
                ShouldRestrictAutoPushForWeapon(out bool allowCloseShotgunPush) &&
                !CanUseCloseShotgunAutoPush(goalEnemy, allowCloseShotgunPush))
            {
                if (TryCreateLowAmmoCoveredPush(goalEnemy, distanceToEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> lowAmmoDecision))
                {
                    return lowAmmoDecision;
                }

                return CreateNoPushDecision(goalEnemy, "lowAmmo");
            }

            // Once push is activated, threat affects movement style, not whether ordered push exists.
            if (pushOrdered || source == PushActivationSource.Automatic || botOwner.Memory.AttackImmediately)
            {
                if (cautiousPush &&
                    TryCreateCautiousPushDecision(goalEnemy, distanceToEnemy, pushOrdered, out AICoreActionResultStruct<BotLogicDecision, GClass26> cautiousDecision))
                {
                    return cautiousDecision;
                }

                bool canRunToEnemy = combatCommon.CanSprintForCombatMovement() &&
                                     combatCommon.CanRunToEnemyNow();
                if ((distanceToEnemy <= Utils.Enemy.EnemyDistance.Close && enemiesAtLocation < 2f) ||
                    (pushOrdered && enemiesAtLocation < 4f))
                {
                    BotLogicDecision pushDecision;
                    if (pushOrdered)
                    {
                        pushDecision = canRunToEnemy
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }
                    else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }
                    else
                    {
                        pushDecision = canRunToEnemy
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }

                    if (!Utils.Enemy.IsClosestEnemy(botOwner) && distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }

                    if (!enemyVisible || pushOrdered)
                    {
                        SetAttackTactic();
                        BotLogicDecision moveDecision = enemyVisible ? BotLogicDecision.goToEnemy : pushDecision;
                        return CreatePushDecision(moveDecision);
                    }

                    if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                    {
                        CustomNavigationPoint? approachPoint = combatCommon.GetApproachableCover(
                            true,
                            avoidBossFireLane: !pushOrdered);
                        if (TryCreateApproachCoverDecision(approachPoint, out AICoreActionResultStruct<BotLogicDecision, GClass26> approachDecision))
                        {
                            return approachDecision;
                        }

                        return CreatePushDecision(BotLogicDecision.goToEnemy);
                    }

                    if (distanceToEnemy == Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    return CreatePushDecision(BotLogicDecision.goToEnemy);
                }

                // Push wanted but unsafe/imperfect conditions.
                if (enemyVisible)
                {
                    if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pushShootFromCover");
                    }

                    if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    SetAttackTactic();
                    return CreatePushDecision(BotLogicDecision.goToEnemy);
                }

                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    return CreatePushDecision(BotLogicDecision.goToEnemy);
                }

                CustomNavigationPoint? blindApproach = combatCommon.GetApproachableCover(
                    distanceToEnemy > Utils.Enemy.EnemyDistance.Mid,
                    avoidBossFireLane: !pushOrdered);
                if (TryCreateApproachCoverDecision(blindApproach, out AICoreActionResultStruct<BotLogicDecision, GClass26> blindApproachDecision))
                {
                    return blindApproachDecision;
                }

                return combatCommon.EnemySearch("push.search", pushOrdered: pushOrdered, cautious: cautiousPush);
            }

            // Old plugin "intimidation" fallback: maintain pressure from cover or hold lane.
            if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pressureShootFromCover");
            }

            if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pressureDogFight");
            }

            if (!enemyVisible && Time.time - goalEnemy.PersonalLastSeenTime < UnityEngine.Random.Range(2f, 3f))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "pressureHold");
            }

            if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
            {
                Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
                Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
                float radius = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? 120f : 40f;
                CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(
                    centerPosition,
                    radius,
                    avoidBossFireLane: true);
                if (TryCreateApproachCoverDecision(shootCover, out AICoreActionResultStruct<BotLogicDecision, GClass26> shootCoverDecision))
                {
                    return shootCoverDecision;
                }

                return combatCommon.EnemySearch("push.search");
            }

            return combatCommon.EnemySearch("push.search");
        }

        private bool TryCreateCautiousPushDecision(
            EnemyInfo goalEnemy,
            Utils.Enemy.EnemyDistance distanceToEnemy,
            bool pushOrdered,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (goalEnemy.IsVisible)
            {
                if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromCover,
                        pushOrdered ? "push.ordered.cautiousShootFromCover" : "push.cautiousShootFromCover");
                    return true;
                }

                if (goalEnemy.CanShoot)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        pushOrdered ? "push.ordered.cautiousShootFromPlace" : "push.cautiousShootFromPlace");
                    return true;
                }
            }

            CustomNavigationPoint? approachCover = combatCommon.GetApproachableCover(
                distanceToEnemy > Utils.Enemy.EnemyDistance.Mid,
                avoidBossFireLane: !pushOrdered);
            if (TryCreateApproachCoverDecision(approachCover, out decision))
            {
                return true;
            }

            if (pushOrdered && distanceToEnemy <= Utils.Enemy.EnemyDistance.Distant)
            {
                decision = CreatePushDecision(BotLogicDecision.goToEnemy);
                return true;
            }

            if (goalEnemy.IsVisible)
            {
                decision = CreatePushDecision(BotLogicDecision.goToEnemy);
                return true;
            }

            decision = combatCommon.EnemySearch(
                "push.search.cautious",
                pushOrdered: pushOrdered,
                cautious: true);
            return true;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateOrderedPushDecision(EnemyInfo goalEnemy)
        {
            if (ShouldUseOrderedForwardApproach(goalEnemy))
            {
                if (TryCreateOrderedForwardCoverDecision(
                        goalEnemy,
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> forwardCoverDecision))
                {
                    return forwardCoverDecision;
                }

                return CreateOrderedProvisionalAdvanceDecision();
            }

            return MarkOrderedPushDecision(EngageEnemy(PushActivationSource.Ordered));
        }

        private bool TryCreateOrderedForwardCoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            CustomNavigationPoint? cover = combatCommon.FindForwardShootCover(
                goalEnemy,
                OrderedForwardShootCoverReason,
                OrderedForwardCoverMaxNavDistance,
                OrderedForwardCoverMinProgress,
                OrderedForwardCoverMinDot);
            if (cover == null ||
                !combatCommon.TryCommitSelectedCombatCoverWithAction(
                    goalEnemy,
                    cover,
                    OrderedForwardShootCoverReason,
                    BotLogicDecision.attackMoving))
            {
                return false;
            }

            decision = combatCommon.CreateCommittedCoverMoveDecision();
            return decision.Action == BotLogicDecision.attackMoving;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateOrderedProvisionalAdvanceDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.goToEnemy,
                OrderedProvisionalAdvanceReason);
        }

        private bool TryPrepareOrderedClosePushTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            EnemyInfo goalEnemy,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (!IsOrderedForwardApproachDecision(currentDecision) ||
                ShouldUseOrderedForwardApproach(goalEnemy))
            {
                return false;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision =
                MarkOrderedPushDecision(EngageEnemy(PushActivationSource.Ordered));
            if (!combatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "orderedClosePushTakeover",
                    nextDecision))
            {
                return false;
            }

            if (IsOrderedForwardCoverMove(currentDecision))
            {
                combatCommon.ClearCommittedCover("orderedClosePushTakeover");
            }

            ClearCommittedPush("orderedClosePushTakeover");
            end = new AICoreActionEndStruct("orderedClosePushTakeover", true);
            return true;
        }

        private bool TryPrepareOrderedForwardCoverTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            EnemyInfo goalEnemy,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (!IsOrderedProvisionalAdvance(currentDecision) ||
                Time.time < nextOrderedForwardCoverScanAt)
            {
                return false;
            }

            nextOrderedForwardCoverScanAt = Time.time + OrderedForwardCoverScanInterval;
            if (!TryCreateOrderedForwardCoverDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision))
            {
                return false;
            }

            if (!combatCommon.TryPrepareDecisionTransition(
                    currentDecision,
                    "orderedForwardCoverAvailable",
                    nextDecision))
            {
                combatCommon.ClearCommittedCover("orderedForwardCoverTransitionRejected");
                return false;
            }

            ClearCommittedPush("orderedForwardCoverAvailable");
            end = new AICoreActionEndStruct("orderedForwardCoverAvailable", true);
            return true;
        }

        private static bool IsOrderedProvisionalAdvance(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action == BotLogicDecision.goToEnemy &&
                   string.Equals(decision.Reason, OrderedProvisionalAdvanceReason, StringComparison.Ordinal);
        }

        private static bool IsOrderedForwardCoverMove(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action == BotLogicDecision.attackMoving &&
                   decision.Reason?.StartsWith(OrderedForwardShootCoverReason, StringComparison.Ordinal) == true;
        }

        private static bool IsOrderedForwardApproachDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return IsOrderedProvisionalAdvance(decision) || IsOrderedForwardCoverMove(decision);
        }

        private static bool ShouldUseOrderedForwardApproach(EnemyInfo goalEnemy)
        {
            return Utils.Enemy.Distance(goalEnemy) > Utils.Enemy.EnemyDistance.Close;
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


        private bool TryCreateApproachCoverDecision(
            CustomNavigationPoint? cover,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (cover == null ||
                combatCommon.IsBlockedPushCover(
                    cover,
                    botOwner.Memory?.GoalEnemy,
                    "push.runToCover"))
            {
                return false;
            }

            combatCommon.AssignCover(cover);
            decision = CreatePushDecision(BotLogicDecision.runToCover);
            return true;
        }

        private bool ShouldRestrictAutoPushForWeapon(out bool allowCloseShotgunPush)
        {
            allowCloseShotgunPush = false;
            Weapon? activeWeapon = botOwner.WeaponManager?.ShootController?.Item;
            if (activeWeapon == null)
            {
                return false;
            }

            // Cylinder launchers report no conventional magazine cartridges. Their range,
            // readiness, and firing safety are owned by the Grenadier objective instead.
            if (FollowerCombatCommon.IsGrenadeLauncherWeapon(activeWeapon))
            {
                return false;
            }

            if (!FollowerCombatCommon.IsAutomaticWeapon(activeWeapon) &&
                combatCommon.HasLoadedAutomaticSecondaryForPush())
            {
                return false;
            }

            MagazineItemClass? magazine = activeWeapon.GetCurrentMagazine();
            int? magazineCount = magazine?.Cartridges?.Count;
            if (!magazineCount.HasValue)
            {
                return false;
            }

            bool lowRemainingAmmo = magazineCount.Value < AutoPushMinMagazineAmmo;
            bool lowCapacityWeapon = IsLowCapacityAutoPushWeapon(activeWeapon, magazine);
            if (!lowRemainingAmmo && !lowCapacityWeapon)
            {
                return false;
            }

            allowCloseShotgunPush = lowRemainingAmmo &&
                                    FollowerCombatCommon.IsShotgunWeapon(activeWeapon) &&
                                    magazineCount.Value >= ShotgunAutoPushMinMagazineAmmo;
            return true;
        }

        private bool IsLowCapacityAutoPushWeapon(Weapon activeWeapon, MagazineItemClass? magazine)
        {
            int magazineCapacity = magazine?.MaxCount ?? activeWeapon.GetMaxMagazineCount();
            if (magazineCapacity <= 0 || magazineCapacity >= StandardAutoPushMagazineCapacity)
            {
                return false;
            }

            if (FollowerCombatCommon.IsShotgunWeapon(activeWeapon))
            {
                return false;
            }

            // A smaller magazine is not the same as low remaining ammo for full-auto weapons.
            // Loaded ammo quality and target armor are handled by the ammo-profile threat policy.
            if (FollowerCombatCommon.IsAutomaticWeapon(activeWeapon))
            {
                return false;
            }

            return !FollowerCombatCommon.IsPrecisionRifleWeapon(activeWeapon) ||
                   magazineCapacity < PrecisionRifleAutoPushMagazineCapacity;
        }

        private bool CanUseCloseShotgunAutoPush(EnemyInfo goalEnemy, bool allowCloseShotgunPush)
        {
            if (!allowCloseShotgunPush)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!FollowerCombatCommon.IsFinite(enemyAnchor))
            {
                return false;
            }

            return (enemyAnchor - botOwner.Position).sqrMagnitude <=
                   ShotgunAutoPushMaxEnemyDistance * ShotgunAutoPushMaxEnemyDistance;
        }

        private bool ShouldUseCautiousPushStyle(
            EnemyInfo goalEnemy,
            bool pushOrdered,
            bool enemyLowThreat,
            float enemiesAtLocation)
        {
            if (goalEnemy == null)
            {
                return true;
            }

            WildSpawnType role = goalEnemy.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            float roleThreat = FollowerDeathEscapeResolver.GetRouteThreatRoleMultiplier(role);
            if (roleThreat > CautiousPushRoleThreatMultiplier)
            {
                return true;
            }

            if (enemiesAtLocation >= CautiousPushEnemyClusterCount)
            {
                return true;
            }

            return !pushOrdered && !enemyLowThreat;
        }

        private bool TryCreateLowAmmoCoveredPush(
            EnemyInfo goalEnemy,
            Utils.Enemy.EnemyDistance distanceToEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (distanceToEnemy < Utils.Enemy.EnemyDistance.Mid)
            {
                return false;
            }

            CustomNavigationPoint? cover = goalEnemy.IsVisible
                ? combatCommon.GetApproachableCover(true, avoidBossFireLane: true)
                : combatCommon.GetApproachableCover(
                    distanceToEnemy > Utils.Enemy.EnemyDistance.Mid,
                    avoidBossFireLane: true);

            return TryCreateApproachCoverDecision(cover, out decision);
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMemoryOnlyAutoSearchDecision(EnemyInfo goalEnemy)
        {
            // Memory-only contact is not authority for an assault push, but it is enough to
            // investigate cautiously. The dedicated path commits a credible last-known point;
            // it never reads the hidden enemy's live CurrPosition or delegates target refresh to
            // vanilla search logic.
            AICoreActionResultStruct<BotLogicDecision, GClass26> searchDecision =
                combatCommon.CreateMemoryOnlyEnemySearchDecision(goalEnemy, "memoryOnlyAutoSearch");
            if (searchDecision.Action != BotLogicDecision.holdPosition ||
                FollowerCombatRegroupObjective.IsRegroupActivationReason(searchDecision.Reason))
            {
                return searchDecision;
            }

            // A distant/blocked cautious search has no movement to perform. Retain the bounded
            // no-cover hold so failed search selection cannot recreate the old per-frame churn.
            return CreateNoPushDecision(goalEnemy, "memoryOnlyAutoPush");
        }

        public static bool IsMemoryOnlySearchReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("memoryOnlyAutoSearch", StringComparison.Ordinal) ||
                    reason.StartsWith("push.ordered.memorySearch", StringComparison.Ordinal));
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateNoPushDecision(EnemyInfo goalEnemy, string reasonPrefix)
        {
            if (combatCommon.CanShootFromCurrentCover(out _))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    $"{reasonPrefix}ShootFromCover");
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    $"{reasonPrefix}ShootFromPlace");
            }

            combatCommon.HoldFor(NoPushHoldSeconds);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{reasonPrefix}Hold");
        }

        public static bool IsNoPushHoldReason(string? reason)
        {
            return string.Equals(reason, "memoryOnlyAutoPushHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "launcherSuppressHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "weaponThreatHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "longGunAmmoHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "lowAmmoHold", StringComparison.Ordinal);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordMemoryOnlyAutoPushBlocked(EnemyInfo goalEnemy, string reason)
        {
#if DEBUG
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (Time.time < nextMemoryOnlyAutoPushBlockLogAt)
            {
                return;
            }

            nextMemoryOnlyAutoPushBlockLogAt = Time.time + MemoryOnlyAutoPushBlockDiagnosticInterval;
            BattleRecorder.RecordObjectiveDiagnostic(
                botOwner,
                "FollowerCombatPush",
                "blockMemoryOnlyAutoPush",
                reason,
                () => new
                {
                    targetProfileId = goalEnemy.ProfileId,
                    targetVisible = goalEnemy.IsVisible,
                    targetCanShoot = goalEnemy.CanShoot,
                    targetHaveSeen = goalEnemy.HaveSeen,
                    targetHaveSeenPersonal = goalEnemy.HaveSeenPersonal,
                    targetPersonalSeenTime = goalEnemy.PersonalSeenTime,
                    targetPersonalLastSeenTime = goalEnemy.PersonalLastSeenTime,
                    targetCause = goalEnemy.GroupInfo?.Cause.ToString(),
                    targetDistance = goalEnemy.Distance
                });
#endif
        }

        private bool TryCreateMarksmanFightDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (combatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "push.marksmanShootFromCover");
                return true;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
            CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(
                centerPosition,
                160f,
                inbetween: false,
                maxDistanceFromBot: 120f,
                avoidCrossingEnemyFront: true,
                avoidBossFireLane: true);

            if (combatCommon.TryCommitSelectedCombatCover(goalEnemy, shootCover, "push.marksmanShootCover"))
            {
                decision = combatCommon.CreateCommittedCoverMoveDecision();
                return true;
            }

            if (goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "push.marksmanShootFromPlace");
                return true;
            }

            return false;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> CreatePushDecision(BotLogicDecision action)
        {
            string suffix = action switch
            {
                BotLogicDecision.runToEnemy => "run",
                BotLogicDecision.goToEnemy => "goToEnemy",
                BotLogicDecision.attackMoving => "attackMoving",
                BotLogicDecision.attackMovingWithSuppress => "attackMovingSuppress",
                BotLogicDecision.runToCover => "runToCover",
                BotLogicDecision.goToPointTactical => "search",
                _ => action.ToString(),
            };

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(action, $"{PushReasonPrefix}{suffix}");
        }

        private void CommitPush(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            // The pusher commits locally first, then may publish a squad push event. The event is
            // only a support trigger for other followers; it is not required for this bot's push.
            combatCommon.CommitPushDecision(decision);
            combatCommon.RefreshCommittedPushEnemyRetention();
            TryEmitPushEvent(decision);
        }

        private bool ShouldInterruptCommittedPush(EnemyInfo goalEnemy, out string reason)
        {
            reason = string.Empty;

            if (combatCommon.HasImmediateExplosiveDanger())
            {
                reason = "pushExplosiveDanger";
                return true;
            }

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) &&
                !combatCommon.TryRestoreMissionTargetIfReady("pushRestoreMission", out goalEnemy) &&
                !combatCommon.TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                reason = "pushEnemyMissingOrDead";
                return true;
            }

            if (combatCommon.IsCommittedPushEnemyChanged(goalEnemy))
            {
                if (combatCommon.IsTemporaryEngagementTarget(goalEnemy))
                {
                    reason = "pushTemporaryTarget";
                    return false;
                }

                reason = "pushEnemyChanged";
                return true;
            }

            if (combatCommon.HasActiveGrenadeLauncherSuppressNearCurrentEnemy())
            {
                reason = "pushLauncherSuppress";
                return true;
            }

            if (combatCommon.TryGetCommittedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush) &&
                IsStartWeakEnemyPushReason(committedPush.Reason) &&
                combatCommon.ShouldBlockWeakEnemyRushForBossDistance(goalEnemy))
            {
                reason = "weakPushBossDistance";
                return true;
            }

            if (combatCommon.TryGetCommittedPushDecision(goalEnemy, out committedPush) &&
                combatCommon.TryPreparePointBlankDogFightDecision(goalEnemy, "pushPointBlankContactDogFight"))
            {
                reason = "pushPointBlankContact";
                return true;
            }

            if (combatCommon.TryGetCommittedPushDecision(goalEnemy, out committedPush) &&
                combatCommon.ShouldBreakCommittedPushForVisibility(
                    goalEnemy,
                    committedPush,
                    ref committedPushActionableVisibleSince))
            {
                PreparePushVisibilityFireDecision(goalEnemy);
                reason = "pushEnemyVisible";
                return true;
            }

            if (combatCommon.HasActiveOrPendingHealWork())
            {
                reason = "pushNeedHeal";
                return true;
            }

            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.5f))
            {
                reason = "pushUnderFire";
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                activeCommand != FollowerCommandType.PushEnemy)
            {
                reason = "pushCommandOverride";
                return true;
            }

            return false;
        }

        private bool ShouldEndRunToEnemyBecauseNotSprinting()
        {
            if (botOwner.Mover?.HasPathAndNoComplete != true)
            {
                runToEnemyNonSprintSince = 0f;
                return false;
            }

            if (botOwner.Mover.Sprinting)
            {
                runToEnemyNonSprintSince = 0f;
                return false;
            }

            if (runToEnemyNonSprintSince <= 0f)
            {
                runToEnemyNonSprintSince = Time.time;
                return false;
            }

            return Time.time - runToEnemyNonSprintSince >= RunToEnemyNonSprintGraceSeconds;
        }

        private bool ShouldPrepareStalledPushFallback(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            out string reason)
        {
            reason = string.Empty;
            if (currentDecision.Action != BotLogicDecision.runToEnemy &&
                currentDecision.Action != BotLogicDecision.goToEnemy)
            {
                ResetStalledPushTracking();
                return false;
            }

            if (!HasCloseObscuredPushTarget(goalEnemy))
            {
                ResetStalledPushTracking();
                return false;
            }

            if (stalledPushSince <= 0f)
            {
                stalledPushSince = Time.time;
                stalledPushLastPosition = botOwner.Position;
                return false;
            }

            if ((botOwner.Position - stalledPushLastPosition).sqrMagnitude > 0.25f * 0.25f)
            {
                stalledPushSince = Time.time;
                stalledPushLastPosition = botOwner.Position;
                return false;
            }

            if (Time.time - stalledPushSince < 1.1f)
            {
                return false;
            }

            if (!combatCommon.TryCreateSoftObstructedSuppressDecision(
                    goalEnemy,
                    "autoSuppress.pushStalled",
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> suppressDecision))
            {
                bool ordered = IsOrderedPushReason(currentDecision.Reason);
                string searchReason = ordered
                    ? "push.ordered.stalledSearch"
                    : "push.stalledSearch";
                AICoreActionResultStruct<BotLogicDecision, GClass26> searchDecision =
                    combatCommon.EnemyCoverSearch(
                        searchReason,
                        avoidBossFireLane: !ordered) ??
                    combatCommon.EnemySimpleSearch(searchReason);
                combatCommon.SetInitialDecision(searchDecision);
                ResetStalledPushTracking();
                reason = "pushStalledSearch";
                return true;
            }

            combatCommon.SetInitialDecision(suppressDecision);
            ResetStalledPushTracking();
            reason = "pushStalledSuppress";
            return true;
        }

        private bool HasCloseObscuredPushTarget(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            if (!FollowerCombatCommon.IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 toEnemy = enemyAnchor - botOwner.Position;
            toEnemy.y = 0f;
            float distance = toEnemy.magnitude;
            if (distance > 25f || distance <= 0.1f)
            {
                return false;
            }

            return true;
        }

        private void ResetStalledPushTracking()
        {
            stalledPushLastPosition = Vector3.zero;
            stalledPushSince = 0f;
        }

        private void PreparePushVisibilityFireDecision(EnemyInfo goalEnemy)
        {
            if (combatCommon.TryPrepareCloseVisibleDogFightDecision(goalEnemy, "pushVisibleDogFight"))
            {
                return;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                combatCommon.SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "pushVisibleShoot"));
                return;
            }

            if (combatCommon.TryCreateSuppressDecision(
                    goalEnemy,
                    "autoSuppress.pushVisible",
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> suppressDecision))
            {
                combatCommon.SetInitialDecision(suppressDecision);
            }
        }

        private void TryEmitPushEvent(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return;
            }

            // Helpers that are already reacting to another follower's event must not become a new
            // emitter. This keeps one push leader and N support followers.
            if (combatCommon.HasActivePushFromOther())
            {
                return;
            }

            // Boss-issued GoForward is a direct command, not an autonomous squad trigger. Otherwise
            // one ordered push would fan out into every nearby follower.
            if (IsOrderedPushReason(decision.Reason) || HasActiveOrderedPushCommand())
            {
                return;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return;
            }

            boss.CombatEvents.TryEmitPush(
                botOwner,
                goalEnemy.ProfileId,
                FollowerCombatCommon.GetEnemyAnchor(goalEnemy),
                GetPushDestination(goalEnemy),
                decision.Reason ?? string.Empty,
                IsEnemySearchPushReason(decision.Reason));
        }

        private bool HasActiveOrderedPushCommand()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                   activeCommand == FollowerCommandType.PushEnemy;
        }

        private static bool IsOrderedPushReason(string? reason)
        {
            return reason != null && reason.StartsWith("push.ordered", StringComparison.Ordinal);
        }

        private void ReleasePushEvent(string reason)
        {
            if (botOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                boss.CombatEvents.TryReleasePush(botOwner, reason);
            }
        }

        private Vector3 GetPushDestination(EnemyInfo goalEnemy)
        {
            CustomNavigationPoint? cover = botOwner.Memory?.CurCustomCoverPoint;
            if (cover != null)
            {
                return cover.Position;
            }

            if (botOwner.GoToSomePointData?.HaveTarget() == true &&
                FollowerCombatCommon.IsFinite(botOwner.GoToSomePointData.Point))
            {
                return botOwner.GoToSomePointData.Point;
            }

            return FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
        }

        private static bool IsEnemySearchPushReason(string? reason)
        {
            return IsStartWeakEnemyPushReason(reason) ||
                   (reason != null && reason.StartsWith("push.search", StringComparison.Ordinal));
        }

        private static bool IsEnemyMarksman(EnemyInfo goalEnemy)
        {
            return FollowerCombatCommon.IsEnemyMarksman(goalEnemy);
        }

        private void SetAttackTactic()
        {
            if (botOwner.Tactic.ShallReturnToAttack)
            {
                botOwner.Tactic.ShallReturnToAttack = false;
                botOwner.Tactic.ReturnToAttackTime = 0f;
            }

            botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
        }

    }
}
