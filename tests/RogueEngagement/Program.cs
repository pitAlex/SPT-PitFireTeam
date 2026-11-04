using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static void Main()
    {
        foreach(bool independent in new[]{false,true})
        {
            var bot=new BotOwner{Position=new(100,0,0)};
            var leader=new BotOwner{Position=new(101.7f,0,0)};
            var common=new FollowerCombatCommon{Aggression=independent?.68f:.60f};
            var policy=new FollowerCombatRiflemanEngagement(bot,common);
            var enemy=new EnemyInfo{Position=new(300,0,0)};
            FollowerCombatAnchor.Leader=leader;FollowerCombatAnchor.Independent=independent;
            BossPlayers.Data.EffectiveSelfDetermination=independent?100:50;
            var result=policy.Evaluate(enemy);
            Check(result.Result==FollowerCombatRiflemanEngagement.Outcome.Hold,"low willingness holds beside commander instead of regrouping toward human");
            Check(Math.Abs(result.CurrentBossDistance-1.7f)<.01f,"engagement distances use selected commander");
            Check(!independent||result.PlayerPullAdjustment==0,"100 determination removes pull without removing commander cohesion");
            var ordered=policy.Evaluate(enemy,true);
            Check(ordered.AllowsEngagement&&ordered.Reason=="commanderAdvance","live commander order bypasses subordinate aggression");
            Check(!policy.Evaluate(enemy).AllowsEngagement,"withdrawn order invalidates cached permission immediately");
            enemy.MemoryOnly=true;common.Reliable=false;
            Check(policy.Evaluate(enemy).Reason=="unreliableEnemyLocation","ordinary search still requires personal reliable contact");
            Check(policy.Evaluate(enemy,true).AllowsEngagement,"admitted commander order can use shared contact");
            common.Critical=true;
            Check(policy.Evaluate(enemy,true).Reason=="survivalOrMedical","commander cannot bypass critical health");common.Critical=false;
            common.Heal=true;
            Check(policy.Evaluate(enemy,true).Reason=="survivalOrMedical","commander cannot bypass pending healing");common.Heal=false;
            common.WeaponPolicy=FollowerCombatCommon.AutoPushWeaponThreatPolicy.VeryCloseOrOrderedOnly;Time.time++;
            Check(policy.Evaluate(enemy,true).Reason=="weaponThreat","commander retains automatic weapon-readiness restriction");
            common.WeaponPolicy=FollowerCombatCommon.AutoPushWeaponThreatPolicy.Normal;Time.time++;
            bot.Memory.IsUnderFire=true;
            Check(policy.Evaluate(enemy,true).Reason=="survivalOrMedical","commander cannot bypass incoming pressure");bot.Memory.IsUnderFire=false;
            FollowerCombatCommon.RecentHit=true;
            Check(policy.Evaluate(enemy,true).Reason=="survivalOrMedical","commander cannot bypass recent hits");FollowerCombatCommon.RecentHit=false;
            pitTeam.Utils.Utils.Complete=false;Time.time++;
            Check(policy.Evaluate(enemy,true).Reason=="incompleteNavPath","commander cannot bypass incomplete movement paths");
            pitTeam.Utils.Utils.Complete=true;Time.time++;
            leader.Position=new(140,0,0);
            Check(policy.Evaluate(enemy).Result==FollowerCombatRiflemanEngagement.Outcome.Regroup,"separated Rogue regroups to commander even at 100 determination");
            leader.Position=new(101.7f,0,0);Time.time++;
            Check(policy.Evaluate(enemy).Result==FollowerCombatRiflemanEngagement.Outcome.Hold,"rejoining commander ends distance regroup");
            FollowerCombatAnchor.Leader=null;Time.time++;
            result=policy.Evaluate(enemy);
            Check(result.Result==(independent?FollowerCombatRiflemanEngagement.Outcome.Hold:FollowerCombatRiflemanEngagement.Outcome.Regroup),
                "no commander preserves ordinary human escort and independent behavior");
        }
        Console.WriteLine($"PASS: {checks} production Rifleman commander arbitration, cache, fallback and survival checks; controlled paths.");
    }
}
namespace UnityEngine
{
    public struct Vector3(float x,float y,float z)
    {
        public float x=x,y=y,z=z;public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
    }
    public static class Time{public static float time=100;}
    public static class Mathf
    {
        public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<.00001f;
        public static float Abs(float v)=>MathF.Abs(v);public static float Max(float a,float b)=>MathF.Max(a,b);
    }
}
namespace UnityEngine.AI
{
    public class NavMeshPath{}
    public struct NavMeshHit{public Vector3 position;}
    public static class NavMesh{public static bool SamplePosition(Vector3 p,out NavMeshHit hit,float radius,int mask){hit=new(){position=p};return true;}}
}
namespace EFT
{
    public enum WildSpawnType{assault,marksman,bossKnight}
    public class AIData{public float PowerOfEquipment=100;}
    public class Settings{public WildSpawnType Role;}
    public class Info{public Settings Settings=new();}
    public class Profile{public Info Info=new();}
    public class Person{public AIData AIData=new();public Profile Profile=new();}
    public class EnemyInfo{public string ProfileId="target";public Vector3 Position;public bool IsVisible,CanShoot,MemoryOnly;public Person Person=new();}
    public class Memory{public bool IsUnderFire;}
    public class BotOwner{public Vector3 Position;public AIData AIData=new();public Memory Memory=new();public string ProfileId="commander";}
}
namespace pitTeam.Components{public class BotFollowerPlayer{public float EffectiveSelfDetermination=50,BossInfluence=1;}}
namespace pitTeam.Modules
{
    public class BossPlayers
    {
        public static BossPlayers Instance=new();public static pitTeam.Components.BotFollowerPlayer Data=new();
        public pitTeam.Components.BotFollowerPlayer GetFollower(BotOwner bot)=>Data;
    }
    public static class FollowerCombatAnchor
    {
        public static BotOwner Leader;public static bool Independent;
        public static bool TryGetCompanionLeader(BotOwner bot,out BotOwner leader){leader=Leader;return leader!=null;}
        public static bool IsCombatIndependent(BotOwner bot)=>Independent;
        public static bool CanAutomaticallyRegroup(BotOwner bot)=>!Independent||Leader!=null;
    }
    public class CombatDistanceConfiguration
    {
        public static CombatDistanceConfiguration Instance=new();
        public float GetBossRegroupTriggerDistance(BotOwner bot)=>18;
        public bool IsUrbanDetourRegroup(float direct,float path)=>false;
    }
    public static class FollowerDeathEscapeResolver{public static float GetRouteThreatRoleMultiplier(WildSpawnType role)=>1;}
    public static class BattleRecorder
    {
        public static bool IsRecordingFor(BotOwner bot,bool requireRecordedCombat)=>false;
        public static void RecordObjectiveDiagnostic(BotOwner bot,string owner,string eventName,string reason,Func<object> data){}
    }
}
namespace pitTeam.Utils
{
    public static class Utils
    {
        public static bool Complete=true;
        public static bool TryGetCompletePathDistance(Vector3 a,Vector3 b,out float distance,UnityEngine.AI.NavMeshPath path)
        {distance=Vector3.Distance(a,b);return Complete;}
    }
    public static class Enemy
    {
        public enum EnemyDistance{VeryClose,Close,Distant}
        public static bool IsMemoryOnlyAcquisitionWithoutPersonalContact(EnemyInfo e)=>e.MemoryOnly;
        public static EnemyDistance Distance(EnemyInfo e)=>EnemyDistance.Distant;
        public static int GetNearbyLivingGroupMemberCount(EnemyInfo e,Vector3 pos,float radius)=>1;
    }
}
namespace pitTeam.BigBrain
{
    public class FollowerCombatCommon
    {
        public enum AutoPushWeaponThreatPolicy{Normal,Cautious,VeryCloseOrOrderedOnly}
        public bool Critical,Heal,Reliable=true;public static bool RecentHit;
        public AutoPushWeaponThreatPolicy WeaponPolicy;
        public float Aggression=.60f;
        public float GetAggression01()=>Aggression;
        public static Vector3 GetEnemyAnchor(EnemyInfo e)=>e.Position;
        public Vector3 GetRealBossPosition()=>new(0,0,0);
        public bool IsFollowerCriticallyWounded()=>Critical;public bool HasActiveOrPendingHealWork()=>Heal;
        public static bool WasHitRecently(BotOwner bot,float duration)=>RecentHit;
        public bool HasReliablePersonalEnemyLocation(EnemyInfo e)=>Reliable;
        public bool HasActiveCombatEnemy(EnemyInfo e)=>e!=null;
        public static bool IsFinite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        public static float GetSafeRegroupDistance(float path,float direct)=>MathF.Max(path,direct);
        public bool ShouldDeferAutonomousRegroupAfterRecentFight(EnemyInfo e,float distance,float trigger)=>false;
        public AutoPushWeaponThreatPolicy GetAutoPushWeaponThreatPolicy(EnemyInfo e)=>WeaponPolicy;
    }
    public static class FollowerCombatRegroupObjective{public static bool IsSameBossLevel(Vector3 a,Vector3 b)=>MathF.Abs(a.y-b.y)<3;}
}
