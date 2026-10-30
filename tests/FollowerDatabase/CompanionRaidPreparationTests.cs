using System.Collections;
using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

internal static class CompanionRaidPreparationTests
{
    internal static void Run(FriendlyTeammateService service, MongoId session, BotBase saved, BotBase kit, BotBase replacement,
        JsonUtil json, Action<bool,string> check)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var cache = (IDictionary)typeof(FriendlyTeammateService).GetField("preparedCompanionRaids", flags)!.GetValue(service)!;
        var type = typeof(FriendlyTeammateService).GetNestedType("PreparedCompanionRaid", BindingFlags.NonPublic)!;
        var spawn = typeof(FriendlyTeammateService).GetMethod("PreparedCompanionForSpawn", flags)!;
        string token = Guid.NewGuid().ToString("N"), aid = saved.Aid!.Value.ToString();
        var configuration = new GetRaidConfigurationRequestData { Location = "bigmap", Side = SideType.Pmc };
        var preparation = Activator.CreateInstance(type, token, FriendlyModeRequestGate.Generation, json.Serialize(configuration))!;
        var kits = (IDictionary)type.GetProperty("Kits")!.GetValue(preparation)!;
        var revisions = (IDictionary)type.GetProperty("JoinRevisions")!.GetValue(preparation)!;
        string join = Guid.NewGuid().ToString("N");
        kits.Add(aid, new FastCloner().Clone(kit));
        revisions.Add(aid, join);
        cache.Add(session.ToString(), preparation);
        void Reject(Action action,string message)
        {
            try {action();} catch(Exception ex) when (ex is InvalidOperationException || ex.InnerException is InvalidOperationException) { check(true,message);return; }
            check(false,message);
        }
        try
        {
            var request = new FriendlyCompanionRaidPreparationRequest { Token=token, Members=[aid,aid], MemberRevisions=new(){{aid,join}}, RaidConfiguration=configuration };
            var first = service.PrepareCompanionRaid(session,request);
            var again = service.PrepareCompanionRaid(session,request);
            check(first.Token==token && first.Members.SequenceEqual(new[]{aid}) && again.Members.SequenceEqual(first.Members),
                "repeated preparation and duplicate selected members reuse the cached kit without invoking generation");
            var current = new FastCloner().Clone(saved)!;
            current.Skills = new() { Common=[new(){Id=SkillTypes.Vitality,Progress=123}], Mastering=[],Points=0 };
            var result = (BotBase)spawn.Invoke(service,[session,current,null,token])!;
            check(json.Serialize(result.Inventory)==json.Serialize(kit.Inventory),"actual spawn adopts exact prepared equipment IDs, supplies, state and layout");
            check(result.Id==current.Id && result.Aid==current.Aid && json.Serialize(result.Skills)==json.Serialize(current.Skills),
                "prepared inventory does not replace saved identity or current earned skills");
            check(json.Serialize(result.Health)==json.Serialize(current.Health),"prepared inventory does not restore old cached health");
            result.Inventory!.Items!.RemoveAt(1);
            var retry = (BotBase)spawn.Invoke(service,[session,current,null,token])!;
            check(json.Serialize(retry.Inventory)==json.Serialize(kit.Inventory),"spawn mutations cannot alter the reserved kit or a retry");
            Reject(()=>spawn.Invoke(service,[new MongoId(),current,null,token]),"another player cannot consume a reserved kit");
            Reject(()=>spawn.Invoke(service,[session,current,null,Guid.NewGuid().ToString("N")]),"a stale attempt cannot fall back to random generation");
            var wrong = new FastCloner().Clone(current)!;wrong.Id=new MongoId();
            Reject(()=>spawn.Invoke(service,[session,wrong,null,token]),"a changed roster identity cannot consume old equipment");
            wrong=new FastCloner().Clone(current)!;wrong.Info!.Settings!.Role="followerBigPipe";
            Reject(()=>spawn.Invoke(service,[session,wrong,null,token]),"a changed role cannot consume Knight equipment");
            Reject(()=>service.PrepareCompanionRaid(session,request with {RaidConfiguration=configuration with {Location="woods"}}),
                "one attempt cannot switch map context while retaining old equipment");
            Reject(()=>service.PrepareCompanionRaid(session,request with {Token="invalid"}),"malformed attempt identities are rejected");
            const string other = "456";
            string otherJoin = Guid.NewGuid().ToString("N");
            kits.Add(other,new FastCloner().Clone(kit));revisions.Add(other,otherJoin);
            string untouched = json.Serialize(kits[other])!;
            var without = request with {Members=[other],MemberRevisions=new(){{other,otherJoin}}};
            service.PrepareCompanionRaid(session,without);
            check(!kits.Contains(aid) && !revisions.Contains(aid),"removing a member releases only that member's reservation");
            check(json.Serialize(kits[other])==untouched,"another member's prepared equipment is preserved on kick");
            Reject(()=>service.PrepareCompanionRaid(session,request),"a delayed request from a removed join cannot restore its old kit");
            string rejoin = Guid.NewGuid().ToString("N");
            var rejoined = request with {Members=[aid,other],MemberRevisions=new(){{aid,rejoin},{other,otherJoin}}};
            // Generation dependencies are deliberately absent in this storage fixture.
            // A fresh join must reach generation, and failure must leave no old kit.
            bool attempted=false;
            try {service.PrepareCompanionRaid(session,rejoined);} catch(NullReferenceException){attempted=true;}
            check(attempted && !kits.Contains(aid),"a new join requests generation rather than restoring removed gear");
            kits[aid]=new FastCloner().Clone(replacement);revisions[aid]=rejoin;
            service.PrepareCompanionRaid(session,rejoined);
            retry=(BotBase)spawn.Invoke(service,[session,current,null,token])!;
            check(retry.Inventory!.Equipment==replacement.Inventory!.Equipment && retry.Inventory.Equipment!=kit.Inventory!.Equipment,
                "spawn uses the rejoined member's replacement kit");
            check(json.Serialize(kits[other])==untouched,"rejoining does not reroll another member");
            var changedJoin=rejoined with {MemberRevisions=new(){{aid,Guid.NewGuid().ToString("N")},{other,otherJoin}}};
            attempted=false;
            try {service.PrepareCompanionRaid(session,changedJoin);} catch(NullReferenceException){attempted=true;}
            check(attempted && !kits.Contains(aid),"lost-release rejoin failure cannot leave stale equipment usable");
            kits[aid]=new FastCloner().Clone(replacement);revisions[aid]=changedJoin.MemberRevisions[aid];
            FriendlyModeRequestGate.ModeChanged();
            Reject(()=>spawn.Invoke(service,[session,current,null,token]),"a mode transition invalidates reserved equipment");
            service.ClearCompanionRaidPreparation(session);
            check(!cache.Contains(session.ToString()),"raid completion clears the preparation");
            Reject(()=>spawn.Invoke(service,[session,current,null,token]),"a cleared attempt cannot silently reroll");
        }
        finally {service.ClearCompanionRaidPreparation(session);}
    }
}
