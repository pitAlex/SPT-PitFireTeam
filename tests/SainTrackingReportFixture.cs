using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;
using SAIN.Components.PlayerComponentSpace;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.Models.Structs;
using SAIN.Preset.Shared.Enums;

namespace UnityEngine
{
    public static class Time{public static float time=100;public static int frameCount;}
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
        public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
}
namespace pitTeam.BigBrain{public static class FollowerEnemyInfoCorrection{public static bool IsInsideLookCheck;}}
namespace EFT
{
    public class Health{public bool IsAlive=true;}
    public class Player{public string ProfileId="enemy";public Health HealthController=new Health();}
    public class BotOwner
    {
        public string ProfileId="follower";public bool Follower=true,Addon;public Vector3 Position;
        public Memory Memory=new Memory();public Controller EnemiesController=new Controller();public Enemy Native;
        public ShootToPoint CurrentEnemyTargetPosition(bool flag)=>new ShootToPoint(Memory.GoalEnemy.CurrPosition,1);
    }
    public class Memory{public EnemyInfo GoalEnemy;}
    public class Controller{public Dictionary<Player,EnemyInfo> EnemyInfos=new Dictionary<Player,EnemyInfo>();}
    public class GroupInfo{public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense;}
    public class EnemyInfo
    {
        public BotOwner Owner;public string ProfileId="enemy";public Player Person=new Player();
        public bool IsVisible,CanShoot;public Vector3 CurrPosition,PersonalLastPos,EnemyLastPositionReal,EnemyLastPosition;
        public float PersonalLastSeenTime,Distance=5;public GroupInfo GroupInfo=new GroupInfo();
    }
    public class ShootToPoint{public ShootToPoint(Vector3 p,float f){} }
}
namespace pitTeam
{
    public class Config<T>{public T Value;public Config(T v){Value=v;}}
    public static class pitFireTeam
    {
        public static bool IsSAINInstalled;public static Config<EnemyTrackingMode> enemyTracking=new Config<EnemyTrackingMode>(EnemyTrackingMode.Realistic);
        public static Config<int> enemyRemember=new Config<int>(60);public static bool UseSainFollowerCombat(BotOwner o)=>o.Addon;
    }
}
namespace pitTeam.Utils{public static class Utils{public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float d){d=1;return true;}}}
namespace pitTeam.Modules
{
    public static class Logger{public static void LogError(string s){throw new Exception(s);}}
    public static class GameplayModeRuntime{public static T GetEffectiveValue<T>(pitTeam.Config<T> c,T fallback)=>c.Value;}
    public class Follower{public void ClearOrderedPushTargetLock(string s){} }
    public class BossPlayers
    {
        public static BossPlayers Instance=new BossPlayers();public static bool IsFollower(BotOwner o)=>o?.Follower==true;
        public Follower GetFollower(BotOwner o)=>new Follower();
    }
    public static class FollowerContactEnemyRetention
    {
        public static bool TryGetActiveRetainedEnemy(BotOwner o,out Player p,out bool priority){p=null;priority=false;return false;}
        public static void ClearAndAllowNextGoalClear(BotOwner o){}
    }
    public static class FollowerCombatTargetCommitments{public static bool IsMissionTarget(BotOwner b,EnemyInfo e)=>false;public static void ClearMission(BotOwner b,object k,string r){} }
    public static class FollowerGoalEnemyTracker
    {
        public struct Scope:IDisposable{public void Dispose(){} }
        public static Scope Begin(string a,string b)=>new Scope();
    }
    // The existing reflection goal resolver is doubled; the entire new production
    // synchronization bridge and tracker run against actual native known-place bodies.
    public static partial class SainGoalEnemyBridge
    {
        private static PropertyInfo enemyInfoProperty,enemyProfileIdProperty,enemyKnownPlacesProperty,
            placesLastKnownProperty,placesTimeProperty,placePositionProperty;
        private static void ResolveEnemyAccessors(Type t)
        {enemyInfoProperty=t.GetProperty("EnemyInfo");enemyProfileIdProperty=t.GetProperty("EnemyProfileId");enemyKnownPlacesProperty=t.GetProperty("KnownPlaces");}
        private static bool TryGetExactSainGoalEnemy(BotOwner o,EnemyInfo e,out object native)
        {native=o.Native;return native!=null&&ReferenceEquals(o.Native.EnemyInfo,e);}
        private static bool IsFinite(Vector3 p)=>FollowerEnemyTracking.IsFinite(p);
        private static void LogAccessorFailureOnce(Exception ex){throw new Exception("Unexpected bridge failure",ex);}
        public static bool TryGetTrackingReport(BotOwner o,EnemyInfo e,out Vector3 p,out float at)
        {
            p=default(Vector3);at=-1;
            if(!pitTeam.pitFireTeam.IsSAINInstalled||o.Native?.KnownPlaces.LastKnownPlace==null||!ReferenceEquals(o.Native.EnemyInfo,e))return false;
            p=o.Native.KnownPlaces.LastKnownPlace.Position;at=o.Native.KnownPlaces.TimeLastKnownUpdated;return true;
        }
        public static bool TryGetRetainedSameGoalEnemy(BotOwner o,EnemyInfo e,out Vector3 p){p=default(Vector3);return false;}
    }
}
namespace SAIN.Preset.Shared.Enums{public enum SAINSoundType{FootStep,Conversation,Shot}}
namespace SAIN
{
    public static class SAINPlugin{public static FixturePreset LoadedPreset=new FixturePreset();}
    public class FixturePreset{public Global GlobalSettings=new Global();}
    public class Global{public Mind Mind=new Mind();}
    public class Mind{public float MaxUnderFireDistance=3;}
}
namespace SAIN.Models.Structs
{
    public struct SAINHearingReport{public Vector3 position;public SAINSoundType soundType;public EEnemyPlaceType placeType;public bool isDanger,shallReportToSquad;}
}
namespace SAIN.SAINComponent.Classes.EnemyClasses
{
    public enum EEnemyPlaceType{Vision,Hearing}
    public struct PlaceData{}
    public class EnemyPlace
    {
        public Vector3 Position{get;private set;}public bool HasSeenPersonal,IsDanger;public SAINSoundType? SoundType;
        public EnemyPlace(PlaceData data,Vector3 p,bool danger,EEnemyPlaceType type,object sound){Position=p;IsDanger=danger;}
        public EnemyPlace(PlaceData data,SAINHearingReport r){Position=r.position;IsDanger=r.isDanger;SoundType=r.soundType;}
        public void UpdatePosition(Vector3 p){Position=p;}
    }
    public class Events{public int Count;public void LastKnownUpdated(EnemyPlace p,float t)=>Count++;}
    public class Enemy
    {
        public EnemyInfo EnemyInfo{get;set;}public string EnemyProfileId=>EnemyInfo.ProfileId;
        public EnemyKnownPlaces KnownPlaces{get;set;}public Events Events=new Events();
        public Enemy(EnemyInfo info){EnemyInfo=info;KnownPlaces=new EnemyKnownPlaces(this);}
    }
    public class EnemyKnownPlaces
    {
        public EnemyPlace LastKnownPlace{get;private set;}public EnemyPlace LastSeenPlace,LastHeardPlace;
        public float TimeLastKnownUpdated{get;private set;}=-1000;
        public bool SearchedAllKnownLocations=true;public List<EnemyPlace> AllEnemyPlaces=new List<EnemyPlace>();
        private PlaceData _placeData=default(PlaceData);private Enemy Enemy;
        public EnemyKnownPlaces(Enemy enemy){Enemy=enemy;}
        /*NATIVE_SEEN*/
        /*NATIVE_HEARD*/
        /*NATIVE_SET_KNOWN*/
    }
}
namespace SAIN.Components.PlayerComponentSpace
{
    public class SoundEvent{public Vector3 Point;public bool Gun;public SAINSoundType Kind;}
    // Actual AISoundData is a struct: each Harmony call receives a newly boxed copy.
    public struct AISoundData
    {
        public SoundEvent Sound;public BotComponent Bot;public Enemy Enemy;public float PlayerDistance;
        public bool IsGunShot=>Sound.Gun;public SAINSoundType SoundType=>Sound.Kind;
    }
    public class BotComponent{public BotOwner BotOwner;public SquadHolder Squad=new SquadHolder();}
    public class SquadHolder{public Squad SquadInfo;}
    public class Squad
    {
        public bool AdjustHeight;
        public void AddPointToSearch(Enemy enemy,Vector3 point,AISoundData sound,BotComponent bot)
        {if(AdjustHeight)point.y=6;enemy.KnownPlaces.UpdatePersonalHeardPosition(new SAINHearingReport{position=point,soundType=sound.SoundType,placeType=EEnemyPlaceType.Hearing},Time.time);}
    }
}
namespace SAIN.Classes.Bot.Sense.Hearing
{
    public class HearingAnalysis{public bool Heard=true;public bool CheckIfSoundHeard(AISoundData s)=>Heard;}
    public class HearingInput{public bool Ignore;public bool IsIgnoringSounds(bool v)=>Ignore;}
    public class HearingDispersion
    {
        public int Calls;public bool Throw;
        [MethodImpl(MethodImplOptions.NoInlining)]public Vector3 CalcRandomizedPosition(AISoundData sound,float extra)
        {Calls++;if(Throw)throw new InvalidOperationException("nativeFailure");return sound.Sound.Point;}
    }
    public class HearingSensor
    {
        public BotComponent Bot;public HearingAnalysis HearingAnalysis=new HearingAnalysis();public HearingInput HearingInput=new HearingInput();
        public HearingDispersion HearingDispersion=new HearingDispersion();public event Action<AISoundData,Enemy> OnEnemySoundHeard;
        private bool ShallChaseGunshot(float d)=>d<100;
        [MethodImpl(MethodImplOptions.NoInlining)]
        /*NATIVE_REACTION*/
        [MethodImpl(MethodImplOptions.NoInlining)]
        /*NATIVE_FLYBY*/
        private void ReactToBulletFlyBy(AISoundData sound,float d,Vector3 point,bool underFire)
        {Bot.Squad.SquadInfo?.AddPointToSearch(sound.Enemy,point,sound,Bot);}
        public void Nested(Action<AISoundData,Enemy> handler){OnEnemySoundHeard+=handler;}
    }
}
internal static class Program
{
    static int checks;
    static void Check(bool v,string why){checks++;if(!v)throw new Exception(why);}
    static void Tick(float t){Time.time=t;Time.frameCount++;}
    static void Main()
    {
        var bot=new BotOwner();var info=new EnemyInfo{Owner=bot,CurrPosition=new Vector3(999,999,999)};
        bot.EnemiesController.EnemyInfos[info.Person]=info;bot.Memory.GoalEnemy=info;bot.Native=new Enemy(info);
        var native=bot.Native;var places=native.KnownPlaces;
        var component=new BotComponent{BotOwner=bot};var sensor=new SAIN.Classes.Bot.Sense.Hearing.HearingSensor{Bot=component};
        var sound=new AISoundData{Bot=component,Enemy=native,PlayerDistance=10,Sound=new SoundEvent{Point=new Vector3(176,6,169),Kind=SAINSoundType.FootStep}};
        var harmony=new Harmony("fixture.tracking");
        FollowerSainTrackingReportPatch.Apply(harmony);
        Check(Harmony.GetPatchInfo(AccessTools.Method(sensor.GetType(),"ReactToHeardSound"))==null,"SAIN-absent installation resolves no native hooks");
        pitTeam.pitFireTeam.IsSAINInstalled=true;FollowerEnemyTracking.BeginRaid();
        places.UpdatePersonalHeardPosition(new SAINHearingReport{position=new Vector3(190,-1,191)},67);
        sensor.ReactToHeardSound(sound);
        Check(places.TimeLastKnownUpdated==67,"unpatched native hearing with no native squad reproduces stale known place");
        Check(FollowerEnemyTracking.Position(info).y==-1,"Core initially receives the stale native report");
        FollowerSainTrackingReportPatch.Apply(harmony);int count=native.Events.Count;int calls=sensor.HearingDispersion.Calls;
        sensor.ReactToHeardSound(sound);
        Check(places.TimeLastKnownUpdated==100&&places.LastKnownPlace.Position.y==6,"real native hearing updates SAIN from the emitted dispersed report");
        Check(FollowerEnemyTracking.Position(info).x==176&&FollowerEnemyTracking.Position(info).y==6,"Core accepts the identical native point on the correct floor");
        Check(sensor.HearingDispersion.Calls==calls+1&&native.Events.Count==count+1,"native hearing runs once and publishes one missing known-place event");
        Check(!info.IsVisible&&!info.CanShoot&&places.LastSeenPlace==null&&!places.LastHeardPlace.HasSeenPersonal,"heard report never grants sight or fire");
        Check(places.LastHeardPlace.SoundType==SAINSoundType.FootStep,"native sound kind is preserved");
        Tick(101);count=native.Events.Count;
        Check(FollowerEnemyTracking.Position(info).y==6&&places.TimeLastKnownUpdated==100&&native.Events.Count==count,"next-frame SAIN read cannot restore old coordinates or renew report time");
        component.Squad.SquadInfo=new Squad{AdjustHeight=true};Tick(102);sound.Sound.Point=new Vector3(177,-1,170);count=native.Events.Count;
        sensor.ReactToHeardSound(sound);
        Check(native.Events.Count==count+1,"already-published native hearing is not published twice");
        Check(FollowerEnemyTracking.Position(info).y==6&&places.LastKnownPlace.Position.y==6,"final native height correction wins over the earlier dispersion local");
        component.Squad.SquadInfo=null;Tick(103);sensor.HearingAnalysis.Heard=false;count=native.Events.Count;calls=sensor.HearingDispersion.Calls;
        sensor.ReactToHeardSound(sound);Check(count==native.Events.Count&&calls==sensor.HearingDispersion.Calls,"inaudible sound supplies no observation and runs no extra sensor");sensor.HearingAnalysis.Heard=true;
        Tick(104);sensor.HearingDispersion.Throw=true;bool threw=false;
        try{sensor.ReactToHeardSound(sound);}catch(InvalidOperationException){threw=true;}
        Check(threw&&count==native.Events.Count,"original native exception survives with no partial report");sensor.HearingDispersion.Throw=false;
        sensor.ReactToHeardSound(sound);Check(places.TimeLastKnownUpdated==104,"failed native reaction does not leak the tracking scope");
        Tick(105);sensor.HearingInput.Ignore=true;count=native.Events.Count;sensor.ReactToBulletFlyBy(sound,1);Check(count==native.Events.Count,"ignored flyby supplies no observation");sensor.HearingInput.Ignore=false;
        sensor.ReactToBulletFlyBy(sound,1);Check(places.TimeLastKnownUpdated==105,"accepted flyby shares the native dispersed point");
        Tick(106);count=native.Events.Count;
        Check(!SainGoalEnemyBridge.TrySyncTrackingReport(info,new Vector3(1,1,1),107,false,native),"future observations cannot enter SAIN");
        Check(SainGoalEnemyBridge.TrySyncTrackingReport(info,new Vector3(1,1,1),90,false,native)&&places.TimeLastKnownUpdated==105,"older reports cannot replace native knowledge");
        var other=new Enemy(new EnemyInfo{Owner=bot});Check(!SainGoalEnemyBridge.TrySyncTrackingReport(info,new Vector3(1,1,1),106,false,other),"mismatched native EnemyInfo cannot be synchronized");
        bot.Follower=false;sensor.ReactToHeardSound(sound);Check(count==native.Events.Count,"ordinary bots retain native behavior without follower report writes");bot.Follower=true;
        bot.EnemiesController.EnemyInfos.Clear();sensor.ReactToHeardSound(sound);Check(count==native.Events.Count,"hearing cannot create an unaccepted enemy");bot.EnemiesController.EnemyInfos[info.Person]=info;
        info.Person.HealthController.IsAlive=false;sensor.ReactToHeardSound(sound);Check(count==native.Events.Count,"dead contacts cannot receive renewed knowledge");info.Person.HealthController.IsAlive=true;
        sound.Sound.Point=new Vector3(float.NaN,6,170);sensor.ReactToHeardSound(sound);
        Check(count==native.Events.Count,"invalid native dispersed point cannot publish knowledge");sound.Sound.Point=new Vector3(177,6,170);
        Tick(107);FollowerEnemyTracking.Report(info,new Vector3(180,6,172),107,"playerReport");
        Check(places.TimeLastKnownUpdated==107&&places.LastKnownPlace.Position.x==180&&places.LastSeenPlace==null,"accepted Core report synchronizes native knowledge without pretending personal sight");
        Tick(108);info.IsVisible=true;info.CurrPosition=new Vector3(181,6,172);
        pitTeam.BigBrain.FollowerEnemyInfoCorrection.IsInsideLookCheck=true;
        Check(FollowerEnemyTracking.Position(info).x==180&&places.TimeLastKnownUpdated==107,"unfinished native look checks cannot publish provisional visibility as new sight");
        pitTeam.BigBrain.FollowerEnemyInfoCorrection.IsInsideLookCheck=false;Tick(108.1f);
        Check(FollowerEnemyTracking.Position(info).x==181&&places.LastSeenPlace.Position.x==181,"genuine corrected sight supersedes stale native memory in both trackers");info.IsVisible=false;
        Tick(109);info.GroupInfo.EnemyLastSeenTimeSense=109;info.EnemyLastPosition=new Vector3(800,800,800);
        Check(FollowerEnemyTracking.Position(info).x==181&&places.TimeLastKnownUpdated==108.1f,"raw EFT mirror bookkeeping cannot overwrite dispersed/native evidence");
        pitTeam.pitFireTeam.IsSAINInstalled=false;Tick(110);count=native.Events.Count;
        FollowerEnemyTracking.Report(info,new Vector3(182,6,172),110,"playerReport");sensor.ReactToHeardSound(sound);
        Check(count==native.Events.Count&&FollowerEnemyTracking.Position(info).x==182,"SAIN-absent behavior keeps normal Core reports and performs no native writes");
        pitTeam.pitFireTeam.IsSAINInstalled=true;pitTeam.pitFireTeam.enemyTracking.Value=EnemyTrackingMode.Simple;
        FollowerEnemyTracking.BeginRaid();Tick(111);info.IsVisible=true;count=native.Events.Count;
        sensor.ReactToHeardSound(sound);FollowerEnemyTracking.TryGetKnownPosition(info,out var remembered,out var reportedAt);
        Check(count==native.Events.Count&&reportedAt==108.1f,"Simple mode keeps existing native-report timing and bypasses new synchronization");
        pitTeam.pitFireTeam.enemyTracking.Value=EnemyTrackingMode.Realistic;FollowerEnemyTracking.BeginRaid();info.IsVisible=false;Tick(112);
        var outerEvent=new SoundEvent{Point=new Vector3(185,6,172),Kind=SAINSoundType.Conversation};
        var inner=sound;inner.Sound=new SoundEvent{Point=new Vector3(186,6,172),Kind=SAINSoundType.FootStep};
        sensor.Nested((data,reportedEnemy)=>{if(ReferenceEquals(data.Sound,outerEvent))sensor.ReactToHeardSound(inner);});
        sound.Sound=outerEvent;sensor.ReactToHeardSound(sound);
        Check(FollowerEnemyTracking.Position(info).x==186&&places.TimeLastKnownUpdated==112,"nested native hearing preserves the newest scope and does not restore the outer point");
        Check(ReferenceEquals(bot.Memory.GoalEnemy,info),"knowledge synchronization never changes the accepted combat goal");
        Console.WriteLine("PASS: "+checks+" production SAIN report synchronization / actual Harmony hearing reaction checks; controlled EFT inputs.");
    }
}
