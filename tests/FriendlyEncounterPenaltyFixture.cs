using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using EFT;
using EFT.Ballistics;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Shared;
using SPT.Common.Http;

namespace EFT {
    public enum WildSpawnType {assault,pmcBEAR,bossKnight,followerBigPipe,followerBirdEye,exUsec}
    public class Settings {public WildSpawnType Role=WildSpawnType.pmcBEAR;}
    public enum EPlayerSide {Usec,Bear,Savage}
    public interface IPlayer {bool IsYourPlayer{get;} string ProfileId{get;} EPlayerSide Side{get;}}
    public class Info {public string Nickname="recruit";public Settings Settings=new Settings();}
    public class Profile {public Info Info=new Info();}
    public class AIData {public BotOwner BotOwner;}
    public class BotsGroup {public bool Enemy;public bool IsEnemy(IPlayer p)=>Enemy;}
    public class EnemyController {public bool Enemy;public bool IsEnemy(IPlayer p)=>Enemy;}
    public class Goal {public Player Person;} public class Memory {public Goal GoalEnemy;}
    public class BotOwner {public BotsGroup BotsGroup=new();public EnemyController EnemiesController=new();public Memory Memory=new();public bool SelectedFriendly=true;}
    public class Health {public bool IsAlive=true;}
    public class Player : IPlayer {public bool IsYourPlayer{get;set;} public bool IsAI=true;public string ProfileId{get;set;} public string AccountId="123";public EPlayerSide Side{get;set;} public Profile Profile=new Profile();public AIData AIData=new(){BotOwner=new()};public Health HealthController=new();}
}
namespace EFT.Ballistics {public class DamageInfo {public Attacker Player;public float Damage=100;}public class Attacker {public IPlayer iPlayer;}}
namespace pitTeam.Utils {public static class Props {public static readonly List<WildSpawnType> BossFollowersType=new List<WildSpawnType>{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye};}}
namespace UnityEngine {public static class Random {public static int Range(int min,int max)=>min;}}
namespace pitTeam.Components {
    public class Boss {public Player realPlayer;}
    public class BotFollowerPlayer {public bool IsSquadMate;public bool HasPersistentProgress=>IsSquadMate;public Boss Owner;public Boss GetBoss()=>Owner;}
}
namespace pitTeam {
    public class Flag {public bool Value=true;}
    public class IntSetting {public int Value=1;}
    public class Language {public string[] traitorKillMessages={"traitor"};public string[] jerkKillMessages={"jerk"};}
    public static class pitFireTeam {public static Flag npcSendMessage=new Flag();public static IntSetting friendlyEncounterChance=new IntSetting();public static Language optionsLang=new Language();}
}
namespace pitTeam.Modules {
    public static class AllegianceScavRelations {public static void RememberBeforeDamage(DamageInfo damage,Player target) { }}
    public static class GameplayModeAddons {public static bool? RogueFriend=true;public static bool? RecruitmentFriendship(string role)=>role=="exUsec"?RogueFriend:null;}
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
    public static class PlayerKilledPatch {public static void TryRememberFriendlyPmcBeforePlayerDamage(DamageInfo damage,Player target)=>KillBoundary.TryRememberFriendlyPmcBeforePlayerDamage(damage,target);}
    public static class KillBoundary {
        private const string KillMessageRoute="/singleplayer/pitfireteam/postraid/kill-message";
        private static readonly HashSet<string> RecordedKillMessageVictims=new HashSet<string>();
        private static readonly object RecordedKillMessageLock=new object();
        private static bool IsFriendlyPmcKillMessageContextEnabled()=>false;
        private static readonly HashSet<string> PlayerDamagedPmcVictims=new();
        private static readonly HashSet<string> FriendlyBeforePlayerDamageVictims=new();
        private static bool WasFriendlyBeforePlayerDamage(string id)=>FriendlyBeforePlayerDamageVictims.Contains(id);
        public static void Hit(Player target,IPlayer attacker)=>TryRememberFriendlyPmcBeforePlayerDamage(new EFT.Ballistics.DamageInfo{Player=new EFT.Ballistics.Attacker{iPlayer=attacker}},target);
        private static bool IsPmc(EPlayerSide side)=>side==EPlayerSide.Usec || side==EPlayerSide.Bear;
        public static void Record(Player victim,IPlayer aggressor)=>TryRecordPlayerKillMessage(victim,aggressor);
        __RECORD_METHOD__
        __KIND_METHOD__
        __TEXT_METHOD__
        __HIT_METHOD__
        __EARLY_METHOD__
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
                if(!State.Entries.Any(entry=>entry.Id==id)) State.Entries.Add(new FriendlyEncounterPenaltyEntry{Id=id,ExpiresAtUnixMs=killedAt+FriendlyEncounterPenaltyPolicy.DurationMs,AffectsRecruitment=(string)body["victimRole"]=="exUsec"});
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
        GameplayModeAddons.RogueFriend=true;
        pitTeam.pitFireTeam.friendlyEncounterChance.Value=30;
        FriendlyEncounterPenaltyRuntime.ReloadAsync().GetAwaiter().GetResult();
    }
    public static void Main() {
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}) {
            Reset();var ally=Recruit("goon-"+role);ally.Profile.Info.Settings.Role=role;
            pitTeam.Patches.KillBoundary.Record(ally,Human);GameplayModeRuntime.WaitReports();
            Check(RequestHandler.Posts.Count==0 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==0,
                "recruited boss ally kill has no traitor report or encounter penalty: "+role);
        }
        Reset();var victim=Recruit("recruit");pitTeam.Patches.KillBoundary.Record(victim,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==1 && (string)RequestHandler.Posts[0]["messageKind"]=="traitor","Allegiance opposite-faction recruit uses existing traitor report despite disabled Friendly PMC Side context");
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"traitor kill immediately reduces friendly chances");
        long savedExpiry=RequestHandler.State.Entries.Single().ExpiresAtUnixMs;
        foreach(int chancePercent in new[]{5,10,20,30,40,60,80}) {
            pitTeam.pitFireTeam.friendlyEncounterChance.Value=chancePercent;
            Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"Each active kill reduces current chances by 15% independently of the slider");
            Check(RequestHandler.State.Entries.Single().ExpiresAtUnixMs==savedExpiry && RequestHandler.Posts.Count==1,"Changing chance retains original expiry and kill identity");
            Check(FriendlyEncounterPenaltyPolicy.GetReductionPercent(FriendlyEncounterPenaltyRuntime.GetActiveEntries(),FriendlyEncounterPenaltyRuntime.NowUnixMs)==FriendlyEncounterPenaltyRuntime.GetReductionPercent(),"Roster and selection use the same reduction");
        }
        FriendlyEncounterPenaltyRuntime.ReloadAsync().GetAwaiter().GetResult();
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"Legacy kill ledger adopts relative penalties without migration");
        pitTeam.Patches.KillBoundary.Record(victim,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==1 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"duplicate death prefix/postfix cannot add a second penalty");
        Reset();pitTeam.pitFireTeam.npcSendMessage.Value=false;pitTeam.Patches.KillBoundary.Record(Recruit("silent"),Human);GameplayModeRuntime.WaitReports();
        Check((string)RequestHandler.Posts.Single()["messageText"]=="" && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"Raid End Messages off still saves penalty with blank traitor record");
        Reset();pitTeam.Patches.KillBoundary.Record(Recruit("spawned",true),Human);
        pitTeam.Patches.KillBoundary.Record(Recruit("foreign",false,false),Human);
        pitTeam.Patches.KillBoundary.Record(Recruit("other-killer"),new Player{ProfileId="ai",IsYourPlayer=false});
        pitTeam.Patches.KillBoundary.Record(new Player{ProfileId="ordinary"},Human);
        var nonAi=Recruit("non-ai");nonAi.IsAI=false;pitTeam.Patches.KillBoundary.Record(nonAi,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==0,"spawned, foreign, other-aggressor, ordinary and non-AI victims are excluded");
        foreach(bool spawned in new[]{false,true}) {
            Reset();var rogue=Recruit("rogue-recruit-"+spawned,spawned);rogue.Profile.Info.Settings.Role=WildSpawnType.exUsec;
            pitTeam.Patches.KillBoundary.Record(rogue,Human);GameplayModeRuntime.WaitReports();
            Check(RequestHandler.Posts.Count==(spawned?0:1) && FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent()==(spawned?0:15),"only field recruited Rogue pays encounter and recruitment penalties, spawned exempt");
        }
        Reset();var savedCleanup=Recruit("saved-cleanup",true);savedCleanup.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        pitTeam.Patches.KillBoundary.Hit(savedCleanup,Human);pitTeam.Patches.KillBoundary.Record(savedCleanup,Human);
        BossPlayers.Followers.Remove(savedCleanup.ProfileId);pitTeam.Patches.KillBoundary.Record(savedCleanup,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0&&FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent()==0,"saved exemption survives native follower cleanup before death postfix");
        Reset();var ambientRogue=new Player{ProfileId="ambient-rogue",Side=EPlayerSide.Savage};ambientRogue.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        pitTeam.Patches.KillBoundary.EarlyDamage(ambientRogue,new DamageInfo{Player=new Attacker{iPlayer=Human}});
        ambientRogue.AIData.BotOwner.BotsGroup.Enemy=true;ambientRogue.AIData.BotOwner.SelectedFriendly=false;
        pitTeam.Patches.KillBoundary.Record(ambientRogue,Human);pitTeam.Patches.KillBoundary.Record(ambientRogue,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==1 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15 && FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent()==15,"pre-hit friendly Rogue counts once after native retaliation and alliance revocation");
        FriendlyEncounterPenaltyRuntime.ReloadAsync().GetAwaiter().GetResult();
        Check(FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent()==15 && RequestHandler.State.Entries.Single().AffectsRecruitment,"Rogue-specific recruitment tag survives acknowledgement/reload");
        Reset();var suspended=new Player{ProfileId="suspended-rogue",Side=EPlayerSide.Savage};suspended.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        GameplayModeAddons.RogueFriend=false;pitTeam.Patches.KillBoundary.Hit(suspended,Human);
        GameplayModeAddons.RogueFriend=true;pitTeam.Patches.KillBoundary.Record(suspended,Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0,"suspended alliance at first hit is not a friendly kill even before native enemy admission");
        foreach(bool selected in new[]{false,true}) {
            Reset();var hostile=new Player{ProfileId="hostile-rogue-"+selected,Side=EPlayerSide.Savage};hostile.Profile.Info.Settings.Role=WildSpawnType.exUsec;
            hostile.AIData.BotOwner.BotsGroup.Enemy=true;hostile.AIData.BotOwner.SelectedFriendly=selected;
            pitTeam.Patches.KillBoundary.Hit(hostile,Human);
            hostile.AIData.BotOwner.BotsGroup.Enemy=false;hostile.AIData.BotOwner.SelectedFriendly=true;
            pitTeam.Patches.KillBoundary.Hit(hostile,Human);pitTeam.Patches.KillBoundary.Record(hostile,Human);GameplayModeRuntime.WaitReports();
            Check(RequestHandler.Posts.Count==0,"first-hit hostile Rogue never becomes a friendly penalty after relationship changes");
        }
        Reset();pitTeam.Patches.KillBoundary.Record(Recruit("ordinary-pmc-recruit"),Human);GameplayModeRuntime.WaitReports();
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15 && FriendlyEncounterPenaltyRuntime.GetRecruitmentReductionPercent()==0,"ordinary recruit penalty does not reduce Rogue acceptance");
        Reset();GameplayModeRuntime.IsAllegiance=false;pitTeam.Patches.KillBoundary.Record(Recruit("hired-mode"),Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==0 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==0,"Guns for Hire is excluded");
        Reset();RequestHandler.LoseResponse=true;pitTeam.Patches.KillBoundary.Record(Recruit("retry"),Human);GameplayModeRuntime.WaitReports();
        Check(RequestHandler.Posts.Count==2 && RequestHandler.State.Entries.Count==1 && FriendlyEncounterPenaltyRuntime.GetReductionPercent()==15,"lost response retries same raid/victim without duplicate penalty");
        Reset();RequestHandler.Hold=true;pitTeam.Patches.KillBoundary.Record(Recruit("first-pending"),Human);RequestHandler.Started.Wait();
        pitTeam.Patches.KillBoundary.Record(Recruit("second-pending"),Human);
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==30,"pending reports reduce chance immediately before server response");
        pitTeam.pitFireTeam.friendlyEncounterChance.Value=5;
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==30,"Both pending reports retain their 30% relative reduction while the response is held");
        RequestHandler.Hold=false;RequestHandler.Release.Set();GameplayModeRuntime.WaitReports();
        Check(FriendlyEncounterPenaltyRuntime.GetReductionPercent()==30 && RequestHandler.State.Entries.Count==2,"older response retains the later pending penalty");
        const long hour=3600000;long expiry=24*hour;
        foreach(var example in new[]{Tuple.Create(24d,"24"),Tuple.Create(23.75d,"24"),Tuple.Create(23.5d,"23.5"),Tuple.Create(2d,"2"),Tuple.Create(1.51d,"1.6"),Tuple.Create(1.5d,"1.5"),Tuple.Create(1d,"1"),Tuple.Create(50d/60d,"50"),Tuple.Create(35d/60d,"35"),Tuple.Create(5d/60d,"5")}) {
            long now=expiry-(long)Math.Round(example.Item1*hour);
            string countdown=FriendlyEncounterPenaltyPolicy.GetCountdown(expiry,now,out bool hours,out long next);
            Check(countdown==example.Item2 && hours==(example.Item1>=1d),"countdown example "+example.Item1);
            Check(next>now && next<=expiry,"countdown schedules future boundary");
        }
        Check(FriendlyEncounterPenaltyPolicy.GetCountdown(expiry,expiry,out _,out _) == "","expired countdown disappears");
        var ledger=new[]{new FriendlyEncounterPenaltyEntry{ExpiresAtUnixMs=expiry},new FriendlyEncounterPenaltyEntry{ExpiresAtUnixMs=expiry+hour}};
        Check(FriendlyEncounterPenaltyPolicy.GetReductionPercent(ledger,expiry-1)==30 && FriendlyEncounterPenaltyPolicy.GetReductionPercent(ledger,expiry)==15 && FriendlyEncounterPenaltyPolicy.GetReductionPercent(ledger,expiry+hour)==0,"penalties expire independently at exact real times");
        ledger[0].AffectsRecruitment=true;
        Check(FriendlyEncounterPenaltyPolicy.GetRecruitmentReductionPercent(ledger,expiry-1)==15 && FriendlyEncounterPenaltyPolicy.GetRecruitmentReductionPercent(ledger,expiry)==0,"only tagged Rogue kills reduce acceptance and expire on the same clock");
        for(int kills=0;kills<=12;kills++) {
            var stacked=Enumerable.Range(0,kills).Select(i=>new FriendlyEncounterPenaltyEntry{Id=i.ToString(),ExpiresAtUnixMs=expiry}).ToArray();
            int reduction=FriendlyEncounterPenaltyPolicy.GetReductionPercent(stacked,expiry-1);
            Check(reduction==Math.Min(100,kills*15),"Kill reductions stack additively with a 100% maximum");
            Check(FriendlyEncounterPenaltyPolicy.GetReductionPercent(stacked,expiry)==0,"Each reduction expires at the exact real time");
            foreach(float chance in new[]{0f,0.03f,0.05f,0.10f,0.15f,0.30f,0.80f,0.90f,1f})
                Check(Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(chance,reduction)-chance*(1f-reduction/100f))<0.000001,"Penalty applies to each final chance rather than subtracting percentage points");
        }
        Check(Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(0.3f,15)-0.255f)<0.000001 && Math.Abs(FriendlyEncounterPenaltyPolicy.Apply(0.03f,15)-0.0255f)<0.000001,"15% reduction applies equally to PMC and Fence-scaled Scav chances");
        Check(FriendlyEncounterPenaltyPolicy.Apply(0.5f,-15)==0.5f && FriendlyEncounterPenaltyPolicy.Apply(0.5f,150)==0,"Invalid reduction percentages clamp safely");
        Check(Logger.Errors.Count==0,"successful and recovered report paths emit no errors");
        Console.WriteLine("Passed "+checks+" encounter penalty client, kill-report and countdown checks.");
    }
}
