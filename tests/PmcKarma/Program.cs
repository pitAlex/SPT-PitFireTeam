using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using pitTeam.Shared;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    public static async Task<int> Main(string[] args)
    {
        string runtime=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_,name) => {
            string path=Path.Combine(runtime,name.Name+".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        await Run(); Console.WriteLine($"PASS: {checks} PMC karma native backend checks."); return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Run()
    {
        string previous=Environment.CurrentDirectory;
        string work=Path.GetFullPath(Path.Combine("tests","artifacts","pmc-karma",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(work,"user","profiles"));
        Environment.CurrentDirectory=work;
        try
        {
            var json=new JsonUtil([new SptJsonConverterRegistrator()]); var file=new FileUtil();
            var session=new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa"); var other=new MongoId("bbbbbbbbbbbbbbbbbbbbbbbb");
            var profiles=new ConcurrentDictionary<MongoId,SptProfile>();
            SaveServer NewServer() {
                var config=(CoreConfig)RuntimeHelpers.GetUninitializedObject(typeof(CoreConfig));
                config.Features=(ServerFeatures)RuntimeHelpers.GetUninitializedObject(typeof(ServerFeatures));
                var constructor=typeof(SaveServer).GetConstructors().Single();
                var server=(SaveServer)constructor.Invoke(constructor.GetParameters().Select(p => p.Name switch {
                    "fileUtil" => (object)file, "jsonUtil" => json, "hashUtil" => new HashUtil(null!),
                    "coreConfig" => config, "saveLoadRouters" => Array.Empty<SaveLoadRouter>(), _ => null
                }).ToArray());
                typeof(SaveServer).GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(server,profiles);
                return server;
            }
            void Profile(double karma) { profiles[session]=new SptProfile { ProfileInfo=new Info(),
                CharacterData=new Characters { PmcData=new PmcData {KarmaValue=karma}, ScavData=new PmcData {KarmaValue=0.7} } }; }
            var server=NewServer(); var service=new PmcKarmaService(server,file,json);
            Profile(0.2);
            var request=json.Deserialize<PmcKarmaRequest>("{\"RaidId\":\"one\",\"Kind\":\"kill\",\"VictimProfileId\":\"friendly\"}")!;
            Check(request.RaidId=="one" && request.Kind=="kill","Actual SPT JSON binds the shared DTO wire names");
            var result=await service.Record(session,request);
            Check(result.KarmaValue==0.175 && result.Sound==-1,"Friendly kill subtracts 0.025 and emits negative sound");
            result=await service.Record(session,request);
            Check(result.KarmaValue==0.175 && result.Sound==-1,"Lost-response retry preserves original sound without another subtraction");
            Check(profiles[session].CharacterData!.ScavData!.KarmaValue==0.7,"Scav profile karma remains untouched");
            profiles[session]=json.Deserialize<SptProfile>(file.ReadFile(Path.Combine("user","profiles",session+".json")))!;
            service=new PmcKarmaService(NewServer(),file,json);
            Check((await service.Record(session,request)).KarmaValue==0.175,"Native saved receipt survives backend restart");
            profiles[other]=new SptProfile {ProfileInfo=new Info(),CharacterData=new Characters {PmcData=new PmcData {KarmaValue=0.5}}};
            Check((await service.Record(other,request)).KarmaValue==0.475,"Receipts and karma are per user");
            Profile(0.01); request.RaidId="low";
            result=await service.Record(session,request);
            Check(result.KarmaValue==0 && result.Sound==-1,"Partial remaining karma reaches zero with sound");
            request.VictimProfileId="next";
            result=await service.Record(session,request);
            Check(result.KarmaValue==0 && result.Sound==0,"Zero karma never emits negative sound");
            Profile(0.2);
            var end=new PmcKarmaReport {RaidId="extract",Kind="end",Allegiance=true,HadFriendlies=true,RecruitedAny=true,
                ExtractedRecruitIds=["one","two","two"]};
            result=await service.Record(session,end);
            Check(result.KarmaValue==0.24 && result.Sound==1,"Two distinct extracted recruits add 0.04 without passive stacking");
            Check((await service.Record(session,end)).KarmaValue==0.24,"Extraction replay cannot double reward");
            Profile(0.99); end.RaidId="cap";
            result=await service.Record(session,end);
            Check(result.KarmaValue==1 && result.Sound==1,"Extraction clamps at one and sounds only a real increase");
            end.RaidId="already-max";
            result=await service.Record(session,end);
            Check(result.KarmaValue==1 && result.Sound==0,"Maximum karma never emits positive sound");
            foreach(bool allegiance in new[]{false,true})
            foreach(bool present in new[]{false,true})
            foreach(bool kill in new[]{false,true})
            foreach(bool recruited in new[]{false,true}) {
                Profile(0.2);
                result=await service.Record(session,new PmcKarmaReport {RaidId=Guid.NewGuid().ToString("N"),Kind="end",
                    Allegiance=allegiance,HadFriendlies=present,KilledFriendly=kill,RecruitedAny=recruited});
                Check(result.KarmaValue==(allegiance && present && !kill && !recruited ? 0.205 : 0.2) && result.Sound==0,
                    "Passive recovery exact eligibility matrix; always silent");
            }
            Check(PmcKarmaPolicy.Apply(0.001,-0.025)==0 && PmcKarmaPolicy.Apply(0.999,0.02)==1,"Shared clamp arithmetic");
            Profile(0.2);
            string blocked=Path.Combine("user","profiles",session+".json");
            File.Delete(blocked); Directory.CreateDirectory(blocked);
            request.RaidId="write-failure";
            bool rejected=false;
            try { await service.Record(session,request); } catch(IOException) { rejected=true; }
            catch(UnauthorizedAccessException) { rejected=true; }
            Check(rejected,"Failed native disk write cannot acknowledge karma or sound");
            Directory.Delete(blocked);
            rejected=false;
            try { await service.Record(session,request); } catch(IOException) { rejected=true; }
            Check(rejected,"Cached attempted save hash cannot turn an unsaved retry into success");
            Console.WriteLine("Temporary native profile artifacts: "+work);
        }
        finally { Environment.CurrentDirectory=previous; }
    }
}
