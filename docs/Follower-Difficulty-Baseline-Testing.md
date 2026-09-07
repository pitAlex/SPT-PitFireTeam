# Follower Difficulty Baseline: Two-Phase Test Plan

Investigated: 2026-09-06. Target: pitFireTeam 0.10.2, SPT 4.1.3, SAIN 4.5.0.

Status: Phase 1 saved-data reset completed and verified; gameplay testing pending. Phase 2 is deferred until the baseline tests are reviewed. No recruitment, runtime difficulty, DLL, or deployment changes are part of Phase 1.

This supplements [Friendly AI Performance Settings](Friendly-AI-Performance-Settings.md). Keep combat decision ownership separate from execution proficiency. The external SAIN compatibility discussed here belongs to the core plugin and applies even when the optional SAIN addon is absent. Machine-local installations and reference checkout roots are recorded in `LOCAL.md`.

## Confirmed findings

### Saved difficulty was not a consistent baseline

The four saved teammates had different `Info.Settings.BotDifficulty` values before this reset:

| Teammate | Account ID | Previous difficulty | Vision | Precision | Reaction | Derived AimSpeed | Preserved tactic / aggression |
|---|---|---|---|---|---|---|---|
| Marks | 1335720 | normal | 120 | 121 | 120 | 120.5 | Marksman / 40 |
| Medved | 1339416 | easy | 100 | 130 | 140 | 135 | Rifleman / 50 |
| Brick | 1862185 | impossible | 100 | 130 | 140 | 135 | Rifleman / 50 |
| Nux | 1878221 | hard | 100 | 100 | 100 | 100 | Rifleman / 50 |

This is not inferred from combat performance. The saved JSON contains these tiers. In the client log for `20260906-060339-RezervBase`, initialization at game time 146.5312 explicitly reports:

- Marks: SAIN Default, role pmcUSEC, difficulty normal, profile multiplier 1.
- Brick: SAIN Default, role pmcUSEC, difficulty impossible, profile multiplier 1.75.

Nux and Medved are verified from their saved files, not a claimed spawn in that recording.

`Default` identifies the SAIN preset bundle; it does not mean every bot uses the hard tier. In SAIN's preset-generation enum the Default bundle itself is named `hard`, but that bundle still contains easy, normal, hard, and impossible per-role settings.

### Where the mismatch enters

- Core/vanilla: `FollowerProficiencyValues.cs` sets `Vanilla.TemplateDifficulty = BotDifficulty.hard`; `BotFollowerPlayer.SetFollowerSettings` uses that template difficulty.
- Core-owned external-SAIN adapter: `FollowerSainProficiency.TryResolveFollowerValues` instead reads the live profile's inherited `Info.Settings.BotDifficulty`. It selects the Default bundle's corresponding role/tier, copies follower-local Difficulty/Core/Aiming/Shoot/Mind/Move values, resolves runtime coefficients, and reapplies tactic overrides through `ReplaceSainOverrides`.
- The same adapter uses the inherited tier in `TryApplyDefaultDifficulty` when choosing the selected-preset settings to clone. Its global hard seed therefore does not enforce hard on every follower.
- `GetProfileDifficultyMultiplier` maps easy/normal/hard/impossible to 0.5/1/1.5/1.75 before the role-group multiplier. This is not a literal aim-time or recognition-speed multiplier.

Server creation paths in `server/Services/FriendlyTeammateService.cs`:

- `CreateTeammate` (Add) already requests a hard generated profile.
- `CreateTeammateFromRecruitCandidate` (Invite/recruit pickup) uses the captured bot profile when available. Only its fallback generation explicitly requests hard. The captured-profile path does not normalize difficulty before saving.
- `PrepareTeammateForFetch` clones the saved profile for spawning but does not correct the saved tier.

The user reports most of these teammates came through Invite and suspects Acid Bot's Placement System randomized the original spawned profiles. The retained tier is proven; which external component originally assigned each tier is not independently established. The persistence gap exists regardless of which spawner supplied it.

### Does bot difficulty affect reaction and aiming time?

It can, but not as a universal, guaranteed speed increase for every higher tier.

