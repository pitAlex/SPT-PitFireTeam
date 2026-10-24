# SAIN addon enemy tracking

Base contract: [shared Enemy Tracking](../../docs/Enemy-Tracking.md), [Core combat](../../docs/Combat-Tactics.md) and [integration](Integration.md). The addon owns the native adaptation for ready SAINGrunt and SAINShooter followers. Both modes keep their selected combat brain; ordinary SAIN bots and shared presets are excluded.

## Realistic

Native known places, genuine report timestamps, search personality and completion remain authoritative. `Enemy.LastKnownPosition` reads the stored latest `EnemyPlace`, while `Enemy.EnemyPosition` reads the live transform. Personal sight, dispersed hearing and squad reports update native known places and publish genuine report events.

Native `EnemyKnownChecker.ShallKnowEnemy` rejects inactive/missing places, then reports older than 400 seconds. Inside the configured normal duration it keeps knowledge. Beyond that duration it extends only during active Search, with this enemy's OnSearch flag and an unfinished latest place. Arrival before ordinary expiry stops retrying the place but does not instantly forget the enemy. Unknown events release native known state; genuine reports can reacquire it.

Core's timer compatibility hook restores the captured Enemy Remember Time after `CalcTimeBeforeSearch`, including the native 120-second recalculation path. Addon eligibility also supplies that duration. No preset mutation or huge forget timer is used.

## Simple

[SainEnemyTrackingPolicy](../SainEnemyTrackingPolicy.cs) projects live coordinates only for a ready follower's active, known native enemy matching the accepted EFT goal, with a genuine known place still inside Enemy Remember Time. Projection does not create a target or renew its timestamp. Unaccepted heard contacts keep native remembered preparation behavior.

[SainEnemyTracking](../SainEnemyTracking.cs) redirects position, remembered head/distance and arrival reads at explicit native tactical callsites: enemy path, search decision/path/action, steering/dogfight, firing-position planning, cover and pursuit. Addon cover, push, marksman and support consumers use the same helper. Native search completion cannot exhaust an eligible moving Simple target; ordinary expiry still releases it. Decision/personality and action ownership remain native/addon-owned.

Native known-place getters, report creation, sight/hearing producers, squad broadcasts and suppression/fire executors are not globally patched. Projection does not call UpdateLastSeenPosition/UpdatePersonalHeardPosition, mutate EnemyPlace, reset searched flags or manufacture LastKnownUpdated events. Genuine sight and actual aim/shot-safety gates remain required by the existing firing paths. Failed engagement/support budgets use genuine remembered anchors, so silent movement cannot continually rearm them.

Hooks install with the addon and use its existing rollback/unpatch lifecycle. The duration compatibility hook belongs in Core because followers need it without this combat brain. Missing/unready addon uses Core fallback.

## Native reference findings

Native hearing uncertainty depends on sound type, distance, direction and previous knowledge, followed by reachability sampling. Squad transmission chance is `25 + 15 * clamp(coordination, 1, 5)` percent per recipient. Core-only tracking does not reproduce those sensors/probabilities.

Native search completion accepts a latest place within 2 m with at most two path corners, or the final corner of a separately qualified partial path within 2 m. Place personal/squad arrival uses 1 m. Latest-place arrival is not proof that every historic place was visited. These native Realistic semantics remain; Core's remembered completion uses a short complete route and a height gate.

## Qualification

The shared fixture covers genuine evidence, finite lifetime and position policy. Installed metadata and production IL transformations validate adapter signatures and tactical coverage. The addon regression suite checks existing combat boundaries. These checks do not establish actual Unity Harmony installation, perception, navigation or frame time.

Pending raids: both addon tactics in both modes, silent hidden relocation, native search/cover/steering agreement, unchanged sight/hearing/report events, ordinary expiry during orders, interrupted search, native partial paths, multi-target handoff and repeated Simple movement after a failed engagement. See [validation](Validation.md) for dated evidence.

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
