# Self-Determination

**Status:** implemented; raid calibration remains pending. Core owns saved preferences, command overrides and shared policy. The [SAIN addon adaptation](../addon/docs/Self-Determination.md) describes its follower-only integration. See [combat tactics](Combat-Tactics.md), [commands](Commands.md) and [proficiency](Friendly-AI-Performance-Settings.md) for the surrounding contracts.

Self-Determination measures tactical dependence on the player. Aggression measures offensive willingness and retains its existing health, gear, ammunition, enemy-number and threat checks. Determination does not alter perception, accuracy, firing permission or command acceptance probability.

## Settings and commands

Saved teammates have a Self-Determination slider under Proficiency, from 0 to 100, default 50. Raid pickups use a fixed 50 baseline; their existing command acceptance personality is separate. Missing or nonfinite values resolve to 50; finite values clamp to 0..100. Tactic changes preserve the saved value and clear old combat overrides. Reset in Proficiency restores determination to 50.

| Combat command | Determination | Aggression |
|---|---|---|
| On Your Own | 100% | Unchanged; Going Rogue Goons: 70% |
| Cover Me | Exactly 20% | Unchanged |
| Hold Position | Unchanged | 0% |
| Go Forward | Unchanged | 100% |
| Go Go Go | Initially configured value | Initially configured value |
| Attention | Initially configured value | Initially configured value |

Overrides are follower-local and survive enemy changes, individual kills, action changes and individual follower handoffs. Core and addon combat registrations are tracked separately, including during a brain switch. After the whole squad has no registered combat or pending EFT combat context for two seconds, both overrides reset. This quiet confirmation preserves existing handoff grace; repeated polling does not renew an already completed reset. Go Go Go and Attention clear both immediately. Commands never overwrite saved percentages. Going Rogue registers an optional command policy for Knight, Big Pipe and Birdeye: combat On Your Own sets aggression to exactly 70% alongside the existing 100% determination override. These use the same follower-local lifetime and reset; peaceful use and ordinary followers retain their existing behavior.

Cover Me cancels conflicting push pressure and issues one regroup using the 20% completion envelope. Even a saved value of zero becomes exactly 20. Repeating the same Cover Me override does not restart the regroup or its hold. Its completion distance is captured on activation, so changing a percentage cannot expand a productive leg's arrival envelope. Ordinary and tight Regroup retain their command-owned distances. Accepted explicit destinations retain the real player anchor even at 100; percentage resets do not erase that anchor.

Go Forward retains existing tactic eligibility and ordered-push execution. Rejected directed orders do not set the aggression override. Peaceful commands retain their movement meaning; peaceful patrol does not supply a combat percentage override. Outside combat and at the next battle's entry, percentages resolve to their saved values unless an optional companion runtime baseline is active as described below.

## Policy and initial calibration

