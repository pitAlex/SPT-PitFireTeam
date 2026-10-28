param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSniper.cs')
$method=[regex]::Matches($source,'(?ms)^        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision\(.*?^        \}')
if($method.Count -ne 1){throw 'Expected one production Marksman decision router'}
$helperNames=@('TryContinueFiringPositionArrival','TryGetCloseQuarterDecision','TryGetRecoverDecision',
    'TryGetActiveCommittedTravelDecision','TryGetPushSupportDecision','TryGetSniperSupportDecision',
    'TryGetBossUnderAttackDecision','TryGetVisibleDecision','TryGetIndirectThreatPressureDecision',
    'TryGetCommittedCoverDecision','TryGetRepositionDecision')
$helpers=foreach($name in $helperNames){"bool $name(EnemyInfo enemy,out Decision d)=>Emit(`"$name`",out d);"}
$fixture=@'
#nullable enable
using System;
using Decision=AICoreActionResult<BotLogicDecision,CoreActionResultParams>;
public enum BotLogicDecision {holdPosition,goToPoint}
public class CoreActionResultParams {}
public struct AICoreActionResult<T,P> {public string Reason;public AICoreActionResult(string reason){Reason=reason;}}
public class EnemyInfo {}
public class Memory {public bool IsUnderFire;}
public class Bot {public Memory Memory=new Memory();}
public class Phase {public bool IsActive;}
public class Common {
    public string Branch="";
    bool Emit(string name,out Decision decision){decision=new Decision(name);return name==Branch;}
    public bool TryGetCommittedGrenadeDecision(out Decision d)=>Emit("committedGrenade",out d);
    public bool HasInitialDecision=>Branch=="initial";
    public Decision ConsumeInitialDecision()=>new Decision("initial");
    public bool HasActiveCombatEnemy(EnemyInfo enemy)=>Branch!="noEnemy";
    public void RefreshShootCover(){}public void ValidateCommittedCover(){}public void ClearInitialDecision(){}
    public bool TryGetReloadRetreatDecision(EnemyInfo e,out Decision d)=>Emit("reload",out d);
    public bool HasCommittedPosition(out Decision d,bool deferCombatBreaks)=>Emit("position",out d);
    public bool TryGetCommittedMovementDecision(EnemyInfo e,bool regroup,bool push,out Decision d)=>Emit("movement",out d);
    public bool TryActivateFollowerGrenade(EnemyInfo e,out Decision d)=>Emit("grenade",out d);
}
public class Router {
    public Common CombatCommon=new Common();public Bot BotOwner=new Bot();public Phase repositionPhase=new Phase();
    public Func<EnemyInfo,Decision?>? companionPosition;public int Calls;
    bool Emit(string name,out Decision d){d=new Decision(name);return CombatCommon.Branch==name;}
    void ClearAggressiveRequests(){}bool HasExplicitRegroupOrder()=>CombatCommon.Branch=="Regroup";
    void ClearCommittedCoverAndRepositionState(){}void ClearRegroupCommand(){}
    Decision Regroup(EnemyInfo enemy,bool isExplicitOrder=false)=>new Decision("Regroup");
    Decision? TryGetMarksmanPreFightDecision(EnemyInfo enemy)=>CombatCommon.Branch=="medical"?new Decision("medical"):(Decision?)null;
    bool ShouldDeferCloseAutoToNearbyRifleman(EnemyInfo enemy)=>false;
    bool ShouldBreakForBossUnderAttack(EnemyInfo enemy)=>false;
    public bool RegroupNeeded;bool ShouldRegroupForBossDistance()=>RegroupNeeded;
    /* HELPERS */
    /* PRODUCTION */
}
public static class RoutingChecks {
    public static int Run(){
        int checks=0;Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        foreach(string branch in new[]{"Regroup","medical","committedGrenade","initial","noEnemy","reload",
            "TryContinueFiringPositionArrival","TryGetCloseQuarterDecision","TryGetRecoverDecision","position","movement",
            "grenade","TryGetActiveCommittedTravelDecision","TryGetPushSupportDecision","TryGetSniperSupportDecision","TryGetBossUnderAttackDecision"}){
            var router=new Router();router.CombatCommon.Branch=branch;
            router.companionPosition=enemy=>{router.Calls++;return new Decision("birdEye");};
            var chosen=router.GetDecision(new EnemyInfo());
            check(router.Calls==0 && chosen.Reason!="birdEye","companion cannot steal shared priority: "+branch);
        }
        var active=new Router();active.CombatCommon.Branch="TryGetVisibleDecision";
        active.companionPosition=enemy=>{active.Calls++;return new Decision("birdEye");};
        check(active.GetDecision(new EnemyInfo()).Reason=="birdEye" && active.Calls==1,"one companion planner runs at fresh positioning boundary");
        var separated=new Router{RegroupNeeded=true};
        separated.companionPosition=enemy=>{separated.Calls++;return new Decision("birdEye");};
        check(separated.GetDecision(new EnemyInfo()).Reason=="Regroup" && separated.Calls==0,"companion positioning cannot renew stale sniper movement beyond regroup distance");
        separated.CombatCommon.Branch="TryGetVisibleDecision";
        check(separated.GetDecision(new EnemyInfo()).Reason=="TryGetVisibleDecision" && separated.Calls==0,"distance guard retains ordinary useful-shot routing");
        foreach(string branch in new[]{"position","movement","TryGetActiveCommittedTravelDecision","TryGetSniperSupportDecision","TryGetBossUnderAttackDecision"}){
            separated.CombatCommon.Branch=branch;
            check(separated.GetDecision(new EnemyInfo()).Reason==branch && separated.Calls==0,"separation does not interrupt protected work: "+branch);
        }
        active.companionPosition=enemy=>null;
        check(active.GetDecision(new EnemyInfo()).Reason=="TryGetVisibleDecision","declined companion point retains ordinary Marksman fallback");
        active.companionPosition=null;
        check(active.GetDecision(new EnemyInfo()).Reason=="TryGetVisibleDecision","ordinary Marksman constructor behavior remains unchanged");
        return checks;
    }
}
'@
$fixture=$fixture.Replace('/* HELPERS */',($helpers -join "`n")).Replace('/* PRODUCTION */',$method[0].Value)
Add-Type -TypeDefinition $fixture -Language CSharp
$count=[RoutingChecks]::Run()
"PASS: $count production Marksman/companion routing priority and fallback checks; controlled selector inputs."
