using System.Collections;
using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

internal static class AddonOnboardingTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings, JsonUtil json, Action<bool,string> check)
    {
        const string stateDocument="squad-onboarding.json", rogue="GoingRogue";
        var service=new FriendlySquadOnboardingService(storage,settings,null!,json);
        string[] all={"GunsForHire","Allegiance",rogue};
        var modes=(IDictionary)typeof(FriendlyGameplayModeAddons).GetField("Modes",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var registered=modes[rogue]!;
        void Mode(string mode)=>settings.SaveAndApply(new(){GameplayMode=mode});
        void Existing(MongoId id,string mode)
        {
            storage.WriteModeDocuments(id,mode,new Dictionary<string,string>{{"765.json","{\"aid\":765,\"Info\":{\"Nickname\":\"Preserved\"}}"}});
            storage.WriteSharedDocument(id,stateDocument,new FriendlySquadOnboardingState
            {
                FirstTimeVisit=true,GameplayMode=mode,WelcomeState="delivered",InviteDueAtMilliseconds=123,
                WelcomeCandidate=new(){ProfileId="aaaaaaaaaaaaaaaaaaaaaaaa",Nickname="Unchanged",ProfileJson="{}"}
            });
        }
        void Refuses(Action action,string description)
        {try{action();}catch(InvalidOperationException){check(true,description);return;}check(false,description);}
        string Welcome(FriendlySquadOnboardingState state)=>json.Serialize(new{state.GameplayMode,state.WelcomeState,state.InviteDueAtMilliseconds,state.WelcomeCandidate})!;
        Mode("GunsForHire");
        var fresh=new MongoId("a11111111111111111111111");
        check(service.GetStatus(fresh).ModeChoices.SequenceEqual(all),"new users receive all three cards with the server addon registered");
        storage.WriteModeDocuments(fresh,"GunsForHire",new Dictionary<string,string>{{"765.json","{\"aid\":765}"}});
        check(service.Complete(fresh,"GunsForHire",all).FirstTimeVisit,"first three-card selection completes ordinary onboarding");
        check(storage.ReadSharedDocument<FriendlySquadOnboardingState>(fresh,stateDocument)!.GoingRogueChoiceCompleted,
            "a three-card first choice also acknowledges the addon without a second prompt");
        check(service.GetStatus(fresh).ModeChoices.Count==0,"first-choice completion does not show another pair of cards");

        foreach(var (idText,current) in new[]{("b11111111111111111111111","GunsForHire"),("b22222222222222222222222","Allegiance")})
        {
            var id=new MongoId(idText);Mode(current);Existing(id,current);
            string roster=storage.ReadModeProfiles(id,current).Single().Value;
            string welcome=Welcome(storage.ReadSharedDocument<FriendlySquadOnboardingState>(id,stateDocument)!);
            var offered=service.GetStatus(id);
            check(offered.AddonChoiceRequired && offered.ModeChoices.SequenceEqual(new[]{current,rogue}),"addon installation offers exactly current mode plus Going Rogue: "+current);
            var restarted=new FriendlySquadOnboardingService(storage,settings,null!,json);
            check(restarted.GetStatus(id).ModeChoices.SequenceEqual(offered.ModeChoices),"Back/restart leaves the addon choice pending: "+current);
            check(!storage.ReadSharedDocument<FriendlySquadOnboardingState>(id,stateDocument)!.GoingRogueChoiceCompleted,"displaying cards does not acknowledge a choice");
            FriendlyModeRequestGate.StartRaid(id.ToString());
            try{Refuses(()=>service.Complete(id,current,offered.ModeChoices.ToArray()),"active raids cannot acknowledge the addon prompt");}
            finally{FriendlyModeRequestGate.EndRaid(id.ToString());}
            Refuses(()=>service.Complete(id,current,new[]{current}),"a client without the addon card cannot acknowledge it");
            var completed=service.Complete(id,current,offered.ModeChoices.ToArray());
            check(!completed.AddonChoiceRequired && completed.ModeChoices.Count==0,"keeping the current mode dismisses the prompt permanently");
            check(Welcome(storage.ReadSharedDocument<FriendlySquadOnboardingState>(id,stateDocument)!)==welcome,"addon selection preserves original welcome state/candidate/deadline");
            check(storage.ReadModeProfiles(id,current).Single().Value==roster && storage.ReadModeProfiles(id,rogue).Count==0,"addon selection preserves current roster and creates no Goons or welcome teammate");
            service.Complete(id,current,offered.ModeChoices.ToArray());
            check(!new FriendlySquadOnboardingService(storage,settings,null!,json).GetStatus(id).AddonChoiceRequired,"acknowledgement survives retries and service restart");
        }

        var switching=new MongoId("c11111111111111111111111");Mode("GunsForHire");Existing(switching,"GunsForHire");
        var pair=service.GetStatus(switching).ModeChoices.ToArray();
        Mode("Allegiance");Refuses(()=>service.Complete(switching,"Allegiance",pair),"an unoffered third mode cannot complete the two-card prompt");
        Mode(rogue);Refuses(()=>service.Complete(switching,"GunsForHire",pair),"failed mode synchronization cannot complete an addon choice");
        check(!service.Complete(switching,rogue,pair).AddonChoiceRequired,"choosing Going Rogue completes after the active server mode changes");
        Mode("GunsForHire");check(!service.GetStatus(switching).AddonChoiceRequired,"later mode switches do not repeat an acknowledged addon installation");
        var already=new MongoId("c33333333333333333333333");Existing(already,rogue);Mode(rogue);
        check(service.GetStatus(already).ModeChoices.Count==0 && storage.ReadSharedDocument<FriendlySquadOnboardingState>(already,stateDocument)!.GoingRogueChoiceCompleted,
            "existing Going Rogue users are acknowledged without two identical mode cards");

        var clientMissing=new MongoId("c22222222222222222222222");Mode("GunsForHire");
        storage.WriteModeDocuments(clientMissing,"GunsForHire",new Dictionary<string,string>{{"765.json","{\"aid\":765}"}});
        service.Complete(clientMissing,"GunsForHire",new[]{"GunsForHire","Allegiance"});
        check(!storage.ReadSharedDocument<FriendlySquadOnboardingState>(clientMissing,stateDocument)!.GoingRogueChoiceCompleted,
            "server-only addon installation cannot consume a client card the player never saw");
        check(service.GetStatus(clientMissing).AddonChoiceRequired,"installing the client addon later still offers the pair");
        modes.Remove(rogue);
        try
        {
            var without=new MongoId("d11111111111111111111111");
            check(service.GetStatus(without).ModeChoices.SequenceEqual(new[]{"GunsForHire","Allegiance"}),"new users without the server addon see only two built-in choices");
            check(!service.GetStatus(clientMissing).AddonChoiceRequired,"unavailable addon cannot expose its installation prompt");
            Refuses(()=>service.Complete(clientMissing,"GunsForHire",pair),"an unavailable addon cannot be acknowledged");
        }
        finally{modes.Add(rogue,registered);}
        check(!service.GetStatus(switching).AddonChoiceRequired,"removing and reinstalling the addon does not repeat a completed choice");
        check(service.GetStatus(clientMissing).AddonChoiceRequired,"an unacknowledged offer resumes after addon reinstall");
        Mode("GunsForHire");
    }
}
