using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using pitTeam;
using pitTeam.Modules;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); } catch { checks++; return; }
        throw new Exception(message);
    }
    public static async Task Main()
    {
        var directory = Path.GetFullPath(Path.Combine("tests", "artifacts", "gameplay-modes", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "test.cfg");
        var config = Bind(path);
        var custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        var original = config.Where(pair => pair.Key.Key != "00 GameplayMode").ToDictionary(pair => pair.Key, pair => pair.Value.GetSerializedValue());
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(SPT.Common.Http.RequestHandler.Mode == "Allegiance", "server mode follows client");
        Check(!pitFireTeam.badGuy.Value && !pitFireTeam.pitFireTeamFLAG.Value, "Allegiance friendliness locks");
        Check(pitFireTeam.enemyTracking.Value == EnemyTrackingMode.Realistic, "realistic tracking");
        Check(pitFireTeam.pickupEnabled.Value && pitFireTeam.tieredPickup.Value && pitFireTeam.recruitPickup.Value, "pickup locks");
        Check(pitFireTeam.teamEscape.Value && !pitFireTeam.teamEscapeUseAnyExtract.Value,
            "Allegiance enables Team Escape and restricts it to player extraction points");
        Check(GameplayModeRuntime.IsLocked(pitFireTeam.teamEscape) && GameplayModeRuntime.IsLocked(pitFireTeam.teamEscapeUseAnyExtract),
            "both Team Escape controls use the Allegiance disabled-control path");
        Check(pitFireTeam.maximumPickup.Value == 2 && pitFireTeam.heatlhMultiplier.Value == 1, "numeric locks");
        Check(pitFireTeam.loadoutManagementMode.Value == LoadoutManagementMode.Immersive && pitFireTeam.healKey.Value.MainKey == KeyCode.None, "loadout and healing locks");
        Check(GameplayModeRuntime.IsLocked(pitFireTeam.healKey) && !GameplayModeRuntime.IsLocked(custom), "only required controls locked");
        pitFireTeam.maximumPickup.Value = 6;
        pitFireTeam.loadoutManagementMode.Value = LoadoutManagementMode.Restricted;
        pitFireTeam.teamEscape.Value = false;
        pitFireTeam.teamEscapeUseAnyExtract.Value = true;
        Check(pitFireTeam.teamEscape.Value && !pitFireTeam.teamEscapeUseAnyExtract.Value,
            "external changes cannot defeat Team Escape locks");
        Check(pitFireTeam.maximumPickup.Value == 2 && pitFireTeam.loadoutManagementMode.Value == LoadoutManagementMode.Immersive, "external changes cannot defeat locks");
        Check(!File.ReadAllText(path + ".guns-for-hire.json").Contains("00 GameplayMode"), "snapshot excludes mode");
        custom.Value = 90;
        config = Bind(path); // Real BepInEx reload, with a new ConfigFile and newly bound entries.
        custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        Check(GameplayModeRuntime.IsAllegiance && pitFireTeam.maximumPickup.Value == 2, "restart preserves mode and locks");
        Check(pitFireTeam.teamEscape.Value && !pitFireTeam.teamEscapeUseAnyExtract.Value, "restart preserves Team Escape locks");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(original.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "restart restores every original setting");
        Check(!pitFireTeam.teamEscape.Value && pitFireTeam.teamEscapeUseAnyExtract.Value, "Guns for Hire restores both Team Escape preferences");
        pitFireTeam.maximumPickup.Value = 5;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(pitFireTeam.maximumPickup.Value == 5, "second cycle snapshots latest Guns for Hire values");
        SPT.Common.Http.RequestHandler.FailBeforeCommit = true;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance), "server failure should reject switch");
        Check(!GameplayModeRuntime.IsAllegiance && pitFireTeam.maximumPickup.Value == 5, "failed switch restores local settings");
        SPT.Common.Http.RequestHandler.FailBeforeCommit = false;
        SPT.Common.Http.RequestHandler.LoseResponse = true;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(GameplayModeRuntime.IsAllegiance, "committed mode recovered after lost response");
        SPT.Common.Http.RequestHandler.LoseResponse = false;
        string snapshot = File.ReadAllText(path + ".guns-for-hire.json");
        File.WriteAllText(path + ".guns-for-hire.json", "broken");
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire), "corrupt snapshot should reject restoration");
        Check(GameplayModeRuntime.IsAllegiance && pitFireTeam.maximumPickup.Value == 2, "corrupt snapshot leaves Allegiance intact");
        File.WriteAllText(path + ".guns-for-hire.json", snapshot);
        pitTeam.Components.SquadControlMenuUi.InRaid = true;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire), "in-raid change should fail");
        pitTeam.Components.SquadControlMenuUi.InRaid = false;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(pitFireTeam.maximumPickup.Value == 5, "recovery after rejected switch");
        using var release = new ManualResetEventSlim();
        var request = GameplayModeRuntime.RunRosterRequest(() => release.Wait());
        Task changing = GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(!changing.IsCompleted && !GameplayModeRuntime.IsAllegiance, "mode waits for delayed post-raid roster requests");
        release.Set();
        await Task.WhenAll(request, changing);
        Check(GameplayModeRuntime.IsAllegiance, "mode proceeds after delayed requests finish");
        Console.WriteLine($"PASS: {checks} gameplay config checks. Artifacts: {directory}");
    }
    static ConfigFile Bind(string path)
    {
        var c = new ConfigFile(path, true);
        pitFireTeam.badGuy = c.Bind("Game", "BadGuy", true);
        pitFireTeam.pitFireTeamFLAG = c.Bind("Game", "Friendly", true);
        pitFireTeam.enemyTracking = c.Bind("Game", "Tracking", EnemyTrackingMode.Simple);
        pitFireTeam.pickupEnabled = c.Bind("Game", "Pickup", false);
        pitFireTeam.tieredPickup = c.Bind("Game", "Tiered", false);
        pitFireTeam.maximumPickup = c.Bind("Game", "Maximum", 6);
        pitFireTeam.recruitPickup = c.Bind("Game", "Recruit", false);
        pitFireTeam.teamEscape = c.Bind("Game", "TeamEscape", false);
        pitFireTeam.teamEscapeUseAnyExtract = c.Bind("Game", "TeamEscapeUseAnyExtract", true);
        pitFireTeam.loadoutManagementMode = c.Bind("Game", "Loadout", LoadoutManagementMode.Restricted);
        pitFireTeam.healKey = c.Bind("Game", "Heal", new KeyboardShortcut(KeyCode.H));
        pitFireTeam.heatlhMultiplier = c.Bind("Game", "Health", 3);
        pitFireTeam.pmcArmbands = c.Bind("Game", "Armbands", true);
        pitFireTeam.restrictedGearMaintenance = c.Bind("Game", "Maintenance", false);
        return c;
    }
}

