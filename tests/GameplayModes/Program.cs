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
        var original = config.Where(pair => pair.Key.Key != "00 GameplayMode" && pair.Value != pitFireTeam.friendlyChanceMultiplier).ToDictionary(pair => pair.Key, pair => pair.Value.GetSerializedValue());
        Check(pitFireTeam.friendlyChanceMultiplier.Value == 1 && GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyChanceMultiplier), "friendly chance defaults to 1 and is disabled in Guns for Hire");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(!GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyChanceMultiplier), "friendly chance is enabled in Allegiance");
        pitFireTeam.friendlyChanceMultiplier.Value = 4;
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.friendlyChanceMultiplier) == 4, "Allegiance friendly chance is editable without a locked override");
        Check(SPT.Common.Http.RequestHandler.Mode == "Allegiance", "server mode follows client");
        Check(original.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "entering Allegiance preserves all saved config preferences");
        Check(SPT.Common.Http.RequestHandler.Loadout == "Immersive", "server synchronization uses effective loadout rather than saved preference");
        Check(!GameplayModeRuntime.GetEffectiveValue(pitFireTeam.badGuy) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pitFireTeamFLAG), "Allegiance friendliness locks");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.enemyTracking) == EnemyTrackingMode.Realistic, "realistic tracking");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pickupEnabled) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.tieredPickup) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.recruitPickup), "pickup locks");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract),
            "Allegiance enables Team Escape and restricts it to player extraction points");
        Check(GameplayModeRuntime.IsLocked(pitFireTeam.teamEscape) && GameplayModeRuntime.IsLocked(pitFireTeam.teamEscapeUseAnyExtract),
            "both Team Escape controls use the Allegiance disabled-control path");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2, "pickup capacity lock");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.heatlhMultiplier) == 3 &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.heatlhMultiplier), "Allegiance uses the saved health multiplier and enables its control");
        pitFireTeam.heatlhMultiplier.Value = 4;
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.heatlhMultiplier) == 4, "Allegiance uses the edited health multiplier");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Immersive, "loadout lock");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.healKey).MainKey == KeyCode.H &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.healKey), "manual healing key remains usable and editable in Allegiance");
        Check(!GameplayModeRuntime.IsLocked(custom), "unrelated controls remain unlocked");
        pitFireTeam.healKey.Value = new KeyboardShortcut(KeyCode.J);
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.healKey).MainKey == KeyCode.J, "Allegiance uses the current manual healing binding");
        pitFireTeam.maximumPickup.Value = 6;
        pitFireTeam.loadoutManagementMode.Value = LoadoutManagementMode.Restricted;
        pitFireTeam.teamEscape.Value = false;
        pitFireTeam.teamEscapeUseAnyExtract.Value = true;
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract),
            "external changes cannot defeat Team Escape locks");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2 && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Immersive, "external changes cannot defeat locks");
        // Simulate Notepad rather than a UI setting-change callback.
        File.WriteAllText(path, File.ReadAllText(path).Replace("Maximum = 6", "Maximum = 9"));
        config.Reload();
        Check(pitFireTeam.maximumPickup.Value == 9, "edited cfg really reloads into the saved preference");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2, "cfg reload cannot increase Allegiance recruitment capacity");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.enemyTracking) == EnemyTrackingMode.Realistic &&
            GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pickupEnabled) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.tieredPickup) &&
            GameplayModeRuntime.GetEffectiveValue(pitFireTeam.recruitPickup), "cfg reload cannot change tracking or recruitment rules");
        Check(!GameplayModeRuntime.GetEffectiveValue(pitFireTeam.badGuy) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pitFireTeamFLAG),
            "cfg reload cannot change friendliness rules");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.heatlhMultiplier) == 4, "cfg reload retains the editable Allegiance health multiplier");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.healKey).MainKey == KeyCode.J, "cfg reload retains the manual healing binding in Allegiance");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract) &&
            GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Immersive, "cfg reload cannot change escape or loadout rules");
        config.Save();
        Check(File.ReadAllText(path).Contains("Maximum = 9"), "saving cfg does not overwrite preferences with the effective policy");
        File.WriteAllText(path, File.ReadAllText(path).Replace("00 GameplayMode = Allegiance", "00 GameplayMode = GunsForHire"));
        config.Reload();
        Check(GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2,
            "cfg reload cannot perform an uncoordinated mode switch");
        Check(!File.ReadAllText(path + ".guns-for-hire.json").Contains("00 GameplayMode"), "snapshot excludes mode");
        Check(!File.ReadAllText(path + ".guns-for-hire.json").Contains("FriendlyChanceMultiplier"), "snapshot excludes Allegiance-only preference");
        custom.Value = 90;
        config = Bind(path); // Real BepInEx reload, with a new ConfigFile and newly bound entries.
        custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        Check(GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2, "restart preserves mode and locks");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.heatlhMultiplier) == 4 &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.heatlhMultiplier), "restart preserves the unlocked Allegiance health multiplier");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.healKey).MainKey == KeyCode.J &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.healKey), "restart preserves the usable Allegiance healing binding");
        Check(pitFireTeam.friendlyChanceMultiplier.Value == 4, "restart preserves Allegiance friendly chance preference");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract), "restart preserves Team Escape locks");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(pitFireTeam.friendlyChanceMultiplier.Value == 4 && GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyChanceMultiplier), "returning to Guns for Hire retains and disables Allegiance preference");
        Check(original.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "restart restores every original setting");
        Check(!GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract), "Guns for Hire restores both Team Escape preferences");
        pitFireTeam.maximumPickup.Value = 5;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(pitFireTeam.friendlyChanceMultiplier.Value == 4 && !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyChanceMultiplier), "returning to Allegiance retains and enables friendly chance preference");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 5, "second cycle snapshots latest Guns for Hire values");
        SPT.Common.Http.RequestHandler.FailBeforeCommit = true;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance), "server failure should reject switch");
        Check(!GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 5, "failed switch restores local settings");
        SPT.Common.Http.RequestHandler.FailBeforeCommit = false;
        SPT.Common.Http.RequestHandler.LoseResponse = true;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(GameplayModeRuntime.IsAllegiance, "committed mode recovered after lost response");
        SPT.Common.Http.RequestHandler.LoseResponse = false;
        string snapshot = File.ReadAllText(path + ".guns-for-hire.json");
        File.WriteAllText(path + ".guns-for-hire.json", "broken");
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire), "corrupt snapshot should reject restoration");
        Check(GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2, "corrupt snapshot leaves Allegiance intact");
        File.WriteAllText(path + ".guns-for-hire.json", snapshot);
        pitTeam.Components.SquadControlMenuUi.InRaid = true;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire), "in-raid change should fail");
        pitTeam.Components.SquadControlMenuUi.InRaid = false;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 5, "recovery after rejected switch");
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
        pitFireTeam.friendlyChanceMultiplier = c.Bind("Game", "FriendlyChanceMultiplier", 1, new ConfigDescription("", new AcceptableValueRange<int>(1,5)));
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
        public static ConfigEntry<int> maximumPickup, heatlhMultiplier, friendlyChanceMultiplier;
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
    internal static class FriendlyEncounterPenaltyRuntime { internal static Task ReloadAsync() => Task.CompletedTask; }
    public enum EnemyTrackingMode { Simple, Realistic }
    internal static class FollowerInsuranceRaidReports { internal static Task WaitForPendingReportsAsync() => Task.CompletedTask; }
}
namespace pitTeam.Components { public static class SquadControlMenuUi { public static bool InRaid; public static bool IsGameRaidActive() => InRaid; } }
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static string Mode = "GunsForHire";
        public static string Loadout;
        public static bool FailBeforeCommit, LoseResponse;
        public static string PostJson(string url, string body)
        {
            if (FailBeforeCommit) throw new IOException("Simulated server failure");
            Mode = JObject.Parse(body)["gameplayMode"].ToString();
            Loadout = JObject.Parse(body)["loadoutManagementMode"].ToString();
            if (LoseResponse) throw new IOException("Simulated lost response");
            return "{\"err\":0,\"data\":null}";
        }
        public static string GetJson(string url) => "{\"gameplayMode\":\"" + Mode + "\"}";
    }
}
