param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

# Dependency-free boundary tests of production dispatch, arrival state, and candidate filtering.
# Controlled EFT/Unity stand-ins do not reproduce physics, path generation, or weapon animations.
$ErrorActionPreference = 'Stop'
$sniperSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSniper.cs')
$commonSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$arrivalSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FiringPositionArrivalState.cs')
function Get-ArrivalMethod([string]$Source, [string]$Name) {
    $pattern = '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $matches = [regex]::Matches($Source, $pattern)
    if ($matches.Count -ne 1) { throw "Expected one production method: $Name; got $($matches.Count)" }
    return $matches[0].Value
}
$sniperMethods = @(
    'ShallEndCurrentDecision', 'DecisionChanged', 'TryPrepareBreakDecision',
    'IsFiringPositionArrivalDecision', 'IsFiringPositionArrivalTravel', 'IsMarksmanArrivalEnd',
    'IsMarksmanPositionMoveReason', 'IsMarksmanSupportPositionReason', 'IsMarksmanCommittedTravelReason',
    'IsAutomaticSupportIntentReason', 'IsCloseIntentDecisionReason',
    'TryGetFiringPositionArrivalShot', 'TryPreparePendingMedicalBreak', 'BeginFiringPositionArrival',
    'TryPrepareFiringPositionArrivalShot', 'GetFiringPositionArrivalDecision', 'TryContinueFiringPositionArrival',
    'PrepareFiringPositionArrivalWait', 'EndFiringPositionArrival', 'ClearFiringPositionArrivalCommitments'
) | ForEach-Object { Get-ArrivalMethod $sniperSource $_ }
$commonMethods = @(
    'TryCreateLocalFiringPositionAdjustment', 'TryFindSupportFiringPosition',
    'IsWithinLocalFiringPositionRadius', 'EndLocalFiringPositionAdjustment'
) | ForEach-Object { Get-ArrivalMethod $commonSource $_ }
$harness = @'
#nullable enable
#pragma warning disable CS0649, CS0414
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Decision = AICoreActionResult<BotLogicDecision, CoreActionResultParams>;
namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Random { public static int Calls; public static float Duration=2f; public static float Range(float a,float b){Calls++;return Duration;} }
    public static class Mathf {
        public const float PI=(float)Math.PI;
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Abs(float a)=>Math.Abs(a);
        public static float Sin(float a)=>(float)Math.Sin(a);
        public static float Cos(float a)=>(float)Math.Cos(a);
        public static float Clamp(float a,float b,float c)=>Math.Max(b,Math.Min(a,c));
    }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 positiveInfinity=>new Vector3(float.PositiveInfinity,0,0);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public void Normalize(){float m=(float)Math.Sqrt(sqrMagnitude);x/=m;y/=m;z/=m;}
        public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
}
namespace UnityEngine.AI {
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh {
        public const int AllAreas=-1; public static bool Available=true; public static int Samples; public static Vector3? PinnedPoint;
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float distance,int mask){Samples++;hit=new NavMeshHit{position=PinnedPoint??p};return Available;}
    }
}
public enum BotLogicDecision { holdPosition, goToPoint, goToPointTactical, runToCover, attackMoving, attackMovingWithSuppress, suppressFire, shootFromCover, shootFromPlace, dogFight, heal }
public class CoreActionResultParams {}
public struct AICoreActionResult<T,P> { public T Action; public string Reason; public AICoreActionResult(T a,string r){Action=a;Reason=r;} }
public struct AICoreActionEnd { public string Reason; public bool Value; public AICoreActionEnd(string r,bool v){Reason=r;Value=v;} }
public class EnemyInfo { public string ProfileId="woods-enemy"; public bool Valid=true; public Vector3 Anchor=new Vector3(100,0,0); }
public class PointData { public Vector3 Point; public bool Target=true,Come=true; public bool HaveTarget()=>Target; public void SetPoint(Vector3 p){Point=p;Target=true;} public bool IsCome()=>Come; }
public class BotMemory { public EnemyInfo GoalEnemy=new EnemyInfo(); public bool IsUnderFire; }
public class Aid { public bool Have2Do,HaveWork,Using; }
public class Medicine { public Aid FirstAid=new Aid(),SurgicalKit=new Aid(); }
public class Look { public int Mask; }
public class BotOwner { public Vector3 Position; public BotMemory Memory=new BotMemory(); public Medicine Medecine=new Medicine(); public PointData GoToSomePointData=new PointData(); public Look LookSensor=new Look(); }
public class ShootToPoint { public ShootToPoint(Vector3 p,float f){} }
public class BotsGroup { public enum BotCurrentTactic { Attack } }
public class CombatDistanceConfiguration { public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration(); public float GetCloseQuarterDistance()=>25f; }
public static class BattleRecorder { public static List<string> Events=new List<string>(); public static void RecordObjectiveDiagnostic(BotOwner b,string o,string a,string r){Events.Add(a+":"+r);} }
public static class FollowerCombatSuppressionObjective { public static bool IsAutomaticSupportIntentReason(string? r)=>false; }
public static class FollowerCombatCommon {
    public const string HealRetryHoldReason="healRetry";
    public static bool IsMedicalDecision(Decision d)=>d.Action==BotLogicDecision.heal;
    public static bool IsMovementDecision(Decision d)=>d.Action==BotLogicDecision.goToPoint||d.Action==BotLogicDecision.goToPointTactical||d.Action==BotLogicDecision.runToCover||d.Action==BotLogicDecision.attackMoving;
    public static bool IsRecoveryManeuverReason(string r)=>r.StartsWith("recovery.");
    public static bool IsRecoveryNoCoverReason(string r)=>r.StartsWith("recovery.");
    public static bool WasHitRecently(BotOwner b,float seconds)=>false;
    public static AICoreActionEnd Continue()=>default;
}
namespace Utils { public static class Utils {
    public static bool Complete=true,Lane=true; public static float NavDistance=10f;
    public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float distance){distance=NavDistance;return Complete;}
    public static bool CanShootToTarget(ShootToPoint s,Vector3 p,int mask,bool a)=>Lane;
} }
public class PhaseStub { public void Clear(){} public void BeginTravel(){} }
public class ArrivalCommon {
    public BotOwner botOwner; public Decision? Next; public bool Gesture,Shot,Recovery,Stalled,Blocked,WrongPolicy,AlternateThreat,Separated;
    public int CoverClears,PositionClears,GenericEnds; public float CommittedUntil,HoldUntil; public Decision? Medical;
    public AICoreActionEnd TravelEnd=new AICoreActionEnd("arrivedAtPoint",true);
    public ArrivalCommon(BotOwner b){botOwner=b;}
    private const float SupportPointSameLevelTolerance=2f,BossFireLaneSoftPenalty=5f,CommittedCoverArrivalHoldDistance=2f;
    private string? lastSupportFiringPositionRejectReason;
    public string? LastSupportFiringPositionRejectReason=>lastSupportFiringPositionRejectReason;
    public bool HasActiveCombatEnemy(EnemyInfo? e=null)=>(e??botOwner.Memory.GoalEnemy)?.Valid==true;
    private static bool IsFinite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool IsFinite(Vector3 v)=>IsFinite(v.x)&&IsFinite(v.y)&&IsFinite(v.z);
    private static Vector3 GetEnemyAnchor(EnemyInfo e)=>e.Anchor;
    private Vector3 GetBossPosition()=>new Vector3(-70,0,0);
    private void SetCoverTactic(BotsGroup.BotCurrentTactic t){}
    private void AddBattlefieldFiringCandidates(List<Vector3> c,Vector3 a,Vector3 b,Vector3 e,Vector3 d,Vector3 s){}
    private bool IsBlockedTacticalPoint(Vector3 c)=>Blocked;
    private bool IsMarksmanFiringPositionAllowed(EnemyInfo e,Vector3 c)=>!WrongPolicy;
    private bool IsMarksmanSupportSeparatedFromBoss(Vector3 c)=>Separated;
    private bool IsSupportPositionBehindBossLine(Vector3 c,Vector3 b,Vector3 e)=>true;
    private bool IsSupportPositionSafeFromAlternateThreats(Vector3 c,string id,bool strict)=>!AlternateThreat;
    private bool IsBossFireLaneMovementRisk(Vector3 c,Vector3 e,bool includePath)=>false;
    private AICoreActionEnd EndTacticalPointIfStalled()=>new AICoreActionEnd("stalled",Stalled);
    public bool CanShootFromCurrentCoverOrStandingIntent(out bool standing){standing=false;return false;}
    public Decision? TryGetImmediateShootDecision(string reason)=>Shot?new Decision(BotLogicDecision.shootFromPlace,reason):(Decision?)null;
    public bool TryPrepareDecisionTransition(Decision source,string end,Decision next){Next=next;return HasActiveCombatEnemy();}
    public bool HasActiveCombatGestureOrder()=>Gesture;
    public Decision? TryGetNeedHealDecision()=>Medical;
    public void ClearCommittedCover(string? reason=null){CoverClears++;}
    public void ClearCommittedPosition(string? reason=null){PositionClears++;}
    public void ClearCommittedMovement(string? reason=null){}
    public void SetCommittedPosition(Vector3 p,Decision d,float duration){CommittedUntil=Time.time+duration;}
    public void HoldFor(float duration){HoldUntil=Time.time+duration;}
    public AICoreActionEnd EndGoToPoint(bool endWhenEnemyVisibleShootable)=>TravelEnd;
    public AICoreActionEnd EndRecoveryNoCoverSuppress(string r)=>default;
    public AICoreActionEnd ShallEndCurrentDecision(Decision d){GenericEnds++;return default;}
    public void HandleSharedDecisionChanged(Decision d){}
    public void HandleCommittedCoverDecisionChanged(Decision d){}
    public void HandleFollowerSuppressDecisionChanged(Decision d){}
    public void UpdateRecoveryNoCoverCommitment(Decision d){}
    public bool TryRenewCommittedPositionHold(Decision d,float duration)=>false;
    public bool ShouldCommitMovementDecision(Decision d,bool b)=>FollowerCombatCommon.IsMovementDecision(d);
    public void CommitMovement(Decision d){}
    public bool IsSameCommittedMovement(Decision d)=>false;
__COMMON_METHODS__
}
public class ArrivalSniper {
    private readonly pitTeam.BigBrain.FiringPositionArrivalState firingPositionArrival=new pitTeam.BigBrain.FiringPositionArrivalState();
    private readonly PhaseStub repositionPhase=new PhaseStub(),supportPhase=new PhaseStub();
    private const string CloseWeaponPrepareHoldReason="sniper.closeWeaponPrepare";
    private const float FiringPositionCooldownSeconds=4f,RepositionHoldTimeoutSeconds=10f;
    private float closeSearchRetryUntil,nextFiringPositionAllowedTime;
    private Decision? currentEndSourceDecision;
    public BotOwner BotOwner=new BotOwner(); public ArrivalCommon CombatCommon;
    public bool ExplicitRegroup,BossEmergency,Far=true; public int GenericHolds,RecoveryMoves,Regroups;
    public ArrivalSniper(){CombatCommon=new ArrivalCommon(BotOwner);}
    public bool ContinueArrival(out Decision d)=>TryContinueFiringPositionArrival(BotOwner.Memory.GoalEnemy,out d);
    public AICoreActionEnd End(Decision d)=>ShallEndCurrentDecision(d);
    public float Deadline=>firingPositionArrival.WaitUntil;
    public Vector3 Origin=>firingPositionArrival.Origin;
    private bool HasExplicitRegroupOrder()=>ExplicitRegroup;
    private void ClearCommittedCoverAndRepositionState(){ClearFiringPositionArrivalCommitments("clear");}
    private bool ShouldBreakMarksmanPositionMoveForVisibleThreat()=>false;
    private AICoreActionEnd EndMarksmanCommittedRunToCover(string r)=>new AICoreActionEnd("arrivedCommittedCover",true);
    private AICoreActionEnd EndMarksmanCommittedAttackMoving(string r)=>default;
    private AICoreActionEnd EndMarksmanPositionMove(string r)=>default;
    private AICoreActionEnd EndMarksmanRecoveryMovement(Decision d){RecoveryMoves++;return default;}
    private AICoreActionEnd EndHoldPosition(Decision d){GenericHolds++;return default;}
    private bool ShouldBreakForBossUnderAttack(EnemyInfo e)=>BossEmergency;
    private bool TryGetBossUnderAttackDecision(EnemyInfo e,out Decision d){d=new Decision(BotLogicDecision.goToPoint,"supportBoss");return BossEmergency;}
    private bool TryPreparePressureRecoveryBreak(EnemyInfo e,string reason,out AICoreActionEnd end){end=new AICoreActionEnd(reason,true);if(!CombatCommon.Recovery)return false;TryPrepareBreakDecision(new Decision(BotLogicDecision.runToCover,"recovery.cover"),false,false);return true;}
    private bool TryGetRecoverDecision(EnemyInfo e,out Decision d){d=new Decision(BotLogicDecision.runToCover,"recovery.cover");return CombatCommon.Recovery;}
    private bool ShouldRegroupForBossDistance()=>Far;
    private Decision Regroup(EnemyInfo e){Regroups++;return new Decision(BotLogicDecision.goToPoint,"regroup.run");}
    private void ClearCloseWeaponPreparation(){}
    private void ApplyMarksmanWeaponPolicy(EnemyInfo? e,Decision d){}
    private void UpdateMarksmanCommittedHolderPhase(Decision d){}
    private bool IsSniperCoverHoldReason(string r)=>false;
__SNIPER_METHODS__
}
public static class MarksmanArrivalChecks {
    private static int checks;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
    private static ArrivalSniper Fresh(){Time.time=100;UnityEngine.Random.Calls=0;UnityEngine.Random.Duration=2;NavMesh.Samples=0;NavMesh.Available=true;NavMesh.PinnedPoint=null;Utils.Utils.Complete=true;Utils.Utils.Lane=true;Utils.Utils.NavDistance=10;return new ArrivalSniper();}
    private static Decision Travel(bool auto=false)=>new Decision(auto?BotLogicDecision.goToPointTactical:BotLogicDecision.runToCover,auto?"sniper.closeSearch":"sniper.reposition.run");
    private static Decision SelectPrepared(ArrivalSniper h){var d=h.CombatCommon.Next!.Value;h.DecisionChanged(null,d);return d;}
    private static Decision ReachAdjustment(ArrivalSniper h,Decision adjust){h.BotOwner.Position=h.BotOwner.GoToSomePointData.Point;h.End(adjust);return SelectPrepared(h);}
    public static int Run(){
        var h=Fresh();Check(h.End(Travel()).Value,"RepositionArrival_PreparesSuccessor");var adjust=SelectPrepared(h);
        Check(adjust.Action==BotLogicDecision.goToPoint&&adjust.Reason=="sniper.position.arrival.adjust","RepositionArrival_OneWalkingCorrection");
        Check(NavMesh.Samples==32&&UnityEngine.Random.Calls==0,"Arrival_OneLocalSearchBeforeWait");
        Check(h.ContinueArrival(out var keep)&&keep.Reason==adjust.Reason&&h.Regroups==0,"Adjustment_FarFromBoss_RetainsIntent");
        Check(!h.End(adjust).Value,"Adjustment_StaleIsCome_DoesNotArriveAtOriginalPoint");
        var wait=ReachAdjustment(h,adjust);
        Check(wait.Reason=="sniper.position.arrival.wait"&&NavMesh.Samples==32,"AdjustmentArrival_DoesNotSearchAgain");
        Check(h.Origin.sqrMagnitude==0&&UnityEngine.Random.Calls==1,"AdjustmentArrival_KeepsOriginAndSamplesWaitOnce");
        float deadline=h.Deadline;Time.time=deadline-.01f;
        Check(!h.End(wait).Value&&h.ContinueArrival(out keep)&&keep.Reason==wait.Reason&&h.Regroups==0,"Wait_BeforeDeadline_NoDistanceRegroup");
        h.DecisionChanged(wait,wait);Check(h.Deadline==deadline&&UnityEngine.Random.Calls==1,"Wait_DecisionChanged_DoesNotRenew");
        Time.time=deadline;Check(h.End(wait).Value&&h.GenericHolds==0,"Wait_Expiry_UsesOwnedEndNotGenericHold");
        Check(h.ContinueArrival(out keep)&&keep.Reason=="regroup.run"&&h.Regroups==1,"Wait_Expiry_AllowsDistanceRegroup");
        Check(!h.ContinueArrival(out keep),"ReleasedArrival_IsConsumedOnlyOnce");

        h=Fresh();h.End(Travel());var retainedPoint=h.BotOwner.GoToSomePointData.Point;
        h.BotOwner.GoToSomePointData.SetPoint(new Vector3(500,0,0));adjust=SelectPrepared(h);
        Check((h.BotOwner.GoToSomePointData.Point-retainedPoint).sqrMagnitude==0&&NavMesh.Samples==32,"PreparedAdjustment_RestoresExactDestinationAtHandoff");
        h=Fresh();NavMesh.Available=false;h.End(Travel(true));wait=SelectPrepared(h);Time.time=h.Deadline;h.End(wait);h.CombatCommon.Shot=true;
        Check(h.ContinueArrival(out keep)&&keep.Reason=="sniper.closeSearch.arrival.shot"&&h.Regroups==0,"ReleasedArrival_NewShotPreemptsDistanceRegroup");
        h=Fresh();NavMesh.Available=false;h.End(Travel());wait=SelectPrepared(h);Time.time=h.Deadline;h.End(wait);h.CombatCommon.Recovery=true;
        Check(h.ContinueArrival(out keep)&&keep.Reason=="recovery.cover"&&h.Regroups==0,"ReleasedArrival_RecoveryPreemptsDistanceRegroup");
        h=Fresh();h.End(Travel());adjust=SelectPrepared(h);h.BotOwner.Memory.GoalEnemy.ProfileId="new-target";
        Check(!h.ContinueArrival(out keep),"Selection_EnemyChanged_DropsOldArrival");

        h=Fresh();NavMesh.Available=false;h.End(Travel(true));wait=SelectPrepared(h);
        Check(wait.Reason=="sniper.closeSearch.arrival.wait"&&UnityEngine.Random.Calls==1,"NoCandidate_AutomaticIntentWaits");
        h.CombatCommon.Shot=true;
        Check(h.End(wait).Value&&h.CombatCommon.Next?.Reason=="sniper.closeSearch.arrival.shot","Wait_ImmediateShot_PreservesAutomaticIntent");
        Check(UnityEngine.Random.Calls==1&&NavMesh.Samples==32,"ShotInterrupt_NoNewSearchOrTimer");

        h=Fresh();h.CombatCommon.Shot=true;h.End(Travel(true));
        Check(h.CombatCommon.Next?.Reason=="sniper.closeSearch.arrival.shot"&&NavMesh.Samples==0&&UnityEngine.Random.Calls==0,"InitialArrival_ActualShotSkipsSearchAndWait");
        h=Fresh();h.End(Travel(true));adjust=SelectPrepared(h);h.CombatCommon.Shot=true;h.End(adjust);
        Check(h.CombatCommon.Next?.Reason=="sniper.closeSearch.arrival.shot"&&UnityEngine.Random.Calls==0,"Adjustment_ActualShotInterruptsBeforeWait");

        foreach(float duration in new[]{1.5f,2.5f}){
            h=Fresh();NavMesh.Available=false;UnityEngine.Random.Duration=duration;h.End(Travel());wait=SelectPrepared(h);
            Check(h.Deadline==100+duration&&h.CombatCommon.CommittedUntil==h.Deadline&&h.CombatCommon.HoldUntil==h.Deadline,"Wait_RandomBoundary_"+duration);
            Time.time=h.Deadline;h.End(wait);h.Far=false;
            Check(!h.ContinueArrival(out keep)&&h.Regroups==0,"Wait_InBounds_ReturnsToOrdinaryRouting_"+duration);
        }
        foreach(bool automatic in new[]{false,true}){
            h=Fresh();h.End(Travel(automatic));adjust=SelectPrepared(h);h.CombatCommon.Stalled=true;h.End(adjust);wait=SelectPrepared(h);
            Check(wait.Action==BotLogicDecision.holdPosition&&NavMesh.Samples==32,"Adjustment_Stall_OneWait_"+automatic);
            h=Fresh();h.End(Travel(automatic));adjust=SelectPrepared(h);h.BotOwner.GoToSomePointData.SetPoint(new Vector3(500,0,0));h.End(adjust);wait=SelectPrepared(h);
            Check(wait.Action==BotLogicDecision.holdPosition&&NavMesh.Samples==32,"Adjustment_TargetLost_OneWait_"+automatic);
        }
        foreach(string interrupt in new[]{"command","enemyChanged","enemyDead","medical","pressure","boss"}){
            foreach(bool duringWait in new[]{false,true}){
                h=Fresh();h.End(Travel(true));var current=SelectPrepared(h);if(duringWait)current=ReachAdjustment(h,current);
                h.CombatCommon.Next=null;int clears=h.CombatCommon.CoverClears;
                if(interrupt=="command")h.CombatCommon.Gesture=true;
                if(interrupt=="enemyChanged")h.BotOwner.Memory.GoalEnemy.ProfileId="changed";
                if(interrupt=="enemyDead")h.BotOwner.Memory.GoalEnemy.Valid=false;
                if(interrupt=="medical"){h.BotOwner.Medecine.FirstAid.Have2Do=true;h.CombatCommon.Medical=new Decision(BotLogicDecision.heal,"healInCover");h.CombatCommon.Shot=true;}
                if(interrupt=="pressure"){h.BotOwner.Memory.IsUnderFire=true;h.CombatCommon.Recovery=true;}
                if(interrupt=="boss")h.BossEmergency=true;
                Check(h.End(current).Value,"Interrupt_"+interrupt+"_Waiting="+duringWait);
                Check(!h.ContinueArrival(out keep),"Interrupt_ReleasesOwnership_"+interrupt+"_Waiting="+duringWait);
                if(interrupt=="medical")Check(h.CombatCommon.Next?.Reason=="healInCover"&&h.CombatCommon.CoverClears==clears,"Medical_PriorityAndDestinationPreserved_"+duringWait);
            }
        }
        h=Fresh();NavMesh.Available=false;h.End(Travel());wait=SelectPrepared(h);h.ExplicitRegroup=true;
        Check(h.End(wait).Reason=="sniperExplicitRegroup"&&!h.ContinueArrival(out keep),"ExplicitRegroup_ImmediateInterruption");
        h=Fresh();h.End(Travel());adjust=SelectPrepared(h);h.BotOwner.Memory.IsUnderFire=true;
        Check(!h.End(adjust).Value&&NavMesh.Samples==32,"Pressure_NoRecoverySuccessor_DoesNotAbandonAdjustment");
        h=Fresh();h.End(Travel());adjust=SelectPrepared(h);h.DecisionChanged(adjust,new Decision(BotLogicDecision.heal,"heal"));
        Check(!h.ContinueArrival(out keep),"DifferentDecision_AbandonsOldTransaction");

        foreach(var move in new[]{new Decision(BotLogicDecision.goToPoint,"sniper.position.reengage.runToPoint"),new Decision(BotLogicDecision.goToPointTactical,"sniper.startCloseSearch")}){
            h=Fresh();h.End(move);adjust=SelectPrepared(h);
            Check(adjust.Action==BotLogicDecision.goToPoint&&!adjust.Reason.Contains(".runToPoint"),"PointArrival_UsesLocalWalkingReason_"+move.Reason);
        }
        foreach(var move in new[]{new Decision(BotLogicDecision.goToPoint,"sniper.NeedSniper.position.goToPoint"),new Decision(BotLogicDecision.runToCover,"recovery.runToHeal"),new Decision(BotLogicDecision.runToCover,"sniper.recoverCover"),new Decision(BotLogicDecision.goToPoint,"regroup.run"),new Decision(BotLogicDecision.goToPointTactical,"enemySearch")}){
            h=Fresh();h.End(move);Check(NavMesh.Samples==0&&h.CombatCommon.Next==null,"UnrelatedMovement_ContractUnchanged_"+move.Reason);
        }
        foreach(var reason in new[]{"sniper.closeSearch.arrival.adjust","sniper.closeSearch.arrival.wait","sniper.closeSearch.arrival.shot"})
            Check(ArrivalSniper.IsAutomaticSupportIntentReason(reason),"AutomaticWeaponIntent_"+reason);

        foreach(string reject in new[]{"outsideRadius","alreadyArrived","noPath","longPath","noLane","blocked","wrongLevel","alternateThreat","marksmanPolicy","nonFinite"}){
            h=Fresh();var common=h.CombatCommon;NavMesh.PinnedPoint=new Vector3(8,0,0);
            if(reject=="outsideRadius")NavMesh.PinnedPoint=new Vector3(15.01f,0,0);
            if(reject=="alreadyArrived")NavMesh.PinnedPoint=new Vector3(2,0,0);
            if(reject=="noPath")Utils.Utils.Complete=false;
            if(reject=="longPath")Utils.Utils.NavDistance=30.01f;
            if(reject=="noLane")Utils.Utils.Lane=false;
            if(reject=="blocked")common.Blocked=true;
            if(reject=="wrongLevel")NavMesh.PinnedPoint=new Vector3(8,3,0);
            if(reject=="alternateThreat")common.AlternateThreat=true;
            if(reject=="marksmanPolicy")common.WrongPolicy=true;
            if(reject=="nonFinite")NavMesh.PinnedPoint=Vector3.positiveInfinity;
            Check(!common.TryCreateLocalFiringPositionAdjustment(h.BotOwner.Memory.GoalEnemy,Vector3.zero,false,"adjust",out keep),"LocalCandidate_Rejects_"+reject);
        }
        h=Fresh();NavMesh.PinnedPoint=new Vector3(15,0,0);Utils.Utils.NavDistance=30;h.CombatCommon.Separated=true;
        Check(h.CombatCommon.TryCreateLocalFiringPositionAdjustment(h.BotOwner.Memory.GoalEnemy,Vector3.zero,false,"adjust",out keep),"LocalCandidate_15mAnd30mBoundaries_NoNewBossLeash");
        h=Fresh();NavMesh.PinnedPoint=new Vector3(4,0,0);h.BotOwner.Memory.GoalEnemy.Anchor=new Vector3(20,0,0);h.CombatCommon.WrongPolicy=true;
        Check(h.CombatCommon.TryCreateLocalFiringPositionAdjustment(h.BotOwner.Memory.GoalEnemy,Vector3.zero,true,"adjust",out keep),"AutomaticCandidate_16mBoundary_NoSniperAdvancePolicy");
        NavMesh.PinnedPoint=new Vector3(4.01f,0,0);
        Check(!h.CombatCommon.TryCreateLocalFiringPositionAdjustment(h.BotOwner.Memory.GoalEnemy,Vector3.zero,true,"adjust",out keep),"AutomaticCandidate_Inside16mRejected");
        NavMesh.PinnedPoint=new Vector3(4,2,0);h.BotOwner.Memory.GoalEnemy.Anchor=new Vector3(19.99f,0,0);
        Check(!h.CombatCommon.TryCreateLocalFiringPositionAdjustment(h.BotOwner.Memory.GoalEnemy,Vector3.zero,true,"adjust",out keep),"AutomaticCandidate_StandoffIsHorizontal");
        h=Fresh();h.End(Travel());adjust=SelectPrepared(h);h.BotOwner.Position=h.BotOwner.GoToSomePointData.Point-new Vector3(2,0,0);
        Check(h.End(adjust).Value&&SelectPrepared(h).Action==BotLogicDecision.holdPosition,"Adjustment_ArrivalUsesTwoMeters");

        var state=new pitTeam.BigBrain.FiringPositionArrivalState();
        Check(state.Begin(new Vector3(3,0,0),"enemy","source","prefix")&&state.TryAdjust(new Vector3(8,0,0)),"State_FirstAdjustmentAccepted");
        Check(!state.TryAdjust(new Vector3(9,0,0))&&!state.Begin(new Vector3(10,0,0),"enemy","source","prefix")&&state.Origin.x==3,"State_CannotReadjustOrRecenter");
        state.BeginWait(100,2);state.BeginWait(101,2.5f);
        Check(state.WaitUntil==102&&!state.TryRelease(101.99f)&&state.TryRelease(102),"State_WaitCannotRenew_ExactExpiry");
        Check(!state.TryAdjust(new Vector3(9,0,0)),"State_ReleasedCannotReadjust");state.Reset();
        Check(state.Begin(Vector3.zero,"newEnemy","newSource","prefix"),"State_ResetAllowsSeparateArrival");
        return checks;
    }
}
__ARRIVAL_SOURCE__
'@
$harness = $harness.Replace('__COMMON_METHODS__', ($commonMethods -join "`n")).Replace('__SNIPER_METHODS__', ($sniperMethods -join "`n")).Replace('__ARRIVAL_SOURCE__', $arrivalSource.Replace('using System;', '').Replace('using UnityEngine;', ''))
Add-Type -TypeDefinition $harness -Language CSharp
$count = [MarksmanArrivalChecks]::Run()
Write-Output "Passed $count Marksman arrival boundary checks against production dispatch/state/planner methods. Actual movement and shooting still require a raid test."
