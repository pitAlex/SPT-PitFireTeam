using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Patches;
using pitTeam.Utils;
using UnityEngine;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;

public static class FireSafetyChecks
{
    private static int count;
    public static Type ResolveType(string name) => typeof(FireSafetyChecks).Assembly.GetType(name, true);
    private static void Check(bool ok, string text)
    { if (!ok) throw new Exception(text); count++; Console.WriteLine("PASS " + text); }
    private static BotOwner Spawn(string id, bool follower = true)
    {
        var owner = new BotOwner { ProfileId = id, IsFollower = follower };
        var player = new Player { ProfileId = id, AIData = new AIData { BotOwner = owner } };
        owner.GetPlayer = player; owner.AIData = player.AIData; player.AIData.Player = player;
        owner.BotFollower = new BotFollower { BossToFollow = new pitAIBossPlayer {
            realPlayer = new Player { ProfileId = "human", Transform = new Transform { position = new Vector3(-5, 0, 0) } } } };
        owner.ShootData = new ShootData(owner);
        var firearm = new Player.FirearmController(player);
        player.HandsController = firearm; owner.WeaponManager.ShootController = firearm;
        owner.Sain = new BotComponent(owner); SAIN.SAINEnableClass.Bots[id] = owner.Sain;
        return owner;
    }
    private static Player.FirearmController Gun(BotOwner owner) => (Player.FirearmController)owner.GetPlayer.HandsController;
    private static Player Leader(BotOwner owner) => owner.BotFollower.BossToFollow.realPlayer;
    private static void Clear(BotOwner owner)
    { Leader(owner).Transform.position = new Vector3(-5, 0, 0); Gun(owner).AdjustDirection = Vector3.zero; owner.GetPlayer.LocalShotDirection = new Vector3(1,0,0); owner.ShootData.EndShoot(); }
    private static bool TryManual(BotOwner owner, Vector3 target, EShootReason reason = EShootReason.Suppress) =>
        owner.Sain.ManualShoot.TryShoot(new Enemy(), target, false, reason);
    [HarmonyPrefix]
    private static bool SainReplacement(ShootData __instance, ref bool __result)
    {
        __instance.ReplacementCalls++; __instance.Shooting = true;
        __instance.ShootController.SetTriggerPressed(true); __result = true; return false;
    }
    public static void Main()
    {
        // Install the competing SAIN-style prefix first to prove priority rather than registration order.
        var replacement = new Harmony("SAIN.BotShootPatch.fixture");
        replacement.Patch(AccessTools.Method(typeof(ShootData), nameof(ShootData.Shoot)),
            prefix: new HarmonyMethod(typeof(FireSafetyChecks), nameof(SainReplacement)));
        new FollowerShootDataSafetyPatch().Enable(); new FollowerFirearmTriggerSafetyPatch().Enable();
        new FollowerFirearmUpdateSafetyPatch().Enable(); new FollowerAutomaticFireSafetyPatch().Enable();
        var core = new Harmony("pitTeam.fireSafety.core.fixture");
        FollowerSainFriendlyFirePatch.Apply(core); FollowerSainManualFireSafety.Apply(core);
        var owner = Spawn("saved"); var gun = Gun(owner); var point = new Vector3(20, 1, 0);
        Check(owner.ShootData.Shoot() && gun.IsTriggerPressed, "clear normal fire reaches SAIN replacement");
        owner.ShootData.EndShoot(); Leader(owner).Transform.position = new Vector3(5, 0, 0);
        int starts = owner.ShootData.ReplacementCalls;
        bool blockedResult = owner.ShootData.Shoot();
        Check(!blockedResult && !owner.ShootData.Shooting && !gun.IsTriggerPressed,
            "shared admission reconciles SAIN replacement result and held-trigger state");
        replacement.UnpatchSelf(); Clear(owner);
        Check(owner.ShootData.Shoot() && owner.ShootData.NativeCalls == 1, "clear core fire reaches native Shoot once");
        Leader(owner).Transform.position = new Vector3(5, 0, 0); starts = owner.ShootData.NativeCalls;
        Check(!owner.ShootData.Shoot() && owner.ShootData.NativeCalls == starts && !owner.ShootData.Shooting,
            "unsafe admission also releases an already-held normal trigger");
        gun.SetTriggerPressed(true);
        Check(!gun.IsTriggerPressed, "direct controller trigger call cannot bypass shared muzzle gate");
        Clear(owner); owner.ShootData.Shoot(); Leader(owner).Transform.position = new Vector3(5, 0, 0);
        int updates = gun.Updates; gun.ManualUpdate(0.1f);
        Check(!gun.IsTriggerPressed && !owner.ShootData.Shooting && gun.Updates == updates + 1,
            "ongoing normal fire stops before operation update while native update still runs");
        Clear(owner); Leader(owner).Transform.position = new Vector3(50, 0, 0);
        owner.AimingManager.CurrentAiming.EndTargetPoint = new Vector3(10, 1, 0);
        Check(!owner.ShootData.Shoot(), "muzzle lane includes allies behind aim target");
        Clear(owner); Leader(owner).Transform.position = new Vector3(0, 0, 5);
        gun.AdjustDirection = new Vector3(0,0,1);
        Check(!owner.ShootData.Shoot(), "adjusted firearm direction overrides clear look direction");
        gun.AdjustDirection = Vector3.zero; gun.IsAIShootFromEyes = () => true;
        owner.GetPlayer.LookDirection = new Vector3(0,0,1); owner.LookSensor.ShootStartPos = new Vector3(0,1,0);
        Check(!owner.ShootData.Shoot(), "eye-origin firing uses native eye direction and origin");
        gun.IsAIShootFromEyes = () => false; Clear(owner);
        gun.LauncherMode = true; gun._underbarrelContainer.Fireport.position = new Vector3(0,1,2);
        Leader(owner).Transform.position = new Vector3(5,0,2);
        Check(!owner.ShootData.Shoot(), "underbarrel mode checks launcher muzzle rather than main barrel");
        gun.LauncherMode = false; Clear(owner);
        owner.GetPlayer.LocalShotDirection = new Vector3(1,1,0); Leader(owner).Transform.position = new Vector3(5,5,0);
        Check(!owner.ShootData.Shoot(), "elevated actual firing lane retains its vertical component");
        Clear(owner); var squadmate = Spawn("squadmate"); squadmate.GetPlayer.Transform.position = new Vector3(5,0,0);
        owner.BotFollower.BossToFollow.Followers.Add(squadmate);
        Check(!owner.ShootData.Shoot(), "same-squad follower body blocks the actual lane");
        squadmate.GetPlayer.Transform.position = new Vector3(5,0,3);
        Check(owner.ShootData.Shoot(), "moving squadmate out of lane allows fire to resume");
        Clear(owner);
        Leader(owner).Transform.position = new Vector3(5,0,0.9f);
        Check(!TryManual(owner, point), "wide suppression lane applies without addon or regroup action");
        Check(owner.Sain.ManualShoot.Reason == EShootReason.None && !owner.Sain.Suppression.SuppressingTarget,
            "blocked first suppression attempt clears native suppression state");
        owner.Sain.Suppression.Fail = true;
        Check(!TryManual(owner, point), "confirmed manual veto survives native suppression cleanup failure");
        owner.Sain.Suppression.Fail = false;
        Clear(owner); Leader(owner).Transform.position = new Vector3(0,0,5);
        Check(!TryManual(owner, new Vector3(0,1,20), EShootReason.Other),
            "ordinary manual fire checks intended lane while muzzle points elsewhere");
        Check(SAINFriendlyFireClass.CheckFriendlyFireStatus(new Vector3(0,1,20), new Vector3(0,1,0), new Vector3(1,0,0), owner.Sain) == FriendlyFireStatus.FriendlyBlock,
            "SAIN target overload blocks intended lane independently of muzzle direction");
        Clear(owner); SAINFriendlyFireClass.NativeStatus = FriendlyFireStatus.FriendlyBlock;
        Check(SAINFriendlyFireClass.CheckFriendlyFireStatus(20, new Vector3(0,1,0), new Vector3(1,0,0), owner.Sain) == FriendlyFireStatus.FriendlyBlock,
            "shared compatibility preserves existing native friendly blocks");
        SAINFriendlyFireClass.NativeStatus = FriendlyFireStatus.Clear;
        Check(TryManual(owner, point), "safe native suppression starts normally");
        owner.Sain.Suppression.SuppressingTarget = true;
        owner.Sain.ManualShoot.ShootPosition = new Vector3(0,1,20); Leader(owner).Transform.position = new Vector3(0,0,5);
        gun.ManualUpdate(0.1f);
        Check(!owner.ShootData.Shooting && owner.Sain.ManualShoot.Reason == EShootReason.None && !owner.Sain.Suppression.SuppressingTarget,
            "changed suppression target stops active burst and clears manual/native suppression lease");
        Clear(owner); owner.ShootData.Shoot(); Leader(owner).Transform.position = new Vector3(5,0,0);
        SAIN.SAINEnableClass.Fail = true; gun.ManualUpdate(0.1f); SAIN.SAINEnableClass.Fail = false;
        Check(!owner.ShootData.Shooting && !gun.IsTriggerPressed,
            "optional SAIN lookup failure cannot disable the shared muzzle guard");
        Clear(owner); TryManual(owner, point); Leader(owner).Transform.position = new Vector3(5,0,0);
        int rounds = gun.Operation.Rounds, ammo = gun.Operation.Ammo;
        gun.IsTriggerPressed = true; gun.Operation.InternalOnFireEvent();
        Check(gun.Operation.Rounds == rounds && gun.Operation.Ammo == ammo && gun.Operation.Idle && !gun.IsTriggerPressed,
            "fixed burst reassertion blocked before ammo consumption through native idle transition");
        gun.Operation.FailIdle = true; gun.Operation.InternalOnFireEvent(); gun.Operation.FailIdle = false;
        Check(gun.Operation.Rounds == rounds && gun.Operation.Ammo == ammo,
            "confirmed automatic veto preserves ammunition if idle transition fails");
        Clear(owner); gun.Operation.Idle = false; gun.Operation.InternalOnFireEvent();
        Check(gun.Operation.Rounds == rounds + 1 && gun.Operation.Ammo == ammo - 1,
            "clear automatic round executes once and consumes exactly one cartridge");
        Leader(owner).Transform.position = new Vector3(5,0,0); gun.Operation.InternalOnFireEvent();
        Check(gun.Operation.Rounds == rounds + 1 && gun.Operation.Ammo == ammo - 1,
            "ally entering between automatic rounds blocks the next round without ammo loss");
        Clear(owner); Leader(owner).Transform.position = new Vector3(5,0,0);
        var recruit = Spawn("recruit"); Leader(recruit).Transform.position = new Vector3(5,0,0);
        Check(!recruit.ShootData.Shoot(), "recruited follower uses same guard");
        var other = Spawn("ordinary", false); Leader(other).Transform.position = new Vector3(5,0,0);
        Check(other.ShootData.Shoot() && TryManual(other, point), "ordinary bots retain native normal/manual policy");
        var human = new Player { ProfileId = "human" }; var humanGun = new Player.FirearmController(human); human.HandsController = humanGun;
        humanGun.SetTriggerPressed(true); humanGun.ManualUpdate(0.1f);
        Check(humanGun.IsTriggerPressed, "human trigger and firearm update are untouched");
        owner.IsFollower = false;
        Check(owner.ShootData.Shoot(), "dismissed follower immediately leaves safety scope"); owner.IsFollower = true;
        Clear(owner); Check(!TryManual(owner, new Vector3(float.NaN,1,0)), "nonfinite manual target cannot start fire");
        owner.GetPlayer.LocalShotDirection = Vector3.zero;
        Check(!TryManual(owner, point), "missing valid muzzle vector blocks manual admission");
        owner.GetPlayer.HandsController = null; gun.ManualUpdate(0.1f);
        Check(gun.Updates > 0, "stale controller update remains native during hands teardown");
        Check(pitTeam.Modules.BattleRecorder.Calls == 0, "friendly-fire guards add no shot-recording events");
        owner=Spawn("head-check");
        var ally=Leader(owner);
        ally.MainParts=new Dictionary<BodyPartType,BodyPart>{{BodyPartType.head,new BodyPart{Position=new Vector3(-.2f,1,0)}}};
        Check(FollowerShotSafety.IsFriendlyInSuppressionLane(owner,new Vector3(0,1,0),point),
            "suppression rejects an actual teammate head immediately behind the muzzle");
        owner.Brain.Agent.Action=BotLogicDecision.attackMovingWithSuppress;
        Check(!owner.ShootData.Shoot(),"core suppression muzzle clearance is enforced at native trigger admission");
        owner.Brain.Agent.Action=BotLogicDecision.none;
        ally.MainParts[BodyPartType.head].Position=new Vector3(5,1,0);
        Check(FollowerShotSafety.IsFriendlyInShotLane(owner,new Vector3(0,1,0),point),
            "actual animated head position blocks fire even when feet are outside the lane");
        ally.MainParts=null;
        Console.WriteLine(count + " shared firing safety checks passed.");
    }
}

namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero => new Vector3(); public static Vector3 up => new Vector3(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z; public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude>0 ? this/magnitude : zero; public void Normalize(){this=normalized;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object o)=>o is Vector3 v&&this==v; public override int GetHashCode()=>x.GetHashCode();
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
    }
    public class Transform { public Vector3 position; public Vector3 TransformDirection(Vector3 v)=>v; }
}
namespace EFT
{
    public class AIData {public BotOwner BotOwner; public Player Player;}
    public class Health {public bool IsAlive=true;}
    public enum BodyPartType { body,head }
    public class BodyPart {public Vector3 Position;}
    public class Player
    {
        public Dictionary<BodyPartType,BodyPart> MainParts;
        public string ProfileId; public AIData AIData; public Health HealthController=new Health(); public Transform Transform=new Transform();
        public Vector3 LookDirection=new Vector3(1,0,0),LocalShotDirection=new Vector3(1,0,0); public object HandsController;
        public Bones PlayerBones=new Bones();
        public class ItemHandsController {protected Player _player; public ItemHandsController(Player p){_player=p;}}
        public class FirearmController : ItemHandsController
        {
            public bool IsTriggerPressed; public Func<bool> IsAIShootFromEyes=()=>false;
            public FirePort CurrentFireport=new FirePort(); public Vector3 AdjustDirection; public int Updates;
            public bool LauncherMode; public UnderbarrelContainer _underbarrelContainer=new UnderbarrelContainer();
            public class UnderbarrelContainer {public FirePort Fireport=new FirePort();}
            public bool IsInLauncherMode()=>LauncherMode;
            public AutomaticFireOperation Operation;
            public FirearmController(Player p):base(p){Operation=new AutomaticFireOperation{Controller=this,Player=p};}
            public void AdjustShotVectors(ref Vector3 origin,ref Vector3 direction){if(AdjustDirection!=Vector3.zero)direction=AdjustDirection;}
            [MethodImpl(MethodImplOptions.NoInlining)] public virtual void SetTriggerPressed(bool pressed){IsTriggerPressed=pressed;}
            [MethodImpl(MethodImplOptions.NoInlining)] public virtual void ManualUpdate(float dt){Updates++;}
            public class AutomaticFireOperation
            {
                public Player Player; public FirearmController Controller; public int Ammo=100,Rounds; public bool Idle,FailIdle;
                [MethodImpl(MethodImplOptions.NoInlining)] public virtual void InternalOnFireEvent(){Ammo--;Rounds++;}
                public void SwitchToIdling(){if(FailIdle)throw new InvalidOperationException();Controller.IsTriggerPressed=false;Idle=true;}
            }
        }
    }
    public class FirePort {public Vector3 position=new Vector3(0,1,0);public Transform Original=new Transform();}
    public class Bones {public Transform WeaponRoot=new Transform{position=new Vector3(0,1,0)};}
    public class BotFollower {public pitAIBossPlayer BossToFollow;}
    public class Aim {public bool IsReady=true;public Vector3 EndTargetPoint=new Vector3(20,1,0);}
    public class AimManager {public Aim CurrentAiming=new Aim();}
    public class WeaponManager {public Player.FirearmController ShootController;}
    public class Sensor {public Vector3 ShootStartPos;}
    public enum BotLogicDecision {none,attackMovingWithSuppress,suppressFire}
    public class Brain {public Agent Agent=new Agent();}
    public class Agent {public BotLogicDecision Action;public Result LastResult()=>new Result{Action=Action};}
    public struct Result {public BotLogicDecision Action;}
    public class BotOwner
    {
        public Brain Brain=new Brain();
        public string ProfileId{get;set;} public bool IsFollower=true,IsDead;public Player GetPlayer;public AIData AIData;
        public BotFollower BotFollower;public ShootData ShootData;public WeaponManager WeaponManager=new WeaponManager();
        public AimManager AimingManager=new AimManager();public Sensor LookSensor=new Sensor();public BotComponent Sain;
        public Vector3 Position=>GetPlayer.Transform.position;
    }
}
public class ShootData
{
    public BotOwner _owner;public bool Shooting;public int ReplacementCalls,NativeCalls;
    public Player.FirearmController ShootController=>_owner.WeaponManager.ShootController;
    public ShootData(BotOwner owner){_owner=owner;}
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Shoot(){NativeCalls++;Shooting=true;ShootController.SetTriggerPressed(true);return true;}
    public void EndShoot(){Shooting=false;ShootController.SetTriggerPressed(false);}
}
namespace pitTeam.Components {public class pitAIBossPlayer {public Player realPlayer;public List<BotOwner> Followers=new List<BotOwner>();}}
namespace pitTeam.Modules
{
    public static class BossPlayers {public static bool IsFollower(BotOwner owner)=>owner?.IsFollower==true;}
    public static class BattleRecorder {public static int Calls;public static void RecordFollowerWeaponSafetyEvent(BotOwner owner,string reason){Calls++;}}
}
namespace pitTeam.BigBrain
{public static class FollowerCombatLayer {public static bool IsFollowerCombatLayerActive(BotOwner o)=>false;public static bool HasLiveGoalEnemyForFire(BotOwner o)=>true;}}
namespace pitTeam {public static class pitFireTeam {public static bool UseSainFollowerCombat(BotOwner owner)=>true;}}
namespace pitTeam.Utils {public static class FollowerRecovery {public static void StopShooting(BotOwner owner){owner?.ShootData?.EndShoot();}}}
namespace SAIN.Models.Enums {public enum EShootReason {None,Suppress,Other}}
namespace SAIN.SAINComponent.Classes.EnemyClasses {public class Enemy {}}
namespace SAIN.Components
{
    public class BotComponent
    {
        public BotOwner BotOwner{get;}public SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass ManualShoot{get;}
        public Suppression Suppression{get;}
        public BotComponent(BotOwner owner){BotOwner=owner;ManualShoot=new SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass(this);Suppression=new Suppression(this);}
    }
    public class Suppression
    {private BotComponent bot;public bool SuppressingTarget,Fail;public Suppression(BotComponent b){bot=b;}public void ResetSuppressing(){if(Fail)throw new InvalidOperationException();SuppressingTarget=false;bot.ManualShoot.Reset();}}
}
namespace SAIN {public static class SAINEnableClass {public static bool Fail;public static Dictionary<string,BotComponent> Bots=new Dictionary<string,BotComponent>();public static bool GetSAIN(string id,out BotComponent bot){if(Fail)throw new InvalidOperationException();return Bots.TryGetValue(id,out bot);}}}
namespace SAIN.SAINComponent.Classes.WeaponFunction
{
    public class ManualShootClass
    {
        public BotComponent Bot{get;}public BotOwner BotOwner=>Bot.BotOwner;public Vector3 ShootPosition{get;set;}public EShootReason Reason{get;private set;}
        public ManualShootClass(BotComponent bot){Bot=bot;}
        [MethodImpl(MethodImplOptions.NoInlining)] public bool TryShoot(Enemy enemy,Vector3 target,bool checkFF,EShootReason reason)
        {if(!BotOwner.ShootData.Shooting&&!BotOwner.ShootData.Shoot())return false;ShootPosition=target;Reason=reason;return true;}
        public void Reset(){BotOwner.ShootData.EndShoot();Reason=EShootReason.None;ShootPosition=Vector3.zero;}
    }
}
namespace SAIN.SAINComponent.Classes
{
    public enum FriendlyFireStatus {Clear,FriendlyBlock}
    public static class SAINFriendlyFireClass
    {
        public static FriendlyFireStatus NativeStatus=FriendlyFireStatus.Clear;
        [MethodImpl(MethodImplOptions.NoInlining)]public static FriendlyFireStatus CheckFriendlyFireStatus(Vector3 target,Vector3 origin,Vector3 direction,BotComponent bot)=>NativeStatus;
        [MethodImpl(MethodImplOptions.NoInlining)]public static FriendlyFireStatus CheckFriendlyFireStatus(float distance,Vector3 origin,Vector3 direction,BotComponent bot)=>NativeStatus;
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]public class PatchPrefixAttribute:Attribute{}
    [AttributeUsage(AttributeTargets.Method)]public class PatchPostfixAttribute:Attribute{}
    public abstract class ModulePatch
    {protected abstract MethodBase GetTargetMethod();public void Enable(){var postfix=AccessTools.Method(GetType(),"PatchPostfix");new Harmony("pitTeam.fixture."+GetType().Name).Patch(GetTargetMethod(),prefix:new HarmonyMethod(AccessTools.Method(GetType(),"PatchPrefix")),postfix:postfix==null?null:new HarmonyMethod(postfix));}}
}
