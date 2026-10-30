using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using pitTeam;
using pitTeam.Modules;
using pitTeam.GoingRogue;
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
        KnightCombatScopeTests.Run(Check);
        await SpawnPreflightTests.Run(Check);
        bool Selects(IEnumerable<string> accounts, string json, bool goonsOnly = false)
        {
            var selection = new pitTeam.GoingRogue.RogueSelectionState();
            selection.Begin(accounts);
            selection.Resolve(json);
            return goonsOnly ? selection.HasGoons : selection.HasRogues;
        }
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye", "exUsec" })
            Check(Selects(new[] { "generated-aid" },
                "[{\"Aid\":\"generated-aid\",\"Role\":\"" + role + "\"}]"), "saved role activates Rogue context: " + role);
        Check(Selects(new[] { "generated-aid" },
            "{\"data\":[{\"aid\":\"generated-aid\",\"role\":\"bossKnight\"}]}"), "wrapped camel-case roster metadata is supported");
        await Reject(() => Task.Run(() => Selects(new[] { "different-aid" },
            "[{\"Aid\":\"generated-aid\",\"Role\":\"bossKnight\"}]")), "missing selected account cannot be treated as a resolved empty raid");
        Check(pitTeam.GoingRogue.RogueRosterSelection.IsRole("bossknight") &&
            !pitTeam.GoingRogue.RogueRosterSelection.IsRole("bossKnightFollower"), "native role casing is accepted without broad boss matching");
        Check(!Selects(new[] { "generated-aid" },
            "[{\"Aid\":\"generated-aid\",\"Role\":\"pmcUSEC\"}]"), "ordinary PMC does not activate Rogue context");
        Check(Selects(new[] { "generated-aid" },
            "[{\"Aid\":\"generated-aid\",\"Role\":\"bossKnight\"}]", goonsOnly: true), "saved Knight activates hostile-Goons blocking");
        Check(!Selects(new[] { "generated-aid" },
            "[{\"Aid\":\"generated-aid\",\"Role\":\"exUsec\"}]", goonsOnly: true), "exUsec alone does not block Goons waves");
        var directory = Path.GetFullPath(Path.Combine("tests", "artifacts", "gameplay-modes", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "test.cfg");
        var config = Bind(path);
        Check(!pitFireTeam.restrictedGearMaintenance.Value, "Field Upkeep defaults off");
        pitFireTeam.restrictedGearMaintenance.Value = true;
        var custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GoingRogue), "Going Rogue requires its addon");
        var modeSpecificKeys = new HashSet<string> { "BadGuy", "Friendly", "Tracking", "Pickup", "Tiered", "Maximum", "Recruit", "TeamEscape", "TeamEscapeUseAnyExtract" };
        var original = config.Where(pair => pair.Key.Section == "Game" && modeSpecificKeys.Contains(pair.Key.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.GetSerializedValue());
        Check(pitFireTeam.friendlyEncounterChance.Value == 30 && !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyEncounterChance), "friendly chance defaults to 30 and is editable in Guns for Hire");
        Check(pitFireTeam.friendlyScavEncounters.Value && GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyScavEncounters), "Scav encounters default on but the toggle is unavailable in Guns for Hire");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        string beforeRogue = File.ReadAllText(path + ".guns-for-hire.json");
        bool roguePresent = false;
        GameplayModeAddons.Register(GameplayMode.GoingRogue, () => roguePresent, () => roguePresent, exUsecFriendly: false,
            gearPolicy: pitTeam.GoingRogue.GoonGearPolicy.ForRole,
            ownsCombat: RogueRosterSelection.IsGoonRole);
        Check(!GameplayModeAddons.OwnsCompanionCombat("bossKnight"), "companion combat ownership is mode scoped");
        Check(GameplayModeAddons.GetCompanionGearPolicy("bossKnight") == null, "Knight's gear restrictions do not affect another mode");
        SPT.Common.Http.RequestHandler.GoingRogueAvailable = false;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.GoingRogue), "Going Rogue requires the server addon too");
        SPT.Common.Http.RequestHandler.GoingRogueAvailable = true;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GoingRogue);
        Check(new[]{"bossKnight","followerBigPipe","followerBirdEye"}.All(GameplayModeAddons.OwnsCompanionCombat),
            "all managed Goons own combat independently of a stale saved SAIN tactic");
        Check(!GameplayModeAddons.OwnsCompanionCombat("exUsec") && !GameplayModeAddons.OwnsCompanionCombat(null),
            "ordinary followers retain their selected combat owner");
        Check(GameplayModeRuntime.IsGoingRogue && GameplayModeRuntime.IsAllegiance && SPT.Common.Http.RequestHandler.Mode == "GoingRogue",
            "Going Rogue selects its own identity with Allegiance policy");
        Check(GameplayModeRuntime.IsLocked(pitFireTeam.maximumPickup) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2,
            "Going Rogue shares Allegiance setting locks");
        Check(File.ReadAllText(path + ".guns-for-hire.json") == beforeRogue, "locked-mode transitions preserve the Guns for Hire snapshot");
        config = Bind(path);
        custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        await GameplayModeRuntime.SynchronizeAsync();
        Check(GameplayModeRuntime.IsGoingRogue && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2,
            "Going Rogue survives config restart with the same settings policy");
        Check(!GameplayModeAddons.IsRogueRaid, "empty Going Rogue raids do not force Bad Guy");
        roguePresent = true;
        Check(GameplayModeAddons.IsRogueRaid && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.badGuy), "rogue presence keeps Allegiance Bad Guy off for Scav encounter diplomacy");
        Check(GameplayModeAddons.HasSelectedBossCompanions && !GameplayModeAddons.IsExUsecFriendlyRaid,
            "Knight blocks hostile Goons while exUsec friendship remains deferred");
        Check(!GameplayModeAddons.IsSpawnCombatReady("knight", "bossKnight"), "missing readiness provider cannot announce Knight");
        Check(GameplayModeAddons.GetCompanionGearPolicy("bossKnight")?.KeepEquipment == true &&
            GameplayModeAddons.GetCompanionGearPolicy("bossKnight").AllowVestPlateUpgrades &&
            !GameplayModeAddons.GetCompanionGearPolicy("bossKnight").AllowBodyContainerLootCommands &&
            GameplayModeAddons.GetCompanionGearPolicy("bossKnight").AllowsSecondaryScore(100, 95),
            "Going Rogue supplies Knight's fixed-kit, plate-upgrade and near-score secondary policy");
        Check(new[] { "followerBigPipe", "followerBirdEye" }.All(role =>
            GameplayModeAddons.GetCompanionGearPolicy(role)?.KeepEquipment == true), "managed Goons share fixed-kit restrictions");
        Check(new[] { "pmcUSEC", "exUsec", null }.All(role =>
            GameplayModeAddons.GetCompanionGearPolicy(role) == null), "Goon gear restriction does not leak to ordinary roles");
        bool claimed = false;
        GameplayModeAddons.RegisterCommandPolicy(GameplayMode.GoingRogue, GoonCommandPolicy.ForRole);
        Check(GameplayModeAddons.EvaluateAdvanceOrder(new CompanionAdvanceOrderContext()) == null, "advance-order arbitration is absent by default");
        GameplayModeAddons.RegisterAdvanceOrderPolicy(GameplayMode.GoingRogue, _ => true);
        Check(GameplayModeAddons.EvaluateAdvanceOrder(new CompanionAdvanceOrderContext()) == true, "registered optional advance-order arbitration is used");
        GameplayModeAddons.RegisterAdvanceOrderPolicy(GameplayMode.GoingRogue, _ => throw new Exception("test arbitration failure"));
        Check(GameplayModeAddons.EvaluateAdvanceOrder(new CompanionAdvanceOrderContext()) == false, "failing optional command arbitration refuses safely");
        GameplayModeAddons.RegisterAdvanceOrderPolicy(GameplayMode.GoingRogue, null);
        foreach (string role in new[] { "bossKnight", "followerBigPipe", "followerBirdEye" })
        {
            Check(GameplayModeAddons.GetCompanionCommandPolicy(role)?.AlwaysAcceptCombatPoint == true,
                role + " accepts valid combat point orders without a pickup roll");
            Check(GameplayModeAddons.GetCompanionCommandPolicy(role)?.OnYourOwnAggression == 70f,
                role + " has the addon-owned 70 percent On Your Own aggression policy");
        }
        var assaultPolicy = GameplayModeAddons.GetCompanionCommandPolicy("bossKnight");
        Check(assaultPolicy.RelativeCombatPower("pmcUSEC") == 1.5f && assaultPolicy.RelativeCombatPower("assault") == 2f,
            "registered assault bosses carry explicit PMC/Scav combat confidence");
        Check(assaultPolicy.RelativeCombatPower("bossSanitar") == 1f && assaultPolicy.RelativeCombatPower("exUsec") == 1f,
            "bosses and elite roles receive no ordinary-target confidence discount");
        Check(GameplayModeAddons.GetCompanionCommandPolicy("followerBirdEye").RelativeCombatPower("assault") == 1f,
            "Birdeye cannot inherit assault confidence");
        Check(assaultPolicy.AllowsPush && GameplayModeAddons.GetCompanionCommandPolicy("followerBigPipe").AllowsPush &&
            !GameplayModeAddons.GetCompanionCommandPolicy("followerBirdEye").AllowsPush,
            "assault Goons allow confidence-based push while Birdeye refuses");
        Check(assaultPolicy.PushAcceptanceChance(200, 100, -5) == 1f &&
            assaultPolicy.PushAcceptanceChance(100, 100, 0) > assaultPolicy.PushAcceptanceChance(50, 100, 0),
            "equipment advantage raises assault-boss confidence");
        Check(assaultPolicy.PushAcceptanceChance(100, 100, 20) < assaultPolicy.PushAcceptanceChance(100, 100, 0),
            "positive enemy threat lowers assault-boss willingness");
        Check(assaultPolicy.PushAcceptanceChance(float.NaN, 100, 0) == 0f &&
            assaultPolicy.PushAcceptanceChance(100, 100, float.PositiveInfinity) == 0f,
            "invalid confidence/threat inputs fail closed");
        Check(GoonCommandPolicy.ForRole("followerBigPipe", false).PushAcceptanceChance(1, 999, 999) == 1f,
            "Big Pipe without Knight always accepts an admitted Go Forward order");
        Check(ReferenceEquals(GoonCommandPolicy.ForRole("followerBigPipe", true), assaultPolicy),
            "Big Pipe with Knight uses Knight's confidence policy");
        Check(ReferenceEquals(GoonCommandPolicy.ForRole("bossKnight", false), assaultPolicy) &&
            !GoonCommandPolicy.ForRole("followerBirdEye", false).AllowsPush &&
            !GoonCommandPolicy.ForRole("followerBirdEye", true).AllowsPush,
            "Knight and sniper Birdeye retain their existing role-specific command rules");
        Check(GameplayModeAddons.GetCompanionCommandPolicy("exUsec") == null &&
            GameplayModeAddons.GetCompanionCommandPolicy("pmcUSEC") == null,
            "boss command policy does not leak to other roles");
        bool released = false;
        GameplayModeAddons.Register(GameplayMode.GoingRogue, () => roguePresent, () => roguePresent,
            resolveSelection: _ => { }, companionReady: (id, role) => claimed,
            abortCompanion: _ => released = true);
        Check(GameplayModeAddons.GetCompanionCommandPolicy("bossKnight") == null,
            "replacing a registration cannot keep an abandoned command policy");
        Check(!GameplayModeAddons.IsSpawnCombatReady("knight", "bossKnight"), "unclaimed Knight remains pending");
        claimed = true;
        Check(GameplayModeAddons.IsSpawnCombatReady("knight", "bossKnight"), "claimed Knight satisfies readiness");
        GameplayModeAddons.AbortSpawn("knight");
        Check(released, "spawn failure releases the addon combat owner");
        SPT.Common.Http.RequestHandler.GoingRogueAvailable = false;
        await GameplayModeRuntime.SynchronizeAsync();
        Check(GameplayModeRuntime.Current == GameplayMode.GunsForHire && !GameplayModeAddons.IsRogueRaid,
            "missing server addon returns to Guns for Hire without retaining Rogue flags");
        SPT.Common.Http.RequestHandler.GoingRogueAvailable = true;
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GoingRogue);
        GameplayModeAddons.Unregister(GameplayMode.GoingRogue);
        await GameplayModeRuntime.SynchronizeAsync();
        Check(GameplayModeRuntime.Current == GameplayMode.GunsForHire && !GameplayModeAddons.IsRogueRaid,
            "missing client addon returns to Guns for Hire without retaining Rogue flags");
        GameplayModeAddons.Register(GameplayMode.GoingRogue, () => roguePresent);
        GameplayModeAddons.RegisterCommandPolicy(GameplayMode.GoingRogue, GoonCommandPolicy.ForRole);
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GoingRogue);
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(GameplayModeAddons.GetCompanionCommandPolicy("bossKnight") == null,
            "registered Goon willingness cannot affect Allegiance");
        Check(!GameplayModeAddons.IsRogueRaid && File.ReadAllText(path + ".guns-for-hire.json") == beforeRogue,
            "leaving Going Rogue releases raid policy and preserves original preferences");
        GameplayModeAddons.Unregister(GameplayMode.GoingRogue);
        Check(GameplayModeAddons.GetCompanionCommandPolicy("bossKnight") == null,
            "unregister removes companion command willingness");
        Check(!GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyEncounterChance), "friendly chance is enabled in Allegiance");
        Check(!GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyScavEncounters), "Scav encounters are editable in Allegiance");
        pitFireTeam.friendlyScavEncounters.Value = false;
        pitFireTeam.friendlyEncounterChance.Value = 45;
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.friendlyEncounterChance) == 45, "Allegiance friendly chance is editable without a locked override");
        Check(SPT.Common.Http.RequestHandler.Mode == "Allegiance", "server mode follows client");
        Check(original.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "entering Allegiance preserves saved mode-specific preferences");
        Check(SPT.Common.Http.RequestHandler.Loadout == "Restricted", "Allegiance synchronization preserves the saved loadout choice");
        Check(SPT.Common.Http.RequestHandler.FieldUpkeep && !GameplayModeRuntime.IsLocked(pitFireTeam.restrictedGearMaintenance),
            "Field Upkeep is shared, editable and synchronized in Allegiance");
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
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Restricted &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.loadoutManagementMode), "loadout management remains editable in Allegiance");
        pitFireTeam.loadoutManagementMode.Value = LoadoutManagementMode.Extreme;
        await GameplayModeRuntime.SynchronizeAsync();
        Check(SPT.Common.Http.RequestHandler.Loadout == "Extreme", "Allegiance synchronizes an edited Realistic loadout choice");
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
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2 && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Restricted, "capacity stays locked while loadout management accepts edits");
        // Simulate Notepad rather than a UI setting-change callback.
        File.WriteAllText(path, File.ReadAllText(path).Replace("Maximum = 6", "Maximum = 9").Replace("Loadout = Restricted", "Loadout = Immersive"));
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
            GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Immersive, "cfg reload preserves escape locks and accepts the edited loadout preference");
        await GameplayModeRuntime.SynchronizeAsync();
        Check(SPT.Common.Http.RequestHandler.Loadout == "Immersive", "reloaded Allegiance loadout choice reaches the server");
        config.Save();
        Check(File.ReadAllText(path).Contains("Maximum = 9"), "saving cfg does not overwrite preferences with the effective policy");
        File.WriteAllText(path, File.ReadAllText(path).Replace("00 GameplayMode = Allegiance", "00 GameplayMode = GunsForHire"));
        config.Reload();
        Check(GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2,
            "cfg reload cannot perform an uncoordinated mode switch");
        Check(!File.ReadAllText(path + ".guns-for-hire.json").Contains("00 GameplayMode"), "snapshot excludes mode");
        Check(!File.ReadAllText(path + ".guns-for-hire.json").Contains("FriendlyEncounterChance"), "snapshot excludes the shared encounter preference");
        Check(!JArray.Parse(File.ReadAllText(path + ".guns-for-hire.json")).Any(value => value["Key"]?.ToString() == "Loadout"), "snapshot excludes shared Loadout Management");
        var savedSnapshot = JArray.Parse(File.ReadAllText(path + ".guns-for-hire.json"));
        Check(savedSnapshot.Count == 9 && savedSnapshot.All(value => value["Section"]?.ToString() == "Game" && modeSpecificKeys.Contains(value["Key"]?.ToString())), "snapshot contains only the nine Allegiance-locked settings");
        Check(original.All(pair => savedSnapshot.Any(value => value["Key"]?.ToString() == pair.Key.Key && value["Value"]?.ToString() == pair.Value)), "snapshot retains the original mode-specific preferences");
        custom.Value = 90;
        pitFireTeam.pmcArmbands.Value = false;
        var shared = config.Where(pair => pair.Key.Key != "00 GameplayMode" && !original.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.GetSerializedValue());
        config = Bind(path); // Real BepInEx reload, with a new ConfigFile and newly bound entries.
        custom = config.Bind("Other", "Untouched", 17);
        GameplayModeRuntime.Initialize(config);
        Check(shared.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "restart retains every edited shared setting");
        Check(GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 2, "restart preserves mode and locks");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.loadoutManagementMode) == LoadoutManagementMode.Immersive &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.loadoutManagementMode), "restart preserves the editable Allegiance loadout choice");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.heatlhMultiplier) == 4 &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.heatlhMultiplier), "restart preserves the unlocked Allegiance health multiplier");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.healKey).MainKey == KeyCode.J &&
            !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.healKey), "restart preserves the usable Allegiance healing binding");
        Check(pitFireTeam.friendlyEncounterChance.Value == 45, "restart preserves Allegiance friendly chance preference");
        Check(!pitFireTeam.friendlyScavEncounters.Value, "restart preserves disabled Scav encounters");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && !GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract), "restart preserves Team Escape locks");
        var legacySnapshot = JArray.Parse(File.ReadAllText(path + ".guns-for-hire.json"));
        foreach (var pair in shared)
            legacySnapshot.Add(JObject.FromObject(new { Section = pair.Key.Section, Key = pair.Key.Key, Value = "invalid-legacy-shared-value" }));
        File.WriteAllText(path + ".guns-for-hire.json", legacySnapshot.ToString());
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(pitFireTeam.restrictedGearMaintenance.Value && SPT.Common.Http.RequestHandler.FieldUpkeep,
            "Field Upkeep survives mode changes as one shared preference");
        Check(pitFireTeam.loadoutManagementMode.Value == LoadoutManagementMode.Immersive && SPT.Common.Http.RequestHandler.Loadout == "Immersive", "returning to Guns for Hire ignores legacy shared snapshot values and retains the current loadout choice");
        Check(pitFireTeam.friendlyEncounterChance.Value == 45 && !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyEncounterChance), "returning to Guns for Hire retains and enables the shared encounter preference");
        Check(!pitFireTeam.friendlyScavEncounters.Value && GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyScavEncounters), "Guns for Hire retains the disabled Scav preference while making its control unavailable");
        Check(original.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "restart restores every original mode-specific setting");
        Check(shared.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "returning to Guns for Hire ignores all legacy shared entries and preserves current shared settings");
        pitFireTeam.restrictedGearMaintenance.Value = false;
        await GameplayModeRuntime.SynchronizeAsync();
        Check(!SPT.Common.Http.RequestHandler.FieldUpkeep, "disabled Field Upkeep reaches the server");
        Check(!GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscape) && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.teamEscapeUseAnyExtract), "Guns for Hire restores both Team Escape preferences");
        pitFireTeam.maximumPickup.Value = 5;
        pitFireTeam.loadoutManagementMode.Value = LoadoutManagementMode.Extreme;
        pitFireTeam.heatlhMultiplier.Value = 5;
        pitFireTeam.healKey.Value = new KeyboardShortcut(KeyCode.H);
        pitFireTeam.pmcArmbands.Value = true;
        custom.Value = 91;
        shared = config.Where(pair => shared.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value.GetSerializedValue());
        await GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance);
        Check(shared.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "shared changes in Guns for Hire carry into Allegiance");
        Check(pitFireTeam.loadoutManagementMode.Value == LoadoutManagementMode.Extreme && SPT.Common.Http.RequestHandler.Loadout == "Extreme", "Allegiance receives the shared Realistic choice");
        Check(pitFireTeam.friendlyEncounterChance.Value == 45 && !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyEncounterChance), "returning to Allegiance retains and enables friendly chance preference");
        Check(!pitFireTeam.friendlyScavEncounters.Value && !GameplayModeRuntime.IsSettingUnavailableInCurrentMode(pitFireTeam.friendlyScavEncounters), "returning to Allegiance preserves disabled Scav encounters and enables their control");
        await GameplayModeRuntime.ChangeAsync(GameplayMode.GunsForHire);
        Check(pitFireTeam.loadoutManagementMode.Value == LoadoutManagementMode.Extreme && SPT.Common.Http.RequestHandler.Loadout == "Extreme", "mode cycling preserves the shared Realistic choice");
        Check(GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 5, "second cycle snapshots latest Guns for Hire values");
        Check(shared.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "mode cycling retains every shared setting");
        SPT.Common.Http.RequestHandler.FailBeforeCommit = true;
        await Reject(() => GameplayModeRuntime.ChangeAsync(GameplayMode.Allegiance), "server failure should reject switch");
        Check(!GameplayModeRuntime.IsAllegiance && GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup) == 5, "failed switch restores local settings");
        Check(shared.All(pair => config[pair.Key].GetSerializedValue() == pair.Value), "failed switch leaves shared settings untouched");
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
        pitFireTeam.restrictedGearMaintenance = c.Bind("Game", "FieldUpkeep", false);
        pitFireTeam.healKey = c.Bind("Game", "Heal", new KeyboardShortcut(KeyCode.H));
        pitFireTeam.heatlhMultiplier = c.Bind("Game", "Health", 3);
        pitFireTeam.friendlyEncounterChance = c.Bind("Game", "FriendlyEncounterChance", 30, new ConfigDescription("", new AcceptableValueRange<int>(5,80)));
        pitFireTeam.friendlyScavEncounters = c.Bind("Game", "FriendlyScavEncounters", true);
        pitFireTeam.pmcArmbands = c.Bind("Game", "Armbands", true);
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
        public static ConfigEntry<bool> badGuy, pitFireTeamFLAG, pickupEnabled, tieredPickup, recruitPickup, pmcArmbands;
        public static ConfigEntry<bool> teamEscape, teamEscapeUseAnyExtract, friendlyScavEncounters, restrictedGearMaintenance;
        public static ConfigEntry<int> maximumPickup, heatlhMultiplier, friendlyEncounterChance;
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
namespace pitTeam.Utils { internal static class SpawnHelper { public static readonly List<int> spawnMemberIdsBoss = new(); } }
namespace pitTeam.Components { public static class SquadControlMenuUi { public static bool InRaid; public static bool IsGameRaidActive() => InRaid; } }
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static string Mode = "GunsForHire";
        public static string Loadout;
        public static bool FieldUpkeep;
        public static bool FailBeforeCommit, LoseResponse;
        public static bool GoingRogueAvailable = true;
        public static string PostJson(string url, string body)
        {
            if (FailBeforeCommit) throw new IOException("Simulated server failure");
            Mode = JObject.Parse(body)["gameplayMode"].ToString();
            Loadout = JObject.Parse(body)["loadoutManagementMode"].ToString();
            FieldUpkeep = JObject.Parse(body)["restrictedGearMaintenance"].Value<bool>();
            if (LoseResponse) throw new IOException("Simulated lost response");
            return "{\"err\":0,\"data\":null}";
        }
        public static string GetJson(string url) => "{\"gameplayMode\":\"" + Mode + "\",\"availableModes\":[\"GunsForHire\",\"Allegiance\"" + (GoingRogueAvailable ? ",\"GoingRogue\"" : "") + "]}";
    }
}
