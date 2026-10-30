using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var settings=new EFT.RaidSettings{IsPmc=true,LocationId="bigmap"};
CompanionRaidPreparation.Prepare(settings,[]);
Check(SPT.Common.Http.RequestHandler.Calls==0,"empty group requires no preparation");
CompanionRaidPreparation.Prepare(settings,["123","456","123"]);
string first=CompanionRaidPreparation.TokenFor("123");
Check(Guid.TryParseExact(first,"N",out _),"fresh matching creates a valid reservation identity");
Check(SPT.Common.Http.RequestHandler.Calls==1 && CompanionRaidPreparation.TokenFor("456")==first,"selected members share one reservation");
Check(CompanionRaidPreparation.TokenFor("foreign")==null,"unselected members do not inherit the reservation");
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check(SPT.Common.Http.RequestHandler.Calls==1 && CompanionRaidPreparation.TokenFor("123")==first,"returning from profile view does not reroll equipment");
settings.KeyId="new-match-bookkeeping";
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check(SPT.Common.Http.RequestHandler.Calls==1 && CompanionRaidPreparation.TokenFor("123")==first,"native match bookkeeping does not reroll unchanged gear");
string knightJoin=(string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["123"];
string pipeJoin=(string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["456"];
CompanionRaidPreparation.Remove("123");
Check(CompanionRaidPreparation.TokenFor("123")==null && CompanionRaidPreparation.TokenFor("456")==first,"kick removes only that member's reservation");
Check((string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["456"]==pipeJoin,"kick retains the other member's join identity");
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check(CompanionRaidPreparation.TokenFor("123")==first && (string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["123"]!=knightJoin,"rejoining gets a fresh member revision without replacing the whole group token");
Check((string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["456"]==pipeJoin,"another Goon does not regenerate on rejoin");
int stableCalls=SPT.Common.Http.RequestHandler.Calls;
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check(SPT.Common.Http.RequestHandler.Calls==stableCalls,"reopening AcceptScreen or a profile preserves the selection");
CompanionRaidPreparation.RemoveAll();
Check(CompanionRaidPreparation.TokenFor("123")==null && CompanionRaidPreparation.TokenFor("456")==null && ((JArray)SPT.Common.Http.RequestHandler.LastRequest["members"]).Count==0,
    "disbanding on AcceptScreen releases every selected kit");
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check((string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["456"]!=pipeJoin,"rebuilding a disbanded group gives every member a fresh join");
settings.LocationId="woods";
CompanionRaidPreparation.Prepare(settings,["123","456"]);
Check(CompanionRaidPreparation.TokenFor("123")!=first,"a changed map configuration cannot reuse earlier equipment");
int calls=SPT.Common.Http.RequestHandler.Calls;
GameplayModeAddons.Current=GameplayMode.Allegiance;
Check(CompanionRaidPreparation.TokenFor("123")==null,"mode changes hide the old reservation");
CompanionRaidPreparation.Reset();
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(SPT.Common.Http.RequestHandler.Calls==calls,"ordinary modes keep their existing spawn flow");
GameplayModeAddons.Current=GameplayMode.GoingRogue;GameplayModeAddons.Available=false;
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(SPT.Common.Http.RequestHandler.Calls==calls,"absent addon does not initialize preparation");
GameplayModeAddons.Available=true;settings.IsPmc=false;
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(SPT.Common.Http.RequestHandler.Calls==calls,"player Scavs keep their existing flow");
settings.IsPmc=true;settings.isInTransition=true;
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(SPT.Common.Http.RequestHandler.Calls==calls,"transit does not generate fresh kits");
settings.isInTransition=false;pitTeam.Utils.Utils.Transit=true;
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(SPT.Common.Http.RequestHandler.Calls==calls,"carried raid flag also excludes preparation");
pitTeam.Utils.Utils.Transit=false;SPT.Common.Http.RequestHandler.Fail=true;
CompanionRaidPreparation.Prepare(settings,["123"]);
string lost=CompanionRaidPreparation.TokenFor("123");
Check(!string.IsNullOrEmpty(lost) && Logger.Errors==1,"lost responses retain the attempt token and log failure");
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(CompanionRaidPreparation.TokenFor("123")==lost && SPT.Common.Http.RequestHandler.Calls==calls+1,"profile redisplay cannot silently replace a failed reservation");
string lostJoin=(string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["123"];
CompanionRaidPreparation.Remove("123");
Check(CompanionRaidPreparation.TokenFor("123")==null && ((JArray)SPT.Common.Http.RequestHandler.LastRequest["members"]).Count==0,"removing the last member submits an empty selection even if its response is lost");
CompanionRaidPreparation.Prepare(settings,["123"]);
Check((string)SPT.Common.Http.RequestHandler.LastRequest["memberRevisions"]["123"]!=lostJoin,"rejoining after a lost release still has a fresh identity");
CompanionRaidPreparation.Reset();SPT.Common.Http.RequestHandler.Fail=false;
CompanionRaidPreparation.Prepare(settings,["123"]);
Check(CompanionRaidPreparation.TokenFor("123")!=lost && CompanionRaidPreparation.TokenFor("123")!=first,"new matching discards the old attempt");
CompanionRaidPreparation.Reset();
Check(CompanionRaidPreparation.TokenFor("123")==null,"raid cleanup removes the token");
Console.WriteLine($"PASS: {checks} production client reservation checks; controlled HTTP/raid-setting doubles, not Unity loading behavior.");

namespace EFT {internal class RaidSettings {[JsonIgnore] public bool IsPmc,isInTransition;[JsonProperty("location")] public string LocationId;[JsonProperty("keyId")] public string KeyId;}}
namespace pitTeam.Utils {internal static class Utils {internal static bool Transit;internal static bool FlagGet(string key)=>Transit;}}
namespace pitTeam.Modules
{
    internal enum GameplayMode {GoingRogue,Allegiance}
    internal static class GameplayModeAddons
    {
        internal static GameplayMode Current=GameplayMode.GoingRogue;
        internal static bool Available=true;
        internal static bool IsAvailable(GameplayMode mode)=>Available;
    }
    internal static class Logger {internal static int Errors;internal static void LogInfo(string value){} internal static void LogError(string value){Errors++;}}
}
namespace SPT.Common.Http
{
    internal static class RequestHandler
    {
        internal static int Calls;internal static bool Fail;internal static JObject LastRequest;
        internal static string PostJson(string url,string request)
        {
            Calls++;
            var data=JObject.Parse(request);LastRequest=data;
            if(Fail)throw new Exception("Lost preparation response");
            return new JObject{["err"]=0,["data"]=new JObject{["Token"]=data["token"]}}.ToString(Formatting.None);
        }
    }
}
