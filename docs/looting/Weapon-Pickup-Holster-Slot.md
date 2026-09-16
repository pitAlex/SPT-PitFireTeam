# Weapon Pickup: Holster Slot

## Status

Phase 1 is implemented for adding a pistol to an empty holster. The follower must have:

- a working weapon in `FirstPrimaryWeapon`
- an empty `Holster`
- `Allow Gear Swapping` enabled
- `Pickup Weapons` enabled

The candidate must be a pistol or a pistol-class revolver. An occupied holster
is never replaced.

## Shared Support Contract

Holster phase 1 does not have a second copy of secondary-weapon policy. The
same support planner owns both destinations:

- a shoulder weapon targets `SecondPrimaryWeapon`
- a pistol targets `Holster`
- compatible source magazines use the same fast-access and reload-reserve plan
- compatible loose ammunition uses the same primary-first tactical policy
- the successful weapon transaction stores the weapon tree and registers a
  new per-slot `BotWeaponInfo`
- acquisition says `LootGeneric`; it does not replace the active primary

Vanilla gives `SecondPrimaryWeapon` precedence as its singular support role.
An occupied secondary no longer blocks acquisition: the pistol still enters
the empty holster and receives its own `BotWeaponInfo`, while the secondary
remains vanilla's preferred support weapon. When one searched source can supply
both roles, an empty shoulder-support slot and its executable magazine package
are resolved first. Secondary maintenance then finishes before holster
maintenance begins.

For an equipped pistol, maintenance tops off the inserted magazine through the
same detach/load/restore transaction used by the primary planner. Compatible
spares are placed next, including an empty spare when compatible ammunition can
fill it, and remaining loose rounds are considered afterward. Fast-access
placement prefers exact-size rig grids, keeping 1x1 pistol magazines and ammo
out of larger cells when suitable 1x1 cells remain. Every plan still preserves
the largest shared reload landing opening required by the equipped weapons.

## Phase 1 Tests

| ID | Setup | Expected |
|---|---|---|
| H1-01 | Working primary, empty secondary/holster, loaded pistol on a body | Pistol enters holster, says `LootGeneric`, and registers as support |
| H1-02 | Same setup, pistol and compatible loaded spare magazine in a container | Spare moves to reload-safe vest/pockets, pistol enters holster |
| H1-03 | Same setup, compatible loose ammunition | Primary gets first refusal; unresolved useful ammo supports the pistol |
| H1-04 | `Pickup Weapons` disabled | Tactical holster add does not run |
| H1-05 | Holster already occupied | Existing pistol is untouched; no replacement occurs |
| H1-06 | Working primary and secondary, empty holster, usable pistol package | Pistol enters holster; existing secondary remains unchanged and preferred by vanilla |
| H1-07 | Pistol is taken through `Loot This` into an empty holster | Physical pickup behavior is unchanged; the settled holster weapon is registered |
| H1-08 | Body/container supplies an empty secondary package plus pistol magazines/ammo | Secondary weapon and reload-safe magazines resolve first; inserted pistol magazine tops off afterward and 1x1 supplies prefer 1x1 rig grids |

## Deferred

- choosing which support weapon receives shared compatible ammunition
- holster replacement or pistol comparison
- revolver cylinder cases not handled by the shared internal-feed path
