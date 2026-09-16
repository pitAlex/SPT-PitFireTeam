// Adapted from SAIN 4.5.1 StandAndShootAction (Solarint, MIT; SAIN-LICENSE.txt).
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;

namespace pitTeam.SAINAddon;

// Retain native shooting/steering and cover posture, without StandAndShoot's
// random entry swing: the push objective has already committed this position.
internal sealed class SAINFollowerPushHoldAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerPushHoldAction)), IBotAction
{
    public override void Start()
    {
        base.Start();
        Bot.Mover.Stop();
        Bot.Mover.Lean.HoldLean(0.66f);
    }
    public override void Update(CustomLayer.ActionData data) => Bot.Mover.Pose.SetPoseToCover(Bot.GoalEnemy);
}
