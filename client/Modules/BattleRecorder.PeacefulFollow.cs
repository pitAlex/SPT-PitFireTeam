using System;
using System.Diagnostics;
using EFT;
using pitTeam.BigBrain.Actions;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        [Conditional("DEBUG")]
        internal static void RecordPeacefulFollowSample(
            BotOwner bot, Vector3 leaderPosition, bool inFollowRange, bool movingToSettlePoint,
            bool followingFormation, Vector3? formationDestination, string? leaderProfileId)
        {
            if (!CanRecordBot(bot)) return;
            RecorderFollowerState state = GetOrCreateState(bot);
            float now = Time.time;
            if (now < state.NextPeacefulFollowSample) return;
            state.NextPeacefulFollowSample = now + 0.1f;

            Player player = bot.GetPlayer;
            BotMover mover = bot.Mover;
            if (player == null || mover == null) return;

            try
            {
                var sprintMotion = state.PeacefulFollowMotion;
                state.PeacefulFollowMotion = default;
                WriteEventInternal("peacefulFollowSample", bot, new
                {
                    frame = Time.frameCount,
                    position = CreateVector(bot.Position),
                    leaderPosition = CreateVector(leaderPosition),
                    leaderProfileId,
                    inFollowRange,
                    movingToSettlePoint,
                    followingFormation,
                    formationDestination = formationDestination.HasValue ? CreateVector(formationDestination.Value) : null,
                    speed = SanitizeFloat(player.MovementContext?.ActualLinearVelocity ?? float.NaN),
                    inFoliage = bot.AIData?.IsInTree,
                    foliageIntersection = SanitizeFloat(bot.AIData?.FoliageIntersectionPercent ?? float.NaN),
                    yaw = SanitizeFloat(player.Rotation.x),
                    steeringMode = bot.Steering?.SteeringMode.ToString(),
                    steeringDirection = bot.Steering != null ? CreateVector(bot.Steering._lookDirection) : null,
                    requestedDirection = bot.Steering?.SteeringMode == EBotSteering.Direction
                        ? CreateVector(bot.Steering._customDirection) : null,
                    requestedPoint = bot.Steering?.SteeringMode == EBotSteering.ToCustomPoint
                        ? CreateVector(bot.Steering._customPoint) : null,
                    sprintRequested = mover.Sprinting,
                    sprintEngaged = FollowerCombatActionBase.IsActuallySprinting(bot),
                    sprintMotion = sprintMotion.Callbacks > 0 ? new
                    {
                        sprintMotion.Callbacks,
                        sprintMotion.SideCollisions,
                        sprintMotion.UsedContexts,
                        sprintMotion.InputsAboveNativeCap,
                        sprintMotion.InputDistance,
                        sprintMotion.PathDistance
                    } : null,
                    movement = CreatePeacefulFollowPathState(mover),
                    avoidanceOffset = CreateVector(mover.LocalAvoidance.TotalOffset)
                });
            }
            catch (Exception ex)
            {
                StopAfterFatalRecorderFailure("Peaceful follow sample failed.", ex);
            }
        }

        [Conditional("DEBUG")]
        internal static void RecordPeacefulFollowPath(
            BotOwner bot, string source, Vector3 requested, NavMeshPathStatus result, int skippedCorners, bool deferred = false)
        {
            if (!CanRecordBot(bot)) return;
            try
            {
                WriteEventInternal("peacefulFollowPath", bot, new
                {
                    frame = Time.frameCount,
                    source,
                    position = CreateVector(bot.Position),
                    requested = CreateVector(requested),
                    result = result.ToString(),
                    skippedPathStart = skippedCorners > 0,
                    skippedCorners,
                    deferred,
                    movement = CreatePeacefulFollowPathState(bot.Mover)
                });
            }
            catch (Exception ex)
            {
                StopAfterFatalRecorderFailure("Peaceful follow path sample failed.", ex);
            }
        }

        internal static bool CanRecordPeacefulFollowCorrection(BotOwner? bot) =>
            bot != null && CanRecordBot(bot) &&
            bot.Brain?.BaseBrain?.CurLayerInfo?.Name() == "pitTeam.FollowerPatrol" &&
            bot.Brain?.Agent?.LastResult().Reason == "FollowerPatrol";

        internal static void RecordPeacefulFollowMotion(
            BotOwner bot, Vector3 input, Vector3 pathDelta, bool sideCollision, bool contextUsed)
        {
            ref PeacefulFollowMotionSample motion = ref GetOrCreateState(bot).PeacefulFollowMotion;
            motion.Callbacks++;
            if (sideCollision) motion.SideCollisions++;
            if (contextUsed) motion.UsedContexts++;
            if (input.sqrMagnitude > 0.3f * 0.3f) motion.InputsAboveNativeCap++;
            input.y = 0f;
            pathDelta.y = 0f;
            motion.InputDistance += input.magnitude;
            motion.PathDistance += pathDelta.magnitude;
        }

        private struct PeacefulFollowMotionSample
        {
            public int Callbacks;
            public int SideCollisions;
            public int UsedContexts;
            public int InputsAboveNativeCap;
            public float InputDistance;
            public float PathDistance;
        }

        internal static void RecordPeacefulFollowCorrection(BotOwner bot, Vector3 before, Vector3 after)
        {
            if ((after - before).sqrMagnitude < 0.2f * 0.2f || !CanRecordBot(bot)) return;
            RecorderFollowerState state = GetOrCreateState(bot);
            float now = Time.time;
            if (now < state.NextPeacefulFollowCorrection) return;
            state.NextPeacefulFollowCorrection = now + 0.1f;

            WriteEventInternal("peacefulFollowPositionCorrection", bot, new
            {
                frame = Time.frameCount,
                before = CreateVector(before),
                after = CreateVector(after),
                lastNavMeshLink = CreateVector(bot.Mover.PrevSuccessLinkedFrom),
                movement = CreatePeacefulFollowPathState(bot.Mover)
            });
        }

        internal static void RecordPeacefulFollowTeleport(BotOwner bot, Vector3 before, Vector3 destination)
        {
            WriteEventInternal("peacefulFollowTeleport", bot, new
            {
                frame = Time.frameCount,
                before = CreateVector(before),
                destination = CreateVector(destination),
                stack = new StackTrace(1, false).ToString()
            });
        }

        private static object CreatePeacefulFollowPathState(BotMover mover)
        {
            BotPathController controller = mover.ActualPathController;
            Vector3? target = controller?.TargetPoint;
            AbstractBotPath? path = controller?.CurPath;
            return new
            {
                hasPath = mover.HasPathAndNoComplete,
                target = target.HasValue ? CreateVector(target.Value) : null,
                lastUpdatedCorner = controller?.HavePath == true ? CreateVector(controller.CurrentCornerPoint) : null,
                currentCorner = path != null && path.CurIndex < path.Length
                    ? CreateVector(path.GetPoint(path.CurIndex)) : null,
                directionToCorner = CreateVector(mover.DirCurPoint),
                positionOnPath = CreateVector(mover.PositionOnWay)
            };
        }
    }
}
