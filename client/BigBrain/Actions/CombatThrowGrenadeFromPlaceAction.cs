using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Explicit throw-grenade-from-place action. It preserves the caller's committed grenade target
    /// and gives the vanilla grenade node only this narrow throw task.
    /// </summary>
    internal sealed class CombatThrowGrenadeFromPlaceAction : FollowerCombatActionBase
    {
        private readonly GClass287 baseLogic;

        public CombatThrowGrenadeFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass287(botOwner);
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
