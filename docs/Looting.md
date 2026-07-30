# Looting

Date: 2026-07-30

## Scope

This document tracks pitFireTeam follower-commanded looting behavior.

It covers:

- loose item pickup from `LootGeneric` / `LootWeapon`
- body looting from `Check Him` / `Loot Body`
- container looting from `LootContainer`
- the `Looting Settings` price and category filters
- tracked follower-loot bookkeeping and post-raid implications
- gear-swapping phase 1 constraints

It does not cover:

- vanilla NPC patrol looting except as a reference point
- LootingBots implementation except as a reference point
- teammate kit purchase pricing; see `docs/Buy Screen.md`
- teammate loadout ownership modes; see `docs/Loadout-Management.md`
- player-death escape and recovery mail simulation; see `docs/Team-Escape.md`

## Authoritative Files

- `client/Components/AIBossPlayer.cs` - phrase routing, follower selection, distance/free-space gates, loot target reservation.
- `client/Components/BotFollowerPlayer.cs` - command state, active loot command tracking, completion helpers.
- `client/BigBrain/Actions/GestureCommandAction.cs` - movement, search delay, item planning, inventory transactions, cleanup.
- `client/Modules/InteractableObjects.cs` - current loot targets, assigned takers, stored follower loot, looted weapon-tree registration.
- `client/Modules/FollowerLootPriceService.cs` - item-tree rouble pricing, min/max threshold checks, backpack/pocket free-area helper.
- `client/Modules/FollowerLootCategoryService.cs` - food/meds/valuables/gear category filter checks.
- `client/Components/SquadControlMenuUi.Settings.cs` - `Looting Settings` UI rows and numeric input handling.
- `client/friendlyPlugin.cs` - BepInEx config entries and max price range.
- `server/Resources/lang/en.json` and `client/Localization/EmbeddedEnglishLanguageProvider.cs` - localized setting names/descriptions.

Related summaries:

- `docs/Commands.md` - command-facing behavior.
- `docs/My-Squad-Screen.md` - settings UI placement and controls.
- `docs/Loadout-Management.md` - teammate corpse/protected gear semantics.
- `docs/Team-Escape.md` - tracked follower loot during player-death recovery.

## Terms

`Follower loot` means an item tree a follower picked up because the player ordered it through pitFireTeam looting commands.

`Tracked follower loot` means follower loot registered through `InteractableObjects.StoreItem(...)`. Tracked loot is eligible for the mod's return/recovery flows when the owning squadmate survives the relevant flow.

`Equipped gear loot` means loot moved into an equipment slot as part of `Allow Gear Swapping`. In `Simple` and `Restricted`, this is add-only: empty slots may be filled, but occupied gear slots are not replaced, and added gear is tracked so it returns as cargo. In `Immersive` and `Realistic`, eligible equipped gear is not tracked as return cargo so an escaped teammate can keep it in the saved kit snapshot.

`Protected teammate gear` means saved teammate equipment that should not become free player gear under `Simple` or `Restricted` loadout management. Protected gear cleanup is owned by the loadout-management and escape/recovery systems, not by the looting planner alone.

`Whole tree` means the root item plus all attached/contained child items. A weapon tree includes installed mods. A helmet tree includes attached face shield/night vision/other devices. A container tree includes contents.

## Command Ownership

All looting commands are request-layer commands executed by `GestureCommandAction` out of combat.

Quick-interaction loot selections enter `AIBossPlayer` through the player's `OnPhraseSay` event. Diagnostic builds trace the menu action, current and stored target, phrase arrival, follower eligibility, target reservation, and accepted command state.

Followers with enemies are not eligible for new body/container loot assignment. A follower already handling an active loot/pickup command is skipped so rapid player commands can be assigned to the next available follower.

A body/container reservation is authoritative through the per-follower target map. The mutable quick-menu target and legacy global owner fields are not paired when that owner already has a mapped target; otherwise looking at target two could make it inherit target one's owner and be rejected as already reserved.

Once body/container searching begins, normal replacement commands are ignored until the loot command finishes. Combat, timeout, target invalidation, player death, or safety cleanup can still interrupt the command.

## Loose Item Pickup

Inputs:

- `EPhraseTrigger.LootGeneric`
- `EPhraseTrigger.LootWeapon`

Current behavior:

- requires `InteractableObjects.GetCurLootItem()`
- chooses the reachable active eligible follower with the shortest complete NavMesh path to the world item
- reserves ownership through `InteractableObjects.SetTaker(...)`
- sets `FollowerCommandType.TakeLootItem` for 35 seconds
- moves to the item, checks inventory destination, and runs one pickup transaction
- keeps EFT's normal destination selection for non-weapons and pistols
- evaluates commanded loose long guns with the same primary-readiness model, independently of `Allow Gear Swapping`
- sends a primary-ready long gun to an empty `FirstPrimaryWeapon` slot
- sends an under-ready long gun to empty `SecondPrimaryWeapon`, then backpack cargo
- if support and backpack are unavailable while first primary is empty, permits a last-resort first-primary placement only when the inserted magazine has at least half of the smaller of its capacity and the weapon's ordinary magazine reference
- leaves a dangerously underloaded weapon at the source when no honest support/cargo destination exists
- registers looted weapon trees through `RegisterLootedWeaponTree(...)`
- releases pickup animation/hand ownership before rebuilding a first-primary weapon's bot-manager state and requesting selection
- stores the item through `StoreItem(...)` only when the follower is a spawned squadmate

Current selection does not require spawned-teammate eligibility for loose item pickup. Body and container looting do.

## Body Looting

Inputs:

- `EPhraseTrigger.CheckHim`
- `EPhraseTrigger.LootBody`

Current assignment:

- requires `InteractableObjects.GetCurBodyLootTarget()`
- only saved teammates spawned through the raid squad flow can be assigned; recruited/picked-up followers are ignored even if they are otherwise squad-managed
- teammate corpses choose the eligible squadmate with the shortest complete NavMesh path
- non-teammate corpses prefer the eligible loot carrier within a 22m complete NavMesh path that has free backpack/pocket grid area
- when no follower in range has ordinary cargo room and `Allow Gear Swapping` is enabled, assignment falls back to the reachable eligible follower with the shortest path so the real planner can still use an empty weapon slot or operational vest space
- ownership is reserved through `InteractableObjects.SetBodyLootTaker(...)`
- an explicit player `Check Him` / `Loot Body` order may revisit a corpse that a follower already completed
- checked-body history is retained for autonomous `Go loot` selection, which skips completed corpses
- neither path can assign a corpse while another follower actively owns its loot reservation
- command timeout is 75 seconds

Execution:

- moves to the corpse
- starts a search phase before moving items
- says a pickup-confirmation phrase after the simulated search, when the first real non-dogtag loot move is queued
- uses `EPhraseTrigger.LootWeapon` only when the executable plan makes a weapon the follower's combat primary during this search
- uses `EPhraseTrigger.LootGeneric` for ordinary loot and every non-primary weapon result, including second-primary support, holster, backpack cargo, potential-weapon packages, and an under-ready long gun held inert in the left-shoulder slot
- before ordinary gear/cargo can claim the search cue, pre-evaluates executable secondary promotion, source-weapon primary equip, and backpack-cargo promotion paths; a primary-producing path runs first and owns the single `LootWeapon` cue
- waits a short beat after that pickup-confirmation phrase before executing the first move, so it does not run into `Ready`
- says `EPhraseTrigger.Negative` if eligible non-dogtag loot exists but no executable move can be built
- says `EPhraseTrigger.LootNothing` if no eligible non-dogtag item exists
- plays the loot-search sound while waiting
- search delay is based on corpse pockets, backpack, and tactical vest grid cells, with a bounded cap
- after the search delay, plans and executes one live inventory transaction at a time
- marks the corpse loot tree searched for the player after normal completion, unless the player is actively viewing/searching that same body
- says `EPhraseTrigger.Ready` when done after at least one successful non-dogtag move

### Teammate Corpses

Teammate corpses use the protected recovery path.

Rules:

- uses empty compatible equipment slots as cargo space when possible
- does not swap or throw away the looting follower's current kit
- tries backpack, tactical vest, and pocket carry containers for remaining body gear
- treats backpacks and rigs as whole cargo if they fit
- does not empty a backpack/rig when the container itself cannot be carried
- pockets are structural and are not moved as a container, so pocket contents are considered individually

Loadout-mode interaction:

- in `Simple` and `Restricted`, protected teammate gear roots are skipped
- non-protected containers may carry protected descendants, and later cleanup strips protected descendants before extraction or return delivery
- in `Immersive` and `Realistic`, protected-equipment skipping is not applied because fallen teammate gear is lootable in those modes

### Non-Teammate Corpses

Non-teammate corpses use filtered looting.

Candidate order:

1. dogtag, when present on a non-teammate USEC/BEAR body
2. backpack contents
3. pockets
4. worn tactical vest, armor, and headwear as whole-tree candidates
5. eligible tactical-vest contents and installed armor-plate fallbacks
6. weapons

Rules:

- dogtags bypass category and price filters, but still need a backpack/pocket destination
- dogtags are moved to backpack or pockets
- dogtag-only body looting still reports `EPhraseTrigger.LootNothing`
- normal filtered looting does not take the corpse's worn backpack as a container shortcut
- `Pickup Gear` allows worn armor, armored rigs, tactical rigs, and headwear to be evaluated as whole cargo trees before any fallback contents
- backpack contents are checked item by item
- pocket and vest contents skip magazines entirely so follower reload space is not disturbed
- backpack and container magazines can still be looted if filters, price, and fit pass
- loose armor plates are ignored; an installed plate becomes a fallback candidate only after its parent armor/rig stays at the source, and only at 50 percent or better durability
- normal cargo weapons are priced and moved as whole weapon trees; they are not stripped into parts
- ordinary cargo weapon moves first try empty compatible weapon slots, such as second primary or holster, then fall back to backpack/pockets
- non-weapon successful moves target only backpack and pockets, never the follower's tactical vest

## Container Looting

Input:

- `EPhraseTrigger.LootContainer`

Current assignment:

- requires `InteractableObjects.GetCurLootContainerTarget()`
- an explicit player `Loot Container` order may revisit a container that a follower already completed
- checked-container history is retained for autonomous `Go loot` selection, which skips completed containers
- an active reservation still prevents two followers from searching the same container
- only saved teammates spawned through the raid squad flow can be assigned; recruited/picked-up followers are ignored even if they are otherwise squad-managed
- locked or inactive containers are ignored
- prefers the eligible loot carrier within a 22m complete NavMesh path that has free backpack/pocket grid area
- when no follower in range has ordinary cargo room and `Allow Gear Swapping` is enabled, assignment falls back to the reachable eligible follower with the shortest path so the real planner can still use an empty weapon slot or operational vest space
- ownership is reserved through `InteractableObjects.SetContainerLootTaker(...)`
- command timeout is 75 seconds

Execution:

- moves to the container
- opens the container if shut
- starts a search phase before moving items
- says a pickup-confirmation phrase after the simulated search, when the first real loot move is queued
- uses `EPhraseTrigger.LootWeapon` only when the executable plan makes a weapon the follower's combat primary during this search
- uses `EPhraseTrigger.LootGeneric` for ordinary loot and every non-primary weapon result, including second-primary support, holster, backpack cargo, potential-weapon packages, and an under-ready long gun held inert in the left-shoulder slot
- before ordinary gear/cargo can claim the search cue, pre-evaluates executable secondary promotion, source-weapon primary equip, and backpack-cargo promotion paths; a primary-producing path runs first and owns the single `LootWeapon` cue
- waits a short beat after that pickup-confirmation phrase before executing the first move, so it does not run into `Ready`
- says `EPhraseTrigger.Negative` if eligible loot exists but no executable move can be built
- says `EPhraseTrigger.LootNothing` if no eligible item exists
- plays the loot-search sound while waiting
- search delay is based on total grid cells in the container tree, with a bounded cap
- after the search delay, plans and executes one live inventory transaction at a time
- marks the container loot tree searched for the player after normal completion, unless the player is actively viewing/searching that same container
- closes the container on normal completion
- combat, timeout, or safety interruption can leave the container open
- says `EPhraseTrigger.Ready` when done after at least one successful move

Container contents use the same filtered-loot planner as non-teammate corpse contents.

## Looting Settings

`Looting Settings` appears in `My Squad -> Settings` after `Raid Settings`.

Price controls:

- `Minimum Price`
- `Maximum Price`

Both are integer rouble inputs. The current max configurable value is `99,999,999`. A value of `0` disables that bound.

Category controls:

- `Pickup Food`
- `Pickup Meds`
- `Pickup Valuables`
- `Pickup Weapons`
- `Pickup Gear`
- `Allow Gear Swapping`

The five pickup category checkboxes default on. `Allow Gear Swapping` defaults off.

Category mapping:

- `Food` covers food and drinks.
- `Meds` covers usable medical items, drugs, stimulators, and med kits.
- `Valuables` covers barter items, keys, special items, info items, money, and other non-gear loot.
- `Weapons` covers weapons, ammunition, magazines, weapon mods, and grenades.
- `Gear` covers helmets, body armor, armored rigs, tactical rigs, and their complete carried trees.

