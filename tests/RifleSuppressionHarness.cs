// Compiles the production target policy and extracted selection/dispatch/recovery methods.
// EFT/Unity geometry and the custom firing body are test doubles; dispatch records their input.
#nullable enable annotations
#nullable disable warnings
using System;
using EFT;
using UnityEngine;
using pitTeam.BigBrain;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b) => new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float f) => new Vector3(a.x*f,a.y*f,a.z*f);
    }
    public static class Time { public static float time; }
    public static class Mathf { public static float Max(float a,float b) => Math.Max(a,b); }
}
public class BotGroupEnemyInfo { public float EnemyLastSeenTimeReal, EnemyLastSeenTimeSense; }
public class EnemyInfo {
    public bool IsVisible,CanShoot;
    public Vector3 PersonalLastPos,EnemyLastPositionReal,EnemyLastPosition,BodyPoint;
    public float PersonalLastSeenTime;
    public BotGroupEnemyInfo? GroupInfo = new BotGroupEnemyInfo();
    public Person Person = new Person();
    public Vector3 GetBodyPartPosition() => BodyPoint;
}
public class Person { public Health HealthController = new Health(); }
public class Health { public bool IsAlive = true; }
public class CustomNavigationPoint { }
public class SuppressController {
    public Vector3? Point;
    public CustomNavigationPoint? PointToSuppressFrom;
    public int InitCalls;
    public Vector3? GetPoint() => Point;
}
namespace EFT {
    public class BotOwner {
        public static Vector3 STAY_HEIGHT = new Vector3(0,1.2f,0);
        public Memory Memory = new Memory();
        public SuppressController SuppressShoot = new SuppressController();
        public Vector3 Position;
        public Transform? WeaponRoot;
        public Steering Steering = new Steering();
        public ShootData ShootData = new ShootData();
    }
    public class Memory { public EnemyInfo? GoalEnemy; }
    public class Transform { public Vector3 position; }
    public class Steering { public void LookToPoint(Vector3 point) {} }
    public class ShootData { public int Shots; public void Shoot() { Shots++; } }
}
public enum BotLogicDecision { suppressFire,holdPosition }
public class CoreActionResultParams {}
public class AimingResultParams : CoreActionResultParams { public AimingResultParams(Vector3? p) { PointToShoot=p; } public Vector3? PointToShoot; }
public struct AICoreActionResult<T,P> {
    public T Action; public string Reason;
    public AICoreActionResult(T a,string r) { Action=a; Reason=r; }
}
public struct AICoreActionEnd { public string Reason; public bool Value; public AICoreActionEnd(string r,bool v) {Reason=r;Value=v;} }
public class CustomLayer { public class ActionData { public string? Reason; } }
public class ShootSuppressNode { public int Updates; public Vector3? Target; public void UpdateNodeByBrain(AimingResultParams p) { Updates++; Target=p.PointToShoot; } }
namespace pitTeam.Components {
    public enum FollowerCommandType { SuppressEnemy,PushEnemy }
    public class BotFollowerPlayer {
        public bool TryPeekActiveCommand(out FollowerCommandType c,out int a,out int b) { c=FollowerCommandType.PushEnemy;a=b=0;return false; }
    }
    public class BossPlayers {
        public static BossPlayers? Instance;
        public BotFollowerPlayer? GetFollower(BotOwner b) => null;
    }
}
namespace pitTeam.Modules {
    public static class BattleRecorder {
        public static void RecordGrenadeEvent(BotOwner b,string eventName,string reason,EnemyInfo goalEnemy) {}
    }
}
namespace pitTeam.BigBrain {
    public class FollowerCombatCommon {
        public BotOwner botOwner = new BotOwner();
        public bool AllowLane=true;
        public bool PendingPrimaryFallback;
        public AICoreActionEnd SuppressEnd;
        public Vector3 SelectedPoint;
        private const string GrenadeLauncherSuppressReasonToken = ".grenadeLauncher";
        public static bool IsFinite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        public static AICoreActionEnd Continue() => new AICoreActionEnd("continue",false);
        public bool HasActiveCombatEnemy(EnemyInfo? e) => e?.Person.HealthController.IsAlive == true;
        private bool TryCreateSuppressDecisionAtTarget(Vector3 point,string prefix,out AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision,bool allowObstructedSuppression) {
            SelectedPoint=point;
            decision=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.suppressFire,prefix+".move");
            return AllowLane;
        }
        private void RequestLauncherPrimaryFallback(string reason) {}
        private bool HasPendingLauncherPrimaryFallback() => PendingPrimaryFallback;
        public bool HasImmediateExplosiveDanger() => false;
        public bool HasActiveOrPendingHealWork() => false;
        public bool IsDogFightActive() => false;
        public AICoreActionEnd EndSuppressFire(string? reason) => SuppressEnd;
        public void HoldFor(float seconds) {}
__COMMON__
    }
    public static class FollowerCombatSuppressionObjective { public static bool IsSuppressionObjectiveReason(string? reason) => reason?.StartsWith("objectiveSuppress.") == true; }
    public static class FollowerCombatGrenadierObjective {
        public static bool IsGrenadierReason(string? reason) => reason?.StartsWith("objectiveGrenadier.") == true;
        public static bool IsAutonomousGrenadierReason(string? reason) => false;
    }
    public static class FollowerImmediateFirePolicy { public static bool CanUseRecentContactSuppress(EnemyInfo e) => !e.IsVisible && e.PersonalLastSeenTime>0 && Time.time-e.PersonalLastSeenTime<=2; }
    public class FollowerCombatOrderedPushObjective {
        private const string PressureRecoveryReasonPrefix = "objectivePush.pressureRecovery";
        public FollowerCombatCommon CombatCommon = new FollowerCombatCommon();
        private float pressureRecoveryUntil=103;
        private bool IsPressureRecoveryActive => Time.time < pressureRecoveryUntil;
        private void ClearPressureRecovery(string reason) { pressureRecoveryUntil=0; }
        private AICoreActionResult<BotLogicDecision,CoreActionResultParams> Hold(string reason) => new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"objectivePush."+reason);
        public AICoreActionEnd End(string reason,EnemyInfo e) => EndPressureRecovery(new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.suppressFire,reason),e);
        public AICoreActionResult<BotLogicDecision,CoreActionResultParams> Select(EnemyInfo e) { TryCreatePressureRecoveryFallback(e,out var result); return result; }
