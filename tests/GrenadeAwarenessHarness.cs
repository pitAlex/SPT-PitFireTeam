// Controlled game stand-ins; production Harmony patches and native danger lifecycle run below.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0);
        public static Vector3 zero=>default;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float n)=>new(a.x*n,a.y*n,a.z*n);
        public static Vector3 operator /(Vector3 a,float n)=>new(a.x/n,a.y/n,a.z/n);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
    }
    public class GameObject { public int layer; }
    public class Transform { public Vector3 position; }
    public class Collider { public bool isTrigger; public GameObject gameObject=new(); public EFT.Player? Player; }
    public static class Time { public static float time=100f; }
    public enum QueryTriggerInteraction { Ignore }
    public struct Ray { public Ray(Vector3 point,Vector3 direction){} }
    public struct RaycastHit { }
    public static class Physics {
        public static bool Blocked;
        public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction trigger)=>Blocked;
        public static bool Raycast(Ray ray,out RaycastHit hit,float length,int mask){hit=default;return Blocked;}
    }
}
namespace Comfort.Common { public static class Singleton<T> where T:new() { public static T Instance=new(); } }
public static class LayersMaskController { public static int HighPolyWithTerrainMask=1,TripwireCheckLayerMask=1; }
public static class LayerMaskExtensions { public static bool Contains(int mask,int layer)=>(mask&(1<<layer))!=0; }
public static class MyExtensions { public static bool IsTrue100(float chance)=>chance>=100; }
public class CoreGrenadeSettings { public float DELTA_GRENADE_END_TIME=5,DELTA_GRENADE_SAFE_DIST_SQRT=144; }
public static class BotInternalSettingsController { public static CoreGrenadeSettings Core=new(); }
namespace EFT {
    public enum EBotState { Inactive,Active }
    public enum EPhraseTrigger { Spreadout,OnEnemyGrenade }
    public enum BodyPartType { head }
    public class BodyPart { public Vector3 Position; }
    public class AIData { public BotOwner BotOwner=null!; }
    public class HealthController { public bool IsAlive=true; }
    public class Player { public HealthController HealthController=new(); public bool IsAI;public AIData AIData=new();public Dictionary<BodyPartType,BodyPart> MainParts=new(); }
    public class Throwable {
        public event Action<Throwable>? DestroyEvent;
        public int Subscribers=>DestroyEvent?.GetInvocationList().Length??0;
        public void BlowUp()=>DestroyEvent?.Invoke(this);
    }
    public class ThrowWeap { public float GetExplDelay=7f; }
    public class Grenade : Throwable {
        public string ProfileId="enemy"; public Transform transform=new();public GameObject gameObject=new();
        public ThrowWeap WeaponSource=new();
    }
    public class SmokeGrenade : Grenade { }
    public class StunGrenade : Grenade { }
    public class MindSettings { public bool GRENADE_DAMAGE_IGNORE;public float CHANCE_TO_IGNORE_TRIPWIRE; }
    public class GrenadeSettings { public int BEWARE_TYPE=1;public bool IGNORE_SMOKE_GRENADE=true; }
    public class LaySettings { public int SHALL_LAY_PROBABILTY_WHEN_ARTILLERY; }
    public class FileSettings { public MindSettings Mind=new();public GrenadeSettings Grenade=new();public LaySettings Lay=new(); }
    public class CurrentSettings { public float CurrentHearingSense=1f; }
    public class BotSettings { public FileSettings FileSettings=new();public CurrentSettings Current=new(); }
    public class BotGroup { public object CoverPointMaster=new(); }
    public class BotTalk {
        public bool IsSilenced;public int Warnings;public void DropNextSayPeriod(){}
        public void Say(EPhraseTrigger phrase,bool immediate){if(phrase==EPhraseTrigger.Spreadout)Warnings++;}
    }
    public partial class BotOwner {
        public string ProfileId=Guid.NewGuid().ToString();
        public bool IsDead,IsFollower=true; public bool? AddonCombatOverride;
        public EBotState BotState=EBotState.Active;
        public Player GetPlayer=new(){IsAI=true};public BotGroup BotsGroup=new();public BotSettings Settings=new();
        public BotBewareGrenade BewareGrenade;
        public BotHearingSensor HearingSensor;
        public BotTalk BotTalk=new();public Transform Transform=new();public Vector3 Position=>Transform.position;
        public WeaponManager WeaponManager=new();
        public DangerSensor ArtilleryDangerPlace=new(),BewareBTR=new();
        public TurnAwaySensor BotTurnAwayLight=new();public FlashSensor FlashGrenade=new();
        public SmokeSensor SmokeGrenade=new();public MineSensor BewarePlantedMine=new();public BotMemory Memory=new();
        public BotOwner(){BewareGrenade=new(this);HearingSensor=new(this);GetPlayer.AIData.BotOwner=this;}
    }
    public class BotCollection { public List<BotOwner> BotOwners=new(); }
    public class BotsController {
        public BotCollection Bots=new(); public int OriginalCalls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnGrenadeThrow(Grenade grenade,Vector3 position,Vector3 force,float mass){
            OriginalCalls++;
            foreach(var bot in Bots.BotOwners)bot.BewareGrenade.AddGrenadeDanger(AIGrenadeHelper.FindDangerPoint(position,force,mass),grenade);
        }
    }
    public class GameWorld {
        public Player GetPlayerByCollider(Collider c)=>c.Player!; public Player? GetAlivePlayerByProfileID(string id)=>null;
        public void TriggerTripwire(EFT.SynchronizableObjects.TripwireSynchronizableObject wire)=>wire.TriggerTripwire();
    }
}
public class BotHearingSensor {
    public BotOwner _botOwner;public EFT.BotSettings _botSettings;
    public BotHearingSensor(BotOwner owner){_botOwner=owner;_botSettings=owner.Settings;}
__NATIVE_HEARING__
}
public class BotBewareGrenade {
    public BotOwner _owner;public BotBewareGrenade(BotOwner owner){_owner=owner;}
    public int Notifications,Accepted,Spotted; public bool Reject,Throw;
    public Grenade? LastGrenade; public Vector3 LastDanger;
    public GrenadeDangerPoint? GrenadeDangerPoint;public Action<Grenade>? OnBewareGrenade;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void AddGrenadeDanger(Vector3 danger,Grenade grenade){
        if(Throw)throw new InvalidOperationException("unavailable follower subsystem");
        Notifications++;LastGrenade=grenade;LastDanger=danger;
        if(!Reject)Accepted++;
    }
    public void SpottedAllPointNearPos(Vector3 position){Spotted++;}
    public bool IsIgnoreByPeriod()=>false;
__NATIVE_BEWARE__
}
public static class AIGrenadeHelper {
    public static Vector3 FindDangerPoint(Vector3 pos,Vector3 force,float mass)=>new(pos.x+(mass==0?0:force.x/mass),pos.y,pos.z);
}
namespace pitTeam {
    public static class pitFireTeam {
        public static bool IsSAINInstalled=true,UseSainFollowerCombat;
        public static bool ShouldDisableSainForFollower(BotOwner owner)=>IsSAINInstalled&&!(owner.AddonCombatOverride??UseSainFollowerCombat);
    }
}
namespace pitTeam.Patches {
    internal static class FollowerForcedPhraseGate { public static void Arm(BotOwner bot,EPhraseTrigger phrase,float duration){} }
}
namespace pitTeam.Modules {
    public class Follower { public BotOwner Bot=null!;public BotOwner GetBot()=>Bot; }
    public class BossPlayers {
        public static BossPlayers? Instance=new();public static List<Follower> Followers=new();
        public static List<Follower> GetFollowers()=>Followers;
        public static bool IsFollower(BotOwner bot)=>Instance!=null&&Followers.Exists(f=>f.Bot==bot);
    }
    public static class Logger {
        public static List<string> Errors=new(),Info=new();
        public static void LogError(string s)=>Errors.Add(s);
        public static void LogInfo(string s)=>Info.Add(s);
    }
}
namespace SAIN.Components { public class BotComponent { public BotOwner BotOwner=null!; } }
namespace SAIN {
    public static class SAINEnableClass {
        public static Dictionary<string,Components.BotComponent> Bots=new();
        public static bool GetSAIN(string profileId,out Components.BotComponent bot)=>Bots.TryGetValue(profileId,out bot);
    }
}
namespace SAIN.SAINComponent.Classes.WeaponFunction {
    public class GrenadeReactionClass {
        public BotOwner BotOwner{get;set;}=null!;
        public int TrackCalls;public bool Fallback;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EnemyGrenadeThrown(Grenade grenade,Vector3 dangerPoint,string profileId){
            TrackCalls++;
            if(Fallback)BotOwner.BewareGrenade.AddGrenadeDanger(dangerPoint,grenade);
        }
    }
}
namespace SAIN.Patches.Generic {
    public static class Vector { public static Vector3 DangerPoint(Vector3 p,Vector3 f,float m)=>AIGrenadeHelper.FindDangerPoint(p,f,m); }
    public static class GrenadeThrownActionPatch {
__SAIN_PREFIX__
    }
}
public static class GrenadeChecks {
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    private static (BotsController controller,BotOwner bot,SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass tracker) Fresh(bool sain=true,bool follower=true){
        BossPlayers.Instance=new();BossPlayers.Followers.Clear();SAIN.SAINEnableClass.Bots.Clear();Logger.Errors.Clear();
        pitTeam.pitFireTeam.IsSAINInstalled=true;pitTeam.pitFireTeam.UseSainFollowerCombat=false;
        var bot=new BotOwner{IsFollower=follower};var controller=new BotsController();controller.Bots.BotOwners.Add(bot);
        if(follower)BossPlayers.Followers.Add(new(){Bot=bot});
        if(sain)SAIN.SAINEnableClass.Bots[bot.ProfileId]=new(){BotOwner=bot};
        return(controller,bot,new(){BotOwner=bot});
    }
    private static void Dispatch((BotsController controller,BotOwner bot,SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass tracker) f,bool trackerFirst=false,Grenade? grenade=null){
        grenade??=new();var position=new Vector3(2,3,4);var force=new Vector3(6,0,0);
        if(trackerFirst)f.tracker.EnemyGrenadeThrown(grenade,new Vector3(5,3,4),grenade.ProfileId);
        f.controller.OnGrenadeThrow(grenade,position,force,2);
        if(!trackerFirst)f.tracker.EnemyGrenadeThrown(grenade,new Vector3(5,3,4),grenade.ProfileId);
    }
    public static int Run(){
        var sain=new Harmony("tests.sain.grenade");
        var target=AccessTools.Method(typeof(BotsController),nameof(BotsController.OnGrenadeThrow));
        sain.Patch(target,prefix:new HarmonyMethod(typeof(SAIN.Patches.Generic.GrenadeThrownActionPatch),"PatchPrefix"));
        var f=Fresh();Dispatch(f);
        Check(f.bot.BewareGrenade.Notifications==0&&f.tracker.TrackCalls==1,"reproduce_missing_native_awareness_before_fix");
        var fix=new Harmony("tests.pitfireteam.grenade");
        pitTeam.Patches.FollowerSainGrenadeAwarenessPatch.Apply(fix);
        Check(Logger.Errors.Count==0,"production_bootstrap_resolves_optional_SAIN_layout");
        Check(Harmony.GetPatchInfo(target).Postfixes.Count==1,"native_postfix_registered");
        foreach(bool trackerFirst in new[]{false,true}){
            f=Fresh();var grenade=new Grenade();Dispatch(f,trackerFirst,grenade);
            Check(f.controller.OriginalCalls==0&&f.bot.BewareGrenade.Notifications==1,"skipped_original_restored_once_"+trackerFirst);
            Check(f.tracker.TrackCalls==0,"parallel_tracker_bypassed_"+trackerFirst);
            Check(ReferenceEquals(f.bot.BewareGrenade.LastGrenade,grenade)&&f.bot.BewareGrenade.LastDanger.x==5,"actual_grenade_and_native_prediction_"+trackerFirst);
        }
        f=Fresh();f.tracker.Fallback=true;Dispatch(f);Check(f.bot.BewareGrenade.Notifications==1,"no_tracker_fallback_duplicate");
        f=Fresh();f.bot.BewareGrenade.Reject=true;Dispatch(f);
        Check(f.bot.BewareGrenade.Notifications==1&&f.bot.BewareGrenade.Accepted==0,"native_recognition_rejection_not_rerolled");
        f=Fresh(false);f.bot.BewareGrenade.Reject=true;Dispatch(f);
        Check(f.bot.BewareGrenade.Notifications==1&&f.bot.BewareGrenade.Accepted==0,"SAIN_excluded_follower_not_notified_twice");
        f=Fresh(true,false);Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0&&f.tracker.TrackCalls==1,"ordinary_SAIN_bot_unchanged");
        f=Fresh(false,false);f.controller.OnGrenadeThrow(new(),default,default,1);
        Check(f.bot.BewareGrenade.Notifications==1,"ordinary_vanilla_bot_unchanged");
        f=Fresh();pitTeam.pitFireTeam.UseSainFollowerCombat=true;Dispatch(f);
        Check(f.bot.BewareGrenade.Notifications==0&&f.tracker.TrackCalls==1,"addon_keeps_SAIN_routing");
        f=Fresh();f.bot.AddonCombatOverride=true;
        var mixedCore=new BotOwner{AddonCombatOverride=false};f.controller.Bots.BotOwners.Add(mixedCore);
        BossPlayers.Followers.Add(new(){Bot=mixedCore});SAIN.SAINEnableClass.Bots[mixedCore.ProfileId]=new(){BotOwner=mixedCore};
        var mixedTracker=new SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass{BotOwner=mixedCore};var mixedGrenade=new Grenade();
        Dispatch(f,grenade:mixedGrenade);mixedTracker.EnemyGrenadeThrown(mixedGrenade,default,mixedGrenade.ProfileId);
        Check(f.bot.BewareGrenade.Notifications==0&&f.tracker.TrackCalls==1,"mixed_squad_SainMan_keeps_its_SAIN_grenade_routing");
        Check(mixedCore.BewareGrenade.Notifications==1&&mixedTracker.TrackCalls==0,"mixed_squad_core_tactic_keeps_native_grenade_routing");
        f=Fresh();Dispatch(f,grenade:new(){ProfileId=f.bot.ProfileId});Check(f.bot.BewareGrenade.Notifications==1,"own_grenade_delivered_to_native_policy");
        f=Fresh();Dispatch(f,grenade:new(){ProfileId="boss"});Check(f.bot.BewareGrenade.Notifications==1,"boss_grenade_delivered_to_native_policy");
        f=Fresh();f.bot.IsDead=true;Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0,"dead_follower");
        f=Fresh();f.bot.BotState=EBotState.Inactive;Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0,"inactive_follower");
        f=Fresh();f.bot.BotsGroup=null!;Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0,"missing_group");
        f=Fresh();f.bot.BewareGrenade=null!;Dispatch(f);Check(Logger.Errors.Count==0,"missing_danger_subsystem");
        f=Fresh();BossPlayers.Instance=null;Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0,"raid_teardown");
        f=Fresh();BossPlayers.Followers.Clear();Dispatch(f);Check(f.bot.BewareGrenade.Notifications==0&&f.tracker.TrackCalls==1,"dismissed_follower");
        f=Fresh();f.controller.OnGrenadeThrow(null!,default,default,1);Check(f.bot.BewareGrenade.Notifications==0,"null_grenade");
        f=Fresh();var second=new BotOwner();BossPlayers.Followers.Add(new(){Bot=second});SAIN.SAINEnableClass.Bots[second.ProfileId]=new(){BotOwner=second};
        f.bot.BewareGrenade.Throw=true;Dispatch(f);
        Check(second.BewareGrenade.Notifications==1&&Logger.Errors.Count==1,"bad_follower_does_not_block_other_followers");
        sain.Unpatch(target,HarmonyPatchType.Prefix,sain.Id);
        f=Fresh();f.controller.OnGrenadeThrow(new(),default,default,1);
        Check(f.controller.OriginalCalls==1&&f.bot.BewareGrenade.Notifications==1,"original_executed_no_duplicate_if_SAIN_hook_absent");
        f=Fresh();pitTeam.pitFireTeam.IsSAINInstalled=false;Dispatch(f);
        Check(f.controller.OriginalCalls==1&&f.bot.BewareGrenade.Notifications==1&&f.tracker.TrackCalls==1,"no_SAIN_native_routing_unchanged");
        return checks;
    }
}
public static class TestEntry {
    public static int Main(){try{Console.WriteLine("Passed "+GrenadeChecks.Run()+" grenade routing checks with real Harmony."); Console.WriteLine("Passed "+TripwireChecks.Run()+" tripwire checks with real Harmony and native danger lifecycle."); Console.WriteLine("Passed "+TripwireAvoidanceChecks.Run()+" tripwire response checks with shared action mapping and native danger lifecycle.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
