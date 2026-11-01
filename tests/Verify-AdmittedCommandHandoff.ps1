param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$method=[regex]::Matches($source,'(?ms)^        internal bool TryPrepareAdmittedTargetTransition\(.*?^        \}')
if($method.Count -ne 1){throw 'Expected one admitted-target handoff helper'}
$fixture=@'
using System;
using System.Collections.Generic;
public enum BotLogicDecision { holdPosition, goToPoint }
public class CoreActionResultParams {}
public struct AICoreActionResult<T,P> {}
public class EnemyInfo {public object Person=new object(); public bool Alive=true,Eligible=true,Known=true;}
public class Memory {public EnemyInfo GoalEnemy;}
public class Enemies {public Dictionary<object,EnemyInfo> EnemyInfos=new Dictionary<object,EnemyInfo>();}
public class BotOwner {public Memory Memory=new Memory();public Enemies EnemiesController=new Enemies();}
public static class FollowerEnemyTracking {
    public static bool IsEligible(EnemyInfo enemy)=>enemy.Eligible;
    public static bool TryGetKnownPosition(EnemyInfo enemy,out int point,out int source){point=source=0;return enemy.Known;}
}
public sealed class Scope:IDisposable {readonly Action finish;public Scope(Action finish){this.finish=finish;}public void Dispose()=>finish();}
public static class FollowerCombatTargetCommitments {
    public static int Depth;
    public static Scope BeginAuthoritativeGoalSet(){Depth++;return new Scope(()=>Depth--);}
}
public static class FollowerGoalEnemyTracker {public static Scope Begin(string source,string reason)=>new Scope(()=>{});}
public class Common {
    public BotOwner botOwner=new BotOwner();public bool Allow=true;public int Prepared;
    bool HasActiveCombatEnemy(EnemyInfo enemy)=>enemy?.Alive==true;
    bool TryPrepareDecisionTransition(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current,string reason,AICoreActionResult<BotLogicDecision,CoreActionResultParams> next){
        if(FollowerCombatTargetCommitments.Depth!=1)throw new Exception("Missing explicit goal authority");
        if(Allow)Prepared++;return Allow;
    }
    /* PRODUCTION */
}
public static class HandoffChecks {
    public static int Run(){
        int checks=0;Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        foreach(string state in new[]{"valid","dead","unadmitted","unknown","untracked","differentTrackedContact","prepareFailed"}){
            var common=new Common();var old=new EnemyInfo();var target=new EnemyInfo();common.botOwner.Memory.GoalEnemy=old;
            common.botOwner.EnemiesController.EnemyInfos[target.Person]=target;
            if(state=="dead")target.Alive=false;
            if(state=="unadmitted")target.Eligible=false;
            if(state=="unknown")target.Known=false;
            if(state=="untracked")common.botOwner.EnemiesController.EnemyInfos.Clear();
            if(state=="differentTrackedContact")common.botOwner.EnemiesController.EnemyInfos[target.Person]=new EnemyInfo();
            if(state=="prepareFailed")common.Allow=false;
            bool accepted=common.TryPrepareAdmittedTargetTransition(default,"knightReassignPush",target,default);
            check(accepted==(state=="valid"),"admission boundary: "+state);
            check(ReferenceEquals(common.botOwner.Memory.GoalEnemy,accepted?target:old),"handoff or rollback owns goal: "+state);
            check(common.Prepared==(accepted?1:0),"only accepted target has a prepared successor: "+state);
            check(old.Alive && old.Known && FollowerCombatTargetCommitments.Depth==0,"old contact and authority cleanup preserved: "+state);
        }
        return checks;
    }
}
'@
Add-Type -TypeDefinition $fixture.Replace('/* PRODUCTION */',$method[0].Value) -Language CSharp
$count=[HandoffChecks]::Run()
"PASS: $count production admitted-target handoff, rollback and memory-preservation checks; controlled EFT inputs."
