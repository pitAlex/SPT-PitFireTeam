using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Patches;
using pitTeam.Modules;
using UnityEngine;

namespace UnityEngine { public struct Vector3 {} public static class Time { public static float time; } }
namespace EFT {
    public interface IPlayer { string ProfileId {get;} }
    public class Player : IPlayer { public string ProfileId {get;set;} }
    public class BotOwner { public string ProfileId;public BotsGroup BotsGroup; public Brain Brain=new Brain();public Settings Settings; }
    public class Settings { public Settings FileSettings,Mind;public float TIME_TO_FORGOR_ABOUT_ENEMY_SEC; }
    public class Brain { public BaseBrain BaseBrain=new BaseBrain();public Agent Agent=new Agent(); }
    public class BaseBrain { public Layer CurLayerInfo=new Layer(); }
    public class Layer { public string Value="pitTeam.FollowerPatrol";public string Name()=>Value; }
    public class Agent { public Result Result=new Result();public Result LastResult()=>Result; }
    public class Result { public string Reason="postCombatWait"; }
    public enum EEnemyPartVisibleType { Visible }
}
public class BotGroupEnemyInfo {
    public Player Player;public BotsGroup _botGroup;
    public UnityEngine.Vector3 EnemyLastVisiblePosition {
        [MethodImpl(MethodImplOptions.NoInlining)] set { _botGroup.EnemyLastSeenTimeReal=UnityEngine.Time.time; }
    }
}
public class BotsGroup {
    private float seen;
    public float EnemyLastSeenTimeReal { get=>seen; [MethodImpl(MethodImplOptions.NoInlining)] set=>seen=value; }
    public BotGroupEnemyInfo Contact;public Exception Failure;public Action Nested;public int Calls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ReportAboutEnemy(IPlayer enemy,EEnemyPartVisibleType kind,BotOwner reporter) {
        Calls++;Nested?.Invoke();if(Failure!=null)throw Failure;Contact.EnemyLastVisiblePosition=default;
    }
}
namespace pitTeam.Patches {
    public class Boss { public List<BotOwner> Followers=new List<BotOwner>(); }
    public class BotsGroupPlayer : BotsGroup { public Boss Boss=new Boss(); }
}
namespace pitTeam.Modules {
    public static class FollowerEnemyTracking { public static string Mode="Realistic";public static float RememberSeconds=60; }
    public class BossPlayers { public static BossPlayers Instance=new BossPlayers();public Follower Follower=new Follower();public Follower GetFollower(BotOwner owner)=>Follower; }
    public class Follower { public string Block="recentGroupContact";public string DescribePatrolCombatBlock()=>Block; }
    public static class SainGoalEnemyBridge { public static string DescribeGoalEnemy(BotOwner owner)=>"none"; }
    public static class Logger { public static void LogError(string message){throw new Exception(message);} }
    public static class BattleRecorder {
        private sealed class RecorderFollowerState { public float NextPatrolWaitProbe,NextPatrolWaitRecord,PatrolWaitStarted;public bool PatrolWaiting;public string PatrolWaitBlock; }
        private static RecorderFollowerState WaitState=new RecorderFollowerState();
        public static List<string> WaitPhases=new List<string>();
        public static void Probe(BotOwner bot)=>RecordPatrolWait(bot,WaitState);
        private static object PatrolGroupEvidence(BotOwner bot)=>null;
        private static float? SanitizeFloat(float value)=>float.IsNaN(value)?(float?)null:value;
        private static object CreateBotSnapshot(BotOwner bot,RecorderFollowerState state)=>null;
        private static void WriteEventInternal(string type,BotOwner bot,object payload)=>WaitPhases.Add((string)payload.GetType().GetProperty("phase").GetValue(payload));
__WAIT_METHOD__
        public static bool IsRecordingEnabled=true,Throw;
        public static bool IsRecordingFor(BotOwner bot)=>true;
        public sealed class Event { public string Enemy,Reporter;public float Before,After;public int Suppressed; }
        public static List<Event> Events=new List<Event>();
        public static void RecordGroupSightWrite(BotOwner owner,string enemyId,string reporterId,float before,float after,string stack,int suppressed) {
            if(Throw)throw new Exception("recorder failure");
            Events.Add(new Event{Enemy=enemyId,Reporter=reporterId,Before=before,After=after,Suppressed=suppressed});
        }
    }
}
namespace pitTeam.Utils { public static class FollowerMedical { public static string DescribePostCombatRecovery(BotOwner owner)=>"none"; } }
namespace pitTeam.BigBrain { public static class FollowerPatrolLayer { public const string CombatReadinessWaitReason="postCombatWait"; } }
public static class GroupSightChecks {
    private static int checks;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
    public static void Main(){
        GroupSightRecorderPatch.Apply(new Harmony("pit.group.recorder.fixture"));
        var g=new BotsGroupPlayer();var bot=new BotOwner{ProfileId="follower",BotsGroup=g};g.Boss.Followers.Add(bot);
        var enemy=new Player{ProfileId="enemy"};g.Contact=new BotGroupEnemyInfo{Player=enemy,_botGroup=g};
        UnityEngine.Time.time=10;g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);
        var e=BattleRecorder.Events[0];
        Check(g.Calls==1&&g.EnemyLastSeenTimeReal==10,"native report runs once and retains timestamp");
        Check(e.Enemy=="enemy"&&e.Reporter=="follower"&&e.Before==0&&e.After==10,"nested visible setter retains report identity and old/new timestamp");
        for(int i=0;i<100;i++)g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);
        Check(BattleRecorder.Events.Count==1&&g.Calls==101,"same-source burst is throttled without skipping native reports");
        UnityEngine.Time.time=12;g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);
        Check(BattleRecorder.Events.Count==2&&BattleRecorder.Events[1].Suppressed==100,"next sample reports suppressed write count");
        g.EnemyLastSeenTimeReal=13;e=BattleRecorder.Events[2];
        Check(e.Enemy==null&&e.Reporter==null,"direct timestamp writer cannot inherit completed report scope");
        var failure=new InvalidOperationException("native report");g.Failure=failure;Exception caught=null;
        try{g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);}catch(Exception ex){caught=ex;}
        Check(ReferenceEquals(caught,failure),"report exception is preserved");g.Failure=null;
        UnityEngine.Time.time=15;g.EnemyLastSeenTimeReal=15;e=BattleRecorder.Events[3];
        Check(e.Reporter==null&&e.Enemy==null,"throwing report restores identity scope");
        BattleRecorder.IsRecordingEnabled=false;int prior=BattleRecorder.Events.Count;
        UnityEngine.Time.time=20;g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);
        Check(BattleRecorder.Events.Count==prior&&g.EnemyLastSeenTimeReal==20,"disabled recorder leaves reporting unchanged and emits nothing");
        BattleRecorder.IsRecordingEnabled=true;
        var ordinary=new BotsGroup();ordinary.EnemyLastSeenTimeReal=20;
        Check(BattleRecorder.Events.Count==prior,"ordinary groups remain unrecorded");
        BattleRecorder.Throw=true;g.ReportAboutEnemy(enemy,EEnemyPartVisibleType.Visible,bot);
        Check(g.EnemyLastSeenTimeReal==20,"recorder exception cannot interrupt native timestamp write");
        UnityEngine.Time.time=30;BattleRecorder.Probe(bot);
        Check(BattleRecorder.WaitPhases.Count==1&&BattleRecorder.WaitPhases[0]=="enter","wait entry records outside combat");
        for(int i=0;i<100;i++)BattleRecorder.Probe(bot);
        Check(BattleRecorder.WaitPhases.Count==1,"same-frame probes cannot spam wait events");
        UnityEngine.Time.time=31;BattleRecorder.Probe(bot);Check(BattleRecorder.WaitPhases.Count==1,"unchanged wait observes heartbeat budget");
        UnityEngine.Time.time=35;BattleRecorder.Probe(bot);Check(BattleRecorder.WaitPhases[1]=="heartbeat","persistent wait continues after recovery recording would stop");
        BossPlayers.Instance.Follower.Block="personalCombatSignal";UnityEngine.Time.time=36;BattleRecorder.Probe(bot);
        Check(BattleRecorder.WaitPhases[2]=="blockChanged","blocker changes are recorded before heartbeat deadline");
        bot.Brain.Agent.Result.Reason="FollowerPatrol";UnityEngine.Time.time=37;BattleRecorder.Probe(bot);
        Check(BattleRecorder.WaitPhases[3]=="exit","wait exit is recorded even without combat or commands");
        UnityEngine.Time.time=45;BattleRecorder.Probe(bot);Check(BattleRecorder.WaitPhases.Count==4,"ordinary patrol does not keep emitting wait records");
        Console.WriteLine($"Passed {checks} production group-sight hook checks.");
    }
}
