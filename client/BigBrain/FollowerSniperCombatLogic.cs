using EFT;
using pitTeam.Components;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerSniperCombatLogic : FollowerCombatLogicBase
    {
        public FollowerSniperCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void StartDecision()
        {
            currentObjective = CombatObjectiveKind.Default;
            sniperObjective.StartDecision();
        }

        protected override FollowerCombatObjectiveBase GetObjective()
        {
            return sniperObjective;
        }

        protected override bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.NeedSniper;
        }

        protected override bool ShouldConsumeSuppressCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return followerData?.SuppressEnemyUseAutomaticSecondary == true &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }
    }
}
