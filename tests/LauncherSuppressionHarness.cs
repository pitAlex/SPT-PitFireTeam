#nullable disable
#pragma warning disable CS0649, CS0414, CS0169, CS8632
using System;
using System.Collections.Generic;
using System.Reflection;
public struct Vector3 {
    public float x,y,z; public Vector3(float a,float b=0,float c=0){x=a;y=b;z=c;}
    public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
}
public static class Time { public static float time=100; }
public enum EquipmentSlot { FirstPrimaryWeapon,SecondPrimaryWeapon,Holster }
public enum BotLogicDecision { holdPosition,suppressFire,goToPoint }
public enum EPhraseTrigger { GetInCover }
public class CoreActionResultParams {}
public struct AICoreActionEnd { public bool Value; public string Reason; public AICoreActionEnd(string reason,bool value){Reason=reason;Value=value;} }
public struct AICoreActionResult<T,D> { public T Action; public string Reason; public AICoreActionResult(T a,string r){Action=a;Reason=r;} }
public class CustomNavigationPoint { public Vector3 Position; }
public class Weapon { public int Rounds=1,Capacity=1; public bool Launcher=true,SingleUse; }
public class Transform { public Vector3 position; }
public class EnemyInfo { public bool Alive=true,IsVisible=true,CanShoot=true; public float Distance=80; public Vector3 Point=new Vector3(80); public Person Person=new Person(); }
public class Person { public AIData AIData=new AIData(); } public class AIData { public bool HaveHelmet; }
public class Memory { public EnemyInfo GoalEnemy=new EnemyInfo(); public bool HaveEnemy=>GoalEnemy!=null; }
public class BotReload { public bool Reloading; }
public class ShootController { public Weapon Item; }
public class BotWeaponInfo { public int BulletCount=10; }
public class BotUnderbarrelLauncherController { public bool Eligible,Enabled,Reloaded; public bool CanUseInsteadOfReload()=>Eligible; public void TryEnable(object c){Enabled=true;} public void TryEnableReloadDisable(object c){Reloaded=true;} }
public class BotWeaponManager {
    public bool IsWeaponReady=true; public BotWeaponSelector Selector; public BotReload Reload=new BotReload(); public ShootController ShootController=new ShootController();
    public Weapon CurrentWeapon; public BotUnderbarrelLauncherController UnderbarrelLauncherController=new BotUnderbarrelLauncherController(); public BotWeaponInfo PistolWeaponInfo=new BotWeaponInfo(),SecondWeaponInfo=new BotWeaponInfo();
}
public class Talk { public void TrySay(EPhraseTrigger t,bool force){} }
public class Steering { public void LookToPoint(Vector3 p){} }
public class Settings { public Settings FileSettings=>this; public Settings Shoot=>this; public float LOW_DIST_TO_CHANGE_WEAPON=5,FAR_DIST_TO_CHANGE_WEAPON=100,CHANCE_TO_CHANGE_WEAPON_WITH_HELMET=100,CHANCE_TO_CHANGE_WEAPON=100; }
public class BotOwner {
    public bool Follower=true,ArcSafe=true,ImpactSafe=true,CanPlan=true; public Weapon Launcher=new Weapon(); public Memory Memory=new Memory(); public Vector3 Position;
    public Transform WeaponRoot=new Transform(); public Steering Steering=new Steering(); public Talk BotTalk=new Talk(); public Settings Settings=new Settings();
    public BotWeaponManager WeaponManager=new BotWeaponManager(); public Suppression SuppressShoot=new Suppression(); public Profile Profile=new Profile(); public string name="Brick";
    public BotOwner(){WeaponManager.Selector=new BotWeaponSelector{_owner=this};WeaponManager.CurrentWeapon=new Weapon{Launcher=false};WeaponManager.ShootController.Item=WeaponManager.CurrentWeapon;}
    public void FinishDraw(){WeaponManager.CurrentWeapon=Launcher;WeaponManager.ShootController.Item=Launcher;WeaponManager.IsWeaponReady=true;WeaponManager.Selector.IsWeaponReady=true;WeaponManager.Selector._lastEquipmentSlot=EquipmentSlot.SecondPrimaryWeapon;}
}
public class Profile {public string Nickname="Brick";}
public class Suppression { public Vector3 Point; public int Inits; public bool InitToPoint(Vector3 p,CustomNavigationPoint c){Point=p;Inits++;return true;} }
public static class BattleRecorder { public static void RecordGrenadeEvent(BotOwner b,string e,string r,EnemyInfo goalEnemy=null,Vector3? target=null,Vector3? suppressFrom=null){} }
public static class BossPlayers { public static bool IsFollower(BotOwner b)=>b?.Follower==true; }
public static class MyExtensions { public static bool IsTrue100(float p)=>true; }
public class BotWeaponSelector {
    public BotOwner _owner; public bool _canChangeToSupportWeapons=true,IsWeaponReady=true,IsChanging,SecondCapability=true,RejectDraw;
    public EquipmentSlot _supportWeapon=EquipmentSlot.SecondPrimaryWeapon,_mainWeapon=EquipmentSlot.FirstPrimaryWeapon,_lastEquipmentSlot=EquipmentSlot.FirstPrimaryWeapon;
    public EquipmentSlot LastEquipmentSlot=>_lastEquipmentSlot; public Weapon SecondPrimaryWeaponItem=>_owner.Launcher; public float _prevChangeWeaponTime;
    public int DrawRequests,SupportRequests,MainRequests; public bool CanChangeWeaponCauseEnemyDistance()=>true;
    public bool ChangeToSupport(){if(!_canChangeToSupportWeapons)return false;SupportRequests++;return true;}
    public bool ChangeToMain(){MainRequests++;return true;}
__NATIVE_RELOAD_METHODS__
}
public static class FollowerWeaponSwitchPolicyRuntime {
    public static BotOwner GetSelectorBotOwner(BotWeaponSelector s)=>s._owner;
__RELOAD_HELPERS__
}
public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); protected static Log Logger=new Log(); }
public class Log {public void LogInfo(string s){}}
public class PatchPrefix:Attribute{} public class PatchFinalizer:Attribute{}
public static class AccessTools {public static MethodInfo Method(Type t,string n)=>t.GetMethod(n);}
public static class pitFireTeam {public static bool IsDebugBuild=>false;}
__RELOAD_PATCHES__
public class FollowerCombatCommon {
    public BotOwner botOwner; public int Plans; public bool AtPosition=true; public FollowerCombatCommon(BotOwner b){botOwner=b;}
    const string GrenadeLauncherSuppressReasonToken=".launcher";
    const float GrenadeLauncherSuppressReloadWaitSeconds=6,OrderedWeaponSuppressMaxSeconds=9,GrenadeLauncherSuppressMinCommitmentSeconds=4.5f;
    public const float SupportWeaponPrepareTimeoutSeconds=3,GrenadeLauncherPrepareTimeoutSeconds=9;
    private GrenadeLauncherFirePlan activeLauncherSuppressPlan;
    private int activeLauncherSuppressTargetIndex,activeLauncherSuppressInitialRounds=-1,activeLauncherSuppressLastRounds=-1,activeLauncherSuppressCapacity=1;
    private bool ownsGrenadeLauncherSwitch,activeLauncherSuppressMultiShot,activeLauncherSuppressShotDetected,activeLauncherSuppressReloadRequested,activeLauncherSuppressCommitmentExpiredRecorded;
    private float activeLauncherSuppressFirstShotAt,activeLauncherSuppressReloadStartedAt,nextLauncherSuppressReloadRequestAt;
    private string activeFollowerSuppressReason="objectiveGrenadier.auto.launcher";
    public static bool IsGrenadeLauncherWeapon(Weapon w)=>w?.Launcher==true;
    public static bool ShouldSuppressFollowerOwnedReloadFallback(BotOwner b)=>false;
    public static bool IsSingleUseLauncherWeapon(Weapon w)=>w?.SingleUse==true;
    public static bool IsSameWeapon(Weapon a,Weapon b)=>ReferenceEquals(a,b);
    public bool HasActiveCombatEnemy(EnemyInfo e)=>e?.Alive==true;
    public static bool HasUsableEquippedGrenadeLauncher(BotOwner b)=>b.Launcher!=null;
    public static Weapon GetEquippedGrenadeLauncher(BotOwner b,out EquipmentSlot slot){slot=EquipmentSlot.SecondPrimaryWeapon;return b.Launcher;}
    public static Weapon GetActiveOrEquippedGrenadeLauncher(BotOwner b)=>b.Launcher;
    public Weapon GetActiveOrEquippedGrenadeLauncher()=>botOwner.Launcher;
    public static int CountLoadedRounds(Weapon w)=>w?.Rounds??0;
    public static int GetLoadedCapacity(Weapon w,int count)=>w.Capacity;
    public static bool IsFinite(Vector3 v)=>!float.IsNaN(v.x);
    public void HoldFor(float t){} public void ClearLauncherSuppressReloadTracking(){}
    public static bool TryStartActiveGrenadeLauncherLooseAmmoReload(BotOwner b,Weapon w,out string reason){reason="";b.WeaponManager.Reload.Reloading=true;return true;}
    public static bool TrySelectEquippedGrenadeLauncher(BotOwner b,out bool changed,out EquipmentSlot slot){
        var s=b.WeaponManager.Selector;changed=false;slot=EquipmentSlot.SecondPrimaryWeapon;
        if(s.LastEquipmentSlot==slot)return true;
        if(!s.IsWeaponReady||s.IsChanging||s.RejectDraw)return false;
        s.DrawRequests++;s.IsWeaponReady=false;b.WeaponManager.IsWeaponReady=false;changed=true;return true;
    }
    private AICoreActionResult<BotLogicDecision,CoreActionResultParams> CreateLauncherPreparationHold(string r)=>new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,r);
    public bool TryPrepareGrenadeLauncherFirePlan(EnemyInfo e,string reason,bool ordered,out GrenadeLauncherFirePlan plan){
        Plans++; plan=botOwner.CanPlan?new GrenadeLauncherFirePlan(reason,ordered,18,new List<Vector3>{e.Point,new Vector3(e.Point.x+10)},null):null;return plan!=null;
    }
    public bool IsAtGrenadeLauncherFirePosition(GrenadeLauncherFirePlan p)=>AtPosition;
    public AICoreActionResult<BotLogicDecision,CoreActionResultParams> CreateGrenadeLauncherMoveDecision(GrenadeLauncherFirePlan p)=>new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.goToPoint,"move");
    private bool RejectGrenadeLauncherSuppress(string r,EnemyInfo e)=>false;
    private bool TryCanFireGrenadeLauncherAtTarget(Vector3 origin,Vector3 target,float radius,out string reason){reason="arc";return botOwner.ArcSafe;}
    private static bool TryValidateGrenadeLauncherSuppressTarget(BotOwner b,Vector3 target,bool ordered,out string reason){reason="impact";return b.ImpactSafe;}
    private void WarnGrenadeLauncherImpacts(List<Vector3> p){}
    private void TryEmitGrenadeLauncherSuppressEvent(EnemyInfo e,Vector3 p,string r){}
    private bool TryKeepEmptyLauncherSuppressReloading(Weapon w,out string reason){reason="";return false;}
    public static bool IsGrenadeLauncherSuppressReason(string r)=>r.Contains(".launcher");
    public bool HasFiredGrenadeLauncherSuppressShot=>activeLauncherSuppressShotDetected;
    public string HolsterFallback; public AICoreActionEnd FireEnd;
    public AICoreActionEnd EndSuppressFire(string reason)=>FireEnd;
    public void PrepareLauncherSuppressWeaponFallback(){}
    public void RequestFirstPrimaryLauncherHolsterFallback(string reason){HolsterFallback=reason;}
    public void BeginBurst(){StartLauncherSuppressFireProfile(activeFollowerSuppressReason);}
    public bool EndBurst(out string r)=>TryGetLauncherSuppressFireEndReason(false,0,out r);
