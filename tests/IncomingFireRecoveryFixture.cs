// Production awareness, fire overlay and recovery ranking against controlled geometry/native inputs.
// This verifies handoff and safety; it does not simulate EFT perception, physics or navigation.
#nullable enable annotations
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using EFT;
using Comfort.Common;
using pitTeam.BigBrain;
using pitTeam.BigBrain.Actions;
using pitTeam.Utils;
using pitTeam.Modules;
using Random = UnityEngine.Random;
namespace UnityEngine {
    public struct Vector3 {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1,0); public static Vector3 back=>new Vector3(0,0,-1);
        public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>(float)Math.Sqrt(sqrMagnitude); public Vector3 normalized=>magnitude>0?this/magnitude:zero;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>(a-b).sqrMagnitude<0.000001;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object b)=>b is Vector3 v&&this==v; public override int GetHashCode()=>x.GetHashCode();
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static float Angle(Vector3 a,Vector3 b)=>0;
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*t;
    }
    public static class Time { public static float time; }
    public static class Random { public static Vector3 onUnitSphere=>new Vector3(1,0,0); }
    public static class Mathf { public static float Sqrt(float a)=>(float)Math.Sqrt(a); public static float Clamp(float a,float b,float c)=>Math.Max(b,Math.Min(c,a)); public static float Exp(float a)=>(float)Math.Exp(a); }
}
namespace Comfort.Common { public static class Singleton<T> where T:new() { public static T Instance=new T(); } }
namespace EFT {
    public enum EBotState { Active,Inactive }
    public class Player { public string ProfileId="guard"; public Health HealthController=new Health(); public Transform Transform=new Transform(); public GameObject gameObject=new GameObject(); }
    public class Health { public bool IsAlive=true; }
    public class GameObject { public bool activeInHierarchy=true; }
    public class Transform { public Vector3 position; }
    public class GameWorld { public Player Source; public Player GetAlivePlayerByProfileID(string id)=>Source!=null&&Source.ProfileId==id?Source:null; }
    public class EnemyInfo {
        public Player Person=new Player(); public string ProfileId=>Person.ProfileId; public bool IsVisible,CanShoot;
        public Vector3 PersonalLastPos,EnemyLastPositionReal,EnemyLastPosition,BodyPoint; public float PersonalLastSeenTime; public BotGroupEnemyInfo GroupInfo=new BotGroupEnemyInfo();
        public Vector3 GetBodyPartPosition()=>BodyPoint; public Vector3 GetVisiblePartToShoot()=>BodyPoint;
    }
    public class BotGroupEnemyInfo { public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense; }
    public class BotOwner {
        public static Vector3 STAY_HEIGHT=new Vector3(0,1.2f,0); public Vector3 Position; public bool IsDead,Sprinting;
        public EBotState BotState=EBotState.Active; public Hostility EnemiesController=new Hostility(); public BotsGroup BotsGroup=new BotsGroup();
        public BotWeaponManager WeaponManager=new BotWeaponManager(); public ShootData ShootData=new ShootData(); public Memory Memory=new Memory();
        public LookSensor LookSensor=new LookSensor(); public Steering Steering=new Steering(); public AimManager AimingManager=new AimManager();
    }
    public class Hostility { public bool Hostile=true; public bool IsEnemy(Player p)=>Hostile; }
    public class BotsGroup { public enum BotCurrentTactic { Attack,Ambush } public bool IsEnemy(Player p)=>false; }
    public class Memory { public EnemyInfo GoalEnemy; public CoverInfo BotCurrentCoverInfo=new CoverInfo(); }
    public class CoverInfo { public CustomNavigationPoint Selected; public int Writes; public void Spotted(){} public void SetCover(CustomNavigationPoint p,bool b){Selected=p;Writes++;} }
    public class LookSensor { public int Mask; }
    public class Steering { public void LookToPoint(Vector3 p){} }
    public class BotWeaponManager { public Reload Reload=new Reload(); public bool HaveBullets=true,IsWeaponReady=true; public ShootController ShootController=new ShootController(); }
    public class Reload { public bool Reloading; }
    public class ShootController { public void SetTriggerPressed(bool b){} }
    public class ShootData { public bool Shooting,CanShootByState=true; public int Shots; public bool Shoot(){Shots++;Shooting=true;return true;} public void EndShoot(){Shooting=false;} }
    public interface IBotAiming { bool IsReady{get;} void SetTarget(Vector3 p); }
    public class Aiming:IBotAiming { public bool Ready=true; public bool IsReady=>Ready; public Vector3 Target; public void SetTarget(Vector3 p){Target=p;} }
    public class AimManager { public IBotAiming CurrentAiming=new Aiming(); public void NodeUpdate(){} }
    public class ShootToPoint { public Vector3 Point; public ShootToPoint(Vector3 p,float f){Point=p;} }
    public class CustomNavigationPoint { public int Id; public Vector3 Position; public bool CanIShootToEnemy,Usable=true,Hard=true; public float NavDistance; }
}
namespace EFT.Ballistics { public class Shot { public string PlayerProfileID="guard"; public Vector3 HitPoint,CurrentPosition; public ShotPlayer Player; } public class ShotPlayer { public Player iPlayer; } }
namespace pitTeam.Modules {
    public static class FollowerEnemyEnforceSuppression { public static bool IsSuppressed(BotOwner b)=>false; }
    public static class BossPlayers { public static bool IsPlayerBoss(string id)=>id=="human"; public static bool IsFollowerProfileId(string id)=>id=="ally"; }
    public static class FollowerEnemyTracking { public static Vector3 Position(EnemyInfo e)=>e.EnemyLastPositionReal; public static ShootToPoint CoverTarget(BotOwner b)=>new ShootToPoint(Vector3.zero,1); }
    public static class BattleRecorder { public static bool IsRecordingFor(BotOwner b,bool requireRecordedCombat)=>false; public static void RecordCombatFireEvent(params object[] args){} public static void RecordCommitmentEvent(BotOwner b,string commitment,string action,string reason,Vector3 target,int coverId){} }
}
namespace pitTeam.Utils {
    public static class Utils { public static bool Direct=true; public static bool CanShootToTarget(ShootToPoint p,Vector3 origin,int mask,bool b)=>Direct; public static bool CanShootToTarget(ShootToPoint p,CustomNavigationPoint point,int mask,bool b)=>point.CanIShootToEnemy; }
    public static class Covers {
        public static HashSet<float> Unsafe=new HashSet<float>(); public static int Probes;
        public static bool IsPathTooCloseToEnemy(Vector3 start,Vector3 point,Vector3 enemy,float distance){Probes++;return Unsafe.Contains(point.x);}
        public static bool IsPathExposedToEnemy(Vector3 start,Vector3 point,Vector3 enemy,int mask,int sampleCount)=>false;
    }
    public static class FollowerAwareness {
        public sealed class State { public float IncomingFireUntil,NextBulletReactionAt,ThreatLookUntil,ThreatSourceDistance,ThreatenedUntil; public Vector3 IncomingFirePoint,ThreatLookPoint; public bool HasIncomingFireSource,HasThreatLookPoint; public Player IncomingFireSource; }
        public static ConditionalWeakTable<BotOwner,State> States=new ConditionalWeakTable<BotOwner,State>();
        private static State GetState(BotOwner b)=>b==null?null:States.GetValue(b,_=>new State());
        public static bool Protected; private static bool IsImpactProtectedByHardCover(BotOwner b,Vector3 p)=>Protected;
        private static bool IsHostileToBossGroup(BotOwner b,Player p)=>false;
        private static bool TryAutoAcquireCloseThreat(BotOwner b,Player p,float d)=>false;
        private static bool CanBotShootEnemy(BotOwner b,Player p)=>false;
        private static bool TryAcquireVisibleHostileOfBossGroup(BotOwner b,Player p)=>false;
        private static bool FakeShot(BotOwner b,Vector3 p,float sourceDistance=float.MaxValue,Player threatSource=null)=>false;
        /* AWARENESS */
    }
}
namespace pitTeam.BigBrain {
    public enum CoverSearchType { Local } public enum CoverShootType { shoot,hide } public enum CoverSearchIntent { RunToCover }
    public class CombatDistanceConfiguration { public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration(); public float GetBulletHearDistanceSqr()=>100; public float GetBulletImpactDispersionSqr()=>10; public float GetCloseQuarterDistance()=>15; }
    public class FollowerCombatCommon {
        public static bool SoftLane; public static bool IsSoftObstructedSuppressionLane(Vector3 a,Vector3 b,int m)=>SoftLane;
        public static bool IsFinite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
        private readonly BotOwner botOwner; public FollowerCombatCommon(BotOwner b){botOwner=b;}
        public List<CustomNavigationPoint> Candidates=new List<CustomNavigationPoint>(); public int Scans; public int Blocked=-1; private bool lastAssignedRetreatCoverWasWeak;
        private const float CombatCoverCenterDistanceWeight=1;
        private Vector3 GetBossPosition()=>botOwner.Position; private Vector3 GetEnemyAnchor(EnemyInfo e)=>e.EnemyLastPositionReal;
        private bool IsCoverUsable(CustomNavigationPoint p,bool spotted)=>p.Usable;
        private bool IsBlockedRecoveryCover(CustomNavigationPoint p)=>p.Id==Blocked;
        private float GetEvaluatedCoverNavDistance(CustomNavigationPoint p)=>p.NavDistance;
        private CoverSearchType SetCoverTacticAndGetSearchType(BotsGroup.BotCurrentTactic t,CoverShootType s,CoverSearchIntent i)=>CoverSearchType.Local;
        private CustomNavigationPoint SelectBestEvaluatedCover(Vector3 anchor,float radius,CoverSearchType type,Func<CustomNavigationPoint,bool> eligible,Func<CustomNavigationPoint,float> score){Scans++;CustomNavigationPoint best=null;float value=float.MaxValue;foreach(var p in Candidates)if(Vector3.Distance(p.Position,anchor)<=radius&&eligible(p)&&score(p)<value){best=p;value=score(p);}return best;}
        private CustomNavigationPoint SelectBestThreatCover(Vector3 anchor,float radius,CoverSearchType type,Vector3 enemy,string id,Func<CustomNavigationPoint,bool> eligible,Func<CustomNavigationPoint,float> score,bool weak,out bool usedWeak){var p=SelectBestEvaluatedCover(anchor,radius,type,x=>eligible(x)&&x.Hard,score);usedWeak=false;if(p==null&&weak){p=SelectBestEvaluatedCover(anchor,radius,type,eligible,score);usedWeak=p!=null;}return p;}
        public bool Recover(EnemyInfo e)=>TrySelectRetreatAttackCover(e,false,400,false,true,true);
        /* RECOVERY */
    }
    public static class FollowerImmediateFirePolicy { public static bool CanUseRecentContactSuppress(EnemyInfo e)=>false; public static Vector3 GetRecentContactSuppressTarget(EnemyInfo e)=>Vector3.zero; }
}
namespace pitTeam.BigBrain.Actions {
    public class FollowerEmergencyFireGate { public void Reset(){} public bool TryFire(BotOwner bot,EnemyInfo enemy,Vector3 target,string action,string reason,bool suppression,out bool firing){firing=false;return false;} }
    public static class FollowerCombatActionBase { public static bool IsActuallySprinting(BotOwner b)=>b.Sprinting; public static void TryGetCurrentShotVector(BotOwner b,out Vector3 origin,out Vector3 direction){origin=b.Position+BotOwner.STAY_HEIGHT;direction=new Vector3(1,0,0);} }
    public static class FollowerShotSafety {
        public static bool Friendly,ActualFriendly,AimFriendly; public static int Checks;
        public static bool IsFriendlyInSuppressionLane(BotOwner b,Vector3 origin,Vector3 target)=>++Checks==1?Friendly:ActualFriendly;
        public static bool IsFriendlyInShotLane(BotOwner b,Vector3 origin,Vector3 target)=>Friendly;
        public static bool IsFriendlyInAimLane(BotOwner b,Vector3 origin,Vector3 direction,float distance)=>AimFriendly;
    }
}
public static class IncomingFireRecoveryChecks {
    private static int count; private static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    private static EnemyInfo Setup(out BotOwner bot){Time.time=100;bot=new BotOwner();var e=new EnemyInfo{EnemyLastPositionReal=new Vector3(40,-62,50)};e.Person.Transform.position=e.EnemyLastPositionReal;Singleton<GameWorld>.Instance.Source=e.Person;bot.Memory.GoalEnemy=e;return e;}
    public static int Run(){
        var enemy=Setup(out var bot);var bullet=new EFT.Ballistics.Shot{HitPoint=new Vector3(1,0,1)};
        FollowerAwareness.BulletFelt(bot,bullet);var state=FollowerAwareness.States.GetOrCreateValue(bot);
        var expected=enemy.Person.Transform.position+BotOwner.STAY_HEIGHT+new Vector3(0.2f,0,0);
        Check(state.IncomingFirePoint==expected,"bullet bearing has firing height and retains planar dispersion");
        Check(FollowerSuppressTargetPolicy.TryGetTargetForOwner(bot,enemy,out var target)&&target==expected,"no visual report can use same-source incoming fire");
        enemy.Person.Transform.position=new Vector3(90,20,30);
        Check(FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out target)&&target==expected,"frozen report does not track hidden live movement");
        var other=new EnemyInfo();other.Person.ProfileId="other";
        Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,other,out _),"incoming fire cannot retarget another enemy's suppress action");
        enemy.Person.HealthController.IsAlive=false;
        Check(!FollowerSuppressTargetPolicy.TryGetTargetForOwner(bot,enemy,out _),"dead source cannot supply fire permission");enemy.Person.HealthController.IsAlive=true;
        enemy.Person.gameObject.activeInHierarchy=false;Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"inactive source rejected");enemy.Person.gameObject.activeInHierarchy=true;
        bot.EnemiesController.Hostile=false;Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"no longer hostile source rejected");bot.EnemiesController.Hostile=true;
        Time.time=103;Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"exact three-second incoming-fire expiry rejected");Time.time=100;
        state.IncomingFirePoint=new Vector3(float.NaN,0,0);Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"invalid point rejected");state.IncomingFirePoint=expected;
        state.HasIncomingFireSource=false;Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"unknown source cannot enter target-scoped action");state.HasIncomingFireSource=true;
        enemy.IsVisible=true;enemy.BodyPoint=new Vector3(10,1,10);Check(FollowerSuppressTargetPolicy.TryGetTargetForOwner(bot,enemy,out target)&&target==enemy.BodyPoint,"direct visual body retains priority");enemy.IsVisible=false;
        foreach(var gate in new[]{"clear","hard","friendly","actualFriendly","aimFriendly","aimNotReady","reload","empty","sprint"}){
            enemy=Setup(out bot);FollowerAwareness.BulletFelt(bot,bullet);Utils.Direct=gate!="hard";FollowerCombatCommon.SoftLane=false;
            FollowerShotSafety.Checks=0;FollowerShotSafety.Friendly=gate=="friendly";FollowerShotSafety.ActualFriendly=gate=="actualFriendly";FollowerShotSafety.AimFriendly=gate=="aimFriendly";
            ((Aiming)bot.AimingManager.CurrentAiming).Ready=gate!="aimNotReady";bot.WeaponManager.Reload.Reloading=gate=="reload";bot.WeaponManager.HaveBullets=gate!="empty";bot.Sprinting=gate=="sprint";
            var overlay=new FollowerCombatFireOverlay(bot);overlay.Update(enemy,"recovery.noCoverThreatHold",true,true,out var fired);
            Check(fired==(gate=="clear")&&bot.ShootData.Shots==(gate=="clear"?1:0),"production overlay gate "+gate);
        }
        enemy=Setup(out bot);FollowerAwareness.Protected=true;FollowerAwareness.BulletFelt(bot,bullet);Check(!FollowerAwareness.TryGetIncomingFireSuppressTarget(bot,enemy,out _),"protected impact grants no suppression");FollowerAwareness.Protected=false;
        var unsafeCover=new CustomNavigationPoint{Id=1,Position=new Vector3(1,0,0),NavDistance=1};var safe=new CustomNavigationPoint{Id=2,Position=new Vector3(-5,0,0),NavDistance=5};
        Covers.Unsafe.Add(1);var common=new FollowerCombatCommon(bot);common.Candidates.AddRange(new[]{unsafeCover,safe});
        Check(common.Recover(enemy)&&bot.Memory.BotCurrentCoverInfo.Selected==safe,"unsafe nearest retreat cannot hide valid alternative");
        Check(bot.Memory.BotCurrentCoverInfo.Writes==1,"only selected safe destination publishes cover");
        Check(common.TryAssignRetreatAttackCover(enemy,false,400)&&bot.Memory.BotCurrentCoverInfo.Selected==unsafeCover,"ordinary non-recovery ranking remains unchanged");
        common.Blocked=2;var writes=bot.Memory.BotCurrentCoverInfo.Writes;Check(!common.Recover(enemy)&&bot.Memory.BotCurrentCoverInfo.Writes==writes,"unsafe and previously failed destinations do not publish");
        common.Blocked=-1;safe.Hard=false;Check(common.Recover(enemy)&&bot.Memory.BotCurrentCoverInfo.Selected==safe,"existing weak fallback still respects safe route");
        common.Candidates.Clear();Covers.Unsafe.Clear();Covers.Probes=0;
        for(int i=1;i<=5;i++){common.Candidates.Add(new CustomNavigationPoint{Id=i,Position=new Vector3(i,0,0),NavDistance=i});if(i<5)Covers.Unsafe.Add(i);}
        writes=bot.Memory.BotCurrentCoverInfo.Writes;
        Check(!common.Recover(enemy)&&Covers.Probes==4&&bot.Memory.BotCurrentCoverInfo.Writes==writes,"route search is bounded and never publishes an untested fifth candidate");
        Console.WriteLine($"PASS: {count} incoming-fire, production overlay safety and retreat-ranking checks. Controlled geometry; live EFT remains unqualified.");return count;
    }
}
