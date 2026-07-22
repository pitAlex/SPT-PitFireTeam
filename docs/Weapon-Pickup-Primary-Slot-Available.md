# Weapon Pickup: Primary Slot Available

Date: 2026-07-10

Baseline commit: `f16caed` (`Implement limited follower gear swapping`)

## Purpose

This document tracks implementation and runtime verification of the weapon-pickup contract for one narrow scenario:

- the follower's `FirstPrimaryWeapon` slot is empty when pickup evaluation begins
- the candidate is a detachable-magazine weapon or one of the explicitly implemented loose-feed weapon types
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
- internal-magazine readiness, loading, loose-ammunition carry planning, and later promotion
- internal-magazine revolver-cylinder reserve compatibility without requiring a separate chamber slot
- non-launcher `OnlyBarrel` single- and multi-chamber readiness, one-round staging, loose-ammunition carry planning, and later promotion
- non-holster revolver classification by weapon class, including shotgun, launcher, and custom rifle/sniper revolver mechanisms
- first-primary reload from magazines successfully acquired with the looted weapon package
- existing `Simple`/`Restricted` return and `Immersive`/`Realistic` persistence rules

Excluded:

- replacing or comparing against an occupied primary weapon
- choosing between multiple competing candidate weapons
- using a cargo magazine to choose a newly found compatible weapon over an existing cargo weapon; this requires weapon comparison
- choosing between multiple simultaneously ready backpack cargo weapons without a comparison policy
- demoting a primary after ammunition is fired
- detachable-magazine top-off and partial-magazine repacking
- holster-revolver gear policy and launcher-specific hands/combat-reload policy
- general changes to vanilla fast-access or reload behavior outside the accepted looted-primary package

## Terms

`Candidate weapon` is the weapon currently being evaluated.

`Fast access` is the game-owned set of equipment locations searched by vanilla detachable-magazine reload logic. In the current SPT client this is `Pockets` plus `TacticalVest`. Backpack magazines are cargo and never contribute to readiness.

`Compatible magazine` is a magazine accepted by the candidate's current magazine slot whose every loaded cartridge is also accepted by the weapon. Mechanical fit alone is insufficient because shared magazine families can hold ammunition for different weapon calibers. A mixed magazine is operational only when every remaining cartridge is compatible.

`Ordinary reference` is the largest capacity among the compatible loaded magazines actually available to the current live or projected plan, capped at 30 rounds. It includes the inserted magazine, compatible magazines already in fast access, and compatible magazines proven to fit through the active transfer plan. Magazine templates that are merely compatible with the weapon do not affect readiness.

`Primary-ready` means the candidate has at least two ordinary magazine equivalents of immediately usable ammunition. For five-round magazines, those equivalents are counted by magazine state rather than by combining rounds.

`Support weapon` is an unready candidate stored in `SecondPrimaryWeapon`. It is an inert holding slot in this scenario, not a custom vanilla main-weapon override. The support slot is never displaced by this feature.

`Cargo` is a weapon or magazine held only for transport. Cargo does not contribute to readiness.

A `recruited backpack spare` is a compatible loose cargo magazine that an active weapon plan can physically move into vest or pockets. It contributes only after that transfer succeeds; while it remains in the backpack it contributes nothing.

## Frozen Readiness Rules

Let `R` be the ordinary reference and, for magazines larger than five rounds, let `T = 2 * R` be the readiness threshold.

Five-round magazines use a full-magazine-equivalent rule. An inserted `4/5` or `5/5` magazine counts as one full equivalent. An inserted magazine at `3/5` or below does not count as a full equivalent, so two full `5/5` spares are required. Partial spare magazines never combine into a full equivalent. Therefore `2/5 + 5/5 + 5/5`, `3/5 + 5/5 + 5/5`, `4/5 + 5/5`, and `5/5 + 5/5` are ready, while `3/5 + 5/5` and `5/5 + 4/5` are not.

Inserted-magazine contribution for magazines larger than five rounds:

- no inserted magazine contributes `0`
- an inserted magazine below half full contributes its actual rounds
- an inserted magazine at least half full contributes at least `R`
- when the inserted magazine contains more than `R`, all of its actual rounds contribute
- exactly half full uses the at-least-half-full rule

Fast-access spare contribution:

- every mechanically compatible, non-empty spare whose loaded cartridges all match the weapon contributes its actual rounds
- partial magazines combine by round count
- wrong-caliber, mixed-incompatible, empty, backpack, and failed-transfer magazines contribute `0`
- an incompatible cartridge in the inserted magazine makes the weapon unready until an explicit later operation replaces or unloads that magazine

Decision for magazines larger than five rounds:

- primary-ready when total contribution is at least `T`
- without an inserted magazine, at least one compatible non-empty fast-access magazine must also be loadable
- a planned transfer never counts until the transaction succeeds
- readiness is recalculated from current inventory state; no historical "magazines still needed" counter is kept

Decision for five-round magazines:

- an inserted magazine at `4/5` or `5/5` contributes one full magazine equivalent
- an inserted magazine at `3/5` or below contributes no full magazine equivalent
- each compatible `5/5` fast-access spare contributes one full magazine equivalent
- partial spare rounds do not combine into a full magazine equivalent
- the weapon is ready at two full magazine equivalents

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
| P1 | Central readiness model, available-magazine reference resolver, actual fast-access scanner, shadow diagnostics, deterministic formula tests | Complete |
| P2 | Body/container pickup with an inserted magazine; final destination from post-transfer live state | Implemented; runtime testing pending |
| P3 | No-inserted-magazine transaction and mandatory magazine load before primary equip | Implemented for body/container sources; staged insertion and overflow salvage paths passed separately at runtime |
| P4 | Commanded loose weapon/magazine pickup integration without changing non-weapon pickup or voice behavior | Implemented; runtime retest pending |
| P5 | Support-to-primary reevaluation and safe promotion | Implemented; happy-path runtime passed |
| P6 | Failure hardening, ownership/return verification, final player documentation | Not started |
| P7 | Internal-magazine readiness, real tube loading, loose-ammo carry planning, and later promotion | Implemented; ready-primary and partial-fit runtime paths passed |
| P10 | Grenade-launcher secondary-slot preference and forced conventional-primary normalization | Implemented; runtime testing pending |
| P11 | Primary-candidate detachable-magazine top-off from compatible loose source ammunition | Implemented; tracked-secondary top-off and promotion runtime path passed |
| P12 | Equipped-primary tactical loose-ammo weighting and fast-access magazine top-off | Implemented; runtime testing pending; cartridge replacement deferred |

## Phase P1 Contract

P1 must not change live weapon destination decisions.

P1 deliverables:

- one side-effect-free round-contribution calculator
- one EFT adapter that derives the ordinary reference from compatible loaded magazines participating in the live or projected plan
- one EFT adapter that snapshots compatible magazines from actual `Inventory.FastAccessSlots`
- reference resolution from the inserted magazine plus compatible actual or planned fast-access magazines
- a diagnostic snapshot containing reference, threshold, inserted state, inserted contribution, spare rounds, total contribution, readiness, and reason
- a projected diagnostic that may include planned fast-access transfers but is clearly labeled as projection rather than actual state
- deterministic Debug-build tests covering the required arithmetic scenarios

