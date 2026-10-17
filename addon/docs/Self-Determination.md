# SAIN Self-Determination

**Core base:** [Self-Determination](../../docs/Self-Determination.md), [Core combat](../../docs/Combat-Tactics.md) and [commands](../../docs/Commands.md). **Status:** implemented; raid qualification remains pending. Core owns preferences and overrides; this page describes only ready SAINGrunt/SAINShooter adaptation. Current supporting behavior is in [Combat](Combat.md), [Squad support](Squad-Support.md) and [SAINShooter](SAINShooter.md).

## Ownership and combat lifetime

Both addon tactics consume the same resolved determination as Core fallback. Ordinary SAIN bots and shared presets are unchanged. Aggression still owns personality interpolation; determination does not select another personality or grant hearing, sight or firing permission.

On Your Own sets determination to 100, Cover Me to exactly 20 and one protective regroup, Hold Position sets aggression to zero, and eligible Go Forward sets it to 100. Each changes only its own percentage. Go Go Go and Attention immediately restore both saved values. Core clears both overrides after aggregate squad combat ends, including a short quiet confirmation. Individual addon linger completion cannot clear them. Addon registration is released on handoff cleanup, readiness loss and shutdown, separately from Core's registration for the same follower.

## Local adaptations

- `SAINFollowerPushAssessment` multiplies its existing player-pull by the shared dependence weight. Intermediate changes reuse cached routes; zero blocks automatic movement that increases separation outside the escort envelope. Ordered missions retain their safety and execution rules.
- `SAINFollowerCoverFinder` weighs existing validated travel/player distances at intermediate values; fifty retains the established ranking. Shooting-lane/protective preferences and native validators remain. Determination does not expand discovery or probe budgets. Emergency/medical cover retains native recovery priority.
- `SainRegroupBridge` supplies shared role/map trigger and automatic completion. `SAINFollowerRegroupObjective` captures completion on activation; Cover Me uses its twenty-percent envelope while normal/tight orders keep their separate distances. Post-regroup cover constraints follow the completed envelope.
- Grunt boss support replaces the old pickup willingness threshold with positive resolved dependence. Useful fire, survival, medicine and explicit missions still retain priority. One hundred disables automatic boss diversion; ordinary ally cooperation is separate.
- Shooter keeps its firing-position/weapon role. Zero rejects automatic positions that increase separation beyond close escort; accepted Need Sniper retains its explicit support permissions. Local shooting cover and native point search keep their existing retry/failure budgets.
- Native SAIN remains the sole decision publisher. Recording reads configured/effective percentages, override presence, dependence and the existing cached arbitration state.

SeekCover includes travel, holding, shooting and suppression. Its enum alone cannot establish inactivity. Productive movement, useful firing, treatment and valid arrival holds remain protected; an unproductive settled objective can reach ordinary support/regroup arbitration. The independent blocked-push and intentionally empty-cover fixes remain part of [current combat](Combat.md).

## Implemented prerequisites

The blocked-push arbitration and intentionally empty-cover fallback corrections predate the slider. Their current contracts remain in [push objectives](Combat.md#push-objectives) and [bounded cover work](Combat.md#bounded-cover-work). Zero determination also constrains ordinary native MoveToEngage destinations and aggressive no-cover fallback; emergency/defensive handling and explicit missions retain priority.

## Evidence and qualification

Earlier raids motivating this work were `20261009-035237-Interchange` (Zver/Hawk separated 43/54 m after initial close contact), `20261009-060834-Interchange` (mixed push/reload/cover commitments separated Zver/Strelok roughly 42/44 m), and `20261009-171202-Woods` (Mattdokn remained healthy and stationary 41..54 m away with weapon-blocked automatic push and stale regroup evaluation). These demonstrate mixed commitment/liveness issues rather than proving that all separation is SAIN ambush behavior. Mattdokn's exact low-level readiness blocker remains unidentified.

Fixtures exercise real objective/publication boundaries, route-cache reuse, zero escort, one hundred independence and captured Cover Me arrival. State fixtures exercise production preference methods, mixed Core/addon lifetime, quiet restoration and legacy saves. Dated results belong in [Validation](Validation.md#2026-10-15---self-determination). Fresh raids must still qualify practical protection, sniper usefulness, recovery separation, repeated commands, navigation and measured frame time; no guard-parity claim follows from fixture success.
