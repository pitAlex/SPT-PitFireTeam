using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Runs to the currently committed/assigned cover point. The action trusts the decision layer
    /// for cover selection, executes sprint movement, and turns back toward the threat on arrival.
    /// </summary>
    internal sealed class CombatRunToCoverAction : FollowerCombatActionBase
    {
        private enum MovementMode
        {
            Run,
            ThreatFacingFire
        }

        private const float PathRefreshInterval = 1.5f;
        private const float ArrivalDistance = 0.75f;
        private const float SprintEngageGraceSeconds = 0.35f;
        private const float PressureRunRetrySeconds = 1f;
        private readonly CombatAttackRetreatAction walkFallback;
        private readonly FollowerCombatFireOverlay recentThreatFireOverlay;
        private readonly FallbackRunRestoreGate restoreRunGate = new FallbackRunRestoreGate();
        private MovementMode movementMode;
        private CustomNavigationPoint? targetCover;
        private float nextPathRefreshTime;
        private float sprintRequestedAt;
        private bool targetPointAssigned;
        private bool combatWalkFallbackStarted;
        private float nextPressureRunRetryAt;
        private string? currentReason;
        private string? lastRecordedMovementState;

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
            walkFallback = new CombatAttackRetreatAction(botOwner);
            recentThreatFireOverlay = new FollowerCombatFireOverlay(botOwner);
        }

        public override void Start()
        {
            base.Start();
            movementMode = MovementMode.Run;
            restoreRunGate.Reset();
            StartRunMode();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            currentReason = GetReason(data);
            TryPreferMarksmanPrimaryAtRange(BotOwner.Memory?.GoalEnemy);

            bool canRun = TryCanActuallyRun(out string runGate);
            if (movementMode == MovementMode.ThreatFacingFire)
            {
                UpdateWalkFallback(data, canRun, runGate);
                return;
            }

            if (!canRun)
            {
                SwitchToWalkFallback(data, runGate);
                return;
            }

            UpdateRun(data);
        }

        public override void Stop()
        {
            if (movementMode == MovementMode.ThreatFacingFire)
            {
                if (combatWalkFallbackStarted)
                {
                    walkFallback.Stop();
                    combatWalkFallbackStarted = false;
                }

                SetCombatSprint(false);
            }
            else
            {
                StopRunMode();
            }

            recentThreatFireOverlay.Stop();
            base.Stop();
        }

        private void StartRunMode()
        {
            targetCover = null;
            nextPathRefreshTime = 0f;
            sprintRequestedAt = 0f;
            targetPointAssigned = false;
            combatWalkFallbackStarted = false;
            nextPressureRunRetryAt = 0f;
        }

        private void StopRunMode()
        {
            targetCover = null;
            sprintRequestedAt = 0f;
            targetPointAssigned = false;
            SetCombatSprint(false);
            StopCombatShooting();
        }

        private void SwitchToWalkFallback(CustomLayer.ActionData data, string gate)
        {
            CustomNavigationPoint? fallbackCover = null;
            if (EnsureTargetCover())
            {
                fallbackCover = targetCover;
                BotOwner.Memory.SetCoverPoints(targetCover);
            }

            StopRunMode();
            targetCover = fallbackCover;
            movementMode = MovementMode.ThreatFacingFire;
            restoreRunGate.Reset();
            nextPressureRunRetryAt = Time.time + PressureRunRetrySeconds;
            RecordMovementState("threatFacingFire", gate);
            if (HasLiveGoalEnemy())
            {
                recentThreatFireOverlay.Stop("liveEnemyRetreatFallback");
                combatWalkFallbackStarted = true;
                walkFallback.Start();
                walkFallback.Update(data);
                return;
            }

            UpdateRecentThreatWalkFallback();
        }

        private void UpdateWalkFallback(CustomLayer.ActionData data, bool canRun, string runGate)
        {
            if (EnsureTargetCover())
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
            }

            if (HasLiveGoalEnemy())
            {
                recentThreatFireOverlay.Stop("liveEnemyRetreatFallback");
                if (!combatWalkFallbackStarted)
                {
                    combatWalkFallbackStarted = true;
                    walkFallback.Start();
                }

                walkFallback.Update(data);
            }
            else
            {
                if (combatWalkFallbackStarted)
                {
                    walkFallback.Stop();
                    combatWalkFallbackStarted = false;
                }

                UpdateRecentThreatWalkFallback();
            }

            if (IsUnderActivePressure())
            {
                restoreRunGate.Reset();
                if (canRun && Time.time >= nextPressureRunRetryAt)
                {
                    RestoreRunMode(data, $"pressureRetry:{runGate}");
                }

                return;
            }

            if (!restoreRunGate.ShouldRestoreToRun(canRun, BotOwner.Memory?.GoalEnemy))
            {
                return;
            }

            RestoreRunMode(data, $"restored:{runGate}");
        }

        private void UpdateRun(CustomLayer.ActionData data)
        {
            if (!EnsureTargetCover())
            {
                StopRun();
                return;
            }

            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                BotOwner.Steering.LookToMovingDirection();
            }

            if ((!targetPointAssigned || Time.time >= nextPathRefreshTime) && targetCover != null)
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
                BotOwner.GoToSomePointData.SetPoint(targetCover.Position);
                targetPointAssigned = true;
                nextPathRefreshTime = Time.time + PathRefreshInterval;
            }

            BotOwner.GoToSomePointData.UpdateToGo(true, 1f, 1f);

            if (BotOwner.Mover.IsComeTo(ArrivalDistance, true, targetCover))
            {
                SetCombatSprint(false);
                StopCombatShooting();
                BotOwner.Memory.ComeToPoint();
                LookTowardThreatOnArrival();
                RecordMovementState("arrived", "coverReached");
                return;
            }

            StopCombatShooting();
            SetCombatSprint(true);
            if (IsActuallySprinting(BotOwner))
            {
                sprintRequestedAt = 0f;
                RecordMovementState("run", "sprintEngaged");
                return;
            }

            if (sprintRequestedAt <= 0f)
            {
                sprintRequestedAt = Time.time;
                RecordMovementState("run", "sprintRequested");
                return;
            }

            if (Time.time - sprintRequestedAt >= SprintEngageGraceSeconds)
            {
                SwitchToWalkFallback(data, "sprintDidNotEngage");
            }
        }

        private void UpdateRecentThreatWalkFallback()
        {
            if (!EnsureTargetCover())
            {
                StopRun();
                return;
            }

            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            bool fireOverlayOwnsLook = recentThreatFireOverlay.Update(
                null,
                currentReason,
                allowThreatSuppression: true,
                forceThreatLook: true,
                out _);
            if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner) && !fireOverlayOwnsLook)
            {
                BotOwner.Steering.LookToMovingDirection();
            }

            if ((!targetPointAssigned || Time.time >= nextPathRefreshTime) && targetCover != null)
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
                BotOwner.GoToSomePointData.SetPoint(targetCover.Position);
                targetPointAssigned = true;
                nextPathRefreshTime = Time.time + PathRefreshInterval;
            }

            BotOwner.GoToSomePointData.UpdateToGo(false, 1f, 1f);
        }

        private void RestoreRunMode(CustomLayer.ActionData data, string gate)
        {
            if (combatWalkFallbackStarted)
            {
                walkFallback.Stop();
                combatWalkFallbackStarted = false;
            }

            recentThreatFireOverlay.Stop("restoreRun");
            movementMode = MovementMode.Run;
            restoreRunGate.Reset();
            StartRunMode();
            RecordMovementState("run", gate);
            UpdateRun(data);
        }

        private bool TryCanActuallyRun(out string gate)
        {
            if (!BotOwner.CanSprintPlayer)
            {
                gate = "canSprintPlayerFalse";
                return false;
            }

            if (BotOwner.Mover == null)
            {
                gate = "moverMissing";
                return false;
            }

            if (BotOwner.Mover.NoSprint)
            {
                gate = "moverNoSprint";
                return false;
            }

            Player? player = BotOwner.GetPlayer ?? BotOwner.AIData?.Player;
            if (player?.MovementContext?.CanSprint == false)
            {
                gate = "movementCannotSprint";
                return false;
            }

            if (player?.MovementContext?.CanWalk == false)
            {
                gate = "movementCannotWalk";
                return false;
            }

            if (player?.HealthController != null &&
                (player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg)))
            {
                gate = "legInjury";
                return false;
            }

            DoorInteractionStatus doorStatus = BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            if (IsDoorInteractionBlockingSprint(doorStatus))
            {
                gate = $"door:{doorStatus}";
                return false;
            }

            gate = (int)doorStatus == 0 ? "canRun:doorStatusPending" : "canRun";
            return true;
        }

        private bool EnsureTargetCover()
        {
            if (targetCover != null)
            {
                return true;
            }

            targetCover = BotOwner.Memory?.CurCustomCoverPoint;
            targetPointAssigned = false;
            return targetCover != null;
        }

        private bool HasLiveGoalEnemy()
        {
            return BotOwner.Memory?.GoalEnemy?.Person?.HealthController?.IsAlive == true;
        }

        private bool IsUnderActivePressure()
        {
            return BotOwner.Memory?.IsUnderFire == true ||
                   FollowerCombatCommon.WasHitRecently(BotOwner, 1.5f) ||
                   FollowerAwareness.WasRecentlyHit(BotOwner);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordMovementState(string mode, string gate)
        {
            if (!BattleRecorder.IsRecordingFor(BotOwner, requireRecordedCombat: true))
            {
                return;
            }

            string state = $"{mode}:{gate}";
            if (string.Equals(lastRecordedMovementState, state, System.StringComparison.Ordinal))
            {
                return;
            }

            lastRecordedMovementState = state;
            BattleRecorder.RecordCombatMovementEvent(
                BotOwner,
                "runToCover",
                currentReason,
                mode,
                gate,
                targetCover?.Position);
        }

        private void StopRun()
        {
            targetCover = null;
            targetPointAssigned = false;
            SetCombatSprint(false);
            BotOwner.StopMove();
        }

        private void LookTowardThreatOnArrival()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
                return;
            }

            Vector3 lookPoint = goalEnemy.EnemyLastPositionReal;
            if (!IsFinite(lookPoint))
            {
                lookPoint = goalEnemy.CurrPosition;
            }

            if (IsFinite(lookPoint))
            {
                BotOwner.Steering.LookToPoint(lookPoint + Vector3.up * 0.8f);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }
    }
}
