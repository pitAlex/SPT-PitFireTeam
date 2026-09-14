using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
namespace SAIN.Models.Enums {public enum EBotMoveStatus {None,ReadyToMove,Moving,Paused,DoorInteraction,Complete}}
namespace SAIN.Components {
    public class PathData {
        public SAIN.Models.Enums.EBotMoveStatus Status=SAIN.Models.Enums.EBotMoveStatus.Moving;
        public UnityEngine.AI.NavMeshPathStatus PathStatus=UnityEngine.AI.NavMeshPathStatus.PathComplete;
        public Vector3 Destination;public float TimeStarted,PathLength;public int CurrentIndex;
        public bool OnLastCorner;public string CurrentSprintStatus="Run",SprintReason="Requested";
        public List<Vector3> PathPoints=new List<Vector3>();
    }
    public class EnemyDecisions {public Vector3? FiringPosition;}
    public partial class Decision {
        public SAIN.SAINComponent.Classes.Decision.BotDecisionManager DecisionManager=>Manager;
        public EnemyDecisions EnemyDecisions=new EnemyDecisions();
    }
}
namespace pitTeam.Modules {
    // Real state-transition/event methods are extracted from BattleRecorder.cs by the runner.
    // Only the disk/game surfaces are replaced. Events serialize with the real JSON library.
    public static partial class BattleRecorder {
        public static bool Enabled=true;public static List<(string Kind,JObject Payload)> Events=new List<(string,JObject)>();
        private static Dictionary<string,RecorderFollowerState> states=new Dictionary<string,RecorderFollowerState>();
        private static bool CanRecordBot(BotOwner bot)=>Enabled&&bot!=null&&bot.IsFollower;
        public static bool IsAddonRecording()=>Enabled;
        private static RecorderFollowerState GetOrCreateState(BotOwner bot){if(!states.TryGetValue(bot.ProfileId,out var s))states[bot.ProfileId]=s=new RecorderFollowerState();return s;}
        private static object CreateBotSnapshot(BotOwner bot,RecorderFollowerState state)=>SainCombatRecorderBridge.Capture(bot)?.Details;
        private static float? SanitizeFloat(float value)=>value;
        private static void WriteEventInternal(string kind,BotOwner bot,object payload){
            var json=JsonConvert.SerializeObject(payload,new JsonSerializerSettings{ContractResolver=new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()});
            Events.Add((kind,JObject.Parse(json)));
        }
        private sealed class RecorderFollowerState {
            public bool InCombat,HasPreviousSnapshot,HasPreviousEffectiveMoveTarget;
            public string CombatOwner,CurrentObjective,LastDecisionAction,LastDecisionReason,LastDecisionEndReason;
            public int CombatEpisodeId,CurrentDecisionInstanceId,LastEndedDecisionInstanceId;
            public float CombatStartedTime,CurrentDecisionSelectedTime,LastDecisionEndTime,LastCombatSeenTime,NextSnapshotTime;
        }
        __RECORDER_METHODS__
    }
}
public static partial class CombatChecks {
    private static BotOwner EngageBot(string id,float boss=80){
        var b=RegroupBot(id,boss);b.Sain.Decision.CurrentCombatDecision=ECombatDecision.MoveToEngage;
        b.Sain.Decision.EnemyDecisions.FiringPosition=new Vector3(10,0,0);return b;
    }
    private static void TestEngageAttempt(){
        var b=EngageBot("oneAttempt");var a=new SAINFollowerMoveToEngageAction(b);var attempt=SAINFollowerRuntime.GetEngageAttempt(b);
        a.Start();a.Update(null);Check(b.Sain.Mover.Destination.x==10,"engage starts with the native firing position");
        b.Sain.Decision.EnemyDecisions.FiringPosition=new Vector3(25,0,0);Time.time+=1;a.Stop();a.Start();a.Update(null);
        Check(b.Sain.Mover.Destination.x==10,"action restart and replacement candidate retain the original attempt destination");
        b.GetPlayer.Position=new Vector3(10,0,0);Time.time+=1;a.Update(null);
        Check(!attempt.Failed,"arrival permits a short vision check before calling the attempt unsuccessful");
        Time.time+=1.1f;a.Update(null);Check(attempt.Failure=="arrivedWithoutShot","arriving without a shot ends the one firing-position attempt");
        b.Sain.Decision.Manager.Frame(ECombatDecision.MoveToEngage);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto,"failed engagement regroups when the player is distant despite another native MoveToEngage choice");
        Check(BattleRecorder.Records.Any(r=>r.EndsWith("engage.arrivedWithoutShot")),"automatic regroup records the concrete failed-attempt reason");
        b.GetPlayer.Position=new Vector3(70,0,0);Time.time+=1;SAINFollowerRuntime.GetRegroup(b).Observe();
        Time.time+=2;SAINFollowerRuntime.GetRegroup(b).Observe();
        for(int cycle=0;cycle<3;cycle++){
            Time.time+=3;b.Sain.Decision.Manager.Frame(ECombatDecision.MoveToEngage);
            Check(!SAINFollowerRuntime.GetRegroup(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover&&attempt.Failed,
                "same failed contact cannot send the follower outward again after regroup, cycle "+cycle);
        }

        b=EngageBot("pathFailureNear",12);a=new SAINFollowerMoveToEngageAction(b);attempt=SAINFollowerRuntime.GetEngageAttempt(b);
        b.Sain.Mover.Complete=false;a.Start();a.Update(null);
        Check(attempt.Failure=="pathRejected","failed native run/walk path is retained as an unsuccessful attempt");
        b.Sain.Decision.Manager.Frame(ECombatDecision.MoveToEngage);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"failed attempt near the player yields to cover without repeating pursuit");
        b.Leader.Position=new Vector3(80,0,0);Time.time+=1;b.Sain.Decision.Manager.Frame(ECombatDecision.MoveToEngage);
        Check(SAINFollowerRuntime.GetRegroup(b).Active,"moving the player away uses the existing failure instead of granting another attempt");

