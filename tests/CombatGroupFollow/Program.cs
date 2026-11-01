using UnityEngine;
using UnityEngine.AI;

internal static class Program
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Main()
    {
        foreach(string mode in new[]{"safe","far","near","unknown","dead","nan","sample","height","partial","fireLane","noPath","shiftTooFar","noFloor","steepFloor","wrongFloor","stair"})
        {
            NavMesh.Reset();var p=new Planner();var enemy=new EnemyInfo{Known=mode!="unknown",Alive=mode!="dead"};
            var lead=new Vector3(mode=="near"?3:mode=="far"?50:15,0,0);
            if(mode=="nan")lead.x=float.NaN;
            NavMesh.Sample=mode!="sample";NavMesh.Complete=mode!="partial";NavMesh.Path=mode!="noPath";
            NavMesh.Shift=mode=="height"?new(0,2,0):mode=="shiftTooFar"?new(2,0,0):default;p.BlockLane=mode=="fireLane";
            Physics.SetMode(mode);
            if(mode=="shiftTooFar")lead=new(50,0,0);
            bool success=p.TryPlanCombatFollowDecision(enemy,lead,"groupFollow",out var point,out var decision);
            Check(success==(mode is "safe" or "far" or "stair"),"bounded safe group-follow admission: "+mode);
            Check(p.botOwner.Position.sqrMagnitude==0,"planning never moves or teleports the actor: "+mode);
            if(success)
            {
                Check(point.x<lead.x && Vector3.Distance(point,lead)>=2,"follow point stays behind and clear of leader");
                Check(point.sqrMagnitude<=400 && decision.Action==BotLogicDecision.goToPoint,"bounded travel uses ordinary movement owner");
            }
        }
        foreach(string mode in new[]{"safe","far","standoff","unknown","dead","sample","height","partial","fireLane","noPath","noFloor","steepFloor","wrongFloor","stair"})
        {
            NavMesh.Reset();var p=new Planner();var enemy=new EnemyInfo{Known=mode!="unknown",Alive=mode!="dead"};
            FollowerEnemyTracking.Anchor=new(mode=="far"?500:mode=="standoff"?61:100,0,0);
            NavMesh.Sample=mode!="sample";NavMesh.Complete=mode!="partial";NavMesh.Path=mode!="noPath";
            NavMesh.Shift=mode=="height"?new(0,2,0):default;p.BlockLane=mode=="fireLane";
            Physics.SetMode(mode);
            bool result=p.TryPlanContactAdvanceDecision(enemy,60,"pursuit",out var point,out var decision);
            Check(result==(mode is "safe" or "far" or "stair"),"contact advance preserves admission/navigation/standoff: "+mode);
            Check(p.botOwner.Position.sqrMagnitude==0,"contact planning remains non-publishing: "+mode);
            if(result)Check(point.x==20 && Vector3.Distance(point,FollowerEnemyTracking.Anchor)>=60,"even a 500m contact yields only a bounded 20m step");
        }
        foreach(string mode in new[]{"safe","noFloor","wrongFloor","steepFloor","stair","nanHit"})
        {
            Physics.SetMode(mode);
            Check(Planner.HasSupportedCombatGround(new(2,3,4))==(mode is "safe" or "stair"),"physical floor admission: "+mode);
            Check(Physics.LastMask==LayersMaskController.PlayerCollisionsMask && Physics.LastTriggers==QueryTriggerInteraction.Ignore,
                "floor query uses native player collision mask and ignores triggers");
        }
        Physics.Calls=0;
        Check(!Planner.HasSupportedCombatGround(new(float.NaN,0,0)) && Physics.Calls==0,"invalid candidate never reaches physics");
        Console.WriteLine($"PASS: {checks} extracted Core group-follow/contact-advance navigation checks; real Unity navigation remains unqualified.");
    }
}
class EnemyInfo {public bool Known=true,Alive=true;}
class BotOwner {public Vector3 Position=default;}
static class LayersMaskController {public const int PlayerCollisionsMask=1234;}
enum BotLogicDecision {goToPoint}
class CoreActionResultParams {}
struct AICoreActionResult<T,D>(T action,string reason) {public T Action=action;public string Reason=reason;}
partial class Planner
{
    internal BotOwner botOwner=new();internal bool BlockLane;
    bool HasActiveCombatEnemy(EnemyInfo e)=>e?.Alive==true;
    static bool IsFinite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
    bool IsBossFireLaneMovementRisk(Vector3 p,Vector3 a,bool includePath)=>BlockLane;
}
static class FollowerEnemyTracking
{
    public static Vector3 Anchor=new(100,0,0);
    public static bool TryGetKnownPosition(EnemyInfo e,out Vector3 p,out string reason){p=Anchor;reason="report";return e.Known;}
}
namespace UnityEngine
{
    public struct Vector3(float x,float y,float z)
    {
        public float x=x,y=y,z=z;public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);
        public static Vector3 right=>new(1,0,0);
        public static Vector3 up=>new(0,1,0);public static Vector3 down=>new(0,-1,0);
        public Vector3 normalized{get{float n=MathF.Sqrt(sqrMagnitude);return new(x/n,y/n,z/n);}}
        public static Vector3 operator+(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator*(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
        public static Vector3 MoveTowards(Vector3 a,Vector3 b,float max)=>Distance(a,b)<=max?b:a+(b-a).normalized*max;
    }
    public static class Mathf{public static float Abs(float x)=>MathF.Abs(x);public static float Min(float a,float b)=>MathF.Min(a,b);}
    public enum QueryTriggerInteraction {Ignore}
    public struct RaycastHit {public Vector3 point,normal;}
    public static class Physics
    {
        public static string Mode="safe";public static int LastMask,Calls;public static QueryTriggerInteraction LastTriggers;
        public static void SetMode(string mode)=>Mode=mode;
        public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction triggers)
        {
            Calls++;LastMask=mask;LastTriggers=triggers;
            if(direction.y!=-1 || distance!=1f)throw new Exception("unexpected ground query geometry");
            float offset=Mode=="wrongFloor"?-1:Mode=="stair"?-0.2f:0;
            hit=new(){point=new(origin.x,Mode=="nanHit"?float.NaN:origin.y-0.5f+offset,origin.z),normal=new(0,Mode=="steepFloor"?0.2f:1,0)};
            return Mode!="noFloor";
        }
    }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus{PathComplete,PathPartial}
    public class NavMeshPath{public NavMeshPathStatus status;}
    public struct NavMeshHit{public Vector3 position;}
    public static class NavMesh
    {
        public const int AllAreas=-1;public static bool Sample=true,Complete=true,Path=true;public static Vector3 Shift;
        public static void Reset(){Sample=Complete=Path=true;Shift=default;}
        public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,int mask){hit=new(){position=p+Shift};return Sample;}
        public static bool CalculatePath(Vector3 a,Vector3 b,int mask,NavMeshPath path){path.status=Complete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return Path;}
    }
}
