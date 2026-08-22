using EFT;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatSniperObjective : FollowerCombatObjectiveBase
    {
        private readonly FollowerCombatSniper decisionSniper;

        public FollowerCombatSniperObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
            decisionSniper = new FollowerCombatSniper(botOwner, combatCommon);
        }

        public override void Reset()
        {
            decisionSniper.Reset();
        }

        public override void Activate()
        {
            // Returning from regroup should discard stale sniper-combat commitments, but it
            // must not look like a fresh combat entry that seeds PrepareStartDecision again.
            decisionSniper.Reset();
            CombatCommon.ClearInitialDecision();
        }

        public override void DecisionChanged(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            decisionSniper.DecisionChanged(prevDecision, nextDecision);
        }

        public override void StartDecision()
        {
            decisionSniper.PrepareStartDecision();
        }

        public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo goalEnemy)
        {
            return decisionSniper.GetDecision(goalEnemy);
        }

        public override AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            return decisionSniper.ShallEndCurrentDecision(currentDecision);
        }
    }
}
