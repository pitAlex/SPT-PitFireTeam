using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using pitTeam.Shared;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

internal static class EncounterPenaltyStorageTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings, Action<bool,string> check)
    {
        long start=DateTimeOffset.Parse("2026-10-06T12:00:00Z").ToUnixTimeMilliseconds();
        var session=new MongoId("cccccccccccccccccccccccc");
        var other=new MongoId("dddddddddddddddddddddddd");
        var service=new FriendlyEncounterPenaltyService(storage,settings);
        var kill=new FriendlyPostRaidKillMessageRequest {RaidId="raid-one",VictimProfileId="recruit-one",MessageKind="traitor",MessageText="",KilledAtUnixMs=start};
        var state=service.Record(session,kill,start);
        check(state.Entries.Count==1 && state.Entries[0].ExpiresAtUnixMs==start+FriendlyEncounterPenaltyPolicy.DurationMs,"traitor report stores 24h penalty even without message text");
        service.Record(session,kill,start+1000);
        check(service.Get(session,start+1000).Entries.Count==1,"repeated report does not double count or extend expiry");
        check(service.Get(other,start).Entries.Count==0,"penalties are isolated by user id");
        kill.VictimProfileId="recruit-two";kill.KilledAtUnixMs=start+3600000;
        state=service.Record(session,kill,start+3600000);
        check(FriendlyEncounterPenaltyPolicy.GetPoints(state.Entries,start+3600000)==10,"kills stack five percentage points each");
        var restartedStorage=new FriendlyTeammateStorage(new FileUtil(),new JsonUtil([new SptJsonConverterRegistrator()]),
            DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>,TestLogger>(),settings);
        var restarted=new FriendlyEncounterPenaltyService(restartedStorage,settings);
        check(restarted.Get(session,start+7200000).Entries.Count==2,"encrypted profile database retains penalties after service/storage restart");
        settings.SaveAndApply(new() {GameplayMode="GunsForHire"});
        check(service.Record(session,kill,start+7200000).Entries.Count==0 && !storage.Exists(session,"friendly-encounter-penalties.json"),"Guns for Hire neither applies nor writes penalty state");
        settings.SaveAndApply(new() {GameplayMode="Allegiance"});
        check(restarted.Get(session,start+7200000).Entries.Count==2,"switching modes does not discard Allegiance penalties");
        kill.VictimProfileId="ordinary-friendly";kill.MessageKind="jerk";kill.KilledAtUnixMs=start+7200000;
        check(service.Record(session,kill,start+7200000).Entries.Count==2,"ordinary friendly PMC jerk report is not a recruit penalty");
        state=service.Get(session,start+FriendlyEncounterPenaltyPolicy.DurationMs);
        check(state.Entries.Count==1 && FriendlyEncounterPenaltyPolicy.GetPoints(state.Entries,start+FriendlyEncounterPenaltyPolicy.DurationMs)==5,"first penalty expires at its own kill plus 24h");
        check(state.Entries[0].Id.EndsWith("recruit-two"),"newer kill retains its separate expiration");
        check(service.Get(session,start+FriendlyEncounterPenaltyPolicy.DurationMs+3600000).Entries.Count==0,"last penalty expires completely");
        kill.MessageKind="traitor";kill.VictimProfileId="recruit-one";kill.KilledAtUnixMs=start;
        check(service.Record(session,kill,start+FriendlyEncounterPenaltyPolicy.DurationMs+7200000).Entries.Count==0,"late retry cannot revive an expired kill penalty");
    }
}
