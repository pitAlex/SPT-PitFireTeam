using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;

namespace pitTeam.GoingRogue.Combat;

internal sealed class BirdEyeCombatLogic : FollowerCombatLogicBase
{
    internal BirdEyeCombatLogic(BotOwner owner) : base(owner) { }
    protected override FollowerCombatObjectiveBase CreateDefaultObjective(BotOwner owner, FollowerCombatCommon common)
        => new BirdEyeCombatObjective(owner, common);
    protected override FollowerCombatObjectiveBase GetObjective() => defaultObjective;
    protected override bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer follower, EnemyInfo enemy)
        => follower != null && follower.TryGetActiveCommand(out var command, out _) && command == FollowerCommandType.NeedSniper;
    protected override bool ShouldConsumeSuppressCommand(BotFollowerPlayer follower, EnemyInfo enemy)
        => follower?.SuppressEnemyUseAutomaticSecondary == true &&
           follower.TryGetActiveCommand(out var command, out _) && command == FollowerCommandType.SuppressEnemy;
    // Sniper support follows Knight's positioning; it never consumes an assault PushEnemy.
}
