using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Generators.Loot;
using System.Collections.Concurrent;
using Path = System.IO.Path;

internal static class Program
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    public static async Task Main(string[] args)
    {
        string runtime=Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_,name) => {
            string path=Path.Combine(runtime,name.Name+".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        await Run(); await DeliveryChecks(runtime); Console.WriteLine($"PASS: {checks} Scav cargo/native Fence hook checks.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task Run()
    {
        var json=new JsonUtil([new SptJsonConverterRegistrator()]);
        var request=new EndLocalRaidRequestData { ServerId="raid", Results=new() {ExitName="co-op",Result=ExitStatus.SURVIVED,Profile=new PmcData {Info=new() {Side="Usec"}}}};
        var ledger=new FriendlyScavCargoLedger {ServerId="raid",Allegiance=true,Cargo=new() {ServerId="raid",ExitName="co-op",ScavProfileIds=["scav"]},MessageId=new MongoId().ToString(),State="prepared",DeliveredIds=["cargo"]};
        foreach(var status in Enum.GetValues<ExitStatus>())
        foreach(var side in new[]{"Usec","Bear","USEC","Savage"})
        foreach(bool correctRaid in new[]{true,false})
        foreach(bool correctExit in new[]{true,false}) {
            request.Results.Result=status;request.Results.Profile.Info.Side=side;
            request.ServerId=correctRaid ? "raid" : "other";request.Results.ExitName=correctExit ? "co-op" : "normal";
            bool expected=(status is ExitStatus.SURVIVED or ExitStatus.RUNNER) && side!="Savage" && correctRaid && correctExit;
            Check(FriendlyScavCargoService.Matches(ledger,request)==expected,"Exact raid/outcome/PMC/exit match matrix");
        }
        request.ServerId="raid";request.Results.ExitName="co-op";request.Results.Result=ExitStatus.SURVIVED;request.Results.Profile.Info.Side="Usec";
        ledger.Allegiance=false;Check(!FriendlyScavCargoService.Matches(ledger,request),"Guns for Hire never gets Scav returns");ledger.Allegiance=true;
        ledger.Cargo.ScavProfileIds.Clear();Check(!FriendlyScavCargoService.Matches(ledger,request),"Exit name alone cannot establish a recruited partner");ledger.Cargo.ScavProfileIds.Add("scav");
        ledger.Cargo.ExitName=request.Results.ExitName="ordinary";
        Check(FriendlyScavCargoService.Matches(ledger,request),"Ordinary exit with a captured recruited partner qualifies");
        ledger.Cargo.ExitName=request.Results.ExitName="";
        Check(!FriendlyScavCargoService.Matches(ledger,request),"Missing actual extraction point cannot qualify");
        ledger.Cargo.ExitName=request.Results.ExitName="co-op";
        var restored=json.Deserialize<FriendlyScavCargoLedger>(json.Serialize(ledger)!)!;
        Check(restored.MessageId==ledger.MessageId && restored.DeliveredIds.SequenceEqual(["cargo"]),"Durable receipt survives native JSON round trip");
        var hooks=new FriendlyScavCargoHooks(DispatchProxy.Create<ISptLogger<FriendlyScavCargoHooks>,Log>());
        await hooks.OnLoadAsync(default);
        foreach(var method in new[]{AccessTools.Method(typeof(MailSendService),"CreateDialogMessage"),AccessTools.Method(typeof(ItemHelper),"AddCartridgesToAmmoBox")})
            Check(Harmony.GetPatchInfo(method)?.Owners.Contains("xyz.pit.fireteam.server.scav-cargo")==true,"Exact installed native hook patched on .NET 10");
        foreach(var name in new[]{"EndLocalRaidAsync","SendCoopTakenFenceMessage"})
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(LocationLifecycleService),name))?.Owners.Contains("xyz.pit.fireteam.server.scav-cargo")==true,"Native completion/gift context uses scoped hooks");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(LocationLifecycleService),"HandleCoopExtract"))?.Owners.Contains("xyz.pit.fireteam.server.scav-cargo")!=true,"Native standing calculation remains unpatched");
        var constructor=typeof(MailSendService).GetConstructors().Single();
        var mail=(MailSendService)constructor.Invoke(constructor.GetParameters().Select(p=>p.ParameterType==typeof(TimeUtil) ? (object)new TimeUtil() : null).ToArray());
        var details=new SendMessageDetails {Trader=Traders.FENCE,Sender=MessageType.MessageWithItems,DialogType=MessageType.NpcTraderMessage,TemplateId="native-fence-locale"};
        var use=typeof(FriendlyScavCargoHooks).GetMethod("UseMessageId",BindingFlags.Static|BindingFlags.NonPublic)!;
        var create=AccessTools.Method(typeof(MailSendService),"CreateDialogMessage");
        var deliveryId=new MongoId(ledger.MessageId!);
        using((IDisposable)use.Invoke(null,[details,deliveryId])!) {
            var message=(Message)create.Invoke(mail,[new MongoId(Traders.FENCE),details])!;
            Check(message.Id==deliveryId && message.UserId==Traders.FENCE,"Native message receives fixed receipt identity before notification");
            Check(new NotifierHelper(null!).CreateNewMessageNotification(message).Message!.Id==deliveryId,"Native notification uses the same delivery ID");
            var unrelated=(Message)create.Invoke(mail,[new MongoId(Traders.FENCE),new SendMessageDetails()])!;
            Check(unrelated.Id!=deliveryId,"Unrelated mail keeps native random identity");
        }
        Check(((Message)create.Invoke(mail,[new MongoId(Traders.FENCE),details])!).Id!=deliveryId,"Message scope restored after delivery");
        var release=new TaskCompletionSource();
        async Task<bool> Probe(SendMessageDetails ownDetails,MongoId ownId) {
            using((IDisposable)use.Invoke(null,[ownDetails,ownId])!) {
                await release.Task;
                return ((Message)create.Invoke(mail,[new MongoId(Traders.FENCE),ownDetails])!).Id==ownId;
            }
        }
        var first=Probe(details,deliveryId);var second=Probe(new SendMessageDetails(),new MongoId());release.SetResult();
        Check(await first && await second,"Concurrent cargo deliveries keep isolated receipt identities");
        new Harmony("xyz.pit.fireteam.server.scav-cargo").UnpatchSelf();
    }
    static T Logger<T>() => DispatchProxy.Create<T,Log>();
    static object Make(Type type,Dictionary<Type,object> dependencies) => type.GetConstructors().Single().Invoke(
        type.GetConstructors().Single().GetParameters().Select(p=>dependencies.GetValueOrDefault(p.ParameterType) ??
            (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition()==typeof(ISptLogger<>) ?
                typeof(Program).GetMethod(nameof(Logger),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(p.ParameterType).Invoke(null,null):null)).ToArray());
    static readonly List<WsNotificationEvent> Notifications=[];
    static bool Notification(WsNotificationEvent notificationMessage,ref Task __result) {Notifications.Add(notificationMessage);__result=Task.CompletedTask;return false;}
    static string GiftTemplate = "";
    static string CartridgeTemplate = "";
    static int GiftAmmoFillCalls;
    static bool RejectCargo;
    static bool RejectCargoProcessing(SendMessageDetails messageDetails)
    {
        if (RejectCargo && messageDetails.Items?.Any(i=>i.Id.ToString()=="111111111111111111111111")==true)
            throw new IOException("Controlled native cargo processing failure");
        return true;
    }
    static bool GiftAmmoFill(List<Item> __0)
    {
        // HarmonyX can run later prefixes after a prior veto. Model the native
        // body only when the production cargo gate actually permits it.
        if (!(bool)AccessTools.Method(typeof(FriendlyScavCargoHooks),"AmmoPrefix").Invoke(null,null)!) return false;
        GiftAmmoFillCalls++;
        __0.Add(new() {Id=new MongoId(),Template=new MongoId(CartridgeTemplate),
            ParentId=__0[0].Id.ToString(),SlotId="cartridges"});
        return false;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task NativeAsyncCompletion(MongoId sessionId, EndLocalRaidRequestData request,
        LocationLifecycleService native, Task release)
    {
        await release;
        AccessTools.Method(typeof(LocationLifecycleService),"SendCoopTakenFenceMessage").Invoke(native,[sessionId]);
    }
    static bool GiftLoot(ref IEnumerable<List<Item>> __result) {__result=[new List<Item> {new() {Id=new MongoId(),Template=new MongoId(GiftTemplate)}}];return false;}
    static bool SkipLevelUp() => false;
    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task DeliveryChecks(string runtime)
    {
        string initial=Environment.CurrentDirectory;
        string work=Path.GetFullPath(Path.Combine("tests","artifacts","scav-cargo",Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(work,"user","profiles"));
        string settingsPath=Path.Combine(AppContext.BaseDirectory,"user","mods","pitFireTeam-ServerMod","Resources","settings.json");
        byte[]? previousSettings=File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        string languagePath=Path.Combine(Path.GetDirectoryName(settingsPath)!,"lang","en.json");
        byte[]? previousLanguage=File.Exists(languagePath) ? File.ReadAllBytes(languagePath) : null;
        var testHarmony=new Harmony("pitfireteam.tests.scav-cargo");
        Environment.CurrentDirectory=work;
        try
        {
            var json=new JsonUtil([new SptJsonConverterRegistrator()]);var files=new FileUtil();
            var settings=new FriendlyServerSettingsService(Logger<ISptLogger<FriendlyServerSettingsService>>(),null!,null!);
            var language=new FriendlyLanguageService(Logger<ISptLogger<FriendlyLanguageService>>());
            settings.SaveAndApply(new() {GameplayMode="Allegiance"});
            var storage=new FriendlyTeammateStorage(files,json,Logger<ISptLogger<FriendlyTeammateStorage>>(),settings);
            var id=new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
            language.GetLanguageJson(id,"en","{\"scavCargoReturn\":[\"One of my men brought your things back. Here’s your stuff, as promised.\"]}");
            var profile=new SptProfile {ProfileInfo=new(),CharacterData=new() {PmcData=new() {Info=new() {Side="Usec"},Inventory=new() {Items=[]},InsuredItems=[]}},DialogueRecords=[]};
            var profiles=new ConcurrentDictionary<MongoId,SptProfile>();profiles[id]=profile;
            var core=(CoreConfig)RuntimeHelpers.GetUninitializedObject(typeof(CoreConfig));core.Features=(ServerFeatures)RuntimeHelpers.GetUninitializedObject(typeof(ServerFeatures));
            var deps=new Dictionary<Type,object> {[typeof(FileUtil)]=files,[typeof(JsonUtil)]=json,[typeof(CoreConfig)]=core,[typeof(HashUtil)]=new HashUtil(null!),
                [typeof(IEnumerable<SPTarkov.Server.Core.DI.SaveLoadRouter>)]=Array.Empty<SPTarkov.Server.Core.DI.SaveLoadRouter>(),[typeof(FriendlyServerSettingsService)]=settings,[typeof(FriendlyTeammateStorage)]=storage};
            var saves=(SaveServer)Make(typeof(SaveServer),deps);
            typeof(SaveServer).GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(saves,profiles);deps[typeof(SaveServer)]=saves;
            deps[typeof(ProfileHelper)]=Make(typeof(ProfileHelper),deps);deps[typeof(DialogueHelper)]=Make(typeof(DialogueHelper),deps);
            var tpl=new MongoId("cccccccccccccccccccccccc");var boxTpl=new MongoId("dddddddddddddddddddddddd");
            TemplateItem Template(MongoId tid,MongoId parent) {var t=(TemplateItem)RuntimeHelpers.GetUninitializedObject(typeof(TemplateItem));t.Id=tid;t.Parent=parent;t.Type="Item";t.Properties=new();return t;}
            var table=(TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
            typeof(TemplateTable).GetProperty("Items")!.SetValue(table,new Dictionary<MongoId,TemplateItem> {[tpl]=Template(tpl,BaseClasses.ITEM),[boxTpl]=Template(boxTpl,BaseClasses.AMMO_BOX)});
            deps[typeof(TemplateTable)]=table;deps[typeof(ItemBaseClassService)]=Make(typeof(ItemBaseClassService),deps);deps[typeof(ItemHelper)]=Make(typeof(ItemHelper),deps);
            deps[typeof(ICloner)]=new TestCloner(json);deps[typeof(TimeUtil)]=new TimeUtil();deps[typeof(NotifierHelper)]=new NotifierHelper(null!);
            deps[typeof(NotificationSendHelper)]=Make(typeof(NotificationSendHelper),deps);
            testHarmony.Patch(AccessTools.Method(typeof(NotificationSendHelper),"SendMessageAsync"),prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(Notification),BindingFlags.Static|BindingFlags.NonPublic)!));
            var mail=(MailSendService)Make(typeof(MailSendService),deps);deps[typeof(MailSendService)]=mail;
            deps[typeof(FriendlyTeammateService)]=Make(typeof(FriendlyTeammateService),deps);
            var diagnostics=(FollowerInsuranceRaidDiagnostics)Make(typeof(FollowerInsuranceRaidDiagnostics),deps);deps[typeof(FollowerInsuranceRaidDiagnostics)]=diagnostics;
            var returns=(FriendlyPostRaidService)Make(typeof(FriendlyPostRaidService),deps);
            var inRaid=(InRaidConfig)RuntimeHelpers.GetUninitializedObject(typeof(InRaidConfig));inRaid.CoopExtracts=["co-op"];
            var traders=json.Deserialize<TraderConfig>(File.ReadAllText(Path.Combine(runtime,"SPT_Data","configs","trader.json")))!;
            deps[typeof(InRaidConfig)]=inRaid;inRaid.CoopExtractBaseStandingGain=0.25;
            deps[typeof(TraderConfig)]=traders;deps[typeof(RandomUtil)]=Make(typeof(RandomUtil),deps);
            deps[typeof(LootGenerator)]=Make(typeof(LootGenerator),deps);deps[typeof(TraderHelper)]=Make(typeof(TraderHelper),deps);
            GiftTemplate=tpl.ToString();
            testHarmony.Patch(AccessTools.Method(typeof(LootGenerator),"CreateRandomLoot"),prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(GiftLoot),BindingFlags.Static|BindingFlags.NonPublic)!));
            testHarmony.Patch(AccessTools.Method(typeof(TraderHelper),"LevelUp"),prefix:new HarmonyMethod(typeof(Program).GetMethod(nameof(SkipLevelUp),BindingFlags.Static|BindingFlags.NonPublic)!));
            var native=(LocationLifecycleService)Make(typeof(LocationLifecycleService),deps);
            profile.CharacterData!.PmcData!.TradersInfo=new() {[Traders.FENCE]=new() {Standing=0,LoyaltyLevel=1}};
            profile.CharacterData.PmcData.CoopExtractCounts=[];
            profile.CharacterData.ScavData=new() {TradersInfo=new() {[Traders.FENCE]=new() {Standing=0,LoyaltyLevel=1}}};
            var service=new FriendlyScavCargoService(storage,settings,json,files,traders,saves,inRaid,(TraderHelper)deps[typeof(TraderHelper)],mail,returns,diagnostics,language,Logger<ISptLogger<FriendlyScavCargoService>>());
            await new FriendlyScavCargoHooks(Logger<ISptLogger<FriendlyScavCargoHooks>>()).OnLoadAsync(default);
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"raid\"}}");
            var root=new MongoId("111111111111111111111111");var child=new MongoId("222222222222222222222222");var box=new MongoId("333333333333333333333333");
            var unrelated=new MongoId("444444444444444444444444");
            profile.CharacterData!.PmcData!.InsuredItems=[new() {ItemId=root,TId=new MongoId(Traders.PRAPOR)},new() {ItemId=unrelated,TId=new MongoId(Traders.PRAPOR)}];
            var cargo=new FriendlyScavCargoRequest {ServerId="raid",ExitName="co-op",InsuranceReportId=Guid.NewGuid().ToString("N"),ScavProfileIds=["scav"],Items=[new() {Id=root,Template=tpl,ParentId="scav-kit",SlotId="backpack"},new() {Id=child,Template=tpl,ParentId=root.ToString(),SlotId="mod"},new() {Id=box,Template=boxTpl,ParentId="scav-kit"}]};
            service.Queue(id,cargo);service.Queue(id,cargo);
            var end=new EndLocalRaidRequestData {ServerId="raid",Results=new() {Result=ExitStatus.SURVIVED,ExitName="co-op",Profile=profile.CharacterData!.PmcData}};
            service.FinishRaid(id,end);
            var message=profile.DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Single();
            Check(message.Items!.Data!.Count==3,"Native Fence delivery preserves exact cargo count including empty ammo box");
            Check(message.TemplateId==null && message.Text=="One of my men brought your things back. Here’s your stuff, as promised.","Cargo-only return uses localized return wording instead of the co-op reward template");
            Check(message.Items.Data.All(i=>i.Id!=root && i.Id!=child && i.Id!=box),"Native mail generates fresh item IDs");
            var mailedRoot=message.Items.Data[0];Check(message.Items.Data[1].ParentId==mailedRoot.Id.ToString(),"Native clone remaps child hierarchy");
            Check(((SPTarkov.Server.Core.Models.Eft.Ws.WsChatMessageReceived)Notifications.Single()).Message!.Items!.Data!.Count==3,"Cargo notification contains exact returned items");
            Check(profile.CharacterData.PmcData.InsuredItems.Single().ItemId==unrelated,"Returned source policy removed while unrelated player insurance remains");
            service.FinishRaid(id,end);service.FinishRaid(id,end);
            Check(profile.DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==1 && Notifications.Count==1,"Repeated end callbacks cannot redeliver cargo");
            var receipt=storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!;
            receipt.State="prepared";storage.WriteModeDocuments(id,true,new Dictionary<string,string> {["scav-cargo.json"]=json.Serialize(receipt)!});
            service.FinishRaid(id,end);Check(profile.DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==1,"Profile mail resolves interrupted receipt without duplicate return");
            profiles[id]=json.Deserialize<SptProfile>(File.ReadAllText(Path.Combine(work,"user","profiles",id+".json")))!;
            var restartStorage=new FriendlyTeammateStorage(files,json,Logger<ISptLogger<FriendlyTeammateStorage>>(),settings);
            var restarted=new FriendlyScavCargoService(restartStorage,settings,json,files,traders,saves,inRaid,(TraderHelper)deps[typeof(TraderHelper)],mail,returns,diagnostics,language,Logger<ISptLogger<FriendlyScavCargoService>>());
            restarted.FinishRaid(id,end);Check(profiles[id].DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==1,"Restart retains delivery receipt");
            Check(profiles[id].CharacterData!.PmcData!.CoopExtractCounts!.Count==0 && profiles[id].CharacterData!.PmcData!.TradersInfo![Traders.FENCE].Standing==0,"Cargo restart does not manufacture native co-op standing");
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"paid-raid\"}}");cargo.ServerId="paid-raid";cargo.InsuranceReportId=Guid.NewGuid().ToString("N");service.Queue(id,cargo);end.ServerId="paid-raid";
            // No native gift invocation: SVM can disable it. Post-native completion still delivers.
            service.FinishRaid(id,end);Check(profiles[id].DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==2,"Paid co-op return works without stock Fence gift");
            Check(profiles[id].CharacterData!.PmcData!.CoopExtractCounts!.Count==0 && profiles[id].CharacterData!.PmcData!.TradersInfo![Traders.FENCE].Standing==0,"Paid co-op with stock rewards disabled receives no supplemental standing");
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"empty-raid\"}}");
            service.Queue(id,new() {ServerId="empty-raid",ExitName="co-op",InsuranceReportId=Guid.NewGuid().ToString("N"),ScavProfileIds=["scav"]});end.ServerId="empty-raid";
            service.FinishRaid(id,end);
            Check(profiles[id].DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==2,"Empty return creates no free reward");
            Check(profiles[id].CharacterData!.PmcData!.CoopExtractCounts!.Count==0 && profiles[id].CharacterData!.PmcData!.TradersInfo![Traders.FENCE].Standing==0,"Empty real co-op cargo still leaves standing entirely to native handling");
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"dead-raid\"}}");cargo.ServerId="dead-raid";cargo.InsuranceReportId=Guid.NewGuid().ToString("N");service.Queue(id,cargo);end.ServerId="dead-raid";end.Results.Result=ExitStatus.KILLED;service.FinishRaid(id,end);
            Check(profiles[id].DialogueRecords![new MongoId(Traders.FENCE)].Messages!.Count==2 && storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!.State=="rejected","Actual failed extraction invalidates pending cargo");
            Check(profiles[id].CharacterData!.PmcData!.CoopExtractCounts!.Count==0 && profiles[id].CharacterData!.PmcData!.TradersInfo![Traders.FENCE].Standing==0,"Failed extraction grants no standing");
            profile=profiles[id];var pmc=profile.CharacterData!.PmcData!;var fence=new MongoId(Traders.FENCE);
            pmc.TradersInfo=new() {[fence]=new() {Standing=0,LoyaltyLevel=1}};pmc.CoopExtractCounts=[];
            profile.CharacterData.ScavData=new() {TradersInfo=new() {[fence]=new() {Standing=0,LoyaltyLevel=1}}};
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"ordinary-raid\"}}");
            cargo.ServerId=end.ServerId="ordinary-raid";cargo.ExitName=end.Results.ExitName="ordinary";
            cargo.ScavProfileIds=["scav","scav-two"];
            cargo.InsuranceReportId=Guid.NewGuid().ToString("N");end.Results.Result=ExitStatus.SURVIVED;
            // The request is a separate client snapshot, not the live server profile.
            end.Results.Profile=json.Deserialize<PmcData>(json.Serialize(pmc)!)!;
            service.Queue(id,cargo);service.FinishRaid(id,end);
            Check(profile.DialogueRecords![fence].Messages!.Count==3,"Ordinary exit returns gifted cargo");
            Check(pmc.TradersInfo[fence].Standing==0.025 && pmc.CoopExtractCounts.Count==0,"Two Scav partners grant flat 0.025 standing without consuming native co-op counts");
            Check(profile.CharacterData.ScavData.TradersInfo![fence].Standing==0.025,"Simulated reward synchronizes PMC and player-Scav Fence standing");
            service.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==0.025 && pmc.CoopExtractCounts.Count==0,"Repeated ordinary completion cannot award standing twice");
            void NativeFenceImport()
            {
                // Execute the installed native import called by HandlePostRaidPmc.
                AccessTools.Method(typeof(LocationLifecycleService),"ApplyTraderStandingAdjustments")
                    .Invoke(native,[pmc.TradersInfo,end.Results.Profile.TradersInfo]);
                profile.CharacterData!.ScavData!.TradersInfo![fence]=pmc.TradersInfo![fence];
            }
            for(int repeat=0;repeat<2;repeat++)
            {
                NativeFenceImport();
                Check(pmc.TradersInfo[fence].Standing==0,"Native raid-end replay restores the original client Fence standing");
                service.FinishRaid(id,end);
                Check(pmc.TradersInfo[fence].Standing==0.025 && profile.CharacterData.ScavData.TradersInfo![fence].Standing==0.025,
                    "Completed cargo receipt restores exactly one simulated reward after native replay");
                Check(profile.DialogueRecords![fence].Messages!.Count==3 && pmc.CoopExtractCounts.Count==0,
                    "Native replay restoration neither redelivers cargo nor advances co-op counters");
            }
            receipt=storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!;
            receipt.State="prepared";storage.WriteModeDocuments(id,true,new Dictionary<string,string> {["scav-cargo.json"]=json.Serialize(receipt)!});
            profiles[id]=json.Deserialize<SptProfile>(File.ReadAllText(Path.Combine(work,"user","profiles",id+".json")))!;
            profile=profiles[id];pmc=profile.CharacterData!.PmcData!;
            NativeFenceImport();
            restarted.FinishRaid(id,end);profile=profiles[id];pmc=profile.CharacterData!.PmcData!;
            Check(pmc.TradersInfo![fence].Standing==0.025 && profile.SptData!.Migrations!.ContainsKey("pitFireTeam/scav-coop/ordinary-raid"),"Profile receipt survives restart between standing save and cargo completion");
            Check(profile.SptData!.Migrations!["pitFireTeam/scav-coop/ordinary-raid/standing"]==25000,
                "Authoritative standing receipt survives restart before cargo acknowledgement");
            Check(end.Results.Profile.TradersInfo![fence].Standing==0,"Restoring the reward does not mutate the client's request snapshot");
            profile.SptData.Migrations.Remove("pitFireTeam/scav-coop/ordinary-raid/standing");
            restarted.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==0.025,"Legacy completed flag-only receipt cannot award a second reward");
            Check(!profile.SptData.Migrations.ContainsKey("pitFireTeam/scav-coop/ordinary-raid/standing"),"Legacy receipt does not invent an authoritative standing result");
            profile.SptData.Migrations["pitFireTeam/scav-coop/ordinary-raid/standing"]=50000;
            NativeFenceImport();restarted.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==0.05,"Existing committed 0.05 reward survives upgrade to the smaller future gain");
            profile.SptData.Migrations["pitFireTeam/scav-coop/ordinary-raid/standing"]=25000;
            restarted.FinishRaid(id,end);
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"ordinary-empty-raid\"}}");
            service.Queue(id,new() {ServerId="ordinary-empty-raid",ExitName="ordinary",InsuranceReportId=Guid.NewGuid().ToString("N"),ScavProfileIds=["scav"]});
            end.ServerId="ordinary-empty-raid";end.Results.Profile=pmc;service.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==0.05 && pmc.CoopExtractCounts!.Count==0 && profile.DialogueRecords![fence].Messages!.Count==3,"Repeat exit use grants another flat 0.025 with empty cargo and no gift");
            pmc.TradersInfo[fence].Standing=0;

            // Exercise actual native co-op reward/standing handlers. Only random loot
            // generation and unrelated trader-level recalculation are fixture stand-ins.
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"native-coop-raid\"}}");
            cargo.ServerId=end.ServerId="native-coop-raid";cargo.ExitName=end.Results.ExitName="co-op";cargo.InsuranceReportId=Guid.NewGuid().ToString("N");
            service.Queue(id,cargo);
            AccessTools.Method(typeof(LocationLifecycleService),"HandleCoopExtract").Invoke(native,[id,pmc,"co-op"]);
            using((IDisposable)typeof(FriendlyScavCargoHooks).GetMethod("UseNativeRaid",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[id,end])!)
                AccessTools.Method(typeof(LocationLifecycleService),"SendCoopTakenFenceMessage").Invoke(native,[id]);
            service.FinishRaid(id,end);
            Check(profile.DialogueRecords![fence].Messages!.Count==4,"Real co-op sends one combined native Fence gift and cargo return");
            var combined=profile.DialogueRecords[fence].Messages!.Last();
            Check(traders.Fence.CoopExtractGift.MessageLocaleIds.Contains(combined.TemplateId!) && string.IsNullOrEmpty(combined.Text),"Combined co-op package preserves the original native message wording");
            Check(combined.Items!.Data!.Count==4 && combined.Items.Data.Count(i=>i.Template==boxTpl)==1,"Combined package retains the native reward and complete cargo, including an empty ammo box");
            Check(((WsChatMessageReceived)Notifications.Last()).Message!.Items!.Data!.Count==4,"The original notification contains gift and cargo together");
            Check(pmc.TradersInfo[fence].Standing==0.25 && profile.CharacterData!.ScavData!.TradersInfo![fence].Standing==0.25,"Real co-op keeps native Fence standing gain and PMC/Scav synchronization");
            Check(pmc.CoopExtractCounts!["co-op"]==1,"Native co-op count changes exactly once");
            service.FinishRaid(id,end);
            Check(profile.DialogueRecords![fence].Messages!.Count==4 && pmc.TradersInfo[fence].Standing==0.25,"Cargo replay cannot duplicate a return or add more standing");
            using((IDisposable)typeof(FriendlyScavCargoHooks).GetMethod("UseNativeRaid",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[id,end])!)
                AccessTools.Method(typeof(LocationLifecycleService),"SendCoopTakenFenceMessage").Invoke(native,[id]);
            Check(profile.DialogueRecords[fence].Messages!.Count==4,"Repeated native reward delivery cannot duplicate the combined receipt");
            AccessTools.Method(typeof(LocationLifecycleService),"HandleCoopExtract").Invoke(native,[id,pmc,"co-op"]);
            Check(pmc.TradersInfo[fence].Standing==0.38,"Subsequent real co-op keeps native diminishing standing rewards");
            foreach(bool survived in new[]{false,true}) {
                string raid=survived?"ordinary-cap":"ordinary-failed";
                pmc.TradersInfo[fence].Standing=traders.Fence.PlayerRepMax-0.01;
                service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\""+raid+"\"}}");
                service.Queue(id,new() {ServerId=raid,ExitName="ordinary",InsuranceReportId=Guid.NewGuid().ToString("N"),ScavProfileIds=["scav"]});
                end.ServerId=raid;end.Results.ExitName="ordinary";end.Results.Result=survived?ExitStatus.SURVIVED:ExitStatus.KILLED;
                end.Results.Profile=json.Deserialize<PmcData>(json.Serialize(pmc)!)!;
                service.FinishRaid(id,end);
                Check(pmc.TradersInfo[fence].Standing==(survived?traders.Fence.PlayerRepMax:traders.Fence.PlayerRepMax-0.01),"Simulated reward respects outcome and configured Fence cap");
                Check(pmc.CoopExtractCounts!["co-op"]==2 && pmc.CoopExtractCounts.Count==1,"Simulated extraction never changes native co-op use history");
                if(survived)
                {
                    NativeFenceImport();service.FinishRaid(id,end);
                    Check(pmc.TradersInfo[fence].Standing==traders.Fence.PlayerRepMax,"Native replay restores the capped result instead of granting a full additional reward");
                    Check(profile.CharacterData!.ScavData!.TradersInfo![fence].Standing==traders.Fence.PlayerRepMax,"Capped replay also restores the synchronized Scav profile");
                }
            }
            pmc.TradersInfo[fence].Standing=-1;
            service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\"ordinary-negative\"}}");
            service.Queue(id,new() {ServerId="ordinary-negative",ExitName="ordinary",InsuranceReportId=Guid.NewGuid().ToString("N"),ScavProfileIds=["scav"]});
            end.ServerId="ordinary-negative";end.Results.Profile=json.Deserialize<PmcData>(json.Serialize(pmc)!)!;
            service.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==-0.975,"Negative Fence standing receives the exact simulated gain");
            NativeFenceImport();service.FinishRaid(id,end);
            Check(pmc.TradersInfo[fence].Standing==-0.975,"Native replay restores a negative authoritative standing result");
            Check(profile.SptData!.Migrations!["pitFireTeam/scav-coop/ordinary-negative/standing"]==-975000,"Signed receipt retains six-decimal standing precision");
            // Exercise the production async-entry scope on a controlled native completion
            // that awaits before sending the real installed Fence reward/mail/notification.
            testHarmony.Patch(AccessTools.Method(typeof(Program),nameof(NativeAsyncCompletion)),
                prefix:new HarmonyMethod(AccessTools.Method(typeof(FriendlyScavCargoHooks),"RaidPrefix")),
                finalizer:new HarmonyMethod(AccessTools.Method(typeof(FriendlyScavCargoHooks),"ScopeFinalizer")));
            void QueueRaid(string raid,List<Item>? contents=null)
            {
                service.BeginRaid(id,"{\"err\":0,\"data\":{\"serverId\":\""+raid+"\"}}");
                service.Queue(id,new() {ServerId=raid,ExitName="co-op",InsuranceReportId=Guid.NewGuid().ToString("N"),
                    ScavProfileIds=["scav"],Items=contents ?? cargo.Items});
                end.ServerId=raid;end.Results.ExitName="co-op";end.Results.Result=ExitStatus.SURVIVED;
            }
            void NativeGiftScoped()
            {
                using((IDisposable)typeof(FriendlyScavCargoHooks).GetMethod("UseNativeRaid",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[id,end])!)
                    AccessTools.Method(typeof(LocationLifecycleService),"SendCoopTakenFenceMessage").Invoke(native,[id]);
            }
            int beforeMessages=profile.DialogueRecords[fence].Messages!.Count;
            QueueRaid("async-merge");var release=new TaskCompletionSource();
            var completion=NativeAsyncCompletion(id,end,native,release.Task);
            Check(profile.DialogueRecords[fence].Messages!.Count==beforeMessages,"Awaiting native completion sends no early cargo message");
            // The caller's context is already restored, so an unrelated native gift is untouched.
            AccessTools.Method(typeof(LocationLifecycleService),"SendCoopTakenFenceMessage").Invoke(native,[id]);
            Check(profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==1,"Async native raid scope does not leak into caller/unassociated Fence mail");
            release.SetResult();await completion;service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==beforeMessages+2 && profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==4,
                "Native async continuation retains its admitted cargo context after the caller restores it");

            CartridgeTemplate=tpl.ToString();GiftTemplate=boxTpl.ToString();
            testHarmony.Patch(AccessTools.Method(typeof(ItemHelper),"AddCartridgesToAmmoBox"),
                prefix:new HarmonyMethod(AccessTools.Method(typeof(Program),nameof(GiftAmmoFill))) {priority=Priority.Last});
            QueueRaid("gift-ammo-merge");GiftAmmoFillCalls=0;NativeGiftScoped();service.FinishRaid(id,end);
            var ammoMerged=profile.DialogueRecords[fence].Messages!.Last();
            Check(GiftAmmoFillCalls==1 && ammoMerged.Items!.Data!.Count==5,"Only the native reward ammo box is auto-filled; identical cargo box stays empty");
            Check(ammoMerged.Items!.Data!.Count(i=>i.Template==boxTpl)==2 && ammoMerged.Items!.Data!.Count(i=>i.SlotId=="cartridges")==1,
                "Reward ammo and empty cargo coexist in one package without changing either tree");
            Check(ammoMerged.Items!.Data!.Count(i=>i.ParentId==ammoMerged.Items.Stash.ToString())==3,
                "Gift and both cargo roots share the native message stash");
            Check(cargo.Items.Count==3 && cargo.Items[1].ParentId==root.ToString(),"Combining a reward never mutates the client cargo snapshot");
            var mergedReceipt=storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!;
            Check(mergedReceipt.NativeCoopGift && mergedReceipt.ExpectedMailItemCount==5 && mergedReceipt.DeliveredIds.Count==3,
                "Durable receipt distinguishes combined mail count from returned source IDs");
            mergedReceipt.State="prepared";storage.WriteModeDocuments(id,true,new Dictionary<string,string> {["scav-cargo.json"]=json.Serialize(mergedReceipt)!});
            int mergedMessages=profile.DialogueRecords[fence].Messages!.Count;
            restarted.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==mergedMessages,"Restart/interrupted cargo acknowledgement reconciles one combined package");
            ammoMerged.RewardCollected=true;ammoMerged.Items!.Data!.Clear();NativeGiftScoped();service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==mergedMessages,"Claimed combined package cannot be regenerated by native gift or cargo replay");
            profile.DialogueRecords[fence].Messages!.Remove(ammoMerged);NativeGiftScoped();service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==mergedMessages-1,"A deleted claimed combined message cannot regenerate from its completed receipt");
            mergedMessages--;

            QueueRaid("empty-merge",[]);NativeGiftScoped();service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==mergedMessages+1 && profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==2,
                "Empty cargo leaves the original native reward and its ammo filling intact");
            QueueRaid("failed-merge");end.Results.Result=ExitStatus.KILLED;NativeGiftScoped();service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==2 &&
                storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!.State=="rejected",
                "Failed raid cannot add cargo to a native reward or generate a return");
            QueueRaid("wrong-raid-merge");end.ServerId="other-raid";NativeGiftScoped();service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==2,"Unmatched native raid cannot borrow another raid's cargo");
            testHarmony.Patch(AccessTools.Method(typeof(MailSendService),"ProcessItemsBeforeAddingToMail"),
                prefix:new HarmonyMethod(AccessTools.Method(typeof(Program),nameof(RejectCargoProcessing))));
            QueueRaid("merge-failure");RejectCargo=true;int failureMessages=profile.DialogueRecords[fence].Messages!.Count;
            NativeGiftScoped();
            Check(profile.DialogueRecords[fence].Messages!.Count==failureMessages+1 && profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==2,
                "Cargo merge failure does not block or alter the native Fence reward");
            var failedReceipt=storage.ReadModeDocument<FriendlyScavCargoLedger>(id,"scav-cargo.json",true)!;
            Check(profile.DialogueRecords[fence].Messages!.Last().Id.ToString()!=failedReceipt.MessageId && failedReceipt.State=="prepared",
                "Failed merge cannot masquerade as an acknowledged combined return");
            RejectCargo=false;service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==failureMessages+2 && profile.DialogueRecords[fence].Messages!.Last().Items!.Data!.Count==3,
                "End callback recovers the exact cargo separately after a native merge failure");
            Check(profile.DialogueRecords[fence].Messages!.Last().Text=="One of my men brought your things back. Here’s your stuff, as promised.","Recovered standalone cargo keeps return wording");
            service.FinishRaid(id,end);
            Check(profile.DialogueRecords[fence].Messages!.Count==failureMessages+2,"Recovered merge failure does not duplicate cargo on replay");
            Console.WriteLine("Temporary native cargo profiles: "+work);
        }
        finally {
            testHarmony.UnpatchSelf();new Harmony("xyz.pit.fireteam.server.scav-cargo").UnpatchSelf();Environment.CurrentDirectory=initial;
            if(previousSettings!=null)File.WriteAllBytes(settingsPath,previousSettings);else if(File.Exists(settingsPath))File.Delete(settingsPath);
            if(previousLanguage!=null)File.WriteAllBytes(languagePath,previousLanguage);else if(File.Exists(languagePath))File.Delete(languagePath);
        }
    }
}
public class TestCloner(JsonUtil json) : ICloner {public T? Clone<T>(T? obj)=>obj==null ? default : json.Deserialize<T>(json.Serialize(obj)!);}
public class Log : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method,object?[]? args) {
        if(method!.Name=="Error")throw new Exception(args?[0]?.ToString());
        return method.ReturnType==typeof(bool) ? false : null;
    }
}
