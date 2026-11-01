using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;

namespace pitTeam.GoingRogue.Combat
{
    // Core owns command admission and objective lifetime. Only the ordinary combat
    // objective is replaced; no Rifleman selector runs beside Knight's selector.
    internal sealed class KnightCombatLogic : FollowerCombatLogicBase
    {
        internal KnightCombatLogic(BotOwner owner) : base(owner) { }
        protected override FollowerCombatObjectiveBase CreateDefaultObjective(BotOwner owner, FollowerCombatCommon common)
            => new KnightCombatObjective(owner, common);
        protected override FollowerCombatObjectiveBase GetObjective() => defaultObjective;
        protected override bool ShouldConsumePushCommand(BotFollowerPlayer follower, EnemyInfo enemy)
            => follower != null && follower.TryPeekActiveCommand(out var command, out _, out _) &&
               command == FollowerCommandType.PushEnemy;
        protected override bool ShouldConsumeSuppressCommand(BotFollowerPlayer follower, EnemyInfo enemy)
            => follower != null && follower.TryGetActiveCommand(out var command, out _) &&
               command == FollowerCommandType.SuppressEnemy;
    }
}
