using EFT;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue.Combat
{
    internal sealed class KnightCombatObjective : GoonCombatObjective
    {
        internal KnightCombatObjective(BotOwner owner, FollowerCombatCommon common)
            : base(owner, common, new KnightVanillaCombat(owner, KnightCombatLayer.LayerPriority), "knight") { }
    }
}