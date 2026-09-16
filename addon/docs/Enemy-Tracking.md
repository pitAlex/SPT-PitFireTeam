# SAINGrunt enemy tracking proposal

**Not implemented.** This is an addon adaptation of the [shared Enemy Tracking proposal](../../docs/Enemy-Tracking.md), which owns mode definitions, default/migration decisions, evidence/expiry semantics, UI and core behavior. Implement that base first. The addon keeps its combat brain in both modes; Simple must not switch it to Rifleman.

## How native SAIN tracks enemies

### Stored information and real position are different

`Enemy.EnemyPosition` reads the live transform. `Enemy.LastKnownPosition` instead reads `KnownPlaces.LastKnownPosition`, returning `LastKnownPlace.Position`, stored in `EnemyPlace._position`.

`EnemyKnownPlaces` keeps personal seen/heard and squad seen/heard places, an `AllEnemyPlaces` collection, `LastKnownPlace`, and `TimeLastKnownUpdated`. A report updates/creates the appropriate place, then `SetLastKnown` selects it, updates the time, resets `SearchedAllKnownLocations`, and publishes `LastKnownUpdated`. That event makes the enemy known again. Merely reading the place does none of this.

`EnemyPlace.UpdatePosition` also resets personal/squad arrival and seen-place flags. It is a state-changing observation update, not a harmless position setter.

### Sight, sound and shared reports

- `EnemyVisionClass.UpdateVisibleState` updates sight time and the seen place while visible. On losing visibility it makes a final last-seen-position update. That sight branch does not subsequently follow hidden movement.
- `HearingSensor.ReactToHeardSound` checks hearing and gunshot-chase conditions, then asks `HearingDispersion` for an estimated position. `Squad.AddPointToSearch` passes it through an `SAINHearingReport`; `EnemyHearing.SetHeard` / `UpdateHeardPosition` update the heard place. Hearing is not a visual report.
- Dispersion depends on sound type, distance, relative direction and prior knowledge, with a maximum clamp and reachable-point sampling. Sounds close to the old known location can reuse that point. Replicating hearing uncertainty in core needs its own sensor adapter; an arbitrary fixed random radius would not be native parity.
- `Squad.ReportEnemyPosition` uses a coordination-dependent transmission chance: `25 + 15 * clamp(coordination, 1, 5)` percent per recipient. A recipient needs an active matching enemy record. It receives seen/heard place data, not personal shooting permission. Current-sight broadcasts are throttled to 0.25 seconds in `UpdateCurrentEnemyPos`; heard broadcasts to one second.

Native Realistic is a gameplay approximation, not a claim that SAIN never consults real coordinates. Hearing dispersion uses distance from the old point to the actual enemy, and `Squad.AddPlaceForCheck` sets report height from current enemy height. Reproduce the intended tracking behavior without presenting every native calculation as physically realistic.

### Searching the remembered place

`SAINEnemyPath.CheckCalcPath` calculates a path to `KnownPlaces.LastKnownPosition`. Recalculation checks known state, a usable position, changed positions and its existing delay. Search stores an `EnemyPlace` as `SearchPathFinder.TargetPlace`.

`SearchDecider.WantToSearch` rejects no enemy, no last-known place, or a place already reached personally/by the squad. It also applies personality, including `WillSearchFromAudio`, and other native combat/search gates. `ShallStartSearch` requires a usable path or an existing valid search target. Tracking should not replace personality's search willingness or combat priorities.

`SearchPathFinder.checkFinishedSearch` marks an accepted target place searched when:

- the bot is within **2 m** and its enemy path has at most two corners; or
- an accepted **partial** path reaches its final corner within **2 m**.

Partial-path acceptance has additional checks in `CheckEnemyPath`; an unreachable enemy does not automatically count as searched. Separately, `EnemyPlace.SetDistances` marks personal or squad arrival inside **1 m**. `SetPlaceAsSearched` distinguishes the place's original owner from a squad-derived place.

`EnemyKnownPlaces.checkSearched` runs at most every **0.25 s**. Despite its name, `SearchedAllKnownLocations` currently tests arrival at the **latest LastKnownPlace**, not a proof that every object in `AllEnemyPlaces` was visited.

### Forgetting and target release

`EnemyKnownChecker.ShallKnowEnemy` applies these rules in order:

1. Inactive enemy or missing last-known place: unknown.
2. Last-known update older than **400 s**: unknown, including during search.
3. Last-known update no older than `Bot.Info.ForgetEnemyTime`: known.
4. Beyond that duration: known only while the bot's combat decision is `Search`, this enemy's `OnSearch` flag is set, and its last-known location has not been searched.
5. Otherwise: unknown.

Unknown-state events remove the contact from `KnownEnemies` and clear known places. They do not necessarily delete the stored enemy record in `Enemies`/`EnemiesArray`. Invalid/allied enemy removal is separate. New accepted evidence can make a record known again.

`SAINEnemyController.ChooseEnemy` selects from native knowledge and clears its goal when no known enemies remain. `SelectEnemy` rejects an existing invalid/inactive/unknown/missing-place goal. Releasing one goal may also mean another target was selected, not that all combat ended.

Example with a 20-second forget duration: seen at A at t=0, heard at B at t=8. Ordinary memory lasts until about t=28. Active unfinished Search can retain B beyond t=28. Arrival at t=31 removes that extension, allowing forgetting on subsequent updates. Arrival at t=14 stops searching B, but the contact may remain known until ordinary expiry. Do not implement unconditional `arrived => delete enemy`.

### Duration ownership requires attention

Native `SAINBotInfoClass.CalcTimeBeforeSearch` derives search delay from personality/role and aggression, then sets forget duration to that delay plus randomized 20–60 seconds. It writes both native `ForgetEnemyTime` and EFT `TIME_TO_FORGOR_ABOUT_ENEMY_SEC`.

Our setup writes configured `enemyRemember` to EFT/native SAIN. `SainManPersonality.RefreshTimers` saves/restores both around its own refresh. However, native `SearchDecider.CalculateSearchTime` can independently call `CalcTimeBeforeSearch` when not already searching, with a 120-second recalculation interval. No separate guard for that call was found in inspected core/addon code. This is a **source-backed duration-drift risk to validate**, not a raid-confirmed cause. A common mode needs one duration owner across all recalculation paths.


## Making SainMan Simple

Keep both addon layers, native actions and personality. The change is tracking policy, not a fallback to Rifleman combat.

Native `KnownEnemies`, `LastKnownPlace`, `SearchDecider`, `SearchPathFinder`, `SAINEnemyPath` and target selection all depend on native knowledge. Changing only layer action routing, keeping only EFT `GoalEnemy` alive, or moving the marker is insufficient.

Prototype one already accepted enemy first:

1. Use core Simple eligibility/retention to decide whether that target remains trackable. Sampling its transform must not create an enemy or renew its evidence deadline.
2. Supply the permitted Simple position to the native path/decision consumers for this follower. Prove search, chase, cover and support consume the same projection before enabling the mode generally.
3. Separate known-state retention/searched-place handling from report production. Do not repeatedly call `UpdateLastSeenPosition` or `UpdatePersonalHeardPosition`: those renew timestamps, reset searched flags, make the target known and can propagate reports. That shortcut creates artificial evidence and churn.
4. Coordinate release. Search completion must not discard a still-eligible Simple target, but core expiry or Attention must not be vetoed by endlessly renewed native knowledge. A huge forget duration is not a substitute.
5. Keep synthetic movement from rerolling search/personality timers or resetting failed engagement budgets. True new sight/reports remain legitimate inputs.

**Candidate seams, not yet implemented or qualified:** follower-scoped interception around native eligibility and position/path consumers; or narrowly copied addon decision/search/path components if manipulating private native state would be more fragile. A prototype must find the smallest complete seam. This inspection does not prove one getter override is enough.

Any native interception used only for SAINGrunt tracking belongs in the addon and is dispatched only for ready followers in this mode. General compatibility remains core-owned; shared SAIN objects remain immutable. Prefer preserving genuine evidence and projecting tactical state. If temporary native projection is necessary, prove that events, squad broadcasts and sensors cannot see it as new sight/hearing; do not claim that safeguard until tested.


## Addon integration constraints

