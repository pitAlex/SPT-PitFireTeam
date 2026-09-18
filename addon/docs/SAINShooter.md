# SAINShooter combat

**Core base:** [Marksman combat](../../docs/Combat-Tactics.md#marksman-combat-behavior), [Core commands](../../docs/Commands.md), and [proficiency](../../docs/Friendly-AI-Performance-Settings.md). Shared addon behavior is described in [Integration](Integration.md), [Combat](Combat.md) and [Commands](Commands.md).

## Boundary

SAINShooter is a native SAIN 4.5.1 adaptation of Core Marksman. Stability, existing SAIN execution and bounded cost take precedence over exact parity. This implementation does not enhance external SAIN, replace its decision manager, add a second perception/weapon system, or reproduce Core's broad geometry searches. The Marksman objective adds no dedicated Harmony hook; shared squad command routing is described in [Squad support](Squad-Support.md).

The displayed and persisted tactic is `SAINShooter` (new enum value 4). Existing `SainMan`/SAINGrunt identity is unchanged. Both plugins are required for selection, and both constructed layers/native/player readiness are required for addon execution. Missing/unready state retains the saved identity and falls back to **Core Marksman**. Picked-up native SAIN bots continue defaulting to SAINGrunt.

Default aggression is **30%**. Core's existing Marksman proficiency baseline, including its SAIN values, applies before saved Vision/Precision/Reaction modifiers. Personality interpolation stays follower-local and uses the existing aggression anchors. Automatic close search uses Core Marksman aggression, range and weapon-threat gates. Zero aggression still allows firing-position support and defensive automatic support weapons.

## Native firing-position support

`SAINFollowerMarksmanObjective` uses the same two addon layers and publication filter as SAINGrunt. Outside an owned squad support objective, it translates ordinary Search/RushEnemy and squad Search/GroupSearch/Help/PushSuppressedEnemy into firing-position support. It bypasses the Grunt Push objective. Already useful visible/shootable contact yields native firing rather than an opportunistic rush.

- First reuse a firing point from the native solo MoveToEngage result being published. Otherwise use SAIN's public `FiringPositionFinder` at a planning boundary on a bounded retry cadence. Ordinary planning requires personal seen/heard knowledge; Need Sniper can request support for the accepted native contact.
- SAIN selects the candidate and checks weapon range, the lane to its last-known head point and native reachability. The addon validates finite coordinates, a distinct point, same floor, destination availability and a complete route. It never supplies hidden live enemy positions to the finder.
- Maintain at least **16 m horizontal standoff**. Ordinary support positions stay inside the player's Marksman regroup radius unless independent and favor positions behind the player or positions that do not materially close on the enemy. Eligible automatic close search may accept a forward native candidate under the separate policy below. Automatic routes are capped at **90 m**. Need Sniper may use forward/lateral native candidates and routes up to **140 m**. A rejected candidate yields native cover; there is no wider Core fallback scan.
- Empty/rejected scans can retry every **4 s** at a passive planning boundary. Need Sniper retains a **2.5 s** support-search window with **2 s** scan spacing; cover travel/arrival keeps the original command pending. All native finder calls stay at least **2 s** apart, including rapid target/order changes. A fresh scan clears only the finder's cached candidate. The finder and its native scan timer are shared with the squad support objective.
- Failed/cancelled started destinations are remembered in a fixed four-position set; candidates within **4 m** of one are rejected. Only a different admitted position resets the per-leg execution budget. Four failed positions cap further same-contact movement/scanning until a changed enemy, eight-metre knowledge change, real visible/shootable opportunity or combat cleanup. Repeated commands cannot erase failed positions.
- Movement reuses `SAINFollowerMoveToEngageAction` and the existing engagement budget: **20 active seconds**, **6 seconds without progress**, and **2 seconds at arrival without a shot**. Interruptions pause the execution budget. The same bounds apply in On Your Own. Failed execution yields native SeekCover and may reach existing auto-regroup admission after ordinary cover/commitment safety gates.
- Existing cover travel and arrival use retain priority. Self-actions, medicine, retreat, grenade reactions, dogfight and melee remain native. Incoming pressure/critical health blocks new pursuit and preserves recovery. Native squad Suppress remains native, including its movement and safety behavior.

The native finder searches at 10/20/32 m radii with 12 directions per radius. Its scan is synchronous; this adaptation bounds invocation count rather than rewriting SAIN's finder. No second Core support scan or local adjustment scan is added. Runtime frame time and navigation still require raid qualification.

## Automatic support weapons

`SainMarksmanWeaponBridge` binds Core's existing cached weapon eligibility, loaded-ammo/penetration selection, accepted slot request, asynchronous readiness and return helpers once per Shooter. Both second primary and holster are supported. Core's existing external-SAIN weapon-selection guard remains authoritative; no second native weapon-selection hook is added.

- A useful visible shot keeps the current weapon. Native medicine, emergency movement and dogfight retain priority.
- A close threat may prepare an eligible automatic weapon even at zero aggression, without cancelling committed cover travel. One accepted request owns an enemy-facing stationary hold for at most three seconds. Rejected/timed-out attempts retry no sooner than four seconds.
- Proactive close search requires positive effective aggression, no temporary Hold Position override or pending medical work, personal native seen/heard evidence, Core Marksman range and ammunition/threat eligibility. Below 40% it retains Core's `AttackImmediately` equipment assessment. The existing Core aggression-dependent enemy-count limit is applied to unique living native-known contacts within 35 m; the higher-aggression low-threat check also retains its fewer-than-three limit. This avoids Core's physical/world enemy enumeration and hidden live positions. Assessment is cached for 0.5 seconds.
- A finite, distinct, same-floor, unreserved native firing position with a complete route no longer than 90 m and at least 16 m horizontal enemy separation is admitted **before** proactive drawing. Preparation retains that exact position and revalidates its separation, route, floor and reservation once at weapon readiness before movement; it never starts a blind Search or a second geometry planner. Timeout or failed eligibility retires the position through existing failed-position protection.
- Defensive preparation cancels when the close threat leaves; a late accepted draw retains restoration ownership until it can safely return to primary. A selected automatic support weapon remains equipped through its close-search movement and while close danger persists. Core's return helper restores primary after the intent/danger ends or combat cleanup; a late asynchronous callback can be restored on a subsequent lifecycle evaluation. Incoming decision and live native medical/survival gates also protect cancellation and primary restoration.
- Defensive cover, dogfight and autonomous suppression execution remain native SAIN. This adaptation supplies eligible weapon intent; it does not copy Core's close-suppression movement planner. Ordered automatic-secondary suppression uses the shared addon squad objective.
- Recorder snapshots include preparation, weapon ownership/readiness state and automatic-destination intent. Recording does not evaluate policy or issue requests.

## Commands, regroup and lifecycle

- **Need Sniper:** retains Core's input/contact, squadmate eligibility and rejection checks. Pending native medicine/survival leaves the original command pending until its Core timeout. When accepted, the addon consumes it into native firing-position support. It can request a fresh bounded scan, but cannot rearm a failed position or replace native target-selection priority; a changed/lost native contact ends that intent.
- **Go Forward:** Core Marksman ignores generic assault. Ready SAINShooter similarly handles the command without creating a Grunt push or forcing 100% aggression. Peaceful Go Forward remains the Core movement command.
- **Suppress:** retains Core Marksman candidate selection (player-facing target or no Rifleman fallback) and eligible automatic secondary/holster requirement. The squad objective waits up to three seconds for an unrelated selector transition to settle, then owns one accepted draw with its own three-second readiness window before the normal six-second attempt/two-second actual-fire budget. No firing or movement is issued while preparation is pending. Native squad suppression is retained; grenade launchers are excluded.
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
| Automatic second-primary/holster draw, preparation and return policy | Reuses Core eligibility, switch request, readiness and return helpers through cached addon-local delegates. Native actions execute the resulting intent. |
| Core push events and exact ally-support arbitration | Bounded [squad support](Squad-Support.md) reads existing Core push events and addon automatic push state plus player/ally engagement cues. No addon-to-Core push publication or complete Core arbitration is added. |
| Stationary Core firing/suppression actions | Uses native firing and suppression. SAIN StandAndShoot can make a lateral entry move within 50 m; native suppression can move toward the enemy. These behaviors are not rewritten. |
| Explicit support target lock/current-position planning | Uses accepted native target priority and last-known knowledge. It does not force a hidden target's live coordinates or change native memory/search completion. |
| Higher aggression's broader offensive closing rules | Core range/aggression/ammo policy admits forward native firing positions. Cluster checks use only SAIN-known contacts, without Core world-space enemy enumeration. No direct-to-enemy fallback. |

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
