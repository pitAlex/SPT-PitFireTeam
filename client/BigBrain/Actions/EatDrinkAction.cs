using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla eat/drink logic when the follower has been routed into a consume item
    /// action. The layer owns when this is allowed; the action only executes the vanilla node.
    /// </summary>
    internal class EatDrinkAction : CustomLogic
    {
        private readonly EatDrinkNode baseLogic;

        public EatDrinkAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new EatDrinkNode(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