        b=EngageBot("independentAttempt");b.Follower.CombatIndependent=true;a=new SAINFollowerMoveToEngageAction(b);attempt=SAINFollowerRuntime.GetEngageAttempt(b);
        a.Start();a.Update(null);b.Sain.Decision.EnemyDecisions.FiringPosition=new Vector3(25,0,0);Time.time+=8;a.Update(null);b.Sain.Decision.Manager.Frame(ECombatDecision.MoveToEngage);
        Check(b.Sain.Mover.Destination.x==25&&!attempt.Failed&&!SAINFollowerRuntime.GetRegroup(b).Active,"On Your Own retains unrestricted native firing-position retries");

        b=EngageBot("attemptPause");a=new SAINFollowerMoveToEngageAction(b);attempt=SAINFollowerRuntime.GetEngageAttempt(b);a.Start();a.Update(null);
        Time.time+=2;a.Update(null);float budget=attempt.ActiveSeconds;a.Stop();Time.time+=60;a.Start();a.Update(null);
        Check(attempt.ActiveSeconds==budget&&!attempt.Failed,"survival/layer interruption time does not exhaust the engagement attempt");
        Time.time+=5;a.Update(null);Check(attempt.Failure=="noProgress","stationary engagement eventually fails without resetting on action starts");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(51,0,0);attempt.Observe(b.Sain.GoalEnemy);
        Check(attempt.Failed,"minor remembered-position jitter cannot rearm the failed attempt");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(60,0,0);attempt.Observe(b.Sain.GoalEnemy);
        Check(!attempt.Failed&&attempt.EnemyId==null,"materially changed enemy information permits a new attempt");
        a.Update(null);attempt.Fail("testFailure");b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;attempt.Observe(b.Sain.GoalEnemy);
        Check(!attempt.Failed,"a real firing opportunity clears the obsolete failure");

