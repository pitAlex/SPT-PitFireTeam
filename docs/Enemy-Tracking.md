# Enemy Tracking: Simple and Realistic

Enemy Tracking selects follower information independently of combat tactic. **Realistic is the default**, including existing installations without a saved value. Core Rifleman/Marksman and ready SAIN addon tactics use the same mode. Simple keeps SAINGrunt/SAINShooter combat ownership.

Core means our follower implementation, not unmodified EFT. Read [Core combat](Combat-Tactics.md), [commands](Commands.md) and the [addon adaptation](../addon/docs/Enemy-Tracking.md). This document describes implemented source behavior; raid qualification remains pending.

## Shared contract

| Concern | Simple | Realistic |
|---|---|---|
| Hidden accepted enemy | Live position drives tactical tracking through walls | Latest accepted sight, hearing, player or squad report drives tracking |
| Evidence | Genuine reports alone renew memory | Genuine reports alone renew memory |
| Ordinary lifetime | Enemy Remember Time after the latest report | Same duration, with active unfinished search extending it up to 400 seconds after the report |
| Search completion | Does not end tracking while the accepted contact remains eligible | Completes that report; fresh evidence, including at the same point, permits another search |
| Visibility/fire | Existing perception, aim and shot-safety gates | Same gates |
| Tactic/commands | Existing ownership and admission | Same rules |

Mode and Enemy Remember Time are captured at raid start. My Squad provides one Enemy Tracking row with its description on the left and Simple above Realistic on the right, separated by 15 pixels. Both choices use native location-time radio controls, with a circular fallback if the prefab has not loaded. Their separate `ToggleGroup` enforces mutual exclusion, and cloned raid-time events are replaced before activation. Choices are disabled during a raid and locked to Realistic in Allegiance; ConfigurationManager exposes the enum. Persisted `03 EnemyTracking` defaults to Realistic. Existing tactic identifiers and `03 EnemyRemember` remain stable. English fallback and Russian/Chinese translations use the central language model.

Ordinary SAIN bots are excluded. Tracking does not admit heard-but-unaccepted enemies, grant permission to fire, or bypass On Your Own, regroup, Attention or explicit-order acceptance.

## Evidence and lifetime

[FollowerEnemyTracking](../client/Modules/FollowerEnemyTracking.cs) stores contacts by follower/enemy identity: position, observation time, source, searched-report time and active-search state. Older/equal timestamps cannot overwrite newer evidence. Tactical reads and restoration never manufacture observations.

Personal sight samples the visible enemy. Hidden personal/group reports retain their own timestamps. An exact native SAIN goal's stored known place and timestamp take precedence over EFT mirror bookkeeping, preserving native hearing dispersion. Automatic squad sharing copies the reporter's stored position and original observation time. Explicit player Contact supplies a new player observation through existing acceptance gates.

Compatibility exception: `Enemy.RepairPersonalMemory` remains enabled for hidden Realistic contacts. Missing/invalid vanilla memory can be seeded from the current enemy position, accepting a recovery-time loss of realism. Group-position repairs receive EFT's setter timestamp. Repeated repair calls preserve valid positions and initialized timestamps, so subsequent hidden movement does not continuously refresh the contact. This is conditional repair, not a permanent once-per-enemy flag; later corruption can be repaired again.

With a 20-second duration, sight at t=1 followed by sound at t=8 gives ordinary expiry at t=28. Active unfinished search can continue beyond t=28, bounded by t=408. Core arrival at t=14 starts a three-second look-around at the verified reachable spot. If no newer report or actual contact interrupts it, completion releases that report from active combat, even before t=28. The living enemy memory and relationship remain; newer evidence, including at the same position, restores eligibility. Simple mode keeps its timer behavior. Native SAIN can retain ordinary enemy knowledge until t=28 while its search decider rejects already-visited locations; Core implements the exhausted-contact release explicitly.

Core search is active only with a reachable current search route or its bounded arrival inspection. A nearby remembered point alone cannot trigger the personal point-blank guard. Completed reports do not keep peaceful following blocked by the group-contact timer. Stopping, switching targets and completion release that flag. Stock unscoped goal clearing cannot discard an eligible unfinished search. Ready addon Realistic follows native Search/OnSearch/latest-place completion semantics with the same upper bound.

