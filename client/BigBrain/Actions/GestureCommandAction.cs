using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Executes boss-issued follower commands outside the combat objective system. It owns command
    /// movement, hold/come/there/regroup behavior, loot and door interaction, and command cleanup
    /// when combat or another command interrupts the active task.
    /// </summary>
    internal class GestureCommandAction : CustomLogic
    {
        private BotFollowerPlayer? followerData;
        private float nextPathCheckAt;
        private bool moveCommandInitialized;
        private float nextHoldLookChangeAt;
        private Vector3 holdLookPoint;
        private float moveArrivalLookUntil;
        private float comeArrivalHoldUntil;
        private Vector3 activeMoveTarget;
        private bool comeTargetInitialized;
        private Vector3 comeTarget;
        private bool comePoseInitialized;
        private float comeMovePose = 1f;
        private bool regroupTargetInitialized;
        private Vector3 regroupTarget;
        private float nextRegroupRefreshAt;
        private bool regroupRunMode;
        private bool regroupReportedOnPosition;
        private bool regroupBossAnchorInitialized;
        private Vector3 regroupBossAnchorPosition;
        private float nextRegroupBossAnchorCheckAt;
        private bool lootPickupInProgress;
        private float lootPickupReadyAt;
        private float lootPickupAttemptStartedAt;
        private LootItem? activeLootItem;
        private LootableContainer? activeLootContainer;
        private bool containerLootMoveInProgress;
        private float containerLootReadyAt;
        private float containerLootNextMoveAt;
        private float containerLootAttemptStartedAt;
        private int containerLootMovesSucceeded;
        private bool containerLootWeaponListDirty;
        private bool containerLootOpened;
        private float containerLootOpenRequestedAt;
        private bool containerLootSearchStarted;
        private readonly HashSet<string> containerLootAttemptedItemIds = new HashSet<string>(StringComparer.Ordinal);
        private Corpse? activeBodyLootCorpse;
        private bool bodyLootMoveInProgress;
        private float bodyLootReadyAt;
        private float bodyLootNextMoveAt;
        private float bodyLootAttemptStartedAt;
        private int bodyLootMovesSucceeded;
        private bool bodyLootWeaponListDirty;
        private bool bodyLootBackpackCapacityAttempted;
        private bool bodyLootSearchStarted;
        private BetterSource? activeLootSearchSource;
        private readonly HashSet<string> bodyLootAttemptedItemIds = new HashSet<string>(StringComparer.Ordinal);
        private Door? activeDoor;
        private bool doorMoveIssued;
        private bool doorInteractIssued;
        private float doorTimeoutAt;
        private FollowerCommandType lastCommand = FollowerCommandType.None;
        private int lastMoveToPointIssueSequence = -1;
        private const float RegroupArriveNavDistance = 4f;
        private const float RegroupRunDistance = 10f;
        private const float SameLevelTolerance = 1.75f;
        private const float RegroupCoverSearchRadius = 15f;
        private const float RegroupRandomRadius = 6f;
        private const float RegroupReservationSpacing = 1.5f;
        private const float RegroupReservationTtl = 2f;
        private const float MoveToPointArrivalDistance = 1.5f;
        private const float MoveToPointForcedArrivalDistance = 0.75f;
        private const float MoveToPointTargetChangeDistanceSqr = 0.25f;
        private const float LootSearchDelayBaseSeconds = 1.20f;
        private const float LootSearchDelayPerSqrtCellSeconds = 0.62f;
        private const float LootSearchDelayMinSeconds = 1.75f;
        private const float LootSearchDelayMaxSeconds = 6.25f;
        private const float LootContainerOpenTimeoutSeconds = 3f;

        public GestureCommandAction(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            StopLootSearchSound();
            ReleaseRegroupReservation();
            nextPathCheckAt = 0f;
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
            comeTargetInitialized = false;
            comeTarget = Vector3.zero;
            comePoseInitialized = false;
            comeMovePose = 1f;
            regroupTargetInitialized = false;
            regroupTarget = Vector3.zero;
            nextRegroupRefreshAt = 0f;
            regroupRunMode = false;
            regroupReportedOnPosition = false;
            regroupBossAnchorInitialized = false;
            regroupBossAnchorPosition = Vector3.zero;
            nextRegroupBossAnchorCheckAt = 0f;
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;
            activeLootContainer = null;
            containerLootMoveInProgress = false;
            containerLootReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            containerLootAttemptStartedAt = 0f;
            containerLootMovesSucceeded = 0;
            containerLootWeaponListDirty = false;
            containerLootOpened = false;
            containerLootOpenRequestedAt = 0f;
            containerLootSearchStarted = false;
            containerLootAttemptedItemIds.Clear();
            activeBodyLootCorpse = null;
            bodyLootMoveInProgress = false;
            bodyLootReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            bodyLootAttemptStartedAt = 0f;
            bodyLootMovesSucceeded = 0;
            bodyLootWeaponListDirty = false;
            bodyLootBackpackCapacityAttempted = false;
            bodyLootSearchStarted = false;
            bodyLootAttemptedItemIds.Clear();
            activeDoor = null;
            doorMoveIssued = false;
            doorInteractIssued = false;
            doorTimeoutAt = 0f;
            lastCommand = FollowerCommandType.None;
            lastMoveToPointIssueSequence = -1;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                ReleaseRegroupReservation();
                lastCommand = FollowerCommandType.None;
                lastMoveToPointIssueSequence = -1;
                return;
            }

            EnsureCommandControl();
            bool moveToPointReissued = command == FollowerCommandType.MoveToPoint &&
                                        followerData.MoveToPointIssueSequence != lastMoveToPointIssueSequence;
            bool commandInstanceChanged = command != lastCommand || moveToPointReissued;

            // Request-layer commands are lower priority than real combat contact. If the command can
            // no longer safely continue, clear interaction state and let combat/patrol take over.
            if (ShouldInterruptCommandForCombat(command))
            {
                ReleaseRegroupReservation();
                CleanupLootInteraction($"CommandInterrupt:{command}");
                CleanupBodyLootInteraction($"CommandInterrupt:{command}");
                CleanupContainerLootInteraction($"CommandInterrupt:{command}");
                CleanupDoorInteraction();
                followerData?.ClearCommand($"CommandInterrupt:{command}");
                BotOwner.StopMove();
                BotOwner.SetPose(1f);
                lastCommand = FollowerCommandType.None;
                lastMoveToPointIssueSequence = -1;
                return;
            }

            if (commandInstanceChanged)
            {
                // Command changes must release resources owned by the previous command. This avoids
                // stale regroup reservations, loot pickup state, or door interaction state carrying
                // into a different command.
                if (lastCommand == FollowerCommandType.RegroupNearBoss)
                {
                    ReleaseRegroupReservation();
                }

                if (lastCommand == FollowerCommandType.TakeLootItem)
                {
                    CleanupLootInteraction($"CommandChanged:{command}");
                }

                if (lastCommand == FollowerCommandType.TakeBodyGear)
                {
                    CleanupBodyLootInteraction($"CommandChanged:{command}");
                }

                if (lastCommand == FollowerCommandType.TakeContainerLoot)
                {
                    CleanupContainerLootInteraction($"CommandChanged:{command}");
                }

                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                regroupTargetInitialized = false;
                regroupTarget = Vector3.zero;
                nextRegroupRefreshAt = 0f;
                regroupReportedOnPosition = false;
                regroupBossAnchorInitialized = false;
                regroupBossAnchorPosition = Vector3.zero;
                nextRegroupBossAnchorCheckAt = 0f;
                lootPickupInProgress = false;
                lootPickupReadyAt = 0f;
                lootPickupAttemptStartedAt = 0f;
                activeLootItem = null;
                activeLootContainer = null;
                containerLootMoveInProgress = false;
                containerLootReadyAt = 0f;
                containerLootNextMoveAt = 0f;
                containerLootAttemptStartedAt = 0f;
                containerLootMovesSucceeded = 0;
                containerLootWeaponListDirty = false;
                containerLootOpened = false;
                containerLootOpenRequestedAt = 0f;
                containerLootSearchStarted = false;
                containerLootAttemptedItemIds.Clear();
                activeBodyLootCorpse = null;
                bodyLootMoveInProgress = false;
                bodyLootReadyAt = 0f;
                bodyLootNextMoveAt = 0f;
                bodyLootAttemptStartedAt = 0f;
                bodyLootMovesSucceeded = 0;
                bodyLootWeaponListDirty = false;
                bodyLootBackpackCapacityAttempted = false;
                bodyLootSearchStarted = false;
                bodyLootAttemptedItemIds.Clear();
                CleanupDoorInteraction();

                if (command == FollowerCommandType.MoveToPoint)
                {
                    ResetMoveToPointState();
                }
            }

            switch (command)
            {
                case FollowerCommandType.HoldPosition:
                    HandleHoldPosition();
                    break;

                case FollowerCommandType.ComeCloser:
                    HandleComeCloser();
                    break;

                case FollowerCommandType.MoveToPoint:
                    HandleMoveToPoint(target);
                    break;

                case FollowerCommandType.RegroupNearBoss:
                    HandleRegroupNearBoss();
                    break;

                case FollowerCommandType.TakeLootItem:
                    HandleTakeLootItem();
                    break;

                case FollowerCommandType.TakeBodyGear:
                    HandleTakeBodyGear();
                    break;

                case FollowerCommandType.TakeContainerLoot:
                    HandleTakeContainerLoot();
                    break;

                case FollowerCommandType.OpenDoor:
                    HandleOpenDoor();
                    break;
            }

            if (commandInstanceChanged)
            {
                lastCommand = command;
                lastMoveToPointIssueSequence = followerData.MoveToPointIssueSequence;
                if (
                    command == FollowerCommandType.MoveToPoint ||
                    command == FollowerCommandType.ComeCloser
                )
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }
        }

        private void ResetMoveToPointState()
        {
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            activeMoveTarget = Vector3.zero;
            nextPathCheckAt = 0f;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
        }

        private void EnsureCommandControl()
        {
            if (BotOwner?.Mover != null)
            {
                if (BotOwner.Mover.Pause)
                {
                    BotOwner.Mover.Pause = false;
                }
            }

            if (BotOwner?.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }
        }

        private void HandleRegroupNearBoss()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:missingBoss");
                return;
            }
            // intrerupt on enemy enagage
            if (ShouldInterruptRegroupForThreatOrState(clearForDanger: true))
            {
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:interrupt");
                BotOwner.StopMove();
                return;
            }

            // Regroup is an urgent converge order: force move-capable state each tick.
            if (BotOwner.Mover.Pause)
            {
                BotOwner.Mover.Pause = false;
            }

            if (BotOwner.Mover.TargetPose < 0.85f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            Vector3 bossPos = boss.realPlayer.Position;
            if (!regroupBossAnchorInitialized)
            {
                regroupBossAnchorInitialized = true;
                regroupBossAnchorPosition = bossPos;
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
            }

            if (Time.time >= nextRegroupBossAnchorCheckAt)
            {
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
                if ((bossPos - regroupBossAnchorPosition).sqrMagnitude > 10f * 10f)
                {
                    regroupBossAnchorPosition = bossPos;
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                }
            }

            float verticalDiff = Mathf.Abs(BotOwner.Position.y - bossPos.y);
            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPos);

            if (verticalDiff <= SameLevelTolerance && navDistanceToBoss <= RegroupArriveNavDistance)
            {
                BotOwner.StopMove();
                if (!regroupReportedOnPosition)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.OnPosition, false);
                    regroupReportedOnPosition = true;
                }
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:arrived");
                return;
            }

            if (!regroupTargetInitialized || Time.time >= nextRegroupRefreshAt)
            {
                if (!TryGetRegroupTarget(bossPos, out regroupTarget))
                {
                    regroupTarget = bossPos;
                }
                regroupTargetInitialized = true;
                nextRegroupRefreshAt = Time.time + 0.8f;
                UpsertRegroupReservation(regroupTarget);
                BotOwner.GoToSomePointData.SetPoint(regroupTarget);
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, regroupTarget, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                    return;
                }
            }

            float regroupDistance = (regroupTarget - BotOwner.Position).magnitude;
            float regroupPressureDistance = Mathf.Max(regroupDistance, navDistanceToBoss);
            if (regroupRunMode)
            {
                regroupRunMode = regroupPressureDistance > 6f;
            }
            else if (regroupPressureDistance >= RegroupRunDistance)
            {
                regroupRunMode = true;
            }

            bool shouldRun = regroupRunMode;
            BotOwner.GoToSomePointData.UpdateToGo(shouldRun, 1, 1f);
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            nextHoldLookChangeAt = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private bool ShouldInterruptRegroupForThreatOrState(bool clearForDanger)
        {
            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing)
            {
                return true;
            }

            bool dangerNow = currentDecision == BotLogicDecision.runAwayGrenade ||
                             currentDecision == BotLogicDecision.runAwayBTR ||
                             BotOwner.BewareGrenade?.ShallRunAway() == true ||
                             BotOwner.BewareBTR?.ShallRunAway() == true;

            if (dangerNow && clearForDanger)
            {
                return true;
            }

            return false;
        }

        private bool ShouldInterruptCommandForCombat(FollowerCommandType command)
        {
            if (command == FollowerCommandType.HoldPosition)
            {
                return false;
            }

            if (command == FollowerCommandType.RegroupNearBoss)
            {
                return ShouldInterruptRegroupForThreatOrState(clearForDanger: true);
            }

            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal ||
                           currentDecision == BotLogicDecision.healStimulators;
            if (healing)
            {
                return true;
            }

            if (BotOwner.BewareGrenade?.ShallRunAway() == true ||
                BotOwner.BewareBTR?.ShallRunAway() == true ||
                currentDecision == BotLogicDecision.runAwayGrenade ||
                currentDecision == BotLogicDecision.runAwayBTR)
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            bool visibleFightNow = goalEnemy.IsVisible &&
                                  goalEnemy.CanShoot &&
                                  BotOwner.LookSensor.EnoughDistToShoot(out _);
            bool closeVisibleThreat = goalEnemy.IsVisible && goalEnemy.Distance <= 18f;
            bool urgentCombatAction = currentDecision == BotLogicDecision.dogFight ||
                                      currentDecision == BotLogicDecision.shootFromPlace ||
                                      currentDecision == BotLogicDecision.shootFromCover ||
                                      currentDecision == BotLogicDecision.attackMoving ||
                                      currentDecision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                                      currentDecision == BotLogicDecision.runToCover ||
                                      currentDecision == BotLogicDecision.goToEnemy ||
                                      currentDecision == BotLogicDecision.runToEnemy;

            return visibleFightNow || closeVisibleThreat || urgentCombatAction || BotOwner.Memory.IsUnderFire;
        }

        private bool TryGetRegroupTarget(Vector3 bossPos, out Vector3 target)
        {
            target = Vector3.zero;
            float bestDistance = float.MaxValue;
            List<CustomNavigationPoint> coverPoints = Covers.GetCoverPoints(
                BotOwner,
                bossPos,
                RegroupCoverSearchRadius,
                point => Mathf.Abs(point.Position.y - bossPos.y) <= SameLevelTolerance && !IsRegroupTargetCrowded(point.Position)
            );

            foreach (CustomNavigationPoint point in coverPoints)
            {
                if (point == null) continue;
                NavMeshPath coverPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, point.Position, NavMesh.AllAreas, coverPath) || coverPath.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathDistance = coverPath.CalculatePathLength();
                if (pathDistance < bestDistance)
                {
                    bestDistance = pathDistance;
                    target = point.Position;
                }
            }

            if (target == Vector3.zero)
            {
                if (TryGetBossCombatEvents(out CombatEvents? combatEvents) &&
                    combatEvents.TryFindBossSpreadDestination(
                        BotOwner,
                        bossPos,
                        1f,
                        RegroupRandomRadius,
                        SameLevelTolerance,
                        RegroupReservationSpacing,
                        out Vector3 spreadTarget))
                {
                    target = spreadTarget;
                }
            }

            return target != Vector3.zero;
        }

        private bool IsRegroupTargetCrowded(Vector3 candidate)
        {
            if (BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss)
            {
                if (boss.CombatEvents.HasDestinationClaimConflict(
                        BotOwner,
                        candidate,
                        RegroupReservationSpacing,
                        includeFollowerPositions: true))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpsertRegroupReservation(Vector3 target)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.UpsertDestinationClaim(BotOwner, target, RegroupReservationTtl);
            }
        }

        private void ReleaseRegroupReservation()
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.ReleaseDestinationClaim(BotOwner);
            }
        }

        private static void CleanupRegroupReservations()
        {
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

        private void HandleComeCloser()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand("ComeCloser:missingBoss");
                return;
            }

            if (!comeTargetInitialized)
            {
                comeTarget = boss.realPlayer.Transform.position;
                comeTargetInitialized = true;
            }
            if (!comePoseInitialized)
            {
                float bossPose = Mathf.Clamp01(boss.realPlayer.MovementContext?.PoseLevel ?? 1f);
                // Snapshot boss stance at command start.
                comeMovePose = bossPose < 0.75f ? 0.1f : 1f;
                comePoseInitialized = true;
            }

            float distance = (comeTarget - BotOwner.Position).magnitude;
            if (distance > 1.5f && comeArrivalHoldUntil > 0f)
            {
                comeArrivalHoldUntil = 0f;
            }
            if (distance <= 1.5f)
            {

                HandleComeArrivalPause();
                if (Time.time < comeArrivalHoldUntil)
                {
                    return;
                }
                comeArrivalHoldUntil = 0f;
                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                followerData?.CompleteComeCloser();
                BotOwner.StopMove();
                return;
            }

            BotOwner.GoToSomePointData.SetPoint(comeTarget);
            BotOwner.GoToSomePointData.UpdateToGo(distance > 16f, 1, comeMovePose);
            BotOwner.Steering.LookToPathDestPoint();
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleMoveToPoint(Vector3 target)
        {
            if (BotOwner.Mover.TargetPose != 1f) BotOwner.Mover.SetPose(1f);

            float distance = (target - BotOwner.Position).magnitude;
            if (distance > MoveToPointArrivalDistance && moveArrivalLookUntil > 0f)
            {
                moveArrivalLookUntil = 0f;
            }
            if (HasArrivedAtMovePoint(distance))
            {
                HandleMovePointArrivalLookAround();
                if (Time.time < moveArrivalLookUntil)
                {
                    return;
                }
                moveArrivalLookUntil = 0f;
                BotOwner.StopMove();
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                moveCommandInitialized = false;
                followerData?.ClearCommand("MoveToPoint:arrived");
                return;
            }

            bool targetChanged = !moveCommandInitialized || (activeMoveTarget - target).sqrMagnitude > MoveToPointTargetChangeDistanceSqr;
            bool targetMissing = BotOwner.GoToSomePointData?.HaveTarget() != true;
            bool targetCompletedEarly = BotOwner.GoToSomePointData?.IsCome() == true && distance > MoveToPointArrivalDistance;
            if (targetChanged || targetMissing || targetCompletedEarly)
            {
                BotOwner.GoToSomePointData.SetPoint(target);
                moveCommandInitialized = true;
                activeMoveTarget = target;
                moveArrivalLookUntil = 0f;
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    followerData?.ClearCommand("MoveToPoint:pathInvalid");
                    BotOwner.StopMove();
                    return;
                }
            }

            // "There" should always be a walk move.
            BotOwner.GoToSomePointData.UpdateToGo(false);

            if (followerData?.TryGetCommandLookOverride(out Vector3 lookOverridePoint) == true)
            {
                BotOwner.Steering.LookToPoint(lookOverridePoint);
            }
            else
            {
                BotOwner.Steering.LookToPathDestPoint();
            }

            nextHoldLookChangeAt = 0f;
        }

        private bool HasArrivedAtMovePoint(float distance)
        {
            if (distance <= MoveToPointForcedArrivalDistance)
            {
                return true;
            }

            return distance <= MoveToPointArrivalDistance &&
                   BotOwner.GoToSomePointData?.IsCome() == true;
        }

        private void HandleMovePointArrivalLookAround()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            // Always start the arrival hold window first so command is not cleared immediately
            // when random look is temporarily paused (e.g. recent contact command).
            if (moveArrivalLookUntil <= 0f)
            {
                moveArrivalLookUntil = Time.time + Utils.Utils.Random(2f, 4f);
                nextHoldLookChangeAt = 0f;
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;

                return;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.8f, 2f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private void HandleHoldPosition()
        {
            if (lootPickupInProgress)
            {
                return;
            }

            BotOwner.StopMove();
            if (followerData?.ShouldCrouchForHoldPosition() == true &&
                (BotOwner.Mover.TargetPose > 0.15f || BotOwner.Mover.TargetPose < 0.05f))
            {
                BotOwner.Mover.SetPose(0.1f);
            }
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                moveCommandInitialized = false;
                moveArrivalLookUntil = 0f;
                comeArrivalHoldUntil = 0f;
                activeMoveTarget = Vector3.zero;
                return;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(2f, 6f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }

            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleComeArrivalPause()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;

                return;
            }

            if (comeArrivalHoldUntil <= 0f)
            {
                comeArrivalHoldUntil = Time.time + Utils.Utils.Random(1.25f, 2.5f);
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.6f, 1.5f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private Vector3 PickNextHoldLookPoint()
        {
            Vector3 baseForward = BotOwner.LookDirection;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
            }
            // Keep hold/look-around horizontal so we don't accumulate upward pitch.
            baseForward.y = 0f;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
                baseForward.y = 0f;
            }

            float yawOffset = UnityEngine.Random.Range(-130f, 130f);
            Vector3 lookDir = Quaternion.Euler(0f, yawOffset, 0f) * baseForward.normalized;
            float lookDistance = UnityEngine.Random.Range(8f, 20f);
            Vector3 lookPoint = BotOwner.Position + lookDir * lookDistance;
            lookPoint.y = BotOwner.Position.y + 1.1f;
            return lookPoint;
        }

        public override void Stop()
        {
            bool botInvalid = BotOwner == null ||
                              BotOwner.IsDead ||
                              BotOwner.BotState != EBotState.Active ||
                              BotOwner.GetPlayer?.HealthController?.IsAlive != true;

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) != true ||
                botInvalid ||
                command != FollowerCommandType.TakeLootItem)
            {
                CleanupLootInteraction("TakeLoot:actionStop");
            }

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType bodyCommand, out _, out _) != true ||
                botInvalid ||
                bodyCommand != FollowerCommandType.TakeBodyGear)
            {
                CleanupBodyLootInteraction("TakeBodyGear:actionStop");
            }

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType containerCommand, out _, out _) != true ||
                botInvalid ||
                containerCommand != FollowerCommandType.TakeContainerLoot)
            {
                CleanupContainerLootInteraction("TakeContainerLoot:actionStop");
            }

            CleanupDoorInteraction();
            base.Stop();
        }

        private void HandleTakeLootItem()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                ClearTakeLootState(guardFailureReason ?? "TakeLoot:invalidState");
                return;
            }

            activeLootItem ??= InteractableObjects.GetAssignedLootItem(BotOwner) ?? InteractableObjects.GetCurLootItem();
            if (activeLootItem == null)
            {
                ClearTakeLootState("TakeLoot:itemMissing");
                return;
            }

            if (TryFinishTransferredLoot("TakeLoot:detectedInInventory"))
            {
                return;
            }

            // Keep the selected loot item pinned for this command so brief quick-panel target changes
            // don't drop execution ownership mid-pickup.
            if (InteractableObjects.GetCurLootItem() == null)
            {
                InteractableObjects.SetCurLootItem(activeLootItem);
            }

            Vector3 lootPosition;
            try
            {
                lootPosition = InteractableObjects.GetLootPosition(BotOwner);
            }
            catch
            {
                ClearTakeLootState("TakeLoot:missingLootPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, lootPosition);
            if (distance > 1.75f)
            {
                lootPickupReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(lootPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeLootItem.transform.position);

            if (lootPickupInProgress)
            {
                if (TryFinishTransferredLoot("TakeLoot:detectedInInventoryDuringPickup"))
                {
                    return;
                }

                if (lootPickupAttemptStartedAt > 0f && Time.time - lootPickupAttemptStartedAt > 3f)
                {
                    StopLootPickupState(BotOwner?.GetPlayer);
                    lootPickupInProgress = false;
                    lootPickupAttemptStartedAt = 0f;
                    lootPickupReadyAt = Time.time + 0.35f;
                }

                return;
            }

            if (lootPickupReadyAt <= 0f)
            {
                lootPickupReadyAt = Time.time + 0.35f;
                return;
            }

            if (Time.time < lootPickupReadyAt)
            {
                return;
            }

            StartLootPickup(activeLootItem);
        }

        private bool CanContinueLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeLoot:botInvalid";
                return false;
            }

            if (!InteractableObjects.IsTaker(BotOwner))
            {
                // Taker ownership can be cleared transiently; try to recover once before aborting.
                if (!InteractableObjects.SetTaker(BotOwner) || !InteractableObjects.IsTaker(BotOwner))
                {
                    reason = "TakeLoot:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeLoot:enemy";
                return false;
            }

            return true;
        }

        private bool TryGetLootExecutionContext(
            LootItem? lootItem,
            out Player? botPlayer,
            out InventoryController? inventory,
            out Item? rootItem,
            out string reason)
        {
            botPlayer = null;
            inventory = null;
            rootItem = null;

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeLoot:invalidState";
                return false;
            }

            if (lootItem == null || lootItem.gameObject == null)
            {
                reason = "TakeLoot:itemMissing";
                return false;
            }

            TraderControllerClass? itemOwner = lootItem.ItemOwner;
            rootItem = itemOwner?.RootItem;
            if (rootItem == null)
            {
                reason = "TakeLoot:itemNull";
                return false;
            }

            botPlayer = BotOwner.GetPlayer;
            inventory = botPlayer?.InventoryController;
            if (botPlayer == null || inventory == null)
            {
                reason = "TakeLoot:noInventory";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void StartLootPickup(LootItem lootItem)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? inventory, out Item? rootItem, out string reason))
                {
                    ClearTakeLootState(reason);
                    return;
                }

                var pickupResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                    rootItem,
                    inventory,
                    inventory.Inventory.Equipment.ToEnumerable<InventoryEquipment>(),
                    InteractionsHandlerClass.EMoveItemOrder.PickUp,
                    true);

                if (!pickupResult.Succeeded)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    ClearTakeLootState("TakeLoot:noSpace");
                    return;
                }

                if (!inventory.CanExecute(pickupResult.Value))
                {
                    ClearTakeLootState("TakeLoot:cannotExecute");
                    return;
                }

                lootPickupInProgress = true;
                lootPickupAttemptStartedAt = Time.time;
                botPlayer.SaveInteractionRayInfo();
                try
                {
                    botPlayer.CurrentManagedState.Pickup(true, () => ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupResult.Value));
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("TakeLoot pickup animation failed; falling back to direct inventory transaction");
                    Modules.Logger.LogError(ex);
                    StopLootPickupState(botPlayer);
                    ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupResult.Value);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot start failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:startException");
            }
        }

        private void ExecuteLootPickupTransaction(LootItem lootItem, Item rootItem, InventoryController inventory, GInterface424 pickupAction)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? currentInventory, out Item? currentRootItem, out string reason))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState(reason);
                    return;
                }

                if (!ReferenceEquals(currentInventory, inventory) || !ReferenceEquals(currentRootItem, rootItem))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState("TakeLoot:itemChanged");
                    return;
                }

                if (pickupAction is GInterface427 moveAction)
                {
                    ItemAddress currentAddress = rootItem.CurrentAddress;
                    if (currentAddress == null || !moveAction.From.Equals(currentAddress))
                    {
                        StopLootPickupState(botPlayer);
                        ClearTakeLootState("TakeLoot:itemMoved");
                        return;
                    }
                }

                inventory.RunNetworkTransaction(pickupAction, new Callback(result => CompleteLootPickup(result, botPlayer, rootItem)));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot transaction failed");
                Modules.Logger.LogError(ex);
                StopLootPickupState(BotOwner?.GetPlayer);
                ClearTakeLootState("TakeLoot:transactionException");
            }
        }

        private void CompleteLootPickup(IResult result, Player? botPlayer, Item rootItem)
        {
            try
            {
                if (result?.Succeed == true || IsLootNowInBotInventory(botPlayer, rootItem))
                {
                    FinishLootPickupSuccess(botPlayer, rootItem, "TakeLoot:done");
                    return;
                }

                BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                ClearTakeLootState("TakeLoot:transactionFailed");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot completion failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:completionException");
            }
            finally
            {
                StopLootPickupState(botPlayer);
            }
        }

        private bool TryFinishTransferredLoot(string reason)
        {
            Item? rootItem = activeLootItem?.ItemOwner?.RootItem;
            Player? botPlayer = BotOwner?.GetPlayer;
            if (rootItem == null || !IsLootNowInBotInventory(botPlayer, rootItem))
            {
                return false;
            }

            FinishLootPickupSuccess(botPlayer, rootItem, reason);
            return true;
        }

        private void FinishLootPickupSuccess(Player? botPlayer, Item rootItem, string reason)
        {
            botPlayer?.UpdateInteractionCast();
            InteractableObjects.RegisterLootedWeaponTree(BotOwner, rootItem);

            if (followerData?.IsSquadMate == true)
            {
                InteractableObjects.StoreItem(BotOwner, rootItem);
            }

            if (rootItem is Weapon && rootItem.GetItemComponent<KnifeComponent>() == null)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            ClearTakeLootState(reason);
        }

        private bool IsLootNowInBotInventory(Player? botPlayer, Item rootItem)
        {
            InventoryController? inventory = botPlayer?.InventoryController ?? BotOwner?.GetPlayer?.InventoryController;
            if (inventory == null || rootItem == null || string.IsNullOrEmpty(rootItem.Id))
            {
                return false;
            }

            return inventory.TryFindItem(rootItem.Id, out Item foundItem) &&
                   ReferenceEquals(foundItem, rootItem);
        }

        private static void StopLootPickupState(Player? botPlayer)
        {
            try
            {
                if (botPlayer == null)
                {
                    return;
                }

                if (botPlayer.MovementContext != null)
                {
                    botPlayer.MovementContext.PickupAction = null;
                }

                if (botPlayer.CurrentManagedState is PickupStateClass pickupState)
                {
                    pickupState.Pickup(false, null);
                }
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        private void CleanupLootInteraction(string reason)
        {
            if (!lootPickupInProgress &&
                lootPickupReadyAt <= 0f &&
                lootPickupAttemptStartedAt <= 0f &&
                activeLootItem == null &&
                BotOwner?.GetPlayer?.CurrentManagedState is not PickupStateClass)
            {
                return;
            }

            StopLootPickupState(BotOwner?.GetPlayer);
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;

            if (BotOwner != null)
            {
                InteractableObjects.RemoveTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurLootItem();
        }

        private void ClearTakeLootState(string reason)
        {
            if (!string.Equals(reason, "TakeLoot:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:detectedInInventory", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:detectedInInventoryDuringPickup", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Take loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupLootInteraction(reason);
            if (IsTakeLootSuccess(reason))
            {
                followerData?.CompleteTakeLootItem();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

        private static bool IsTakeLootSuccess(string reason)
        {
            return string.Equals(reason, "TakeLoot:done", StringComparison.Ordinal) ||
                   string.Equals(reason, "TakeLoot:detectedInInventory", StringComparison.Ordinal) ||
                   string.Equals(reason, "TakeLoot:detectedInInventoryDuringPickup", StringComparison.Ordinal);
        }

        private void HandleTakeBodyGear()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueBodyLootCommand(out string? guardFailureReason))
            {
                ClearBodyLootState(guardFailureReason ?? "TakeBodyGear:invalidState");
                return;
            }

            activeBodyLootCorpse ??= InteractableObjects.GetAssignedBodyLootTarget(BotOwner) ?? InteractableObjects.GetCurBodyLootTarget();
            if (activeBodyLootCorpse == null || activeBodyLootCorpse.gameObject == null)
            {
                ClearBodyLootState("TakeBodyGear:corpseMissing");
                return;
            }

            if (InteractableObjects.GetCurBodyLootTarget() == null)
            {
                InteractableObjects.SetCurBodyLootTarget(activeBodyLootCorpse);
            }

            Vector3 bodyPosition;
            try
            {
                bodyPosition = InteractableObjects.GetBodyLootPosition(BotOwner);
            }
            catch
            {
                ClearBodyLootState("TakeBodyGear:missingBodyPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, bodyPosition);
            if (distance > 1.9f)
            {
                bodyLootReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(bodyPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeBodyLootCorpse.transform.position);

            if (bodyLootMoveInProgress)
            {
                if (bodyLootAttemptStartedAt > 0f && Time.time - bodyLootAttemptStartedAt > 4f)
                {
                    bodyLootMoveInProgress = false;
                    bodyLootAttemptStartedAt = 0f;
                    bodyLootNextMoveAt = Time.time + 0.25f;
                }

                return;
            }

            if (!bodyLootSearchStarted)
            {
                if (!TryGetBodyLootExecutionContext(
                        out InventoryController? inventory,
                        out InventoryEquipment? corpseEquipment,
                        out InventoryEquipment? followerEquipment,
                        out string contextFailureReason))
                {
                    ClearBodyLootState(contextFailureReason);
                    return;
                }

                StartBodyLootSearchDelay(inventory, corpseEquipment, followerEquipment);
                return;
            }

            if (Time.time < bodyLootReadyAt || Time.time < bodyLootNextMoveAt)
            {
                return;
            }

            TryStartNextBodyGearMove();
        }

        private void StartBodyLootSearchDelay(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            bodyLootSearchStarted = true;
            followerData?.BeginCommittedLootCommand(FollowerCommandType.TakeBodyGear);

            Item? soundSource = GetBestBodyLootSearchSoundSource(corpseEquipment);
            int gridCells = GetBodyLootSearchGridCells(corpseEquipment);
            bodyLootReadyAt = Time.time + CalculateLootSearchDelaySeconds(gridCells);
            if (HasEligibleBodyLootMove(inventory, corpseEquipment, followerEquipment))
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.OnLoot, false);
            }

            StartLootSearchSound(soundSource, BotOwner?.Position ?? Vector3.zero);
        }

        private bool CanContinueBodyLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeBodyGear:botInvalid";
                return false;
            }

            if (!InteractableObjects.IsBodyLootTaker(BotOwner))
            {
                if (!InteractableObjects.SetBodyLootTaker(BotOwner) || !InteractableObjects.IsBodyLootTaker(BotOwner))
                {
                    reason = "TakeBodyGear:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeBodyGear:enemy";
                return false;
            }

            return true;
        }

        private void TryStartNextBodyGearMove()
        {
            try
            {
                if (!TryGetBodyLootExecutionContext(out InventoryController? inventory, out InventoryEquipment? corpseEquipment, out InventoryEquipment? followerEquipment, out string reason))
                {
                    ClearBodyLootState(reason);
                    return;
                }

                if (!TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment))
                {
                    TryStartNextFilteredBodyLootMove(inventory, corpseEquipment, followerEquipment);
                    return;
                }

                // Try the corpse backpack first as a capacity source, but only if it can be
                // carried inside the follower's own backpack. After that move succeeds, the next
                // planning pass sees its nested grids through normal live inventory state.
                BodyGearMove? backpackCapacityMove = TryBuildCorpseBackpackCapacityMove(inventory, corpseEquipment, followerEquipment);
                if (backpackCapacityMove != null)
                {
                    StartBodyGearMove(inventory, backpackCapacityMove);
                    return;
                }

                // Plan one live inventory transaction at a time. This keeps interruption behavior
                // simple: completed moves remain valid cargo, and unmoved body gear stays on the corpse.
                foreach (BodyGearCandidate candidate in GetBodyGearCandidates(corpseEquipment))
                {
                    if (candidate.Item == null ||
                        string.IsNullOrEmpty(candidate.Item.Id) ||
                        bodyLootAttemptedItemIds.Contains(candidate.Item.Id) ||
                        InteractableObjects.IsProtectedFollowerEquipment(candidate.Item) ||
                        !IsBodyGearCandidateLootable(candidate.Item) ||
                        IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                    {
                        continue;
                    }

                    bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                    if (!TryBuildBodyGearMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                    {
                        continue;
                    }

                    StartBodyGearMove(inventory, move);
                    return;
                }

                FinishBodyLootNoMoreMoves();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeBodyGear planning failed");
                Modules.Logger.LogError(ex);
                ClearBodyLootState("TakeBodyGear:planningException");
            }
        }

        private bool TryGetBodyLootExecutionContext(
            out InventoryController? inventory,
            out InventoryEquipment? corpseEquipment,
            out InventoryEquipment? followerEquipment,
            out string reason)
        {
            inventory = BotOwner?.GetPlayer?.InventoryController;
            followerEquipment = inventory?.Inventory?.Equipment;
            corpseEquipment = activeBodyLootCorpse?.ItemOwner?.RootItem as InventoryEquipment;

            if (!CanContinueBodyLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeBodyGear:invalidState";
                return false;
            }

            if (activeBodyLootCorpse == null || activeBodyLootCorpse.gameObject == null)
            {
                reason = "TakeBodyGear:corpseMissing";
                return false;
            }

            if (inventory == null || followerEquipment == null)
            {
                reason = "TakeBodyGear:noInventory";
                return false;
            }

            if (corpseEquipment == null)
            {
                reason = "TakeBodyGear:noCorpseEquipment";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void TryStartNextFilteredBodyLootMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            foreach (BodyGearCandidate candidate in GetFilteredBodyLootCandidates(corpseEquipment))
            {
                if (!CanTryFilteredLootCandidate(candidate, bodyLootAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                bodyLootAttemptedItemIds.Add(candidate.Item.Id);
                if (!TryBuildFilteredLootMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                {
                    continue;
                }

                StartBodyGearMove(inventory, move);
                return;
            }

            FinishBodyLootNoMoreMoves();
        }

        private bool HasEligibleBodyLootMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (inventory == null || corpseEquipment == null || followerEquipment == null)
            {
                return false;
            }

            if (!TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment))
            {
                return HasEligibleFilteredLootMove(
                    inventory,
                    followerEquipment,
                    GetFilteredBodyLootCandidates(corpseEquipment));
            }

            Item corpseBackpack = corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (corpseBackpack != null &&
                !string.IsNullOrEmpty(corpseBackpack.Id) &&
                !InteractableObjects.IsProtectedFollowerEquipment(corpseBackpack) &&
                IsBodyGearCandidateLootable(corpseBackpack))
            {
                BodyGearCandidate backpackCandidate = new BodyGearCandidate(
                    corpseBackpack,
                    EquipmentSlot.Backpack,
                    "bodyBackpackCapacity",
                    0);

                if (TryBuildBodyGearMove(inventory, followerEquipment, backpackCandidate, out _))
                {
                    return true;
                }
            }

            foreach (BodyGearCandidate candidate in GetBodyGearCandidates(corpseEquipment))
            {
                if (candidate.Item == null ||
                    string.IsNullOrEmpty(candidate.Item.Id) ||
                    InteractableObjects.IsProtectedFollowerEquipment(candidate.Item) ||
                    !IsBodyGearCandidateLootable(candidate.Item) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                if (TryBuildBodyGearMove(inventory, followerEquipment, candidate, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private BodyGearMove? TryBuildCorpseBackpackCapacityMove(
            InventoryController inventory,
            InventoryEquipment corpseEquipment,
            InventoryEquipment followerEquipment)
        {
            if (bodyLootBackpackCapacityAttempted)
            {
                return null;
            }

            bodyLootBackpackCapacityAttempted = true;

            Item corpseBackpack = corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
            if (corpseBackpack == null || string.IsNullOrEmpty(corpseBackpack.Id))
            {
                return null;
            }

            if (InteractableObjects.IsProtectedFollowerEquipment(corpseBackpack) ||
                !IsBodyGearCandidateLootable(corpseBackpack))
            {
                return null;
            }

            BodyGearCandidate candidate = new BodyGearCandidate(
                corpseBackpack,
                EquipmentSlot.Backpack,
                "bodyBackpackCapacity",
                0);

            if (TryBuildBodyGearMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
            {
                bodyLootAttemptedItemIds.Add(corpseBackpack.Id);
                return move;
            }

            return null;
        }

        private bool TryBuildBodyGearMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;

            if (candidate.Item == null)
            {
                return false;
            }

            // Empty compatible slots are allowed because they increase carry capacity without
            // sacrificing the follower's current fighting kit. Existing gear is never thrown or swapped.
            if (TryFindBodyGearEquipmentSlot(followerEquipment, candidate, out ItemAddress? equipAddress) &&
                TryCreateBodyGearMove(inventory, candidate.Item, equipAddress, candidate.SourceName, out move))
            {
                return true;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetBodyGearCarryContainers(followerEquipment, candidate.Item))
            {
                if (!container.TryFindLocationForItem(candidate.Item, out ItemAddress packAddress) ||
                    candidate.Item.Parent.Equals(packAddress))
                {
                    continue;
                }

                if (TryCreateBodyGearMove(inventory, candidate.Item, packAddress, candidate.SourceName, out move))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildFilteredLootMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            BodyGearCandidate candidate,
            out BodyGearMove? move)
        {
            move = null;
            if (candidate.Item == null)
            {
                return false;
            }

            if (ShouldUseFilteredLootEquipmentSlot(candidate) &&
                TryFindBodyGearEquipmentSlot(followerEquipment, candidate, out ItemAddress? equipAddress) &&
                TryCreateBodyGearMove(inventory, candidate.Item, equipAddress, candidate.SourceName, out move))
            {
                return true;
            }

            foreach (EFT.InventoryLogic.IContainer container in GetFilteredLootCarryContainers(followerEquipment))
            {
                if (!container.TryFindLocationForItem(candidate.Item, out ItemAddress packAddress) ||
                    candidate.Item.Parent.Equals(packAddress))
                {
                    continue;
                }

                if (TryCreateBodyGearMove(inventory, candidate.Item, packAddress, candidate.SourceName, out move))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasEligibleFilteredLootMove(
            InventoryController inventory,
            InventoryEquipment followerEquipment,
            IEnumerable<BodyGearCandidate> candidates)
        {
            if (inventory == null || followerEquipment == null || candidates == null)
            {
                return false;
            }

            HashSet<string> dryRunAttemptedItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyGearCandidate candidate in candidates)
            {
                if (!CanTryFilteredLootCandidate(candidate, dryRunAttemptedItemIds) ||
                    IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                {
                    continue;
                }

                if (TryBuildFilteredLootMove(inventory, followerEquipment, candidate, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldUseFilteredLootEquipmentSlot(BodyGearCandidate candidate)
        {
            return candidate?.Item is Weapon weapon && weapon.GetItemComponent<KnifeComponent>() == null;
        }

        private bool TryCreateBodyGearMove(
            InventoryController inventory,
            Item item,
            ItemAddress address,
            string sourceName,
            out BodyGearMove? move)
        {
            move = null;

            GStruct154<GClass3411> moveResult = InteractionsHandlerClass.Move(item, address, inventory, true);
            if (moveResult.Failed || moveResult.Value.ItemsDestroyRequired || !inventory.CanExecute(moveResult.Value))
            {
                return false;
            }

            move = new BodyGearMove(item, moveResult.Value, sourceName);
            return true;
        }

        private void StartBodyGearMove(InventoryController inventory, BodyGearMove move)
        {
            bodyLootMoveInProgress = true;
            bodyLootAttemptStartedAt = Time.time;
            inventory.RunNetworkTransaction(move.Operation, new Callback(result => CompleteBodyGearMove(result, move)));
        }

        private void CompleteBodyGearMove(IResult result, BodyGearMove move)
        {
            try
            {
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                bodyLootNextMoveAt = Time.time + 0.2f;

                if (result?.Succeed == true || IsLootNowInBotInventory(BotOwner?.GetPlayer, move.Item))
                {
                    bodyLootMovesSucceeded++;
                    InteractableObjects.RegisterLootedWeaponTree(BotOwner, move.Item);

                    if (followerData?.IsSquadMate == true)
                    {
                        InteractableObjects.StoreItem(BotOwner, move.Item);
                    }

                    if (move.Item is Weapon && move.Item.GetItemComponent<KnifeComponent>() == null)
                    {
                        bodyLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Body gear move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeBodyGear move completion failed");
                Modules.Logger.LogError(ex);
                bodyLootMoveInProgress = false;
                bodyLootAttemptStartedAt = 0f;
                bodyLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishBodyLootNoMoreMoves()
        {
            if (bodyLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                bodyLootWeaponListDirty = false;
            }

            if (bodyLootMovesSucceeded > 0)
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                ClearBodyLootState("TakeBodyGear:done");
                return;
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.LootNothing, false);
            ClearBodyLootState("TakeBodyGear:noSpace");
        }

        private IEnumerable<BodyGearCandidate> GetBodyGearCandidates(InventoryEquipment corpseEquipment)
        {
            foreach (EquipmentSlot slot in BodyGearTopLevelSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (item != null)
                {
                    yield return new BodyGearCandidate(item, slot, slot.ToString(), 0);
                }
            }

            foreach (EquipmentSlot slot in BodyGearContentSlotOrder)
            {
                Item root = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (root is not CompoundItem compound ||
                    (slot != EquipmentSlot.Pockets && root is SearchableItemItemClass))
                {
                    continue;
                }

                List<Item> contents = new List<Item>();
                compound.GetAllAssembledItems(contents);

                foreach (Item item in contents
                             .Where(item => item != null && item != root && item is not SearchableItemItemClass)
                             .OrderByDescending(GetBodyGearContentPriority)
                             .ThenByDescending(GetItemArea)
                             .ThenByDescending(item => item.Template?.CreditsPrice ?? 0))
                {
                    yield return new BodyGearCandidate(item, null, $"{slot}.Contents", 1);
                }
            }
        }

        private IEnumerable<BodyGearCandidate> GetFilteredBodyLootCandidates(InventoryEquipment corpseEquipment)
        {
            if (TryGetNonTeammatePmcDogtag(corpseEquipment, out Item dogtag))
            {
                yield return new BodyGearCandidate(
                    dogtag,
                    EquipmentSlot.Dogtag,
                    "Dogtag",
                    0,
                    bypassPriceThreshold: true,
                    bypassBodyGearLootability: true);
            }

            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                          corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem,
                          "Backpack.Contents",
                          skipMagazines: false))
            {
                yield return candidate;
            }

            // Pocket and vest magazines belong to the corpse's reload setup; backpack/container
            // magazines are regular cargo and can be moved into follower backpack/pocket space.
            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                          corpseEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem,
                          "Pockets.Contents",
                          skipMagazines: true))
            {
                yield return candidate;
            }

            foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                          corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem,
                          "TacticalVest.Contents",
                          skipMagazines: true))
            {
                yield return candidate;
            }

            foreach (EquipmentSlot slot in BodyGearWeaponSlotOrder)
            {
                Item item = corpseEquipment.GetSlot(slot)?.ContainedItem;
                if (item is Weapon && item.GetItemComponent<KnifeComponent>() == null)
                {
                    yield return new BodyGearCandidate(item, slot, slot.ToString(), 2);
                }
            }
        }

        private static bool TryGetNonTeammatePmcDogtag(InventoryEquipment corpseEquipment, out Item dogtag)
        {
            dogtag = null;
            if (corpseEquipment == null ||
                TeammateCorpseIdentity.IsTeammateCorpseEquipment(corpseEquipment))
            {
                return false;
            }

            Slot dogtagSlot = corpseEquipment.GetSlot(EquipmentSlot.Dogtag);
            Item item = dogtagSlot?.ContainedItem;
            DogtagComponent dogtagComponent = item?.GetItemComponent<DogtagComponent>();
            if (item == null ||
                dogtagComponent == null ||
                (dogtagComponent.Side != EPlayerSide.Bear && dogtagComponent.Side != EPlayerSide.Usec))
            {
                return false;
            }

            dogtag = item;
            return true;
        }

        private static IEnumerable<BodyGearCandidate> GetStorageLootCandidates(
            Item root,
            string sourceName,
            bool skipMagazines)
        {
            if (root == null)
            {
                yield break;
            }

            foreach (Item item in GetDirectLootChildren(root)
                         .OrderByDescending(GetBodyGearContentPriority)
                         .ThenByDescending(GetItemArea)
                         .ThenByDescending(item => item.Template?.CreditsPrice ?? 0))
            {
                if (item == null)
                {
                    continue;
                }

                // Price-only plate stripping makes followers over-prioritize armor internals.
                // Filtered body/container looting leaves plates alone entirely.
                if (item is ArmorPlateItemClass)
                {
                    continue;
                }

                if (ShouldSearchContentsInsteadOfMovingRoot(item))
                {
                    foreach (BodyGearCandidate child in GetStorageLootCandidates(
                                 item,
                                 $"{sourceName}.{item.TemplateId}",
                                 skipMagazines))
                    {
                        yield return child;
                    }

                    continue;
                }

                yield return new BodyGearCandidate(item, null, sourceName, 1, skipMagazines);
            }
        }

        private static IEnumerable<Item> GetDirectLootChildren(Item root)
        {
            if (root == null)
            {
                yield break;
            }

            foreach (Item child in root.GetAllItems())
            {
                if (child == null || ReferenceEquals(child, root))
                {
                    continue;
                }

                if (ReferenceEquals(child.Parent?.Container?.ParentItem, root))
                {
                    yield return child;
                }
            }
        }

        private static bool ShouldSearchContentsInsteadOfMovingRoot(Item item)
        {
            return item is SearchableItemItemClass && item is not Weapon;
        }

        private bool CanTryFilteredLootCandidate(
            BodyGearCandidate candidate,
            HashSet<string> attemptedItemIds)
        {
            Item item = candidate?.Item;
            if (item == null ||
                string.IsNullOrEmpty(item.Id) ||
                attemptedItemIds.Contains(item.Id) ||
                InteractableObjects.IsProtectedFollowerEquipment(item))
            {
                return false;
            }

            if (!candidate.BypassBodyGearLootability && !IsBodyGearCandidateLootable(item))
            {
                return false;
            }

            if (item is ArmorPlateItemClass)
            {
                attemptedItemIds.Add(item.Id);
                return false;
            }

            if (candidate.SkipMagazine && item is MagazineItemClass)
            {
                attemptedItemIds.Add(item.Id);
                return false;
            }

            if (!candidate.BypassPriceThreshold && !FollowerLootPriceService.PassesPriceThreshold(item))
            {
                attemptedItemIds.Add(item.Id);
                return false;
            }

            return true;
        }

        private static bool TryFindBodyGearEquipmentSlot(
            InventoryEquipment equipment,
            BodyGearCandidate candidate,
            out ItemAddress? address)
        {
            address = null;

            if (candidate.Item is BackpackItemClass)
            {
                return false;
            }

            foreach (EquipmentSlot slotName in GetBodyGearEquipmentSlotOrder(candidate))
            {
                Slot slot = equipment.GetSlot(slotName);
                if (slot == null || slot.Deleted || slot.ContainedItem != null)
                {
                    continue;
                }

                Error error;
                ItemAddress candidateAddress = slot.FindLocationForItem(candidate.Item, out error);
                if (candidateAddress != null)
                {
                    address = candidateAddress;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<EquipmentSlot> GetBodyGearEquipmentSlotOrder(BodyGearCandidate candidate)
        {
            if (candidate.SourceSlot == EquipmentSlot.FirstPrimaryWeapon ||
                candidate.SourceSlot == EquipmentSlot.SecondPrimaryWeapon)
            {
                yield return EquipmentSlot.SecondPrimaryWeapon;
                yield return EquipmentSlot.FirstPrimaryWeapon;
                yield break;
            }

            Item item = candidate.Item;
            if (item is PistolItemClass || item is RevolverItemClass)
            {
                yield return EquipmentSlot.Holster;
                yield break;
            }

            if (item is ArmorItemClass)
            {
                yield return EquipmentSlot.ArmorVest;
                yield break;
            }

            if (item is VestItemClass)
            {
                yield return EquipmentSlot.TacticalVest;
                yield break;
            }

            if (item is HeadwearItemClass)
            {
                yield return EquipmentSlot.Headwear;
                yield break;
            }

            if (item is HeadphonesItemClass)
            {
                yield return EquipmentSlot.Earpiece;
                yield break;
            }

            if (item is FaceCoverItemClass)
            {
                yield return EquipmentSlot.FaceCover;
                yield break;
            }

            if (item is VisorsItemClass)
            {
                yield return EquipmentSlot.Eyewear;
                yield break;
            }

            if (item is Weapon && item.GetItemComponent<KnifeComponent>() == null)
            {
                yield return EquipmentSlot.SecondPrimaryWeapon;
                yield return EquipmentSlot.FirstPrimaryWeapon;
            }
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetBodyGearCarryContainers(InventoryEquipment equipment, Item item)
        {
            HashSet<EFT.InventoryLogic.IContainer> seen = new HashSet<EFT.InventoryLogic.IContainer>();

            foreach (EquipmentSlot slot in BodyGearCarrySlotOrder)
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root is not SearchableItemItemClass searchable)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
                {
                    if (container != null && seen.Add(container))
                    {
                        yield return container;
                    }
                }
            }
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetFilteredLootCarryContainers(InventoryEquipment equipment)
        {
            HashSet<EFT.InventoryLogic.IContainer> seen = new HashSet<EFT.InventoryLogic.IContainer>();

            foreach (EquipmentSlot slot in FilteredLootCarrySlotOrder)
            {
                Item root = equipment.GetSlot(slot)?.ContainedItem;
                if (root is not SearchableItemItemClass searchable)
                {
                    continue;
                }

                foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(searchable))
                {
                    if (container != null && seen.Add(container))
                    {
                        yield return container;
                    }
                }
            }
        }

        private static IEnumerable<EFT.InventoryLogic.IContainer> GetSearchableContainersRecursive(SearchableItemItemClass item)
        {
            foreach (EFT.InventoryLogic.IContainer container in item.Containers ?? Enumerable.Empty<EFT.InventoryLogic.IContainer>())
            {
                yield return container;
            }

            foreach (Item child in item.GetAllItems())
            {
                if (child != null && child != item && child is SearchableItemItemClass nested)
                {
                    foreach (EFT.InventoryLogic.IContainer container in GetSearchableContainersRecursive(nested))
                    {
                        yield return container;
                    }
                }
            }
        }

        private static float CalculateLootSearchDelaySeconds(int gridCells)
        {
            float searchedCells = Mathf.Max(1f, gridCells);
            float delay = LootSearchDelayBaseSeconds + Mathf.Sqrt(searchedCells) * LootSearchDelayPerSqrtCellSeconds;
            return Mathf.Clamp(delay, LootSearchDelayMinSeconds, LootSearchDelayMaxSeconds);
        }

        private static int GetBodyLootSearchGridCells(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                return 0;
            }

            return GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem) +
                   GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem) +
                   GetSearchableGridCellCount(corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem);
        }

        private static int GetSearchableGridCellCount(Item? item)
        {
            if (item is not SearchableItemItemClass searchable || searchable.Grids == null)
            {
                return 0;
            }

            int cells = GetDirectGridCellCount(searchable);
            foreach (Item child in searchable.GetAllItems())
            {
                if (child != null && child != searchable && child is SearchableItemItemClass nested)
                {
                    cells += GetDirectGridCellCount(nested);
                }
            }

            return cells;
        }

        private static int GetDirectGridCellCount(SearchableItemItemClass searchable)
        {
            if (searchable?.Grids == null)
            {
                return 0;
            }

            int cells = 0;
            foreach (StashGridClass grid in searchable.Grids)
            {
                if (grid != null)
                {
                    cells += Mathf.Max(0, grid.GridHeight * grid.GridWidth);
                }
            }

            return cells;
        }

        private static Item? GetBestBodyLootSearchSoundSource(InventoryEquipment corpseEquipment)
        {
            if (corpseEquipment == null)
            {
                return null;
            }

            foreach (EquipmentSlot slot in new[] { EquipmentSlot.Backpack, EquipmentSlot.TacticalVest, EquipmentSlot.Pockets })
            {
                SearchableItemItemClass searchable = corpseEquipment.GetSlot(slot)?.ContainedItem as SearchableItemItemClass;
                if (searchable != null && !string.IsNullOrWhiteSpace(searchable.SearchSound))
                {
                    return searchable;
                }
            }

            return null;
        }

        private void StartLootSearchSound(Item? soundSource, Vector3 worldPosition)
        {
            StopLootSearchSound();

            try
            {
                if (soundSource is not SearchableItemItemClass searchable ||
                    string.IsNullOrWhiteSpace(searchable.SearchSound) ||
                    !Singleton<GUISounds>.Instantiated)
                {
                    PlayLootSearchFallbackSound();
                    return;
                }

                AudioClip clip = Singleton<GUISounds>.Instance.GetLootingClip(searchable.SearchSound);
                if (clip == null)
                {
                    PlayLootSearchFallbackSound();
                    return;
                }

                try
                {
                    BetterAudio audio = Singleton<BetterAudio>.Instance;
                    BetterSource source = audio?.PlayAtPoint(
                        worldPosition,
                        clip,
                        BetterAudio.AudioSourceGroupType.Character,
                        30,
                        1f,
                        EOcclusionTest.None,
                        null,
                        spatialize: true,
                        oneShot: false,
                        autoReleaseSource: false,
                        enabledHighPassFilter: true);

                    if (source != null)
                    {
                        source.Loop = true;
                        activeLootSearchSource = source;
                        return;
                    }
                }
                catch
                {
                    // Fall through to the guaranteed UI audio path below.
                }

                Singleton<GUISounds>.Instance.PlaySound(clip, false, true, 1f);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to play loot search sound: {ex.Message}");
            }
        }

        private void StopLootSearchSound()
        {
            BetterSource source = activeLootSearchSource;
            activeLootSearchSource = null;
            if (source == null)
            {
                return;
            }

            try
            {
                source.Loop = false;
                source.Stop(0f);
                source.Release();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to stop loot search sound: {ex.Message}");
            }
        }

        private static void PlayLootSearchFallbackSound()
        {
            try
            {
                if (Singleton<GUISounds>.Instantiated)
                {
                    GClass3517.PlayInstantSearchSound();
                }
            }
            catch
            {
                // best-effort audio only
            }
        }

        private static int GetBodyGearContentPriority(Item item)
        {
            if (item is Weapon && item.GetItemComponent<KnifeComponent>() == null)
            {
                return 100;
            }

            if (item is ArmorItemClass || item is VestItemClass)
            {
                return 90;
            }

            if (item is HeadwearItemClass || item is HeadphonesItemClass || item is FaceCoverItemClass || item is VisorsItemClass)
            {
                return 80;
            }

            return 10;
        }

        private static bool IsBodyGearCandidateLootable(Item item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.IsSpecialSlotOnly ||
                item is ArmBandItemClass ||
                item.GetItemComponent<KnifeComponent>() != null)
            {
                return false;
            }

            // Respect vanilla lootability/removal flags from the corpse equipment slot. We check the
            // raw component data here because pitFireTeam relaxes UnlootableComponent elsewhere so
            // players can inspect/reorganize teammate gear during raid.
            Slot sourceSlot = item.CurrentAddress?.Container as Slot;
            if (sourceSlot == null || sourceSlot.ParentItem is not InventoryEquipment)
            {
                return true;
            }

            if (IsAlwaysNonLootableEquipmentSlot(sourceSlot))
            {
                return false;
            }

            if (item.TryGetItemComponent<UnlootableComponent>(out UnlootableComponent unlootableComponent) &&
                IsUnlootableFromSlotIgnoringPatch(unlootableComponent, sourceSlot))
            {
                return false;
            }

            if (item.TryGetItemComponent<CantRemoveFromSlotsDuringRaidComponent>(out CantRemoveFromSlotsDuringRaidComponent cantRemoveComponent) &&
                !cantRemoveComponent.CanRemoveFromSlotDuringRaid(sourceSlot.ID))
            {
                return false;
            }

            return true;
        }

        private static bool IsAlwaysNonLootableEquipmentSlot(Slot slot)
        {
            return string.Equals(slot.ID, EquipmentSlot.ArmBand.ToString(), StringComparison.Ordinal) ||
                   string.Equals(slot.ID, EquipmentSlot.Scabbard.ToString(), StringComparison.Ordinal);
        }

        private static bool IsUnlootableFromSlotIgnoringPatch(UnlootableComponent component, Slot slot)
        {
            if (component?.Template == null ||
                slot == null ||
                string.IsNullOrEmpty(component.Template.SlotName) ||
                !slot.ID.Contains(component.Template.SlotName))
            {
                return false;
            }

            if (slot.ParentItem?.Owner is GClass3384 equipmentOwner)
            {
                return component.Template.Side.CheckSide(equipmentOwner.Side);
            }

            return false;
        }

        private static int GetItemArea(Item item)
        {
            try
            {
                XYCellSizeStruct size = item.CalculateCellSize();
                return Mathf.Max(1, size.X) * Mathf.Max(1, size.Y);
            }
            catch
            {
                return 1;
            }
        }

        private void CleanupBodyLootInteraction(string reason)
        {
            if (!bodyLootMoveInProgress &&
                bodyLootReadyAt <= 0f &&
                bodyLootNextMoveAt <= 0f &&
                bodyLootAttemptStartedAt <= 0f &&
                activeBodyLootCorpse == null &&
                activeLootSearchSource == null &&
                bodyLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            StopLootSearchSound();
            bodyLootMoveInProgress = false;
            bodyLootReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            bodyLootAttemptStartedAt = 0f;
            bodyLootMovesSucceeded = 0;
            bodyLootWeaponListDirty = false;
            bodyLootBackpackCapacityAttempted = false;
            bodyLootSearchStarted = false;
            activeLootSearchSource = null;
            bodyLootAttemptedItemIds.Clear();
            activeBodyLootCorpse = null;
            followerData?.EndCommittedLootCommand(FollowerCommandType.TakeBodyGear);

            if (BotOwner != null)
            {
                InteractableObjects.RemoveBodyLootTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurBodyLootTarget();
        }

        private void ClearBodyLootState(string reason)
        {
            if (!string.Equals(reason, "TakeBodyGear:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeBodyGear:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Body gear loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupBodyLootInteraction(reason);
            if (string.Equals(reason, "TakeBodyGear:done", StringComparison.Ordinal))
            {
                followerData?.CompleteTakeBodyGear();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

        private void HandleTakeContainerLoot()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueContainerLootCommand(out string? guardFailureReason))
            {
                ClearContainerLootState(guardFailureReason ?? "TakeContainerLoot:invalidState");
                return;
            }

            activeLootContainer ??= InteractableObjects.GetAssignedLootContainerTarget(BotOwner) ?? InteractableObjects.GetCurLootContainerTarget();
            if (activeLootContainer == null || activeLootContainer.gameObject == null)
            {
                ClearContainerLootState("TakeContainerLoot:containerMissing");
                return;
            }

            if (!activeLootContainer.isActiveAndEnabled || activeLootContainer.DoorState == EDoorState.Locked)
            {
                ClearContainerLootState("TakeContainerLoot:containerUnavailable");
                return;
            }

            if (InteractableObjects.GetCurLootContainerTarget() == null)
            {
                InteractableObjects.SetCurLootContainerTarget(activeLootContainer);
            }

            Vector3 containerPosition;
            try
            {
                containerPosition = InteractableObjects.GetContainerLootPosition(BotOwner);
            }
            catch
            {
                ClearContainerLootState("TakeContainerLoot:missingContainerPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, containerPosition);
            if (distance > 1.9f)
            {
                containerLootReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(containerPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeLootContainer.transform.position);

            if (activeLootContainer.DoorState != EDoorState.Open)
            {
                if (!containerLootOpened && activeLootContainer.DoorState == EDoorState.Shut)
                {
                    if (BotOwner.GetPlayer == null)
                    {
                        ClearContainerLootState("TakeContainerLoot:noPlayer");
                        return;
                    }

                    InteractLootContainer(activeLootContainer, BotOwner.GetPlayer, EInteractionType.Open);
                    containerLootOpened = true;
                    containerLootOpenRequestedAt = Time.time;
                    return;
                }

                if (containerLootOpened &&
                    containerLootOpenRequestedAt > 0f &&
                    Time.time - containerLootOpenRequestedAt > LootContainerOpenTimeoutSeconds)
                {
                    ClearContainerLootState("TakeContainerLoot:openTimeout");
                }

                return;
            }

            if (containerLootMoveInProgress)
            {
                if (containerLootAttemptStartedAt > 0f && Time.time - containerLootAttemptStartedAt > 4f)
                {
                    containerLootMoveInProgress = false;
                    containerLootAttemptStartedAt = 0f;
                    containerLootNextMoveAt = Time.time + 0.25f;
                }

                return;
            }

            if (!containerLootSearchStarted)
            {
                if (!TryGetContainerLootExecutionContext(
                        out InventoryController? inventory,
                        out SearchableItemItemClass? containerRoot,
                        out InventoryEquipment? followerEquipment,
                        out string contextFailureReason))
                {
                    ClearContainerLootState(contextFailureReason);
                    return;
                }

                StartContainerLootSearchDelay(inventory, containerRoot, followerEquipment);
                return;
            }

            if (Time.time < containerLootReadyAt || Time.time < containerLootNextMoveAt)
            {
                return;
            }

            TryStartNextContainerLootMove();
        }

        private void StartContainerLootSearchDelay(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            containerLootSearchStarted = true;
            followerData?.BeginCommittedLootCommand(FollowerCommandType.TakeContainerLoot);

            int gridCells = GetSearchableGridCellCount(containerRoot);
            containerLootReadyAt = Time.time + CalculateLootSearchDelaySeconds(gridCells);
            if (HasEligibleContainerLootMove(inventory, containerRoot, followerEquipment))
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.OnLoot, false);
            }

            StartLootSearchSound(containerRoot, activeLootContainer?.transform?.position ?? BotOwner?.Position ?? Vector3.zero);
        }

        private bool CanContinueContainerLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeContainerLoot:botInvalid";
                return false;
            }

            if (!InteractableObjects.IsContainerLootTaker(BotOwner))
            {
                if (!InteractableObjects.SetContainerLootTaker(BotOwner) || !InteractableObjects.IsContainerLootTaker(BotOwner))
                {
                    reason = "TakeContainerLoot:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeContainerLoot:enemy";
                return false;
            }

            return true;
        }

        private bool TryGetContainerLootExecutionContext(
            out InventoryController? inventory,
            out SearchableItemItemClass? containerRoot,
            out InventoryEquipment? followerEquipment,
            out string reason)
        {
            inventory = BotOwner?.GetPlayer?.InventoryController;
            followerEquipment = inventory?.Inventory?.Equipment;
            containerRoot = activeLootContainer?.ItemOwner?.Items?.FirstOrDefault() as SearchableItemItemClass;

            if (!CanContinueContainerLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeContainerLoot:invalidState";
                return false;
            }

            if (activeLootContainer == null || activeLootContainer.gameObject == null)
            {
                reason = "TakeContainerLoot:containerMissing";
                return false;
            }

            if (!activeLootContainer.isActiveAndEnabled || activeLootContainer.DoorState == EDoorState.Locked)
            {
                reason = "TakeContainerLoot:containerUnavailable";
                return false;
            }

            if (inventory == null || followerEquipment == null)
            {
                reason = "TakeContainerLoot:noInventory";
                return false;
            }

            if (containerRoot == null)
            {
                reason = "TakeContainerLoot:noContainerRoot";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void TryStartNextContainerLootMove()
        {
            try
            {
                if (!TryGetContainerLootExecutionContext(out InventoryController? inventory, out SearchableItemItemClass? containerRoot, out InventoryEquipment? followerEquipment, out string reason))
                {
                    ClearContainerLootState(reason);
                    return;
                }

                foreach (BodyGearCandidate candidate in GetStorageLootCandidates(
                             containerRoot,
                             "Container.Contents",
                             skipMagazines: false))
                {
                    if (!CanTryFilteredLootCandidate(candidate, containerLootAttemptedItemIds) ||
                        IsLootNowInBotInventory(BotOwner?.GetPlayer, candidate.Item))
                    {
                        continue;
                    }

                    containerLootAttemptedItemIds.Add(candidate.Item.Id);
                    if (!TryBuildFilteredLootMove(inventory, followerEquipment, candidate, out BodyGearMove? move))
                    {
                        continue;
                    }

                    StartContainerLootMove(inventory, move);
                    return;
                }

                FinishContainerLootNoMoreMoves();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeContainerLoot planning failed");
                Modules.Logger.LogError(ex);
                ClearContainerLootState("TakeContainerLoot:planningException");
            }
        }

        private bool HasEligibleContainerLootMove(
            InventoryController inventory,
            SearchableItemItemClass containerRoot,
            InventoryEquipment followerEquipment)
        {
            return HasEligibleFilteredLootMove(
                inventory,
                followerEquipment,
                GetStorageLootCandidates(containerRoot, "Container.Contents", skipMagazines: false));
        }

        private void StartContainerLootMove(InventoryController inventory, BodyGearMove move)
        {
            containerLootMoveInProgress = true;
            containerLootAttemptStartedAt = Time.time;
            inventory.RunNetworkTransaction(move.Operation, new Callback(result => CompleteContainerLootMove(result, move)));
        }

        private void CompleteContainerLootMove(IResult result, BodyGearMove move)
        {
            try
            {
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                containerLootNextMoveAt = Time.time + 0.2f;

                if (result?.Succeed == true || IsLootNowInBotInventory(BotOwner?.GetPlayer, move.Item))
                {
                    containerLootMovesSucceeded++;
                    InteractableObjects.RegisterLootedWeaponTree(BotOwner, move.Item);

                    if (followerData?.IsSquadMate == true)
                    {
                        InteractableObjects.StoreItem(BotOwner, move.Item);
                    }

                    if (move.Item is Weapon && move.Item.GetItemComponent<KnifeComponent>() == null)
                    {
                        containerLootWeaponListDirty = true;
                    }

                    return;
                }

                Modules.Logger.LogInfo(
                    $"[LootCommand] Container loot move failed for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {move.SourceName}:{move.Item?.TemplateId ?? "unknown"}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeContainerLoot move completion failed");
                Modules.Logger.LogError(ex);
                containerLootMoveInProgress = false;
                containerLootAttemptStartedAt = 0f;
                containerLootNextMoveAt = Time.time + 0.2f;
            }
        }

        private void FinishContainerLootNoMoreMoves()
        {
            if (containerLootWeaponListDirty)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
                containerLootWeaponListDirty = false;
            }

            TryCloseActiveLootContainerAfterSearch();

            if (containerLootMovesSucceeded > 0)
            {
                BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);
                ClearContainerLootState("TakeContainerLoot:done");
                return;
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.LootNothing, false);
            ClearContainerLootState("TakeContainerLoot:noSpace");
        }

        private void TryCloseActiveLootContainerAfterSearch()
        {
            if (activeLootContainer == null ||
                activeLootContainer.gameObject == null ||
                activeLootContainer.DoorState != EDoorState.Open)
            {
                return;
            }

            IPlayer player = BotOwner?.GetPlayer;
            if (player == null)
            {
                return;
            }

            try
            {
                InteractLootContainer(activeLootContainer, player, EInteractionType.Close);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[LootCommand] Failed to close looted container: {ex.Message}");
            }
        }

        private void CleanupContainerLootInteraction(string reason)
        {
            if (!containerLootMoveInProgress &&
                containerLootReadyAt <= 0f &&
                containerLootNextMoveAt <= 0f &&
                containerLootAttemptStartedAt <= 0f &&
                activeLootContainer == null &&
                activeLootSearchSource == null &&
                containerLootAttemptedItemIds.Count == 0)
            {
                return;
            }

            StopLootSearchSound();
            containerLootMoveInProgress = false;
            containerLootReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            containerLootAttemptStartedAt = 0f;
            containerLootMovesSucceeded = 0;
            containerLootWeaponListDirty = false;
            containerLootOpened = false;
            containerLootOpenRequestedAt = 0f;
            containerLootSearchStarted = false;
            containerLootAttemptedItemIds.Clear();
            activeLootContainer = null;
            followerData?.EndCommittedLootCommand(FollowerCommandType.TakeContainerLoot);

            if (BotOwner != null)
            {
                InteractableObjects.RemoveContainerLootTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurLootContainerTarget();
        }

        private void ClearContainerLootState(string reason)
        {
            if (!string.Equals(reason, "TakeContainerLoot:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeContainerLoot:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Container loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupContainerLootInteraction(reason);
            if (string.Equals(reason, "TakeContainerLoot:done", StringComparison.Ordinal))
            {
                followerData?.CompleteTakeContainerLoot();
            }
            else
            {
                followerData?.ClearCommand(reason);
            }
        }

        private static void InteractLootContainer(LootableContainer container, IPlayer player, EInteractionType action)
        {
            InteractionResult result = new InteractionResult(action);
            container.InteractingPlayer = player;
            container.Interact(result);
        }

        private sealed class BodyGearMove
        {
            public BodyGearMove(Item item, GInterface424 operation, string sourceName)
            {
                Item = item;
                Operation = operation;
                SourceName = sourceName;
            }

            public Item Item { get; }
            public GInterface424 Operation { get; }
            public string SourceName { get; }
        }

        private sealed class BodyGearCandidate
        {
            public BodyGearCandidate(
                Item item,
                EquipmentSlot? sourceSlot,
                string sourceName,
                int sourceTier,
                bool skipMagazine = false,
                bool bypassPriceThreshold = false,
                bool bypassBodyGearLootability = false)
            {
                Item = item;
                SourceSlot = sourceSlot;
                SourceName = sourceName;
                SourceTier = sourceTier;
                SkipMagazine = skipMagazine;
                BypassPriceThreshold = bypassPriceThreshold;
                BypassBodyGearLootability = bypassBodyGearLootability;
            }

            public Item Item { get; }
            public EquipmentSlot? SourceSlot { get; }
            public string SourceName { get; }
            public int SourceTier { get; }
            public bool SkipMagazine { get; }
            public bool BypassPriceThreshold { get; }
            public bool BypassBodyGearLootability { get; }
        }

        private static readonly EquipmentSlot[] BodyGearTopLevelSlotOrder =
        {
            // Mirrors the recovery priority used for player/fallen gear, with backpack already
            // attempted in a capacity-first pass. Scabbard, armband, and secure container are omitted.
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.ArmorVest,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Headwear,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster,
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets,
            EquipmentSlot.Earpiece,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Eyewear
        };

        private static readonly EquipmentSlot[] BodyGearContentSlotOrder =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets
        };

        private static readonly EquipmentSlot[] BodyGearCarrySlotOrder =
        {
            EquipmentSlot.Backpack,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets
        };

        private static readonly EquipmentSlot[] BodyGearWeaponSlotOrder =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        private static readonly EquipmentSlot[] FilteredLootCarrySlotOrder =
        {
            EquipmentSlot.Backpack,
            EquipmentSlot.Pockets
        };

        private void HandleOpenDoor()
        {
            activeDoor ??= InteractableObjects.GetDoorToOpen(BotOwner);
            if (activeDoor == null)
            {
                ClearOpenDoorState("OpenDoor:missingDoor");
                return;
            }

            if (activeDoor.DoorState == EDoorState.Open)
            {
                ClearOpenDoorState("OpenDoor:alreadyOpen");
                return;
            }

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();

            if (!doorMoveIssued)
            {
                Vector3 position = activeDoor.transform.position;
                if (!NavMesh.SamplePosition(position, out NavMeshHit navMeshHit, 2f, NavMesh.AllAreas))
                {
                    ClearOpenDoorState("OpenDoor:noNavMesh");
                    return;
                }

                if (BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) != NavMeshPathStatus.PathComplete)
                {
                    ClearOpenDoorState("OpenDoor:pathInvalid");
                    return;
                }

                BotOwner.GoToSomePointData.SetPoint(navMeshHit.position);
                BotOwner.Steering.LookToMovingDirection();
                doorMoveIssued = true;
                doorTimeoutAt = Time.time + 7f;
                return;
            }

            if (doorTimeoutAt > 0f && Time.time > doorTimeoutAt)
            {
                ClearOpenDoorState("OpenDoor:timeout");
                return;
            }

            if (!doorInteractIssued)
            {
                BotOwner.GoToSomePointData.UpdateToGo(false);
            }

            if (!BotOwner.GoToSomePointData.IsCome())
            {
                return;
            }

            if (doorInteractIssued)
            {
                return;
            }

            BotOwner.StopMove();
            BotOwner.DoorOpener.OnEndInteract -= OnDoorInteractEnded;
            BotOwner.DoorOpener.OnEndInteract += OnDoorInteractEnded;
            BotOwner.DoorOpener.Interact(activeDoor, EInteractionType.Open);
            doorInteractIssued = true;
        }

        private void OnDoorInteractEnded()
        {
            ClearOpenDoorState("OpenDoor:done");
        }

        private void CleanupDoorInteraction()
        {
            if (BotOwner?.DoorOpener != null)
            {
                BotOwner.DoorOpener.OnEndInteract -= OnDoorInteractEnded;
            }

            activeDoor = null;
            doorMoveIssued = false;
            doorInteractIssued = false;
            doorTimeoutAt = 0f;
        }

        private void ClearOpenDoorState(string reason)
        {
            CleanupDoorInteraction();
            InteractableObjects.RemoveOpener(BotOwner);
            InteractableObjects.SetCurDoor(null);
            followerData?.ClearCommand(reason);
        }
    }
}
