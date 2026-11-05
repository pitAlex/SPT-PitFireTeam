using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using pitTeam.GoingRogue;
using pitTeam.GoingRogue.Server;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Common.Models.Logging;
using Path = System.IO.Path;

internal static class RogueAllianceChecks
{
    internal static void Run(JsonUtil json, FriendlyServerSettingsService settings, SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable templates, Action<bool,string> check)
    {
        RogueAlliance.Configure(templates);
        var player=new BotBase {Quests=[],TradersInfo=new(){[KnightTraderCatalog.Id]=new(){Standing=1}}};
        for(int count=0;count<=7;count++)
        {
            player.Quests=RogueAlliancePolicy.Quests.Take(count).Select(id=>new QuestStatus {StartTime=0,StatusTimers=[],QId=new(id),Status=QuestStatusEnum.Success}).ToList();
            check(RogueAlliance.Completed(player)==count && RogueAlliancePolicy.Acceptance(count,1,false)==(count==7?0.5f:0f),"full-chain recruitment gate "+count);
            check(RogueAlliancePolicy.Acceptance(count,1,true)==(count==7?1f:0f),"escort cannot bypass incomplete quests "+count);
            check(RogueAlliance.RosterInvitation(player)==(count==7?100:0),"post-raid invitation independently requires full chain "+count);
        }
        foreach(double standing in new[]{0.99,1,1.05,1.25,1.5,1.75,1.95,2,3,double.NaN}){
            float expected=double.IsNaN(standing)||standing<1?0:(float)(0.5+0.2*Math.Min(1,standing-1));
            check(Math.Abs(RogueAlliancePolicy.Acceptance(7,standing,false)-expected)<0.0001f,"smooth standing recruitment chance "+standing);
            check(RogueAlliancePolicy.Acceptance(7,standing,true)==(double.IsNaN(standing)||standing<1?0:1),"escort guarantee preserves standing gate "+standing);
        }
        player.Quests=[new(){StartTime=0,StatusTimers=[],QId=new(RogueAlliancePolicy.Quests[0]),Status=QuestStatusEnum.AvailableForFinish}];
        check(RogueAlliance.Completed(player)==0,"ready-to-hand-in is not completed progression");
        foreach(string role in new[]{"bossKnight","followerBigPipe","followerBirdEye","exUsec"})
        foreach(double standing in new[]{0.1,0.49,0.5,0.99,1.0,1.5})
        {
            player.Quests=[];player.TradersInfo[KnightTraderCatalog.Id].Standing=standing;
            check((RogueAlliance.Restriction(player,role)==null)==(standing>=RogueAlliancePolicy.RequiredStanding(role)),"standing admission "+role+"/"+standing);
            foreach(string id in templates.Quests.Where(entry => RogueAlliance.HasHostileObjectives(entry.Value)).Select(entry => entry.Key.ToString()))
            foreach(var status in new[]{QuestStatusEnum.Started,QuestStatusEnum.AvailableForFinish,QuestStatusEnum.Success,QuestStatusEnum.Fail})
            {
                player.Quests=[new(){StartTime=0,StatusTimers=[],QId=new(id),Status=status}];
                bool conflict=status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish;
                check(RogueAlliance.Conflict(player)==conflict && (RogueAlliance.Restriction(player,role)==null)==(!conflict&&standing>=RogueAlliancePolicy.RequiredStanding(role)),"hostile task state/admission "+role+"/"+id+"/"+status);
            }
        }
        player.Quests=RogueAlliancePolicy.Quests.Select(id=>new QuestStatus{StartTime=0,StatusTimers=[],QId=new(id),Status=QuestStatusEnum.Success}).ToList();
        player.TradersInfo[KnightTraderCatalog.Id].Standing=1;
        check(RogueAlliance.RosterInvitation(player)==100,"accepted Rogue does not face a second level roll for roster invitation");
        player.TradersInfo[KnightTraderCatalog.Id].Standing=0.99;
        check(RogueAlliance.RosterInvitation(player)==0,"low standing cannot produce new Rogue roster invitations");
        check(RogueAlliancePolicy.AfterPenalty(1,"exUsec")==0.975 && RogueAlliancePolicy.AfterPenalty(1,"bossKnight")==0.9,"distinct Rogue/Goon penalty amounts");
        check(RogueAlliancePolicy.AfterPenalty(0.1,"exUsec")==0.1,"legacy minimum standing retained");

        NativePersistence(json,settings,check).GetAwaiter().GetResult();
    }
    private static async Task NativePersistence(JsonUtil json,FriendlyServerSettingsService settings,Action<bool,string> check)
    {
        string original=Environment.CurrentDirectory;
        string work=Path.Combine(original,"standing-persistence");Directory.CreateDirectory(Path.Combine(work,"user","profiles"));
        Environment.CurrentDirectory=work;
        try
        {
            var files=new FileUtil();var session=new MongoId("abcdabcdabcdabcdabcdabcd");
            var profiles=new ConcurrentDictionary<MongoId,SptProfile>();
            SaveServer Server()
            {
                var config=(CoreConfig)RuntimeHelpers.GetUninitializedObject(typeof(CoreConfig));
                config.Features=(ServerFeatures)RuntimeHelpers.GetUninitializedObject(typeof(ServerFeatures));
                var ctor=typeof(SaveServer).GetConstructors().Single();
                var server=(SaveServer)ctor.Invoke(ctor.GetParameters().Select(p=>p.Name switch {
                    "fileUtil"=>(object)files,"jsonUtil"=>json,"hashUtil"=>new HashUtil(null!),"coreConfig"=>config,
                    "saveLoadRouters"=>Array.Empty<SaveLoadRouter>(),_=>null}).ToArray());
                typeof(SaveServer).GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(server,profiles);
                return server;
            }
            profiles[session]=new(){ProfileInfo=new(),CharacterData=new(){PmcData=new(){
                TradersInfo=new(){[KnightTraderCatalog.Id]=new(){Standing=1,Disabled=false}},
                Quests=[new(){StartTime=0,StatusTimers=[],QId=new(RogueAlliancePolicy.Quests[0]),Status=QuestStatusEnum.Success}]
            },ScavData=new(){KarmaValue=0.7}}};
            var report=new RogueStandingReport(Guid.NewGuid().ToString("N"),"victim","exUsec");
            var storage=new FriendlyTeammateStorage(files,json,DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>,TestLogger>(),settings);
            var service=new RogueStandingService(Server(),settings,files,json,storage);
            check(await service.Record(session,report)==0.975,"native service persists Rogue kill loss");
            check(await service.Record(session,report)==0.975,"lost-response replay cannot deduct standing twice");
            profiles[session]=json.Deserialize<SptProfile>(files.ReadFile(Path.Combine("user","profiles",session+".json")))!;
            service=new(Server(),settings,files,json,storage);
            check(await service.Record(session,report)==0.975,"standing receipt survives actual native save/reload");
            check(Math.Abs(await service.Record(session,report with{VictimId="goon",Role="bossKnight"})-0.875)<0.000001,"Goon penalty uses 0.1 independently");
            check(profiles[session].CharacterData!.ScavData!.KarmaValue==0.7,"Knight penalties do not alter Scav karma");
            foreach(string role in new[]{"exUsec","bossKnight","followerBigPipe","followerBirdEye"}){
                var member=new BotBase{Id=new MongoId(),Aid=800,Info=new(){Settings=new(){Role=role}}};
                storage.Write(session,"800.json",member);
                double before=profiles[session].CharacterData!.PmcData!.TradersInfo![KnightTraderCatalog.Id].Standing!.Value;
                check(await service.Record(session,report with{VictimId=member.Id.ToString()!,Role=role})==before,"server also rejects stale standing charges for saved "+role);
            }
            string path=Path.Combine("user","profiles",session+".json");File.Delete(path);Directory.CreateDirectory(path);
            bool rejected=false;
            try {await service.Record(session,report with{VictimId="disk-failure"});}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
            check(rejected,"failed native save cannot acknowledge a standing deduction");
            Directory.Delete(path);
            rejected=false;try{await service.Record(session,report with{VictimId="disk-failure"});}catch(IOException){rejected=true;}
            check(rejected,"native cached save hash cannot acknowledge an unsaved retry");
        }
        finally {Environment.CurrentDirectory=original;}
    }
}