Optional companion brains may supply runtime aggression/determination through `SetCompanionCombatPreferences` and must clear them on release. Effective values resolve in this order: player command override, companion runtime baseline, saved preferences (or the normal pickup baseline). Command resets clear only their own layer; they do not write saved values or erase an active companion baseline. Core has no role-specific policy here. Going Rogue owns the [Knight-led unit composition and death rules](../going-rogue/docs/Knight-Combat.md#solo-protection-and-goon-unit-initiative); without that baseline the existing table and reset behavior apply unchanged.

A companion may also supply a runtime leader with `SetCompanionCombatLeader`. `FollowerCombatAnchor` validates that the leader is alive, active, finite and registered to the same human owner before using it ahead of ordinary nearest-ally anchoring. Ordinary peaceful `FollowAction` uses that leader's player object without changing the native boss/group link; a self-led commander still follows the human peacefully. Pending commands, consumed player Regroup and dependent determination retain the real human anchor. Clearing companion preferences also clears the leader reference. Absent or invalid references leave ordinary following available. Marksman automatic distance-regroup is disabled at full independence; explicit player Regroup uses its existing separate path.

Regroup remembers whether activation was automatic. When full independence makes an automatic regroup obsolete, its own ordinary regroup action may complete through the normal objective lifecycle after protected medical, weapon/grenade work and airborne movement. Explicit/consumed player Regroup is excluded. Shared medical actions are evaluated by their existing end handler before this completion check. This removes only the regroup's local target/claim/move state and does not clear enemy memory.

`shared/SelfDeterminationPolicy.cs` supplies normalization, influence and distance calibration. The dependence weight is `2 * (1 - determination / 100)`: 2 at zero, 1 at fifty and 0 at one hundred. It scales the existing player-separation cost independently of aggression. Both brains reuse cached navigation rather than recomputing routes merely because an intermediate percentage changes.

Zero is close escort, including for Marksman: automatic advancement cannot increase player separation beyond the escort envelope. Immediate useful fire, medical/survival work and accepted explicit orders keep their existing priority. Fifty targets a balanced guard; one hundred uses existing independent combat routing and disables automatic boss-distance regroup and boss-protection diversion. Ordinary ally support retains its own admission. The old pickup protection threshold and radius multiplier no longer distort the fixed-50 baseline. Intermediate dependence does not encounter a binary protection cutoff.

Let `R50` be the role's existing trigger before pickup-personality multiplication: the configured Rifleman radius (clamped 10..38 m), or the existing map-dependent Marksman multiplier. Trigger interpolates from 10 m at zero to `R50` at fifty, then toward `2 * R50` below one hundred. At one hundred, the trigger is unused because automatic boss regroup is disabled.

Automatic completion interpolates from 6 m at zero to 10 m for Rifleman / the existing ordered Marksman distance at fifty, then toward the role/map ordered distance. It is capped at `trigger - 2 m`. Automatic regroup and Cover Me capture this completion at activation and share it with arrival-cover selection and settle tests.

Example on an ordinary map with the default 18 m configured radius:

| Determination | Rifleman trigger / completion | Marksman trigger / completion |
|---|---|---|
| 0% | 10 / 6 m | 10 / 6 m |
| 25% | 14 / 8 m | 18.5 / 15 m |
| 50% | 18 / 10 m | 27 / 24 m |
| 75% | 27 / 14 m | 40.5 / 24 m |
| 100% | Automatic boss regroup disabled | Automatic boss regroup disabled |

These are fallback thresholds, not leashes that cancel active fire or an accepted push. They are initial calibration, not measured equivalence to native guards. The 50% behavioral reference is vanilla `FollowerBullyProtectLayer`: immediate combat first, useful cover around its boss, fresh-contact pursuit and otherwise holding/returning. Installed followerbully settings use a 25 m boss-cover bound, 1.5 s group-sighting pursuit check, 8.5 s other protection sight-history check and a squared-distance fallback of 100 (10 m). Core preserves its own richer threat and commitment rules rather than copying those timers.

## Stability and persistence

- Determination affects ordinary planning/reselection. A progressing route, useful shot/support opportunity, medical work or valid arrival hold retains ownership.
- Blocked automatic advancement can yield to existing support/regroup arbitration. Returning cannot erase a failed approach or immediately authorize the same outward leg from unchanged knowledge.
- Floor, reachability, reservations, anti-crowding, threat and shot-safety gates remain authoritative. Emergency escape and medical cover may lead away from the player.
- Cover enumeration, collider search bounds and native finder cadences do not grow with the percentage. Recorder percentages and commitment diagnostics are passive.
- Settings store determination alongside aggression. Profile options and spawn details carry it; the proficiency-save request accepts an optional determination field, preserving it when omitted by older clients.
- The editor captures account identity, proficiency clone and determination together before its existing 0.35 s debounce. Percentage signs are separate labels beside plain numeric inputs; numeric formats remain unchanged.

Validation is recorded in [the evidence ledger](../addon/docs/Validation.md#2026-10-15---self-determination). Fixtures/builds do not establish Unity navigation, perceived protection, frame time, guard parity or UI layout. Qualify low/high determination with low/high aggression, mixed brains, building/stair routes, blocked weapons, repeated Cover Me, accepted orders at 100 and post-combat restoration in a fresh raid.