1. SAIN's aim-time replacement consumes `CurrentAccuratySpeed`, angle/distance curves, its per-bot Aiming settings, movement/cover/panic, CQB settings, equipment/target effects, and min/max clamps.
2. Recognition is a separate pipeline. EFT `EnemyInfo.GetVisibilityChangeSpeedK` uses `RuntimeVisionEffectsK`; our SAIN adapter derives that runtime coefficient from `Difficulty.GainSightCoef` sources. SAIN then applies its environmental/target visibility modifier. Whether a tier switch changes recognition depends on the actual resolved values, not the label alone.
3. SAIN Default's `ApplyBase` disables `FasterCQBReactions` for easy and enables it for normal/hard/impossible. Its ordinary normal/hard/impossible CQB distance and minimum defaults are otherwise shared (30 m / 0.33 s before follower tactic overrides and the selected-preset global gate).
4. The current installed base pmcUSEC database has identical normal/hard/impossible `Core.AccuratySpeed` (0.2) and `Aiming.MAX_AIM_TIME` (1.5). This is a disk database comparison, not a dump of the server's post-mod in-memory generated bundle. It does not prove that every runtime modifier is identical.
5. Default does differ by tier in other inputs, including PMC vision distance/angle, strafe speed, and the profile difficulty multiplier. Our own class/proficiency/target policies can override some template differences; in particular, do not attribute follower head preference to SAIN's tier table because core owns that preference.
6. SAIN `BotWeaponInfoClass.calculateShootModifier` explicitly includes the profile difficulty multiplier alongside weapon class, ammunition, ergonomics, recoil, and weapon proficiency. Difficulty is therefore a real weapon-handling input even when some direct aim-time coefficients are equal.

Consequently, do not claim that Brick's impossible tier alone explains a particular aim time or every kill. His Precision 130 and Reaction 140 are separate, confirmed enhancements: their derived aim-speed factor is 1.35, and the final regular-firearm aim time is divided by that factor (subject to the safety clamp). Precision also supplies the inverse 1.3 final recoil scaling. His modified weapon and ammunition remain additional factors.

The 1.75 profile multiplier is not evidence of 75% faster target recognition. Likewise, hard-to-normal is not guaranteed to slow aim/recognition unless the corresponding effective values actually change.

### Can difficulty change without saving?

A follower-local runtime baseline override is feasible in principle, without rewriting the saved teammate. It is not currently a supported, verified live toggle.

Merely changing `bot.Profile.Info.Settings.BotDifficulty` is insufficient:

- `FollowerSainProficiency.ApplyToFollower` caches a `FollowerState` by profile ID and resolves the Default values when creating that state. Reapplying an existing state does not automatically rerun that resolution.
- The role/tier settings, follower-local categories, runtime modifiers, profile multiplier/square-root, and weapon-derived cached values must be refreshed coherently.
- `BotSettingsInGameModif` must be dismissed before replacement. Mutating an applied modifier breaks the inverse restoration operation.
- Vanilla settings copies may share category objects; any runtime template change must preserve follower-local ownership rather than mutate a shared hard/normal template.

The safe design direction is an explicit effective runtime tier separate from the saved tier, not an in-place enum edit followed by an assumption that SAIN noticed it. Same-spawn live switching needs a cache/lifecycle audit and testing before it can be promised. A spawn-time override is the simpler first boundary; whether a live UI switch is needed remains a Phase 2 decision.

## User observations and hypotheses to retain

- Historical tuning was conducted against inconsistent inherited difficulty and non-neutral proficiency. Re-test performance on a known baseline before deciding further balance changes.
- This does not automatically invalidate recorder-proven movement, reload, healing, cover, or objective-handoff fixes; those failures were independently observable.
- Hard + 100% may be too powerful against SAIN Less Difficult or Baby Bots opponents. The user wants normal available at runtime if testing supports it.
- The concern that lowering proficiency cannot compensate sufficiently is a hypothesis, not a demonstrated limitation. Existing controls have substantial range (runtime factor floor 0.05 and final aim safety cap 15 seconds), but they do not cover every tier-dependent behavior.
- No automatic mapping from a selected SAIN preset to a follower tier has been approved. Do not implement one merely because it sounds convenient.
- Preserve the player's configured weapon, ammunition, skills, tactic, and aggression when comparing the neutral baseline. Neutral proficiency means neutral relative to that tactic, not identical stats across Marksman and Rifleman.

## Phase 1: Saved-data reset and baseline testing

### Completed preparation

- Backed up all four numeric teammate profiles and their four companion settings files, with source/copy SHA-256 verification.
- Set every saved profile to hard.
- Set all authoritative proficiency controls to 100: `VisionDistance` (Vision), `Accuracy` (Precision), and `VisionSpeed` (Reaction).
- Reset the persisted derived `AimSpeed` field to 100 as well.
- Marks, Medved, and Brick required edits. Nux already matched and was left untouched.
- Preserved equipment, inventory, progression, skills, tactic, aggression, AutoJoin, loadout selection, appearance, and all other JSON content.
- Verified all eight JSON files parse and differ from their backups only in the authorized values plus a final newline on edited files. The four equipment snapshots and recruitment-request file retained their exact hashes.
- No gameplay source changes, build, deploy, or release-note entry: this is local test-data preparation, not a shipped fix.

