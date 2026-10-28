using EFT;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue.Combat;

internal sealed class BirdEyeCombatObjective : FollowerCombatObjectiveBase
{
    private readonly FollowerCombatSniper sniper;
    private readonly BirdEyeSniperPositioning positioning;
    internal BirdEyeCombatObjective(BotOwner owner, FollowerCombatCommon common) : base(owner, common)
    {
        positioning = new BirdEyeSniperPositioning(owner, common);
        sniper = new FollowerCombatSniper(owner, common, positioning.GetDecision);
    }
    public override void Reset() { positioning.Reset(); sniper.Reset(); }
    public override void Activate() { Reset(); CombatCommon.ClearInitialDecision(); }
    public override void Deactivate() => positioning.Reset();
    public override void StartDecision() => CombatCommon.ClearInitialDecision();
    public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo enemy) => sniper.GetDecision(enemy);
    public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        => sniper.ShallEndCurrentDecision(current);
    public override void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previous,
        AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) => sniper.DecisionChanged(previous, next);
}
