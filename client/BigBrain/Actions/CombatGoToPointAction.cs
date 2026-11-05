using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using System;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Generic combat point movement. The decision layer must set <c>GoToSomePointData</c> before
    /// selecting this action; the action only executes that destination and keeps the bot combat-ready.
    /// </summary>
    internal sealed class CombatGoToPointAction : FollowerCombatActionBase
    {
        private readonly GoToSomePoint baseLogic;

        public CombatGoToPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GoToSomePoint(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            string? reason = GetReason(data);
            bool controlledApproach = reason != null &&
                (reason.StartsWith("knight.", StringComparison.Ordinal) || reason.StartsWith("bigPipe.", StringComparison.Ordinal)) &&
                (reason.IndexOf(".walkToPoint", StringComparison.Ordinal) >= 0 || reason.IndexOf(".runToPoint", StringComparison.Ordinal) >= 0);
            bool run = ShouldRunToPoint(data);
            if (!controlledApproach)
            {
                if (run)
                {
                    BotOwner.SetTargetMoveSpeed(1f);
                    SetCombatSprint(true);
                    BotOwner.SetPose(1f);
                }
                baseLogic.UpdateNodeByBrain(GetData<CoreActionResultGoToPoint>(data));
                return;
            }
            // Native go-to-point updates can overwrite sprint. The admitted aggressor policy
            // owns the final run/walk choice only for its explicitly marked point decisions.
            baseLogic.UpdateNodeByBrain(GetData<CoreActionResultGoToPoint>(data));
            run = run && BotOwner.Memory?.GoalEnemy is { } enemy && FollowerEnemyTracking.Distance(enemy) > 30f;
            run = run && BotOwner.CanSprintPlayer && BotOwner.Mover?.NoSprint != true;
            if (run)
            {
                BotOwner.SetTargetMoveSpeed(1f);
                SetCombatSprint(true);
                BotOwner.SetPose(1f);
            }

            else SetCombatSprint(false);
        }

        private static bool ShouldRunToPoint(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data);
            return reason != null &&
                   reason.IndexOf(".runToPoint", StringComparison.Ordinal) >= 0;
        }
    }
}
