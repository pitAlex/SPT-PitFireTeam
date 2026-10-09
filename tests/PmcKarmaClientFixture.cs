using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using Newtonsoft.Json;
using pitTeam.Modules;
using pitTeam.Shared;

namespace Comfort.Common { public static class Singleton<T> { public static T Instance; } }
namespace EFT {
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum ExitStatus { Survived, Runner, Killed, MissingInAction, Left, Transit }
    public interface IPlayer { EPlayerSide Side {get;} string ProfileId {get;} bool IsYourPlayer {get;} }
    public class Profile { public readonly float KarmaValue=0.2f; }
    public class Health { public bool IsAlive=true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player : IPlayer {
        public string ProfileId {get;set;} public EPlayerSide Side {get;set;} public bool IsYourPlayer {get;set;}
        public bool IsAI; public Profile Profile=new Profile(); public Health HealthController=new Health(); public AIData AIData=new AIData();
    }
    public class GameWorld { public Player MainPlayer; }
    public class Group { public bool Hostile; public bool IsEnemy(IPlayer p)=>Hostile; }
    public class Enemies { public bool Hostile; public bool IsEnemy(IPlayer p)=>Hostile; }
    public class Enemy { public IPlayer Person; }
    public class Memory { public Enemy GoalEnemy; }
    public class BotOwner { public string ProfileId; public Player GetPlayer; public bool IsDead; public Group BotsGroup=new Group(); public Enemies EnemiesController=new Enemies(); public Memory Memory=new Memory(); }
}
namespace EFT.Ballistics { public struct DamageInfo { public float Damage; public Attacker Player; } public class Attacker { public IPlayer iPlayer; } }
namespace EFT.UI { public class GUISounds { public List<bool> Sounds=new List<bool>(); public void PlayKarmaSound(bool positive)=>Sounds.Add(positive); } }
namespace pitTeam {
    public class Flag {public bool Value;}
    public static class pitFireTeam {public static Flag pitFireTeamFLAG=new Flag(),badGuy=new Flag();}
}
namespace pitTeam.Components {
    public class Boss {public Player realPlayer;}
    public class BotFollowerPlayer {public bool IsSquadMate; public BotOwner Bot; public Boss Owner; public BotOwner GetBot()=>Bot;public Boss GetBoss()=>Owner;}
}
namespace pitTeam.Modules {
    public static class Logger {public static List<string> Errors=new List<string>();public static void LogError(object s){lock(Errors)Errors.Add(s.ToString());}public static void LogInfo(string s){} }
    public static class GameplayModeRuntime {
        public static bool IsAllegiance; public static List<Task> Tasks=new List<Task>();
        public static ManualResetEvent HoldNext; public static int WorkersStarted;
        public static T GetEffectiveValue<T>(pitTeam.Flag flag)=>(T)(object)flag.Value;
        public static bool GetEffectiveValue(pitTeam.Flag flag)=>flag.Value;
        public static Task RunRosterRequest(Action action){var hold=HoldNext;HoldNext=null;var task=Task.Run(()=>{Interlocked.Increment(ref WorkersStarted);hold?.WaitOne();action();});Tasks.Add(task);return task;}
    }
    public static class AllegiancePmcFriendship {public static HashSet<string> Allowed=new HashSet<string>();public static bool CanRecruit(BotOwner bot,IPlayer p)=>Allowed.Contains(bot.ProfileId);}
    public static class BossPlayers {
        public static List<pitTeam.Components.BotFollowerPlayer> Followers=new List<pitTeam.Components.BotFollowerPlayer>();
        public static pitTeam.Components.BotFollowerPlayer GetFollowerByProfileId(string id)=>Followers.FirstOrDefault(f=>f.Bot.ProfileId==id);
        public static List<pitTeam.Components.BotFollowerPlayer> GetFollowersByBoss(string id)=>Followers.Where(f=>f.Owner.realPlayer.ProfileId==id).ToList();
    }
}
namespace SPT.Common.Http {
    public static class RequestHandler {
        public static List<PmcKarmaReport> Reports=new List<PmcKarmaReport>(); public static double Karma=0.2; public static bool LoseResponse;
        private static Dictionary<string,int> Receipts=new Dictionary<string,int>();
        public static void Reset(){Reports.Clear();Receipts.Clear();Karma=0.2;LoseResponse=false;}
        public static string PostJson(string route,string json){
            var report=JsonConvert.DeserializeObject<PmcKarmaReport>(json); Reports.Add(report);
            string key=report.RaidId+report.Kind+report.VictimProfileId;
            if(!Receipts.TryGetValue(key,out int sound)) {
                double before=Karma;
                Karma=PmcKarmaPolicy.Apply(Karma,report.Kind=="kill" ? -0.025 : PmcKarmaPolicy.RaidGain(report.ExtractedRecruitIds.Distinct().Count(),report.Allegiance,report.HadFriendlies,report.KilledFriendly,report.RecruitedAny));
                sound=before==Karma ? 0 : report.Kind=="kill" ? -1 : report.ExtractedRecruitIds.Count>0 ? 1 : 0; Receipts[key]=sound;
            }
            if(LoseResponse){LoseResponse=false;throw new Exception("Lost response");}
            return JsonConvert.SerializeObject(new {err=0,data=new PmcKarmaResult {KarmaValue=Karma,Sound=sound}});
        }
    }
}
public sealed class PumpContext : SynchronizationContext {
    private Queue<Action> Work=new Queue<Action>();
    public override void Post(SendOrPostCallback callback,object state){lock(Work)Work.Enqueue(()=>callback(state));}
    public void Pump(){lock(Work)while(Work.Count>0)Work.Dequeue()();}
}
public static class KarmaClientChecks {
    private static int count; private static Player human; private static PumpContext context=new PumpContext();
    private static void Check(bool b,string label){if(!b)throw new Exception(label);count++;}
    private static void Complete(){Task.WaitAll(GameplayModeRuntime.Tasks.ToArray());context.Pump();GameplayModeRuntime.Tasks.Clear();Check(Logger.Errors.Count==0,"No runtime errors");}
    private static void Reset(bool allegiance=false){Complete();Logger.Errors.Clear();BossPlayers.Followers.Clear();AllegiancePmcFriendship.Allowed.Clear();SPT.Common.Http.RequestHandler.Reset();GameplayModeRuntime.IsAllegiance=allegiance;pitTeam.pitFireTeam.pitFireTeamFLAG.Value=true;pitTeam.pitFireTeam.badGuy.Value=false;
        human=new Player {ProfileId="human",Side=EPlayerSide.Usec,IsYourPlayer=true};Comfort.Common.Singleton<GameWorld>.Instance=new GameWorld {MainPlayer=human};Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance=new EFT.UI.GUISounds();PmcKarmaRuntime.BeginRaid();}
    private static BotOwner Bot(string id,EPlayerSide side=EPlayerSide.Usec){var p=new Player {ProfileId=id,Side=side,IsAI=true};var bot=new BotOwner {ProfileId=id,GetPlayer=p};p.AIData.BotOwner=bot;return bot;}
    private static void Recruit(BotOwner bot,bool saved=false,bool spawned=false){BossPlayers.Followers.Add(new pitTeam.Components.BotFollowerPlayer {Bot=bot,IsSquadMate=saved,Owner=new pitTeam.Components.Boss {realPlayer=human}});if(!saved&&!spawned)PmcKarmaRuntime.NoteRecruitment(bot,human,false);}
    private static void Hit(BotOwner b){PmcKarmaRuntime.RememberBeforePlayerDamage(new EFT.Ballistics.DamageInfo {Damage=10,Player=new EFT.Ballistics.Attacker {iPlayer=human}},b.GetPlayer);}
    public static void Main(){SynchronizationContext.SetSynchronizationContext(context);
        foreach(bool allegiance in new[]{false,true}) {
            Reset(allegiance);var recruit=Bot("recruit",EPlayerSide.Bear);Recruit(recruit);SPT.Common.Http.RequestHandler.LoseResponse=true;
            Hit(recruit);PmcKarmaRuntime.RecordKill(recruit.GetPlayer,human);PmcKarmaRuntime.RecordKill(recruit.GetPlayer,human);Complete();
            Check(SPT.Common.Http.RequestHandler.Karma==0.175 && Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.SequenceEqual(new[]{false}),"Recruit betrayal is mode-independent, deduplicated, and sounds once after a lost response");
            Check(Math.Abs(human.Profile.KarmaValue-0.175f)<0.00001,"Native readonly karma field is reconciled on the main context");
            Reset(allegiance);var saved=Bot("saved");Recruit(saved,true);var spawned=Bot("spawned");Recruit(spawned,false,true);Hit(saved);Hit(spawned);PmcKarmaRuntime.RecordKill(saved.GetPlayer,human);PmcKarmaRuntime.RecordKill(spawned.GetPlayer,human);Complete();
            Check(SPT.Common.Http.RequestHandler.Reports.Count==0,"Saved and automatically spawned followers are excluded");
            Reset(allegiance);var raw=Bot("raw",allegiance?EPlayerSide.Bear:EPlayerSide.Usec);AllegiancePmcFriendship.Allowed.Add(raw.ProfileId);PmcKarmaRuntime.NoteFriendly(raw);Hit(raw);raw.BotsGroup.Hostile=true;AllegiancePmcFriendship.Allowed.Clear();PmcKarmaRuntime.RecordKill(raw.GetPlayer,human);Complete();
            Check(SPT.Common.Http.RequestHandler.Karma==0.175,"Unrecruited pre-damage friendship survives retaliation and opposite faction in Allegiance");
            PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Survived);Complete();Check(SPT.Common.Http.RequestHandler.Karma==0.175,"Friendly kill blocks quiet recovery");
            Reset(allegiance);var hostile=Bot("hostile");hostile.BotsGroup.Hostile=true;Hit(hostile);PmcKarmaRuntime.RecordKill(hostile.GetPlayer,human);Complete();Check(SPT.Common.Http.RequestHandler.Reports.Count==0,"Hostile victim does not cost karma");
            Reset(allegiance);var one=Bot("one");var two=Bot("two",EPlayerSide.Bear);Recruit(one);Recruit(two);Recruit(Bot("saved"),true);var dead=Bot("dead");Recruit(dead);dead.GetPlayer.HealthController.IsAlive=false;
            PmcKarmaRuntime.NoteFriendly(one);PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Runner);PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Runner);Complete();
            Check(SPT.Common.Http.RequestHandler.Reports.Single().ExtractedRecruitIds.Count==2 && SPT.Common.Http.RequestHandler.Karma==0.24,"Only distinct living raid recruits reward at extraction, including run-through");
            Check(Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.SequenceEqual(new[]{true}),"One positive sound for the combined confirmed extraction gain");
            Reset(allegiance);PmcKarmaRuntime.NoteFriendly(Bot("friendly"));PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Killed);Complete();
            Check(SPT.Common.Http.RequestHandler.Karma==(allegiance?0.205:0.2) && Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.Count==0,"Quiet recovery is once per completed Allegiance raid and silent");
            Reset(allegiance);var recruited=Bot("no-stack");PmcKarmaRuntime.NoteFriendly(recruited);Recruit(recruited);recruited.IsDead=true;
            PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Killed);Complete();Check(SPT.Common.Http.RequestHandler.Karma==0.2,"Recruiting suppresses quiet recovery even without extraction");
            Reset(allegiance);human.Side=EPlayerSide.Savage;var scavFriend=Bot("scav-friend");Recruit(scavFriend);Hit(scavFriend);PmcKarmaRuntime.RecordKill(scavFriend.GetPlayer,human);PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Survived);Complete();Check(SPT.Common.Http.RequestHandler.Reports.Count==0,"Player Scav raids do not touch PMC karma");
        }
        Reset(true);var carried=Bot("carried");PmcKarmaRuntime.NoteFriendly(carried);Recruit(carried);PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Transit);Complete();Check(SPT.Common.Http.RequestHandler.Reports.Count==0,"Transit does not extract recruits or stack recovery");PmcKarmaRuntime.BeginRaid(true);PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Survived);Complete();Check(SPT.Common.Http.RequestHandler.Karma==0.22,"Transit retains recruit identities for final extraction");
        Reset();var zero=Bot("zero");Recruit(zero);SPT.Common.Http.RequestHandler.Karma=0;PmcKarmaRuntime.RecordKill(zero.GetPlayer,human);Complete();Check(Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.Count==0,"No negative sound at zero");
        Reset();Recruit(Bot("max"));SPT.Common.Http.RequestHandler.Karma=1;PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Survived);Complete();Check(Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.Count==0,"No positive sound at one");
        Reset();var lastVictim=Bot("last-victim");Recruit(lastVictim);Recruit(Bot("survivor"));SPT.Common.Http.RequestHandler.Karma=1;
        using(var release=new ManualResetEvent(false)) {
            GameplayModeRuntime.WorkersStarted=0;GameplayModeRuntime.HoldNext=release;
            PmcKarmaRuntime.RecordKill(lastVictim.GetPlayer,human);lastVictim.IsDead=true;
            PmcKarmaRuntime.EndRaid(human.ProfileId,ExitStatus.Survived);
            bool scheduled=SpinWait.SpinUntil(()=>Volatile.Read(ref GameplayModeRuntime.WorkersStarted)==2,5000);
            release.Set();Complete();Check(scheduled,"Both asynchronous reports scheduled");
        }
        Check(SPT.Common.Http.RequestHandler.Reports.Select(r=>r.Kind).SequenceEqual(new[]{"kill","end"}) && SPT.Common.Http.RequestHandler.Karma==0.995,"Kill then extraction retain capture order even when first worker starts late near the karma cap");
        Check(Comfort.Common.Singleton<EFT.UI.GUISounds>.Instance.Sounds.SequenceEqual(new[]{false,true}),"Ordered kill and extraction feedback uses both native sounds");
        Console.WriteLine("PASS: "+count+" PMC karma client checks.");
    }
}
