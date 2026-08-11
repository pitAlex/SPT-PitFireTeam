using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
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
    internal partial class GestureCommandAction
    {
        private void ResetMoveToPointState()
        {
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            activeMoveTarget = Vector3.zero;
            nextPathCheckAt = 0f;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
            ResetMoveToPointDiagnostics();
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void ResetMoveToPointDiagnostics()
        {
#if DEBUG
            moveLastProgressDistance = 0f;
            moveLastProgressAt = 0f;
            nextMoveProgressDiagnosticAt = 0f;
            lastMoveDiagnosticKey = string.Empty;
            nextMoveDiagnosticAt = 0f;
#endif
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
            TrackMoveToPointProgress(target, distance);
            if (distance > MoveToPointArrivalDistance && moveArrivalLookUntil > 0f)
            {
                RecordMoveToPointDiagnostic("arrivalHoldCancelled", target, distance, () => CreateMoveToPointDiagnostic(target, distance));
                moveArrivalLookUntil = 0f;
            }
            if (HasArrivedAtMovePoint(distance))
            {
                HandleMovePointArrivalLookAround();
                if (Time.time < moveArrivalLookUntil)
                {
                    RecordMoveToPointDiagnostic("arrivalHoldWaiting", target, distance, () => CreateMoveToPointDiagnostic(target, distance));
                    return;
                }
                moveArrivalLookUntil = 0f;
                BotOwner.StopMove();
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                moveCommandInitialized = false;
                RecordMoveToPointDiagnostic("arrived", target, distance, () => CreateMoveToPointDiagnostic(target, distance));
                followerData?.CompleteMoveToPoint("MoveToPoint:arrived");
                return;
            }

            bool targetChanged = !moveCommandInitialized || (activeMoveTarget - target).sqrMagnitude > MoveToPointTargetChangeDistanceSqr;
            bool targetMissing = BotOwner.GoToSomePointData?.HaveTarget() != true;
            bool targetCompletedEarly = BotOwner.GoToSomePointData?.IsCome() == true && distance > MoveToPointArrivalDistance;
            bool targetRefreshed = false;
            if (targetChanged || targetMissing || targetCompletedEarly)
            {
                RecordMoveToPointDiagnostic(
                    "targetRefresh",
                    target,
                    distance,
                    () => CreateMoveToPointDiagnostic(
                        target,
                        distance,
                        new
                        {
                            targetChanged,
                            targetMissing,
                            targetCompletedEarly
                        }));
                BotOwner.GoToSomePointData.SetPoint(target);
                moveCommandInitialized = true;
                activeMoveTarget = target;
                moveArrivalLookUntil = 0f;
                nextHoldLookChangeAt = 0f;
                targetRefreshed = true;
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    RecordMoveToPointDiagnostic(
                        "pathInvalid",
                        target,
                        distance,
                        () => CreateMoveToPointDiagnostic(
                            target,
                            distance,
                            new
                            {
                                pathStatus = path.status.ToString(),
                                cornerCount = path.corners?.Length ?? 0
                            }));
                    followerData?.CompleteMoveToPoint("MoveToPoint:pathInvalid");
                    BotOwner.StopMove();
                    return;
                }

                // EFT keeps the point after its active mover path ends, so HaveTarget() alone is
                // not a liveness signal. Re-arm the same point on this existing 0.5s validation
                // cadence instead of waiting for BotGoToPointData's three-second retry.
                if (!targetRefreshed && BotOwner.Mover?.HasPathAndNoComplete != true)
                {
                    RecordMoveToPointDiagnostic(
                        "pathRecovered",
                        target,
                        distance,
                        () => CreateMoveToPointDiagnostic(
                            target,
                            distance,
                            new
                            {
                                pathStatus = path.status.ToString(),
                                cornerCount = path.corners?.Length ?? 0
                            }));
                    BotOwner.GoToSomePointData.SetPoint(target);
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

        [System.Diagnostics.Conditional("DEBUG")]
        private void TrackMoveToPointProgress(Vector3 target, float distance)
        {
#if DEBUG
            if (!moveCommandInitialized || moveLastProgressAt <= 0f)
            {
                moveLastProgressDistance = distance;
                moveLastProgressAt = Time.time;
                return;
            }

            if (distance < moveLastProgressDistance - MoveToPointProgressEpsilon)
            {
                moveLastProgressDistance = distance;
                moveLastProgressAt = Time.time;
                return;
            }

            if (Time.time - moveLastProgressAt < MoveToPointNoProgressSeconds ||
                Time.time < nextMoveProgressDiagnosticAt)
            {
                return;
            }

            nextMoveProgressDiagnosticAt = Time.time + MoveToPointDiagnosticThrottleSeconds;
            RecordMoveToPointDiagnostic(
                "noProgress",
                target,
                distance,
                () => CreateMoveToPointDiagnostic(
                    target,
                    distance,
                    new
                    {
                        lastProgressDistance = SanitizeFloat(moveLastProgressDistance),
                        noProgressSeconds = SanitizeFloat(Time.time - moveLastProgressAt)
                    }));
#endif
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordMoveToPointDiagnostic(string reason, Vector3 target, float distance, Func<object?> detailsFactory)
        {
#if DEBUG
            if (BotOwner == null || !BattleRecorder.IsRecordingFor(BotOwner))
            {
                return;
            }

            string key = $"MoveToPoint:{reason}";
            if (StringComparer.Ordinal.Equals(key, lastMoveDiagnosticKey) && Time.time < nextMoveDiagnosticAt)
            {
                return;
            }

            lastMoveDiagnosticKey = key;
            nextMoveDiagnosticAt = Time.time + MoveToPointDiagnosticThrottleSeconds;
            BattleRecorder.RecordCommandDiagnostic(BotOwner, FollowerCommandType.MoveToPoint, "moveToPoint", reason, detailsFactory);
#endif
        }

        private object CreateMoveToPointDiagnostic(Vector3 target, float distance, object? extra = null)
        {
            bool hasCurrentTarget = BotOwner.GoToSomePointData?.HaveTarget() == true;
            Vector3 currentTarget = hasCurrentTarget ? BotOwner.GoToSomePointData.Point : Vector3.zero;
            Vector3 lookOverridePoint = Vector3.zero;
            bool hasLookOverride = followerData?.TryGetCommandLookOverride(out lookOverridePoint) == true;
            return new
            {
                position = CreateDiagnosticVector(BotOwner.Position),
                target = CreateDiagnosticVector(target),
                activeMoveTarget = moveCommandInitialized ? CreateDiagnosticVector(activeMoveTarget) : null,
                currentMoveTarget = hasCurrentTarget ? CreateDiagnosticVector(currentTarget) : null,
                distance = SanitizeFloat(distance),
                initialized = moveCommandInitialized,
                issueSequence = followerData?.MoveToPointIssueSequence,
                lastIssueSequence = lastMoveToPointIssueSequence,
                arrivalHoldRemaining = moveArrivalLookUntil > 0f ? SanitizeFloat(Mathf.Max(0f, moveArrivalLookUntil - Time.time)) : null,
                nextPathCheckIn = SanitizeFloat(Mathf.Max(0f, nextPathCheckAt - Time.time)),
                movement = new
                {
                    moverPaused = BotOwner.Mover?.Pause == true,
                    sprinting = BotOwner.Mover?.Sprinting == true,
                    hasPathAndNoComplete = BotOwner.Mover?.HasPathAndNoComplete == true,
                    hasGoToTarget = hasCurrentTarget,
                    reachedTarget = BotOwner.GoToSomePointData?.IsCome() == true,
                    targetPose = SanitizeFloat(BotOwner.Mover?.TargetPose ?? 0f),
                    poseLevel = SanitizeFloat(BotOwner.GetPlayer?.MovementContext?.PoseLevel ?? 0f),
                    isInPatrol = BotOwner.GetPlayer?.MovementContext?.IsInPatrol == true,
                    blockFirearms = BotOwner.GetPlayer?.MovementContext?.BlockFirearms == true
                },
                weaponPosture = CreateWeaponPostureDiagnostic(),
                look = new
                {
                    lookDirection = CreateDiagnosticVector(BotOwner.LookDirection),
                    hasCommandLookOverride = hasLookOverride,
                    commandLookOverride = hasLookOverride ? CreateDiagnosticVector(lookOverridePoint) : null
                },
                brain = new
                {
                    layer = BotOwner.Brain?.BaseBrain?.CurLayerInfo?.Name(),
                    node = BotOwner.Brain?.Agent?.GetActiveNodeName(),
                    lastAction = BotOwner.Brain?.Agent?.LastResult().Action.ToString(),
                    lastReason = BotOwner.Brain?.Agent?.LastResult().Reason
                },
                extra
            };
        }

        private object CreateWeaponPostureDiagnostic()
        {
            IFirearmHandsController? shootController = BotOwner.WeaponManager?.ShootController;
            BotWeaponSelector? selector = BotOwner.WeaponManager?.Selector;
            IBotAiming? currentAiming = BotOwner.AimingManager?.CurrentAiming;
            return new
            {
                peaceHardAimActive = BotOwner.PeaceHardAim?.HaveActions() == true,
                secondWeaponWatchActive = BotOwner.SecondWeaponData?.HaveActions() == true,
                shootControllerPresent = shootController != null,
                isAiming = shootController?.IsAiming == true,
                weaponReady = BotOwner.WeaponManager?.IsWeaponReady == true,
                selectorWeaponReady = selector?.IsWeaponReady == true,
                selectorChanging = selector?.IsChanging == true,
                canChangeToSecondWeapons = selector?.CanChangeToSecondWeapons == true,
                currentSlot = selector?.LastEquipmentSlot.ToString(),
                reloading = BotOwner.WeaponManager?.Reload?.Reloading == true,
                canShootByState = BotOwner.ShootData?.CanShootByState == true,
                aimingType = BotOwner.AimingManager?.Current.ToString(),
                currentAimingReady = currentAiming?.IsReady == true,
                currentAimingHardAim = currentAiming?.HardAim == true,
                currentAimingDistance = currentAiming != null ? SanitizeFloat(currentAiming.LastDist2Target) : null
            };
        }

        private static object CreateDiagnosticVector(Vector3 value)
        {
            return new
            {
                x = SanitizeFloat(value.x),
                y = SanitizeFloat(value.y),
                z = SanitizeFloat(value.z)
            };
        }

        private static float? SanitizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return null;
            }

            return value;
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
                RecordMoveToPointDiagnostic(
                    "arrivalHoldStart",
                    activeMoveTarget,
                    (activeMoveTarget - BotOwner.Position).magnitude,
                    () => CreateMoveToPointDiagnostic(activeMoveTarget, (activeMoveTarget - BotOwner.Position).magnitude));
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

    }
}