P1 acceptance:

- all deterministic formula scenarios pass during Debug plugin initialization
- client builds with no new warnings or errors
- current body/container destination logic remains unchanged
- runtime logs expose both actual readiness and planned projection for a candidate

## Phase P2 Contract

P2 changes primary/support destination decisions only for body/container candidates that already have an inserted detachable magazine. Candidates without an inserted magazine use the separate P3 transaction below.

P2 sequence:

1. plan compatible non-empty source magazines against the follower's tactical vest and pockets
2. preserve one shared fast-access landing space for the candidate's inserted magazine when readiness depends on a spare
3. execute each planned fast-access move as its own live inventory transaction
4. continue the chain when a planned magazine move fails, without counting that failed transfer
5. after all planned fast-access moves settle, recalculate readiness from the follower's actual inventory
6. place the weapon in primary when ready, otherwise use empty secondary; when secondary is occupied, return the weapon to ordinary filtered cargo evaluation

Empty-primary, occupied-secondary branch:

- when projected readiness is insufficient and secondary is occupied, do not move the candidate's magazines into fast access
- when `Pickup Gear` is enabled and the whole weapon tree passes minimum/maximum price, treat the under-ready candidate and all compatible loaded source magazines as a potential-weapon cargo package
- cargo permission comes from the ordinary gear and price filters; `Allow Gear Swapping` does not bypass them
- preflight the package and move magazines first so later loot can complete the weapon
- if the complete package cannot fit, leave its magazines at the source and try the weapon alone as cargo
- when `Pickup Gear` is disabled or the weapon fails price, leave the weapon and its package magazines at the source

Working-primary support-add branch:

- when first primary already contains a working weapon and second primary is empty, an accepted long gun with an inserted magazine and usable ammunition may be added directly as the real support weapon
- compatible loaded source magazines join only when they fit in tactical vest or pockets while preserving reload landing space for the inserted magazine
- source magazines that do not fit fast access remain at the source; they are not converted into automatic backpack cargo
- after the weapon reaches second primary, refresh vanilla weapon slots and create `WeaponManager.Info[SecondPrimaryWeapon]` without switching hands away from the working primary
- once first and second primary are occupied, later long guns use ordinary filtered cargo only; their magazines no longer inherit the future-primary package bypass
- an occupied holster applies the same ordinary-cargo rule to later pistols and pistol magazines

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
- if the immediate slot move is temporarily blocked by hands/reload state, retain that exact validated weapon and retry its slot transaction after loot completion, the two-second Attention-style reset, and bounded hands-state checks; the combat-gated idle watcher remains only a fallback

P2 boundaries:

- existing compatible magazines already in pockets or tactical vest count
- backpack magazines never count
- projected transfers are diagnostic only and never choose the destination
- source magazines that cannot become fast access remain available to ordinary filtered looting except when reserved by the cargo-bundle branch
- no occupied equipment slot is displaced
- body and container commands use the same planner and post-transfer decision
- when a tracked secondary can become ready from newly found compatible magazines, promote it before evaluating any new weapon package from the same source
- after that promotion fills primary, other source weapons fall through to ordinary filtered cargo handling

## Phase P3 Contract

P3 handles a detachable-magazine weapon whose magazine slot is empty when it is found in a body or searchable container.

P3 sequence:

1. scan compatible non-empty loose source magazines without detaching magazines from another weapon
2. prefer the source magazine with the most loaded rounds as the insertion candidate
3. execute a real `InteractionsHandlerClass.Move(...)` into `weapon.GetMagazineSlot().CreateItemAddress()` as a staging transaction
4. leave the weapon unattempted so the next ordinary planning pass sees its live inserted magazine
5. let the existing inserted-magazine planner reserve reload landing space and plan remaining compatible source magazines into fast access
6. evaluate readiness and destination from the same live/projected rules used by every already-loaded weapon
7. move accepted spares one at a time and classify the weapon from settled live inventory
8. move the weapon last, then use the existing primary/support bind path when its destination is usable equipment
9. after normal weapon planning, preflight and salvage ammunition from compatible source magazines that remain behind because no reload-safe fast-access slot was available

P3 boundaries:

- the insertion magazine must come from the same body/container source; manually supplied follower cargo is not borrowed for this first load
- insertion itself is not readiness-gated; the normal loaded-weapon policy decides whether the resulting weapon is primary-ready, held as secondary, or considered for cargo
- under-ready ordinary cargo remains governed by `Pickup Gear`, whole-tree price, and backpack package fit
- build-time load-operation rejection returns to ordinary cargo planning
- a runtime load failure does not move the remaining magazines or weapon
- overflow-ammo salvage fully preflights destination capacity per magazine before it starts, runs only while the weapon remains in `FirstPrimaryWeapon`, and does not make the loaded source magazine itself into cargo; EFT still commits each generated loose stack as its own inventory transaction, so an interruption or runtime transaction failure stops the remaining salvage rather than pretending the completed transactions can be rolled back atomically
- holster-revolver gear policy, launcher-specific feed, detachable-magazine top-off, and magazine-repacking policies remain deferred
- commanded loose-world weapon pickup does not recruit a separate world magazine; that command still handles only the item explicitly targeted by the player

## Phase P4 Contract

P4 gives commanded loose long guns an explicit destination instead of allowing EFT's generic pickup order to choose the first available weapon slot.

Destination order:

1. if actual readiness passes, reload landing space is safe, and first primary is empty, move to `FirstPrimaryWeapon`
2. otherwise move to empty `SecondPrimaryWeapon` as the visible under-ready holding state
3. otherwise move to backpack cargo
4. if support and backpack are unavailable while first primary is empty, permit `FirstPrimaryWeapon` only when the inserted magazine is not dangerously low
5. otherwise leave the weapon at the source and report negative

The last-resort minimum is half, rounded up, of the smaller of the inserted magazine capacity and ordinary reference. Examples with `R=30`:

- `15/30`, `15/60`, and full `5/5` pass the last-resort floor
- `14/30`, `14/60`, and no inserted magazine fail it

This placement policy is part of the direct player command and does not depend on `Allow Gear Swapping`, `Pickup Gear`, or body/container price filters. A long gun physically placed in `FirstPrimaryWeapon` is always registered as the bot's primary so the right-shoulder visual state remains truthful. After pickup ownership clears, selection waits two seconds for inventory and interaction state to settle, applies the same soft recovery reset used by `Attention`, and then enters the vanilla binding/selection path.

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
| WP-16 | 2/5 | 5 + 5 | Primary-ready with two full spare equivalents | Debug pending | Runtime pending |
| WP-17 | 3/5 | 5 + 5 | Primary-ready with two full spare equivalents | Debug pending | Runtime pending |
| WP-18 | 4/5 | 5 | Primary-ready with inserted plus spare equivalents | Debug pending | Runtime pending |
| WP-19 | 5/5 | 5 | Primary-ready with inserted plus spare equivalents | Debug pending | Runtime pending |
| WP-20 | 5/5 | 4 | Not ready; partial spare does not form an equivalent | Debug pending | Runtime pending |
| WP-21 | 3/5 | 5 | Not ready; only one full equivalent | Debug pending | Runtime pending |

## Integration Scenario Matrix