Whole wearable trees are considered before their contents. A qualifying helmet moves with all installed devices. A qualifying armor vest, armored rig, or tactical rig moves with installed plates and carried contents. If armor or a rig cannot be taken as a whole, eligible contents are considered separately. An installed plate is eligible only through this fallback, only when `Pickup Gear` is enabled, only when it passes price, and only when current durability is at least 50 percent of current maximum durability. Loose plates remain excluded.

`Allow Gear Swapping` defaults off. It is the explicit gate for gear equip/swap behavior in every loadout mode; post-raid ownership follows the active loadout management mode.

`Pickup Weapons` controls ordinary weapon cargo and optional weapon additions beyond the combat primary. With `Allow Gear Swapping` enabled, acquiring a missing primary and future accepted better-primary swaps remain independent of `Pickup Weapons` and min/max price. Adding a weapon to second primary or holster requires `Pickup Weapons`; any ordinary weapon cargo fallback also requires `Pickup Weapons` and must pass min/max price.

`Pickup Gear` controls wearable cargo independently of `Pickup Weapons`. It does not authorize an equip or replacement by itself. Implemented equipment plans remain gated by `Allow Gear Swapping` and retain their protection, loadout-mode, and executable-placement checks.

## Price Checks

Filtered body/container looting checks category before price.

Money is still controlled by `Pickup Valuables`, but it ignores `Minimum Price` and `Maximum Price` once that category is enabled.

`FollowerLootPriceService.CalculateItemTreeRoublePrice(...)` prices the whole item tree once:

- uses cached ragfair prices when available
- falls back to handbook base prices
- refreshes market prices at most every 300 seconds
- counts stack sizes
- treats roubles as 1 rouble each
- ignores structural inventory roots such as `InventoryEquipment`, `PocketsItemClass`, and `BuiltInInsertsItemClass`
- floors the final total to a non-negative rouble value

The price service intentionally evaluates the tree as a whole. It should not be used to justify stripping a weapon, helmet, armor item, or container into sub-parts.

## Inventory Destinations

Filtered body/container looting uses these destination rules:

- weapons can use empty compatible weapon slots before backpack/pockets
- non-weapons use backpack and pockets only
- the follower's tactical vest is not used as carry space
- secure container is not used
- eligible installed plate fallbacks use backpack or pockets; loose plates remain excluded

This preserves tactical vest space for magazines and avoids destabilizing combat reload behavior.

## Tracking And Return Bookkeeping

Successful squadmate cargo moves call `InteractableObjects.StoreItem(...)`.

Items transferred through `View Backpack` have a separate strict-cargo provenance:

- each newly added root and every item currently inside that root are recorded by item id
- strict-cargo weapons are not promoted into primary/support roles
- strict-cargo magazines are not recruited from the backpack for weapon readiness
- provenance is per item tree, so a manually supplied magazine remains strict cargo even when a different compatible magazine was acquired through a command
- taking an item back out of the follower backpack removes its strict-cargo provenance; ordering the follower to pick it up later makes it gear-eligible again
- an explicit loose-item pickup, body search, or container search clears strict-cargo status only for the exact tree acquired by that command
- narrow missing-weapon exception: when both shoulder slots are empty and the player orders a compatible loose detachable-magazine weapon pickup, compatible magazines and loose ammunition already in the follower backpack are adopted as that weapon's support package; unrelated manually placed cargo remains strict
- this exception settles magazine top-off or insertion and reload-safe fast-access moves before the existing live-readiness evaluator selects first primary or second primary
- backpack inspection remains active until this provenance is recorded, preventing the idle weapon evaluator from racing the close callback

Equipped gear moves use a mode-specific rule:

- `Simple` and `Restricted`: only add into empty equipment slots, then store the added loot through `StoreItem(...)` so the weapon and supporting magazines return by mail like normal cargo.
- The seated magazine is also retained as a fallback tracked root. If combat reload ejects it from the tracked weapon tree, it remains temporary cargo instead of leaking into the teammate's persisted kit; while it stays seated, return-root ancestor checks prevent duplicate delivery.
- `Immersive` and `Realistic`: allow the implemented occupied-slot swap cases and do not store equipped loot as return cargo, so the escaped teammate's live equipment snapshot can keep it as the new kit.

Weapon trees are also registered through `RegisterLootedWeaponTree(...)` so patrol reload maintenance can treat picked-up weapons as carried loot and avoid wasting spawned magazines on them.

Weapon moves refresh the follower weapon list, item icon, and equipped slot model after the bot-side transaction so the assembled weapon tree is rendered from its current parts.

Tracked follower loot is not the same thing as protected teammate gear:

- tracked follower loot belongs to the player-facing return/recovery flow
- protected teammate gear belongs to the loadout-management/extraction cleanup flow
- an item tree can pass through real inventory space, so code touching loot movement must keep physical inventory state and tracking caches aligned

Any future gear-swap implementation must update both physical item state and bookkeeping together.

## Reference Behavior

### Vanilla

The vanilla patrol real-looting path is a simple state machine:

1. assemble all loot-point items
2. try equipment replacement
3. fallback non-equipped items into cached bot containers

For replacement, vanilla asks `FindEquipmentSlotToReplaceWithBetterItem(...)` for a compatible slot. If a replacement is accepted, vanilla throws the current item first, then moves the new item into the equipment slot.

The helper uses compatible equipment slots and compares occupied-slot replacement candidates by base price and rarity. Patrol real-looting calls it with primary weapon replacement disabled, so generic weapons are considered for secondary/holster instead of first primary.

This is useful as a reference but is too blunt for spawned squadmates because throw-first replacement can break follower loot tracking, protected item ids, and post-raid bookkeeping if copied directly.

### LootingBots

LootingBots uses a richer policy layer:

- optional simulated examine time
- handbook or ragfair prices
- optional weapon value from attachments
- optional weapon attachment stripping
- gear equip/pickup allow-lists
- armor/helmet/armored-rig swaps by armor class
- backpack swaps by container size
- tactical rig swaps by size with armor-class constraints
- weapon swaps by price against equipped weapons
- transfer of loot from old thrown backpacks/rigs after a swap

This is useful as a reference but does not match pitFireTeam's current constraints. pitFireTeam should keep whole-tree handling and should not inherit weapon stripping, plate stripping, or broad throw-first gear replacement.

## Gear Swapping Phase 1 Contract

Gear swapping is partially live. This section records the implemented slices and the remaining constraints for later swap work.

Gear swapping phase 1 starts with easy weapon opportunities and a narrow tactical-vest protection swap. Primary weapon replacement is intentionally deferred because vanilla bot weapon state is cached beyond the physical inventory slots.

General rules:

- expose gear equip/swap through `Allow Gear Swapping`, separate from the `Pickup Weapons`, `Pickup Gear`, and min/max price filters
- allow additive gear equip behavior in any loadout management mode when the setting is enabled
- bypass `Pickup Weapons` and min/max price for missing-primary acquisition and implemented true swap decisions, but require `Pickup Weapons` for optional second-primary or holster weapon additions and keep ordinary weapon cargo under both category and price filters
- in `Simple` and `Restricted`, only add gear into empty equipment slots and treat that added gear as return cargo instead of saved kit
- in `Immersive` and `Realistic`, allow implemented occupied-slot swaps and leave equipped gear untracked so it can persist as teammate kit
- add easy gear equip as an explicit planner before the current carry-space planner
- keep destructive throw/drop swaps disabled; the narrow tactical-vest upgrade path below only runs when the old vest can be preserved first
- preflight the full swap before executing any destructive transaction
- compare whole item trees; do not compare a weapon by disassembling it
- do not strip weapons for attachments
- do not strip helmets for accessories
- do not strip plates as part of an equip/swap decision; the separate filtered-cargo fallback may take an eligible installed plate after its parent gear remains at the source
- do not use the follower's tactical vest as cargo
- operational magazine moves into the follower's tactical vest are allowed only as part of an accepted weapon equip or vest upgrade plan
- preserve tracked-loot and protected-gear bookkeeping on every successful move/drop
- keep body/container command assignment squadmate-only

### Easy Weapon Equip

This is the preferred first weapon path because it does not displace an existing weapon.

Rules:

