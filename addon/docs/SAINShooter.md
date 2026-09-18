# SAINShooter combat

**Core base:** [Marksman combat](../../docs/Combat-Tactics.md#marksman-combat-behavior), [Core commands](../../docs/Commands.md), and [proficiency](../../docs/Friendly-AI-Performance-Settings.md). Shared addon behavior is described in [Integration](Integration.md), [Combat](Combat.md) and [Commands](Commands.md).

## Boundary

SAINShooter is a native SAIN 4.5.1 adaptation of Core Marksman. Stability, existing SAIN execution and bounded cost take precedence over exact parity. This implementation does not enhance external SAIN, replace its decision manager, add a second perception/weapon system, or reproduce Core's broad geometry searches. No new Harmony hook is introduced.

The displayed and persisted tactic is `SAINShooter` (new enum value 4). Existing `SainMan`/SAINGrunt identity is unchanged. Both plugins are required for selection, and both constructed layers/native/player readiness are required for addon execution. Missing/unready state retains the saved identity and falls back to **Core Marksman**. Picked-up native SAIN bots continue defaulting to SAINGrunt.

Default aggression is **30%**. Core's existing Marksman proficiency baseline, including its SAIN values, applies before saved Vision/Precision/Reaction modifiers. Personality interpolation stays follower-local and uses the existing aggression anchors. The role's restriction on automatic assault applies even at high aggression; zero aggression still allows firing-position support.

## Native firing-position support

`SAINFollowerMarksmanObjective` uses the same two addon layers and publication filter as SAINGrunt. It translates ordinary Search/RushEnemy and squad Search/GroupSearch/Help/PushSuppressedEnemy into firing-position support. It bypasses the Grunt Push objective. Already useful visible/shootable contact yields native firing rather than an opportunistic rush.

- First reuse a firing point from the native solo MoveToEngage result being published. Otherwise use SAIN's public `FiringPositionFinder` at a planning boundary on a bounded retry cadence. Ordinary planning requires personal seen/heard knowledge; Need Sniper can request support for the accepted native contact.
- SAIN selects the candidate and checks weapon range, the lane to its last-known head point and native reachability. The addon validates finite coordinates, a distinct point, same floor, destination availability and a complete route. It never supplies hidden live enemy positions to the finder.
- Maintain at least **16 m horizontal standoff**. Automatic positions stay inside the player's Marksman regroup radius unless independent and favor positions behind the player or positions that do not materially close on the enemy. Automatic routes are capped at **90 m**. Need Sniper may use forward/lateral native candidates and routes up to **140 m**. A rejected candidate yields native cover; there is no wider Core fallback scan.
- Empty/rejected scans can retry every **4 s** at a passive planning boundary. Need Sniper retains a **2.5 s** support-search window with **2 s** scan spacing; cover travel/arrival keeps the original command pending. All native finder calls stay at least **2 s** apart, including rapid target/order changes. A fresh scan clears only the finder's cached candidate.
- Failed/cancelled started destinations are remembered in a fixed four-position set; candidates within **4 m** of one are rejected. Only a different admitted position resets the per-leg execution budget. Four failed positions cap further same-contact movement/scanning until a changed enemy, eight-metre knowledge change, real visible/shootable opportunity or combat cleanup. Repeated commands cannot erase failed positions.
- Movement reuses `SAINFollowerMoveToEngageAction` and the existing engagement budget: **20 active seconds**, **6 seconds without progress**, and **2 seconds at arrival without a shot**. Interruptions pause the execution budget. The same bounds apply in On Your Own. Failed execution yields native SeekCover and may reach existing auto-regroup admission after ordinary cover/commitment safety gates.
- Existing cover travel and arrival use retain priority. Self-actions, medicine, retreat, grenade reactions, dogfight and melee remain native. Incoming pressure/critical health blocks new pursuit and preserves recovery. Native squad Suppress remains native, including its movement and safety behavior.

The native finder searches at 10/20/32 m radii with 12 directions per radius. Its scan is synchronous; this adaptation bounds invocation count rather than rewriting SAIN's finder. No second Core support scan or local adjustment scan is added. Runtime frame time and navigation still require raid qualification.

## Commands, regroup and lifecycle

- **Need Sniper:** retains Core's input/contact, squadmate eligibility and rejection checks. Pending native medicine/survival leaves the original command pending until its Core timeout. When accepted, the addon consumes it into native firing-position support. It can request a fresh bounded scan, but cannot rearm a failed position or replace native target-selection priority; a changed/lost native contact ends that intent.
- **Go Forward:** Core Marksman ignores generic assault. Ready SAINShooter similarly handles the command without creating a Grunt push or forcing 100% aggression. Peaceful Go Forward remains the Core movement command.
- **Suppress:** Core's automatic-secondary ordered-suppression fallback is not implemented by this native policy and is rejected for ready Shooter. Native squad suppression is retained. Unready Shooter follows the normal Core Marksman command path.
- **There / Come here / commanded regroup / Exit Located:** reuse the existing addon translations. Replacement commands release the Marksman destination before another objective consumes the request. Regroup also releases the old destination and retires the started position within that remembered contact, preventing a return to its cached position on completion.
- **Automatic regroup:** retains the shared conservative fallback gates. Marksman trigger is normally 1.5x the configured radius, 2x in Factory mode. Normal commanded completion uses Core's 24 m Marksman distance; Factory mode uses the shared 10 m distance, and tight regroup retains its separate distance. No special Core support-event regroup interruption is added.
- **On Your Own:** removes player-area restrictions/automatic regroup and retains the existing investigation exception, while keeping the Marksman role and bounded firing attempt.
- Existing accepted-goal admission, enemy markers, linger, post-combat healing, friendly-fire safety and recorder episodes remain shared. Tactic switches use Core fallback until the new role is prepared and release the previous role's movement/claims without clearing living enemy memory.

Recorder snapshots expose `objectives.marksman` (reason, enemy, attempted scan, order, movement, destination, candidate, rejection, failed-position count and retry delay). `sainMarksman` events report planning/selection/failure; snapshots distinguish an empty native finder from failed-position, floor, route, reservation, standoff and player-area rejections. Existing `sainEngageAttempt` and native action records provide execution results. Recording remains passive and disabled recording does not create these payloads.

## Intentional differences from Core

| Core facility | Native adaptation boundary |
| --- | --- |
| Broad support/backline/alternate-threat candidate search | Uses only a native finder candidate plus cheap role/path admission; no world enemy scan or Core geometry planner. |
| One local arrival adjustment, variable arrival wait and ten-second tactical holds | Uses the existing bounded two-second firing-position arrival, then native cover. No extra local search is added. |
| Automatic second-primary/holster draw, preparation and return policy | Retains current SAIN/EFT compatibility behavior. No new support-weapon controller or proactive automatic close-assault branch. |
| Core push events and exact ally-support arbitration | Uses native squad support proposals. Does not add mixed-Core push event plumbing solely to match every support branch. |
| Stationary Core firing/suppression actions | Uses native firing and suppression. SAIN StandAndShoot can make a lateral entry move within 50 m; native suppression can move toward the enemy. These behaviors are not rewritten. |
| Explicit support target lock/current-position planning | Uses accepted native target priority and last-known knowledge. It does not force a hidden target's live coordinates or change native memory/search completion. |
| Higher aggression's broader offensive closing rules | Native personality changes still apply, but this version remains a firing-support role and does not implement Core automatic close search. |

These are deliberate limits, not promises of automatic later parity. Extend only when raid evidence establishes a concrete need and SAIN provides a stable, reasonably priced way to do it. The earlier broader SAINShooter proposal is superseded by this boundary.

## Source references

Core: `FollowerCombatSniper.GetDecision`, `ClearAggressiveRequests`, `TryGetPushSupportDecision`, `TryCreateSafeCloseSearchDecision`, `FollowerCombatNeedSniperObjective`, `FollowerCombatRegroupObjective`, `FollowerProficiencyValues`, and shared `FollowerCombatCommon` position/arrival helpers.

External SAIN 4.5.1 (source root from `LOCAL.md`):

- `Classes/Bot/Decision/FiringPositionFinder.cs`: public native candidate search.
- `Classes/Bot/Decision/EnemyDecisionClass.cs`: native MoveToEngage is primarily for unreachable/sniper enemies, rather than a general Marksman role.
- `Layers/Combat/Solo/MoveToEngageAction.cs`, `StandAndShootAction.cs`, `SearchAction.cs`: movement, shooting and pursuit execution.
- `Layers/Combat/Squad/CombatSquadLayer.cs`, `SuppressAction.cs`: native Help/Search/Rush mapping and suppression movement.
- `Classes/Bot/WeaponFunction/SAINShootData.cs`: native selection is not Core's support-weapon transaction; Core's general weapon guard remains authoritative.
- `Classes/Bot/EnemyClasses/Checkers/EnemyKnownChecker.cs`: native evidence/forgetting is unchanged. Search can extend known-contact retention; the addon does not fabricate a completed search.

Implementation: [Marksman objective](../SAINFollowerMarksmanObjective.cs), [coordinator](../SAINFollowerObjectives.cs), [bounded movement](../SAINFollowerMoveToEngageAction.cs), [role mapping](../../client/Components/FollowerCombatTactics.cs). See [Validation](Validation.md) for checks and pending raid qualification.