__COMMON_METHODS__
__PLAN__
}
public class Objective {
    public BotOwner BotOwner; public FollowerCombatCommon CombatCommon; public string Failure; public bool ordered,launcherReady;
    const float PreparationProbeSeconds=.15f,OpportunityWindowSeconds=5;
    private float activeUntil,launcherPreparationUntil,nextPreparationProbeAt; private FollowerCombatCommon.GrenadeLauncherFirePlan launcherPlan;
    public Objective(BotOwner b){BotOwner=b;CombatCommon=new FollowerCombatCommon(b);}
    private bool TryGetEmergencyDecision(EnemyInfo e,out AICoreActionResult<BotLogicDecision,CoreActionResultParams>d){d=default;return false;}
    private AICoreActionResult<BotLogicDecision,CoreActionResultParams> FinishForEmergency(AICoreActionResult<BotLogicDecision,CoreActionResultParams>d)=>d;
    private AICoreActionResult<BotLogicDecision,CoreActionResultParams> FailObjective(string r){Failure=r;return new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,r);}
    private AICoreActionResult<BotLogicDecision,CoreActionResultParams> RetryOrFail(string r)=>new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(BotLogicDecision.holdPosition,"retry");
    private bool complete; public string Cooldown;
    private void RecordAttemptCooldown(string reason){Cooldown=reason;}
    private void ClearObjectiveCommitments(){}
    public AICoreActionEnd EndFire()=>EndLauncherFire("objectiveGrenadier.auto.launcher");
    private string GetModeReasonPrefix()=>ordered?"objectiveGrenadier.ordered":"objectiveGrenadier.auto";