// Only host/UI/HTTP dependencies are replaced. Config serialization, persistence and mode logic are production code.
namespace pitTeam
{
    public enum LoadoutManagementMode { Restricted, Immersive, Extreme }
    public class ConfigurationManagerAttributes { public bool Browsable; }
    public static class pitFireTeam
    {
        public static ConfigEntry<bool> badGuy, pitFireTeamFLAG, pickupEnabled, tieredPickup, recruitPickup, pmcArmbands, restrictedGearMaintenance;
        public static ConfigEntry<bool> teamEscape, teamEscapeUseAnyExtract;
        public static ConfigEntry<int> maximumPickup, heatlhMultiplier;
        public static ConfigEntry<EnemyTrackingMode> enemyTracking;
        public static ConfigEntry<LoadoutManagementMode> loadoutManagementMode;
        public static ConfigEntry<KeyboardShortcut> healKey;
        public static readonly TestLog Log = new();
        public static string GetSocialUiText(string key) => key;
        public class TestLog { public void LogError(object message) => Console.WriteLine(message); }
    }
}
namespace pitTeam.Modules
{
    public enum EnemyTrackingMode { Simple, Realistic }
    internal static class FollowerInsuranceRaidReports { internal static Task WaitForPendingReportsAsync() => Task.CompletedTask; }
}
namespace pitTeam.Components { public static class SquadControlMenuUi { public static bool InRaid; public static bool IsGameRaidActive() => InRaid; } }
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static string Mode = "GunsForHire";
        public static bool FailBeforeCommit, LoseResponse;
        public static string PostJson(string url, string body)
        {
            if (FailBeforeCommit) throw new IOException("Simulated server failure");
            Mode = JObject.Parse(body)["gameplayMode"].ToString();
            if (LoseResponse) throw new IOException("Simulated lost response");
            return "{\"err\":0,\"data\":null}";
        }
        public static string GetJson(string url) => "{\"gameplayMode\":\"" + Mode + "\"}";
    }
}
