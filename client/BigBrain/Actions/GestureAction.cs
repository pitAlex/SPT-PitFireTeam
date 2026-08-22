using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla gesture execution. Higher layers decide which gesture is appropriate;
    /// this action only runs the stock gesture node.
    /// </summary>
    internal class GestureAction : CustomLogic
    {
        private readonly GestureNode baseLogic;

        public GestureAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GestureNode(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
