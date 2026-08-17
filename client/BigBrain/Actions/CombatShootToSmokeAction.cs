using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Smoke-fire action wrapper. Used when the combat decision intentionally wants the vanilla
    /// shoot-to-smoke behavior rather than normal visible-enemy fire.
    /// </summary>
    internal sealed class CombatShootToSmokeAction : FollowerCombatActionBase
    {
        private readonly GClass185 baseLogic;

        public CombatShootToSmokeAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass185(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (StopUnownedGrenadeLauncherFire(GetReason(data), BotOwner.Memory?.GoalEnemy))
            {
                return;
            }

            string? reason = GetReason(data);
            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
            EnforceCloseThreatStandingPose("shootToSmoke", reason);
        }
    }
}
