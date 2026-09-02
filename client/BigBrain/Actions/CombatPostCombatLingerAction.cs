using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Short post-combat transition owned by the core combat layer. Unlike the generic vanilla
    /// hold node, this action never chooses prone or combat hold policy after the enemy disappears.
    /// It preserves the inherited posture/look briefly, then settles into one stable natural pose
    /// and one lateral glance for the rest of the linger window.
    /// </summary>
    internal sealed class CombatPostCombatLingerAction : FollowerCombatActionBase
    {
        private const float TransitionMinSeconds = 0.35f;
        private const float TransitionMaxSeconds = 0.6f;
        private const float LateralLookMinDegrees = 35f;
        private const float LateralLookMaxDegrees = 75f;
        private const float DirectionEpsilonSqr = 0.01f;
        private const float StationarySpeedThreshold = 0.2f;
        private const float StableGroundSeconds = 0.25f;
        private const float GroundProbeOriginHeight = 0.35f;
        private const float GroundProbeDistance = 1.5f;

        private float transitionUntil;
        private float stationaryGroundSince;
        private float targetPose;
        private Vector3 entryLookDirection;
        private Vector3 lingerLookDirection;
        private bool stanceApplied;

        public CombatPostCombatLingerAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            try
            {
                base.Start();
                StopStationaryCombatMovement();
                FollowerRecovery.StopShooting(BotOwner);

                entryLookDirection = GetHorizontalLookDirection();
                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                float yaw = UnityEngine.Random.Range(LateralLookMinDegrees, LateralLookMaxDegrees) * side;
                lingerLookDirection = Quaternion.Euler(0f, yaw, 0f) * entryLookDirection;
                lingerLookDirection.y = 0f;
                lingerLookDirection.Normalize();

                targetPose = UnityEngine.Random.Range(0, 3) switch
                {
                    0 => 1f,
                    1 => 0.5f,
                    _ => 0f,
                };
                transitionUntil = Time.time + UnityEngine.Random.Range(TransitionMinSeconds, TransitionMaxSeconds);
                stationaryGroundSince = 0f;
                stanceApplied = false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[PostCombatLinger] Failed to start for {BotOwner?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
                FollowerRecovery.StopShooting(BotOwner);
                BotOwner?.StopMove();
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            try
            {
                FollowerRecovery.StopShooting(BotOwner);

                bool isProne = BotOwner?.BotLay?.IsLay == true;
                if (isProne)
                {
                    // If the preceding combat action legitimately ended while prone, use EFT's
                    // normal get-up transition. Do not snap the animation or treat crouch as prone.
                    BotOwner.BotLay.GetUp(false);
                }

                bool transitioning = Time.time < transitionUntil;
                bool stationaryOnStableGround = stanceApplied || UpdateStableGroundState();
                Vector3 lookDirection = transitioning ? entryLookDirection : lingerLookDirection;
                if (lookDirection.sqrMagnitude >= DirectionEpsilonSqr)
                {
                    BotOwner.Steering.LookToDirection(lookDirection);
                }

                if (!transitioning &&
                    !stanceApplied &&
                    stationaryOnStableGround &&
                    BotOwner.BotLay?.IsLay != true &&
                    BotOwner.Mover != null)
                {
                    if (!Mathf.Approximately(BotOwner.Mover.TargetPose, targetPose))
                    {
                        BotOwner.SetPose(targetPose);
                    }

                    stanceApplied = true;
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[PostCombatLinger] Update failed for {BotOwner?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
                FollowerRecovery.StopShooting(BotOwner);
                BotOwner?.StopMove();
            }
        }

        private bool UpdateStableGroundState()
        {
            var movementContext = BotOwner?.GetPlayer?.MovementContext;
            if (movementContext == null || !movementContext.IsGrounded)
            {
                stationaryGroundSince = 0f;
                return false;
            }

            Vector3 velocity = movementContext.Velocity;
            float stationarySpeedSqr = StationarySpeedThreshold * StationarySpeedThreshold;
            if (!IsFinite(velocity) || velocity.sqrMagnitude > stationarySpeedSqr)
            {
                stationaryGroundSince = 0f;
                return false;
            }

            Vector3 groundProbeOrigin = BotOwner.Position + Vector3.up * GroundProbeOriginHeight;
            if (!Physics.Raycast(
                    groundProbeOrigin,
                    Vector3.down,
                    GroundProbeDistance,
                    LayersMaskController.HighPolyWithTerrainMask,
                    QueryTriggerInteraction.Ignore))
            {
                stationaryGroundSince = 0f;
                return false;
            }

            if (stationaryGroundSince <= 0f)
            {
                stationaryGroundSince = Time.time;
                return false;
            }

            return Time.time - stationaryGroundSince >= StableGroundSeconds;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        private Vector3 GetHorizontalLookDirection()
        {
            Vector3 direction = BotOwner?.LookDirection ?? Vector3.zero;
            direction.y = 0f;
            if (direction.sqrMagnitude < DirectionEpsilonSqr)
            {
                direction = BotOwner?.GetPlayer?.Transform?.forward ?? Vector3.forward;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude < DirectionEpsilonSqr)
            {
                return Vector3.forward;
            }

            return direction.normalized;
        }
    }
}
