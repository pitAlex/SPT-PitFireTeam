using EFT;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue.Combat
{
    public sealed class BigPipeCombatLayer : GoonCombatLayer
    {
        public BigPipeCombatLayer(BotOwner owner, int priority)
            : base(owner, priority, "followerBigPipe", "BigPipe", "BigPipe") { }
        internal static bool IsOwned(BotOwner owner) => GoonCombatLayer.IsOwned(owner, "followerBigPipe");
        internal override bool InitializeRole() => BotOwner.Brain.BaseBrain is FollowerBigPipeLayersStrategy;
        internal override FollowerCombatLogicBase CreateLogic(BotOwner owner) => new BigPipeCombatLogic(owner);
    }
}
