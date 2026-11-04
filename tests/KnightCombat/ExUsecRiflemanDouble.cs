using EFT;
using pitTeam.BigBrain;

namespace pitTeam.BigBrain;

// Only companion adapter delegation is doubled. The production Rifleman router
// and advancement gate have their own extracted boundary fixture.
internal sealed class FollowerCombatDefault
{
    internal static FollowerCombatDefault Last;
    internal int Starts, Resets, Changes, Ends, Stops;
    internal bool StopAllowed=true;
    internal Func<EnemyInfo,bool> Allowed;
    private readonly Func<EnemyInfo,AICoreActionResult<BotLogicDecision,CoreActionResultParams>?> command;
    internal FollowerCombatDefault(BotOwner owner,FollowerCombatCommon common,
        Func<EnemyInfo,AICoreActionResult<BotLogicDecision,CoreActionResultParams>?> command,Func<EnemyInfo,bool> allowed)
    {Last=this;this.command=command;Allowed=allowed;}
    internal void Reset()=>Resets++;
    internal void PrepareStartDecision()=>Starts++;
    internal void DecisionChanged(AICoreActionResult<BotLogicDecision,CoreActionResultParams>? previous,
        AICoreActionResult<BotLogicDecision,CoreActionResultParams> next)=>Changes++;
    internal AICoreActionResult<BotLogicDecision,CoreActionResultParams> GetDecision(EnemyInfo enemy)
        =>command(enemy)??new(BotLogicDecision.holdPosition,"rifleman");
    internal AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)
    {Ends++;return new("riflemanEnd",true);}
    internal bool TryPrepareCompanionAdvanceStop(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current,string reason,out AICoreActionEnd end)
    {Stops++;end=new(reason,StopAllowed);return StopAllowed;}
}

internal static class FollowerCombatPush
{
    internal static bool IsPushReason(string reason)=>reason?.StartsWith("push.")==true;
    internal static bool IsStartWeakEnemyPushReason(string reason)=>reason?.StartsWith("startWeakEnemy")==true;
}
