# Weapon Pickup: Primary Slot Available

Date: 2026-07-10

Baseline commit: `f16caed` (`Implement limited follower gear swapping`)

## Purpose

This document tracks implementation and runtime verification of the weapon-pickup contract for one narrow scenario:

- the follower's `FirstPrimaryWeapon` slot is empty when pickup evaluation begins
- the candidate is a detachable-magazine weapon
- `SecondPrimaryWeapon` may be empty or occupied
- the candidate may come from a searched body, searched container, or commanded loose-item pickup

The destination is decided from the candidate's inserted magazine, compatible magazines in the follower's actual fast-access inventory, compatible magazines acquired with the candidate, and available support/cargo space.

This document is the implementation tracker. `docs/Looting.md` remains the broader authority for command assignment, search simulation, generic filtering, voice feedback, and return bookkeeping.

## Scope Boundaries

Included:

- ordinary magazine reference calculation
- inserted-magazine contribution
- compatible fast-access magazine contribution
- body/container magazine-first transfer flow
- promotion of a tracked backpack cargo weapon when a newly found compatible spare completes readiness
- loading a magazine when the candidate has no inserted magazine
- primary, support, backpack, or leave-at-source placement
- later promotion from support to primary
- loose weapon and magazine pickup integration
- existing `Simple`/`Restricted` return and `Immersive`/`Realistic` persistence rules

Excluded:

- replacing or comparing against an occupied primary weapon
- choosing between multiple competing candidate weapons
- using a cargo magazine to choose a newly found compatible weapon over an existing cargo weapon; this requires weapon comparison
- passively promoting backpack cargo weapons outside an active body/container loot command
- demoting a primary after ammunition is fired
- magazine repacking or loose-ammunition management
- internal-magazine, tube-fed, revolver, and chamber-fed weapon policy
- changing vanilla fast-access or reload behavior

## Terms

`Candidate weapon` is the weapon currently being evaluated.

`Fast access` is the game-owned set of equipment locations searched by vanilla detachable-magazine reload logic. In the current SPT client this is `Pockets` plus `TacticalVest`. Backpack magazines are cargo and never contribute to readiness.

`Compatible magazine` is a magazine accepted by the candidate's current magazine slot. Caliber alone is not sufficient.

`Ordinary reference` is the largest compatible magazine capacity up to a maximum of 30 rounds. A weapon supporting 10-, 20-, 30-, and 60-round magazines therefore has a 30-round ordinary reference. A weapon whose largest compatible magazine is 24 rounds has a 24-round reference.

`Primary-ready` means the candidate has at least two ordinary magazine equivalents of immediately usable ammunition.

`Support weapon` is an unready candidate stored in `SecondPrimaryWeapon`. It is an inert holding slot in this scenario, not a custom vanilla main-weapon override. The support slot is never displaced by this feature.

`Cargo` is a weapon or magazine held only for transport. Cargo does not contribute to readiness.

A `recruited backpack spare` is a compatible loose cargo magazine that an active weapon plan can physically move into vest or pockets. It contributes only after that transfer succeeds; while it remains in the backpack it contributes nothing.

## Frozen Readiness Rules

Let `R` be the ordinary reference and `T = 2 * R` be the readiness threshold.

Inserted-magazine contribution:

- no inserted magazine contributes `0`
- an inserted magazine below half full contributes its actual rounds
- an inserted magazine at least half full contributes at least `R`
- when the inserted magazine contains more than `R`, all of its actual rounds contribute
- exactly half full uses the at-least-half-full rule

Fast-access spare contribution:

- every compatible non-empty spare in actual fast access contributes its actual rounds
- partial magazines combine by round count
- incompatible, empty, backpack, and failed-transfer magazines contribute `0`

Decision:

- primary-ready when total contribution is at least `T`
- without an inserted magazine, at least one compatible non-empty fast-access magazine must also be loadable
- a planned transfer never counts until the transaction succeeds
- readiness is recalculated from current inventory state; no historical "magazines still needed" counter is kept

## Existing Reload-Space Invariant

The looting planner preserves a magazine-shaped landing space for the candidate's inserted magazine before accepting spare magazines into the tactical vest or pockets. This prevents a nominally accessible spare from producing a reload that cannot return the removed magazine to fast access.

