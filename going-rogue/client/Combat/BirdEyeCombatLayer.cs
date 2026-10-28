using EFT;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue.Combat;

public sealed class BirdEyeCombatLayer : GoonCombatLayer
{
    public BirdEyeCombatLayer(BotOwner owner, int priority)
        : base(owner, priority, "followerBirdEye", "BirdEye", "BirdEye") { }
    internal override bool InitializeRole() => BotOwner.Brain.BaseBrain is FollowerBirdEyeLayersStrategy;
    internal override FollowerCombatLogicBase CreateLogic(BotOwner owner) => new BirdEyeCombatLogic(owner);
}
