using pitTeam.GoingRogue.Quests;
using EFT;
using EFT.UI.Matchmaker;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;
using pitTeam.Patches;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var profile = new Profile { Info=new(){Level=20}, QuestsData=[new(){Id=KnightQuestPolicy.Competition,Status=EFT.Quests.EQuestStatus.Started}] };
var settings = new RaidSettings {IsPmc=true,LocationId="woods"};
Check(QuestCompanionPreview.RolesFor(profile,settings).SequenceEqual(new[]{"bossKnight"}),"started solo PMC quest selects Knight without any saved roster member");
foreach(string map in new[]{"woods","shoreline","lighthouse"})
{settings.LocationId=map;Check(QuestCompanionPreview.RolesFor(profile,settings).Count==1,"admitted quest maps share preview/spawn selection");}
settings.LocationId="factory";
Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"wrong map does not preview Knight");settings.LocationId="woods";
profile.QuestsData[0].Status=EFT.Quests.EQuestStatus.Success;
Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"finished task does not preview Knight");profile.QuestsData[0].Status=EFT.Quests.EQuestStatus.Started;
profile.Info.Level=19;Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"level gate matches server");profile.Info.Level=20;
profile.Trader.Disabled=true;Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"disabled trader denies preview");profile.Trader.Disabled=false;
settings.IsPmc=false;Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"Scav raids excluded");settings.IsPmc=true;
GameplayModeRuntime.IsGoingRogueActive=false;Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"other modes excluded");GameplayModeRuntime.IsGoingRogueActive=true;
Check(QuestCompanionPreview.RolesFor(null,settings).Count==0,"missing profile excluded safely");
var maps=new[]{"woods","shoreline","lighthouse"};
var objectives=new[]{KnightQuestPolicy.WoodsKills,KnightQuestPolicy.ShorelineKills,KnightQuestPolicy.LighthouseKills};
for(int mask=0;mask<8;mask++)
{
    profile.QuestsData[0].CompletedConditions.Clear();
    for(int i=0;i<3;i++)if((mask&(1<<i))!=0)profile.QuestsData[0].CompletedConditions.Add(new MongoID(objectives[i]));
    for(int i=0;i<3;i++)
    {
        settings.LocationId=maps[i].ToUpperInvariant();
        Check(QuestCompanionPreview.RolesFor(profile,settings).Count==(((mask&(1<<i))==0)?1:0),"each map independently checks its native completed objective: "+mask+"/"+maps[i]);
    }
    Check(profile.QuestsData[0].Status==EFT.Quests.EQuestStatus.Started && profile.QuestsData[0].CompletedConditions.Count==objectives.Where((_,i)=>(mask&(1<<i))!=0).Count(),"spawn admission never edits native quest progress");
}
profile.QuestsData[0].CompletedConditions.Clear();settings.LocationId="woods";
var controller = new MatchmakerPlayersController();
controller.CurrentPlayer = new RaidPlayer(new(){AccountId="player",Info=new(){Nickname="player",Level=43,SavageLockTime=123}});
controller.GroupPlayers.Add(controller.CurrentPlayer);
CompanionRaidPreparation.QuestProfile=JObject.FromObject(new EFT.ProfileDescriptor {Id="native-knight",AccountId="temporary-aid",Info=new(){Level=60,Nickname="Knight"}});
QuestCompanionPreview.Refresh(controller,"bossKnight");
Check(controller.GroupPlayers.Count==2 && controller.GroupPlayers[1].Id=="native-knight","native visual-only group contains the reserved Knight identity");
Check(MainMenuControllerPatch.GroupPlayers.Count==0 && QuestCompanionPreview.RolesFor(profile,settings).Count==1,"visual membership never defeats solo eligibility");
var bound=controller.GroupPlayers[1];
Check(bound.Info.SavageNickname=="player","automatic Knight supplies the ordinary teammate matchmaking nickname required even in PMC preview");
Check(bound.Info.SavageLockTime==123,"automatic Knight carries the current player's matchmaking lock metadata");
#if NATIVE_TRANSLITERATION
bool nativeFailure=false;
try { Transliteration.Transliterate(null,"en"); } catch(NullReferenceException) { nativeFailure=true; }
Check(nativeFailure,"native transliteration reproduces the recorded missing-nickname exception");
Check(bound.Info.SavageNickname.Transliterate("en")=="player","production preview survives the exact native nickname operation before visual binding");
#endif
QuestCompanionPreview.Refresh(controller,"bossKnight");
Check(controller.GroupPlayers.Count==2 && ReferenceEquals(bound,controller.GroupPlayers[1]),"repeated display retains native visual bindings without duplication");
Check(QuestCompanionPreview.IsPreview("temporary-aid") && !QuestCompanionPreview.IsPreview("player"),"UI status/context classification affects only automatic Knight");
profile.QuestsData[0].CompletedConditions.Add(new MongoID(KnightQuestPolicy.WoodsKills));
QuestCompanionPreview.Refresh(controller,QuestCompanionPreview.RolesFor(profile,settings).FirstOrDefault());
Check(controller.GroupPlayers.Count==1 && !QuestCompanionPreview.IsPreview("temporary-aid"),"completed Woods objective removes cached Knight despite an active quest and prepared profile");
profile.QuestsData[0].CompletedConditions.Clear();QuestCompanionPreview.Refresh(controller,"bossKnight");
QuestCompanionPreview.Remove();
Check(controller.GroupPlayers.Count==1 && !QuestCompanionPreview.IsPreview("temporary-aid"),"closing preparation detaches visual-only membership");
QuestCompanionPreview.Refresh(controller,"bossKnight");
Check(controller.GroupPlayers.Count==2 && controller.GroupPlayers[1].Id=="native-knight","loading rebuilds the same reserved profile after preparation closes");
QuestCompanionPreview.Remove();
settings.LocationId="factory";
QuestCompanionPreview.Refresh(controller,QuestCompanionPreview.RolesFor(profile,settings).FirstOrDefault());
Check(controller.GroupPlayers.Count==1 && !QuestCompanionPreview.IsPreview("temporary-aid"),"back from quest confirmation, then a non-quest map, cannot restore cached Knight");
settings.LocationId="woods";
QuestCompanionPreview.Refresh(controller,QuestCompanionPreview.RolesFor(profile,settings).FirstOrDefault());
Check(controller.GroupPlayers.Count==2,"returning alone to an eligible quest map restores automatic Knight");
controller.CurrentPlayer.Info.Nickname=null;QuestCompanionPreview.Remove();QuestCompanionPreview.Refresh(controller,"bossKnight");
Check(controller.GroupPlayers[1].Info.SavageNickname=="Knight","a missing player nickname falls back to Knight without changing his visible identity");
controller.CurrentPlayer.Info.Nickname="player";
QuestCompanionPreview.Remove();
var invited=new RaidPlayer(new(){AccountId="invited",Id="normal-teammate"});
MainMenuControllerPatch.GroupPlayers.Add(invited);
controller.GroupPlayers.Add(invited);
Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"invited ordinary member suppresses automatic Knight");
QuestCompanionPreview.Refresh(controller,QuestCompanionPreview.RolesFor(profile,settings).FirstOrDefault());
Check(controller.GroupPlayers.Count==2 && controller.GroupPlayers.Contains(invited) && !QuestCompanionPreview.IsPreview("temporary-aid"),"back, invite a normal teammate, then quest-map confirmation keeps that teammate and excludes cached Knight");
MainMenuControllerPatch.GroupPlayers.Clear();controller.GroupPlayers.Remove(invited);CompanionRaidPreparation.QuestProfile=null;
QuestCompanionPreview.Refresh(controller,"bossKnight");
Check(controller.GroupPlayers.Count==1,"failed or denied preparation cannot show a fabricated Knight");
LocalRaidChecks.Run(Check, profile, settings);
profile.QuestsData = [new(){Id=KnightQuestPolicy.BadgeBandit,Status=EFT.Quests.EQuestStatus.Started}];
settings.LocationId="tarkovstreets";
var goonRoles=QuestCompanionPreview.RolesFor(profile,settings);
Check(goonRoles.Count==3 && goonRoles.Contains("bossKnight") && goonRoles.Contains("followerBigPipe") && goonRoles.Contains("followerBirdEye"),"Badge Bandit selects all three Goons on Streets");
foreach(var role in goonRoles) CompanionRaidPreparation.Profiles[role]=JObject.FromObject(new EFT.ProfileDescriptor {Id="quest-"+role,AccountId="quest-aid-"+role,Info=new(){Level=60,Nickname=role}});
QuestCompanionPreview.RefreshAll(controller,goonRoles);
Check(controller.GroupPlayers.Count==4 && goonRoles.All(role=>QuestCompanionPreview.IsPreview("quest-aid-"+role)),"all three reserved Goon models appear without becoming invited roster members");
QuestCompanionPreview.RefreshAll(controller,goonRoles);
Check(controller.GroupPlayers.Count==4,"redisplay does not duplicate any Goon visual");
profile.QuestsData[0].CompletedConditions.Add(new MongoID(KnightQuestPolicy.BadgeBanditKills));
QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));
Check(controller.GroupPlayers.Count==1 && goonRoles.All(role=>!QuestCompanionPreview.IsPreview("quest-aid-"+role)),"completed Kollontay objective clears every automatic Goon visual");
profile.QuestsData=[new(){Id=KnightQuestPolicy.EnemySpotted,Status=EFT.Quests.EQuestStatus.Started}];settings.LocationId="woods";
var birdRoles=QuestCompanionPreview.RolesFor(profile,settings);
Check(birdRoles.SequenceEqual(new[]{"followerBirdEye"}),"Enemy Spotted selects only Birdeye for solo Woods");
QuestCompanionPreview.RefreshAll(controller,birdRoles);
Check(controller.GroupPlayers.Count==2 && QuestCompanionPreview.IsPreview("quest-aid-followerBirdEye"),"reserved Birdeye appears in confirmation without an invitation");
QuestCompanionPreview.Remove();QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));
Check(controller.GroupPlayers.Count==2,"loading restores the same reserved quest Birdeye");
settings.LocationId="shoreline";Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"quest Birdeye does not deploy to another map");settings.LocationId="woods";
MainMenuControllerPatch.GroupPlayers.Add(invited);Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"any invited member suppresses automatic quest Birdeye");MainMenuControllerPatch.GroupPlayers.Clear();
profile.QuestsData[0].CompletedConditions.Add(new MongoID(KnightQuestPolicy.EnemySpottedKills));
QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));
Check(controller.GroupPlayers.Count==1,"completed Enemy Spotted objective removes automatic Birdeye preview");
profile.QuestsData=[new(){Id=KnightQuestPolicy.OffLimits,Status=EFT.Quests.EQuestStatus.Started}];settings.LocationId="shoreline";
var pierRoles=QuestCompanionPreview.RolesFor(profile,settings);
Check(pierRoles.Count==2 && pierRoles.Contains("followerBigPipe") && pierRoles.Contains("followerBirdEye") && !pierRoles.Contains("bossKnight"),"Off Limits selects only Pipe and Birdeye on Shoreline");
QuestCompanionPreview.RefreshAll(controller,pierRoles);Check(controller.GroupPlayers.Count==3,"confirmation shows both reserved pier companions");
QuestCompanionPreview.Remove();QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));Check(controller.GroupPlayers.Count==3,"loading restores the same reserved pair");
settings.LocationId="woods";Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"pier quest cannot deploy pair on Woods");settings.LocationId="shoreline";
MainMenuControllerPatch.GroupPlayers.Add(invited);Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"invited member suppresses the automatic pier pair");MainMenuControllerPatch.GroupPlayers.Clear();
profile.QuestsData[0].CompletedConditions.Add(new MongoID(KnightQuestPolicy.OffLimitsKills));QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));
Check(controller.GroupPlayers.Count==1,"finished pier objective removes both automatic previews");
profile.QuestsData=[new(){Id=KnightQuestPolicy.AFavor,Status=EFT.Quests.EQuestStatus.Started}];settings.LocationId="lighthouse";
Check(QuestCompanionPreview.RolesFor(profile,settings).SequenceEqual(new[]{"followerBirdEye"}),"Partizan hunt selects only Birdeye on Lighthouse");
QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));Check(controller.GroupPlayers.Count==2,"hunt Birdeye appears in confirmation");
QuestCompanionPreview.Remove();QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));Check(controller.GroupPlayers.Count==2,"hunt Birdeye restores for loading");
settings.LocationId="woods";QuestCompanionPreview.RefreshAll(controller,QuestCompanionPreview.RolesFor(profile,settings));Check(controller.GroupPlayers.Count==1,"returning to Woods removes hunt Birdeye");settings.LocationId="lighthouse";
MainMenuControllerPatch.GroupPlayers.Add(invited);Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"invitation suppresses automatic hunt companion");MainMenuControllerPatch.GroupPlayers.Clear();
profile.QuestsData[0].CompletedConditions.Add(new MongoID(KnightQuestPolicy.AFavorKills));Check(QuestCompanionPreview.RolesFor(profile,settings).Count==0,"completed hunt does not auto-spawn Birdeye");
Console.WriteLine($"PASS: {checks} production quest preview checks; native visual construction is a host double, not Unity rendering evidence.");

