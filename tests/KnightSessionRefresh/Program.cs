using Comfort.Common;
using EFT;
using EFT.Quests;
using pitTeam.GoingRogue.Trading;
using pitTeam.GoingRogue.Quests;
using pitTeam.Modules;
using SPT.Common.Http;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
async Task Reject(Func<Task> action,string message){try{await action();}catch(InvalidOperationException){checks++;return;}catch(IOException){checks++;return;}throw new Exception(message);}
await KnightSessionRefresh.RefreshAsync();Check(RequestHandler.Calls==0,"startup without a backend session does not request addon data");
const string knight=pitTeam.Utils.Props.KnightTrader;
var info=new Profile.TraderInfo{Standing=0.5,SalesSum=7000};
var ordinary=new Profile.TraderInfo{Unlocked=true,Standing=1.5,SalesSum=200};
var profile=new Profile{Id="player",TradersInfo=new(){{knight,info},{"ordinary",ordinary}}};
var book=new QuestBook{ProfileId=profile.Id};
var existing=new QuestTemplate{Id=KnightQuestPolicy.Competition};book.Templates.Add(existing);book.Progress[existing.Id]=4;
var session=new TestSession{Profile=profile};
session.Traders.Add(new(){Id=knight,Info=info});session.Traders.Add(new(){Id="ordinary",Info=ordinary});
session.Templates=[new(){Id=KnightQuestPolicy.Competition},new(){Id=KnightQuestPolicy.MyLand},new(){Id="foreign"}];
var menu=new MainMenuShowOperation{QuestController=new(){Quests=book}};
var app=new TarkovApplication(menu){Session=session};Singleton<ClientApplication<IEftSession>>.Instance=app;
GameplayModeAddons.Current=GameplayMode.Allegiance;RequestHandler.Mode="Allegiance";RequestHandler.Unlocked=true;
await KnightSessionRefresh.RefreshAsync();
Check(info.Unlocked && !info.Disabled && info.AvailabilityEvents==1,"switching from a locked Allegiance session applies authoritative access with native availability events");
Check(info.Standing==0.5 && info.SalesSum==7000 && ordinary.Standing==1.5 && ordinary.SalesSum==200 && ordinary.Unlocked,"access refresh retains all progression and unrelated trader state");
Check(book.Templates.Count==2 && book.Templates.Any(t=>t.Id==KnightQuestPolicy.MyLand) && !book.Templates.Any(t=>t.Id=="foreign"),"only returned Knight quest templates are merged through the native book boundary");
Check(ReferenceEquals(book.Templates[0],existing) && book.Progress[existing.Id]==4,"existing quest identity/status/counter data remain in the live book");
Check(session.WithCompleted,"native quest request includes completed templates");
await KnightSessionRefresh.RefreshAsync();
Check(info.AvailabilityEvents==1 && book.Templates.Count==2,"repeated refresh does not duplicate quest objects or availability changes");
int requests=session.Requests;
GameplayModeAddons.Current=GameplayMode.GunsForHire;RequestHandler.Mode="GunsForHire";RequestHandler.Unlocked=false;
await KnightSessionRefresh.RefreshAsync();
Check(!info.Unlocked && session.Requests==requests && book.Progress[existing.Id]==4,"leaving Going Rogue relocks the trader without deleting quests/counters or fetching foreign-mode templates");
GameplayModeAddons.Current=GameplayMode.Allegiance;RequestHandler.Mode="Allegiance";RequestHandler.Unlocked=false;
await KnightSessionRefresh.RefreshAsync();Check(!info.Unlocked,"server level-19 access cannot be bypassed by changing mode");
RequestHandler.Unlocked=true;RequestHandler.Disabled=true;await KnightSessionRefresh.RefreshAsync();
Check(info.Disabled && info.Unlocked,"native disabled status remains distinct from the mode access flag");RequestHandler.Disabled=false;
var separate=new Profile.TraderInfo();session.Traders[0].Info=separate;
await KnightSessionRefresh.RefreshAsync();Check(separate.Unlocked && !separate.Disabled,"a separately cached native Trader.Info receives the same authoritative flags");
RequestHandler.Mode="GunsForHire";requests=session.Requests;
await Reject(KnightSessionRefresh.RefreshAsync,"a wrong-mode response cannot update cached access");
Check(session.Requests==requests && info.Unlocked,"wrong-mode response fails before native quest or trader mutations");RequestHandler.Mode="Allegiance";
RequestHandler.Fail=true;await Reject(KnightSessionRefresh.RefreshAsync,"network failure propagates to coordinated mode rollback");RequestHandler.Fail=false;
info.Unlocked=false;session.FailTemplates=true;
await Reject(KnightSessionRefresh.RefreshAsync,"quest reload failure propagates before changing native access");
Check(!info.Unlocked && book.Progress[existing.Id]==4,"failed loading cannot publish a partial unlock or discard progress");session.FailTemplates=false;
book.ProfileId="foreign-player";int calls=RequestHandler.Calls;
await Reject(KnightSessionRefresh.RefreshAsync,"foreign quest books are rejected before requests");
Check(RequestHandler.Calls==calls,"profile mismatch cannot fetch/apply data to another profile");book.ProfileId=profile.Id;
session.Gate=new();session.Requested=new();
Task refreshing=KnightSessionRefresh.RefreshAsync();await session.Requested.Task;
GameplayModeAddons.Current=GameplayMode.GunsForHire;session.Gate.SetResult(session.Templates);
await Reject(()=>refreshing,"mode changes during loading cannot apply a late unlock");
Check(!info.Unlocked,"late response leaves native access unchanged");
GameplayModeAddons.Current=GameplayMode.Allegiance;session.Gate=new();session.Requested=new();
refreshing=KnightSessionRefresh.RefreshAsync();await session.Requested.Task;
var replacementInfo=new Profile.TraderInfo();session.Profile=new(){Id="other-player",TradersInfo=new(){{knight,replacementInfo}}};
session.Gate.SetResult(session.Templates);
await Reject(()=>refreshing,"a profile replaced on the same backend session cannot receive a late refresh");
Check(!replacementInfo.Unlocked && !info.Unlocked,"profile replacement leaves both players' native access untouched");
Console.WriteLine($"PASS: {checks} production Knight session refresh checks; native UI/book methods use controlled hosts, with installed API checks separate.");