| ID | Scenario | Expected | Phase | Status |
|---|---|---|---|---|
| WI-01 | More source magazines exist than fast access can hold | Successful fast-access transfers alone determine readiness; search continues | P2 | Implemented; runtime pending |
| WI-02 | One planned magazine transfer fails | Failed magazine contributes nothing; destination uses resulting live state | P2 | Implemented; runtime pending |
| WI-03 | Primary-ready while secondary is occupied | Candidate still goes to primary | P2 | Implemented; runtime pending |
| WI-04 | Not ready and secondary is empty | Candidate goes to support; primary remains empty | P2 | Runtime passed |
| WI-05 | Not ready, secondary occupied, `Pickup Gear` enabled, whole weapon tree passes price, and package fits | Weapon plus compatible loaded source magazines become filtered backpack cargo | P2 | Implemented; runtime pending |
| WI-06 | Potential package does not fit | Leave package magazines at source, try weapon-only cargo, and leave weapon too when it cannot fit | P2 | Implemented; runtime pending |
| WI-07 | Compatible magazine exists only in backpack | It contributes nothing while in backpack; a successful move into fast access can then make it eligible | P2/P5 | P2 implemented; runtime pending |
| WI-08 | Candidate has no inserted magazine and loading succeeds | Load one, recalculate, then place | P3 | Passed |
| WI-09 | Candidate has no inserted magazine and loading fails | Prefer the weapon-and-compatible-magazine backpack package; if it does not fit, leave the magazines and try the weapon alone; leave the weapon too when it cannot fit | P3 | Cargo safety implemented; runtime pending |
| WI-10 | Compatible magazines arrive one at a time | Same final inventory state produces the same final decision | P4/P5 | Secondary manual-transfer path passed; cargo manual/loose-pickup fix implemented, runtime pending |
| WI-11 | Primary becomes occupied before support promotion | Keep candidate in support | P5 | Implemented; runtime pending |
| WI-12 | Follower dies or enters combat during promotion | Do not start the move; dead/inactive completion does not rebind | P5/P6 | Implemented; runtime pending |
| WI-13 | `Simple` or `Restricted` follower extracts after equip | Weapon and supporting acquired magazines remain return cargo | P6 | Not started |
| WI-14 | `Immersive` or `Realistic` follower extracts after equip | Accepted equipment remains part of the saved kit | P6 | Not started |
| WI-15 | A newly found weapon and spare are compatible with a loose spare carried beside an existing cargo weapon | Compare the weapons before deciding which one receives the cargo spare | Future weapon swapping | Deferred |
| WI-16 | A tracked cargo weapon and spare are in backpack, then a new compatible source spare is found | Move the new spare and loose cargo spare into fast access with reload reserve, then promote the cargo weapon from live state | P2 | Implemented; runtime pending |
| WI-17 | One compatible magazine was manually placed through `View Backpack`, then another was acquired through a command | Only the command-acquired magazine may contribute to readiness; the manually placed magazine remains strict cargo | P2 | Implemented; runtime pending |
| WI-18 | A strict-cargo weapon or magazine is removed through `View Backpack`, then acquired through `Loot This` | Removal clears strict provenance for the complete removed tree; commanded reacquisition may participate in gear readiness | P2 | Implemented; runtime pending |
| WI-19 | Commanded loose long gun is primary-ready and first primary is empty | Move directly to first primary, then release pickup state and bind/select it | P4 | Implemented; runtime pending |
| WI-20 | Commanded loose long gun is under-ready and second primary is empty | Move to second primary; do not register it as current primary | P4 | Implemented; runtime pending |
| WI-21 | Commanded loose long gun cannot use primary and second primary is occupied | Move to backpack cargo when it fits | P4 | Implemented; runtime pending |
| WI-22 | Commanded loose long gun has no support/backpack destination but has a safe inserted-magazine floor | Use first primary as a last resort and register/select it | P4 | Implemented; runtime pending |
| WI-23 | Commanded loose long gun has no support/backpack destination and its inserted magazine is dangerously low or absent | Leave at source and report negative | P4 | Implemented; runtime pending |
| WI-24 | Working primary, `Pickup Gear` enabled, empty second primary, found usable long gun plus compatible source magazines | Move only fitting reload-safe magazines to fast access, equip/register the weapon as second-primary support, announce `LootGeneric`, and keep the working primary in hand | Support add | Runtime passed |
| WI-25 | First and second primary occupied, found long gun passes ordinary gear/price filters but its magazines do not | Move only the weapon as ordinary cargo; do not grant its magazines the future-primary package bypass | Cargo boundary | Implemented; runtime pending |
| WI-26 | Holster occupied, found pistol passes ordinary gear/price filters but its magazines do not | Move only the pistol as ordinary cargo; magazines retain normal filters | Cargo boundary | Implemented; runtime pending |
| WI-27 | No-inserted-magazine weapon has three compatible source magazines but reload-safe fast access accepts only the insertion magazine and one spare | Equip the ready weapon, leave the third magazine shell at the source, and move its consolidated ammo stacks in secure/pockets/backpack/vest order without consuming reload reserve | P3 ammo salvage | Passed through combined staging and overflow-salvage runtime coverage |
| WI-28 | Follower spawned with a holstered pistol using a two-cell magazine and also has an equipped long gun | Vest-fallback ammo must preserve independent landing footprints for the largest compatible long-gun and pistol magazines; a raid-acquired pistol must not add the second reserve | P3 ammo salvage | Implemented; runtime pending |
| WI-29 | Empty-primary follower finds an internal-magazine weapon with enough compatible loose ammunition for two tube loads | Load the attached magazine first through a real EFT transaction, move fitting loose reserves in secure/pockets/backpack/reload-safe-vest order, then equip and register primary from settled counts | P7 | Runtime passed for loaded M870 plus fitting reserve |
| WI-30 | Internal-magazine weapon plus loose ammunition remains below two-load readiness | Keep the loaded weapon in empty second primary; do not register it as the main weapon | P7 | Implemented; runtime pending |
| WI-31 | Internal-magazine weapon is ready but only some source stacks fit the protected loose-ammo destinations | Count only the rounds loaded into the weapon and loose stacks accepted by the executable carry plan | P7 | Runtime passed |
| WI-32 | Tracked internal-magazine secondary later finds enough compatible loose ammunition | Load the attached magazine first when space remains, carry fitting source rounds, then promote from the final live state | P7 | Implemented; runtime pending |
| WI-33 | Loose ammunition is inside another weapon or magazine, or was manually placed as strict cargo | It does not contribute to internal-feed readiness | P7 | Implemented; runtime pending |
| WI-34 | Accepted detachable-magazine weapon has compatible loose ammunition beside its magazines | Carry fitting loose ammunition in secure/pockets/backpack/reload-safe-vest order, but do not count it toward detachable readiness | P7 loose-ammo support | Implemented; runtime pending |
| WI-35 | Non-Realistic follower already carries the compatible bullet target across mixed loose-ammo stacks | Ignore ordinary source ammunition; count bullets, not stack objects | P7 loose-ammo support | Implemented; runtime pending |
| WI-36 | Saturated non-Realistic follower finds a better same-caliber round | Take the better source stack when it fits; compare penetration, then damage, then armor damage | P7 loose-ammo support | Implemented; runtime pending |
| WI-37 | Realistic follower already carries the normal compatible bullet target | Ignore saturation and take every compatible source stack that fits | P7 loose-ammo support | Implemented; runtime pending |
| WI-38 | Working primary, `Pickup Gear` disabled, empty second primary, and `Allow Gear Swapping` enabled | Reject the optional support add before moving its weapon, magazines, or loose ammunition; ordinary filtered looting also leaves that gear at the source | Support gate | Runtime passed |
| WI-39 | Empty `OnlyBarrel` double-barrel shotgun and compatible loose shells | Load both chambers through real transactions, keep seven total rounds under-ready, require eight or more for primary readiness, and carry accepted compatible source stacks whole | P8 | Runtime passed |
| WI-40 | Empty non-launcher single-chamber `OnlyBarrel` weapon and compatible loose ammunition | Load `Chambers[0]` through the shared real transaction, require eight total rounds, carry accepted source stacks whole, then classify from settled state | P8 | Runtime passed |
| WI-41 | A `RevolverItemClass` weapon is classified for equipment use | Keep `WeapClass=pistol` revolvers on the holster path; route shotgun, launcher, and custom rifle/sniper revolvers through the shoulder-weapon readiness pipeline | P9 | Implemented; MTs-255 runtime pending |
| WI-42 | A mechanically compatible magazine contains ammunition for another caliber, or a mixed incompatible load | Reject it from support-magazine transfer and readiness; reject an incompatible inserted load as weapon-unready even when good spares exist | Magazine ammunition validation | Implemented; runtime pending |
| WI-43 | A loaded internal-magazine revolver has enough compatible loose reserves but exposes no accepting chamber slot | Treat cylinder compatibility as authoritative, count only reserves proven to fit protected carry space, and classify primary at two cylinder capacities | P9 cylinder readiness | Runtime passed with M32 `6/6` plus six loose grenades |
| WI-44 | A usable launcher is equipped in `FirstPrimaryWeapon` and combat starts | Keep it as the real primary, use the grenadier objective only for explosive safety/positioning, then fire through the ordinary weapon action so repeated shots and reload remain normal combat behavior | P9 launcher combat handoff | Runtime passed under old suppress action; normal-fire runtime retest pending |
| WI-45 | A looted first-primary cylinder launcher becomes empty while compatible grenades remain carried | Store grenades in vest, pockets, backpack, then secure fallback; tolerate the automatic pistol switch, reselect the launcher within a bounded window, and allow this launcher alone through patrol reload maintenance | P9 launcher reload | Implemented; runtime pending |
| WI-46 | A shallow launcher arc reaches an enemy root point on sloping terrain | Treat terrain contact within the grenade's effective and friendly-safe impact tolerance as a valid detonation; reject hard geometry farther up the arc and record its collider geometry | P9 launcher arc lane | Implemented; runtime pending |
| WI-47 | A first-primary launcher cannot produce a safe ordinary shot during its grenadier opportunity | If a loaded non-launcher holster weapon exists, queue and settle a pistol switch before ordinary combat resumes; do not trigger after successful launcher fire or during launcher reload | P9 rejected-launcher fallback | Implemented; runtime pending |
| WI-48 | Missing-primary candidate has partial acquired magazines and compatible loose source ammunition | Top off the fullest operational acquired magazines through settled transactions, then run ordinary readiness from their live counts | P11 magazine top-off | Implemented; runtime pending |
| WI-49 | Missing-primary candidate has no inserted magazine, an empty compatible source magazine, and compatible loose source ammunition | Fill the source magazine when its shape is operational, then let the existing staged insertion path load it into the weapon | P11 magazine top-off | Implemented; runtime pending |
| WI-50 | Candidate has a partial inserted external magazine and the source has compatible loose ammunition plus free grid space | Detach the magazine into the same source, top it off, restore it, then evaluate readiness | P11 inserted-magazine top-off | Implemented; runtime pending |
| WI-51 | Loose ammunition fits the magazine mechanically but is incompatible with the candidate weapon, or top-off transaction fails | Do not load or count those rounds; decide from settled compatible magazine state only | P11 compatibility/failure boundary | Implemented; runtime pending |
| WI-52 | Candidate source has 35-penetration loose ammunition while the follower already carries a two-magazine reserve averaging 45 penetration | Reject the weaker source ammunition; quantity need is zero and power weight is negative | P12 tactical ammo weighting | Implemented; runtime pending |
| WI-53 | Follower has less than one magazine of 45-penetration compatible ammunition and finds 35-penetration rounds | Accept the weaker source because critical need overrides the downgrade | P12 tactical ammo weighting | Implemented; runtime pending |
| WI-54 | Equipped primary is under-ready and has an empty or partial compatible fast-access magazine while `Pickup Gear` is disabled | Fill only free magazine capacity from carried loose ammunition first; Immersive/Realistic may then use searched-source ammunition | P12 primary top-off | Implemented; runtime pending |
| WI-55 | Equipped primary is sufficiently stocked and finds stronger loose ammunition | Do not unload or replace existing cartridges in this phase; revisit only after top-off scenarios are stable | P12 opportunity swap | Deferred |
| WI-56 | A looted weapon is promoted to first primary with an empty inserted magazine and loaded package magazines in fast access | Permit patrol reload only when EFT selects a magazine recorded from that weapon's accepted package; never consume a compatible spawned magazine | P6 reload hardening | Implemented; runtime pending |
| WI-57 | An acquired weapon package contains a partial inserted magazine and several compatible partial source magazines | Prove operational placement, fill the inserted magazine first, then consolidate fullest accepted spares from least-full donors through settled EFT magazine-load transactions before readiness | P13 donor-magazine consolidation | Passed with `20/30 + 20/30 + 20/30` becoming one inserted `30/30`, one operational `30/30`, and one empty donor left at source |
| WI-58 | Repacking makes a tracked second-primary package ready while the loot transaction still owns the follower's hands | Preserve the validated promotion, finish all remaining loot, then move second primary to first primary through the delayed command-owned binder without waiting for combat memory to clear | P13 promotion completion | Implemented; runtime pending |

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
| RT-15 | Existing secondary occupied; new weapon had exactly `15/30` inserted and one compatible `30/30` source spare with reload reserve | Half-full inserted magazine contributed `30`; the spare raised readiness to exactly `60/60`; new weapon entered first primary and the prior weapon registered as usable second-primary support | Passed |
| RT-16 | Existing secondary occupied; new weapon had `11/30` inserted plus one compatible `30/30` source spare | Earlier policy moved only the weapon through ordinary cargo and left the spare behind | Superseded by potential-package policy; retest pending |
| RT-17 | Tracked cargo rifle with `7/30` inserted remained in backpack, then two compatible `30/30` magazines reached follower fast access | Idle cargo reevaluation reached `67/60` and moved the uniquely ready tracked cargo weapon to first primary; no promotion failure followed | Passed after fix |
| RT-18 | Corpse shotgun had no inserted magazine and one loose half-loaded `2/4` compatible magazine in its tactical vest | Legacy no-inserted-magazine branch moved the loose magazine to fast access and equipped the shotgun directly into first primary without a final readiness gate | P3 gap reproduced; cargo fix implemented, retest pending |
| RT-19 | Brick received manually placed weapon and magazine trees through `View Backpack`, then inspection remained closed for about 14 seconds | All 15 tree IDs remained strict cargo and no idle readiness promotion occurred | Passed |
| RT-20 | RT-19 trees were taken back out through `View Backpack` | Close bookkeeping logged `removedFromBackpack` and cleared all 15 strict IDs | Passed |
| RT-21 | Removed weapon was reacquired through the old generic `Loot This` path with one full magazine | EFT incorrectly placed it directly in first primary without readiness; selection exhausted retries with `handsBusy` | Bug reproduced; explicit P4 placement/hand-release fix implemented, runtime retest pending |
| RT-22 | Empty-weapon source-magazine staging was already verified; a later ready weapon had one fitting source spare and two additional magazines rejected by reload-safe fast-access fit | The fitting spare moved to the vest, readiness reached `65/60`, the weapon entered first primary and was selected, and both overflow magazines were emptied into the secure container without consuming reload reserve | Passed; together with the earlier staging test, closes WI-27 |
| RT-23 | Empty internal-magazine shotgun plus loose source ammunition | The load reached `8` live rounds, but pre-load source references were then reused as reserve moves; the shotgun appeared empty while the loose ammunition reached the backpack | Bug reproduced; staging now discards the pre-load ammo plan and rebuilds from live state, runtime retest pending |
| RT-24 | Empty-primary follower found a loaded M870 and two compatible 20-round shell stacks, with protected inventory room for only one stack | Planner accepted one stack, rejected the other before execution, calculated `8 + 20 = 28` against the internal-capacity threshold of `14`, moved the accepted stack to tactical vest, equipped first primary, and completed vanilla selection on retry 3 | Passed; closes the loaded form of WI-29 and the partial-fit boundary WI-31 |
| RT-25 | Container search equipped a primary-ready M870, then completed the remaining loot command before requesting the weapon switch | Selection queued with the one-second post-loot delay and completed through the centralized binder on attempt 4 | Passed; post-loot Attention-style handoff verified |
| RT-26 | Working primary and empty second primary with `Pickup Gear` disabled; corpse contained a usable long gun package plus unrelated eligible loot | Planner repeatedly reported `secondaryAddRejected`, `destination=Source`, `decisionReason=pickupGearDisabled`; the weapon package stayed while unrelated material and money moved normally | Passed; closes WI-38 |
| RT-27 | Same occupied-primary support opportunity after enabling `Pickup Gear`; candidate had `5/10` inserted plus one compatible `10/10` source magazine | Source magazine moved to reload-safe fast access, readiness reached `20/20`, weapon entered and registered in `SecondPrimaryWeapon`, and both moves used `LootGeneric` without selecting away from the working primary | Passed; closes WI-24 |
| RT-28 | Empty double-barrel shotgun plus 47 compatible loose shells in one searched container | Current planner classified the shotgun as `detachableMagazine`, found no magazine candidates, produced `ordinaryReferenceUnavailable`, then rejected equipment staging with `magazineSlotUnavailable`; loose shells never entered weapon readiness | Expected unsupported baseline; opens WI-39 |
| RT-29 | Same double-barrel test after placing live rounds in its chambers | Planner still classified it as `detachableMagazine`, reported no inserted magazine, and rejected it at `magazineSlotUnavailable`; chamber contents were never read | Baseline reproduced; confirms WI-39 is feed classification rather than reserve quantity |
| RT-30 | Empty-primary follower searched a body containing an empty MP-43-1C double barrel and `47` compatible shells in three source stacks | Planner classified `feed=chamberFed`, staged two one-shell transactions from settled `loadedBefore=0` and `1`, carried all accepted remaining shell stacks, equipped `FirstPrimaryWeapon`, and completed vanilla selection on attempt 4; readiness still used the provisional `47/4` result | Feed transactions, whole-stack carry, and selection passed; threshold superseded |
| RT-31 | Empty MP-43-1C plus only three shells, followed by a later body with compatible shell stacks | First search settled two chambered plus one reserve shell and retained the under-ready weapon in `SecondPrimaryWeapon`; the later search promoted it to `FirstPrimaryWeapon` and vanilla selection succeeded on attempt 4 | Under-ready and later-promotion chain passed; provisional `4`-round threshold superseded by the eight-shell policy |
| RT-32 | Empty MP-43-1C plus exactly seven shells, followed by a later container with two whole 20-shell stacks | Initial staging settled `2` chambered plus `5` reserve against `threshold=8`, retained the weapon in `SecondPrimaryWeapon`, and announced generic loot; the later whole-stack transfer reached `47/8`, promoted to first primary, announced weapon loot, and selected on attempt 4 | Passed; closes WI-39 |
| RT-33 | Empty single-chamber `OnlyBarrel` weapon `61f7c9e189e6fb1a5e3ea78d` with 40 compatible loose rounds | Loaded one round into `Chambers[0]`, moved the remaining 39-round source stack whole, classified `40/8` as primary-ready, equipped `FirstPrimaryWeapon`, and selected it after the pickup reset | Passed |
| RT-34 | M32 equipped in first primary entered combat with six loaded camoras and six compatible grenades stored in secure container | Grenadier objective selected the M32 and fired all six rounds; reload then failed because the revolver-style search saw only fast-access ammo and the selector abandoned the empty launcher | Combat handoff passed; opened WI-45 |
| RT-35 | Loaded first-primary M32 engaged downhill targets at roughly `73m` to `101m` where only a shallow arc was required | Both autonomous and ordered grenadier windows rejected `launcherArcLaneBlocked`, default rifle fire was correctly blocked for the primary launcher, and the follower stood without firing until killed | False arc rejection reproduced; WI-46 arc fix and WI-47 loaded-pistol fallback implemented, runtime pending |
| RT-36 | Loaded `6/6` first-primary M32 engaged a visible target near `81m`; the target later closed to `9.8m` | Grenadier never activated because the early autonomous gate required straight-rifle `CanShoot`; ordinary Default/OrderedPush actions correctly refused to fire an unowned launcher. The follow-up command was recorded as `SetPushEnemy`, not `SetSuppressEnemy` | Regression reproduced; activation gate removed, normal aim worker receives the validated arc point, and close dogfight requests holster fallback; runtime pending |
| RT-37 | Empty-primary follower carried a tracked HK416 in second primary with an empty `0/30` inserted magazine; a later corpse supplied one compatible `30/30` magazine and compatible loose ammunition | The inserted magazine was detached into the corpse vest, filled from `0/30` to `30/30`, restored, and reevaluated from settled state. The source spare moved to operational vest, readiness reached `60/60`, the weapon promoted to first primary with one `LootWeapon` cue, and accepted remaining loose rounds moved to secure storage | Passed; verifies top-off -> magazine plan -> promotion -> loose-ammo carry ordering |
| RT-38 | Follower already had a working primary and empty second primary; a later corpse supplied a ready long gun with `50/50` inserted plus loaded `50/50`, `20/20`, and `20/20` compatible magazines | All three fitting magazines moved to operational vest while preserving reload reserve; projected support readiness reached `140/60`; the weapon entered `SecondPrimaryWeapon`, registered with `canChange=True`, and retained the expected `LootGeneric` cue | Passed; verifies intentional operational-secondary package acquisition |
| RT-39 | Tracked second-primary HK416 had `20/30`; a later corpse supplied `20/30`, `10/30`, one empty compatible magazine after donor consolidation, and `40` accepted loose rounds | Donor consolidation reached settled `60/60`, but the immediate promotion was discarded as `handsBusy`; the idle watcher promoted only after combat ended, and the empty magazine was rejected before top-off placement validation | Regression reproduced; delayed command-owned promotion and provisional empty-mag placement implemented, runtime retest pending |

