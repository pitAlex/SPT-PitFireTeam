using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;

namespace pitTeam.BigBrain.Actions
{
    // Patrol's explicit owner while squad combat still prevents normal following.
    // Preserve the inherited pose/look; do not run vanilla idle prone/reload behavior.
    internal sealed class PatrolCombatWaitAction : CustomLogic
    {
        public PatrolCombatWaitAction(BotOwner botOwner) : base(botOwner) { }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerRecovery.StopShooting(BotOwner);
            BotOwner.Mover?.Stop();
            if (BotOwner.Mover?.Sprinting == true)
                BotOwner.Mover.Sprint(false, false);
            BotOwner.StopMove();
        }
    }
}
