using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerRequestLayer : CustomLayer
    {
        private const bool EnableRequestLayerDebug = false;
#if DEBUG
        private const float RequestLayerDiagnosticThrottleSeconds = 1f;
#endif
        private BotFollowerPlayer? followerData;
#if DEBUG
        private string lastDiagnosticKey = string.Empty;
        private float nextDiagnosticAt;
#endif

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
                RecordRequestLayerDiagnostic(command, "combatOwnedCommand", () => CreateRequestLayerDiagnostic(command, false, false, false));
                return false;
            }

            bool hasKnownEnemy = hasCommand && followerData.HasKnownEnemy();
            bool readyForPatrolAfterCombat = followerData.IsReadyForPatrolAfterCombat();
            bool canRunDuringPostCombatHandoff = CanRunDuringPostCombatHandoff(command, hasKnownEnemy);
            if (!readyForPatrolAfterCombat &&
                !canRunDuringPostCombatHandoff)
            {
                RecordRequestLayerDiagnostic(
                    command,
                    "notReadyForPatrolAfterCombat",
                    () => CreateRequestLayerDiagnostic(command, readyForPatrolAfterCombat, canRunDuringPostCombatHandoff, hasKnownEnemy));
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
                RecordRequestLayerDiagnostic(
                    command,
                    "knownEnemyAcquired",
                    () => CreateRequestLayerDiagnostic(command, readyForPatrolAfterCombat, canRunDuringPostCombatHandoff, hasKnownEnemy));
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

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordRequestLayerDiagnostic(FollowerCommandType command, string reason, Func<object?> detailsFactory)
        {
#if DEBUG
            if (command == FollowerCommandType.None || BotOwner == null || !BattleRecorder.IsRecordingFor(BotOwner))
            {
                return;
            }

            string key = $"{command}:{reason}";
            if (StringComparer.Ordinal.Equals(key, lastDiagnosticKey) && Time.time < nextDiagnosticAt)
            {
                return;
            }

            lastDiagnosticKey = key;
            nextDiagnosticAt = Time.time + RequestLayerDiagnosticThrottleSeconds;
            BattleRecorder.RecordCommandDiagnostic(BotOwner, command, "requestLayer", reason, detailsFactory);
#endif
        }

        private object CreateRequestLayerDiagnostic(
            FollowerCommandType command,
            bool readyForPatrolAfterCombat,
            bool canRunDuringPostCombatHandoff,
            bool hasKnownEnemy)
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            return new
            {
                readyForPatrolAfterCombat,
                canRunDuringPostCombatHandoff,
                hasKnownEnemy,
                activeCommand = command.ToString(),
                memory = new
                {
                    haveEnemy = BotOwner?.Memory?.HaveEnemy == true,
                    goalEnemyPresent = goalEnemy != null,
                    goalEnemyAlive = goalEnemy?.Person?.HealthController?.IsAlive == true,
                    goalEnemyVisible = goalEnemy?.IsVisible == true,
                    goalEnemyCanShoot = goalEnemy?.CanShoot == true,
                    underFire = BotOwner?.Memory?.IsUnderFire == true
                },
                brain = new
                {
                    layer = BotOwner?.Brain?.BaseBrain?.CurLayerInfo?.Name(),
                    node = BotOwner?.Brain?.Agent?.GetActiveNodeName(),
                    lastAction = BotOwner?.Brain?.Agent?.LastResult().Action.ToString(),
                    lastReason = BotOwner?.Brain?.Agent?.LastResult().Reason
                },
                combatLayerActive = FollowerCombatLayer.IsFollowerCombatLayerActive(BotOwner)
            };
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
