using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for EFT's attack-moving flank node. Used when the decision tree wants vanilla
    /// flank movement semantics rather than the follower-owned attack-moving wrapper.
    /// </summary>
    internal sealed class CombatAttackMovingFlankAction : FollowerCombatActionBase
    {
        private readonly AttackMovingFlank baseLogic;

        public CombatAttackMovingFlankAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new AttackMovingFlank(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<CoreActionResultParamsFlankMove>(data));
            EnforceCloseThreatStandingPose("attackMovingFlank", GetReason(data));
        }
    }
}
