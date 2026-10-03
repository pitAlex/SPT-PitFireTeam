using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using pitTeam.Modules;
using pitTeam.Components;

namespace UnityEngine {
    public static class Time { public static float time; }
    public struct Vector3 {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public Vector3 normalized=>this*(1f/(float)Math.Sqrt(sqrMagnitude));
        public static Vector3 zero=>new Vector3(); public static Vector3 up=>new Vector3(0,1,0);
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float n)=>new Vector3(a.x*n,a.y*n,a.z*n);
    }
}
namespace EFT {
    public enum EBotState {Active,Inactive}
    public class Health {public bool IsAlive=true;}
    public class GameObject {public bool activeInHierarchy=true;}
    public class Player {public string ProfileId="enemy";public Vector3 Position,LookDirection=new Vector3(-1,0,0);public Health HealthController=new Health();public GameObject gameObject=new GameObject();}
    public class Group {public bool Hostile=true;public bool IsEnemy(Player p)=>Hostile;public bool IsPlayerEnemy(Player p)=>Hostile;}
    public class Follower {public object BossToFollow;}
    public class Controller {public Dictionary<string,EnemyInfo> EnemyInfos=new Dictionary<string,EnemyInfo>();}
    public class EnemyInfo {public string ProfileId;}
    public class Mover {public bool Sprinting;}
    public class Steering {public Vector3 Point;public void LookToPoint(Vector3 p){Point=p;}}
    public class BotOwner {
        public bool Registered=true,Addon,IsDead,Accepted,Medical,Suppressed;
        public EBotState BotState=EBotState.Active; public Vector3 Position;
        public Group BotsGroup=new Group();public Follower BotFollower=new Follower();
        public Controller EnemiesController=new Controller();public Mover Mover=new Mover();public Steering Steering=new Steering();
        public BotFollowerPlayer Data=new BotFollowerPlayer();
    }
}
namespace pitTeam {public static class pitFireTeam {public static bool UseSainFollowerCombat(BotOwner b)=>b.Addon;}}
namespace pitTeam.Components {
    public enum FollowerCommandType {None,HoldPosition,MoveToPoint,ComeCloser,RegroupNearBoss,Loot}
    public class BotFollowerPlayer {
        public bool IsBackpackInspectionActive,LookOverride;public FollowerCommandType Command;
        public bool TryPeekActiveCommand(out FollowerCommandType c,out Vector3 p,out float t){c=Command;p=default;t=0;return c!=FollowerCommandType.None;}
        public bool TryGetCommandLookOverride(out Vector3 p){p=default;return LookOverride;}
    }
    public class pitAIBossPlayer {public Player realPlayer=new Player{ProfileId="boss"};public List<BotOwner> Followers=new List<BotOwner>();}
}
namespace pitTeam.Modules {
    public class BossPlayers {
        public static BossPlayers Instance=new BossPlayers();
        public static bool IsFollower(BotOwner b)=>b.Registered;
        public static bool IsPlayerBoss(string id)=>id=="boss";
        public static bool IsFollowerProfileId(string id)=>id=="friendly";
        public BotFollowerPlayer GetFollower(BotOwner b)=>b?.Registered==true?b.Data:null;
    }
    public static class SainAddonBridge {public static bool HasAcceptedGoalEnemy(BotOwner b)=>b.Accepted;public static bool IsUsingMedical(BotOwner b)=>b.Medical;}
    public static class FollowerEnemyEnforceSuppression {public static bool IsSuppressed(BotOwner b)=>b.Suppressed;}
    public class CombatDistanceConfiguration {public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration();public float Radius=10;public float GetRegroupBossMoveRefreshDistance()=>Radius;}
    public static class FollowerEnemyTracking {public static bool IsFinite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x);}
}
namespace pitTeam.Utils {public static class FollowerAwareness {public static bool IsHostileToBossGroupForReaction(BotOwner b,Player p)=>false;}}
public static class CoreSoundChecks {
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static bool Look(BotOwner b){b.Steering.Point=new Vector3(0,0,100);FollowerSoundAwareness.ApplyLook(b);return b.Steering.Point.z!=100;}
    private static void Hear(BotOwner b,Player p,float x=20,bool shot=false,bool audible=true,float y=0){FollowerSoundAwareness.Observe(b,p,new Vector3(x,y,0),shot,audible);}
    public static void Main(){
        var bot=new BotOwner();var enemy=new Player();var boss=new pitAIBossPlayer();bot.BotFollower.BossToFollow=boss;
        Hear(bot,enemy,audible:false);Check(!Look(bot),"inaudible sound ignored");
        Hear(bot,enemy);Check(Look(bot)&&bot.Steering.Point.x==20,"heard local bearing survives peaceful scanning");
        enemy.Position=new Vector3(-80,0,0);Check(Look(bot)&&bot.Steering.Point.x==20,"hidden source movement cannot alter bearing");
        Check(!bot.Accepted,"orientation does not acquire an enemy");
        Time.time=3.1f;Check(!Look(bot),"bearing expires");
        Hear(bot,enemy,25.1f);Check(!Look(bot),"local sound capped at 25 m");
        Hear(bot,enemy,25);Check(Look(bot),"local boundary included");
        bot.Data.LookOverride=true;Check(!Look(bot),"explicit look wins");bot.Data.LookOverride=false;
        bot.Mover.Sprinting=true;Check(!Look(bot),"sprint wins");bot.Mover.Sprinting=false;
        FollowerSoundAwareness.Attention(bot);Hear(bot,enemy);Check(!Look(bot),"Attention ignores repeated local identity");
        Hear(bot,enemy,100,true);Check(Look(bot),"directed gunfire bypasses sector ignore");
        Hear(bot,new Player{ProfileId="other"},5);Check(Look(bot)&&bot.Steering.Point.x==100,"directed shot takes priority over nearer incidental sound");
        Time.time+=4;enemy.LookDirection=new Vector3(1,0,0);Hear(bot,enemy,100,true);Check(!Look(bot),"shot away from squad ignored");
        enemy.LookDirection=new Vector3(-1,0,0);Hear(bot,enemy,100,true,y:100);Check(!Look(bot),"directional shot test includes elevation");
        boss.realPlayer.Position=new Vector3(10.1f,0,0);Hear(bot,enemy);Check(Look(bot),"player sector movement restores local hearing");
        foreach(var kind in new[]{"medical","combat","loot","backpack","suppressed","dead","ordinary","addon","neutral"}){
            FollowerSoundAwareness.ClearRaid();bot.Medical=kind=="medical";bot.Accepted=kind=="combat";bot.Data.Command=kind=="loot"?FollowerCommandType.Loot:FollowerCommandType.None;
            bot.Data.IsBackpackInspectionActive=kind=="backpack";bot.Suppressed=kind=="suppressed";bot.IsDead=kind=="dead";bot.Registered=kind!="ordinary";bot.Addon=kind=="addon";bot.BotsGroup.Hostile=kind!="neutral";
            Hear(bot,enemy);Check(!Look(bot),kind+" retains ownership/exclusion");
        }
        bot.BotsGroup.Hostile=true;bot.Addon=false;
        Hear(bot,enemy);Check(Look(bot),"normal hearing resumes after ownership clears");
        enemy.HealthController.IsAlive=false;Check(!Look(bot),"dead contact ends bearing");
        enemy.HealthController.IsAlive=true;Hear(bot,enemy);FollowerSoundAwareness.ClearRaid();Check(!Look(bot),"raid teardown clears bearings and ignores");
        Check(!FollowerSoundAwareness.Faces(default,default,new Vector3(1,0,0)),"invalid facing rejected");
        Console.WriteLine("Core sound awareness: "+checks+" behavior checks passed.");
    }
}