- if `FirstPrimaryWeapon` is empty, a detachable-magazine long gun is evaluated from its inserted magazine and all compatible non-empty magazines in vanilla fast access
- vanilla fast access is the follower's tactical vest plus pockets; backpack magazines remain cargo and never contribute
- readiness normally requires two ordinary magazine equivalents; the ordinary reference is the largest capacity among compatible loaded magazines actually inserted, already in fast access, or accepted by the active transfer plan, capped at 30 rounds
- theoretical compatible magazine templates do not affect the reference; five-round magazines require two full-magazine equivalents, where an inserted `4/5` or `5/5` counts as one and each full `5/5` fast-access spare counts as one
- an inserted five-round magazine at `3/5` or below does not count as a full equivalent, so two full spares are required; partial spare magazines do not combine
- for references larger than five rounds, an inserted magazine below half full contributes its actual rounds, at least half full contributes at least one ordinary reference, and compatible fast-access spares contribute their actual rounds
- a detachable-magazine weapon with no inserted magazine first receives the most-loaded compatible magazine from the same body/container through a real transaction
- insertion is a staging step: after it completes, the normal loaded-weapon planner runs again against live inventory, reserves reload landing space, moves fitting spares, and decides primary, secondary, or cargo placement
- a package that remains under-ready after insertion follows the existing secondary and potential-cargo policies; `Pickup Weapons`, whole-tree price, and backpack fit still control ordinary cargo
- a no-inserted-magazine cargo package moves compatible magazines first and the weapon last; if the complete package cannot fit, the magazines stay at the source and the weapon is tried alone as cargo; if the weapon also cannot fit, it stays at the source
- compatible spare magazines from the loot source must physically fit in the vest or pockets as operational magazines, not cargo
- compatible spare magazines must be loaded to count as operational support
- for an empty-primary candidate, magazine maintenance runs only after the planner proves which source magazines can enter operational vest/pocket carry
- the inserted magazine is maintained first; accepted partial spares are then considered from fullest to least full, with equal-shape placement candidates ordered by their live round count
- compatible source magazines may donate their top cartridge stack directly into the inserted magazine or a fuller accepted spare through EFT's real magazine-load transaction; the least-full useful donors are drained first
- a source magazine that cannot itself be carried may still donate good compatible rounds, but accepted spares never pull rounds back out of a fuller accepted spare on a later planning pass
- after donor consolidation, compatible standalone loose source ammunition tops off the remaining useful acquired magazines through the existing real EFT transactions
- empty same-source magazines may enter this top-off plan when their shape can satisfy the operational carry and reload-reserve rules after loading
- empty magazines are admitted only to this provisional placement plan; they are not moved as operational or backpack cargo until a successful top-off gives them usable rounds
- an external inserted magazine is temporarily moved into free grid space on the same body/container, filled there, and restored before normal weapon planning resumes; no source grid space means that inserted magazine is left unchanged
- top-off never modifies the follower's existing pre-raid magazines, preventing found rounds from being merged into an original equipment tree whose Simple/Restricted return ownership would be ambiguous
- top-off compares each source cartridge with all compatible ammunition already carried by the follower plus the inserted/source magazines accepted into the operational package; quantity need and the penetration delta against that round-weighted stock jointly decide whether a downgrade is worthwhile
- readiness being satisfied by partial magazines does not skip top-off: the same need/power/opportunity policy may still fill their free capacity with a worthwhile upgrade, while sufficient equal-or-better package ammunition rejects redundant source rounds
- penetration quality is evaluated in five-point steps: `38` accepts down to `35`, while an exact `35` boundary accepts down to `30` when ammunition is needed
- stocked-ammo opportunities score penetration improvement in those five-point steps against the useful source quantity; `50+` penetration is always worth acquiring when the upgrade path is enabled
- this carried-ammunition comparison is broader than immediate reload readiness: strict cargo can prevent collecting redundant weaker rounds, but it still cannot make a detachable-magazine weapon operational until valid magazines occupy fast access
- every top-off transaction must settle before the resulting magazine count participates in readiness; failures leave the weapon decision to the actual live counts
- the same top-off-first order applies when a later body/container search supplies ammunition for a tracked second-primary or backpack-cargo candidate: fill its acquired inserted/source magazines, rerun operational-magazine placement and readiness, then route any remaining accepted loose rounds through secure container, pockets, backpack, and reload-safe vest space
- reload landing space is selected from the compatible magazines actually available for the weapon, not permanently from the inserted magazine
- candidate magazine shapes are tested largest first; a shape qualifies as the reload reserve only when fast access can hold one magazine of that shape and still leave room for another of the same shape to land
- if a large shape cannot satisfy that pair test, the planner tries the next smaller shape; after finding a valid reserve, it revisits larger magazines and carries each one that individually fits while preserving the selected smaller landing space
- this permits layouts such as `1x1 magazine | 1x2 magazine | empty 1x1 landing space`: the `1x2` can be used and dropped when it cannot land, while the `1x1` magazine retains a valid reload cycle
- an oversized inserted magazine that cannot land in available fast access does not block the weapon; vanilla may drop it on the first reload, after which the selected fitting magazine shape owns the reload reserve
- the accepted weapon equip queues compatible loaded source magazines one at a time while fast-access space remains valid, then decides the weapon destination from settled live inventory
- support magazines bypass normal loot filters once the weapon itself has been accepted; they still must be loaded, compatible with the accepted weapon, safe to take, not already in the follower inventory, and physically placeable in fast access
- magazine fit must use the actual magazine shape, not just total cell count; two-cell, three-cell vertical, and two-by-two magazines have different practical vest requirements
- oversized compatible spare magazines that cannot fit in vest or pockets do not count as operational spares
- compatible magazines that cannot enter fast access are moved to backpack cargo when space permits; those backpack magazines do not contribute to readiness
- only after an accepted weapon is physically equipped in `FirstPrimaryWeapon`, compatible source magazines that fit neither fast access nor backpack remain at the source and may be emptied for ammunition
- secondary, holster, backpack-cargo, and rejected weapon outcomes do not salvage ammunition; their leftover magazines remain loaded at the source
- left-behind magazine ammo is planned one cartridge stack at a time in this order: secure container, pockets, backpack, then tactical vest; oversized internal cartridge groups are split at the ammo template's loose-stack limit, and the complete magazine is capacity-preflighted before its first stack moves, so known insufficient room leaves that magazine loaded at the source; EFT commits generated loose stacks separately, therefore an interruption or runtime transaction failure stops the remaining salvage without claiming cross-transaction rollback
- cartridge groups inside a magazine are never moved as ordinary inventory items; execution follows EFT's unload model by seeding a one-round loose stack through the ammo-to-address operation, filling it through ammo-to-ammo transfers, then advancing to the next internal cartridge group
- tactical-vest ammo placement must still leave an opening for the largest compatible magazine carried for an equipped primary/secondary weapon; an inserted oversized magazine that cannot fit any vest grid is excluded because vanilla must place or drop it elsewhere during reload
- a holstered pistol captured as part of the follower's initial equipment reserves a second, independent vest opening using its largest compatible carried magazine shape; raid-acquired holster weapons do not receive this additional reserve
- ordinary cargo weapons do not receive this ammo-salvage bypass; their weapon and magazine package remains controlled by `Pickup Weapons`, price, and backpack fit
- a weapon that reaches the readiness threshold goes into `FirstPrimaryWeapon`; a weapon still below threshold uses empty `SecondPrimaryWeapon` as an inert support holding slot
- pitFireTeam does not force vanilla to treat a secondary-only long gun as the bot's main weapon
- a compatible loose magazine already in the follower backpack may be moved into vest/pockets for a newly found weapon, but only when the complete combined plan makes that weapon primary-ready
- if the weapon plus backpack spare remain under threshold, the spare stays in the backpack and the weapon goes to secondary
- when a low-loaded found weapon, a found source spare, and a backpack spare collectively pass readiness, the source spare moves first, the backpack spare follows, and the weapon then equips as primary
- when a later compatible magazine raises the actual fast-access total to the threshold, an idle out-of-combat follower promotes the tracked support weapon, or one unambiguous tracked backpack cargo weapon, into the still-empty primary slot and registers it normally
- this idle reevaluation runs after a commanded loose-magazine pickup finishes, so the pickup command keeps its existing placement and voice behavior while the settled inventory can make a carried weapon usable
- a ready tracked secondary keeps priority; if multiple backpack weapons are simultaneously ready, automatic cargo promotion waits for the future weapon-comparison phase
- when settled magazine work makes the tracked secondary ready but EFT still reports busy hands, the command retains that exact promotion through loot completion, waits for the two-second Attention-style reset, and retries the slot transaction without waiting for combat memory to clear
- when that later spare is found during another body/container search, it starts the transfer chain; compatible backpack cargo then moves into fast access before the tracked secondary promotes
- evaluate a tracked secondary against newly found compatible magazines before evaluating another weapon package from the same body/container
- if that secondary becomes ready, promote it to primary; other source weapons then use ordinary filtered cargo handling
- while first primary is empty, if second primary is occupied and a new candidate remains unready, `Pickup Weapons` and the weapon's whole-tree minimum/maximum price decide whether it may become potential-weapon cargo
- after the weapon passes those ordinary cargo filters, compatible loaded source magazines join its backpack package when space permits; magazines move first and the weapon moves last
- if the package cannot fit, leave its magazines at the source and try the weapon alone; if the weapon cannot fit, leave it too
- if `Pickup Weapons` is disabled or the weapon fails price, leave both the weapon and its package magazines at the source
- compatible bundle magazines that do not fit in the backpack remain at the source
- if `FirstPrimaryWeapon` is occupied, `Pickup Weapons` is enabled, and `SecondPrimaryWeapon` is empty, a long gun with an inserted magazine and usable ammunition may be added as a real vanilla support weapon
- compatible loaded source magazines for that support add bypass price only while they fit in vest/pockets and preserve landing space for the inserted magazine; because the weapon remains second-primary support, overflow magazines remain loaded at the source
- after that support weapon is moved, announce it with `EPhraseTrigger.LootGeneric`, refresh vanilla slot state, and create `WeaponManager.Info[SecondPrimaryWeapon]` without forcing a hand switch away from the working primary
- once first and second primary are occupied, another long gun is ordinary filtered cargo; it cannot recruit a potential-weapon magazine package
- when holster is occupied, a found pistol is likewise ordinary filtered cargo and its compatible magazines do not inherit a package bypass
- if `Pickup Weapons` is enabled and `Holster` is empty, a valid pistol may be equipped there as a non-primary `EPhraseTrigger.LootGeneric` result
- if the matching slot is occupied, do not replace it in the empty-slot phase
- still register the moved weapon tree as looted so patrol reload maintenance does not feed spawned magazines into cargo/support weapons
- when an accepted looted weapon becomes first primary, patrol reload may use its originally inserted magazine and magazines successfully acquired for that weapon package; mechanically compatible spawned magazines remain excluded
- body/container looting records a newly equipped primary but does not select it while more loot transactions or the request-layer interaction remain active
- after all moves finish and the loot command clears, wait one second for inventory/interaction ownership to close, apply the same `FollowerRecovery.SoftReset(...)` recovery step used by `Attention`, then refresh selector slot caches, rebuild `WeaponManager.Info[FirstPrimaryWeapon]`, and request the normal vanilla main-hand transition
- commanded loose-primary pickup uses the same one-second post-pickup recovery and selection handoff after its pickup state clears
- if a tracked looted weapon was already held inert in second primary, register `WeaponManager.Info[SecondPrimaryWeapon]` once the new first primary exists so vanilla can use it as the real support weapon
- do not pre-gate that request with `CanChangeHands()`: the check includes interaction/controller states that vanilla's scheduled weapon process owns and may not clear until the transition is requested
- if selector state remains mid-transition, retry through the bot delayed-task manager and use a bounded current-state fast-forward only after the ordinary draw window; stop immediately if the follower dies or leaves the active bot state