namespace EFT
{
    public class LocalizationManager {public static LocalizationManager Instance=new();public string Culture="en";}
    public enum ERaidMode {Online,Local,Coop}
    public class RaidSettings {public bool IsPmc,IsPveOffline;public string LocationId;public ERaidMode RaidMode;public bool Local=>RaidMode==ERaidMode.Local;}
    public class ProfileDescriptor {public string Id,AccountId;public JsonType.PlayerInfo Info;}
    public class Profile
    {
        public string Id,AccountId;public JsonType.PlayerInfo Info;
        public List<QuestData> QuestsData=[];public TraderInfo Trader=new();
        public Profile(){} public Profile(ProfileDescriptor data){Id=data.Id;AccountId=data.AccountId;Info=data.Info;}
        public PlayerVisualRepresentation GetVisualEquipmentState(bool firstLevelItemsOnly)=>new(){Info=Info};
        public bool TryGetTraderInfo(string id,out TraderInfo trader){trader=Trader;return true;}
        public class TraderInfo {public bool Disabled;}
    }
    public class QuestData {public string Id;public Quests.EQuestStatus Status;public HashSet<MongoID> CompletedConditions=new();}
    public class GroupPlayer {public string Id,AccountId;public JsonType.PlayerInfo Info;public PlayerVisualRepresentation PlayerVisualRepresentation;public bool IsReady;}
    public class PlayerVisualRepresentation {public JsonType.PlayerInfo Info;}
}
namespace EFT.Quests {public enum EQuestStatus {Started,Success}}
namespace JsonType {public class PlayerInfo {public int Level,SavageLockTime;public string Nickname,GameVersion,SavageNickname;public bool HasCoopExtension;}}
namespace EFT.UI.Matchmaker
{
    public class RaidPlayer:GroupPlayer {public RaidPlayer(GroupPlayer p){Id=p.Id;AccountId=p.AccountId;Info=p.Info;PlayerVisualRepresentation=p.PlayerVisualRepresentation;}}
    public class MatchmakerPlayersController {public RaidPlayer CurrentPlayer;public List<RaidPlayer> GroupPlayers=[];public Group Group=new();}
    public class Group {public void RemoveOwner(){}}
}
namespace pitTeam.Modules
{
    internal static class GameplayModeRuntime {internal static bool IsGoingRogueActive=true;}
    internal static class GameplayModeAddons {internal static bool CanSpawnQuestCompanion(string quest,string map,Func<string,bool> completed)=>KnightQuestPolicy.NeedsCompanion(quest,map,completed);}
    internal static class CompanionRaidPreparation {internal static JToken QuestProfile; internal static Dictionary<string,JToken> Profiles=new(); internal static JToken QuestProfileFor(string role)=>Profiles.TryGetValue(role,out var p)?p:null;}
    internal static class Logger {internal static void LogInfo(string message){}}
}
namespace pitTeam.Patches
{
    internal static class MainMenuControllerPatch {internal static List<RaidPlayer> GroupPlayers=[];}
    internal static class OtherPlayerProfileScreenPatch {internal static Newtonsoft.Json.JsonConverter[] GetDefaultJsonConverters()=>[];}
    internal static class SyntheticTeammateAutoJoinLoader {internal static void EnsureLocalGroupOwner(MatchmakerPlayersController controller){}}
}
namespace pitTeam.Utils
{
    internal static class Props
    {
        internal static string KnightTrader="knight";
        internal static Dictionary<string,List<string>> Quests=new(){{"Knight",[KnightQuestPolicy.Competition,KnightQuestPolicy.MyLand,KnightQuestPolicy.BadgeBandit]},{"BigPipe",[KnightQuestPolicy.BadgeBandit,KnightQuestPolicy.OffLimits]},{"BirdEye",[KnightQuestPolicy.BadgeBandit,KnightQuestPolicy.EnemySpotted,KnightQuestPolicy.OffLimits,KnightQuestPolicy.AFavor]}};
        internal static Dictionary<string,List<string>> QuestsLocations=new(){{KnightQuestPolicy.Competition,["woods","shoreline","lighthouse"]},{KnightQuestPolicy.MyLand,["bigmap"]},{KnightQuestPolicy.BadgeBandit,["tarkovstreets"]},{KnightQuestPolicy.EnemySpotted,["woods"]},{KnightQuestPolicy.OffLimits,["shoreline"]},{KnightQuestPolicy.AFavor,["lighthouse"]}};
    }
}

public readonly struct MongoID : IEquatable<MongoID>
{
    private readonly string value;
    public MongoID(string value){this.value=value;}
    public bool Equals(MongoID other)=>value==other.value;
    public override bool Equals(object other)=>other is MongoID id&&Equals(id);
    public override int GetHashCode()=>value?.GetHashCode()??0;
}
