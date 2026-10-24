using System.Collections;
using System.Reflection;
using LiteDB;
using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using pitTeam.Server.Routers.Static;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Launcher;
using SPTarkov.Server.Core.Models.Spt.Launcher;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

internal static class ProfileWipeTests
{
    public static async Task Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings,
        JsonUtil json, Action<bool, string> check)
    {
        var account = new MongoId("111111111111111111111111");
        var other = new MongoId("222222222222222222222222");
        var saveConstructor = typeof(SaveServer).GetConstructors().Single();
        var saves = (SaveServer)saveConstructor.Invoke(new object?[saveConstructor.GetParameters().Length]);
        saves.CreateProfile(new() { ProfileId = account, Username = "WipedAccount", IsWiped = true });
        saves.CreateProfile(new() { ProfileId = other, Username = "OtherAccount", IsWiped = false });
        var constructor = typeof(FriendlyTeammateService).GetConstructors().Single();
        var teammates = (FriendlyTeammateService)constructor.Invoke(constructor.GetParameters()
            .Select(parameter => parameter.Name switch
            {
                "storage" => (object)storage,
                "settingsService" => settings,
                _ => null
            }).ToArray());
        var callbacks = new FriendlyProfileWipeCallbacks(saves, teammates, json,
            DispatchProxy.Create<ISptLogger<FriendlyProfileWipeCallbacks>, TestLogger>());
        var router = new FriendlyProfileWipeRouter(json, callbacks);
        var request = new RegisterData { Username = "WipedAccount", Edition = "Standard" };
        string body = json.Serialize(request)!;
        string success = json.Serialize(new LauncherV2WipeResponse { Response = true, Profiles = [] })!;
        // Only the wipe switch is consumed by this isolated native controller path.
        var coreConfig = Activator.CreateInstance<CoreConfig>();
        coreConfig.AllowProfileWipe = false;
        var launcherConstructor = typeof(LauncherV2Controller).GetConstructors().Single();
        var launcher = (LauncherV2Controller)launcherConstructor.Invoke(launcherConstructor.GetParameters()
            .Select(parameter => parameter.Name switch
            {
                "saveServer" => (object)saves,
                "coreConfig" => coreConfig,
                _ => null
            }).ToArray());
        check(new SPTarkov.Server.Core.Routers.Static.LauncherV2StaticRouter(null!, json).CanHandle("/launcher/v2/wipe"),
            "installed SPT launcher router exposes the hooked wipe route");
        bool nativeRejected = launcher.Wipe(request);
        check(!nativeRejected, "installed SPT rejects wipe when disabled");
        string failure = json.Serialize(new LauncherV2WipeResponse { Response = nativeRejected, Profiles = [] })!;
        var onboarding = new FriendlySquadOnboardingService(storage, settings, null!, json);
        var state = new FriendlySquadOnboardingState
        {
            FirstTimeVisit = true, GameplayMode = "Allegiance", WelcomeState = "scheduled",
            InviteDueAtMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1,
            WelcomeCandidate = new() { ProfileId = "333333333333333333333333", AccountId = "901", IsWelcomeTeammate = true }
        };
        foreach (bool allegiance in new[] { false, true })
        {
            storage.WriteModeDocuments(account, allegiance, new Dictionary<string, string>
            {
                ["900.json"] = "{\"aid\":900}", ["900-settings.json"] = "{}", ["900-equipment.json"] = "[]",
                ["recruit-requests.json"] = "[]", ["pending-creation.json"] = "{}",
                ["pending-deletion.json"] = "{}", ["accepted-recruits.json"] = "{}",
                ["friendly-encounter-penalties.json"] = "{}", ["recovery/old/900.json"] = "{}",
                ["welcome-invitation-delivery.json"] = "{\"welcomeState\":\"delivered\"}"
            });
            storage.WriteModeDocuments(other, allegiance, new Dictionary<string, string> { ["902.json"] = "{\"aid\":902}" });
        }
        storage.WriteSharedDocument(account, "squad-onboarding.json", state);
        storage.WriteSharedDocument(other, "squad-onboarding.json", new FriendlySquadOnboardingState { FirstTimeVisit = true });

        string root = Path.Combine(Environment.CurrentDirectory, "user", "mods", "pitFireTeam-ServerMod", "Resources");
        var otherBefore = new[] { "teammates", "teammates-allegiance" }.ToDictionary(mode => mode,
            mode => File.ReadAllBytes(Path.Combine(root, mode, other + ".db")));
        string legacy = Path.Combine(root, "teammates", account.ToString());
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(legacy + ".backup");
        const string oldJson = "{\"aid\":900}";
        File.WriteAllText(Path.Combine(legacy, "900.json"), oldJson);
        File.WriteAllText(Path.Combine(legacy + ".backup", "900.json"), oldJson);

        check(router.CanHandle("/launcher/v2/wipe"), "wipe hook matches the launcher's native route");
        var rejected = await router.HandleStaticAsync("/launcher/v2/wipe", body, other, failure);
        check((string)rejected == failure && storage.HasTeammatesInEitherMode(account) && onboarding.GetStatus(account).FirstTimeVisit,
            "rejected native wipe preserves squads and onboarding and returns the unchanged response");
        await router.HandleStaticAsync("/launcher/v2/wipe", body, other, "");
        check(storage.HasTeammatesInEitherMode(account), "missing native wipe output cannot clear an account");
        saves.GetProfile(account).ProfileInfo!.IsWiped = false;
        bool unconfirmed = false;
        try { await router.HandleStaticAsync("/launcher/v2/wipe", body, other, success); }
        catch (InvalidOperationException) { unconfirmed = true; }
        check(unconfirmed && storage.HasTeammatesInEitherMode(account), "success output without native account confirmation cannot clear a squad");
        coreConfig.AllowProfileWipe = true;
        bool nativeAccepted = launcher.Wipe(request);
        check(nativeAccepted && saves.GetProfile(account).ProfileInfo!.IsWiped == true,
            "installed SPT accepts the username-based wipe and publishes the confirmation flag");
        success = json.Serialize(new LauncherV2WipeResponse { Response = nativeAccepted, Profiles = [] })!;

        var notices = (IDictionary)typeof(FriendlyTeammateService).GetField("profileRecoveryNotices", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(teammates)!;
        notices.Add(account + ":900", new FriendlyTeammateProfileRecoveryNotice());
        notices.Add(other + ":902", new FriendlyTeammateProfileRecoveryNotice());
        FriendlyModeRequestGate.StartRaid(account.ToString());
        FriendlyModeRequestGate.StartRaid(other.ToString());
        settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
        object result = await router.HandleStaticAsync("/launcher/v2/wipe", body, other, success);
        check((string)result == success, "successful wipe preserves the native launcher response");
        check(!storage.HasTeammatesInEitherMode(account) && !onboarding.GetStatus(account).FirstTimeVisit,
            "successful launcher wipe clears both rosters and resets the first-visit flag");
        check(FriendlyModeRequestGate.HasActiveRaid && !notices.Contains(account + ":900") && notices.Contains(other + ":902"),
            "wipe clears only that account's transient raid and recovery state");
        FriendlyModeRequestGate.EndRaid(other.ToString());
        check(!FriendlyModeRequestGate.HasActiveRaid, "wiped account cannot retain its old raid state");
        check(settings.LoadSettings().GameplayMode == "Allegiance", "account wipe retains global mode preferences");
        check(!onboarding.DeliverWelcomeInvitation(account).InvitationDelivered
            && storage.ReadModeDocument<List<FriendlyRecruitRequestEntry>>(account, "recruit-requests.json", true) == null,
            "an old delayed welcome callback cannot recreate an invitation after wipe");
        foreach (string mode in new[] { "teammates", "teammates-allegiance" })
        {
            using var database = new LiteDatabase(Path.Combine(root, mode, account + ".db"));
            var documents = database.GetCollection("documents").FindAll().ToList();
            check(documents.Count == 1 && documents[0]["_id"].AsString == "migration/legacy-json-v1",
                "wipe removes all mode documents while keeping only the completed import marker: " + mode);
            check(File.ReadAllBytes(Path.Combine(root, mode, other + ".db")).SequenceEqual(otherBefore[mode]),
                "another account's database stays byte-for-byte unchanged: " + mode);
        }
        await router.HandleStaticAsync("/launcher/v2/wipe", body, other, success);
        check(!storage.HasTeammatesInEitherMode(account), "repeated launcher wipe is safe");
        var restarted = new FriendlyTeammateStorage(new FileUtil(), json,
            DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>(), settings);
        check(!restarted.HasTeammatesInEitherMode(account)
            && restarted.ReadSharedDocument<FriendlySquadOnboardingState>(account, "squad-onboarding.json") == null,
            "restart keeps squads and initial mode choice cleared despite restored legacy files");
        check(File.ReadAllText(Path.Combine(legacy, "900.json")) == oldJson
            && File.ReadAllText(Path.Combine(legacy + ".backup", "900.json")) == oldJson,
            "wipe preserves legacy source and backup files without reimporting them");
        check(onboarding.GetStatus(other).FirstTimeVisit && restarted.HasTeammatesInEitherMode(other),
            "request username selects the wiped account instead of the unrelated HTTP session");
        settings.SaveAndApply(new());
    }
}