Keep this safety invariant while implementing the new readiness calculation unless runtime evidence shows vanilla can complete the reload through another safe destination. Its grid-fit verification is separate from the round-count formula.

## Baseline Behavior

The `f16caed` implementation already provides:

- body/container candidate discovery
- magazine compatibility through the candidate's magazine slot
- magazine-first sequential inventory transactions
- tactical-vest shape simulation with reload landing-space reserve
- full inserted `60+` magazine exception
- empty secondary, backpack, then leave-at-source fallback
- looted-weapon registration and primary weapon-manager rebind
- item presentation refresh
- loadout-mode-specific return/persistence bookkeeping

Known baseline differences from this contract:

- one planned operational vest magazine currently makes the weapon primary regardless of round count
- existing compatible magazines already held by the follower are not counted
- only tactical-vest placement is planned even though vanilla fast access also includes pockets
- the destination is chosen before all planned transfers finish
- a candidate without an inserted magazine is not loaded during pickup handling
- support weapons are not promoted after later compatible-magazine acquisition
- commanded loose weapon pickup still delegates destination selection to EFT's generic pickup placement

## Implementation Phases

| Phase | Scope | Status |
|---|---|---|
| P1 | Central readiness model, template-capacity resolver, actual fast-access scanner, shadow diagnostics, deterministic formula tests | Complete |
| P2 | Body/container pickup with an inserted magazine; final destination from post-transfer live state | Implemented; runtime testing pending |
| P3 | No-inserted-magazine transaction and mandatory magazine load before primary equip | Not started |
| P4 | Commanded loose weapon/magazine pickup integration without changing non-weapon pickup or voice behavior | Not started |
| P5 | Support-to-primary reevaluation and safe promotion | Implemented; happy-path runtime passed |
| P6 | Failure hardening, ownership/return verification, final player documentation | Not started |

## Phase P1 Contract

P1 must not change live weapon destination decisions.

P1 deliverables:

- one side-effect-free round-contribution calculator
- one EFT adapter that derives the ordinary reference from compatible magazine templates
- one EFT adapter that snapshots compatible magazines from actual `Inventory.FastAccessSlots`
- cached ordinary-reference resolution per assembled magazine-slot filter signature
- a diagnostic snapshot containing reference, threshold, inserted state, inserted contribution, spare rounds, total contribution, readiness, and reason
- a projected diagnostic that may include planned fast-access transfers but is clearly labeled as projection rather than actual state
- deterministic Debug-build tests covering the required arithmetic scenarios

P1 acceptance:

- all deterministic formula scenarios pass during Debug plugin initialization
- client builds with no new warnings or errors
- current body/container destination logic remains unchanged
- runtime logs expose both actual readiness and planned projection for a candidate

## Phase P2 Contract

P2 changes destination decisions only for body/container candidates that already have an inserted detachable magazine. The no-inserted-magazine path remains on its isolated legacy behavior until P3.

P2 sequence:

1. plan compatible non-empty source magazines against the follower's tactical vest and pockets
2. preserve one shared fast-access landing space for the candidate's inserted magazine when readiness depends on a spare
3. execute each planned fast-access move as its own live inventory transaction
4. continue the chain when a planned magazine move fails, without counting that failed transfer
5. after all planned fast-access moves settle, recalculate readiness from the follower's actual inventory
6. place the weapon in primary when ready, otherwise use empty secondary; when secondary is occupied, return the weapon to ordinary filtered cargo evaluation

Occupied-secondary branch:

- when projected readiness is insufficient and secondary is occupied, do not move the candidate's magazines into fast access
- do not move the weapon into the backpack through the gear planner
- leave the candidate available to ordinary `Pickup Gear`, category, price, and backpack-fit evaluation
- when ordinary cargo filters reject it, leave it at the source
- this prevents `Allow Gear Swapping` from silently bypassing normal cargo policy after both equipment slots are unavailable

Cargo promotion branch:

- a tracked cargo weapon remains inert while only the weapon and its original spare are in the backpack
- discovering a new compatible loose magazine during a body/container search triggers a projected readiness and grid-fit check for that cargo weapon
- the new source magazine moves first, followed by compatible loose cargo magazines
- all required spares must fit in vest/pockets while preserving landing space for the cargo weapon's inserted magazine
- after the transfers settle, actual live readiness is checked again before the cargo weapon moves from backpack to primary
- sources containing another candidate weapon remain outside this promotion scenario because selecting between weapons requires the later comparison/swap policy

