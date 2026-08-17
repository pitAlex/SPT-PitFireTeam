# Weapon Pickup: Secondary Slot

## Purpose

This document tracks the secondary-slot phase of commanded follower looting.
It begins after the empty-primary readiness work in
`Weapon-Pickup-Primary-Slot-Available.md`.

The current phase concerns an already-equipped weapon in
`SecondPrimaryWeapon` while the follower still has a working
`FirstPrimaryWeapon`. It does not replace either weapon.

## Implemented Baseline

- `Pickup Weapons` may add a usable long gun to an empty second-primary slot
  when the follower already has a working primary.
- The support weapon needs a real ammunition source but does not need to meet
  the two-magazine primary-readiness threshold.
- Compatible source magazines may enter vest or pockets only when the planner
  preserves reload landing space.
- The support weapon is registered through the vanilla selector and weapon
  manager without switching hands away from the primary.
- Internal-magazine, chamber-fed, and launcher support additions reuse their
  dedicated feed planners.
- `RT-27` and `RT-38` in the primary-slot ledger verified detachable-magazine
  support addition and registration.

## Phase S1: Tactical Loose Ammunition

S1 extends later body/container searches to maintain an existing detachable-
magazine secondary.

Transaction order:

1. evaluate and maintain the equipped primary first
2. pass only unresolved compatible source ammunition to the secondary
3. top off eligible secondary magazines from carried loose ammunition
4. when ownership permits, top off those magazines from searched ammunition
5. evaluate any remaining source stacks through the existing tactical
   need/power/opportunity policy
6. carry accepted loose ammunition through reload-safe vest space, pockets,
   backpack, then the secure container

Ownership rules:

- A looted secondary may modify only fast-access magazines registered to that
  weapon's accepted package.
- Compatible magazines belonging to the primary or another weapon are never
  secondary top-off targets merely because they share a magazine type.
- A non-looted secondary may use the follower's ordinary compatible
  fast-access magazines, matching the original loadout's shared supply.
- Searched ammunition may fill a looted secondary's registered package
  magazines in every loadout mode because the resulting rounds remain inside
  tracked loot.
- Searched ammunition may modify protected original magazines only in
  `Immersive` or `Realistic`; `Simple` and `Restricted` retain the existing
  protected-magazine boundary.

S1 does not:

- move the secondary to first primary
- replace an occupied weapon slot
- switch the follower's hands
- implement internal-magazine, chamber-fed, or holster maintenance
- reserve ammunition exclusively against vanilla combat reload selection

## S1 Test Matrix

| ID | Setup | Expected |
|---|---|---|
| S1-01 | Working primary, looted secondary, partial registered secondary magazine, source ammo compatible only with secondary | Registered magazine tops off; weapon remains secondary; primary stays selected |
| S1-02 | Primary and secondary share ammunition; primary has an operational magazine gap | Primary consumes the useful source rounds first; secondary receives only what remains |
| S1-03 | Shared ammunition, primary no longer needs maintenance, secondary has a partial registered package magazine | Primary defers the unresolved stack; secondary tops off its registered magazine |
| S1-04 | Looted secondary shares magazine type with the primary, but only the primary magazine is partial | Secondary maintenance does not modify the unregistered primary magazine |
| S1-05 | Looted secondary is sufficiently stocked and finds equal or weaker ammunition | Tactical policy rejects the source stack and ordinary filtered looting does not reclaim it |
| S1-06 | Secondary accepts loose ammunition but protected storage has no valid destination | Ammo remains at the source without changing either weapon slot |
| S1-07 | Same supported setup from a body and a container | Both sources use the same ordering and ownership rules |
| S1-08 | Combat begins after maintenance | Registered secondary remains usable and can reload without consuming an unapproved looted-primary package magazine |

## Phase S2: Operational Magazines

S2 allows an already-equipped detachable-magazine secondary to receive compatible
magazines from later commands:

- a magazine picked up directly through `Loot This` is assigned to the first
  compatible equipped long gun, primary before secondary, only when it can enter
  tactical vest or pockets without consuming the shared reload landing space
- when no reload-safe fast-access placement exists, direct pickup tries the
  backpack and leaves the magazine as unregistered cargo
- a magazine that lands in the backpack remains cargo and is not approved for reload
- magazines placed manually through `View Backpack` remain strict cargo and are not
  adopted automatically
- body/container searches scan loaded magazines for both mechanical compatibility
  and compatible loaded cartridges
- accepted source magazines move only into tactical vest or pockets and are registered
  to the secondary after the real transaction succeeds
- each source move is planned again from live inventory and preserves one shared
  landing opening sized for the largest relevant primary/secondary magazine; weapons
  reload sequentially, so simultaneous openings are not required
- magazines that cannot satisfy the combined fast-access and reload-space check remain
  at the source; they do not fall through into ordinary container cargo
