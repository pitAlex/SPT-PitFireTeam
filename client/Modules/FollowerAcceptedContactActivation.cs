using EFT;
using pitTeam.Patches;

namespace pitTeam.Modules
{
    // General external-SAIN compatibility: native GoalEnemy hides AI targets while
    // their EFT standby is sleeping. Only an already-admitted hostile goal may wake
    // that target; merely hearing/reporting an unadmitted enemy is insufficient.
    internal static class FollowerAcceptedContactActivation
    {
        internal static bool TryActivate(BotOwner owner, EnemyInfo goal)
        {
            if (!pitFireTeam.IsSAINInstalled || owner == null || owner.IsDead ||
                owner.BotState != EBotState.Active || !BossPlayers.IsFollower(owner) ||
                goal?.Person?.HealthController?.IsAlive != true ||
                !ReferenceEquals(owner.Memory?.GoalEnemy, goal) || owner.BotsGroup == null ||
                (!owner.BotsGroup.IsEnemy(goal.Person) && !owner.BotsGroup.IsPlayerEnemy(goal.Person)))
                return false;

            BotOwner target = goal.Person.AIData?.BotOwner;
            if (target == null || target.IsDead || target.BotState != EBotState.Active ||
                BossPlayers.IsFollower(target)) return false;

            // Reuse EFT activation rather than bypassing SAIN's IsEnemyActive or
            // changing lifecycle state, standby settings, knowledge or perception.
            HostilePeacefulLayerInterrupt.WakeHostileBot(target);
            return target.StandBy?.StandByType == BotStandByType.active;
        }
    }
}
