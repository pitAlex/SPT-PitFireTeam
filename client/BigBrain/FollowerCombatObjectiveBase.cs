using EFT;

namespace pitTeam.BigBrain
{
    internal abstract class FollowerCombatObjectiveBase
    {
        protected readonly BotOwner BotOwner;
        protected readonly FollowerCombatCommon CombatCommon;

        protected FollowerCombatObjectiveBase(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            BotOwner = botOwner;
            CombatCommon = combatCommon;
        }

        public virtual bool IsComplete => false;

        public virtual void Reset()
        {
        }

        public virtual void Activate()
        {
        }

        public virtual void Deactivate()
        {
        }

        public abstract void DecisionChanged(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? prevDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision);

        public abstract void StartDecision();

        public abstract AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo goalEnemy);

        public abstract AICoreActionEnd ShallEndCurrentDecision(
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision);
    }
}