- the operation is controlled by `Allow Gear Swapping` and bypasses `Pickup Weapons`,
  category, and price filters because it maintains an equipped weapon

## S2 Test Matrix

| ID | Setup | Expected |
|---|---|---|
| S2-01 | Working primary, looted secondary, compatible partial loose magazine ordered with `Loot This` | Magazine lands in fast access and is registered to the secondary when the primary is incompatible |
| S2-02 | Same package, body has compatible loose ammo and two compatible loaded secondary magazines | Existing partial magazine tops off; reload-safe source magazines move and register; overflow remains |
| S2-03 | Commanded magazine is compatible with both primary and secondary | Primary receives ownership first |
| S2-04 | Fast access has only the shared largest-magazine reload landing space; commanded compatible magazine has backpack room | Reload landing space remains empty; magazine goes to backpack as cargo and is not approved for either looted weapon |
| S2-05 | Source magazine contains ammunition incompatible with the secondary | Magazine remains at the source |
| S2-06 | Only enough fast-access space for one source magazine plus the shared largest-magazine reload opening | One magazine moves and later overflow remains at the source |
| S2-07 | Same supported setup from a body and a container | Both source types follow the same transaction and registration rules |

## Phase S3: Loose-Feed Secondary Maintenance

S3 extends the proven internal-magazine and chamber-fed acquisition transactions
to a weapon that is already equipped in `SecondPrimaryWeapon` beside a working
primary:

- the primary receives first refusal on compatible searched ammunition
- unresolved compatible ammunition is evaluated for the secondary by the same
  need, penetration, and opportunity policy used by weapon acquisition
- secure-container capacity bypasses quantity sufficiency for equal, already
  carried, or stronger ammunition, but materially worse rounds still require
  tactical shortage before they are accepted
- shotgun tactical supply targets three complete loose-ammunition stacks rather
  than three small tube/chamber capacities
- a looted secondary loads its attached magazine or live chambers before reserve
  stacks are carried; protected original equipment is modified only in
  `Immersive` or `Realistic`
- internal magazines may accept several rounds in one transaction, while
  chamber-fed weapons replan after each one-round chamber transaction
- useful reserves retain the reload-safe vest, pockets, backpack, then
  secure-container destination order
- each successful load counts as loot even when it consumes the entire source
  stack, so the command does not incorrectly finish with `LootNothing`
- the secondary remains equipped, the working primary remains selected, and no
  weapon replacement or promotion is attempted
- maintenance requires `Allow Gear Swapping` and bypasses ordinary pickup and
  price filters; rejected tactical ammunition cannot fall through into cargo

## S3 Test Matrix

| ID | Setup | Expected |
|---|---|---|
| S3-01 | Working primary, partially loaded tube-fed secondary, compatible shells in a container | Secondary loads first, useful remainder is carried, primary stays selected |
| S3-02 | Same setup from a body | Body source follows the same load and reserve rules |
| S3-03 | Primary and secondary both accept the source ammunition | Primary receives first refusal; secondary sees only unresolved rounds |
| S3-04 | Secondary is sufficiently stocked and secure storage has room | Equal/already-carried or stronger ammunition fills secure storage; materially worse ammunition remains unless shortage policy accepts it |
| S3-05 | Empty or partial double-/single-barrel secondary with compatible shells | One chamber transaction settles at a time; useful remainder is carried |
| S3-06 | Simple/Restricted follower spawned with the secondary | Searched rounds may be carried but do not directly modify protected original equipment |

## Deferred Secondary Verification

The secondary implementation is sufficiently stable to begin holster work. The
following combinations are not recorded as failed; they simply have not had a
clean, named runtime verification in this ledger and should be revisited if
secondary behavior regresses:

- shared-ammunition contention: S1-02 and S1-03, where primary first refusal
  must either consume the source or explicitly leave it for the secondary
- shared-magazine ownership: S1-04 and S2-03, ensuring a compatible magazine
  is never adopted by the wrong weapon merely because its shape matches
- fully stocked / lower-quality ammunition rejection: S1-05 and S3-04
- no-destination behavior: S1-06 and S2-04, where tactical ammo remains at
  the source rather than consuming the reserved reload landing space
- incompatible cartridges inside an otherwise compatible source magazine:
  S2-05
- exact fast-access overflow: S2-06, where one source magazine fits beside the
  shared reload opening and later magazines remain at the source
- explicit body/container parity records for S1-07 and S2-07
- combat reload after secondary maintenance: S1-08

These are verification debt, not a reason to expand secondary-slot policy
before holster work. They remain bounded by the current contracts: primary
first refusal, one shared reload landing space, registered magazine ownership,
and no occupied-secondary replacement.

## Later Secondary Phases

- S4: persistence, death, interruption, and failed-transaction hardening.
- Holster work receives its own phase after empty-secondary behavior is stable.
- Occupied secondary replacement remains out of scope.
