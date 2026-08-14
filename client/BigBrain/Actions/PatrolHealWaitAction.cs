using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Owns the stationary gap between out-of-combat medical uses. A completed vanilla heal node
    /// is allowed to end, while patrol remains stopped until the next treatment can start.
    /// </summary>
    internal sealed class PatrolHealWaitAction : CustomLogic
    {
        public PatrolHealWaitAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            base.Start();
            StopForMedicalWait();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            StopForMedicalWait();
        }

        private void StopForMedicalWait()
        {
            if (BotOwner == null)
            {
                return;
            }

            FollowerRecovery.StopShooting(BotOwner);
            BotOwner.Mover?.Stop();
            if (BotOwner.Mover?.Sprinting == true)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
        }
    }
}
