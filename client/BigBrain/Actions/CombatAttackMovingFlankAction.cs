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
        private readonly GClass209 baseLogic;

        public CombatAttackMovingFlankAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass209(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass29>(data));
            EnforceCloseThreatStandingPose("attackMovingFlank", GetReason(data));
        }
    }
}
