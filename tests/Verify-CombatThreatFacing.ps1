param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

# Compile production facing/close-stop/sampling methods against controlled EFT/Unity stand-ins.
# These are boundary checks, not a simulation of physics, pathfinding, steering animation or a raid.
$ErrorActionPreference = 'Stop'
$actionRoot = Join-Path $RepositoryRoot 'client/BigBrain/Actions'
$lookSource = Get-Content -Raw (Join-Path $actionRoot 'CombatAttackMoveLook.cs')
$runSource = Get-Content -Raw (Join-Path $actionRoot 'CombatRunToEnemyAction.cs')
$walkSource = Get-Content -Raw (Join-Path $actionRoot 'CombatGoToEnemyAction.cs')
$holdSource = Get-Content -Raw (Join-Path $actionRoot 'CombatHoldPositionAction.cs')
$dogSource = Get-Content -Raw (Join-Path $actionRoot 'CombatDogFightAction.cs')
function Get-CombatMethod([string]$Source, [string]$Name) {
    $pattern = '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $matches = [regex]::Matches($Source, $pattern)
    if ($matches.Count -ne 1) { throw "Expected one source method: $Name; got $($matches.Count)" }
    return $matches[0].Value
}
$runMethods = @(
    'UpdateLook', 'TryApplyCommittedLook', 'TryLookAtKnownThreat', 'TryGetThreatLookPoint',
    'CommitLookMode', 'TryStopUnsafeCloseKnownThreatAdvance', 'TryLookAtCloseKnownThreat',
    'TryGetCloseKnownThreatData', 'TryGetMoveTargetDirection', 'LookAtThreatAnchor',
    'TrySampleRunPoint', 'Flatten', 'IsFinite'
) | ForEach-Object { Get-CombatMethod $runSource $_ }
$walkMethods = @('TryGetEnemyLookAnchor', 'TryGetOwnedEnemyLookDirection', 'Flatten', 'IsFinite') |
    ForEach-Object { Get-CombatMethod $walkSource $_ }