New-weapon backpack-spare branch:

- scan only loose compatible non-empty magazines in the follower backpack; never detach a magazine from another weapon tree
- evaluate source magazines first, then backpack magazines, against shared vest/pocket space and the inserted-magazine landing reserve
- use the combined plan only when its projected fast-access result makes the newly found weapon primary-ready
- when the combined result remains under threshold, retain backpack magazines as cargo and classify the weapon from source-only live state
- when the combined result is ready, move source magazines first, then recruited backpack magazines, and classify the weapon only after the live transactions settle

Secondary-source promotion branch:

- an under-threshold tracked weapon may remain inert in `SecondPrimaryWeapon` while its compatible spare remains backpack cargo
- a newly found compatible source spare triggers a combined source-plus-backpack projection for that support weapon
- the new source spare must start the chain successfully before any backpack cargo is reorganized
- after source and fitting backpack magazines move into fast access, actual live readiness is checked before moving second primary to first primary
- if the immediate slot move is temporarily blocked by hands/reload state, the existing idle secondary-promotion watcher can complete it after the loot command ends

P2 boundaries:

- existing compatible magazines already in pockets or tactical vest count
- backpack magazines never count
- projected transfers are diagnostic only and never choose the destination
- source magazines that cannot become fast access remain available to ordinary filtered looting except when reserved by the cargo-bundle branch
- no occupied equipment slot is displaced
- body and container commands use the same planner and post-transfer decision
- when a tracked secondary can become ready from newly found compatible magazines, promote it before evaluating any new weapon package from the same source
- after that promotion fills primary, other source weapons fall through to ordinary filtered cargo handling

## Phase P5 Contract

P5 handles the exact continuation of an under-threshold pickup when both long-gun slots were initially empty:

1. the body/container planner moves compatible spares into fast access while preserving reload landing space
2. if the resulting live ammunition total is still below `T`, the weapon goes into `SecondPrimaryWeapon`
3. the secondary weapon remains a physical holding item; pitFireTeam does not try to make vanilla treat a secondary-only long gun as the bot's main weapon
4. a later inventory change, including a player manually giving the follower another magazine, causes a periodic actual-inventory reevaluation
5. once compatible fast-access ammunition reaches `T`, the primary slot is still empty, and reload landing space remains valid, the tracked weapon moves from second primary to first primary
6. after that transaction succeeds, the shared looted-primary binder refreshes vanilla selector state, rebuilds `WeaponManager.Info[FirstPrimaryWeapon]`, and requests the normal main-weapon switch
7. when a tracked looted weapon already occupies second primary, the binder also creates its missing `WeaponManager.Info[SecondPrimaryWeapon]` after first primary exists, allowing vanilla to treat it as the actual support weapon

Promotion is blocked while the follower is dead, inactive, in combat, teleporting, reloading, changing hands, or executing another loot/pickup command. It never displaces an occupied primary and never counts backpack magazines.

## Formula Scenario Matrix

Unless specified otherwise, the ordinary reference is 30 and the threshold is 60.

| ID | Inserted magazine | Compatible fast-access spares | Expected | Formula | Runtime |
|---|---:|---:|---|---|---|
| WP-01 | 60/60 | none | Primary-ready | Debug passed | P1 shadow and existing path passed; retest P2 |
| WP-02 | 45/45 | none | Not ready | Debug passed | Pending P2 |
| WP-03 | 30/30 | 30 | Primary-ready | Debug passed | Pending P2 |
| WP-04 | 20/30 | 30 | Primary-ready | Debug passed | Pending P2 |
| WP-05 | 15/30 | 30 | Primary-ready | Debug passed | Pending P2 |
| WP-06 | 14/30 | 30 | Not ready | Debug passed | Pending P2 |
| WP-07 | 14/30 | 30 + 16 | Primary-ready | Debug passed | Pending P2 |
| WP-08 | none | 30 + 30 | Primary-ready; load required | Debug passed | Pending P3 |
| WP-09 | none | 20 + 20 + 20 | Primary-ready; load required | Debug passed | Pending P3 |
| WP-10 | 30/30 | backpack 30 only | Not ready | Debug passed | Pending P2 |
| WP-11 | support candidate below threshold, then gains sufficient fast-access rounds | Primary-ready after acquisition | Promote to primary | Debug passed | Runtime passed |
| WP-12 | 12/24 | 24 | Primary-ready with `R=24` | Debug passed | Pending P2 |
| WP-13 | 11/24 | 24 | Not ready with `R=24` | Debug passed | Pending P2 |
| WP-14 | 30/60 | none | Not ready | Debug passed | Pending P2 |
| WP-15 | 31/60 | 29 | Primary-ready | Debug passed | Pending P2 |