Not yet runtime-covered:

- `BS-01`: full inserted magazine plus compatible backpack spare recruited into fast access
- `BS-02`: low inserted magazine plus backpack spare remains under threshold, leaving the spare in backpack and weapon in second primary
- `BS-03`: low inserted magazine plus source spare plus backpack spare becomes primary-ready
- `BS-04`: a later source spare recruits backpack cargo and promotes an existing tracked secondary
- equivalent backpack-recruitment and secondary-promotion paths from searchable containers
- occupied-secondary rejection with `Pickup Gear` disabled, enabled-but-price-rejected, and enabled-with-valid-backpack-cargo cases
- promotion while primary becomes occupied, hands become busy, combat starts, or the follower dies during the sequence
- post-raid return/persistence after a secondary-to-primary promotion in each loadout-management mode
- mixed provenance: one manual strict-cargo magazine plus one command-acquired compatible magazine
- empty internal-magazine staging retest from `RT-23`, plus under-ready and later-promotion scenarios `WI-30` and `WI-32`
- nested/manual strict-cargo exclusion scenario `WI-33`
- loose-ammunition support and saturation scenarios `WI-34` through `WI-37`
- P8 single-chamber runtime passed: one chamber load settled, the accepted source stack moved whole, and 40 total rounds equipped/selected primary
- P9 non-holster revolver classification is implemented; MTs-255 cylinder loading/readiness remains runtime pending
- M32 inventory/readiness passed: its full `6/6` cylinder plus six compatible loose grenades evaluated as `12/12`, equipped in `FirstPrimaryWeapon`, and selected successfully
- M32 previously fired its complete six-round cylinder through launcher suppression; that route was rejected because it completed as a one-shot suppression task. The grenadier objective now returns the ordinary fire action after safety planning, and repeated-fire/runtime reload behavior requires retest.
- M32 reload hardening is implemented and awaits runtime verification: launcher grenades now fill vest, pockets, and backpack before secure fallback; the custom reload search covers those destinations; and an empty first-primary launcher gets bounded combat reselection plus the narrow patrol reload exception
- M32 shallow-arc regression `RT-35` awaits runtime verification: near-target terrain impact now uses the loaded grenade's blast radius constrained by the friendly-clear margin, while earlier hard obstructions remain rejected with detailed arc geometry in the recorder
- M32 rejected-opportunity fallback `WI-47` awaits runtime verification: a loaded holster pistol is queued through a pending selector handoff after the grenadier window fails, preventing ordinary combat from remaining blocked behind a first-primary launcher
- M32 normal-fire regression `RT-36` awaits runtime verification: visible launcher targets can activate without straight-rifle `CanShoot`, the `shootFromPlace` action passes the arc-compensated point to EFT's normal aiming/trigger worker, and point-blank dogfight requests the loaded holster before combat resumes