$holdMethods = @('GetEnemyLookPoint', 'TryLookTowardEnemy', 'Look', 'LookInRandomDirection', 'TryGetClosestAllyLookPoint', 'TryCollectClosestAllyFromEnumerable', 'TryUpdateClosestAlly') | ForEach-Object { (Get-CombatMethod $holdSource $_).Replace('public override void Look()', 'public void UpdateLook()') }
$holdConstants = [regex]::Matches($holdSource, '(?m)^        private const float [^\r\n]+') | ForEach-Object { $_.Value }
$dogMethods = @('MaintainThreatFacing', 'GetLookAngleToPoint') | ForEach-Object { Get-CombatMethod $dogSource $_ }
$runConstants = [regex]::Matches($runSource, '(?m)^        private const float [^\r\n]+') | ForEach-Object { $_.Value }
$lookEnum = [regex]::Match($runSource, '(?ms)^        private enum RunLookMode.*?^        \}').Value
$harness = @'
#nullable enable
#pragma warning disable CS0649, CS0414, CS8600
using System;
using System.Collections;
using EFT;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;
using pitTeam.Components;
using pitTeam.BigBrain.Actions;
namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Random { public static int Calls; public static float Range(float a,float b){Calls++;return (a+b)/2f;} }
    public static class Mathf { public const float PI=(float)Math.PI; public static float Sin(float a)=>(float)Math.Sin(a); public static float Cos(float a)=>(float)Math.Cos(a); public static float Abs(float a)=>Math.Abs(a); public static float Clamp01(float a)=>Math.Max(0,Math.Min(1,a)); }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1,0); public static Vector3 forward=>new Vector3(0,0,1);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude>0?this/magnitude:zero; public void Normalize(){this=normalized;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new Vector3(a.x/b,a.y/b,a.z/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<0.00001f;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object? other)=>other is Vector3 v&&this==v;
        public override int GetHashCode()=>x.GetHashCode()^y.GetHashCode()^z.GetHashCode();
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Angle(Vector3 a,Vector3 b) {
            double length=a.magnitude*b.magnitude;
            return length<0.00001?0:(float)(Math.Acos(Math.Max(-1d,Math.Min(1d,(a.x*b.x+a.y*b.y+a.z*b.z)/length)))*180/Math.PI);
        }
    }
}
namespace UnityEngine.AI {
    public struct NavMeshHit { public Vector3 position; }
    public static class NavMesh {
        public const int AllAreas=-1; public static bool Success=true; public static Vector3 Sample;
        public static bool SamplePosition(Vector3 p,out NavMeshHit h,float radius,int areas){h=new NavMeshHit{position=Sample};return Success;}
    }
}
namespace EFT {
    public class EnemyInfo {
        public string ProfileId="enemy"; public Person Person=new Person();
        public bool IsVisible,CanShoot; public float PersonalLastSeenTime;
        public Vector3 PersonalLastPos,EnemyLastPositionReal,CurrPosition;
        public GroupInfo? GroupInfo=new GroupInfo();
        public Vector3 GetBodyPartPosition()=>CurrPosition+Vector3.up*1.6f;
    }
    public class Person { public Health HealthController=new Health(); }
    public class Health { public bool IsAlive=true; }
    public class Transform { public Vector3 position,forward=new Vector3(1,0,0); }
    public class GroupInfo { public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense; }
    public class Mover { public bool HasPathAndNoComplete=true,Sprinting; public Vector3 DirCurPoint; public Vector3? TargetPoint=new Vector3(190,0,-36); }
    public class LookData { public void SetLookPointByHearing(object? p){} }
    public class Observer { public void Stop(){} }
    public class Memory { public Observer botObserveData=new Observer(); public EnemyInfo? GoalEnemy,LastEnemy; }
    public class Steering {
        public string Mode=""; public Vector3 Point;
        public void LookToPoint(Vector3 p){Mode="point";Point=p;}
        public void LookToDirection(Vector3 p){Mode="direction";Point=p;}
        public void LookToMovingDirection(){Mode="route";}
    }
    public class BotsGroup { public BotOwner[] Members=Array.Empty<BotOwner>(); public IEnumerable? Allies; public int MembersCount=>Members.Length; public BotOwner Member(int i)=>Members[i]; }
    public class BotOwner {
        public bool IsDead,IsFollower=true; public BotsGroup? BotsGroup;
        public Transform? WeaponRoot; public Transform Transform=new Transform();
        public Vector3 Position=new Vector3(160,0,-24),LookDirection=new Vector3(1,0,0);
        public Mover Mover=new Mover(); public Steering Steering=new Steering(); public Memory Memory=new Memory();
        public LookData LookData=new LookData(); public bool Stopped;
        public void StopMove(){Stopped=true;Mover.HasPathAndNoComplete=false;} public void SetPose(float p){}
    }
}
namespace pitTeam.Modules { public static class BossPlayers { public static bool IsFollower(BotOwner b)=>b.IsFollower; } }
namespace pitTeam.Components { public static class BotFollowerPlayer { public static bool TryApplyCommandLookOverride(BotOwner b)=>false; } }
namespace pitTeam.Utils {
    public static class FollowerAwareness {
        public static bool HasThreat; public static Vector3 Threat;
        public static bool TryGetTargetHandoffLookPoint(BotOwner bot,out Vector3 point){point=Threat;return false;}
        public static bool TryGetRecentThreatLookPoint(BotOwner bot,out Vector3 point){point=Threat;return HasThreat;}
        public static bool TryGetRecentFireThreatLookPoint(BotOwner bot,out Vector3 point,out bool dead){point=Threat;dead=false;return HasThreat;}
    }
}
public static class MyExtensions { public static float Random(float a,float b)=>a; }
namespace pitTeam.BigBrain {
    public static class FollowerCombatCommon {
        public static bool IsFinite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
        public static Vector3 GetEnemyCurrentPosition(EnemyInfo e)=>e.CurrPosition;
    }
}
namespace pitTeam.BigBrain.Actions {
    public class RunHarness {
        __RUN_CONSTANTS__
        __LOOK_ENUM__
        private RunLookMode committedLookMode; private float committedLookModeUntil;
        public BotOwner BotOwner=new BotOwner(); public bool Sprint=true,CommitCleared;
        private void ClearCommittedRunPoint(){CommitCleared=true;} private void SetCombatSprint(bool value){Sprint=value;}
        public bool StopForThreat(EnemyInfo e)=>TryStopUnsafeCloseKnownThreatAdvance(e);
        public bool GetLook(EnemyInfo e,float distance,out Vector3 point)=>TryGetThreatLookPoint(e,distance,out point);
        public void Look(EnemyInfo e)=>UpdateLook(e,true);
        public bool ApplyCommittedLook(EnemyInfo e)=>TryApplyCommittedLook(e,true);
        public static bool Sample(Vector3 candidate,Vector3 enemy,out Vector3 point)=>TrySampleRunPoint(candidate,enemy,out point);
        __RUN_METHODS__
    }
    public class WalkHarness {
        public BotOwner BotOwner=new BotOwner();
        public bool GetLook(EnemyInfo e,out Vector3 direction)=>TryGetOwnedEnemyLookDirection(e,out direction);
        __WALK_METHODS__
    }
    public class HoldHarness {
        __HOLD_CONSTANTS__
        private Vector3 idleLookDirection; private float nextIdleLookAt;
        private bool TryLookTowardCloseUnseenThreat()=>false;
        private bool TryLookTowardSuppressionThreat()=>false;
        private bool TryLookTowardBossRangedThreat()=>false;
        public bool AllyPoint(out Vector3 point)=>TryGetClosestAllyLookPoint(out point);
        public BotOwner _owner=new BotOwner(); public bool Cleared,Applied;
        private bool CanKeepCurrentLook(EnemyInfo e)=>true;
        private bool TryAcquireNewLook(EnemyInfo e)=>true;
        private void ApplyCurrentLook(){Applied=true;}
        private void ClearCurrentLook(){Cleared=true;}
        public bool Look()=>TryLookTowardEnemy();
        __HOLD_METHODS__
    }
    public class DogHarness {
        public BotOwner BotOwner=new BotOwner();
        public void Look(EnemyInfo e)=>MaintainThreatFacing(e);
        public float Angle(Vector3 point)=>GetLookAngleToPoint(point);
        __DOG_METHODS__
    }
    public static class ThreatFacingChecks {
        private static int count;
        private static void Check(bool value,string name){count++;if(!value)throw new Exception(name);}
        private static bool Same(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<0.00001f;
        private static EnemyInfo Enemy(float age=62f)=>new EnemyInfo {
            PersonalLastSeenTime=Time.time-age, PersonalLastPos=new Vector3(152,0,-21),
            EnemyLastPositionReal=new Vector3(152,0,-21), CurrPosition=new Vector3(187,0,-36),
            GroupInfo=new GroupInfo{EnemyLastSeenTimeReal=Time.time-age,EnemyLastSeenTimeSense=Time.time}
        };
        public static int Run() {
            Time.time=1447.87f;
            var h=new RunHarness(); var e=Enemy();
            Check(!h.StopForThreat(e)&&!h.BotOwner.Stopped&&!h.CommitCleared,"Woods_OldPassedPosition_DoesNotStopRun");
            Check(!h.GetLook(e,90,out _),"Woods_OldPassedPosition_DoesNotOwnLook");
            h.Look(e);Check(h.BotOwner.Steering.Mode=="direction"&&Same(h.BotOwner.Steering.Point,CombatAttackMoveLook.GetMovementOrLevelDirection(h.BotOwner)),"NoFreshLook_PathBeforeSprint_FacesRoute");
            h=new RunHarness();h.BotOwner.Mover.Sprinting=true;h.Look(e);
            Check(h.BotOwner.Steering.Mode=="route","SprintingWithPath_StillFacesRoute");
            Check(h.ApplyCommittedLook(e),"SprintingWithPath_KeepsRouteCommitment");
            h.BotOwner.Mover.Sprinting=false;
            Check(!h.ApplyCommittedLook(e),"SprintStops_RouteCommitmentReleasesBeforeLeaseExpires");
            var freshAfterSprint=Enemy(1);freshAfterSprint.PersonalLastPos=new Vector3(140,0,-24);
            freshAfterSprint.EnemyLastPositionReal=freshAfterSprint.PersonalLastPos;
            h.Look(freshAfterSprint);
            Check(h.BotOwner.Steering.Mode=="point"&&Same(h.BotOwner.Steering.Point,freshAfterSprint.PersonalLastPos+Vector3.up*0.8f),"SprintStops_FreshThreatLookWinsImmediately");
            h=new RunHarness();h.BotOwner.Mover.Sprinting=true;h.BotOwner.Mover.HasPathAndNoComplete=false;h.Look(e);
            Check(h.BotOwner.Steering.Mode=="direction","SprintWithoutPath_DoesNotCommitRouteLook");
            h=new RunHarness();h.BotOwner.Mover.Sprinting=true;h.Look(e);h.BotOwner.Mover.HasPathAndNoComplete=false;
            Check(!h.ApplyCommittedLook(e),"PathLost_RouteCommitmentReleasesBeforeLeaseExpires");
            e.PersonalLastPos=new Vector3(90,0,-24);e.EnemyLastPositionReal=e.PersonalLastPos;
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(h.BotOwner,e,out _),"LongRange_OldSighting_DoesNotOwnTacticalLook");
            Check(!new WalkHarness().GetLook(e,out _),"Walking_UsesSameExpiry");
            foreach(float age in new[]{0f,1f,12f}) {
                e=Enemy(age);h=new RunHarness();
                Check(h.StopForThreat(e)&&h.BotOwner.Stopped&&!h.Sprint,"RecentCloseThreat_StillStopsBadFacing_"+age);
                Check(Same(h.BotOwner.Steering.Point,e.PersonalLastPos+Vector3.up*0.8f),"RecentCloseThreat_UsesOneLookHeight_"+age);
            }
            e=Enemy(12.01f);Check(!new RunHarness().StopForThreat(e),"JustExpired_NoCloseStop");
            e=Enemy();e.IsVisible=true;e.CurrPosition=new Vector3(155,0,-24);h=new RunHarness();
            Check(h.StopForThreat(e)&&Same(h.BotOwner.Steering.Point,e.GetBodyPartPosition()),"VisibleCloseThreat_OverridesStaleMemory");
            e.CanShoot=true;h=new RunHarness();Check(!h.StopForThreat(e),"VisibleShootableThreat_LeavesImmediateFireOwner");
            e=Enemy(1);e.PersonalLastPos=new Vector3(163,0,-24);e.EnemyLastPositionReal=e.PersonalLastPos;
            Check(new RunHarness().StopForThreat(e),"FreshPointBlank_StopsEvenWhenFacingForward");
            e.PersonalLastPos=new Vector3(168,0,-24);e.EnemyLastPositionReal=e.PersonalLastPos;h=new RunHarness();
            h.BotOwner.Mover.TargetPoint=new Vector3(150,0,-24);
            Check(h.StopForThreat(e),"FreshCloseThreat_BadPathStillStops");
            h=new RunHarness();h.BotOwner.Mover.TargetPoint=new Vector3(180,0,-24);
            Check(!h.StopForThreat(e),"FreshCloseThreat_AlignedAdvanceContinues");
            e=Enemy(1);e.PersonalLastPos=new Vector3(90,0,-24);e.EnemyLastPositionReal=e.PersonalLastPos;
            h=new RunHarness();Check(h.GetLook(e,90,out _)&&!h.StopForThreat(e),"FreshDistantThreat_LookWithoutCloseStop");
            Check(new WalkHarness().GetLook(e,out _),"Walking_FreshDistantThreatPreserved");
            e=Enemy(5);e.GroupInfo!.EnemyLastSeenTimeReal=Time.time-1;e.EnemyLastPositionReal=new Vector3(180,0,-24);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(h.BotOwner,e,out var point)&&Same(point,e.EnemyLastPositionReal+Vector3.up*0.8f),"NewerSharedSighting_BeatsOldPersonalPosition");
            e=Enemy();e.GroupInfo!.EnemyLastSeenTimeReal=Time.time-1;e.EnemyLastPositionReal=new Vector3(180,0,-24);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(h.BotOwner,e,out point)&&Same(point,e.EnemyLastPositionReal+Vector3.up*0.8f),"FreshSharedSighting_SurvivesExpiredPersonal");
            e=Enemy(1);e.EnemyLastPositionReal=new Vector3(180,0,-24);e.GroupInfo!.EnemyLastSeenTimeReal=Time.time-2;
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(h.BotOwner,e,out point)&&Same(point,e.PersonalLastPos+Vector3.up*0.8f),"NewerPersonalSighting_Wins");
            e=Enemy();e.PersonalLastSeenTime=0;e.GroupInfo!.EnemyLastSeenTimeReal=0;
            Check(!h.GetLook(e,90,out _),"SenseOnlyAndHiddenLiveTransform_DoNotGrantLook");
            e=Enemy(-1);Check(!h.GetLook(e,90,out _),"FutureTimestamps_DoNotGrantLook");
            e=Enemy(1);e.GroupInfo=null;Check(h.GetLook(e,90,out _),"MissingGroup_PersonalMemoryStillWorks");
            foreach(var bad in new[]{Vector3.zero,h.BotOwner.Position,new Vector3(float.NaN,0,1),new Vector3(1,0,float.PositiveInfinity)}) {
                e=Enemy(1);e.PersonalLastPos=bad;e.EnemyLastPositionReal=bad;
                Check(!h.GetLook(e,90,out _),"InvalidOrCoincidentMemory_Rejected");
            }
            e=Enemy(1);h=new RunHarness();h.BotOwner.Mover.HasPathAndNoComplete=false;h.Look(e);
            Check(h.BotOwner.Steering.Mode=="point","FreshCloseThreat_NoPathStillFacesThreat");
            e=Enemy(1);e.PersonalLastPos=new Vector3(90,0,-24);e.EnemyLastPositionReal=e.PersonalLastPos;h=new RunHarness();
            h.Look(e);Check(h.BotOwner.Steering.Mode=="point","FreshDistantThreat_OwnsWalkingLook");
            Time.time+=13;h.Look(e);Check(h.BotOwner.Steering.Mode=="direction","ExpiredCommittedLook_ReleasesToLevelRoute");
            var walk=new WalkHarness();e=Enemy(5);
            walk.BotOwner.Position=e.PersonalLastPos+new Vector3(0.5f,0,0);
            Check(!walk.GetLook(e,out _),"Walking_ReachedSharedMemoryRejected");
            e.GroupInfo!.EnemyLastSeenTimeReal=Time.time-1;e.EnemyLastPositionReal=new Vector3(180,0,-24);
            Check(walk.GetLook(e,out _),"Walking_FreshDifferentSharedPointNotDiscarded");
            Time.time=658.036743f;
            var brick=new WalkHarness();
            brick.BotOwner.Position=new Vector3(-161.219452f,2.19807339f,233.153915f);
            e=Enemy(12.1033936f);e.PersonalLastPos=new Vector3(-170,2.3f,230);
            e.GroupInfo!.EnemyLastSeenTimeReal=649.883362f;
            e.EnemyLastPositionReal=new Vector3(-161.109772f,2.57785726f,233.032715f);
            Check(!brick.GetLook(e,out _),"Brick_Recorded82DegreeRequest_RejectedAtReachedReport");
            brick.BotOwner.Position+=new Vector3(4,0,0);
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(brick.BotOwner,e,out _),"ReachedReport_CannotReviveWhenBotMovesAwayOrChangesAction");
            e.GroupInfo.EnemyLastSeenTimeReal=Time.time;
            Check(brick.GetLook(e,out _),"NewReport_RenewsLookAfterReachedRelease");
            var crossed=new BotOwner{Position=new Vector3(100,0,100)};
            e=Enemy(5);e.PersonalLastPos=e.EnemyLastPositionReal=new Vector3(103,0,100);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(crossed,e,out _),"ReportAhead_UsefulBeforeCrossing");
            crossed.Position=new Vector3(106,0,100);
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(crossed,e,out _),"CrossedReport_RejectedEvenWhenUpdateSkipsArrivalRadius");
            crossed.Position=new Vector3(110,0,100);
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(crossed,e,out _),"CrossedReport_DoesNotRenewOnRepeatedLookup");
            var near=new BotOwner{Position=new Vector3(100,0,100)};
            e=Enemy(1);e.PersonalLastPos=e.EnemyLastPositionReal=new Vector3(101,0,100);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(near,e,out _),"FreshCloseContact_Preserved");
            Time.time+=1.01f;near.Position=new Vector3(104,0,100);
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(near,e,out _),"ReachedWhileFresh_ReleasesWhenFreshnessExpires");
            near=new BotOwner{Position=new Vector3(100,0,100)};e=Enemy(5);e.PersonalLastPos=e.EnemyLastPositionReal=new Vector3(100,5,100);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(near,e,out _),"OtherFloorMemory_NotTreatedAsReached");
            e.IsVisible=true;e.CurrPosition=new Vector3(100,6,100);
            Check(CombatAttackMoveLook.TryGetReliableThreatLookPoint(near,e,out point)&&Same(point,e.GetBodyPartPosition()),"VisibleElevatedEnemy_PreservesVerticalLook");
            var originBot=new BotOwner{Position=new Vector3(100,0,100),WeaponRoot=new Transform{position=new Vector3(100,1.2f,100)}};
            Check(CombatAttackMoveLook.TryGetLookDirection(originBot,new Vector3(104,1.2f,100),out var level)&&Math.Abs(level.y)<0.00001f,"WeaponHeightTarget_ProducesLevelDirection");
            originBot.WeaponRoot=null;
            Check(CombatAttackMoveLook.TryGetLookDirection(originBot,new Vector3(104,1.2f,100),out level)&&Math.Abs(level.y)<0.00001f,"FallbackOrigin_IncludesStandingHeight");
            originBot.LookDirection=new Vector3(1,20,0);originBot.Mover.HasPathAndNoComplete=false;
            CombatAttackMoveLook.LookAlongMovementOrLevel(originBot);
            Check(Math.Abs(originBot.Steering.Point.y)<0.00001f,"NoPath_LevelsPreviousSkyLook");
            e=Enemy(5);e.CurrPosition=new Vector3(1000,2000,3000);
            Check(!CombatAttackMoveLook.TryGetCombatThreatLookPoint(originBot,e,out _),"Dogfight_DoesNotUseOldReportOrHiddenLiveTransform");
            pitTeam.Utils.FollowerAwareness.HasThreat=true;pitTeam.Utils.FollowerAwareness.Threat=new Vector3(90,1.2f,100);
            Check(CombatAttackMoveLook.TryLookThreatFacing(originBot,e,true)&&Same(originBot.Steering.Point,pitTeam.Utils.FollowerAwareness.Threat),"Retreat_PreservesFreshIncomingFireBearing");
            pitTeam.Utils.FollowerAwareness.HasThreat=false;e=Enemy(1);
            Check(CombatAttackMoveLook.TryGetCombatThreatLookPoint(new BotOwner(),e,out _),"Dogfight_FreshReportRemainsEligible");
            e.Person.HealthController.IsAlive=false;
            Check(!CombatAttackMoveLook.TryGetReliableThreatLookPoint(new BotOwner(),e,out _),"DeadEnemy_DoesNotOwnLook");
            e=Enemy(1);var hold=new HoldHarness();hold._owner.Memory.GoalEnemy=e;
            Check(hold.Look()&&hold.Applied,"Hold_FreshReportKeepsCachedLook");
            Time.time+=12f;hold.Applied=false;
            Check(!hold.Look()&&hold.Cleared&&!hold.Applied,"Hold_ExpiryClearsCachedLookBeforeItCanRun");
            e=Enemy(5);hold=new HoldHarness();hold._owner.Position=e.EnemyLastPositionReal+new Vector3(0.2f,0,0);hold._owner.Memory.GoalEnemy=e;
            Check(!hold.Look()&&hold.Cleared,"Hold_VisitedReportCannotReuseCornerCache");
            hold=new HoldHarness();hold._owner.Position=new Vector3(100,0,100);
            var nearAlly=new BotOwner{Position=new Vector3(100.5f,0,100)};
            var farAlly=new BotOwner{Position=new Vector3(104,0,100),WeaponRoot=new Transform{position=new Vector3(104,1.4f,100)}};
            var fartherAlly=new BotOwner{Position=new Vector3(107,0,100)};
            hold._owner.BotsGroup=new BotsGroup{Members=new[]{hold._owner,nearAlly,fartherAlly,farAlly}};
            Check(hold.AllyPoint(out point)&&Same(point,farAlly.WeaponRoot.position),"Hold_SkipsCloseFollowerAndChoosesNearestEligibleUpperBody");
            hold.UpdateLook();Check(hold._owner.Steering.Mode=="point"&&Same(hold._owner.Steering.Point,point),"Hold_FullLookUsesEligibleFollower");
            farAlly.IsDead=true;fartherAlly.IsFollower=false;
            Check(!hold.AllyPoint(out _),"Hold_DeadNonFollowerAndNearbyMembersDoNotOwnLook");
            UnityEngine.Random.Calls=0;hold.UpdateLook();var randomDirection=hold._owner.Steering.Point;
            Check(hold._owner.Steering.Mode=="direction"&&Math.Abs(randomDirection.y)<0.00001f&&randomDirection.sqrMagnitude>0.99f,"Hold_NoEligibleFollowerChoosesHorizontalRandomLook");
            Check(UnityEngine.Random.Calls==2,"Hold_SamplesDirectionAndLeaseOnce");
            Time.time+=0.5f;hold.UpdateLook();Check(UnityEngine.Random.Calls==2&&Same(randomDirection,hold._owner.Steering.Point),"Hold_RandomLookDoesNotJitterEveryUpdate");
            Time.time+=4f;hold.UpdateLook();Check(UnityEngine.Random.Calls==4,"Hold_RandomScanRenewsAfterLease");
            farAlly.IsDead=false;hold.UpdateLook();Check(hold._owner.Steering.Mode=="point","Hold_EligibleFollowerPreemptsRandomLease");
            farAlly.Position=new Vector3(102.99f,0,100);farAlly.WeaponRoot=null;
            Check(!hold.AllyPoint(out _),"Hold_RejectsFollowerInsideThreeMeters");
            farAlly.Position=new Vector3(103,0,100);
            Check(hold.AllyPoint(out point)&&Math.Abs(point.y-1.2f)<0.001f,"Hold_ThreeMeterBoundaryUsesHeightFallback");
            farAlly.Position=new Vector3(100.5f,8,100);
            Check(!hold.AllyPoint(out _),"Hold_ClosePlanarFollowerOnAnotherFloorCannotCauseVerticalStare");
            var alliedFollower=new BotOwner{Position=new Vector3(105,0,100)};
            hold._owner.BotsGroup.Allies=new[]{alliedFollower};
            Check(hold.AllyPoint(out point)&&Same(point,CombatAttackMoveLook.GetLookOrigin(alliedFollower)),"Hold_AlliedFollowerFallbackStillWorks");
            e=Enemy(0);hold._owner.Memory.GoalEnemy=e;hold.Applied=false;hold.UpdateLook();
            Check(hold.Applied,"Hold_FreshEnemyPreemptsAllyFallback");
            hold._owner.Memory.GoalEnemy=null;pitTeam.Utils.FollowerAwareness.HasThreat=true;pitTeam.Utils.FollowerAwareness.Threat=new Vector3(90,1,100);hold.UpdateLook();
            Check(Same(hold._owner.Steering.Point,pitTeam.Utils.FollowerAwareness.Threat),"Hold_FreshThreatPreemptsAllyFallback");
            pitTeam.Utils.FollowerAwareness.HasThreat=false;
            var dog=new DogHarness();e=Enemy(5);dog.BotOwner.LookDirection=new Vector3(1,5,0);
            dog.Look(e);
            Check(dog.BotOwner.Steering.Mode=="direction"&&Math.Abs(dog.BotOwner.Steering.Point.y)<0.00001f,"Dogfight_StaleContactFallsBackToLevelMovement");
            e.IsVisible=true;dog.Look(e);
            Check(dog.BotOwner.Steering.Mode=="point"&&Same(dog.BotOwner.Steering.Point,e.GetBodyPartPosition()),"Dogfight_VisibleEnemyImmediatelyRegainsLook");
            e.IsVisible=false;pitTeam.Utils.FollowerAwareness.HasThreat=true;pitTeam.Utils.FollowerAwareness.Threat=new Vector3(175,1.2f,-24);dog.Look(e);
            Check(Same(dog.BotOwner.Steering.Point,pitTeam.Utils.FollowerAwareness.Threat),"Dogfight_PreservesFreshIncomingFireLook");
            pitTeam.Utils.FollowerAwareness.HasThreat=false;dog.BotOwner.Position=new Vector3(100,0,100);dog.BotOwner.LookDirection=new Vector3(1,0,0);dog.BotOwner.WeaponRoot=new Transform{position=new Vector3(100,1.2f,100)};
            Check(dog.Angle(new Vector3(104,1.2f,100))<0.001f,"Dogfight_AngleUsesWeaponOrigin");
            var enemyPoint=new Vector3(187,0,-36);NavMesh.Sample=new Vector3(179,0,-36);
            Check(RunHarness.Sample(NavMesh.Sample,enemyPoint,out point)&&Same(point,NavMesh.Sample),"RunSample_ReturnsValidatedPointNotOrigin");
            foreach(float distance in new[]{5f,10f}) {
                NavMesh.Sample=enemyPoint+new Vector3(distance,0,0);
                Check(RunHarness.Sample(NavMesh.Sample,enemyPoint,out point)&&Same(point,NavMesh.Sample),"RunSample_StandoffBoundary_"+distance);
            }
            foreach(var bad in new[]{enemyPoint+new Vector3(4.99f,0,0),enemyPoint+new Vector3(10.01f,0,0),enemyPoint+new Vector3(8,1.26f,0),new Vector3(float.NaN,0,0)}) {
                NavMesh.Sample=bad;Check(!RunHarness.Sample(bad,enemyPoint,out point)&&Same(point,Vector3.zero),"RunSample_InvalidDoesNotPublishDestination");
            }
            NavMesh.Success=false;Check(!RunHarness.Sample(Vector3.zero,enemyPoint,out point)&&Same(point,Vector3.zero),"RunSample_FailedNavMeshDoesNotPublishDestination");
            return count;
        }
    }
}
'@
$lookSource = $lookSource -replace '(?m)^using [^;]+;\r?\n', ''
$harness = $harness.Replace('__RUN_CONSTANTS__', ($runConstants -join "`n")).Replace('__LOOK_ENUM__', $lookEnum).
    Replace('__HOLD_CONSTANTS__', ($holdConstants -join "`n")).Replace('__RUN_METHODS__', ($runMethods -join "`n")).Replace('__WALK_METHODS__', ($walkMethods -join "`n")).Replace('__HOLD_METHODS__', ($holdMethods -join "`n")).Replace('__DOG_METHODS__', ($dogMethods -join "`n"))
Add-Type -TypeDefinition ($harness + "`n" + $lookSource) -Language CSharp
$count = [pitTeam.BigBrain.Actions.ThreatFacingChecks]::Run()
Write-Output "Passed $count threat-facing boundary checks against production methods. In-game steering and navigation still require a raid test."
