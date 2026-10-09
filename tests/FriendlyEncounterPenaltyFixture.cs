using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Shared;
using SPT.Common.Http;

namespace EFT {
    public enum EPlayerSide {Usec,Bear,Savage}
    public interface IPlayer {bool IsYourPlayer{get;} string ProfileId{get;} EPlayerSide Side{get;}}
    public class Info {public string Nickname="recruit";}
    public class Profile {public Info Info=new Info();}
    public class Player : IPlayer {public bool IsYourPlayer{get;set;} public bool IsAI=true;public string ProfileId{get;set;} public string AccountId="123";public EPlayerSide Side{get;set;} public Profile Profile=new Profile();}
}
namespace UnityEngine {public static class Random {public static int Range(int min,int max)=>min;}}
namespace pitTeam.Components {
    public class Boss {public Player realPlayer;}
    public class BotFollowerPlayer {public bool IsSquadMate;public Boss Owner;public Boss GetBoss()=>Owner;}
}
namespace pitTeam {
    public class Flag {public bool Value=true;}
    public class IntSetting {public int Value=1;}
    public class Language {public string[] traitorKillMessages={"traitor"};public string[] jerkKillMessages={"jerk"};}
    public static class pitFireTeam {public static Flag npcSendMessage=new Flag();public static IntSetting friendlyChanceMultiplier=new IntSetting();public static Language optionsLang=new Language();}
}
namespace pitTeam.Modules {
    public static class Logger {public static List<string> Errors=new List<string>();public static void LogError(object error){lock(Errors) Errors.Add(error.ToString());}}
    public static class BossPlayers {public static Dictionary<string,BotFollowerPlayer> Followers=new Dictionary<string,BotFollowerPlayer>();public static BotFollowerPlayer GetFollowerByProfileId(string id)=>Followers.TryGetValue(id,out var follower)?follower:null;}
    public static class GameplayModeRuntime {
        public static bool IsAllegiance;
        private static readonly List<Task> Reports=new List<Task>();
        public static Task RunRosterRequest(Action action) {var task=Task.Run(action);lock(Reports) Reports.Add(task);return task;}
        public static void WaitReports() {Task[] tasks;lock(Reports) tasks=Reports.ToArray();Task.WhenAll(tasks).GetAwaiter().GetResult();}
    }
}
namespace pitTeam.Patches {
    public static class KillBoundary {
        private const string KillMessageRoute="/singleplayer/pitfireteam/postraid/kill-message";
        private static readonly HashSet<string> RecordedKillMessageVictims=new HashSet<string>();
        private static readonly object RecordedKillMessageLock=new object();
        private static bool IsFriendlyPmcKillMessageContextEnabled()=>false;
        private static bool WasFriendlyBeforePlayerDamage(string id)=>false;
        private static bool IsPmc(EPlayerSide side)=>side==EPlayerSide.Usec || side==EPlayerSide.Bear;
        public static void Record(Player victim,IPlayer aggressor)=>TryRecordPlayerKillMessage(victim,aggressor);
        __RECORD_METHOD__
        __KIND_METHOD__
        __TEXT_METHOD__
    }
}
namespace SPT.Common.Http {
    public static class RequestHandler {
        public static readonly List<Newtonsoft.Json.Linq.JObject> Posts=new List<Newtonsoft.Json.Linq.JObject>();
        public static FriendlyEncounterPenaltyState State=new FriendlyEncounterPenaltyState();
        public static bool LoseResponse;
        public static bool Hold;
        public static ManualResetEventSlim Started=new ManualResetEventSlim(),Release=new ManualResetEventSlim();
        public static long Clock=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static string Response() {State.ServerNowUnixMs=Clock;return JsonConvert.SerializeObject(new {err=0,data=State});}
        public static string GetJson(string route)=>Response();
        public static string PostJson(string route,string json) {
            var body=Newtonsoft.Json.Linq.JObject.Parse(json);Posts.Add(body);
            if(Hold) {Started.Set();Release.Wait();}
            long killedAt=(long)body["killedAtUnixMs"];
            if(killedAt>0) {
                string id=(string)body["raidId"]+"/"+(string)body["victimProfileId"];
                if(!State.Entries.Any(entry=>entry.Id==id)) State.Entries.Add(new FriendlyEncounterPenaltyEntry{Id=id,ExpiresAtUnixMs=killedAt+FriendlyEncounterPenaltyPolicy.DurationMs});
            }
            if(LoseResponse) {LoseResponse=false;throw new System.IO.IOException("Simulated lost response after committed penalty");}
            return Response();
        }
    }
}
public static class PenaltyChecks {
    private static int checks;
    private static readonly Player Human=new Player {ProfileId="human",IsYourPlayer=true,IsAI=false};
    private static void Check(bool value,string name) {checks++;if(!value) throw new Exception(name);}
    private static Player Recruit(string id,bool spawned=false,bool owned=true) {
        var victim=new Player{ProfileId=id,Side=EPlayerSide.Bear};
        BossPlayers.Followers[id]=new BotFollowerPlayer{IsSquadMate=spawned,Owner=new Boss{realPlayer=owned?Human:new Player{ProfileId="other-human"}}};return victim;
    }
    private static void Reset() {
        GameplayModeRuntime.WaitReports();RequestHandler.Posts.Clear();RequestHandler.State=new FriendlyEncounterPenaltyState();
        GameplayModeRuntime.IsAllegiance=true;pitTeam.pitFireTeam.npcSendMessage.Value=true;
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=1;
        FriendlyEncounterPenaltyRuntime.ReloadAsync().GetAwaiter().GetResult();
    }
    public static void Main() {
        Reset();var victim=Recruit("recruit");pitTeam.Patches.KillBoundary.Record(victim,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==1 && (string)RequestHandler.Posts[0]["messageKind"]=="traitor","Allegiance opposite-faction recruit uses existing traitor report despite disabled Friendly PMC Side context");
        Check(FriendlyEncounterPenaltyRuntime.GetPoints()==5,"traitor kill immediately reduces friendly chances");
        long savedExpiry=RequestHandler.State.Entries.Single().ExpiresAtUnixMs;
        foreach (int multiplier in new[]{1,2,3,4,5,1}) {
            pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=multiplier;
            Check(FriendlyEncounterPenaltyRuntime.GetPoints()==5*multiplier,"Existing penalty scales with current chance multiplier "+multiplier);
            Check(RequestHandler.State.Entries.Single().ExpiresAtUnixMs==savedExpiry && RequestHandler.Posts.Count==1,"Changing multiplier preserves expiry and does not write another kill report");
            Check(FriendlyEncounterPenaltyPolicy.GetPoints(FriendlyEncounterPenaltyRuntime.GetActiveEntries(),FriendlyEncounterPenaltyRuntime.NowUnixMs,multiplier)==FriendlyEncounterPenaltyRuntime.GetPoints(),"Roster total and recruitment penalty use the same scaled arithmetic");
        }
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;
        FriendlyEncounterPenaltyRuntime.ReloadAsync().GetAwaiter().GetResult();
        Check(FriendlyEncounterPenaltyRuntime.GetPoints()==25,"Reloading the saved legacy ledger retains scaled penalty without migration");
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=1;
        pitTeam.Patches.KillBoundary.Record(victim,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==1 && FriendlyEncounterPenaltyRuntime.GetPoints()==5,"duplicate death prefix/postfix cannot add a second penalty");
        Reset();pitTeam.pitFireTeam.npcSendMessage.Value=false;pitTeam.Patches.KillBoundary.Record(Recruit("silent"),Human);GameplayModeRuntime.WaitReports();
        Check((string)RequestHandler.Posts.Single()["messageText"]=="" && FriendlyEncounterPenaltyRuntime.GetPoints()==5,"Raid End Messages off still saves penalty with blank traitor record");
        Reset();pitTeam.Patches.KillBoundary.Record(Recruit("spawned",true),Human);
        pitTeam.Patches.KillBoundary.Record(Recruit("foreign",false,false),Human);
        pitTeam.Patches.KillBoundary.Record(Recruit("other-killer"),new Player{ProfileId="ai",IsYourPlayer=false});
        pitTeam.Patches.KillBoundary.Record(new Player{ProfileId="ordinary"},Human);
        var nonAi=Recruit("non-ai");nonAi.IsAI=false;pitTeam.Patches.KillBoundary.Record(nonAi,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0 && FriendlyEncounterPenaltyRuntime.GetPoints()==0,"spawned, foreign, other-aggressor, ordinary and non-AI victims are excluded");
        Reset();GameplayModeRuntime.IsAllegiance=false;pitTeam.Patches.KillBoundary.Record(Recruit("hired-mode"),Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0 && FriendlyEncounterPenaltyRuntime.GetPoints()==0,"Guns for Hire is excluded");
        Reset();RequestHandler.LoseResponse=true;pitTeam.Patches.KillBoundary.Record(Recruit("retry"),Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==2 && RequestHandler.State.Entries.Count==1 && FriendlyEncounterPenaltyRuntime.GetPoints()==5,"lost response retries same raid/victim without duplicate penalty");
        Reset();RequestHandler.Hold=true;pitTeam.Patches.KillBoundary.Record(Recruit("first-pending"),Human);RequestHandler.Started.Wait();
        pitTeam.Patches.KillBoundary.Record(Recruit("second-pending"),Human);
        Check(FriendlyEncounterPenaltyRuntime.GetPoints()==10,"pending reports reduce chance immediately before server response");
        pitTeam.pitFireTeam.friendlyChanceMultiplier.Value=5;
        Check(FriendlyEncounterPenaltyRuntime.GetPoints()==50,"Both pending reports scale immediately while the HTTP response is held");
        RequestHandler.Hold=false;RequestHandler.Release.Set();GameplayModeRuntime.WaitReports();
        Check(FriendlyEncounterPenaltyRuntime.GetPoints()==50 && RequestHandler.State.Entries.Count==2,"older response retains later pending scaled penalty");
        const long hour=3600000;long expiry=24*hour;
        foreach(var example in new[]{Tuple.Create(24d,"24"),Tuple.Create(23.75d,"24"),Tuple.Create(23.5d,"23.5"),Tuple.Create(2d,"2"),Tuple.Create(1.51d,"1.6"),Tuple.Create(1.5d,"1.5"),Tuple.Create(1d,"1"),Tuple.Create(50d/60d,"50"),Tuple.Create(35d/60d,"35"),Tuple.Create(5d/60d,"5")}) {
            long now=expiry-(long)Math.Round(example.Item1*hour);
            string countdown=FriendlyEncounterPenaltyPolicy.GetCountdown(expiry,now,out bool hours,out long next);
            Check(countdown==example.Item2 && hours==(example.Item1>=1d),"countdown example "+example.Item1);
            Check(next>now && next<=expiry,"countdown schedules future boundary");
        }
        Check(FriendlyEncounterPenaltyPolicy.GetCountdown(expiry,expiry,out _,out _) == "","expired countdown disappears");
        var ledger=new[]{new FriendlyEncounterPenaltyEntry{ExpiresAtUnixMs=expiry},new FriendlyEncounterPenaltyEntry{ExpiresAtUnixMs=expiry+hour}};
        Check(FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry-1)==10 && FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry)==5 && FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry+hour)==0,"penalties expire independently at exact real times");
        foreach (int multiplier in new[]{1,2,3,4,5}) {
            Check(FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry-1,multiplier)==10*multiplier &&
                  FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry,multiplier)==5*multiplier &&
                  FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry+hour,multiplier)==0,"Scaled penalties expire independently at their original times");
        }
        Check(FriendlyEncounterPenaltyPolicy.ScalePoints(5,0)==5 && FriendlyEncounterPenaltyPolicy.ScalePoints(5,6)==25,"Multiplier outside supported range is clamped to one through five");
        Check(Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(1f,FriendlyEncounterPenaltyPolicy.GetPoints(ledger,expiry,5))-0.75f)<0.0001,"One active kill at multiplier five reduces guaranteed friendship to 75 percent");
        Check(Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(0.3f,5)-0.25f)<0.0001 && Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(1f,5)-0.95f)<0.0001 && FriendlyEncounterPenaltyPolicy.Apply(0.15f,20)==0,"subtracts percentage points after multiplier with zero floor");
        Check(Logger.Errors.Count==0,"successful and recovered report paths emit no errors");
        Console.WriteLine("Passed "+checks+" encounter penalty client, kill-report and countdown checks.");
    }
}
