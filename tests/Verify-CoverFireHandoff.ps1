param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Source, [string]$Name) {
    $pattern = '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $found = [regex]::Matches($Source, $pattern)
    if ($found.Count -ne 1) { throw "Expected one $Name; found $($found.Count)" }
    return $found[0].Value
}
$common = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$tactic = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatDefault.cs')
$gate = Read-Method $common 'ShouldBreakRunToCoverForImmediateFire'
$methods = @('EndCoverMoveOrAttackMoving', 'TryPrepareCloseCoverFightHandoff', 'TryGetPreparedCloseCoverFightDecision', 'ShouldContinueCommittedRecoveryMove') | ForEach-Object { Read-Method $tactic $_ }
$constant = [regex]::Match($common, '(?m)^        private const float StableVisibleImmediateFireSeconds[^\r\n]+').Value
$harness = @"
#nullable enable
#pragma warning disable CS0649, CS8602
using System;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
public enum BotLogicDecision { runToCover, shootFromPlace, dogFight, holdPosition }
public class CoreActionResultParams {}
public struct AICoreActionResult<T,U> { public T Action; public string Reason; public AICoreActionResult(T a,string r){Action=a;Reason=r;} }
public struct AICoreActionEnd { public string Reason; public bool Value; public AICoreActionEnd(string r,bool v){Reason=r;Value=v;} }
public static class Time { public static float time=100; }
public class EnemyInfo { public bool IsVisible=true,CanShoot=true,Alive=true; public float PersonalSeenTime=99; public Enemy.EnemyDistance Band=Enemy.EnemyDistance.Close; }
public static class Enemy { public enum EnemyDistance { VeryClose,Close,Far } public static EnemyDistance Distance(EnemyInfo e)=>e.Band; }
public class ShootToPoint {}
public class Sensor { public bool Enough=true; public object Mask=new object(); public bool EnoughDistToShoot(out string why){why="";return Enough;} }
public class Transform { public object position=new object(); }
public class Memory { public EnemyInfo? GoalEnemy=new EnemyInfo(); }
public class BotOwner { public Memory Memory=new Memory(); public Sensor LookSensor=new Sensor(); public Transform WeaponRoot=new Transform(); public bool Hit; public ShootToPoint? Target=new ShootToPoint(); public ShootToPoint? CurrentEnemyTargetPosition(bool x)=>Target; }
namespace Utils { public static class Utils { public static bool Lane=true; public static bool CanShootToTarget(ShootToPoint p,object o,object m,bool x)=>Lane; } }
public class FollowerCombatCommon {
    $constant
    public BotOwner botOwner=new BotOwner(); public bool Committed=true,AtCover,IsCommittedCoverLockExpired=true,ClearedCover,ClearedMove;
    public AICoreActionEnd EndResult=new AICoreActionEnd("stableImmediateFire",true);
    public bool HasActiveCombatEnemy(EnemyInfo? e)=>e?.Alive==true;
    public bool HasActiveCombatEnemy()=>HasActiveCombatEnemy(botOwner.Memory.GoalEnemy);
    public bool HasCommittedCover()=>Committed;
    public bool IsBotInCommittedCover()=>AtCover;
    public static bool WasHitRecently(BotOwner b,float t)=>b.Hit;
    public void ClearCommittedCover(){ClearedCover=true;Committed=false;}
    public void ClearCommittedMovement(){ClearedMove=true;}
    public bool IsInFight(BotLogicDecision d)=>d==BotLogicDecision.shootFromPlace||d==BotLogicDecision.dogFight;
    public static bool IsMedicalRetreatMovementReason(string r)=>r=="runToHeal";
    public static bool IsRecoveryManeuverReason(string r)=>r.StartsWith("recovery.");
    public static AICoreActionEnd Continue()=>new AICoreActionEnd("",false);
    public AICoreActionEnd ShallEndCurrentDecision(Decision d)=>EndResult;
    $gate
}
public class Tactic {
    public FollowerCombatCommon combatCommon=new FollowerCombatCommon();
    private BotOwner botOwner=>combatCommon.botOwner;
    private Decision? preparedCloseCoverFightDecision;
    public bool CanPrepare=true,IntentCleared; public BotLogicDecision Successor=BotLogicDecision.shootFromPlace;
    private bool IsBossSupportDecision(Decision d)=>false;
    private bool ShouldBreakForBossUnderAttack(EnemyInfo e)=>false;
    private void ClearCoverIntent(){IntentCleared=true;}
    private bool TryGetImmediateFightDecision(out Decision d){d=new Decision(Successor,"preparedFire");return CanPrepare;}
    public AICoreActionEnd End(string reason="shootCover")=>EndCoverMoveOrAttackMoving(new Decision(BotLogicDecision.runToCover,reason));
    public bool Consume(out Decision d)=>TryGetPreparedCloseCoverFightDecision(out d);
    $($methods -join "`n")
}
public static class CoverHandoffChecks {
    private static int count; private static void Check(bool ok,string name){count++;if(!ok)throw new Exception(name);}
    public static int Run(){
        var c=new FollowerCombatCommon();var e=c.botOwner.Memory.GoalEnemy!;
        e.CanShoot=false;Check(!c.ShouldBreakRunToCoverForImmediateFire(),"Factory_VisibleThreatWithoutShotKeepsCoverRoute");
        c.botOwner.Hit=true;Check(!c.ShouldBreakRunToCoverForImmediateFire(),"HitWithoutShotDoesNotFakeImmediateFire");
        e.CanShoot=true;e.IsVisible=false;Check(!c.ShouldBreakRunToCoverForImmediateFire(),"UnseenThreatDoesNotFakeImmediateFire");
        e.IsVisible=true;e.Alive=false;Check(!c.ShouldBreakRunToCoverForImmediateFire(),"DeadTargetCannotBreakCoverForFire");
        e.Alive=true;c.botOwner.Hit=false;c.IsCommittedCoverLockExpired=false;
        Check(!c.ShouldBreakRunToCoverForImmediateFire(),"UnexpiredCoverLockRemainsSticky");
        c.botOwner.Hit=true;Utils.Utils.Lane=false;
        Check(!c.ShouldBreakRunToCoverForImmediateFire(),"HitWithBlockedGeometryKeepsCoverRoute");
        Utils.Utils.Lane=true;Check(c.ShouldBreakRunToCoverForImmediateFire(),"HitCanBreakLockWhenActualShotExists");
        c.botOwner.Hit=false;c.IsCommittedCoverLockExpired=true;c.botOwner.LookSensor.Enough=false;
        Check(!c.ShouldBreakRunToCoverForImmediateFire(),"InsufficientShootDistanceKeepsRoute");
        c.botOwner.LookSensor.Enough=true;c.botOwner.Target=null;
        Check(!c.ShouldBreakRunToCoverForImmediateFire(),"MissingAimTargetKeepsRoute");
        c.botOwner.Target=new ShootToPoint();e.Band=Enemy.EnemyDistance.Far;e.PersonalSeenTime=99.9f;
        Check(!c.ShouldBreakRunToCoverForImmediateFire(),"FarContactMustStabilize");
        e.PersonalSeenTime=99;Check(c.ShouldBreakRunToCoverForImmediateFire(),"StableVisibleClearShotCanBreak");
        var t=new Tactic();t.combatCommon.botOwner.Memory.GoalEnemy!.CanShoot=false;
        for(int i=0;i<7;i++)Check(!t.End().Value,"Factory_SevenRepeatedBreakRequestsCannotRestartCover");
        Check(!t.combatCommon.ClearedCover&&!t.combatCommon.ClearedMove&&!t.Consume(out _),"RejectedBreakPreservesCommitmentAndHasNoSuccessor");
        t=new Tactic{CanPrepare=false};Check(!t.End().Value&&!t.combatCommon.ClearedCover,"NoPreparedFireKeepsCover");
        t=new Tactic{Successor=BotLogicDecision.holdPosition};Check(!t.End().Value,"NonFiringSuccessorCannotClaimImmediateFire");
        t=new Tactic();Check(t.End().Value&&t.combatCommon.ClearedCover&&t.combatCommon.ClearedMove&&t.IntentCleared,"ValidFireReleasesMovementOnlyAfterPreparation");
        Check(t.Consume(out var d)&&d.Action==BotLogicDecision.shootFromPlace,"PreparedShotIsReturnedToNextDecision");
        Check(!t.Consume(out _),"PreparedShotConsumedOnce");
        t=new Tactic{Successor=BotLogicDecision.dogFight};t.combatCommon.EndResult=new AICoreActionEnd("visibleCloseFireBreakCoverMove",true);
        Check(t.End().Value&&t.Consume(out d)&&d.Action==BotLogicDecision.dogFight,"ExistingCloseDogfightHandoffPreserved");
        t=new Tactic();t.End();t.combatCommon.botOwner.Memory.GoalEnemy!.Alive=false;
        Check(!t.Consume(out _),"DeadTargetInvalidatesPreparedFire");
        t=new Tactic{CanPrepare=false};t.combatCommon.EndResult=new AICoreActionEnd("arrivedCommittedCover",true);
        Check(t.End().Value&&!t.combatCommon.ClearedCover,"NormalArrivalStillEndsWithoutFireSuccessor");
        t=new Tactic{CanPrepare=false};t.combatCommon.EndResult=new AICoreActionEnd("coverSpotted",true);
        Check(t.End().Value,"NonFireSafetyExitRemainsAvailable");
        return count;
    }
}
"@
Add-Type -TypeDefinition $harness -Language CSharp
$count = [CoverHandoffChecks]::Run()
Write-Output "Passed $count production cover-fire eligibility and prepared-handoff checks. Geometry and tactical integration require an in-raid test."