## Feed-System Revisit

Internal-magazine weapons now have a separate first implementation. Their reference is the attached magazine capacity, their readiness threshold is two complete capacity equivalents, and their contribution is the rounds already in the attached magazine/chamber plus compatible loose reserve rounds that are already carried or proven to fit in secure container, pockets, backpack, or reload-safe vest space. Before equipment placement, compatible source ammunition fills the attached magazine through a real EFT load transaction. A failed or interrupted load contributes nothing. For `RevolverItemClass` internal feeds, the cylinder's compatibility is authoritative because shoulder-fired revolvers such as the M32 may expose no separate accepting chamber slot.

The internal-feed path deliberately excludes loose rounds nested inside another weapon or magazine and strict cargo placed manually through `View Backpack` from readiness. The shared saturation check still counts all compatible loose bullets physically carried by a non-Realistic follower, including strict cargo, so manual cargo can prevent unnecessary collection without silently becoming usable gear. The path supports later source-ammo promotion for one tracked second-primary or backpack weapon. It does not change detachable-magazine fast-access rules.

Non-launcher `OnlyBarrel` weapons now use a separate chamber-fed implementation for both single- and multi-chamber break actions. Their threshold is the larger of two chamber-load equivalents or eight total rounds, so low-capacity weapons need eight rounds. Only live unspent chamber rounds plus compatible loose reserves that are already carried or proven to fit can contribute. Empty chambers are loaded one round at a time through vanilla's off-hands `Weapon.Apply(...)` transaction; vanilla selects `Chambers[0]` for a single chamber and the first free slot for multiple chambers. Each transaction must increase the live chamber count before planning continues, which prevents speculative rounds or stale split-stack references from entering readiness. The threshold controls weapon classification only; accepted compatible loose-ammo stacks retain the existing whole-stack transfer policy.

