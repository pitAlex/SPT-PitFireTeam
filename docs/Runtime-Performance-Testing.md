# Follower Runtime Performance Testing

**Status:** first recorder hot-path optimizations implemented; live A/B validation pending

**Target:** SPT 4.1.3, pitFireTeam `0.10.1`

**Captured:** 2026-08-30

This document preserves the repeatable settings, evidence, and next test order for diagnosing frame-time impact with multiple followers. It concerns runtime cost and frame pacing. Shooting proficiency settings are documented separately in `docs/Friendly-AI-Performance-Settings.md`.

## Current live test baseline

| Variable | Captured value |
|---|---|
| Map | Customs (`bigmap`) |
| Followers | 2 (`Brick` and `Medved`) |
| pitFireTeam build | Debug DLL |
| External SAIN plugin | Installed |
| pitFireTeam SAIN addon | Not installed |
| Follower combat ownership | pitFireTeam core combat; external SAIN calculations still apply where SAIN patches EFT calculations |
| `27 BattleRecorder` | `true` |
| `28 BattleRecorderSnapshotIntervalMs` | `200` ms |
| `03 EnemyRemember` | `60` seconds, the maximum exposed value |

The live configuration also contains `29 VanillaCombatTestMode = true`, but the current source does not bind or read that key. Treat it as stale configuration text, not an active test setting.

The complete record used for this checkpoint was `20260830-005753-bigmap.jsonl`:

- size: `41.70 MiB`
- events: `11,665`
- recorded event-time span: `2,076.07` seconds
- whole-raid average: `5.62` events/second and `20.57 KiB/second`
- follower event counts: `6,252` for Brick and `5,411` for Medved

The largest event groups were:

| Event | Count | Serialized size |
|---|---:|---:|
| `snapshot` | 4,917 | 25.86 MiB |
| `goalEnemyTransition` | 3,772 | 10.82 MiB |
| `combatFire` | 1,159 | 2.46 MiB |
| `commitmentEvent` | 401 | 0.65 MiB |

Of the `goalEnemyTransition` events, `3,316` were `memoryOnlyGoalEnemyBlocked` and `261` were `retentionBlockedSet:notVisibleOrShootable`. This volume matters to recorder cost even though the rejected target setters did not themselves churn combat actions.

## Current conclusion

The Battle Recorder is the largest avoidable suspect during combat and active commands, but it does not explain every possible slowdown during ordinary follow. The first implementation pass now coalesces burst-identical goal-enemy setter outcomes and keeps the recorder's manual-update callback off non-followers; the captured baseline above predates both changes.

The record's average disk rate is modest. The more important risk is main-thread work and short spikes:

- each event constructs a payload and synchronously runs `JsonConvert.SerializeObject(...)` plus `StreamWriter.WriteLine(...)`;
- output is flushed after 64 events or one second;
- at `200` ms, each actively recorded follower can request five full snapshots per second;
- a weapon-state probe runs every `100` ms per follower, even though it writes only when the state changes;
- before schema 9, `BotOwnerUpdateHub` invoked the recorder from every active bot's manual update before the recorder rejected non-followers; the recorder now uses a follower-only subscription and ordinary bots do not enter its callback;
- full snapshots collect movement, enemy, medical, weapon, cover, boss, commitment, and proficiency state, including some ray/overlap-backed combat diagnostics.

A prior live system sample did not show machine-wide exhaustion: total CPU was about `25%`, the game averaged about `2.66` logical cores, its hottest sampled thread used about `76%` of one core, available RAM was about `32.9 GiB`, and paging was low. That does not rule out Unity main-thread frame-time spikes.

## Ranked areas to isolate

1. **Battle Recorder during combat or commands.** This is the first A/B test because it is optional, synchronous, and currently produces large follower snapshots and repeated target-transition events.
2. **Expected per-follower game and external-SAIN work.** A second follower owns another EFT bot, pitFireTeam decision stack, and external-SAIN calculation state. This is a real baseline cost, not automatically a mod bug.
3. **Follow settle-cover scans.** `FollowAction.TryFindExpandedCoverPoint(...)` requests cover within an `80 m` search radius, filters the candidates, and sorts valid results. During the move strategy a failed search can repeat every three seconds, independently per follower.
4. **Combat cover evaluation.** `FollowerCombatCommon.RefreshShootCover()` refreshes after `0.6` seconds while unstable or `1.2` seconds while stable. Candidate reuse is follower-local, so two followers can perform separate cover and ray checks.
5. **Event-driven awareness.** Player bullet impacts and hostile footsteps iterate the follower collection. Their cost grows with follower count and with the amount of gunfire and movement in a fight.
6. **Two-follower enemy sharing.** `AIBossPlayer.ReportEnemyToIdleFollowers()` becomes active only when at least two followers exist and runs from the boss-group update interval. Its current collection work is a smaller suspect, but it is unique to the two-follower case.

