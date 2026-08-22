using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla stimulator use. The combat/patrol layers decide when stimulators are
    /// safe or urgent; this action only updates the stock stim node.
    /// </summary>
    internal class HealStimulatorsAction : CustomLogic
    {
        private readonly StimulatorsNode baseLogic;

        public HealStimulatorsAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new StimulatorsNode(botOwner);
        }

        public override void Start()
        {
            base.Start();
            FollowerRecovery.StopShooting(BotOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerRecovery.StopShooting(BotOwner);
            baseLogic.UpdateNodeByBrain(data);
            FollowerRecovery.StopShooting(BotOwner);
        }
    }
}