The first detachable-magazine top-off slice is implemented for a missing-primary candidate:

- compatible ammunition may come from a source magazine's top cartridge stack or exist loose in the same body/container; it must be accepted by both the weapon and target magazine
- targets are the acquired weapon's inserted magazine plus same-source partial or empty magazines whose shapes are selected for operational fast-access carry
- the inserted magazine is filled first; remaining accepted targets are filled from the highest fill ratio downward, while donor magazines are drained from least-full upward
- the follower's full compatible cartridge stock, including rounds loaded in carried weapons and magazines, establishes quantity and round-weighted penetration; the shared tactical policy balances reserve deficit against the source penetration delta
- an inserted external magazine is staged into free space on the same source, topped off, and restored before the ordinary planner resumes
- every transaction settles before planning continues; no projected cartridge contributes to readiness
- existing follower magazines are deliberately excluded so found ammunition cannot merge into pre-raid magazine ownership in `Simple`/`Restricted`

Later phases still need these distinct ownership and transaction models:

- first-primary grenade and rocket launchers enter the grenadier combat objective only when no conventional long gun is available; body/container gear planning now prefers launcher-as-secondary and can force an empty or under-ready conventional weapon into primary, while launcher-versus-launcher comparison and occupied-secondary displacement remain separate
- non-pistol revolvers now enter the shoulder-weapon pipeline by `WeapClass`; the MTs-255 cylinder must verify the shared internal-magazine transaction path in raid, while holster-revolver gear behavior remains separate
- equipped-primary donor consolidation remains separate from the acquired-package implementation

P12 equipped-primary top-off fills free capacity in compatible vest/pocket magazines, prefers loose ammunition already carried by the follower, and allows Immersive/Realistic to use searched-source rounds without consulting `Pickup Gear`. Weapon readiness requires two ordinary magazine equivalents, while tactical loose-ammunition stocking continues to three ordinary magazine equivalents before quantity need is considered satisfied. High-penetration ammunition at `50+` remains an upgrade opportunity even above that stock target. It does not unload or replace existing cartridges. Acquired-package donor consolidation is implemented separately; applying the same ownership model to spawned primary magazines remains deferred.