__OBJECTIVE_METHODS__
}
public static class LauncherChecks {
    static int checks; static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    static bool Reload(BotWeaponSelector s,bool probe){
        var type=probe?typeof(FollowerLauncherNoAmmoSwitchPatch):typeof(FollowerCombatReloadFallbackSuppressPatch);
        var flags=BindingFlags.NonPublic|BindingFlags.Static; object[] args={s,false};
        object run=type.GetMethod("PatchPrefix",flags).Invoke(null,args);
        try {if(run is bool allowed&&!allowed)return false;if(probe)return s.ShallChangeIfNoAmmo(s._owner.Memory.GoalEnemy);s.TrySwitchToLauncherOrChangeWeapon();return false;}
        finally {type.GetMethod("PatchFinalizer",flags).Invoke(null,new object[]{s,args[1]});}
    }
    static Objective Fresh(){Time.time=100;return new Objective(new BotOwner());}
    public static int Run(){
        var o=Fresh();var e=o.BotOwner.Memory.GoalEnemy;var d=o.GetDecision(e);
        Check(o.CombatCommon.Plans==1&&o.BotOwner.WeaponManager.Selector.DrawRequests==1&&d.Action==BotLogicDecision.holdPosition,"plan before draw");
        e.IsVisible=e.CanShoot=false;e.Point=new Vector3(500);Time.time+=.15f;o.GetDecision(e);
        Check(o.Failure==null&&o.CombatCommon.Plans==1&&o.BotOwner.WeaponManager.Selector.DrawRequests==1,"0.15s callback wait preserves plan");
        Time.time+=1.75f;o.BotOwner.FinishDraw();d=o.GetDecision(e);
        Check(d.Action==BotLogicDecision.suppressFire&&o.BotOwner.SuppressShoot.Point.x==80&&o.CombatCommon.Plans==1,"hidden enemy fires original point at 1.9s");
        o.CombatCommon.BeginBurst();Check(!o.CombatCommon.EndBurst(out _),"trigger alone does not complete shot");
        o.BotOwner.Launcher.Rounds=0;Check(o.CombatCommon.EndBurst(out var end)&&end=="launcherSingleShotFired","actual single discharge completes burst");
        o=Fresh();o.GetDecision(o.BotOwner.Memory.GoalEnemy);Time.time+=3.01f;o.GetDecision(o.BotOwner.Memory.GoalEnemy);Check(o.Failure=="launcherPreparationTimedOut","draw timeout bounded");
        o=Fresh();o.BotOwner.CanPlan=false;o.GetDecision(o.BotOwner.Memory.GoalEnemy);Check(o.Failure=="noSuppressionPlan"&&o.BotOwner.WeaponManager.Selector.DrawRequests==0,"no safe plan does not draw");
        o=Fresh();o.BotOwner.WeaponManager.Selector.RejectDraw=true;o.GetDecision(o.BotOwner.Memory.GoalEnemy);Check(o.Failure=="weaponSwitchFailed","real request rejection remains failure");
        o=Fresh();e=o.BotOwner.Memory.GoalEnemy;o.GetDecision(e);o.BotOwner.FinishDraw();o.BotOwner.ImpactSafe=false;d=o.GetDecision(e);Check(d.Action==BotLogicDecision.holdPosition&&o.BotOwner.SuppressShoot.Inits==0,"new friendly risk rejects launch");
        e.IsVisible=false;e.Point=new Vector3(300);o.BotOwner.ImpactSafe=true;d=o.GetDecision(e);Check(d.Action==BotLogicDecision.suppressFire&&o.BotOwner.SuppressShoot.Point.x==80,"safe retry keeps original plan");
        o=Fresh();o.GetDecision(o.BotOwner.Memory.GoalEnemy);o.BotOwner.FinishDraw();o.BotOwner.ArcSafe=false;d=o.GetDecision(o.BotOwner.Memory.GoalEnemy);Check(d.Action==BotLogicDecision.holdPosition&&o.BotOwner.SuppressShoot.Inits==0,"new arc obstruction rejects launch");
        o=Fresh();o.BotOwner.Launcher.Rounds=o.BotOwner.Launcher.Capacity=4;e=o.BotOwner.Memory.GoalEnemy;o.GetDecision(e);o.BotOwner.FinishDraw();o.GetDecision(e);o.CombatCommon.BeginBurst();o.CombatCommon.EndBurst(out _);
        Check(o.BotOwner.SuppressShoot.Point.x==80,"no discharge keeps point");o.BotOwner.Launcher.Rounds=3;o.CombatCommon.EndBurst(out _);Check(o.BotOwner.SuppressShoot.Point.x==90,"discharge advances point");
        o.BotOwner.Launcher.Rounds=2;o.CombatCommon.EndBurst(out _);Check(o.BotOwner.SuppressShoot.Point.x==80,"cylinder cycles retained points");
        Time.time+=10;Check(o.CombatCommon.EndBurst(out end)&&end=="launcherMultiShotTimedOut","post-shot burst bounded");
        Check(o.CombatCommon.IsGrenadeLauncherSuppressCommitmentExpired("objectiveGrenadier.auto.launcher",5)==false,"observed shots leave no-shot timeout");
        o=Fresh();Check(o.CombatCommon.IsGrenadeLauncherSuppressCommitmentExpired("objectiveGrenadier.auto.launcher",4.49f)==false&&o.CombatCommon.IsGrenadeLauncherSuppressCommitmentExpired("objectiveGrenadier.auto.launcher",4.5f),"no-shot timeout exact boundary");
        var b=new BotOwner();var s=b.WeaponManager.Selector;Check(!Reload(s,true)&&s.SupportRequests==0,"vanilla no-ammo probe cannot select launcher");Reload(s,false);Check(s.SupportRequests==0&&s._canChangeToSupportWeapons&&s.SecondCapability,"rejected reload excludes launcher and restores capabilities");
        b.Launcher.Launcher=false;Check(Reload(s,true)&&s.SupportRequests==1,"ordinary secondary retains vanilla switch");
        b=new BotOwner{Follower=false};s=b.WeaponManager.Selector;Check(Reload(s,true)&&s.SupportRequests==1,"non-follower untouched");
        b=new BotOwner();s=b.WeaponManager.Selector;s._supportWeapon=EquipmentSlot.Holster;Check(Reload(s,true)&&s.SupportRequests==1,"vanilla holster support untouched");
        b=new BotOwner();s=b.WeaponManager.Selector;b.WeaponManager.UnderbarrelLauncherController.Eligible=true;Reload(s,false);Check(b.WeaponManager.UnderbarrelLauncherController.Enabled,"underbarrel fallback untouched");
        bool outer=FollowerWeaponSwitchPolicyRuntime.ExcludeLauncherFromReloadSupport(s);bool inner=FollowerWeaponSwitchPolicyRuntime.ExcludeLauncherFromReloadSupport(s);FollowerWeaponSwitchPolicyRuntime.RestoreReloadSupport(s,inner);Check(!s._canChangeToSupportWeapons,"nested masking stays excluded");FollowerWeaponSwitchPolicyRuntime.RestoreReloadSupport(s,outer);Check(s._canChangeToSupportWeapons,"outer restore restores support");
        o=Fresh();o.CombatCommon.FireEnd=new AICoreActionEnd("launcherCommitmentExpired",true);o.EndFire();
        Check(o.Cooldown=="fail.launcherCommitmentExpired"&&o.CombatCommon.HolsterFallback=="grenadierFail.launcherCommitmentExpired","no-shot completion uses failure cooldown and holster fallback");
        o=Fresh();o.CombatCommon.BeginBurst();o.BotOwner.Launcher.Rounds=0;o.CombatCommon.EndBurst(out end);o.CombatCommon.FireEnd=new AICoreActionEnd(end,true);o.EndFire();
        Check(o.Cooldown=="launcherSingleShotFired"&&o.CombatCommon.HolsterFallback==null,"discharged reusable launcher is successful completion");
        b=new BotOwner();s=b.WeaponManager.Selector;Reload(s,false);Check(FollowerCombatCommon.TrySelectEquippedGrenadeLauncher(b,out _,out _),"explicit launcher draw still available after reload exclusion");
        b=new BotOwner();s=b.WeaponManager.Selector;s._canChangeToSupportWeapons=false;Reload(s,false);Check(!s._canChangeToSupportWeapons,"original disabled support flag stays disabled");
        var points=new List<Vector3>{new Vector3(80)};var plan=new FollowerCombatCommon.GrenadeLauncherFirePlan("auto",false,18,points,null);points[0]=new Vector3(999);Check(plan.FirstTarget.x==80,"plan owns independent point snapshot");
        return checks;
    }
}
