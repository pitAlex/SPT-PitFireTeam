#nullable disable
#pragma warning disable CS8632
using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;
using pitTeam.Modules;
using pitTeam.Patches;
using EFT.Interactive;
using UnityEngine.AI;
using DrakiaXYZ.BigBrain.Brains;
using pitTeam.BigBrain;
using pitTeam.BigBrain.Actions;
using pitTeam.Utils;
using pitTeam.Components;
namespace UnityEngine {
 public class MonoBehaviour {public static int ComponentReads;public bool isActiveAndEnabled=true;public Collider Attached;public T GetComponent<T>() where T:class{ComponentReads++;return Attached as T;}}
 public static class Time {public static float time=100;}
 public class Object {
  public static readonly Dictionary<Type,Array> Found=new Dictionary<Type,Array>(); public static int Scans;
  public static T[] FindObjectsOfType<T>(bool includeInactive) {Scans++;return Found.TryGetValue(typeof(T),out var a)?(T[])a:Array.Empty<T>();}
 }
 public struct Vector2 {public float x,y;public static Vector2 zero=>new Vector2();}
 public struct Vector3 {
  public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up=>new Vector3(0,1,0);
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
  public static Vector3 one=>new Vector3(1,1,1);public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>(float)Math.Sqrt(sqrMagnitude);
  public Vector3 normalized=>this*(1/magnitude);public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
 }
 public static class Mathf {public static float Abs(float a)=>Math.Abs(a);public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);}
 public struct Bounds {
  public Vector3 center,extents;public void Expand(Vector3 size){extents+=size*.5f;}
  public Vector3 min=>center-extents;public Vector3 max=>center+extents;
  public float SqrDistance(Vector3 p){var d=p-center;float x=Math.Max(0,Math.Abs(d.x)-extents.x),y=Math.Max(0,Math.Abs(d.y)-extents.y),z=Math.Max(0,Math.Abs(d.z)-extents.z);return x*x+y*y+z*z;}
 }
 public struct Matrix4x4 {
  public float m00,m01,m02,m03,m10,m11,m12,m13,m20,m21,m22,m23;
  public Vector3 MultiplyPoint3x4(Vector3 p)=>new Vector3(m00*p.x+m01*p.y+m02*p.z+m03,m10*p.x+m11*p.y+m12*p.z+m13,m20*p.x+m21*p.y+m22*p.z+m23);
  public static Matrix4x4 Identity=>new Matrix4x4{m00=1,m11=1,m22=1};
 }
 public class Transform {public Matrix4x4 worldToLocalMatrix=Matrix4x4.Identity;}
 public class GameObject {public bool activeInHierarchy=true;}
 public class Collider {public static int BoundsReads;private Bounds storedBounds;public bool enabled=true,isTrigger=true;public GameObject gameObject=new GameObject();public Transform transform=new Transform();public Bounds bounds{get{BoundsReads++;return storedBounds;}set{storedBounds=value;}}}
 public class BoxCollider:Collider {public Vector3 center,size;}
}
namespace UnityEngine.AI {
 public enum NavMeshPathStatus {PathComplete,PathPartial}
 public class NavMeshPath {public static int Allocations;public NavMeshPath(){Allocations++;}public NavMeshPathStatus status;public Vector3[] corners;}
 public struct NavMeshHit {public Vector3 position;}
 public static class NavMesh {
  public const int AllAreas=-1;public static int Calls;public static bool Available=true,Partial;public static float ShiftY,MaxX=1000;
  public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float d,int mask){p.y+=ShiftY;hit=new NavMeshHit{position=p};return Available&&Math.Abs(p.x)<=MaxX;}
  public static bool CalculatePath(Vector3 a,Vector3 b,int mask,NavMeshPath p){Calls++;p.status=Partial?NavMeshPathStatus.PathPartial:NavMeshPathStatus.PathComplete;p.corners=new[]{a,b};return Available;}
 }
}
namespace Comfort.Common {public static class Singleton<T> {public static T Instance;}}
namespace EFT {
 public class AIData {public BotOwner BotOwner;}
 public class Danger {public bool Active;public bool ShallRunAway()=>Active;}
 public class Grenades {public bool ThrowindNow;}
 public class Weapons {public Grenades Grenades=new Grenades();}
 public class Treatment {public bool Have2Do,HaveWork,Using;}
 public class Medicine {public Treatment FirstAid=new Treatment(),SurgicalKit=new Treatment();}
 public class Mover {public Vector3 CurrentCornerPoint;public bool Pause;public int Paths,Stops;public Vector3[] LastPath;
  public void GoToByWay(Vector3[] way,float reach){Paths++;LastPath=way;}public void Stop(){Stops++;}public void Sprint(bool a,bool b){} }
 public class Door {public void UpdateDoorInteractionStatus(){}}
 public class Lay {public bool IsLay;public void GetUp(bool check){IsLay=false;}}
 public class Steering {public void LookToMovingDirection(){}}
 public class BotOwner {public int Id;public bool Follower,IsDead,HasPathAndNotComplete=true;public string ProfileId="bot";public Vector3 Position;
  public Player GetPlayer;public Mover Mover=new Mover();public Danger BewareGrenade=new Danger(),BewareBTR=new Danger();public Weapons WeaponManager=new Weapons();public Medicine Medecine=new Medicine();public Door DoorOpener=new Door();public Lay BotLay=new Lay();public Steering Steering=new Steering();
  public void SetPose(float p){}public void SetTargetMoveSpeed(float s){}public void StopMove(){} }
 public class Player {
  public string ProfileId="bot";public bool IsAI=true,Sprinting=true;public AIData AIData=new AIData{BotOwner=new BotOwner{Follower=true}};public Vector3 Position;public int ZeroMoves;
  public void EnableSprint(bool enabled){Sprinting=enabled;}public void Move(Vector2 direction){ZeroMoves++;}
 }
 public interface IObserverToPlayerBridge {Player iPlayer{get;}}
 public class WireBridge:IObserverToPlayerBridge {public Player iPlayer{get;set;}}
 public class GameWorld {public static int Lookups;public string LocationId="labyrinth";public TriggersEmitter TriggersEmitter=new TriggersEmitter();public Player Player;public Player GetAlivePlayerByProfileID(string id){Lookups++;return Player;}}
 public class MovementContext {public virtual void DirectApplyMotion(Vector3 motion,float deltaTime){}}
}
namespace EFT.GameTriggers {
 public class TriggerZone:MonoBehaviour {
  public string _triggerId;public BoxCollider[] Colliders=Array.Empty<BoxCollider>();
  public T[] GetComponents<T>()=> (T[])(object)Colliders;
 }
 public class HandlerTriggerState {public bool _targetState;public string[] _controlledTriggerId;}
 public class HandlerDamage {public string _triggerIdStartDamage;}
 public class TriggersEmitter {public HashSet<string> _ignoreTriggers=new HashSet<string>();}
}
namespace EFT.Interactive {public class BarbedWire:MonoBehaviour {public void AddPenalty(IObserverToPlayerBridge p){}}}
namespace HarmonyLib {public static class AccessTools {public static MethodInfo Method(Type t,string name,Type[] types=null)=>types==null?t.GetMethod(name):t.GetMethod(name,types);}}
namespace SPT.Reflection.Patching {public abstract class ModulePatch{protected abstract MethodBase GetTargetMethod();}public class PatchPrefix:Attribute{}public class PatchPostfix:Attribute{}}
namespace DrakiaXYZ.BigBrain.Brains {
 public abstract class CustomLayer {
  public class ActionData{}public class Action {public Type Type;public string Reason;public ActionData Data;public Action(Type t,string r,ActionData d){Type=t;Reason=r;Data=d;}}
  protected BotOwner BotOwner;protected CustomLayer(BotOwner b,int p){BotOwner=b;}public abstract string GetName();public abstract bool IsActive();public abstract Action GetNextAction();public abstract bool IsCurrentActionEnding();public virtual void Stop(){}
 }
 public abstract class CustomLogic {protected BotOwner BotOwner;protected CustomLogic(BotOwner b){BotOwner=b;}public virtual void Start(){}public virtual void Stop(){}public abstract void Update(CustomLayer.ActionData d);}
}
namespace pitTeam.Utils {
 public static class FollowerMedical {public static int Cancelled;public static bool IsUsingMedical(BotOwner b)=>b.Medecine.FirstAid.Using||b.Medecine.SurgicalKit.Using;public static void CancelActiveMedical(BotOwner b){Cancelled++;b.Medecine.FirstAid.Using=false;b.Medecine.SurgicalKit.Using=false;}}
 public static class FollowerRecovery {public static void StopShooting(BotOwner b){}}
}
namespace pitTeam.Modules {
 public static class SainAddonBridge {public static int Queries,Releases;public static void ReleaseWireCover(BotOwner b){Releases++;}public static bool Handled;public static Vector3? Corner;public static bool TryGetMovementCorner(BotOwner b,out Vector3? corner){Queries++;corner=Corner;return Handled;}}
 public class BossPlayers {public static BossPlayers Instance=new BossPlayers();public BotFollowerPlayer FollowerData=new BotFollowerPlayer();public BotFollowerPlayer GetFollower(BotOwner b)=>FollowerData;public static bool IsFollower(BotOwner bot)=>bot?.Follower==true;}
 public static class Logger {public static void LogInfo(string s){}public static void LogError(string s){throw new Exception(s);}}
}
namespace pitTeam.Components {
 public enum FollowerCommandType {None,Regroup}
 public class BotFollowerPlayer {public FollowerCommandType Command;public float Until;public int MoveToPointIssueSequence,PushEnemyIssueSequence;
  public bool TryPeekActiveCommand(out FollowerCommandType c,out Vector3 p,out float until){c=Command;p=default;until=Until;return c!=FollowerCommandType.None;} }
}
/* PRODUCTION */
public class CustomNavigationPoint {public Vector3 Position;public bool Free=true,IsSpotted;public bool IsFreeById(int id)=>Free;}
public class CoreCoverUse {public BotOwner botOwner; /* USABLE METHOD */ }
public static class FollowerHazardChecks {
 static int count;static void Check(bool ok,string name){count++;if(!ok)throw new Exception(name);}
 static Vector3 V(float x,float y,float z)=>new Vector3(x,y,z);
 static BoxCollider Box(float x,float y,float z,float sx=4,float sy=2,float sz=4) {
  var matrix=Matrix4x4.Identity;matrix.m03=-x;matrix.m13=-y;matrix.m23=-z;
  return new BoxCollider{size=V(sx,sy,sz),bounds=new Bounds{center=V(x,y,z),extents=V(sx/2,sy/2,sz/2)},transform=new Transform{worldToLocalMatrix=matrix}};
 }
 static bool Stop(Player p,float x=0,float y=0,float z=.2f)=>FollowerHazardAvoidance.ShouldStop(p,V(x,y,z));
 static bool Plan(BotOwner b,bool allow,out Vector3[] route,out bool escaping){float next=0;return FollowerWireNavigation.TryPlan(b,allow,out route,out escaping,ref next);}
 static void Setup(TriggerZone[] zones,HandlerTriggerState[] switches,HandlerDamage[] damage,string map="labyrinth") {
  FollowerHazardAvoidance.Reset();UnityEngine.Object.Found.Clear();UnityEngine.Object.Scans=0;
  Singleton<GameWorld>.Instance=new GameWorld{LocationId=map};
  UnityEngine.Object.Found[typeof(TriggerZone)]=zones;UnityEngine.Object.Found[typeof(HandlerTriggerState)]=switches;UnityEngine.Object.Found[typeof(HandlerDamage)]=damage;
 }
 public static int Run() {
  var half=V(1,1,1);
  Check(FollowerHazardGeometry.BlocksEntry(V(0,0,-2),V(0,0,2),half),"swept segment catches crossing even if endpoint is beyond zone");
  Check(!FollowerHazardGeometry.BlocksEntry(V(2,0,-2),V(2,0,2),half),"parallel adjacent route stays clear");
  Check(!FollowerHazardGeometry.BlocksEntry(V(0,3,-2),V(0,3,2),half),"different floor stays clear");
  Check(FollowerHazardGeometry.BlocksEntry(V(0,0,-1.1f),V(0,0,-1),half),"boundary contact is blocked");
  Check(!FollowerHazardGeometry.BlocksEntry(V(0,0,-.9f),V(0,0,-1.1f),half),"inside can retreat out");
  Check(FollowerHazardGeometry.BlocksEntry(V(0,0,-.9f),V(0,0,-.8f),half),"inside cannot advance deeper");
  Check(!FollowerHazardGeometry.BlocksEntry(V(0,0,0),V(.1f,0,0),half),"bot at center can start escaping");
  Check(FollowerHazardGeometry.BlocksEntry(V(-.99f,0,.5f),V(-.97f,0,.6f),half),"diagonal cannot use radial growth to slip inward through nearest boundary");
  Check(!FollowerHazardGeometry.BlocksEntry(V(0,0,-1.1f),V(0,0,-2),half),"moving away outside stays allowed");
  var box=Box(0,.5f,6);var zone=new TriggerZone{_triggerId="toxicZoneEnter1_2612876931",Colliders=new[]{box}};
  var sw=new HandlerTriggerState{_controlledTriggerId=new[]{zone._triggerId}};var dmg=new HandlerDamage{_triggerIdStartDamage=zone._triggerId};
  Setup(new[]{zone},new[]{sw},new[]{dmg});var player=new Player{Position=V(0,0,3.3f)};
  Check(Stop(player),"live water entry stops before collider at body clearance");
  Check(UnityEngine.Object.Scans==4,"one discovery scan per component type including barbed wire");
  bool stable=true;for(int i=0;i<100;i++)stable&=Stop(player);Check(stable,"stable repeated hold");
  Check(UnityEngine.Object.Scans==4,"no repeated scene scan during hold");
  var emitter=Singleton<GameWorld>.Instance.TriggersEmitter;emitter._ignoreTriggers.Add(zone._triggerId);
  Check(!Stop(player),"native WaterRemove disable immediately releases movement");
  emitter._ignoreTriggers.Remove(zone._triggerId);Check(Stop(player),"reenabling trigger restores guard");
  box.enabled=false;Check(!Stop(player),"disabled collider releases guard");box.enabled=true;
  box.isTrigger=false;Check(Stop(player),"native steam volume without isTrigger still guarded");box.isTrigger=true;
  box.gameObject.activeInHierarchy=false;Check(!Stop(player),"disabled trap object releases guard");box.gameObject.activeInHierarchy=true;
  zone.isActiveAndEnabled=false;Check(!Stop(player),"disabled entry component releases guard");zone.isActiveAndEnabled=true;
  Check(!Stop(player,0,-.3f,0),"gravity alone is never blocked");
  Check(!Stop(player,0,0,-.2f),"movement away from zone is permitted");
  player.Position=V(0,0,3.5f);Check(!Stop(player,0,-.2f,-.2f),"inside safety margin can retreat while falling");Check(Stop(player),"inside safety margin cannot advance");
  player.Position=V(0,4,3.3f);Check(!Stop(player),"upstairs path is not stopped by downstairs collider");
  player.Position=V(0,-5,3.3f);Check(!Stop(player),"downstairs path is not stopped by upstairs collider");
  player.Position=V(0,0,2);Check(Stop(player,0,0,10),"large sprint displacement cannot tunnel through zone");
  player.Position=V(0,0,3.3f);player.IsAI=false;Check(!Stop(player),"human player unchanged");player.IsAI=true;
  player.AIData.BotOwner.Follower=false;Check(!Stop(player),"ordinary/dismissed bots unchanged");player.AIData.BotOwner.Follower=true;
  var hook=typeof(FollowerHazardMovementPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
  object[] args={player,V(0,-.02f,.2f)};hook.Invoke(null,args);var motion=(Vector3)args[1];
  Check(motion.x==0&&motion.z==0&&motion.y==-.02f,"physical execution stops horizontal displacement and preserves gravity");
  Check(!player.Sprinting&&player.ZeroMoves==1,"execution stops sprint and movement presentation");
  emitter._ignoreTriggers.Add(zone._triggerId);args=new object[]{player,V(.1f,-.02f,.2f)};hook.Invoke(null,args);motion=(Vector3)args[1];
  Check(motion.x==.1f&&motion.z==.2f&&motion.y==-.02f&&player.ZeroMoves==1,"switch off resumes normal native motion without another stop");
  // Rotated/nonuniform volume with a large AABB: narrow oriented check must reject false positives.
  var thin=Box(0,.5f,6,6,2,.2f);float a=.70710678f;var m=Matrix4x4.Identity;m.m00=a;m.m02=a;m.m20=-a;m.m22=a;m.m03=-6*a;m.m13=-.5f;m.m23=-6*a;thin.transform.worldToLocalMatrix=m;var thinBounds=thin.bounds;thinBounds.extents=V(2.2f,1,2.2f);thin.bounds=thinBounds;
  zone.Colliders=new[]{thin};Setup(new[]{zone},new[]{sw},new[]{dmg});player.Position=V(-2,0,8);Check(!Stop(player),"oriented narrow test excludes an empty AABB corner");
  player.Position=V(0,0,5);Check(Stop(player,0,0,1),"oriented narrow test blocks actual diagonal crossing");
  zone.Colliders=new[]{box};zone._triggerId="guntrap_trigger_123";sw._controlledTriggerId=new[]{zone._triggerId};
  Setup(new[]{zone},new[]{sw},Array.Empty<HandlerDamage>());player.Position=V(0,0,3.3f);Check(Stop(player),"gun-trap entry is guarded without direct HandlerDamage");
  zone._triggerId="alarm_enter_123";sw._controlledTriggerId=new[]{zone._triggerId};Setup(new[]{zone},new[]{sw},Array.Empty<HandlerDamage>());Check(!Stop(player),"alarm triggers are not damage zones");
  zone._triggerId="wire_enter_zone_123";dmg._triggerIdStartDamage=zone._triggerId;sw._controlledTriggerId=new[]{"other"};Setup(new[]{zone},new[]{sw},new[]{dmg});Check(!Stop(player),"unswitchable traps do not create permanent movement barriers");
  sw._controlledTriggerId=new[]{zone._triggerId};sw._targetState=true;Setup(new[]{zone},new[]{sw},new[]{dmg});Check(!Stop(player),"enable-only trigger handler is not classified as a trap switch");sw._targetState=false;
  Setup(new[]{zone},new[]{sw},new[]{dmg},"woods");Check(!Stop(player)&&UnityEngine.Object.Scans==0,"other maps perform no hazard or wire discovery");
  Setup(new[]{zone},new[]{sw},new[]{dmg},"6733700029c367a3d40b02af");Check(Stop(player),"native Labyrinth location id supported");
  Singleton<GameWorld>.Instance=new GameWorld{LocationId="woods"};Check(!Stop(player),"new world never retains previous map hazards");
  FollowerHazardAvoidance.Reset();Check(!Stop(null),"null actor is safe");
  // Installed Labyrinth water bounds, and the recorded walkway height before the failed entry.
  box=Box(-38.46725f,-.315857f,55.59258f,4.5f,1f,6f);
  zone=new TriggerZone{_triggerId="toxicZoneEnter1_2612876931",Colliders=new[]{box}};
  sw=new HandlerTriggerState{_controlledTriggerId=new[]{zone._triggerId}};dmg=new HandlerDamage{_triggerIdStartDamage=zone._triggerId};
  Setup(new[]{zone},new[]{sw},new[]{dmg});player.Position=V(-38.21f,.483774f,59.31f);
  Vector3 oldStart=player.Position+Vector3.up*.9f,oldEnd=oldStart+V(0,0,-.2f);var oldBroad=box.bounds;oldBroad.Expand(V(1.2f,1.8f,1.2f));
  Check(!FollowerHazardGeometry.Intersects(oldStart-oldBroad.center,oldEnd-oldBroad.center,oldBroad.extents),"reproduces old above-feet envelope missing water below walkway");
  Check(Stop(player,0,-.01f,-.2f),"installed water boundary stops approach before leaving elevated walkway");
  player.Position=V(-38.21f,4.483774f,59.31f);Check(!Stop(player,0,0,-.2f),"below-feet clearance still excludes a separate upper floor");
  player.Position=V(-38.21f,.483774f,59.31f);Singleton<GameWorld>.Instance.TriggersEmitter._ignoreTriggers.Add(zone._triggerId);
  Check(!Stop(player,0,0,-.2f),"drained water releases the corrected ledge boundary");
  RunOpenMapChecks();RunWireChecks();return count;
 }
 static void RunOpenMapChecks() {
  var box=Box(0,.3f,0,2,.6f,2);var wire=new BarbedWire{Attached=box};var player=new Player();var bot=player.AIData.BotOwner;bot.GetPlayer=player;bot.Position=V(0,0,0);bot.Mover.CurrentCornerPoint=V(0,0,4);bot.Medecine.FirstAid.Using=true;
  var contact=typeof(FollowerBarbedWireContactPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(string map in new[]{"woods","Shoreline","bigmap","RezervBase","Lighthouse","TarkovStreets","Interchange","Sandbox","factory4_day","laboratory"}) {
   Setup(Array.Empty<TriggerZone>(),Array.Empty<HandlerTriggerState>(),Array.Empty<HandlerDamage>(),map);UnityEngine.Object.Found[typeof(BarbedWire)]=new[]{wire};Singleton<GameWorld>.Instance.Player=player;
   int bounds=Collider.BoundsReads,components=MonoBehaviour.ComponentReads,queries=SainAddonBridge.Queries,paths=NavMesh.Calls,allocations=NavMeshPath.Allocations,lookups=GameWorld.Lookups,med=FollowerMedical.Cancelled,moves=bot.Mover.Paths;
   var layer=new FollowerWireLayer(bot,76);var action=new FollowerWireMoveAction(bot);
   for(int i=0;i<20;i++) {
    Check(!FollowerWireNavigation.IsCoverUnsafe(V(0,0,0))&&!FollowerWireNavigation.IsInside(V(0,0,0)),map+": no wire geometry or cover filtering");
    Check(!Plan(bot,true,out _,out _)&&!layer.IsActive()&&!Stop(player),map+": route, layer and trap guards inactive");
    FollowerHazardAvoidance.RegisterWire(wire);contact.Invoke(null,new object[]{wire,new WireBridge{iPlayer=player}});
    action.Update(new WireMoveData(new[]{V(0,0,0),V(3,0,0)},true));
   }
   Check(FollowerHazardAvoidance.GetWires().Count==0&&UnityEngine.Object.Scans==0,map+": zero scene scans and no cached wires");
   Check(Collider.BoundsReads==bounds&&MonoBehaviour.ComponentReads==components,map+": zero collider or component queries");
   Check(SainAddonBridge.Queries==queries&&NavMesh.Calls==paths&&NavMeshPath.Allocations==allocations,map+": zero SAIN/path queries or path allocations");
   Check(GameWorld.Lookups==lookups&&FollowerMedical.Cancelled==med&&bot.Mover.Paths==moves,map+": native contact and stale action cannot look up actors, cancel healing or move");
  }
  Check(NavMeshPath.Allocations==0,"unsupported maps never construct the lazy navigation path");
  Setup(Array.Empty<TriggerZone>(),Array.Empty<HandlerTriggerState>(),Array.Empty<HandlerDamage>(),"LABYRINTH");UnityEngine.Object.Found[typeof(BarbedWire)]=new[]{wire};
  Check(FollowerWireNavigation.IsCoverUnsafe(V(0,0,0)),"Labyrinth map name is case-insensitive and populates unsafe cover cache");
  var stale=FollowerHazardAvoidance.GetWires()[0];Singleton<GameWorld>.Instance.LocationId="woods";
  int oldScans=UnityEngine.Object.Scans,oldBounds=Collider.BoundsReads;
  Check(!FollowerWireNavigation.IsCoverUnsafe(V(0,0,0))&&!FollowerWireNavigation.Intersects(stale,V(0,0,0),V(0,0,3)),"same-world map change clears stale cover/geometry admission");
  Check(FollowerHazardAvoidance.GetWires().Count==0&&Collider.BoundsReads==oldBounds&&UnityEngine.Object.Scans==oldScans,"map transition clears cached volumes without scene or geometry queries");
  Singleton<GameWorld>.Instance.LocationId=null;Check(!FollowerHazardAvoidance.IsSupportedMap&&FollowerHazardAvoidance.GetWires().Count==0&&UnityEngine.Object.Scans==oldScans,"unknown location fails open without scans until identification becomes available");
  Singleton<GameWorld>.Instance.LocationId="6733700029c367a3d40b02af";
  Check(FollowerWireNavigation.IsInside(V(0,0,0)),"native Labyrinth id re-enables discovery after a location change");
  Singleton<GameWorld>.Instance=null;Check(!FollowerWireNavigation.IsInside(V(0,0,0))&&FollowerHazardAvoidance.GetWires().Count==0,"world teardown fails open and releases wire references");
 }
 static void RunWireChecks() {
  var wireBox=Box(0,.3f,0,2,.6f,2);var wire=new BarbedWire{Attached=wireBox};
  Setup(Array.Empty<TriggerZone>(),Array.Empty<HandlerTriggerState>(),Array.Empty<HandlerDamage>());
  UnityEngine.Object.Found[typeof(BarbedWire)]=new[]{wire};var p=new Player{Position=V(0,0,-4)};var b=p.AIData.BotOwner;b.GetPlayer=p;b.Position=p.Position;b.Mover.CurrentCornerPoint=V(0,0,4);
  var list=FollowerHazardAvoidance.GetWires();Check(list.Count==1,"barbed wire is discovered on Labyrinth");
  var usability=new CoreCoverUse{botOwner=b};var cover=new CustomNavigationPoint{Position=V(0,0,0)};
  Check(!usability.IsCoverUsable(cover),"Core rejects cover positions inside wire");
  Check(!usability.IsCoverUsable(cover,true),"ignore-spotted cannot bypass wire safety");
  cover.Position=V(0,0,-4);Check(usability.IsCoverUsable(cover),"Core retains safe cover outside wire");
  cover.Free=false;Check(!usability.IsCoverUsable(cover),"Core cover ownership remains authoritative");
  int farCalls=NavMesh.Calls;b.Position=V(0,0,-40);b.Mover.CurrentCornerPoint=V(0,0,4);Check(!Plan(b,false,out _,out _)&&NavMesh.Calls==farCalls,"distant wires add no navigation probes");b.Position=p.Position;
  Check(Plan(b,false,out var route,out var escaping)&&!escaping,"proactive complete local bypass found");
  Check(route.Length>=4&&Math.Abs(route[1].x)>1.3f,"bypass actually takes a side around the wire");
  bool safe=true;for(int i=1;i<route.Length;i++)safe&=!FollowerWireNavigation.Intersects(list[0],route[i-1],route[i]);Check(safe,"every bypass segment clears expanded wire body volume");
  Check(!Stop(p,0,0,5),"wire does not become a permanent movement barrier");
  SainAddonBridge.Handled=true;SainAddonBridge.Corner=V(0,0,4);b.HasPathAndNotComplete=false;
  Check(Plan(b,false,out _,out _),"addon-owned SAIN corner drives bypass even without a native EFT path");
  SainAddonBridge.Corner=null;Check(!Plan(b,false,out _,out _),"idle SAIN movement cannot use a stale native EFT route");SainAddonBridge.Handled=false;b.HasPathAndNotComplete=true;
  int scans=UnityEngine.Object.Scans;for(int i=0;i<20;i++)FollowerWireNavigation.IsInside(p.Position);Check(UnityEngine.Object.Scans==scans,"wire queries reuse scene cache");
  NavMesh.Partial=true;Check(!Plan(b,false,out _,out _),"partial paths cannot commit a bypass");NavMesh.Partial=false;
  NavMesh.ShiftY=2;Check(!Plan(b,false,out _,out _),"another floor cannot supply a bypass");NavMesh.ShiftY=0;
  NavMesh.MaxX=1;Check(!Plan(b,false,out _,out _),"narrow corridor with no side route keeps original movement");NavMesh.MaxX=1000;
  b.Mover.CurrentCornerPoint=V(0,0,0);Check(!Plan(b,false,out _,out _),"destination in wire is not replaced by a fake crossing target");
  b.Position=V(0,0,0);Check(FollowerWireNavigation.IsInside(b.Position),"standing body intersects low wire");
  Check(!Plan(b,false,out _,out _),"ordinary crossing is not turned into forced escape");
  Check(Plan(b,true,out route,out escaping)&&escaping&&!FollowerWireNavigation.IsInside(route[route.Length-1]),"medical escape ends fully outside wire");
  b.Position=V(0,4,0);Check(!FollowerWireNavigation.IsInside(b.Position),"wire on another floor cannot trigger escape");b.Position=V(0,0,0);
  wireBox.enabled=false;Check(!FollowerWireNavigation.IsInside(b.Position),"disabled wire collider releases overlap");wireBox.enabled=true;
  wire.isActiveAndEnabled=false;Check(!FollowerWireNavigation.IsInside(b.Position),"disabled wire component releases overlap");wire.isActiveAndEnabled=true;
  FollowerHazardAvoidance.RegisterWire(wire);Check(FollowerHazardAvoidance.GetWires().Count==1,"late contact deduplicates same wire collider");
  var late=new BarbedWire{Attached=Box(20,.3f,20)};FollowerHazardAvoidance.RegisterWire(late);Check(FollowerHazardAvoidance.GetWires().Count==2,"late spawned barbed wire can register on native contact");
  var spikeBox=Box(0,.15f,0,2,.2f,2);var zone=new TriggerZone{_triggerId="wire_enter_zone_1",Colliders=new[]{spikeBox}};
  Setup(new[]{zone},Array.Empty<HandlerTriggerState>(),new[]{new HandlerDamage{_triggerIdStartDamage=zone._triggerId}});
  Check(FollowerWireNavigation.IsInside(b.Position),"native Labyrinth spike wire participates without a switch");
  Check(FollowerWireNavigation.IsInside(V(0,.484f,0)),"recorded native bot feet height still detects low Labyrinth wire");
  Singleton<GameWorld>.Instance.TriggersEmitter._ignoreTriggers.Add(zone._triggerId);Check(!FollowerWireNavigation.IsInside(b.Position),"disabled spike trigger releases awareness");
  Singleton<GameWorld>.Instance.TriggersEmitter._ignoreTriggers.Clear();
  var layer=new FollowerWireLayer(b,76);Time.time=100;b.Medecine.FirstAid.Using=true;
  Check(layer.IsActive(),"medical use inside wire activates step-clear layer");var action=layer.GetNextAction();var move=new FollowerWireMoveAction(b);move.Start();int cancelled=FollowerMedical.Cancelled;move.Update(action.Data);
  Check(FollowerMedical.Cancelled==cancelled+1&&!b.Medecine.FirstAid.Using,"step-clear uses native medical cancellation once");
  Check(SainAddonBridge.Releases==1,"step-clear releases unsafe addon cover once before moving");
  Check(b.Mover.Paths==1&&!b.Mover.Pause,"step-clear issues one accepted route and unpauses mover");
  move.Update(action.Data);Check(b.Mover.Paths==1&&FollowerMedical.Cancelled==cancelled+1,"updates cannot recreate route or repeatedly cancel medicine");
  b.Position=V(4,0,0);Check(layer.IsCurrentActionEnding(),"clearing contact releases layer for normal healing");move.Stop();layer.Stop();
  b.Position=V(0,0,0);Time.time=100.5f;b.Medecine.FirstAid.Using=true;Check(!layer.IsActive(),"release cooldown prevents immediate layer churn");
  Time.time=103;b.BewareGrenade.Active=true;Check(!layer.IsActive(),"native grenade escape retains priority");b.BewareGrenade.Active=false;b.WeaponManager.Grenades.ThrowindNow=true;Check(!layer.IsActive(),"throw hands retain ownership");b.WeaponManager.Grenades.ThrowindNow=false;
  b.Follower=false;Check(!layer.IsActive(),"ordinary bots are excluded");b.Follower=true;
  b.Medecine.FirstAid.Using=false;b.Medecine.FirstAid.Have2Do=false;Check(!new FollowerWireLayer(b,76).IsActive(),"uninjured ordinary crossing keeps its existing brain");
  var stalled=new FollowerWireLayer(b,76);b.Medecine.FirstAid.Using=true;Check(stalled.IsActive(),"second legitimate medical escape can start");stalled.GetNextAction();Time.time=107;Check(stalled.IsCurrentActionEnding(),"unproductive escape has bounded progress timeout");stalled.Stop();
  b.Position=V(0,0,-4);b.Mover.CurrentCornerPoint=V(0,0,4);b.Medecine.FirstAid.Using=false;var bypass=new FollowerWireLayer(b,76);Check(bypass.IsActive(),"shared layer admits proactive bypass");var command=bypass.GetNextAction();Check(command.Reason=="wireBypass","bypass is distinct from medical escape");
  int before=NavMesh.Calls;for(int i=0;i<100;i++)bypass.IsActive();Check(NavMesh.Calls==before,"committed bypass cannot replan each frame");
  BossPlayers.Instance.FollowerData.Command=FollowerCommandType.Regroup;Check(bypass.IsCurrentActionEnding(),"new player order interrupts a proactive bypass without clearing that order");bypass.Stop();BossPlayers.Instance.FollowerData.Command=FollowerCommandType.None;
  Time.time=110;NavMesh.Available=false;var failed=new FollowerWireLayer(b,76);Check(!failed.IsActive(),"unavailable route yields to existing brain");before=NavMesh.Calls;for(int i=0;i<100;i++)failed.IsActive();Check(NavMesh.Calls==before,"failed bypass cannot retry planning every frame");NavMesh.Available=true;
  Singleton<GameWorld>.Instance=new GameWorld{LocationId="woods"};UnityEngine.Object.Found.Clear();Check(!FollowerWireNavigation.IsInside(V(0,0,0)),"new world clears cached wire geometry");
 }
}
