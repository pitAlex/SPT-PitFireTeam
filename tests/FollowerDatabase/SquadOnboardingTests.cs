using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

internal static class SquadOnboardingTests
{
    private const string StateDocument = "squad-onboarding.json";
    public static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings, JsonUtil json, Action<bool, string> check)
    {
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
        var service = new FriendlySquadOnboardingService(storage, settings, null!, json);
        var existing = new MongoId("cccccccccccccccccccccccc");
        check(!service.GetStatus(existing).FirstTimeVisit, "existing accounts without an onboarding flag must choose");
        storage.WriteModeDocuments(existing, false, new Dictionary<string, string> { ["711.json"] = "{\"aid\":711}" });
        settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        var completed = service.Complete(existing, "Allegiance");
        check(completed.FirstTimeVisit && !completed.WelcomePending, "a squad in the inactive mode suppresses the welcome invite");
        check(storage.ReadSharedDocument<FriendlySquadOnboardingState>(existing, StateDocument)!.WelcomeState == "skipped", "suppressed onboarding outcome is durable");
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
        check(service.GetStatus(existing).FirstTimeVisit, "the first-visit flag follows the account across modes");
        check(storage.ReadProfiles(existing).Count == 1, "onboarding preserves the existing roster");
        check(storage.ReadModeDocument<FriendlySquadOnboardingState>(existing, StateDocument, true) == null, "onboarding has one canonical document");
        check(service.Complete(existing, "GunsForHire").FirstTimeVisit, "repeated completion does not grant a starter");

        var session = new MongoId("dddddddddddddddddddddddd");
        settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        bool mismatchedMode = false;
        try { service.Complete(session, "GunsForHire"); }
        catch (InvalidOperationException) { mismatchedMode = true; }
        check(mismatchedMode && !service.GetStatus(session).FirstTimeVisit, "failed mode confirmation leaves the initial choice pending");
        check(!service.ScreenRefreshed(session).WelcomePending, "refresh without a completed choice cannot grant a gift");
        var candidate = new FriendlyRecruitRequestEntry
        {
            ProfileId = "eeeeeeeeeeeeeeeeeeeeeeee", AccountId = "712", Nickname = "Starter",
            Level = 1, Side = "Bear", Voice = "Bear_1", Head = "test-head", IsWelcomeTeammate = true,
            ProfileJson = "{\"info\":{\"level\":1,\"experience\":0}}"
        };
        var intent = new FriendlySquadOnboardingState
        {
            FirstTimeVisit = true, GameplayMode = "Allegiance", WelcomeState = "waiting-refresh", WelcomeCandidate = candidate
        };
        storage.WriteSharedDocument(session, StateDocument, intent);
        check(storage.ReadProfiles(session).Count == 0, "shared onboarding state never appears in roster enumeration");
        check(!service.DeliverWelcomeInvitation(session).InvitationDelivered, "welcome invite waits for screen refresh acknowledgment");
        var ready = service.ScreenRefreshed(session);
        check(ready.WelcomePending && ready.DelayMilliseconds is > 1500 and <= 2000, "two-second deadline starts after refresh");
        long deadline = storage.ReadSharedDocument<FriendlySquadOnboardingState>(session, StateDocument)!.InviteDueAtMilliseconds;
        service.ScreenRefreshed(session);
        check(storage.ReadSharedDocument<FriendlySquadOnboardingState>(session, StateDocument)!.InviteDueAtMilliseconds == deadline, "repeated refresh does not restart the timer");
        check(!service.DeliverWelcomeInvitation(session).InvitationDelivered, "early delivery is rejected");

        intent = storage.ReadSharedDocument<FriendlySquadOnboardingState>(session, StateDocument)!;
        intent.InviteDueAtMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1;
        storage.WriteSharedDocument(session, StateDocument, intent);
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
        check(!service.DeliverWelcomeInvitation(session).InvitationDelivered, "switching modes defers delivery instead of sending to the wrong roster");
        check(storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json") == null, "inactive-mode invite cannot leak into the active inbox");
        settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        FriendlyModeRequestGate.StartRaid(session.ToString());
        check(!service.DeliverWelcomeInvitation(session).InvitationDelivered, "delivery waits while a raid is active");
        FriendlyModeRequestGate.EndRaid(session.ToString());
        var restarted = new FriendlySquadOnboardingService(storage, settings, null!, json);
        check(restarted.DeliverWelcomeInvitation(session).InvitationDelivered, "restart resumes the saved candidate in its selected mode");
        var inbox = storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!;
        check(inbox.Count == 1 && inbox[0].ProfileJson == candidate.ProfileJson && inbox[0].IsWelcomeTeammate, "delivered candidate and server-only starter policy survive storage");
        restarted.DeliverWelcomeInvitation(session);
        check(storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Count == 1, "duplicate delivery cannot create another invite");
        check(storage.ReadProfiles(session).Count == 0, "an invitation does not immediately create a teammate");
        // Simulate a crash after target inbox/receipt commit but before the shared outcome save,
        // followed by acceptance or decline removing the original invitation.
        storage.Write(session, "recruit-requests.json", new List<FriendlyRecruitRequestEntry>());
        intent.WelcomeState = "scheduled";
        storage.WriteSharedDocument(session, StateDocument, intent);
        restarted.DeliverWelcomeInvitation(session);
        check(storage.Read<List<FriendlyRecruitRequestEntry>>(session, "recruit-requests.json")!.Count == 0, "cross-database recovery never resurrects an accepted or declined invite");
        check(storage.ReadSharedDocument<FriendlySquadOnboardingState>(session, StateDocument)!.WelcomeCandidate == null, "completed shared state releases the candidate payload");

        var lateSquad = new MongoId("ffffffffffffffffffffffff");
        intent.WelcomeState = "scheduled";
        storage.WriteSharedDocument(lateSquad, StateDocument, intent);
        storage.WriteModeDocuments(lateSquad, false, new Dictionary<string, string> { ["713.json"] = "{\"aid\":713}" });
        check(!service.DeliverWelcomeInvitation(lateSquad).WelcomePending, "a squad acquired before delivery suppresses the gift");
        check(storage.ReadSharedDocument<FriendlySquadOnboardingState>(lateSquad, StateDocument)!.WelcomeState == "skipped", "late suppression is permanent");

        var profile = new BotBase
        {
            Info = new() { Level = 45, Experience = 400000 },
            Skills = new Skills { Common = [new CommonSkill { Id = SkillTypes.Assault, Progress = 4500, PointsEarnedDuringSession = 50 }], Points = 40 },
            Stats = new Stats { Eft = new EftStats { TotalInGameTime = 6000, OverallCounters = new OverallCounters { Items = [] } } }
        };
        var normalize = typeof(FriendlyTeammateService).GetMethod("NormalizeWelcomeProfile", BindingFlags.NonPublic | BindingFlags.Static)!;
        normalize.Invoke(null, [profile]);
        check(profile.Info.Level == 1 && profile.Info.Experience == 0, "starter normalization fixes level and experience");
        check(profile.Skills.Common!.Single().Progress == 0 && profile.Skills.Common.Single().PointsEarnedDuringSession == 0
            && profile.Skills.Points == 0 && !profile.Skills.Mastering!.Any(), "starter normalization clears generated skill, mastery and session bonuses");
        check(profile.Stats.Eft.TotalInGameTime == 0, "starter has no fabricated raid history");
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
    }
}
