using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;

internal static class ModeStorageTests
{
    public static FriendlyServerSettingsService CreateSettings() => new(
        DispatchProxy.Create<ISptLogger<FriendlyServerSettingsService>, TestLogger>(), null!, null!);

    public static async Task Run(Action<bool, string> check)
    {
        EquippedGearRetentionTests.Run(check);
        string initialDirectory = Environment.CurrentDirectory;
        string work = Path.GetFullPath(Path.Combine("tests", "artifacts", "mode-storage", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work);
        // Settings intentionally use this test executable's output directory, never the live install.
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "user", "mods", "pitFireTeam-ServerMod", "Resources", "settings.json");
        byte[]? previousSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        Environment.CurrentDirectory = work;
        try
        {
            var settings = CreateSettings();
            settings.SaveAndApply(new());
            StorageConcurrencyTests.Run(work, check);
            var storage = new FriendlyTeammateStorage(new FileUtil(), new JsonUtil([new SptJsonConverterRegistrator()]),
                DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>(), settings);
            var session = new MongoId("aaaaaaaaaaaaaaaaaaaaaaaa");
            storage.InitializeProfile(session);
            RecruitAggressionPersistenceTests.Run(storage,session,check);
            storage.WriteBatch(session, new Dictionary<string, string>
            {
                ["123.json"] = "{\"aid\":123}",
                ["123-settings.json"] = "{\"name\":\"Guns for Hire\"}",
                ["123-equipment.json"] = "[]",
                ["recruit-requests.json"] = "{\"mode\":\"Guns for Hire\"}"
            });
            string root = Path.Combine(work, "user", "mods", "pitFireTeam-ServerMod", "Resources");
            string hiredDatabase = Path.Combine(root, "teammates", session + ".db");
            byte[] hiredBytes = File.ReadAllBytes(hiredDatabase);
            storage.PrepareMode(session, true);
            settings.SaveAndApply(new() { GameplayMode = "Allegiance", LoadoutManagementMode = "Restricted" });
            check(settings.LoadSettings().LoadoutManagementMode == "Immersive", "server enforces Immersive");
            check(storage.ReadProfiles(session).Count == 0 && !storage.Exists(session, "recruit-requests.json"), "Allegiance starts with a separate empty database");
            EncounterPenaltyStorageTests.Run(storage, settings, check);
            storage.WriteBatch(session, new Dictionary<string, string>
            {
                ["456.json"] = "{\"aid\":456}",
                ["456-settings.json"] = "{\"name\":\"Allegiance\"}",
                ["recruit-requests.json"] = "{\"mode\":\"Allegiance\"}"
            });
            check(storage.GetAllAccountIds().SetEquals([123, 456]), "account IDs reserve both modes");
            check(File.ReadAllBytes(hiredDatabase).SequenceEqual(hiredBytes), "inactive Guns for Hire database is unchanged");
            var service = (FriendlyTeammateService)RuntimeHelpers.GetUninitializedObject(typeof(FriendlyTeammateService));
            var field = typeof(FriendlyTeammateService).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(f => f.FieldType == typeof(FriendlyServerSettingsService));
            field.SetValue(service, settings);
            AssertHiringBlocked(() => service.PrepareTeammateCreation(session, new()), check);
            AssertHiringBlocked(() => service.CreateTeammate(session, new()), check);
            settings.SaveAndApply(new() { GameplayMode = "GunsForHire" });
            check(storage.ReadProfiles(session).Single().Aid == 123 && storage.Exists(session, "123-equipment.json"), "Guns for Hire roster and equipment restored");
            check(storage.Read<Dictionary<string, string>>(session, "recruit-requests.json")!["mode"] == "Guns for Hire", "pending recruits are isolated");
            settings.SaveAndApply(new() { GameplayMode = "Allegiance" });
            var restarted = new FriendlyTeammateStorage(new FileUtil(), new JsonUtil([new SptJsonConverterRegistrator()]),
                DispatchProxy.Create<ISptLogger<FriendlyTeammateStorage>, TestLogger>(), CreateSettings());
            check(restarted.ReadProfiles(session).Single().Aid == 456, "restart selects persisted Allegiance database");
            check(restarted.DeleteTeammate(session, 456), "Allegiance removal succeeds");
            settings.SaveAndApply(new());
            check(restarted.ReadProfiles(session).Single().Aid == 123, "Allegiance removal preserves Guns for Hire");
            File.WriteAllText(settingsPath, "broken");
            bool failed = false;
            try { restarted.ReadProfiles(session); } catch (JsonException) { failed = true; }
            check(failed, "damaged settings cannot silently select another database");

            var entered = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            var writer = FriendlyModeRequestGate.Run(async () => { entered.SetResult(); await release.Task; return "done"; }).AsTask();
            await entered.Task;
            bool switched = false;
            var change = FriendlyModeRequestGate.Run(() => { switched = true; return new ValueTask<string>("changed"); }).AsTask();
            check(!switched && !change.IsCompleted, "mode change waits for ongoing teammate request");
            release.SetResult();
            await Task.WhenAll(writer, change);
            check(switched, "mode change proceeds after request completes");
            long previousGeneration = FriendlyModeRequestGate.Generation;
            FriendlyModeRequestGate.ModeChanged();
            bool invited = false;
            await FriendlyModeRequestGate.RunIfCurrent(previousGeneration, () => { invited = true; return Task.CompletedTask; });
            check(!invited, "late invitation from outgoing mode is discarded");
            await FriendlyModeRequestGate.RunIfCurrent(FriendlyModeRequestGate.Generation, () => { invited = true; return Task.CompletedTask; });
            check(invited, "current mode invitations remain available");
            FriendlyModeRequestGate.StartRaid(session.ToString());
            check(FriendlyModeRequestGate.HasActiveRaid, "server records active raid");
            FriendlyModeRequestGate.EndRaid(session.ToString());
            check(!FriendlyModeRequestGate.HasActiveRaid, "server releases raid restriction");
            SquadOnboardingTests.Run(storage, settings, new JsonUtil([new SptJsonConverterRegistrator()]), check);
            await ProfileWipeTests.Run(storage, settings, new JsonUtil([new SptJsonConverterRegistrator()]), check);
        }
        finally
        {
            Environment.CurrentDirectory = initialDirectory;
            if (previousSettings != null) File.WriteAllBytes(settingsPath, previousSettings);
            else if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static void AssertHiringBlocked(Action action, Action<bool, string> check)
    {
        bool blocked = false;
        try { action(); }
        catch (FriendlyTeammateException ex) { blocked = ex.Message == "SettingsUnavailableInAllegiance"; }
        check(blocked, "manual hiring rejected before candidate generation or payment");
    }
}
