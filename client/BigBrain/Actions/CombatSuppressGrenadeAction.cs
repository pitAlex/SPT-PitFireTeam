using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Vanilla suppress-grenade wrapper used only when the decision tree explicitly selected grenade
    /// suppression. Regular opportunistic grenade use is blocked elsewhere.
    /// </summary>
    internal sealed class CombatSuppressGrenadeAction : FollowerCombatActionBase
    {
        private readonly GrenadeSuppressNode baseLogic;

        public CombatSuppressGrenadeAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GrenadeSuppressNode(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerGrenadeRuntimeGate.EnableExplicitThrow(BotOwner);
            BotOwner.SetPose(1f);
            baseLogic.UpdateNodeByBrain(GetRawData(data));
            BotOwner.SetPose(1f);
        }
    }
}