        b=EngageBot("attemptLifetime");a=new SAINFollowerMoveToEngageAction(b);attempt=SAINFollowerRuntime.GetEngageAttempt(b);a.Start();a.Update(null);
        for(int i=0;i<4;i++){Time.time+=5;b.GetPlayer.Position=new Vector3(0,0,i+1);a.Update(null);}
        Check(attempt.Failure=="attemptExpired","continuous ineffective movement still has one bounded attempt budget");
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.Surgery);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"failed attempt never interrupts fresh medical recovery");
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();Check(attempt.EnemyId==null,"tactic opt-out clears engagement state");
    }
    private static void TestSainRecorder(){
        var b=EngageBot("recorder");var solo=new SAINFollowerSoloCombatLayer(b,74);var squad=new SAINFollowerSquadCombatLayer(b,75);Tick();
        int start=BattleRecorder.Events.Count(e=>e.Kind=="combatStart");
        b.Sain.Decision.Manager.Publish(ECombatDecision.StandAndShoot);solo.IsActive();solo.GetNextAction();
        Check(BattleRecorder.Events.Count(e=>e.Kind=="combatStart")==start+1&&SainCombatRecorderBridge.IsActive(b),"native addon decision opens a real recorder combat episode");
        var selected=BattleRecorder.Events.Last(e=>e.Kind=="sainActionSelected").Payload;
        Check((string)selected["state"]["combatOwner"]=="sainAddon"&&(bool)selected["state"]["inCombat"],"addon action events are classified as combat with explicit ownership");
        b.Sain.Decision.Manager.Publish(ECombatDecision.None,ESquadDecision.Suppress);squad.IsActive();squad.GetNextAction();
        Check(BattleRecorder.Events.Count(e=>e.Kind=="combatStart")==start+1,"solo-to-squad handoff keeps the same combat episode");
        Check(BattleRecorder.Events.Any(e=>e.Kind=="sainActionEnd")&&BattleRecorder.Events.Last(e=>e.Kind=="sainDecision").Payload["details"]["squad"].ToString()=="Suppress","native publication and action replacement both reach the recorder");
        b.Sain.Mover.ActivePath=new SAIN.Components.PathData{Destination=new Vector3(9,2,7),PathLength=14,CurrentIndex=2};
        b.Sain.Mover.ActivePath.PathPoints.Add(new Vector3(1,2,3));b.Sain.Mover.Moving=true;b.Sain.Mover.Running=true;
        int evaluations=b.Sain.Decision.Manager.SoloEvaluations,resets=b.Sain.Decision.Resets;
        b.Medecine.FirstAid.Have2Do=true;b.Medecine.FirstAid.IsBleeding=true;b.Medecine.FirstAid.CurUsingMeds=new MedicineItem();
        var heard=new Enemy{Seen=false,Heard=true,InLineOfSight=true};heard.EnemyPlayer.ProfileId="heard-medical-blocker";
        b.Sain.EnemyController.KnownEnemies.Add(heard);
        var snapshot=SainCombatRecorderBridge.Capture(b);var json=JObject.Parse(JsonConvert.SerializeObject(snapshot.Details,new JsonSerializerSettings{ContractResolver=new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()}));
        Check((float)json["personality"]["aggression"]==b.Follower.EffectiveCombatAggression&&(string)json["personality"]["personality"]==b.Sain.Info.Personality.ToString(),"native snapshot includes passive aggression selection");
        Check(snapshot.ControlsMovement&&snapshot.Destination.Value.x==9&&snapshot.Moving&&snapshot.Running,"snapshot exposes native SAIN movement instead of an EFT mover target");
        Check((bool)json["medical"]["bleeding"]&&(string)json["medical"]["firstAidItemId"]=="fixture-med", "medical recording captures selected supplies and bleeding without starting treatment");
        Check(json["medical"]["enemies"].Any(e=>(string)e["profileId"]=="heard-medical-blocker"&&(bool)e["heard"]&&(bool)e["inLineOfSight"]),"medical recording includes non-goal heard threats considered by native first aid");
        b.Sain.EnemyController.KnownEnemies.Remove(heard);
        Check((int)json["movement"]["path"]["currentIndex"]==2&&(float)json["movement"]["path"]["points"][0]["z"]==3,"SAIN path and vector snapshots serialize without Unity object graphs");
        Check(b.Sain.Decision.Manager.SoloEvaluations==evaluations&&b.Sain.Decision.Resets==resets,"recording reads state without evaluating or resetting AI");
        var cover=JObject.FromObject(snapshot.Cover);Check((string)cover["owner"]=="sain","cover snapshots identify the native SAIN owner");
        // Losing runtime readiness must not continue classifying fallback core combat as addon combat.
        b.Leader=null;Check(!SainCombatRecorderBridge.IsActive(b),"unready addon cannot hold recorder combat ownership");
        b.Leader=new Player();
        b.Sain.Mover.ActivePath=null;snapshot=SainCombatRecorderBridge.Capture(b);
        Check(!snapshot.HasPath&&!snapshot.Destination.HasValue,"missing native path never falls back to a stale EFT target while SAIN owns movement");
        b.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=false;SAINFollowerRuntime.GetCombatPhase(b);
        Check(SainCombatRecorderBridge.IsActive(b),"post-combat linger stays inside the same recorded episode");
        Time.time+=3.1f;SAINFollowerRuntime.GetCombatPhase(b);
        Check(!SainCombatRecorderBridge.IsActive(b)&&BattleRecorder.Events.Last(e=>e.Kind=="combatStop").Payload["reason"].ToString()=="Released","linger completion closes the addon recorder episode");
        var recorder=SAINFollowerRuntime.GetRecorder(b);recorder.Dispose();int events=BattleRecorder.Events.Count;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(BattleRecorder.Events.Count==events,"recorder cleanup detaches native decision subscription");
        Func<BotOwner,SainCombatSnapshot> broken=owner=>throw new InvalidOperationException("recorder fixture failure");
        Func<BotOwner,bool> inactive=owner=>false;
        int errors=Logger.Errors.Count;SainCombatRecorderBridge.Register(broken,inactive);
        Check(SainCombatRecorderBridge.Capture(b)==null&&SainCombatRecorderBridge.Capture(b)==null&&Logger.Errors.Count==errors+1,"snapshot errors are isolated and reported once");
        Logger.Errors.RemoveAt(Logger.Errors.Count-1);SainCombatRecorderBridge.Unregister(broken,inactive);
        Check(SainCombatRecorderBridge.Capture(b)==null&&!SainCombatRecorderBridge.IsActive(b),"recorder callback unregister releases addon capture");
        BattleRecorder.Enabled=false;events=BattleRecorder.Events.Count;
        SainCombatRecorderBridge.RecordEvent(b,"sainTest",new {test=true});
        Check(BattleRecorder.Events.Count==events,"disabled battle recorder emits no addon data");BattleRecorder.Enabled=true;
    }
}
