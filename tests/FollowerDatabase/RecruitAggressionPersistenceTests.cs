using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using System.Text.Json;

internal static class RecruitAggressionPersistenceTests
{
    internal static void Run(FriendlyTeammateStorage storage,MongoId session,Action<bool,string> check)
    {
        foreach(float aggression in new[]{0f,20f,30f,40f,50f,70f,100f})
        {
            var payload=JsonSerializer.Serialize(new FriendlyRecruitPickupCandidate {Aggression=aggression});
            var candidate=JsonSerializer.Deserialize<FriendlyRecruitPickupCandidate>(payload)!;
            check(candidate.GetSavedAggression()==aggression,"Client JSON preserves mapped recruit aggression including zero");
            var invitation=new FriendlyRecruitRequestEntry {ProfileId="native-test",Aggression=candidate.GetSavedAggression()};
            storage.Write(session,"personality-invitations.json",new[]{invitation});
            var saved=storage.Read<List<FriendlyRecruitRequestEntry>>(session,"personality-invitations.json")!.Single();
            storage.Write(session,"987-settings.json",new FriendlyTeammateSettings {Aggression=saved.GetSavedAggression()});
            check(storage.Read<FriendlyTeammateSettings>(session,"987-settings.json")!.Aggression==aggression,"Invite and roster database retain captured aggression");
        }
        var legacy=JsonSerializer.Deserialize<FriendlyRecruitRequestEntry>("{\"ProfileId\":\"legacy\"}")!;
        check(legacy.GetSavedAggression()==50f,"Missing legacy aggression uses original default");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            check(new FriendlyRecruitPickupCandidate {Aggression=invalid}.GetSavedAggression()==50f,"Invalid aggression uses safe legacy default");
        check(new FriendlyRecruitPickupCandidate {Aggression=-5f}.GetSavedAggression()==0f,"Below-range aggression clamps");
        check(new FriendlyRecruitPickupCandidate {Aggression=200f}.GetSavedAggression()==100f,"Above-range aggression clamps");
        foreach(var side in new[]{"Bear","Usec"})
        {
            string leader=side=="Bear" ? "Usec" : "Bear";
            var candidate=JsonSerializer.Deserialize<FriendlyRecruitPickupCandidate>(JsonSerializer.Serialize(new FriendlyRecruitPickupCandidate {Side=side.ToLowerInvariant()}))!;
            check(candidate.GetSavedSide(leader)==side,"Opposite-faction recruit side survives JSON and is normalized");
            storage.Write(session,"faction-invitations.json",new[]{new FriendlyRecruitRequestEntry {Side=candidate.GetSavedSide(leader)!}});
            var saved=storage.Read<List<FriendlyRecruitRequestEntry>>(session,"faction-invitations.json")!.Single();
            check(saved.GetSavedSide(leader)==side,"Database invitation keeps faction independently of the leader");
        }
        check(new FriendlyRecruitPickupCandidate().GetSavedSide("Usec")=="Usec","Legacy missing faction falls back to player");
        check(new FriendlyRecruitPickupCandidate {Side="Savage"}.GetSavedSide("Bear")=="Bear","Non-PMC faction cannot enter fallback recruit generation");
        // Exercise the production captured-profile normalizer without constructing
        // unrelated server generators. This boundary uses only the supplied profiles.
        var service=(FriendlyTeammateService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FriendlyTeammateService));
        var normalize=typeof(FriendlyTeammateService).GetMethod("NormalizeCapturedRecruitProfile",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        foreach(var side in new[]{"Bear","Usec"})
        {
            string leader=side=="Bear" ? "Usec" : "Bear";
            var player=new PmcData {Info=new() {Side=leader}};
            var captured=new BotBase {Info=new() {Side=side}};
            normalize.Invoke(service,new object[]{captured,player,new FriendlyRecruitPickupCandidate {Side=side}});
            check(captured.Info.Side==side,"Production captured-profile normalization retains opposite faction");
            captured=new BotBase {Info=new()};
            normalize.Invoke(service,new object[]{captured,player,new FriendlyRecruitPickupCandidate {Side=side}});
            check(captured.Info.Side==side,"Missing captured side uses invitation faction before leader fallback");
        }
    }
}
