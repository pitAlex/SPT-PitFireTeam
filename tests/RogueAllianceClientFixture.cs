using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.Quests;
using pitTeam.Modules;
using pitTeam.GoingRogue;
using Newtonsoft.Json.Linq;

namespace Comfort.Common { public static class Singleton<T> { public static T Instance;public static bool Instantiated=>Instance!=null; } }
namespace EFT.Quests {
 public enum EQuestStatus { Started,AvailableForFinish,Success,Fail,AvailableForStart,Locked,FailRestartable }
 public class Quest{public string Id;public EQuestStatus Status;public QuestTemplate Template;}
 public class QuestTemplate {public Dictionary<EQuestStatus,List<Condition>> Conditions=new();}
 public class Condition {} public class ConditionKills:Condition {public string[] savageRole;}
 public class ConditionHit:Condition {public string[] savageRole;}
 public class ConditionCounterCreator:Condition {public List<Condition> Conditions=new();}
}
namespace EFT {
 public enum WildSpawnType {pmcUSEC,exUsec,bossKnight,followerBigPipe,followerBirdEye}
 public enum EBotState {Active,NonActive}
 public enum EBotEnemyCause {addPlayer,addPlayerToBoss}
 public interface IPlayer {bool IsYourPlayer{get;} string ProfileId{get;} Profile Profile{get;} }
 public class Profile { public List<Quest> QuestsData=new();public Info Info=new();public Trader Trader=new();public bool TryGetTraderInfo(string id,out Trader trader){trader=Trader;return true;} }
 public class Trader {public bool Disabled;public double Standing=1;public void SetStanding(double value){Standing=value;} }
 public class Info { public Settings Settings=new();} public class Settings {public WildSpawnType Role;}
 public class Health {public bool IsAlive=true;}
 public class Player:IPlayer {public Health HealthController=new();public bool IsAI;public bool IsYourPlayer{get;set;}public string ProfileId{get;set;}public Profile Profile{get;set;}=new();public GameWorld GameWorld;public AIData AIData=new();public void OnBeenKilledByAggressor(){} }
 public class AIData {public BotOwner BotOwner;}
 public class BotOwner {public EBotState BotState=EBotState.Active;public bool IsDead;public Player GetPlayer;public Profile Profile=>GetPlayer.Profile;public BotsGroup BotsGroup=new();}
 public class GameWorld {public Player MainPlayer;public List<Player> AllAlivePlayersList=new();public void OnGameStarted(){} }
 public interface IEftSession {} public class ClientApplication<T> {public Session Session;public Session GetClientBackEndSession()=>Session;}public class Session {public Profile Profile;}
 public class BotsGroup {public HashSet<IPlayer> Enemies=new();public int Attempts,RejectNext;public bool IsEnemy(IPlayer p)=>Enemies.Contains(p);public bool AddEnemy(IPlayer p,EBotEnemyCause cause){Attempts++;if(RejectNext>0){RejectNext--;return false;}return Enemies.Add(p);}}
}
namespace pitTeam.Utils {public static class Props {public const string KnightTrader="knight";} }
public class BotGroupEnemyInfo {public Player Player;}
public class BotGroupWarnData {
 public BotOwner Boss;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public List<BotGroupEnemyInfo> UpdateWarnTargets(List<BotGroupEnemyInfo> targets)=>new(targets);
}
namespace pitTeam.Modules {
 public enum GameplayMode{Allegiance,GunsForHire}
 public static class GameplayModeAddons {public static GameplayMode Current=GameplayMode.Allegiance;}
 public static class GameplayModeRuntime {public static List<Task> Pending=new(); public static Task RunRosterRequest(Action action){var task=Task.Run(action);Pending.Add(task);return task;} }
 public static class FriendlyEncounterPenaltyRuntime {public static int Reduction;public static int GetRecruitmentReductionPercent()=>Reduction;}
 public class Follower {public string LeaderId="human";public BotOwner Bot;public bool HasPersistentProgress;public BotOwner GetBot()=>Bot;}
 public static class BossPlayers {public static List<Follower> Followers=new();public static bool IsFollower(BotOwner bot)=>Followers.Any(f=>f.Bot==bot);public static List<Follower> GetFollowersByBoss(string id)=>Followers.Where(f=>f.LeaderId==id).ToList();public static Follower GetFollowerByProfileId(string id)=>Followers.FirstOrDefault(f=>f.Bot.GetPlayer.ProfileId==id);}
 public static class Logger {public static List<string> Errors=new();public static void LogInfo(string value){}public static void LogError(string value)=>Errors.Add(value);}
}
namespace SPT.Common.Http {
 public static class RequestHandler {
  public static int Calls;public static double Standing=1;public static bool LoseFirstResponse;private static HashSet<string> receipts=new();
  public static void Reset(){Calls=0;Standing=1;LoseFirstResponse=false;receipts.Clear();}
  public static string PostJson(string url,string body){Calls++;var data=JObject.Parse(body);string key=(string)data["raidId"]+"/"+(string)data["victimId"];
   if(receipts.Add(key))Standing=RogueAlliancePolicy.AfterPenalty(Standing,(string)data["role"]);
   if(LoseFirstResponse){LoseFirstResponse=false;throw new System.IO.IOException("lost after commit");}
   return new JObject{["err"]=0,["data"]=new JObject{["standing"]=Standing}}.ToString();
  }
 }
}
internal static class Checks {
 static int count;static GameWorld world;static Player human;static void Check(bool ok,string text){if(!ok)throw new Exception(text);count++;}
 static Quest HostileQuest(string role="exUsec") => new(){Id=Guid.NewGuid().ToString("N"),Status=EQuestStatus.Started,
  Template=new(){Conditions=new(){[EQuestStatus.AvailableForFinish]=new(){new ConditionCounterCreator{Conditions=new(){new ConditionKills{savageRole=new[]{role}}}}}}}};
 static void HostileQuestChecks(){
  foreach(string role in new[]{"exUsec","bossKnight","followerBigPipe","followerBirdEye"}){
   Setup();var quest=HostileQuest(role);human.Profile.QuestsData.Add(quest);
   Check(RogueAlliance.Conflict,"startup reads active custom quest completion objectives: "+role);
   foreach(var status in new[]{EQuestStatus.Success,EQuestStatus.Started,EQuestStatus.AvailableForFinish,EQuestStatus.Fail,EQuestStatus.FailRestartable,EQuestStatus.AvailableForStart,EQuestStatus.Locked}){
    quest.Status=status;bool expected=status==EQuestStatus.Started||status==EQuestStatus.AvailableForFinish;
    Check(RogueAlliance.Conflict==expected,"live status transition without cached conflict: "+role+"/"+status);
   }
   quest.Status=EQuestStatus.Started;quest.Template.Conditions[EQuestStatus.Fail]=quest.Template.Conditions[EQuestStatus.AvailableForFinish];
   quest.Template.Conditions[EQuestStatus.AvailableForFinish]=new();Check(!RogueAlliance.Conflict,"failure-only killing conditions never suspend alliance: "+role);
  }
  foreach(string role in new[]{"assault","pmcUSEC","bossSanitar","followerBully","","Any"}){
   Setup();human.Profile.QuestsData.Add(HostileQuest(role));Check(!RogueAlliance.Conflict,"unrelated kill target: "+role);
  }
  Setup();var late=HostileQuest();var template=late.Template;late.Template=null;human.Profile.QuestsData.Add(late);
  Check(!RogueAlliance.Conflict,"unbound quest template does not invent a target");late.Template=template;
  Check(RogueAlliance.Conflict,"native startup template binding is observed without a restart");
  human.Profile.QuestsData.Clear();Check(!RogueAlliance.Conflict,"removed or replaced quest releases restriction");
  Setup();late=HostileQuest();late.Template.Conditions[EQuestStatus.AvailableForFinish]=new(){new ConditionCounterCreator{Conditions=new(){new ConditionHit{savageRole=new[]{"exUsec"}}}}};
  human.Profile.QuestsData.Add(late);Check(!RogueAlliance.Conflict,"hit objectives are not kill objectives");
  Setup();human.Profile.QuestsData.Add(HostileQuest());
  Comfort.Common.Singleton<ClientApplication<IEftSession>>.Instance=new(){Session=new(){Profile=human.Profile}};
  Comfort.Common.Singleton<GameWorld>.Instance=null;
  Check(RogueAlliance.Conflict,"startup/lobby session profile is checked without a raid");
  human.Profile.QuestsData.Clear();Check(!RogueAlliance.Conflict,"lobby completion/removal has no stale conflict");
  Comfort.Common.Singleton<ClientApplication<IEftSession>>.Instance=null;
 }
 static void Wait(){Task.WaitAll(GameplayModeRuntime.Pending.ToArray());GameplayModeRuntime.Pending.Clear();}
 static Player Actor(string id,WildSpawnType role,bool follower=false,bool saved=false){var player=new Player{ProfileId=id,GameWorld=world,IsAI=true};player.Profile.Info.Settings.Role=role;var bot=new BotOwner{GetPlayer=player};player.AIData.BotOwner=bot;world.AllAlivePlayersList.Add(player);if(follower)BossPlayers.Followers.Add(new(){Bot=bot,HasPersistentProgress=saved});return player;}
 static void Setup(){Wait();RogueAlliance.Reset();SPT.Common.Http.RequestHandler.Reset();BossPlayers.Followers.Clear();Logger.Errors.Clear();GameplayModeAddons.Current=GameplayMode.Allegiance;FriendlyEncounterPenaltyRuntime.Reduction=0;
  world=new();human=new(){ProfileId="human",IsYourPlayer=true,GameWorld=world};world.MainPlayer=human;Comfort.Common.Singleton<GameWorld>.Instance=world;
  human.Profile.QuestsData=RogueAlliancePolicy.Quests.Select(id=>new Quest{Id=id,Status=EQuestStatus.Success}).ToList();
 }
 private static void WarningChecks(){
  new HarmonyLib.Harmony("pit.fixture.rogue-warnings").Patch(
   HarmonyLib.AccessTools.Method(typeof(BotGroupWarnData),nameof(BotGroupWarnData.UpdateWarnTargets)),
   postfix:new HarmonyLib.HarmonyMethod(typeof(RogueWarningPatch),"Postfix"));
  foreach(var status in new[]{EQuestStatus.Started,EQuestStatus.AvailableForFinish,EQuestStatus.Success,EQuestStatus.Fail}){
   Setup();human.Profile.QuestsData=new(){new(){Id=RogueAlliancePolicy.Quests[0],Status=status}};
   var guard=Actor("guard-"+status,WildSpawnType.exUsec);var other=new Player{ProfileId="other"};
   var warning=new BotGroupWarnData{Boss=guard.AIData.BotOwner};
   var targets=warning.UpdateWarnTargets(new(){new(){Player=human},new(){Player=other}});
   Check(targets.Any(t=>t.Player==human)==(status!=EQuestStatus.Success),"Rogue warnings stop only after first quest native Success: "+status);
   Check(targets.Any(t=>t.Player==other),"other warning targets remain native");
  }
  Setup();var rogue=Actor("warning-rogue",WildSpawnType.exUsec);var controller=new BotGroupWarnData{Boss=rogue.AIData.BotOwner};
  bool Warns()=>controller.UpdateWarnTargets(new(){new(){Player=human}}).Count!=0;
  human.Profile.Trader.Standing=0.1;Check(!Warns(),"warning immunity uses unlocked friendship, not recruitment standing or invited Goons");
  GameplayModeAddons.Current=GameplayMode.GunsForHire;Check(Warns(),"other mode retains native warnings");GameplayModeAddons.Current=GameplayMode.Allegiance;
  human.Profile.QuestsData.Add(HostileQuest());Check(Warns(),"conflicting Rogue-kill quest restores native warning eligibility");
  Setup();rogue=Actor("betrayed-guard",WildSpawnType.exUsec);controller.Boss=rogue.AIData.BotOwner;
  RogueAlliance.PlayerKilled(Actor("betrayal-victim",WildSpawnType.exUsec),human);Wait();Check(Warns(),"raid betrayal restores native behavior");
  Setup();foreach(var role in new[]{WildSpawnType.pmcUSEC,WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}){
   controller.Boss=Actor("other-warning-"+role,role).AIData.BotOwner;Check(Warns(),"non-Rogue warning groups unchanged: "+role);
  }
 }
 public static void Main(){
  HostileQuestChecks();
  HostileTaskRelationships();
  RecruitmentChecks();
  WarningChecks();
  Setup();Check(RogueAlliance.Friendship("exUsec")==true&&RogueAlliance.Acceptance("exUsec")==0.5f,"completed chain at standing one unlocks 50 percent acceptance");
  FriendlyEncounterPenaltyRuntime.Reduction=15;Check(Math.Abs(RogueAlliance.Acceptance("exUsec").Value-0.425f)<0.000001,"one friendly Rogue kill reduces unescorted acceptance by 15 percent relative");
  FriendlyEncounterPenaltyRuntime.Reduction=30;Check(Math.Abs(RogueAlliance.Acceptance("exUsec").Value-0.35f)<0.000001,"two friendly Rogue kills reduce unescorted acceptance by 30 percent relative");
  FriendlyEncounterPenaltyRuntime.Reduction=0;
  human.Profile.Trader.Standing=0.99;Check(!RogueAlliance.CanRecruit("exUsec")&&RogueAlliance.Friendship("exUsec")==true,"standing controls recruitment separately from friendship");
  human.Profile.Trader.Standing=1;human.Profile.QuestsData.Add(HostileQuest());
  Check(RogueAlliance.Conflict&&RogueAlliance.Friendship("exUsec")==false&&!RogueAlliance.NeedsCompanion("6775d9957e2dbcb3bd0a02c7","woods",_=>false),"active hostile task suspends friendship/automatic companions");
  foreach(var role in new[]{WildSpawnType.exUsec,WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}){
   Setup();var saved=Actor("saved-"+role,role,true,true);RogueAlliance.PlayerKilled(saved,human);Wait();
   Check(SPT.Common.Http.RequestHandler.Calls==0&&human.Profile.Trader.Standing==1&&RogueAlliance.Friendship("exUsec")==true,"spawned saved ally exempt from standing tax and betrayal: "+role);
  }
  Setup();var questGoon=Actor("quest-knight",WildSpawnType.bossKnight,true);RogueAlliance.PlayerKilled(questGoon,human);Wait();
  Check(SPT.Common.Http.RequestHandler.Calls==0&&RogueAlliance.Friendship("exUsec")==true,"temporary quest Goon is spawned and exempt too");
  Setup();var fieldRogue=Actor("field-rogue",WildSpawnType.exUsec,true);RogueAlliance.PlayerKilled(fieldRogue,human);Wait();
  Check(SPT.Common.Http.RequestHandler.Calls==1&&human.Profile.Trader.Standing==0.975&&RogueAlliance.Friendship("exUsec")==false,"field recruited Rogue retains standing tax and raid betrayal");
  Setup();var victim=Actor("victim",WildSpawnType.exUsec);var ambient=Actor("ambient",WildSpawnType.followerBigPipe);var recruit=Actor("recruit",WildSpawnType.exUsec,true);var ordinary=Actor("ordinary",WildSpawnType.pmcUSEC,true);
  var enemyPmc=Actor("enemy-pmc",WildSpawnType.pmcUSEC);RogueAlliance.PlayerKilled(victim,enemyPmc);Wait();
  Check(SPT.Common.Http.RequestHandler.Calls==0&&RogueAlliance.Friendship("exUsec")==true,"AI kills never tax the player or betray alliance");
  SPT.Common.Http.RequestHandler.LoseFirstResponse=true;RogueAlliance.PlayerKilled(victim,human);Wait();
  Check(Math.Abs(human.Profile.Trader.Standing-0.975)<0.000001&&SPT.Common.Http.RequestHandler.Standing==0.975,"player Rogue kill deducts exactly 0.025 locally/backend");
  Check(SPT.Common.Http.RequestHandler.Calls==2&&Logger.Errors.Count==0,"lost report response retries same receipt");
  Check(ambient.AIData.BotOwner.BotsGroup.Enemies.SetEquals(new IPlayer[]{human,recruit,ordinary}),"ambient Goons target the player and both Rogue/ordinary teammates");
  Check(!recruit.AIData.BotOwner.BotsGroup.IsEnemy(human)&&!ordinary.AIData.BotOwner.BotsGroup.IsEnemy(human),"saved/recruited teammates never turn against the player");
  Check(RogueAlliance.Friendship("exUsec")==false&&!RogueAlliance.CanRecruit("exUsec"),"betrayal disables friendship/recruitment for the raid");
  RogueAlliance.PlayerKilled(victim,human);Wait();Check(SPT.Common.Http.RequestHandler.Calls==2,"duplicate victim death callback never reports twice");
  var later=Actor("late-rogue",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();Check(later.AIData.BotOwner.BotsGroup.IsEnemy(human)&&later.AIData.BotOwner.BotsGroup.IsEnemy(recruit),"late spawns inherit raid hostility to full squad");
  RogueAlliance.PlayerKilled(ambient,human);Wait();Check(Math.Abs(SPT.Common.Http.RequestHandler.Standing-0.875)<0.000001,"Goon death costs 0.1 after Rogue death");
  Setup();Check(RogueAlliance.Friendship("exUsec")==true,"new raid restores eligible alliance");
  GameplayModeAddons.Current=GameplayMode.GunsForHire;victim=Actor("other-mode",WildSpawnType.exUsec);RogueAlliance.PlayerKilled(victim,human);Wait();Check(SPT.Common.Http.RequestHandler.Calls==0,"other mode has no Rogue penalties");
  Console.WriteLine("PASS: "+count+" production Rogue alliance client/death/hostility/report checks. Controlled EFT hosts.");
 }
 private static void HostileTaskRelationships(){
  foreach(var status in new[]{EQuestStatus.Started,EQuestStatus.AvailableForFinish})
  foreach(var role in new[]{WildSpawnType.exUsec,WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}){
   Setup();var task=HostileQuest();task.Status=status;human.Profile.QuestsData.Add(task);
   var ambient=Actor("ambient",role);var ordinary=Actor("ordinary-pmc",WildSpawnType.pmcUSEC);
   RogueAlliance.ApplyHostility();
   Check(ambient.AIData.BotOwner.BotsGroup.IsEnemy(human),"active hostile task declares ambient enemy without a betrayal kill or followers: "+role+"/"+status);
   Check(!ordinary.AIData.BotOwner.BotsGroup.IsEnemy(human)&&SPT.Common.Http.RequestHandler.Calls==0&&human.Profile.Trader.Standing==1,
    "hostile task does not alter ordinary PMC relations, standing or reports");
   var member=Actor("saved-pmc",WildSpawnType.pmcUSEC,true,true);var savedRogue=Actor("saved-rogue",WildSpawnType.exUsec,true,true);var savedGoon=Actor("saved-pipe",WildSpawnType.followerBigPipe,true,true);
   RogueAlliance.ApplyHostility();
   foreach(var follower in new[]{member,savedRogue,savedGoon}){
    Check(ambient.AIData.BotOwner.BotsGroup.IsEnemy(follower)&&follower.AIData.BotOwner.BotsGroup.IsEnemy(ambient),"hostility includes late squad members in both directions");
    Check(!follower.AIData.BotOwner.BotsGroup.IsEnemy(human),"owned Rogue/Goon/PMC never turns against player");
   }
   int attempts=ambient.AIData.BotOwner.BotsGroup.Attempts;RogueAlliance.ApplyHostility();
   Check(ambient.AIData.BotOwner.BotsGroup.Attempts==attempts,"existing relationships are not repeatedly re-added");
   var late=Actor("late-ambient",WildSpawnType.exUsec);late.AIData.BotOwner.BotsGroup.RejectNext=1;
   RogueAlliance.ApplyHostility();Check(!late.AIData.BotOwner.BotsGroup.IsEnemy(human),"native deferred/rejected admission is respected");
   RogueAlliance.ApplyHostility();Check(late.AIData.BotOwner.BotsGroup.IsEnemy(human),"late spawn retries admission through native AddEnemy");
   task.Status=EQuestStatus.Success;var after=Actor("after-completion",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();
   Check(!after.AIData.BotOwner.BotsGroup.IsEnemy(human)&&ambient.AIData.BotOwner.BotsGroup.IsEnemy(human),"hand-in stops new task hostility without erasing living enemy memory");
  }
  foreach(var status in new[]{EQuestStatus.Success,EQuestStatus.Fail,EQuestStatus.AvailableForStart,EQuestStatus.Locked}){
   Setup();var task=HostileQuest();task.Status=status;human.Profile.QuestsData.Add(task);var ambient=Actor("not-active",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();
   Check(!ambient.AIData.BotOwner.BotsGroup.IsEnemy(human),"inactive task does not force ambient hostility: "+status);
  }
  Setup();human.Profile.QuestsData.Add(HostileQuest());GameplayModeAddons.Current=GameplayMode.GunsForHire;var other=Actor("other-mode",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();
  Check(!other.AIData.BotOwner.BotsGroup.IsEnemy(human),"other mode keeps native Rogue relations");
  Setup();human.Profile.QuestsData.Add(HostileQuest());other=Actor("old-world",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();
  world=new(){MainPlayer=new(){ProfileId="new-human",IsYourPlayer=true}};Comfort.Common.Singleton<GameWorld>.Instance=world;human=world.MainPlayer;human.GameWorld=world;
  var fresh=Actor("new-world",WildSpawnType.exUsec);RogueAlliance.ApplyHostility();Check(!fresh.AIData.BotOwner.BotsGroup.IsEnemy(human),"task state does not leak into replacement raid/profile");
 }
 private static void RecruitmentChecks(){
  for(int completed=0;completed<RogueAlliancePolicy.Quests.Length;completed++)
  foreach(bool escort in new[]{false,true}){
   Setup();human.Profile.QuestsData=human.Profile.QuestsData.Take(completed).ToList();
   if(escort)Actor("knight",WildSpawnType.bossKnight,true);
   Check(RogueAlliance.Acceptance("exUsec")==0&&!RogueAlliance.CanRecruit("exUsec"),"unfinished quest chain cannot recruit even with Goon escort: "+completed+"/"+escort);
   Check(RogueAlliance.Friendship("exUsec")== (completed>0),"first-quest friendship remains separate from full-chain recruitment");
  }
  foreach(double standing in new[]{0.99,1,1.1,1.25,1.5,1.75,2,2.5}){
   Setup();human.Profile.Trader.Standing=standing;
   float expected=standing<1?0:(float)(0.5+0.2*Math.Min(1,standing-1));
   Check(Math.Abs(RogueAlliance.Acceptance("exUsec").Value-expected)<0.000001,"standing progression without escort: "+standing);
   Check(RogueAlliance.CanRecruit("exUsec")== (standing>=1),"standing one admission retained: "+standing);
  }
  foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
  foreach(string state in new[]{"alive","dead","health","inactive","ambient","foreign"}){
   Setup();var goon=Actor("escort",role,state!="ambient");
   if(state=="dead")goon.AIData.BotOwner.IsDead=true;
   if(state=="health")goon.HealthController.IsAlive=false;
   if(state=="inactive")goon.AIData.BotOwner.BotState=EBotState.NonActive;
   if(state=="foreign")BossPlayers.Followers.Single().LeaderId="foreign";
   Check(RogueAlliance.Acceptance("exUsec")== (state=="alive"?1f:0.5f),"escort guarantee requires actual living owned Goon: "+role+"/"+state);
   if(state=="alive"){
    FriendlyEncounterPenaltyRuntime.Reduction=100;
    Check(RogueAlliance.Acceptance("exUsec")==1f,"explicit Goon guarantee supersedes chance penalties");
    human.Profile.Trader.Standing=0.99;
    Check(!RogueAlliance.CanRecruit("exUsec")&&RogueAlliance.Acceptance("exUsec")==0f,"escort cannot bypass minimum standing");
   }
  }
  Setup();Check(RogueAlliance.Acceptance("pmcUSEC")==null&&RogueAlliance.CanRecruit("pmcUSEC"),"ordinary PMC policy remains outside Rogue gates");
 }
}