## Integration Scenario Matrix

| ID | Scenario | Expected | Phase | Status |
|---|---|---|---|---|
| WI-01 | More source magazines exist than fast access can hold | Successful fast-access transfers alone determine readiness; search continues | P2 | Implemented; runtime pending |
| WI-02 | One planned magazine transfer fails | Failed magazine contributes nothing; destination uses resulting live state | P2 | Implemented; runtime pending |
| WI-03 | Primary-ready while secondary is occupied | Candidate still goes to primary | P2 | Implemented; runtime pending |
| WI-04 | Not ready and secondary is empty | Candidate goes to support; primary remains empty | P2 | Runtime passed |
| WI-05 | Not ready, secondary occupied, ordinary `Pickup Gear` allows it, backpack fits | Candidate becomes ordinary filtered backpack cargo | P2 | Implemented; runtime pending |
| WI-06 | Not ready, secondary occupied, ordinary filters reject it or backpack is full | Candidate remains at source | P2 | Implemented; runtime pending |
| WI-07 | Compatible magazine exists only in backpack | It contributes nothing while in backpack; a successful move into fast access can then make it eligible | P2/P5 | P2 implemented; runtime pending |
| WI-08 | Candidate has no inserted magazine and loading succeeds | Load one, recalculate, then place | P3 | Not started |
| WI-09 | Candidate has no inserted magazine and loading fails | Never place it in primary | P3 | Not started |
| WI-10 | Compatible magazines arrive one at a time | Same final inventory state produces the same final decision | P4/P5 | P5 manual-transfer path passed; loose-pickup P4 pending |
| WI-11 | Primary becomes occupied before support promotion | Keep candidate in support | P5 | Implemented; runtime pending |
| WI-12 | Follower dies or enters combat during promotion | Do not start the move; dead/inactive completion does not rebind | P5/P6 | Implemented; runtime pending |
| WI-13 | `Simple` or `Restricted` follower extracts after equip | Weapon and supporting acquired magazines remain return cargo | P6 | Not started |
| WI-14 | `Immersive` or `Realistic` follower extracts after equip | Accepted equipment remains part of the saved kit | P6 | Not started |
| WI-15 | A newly found weapon and spare are compatible with a loose spare carried beside an existing cargo weapon | Compare the weapons before deciding which one receives the cargo spare | Future weapon swapping | Deferred |
| WI-16 | A tracked cargo weapon and spare are in backpack, then a new compatible source spare is found | Move the new spare and loose cargo spare into fast access with reload reserve, then promote the cargo weapon from live state | P2 | Implemented; runtime pending |

## Backpack Spare Scenario Matrix

| ID | Starting state | New source | Expected | Status |
|---|---|---|---|---|
| BS-01 | Compatible spare in backpack; primary and secondary empty | Weapon with a full inserted magazine | Move the backpack spare to fast access; equip primary when combined readiness passes | Implemented; runtime pending |
| BS-02 | Compatible spare in backpack; primary and secondary empty | Weapon with a low inserted magazine and no other spare | If combined readiness remains below threshold, keep the spare in backpack and place the weapon in secondary | Implemented; runtime pending |
| BS-03 | Compatible spare in backpack; primary and secondary empty | Weapon with a low inserted magazine plus a compatible source spare | Move source spare first, then backpack spare; equip primary when combined readiness passes | Implemented; runtime pending |
| BS-04 | BS-02 has already placed the tracked weapon in secondary | A later compatible source spare | Move source spare first, then backpack spare; promote secondary to primary after live readiness passes | Implemented; runtime pending |

## Runtime Test Coverage

