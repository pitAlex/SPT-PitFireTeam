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

        private float transitionUntil;
        private float targetPose;
        private Vector3 entryLookDirection;
        private Vector3 lingerLookDirection;

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
                Vector3 lookDirection = transitioning ? entryLookDirection : lingerLookDirection;
                if (lookDirection.sqrMagnitude >= DirectionEpsilonSqr)
                {
                    BotOwner.Steering.LookToDirection(lookDirection);
                }

                if (!transitioning && BotOwner.BotLay?.IsLay != true && BotOwner.Mover != null &&
                    !Mathf.Approximately(BotOwner.Mover.TargetPose, targetPose))
                {
                    BotOwner.SetPose(targetPose);
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