Acquired-package repacking uses real EFT magazine-load transactions and settled counts. It tops off the inserted magazine first, then the fullest useful accepted targets from the least-full donors, and reruns the existing largest-available reference and readiness evaluation after each transfer. Donor rounds cannot count twice; a failed or interrupted transfer leaves readiness based only on the magazine states that actually settled.

Loose cartridges must not contribute speculatively. Compatibility, available stack count, magazine capacity, inventory ownership, and every real load, top-off, or repacking transaction must succeed before the resulting loaded rounds count toward primary readiness. These feed systems require their own scenario matrices and runtime tests rather than being folded into the current magazine-move logic.

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

### 2026-07-22

- Preserved a readiness-qualified secondary promotion when the immediate slot move reports `handsBusy`; loot completion now owns the delayed reset, bounded slot-move retry, primary rebind, and selection instead of waiting for the idle combat gate.
- Corrected empty source-magazine top-off planning so a mechanically compatible empty magazine may reserve a valid fast-access shape, receive accepted loose ammunition, and only then enter the ordinary loaded-magazine move plan.

### 2026-07-18

- Runtime verified the P11 tracked-secondary top-off chain: compatible loose ammunition filled the acquired empty inserted magazine through detach/apply/restore transactions, the next live pass combined it with a fitting full source spare at `60/60`, and the weapon promoted with a single `LootWeapon` cue.
- Extended top-off-first replanning to tracked second-primary and backpack-cargo candidates. Their acquired inserted/source magazines settle before operational magazine readiness is recalculated, and accepted remaining loose rounds are appended through the protected storage planner.
- Runtime verified the occupied-primary addition policy with a second ready weapon package: three compatible loaded magazines fit operational vest, the weapon registered as true second-primary support, and the non-primary result used `LootGeneric`.

### 2026-07-17

- Added the P11 missing-primary magazine top-off stage. Compatible loose rounds from the same body/container fill the fullest acquired operational magazines through real EFT transactions before readiness is recalculated.
- Included partial inserted magazines and fitting partial or empty same-source magazines. External inserted magazines are detached into free same-source grid space, filled, and restored before the ordinary weapon planner continues.
- Kept follower-owned magazines outside acquired-package top-off and donor consolidation so raid ammunition cannot merge into pre-raid magazine ownership in `Simple`/`Restricted`.
- Added tactical source-ammo suppression: loaded and loose cartridges already carried establish the quality baseline, and sufficient equal-or-better stock prevents weaker body/container rounds from being loaded or collected.
- Replaced the hard quality cutoff with the P12 need/power/opportunity evaluator. Carried quantity and round-weighted penetration govern replenishment; stocked-ammo opportunity classification remains available for the deferred replacement phase.
- Corrected the ready-package boundary so arithmetic readiness from partial magazines no longer bypasses top-off. The weight baseline includes the inserted magazine and every source magazine planned for operational fast access, allowing worthwhile rounds to fill partials before readiness while rejecting redundant equal/weaker ammunition.
- Replaced percentage-scale opportunity tuning with five-point penetration bands. Shortage accepts the immediately lower band (`38 -> 35`, exact `35 -> 30`), stocked upgrades combine band improvement with useful quantity, and `50+` penetration is accepted outright when upgrades are enabled.
- Narrowed P12 to top-off before replacement: an equipped under-ready primary fills free capacity in compatible vest/pocket magazines without consulting `Pickup Gear` and never unloads existing cartridges.
- Carried loose ammunition is now the first maintenance supply, including the generated secure-container primary-ammo stacks in non-Realistic modes. Immersive/Realistic may then use searched-source rounds; Simple/Restricted keep searched rounds out of protected spawned magazines.

### 2026-07-16

- Added P10 launcher slot normalization for body/container gear swapping: conventional long guns are processed before launchers, an existing first-primary launcher can move to empty second primary, and an under-ready conventional second primary can be promoted without a readiness gate when a found launcher needs the support slot.
- Kept launcher-only acquisition valid when no conventional long gun exists. A launcher added beside an already-working primary is an `Allow Gear Swapping` equipment decision and uses `LootGeneric`; paired plans that create/promote the conventional primary retain `LootWeapon`.
- Routed preferred-secondary launchers back through the shared loose-ammunition carry planner after slot selection. Primary weapon support ammunition remains earlier in the candidate order; compatible launcher grenades then replan one move at a time through vest, pockets, backpack, and secure fallback.
- Replaced the inserted-magazine-only landing reserve with an available-shape planner. It tests compatible magazines largest-to-smallest for `one carried + one landing` fit, preserves the first valid shape, then revisits larger magazines for individual placement against that smaller reserve. An oversized inserted magazine may therefore drop on first reload instead of blocking fitting spares.
- Carried planned magazine overflow into execution before ammo salvage. A source magazine proven to fit the backpack now moves there; only a magazine still left at the source after that attempt may be emptied.
- Runtime verified M32 inventory readiness at `12/12` and reproduced the combat failure: the weapon was selected in `FirstPrimaryWeapon`, but second-primary-only launcher ownership repeatedly rejected it as an unowned launcher and ordinary ammo logic treated its cylinder as empty.
- Generalized launcher resolution, selection, preparation, command eligibility, and loaded-round diagnostics across both primary slots. First-primary launchers now route through the grenadier objective and remain the main weapon; second-primary launchers retain temporary support/fallback behavior.
- Preserved launcher safety ownership: ordinary rifle actions stop explosive fire outside the grenadier objective, while launch planning still owns range, ballistic arc, launch lane, impact radius, and friendly checks.
- Runtime verified that the M32 fired its full six-round cylinder, then reproduced reload failure after EFT automatically switched to the holster and the remaining grenades were invisible in secure-only storage.
- Added launcher-grenade storage priority `vest -> pockets -> backpack -> secure`, matched combat reload search to those locations, retained a bounded empty-launcher reselection window, and allowed only a looted first-primary launcher through patrol reload maintenance.
- Reproduced a false `launcherArcLaneBlocked` rejection against downhill targets at `73m` to `101m`. Ground-root terrain contact now counts as a valid near-target detonation only inside the loaded grenade's blast radius and remaining friendly-safety budget; earlier hard obstructions still reject and now emit collider-level arc diagnostics.
- Added the separate failure fallback for first-primary launchers: when a grenadier opportunity genuinely expires or fails, a loaded holster pistol is selected through a persistent hands-ready retry before normal combat resumes.
- Removed `SuppressShoot` ownership from grenadier firing. Arc, impact, and friendly safety stay in the grenadier planner and are rechecked during the action, while the ordinary `shootFromPlace` action feeds the compensated target into EFT's normal aim-and-trigger worker for cadence and repeated shots. Successful first-primary fire no longer starts the autonomous suppression cooldown.

### 2026-07-15

