using EFT;
using pitTeam.BigBrain;

namespace pitTeam.BigBrain;

// The actual Marksman weapon/arrival policy is exercised by its extraction fixtures.
// This double verifies the addon's delegation and injected-policy wiring only.
internal sealed class FollowerCombatSniper
{
    internal static FollowerCombatSniper Last;
    internal int Resets, Changes, Ends;
    private readonly Func<EnemyInfo,AICoreActionResult<BotLogicDecision,CoreActionResultParams>?> positioning;
    internal FollowerCombatSniper(BotOwner owner,FollowerCombatCommon common,
        Func<EnemyInfo,AICoreActionResult<BotLogicDecision,CoreActionResultParams>?> positioning)
    {Last=this;this.positioning=positioning;}
    internal void Reset()=>Resets++;
    internal void DecisionChanged(AICoreActionResult<BotLogicDecision,CoreActionResultParams>? previous,
        AICoreActionResult<BotLogicDecision,CoreActionResultParams> next)=>Changes++;
    internal AICoreActionResult<BotLogicDecision,CoreActionResultParams> GetDecision(EnemyInfo enemy)
        =>positioning(enemy)??new(BotLogicDecision.holdPosition,"sharedMarksman");
    internal AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)
    {Ends++;return new("sharedMarksmanEnd",true);}
}
