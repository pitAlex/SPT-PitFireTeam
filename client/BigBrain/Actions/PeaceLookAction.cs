using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla peaceful look behavior. Used for idle scanning when no combat action
    /// or explicit command owns the follower's attention.
    /// </summary>
    internal class PeaceLookAction : CustomLogic
    {
        private readonly PeaceLookNode baseLogic;

        public PeaceLookAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new PeaceLookNode(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
