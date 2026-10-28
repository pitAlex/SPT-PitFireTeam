using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Utils;

internal static partial class Program
{
    private static void TestRaidHealth(FriendlyTeammateService service, FriendlyTeammateStorage storage,
        FriendlyServerSettingsService settings, JsonUtil json, MongoId session, PmcData player, HealthConfig nativeConfig)
    {
        BotBaseHealth Baseline() => new()
        {
            UpdateTime = 1000, Energy = new() { Current = 77, Maximum = 100 }, Hydration = new() { Current = 88, Maximum = 100 },
            BodyParts = FriendlyTeammateTreatmentPricing.BodyParts.ToDictionary(p => p, p => new BodyPartHealth
            {
                Health = new() { Current = 60, Minimum = 0, Maximum = 60 },
                Effects = new() { ["HeavyBleeding"] = new(), ["Fracture"] = new(), ["DestroyedPart"] = new(), ["Stimulator"] = new() }
            })
        };
        FriendlyTeammateRaidHealth Snapshot(double current = 60, double spawnMaximum = 120) => new()
        {
            BodyParts = FriendlyTeammateTreatmentPricing.BodyParts.ToDictionary(p => p, _ => new FriendlyTeammateRaidBodyPartHealth
            { Current = current, SpawnMaximum = spawnMaximum })
        };
        var saved = Baseline();
        var raid = Snapshot();
        raid.BodyParts!["LeftLeg"].Current = 0;
        raid.BodyParts["RightLeg"].Current = 34; // Surgery shrank runtime max; original spawn maximum stays 120.
        Check(FriendlyTeammateRaidHealthPolicy.Apply(saved, raid, true, 0.1, 0.3, 2000), "complete survivor HP snapshot accepted");
        Check(saved.BodyParts!["Head"].Health!.Current == 30 && saved.BodyParts["Head"].Health!.Maximum == 60,
            "health multiplier normalizes back to canonical HP without changing saved maxima");
        Check(saved.BodyParts["LeftLeg"].Health!.Current == 6, "surviving blacked part receives native ten-percent penalty");
        Check(saved.BodyParts["RightLeg"].Health!.Current == 17, "surgery does not become free healing by normalizing to its reduced runtime maximum");
        Check(saved.BodyParts.Values.All(p => p.Effects == null), "all saved effects are removed, including bleeding, fracture, destroyed parts and stimulants");
        Check(saved.Energy!.Current == 77 && saved.Hydration!.Current == 88 && saved.UpdateTime == 2000,
            "HP-only result preserves resources and starts a fresh recovery checkpoint");
        Check(raid.BodyParts["LeftLeg"].Current == 0 && raid.BodyParts["RightLeg"].SpawnMaximum == 120, "raid payload is immutable input");
        var death = Baseline();
        Check(FriendlyTeammateRaidHealthPolicy.Apply(death, Snapshot(0), false, 0.1, 0.3, 2000)
            && death.BodyParts!.Values.All(p => p.Health!.Current == 18 && p.Effects == null),
            "lost/dead outcomes use native thirty-percent recovery instead of a dead saved profile");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0d, -1d })
        {
            var invalidRaid = Snapshot();
            invalidRaid.BodyParts!["Head"].SpawnMaximum = invalid;
            var unchanged = Baseline();
            string before = json.Serialize(unchanged)!;
            Check(!FriendlyTeammateRaidHealthPolicy.Apply(unchanged, invalidRaid, true, 0.1, 0.3, 2000) && json.Serialize(unchanged) == before,
                "invalid normalization maximum is rejected atomically");
        }
        var incomplete = Snapshot();
        incomplete.BodyParts!.Remove("Head");
        var legacy = Baseline();
        string legacyBefore = json.Serialize(legacy)!;
        Check(!FriendlyTeammateRaidHealthPolicy.Apply(legacy, incomplete, true, 0.1, 0.3, 2000)
            && !FriendlyTeammateRaidHealthPolicy.Apply(legacy, null, true, 0.1, 0.3, 2000) && json.Serialize(legacy) == legacyBefore,
            "partial and legacy no-health requests leave HP unchanged");
        var over = Baseline();
        FriendlyTeammateRaidHealthPolicy.Apply(over, Snapshot(180), true, 0.1, 0.3, 2000);
        Check(over.BodyParts!.Values.All(p => p.Health!.Current == 60), "temporary over-max HP is capped to canonical maximum");
        var resolved = service.ResolveRaidOutcomes([new() { Aid = "991", RaidHealth = Snapshot(), RollEscape = false }]);
        Check(resolved.Entries[0].RaidHealth!.BodyParts!["Head"].SpawnMaximum == 120, "Team Escape resolution preserves the per-part HP snapshot");

        // Real encrypted-profile commit/receipt path, independent of gear-maintenance mode.
        var previous = settings.LoadSettings();
        settings.SaveAndApply(new() { GameplayMode = "GunsForHire", LoadoutManagementMode = "Restricted", RestrictedGearMaintenance = false });
        var profile = new BotBase
        {
            Aid = 991, Id = new MongoId(), Info = new() { Side = "Usec", Nickname = "HP retention test", Settings = new() },
            Health = Baseline(), Inventory = new() { Items = [] }
        };
        storage.Write(session, "991.json", profile);
        var entry = new FriendlyTeammateDeathEscapeEntry { Aid = "991", Escaped = true, RaidHealth = Snapshot() };
        service.PersistDeathEscapeOutcomes(session, [entry], "raid-hp-test");
        var persisted = storage.Read<BotBase>(session, "991.json")!;
        Check(persisted.Health!.BodyParts!["Head"].Health!.Current == 30 && persisted.Health.BodyParts.Values.All(p => p.Effects == null),
            "real outcome service saves HP and clears effects even with Restricted upkeep disabled");
        Check(storage.Read<HashSet<string>>(session, "health/raid-outcomes-v1")!.Contains("raid-hp-test/991"),
            "health result and deduplication receipt are saved together");
        persisted.Health.BodyParts["Head"].Health!.Current = 55;
        storage.Write(session, "991.json", persisted);
        service.PersistDeathEscapeOutcomes(session, [entry], "raid-hp-test");
        Check(storage.Read<BotBase>(session, "991.json")!.Health!.BodyParts!["Head"].Health!.Current == 55,
            "duplicate outcome cannot reapply an injury after recovery or treatment");
        entry.Escaped = false;
        service.PersistDeathEscapeOutcomes(session, [entry], "next-raid-hp-test");
        Check(storage.Read<BotBase>(session, "991.json")!.Health!.BodyParts!.Values.All(p => Math.Abs(p.Health!.Current!.Value - 60 * nativeConfig.HealthMultipliers.Death) < 1e-9),
            "a new lost raid applies the configured native death penalty");
        persisted = storage.Read<BotBase>(session, "991.json")!;
        foreach (var part in persisted.Health!.BodyParts!.Values) part.Health!.Current = 30;
        persisted.Health.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        persisted.Health.BodyParts["LeftLeg"].Effects = new() { ["Fracture"] = new() };
        storage.Write(session, "991.json", persisted);
        Check(!service.TryGetSpawnProfile(session, "991", 2, out var rejectedSpawn) && rejectedSpawn == null,
            "wounded teammate cannot bypass invite readiness by requesting a spawn profile directly");
        var prepared = (BotBase)Private(service, "PrepareTeammateForFetch", persisted, 2d, false)!;
        Check(prepared.Health!.BodyParts!.Values.All(p => p.Health!.Maximum == 120 && p.Health.Current == 60 && p.Effects == null),
            "spawn preparation still preserves injured ratios internally while public admission rejects them");
        Check(storage.Read<BotBase>(session, "991.json")!.Health!.BodyParts!["Head"].Health!.Maximum == 60,
            "spawn multiplier never inflates the saved teammate maximum");
        TestWoundedAdmission(service, storage, json, session, persisted);
        storage.DeleteTeammate(session, 991);
        settings.SaveAndApply(previous);
    }

    private static void TestWoundedAdmission(FriendlyTeammateService service, FriendlyTeammateStorage storage,
        JsonUtil json, MongoId session, BotBase teammate)
    {
        string languageFile = System.IO.Path.Combine(AppContext.BaseDirectory, "user/mods/pitFireTeam-ServerMod/Resources/lang/en.json");
        byte[]? originalLanguage = File.Exists(languageFile) ? File.ReadAllBytes(languageFile) : null;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(languageFile)!);
        File.WriteAllText(languageFile, "{\"socialUi\":{\"TeammateStillWounded\":\"{0} is still wounded and cannot join a raid yet\"}}");
        try
        {
            Check(FriendlyTeammateRegeneration.IsWounded(teammate.Health), "partially healed health is wounded");
            Check(FriendlyTeammateRegeneration.IsWounded(null), "missing health cannot be declared raid-ready");
            storage.Write(session, "991-settings.json", new FriendlyTeammateSettings
            {
                AutoJoinEnabled = true, SelectedLoadoutId = "000000000000000000000000"
            });
            Check(!service.TryGetRaidGroupCharacter(session, "991", out var declined, out var reason)
                && declined != null && reason == "HP retention test is still wounded and cannot join a raid yet",
                "native invite declines with the exact localized wounded message and teammate name");
            Check(!service.GetAutoJoinTeammateAccountIds(session).Contains("991")
                && storage.Read<FriendlyTeammateSettings>(session, "991-settings.json")!.AutoJoinEnabled,
                "wounded teammate is skipped by auto-join without losing the saved preference");
            using (var state = System.Text.Json.JsonDocument.Parse(json.Serialize(service.GetTeammateHealthStates(session))!))
                Check(state.RootElement.EnumerateArray().Single(e => e.GetProperty("Aid").GetString() == "991").GetProperty("IsWounded").GetBoolean(),
                    "lightweight roster health status marks the wounded portrait");
            var full = storage.Read<BotBase>(session, "991.json")!;
            foreach (var part in full.Health!.BodyParts!.Values)
            {
                part.Health!.Current = part.Health.Maximum;
                part.Effects = null;
            }
            Check(!FriendlyTeammateRegeneration.IsWounded(full.Health), "all parts at maximum are healthy");
            full.Health.BodyParts["LeftArm"].Health!.Current -= 0.001;
            Check(FriendlyTeammateRegeneration.IsWounded(full.Health), "even one slightly injured part is still wounded");
            full.Health.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600;
            storage.Write(session, "991.json", full);
            using (var state = System.Text.Json.JsonDocument.Parse(json.Serialize(service.GetTeammateHealthStates(session))!))
                Check(!state.RootElement.EnumerateArray().Single(e => e.GetProperty("Aid").GetString() == "991").GetProperty("IsWounded").GetBoolean(),
                    "completed passive recovery clears wounded roster status without payment");
            Check(service.TryGetSpawnProfile(session, "991", 2, out var healthySpawn)
                && healthySpawn!.Health!.BodyParts!.Values.All(p => p.Health!.Current == p.Health.Maximum),
                "fully recovered teammate passes the public spawn health gate");
            Check(!service.TryGetRaidGroupCharacter(session, "991", out _, out var kitReason)
                && kitReason != null && !kitReason.Contains("still wounded"),
                "recovered teammate reaches the independent kit gate rather than stale wounded rejection");
            var idle = storage.Read<BotBase>(session, "991.json")!;
            idle.Health!.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600;
            double idleCheckpoint = idle.Health.UpdateTime.Value;
            storage.Write(session, "991.json", idle);
            service.GetTeammateHealthStates(session);
            Check(storage.Read<BotBase>(session, "991.json")!.Health!.UpdateTime == idleCheckpoint,
                "roster polling does not rewrite fully healed teammate checkpoints");
        }
        finally
        {
            if (originalLanguage != null) File.WriteAllBytes(languageFile, originalLanguage);
            else File.Delete(languageFile);
        }
    }
}
