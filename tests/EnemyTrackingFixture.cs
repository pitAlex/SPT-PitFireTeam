using System;
using System.Collections.Generic;
using EFT;
using pitTeam;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using UnityEngine;

namespace UnityEngine {
    public static class Time { public static float time; public static int frameCount; }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
}
namespace EFT {
    public class Player { public string ProfileId="enemy"; public Vector3 Position; }
    public class BotOwner {
        public string ProfileId="follower"; public bool Follower=true,Addon;
        public Vector3 Position; public Memory Memory=new Memory(); public Controller EnemiesController=new Controller();
        public ShootToPoint CurrentEnemyTargetPosition(bool _)=>new ShootToPoint(Memory.GoalEnemy.CurrPosition,1);
    }
    public class Memory {public EnemyInfo GoalEnemy;}
    public class Controller {public Dictionary<string,EnemyInfo> EnemyInfos=new Dictionary<string,EnemyInfo>();}
    public class GroupInfo {
        public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense;
        private Vector3 sensed,seen;
        public Vector3 EnemyLastPosition {get=>sensed;set{sensed=value;EnemyLastSeenTimeSense=Time.time;}}
        public Vector3 EnemyLastVisiblePosition {get=>seen;set{seen=value;EnemyLastSeenTimeReal=Time.time;}}
    }
    public class EnemyInfo {
        public BotOwner Owner;public string ProfileId="enemy";public bool IsVisible,CanShoot;
        public Vector3 CurrPosition,PersonalLastPos;
        public Vector3 EnemyLastPositionReal {get=>GroupInfo.EnemyLastVisiblePosition;set=>GroupInfo.EnemyLastVisiblePosition=value;}
        public Vector3 EnemyLastPosition {get=>GroupInfo.EnemyLastPosition;set=>GroupInfo.EnemyLastPosition=value;}
        public float PersonalLastSeenTime,PersonalSeenTime,Distance=50;public bool HaveSeenPersonal;public GroupInfo GroupInfo;
    }
    public class ShootToPoint {public Vector3 Point;public ShootToPoint(Vector3 p,float _){Point=p;}}
}
namespace pitTeam {
    public class ConfigValue<T> {public T Value;public ConfigValue(T v){Value=v;}}
    public static class pitFireTeam {
        public static ConfigValue<EnemyTrackingMode> enemyTracking=new ConfigValue<EnemyTrackingMode>(EnemyTrackingMode.Realistic);
        public static ConfigValue<int> enemyRemember=new ConfigValue<int>(20);
        public static bool UseSainFollowerCombat(BotOwner owner)=>owner.Addon;
    }
}
namespace pitTeam.Utils {public static class Utils {
    public static float Route=1;public static bool Reachable=true;
    public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float distance){distance=Route;return Reachable;}
}}
namespace pitTeam.Modules {
    public class Follower {public void ClearOrderedPushTargetLock(string _){} }
    public class BossPlayers {
        public static BossPlayers Instance=new BossPlayers();public static bool IsFollower(BotOwner o)=>o.Follower;
        public Follower GetFollower(BotOwner o)=>new Follower();
    }
    public static class SainGoalEnemyBridge {
        public static bool Native;public static Vector3 NativePoint;public static float NativeTime;
        public static bool TryGetTrackingReport(BotOwner owner,EnemyInfo enemy,out Vector3 point,out float time){point=NativePoint;time=NativeTime;return Native;}
        public static bool TryGetRetainedSameGoalEnemy(BotOwner owner,EnemyInfo enemy,out Vector3 point){point=NativePoint;return Native;}
    }
    public static class FollowerContactEnemyRetention {
        public static bool TryGetActiveRetainedEnemy(BotOwner owner,out Player enemy,out bool prioritized){enemy=null;prioritized=false;return false;}
        public static void ClearAndAllowNextGoalClear(BotOwner owner){}
    }
    public static class FollowerCombatTargetCommitments {
        public static int Clears;public static bool Mission;
        public static bool IsMissionTarget(BotOwner owner,EnemyInfo info)=>Mission;
        public static void ClearMission(BotOwner owner,object kind,string reason){Clears++;}
    }
    public static class FollowerGoalEnemyTracker {
        public struct Scope:IDisposable {public void Dispose(){} }
        public static Scope Begin(string a,string b)=>new Scope();
    }
    public static class SainAddonBridge {public static bool HasAcceptedGoalEnemy(BotOwner owner)=>owner.Memory.GoalEnemy!=null;}
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public class Places {public object LastKnownPlace=new object();public float TimeLastKnownUpdated;}
    public class Enemy {
        public BotOwner BotOwner;public EnemyInfo EnemyInfo;public Places KnownPlaces=new Places();
        public Vector3 EnemyPosition;public Vector3? LastKnownPosition;public bool Active=true,EnemyKnown=true;
        public static bool IsEnemyActive(Enemy enemy)=>enemy.Active;
    }
}
public static class TrackingChecks {
    private static int count;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
    private static void Tick(float time){Time.time=time;Time.frameCount++;}
    private static EnemyInfo Contact(BotOwner owner,string id,float x) {
        var info=new EnemyInfo{Owner=owner,ProfileId=id,CurrPosition=new Vector3(x,0,0),IsVisible=true};
        owner.EnemiesController.EnemyInfos[id]=info;owner.Memory.GoalEnemy=info;return info;
    }
    private static void CheckMemoryRepair() {
        FollowerEnemyTracking.BeginRaid();Tick(10);
        var owner=new BotOwner();
        var enemy=new EnemyInfo {
            Owner=owner,ProfileId="repair",CurrPosition=new Vector3(30,0,0),
            PersonalLastPos=new Vector3(float.PositiveInfinity,0,0),GroupInfo=new GroupInfo()
        };
        enemy.GroupInfo.EnemyLastPosition=new Vector3(float.NaN,0,0);
        enemy.GroupInfo.EnemyLastVisiblePosition=new Vector3(float.PositiveInfinity,0,0);
        Check(!FollowerEnemyTracking.IsEligible(enemy),"broken hidden memory initially has no eligible observation");
        pitTeam.Utils.Enemy.RepairPersonalMemory(enemy,new Vector3(40,0,0),true);
        Check(enemy.PersonalLastPos.x==30 && enemy.EnemyLastPosition.x==30 && enemy.EnemyLastPositionReal.x==30,
            "hidden Realistic repair restores invalid personal and group positions");
        Tick(11);
        Check(FollowerEnemyTracking.TryGetKnownPosition(enemy,out var point,out var observedAt) && point.x==30 && observedAt==10,
            "repaired vanilla memory supplies a tracking anchor with repair time");
        Check(FollowerEnemyTracking.IsEligible(enemy),"repaired contact becomes eligible");
        enemy.CurrPosition=new Vector3(90,0,0);
        pitTeam.Utils.Enemy.RepairPersonalMemory(enemy,new Vector3(90,0,0),true);
        Tick(12);
        Check(enemy.PersonalLastPos.x==30 && enemy.EnemyLastPosition.x==30 && enemy.EnemyLastPositionReal.x==30,
            "repeated repair preserves valid memory after hidden movement");
        Check(enemy.PersonalLastSeenTime==10 && enemy.PersonalSeenTime==10 &&
            enemy.GroupInfo.EnemyLastSeenTimeReal==10 && enemy.GroupInfo.EnemyLastSeenTimeSense==10,
            "repeated repair does not renew initialized timestamps");
        Check(FollowerEnemyTracking.Position(enemy).x==30,"tracking retains repaired anchor after hidden movement");
        Tick(31);pitTeam.Utils.Enemy.RepairPersonalMemory(enemy,enemy.CurrPosition,true);
        Check(!FollowerEnemyTracking.IsEligible(enemy),"repeated repair cannot extend ordinary expiry");
        var senseOnly=new EnemyInfo {Owner=owner,ProfileId="senseRepair",CurrPosition=new Vector3(50,0,0),GroupInfo=new GroupInfo()};
        pitTeam.Utils.Enemy.RepairPersonalMemory(senseOnly,senseOnly.CurrPosition,false);
        Check(!senseOnly.HaveSeenPersonal && senseOnly.PersonalLastSeenTime==0 && senseOnly.GroupInfo.EnemyLastSeenTimeReal==0 &&
            FollowerEnemyTracking.IsEligible(senseOnly),"sense-only repair restores knowledge without claiming personal sight");
        var noGroup=new EnemyInfo {Owner=owner,ProfileId="nullGroup",CurrPosition=new Vector3(float.NaN,0,0)};
        pitTeam.Utils.Enemy.RepairPersonalMemory(noGroup,new Vector3(60,0,0),true);
        Check(noGroup.PersonalLastPos.x==60 && FollowerEnemyTracking.IsEligible(noGroup),
            "null group and invalid live coordinates recover from the supplied fallback");
    }
    public static void Main() {
        CheckMemoryRepair();
        FollowerEnemyTracking.BeginRaid();Tick(1);
        var owner=new BotOwner();var enemy=Contact(owner,"a",10);
        Check(FollowerEnemyTracking.Position(enemy).x==10,"visible contact records position");
        enemy.IsVisible=false;enemy.CurrPosition=new Vector3(80,0,0);Tick(2);
        Check(FollowerEnemyTracking.Position(enemy).x==10,"silent hidden movement cannot move the tactical anchor");
        Check(FollowerEnemyTracking.Distance(enemy)==10,"distance uses the remembered point");
        Check(!enemy.IsVisible&&!enemy.CanShoot,"tracking never grants sight or fire");
        Tick(8);FollowerEnemyTracking.Report(enemy,new Vector3(20,0,0),8,"heard");
        Check(FollowerEnemyTracking.Position(enemy).x==20,"accepted sound/report updates anchor");
        FollowerEnemyTracking.CompleteSearch(enemy,8);Check(FollowerEnemyTracking.IsSearched(enemy),"arrival exhausts one report");
        Check(!FollowerEnemyTracking.IsEligible(enemy),"completed Core inspection releases active eligibility before remember time");
        Check(!FollowerEnemyTracking.CanRestore(owner,"a"),"retention cannot resurrect an exhausted report");
        Check(FollowerEnemyTracking.TryGetKnownPosition(enemy,out var kept,out _) && kept.x==20,"completion preserves living enemy memory");
        FollowerEnemyTracking.SearchTick(enemy);
        Check(!FollowerEnemyTracking.IsEligible(enemy),"search tick alone cannot renew exhausted evidence");
        owner.Addon=true;
        Check(FollowerEnemyTracking.IsEligible(enemy),"addon retains its native ordinary-memory semantics");
        owner.Addon=false;
        Tick(9);FollowerEnemyTracking.Report(enemy,new Vector3(20,0,0),9,"fresh same point");
        Check(!FollowerEnemyTracking.IsSearched(enemy),"fresh evidence at the same point reopens search");
        FollowerEnemyTracking.Report(enemy,new Vector3(99,0,0),8,"old retransmission");
        Check(FollowerEnemyTracking.Position(enemy).x==20,"old retransmissions do not replace evidence");
        Tick(29);Check(FollowerEnemyTracking.IsEligible(enemy),"arrival does not immediately delete ordinary memory");
        FollowerEnemyTracking.SearchTick(enemy);Tick(30);
        Check(FollowerEnemyTracking.IsEligible(enemy),"active unfinished search extends ordinary expiry");
        Check(FollowerEnemyTracking.ShouldRetainSearch(enemy),"stock forgetting cannot clear an active unfinished remembered search");
        enemy.CurrPosition=new Vector3(150,0,0);
        Check(FollowerEnemyTracking.Snapshot(owner,enemy)!=null && FollowerEnemyTracking.Position(enemy).x==20,"passive snapshot cannot replace evidence with a hidden position");
        FollowerEnemyTracking.EndSearch(owner,"a");
        Check(!FollowerEnemyTracking.IsEligible(enemy),"interrupted search cannot retain stale contact");
        Check(!FollowerEnemyTracking.ShouldRetainSearch(enemy),"interruption removes the stock-clear veto");
        FollowerEnemyTracking.SearchTick(enemy);Tick(410);
        Check(!FollowerEnemyTracking.IsEligible(enemy),"search has a finite 400 second bound");
        var second=Contact(owner,"b",30);FollowerEnemyTracking.Position(second);
        owner.Memory.GoalEnemy=enemy;FollowerEnemyTracking.Update(owner);
        Check(owner.Memory.GoalEnemy==null&&FollowerEnemyTracking.IsEligible(second),"forgetting one enemy preserves another contact");
        Check(FollowerCombatTargetCommitments.Clears==0,"expiring a temporary contact preserves an unrelated mission");
        Check(!FollowerEnemyTracking.CanRestore(owner,"a"),"expired retained contact cannot resurrect");
        Tick(411);FollowerEnemyTracking.Report(enemy,new Vector3(25,0,0),411,"new contact");
        Check(FollowerEnemyTracking.CanRestore(owner,"a"),"genuine reacquisition can restore eligibility");
        SainGoalEnemyBridge.Native=true;SainGoalEnemyBridge.NativePoint=new Vector3(45,0,0);SainGoalEnemyBridge.NativeTime=412;
        enemy.GroupInfo=new GroupInfo{EnemyLastSeenTimeSense=999};enemy.EnemyLastPosition=new Vector3(200,0,0);Tick(412);
        Check(FollowerEnemyTracking.Position(enemy).x==45,"native dispersed report wins over EFT mirror bookkeeping");
        SainGoalEnemyBridge.Native=false;
        var ordinary=new EnemyInfo{Owner=new BotOwner{Follower=false},CurrPosition=new Vector3(70,0,0)};
        Check(FollowerEnemyTracking.Position(ordinary).x==70,"ordinary bots are outside the policy");
        pitFireTeam.enemyTracking.Value=EnemyTrackingMode.Simple;
        Check(FollowerEnemyTracking.Mode==EnemyTrackingMode.Realistic,"mode is captured until next raid");
        FollowerEnemyTracking.BeginRaid();Tick(1);
        enemy.CurrPosition=new Vector3(80,0,0);owner.Memory.GoalEnemy=enemy;
        Check(FollowerEnemyTracking.Position(enemy).x==80,"Core Simple uses live accepted position");
        owner.Addon=true;
        var native=new SAIN.SAINComponent.Classes.EnemyClasses.Enemy {BotOwner=owner,EnemyInfo=enemy,LastKnownPosition=new Vector3(10,0,0),EnemyPosition=new Vector3(80,0,0)};
        native.KnownPlaces.TimeLastKnownUpdated=1;
        Check(SainEnemyTracking.Position(native).Value.x==80,"SAIN Simple uses the same live projection");
        native.EnemyPosition=new Vector3(100,0,0);Tick(2);
        Check(SainEnemyTracking.Position(native).Value.x==100,"native Simple follows silent relocation");
        Check(native.LastKnownPosition.Value.x==10&&native.KnownPlaces.TimeLastKnownUpdated==1,"Simple projection does not rewrite genuine evidence or time");
        owner.Memory.GoalEnemy=null;
        Check(SainEnemyTracking.Position(native).Value.x==10,"unadmitted native record cannot gain live tracking");
        owner.Memory.GoalEnemy=enemy;Tick(22);
        Check(!SainEnemyTracking.Simple(native),"synthetic movement cannot extend Simple lifetime");
        owner.Position=new Vector3(0,4,0);
        Check(!FollowerEnemyTracking.HasArrived(owner,new Vector3(0,0,0)),"another floor cannot count as arrival");
        owner.Position=new Vector3(0,0,0);pitTeam.Utils.Utils.Route=20;
        Check(!FollowerEnemyTracking.HasArrived(owner,new Vector3(1,0,0)),"nearby point behind a long wall route is not reached");
        pitTeam.Utils.Utils.Route=1;
        Check(FollowerEnemyTracking.HasArrived(owner,new Vector3(1,0,0)),"short complete route permits arrival");
        FollowerEnemyTracking.EndRaid();
        Check(FollowerEnemyTracking.Snapshot(owner,enemy)==null,"raid teardown releases tracking state");
        Console.WriteLine($"Enemy Tracking: {count} behavior checks passed.");
    }
}
