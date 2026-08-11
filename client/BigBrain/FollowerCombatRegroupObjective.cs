using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatRegroupObjective : FollowerCombatObjectiveBase
    {
        // Internal trigger reason used by the default objective to request an immediate switch into
        // regroup. This should never survive long enough to become a real layer action.
        internal const string ActivateRegroupReason = "objective.regroup";
        private const float CombatRegroupOrderedDistance = 18f;
        private const float CombatRegroupOrderedDistanceMarksman = 24f;
        private const float CombatRegroupOrderedDistanceFactory = 10f;
        private const float CombatRegroupSameLevelTolerance = 1.75f;
        private const float RegroupHotContactSeconds = 2.5f;
        private const float RegroupFallbackSpreadMinRadius = 1f;
        private const float RegroupFallbackSpreadMaxRadius = 6f;
        private const float RegroupFallbackSpacing = 2f;
        private const float RegroupClaimTtlSeconds = 2f;
        private const float RegroupArrivalSettleSeconds = 1.5f;
        private const float RegroupWithdrawProgressCheckInterval = 0.5f;
        private const float RegroupWithdrawMinProgressDistance = 0.35f;
        private const float RegroupWithdrawStalledSeconds = 2f;
        internal const string RegroupReasonPrefix = "regroup.";
        private const string RegroupArrivedReason = "regroup.arrived";
        private const string RegroupUrbanDetourReason = "regroup.urbanDetour";
        private const string RegroupWithdrawBackwardReason = "regroup.withdraw.backward";
        private const string RegroupWithdrawForwardReason = "regroup.withdraw.forward";
        private const string RegroupWithdrawSideReason = "regroup.withdraw.side";
        private const float RegroupSideDotThreshold = 0.35f;

        private Vector3 currentTarget;
        private Vector3 bossSectorAnchor;
        private bool hasTarget;
        private bool hasBossSectorAnchor;
        private bool complete;
        private BotLogicDecision committedRegroupAction;
        private string? committedRegroupReason;
        private float arrivedSettleUntil;
        private float regroupActivatedAt;
        private Vector3 lastWithdrawProgressPosition;
        private float lastWithdrawProgressTargetDistance;
        private float withdrawStallStartedAt;
        private float nextWithdrawProgressCheckAt;

        private enum RegroupBossDirection
        {
            Front,
            Back,
            Side
        }

        public FollowerCombatRegroupObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            bossSectorAnchor = Vector3.zero;
            hasTarget = false;
            hasBossSectorAnchor = false;
            complete = false;
            committedRegroupAction = default;
            committedRegroupReason = null;
            arrivedSettleUntil = 0f;
            regroupActivatedAt = 0f;
            ResetWithdrawProgressTracking();
        }

        public override void Activate()
        {
            // Regroup is command-triggered but objective-owned, so each activation should discard
            // previous bossward targets and recompute from current combat geometry.
            Reset();
            regroupActivatedAt = Time.time;
        }

        public override void Deactivate()
        {
            ReleaseDestinationClaim();
            ClearCommittedRegroupMove();
            complete = false;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (IsRegroupReason(nextDecision.Reason))
            {
                // Regroup should not inherit a low combat pose from prior cover/shoot logic.
                // Bring the bot upright so backward withdraw and bossward movement read clearly.
                if (BotOwner.Mover.TargetPose < 0.85f)
                {
                    BotOwner.SetPose(1f);
                }
            }
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            // Regroup owns the movement objective, but survival work still preempts the current regroup
            // phase. The objective itself remains regroup until it completes or is explicitly replaced.
            if (TryGetMedicalInterrupt(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> medicalDecision))
            {
                return medicalDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                return dogFight.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }


            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (HasReachedBoss(bossPosition))
            {
                return GetArrivedSettleDecision();
            }

            if (ShouldAvoidUrbanDetourRegroup(bossPosition))
            {
                return GetArrivedSettleDecision(RegroupUrbanDetourReason);
            }

            if (TryGetCommittedRegroupMove(goalEnemy, bossPosition, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedMove))
            {
                return committedMove;
            }

            // The regroup movement style depends on where the boss is from this bot's perspective.
            // Back means withdraw while staying oriented to the threat; front/side can use the normal
            // attack-moving action because our attack-moving look helper keeps aim on the threat lane.
            RegroupBossDirection bossDirection = GetBossDirectionFromBot(goalEnemy, bossPosition);
            bool hotContact = IsHotEnemyContact(goalEnemy);

            // While contact is still hot, regroup stays combat-active and moves bossward without
            // dropping threat orientation.
            if (hotContact)
            {
                if (TryGetRegroupCombatMove(goalEnemy, bossPosition, bossDirection, out AICoreActionResultStruct<BotLogicDecision, GClass26> combatMove))
                {
                    CommitRegroupMove(combatMove);
                    return combatMove;
                }
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> runDecision = GetRegroupRunDecision(bossPosition);
            CommitRegroupMove(runDecision);
            return runDecision;
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            // A generic action such as attackMoving can still be a medical retreat. Route by
            // action + reason before applying regroup completion to the shared movement action.
            if (FollowerCombatCommon.IsMedicalDecision(currentDecision))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearCommittedRegroupMove();
                return new AICoreActionEndStruct("regroupEnemyMissing", true);
            }

            if (IsRegroupSettleReason(currentDecision.Reason))
            {
                if (Time.time < arrivedSettleUntil)
                {
                    return default;
                }

                complete = true;
                ClearCommittedRegroupMove();
                return new AICoreActionEndStruct("regroupArrivedSettled", true);
            }

            if (HasReachedBoss(CombatCommon.GetBossPosition()))
            {
                ClearCommittedRegroupMove();
                if (arrivedSettleUntil <= 0f)
                {
                    arrivedSettleUntil = Time.time + RegroupArrivalSettleSeconds;
                }

                return new AICoreActionEndStruct("regroupReachedBossSettle", true);
            }

            if (HasActivePushOrder())
            {
                complete = true;
                ClearCommittedRegroupMove();
                return new AICoreActionEndStruct("regroupPushOverride", true);
            }

            if (HasActiveSuppressOrder())
            {
                complete = true;
                ClearCommittedRegroupMove();
                return new AICoreActionEndStruct("regroupSuppressOverride", true);
            }

            if (currentDecision.Reason != null && currentDecision.Reason.StartsWith("regroup.withdraw", System.StringComparison.Ordinal))
            {
                if (CombatCommon.TryGetNeedHealDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupNeedHeal", true);
                }

                if (CombatCommon.TryGetDogFightDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupDogFight", true);
                }

                if (HasReachedCurrentTarget())
                {
                    ClearCurrentTarget();
                    ClearCommittedRegroupMove();
                    return new AICoreActionEndStruct("regroupReachedWithdrawTarget", true);
                }

                if (TryGetStalledWithdrawEnd(CombatCommon.GetBossPosition(), out AICoreActionEndStruct stalledWithdrawEnd))
                {
                    ClearCurrentTarget();
                    ClearCommittedRegroupMove();
                    if (stalledWithdrawEnd.Reason == "regroupWithdrawStalledNearBoss" && arrivedSettleUntil <= 0f)
                    {
                        arrivedSettleUntil = Time.time + RegroupArrivalSettleSeconds;
                    }

                    return stalledWithdrawEnd;
                }

                if (ShouldReturnMarksmanToSupport(goalEnemy))
                {
                    complete = true;
                    ClearCurrentTarget();
                    ClearCommittedRegroupMove();
                    return new AICoreActionEndStruct("regroupMarksmanSupportOpportunity", true);
                }

                return default;
            }

            if (currentDecision.Reason != null && currentDecision.Reason.StartsWith("regroup.run", System.StringComparison.Ordinal))
            {
                if (CombatCommon.TryGetNeedHealDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupNeedHeal", true);
                }

                if (CombatCommon.TryGetDogFightDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupDogFight", true);
                }

                if (ShouldReturnMarksmanToSupport(goalEnemy))
                {
                    complete = true;
                    ClearCurrentTarget();
                    ClearCommittedRegroupMove();
                    return new AICoreActionEndStruct("regroupMarksmanSupportOpportunity", true);
                }

                return default;
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> GetArrivedSettleDecision(string reason = RegroupArrivedReason)
        {
            ClearCurrentTarget();
            ClearCommittedRegroupMove();
            if (arrivedSettleUntil <= 0f)
            {
                arrivedSettleUntil = Time.time + RegroupArrivalSettleSeconds;
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, arrivedSettleUntil - Time.time));
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, reason);
        }

        private bool TryGetCommittedRegroupMove(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (committedRegroupAction == default || string.IsNullOrEmpty(committedRegroupReason))
            {
                return false;
            }

            RefreshCommittedRegroupTargetIfBossMoved(goalEnemy, bossPosition);

            if (IsWithdrawReason(committedRegroupReason) && !IsHotEnemyContact(goalEnemy))
            {
                ClearCurrentTarget();
                ClearCommittedRegroupMove();
                return false;
            }

            if (hasTarget)
            {
                if (HasReachedCurrentTarget())
                {
                    ClearCurrentTarget();
                    ClearCommittedRegroupMove();
                    return false;
                }

                BotOwner.GoToSomePointData.SetPoint(currentTarget);
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedRegroupAction, committedRegroupReason);
            return true;
        }

        private void RefreshCommittedRegroupTargetIfBossMoved(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            if (!HasBossSectorChanged(bossPosition))
            {
                return;
            }

            bossSectorAnchor = bossPosition;
            hasBossSectorAnchor = true;

            if (!IsWithdrawReason(committedRegroupReason))
            {
                return;
            }

            Vector3 previousTarget = currentTarget;
            RegroupBossDirection bossDirection = GetBossDirectionFromRegroupReason(committedRegroupReason);
            if (!TryAssignRegroupCover(goalEnemy, bossPosition, bossDirection, out Vector3 targetPosition))
            {
                targetPosition = GetFallbackBossDestination(bossPosition);
            }

            currentTarget = targetPosition;
            hasTarget = true;
            UpsertDestinationClaim(currentTarget);
            BotOwner.GoToSomePointData.SetPoint(currentTarget);

            if ((previousTarget - currentTarget).sqrMagnitude > 1f)
            {
                StartWithdrawProgressTracking();
            }
        }

        private void CommitRegroupMove(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (!IsRegroupReason(decision.Reason) || decision.Action == BotLogicDecision.holdPosition)
            {
                return;
            }

            committedRegroupAction = decision.Action;
            committedRegroupReason = decision.Reason;
            arrivedSettleUntil = 0f;
            if (IsWithdrawReason(decision.Reason))
            {
                StartWithdrawProgressTracking();
            }
            else
            {
                ResetWithdrawProgressTracking();
            }
        }

        private void ClearCommittedRegroupMove()
        {
            committedRegroupAction = default;
            committedRegroupReason = null;
            ResetWithdrawProgressTracking();
        }

        private void StartWithdrawProgressTracking()
        {
            lastWithdrawProgressPosition = BotOwner.Position;
            lastWithdrawProgressTargetDistance = hasTarget
                ? (BotOwner.Position - currentTarget).magnitude
                : 0f;
            withdrawStallStartedAt = 0f;
            nextWithdrawProgressCheckAt = Time.time + RegroupWithdrawProgressCheckInterval;
        }

        private void ResetWithdrawProgressTracking()
        {
            lastWithdrawProgressPosition = Vector3.zero;
            lastWithdrawProgressTargetDistance = 0f;
            withdrawStallStartedAt = 0f;
            nextWithdrawProgressCheckAt = 0f;
        }

        private bool TryGetStalledWithdrawEnd(Vector3 bossPosition, out AICoreActionEndStruct end)
        {
            end = default;
            if (Time.time < nextWithdrawProgressCheckAt)
            {
                return false;
            }

            nextWithdrawProgressCheckAt = Time.time + RegroupWithdrawProgressCheckInterval;
            float movedDistance = (BotOwner.Position - lastWithdrawProgressPosition).magnitude;
            float targetDistance = hasTarget
                ? (BotOwner.Position - currentTarget).magnitude
                : 0f;
            bool progressed =
                movedDistance >= RegroupWithdrawMinProgressDistance ||
                (hasTarget && targetDistance <= lastWithdrawProgressTargetDistance - RegroupWithdrawMinProgressDistance);

            if (progressed)
            {
                lastWithdrawProgressPosition = BotOwner.Position;
                lastWithdrawProgressTargetDistance = targetDistance;
                withdrawStallStartedAt = 0f;
                return false;
            }

            if (withdrawStallStartedAt <= 0f)
            {
                withdrawStallStartedAt = Time.time;
                return false;
            }

            if (Time.time - withdrawStallStartedAt < RegroupWithdrawStalledSeconds)
            {
                return false;
            }

            string reason = IsWithinHotRegroupSettleEnvelope(bossPosition)
                ? "regroupWithdrawStalledNearBoss"
                : "regroupWithdrawStalledRepath";
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        private bool TryGetMedicalInterrupt(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // Reuse the shared heal decision tree, but let regroup remain the owning objective.
            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision == null)
            {
                return false;
            }

            decision = healDecision.Value;
            return true;
        }

        private bool TryGetRegroupCombatMove(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            RegroupBossDirection bossDirection,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (TryAssignRegroupCover(goalEnemy, bossPosition, bossDirection, out Vector3 targetPosition))
            {
                // Keep a single bossward target for the current regroup phase so the action can keep
                // moving continuously instead of re-picking a slightly different point every update.
                currentTarget = targetPosition;
                bossSectorAnchor = bossPosition;
                hasTarget = true;
                hasBossSectorAnchor = true;
                UpsertDestinationClaim(targetPosition);
                BotOwner.GoToSomePointData.SetPoint(targetPosition);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.attackMoving,
                    GetWithdrawReason(bossDirection));
                return true;
            }

            currentTarget = GetFallbackBossDestination(bossPosition);
            bossSectorAnchor = bossPosition;
            hasTarget = true;
            hasBossSectorAnchor = true;
            UpsertDestinationClaim(currentTarget);
            BotOwner.GoToSomePointData.SetPoint(currentTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.attackMoving,
                GetWithdrawReason(bossDirection));
            return true;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> GetRegroupRunDecision(Vector3 bossPosition)
        {
            // Once contact cools off, regroup becomes a pure "close back to boss" objective.
            // Do not select the destination here. CombatRegroupRunAction owns one cached run
            // target from Start() until the action reaches it, so repeated decisions cannot
            // keep nudging the point while the bot is already running.
            if (!hasBossSectorAnchor || HasBossSectorChanged(bossPosition))
            {
                bossSectorAnchor = bossPosition;
                hasBossSectorAnchor = true;
            }

            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            hasTarget = false;
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "regroup.run");
        }

        private bool TryAssignRegroupCover(EnemyInfo goalEnemy, Vector3 bossPosition, RegroupBossDirection bossDirection, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            float regroupCoverRadius = GetRegroupCoverRadius();

            if (bossDirection == RegroupBossDirection.Back)
            {
                // True withdrawal: prefer bossward cover that hides from the enemy over cover that only
                // provides a shooting lane.
                if (CombatCommon.TryFindCoverTowardBoss(
                    goalEnemy,
                    bossPosition,
                    regroupCoverRadius,
                    requireShootLane: false,
                    requireHideFromEnemy: true,
                    out CustomNavigationPoint? withdrawCover) &&
                    TryAcceptRegroupCover(withdrawCover, bossPosition, out targetPosition))
                {
                    CombatCommon.AssignCover(withdrawCover);
                    return true;
                }
            }
            else
            {
                // Boss is ahead or lateral, so regroup is a rejoin under contact. Prefer bossward cover
                // that still preserves a shot while moving up or across.
                if (CombatCommon.TryFindCoverTowardBoss(
                    goalEnemy,
                    bossPosition,
                    regroupCoverRadius,
                    requireShootLane: true,
                    requireHideFromEnemy: false,
                    out CustomNavigationPoint? supportCover) &&
                    TryAcceptRegroupCover(supportCover, bossPosition, bossDirection, out targetPosition))
                {
                    CombatCommon.AssignCover(supportCover);
                    return true;
                }
            }

            if (CombatCommon.TryFindBossCover(goalEnemy, bossPosition, regroupCoverRadius, out CustomNavigationPoint? bossCover) &&
                TryAcceptRegroupCover(bossCover, bossPosition, bossDirection, out targetPosition))
            {
                CombatCommon.AssignCover(bossCover);
                return true;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                targetPosition = bossHit.position;
                return true;
            }

            targetPosition = bossPosition;
            return true;
        }

        private bool IsHotEnemyContact(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                return true;
            }

            // Recently seen contact keeps regroup in its combat-withdraw phase for a short window before
            // collapsing into a pure run-back-to-boss phase.
            return Time.time - goalEnemy.PersonalLastSeenTime < RegroupHotContactSeconds;
        }

        private RegroupBossDirection GetBossDirectionFromBot(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 toEnemy = enemyAnchor - BotOwner.Position;
            Vector3 toBoss = bossPosition - BotOwner.Position;
            toEnemy.y = 0f;
            toBoss.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.01f || toBoss.sqrMagnitude <= 0.01f)
            {
                return RegroupBossDirection.Front;
            }

            float dot = Vector3.Dot(toEnemy.normalized, toBoss.normalized);
            if (dot <= -RegroupSideDotThreshold)
            {
                return RegroupBossDirection.Back;
            }

            if (dot >= RegroupSideDotThreshold)
            {
                return RegroupBossDirection.Front;
            }

            return RegroupBossDirection.Side;
        }

        private static string GetWithdrawReason(RegroupBossDirection bossDirection)
        {
            return bossDirection switch
            {
                RegroupBossDirection.Back => RegroupWithdrawBackwardReason,
                RegroupBossDirection.Side => RegroupWithdrawSideReason,
                _ => RegroupWithdrawForwardReason,
            };
        }

        private static RegroupBossDirection GetBossDirectionFromRegroupReason(string? reason)
        {
            return reason switch
            {
                RegroupWithdrawBackwardReason => RegroupBossDirection.Back,
                RegroupWithdrawSideReason => RegroupBossDirection.Side,
                _ => RegroupBossDirection.Front,
            };
        }

        private bool HasReachedBoss(Vector3 bossPosition)
        {
            if (!IsSameBossLevel(BotOwner.Position, bossPosition))
            {
                return false;
            }

            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPosition);
            return navDistanceToBoss <= GetRegroupCompleteDistance();
        }

        private bool ShouldAvoidUrbanDetourRegroup(Vector3 bossPosition)
        {
            if (!IsSameBossLevel(BotOwner.Position, bossPosition))
            {
                // A long path is expected when the follower is on a different floor. Treating that
                // as "close enough" strands the bot above/below the squad instead of routing home.
                return false;
            }

            float directDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            if (!Utils.Utils.TryGetCompletePathDistance(BotOwner.Position, bossPosition, out float pathDistance))
            {
                return false;
            }

            return CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directDistance, pathDistance);
        }

        private bool IsWithinHotRegroupSettleEnvelope(Vector3 bossPosition)
        {
            if (!IsSameBossLevel(BotOwner.Position, bossPosition))
            {
                return false;
            }

            float acceptableDistance = Mathf.Max(GetRegroupCompleteDistance(), GetRegroupCoverRadius());
            float directDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            if (directDistance <= acceptableDistance)
            {
                return true;
            }

            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPosition);
            return navDistanceToBoss > 0f && navDistanceToBoss <= acceptableDistance;
        }

        private float GetRegroupCompleteDistance()
        {
            return GetOrderedRegroupDistance();
        }

        private float GetRegroupCoverRadius()
        {
            return GetOrderedRegroupDistance();
        }

        private float GetOrderedRegroupDistance()
        {
            if (CombatDistanceConfiguration.Instance.IsFactoryMode)
            {
                return CombatRegroupOrderedDistanceFactory;
            }

            return CombatCommon.GetFollowerTactic() == FollowerCombatTactic.Marksman
                ? CombatRegroupOrderedDistanceMarksman
                : CombatRegroupOrderedDistance;
        }

        private bool HasReachedCurrentTarget()
        {
            if (!hasTarget)
            {
                return false;
            }

            if ((BotOwner.Position - currentTarget).sqrMagnitude <= 2f * 2f)
            {
                return true;
            }

            return BotOwner.GoToSomePointData?.HaveTarget() == true &&
                   BotOwner.GoToSomePointData.IsCome() &&
                   (BotOwner.GoToSomePointData.Point - currentTarget).sqrMagnitude <= 1f;
        }

        private bool TryAcceptRegroupCover(CustomNavigationPoint? cover, Vector3 bossPosition, out Vector3 targetPosition)
        {
            return TryAcceptRegroupCover(cover, bossPosition, RegroupBossDirection.Side, out targetPosition);
        }

        private bool TryAcceptRegroupCover(
            CustomNavigationPoint? cover,
            Vector3 bossPosition,
            RegroupBossDirection bossDirection,
            out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            if (cover == null)
            {
                return false;
            }

            float regroupCoverRadius = GetRegroupCoverRadius();
            if ((cover.Position - bossPosition).sqrMagnitude > regroupCoverRadius * regroupCoverRadius)
            {
                return false;
            }

            if (!IsSameBossLevel(cover.Position, bossPosition))
            {
                return false;
            }

            if (ShouldRejectDetourTarget(cover.Position, bossPosition))
            {
                return false;
            }

            // A bossward cover is only an intermediate step. Once that step is reached, do not pick
            // the same nearby owned cover again unless it also completes regroup.
            if (!HasReachedBoss(bossPosition) && HasReachedPosition(cover.Position))
            {
                return false;
            }

            if (bossDirection == RegroupBossDirection.Front && IsBehindBotRelativeToBoss(cover.Position, bossPosition))
            {
                return false;
            }

            targetPosition = cover.Position;
            return true;
        }

        private bool ShouldRejectDetourTarget(Vector3 targetPosition, Vector3 bossPosition)
        {
            if (!IsSameBossLevel(BotOwner.Position, bossPosition))
            {
                // When separated by floors, the stair/ramp path back to the boss is the objective,
                // not a detour to reject.
                return false;
            }

            float directBossDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            if (!Utils.Utils.TryGetCompletePathDistance(BotOwner.Position, targetPosition, out float targetPathDistance))
            {
                return true;
            }

            return CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directBossDistance, targetPathDistance);
        }

        private bool IsBehindBotRelativeToBoss(Vector3 position, Vector3 bossPosition)
        {
            Vector3 toBoss = bossPosition - BotOwner.Position;
            Vector3 toPosition = position - BotOwner.Position;
            toBoss.y = 0f;
            toPosition.y = 0f;
            if (toBoss.sqrMagnitude <= 0.01f || toPosition.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Dot(toPosition.normalized, toBoss.normalized) < -0.1f;
        }

        private bool HasReachedPosition(Vector3 position)
        {
            return (BotOwner.Position - position).sqrMagnitude <= 2f * 2f;
        }

        private void ClearCurrentTarget()
        {
            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            hasTarget = false;
        }

        private Vector3 GetFallbackBossDestination(Vector3 bossPosition)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents) &&
                combatEvents.TryFindBossSpreadDestination(
                    BotOwner,
                    bossPosition,
                    RegroupFallbackSpreadMinRadius,
                    RegroupFallbackSpreadMaxRadius,
                    CombatRegroupSameLevelTolerance,
                    RegroupFallbackSpacing,
                    out Vector3 spreadTarget))
            {
                return spreadTarget;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                return bossHit.position;
            }

            return bossPosition;
        }

        private void UpsertDestinationClaim(Vector3 target)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.UpsertDestinationClaim(BotOwner, target, RegroupClaimTtlSeconds);
            }
        }

        private void ReleaseDestinationClaim()
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.ReleaseDestinationClaim(BotOwner);
            }
        }

        private bool TryGetBossCombatEvents(out CombatEvents? combatEvents)
        {
            combatEvents = null;
            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            combatEvents = boss.CombatEvents;
            return combatEvents != null;
        }

        private bool HasBossSectorChanged(Vector3 bossPosition)
        {
            if (!hasBossSectorAnchor)
            {
                return false;
            }

            float sectorRadius = CombatDistanceConfiguration.Instance.GetRegroupBossMoveRefreshDistance();
            return (bossPosition - bossSectorAnchor).sqrMagnitude > sectorRadius * sectorRadius;
        }

        internal static bool IsSameBossLevel(Vector3 followerPosition, Vector3 bossPosition)
        {
            return Mathf.Abs(followerPosition.y - bossPosition.y) <= CombatRegroupSameLevelTolerance;
        }

        private static bool IsRegroupSettleReason(string? reason)
        {
            return string.Equals(reason, RegroupArrivedReason, System.StringComparison.Ordinal) ||
                   string.Equals(reason, RegroupUrbanDetourReason, System.StringComparison.Ordinal);
        }

        private bool HasActivePushOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.PushEnemy;
        }

        private bool HasActiveSuppressOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }

        private bool ShouldReturnMarksmanToSupport(EnemyInfo goalEnemy)
        {
            if (CombatCommon.GetFollowerTactic() != FollowerCombatTactic.Marksman)
            {
                return false;
            }

            if (Time.time - regroupActivatedAt < 1f)
            {
                return false;
            }

            if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (CombatCommon.TryGetActivePushEventForCurrentEnemy(out _))
            {
                return true;
            }

            return CombatCommon.TryGetAllyEngagementEnemy(out _, out _);
        }

        public static bool IsRegroupReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                reason != null &&
                reason.StartsWith(RegroupReasonPrefix, System.StringComparison.Ordinal);
        }

        public static bool IsRunReason(string? reason)
        {
            return string.Equals(reason, "regroup.run", System.StringComparison.Ordinal);
        }

        public static bool IsWithdrawReason(string? reason)
        {
            return string.Equals(reason, RegroupWithdrawBackwardReason, System.StringComparison.Ordinal) ||
                   string.Equals(reason, RegroupWithdrawForwardReason, System.StringComparison.Ordinal) ||
                   string.Equals(reason, RegroupWithdrawSideReason, System.StringComparison.Ordinal);
        }

        public static bool IsRegroupActivationReason(string? reason)
        {
            return string.Equals(reason, ActivateRegroupReason, System.StringComparison.Ordinal);
        }

        public static bool IsBackwardWithdrawReason(string? reason)
        {
            return string.Equals(reason, RegroupWithdrawBackwardReason, System.StringComparison.Ordinal);
        }
    }
}