public class ClientApplication<T>{public T Session;public T GetClientBackEndSession()=>Session;}
public class TestSession:EFT.IEftSession
{
    public EFT.Profile Profile{get;set;} public List<EFT.Trading.Trader> Traders{get;}=[];
    public List<EFT.Quests.QuestTemplate> Templates;public int Requests;public bool WithCompleted,FailTemplates;
    public TaskCompletionSource<List<EFT.Quests.QuestTemplate>> Gate;public TaskCompletionSource<bool> Requested;
    public Task<List<EFT.Quests.QuestTemplate>> RequestQuestsTemplates(bool completed)
    {Requests++;WithCompleted=completed;Requested?.SetResult(true);if(FailTemplates)throw new IOException("quest request failed");return Gate?.Task??Task.FromResult(Templates);}
}
namespace Comfort.Common{public static class Singleton<T> where T:class{public static T Instance;public static bool Instantiated=>Instance!=null;}}
namespace EFT
{
    public interface IEftSession{Profile Profile{get;}List<Trading.Trader> Traders{get;}Task<List<Quests.QuestTemplate>> RequestQuestsTemplates(bool withCompleted);}
    public class TarkovApplication:ClientApplication<IEftSession>{private MainMenuShowOperation _menuOperation;public TarkovApplication(MainMenuShowOperation menu){_menuOperation=menu;}}
    public class MainMenuShowOperation{public Quests.QuestController QuestController;}
    public class Profile
    {
        public string Id;public Dictionary<string,TraderInfo> TradersInfo;
        public class TraderInfo
        {
            public bool Unlocked,Disabled;public int AvailabilityEvents;public double Standing;public long SalesSum;
            public void SetUnlocked(bool value){if(Unlocked!=value)AvailabilityEvents++;Unlocked=value;}
            public void SetDisabled(bool value){if(Disabled!=value)AvailabilityEvents++;Disabled=value;}
            public void UpdateLevel(){}
        }
    }
}
namespace EFT.Trading{public class Trader{public string Id;public EFT.Profile.TraderInfo Info;}}
namespace EFT.Quests
{
    public class QuestController{public QuestBook Quests;}
    public class QuestTemplate{public string Id;}
    public class QuestBook
    {
        public string ProfileId;public List<QuestTemplate> Templates=[];public Dictionary<string,int> Progress=[];
        public void AddTemplates(IEnumerable<QuestTemplate> templates){foreach(var t in templates)if(!Templates.Any(old=>old.Id==t.Id))Templates.Add(t);}
    }
}
namespace HarmonyLib{public static class AccessTools{public static System.Reflection.FieldInfo Field(Type type,string name)=>type.GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);}}
namespace pitTeam.Modules
{
    public enum GameplayMode{GunsForHire,Allegiance,GoingRogue}
    public static class GameplayModeAddons{public static GameplayMode Current;}
    public static class Logger{public static void LogInfo(string text){}}
}
namespace pitTeam.Utils{public static class Props{public const string KnightTrader="67768b19fa281ca31708b187";}}
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static int Calls;public static string Mode;public static bool Unlocked,Disabled,Fail;
        public static string GetJson(string url)
        {Calls++;if(Fail)throw new IOException("access request failed");return new Newtonsoft.Json.Linq.JObject{["err"]=0,["data"]=new Newtonsoft.Json.Linq.JObject{["mode"]=Mode,["unlocked"]=Unlocked,["disabled"]=Disabled}}.ToString();}
    }
}