Selected expired contacts release their matching retention/mission authority. Restoration and the GoalEnemy setter check eligibility, preventing order-driven resurrection. Other contacts, relationships and genuine reacquisition remain available. Dismissal, Attention and raid teardown clear the relevant local tracking state.

## Core adaptation

Central anchors/distances and tactical pursuit, push, cover and markers resolve the permitted position. Hidden Realistic pursuit enters the existing committed memory-search action. Completion requires proximity, compatible height and a short complete route, avoiding arrival across walls or on another floor. Nearby NavMesh sampling is restricted to the remembered floor. Fresh evidence at the same point can reopen search.

Sensors, immediate-fire and suppression target policies retain real geometry and existing safety gates. No global enemy/player position getter is overridden. Hidden cover planning uses remembered target geometry. Core markers use remembered coordinates in Realistic and live coordinates in Simple, keeping existing cadence and visibility colors.

Simple markers for both Core and SAIN addon contacts capture the current position on each ping and on first display; hidden markers otherwise refresh every five seconds while displayed. Visible markers refresh continuously. Realistic addon markers retain immediate updates from native remembered knowledge. Marker cadence does not throttle the live positions used by Simple tactical decisions/actions.

[SainGoalEnemyBridge](../client/Modules/SainGoalEnemyBridge.cs) remains a soft dependency. [FollowerSainTrackingTimerPatch](../client/Patches/FollowerSainTrackingTimerPatch.cs) restores the captured follower duration after native search-delay recalculation, which otherwise rewrites native/EFT forget time. Shared presets and ordinary-bot timers are untouched. Absent/unready addon state uses Core fallback.

Recorder snapshots include passive `enemyTracking` mode, source, position/time, searched time, search state and deadlines. Recording never samples perception or publishes decisions.

## Qualification and limits

`tests/Verify-EnemyTracking.ps1` compiles the production contact service and addon position policy, checks installed native metadata, and executes production IL transformations with matching stack signatures. The existing addon regression suite remains required. Dated results belong in [addon validation](../addon/docs/Validation.md).

Pending controlled raids must cover:

- Silent relocation in both modes with Core, SAINGrunt and SAINShooter; compare marker, facing, destination and cover.
- Genuine sound/squad reports, same-point fresh evidence, interrupted search, ordinary expiry and the finite search cap.
- Complete/invalid routes, native accepted partial routes, walls and connected/disconnected floors.
- Orders, multiple contacts, native/EFT goal changes, Attention, medical/grenade interruption, death/despawn and teardown.
- Core-only, external SAIN without addon, unready fallback and ordinary-bot exclusions.
- Actual Unity Harmony installation, navigation/perception and frame time.

Core reuses existing hearing/acquisition and squad paths. Without SAIN it does not reproduce native hearing dispersion or coordination-dependent transmission probability. Core search completion requires a complete route; native Realistic retains its qualified partial-path semantics. Tracking equivalence is not full sensor/personality parity.

## Source map

- Policy/lifecycle: [FollowerEnemyTracking](../client/Modules/FollowerEnemyTracking.cs), [BossPlayers](../client/Modules/BossPlayers.cs).
- Search/anchors: [FollowerCombatCommon](../client/BigBrain/FollowerCombatCommon.cs), [CombatSearchAction](../client/BigBrain/Actions/CombatSearchAction.cs), [Enemy](../client/Utils/Enemy.cs).
- Admission/restore: [BotMemoryPatch](../client/Patches/BotMemoryPatch.cs), [FollowerContactEnemyRetention](../client/Modules/FollowerContactEnemyRetention.cs), [FollowerCombatTargetCommitments](../client/Modules/FollowerCombatTargetCommitments.cs).
- Reports/markers: [AIBossPlayer](../client/Components/AIBossPlayer.cs), [FollowerAwareness](../client/Utils/FollowerAwareness.cs), [PingTeamates](../client/Utils/PingTeamates.cs).
