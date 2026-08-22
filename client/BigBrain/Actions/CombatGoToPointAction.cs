using DrakiaXYZ.BigBrain.Brains;
using EFT;
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

            if (ShouldRunToPoint(data))
            {
                BotOwner.SetTargetMoveSpeed(1f);
                SetCombatSprint(true);
                BotOwner.SetPose(1f);
            }

            baseLogic.UpdateNodeByBrain(GetData<CoreActionResultGoToPoint>(data));
        }

        private static bool ShouldRunToPoint(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data);
            return reason != null &&
                   reason.IndexOf(".runToPoint", StringComparison.Ordinal) >= 0;
        }
    }
}