- Added P7 loose-ammunition support for accepted weapons and a separate `InternalMagazine` readiness model based on settled loaded rounds plus executable compatible loose reserves.
- Added real internal-feed loading before final placement, live-state replanning after staging, protected loose-ammo destinations, mixed-bullet saturation, and better-round bypass rules.
- Preserved the detachable-magazine boundary: loose cartridges may accompany those weapons but do not contribute to readiness until a future real top-off/repacking phase exists.
- Allowed body/container assignment to fall back to the closest in-range gear-capable follower when every candidate has zero ordinary backpack/pocket cargo area.
- Made the physical `FirstPrimaryWeapon` result authoritative for registration and removed the broad `CanChangeHands()` precondition from the vanilla selector request. Retries now stop on follower death/inactivity and recover only a selector transition that remains stuck past the normal draw window.
- Runtime verified a loaded M870 with only one of two source shell stacks fitting: only the accepted stack contributed, readiness resolved to `28/14`, and the shotgun equipped and selected as primary on retry 3 without `handsBusy` failure.
- Deferred primary selection until body/container looting has completed every move and cleared its command ownership. Body, container, and commanded loose-primary pickup now wait one additional second, apply the `Attention` command's `FollowerRecovery.SoftReset(...)` step, and only then request vanilla selection.
- Restricted occupied-primary weapon additions to `Pickup Gear`: with that filter disabled, gear swapping still acquires a missing primary but does not add a second-primary/holster/cargo weapon. Non-primary weapon outcomes announce `LootGeneric`; only a weapon becoming combat primary announces `LootWeapon`.
- Runtime verified both sides of that boundary: disabled `Pickup Gear` left the optional support package untouched while ordinary loot continued, and enabled `Pickup Gear` moved a `5/10` weapon plus `10/10` spare into registered second-primary support with `LootGeneric`.
- Captured the `OnlyBarrel` baseline with an empty double barrel and 47 loose shells in one container: the detachable fallback rejected it at `magazineSlotUnavailable` and never associated the shells, providing the first chamber-fed regression fixture.
- Confirmed that loading the double barrel did not change the old failure: chamber contents were also invisible to the detachable fallback. Added the initial P8 multi-barrel `OnlyBarrel` readiness path with live chamber counting, one-shell vanilla staging, protected loose-shell reserves, and later secondary/backpack promotion.
- Runtime verified MP-43-1C chamber staging and primary selection, then separately verified the under-ready-secondary to later-primary promotion chain. Those tests exposed the provisional `4`-round threshold, so P8 changed to eight total shells while preserving whole-stack compatible-ammo pickup; RT-32 subsequently passed the final seven-versus-eight boundary.
- Runtime verified the final MP-43-1C threshold boundary: seven total shells remained second-primary, then two whole source stacks moved and promoted the weapon at `47/8` with successful selection. Captured the single-chamber baseline at `magazineSlotUnavailable`, then widened the verified chamber transaction path to ordinary single-chamber `OnlyBarrel` weapons while retaining explicit launcher exclusions.

### 2026-07-14

- Simplified P3 empty-weapon handling to a real insertion-first staging transaction.
- Removed the hypothetical complete-package gate: after insertion, the established inserted-magazine planner runs again from live inventory and owns readiness, spare placement, destination, and late overflow-ammo salvage.
- Staging insertion does not count as looted cargo, consume the weapon's attempted state, or emit an early loot voice cue.
- Corrected overflow-ammo execution to use EFT's ammo-specific unload operations. Each planned loose stack is seeded with one round and then filled before the next internal magazine cartridge group is processed; internal cartridge groups are not moved as ordinary items.
- Restricted overflow-ammo salvage to weapons that have settled into `FirstPrimaryWeapon`; secondary, holster, cargo, and rejected outcomes leave source magazines loaded.

### 2026-07-13

- Added P3 overflow-ammo salvage for an accepted body/container weapon package.
- The complete left-behind magazine is preflighted before moving ammunition; cartridge groups are processed in StackSlot LIFO order, split at loose-ammo stack limits, and consolidated by ammo type where stack capacity permits.
- Salvaged output stacks use secure container, pockets, backpack, then vest. Vest fallback preserves the largest structurally compatible long-gun magazine opening and ignores inserted magazine shapes that cannot fit that vest at all.
- Initial-equipment holster identity is captured before commands can alter the loadout. Its largest compatible carried magazine gets a second vest landing reserve; acquired pistols do not.
- Added `WI-27` as the next three-magazine runtime test.

### 2026-07-10

- Created the dedicated phase and scenario tracker.
- Began P1 against baseline `f16caed`.
- Confirmed vanilla `Inventory.FastAccessSlots` contains `Pockets` and `TacticalVest`.
- Confirmed vanilla `BotReload.GetMagazineForReload(...)` searches reachable fast-access magazines and validates the move against the candidate magazine slot.
- Added the centralized readiness formula, reference resolver, actual fast-access scanner, and planned-projection diagnostics.
- Added all 15 formula scenarios as Debug startup self-tests.
- Verified the Debug client build with zero warnings and zero errors before runtime testing.
- Verified the in-game Debug startup self-test passed all 15 formula scenarios.
- Verified a live body candidate with `10/30` inserted and one projected `30/30` compatible spare resolved `R=30`, `T=60`, and `40` total contribution, producing `primaryReady=False`.
- The initial P1 implementation resolved the reference from every compatible magazine template; this was later replaced because theoretical magazines not present in the scenario must not affect readiness.
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
- Extended the idle reevaluation path to a uniquely eligible tracked backpack weapon after manual inventory changes or commanded loose-magazine pickup. A ready secondary retains priority, and multiple ready cargo weapons remain deferred until weapon comparison exists.
- Removed the legacy direct-primary path for weapons without an inserted magazine. Until explicit magazine loading is implemented, compatible loose magazines and the weapon are preflighted as one backpack package and moved magazines-first. When the complete package cannot fit, magazines remain at source and the weapon is attempted alone as cargo.
- Runtime verified the complete support-promotion path with `R=30`, `T=60`: a `10/30` inserted magazine plus one `30/30` spare produced `40` and placed the weapon in second primary; a later manually transferred `30/30` compatible spare produced `70` and promoted it to first primary.
- Verified the promoted weapon switched into the follower's hands and was selected and fired normally during subsequent combat.
- Added readiness-gated recruitment of compatible loose follower-backpack magazines for a newly found weapon. Backpack cargo is moved only when the combined executable plan produces a primary-ready weapon.
- Added source-triggered promotion for a tracked secondary weapon: a later found spare moves first, compatible backpack cargo follows, and the support weapon promotes only after actual readiness passes.
- Removed the gear planner's automatic weapon-and-magazine backpack bundle when secondary is occupied. Rejected candidates now fall through to ordinary `Pickup Gear` and price rules.
- Moved tracked-secondary readiness evaluation ahead of new weapon candidates so newly found compatible magazines complete the existing weapon before any future weapon-comparison policy is required.
- Extended ordinary filtered weapon cargo to retain compatible source magazines for future readiness. `Pickup Gear`, whole-tree price, and backpack fit gate the weapon; accepted compatible magazines join when the package fits.
- Restricted that future-primary package to the empty-primary workflow and excluded pistols from it.
- Added the working-primary support path: a usable long gun fills empty second primary, fitting compatible source magazines move only to reload-safe fast access, and vanilla receives a fresh second-primary `BotWeaponInfo` without a forced hand switch.
- Once the matching usable weapon slots are occupied, later weapons fall through to ordinary filtered cargo and their magazines no longer inherit the weapon's price/category acceptance.