Missing-primary weapon acquisition ignores min/max price and bypasses the `Pickup Weapons` category filter because it is an explicit primary-equipment plan rather than ordinary weapon cargo. A working-primary follower only adds an optional second-primary or holster weapon when `Pickup Weapons` is enabled. Supporting spare magazines bypass the normal loot filters only after the corresponding primary or Pickup-Weapons-authorized support plan is accepted.

### Grenade-Launcher Slot Preference

Standalone grenade and rocket launchers are the exception to the ordinary missing-primary destination policy. `Allow Gear Swapping` treats them as tactical support weapons and prefers `SecondPrimaryWeapon` whenever a conventional shoulder weapon can occupy `FirstPrimaryWeapon`.

- when the same body/container contains a launcher and a conventional long gun, plan the conventional weapon first, force it into first primary even when it is empty or below the normal readiness threshold, and place the launcher in second primary
- when the follower currently has a launcher in first primary and finds a conventional long gun, move the launcher to empty second primary before processing the new weapon; the conventional weapon then becomes first primary
- when first primary is empty and a conventional weapon is being held in second primary for insufficient ammunition, finding a launcher promotes that conventional weapon to first primary without a readiness gate, then places the launcher in second primary
- when the follower already has a conventional first primary and empty second primary, a found launcher may fill second primary as an equipment decision even if `Pickup Weapons` is disabled; this support-only result uses `LootGeneric`
- loose-ammunition support is planned in weapon-role order: finish the accepted conventional primary package first, then plan compatible ammunition for the accepted secondary launcher
- accepted launcher grenades use the existing live-space order of tactical vest, pockets, backpack, then secure container; each follow-up rechecks the settled inventory before choosing its destination
- when no conventional shoulder weapon is carried or found, a launcher may still use first primary through the existing missing-primary path
- these internal slot moves are staging transactions: they do not count the already-carried weapon as newly looted or register it for post-raid return a second time
- once the conventional primary exists, the normal delayed primary registration/selection path also refreshes the launcher as vanilla support weapon state

This is slot-role normalization, not general weapon comparison. It does not compare two conventional primaries, displace an occupied second-primary slot, or select a better launcher.

### Compatible Loose-Ammunition Support

Once a body/container weapon has been accepted as equipment or as filtered weapon cargo, compatible loose ammunition from that same source may accompany it regardless of whether the weapon uses detachable magazines, an internal feed, or supported direct chambers.

- loose ammunition never contributes speculatively to detachable-magazine readiness; only rounds committed by the dedicated top-off path count
- internal-magazine and supported chamber-fed weapons may first load compatible loose ammunition through their dedicated real transactions; only settled loaded rounds and fitting reserve stacks contribute to readiness
- ordinary loose ammunition is placed in secure container, pockets, backpack, then tactical vest
- launcher grenades are the deliberate exception: place them in tactical vest, pockets, and backpack before using the secure container as the final fallback
- launcher-ammo planning and execution both re-evaluate live container occupancy after each move, so later grenades spill into the next normal storage area instead of being sent directly to secure storage
- tactical-vest fallback must preserve the existing long-gun and initial-holster reload landing spaces
- source ammunition inside another weapon or magazine is not loose ammunition and is never selected by this path

### Tactical Primary Ammunition

With `Allow Gear Swapping` enabled, loose ammunition on a searched body/container is evaluated against the equipped detachable-magazine primary before ordinary filtered pickup:

- magazine top-off is readiness maintenance and does not depend on `Pickup Weapons` or ordinary price/category filters
- if the primary is not ready, compatible empty or partial magazines already in vest/pockets are filled before source-ammo acquisition is considered; the inserted magazine is not modified by this phase
- carried loose ammunition is preferred as the top-off supply; this includes the managed primary-ammo stacks injected into the secure container in every non-Realistic mode
- `Immersive`/`Realistic` may use compatible searched-source ammunition after carried supply; `Simple`/`Restricted` do not merge searched rounds into protected spawned magazines and may only carry accepted source ammunition as returnable cargo
- top-off only fills free capacity: it never unloads, replaces, or rearranges cartridges that are already inside a magazine
- **need weight** is the compatible carried-round deficit against the weapon's two-magazine reserve target
- **power weight** is source penetration versus the round-weighted penetration of all compatible rounds already carried, including loaded magazines, chambers, loose stacks, and strict cargo
- below one ordinary magazine, critical need accepts any mechanically compatible source round
- between one and two magazine equivalents, need weight and the relative penetration gain/loss are combined; severe downgrades can be rejected when the remaining shortage is small
- at or above reserve, the existing-primary path acquires no additional source rounds; stronger-ammunition replacement remains deferred until the top-off cases are runtime-stable
- these tactical decisions supersede ordinary price/category pickup only for loose rounds compatible with the equipped primary; unrelated ammunition remains under normal filters
- compatible loose ammunition accompanying an accepted weapon package reuses this evaluator; accepted source stacks are counted sequentially so crossing the reserve target can stop later redundant stacks in the same search
- penetration drives the weighted decision; damage and armor damage are deterministic ordering tie-breaks between otherwise equal source cartridges

### Internal-Magazine Weapon Readiness

Tube-fed and other `InternalMagazine` weapons use a separate loose-ammunition path:

- the attached internal magazine capacity is the readiness reference; primary readiness requires two capacity equivalents
- loaded contribution is the live rounds in the attached magazine plus live rounds in the chamber
- reserve contribution comes only from compatible loose ammunition already carried in vanilla reload-search locations, or source stacks proven to fit in secure container, pockets, backpack, or reload-safe vest space
- loose ammunition inside another magazine or weapon never counts
- ammunition manually placed as strict cargo through `View Backpack` never counts until it is removed and reacquired through a command
- if the attached magazine has room, the planner loads compatible source ammunition into it first through a real EFT transaction
- the load transaction is verified by an increased live loaded-round count before reserve moves or weapon placement continue
- internal-magazine revolvers use the cylinder's ammunition compatibility directly; they do not require a separate chamber slot to accept loose reserves
- compatible source reserves bypass price/category filters only inside the accepted internal-weapon plan; stacks that do not fit the shared loose-ammo destination policy remain at the source and contribute nothing
- after all transactions settle, a ready weapon enters first primary; an under-ready loaded weapon uses empty second primary; with no equipment destination, ordinary `Pickup Weapons` and price rules own cargo
- when the follower already has a working primary and `Pickup Weapons` is enabled, a loaded internal-magazine weapon may be added to empty second primary as usable support and announces `EPhraseTrigger.LootGeneric`
- later compatible source ammunition may complete and promote one tracked internal-magazine secondary or backpack weapon

