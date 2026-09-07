param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

# Compile production facing/close-stop/sampling methods against controlled EFT/Unity stand-ins.
# These are boundary checks, not a simulation of physics, pathfinding, steering animation or a raid.
$ErrorActionPreference = 'Stop'
$actionRoot = Join-Path $RepositoryRoot 'client/BigBrain/Actions'
$lookSource = Get-Content -Raw (Join-Path $actionRoot 'CombatAttackMoveLook.cs')
$runSource = Get-Content -Raw (Join-Path $actionRoot 'CombatRunToEnemyAction.cs')
$walkSource = Get-Content -Raw (Join-Path $actionRoot 'CombatGoToEnemyAction.cs')
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
$walkMethods = @('TryGetEnemyLookAnchor', 'TryGetOwnedEnemyLookDirection', 'IsSameLocalLookPoint', 'Flatten', 'IsFinite') |
    ForEach-Object { Get-CombatMethod $walkSource $_ }
$runConstants = [regex]::Matches($runSource, '(?m)^        private const float [^\r\n]+') | ForEach-Object { $_.Value }
$lookEnum = [regex]::Match($runSource, '(?ms)^        private enum RunLookMode.*?^        \}').Value
$harness = @'
#nullable enable
#pragma warning disable CS0649, CS0414
using System;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using pitTeam.Components;
using pitTeam.BigBrain.Actions;
namespace UnityEngine {
    public static class Time { public static float time; }
    public static class Mathf { public static float Abs(float a)=>Math.Abs(a); }
    public struct Vector3 {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1,0);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized=>magnitude>0?this/magnitude:zero; public void Normalize(){this=normalized;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>new Vector3(a.x/b,a.y/b,a.z/b);
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
        public bool IsVisible,CanShoot; public float PersonalLastSeenTime;
        public Vector3 PersonalLastPos,EnemyLastPositionReal,CurrPosition;
        public GroupInfo? GroupInfo=new GroupInfo();
        public Vector3 GetBodyPartPosition()=>CurrPosition+Vector3.up*1.6f;
    }
    public class GroupInfo { public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense; }
    public class Mover { public bool HasPathAndNoComplete=true,Sprinting; public Vector3? TargetPoint=new Vector3(190,0,-36); }
    public class LookData { public void SetLookPointByHearing(object? p){} }
    public class Observer { public void Stop(){} }
    public class Memory { public Observer botObserveData=new Observer(); }
    public class Steering {
        public string Mode=""; public Vector3 Point;
        public void LookToPoint(Vector3 p){Mode="point";Point=p;}
        public void LookToDirection(Vector3 p){Mode="direction";Point=p;}
        public void LookToMovingDirection(){Mode="route";}
    }
    public class BotOwner {
        public Vector3 Position=new Vector3(160,0,-24),LookDirection=new Vector3(1,0,0);
        public Mover Mover=new Mover(); public Steering Steering=new Steering(); public Memory Memory=new Memory();
        public LookData LookData=new LookData(); public bool Stopped;
        public void StopMove(){Stopped=true;Mover.HasPathAndNoComplete=false;} public void SetPose(float p){}
    }
}
namespace pitTeam.Components { public static class BotFollowerPlayer { public static bool TryApplyCommandLookOverride(BotOwner b)=>false; } }
namespace pitTeam.Utils {}
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
        public BotOwner BotOwner=new BotOwner(); public bool RejectPassedLocalLook;
        private bool ShouldPreferMovingDirectionOverStaleLocalLook(EnemyInfo e)=>RejectPassedLocalLook;
        public bool GetLook(EnemyInfo e,out Vector3 direction)=>TryGetOwnedEnemyLookDirection(e,out direction);
        __WALK_METHODS__
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
            h.Look(e);Check(h.BotOwner.Steering.Mode=="direction"&&Same(h.BotOwner.Steering.Point,h.BotOwner.LookDirection),"NoFreshLook_PathBeforeSprint_PreservesLook");
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
            Time.time+=13;h.Look(e);Check(h.BotOwner.Steering.Mode=="direction","ExpiredCommittedLook_DoesNotForceWalkingToFaceRoute");
            var walk=new WalkHarness{RejectPassedLocalLook=true};e=Enemy(5);
            Check(!walk.GetLook(e,out _),"Walking_PassedLocalGuardPreserved");
            e.GroupInfo!.EnemyLastSeenTimeReal=Time.time-1;e.EnemyLastPositionReal=new Vector3(180,0,-24);
            Check(walk.GetLook(e,out _),"Walking_FreshDifferentSharedPointNotDiscarded");
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
    Replace('__RUN_METHODS__', ($runMethods -join "`n")).Replace('__WALK_METHODS__', ($walkMethods -join "`n"))
Add-Type -TypeDefinition ($harness + "`n" + $lookSource) -Language CSharp
$count = [pitTeam.BigBrain.Actions.ThreatFacingChecks]::Run()
Write-Output "Passed $count threat-facing boundary checks against production methods. In-game steering and navigation still require a raid test."
