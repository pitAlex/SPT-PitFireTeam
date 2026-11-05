param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$method=[regex]::Match($source,'(?ms)^        internal bool TryCommitCompanionSupportPoint\(.*?^        \}').Value
if(!$method){throw 'Missing support transaction'}
$fixture=@'
using System;
public struct Vector3 { public float x; }
public class EnemyInfo { public string ProfileId; public bool Alive=true,Eligible=true,Known=true; }
public class Memory {
    public bool Block; public int Clears; private EnemyInfo goal;
    public EnemyInfo GoalEnemy {get=>goal;set {if(value==null)Clears++;if(!Block)goal=value;}}
}
public class Owner { public Memory Memory=new Memory(); }
public static class FollowerEnemyTracking {
    public static bool IsEligible(EnemyInfo e)=>e.Eligible;
    public static bool TryGetKnownPosition(EnemyInfo e,out Vector3 p,out float at){p=default;at=100;return e.Known;}
}
public static class FollowerCombatTargetCommitments {
    public static bool Mission,Match;
    public static bool HasMission(Owner o)=>Mission;
    public static bool IsMissionTarget(Owner o,EnemyInfo e)=>Match;
}
public static class FollowerGoalEnemyTracker {
    private class Scope:IDisposable {public void Dispose(){}}
    public static IDisposable Begin(string s,string r)=>new Scope();
}
public class Transaction {
    public Owner botOwner=new Owner(); public EnemyInfo Tracked; public int Published;
    private EnemyInfo GetTrackedEnemyByProfileId(string id)=>Tracked;
    private bool HasActiveCombatEnemy(EnemyInfo e)=>e!=null&&e.Alive;
    private static bool IsFinite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x);
    private void CommitContactAdvancePoint(Vector3 p)=>Published++;
    /* METHOD */
}
public static class Checks {
    public static int Run(){int count=0;foreach(string gate in new[]{"clear","dead","untracked","expired","unknown","mission","sameMission","invalid","setterVeto"}){
        var tx=new Transaction();var old=new EnemyInfo{ProfileId="old"};var next=new EnemyInfo{ProfileId="new"};tx.Tracked=next;tx.botOwner.Memory.GoalEnemy=old;
        FollowerCombatTargetCommitments.Mission=gate=="mission"||gate=="sameMission";FollowerCombatTargetCommitments.Match=gate=="sameMission";
        var point=new Vector3{x=1};if(gate=="dead")next.Alive=false;if(gate=="untracked")tx.Tracked=null;if(gate=="expired")next.Eligible=false;
        if(gate=="unknown")next.Known=false;if(gate=="invalid")point.x=float.NaN;if(gate=="setterVeto")tx.botOwner.Memory.Block=true;
        bool success=gate=="clear"||gate=="sameMission";
        if(tx.TryCommitCompanionSupportPoint(next,point)!=success||tx.Published!=(success?1:0)||!ReferenceEquals(tx.botOwner.Memory.GoalEnemy,success?next:old)||tx.botOwner.Memory.Clears!=0)
            throw new Exception("Support transaction violated "+gate);count++;
    }return count;}
}
'@
Add-Type -TypeDefinition $fixture.Replace('/* METHOD */',$method) -Language CSharp
Write-Output "PASS: $([Checks]::Run()) production support target/movement transaction checks; controlled setter admission."
