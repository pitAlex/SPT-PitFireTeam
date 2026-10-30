using UnityEngine;
using UnityEngine.AI;
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
var bot=new BotOwner();var enemy=new Player();var actual=new Vector3(20,1,0);var heard=new Vector3(-20,1,0);
BossPlayers.Current=enemy;
Time.time=100;Awareness.Reset();Awareness.Register(bot,heard,enemy,false);
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"hearing cannot authorize suppression");
Check(!Awareness.WasRecentlyThreatened(bot),"hearing cannot pose as a recent hit or force recovery");
Awareness.Register(bot,actual,enemy,true);
Check(Awareness.TryGetRecentFireThreatLookPoint(bot,out var point,out _)&&point.x==20,"actual incoming fire authorizes a short bearing");
Check(Awareness.WasRecentlyThreatened(bot),"actual incoming fire retains recovery pressure");
Time.time=102;Awareness.Register(bot,heard,enemy,false);
Check(Awareness.TryGetRecentFireThreatLookPoint(bot,out point,out _)&&point.x==20,"heard sound cannot overwrite an incoming-fire target");
Time.time=103.01f;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"hearing cannot extend incoming-fire permission");
Time.time=110;Awareness.Register(bot,actual,enemy,true);enemy.HealthController.IsAlive=false;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out var dead)&&dead,"known source death cancels suppression immediately");
enemy.HealthController.IsAlive=true;enemy.gameObject.activeInHierarchy=false;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"despawned source cannot authorize suppression");
enemy.gameObject.activeInHierarchy=true;enemy.Hostile=false;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"source becoming friendly cancels suppression");
enemy.Hostile=true;enemy.IsFollower=true;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"squad source cannot authorize suppression");
enemy.IsFollower=false;enemy.IsBoss=true;
Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"human leader cannot authorize suppression");
Awareness.Register(bot,actual,null,true);Check(Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"unknown-source hit retains short defensive evidence");
Time.time=114;Check(!Awareness.TryGetRecentFireThreatLookPoint(bot,out _,out _),"unknown-source evidence also expires");
foreach(var role in new[]{Role.bossKnight,Role.followerBigPipe,Role.followerBirdEye,Role.pmcUSEC})
{
    bot.Profile.Info.Settings.Role=role;
    Check(SainAddonBridge.IsSainManSelected(bot)==(role==Role.pmcUSEC),"SAIN command selection respects native companion owner: "+role);
    Check(SainAddonBridge.IsAddonTacticSelected(bot)==(role==Role.pmcUSEC),"SAIN runtime preparation respects native companion owner: "+role);
}
GameplayModeAddons.Enabled=false;bot.Profile.Info.Settings.Role=Role.bossKnight;
Check(SainAddonBridge.IsAddonTacticSelected(bot),"absent companion owner retains the ordinary readiness path");
foreach(string fault in new[]{"none","playerFloor","candidateFloor","navBoundary","noGround","badSupport","blockedCapsule","overlap"})
{
    NavMesh.Fault=fault;NavMesh.Samples=0;Physics.Fault=fault;
    var reserved=new List<Vector3>{Vector3.zero};
    if(fault=="overlap")reserved.Add(new(2.25f,0,0));
    bool found=Teleport.Find(Vector3.zero,reserved,out var target);
    Check(found==(fault=="none"||fault=="overlap"),"teleport validation: "+fault);
    if(found)Check(Math.Abs(target.y)<.01f&&(target-Vector3.zero).sqrMagnitude>=4,"teleport remains on player floor with spacing");
    Check(NavMesh.Samples<=25,"teleport search stays bounded");
}
Console.WriteLine($"PASS: {checks} extracted production incoming-fire and teleport checks; physics/NavMesh stand-ins.");

public class BotOwner { public Enemies EnemiesController=new();public Profile Profile=new(); }
public class Profile {public Info Info=new();}
public class Info {public Settings Settings=new();}
public class Settings {public Role Role;}
public enum Role {bossKnight,followerBigPipe,followerBirdEye,pmcUSEC}
public enum FollowerCombatTactic {SainMan}
public class Follower {public FollowerCombatTactic CombatTactic;}
public static class FollowerCombatTactics {public static bool UsesSainCombat(FollowerCombatTactic tactic)=>true;}
public static class pitFireTeam {public static bool IsSainManTacticAvailable=true;}
public static class GameplayModeAddons {public static bool Enabled=true;public static bool OwnsCompanionCombat(string role)=>Enabled&&role is "bossKnight" or "followerBigPipe" or "followerBirdEye";}
public class Enemies {public bool IsEnemy(Player p)=>p.Hostile;}
public class Player {public string ProfileId=>"source";public bool Hostile=true,IsFollower,IsBoss;public Health HealthController=new();public GameObject gameObject=new();}
public class Health {public bool IsAlive=true;}
public class GameObject {public bool activeInHierarchy=true;}
public static class BossPlayers {public static bool IsPlayerBoss(string id)=>Current?.IsBoss==true;public static bool IsFollowerProfileId(string id)=>Current?.IsFollower==true;public static Player Current;
public static Registry Instance=new();public class Registry {public Follower GetFollower(BotOwner owner)=>new();}
}
public static class FollowerCombatCommon {public static bool IsFinite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);}
public static class LayersMaskController {public const int HighPolyWithTerrainMask=1;}
namespace UnityEngine {
 public static class Time {public static float time;}
 public static class Mathf {public const float Deg2Rad=MathF.PI/180;public static float Abs(float v)=>MathF.Abs(v);public static float Cos(float v)=>MathF.Cos(v);public static float Sin(float v)=>MathF.Sin(v);}
 public static class Random {public static float Range(float a,float b)=>0;}
 public struct Vector3(float x,float y,float z) {public float x=x,y=y,z=z;public static Vector3 zero=>new();public static Vector3 up=>new(0,1,0);public static Vector3 down=>new(0,-1,0);public float sqrMagnitude=>x*x+y*y+z*z;public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);public override bool Equals(object o)=>o is Vector3 v&&this==v;public override int GetHashCode()=>HashCode.Combine(x,y,z);}
 public struct RaycastHit {public Vector3 point;}
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {public static string Fault;public static bool Raycast(Vector3 p,Vector3 d,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction trigger){hit=new(){point=p+Vector3.down*(Fault=="badSupport"?1f:.35f)};return Fault!="noGround";}public static bool CheckCapsule(Vector3 a,Vector3 b,float radius,int mask,QueryTriggerInteraction trigger)=>Fault=="blockedCapsule";}
}
namespace UnityEngine.AI {
 public struct NavMeshHit {public Vector3 position;}
 public static class NavMesh {public const int AllAreas=-1;public static string Fault;public static int Samples;public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,int areas){Samples++;hit=new(){position=new(p.x,Fault=="playerFloor"||Fault=="candidateFloor"&&Samples>1?-3:0,p.z)};return true;}public static bool Raycast(Vector3 a,Vector3 b,out NavMeshHit hit,int area){hit=default;return Fault=="navBoundary";}}
}
