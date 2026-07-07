using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerRequestLayer : CustomLayer
    {
        private const bool EnableRequestLayerDebug = false;
        private BotFollowerPlayer? followerData;

        public FollowerRequestLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "pitTeam.FollowerRequest";
        }

        public override void Start()
        {
            base.Start();

            if (BotOwner?.Mover != null)
            {
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }
            }

            if (BotOwner?.Mover?.TargetPose < 0.85f)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner?.PatrollingData?.Pause();

            if (BotOwner?.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }

            if (BotOwner != null)
            {
                FollowerRecovery.SoftReset(BotOwner);
            }
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;

            if (followerData == null) followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);

            if (followerData == null)
            {
                return false;
            }

            if (followerData.IsBackpackInspectionActive)
            {
                return false;
            }

            bool hasCommand = followerData.TryGetActiveCommand(out FollowerCommandType command, out _);
            if (hasCommand &&
                (command == FollowerCommandType.PushEnemy ||
                 command == FollowerCommandType.SuppressEnemy ||
                 command == FollowerCommandType.NeedSniper ||
                 command == FollowerCommandType.CombatComeToBossCover ||
                 command == FollowerCommandType.CombatMoveToPointTactical))
            {
                return false;
            }

            bool hasKnownEnemy = hasCommand && followerData.HasKnownEnemy();
            if (!followerData.IsReadyForPatrolAfterCombat() &&
                !CanRunDuringPostCombatHandoff(command, hasKnownEnemy))
            {
                return false;
            }

            if (hasCommand && hasKnownEnemy)
            {
                if (command == FollowerCommandType.RegroupNearBoss)
                {
                    // let sain continue the regroup on entering combat
                    if (pitFireTeam.ShouldSainRegroupLayerHandle(BotOwner))
                    {
                        BotOwner.StopMove();
                        return false;
                    }

                    // Core combat regroup is now a combat objective trigger, not a request-layer
                    // action. Keep the command intact and let the combat logic consume it.
                    return false;
                }

                if (command == FollowerCommandType.PushEnemy ||
                    command == FollowerCommandType.SuppressEnemy ||
                    command == FollowerCommandType.NeedSniper ||
                    command == FollowerCommandType.CombatComeToBossCover ||
                    command == FollowerCommandType.CombatMoveToPointTactical)
                {
                    return false;
                }

                InteractableObjects.RemoveTaker(BotOwner);
                InteractableObjects.RemoveBodyLootTaker(BotOwner);
                InteractableObjects.RemoveContainerLootTaker(BotOwner);
                InteractableObjects.RemoveOpener(BotOwner);
                followerData.ClearCommand("KnownEnemyAcquired");
                return false;
            }


            return hasCommand;
        }

        private static bool CanRunDuringPostCombatHandoff(FollowerCommandType command, bool hasKnownEnemy)
        {
            if (hasKnownEnemy)
            {
                return false;
            }

            return command == FollowerCommandType.HoldPosition ||
                   command == FollowerCommandType.MoveToPoint ||
                   command == FollowerCommandType.ComeCloser;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(GestureCommandAction), "GestureCommand");
        }

        public override bool IsCurrentActionEnding()
        {
            if (!IsActive())
            {
                return true;
            }

            int gestureCommandLogicId = GetGestureCommandLogicId();
            if (gestureCommandLogicId < 0)
            {
                return true;
            }

            return BotOwner?.Brain?.Agent?.LastResult().Action != (BotLogicDecision)gestureCommandLogicId;
        }

        private static int GetGestureCommandLogicId()
        {
            return BrainManager.CustomLogicsReadOnly.TryGetValue(typeof(GestureCommandAction), out int logicId)
                ? logicId
                : -1;
        }
    }
}