Items 3-6 are code-review suspects, not measured causes. Instrument elapsed time and invocation counts before changing their behavior; cover and awareness optimizations can easily introduce tactical regressions.

## Controlled A/B test order

Change one variable at a time and keep the map, route, graphics, follower profiles, equipment, SAIN preset, and command sequence as similar as practical.

### A. Recorder on versus off

1. Start with two followers, external SAIN installed, addon absent, `BattleRecorder = true`, and interval `200` ms.
2. Exercise both ordinary follow and a sustained fight.
3. In the same raid, set `BattleRecorder = false` and repeat comparable movement and combat for several minutes.
4. Repeat the route in the next raid with the recorder disabled from raid start.

Disabling the recorder immediately closes the current record and unregisters its global bot-update callback; no restart is required. Re-enabling it during that same raid does not start a new writer, so turn it back on before the next raid when another record is needed.

Merely changing the snapshot interval to `1000` ms is not a clean off-test. It reduces snapshots, but transition events and the global update subscription remain active.

### B. Follower scaling

With the recorder disabled, compare the same route and encounter using:

1. one follower;
2. two followers.

If the impact appears only with two followers, capture whether it happens during continuous follow, sector changes, combat acquisition, or heavy gunfire. Those moments distinguish settle-cover scans, combat-cover scans, and awareness fan-out.

### C. External SAIN isolation

Only after A and B, compare the same two-follower test with and without the external SAIN plugin. Do not change the SAIN addon state during this comparison. External SAIN is currently an independent variable because it remains active for low-level follower calculations even though pitFireTeam owns the combat decisions.

## Recommended test presets

| Purpose | Recorder | Snapshot interval | Notes |
|---|---:|---:|---|
| Normal gameplay / frame-time baseline | Off | Irrelevant | Cleanest performance baseline |
| Short tactical bug reproduction | On | `200` ms | Current high-detail recorder setup |
| Long lower-detail diagnostic raid | On | `1000` ms | Reduces snapshots but not transition logging or the update callback |

Keep `EnemyRemember = 60` unchanged during the first recorder A/B so the test changes only recorder ownership. Test retention separately only if later evidence links retained-enemy activity to frame-time spikes.

## Engineering follow-up if the recorder is confirmed

Use this order:

1. **Implemented:** coalesce burst-identical `goalEnemyTransition` outcomes in one-second windows, preserving the first full event and emitting `goalEnemyTransitionRepeat` with the suppressed repeat count and lightweight state; changes in target, source/reason, visibility/shootability, combat state, memory pressure, objective, or decision start a new full event immediately;
2. add a deliberately lightweight snapshot mode for longer raids;
3. measure recorder capture, serialization, and flush time separately;
4. copy Unity-owned values into plain data on the main thread, then queue only JSON serialization and file writing off-thread;
5. never read `BotOwner`, Unity transforms, physics, NavMesh, or other Unity/EFT objects from the writer thread;
6. only after recorder measurements, add passive timings around follow and combat cover scans before considering shared caching or staggered scheduling.

The goal is to remove diagnostic overhead without weakening the recorder evidence used to diagnose tactical regressions.

## Source anchors

- `client/Modules/BattleRecorder.cs`: activation, snapshot gating, payload construction, synchronous writing, and update-hub registration
- `client/Modules/BotOwnerUpdateHub.cs` and `client/Patches/BotOwnerPatch.cs`: split general/follower callback paths
- `client/BigBrain/Actions/FollowAction.cs`: sector settle-cover selection
- `client/BigBrain/FollowerCombatCommon.cs`: shoot-cover refresh and per-cycle candidate reuse
- `client/Patches/BulletImpactPatch.cs` and `client/Patches/HearingSensorPatch.cs`: event-driven follower awareness fan-out
- `client/Components/AIBossPlayer.cs`: two-or-more-follower enemy sharing