This ledger records observed in-raid results. `Implemented` elsewhere in this document does not mean runtime-tested unless the scenario is listed as passed here.

| ID | Runtime setup | Observed result | Status |
|---|---|---|---|
| RT-01 | Debug startup readiness formula suite | All 15 deterministic arithmetic scenarios passed | Passed |
| RT-02 | `R=30`, inserted `10/30`, one compatible `30/30` source spare | Projected and final total was `40/60`; spare moved to fast access and weapon went to second primary | Passed |
| RT-03 | Full `60/60` inserted magazine with no compatible spare | Inserted contribution alone reached the threshold and weapon went to primary | Passed |
| RT-04 | Enough vest space for all accepted compatible source magazines | Weapon and all planned magazines moved successfully | Passed on the earlier phase-1 planner |
| RT-05 | Only partial vest space for accepted compatible source magazines | Weapon was accepted and only magazines that fit the operational plan moved | Passed on the earlier phase-1 planner |
| RT-06 | No usable vest space; compatible magazines fell back to backpack | Vanilla bot reload did not use the backpack magazines | Confirmed limitation; policy changed |
| RT-07 | Under-threshold weapon in second primary later received another compatible fast-access spare | Readiness changed from `40/60` to `70/60`; weapon promoted into first primary | Passed |
| RT-08 | RT-07 after promotion | Follower switched to the promoted weapon and used it during combat | Passed |
| RT-09 | Corpse had two weapons; first became under-threshold secondary, second shotgun was also taken | Gear planner incorrectly built an automatic backpack cargo bundle for the shotgun | Bug reproduced; fixed, retest pending |
| RT-10 | Existing secondary had compatible magazines on a corpse that also contained another weapon package | Existing secondary was evaluated first; a newly found `30/30` spare raised readiness from `42/60` to `72/60`, promoted it to primary, and left the other corpse weapons to ordinary cargo rules | Passed |
| RT-11 | Existing secondary had no compatible source magazines; corpse contained a different ready weapon package | Existing `4/10` secondary was left unchanged; new `22/30` weapon moved three compatible `30/30` spares into fast access, reached `120/60`, and entered primary | Passed |
| RT-12 | A tracked looted secondary was physically present when a new first primary became available | Shared binder created `WeaponManager.Info[SecondPrimaryWeapon]`; runtime log reported `supportSlot=SecondPrimaryWeapon` and `canChange=True` | Passed |
| RT-13 | Existing secondary occupied; new weapon had `12/30` inserted and three compatible `30/30` source spares, but no spare could fit fast access with reload reserve | All three spares failed `fastAccessFit`; projected readiness remained `12/60`; gear planner rejected the weapon to ordinary cargo handling | Passed |
| RT-14 | Existing secondary occupied; new weapon had `5/30` inserted and one compatible `30/30` source spare with sufficient fast-access space | Spare successfully planned for the vest, but projected readiness was only `35/60`; gear planner rejected the under-threshold weapon package without moving it | Passed |

Not yet runtime-covered:

- `BS-01`: full inserted magazine plus compatible backpack spare recruited into fast access
- `BS-02`: low inserted magazine plus backpack spare remains under threshold, leaving the spare in backpack and weapon in second primary
- `BS-03`: low inserted magazine plus source spare plus backpack spare becomes primary-ready
- `BS-04`: a later source spare recruits backpack cargo and promotes an existing tracked secondary
- equivalent backpack-recruitment and secondary-promotion paths from searchable containers
- occupied-secondary rejection with `Pickup Gear` disabled, enabled-but-price-rejected, and enabled-with-valid-backpack-cargo cases
- promotion while primary becomes occupied, hands become busy, combat starts, or the follower dies during the sequence
- post-raid return/persistence after a secondary-to-primary promotion in each loadout-management mode
- no-inserted-magazine weapons, tube-fed/internal-magazine shotguns, revolvers, and loose-ammunition feed policy

## Diagnostic Contract

Readiness diagnostics use the `[LootCommand][Readiness]` prefix.

Each snapshot should identify:

- follower and candidate weapon
- evaluation kind: `actual` or `plannedProjection`
- ordinary reference and threshold
- inserted rounds, capacity, and contribution
- compatible fast-access magazine count and total rounds
- projected additional fast-access rounds, when applicable
- total contribution
- `primaryReady`
- `requiresMagazineLoad`
- stable reason code

