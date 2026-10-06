using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace pitTeam.Modules
{
    public enum GameplayMode { GunsForHire, Allegiance }

    internal static class GameplayModeRuntime
    {
        private static readonly SemaphoreSlim SyncGate = new SemaphoreSlim(1, 1);
        private static readonly List<Task> PendingRosterRequests = new List<Task>();

        internal static Task RunRosterRequest(Action send)
        {
            lock (PendingRosterRequests)
            {
                PendingRosterRequests.RemoveAll(task => task.IsCompleted);
                Task task = Task.Run(send);
                PendingRosterRequests.Add(task);
                return task;
            }
        }

        private static async Task WaitForRosterRequestsAsync()
        {
            Task pending;
            lock (PendingRosterRequests) pending = Task.WhenAll(PendingRosterRequests.ToArray());
            pending = Task.WhenAll(pending, FollowerInsuranceRaidReports.WaitForPendingReportsAsync());
            if (await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(30))) != pending)
                throw new TimeoutException("Post-raid roster requests are still pending.");
            await pending;
        }
        private static ConfigFile config;
        private static ConfigEntry<GameplayMode> modeEntry;
        private static Dictionary<ConfigEntryBase, object> lockedValues;
        public static GameplayMode Current { get; private set; }
        public static bool IsAllegiance => Current == GameplayMode.Allegiance;
        public static bool IsApplying { get; private set; }
        public static bool IsSwitching { get; private set; }
        private static string SnapshotPath => config.ConfigFilePath + ".guns-for-hire.json";

        internal sealed class SavedValue
        {
            public string Section;
            public string Key;
            public string Value;
        }

        public static void Initialize(ConfigFile settings)
        {
            config = settings;
            modeEntry = config.Bind("", "00 GameplayMode", GameplayMode.GunsForHire,
                new ConfigDescription(pitFireTeam.GetSocialUiText("SquadControlModeTab"), null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            Current = Enum.IsDefined(typeof(GameplayMode), modeEntry.Value) ? modeEntry.Value : GameplayMode.GunsForHire;
            lockedValues = new Dictionary<ConfigEntryBase, object>
            {
                [pitFireTeam.badGuy] = false,
                [pitFireTeam.pitFireTeamFLAG] = false,
                [pitFireTeam.enemyTracking] = EnemyTrackingMode.Realistic,
                [pitFireTeam.pickupEnabled] = true,
                [pitFireTeam.tieredPickup] = true,
                [pitFireTeam.maximumPickup] = 2,
                [pitFireTeam.recruitPickup] = true,
                [pitFireTeam.teamEscape] = true,
                [pitFireTeam.teamEscapeUseAnyExtract] = false,
                [pitFireTeam.loadoutManagementMode] = LoadoutManagementMode.Immersive,
                [pitFireTeam.healKey] = new KeyboardShortcut(KeyCode.None),
                [pitFireTeam.heatlhMultiplier] = 1
            };
            config.SettingChanged -= EnforceLockedValues;
            config.SettingChanged += EnforceLockedValues;
            modeEntry.SettingChanged -= RejectUncoordinatedModeChange;
            modeEntry.SettingChanged += RejectUncoordinatedModeChange;
            if (IsAllegiance)
            {
                // Never replace a missing/damaged original snapshot with already-forced values.
                if (!File.Exists(SnapshotPath))
                    pitFireTeam.Log.LogError("Guns for Hire snapshot is missing; mode restoration is unavailable until it is recovered.");
                Apply(() => ForceAllegianceValues());
            }
        }

        public static bool IsLocked(ConfigEntryBase entry) => IsAllegiance && entry != null && lockedValues.ContainsKey(entry);

        private static void RejectUncoordinatedModeChange(object sender, EventArgs args)
        {
            if (!IsApplying && modeEntry.Value != Current) Apply(() => modeEntry.Value = Current);
        }

        private static void EnforceLockedValues(object sender, SettingChangedEventArgs args)
        {
            if (IsAllegiance && !IsApplying && lockedValues.ContainsKey(args.ChangedSetting)) Apply(ForceAllegianceValues);
        }

        private static void ForceAllegianceValues()
        {
            foreach (var pair in lockedValues) pair.Key.BoxedValue = pair.Value;
        }

        private static List<SavedValue> Capture() => config
            .Where(pair => pair.Value != modeEntry)
            .Select(pair => new SavedValue { Section = pair.Key.Section, Key = pair.Key.Key, Value = pair.Value.GetSerializedValue() }).ToList();

        private static List<SavedValue> ReadSnapshot()
        {
            var values = JsonConvert.DeserializeObject<List<SavedValue>>(File.ReadAllText(SnapshotPath));
            if (values == null || values.Count == 0) throw new InvalidDataException("Empty Guns for Hire settings snapshot.");
            // Validate every known value before applying any of them.
            foreach (var value in values)
            {
                var definition = new ConfigDefinition(value.Section, value.Key);
                if (config.ContainsKey(definition) && config[definition] != modeEntry)
                    TomlTypeConverter.ConvertToValue(value.Value, config[definition].SettingType);
            }
            return values;
        }

        private static void Restore(IEnumerable<SavedValue> values)
        {
            foreach (var value in values)
            {
                var definition = new ConfigDefinition(value.Section, value.Key);
                if (config.ContainsKey(definition) && config[definition] != modeEntry)
                    config[definition].SetSerializedValue(value.Value);
            }
        }

        private static void SaveSnapshot(List<SavedValue> values)
        {
            string temporary = SnapshotPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(values, Formatting.Indented));
            if (File.Exists(SnapshotPath)) File.Replace(temporary, SnapshotPath, null);
            else File.Move(temporary, SnapshotPath);
        }

        private static void Apply(Action action)
        {
            bool previousApplying = IsApplying;
            bool previousSave = config.SaveOnConfigSet;
            IsApplying = true;
            config.SaveOnConfigSet = false;
            try { action(); config.Save(); }
            finally { config.SaveOnConfigSet = previousSave; IsApplying = previousApplying; }
        }

        public static async Task ChangeAsync(GameplayMode next)
        {
            if (IsSwitching || next == Current) return;
            if (Components.SquadControlMenuUi.IsGameRaidActive()) throw new InvalidOperationException("Cannot change gameplay mode during a raid.");
            IsSwitching = true;
            await SyncGate.WaitAsync();
            GameplayMode previous = Current;
            List<SavedValue> before = null;
            try
            {
                await WaitForRosterRequestsAsync();
                before = Capture();
                var restored = next == GameplayMode.GunsForHire ? ReadSnapshot() : before;
                if (next == GameplayMode.Allegiance) SaveSnapshot(before);
                Apply(() =>
                {
                    Current = next;
                    if (IsAllegiance) ForceAllegianceValues(); else Restore(restored);
                    modeEntry.Value = Current;
                });
                try { await SendSettingsAsync(); }
                catch
                {
                    // A lost HTTP response may follow a committed server switch. Query before rolling back.
                    string response = await Task.Run(() => RequestHandler.GetJson("/singleplayer/pitfireteam/gameplay-mode"));
                    JToken root = JToken.Parse(response);
                    string actual = (root["data"] ?? root)["gameplayMode"]?.ToString();
                    if (!string.Equals(actual, Current.ToString(), StringComparison.Ordinal)) throw;
                }
            }
            catch
            {
                if (before != null)
                {
                    Apply(() => { Current = previous; Restore(before); modeEntry.Value = previous; });
                    // Reconcile an ambiguous lost response with the restored client state.
                    try { await SendSettingsAsync(); }
                    catch (Exception ex) { pitFireTeam.Log.LogError("Mode rollback synchronization failed: " + ex); }
                }
                throw;
            }
            finally { SyncGate.Release(); IsSwitching = false; }
        }

        public static async Task SynchronizeAsync()
        {
            if (IsApplying || IsSwitching || config == null) return;
            await SyncGate.WaitAsync();
            try { await SendSettingsAsync(); }
            finally { SyncGate.Release(); }
        }

        private static Task SendSettingsAsync()
        {
            string body = JsonConvert.SerializeObject(new
            {
                gameplayMode = Current.ToString(),
                pmcArmbands = pitFireTeam.pmcArmbands.Value,
                loadoutManagementMode = pitFireTeam.loadoutManagementMode.Value.ToString(),
                restrictedGearMaintenance = pitFireTeam.restrictedGearMaintenance.Value
            });
            return Task.Run(() =>
            {
                var response = JObject.Parse(RequestHandler.PostJson("/singleplayer/pitfireteam/settings", body));
                if (response["err"]?.Value<int>() != 0) throw new InvalidOperationException(response["errmsg"]?.ToString() ?? "Mode synchronization failed.");
            });
        }
    }
}