__OBJECTIVE__
    }
}
namespace pitTeam.BigBrain.Actions {
    public static class FollowerShotSafety {
        public static bool IsFriendlyInSuppressionLane(BotOwner b,Vector3 point) => false;
        public static bool IsFriendlyInSuppressionLane(BotOwner b,Vector3 origin,Vector3 point) => false;
    }
    public class CombatSuppressFireAction {
        public BotOwner BotOwner = new BotOwner();
        public ShootSuppressNode baseLogic = new ShootSuppressNode();
        public int Stops,CustomUpdates,ResetCount;
        public Vector3? CustomTarget;
        private void StopCombatShooting() { Stops++; }
        private void EnforceCloseThreatStandingPose(string s,string? r,EnemyInfo e) {}
        private bool StopUnownedGrenadeLauncherFire(string? reason,EnemyInfo e) => false;
        private string? ResolveSuppressReason(CustomLayer.ActionData data) => data.Reason;
        private void ResetMovingSuppressLane() { ResetCount++; }
        private void RecordWeaponSuppressState(string? reason,string state,Vector3? target) {}
        private bool CanSuppressFromCurrentPosition(Vector3 origin,Vector3 target) => true;
        private bool IsCurrentSuppressionAimUnsafe(Vector3 origin,Vector3 target) => false;
        private bool ShouldHoldSuppressFireUntilAimed(Vector3 origin,Vector3 target) => false;
        private void UpdateFollowerSuppress(string? reason,Vector3? target) { CustomUpdates++; CustomTarget=target; }
__ACTION__
    }
}
__POLICY__
public static class RifleSuppressionChecks {
    private static int count;
    private static void Check(bool ok,string name) { if(!ok)throw new Exception(name);count++; }
    private static bool Same(Vector3 a,Vector3 b) => Math.Abs(a.x-b.x)<0.001 && Math.Abs(a.y-b.y)<0.001 && Math.Abs(a.z-b.z)<0.001;
    private static EnemyInfo Enemy() => new EnemyInfo { PersonalLastPos=new Vector3(1,0,2), EnemyLastPosition=new Vector3(10,0,20), EnemyLastPositionReal=new Vector3(30,0,40), BodyPoint=new Vector3(50,1.5f,60) };
    public static int Run() {
        Time.time=100;
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(null,out _),"missing enemy");
        var e=Enemy();
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"zero timestamps are not contact");
        e.PersonalLastSeenTime=100-45.58f;e.GroupInfo!.EnemyLastSeenTimeSense=100-32.22f;
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"Nux stale visual and sensed reports rejected");
        foreach(float invalid in new[]{-1f,0f,97.999f,100.001f,float.NaN,float.PositiveInfinity}) {
            e=Enemy();e.GroupInfo!.EnemyLastSeenTimeSense=invalid;
            Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"invalid or expired report "+invalid);
        }
        e=Enemy();e.GroupInfo!.EnemyLastSeenTimeSense=98;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out var target) && Same(target,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"exact two-second sensed boundary accepted");
        e.GroupInfo.EnemyLastSeenTimeReal=99;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out target) && Same(target,e.EnemyLastPositionReal+BotOwner.STAY_HEIGHT),"newer visual report replaces sensed point");
        e.GroupInfo.EnemyLastSeenTimeSense=99.5f;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out target) && Same(target,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"newer sensed report replaces visual point");
        e.PersonalLastSeenTime=99.75f;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out target) && Same(target,e.PersonalLastPos+BotOwner.STAY_HEIGHT),"newest personal position keeps its own timestamp");
        e.GroupInfo=null;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out target),"personal report without group");
        e=Enemy();e.IsVisible=true;
        Check(FollowerSuppressTargetPolicy.TryGetTarget(e,out target) && Same(target,e.BodyPoint),"current visible body beats old memory");
        e.BodyPoint=new Vector3(float.NaN,0,0);
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"invalid current target rejected");
        e=Enemy();e.GroupInfo!.EnemyLastSeenTimeSense=100;e.EnemyLastPosition=new Vector3(float.PositiveInfinity,0,0);
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"invalid remembered coordinates rejected");
        e.EnemyLastPosition=Vector3.zero;
        Check(!FollowerSuppressTargetPolicy.TryGetTarget(e,out _),"uninitialized remembered point rejected");

        string[] reasons={"objectivePush.pressureRecoverySuppress.softObstructedPlace","objectivePush.pressureRecoverySuppress.move","objectivePush.recoveryNoCoverSuppress.place"};
        foreach(var reason in reasons) {
            Check(FollowerCombatCommon.IsFollowerSuppressReason(reason),"ordered push recognized: "+reason);
            var action=new CombatSuppressFireAction();e=Enemy();e.GroupInfo!.EnemyLastSeenTimeSense=100;action.BotOwner.Memory.GoalEnemy=e;
            action.BotOwner.SuppressShoot.Point=new Vector3(-100,0,-100);
            action.BotOwner.SuppressShoot.PointToSuppressFrom=new CustomNavigationPoint();
            var data=new CustomLayer.ActionData {Reason=reason};action.Update(data);
            Check(action.CustomUpdates==1 && action.baseLogic.Updates==0,"ordered push uses custom action without active command");
            Check(Same(action.CustomTarget!.Value,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"action receives fresh point instead of stale controller point");
            Check(action.BotOwner.SuppressShoot.PointToSuppressFrom!=null && action.BotOwner.SuppressShoot.InitCalls==0,"target lookup preserves prepared movement and controller lifetime");
            Time.time=101;e.GroupInfo.EnemyLastSeenTimeSense=101;e.EnemyLastPosition=new Vector3(15,0,25);action.Update(data);
            Check(Same(action.CustomTarget!.Value,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"fresh report retargets subsequent firing update");
            Time.time=103.001f;action.Update(data);
            Check(action.CustomUpdates==2 && action.Stops==1 && action.ResetCount==1,"expiry stops firing before next action update");
            Time.time=100;
        }
        Check(!FollowerCombatCommon.IsFollowerSuppressReason("objectivePush.pressureRecoveryThreatHold"),"hold is not suppression");
        var launcher=new CombatSuppressFireAction();launcher.BotOwner.Memory.GoalEnemy=Enemy();launcher.BotOwner.SuppressShoot.Point=new Vector3(90,0,90);
        launcher.Update(new CustomLayer.ActionData {Reason="objectiveGrenadier.grenadeLauncher"});
        Check(launcher.CustomUpdates==1 && Same(launcher.CustomTarget!.Value,new Vector3(90,0,90)),"launcher retains committed point despite stale rifle memory");
        var fallback=new CombatSuppressFireAction();e=Enemy();e.GroupInfo!.EnemyLastSeenTimeSense=100;fallback.BotOwner.Memory.GoalEnemy=e;
        fallback.Update(new CustomLayer.ActionData {Reason="otherSuppress"});
        Check(fallback.baseLogic.Updates==1 && Same(fallback.baseLogic.Target!.Value,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"native fallback receives explicitly validated point");
        Time.time=102.001f;fallback.Update(new CustomLayer.ActionData {Reason="otherSuppress"});
        Check(fallback.baseLogic.Updates==1 && fallback.Stops==1,"native fallback stops on stale contact");Time.time=100;
        var common=new FollowerCombatCommon();e=Enemy();
        Check(!common.TryCreateSuppressDecision(e,reasons[0],out _),"selection rejects stale report before lane planning");
        Check(!common.TryCreateOrderedSuppressWeaponFallbackDecision(e,"objectiveSuppress",out _),"rifle fallback cannot reuse old launcher impact point");
        e.GroupInfo!.EnemyLastSeenTimeSense=100;
        Check(common.TryCreateOrderedSuppressWeaponFallbackDecision(e,"objectiveSuppress",out _) && Same(common.SelectedPoint,e.EnemyLastPosition+BotOwner.STAY_HEIGHT),"rifle fallback uses fresh report");
        var objective=new FollowerCombatOrderedPushObjective();e=Enemy();
        Check(objective.Select(e).Action==BotLogicDecision.holdPosition,"stale push recovery reassesses into threat hold");
        e.GroupInfo!.EnemyLastSeenTimeSense=100;
        Check(objective.Select(e).Action==BotLogicDecision.suppressFire,"fresh push recovery can select suppression");
        foreach(var endReason in new[]{"followerSuppressContactExpired","followerSuppressHardBlockedLane","followerSuppressBlockedLane","followerSuppressComplete"}) {
            objective.CombatCommon.SuppressEnd=new AICoreActionEnd(endReason,true);
            var end=objective.End(reasons[0],e);
            Check(end.Value && end.Reason==endReason,"recovery honors shared end: "+endReason);
        }
        objective.CombatCommon.SuppressEnd=FollowerCombatCommon.Continue();
        Check(!objective.End(reasons[0],e).Value,"valid burst continues during recovery");
        Time.time=103;
        Check(objective.End(reasons[0],e).Reason=="orderedPressureRecoveryComplete","recovery keeps original deadline");
        return count;
    }
}