Local rollback locator: the live server mod's `Backups/20260906-064246-hard-baseline/` contains the originals for session `6a875d350ecfc1eccbd6fad0`. The active files remain under `Resources/teammates/<sessionId>/`. Resolve the server mod root through `LOCAL.md`. Restore only with the game/server closed, and do not overwrite later raid progression indiscriminately; after new tests, restore only the original difficulty/proficiency fields if rollback is desired.

The previous raid ended before this edit. The game and server were still running and were not stopped. Neither needs restarting for these saved-data edits: start a fresh raid from the menu. `BotsControllerPatch.FetchMemberProfile` requests `/client/game/bot/followergenerate`, while spawning separately requests `/client/game/bot/followerdetails`; server `LoadTeammates` and `GetTeammateSettings` deserialize the files on those paths. Raid cleanup clears the profile-creation task cache. Exceptions: profiles already prefetched for a loading/current raid are not retuned, and map transit can reuse a carried profile through `FollowerTransitStateCache` instead of fetching it. Do not submit stale proficiency values from an already-open profile UI.

### Test procedure

1. Start a fresh raid from the menu (not a map transit); no client/server restart is required. Reopen the profile UI if necessary to verify the three controls are 100 for all four teammates, and enable Battle Recorder before the raid.
2. Check initialization logs for the correct follower IDs and `difficulty=hard`. Compare recorded configured percentages and factors (100 / 1.0). Retain tactic-specific differences; Marks is still a Marksman.
3. First test the current SAIN opponent preset unchanged. Record its exact name, active addon state, follower tactic/aggression, gear/ammo, range, movement, visibility/weather, and enemy class/armor when known.
4. Collect repeated close-, medium-, and long-range engagements, separating stationary from moving shots. Do not conclude from a single rapid kill.
5. Measure what the available telemetry supports: enemy acquisition, goal selection, aim plans before/after modifiers, shots/ammo use, and actual kill attribution. Mark unrecorded LOS/hit timestamps as unavailable rather than inferring them.
6. If hard + 100 still appears excessive, collect an explicitly labeled easier-opponent-preset sample while keeping follower proficiency/gear unchanged. This is not yet a hard-versus-normal follower comparison; Phase 1 does not add the runtime override.
7. Review findings with the user before beginning Phase 2 or choosing balance coefficients.

Authoritative kills already exist in `SquadRaidKillReport`: the `PlayerPatch` death callback records copied EFT `VictimStats` with killer attribution, victim, weapon, body part, distance, time, and location for the results screen. The current cache is in memory and is not exported as explicit Battle Recorder kill events. Use the displayed kill report for corroboration; ammunition drops and enemy removal are not proof of killing blows or exact time-to-kill.

### Findings ledger

| Test | Status | Evidence / conclusion |
|---|---|---|
| Existing profile and settings audit | Complete | Four different tiers; three followers had boosted proficiency |
| Hard + 100 saved-data reset | Complete | All four verified; backups retained; unrelated data unchanged |
| Fresh-raid hard initialization and neutral factors | Pending user test | No post-reset raid checked yet |
| Neutral Rifleman and Marksman combat samples | Pending user test | Capture runtime/log/results evidence |
| Hard + 100 against easier SAIN opponents, if needed | Pending user test | Keep separate from initial constant-preset test |
| Balance decision and approval to start Phase 2 | Pending review | Do not assume normal is sufficient or necessary |

Phase 1 ends with reviewed evidence about the clean baseline, not with a guessed tuning value.

## Phase 2: Normalize new teammates and implement the approved runtime baseline

Deferred work, not implemented by this document.

### Persistent creation invariant

- Centralize hard normalization after either generated or captured profile acquisition and before saving a newly added teammate.
- Keep Add's explicit hard generation request, but validate/normalize the returned result as well; a request is not a postcondition when other mods can participate.
- Apply the same invariant to Invite's captured-profile and fallback-generation paths.
- Preserve equipment, identity, progression, skills, and user proficiency. Difficulty normalization is not a profile regeneration or another proficiency reset.
- Decide how to migrate existing saved teammates for other users; the four local edits are not a shipped migration.
- Separately decide the scope for temporary in-raid recruits that are never saved.