The final placement phases must log one additional post-transfer `actual` snapshot immediately before selecting primary, support, backpack, or leave-at-source.

## Progress Log

### 2026-07-10

- Created the dedicated phase and scenario tracker.
- Began P1 against baseline `f16caed`.
- Confirmed vanilla `Inventory.FastAccessSlots` contains `Pockets` and `TacticalVest`.
- Confirmed vanilla `BotReload.GetMagazineForReload(...)` searches reachable fast-access magazines and validates the move against the candidate magazine slot.
- Added the centralized readiness formula, compatible-template reference resolver, actual fast-access scanner, and planned-projection diagnostics.
- Added all 15 formula scenarios as Debug startup self-tests.
- Verified the Debug client build with zero warnings and zero errors before runtime testing.
- Verified the in-game Debug startup self-test passed all 15 formula scenarios.
- Verified a live body candidate with `10/30` inserted and one projected `30/30` compatible spare resolved `R=30`, `T=60`, and `40` total contribution, producing `primaryReady=False`.
- Verified the template resolver found 20 compatible magazine templates and the second diagnostic reused the cached reference.
- Completed P1 without changing the existing weapon destination decision.
- Verified a live `60/60` inserted-magazine candidate with no compatible spare resolved `60/60`, produced `primaryReady=True`, and used the existing full-60 primary path. One unrelated `21/21` magazine at the source was correctly rejected as incompatible.
- Implemented P2 for body and container candidates that already have an inserted magazine.
- Extended operational magazine placement from tactical vest only to vanilla fast access: pockets plus tactical vest.
- Preserved one shared reload landing space across both fast-access containers whenever readiness depends on spare magazines.
- Changed the weapon destination to use a final post-transfer `actual` readiness snapshot after all planned fast-access transactions settle.
- Kept failed magazine transactions out of readiness while allowing the remaining chain and final classification to continue.
- Kept backpack magazines and pre-transfer projections non-authoritative.
- Added the occupied-secondary cargo branch: an unready weapon and fitting compatible spares go to the backpack without staging those spares in vest or pockets.
- Marked all magazines reserved by that cargo decision as handled so non-fitting spares remain at the source instead of leaking into generic looting.
- Excluded magazines installed inside any weapon tree so cargo promotion cannot strip another weapon's inserted magazine.
- Added the inverse cargo-promotion scenario: a newly found compatible spare can complete a tracked backpack weapon using its existing loose cargo spare.
- Required the new source magazine to start the promotion chain and retained the post-transfer live readiness check before moving the cargo weapon to primary.
- Confirmed vanilla does not treat a secondary-only long gun as a normal main/support weapon when `FirstPrimaryWeapon` is empty, so pitFireTeam no longer attempts to override that spawn-time assumption.
- Implemented periodic reevaluation of a tracked looted weapon held in `SecondPrimaryWeapon` when `FirstPrimaryWeapon` remains empty.
- A later compatible magazine in vest or pockets now promotes the support weapon only after actual live readiness reaches `T` and reload landing space remains available.
- Promotion uses a real second-primary-to-first-primary inventory transaction, then the same centralized primary registration and switch path used by immediate looting.
- Runtime verified the complete support-promotion path with `R=30`, `T=60`: a `10/30` inserted magazine plus one `30/30` spare produced `40` and placed the weapon in second primary; a later manually transferred `30/30` compatible spare produced `70` and promoted it to first primary.
- Verified the promoted weapon switched into the follower's hands and was selected and fired normally during subsequent combat.
- Added readiness-gated recruitment of compatible loose follower-backpack magazines for a newly found weapon. Backpack cargo is moved only when the combined executable plan produces a primary-ready weapon.
- Added source-triggered promotion for a tracked secondary weapon: a later found spare moves first, compatible backpack cargo follows, and the support weapon promotes only after actual readiness passes.
- Removed the gear planner's automatic weapon-and-magazine backpack bundle when secondary is occupied. Rejected candidates now fall through to ordinary `Pickup Gear` and price rules.
- Moved tracked-secondary readiness evaluation ahead of new weapon candidates so newly found compatible magazines complete the existing weapon before any future weapon-comparison policy is required.