### Chamber-Fed Weapon Readiness

Supported non-launcher `OnlyBarrel` weapons, including single-shot and double-barrel break actions, use their live chambers as the attached feed:

- readiness is the larger of two complete chamber-load equivalents or eight total rounds; a two-chamber shotgun therefore needs eight shells
- only live, unspent chamber rounds contribute as loaded ammunition
- compatible loose reserve rounds use the same strict-cargo exclusion and secure-container, pockets, backpack, reload-safe-vest placement rules as internal-magazine reserves
- an empty or partially loaded weapon fills one empty chamber at a time through vanilla's off-hands `Weapon.Apply(...)` inventory transaction
- every chamber transaction must settle and increase the live chamber count before another shell, reserve stack, or weapon destination is planned
- eight rounds is only the primary-readiness threshold; accepted compatible source stacks still move whole through the established loose-ammo policy
- a ready chamber-fed weapon enters first primary; an under-ready loaded weapon may use empty second primary; ordinary `Pickup Weapons` and price rules own cargo when no equipment destination is available
- with a working primary, an optional chamber-fed support add still requires `Pickup Weapons` and announces `EPhraseTrigger.LootGeneric`
- later compatible source shells may complete and promote one tracked chamber-fed secondary or backpack weapon

Still deferred weapon-feed cases:

- launcher-versus-launcher comparison and replacement, and any case requiring displacement of an occupied second-primary slot
- holster-revolver gear decisions and cylinder transactions that cannot use the shared internal-magazine path
- equipped-primary donor-magazine consolidation remains separate from the acquired-weapon package path
- keep each feed system separate from detachable-magazine handling and implement/test it as its own scenario

### Narrow Tactical-Vest Upgrade

Tactical-vest replacement is an early gear-swap candidate, but only under strict preflight because it touches operational magazine space and, for plate carriers, protection.

This path is active in a conservative phase 1 form.

Eligible cases:

- empty follower tactical vest slot: equip a found tactical vest directly
- `Immersive`/`Realistic` only: current tactical vest has no plate-carrier capability, the found vest has plate-carrier capability, the found vest can end the transaction with usable protection, and the follower is not wearing separate armor
- `Immersive`/`Realistic` only: current tactical vest is a plate carrier, and the found plate carrier is meaningfully better

Plate-carrier comparison rules:

- compare the found vest as a whole equipment tree, including installed plates
- phase 1 does not move current plates into the found vest; it preserves the old vest tree instead
- require the found protection score to be higher when replacing an existing armored vest
- do not strip plates during the vest-upgrade transaction; filtered cargo may later consider an installed plate only if the whole vest remains at the source

Vest-upgrade transaction rules:

- preflight current vest contents and magazine positions before changing anything
- `Simple` and `Restricted` stop after the empty-slot add case; they never replace an occupied tactical vest
- phase 1 refuses occupied-vest replacement when the old vest has any non-plate contents, preserving operational magazines in the current vest instead of moving them
- do not use the tactical vest as general cargo during the swap; its purpose is operational space
- move the old vest tree into the follower's backpack first; only then equip the found vest as a follow-up move
- if the old vest cannot fit in the backpack, do not throw it down in phase 1
- if any step cannot be simulated or executed safely, skip the vest upgrade and leave the current vest untouched

If the found vest is superior for protection but cannot preserve the current vest contents, it is not an equipment upgrade. Treat it as cargo: pick up the found vest into the backpack only if it fits there. If it cannot be carried as cargo, leave it.

### Weapon Replacement Complexity

Primary replacement is not a simple inventory move.

Vanilla `BotWeaponSelector` caches `FirstPrimaryWeaponItem`, `SecondPrimaryWeaponItem`, `HolsterItem`, and slot availability. `BotWeaponManager` also owns a per-slot `BotWeaponInfo` dictionary, and each `BotWeaponInfo` creates a `BotReload` instance for the weapon that was present when the info was built. Replacing the physical `FirstPrimaryWeapon` item without rebuilding that slot's weapon info can leave reload, fire-mode, selector, or current-weapon state pointing at the old weapon.

Because of that, primary replacement needs a controlled rebind flow:

1. require out-of-combat, not reloading, and hands-change safe state
2. move hands away from primary, preferring secondary, then holster, then scabbard/knife
3. atomically swap or move the candidate into `FirstPrimaryWeapon`
4. refresh selector slot caches
5. rebuild `WeaponManager.Info[FirstPrimaryWeapon]` for the new weapon
6. change back to main
7. verify `CurrentWeapon`, `MainWeaponInfo.weapon`, and `MainWeaponInfo.Reload.Weapon` all match the new primary

If any step cannot be proven safe, skip the replacement and leave follower gear untouched.

### Restricted Weapon Replacement

If primary replacement is later enabled, keep it narrow:

- empty compatible slots are still preferred before replacing an equipped weapon
- only `FirstPrimaryWeapon` may be displaced
- `SecondPrimaryWeapon` is never displaced and is never used as a rotation slot
- `Holster` is never displaced; pistols only equip into an empty holster
- secondary, holster, and scabbard/knife may be used only as temporary hands state before rebinding primary
- replacement must compare whole looted weapon tree value against the current primary weapon tree
- compatible magazines belonging to the follower's secondary/support weapon must not be consumed by the new primary
- patrol reload must not blindly search every reachable magazine/ammo stack for acquired weapons

Primary replacement ammo cases:

- same caliber plus compatible current primary magazines is the cleanest replacement case
- same caliber but incompatible current magazines requires compatible new magazines from the loot source
- same caliber with incompatible current magazines and empty/partial new magazines is a mag-migration case, not an easy swap
- different caliber requires loaded compatible magazines from the loot source
- no compatible magazines means no replacement, even if the weapon value is better

Mag-migration preflight:

- confirm backpack space for the old incompatible magazines
- confirm tactical-vest slots for the new compatible magazines
- move old magazines from vest to backpack only if all old mags can be preserved
- move new compatible magazines into vest only if they fit without disturbing support-weapon magazines
- transfer ammo from old magazines into new magazines only after the EFT transaction path is verified
- if any migration step cannot be simulated or executed safely, skip the replacement

Mag migration should not be part of gear swapping phase 1.

Backpack swaps:

- only consider a backpack swap when the looted backpack gives a meaningful capacity improvement
- account for the old backpack contents before throwing or replacing it
- do not lose tracked follower loot that is inside the old backpack
- do not use the follower's tactical vest as temporary cargo unless a later explicit design changes the rig rule

Additional armor and armored-rig swaps:

- broader armor/rig swapping remains high risk because it touches protection, plate systems, reload space, and protected gear cleanup
- if implemented beyond the narrow tactical-vest upgrade above, compare the worn item as a whole tree
- do not use plate stripping as an armor-swap shortcut; filtered cargo fallback remains separate from replacement policy
- do not replace body armor or armored rigs beyond the narrow vest path unless current contents, protection policy, and magazine-space policy are explicitly preserved

Headwear swaps:

- compare the helmet/headwear tree as a whole, including face shield, mounts, and night vision devices
- do not strip attached devices as separate loot
- avoid replacing special-purpose headgear without a clear improvement rule

Transaction rules:

- avoid vanilla-style throw-first unless the destination and old-item handling are already proven valid
- prefer a planned sequence that can fail before altering the follower's current kit
- after every successful transaction, update stored-loot and weapon-tree tracking immediately
- cleanup must clear taker ownership and active command state even after failure

## Test Checklist

Body/container basics:

- no eligible non-dogtag items -> `LootNothing`
- eligible item but no backpack/pocket/equipment destination -> `Negative`
- eligible non-dogtag items -> search sound, wait, pickup-confirmation phrase, short beat, move items, `Ready`
- weapon becomes the follower's combat primary during this search -> pickup confirmation is `LootWeapon`
- weapon becomes second-primary support, enters holster, remains backpack cargo, or stays an under-ready left-shoulder candidate -> pickup confirmation is `LootGeneric`
- search sound stops when search ends
- completed container closes
- interrupted container may remain open
- command replacement is ignored while searching
- combat interruption stops looting

Assignment:

- body/container commands only assign saved teammates spawned through the raid squad flow
- non-teammate body/container assignment only picks followers with a complete NavMesh path of 22m or less
- followers with active/pending loot commands are skipped so rapid commands split across followers
- followers with no backpack/pocket free area are skipped for filtered body/container looting
- explicitly ordering a completed corpse searches it again; autonomous `Go loot` skips completed corpses
- an active body reservation still prevents a second follower from looting the same corpse

Filtered body/container rules:

- dogtag is moved before any vest, magazine, or weapon transaction on non-teammate USEC/BEAR bodies
- dogtag-only body looting still says `LootNothing`
- teammate dogtags are not treated as filtered dogtag loot
- backpack magazines can be looted if eligible
- pocket and vest magazines are skipped
- loose armor plates are ignored; installed plates may be taken as price-qualified, 50-percent-durability fallbacks after their parent armor/rig stays behind
- tactical vest is not used as follower carry space, except operational magazine placement during an accepted weapon equip or vest upgrade
- ordinary cargo price minimum and maximum apply to whole item trees
- ordinary cargo category filters apply before price
- missing-primary acquisition and implemented true swaps are controlled by `Allow Gear Swapping`; optional support/holster weapon additions additionally require `Pickup Weapons`

Gear swapping phase 1 tests:

- missing-primary weapon equip uses the centralized two-ordinary-magazine readiness formula after all planned fast-access transfers settle
- tube-fed/internal-magazine weapons load the attached magazine first, then count only settled loaded rounds and fitting loose reserves toward two-load readiness
- supported single- and multi-chamber break-action weapons load empty chambers one real round transaction at a time and require at least eight total rounds; accepted compatible loose-ammo stacks still move whole
- internal-feed loose ammunition nested in magazines/weapons or marked as strict cargo is ignored
- accepted weapon-support loose ammunition uses secure container, pockets, backpack, then reload-safe vest space; detachable-magazine readiness remains magazine-only
- detachable-magazine loose-ammo top-off fills acquired inserted/source magazines before readiness and never counts uncommitted loose cartridges
- acquired detachable-magazine packages consolidate compatible partial donor magazines before loose-ammo top-off and readiness; every donor transfer must settle before the next planning pass
- tracked-secondary top-off runtime passed with an empty `0/30` inserted magazine, one `30/30` source spare, and compatible loose ammunition: settled readiness reached `60/60`, promotion used `LootWeapon`, and remaining accepted rounds entered protected storage
- a later donor-consolidation test exposed two ordering gaps now awaiting retest: loot-time `handsBusy` discarded a ready promotion until combat ended, and a refillable empty source magazine was rejected before top-off; both are now preserved through command-owned post-loot promotion and provisional empty-mag placement
- after magazine maintenance, remaining source-ammo pickup uses the shared need/power model against the follower's complete compatible cartridge stock; top-off itself is driven by missing operational magazine capacity
- an equipped primary with a critical shortage accepts weaker compatible ammunition; a small shortage can reject a large penetration downgrade; sufficient stock rejects equal/weaker ammunition
- equipped-primary top-off works with `Pickup Weapons` disabled, uses carried loose supply before searched rounds, and never removes existing magazine cartridges
- large compatible spare magazines that do not fit the vest/pocket grids while preserving reload landing space do not count as operational spares
- an under-threshold weapon uses empty secondary; with secondary occupied, only ordinary filtered cargo rules may move it into the backpack
- with a working primary and `Pickup Weapons` disabled, an optional support weapon and its magazines/ammunition remain at the source
- with a working primary and `Pickup Weapons` enabled, an accepted second-primary support weapon uses `LootGeneric` and does not take the current primary out of hand
- a ready second-primary package may take every compatible loaded source magazine that fits operational fast access while preserving reload reserve; the runtime `50/50 + 50/50 + 20/20 + 20/20` package registered successfully as support
- a tracked under-threshold secondary weapon promotes into empty primary after later compatible fast-access ammunition makes it ready
- newly found weapons recruit compatible backpack cargo only when the executable combined fast-access plan reaches readiness
- a later source spare can recruit the backpack spare for a tracked secondary, then promote that weapon from settled live state
- narrow vest upgrade can fill an empty tactical vest slot or replace a worn vest only after preserving the old vest tree in the backpack
- `Simple`/`Restricted` narrow vest behavior stops at empty-slot add; occupied vest replacement is refused
- looted weapon does not trigger patrol reload maintenance with spawned magazines
- after a looted primary reloads, follower death/raid-end cleanup does not leave its ejected original magazine in the teammate's persisted `Simple`/`Restricted` kit
- loose weapon pickup into `FirstPrimaryWeapon` registers as the combat primary, and a pending weapon-taken callback cannot fault after that follower dies
- a looted launcher in `FirstPrimaryWeapon` remains the follower's real primary and enters the grenadier objective for eligible combat targets; only a launcher used from `SecondPrimaryWeapon` returns to another main weapon after the attempt
- rejected swap leaves current follower gear untouched
- accepted swap updates tracking and does not duplicate or orphan old gear
- rig magazine space remains stable