### Runtime baseline design and constraints

- Use a core-owned, follower-local effective tier, with hard as the intended saved/default baseline and normal as the candidate alternative to evaluate.
- Keep saved tier, effective tier, selected SAIN preset, and user percentages distinct. Do not rewrite the saved profile to apply a temporary runtime choice.
- Reuse one effective-tier resolver across all relevant adapter lookups and state/cache invalidation. Avoid a mixture of normal normalized settings and cached hard coefficients.
- Rebuild baseline values, reapply tactic values and the user's modifiers once, and update derived SAIN/profile/weapon state through verified lifecycle boundaries.
- Preserve selected-preset policy and ordinary enemy bots. Never mutate shared SAIN preset objects or use the optional addon to own this compatibility behavior.
- Audit the vanilla/no-SAIN path explicitly: it currently selects hard independently, so an SAIN-only override must not be advertised as a universal baseline switch.
- Start with a safe spawn-time boundary unless same-spawn switching is required and verified. For live switching, preserve current combat objective/action, medical/reload work, and weapon commitment; do not reset the brain just to change proficiency.
- Do not promise slower recognition/aim from hard-to-normal where effective coefficients are identical. Use Phase 1 measurements to decide whether the effective baseline also needs deliberately calibrated performance values.
- Decide UI/config scope and persistence of the *preference* with the user. Saving a preference is separate from changing teammate profile difficulty.

### Diagnostics and acceptance

- Log saved tier, effective tier, baseline bundle, tactic, configured percentages, and effective runtime coefficients.
- If kill telemetry is needed, export from the existing accepted `SquadRaidKillReport` record boundary. Do not introduce a parallel kill tracker or infer kills from ammo/target disappearance.
- Test Add, captured Invite, and fallback Invite against incoming easy/normal/hard/impossible profiles.
- Test hard -> normal -> hard for the approved runtime boundary: no stacked modifiers, no shared-setting leakage, unchanged disk profiles, and coherent aim/vision/weapon-derived state.
- Verify other followers and non-followers remain unaffected by a follower-local override.
- Verify core without SAIN, core with SAIN and no addon, and SAIN addon combat separately.
- Re-test decision/movement stability and restore/dismiss behavior. Runtime difficulty must not recreate action or weapon-switch churn.
- Approve balance based on measured results, including the easier-opponent cases, before shipping.

## Source anchors

Repository:

- `client/Modules/FollowerSainProficiency.cs`: `ApplyToFollower`, `TryResolveFollowerValues`, `TryApplyDefaultDifficulty`, `ResolveRuntimeDifficulty`, `GetProfileDifficultyMultiplier`.
- `client/Modules/FollowerProficiencyValues.cs`: hard vanilla template, tactic baselines, percentage mapping, derived aim speed.
- `client/Components/BotFollowerPlayer.cs`: `SetFollowerSettings`, template and immutable modifier application.
- `client/Patches/FollowerAimTimeProficiencyPatch.cs`: authoritative final aim-time scaling.
- `server/Services/FriendlyTeammateService.cs`: `CreateTeammate`, `CreateTeammateFromRecruitCandidate`, `PrepareTeammateForFetch`, `LoadTeammates`, `GetTeammateSettings`.
- `client/Modules/SquadRaidKillReport.cs`, `client/Patches/PlayerPatch.cs`: authoritative cached kill attribution.

External SPT 4.1.3 / SAIN 4.5.0 references (resolve local roots through `LOCAL.md`):

- `SAINServerMod/Generators/PresetGenerationService.cs`: Default preset identification and per-tier source generation.
- `SAINServerMod/Extensions/PresetTunerExtensions.cs`: `ApplyBase`; do not confuse it with `ApplyHarderPMCs`.
- `SAIN.Preset.Shared/BotSettings/SAINSettings/Categories/SAINAimingSettings.cs`: ordinary CQB defaults and copied aim settings.
- `SAIN/Patches/Shoot/AimDataPatches.cs`: `AimTimePatch.CalculateAim` and `CalcFasterCQB`.
- `SAIN/Patches/VisionPatches.cs` and `SAIN/Classes/Bot/EnemyClasses/Vision/EnemyGainSightClass.cs`: recognition modifiers.
- `SAIN/Classes/Bot/Info/BotWeaponInfoClass.cs`: cached weapon calculation and profile difficulty input.
- EFT decompile `Assembly-CSharp/EnemyInfo.cs`: visibility accumulation coefficient.