- The latest `SainEnemyContact` in `SainAddonBridge` is a passive selected-contact **UI** view: profile, remembered point, optional visible point and sight age. It has no provenance, search outcome or expiry authority. Reuse its display boundary, but it is not the full policy service.
- `SAINFollowerCombatHandoff` considers the selected enemy and other native known enemies, behind accepted-goal/On Your Own gating. One forgotten goal need not start linger while another threat remains. Medical/urgent grenade exceptions must survive.
- `SAINFollowerEngageAttempt` permits reset for target change, native known-anchor movement of at least 8 m, or visible/shootable contact. Simple synthetic position updates must not become “new evidence” that endlessly rearms a failed attempt.
- Boss-cover and regroup consume native positions/path state. Keep commitments, arrival holds, command priorities and fallback rules; tracking changes information, not those tactical priorities.

## Native source map

Native paths are relative to the external `SAIN/` source root from LOCAL.md. Line numbers are investigation anchors; use method names if they shift.

| Area | Source anchors |
|---|---|
| Known places | `Classes/Bot/EnemyClasses/Position/EnemyKnownPlaces.cs`: `checkSearched` (76), `OnEnemyKnownChanged` (145), report updates (240 onward); `EnemyPlace.cs`: `SetDistances` (75), `Position` / `UpdatePosition` (159 onward) |
| Forgetting / selection | `Classes/Bot/EnemyClasses/Checkers/EnemyKnownChecker.cs`: `ShallKnowEnemy`, `BotIsSearchingForMe`; `Classes/Bot/EnemyControllers/SAINEnemyController.cs`: lists, `ChooseEnemy` (213), `SelectEnemy`, EFT publication (461) |
| Sight / hearing reports | `Classes/Bot/EnemyClasses/Vision/EnemyVisionClass.cs`: `UpdateVisibleState`; `Enemy.cs`: report methods (611 onward); `Other/EnemyHearing.cs`: `UpdateHeardPosition`; `Other/EnemyEvents.cs`: `LastKnownUpdated` |
| Hearing / squad sharing | `Classes/Bot/Sense/Hearing/HearingSensor.cs`: `ReactToHeardSound`; `HearingDispersion.cs`: `CalcRandomizedPosition`; `Classes/BotManager/Squad.cs`: `ReportEnemyPosition`, `AddPlaceForCheck` |
| Search / path / timers | `Classes/Bot/Search/SearchDecider.cs`: `ShallStartSearch`, `CalculateSearchTime`, `WantToSearch`; `SearchPathFinder.cs`: `checkFinishedSearch`, `CheckEnemyPath`; `Classes/Bot/EnemyClasses/Path/SAINEnemyPath.cs`: `CheckCalcPath`, `ShallCalcNewPath`; `Classes/Bot/Info/SAINBotInfoClass.cs`: `CalcTimeBeforeSearch` |
| EFT data | Decompile `EnemyInfo.cs`: `CurrPosition` (173), `TimeLastSeen` (94), `ShallKnowEnemy` (476); `BotEnemyChooser.cs`: `FindDangerEnemy` |
| Optional bridges/timers | [SainGoalEnemyBridge.cs](../../client/Modules/SainGoalEnemyBridge.cs), [SainAddonBridge.cs](../../client/Modules/SainAddonBridge.cs), [SainManPersonality.cs](../SainManPersonality.cs): `RefreshTimers`; [BotFollowerPlayer.cs](../../client/Components/BotFollowerPlayer.cs): setup and `TryOverrideSainForgetEnemyTime` |
| Addon/UI | [SAINFollowerRuntime.cs](../SAINFollowerRuntime.cs): `GetEnemyContact`; [SAINFollowerCombatHandoff.cs](../SAINFollowerCombatHandoff.cs), [SAINFollowerEngageAttempt.cs](../SAINFollowerEngageAttempt.cs), [SAINFollowerCoverFinder.cs](../SAINFollowerCoverFinder.cs), [PingTeamates.cs](../../client/Utils/PingTeamates.cs), [SquadControlMenuUi.Settings.cs](../../client/Components/SquadControlMenuUi.Settings.cs) |

## Qualification

In addition to the [shared matrix](../../docs/Enemy-Tracking.md#implementation-sequence-and-validation), verify native path/search/cover consumers agree on one permitted position, synthetic movement never produces sight/hearing or squad reports, both native search timers remain bounded, expired knowledge cannot be resurrected by a retained order, and repeated eight-metre Simple projections cannot rearm failed engagement attempts.

Hook ownership follows [Integration](Integration.md): a hook needed only by this combat brain belongs in the addon; compatibility needed without it stays in core. Shared presets and genuine sensor evidence are never rewritten by a display policy.
