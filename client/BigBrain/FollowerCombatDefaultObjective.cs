using EFT;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatDefaultObjective : FollowerCombatObjectiveBase
    {
        private readonly FollowerCombatDefault decisionDefault;

        public FollowerCombatDefaultObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
            decisionDefault = new FollowerCombatDefault(botOwner, combatCommon);
        }

        public override void Reset()
        {
            decisionDefault.Reset();
        }

        public override void Activate()
        {
            // Returning from regroup should discard stale local default-combat commitments, but it
            // must not look like a fresh combat entry that seeds PrepareStartDecision again.
            decisionDefault.Reset();
            CombatCommon.ClearInitialDecision();
        }

        public override void DecisionChanged(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision)
        {
            decisionDefault.DecisionChanged(prevDecision, nextDecision);
        }

        public override void StartDecision()
        {
            decisionDefault.PrepareStartDecision();
        }

        public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo goalEnemy)
        {
            return decisionDefault.GetDecision(goalEnemy);
        }

        public override AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision)
        {
            return decisionDefault.ShallEndCurrentDecision(currentDecision);
        }
    }
}
