# Looting

Date: 2026-07-08

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

Followers with enemies are not eligible for new body/container loot assignment. A follower already handling an active loot/pickup command is skipped so rapid player commands can be assigned to the next available follower.

Once body/container searching begins, normal replacement commands are ignored until the loot command finishes. Combat, timeout, target invalidation, player death, or safety cleanup can still interrupt the command.

## Loose Item Pickup

Inputs:

- `EPhraseTrigger.LootGeneric`
- `EPhraseTrigger.LootWeapon`

Current behavior:

- requires `InteractableObjects.GetCurLootItem()`
- chooses the closest active eligible follower to the world item
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
- teammate corpses choose the closest eligible squadmate
- non-teammate corpses choose the closest eligible loot carrier within 22m
- non-teammate assignment ignores followers with no free backpack/pocket grid area
- ownership is reserved through `InteractableObjects.SetBodyLootTaker(...)`
- an explicit player `Check Him` / `Loot Body` order may revisit a corpse that a follower already completed
- checked-body history is retained for autonomous `Go loot` selection, which skips completed corpses
- neither path can assign a corpse while another follower actively owns its loot reservation
- command timeout is 75 seconds

Execution:

- moves to the corpse
- starts a search phase before moving items
- says a pickup-confirmation phrase after the simulated search, when the first real non-dogtag loot move is queued
- uses `EPhraseTrigger.LootWeapon` only when the executable plan makes a weapon usable as primary, secondary, or holster equipment during this search
- uses `EPhraseTrigger.LootGeneric` for ordinary loot, backpack weapon cargo, potential-weapon packages, and an under-ready long gun held inert in the left-shoulder slot
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
4. tactical vest contents

Rules:

- dogtags bypass category and price filters, but still need a backpack/pocket destination
- dogtags are moved to backpack or pockets
- dogtag-only body looting still reports `EPhraseTrigger.LootNothing`
- normal filtered looting does not take the corpse's worn backpack, armor, armored rig, or tactical vest as whole equipment
- backpack contents are checked item by item
- pocket and vest contents skip magazines entirely so follower reload space is not disturbed
- backpack and container magazines can still be looted if filters, price, and fit pass
- armor plates are ignored, including loose cargo plates and installed plates inside armor or plate-carrier trees
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
- chooses the closest eligible loot carrier within 22m
- ignores followers with no free backpack/pocket grid area
- ownership is reserved through `InteractableObjects.SetContainerLootTaker(...)`
- command timeout is 75 seconds

Execution:

- moves to the container
- opens the container if shut
- starts a search phase before moving items
- says a pickup-confirmation phrase after the simulated search, when the first real loot move is queued
- uses `EPhraseTrigger.LootWeapon` only when the executable plan makes a weapon usable as primary, secondary, or holster equipment during this search
- uses `EPhraseTrigger.LootGeneric` for ordinary loot, backpack weapon cargo, potential-weapon packages, and an under-ready long gun held inert in the left-shoulder slot
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
- `Pickup Gear`
- `Allow Gear Swapping`

The four pickup category checkboxes default on. `Allow Gear Swapping` defaults off.

Category mapping:

- `Food` covers food and drinks.
- `Meds` covers usable medical items, drugs, stimulators, and med kits.
- `Valuables` covers barter items, keys, special items, info items, money, and other non-gear loot.
- `Gear` covers weapons, armor, headgear, ammo, magazines, weapon mods, grenades, and other equipment-class items.

Armor plates remain ignored even when `Pickup Gear` is enabled.

`Allow Gear Swapping` defaults off. It is the explicit gate for gear equip/swap behavior in every loadout mode; post-raid ownership follows the active loadout management mode.

`Pickup Gear` controls gear taken as cargo. It does not disable `Allow Gear Swapping`: eligible add/swap candidates bypass category and min/max price filters but still keep protection, compatibility, and executable-placement safety gates. If the gear cannot be equipped and falls back to ordinary cargo handling, `Pickup Gear` and min/max price apply again.

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
- armor plates are not moved

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

- expose gear equip/swap through `Allow Gear Swapping`, separate from the existing `Pickup Gear` category filter and min/max price filters
- allow additive gear equip behavior in any loadout management mode when the setting is enabled
- bypass `Pickup Gear` and min/max price for the actual add/swap planner, but keep those filters authoritative for ordinary gear cargo fallback
- in `Simple` and `Restricted`, only add gear into empty equipment slots and treat that added gear as return cargo instead of saved kit
- in `Immersive` and `Realistic`, allow implemented occupied-slot swaps and leave equipped gear untracked so it can persist as teammate kit
- add easy gear equip as an explicit planner before the current carry-space planner
- keep destructive throw/drop swaps disabled; the narrow tactical-vest upgrade path below only runs when the old vest can be preserved first
- preflight the full swap before executing any destructive transaction
- compare whole item trees; do not compare a weapon by disassembling it
- do not strip weapons for attachments
- do not strip helmets for accessories
- do not remove armor plates as standalone loot
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
- a package that remains under-ready after insertion follows the existing secondary and potential-cargo policies; `Pickup Gear`, whole-tree price, and backpack fit still control ordinary cargo
- a no-inserted-magazine cargo package moves compatible magazines first and the weapon last; if the complete package cannot fit, the magazines stay at the source and the weapon is tried alone as cargo; if the weapon also cannot fit, it stays at the source
- compatible spare magazines from the loot source must physically fit in the vest or pockets as operational magazines, not cargo
- compatible spare magazines must be loaded to count as operational support
- the accepted weapon equip queues compatible loaded source magazines one at a time while fast-access space remains valid, then decides the weapon destination from settled live inventory
- support magazines bypass normal loot filters once the weapon itself has been accepted; they still must be loaded, compatible with the accepted weapon, safe to take, not already in the follower inventory, and physically placeable in fast access
- magazine fit must use the actual magazine shape, not just total cell count; two-cell, three-cell vertical, and two-by-two magazines have different practical vest requirements
- oversized compatible spare magazines that cannot fit in vest or pockets do not count as operational spares
- only after an accepted weapon is physically equipped in `FirstPrimaryWeapon`, compatible source magazines that could not be moved into fast access remain at the source but may be emptied for ammunition
- secondary, holster, backpack-cargo, and rejected weapon outcomes do not salvage ammunition; their leftover magazines remain loaded at the source
- left-behind magazine ammo is planned one cartridge stack at a time in this order: secure container, pockets, backpack, then tactical vest; oversized internal cartridge groups are split at the ammo template's loose-stack limit, and the complete magazine is capacity-preflighted before its first stack moves, so known insufficient room leaves that magazine loaded at the source; EFT commits generated loose stacks separately, therefore an interruption or runtime transaction failure stops the remaining salvage without claiming cross-transaction rollback
- cartridge groups inside a magazine are never moved as ordinary inventory items; execution follows EFT's unload model by seeding a one-round loose stack through the ammo-to-address operation, filling it through ammo-to-ammo transfers, then advancing to the next internal cartridge group
- tactical-vest ammo placement must still leave an opening for the largest compatible magazine carried for an equipped primary/secondary weapon; an inserted oversized magazine that cannot fit any vest grid is excluded because vanilla must place or drop it elsewhere during reload
- a holstered pistol captured as part of the follower's initial equipment reserves a second, independent vest opening using its largest compatible carried magazine shape; raid-acquired holster weapons do not receive this additional reserve
- ordinary cargo weapons do not receive this ammo-salvage bypass; their weapon and magazine package remains controlled by `Pickup Gear`, price, and backpack fit
- a weapon that reaches the readiness threshold goes into `FirstPrimaryWeapon`; a weapon still below threshold uses empty `SecondPrimaryWeapon` as an inert support holding slot
- pitFireTeam does not force vanilla to treat a secondary-only long gun as the bot's main weapon
- a compatible loose magazine already in the follower backpack may be moved into vest/pockets for a newly found weapon, but only when the complete combined plan makes that weapon primary-ready
- if the weapon plus backpack spare remain under threshold, the spare stays in the backpack and the weapon goes to secondary
- when a low-loaded found weapon, a found source spare, and a backpack spare collectively pass readiness, the source spare moves first, the backpack spare follows, and the weapon then equips as primary
- when a later compatible magazine raises the actual fast-access total to the threshold, an idle out-of-combat follower promotes the tracked support weapon, or one unambiguous tracked backpack cargo weapon, into the still-empty primary slot and registers it normally
- this idle reevaluation runs after a commanded loose-magazine pickup finishes, so the pickup command keeps its existing placement and voice behavior while the settled inventory can make a carried weapon usable
- a ready tracked secondary keeps priority; if multiple backpack weapons are simultaneously ready, automatic cargo promotion waits for the future weapon-comparison phase
- when that later spare is found during another body/container search, it starts the transfer chain; compatible backpack cargo then moves into fast access before the tracked secondary promotes
- evaluate a tracked secondary against newly found compatible magazines before evaluating another weapon package from the same body/container
- if that secondary becomes ready, promote it to primary; other source weapons then use ordinary filtered cargo handling
- while first primary is empty, if second primary is occupied and a new candidate remains unready, `Pickup Gear` and the weapon's whole-tree minimum/maximum price decide whether it may become potential-weapon cargo
- after the weapon passes those ordinary cargo filters, compatible loaded source magazines join its backpack package when space permits; magazines move first and the weapon moves last
- if the package cannot fit, leave its magazines at the source and try the weapon alone; if the weapon cannot fit, leave it too
- if `Pickup Gear` is disabled or the weapon fails price, leave both the weapon and its package magazines at the source
- compatible bundle magazines that do not fit in the backpack remain at the source
- if `FirstPrimaryWeapon` is occupied and `SecondPrimaryWeapon` is empty, a long gun with an inserted magazine and usable ammunition may be added as a real vanilla support weapon
- compatible loaded source magazines for that support add bypass price only while they fit in vest/pockets and preserve landing space for the inserted magazine; because the weapon remains second-primary support, overflow magazines remain loaded at the source
- after that support weapon is moved, refresh vanilla slot state and create `WeaponManager.Info[SecondPrimaryWeapon]` without forcing a hand switch away from the working primary
- once first and second primary are occupied, another long gun is ordinary filtered cargo; it cannot recruit a potential-weapon magazine package
- when holster is occupied, a found pistol is likewise ordinary filtered cargo and its compatible magazines do not inherit a package bypass
- if `Holster` is empty, a valid pistol may be equipped there
- if the matching slot is occupied, do not replace it in the empty-slot phase
- still register the moved weapon tree as looted so patrol reload maintenance does not feed spawned magazines into cargo/support weapons
- after equip, refresh selector slot caches, rebuild `WeaponManager.Info[FirstPrimaryWeapon]` for the new weapon, and request a main-hand switch when hands can safely change
- if a tracked looted weapon was already held inert in second primary, register `WeaponManager.Info[SecondPrimaryWeapon]` once the new first primary exists so vanilla can use it as the real support weapon
- if hands are temporarily busy or selector state is mid-transition, retry the main-hand switch briefly through the bot delayed-task manager and log the final blocker if the new primary never becomes active

Easy weapon equip ignores min/max price and bypasses the `Pickup Gear` category filter because it is an explicit equipment plan rather than ordinary gear cargo. Supporting spare magazines bypass the normal loot filters after the weapon itself is accepted so the follower can build a usable reload pool.

Known next weapon-feed case:

- tube-fed, internal-magazine, and chamber-fed weapons, including applicable shotguns and bolt-action rifles, are not covered by the detachable-magazine planner and will need a separate loose-round loading policy
- compatible loose ammunition may later top off partial detachable magazines and make them readiness-eligible, but those rounds must not count until a real top-off transaction succeeds
- when several compatible magazines are partially loaded, a later repacking phase should top off the fullest useful magazines from partial donor magazines first, then evaluate readiness from the resulting settled magazine states
- repacked ammunition must move through real inventory transactions; failed or interrupted transfers must not contribute speculatively or count donor rounds twice
- these weapons need a separate easy-equip rule based on their loaded shells plus compatible loose ammunition found in the same body/container
- keep this separate from detachable-magazine handling and implement/test it as its own scenario

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
- do not take plates as standalone loot outside the accepted vest-upgrade transaction

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
- do not take plates separately as a shortcut
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
- weapon becomes usable equipment during this search -> pickup confirmation is `LootWeapon`
- weapon remains backpack cargo or an under-ready left-shoulder candidate -> pickup confirmation is `LootGeneric`
- search sound stops when search ends
- completed container closes
- interrupted container may remain open
- command replacement is ignored while searching
- combat interruption stops looting

Assignment:

- body/container commands only assign saved teammates spawned through the raid squad flow
- non-teammate body/container assignment only picks followers within 22m
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
- armor plates are ignored
- tactical vest is not used as follower carry space, except operational magazine placement during an accepted weapon equip or vest upgrade
- ordinary cargo price minimum and maximum apply to whole item trees
- ordinary cargo category filters apply before price
- gear add/swap is controlled by `Allow Gear Swapping`, not by cargo price/category filters

Gear swapping phase 1 tests:

- missing-primary weapon equip uses the centralized two-ordinary-magazine readiness formula after all planned fast-access transfers settle
- tube-fed/internal-magazine shotgun support remains a separate loose-ammunition scenario to implement
- large compatible spare magazines that do not fit the vest/pocket grids while preserving reload landing space do not count as operational spares
- an under-threshold weapon uses empty secondary; with secondary occupied, only ordinary filtered cargo rules may move it into the backpack
- a tracked under-threshold secondary weapon promotes into empty primary after later compatible fast-access ammunition makes it ready
- newly found weapons recruit compatible backpack cargo only when the executable combined fast-access plan reaches readiness
- a later source spare can recruit the backpack spare for a tracked secondary, then promote that weapon from settled live state
- narrow vest upgrade can fill an empty tactical vest slot or replace a worn vest only after preserving the old vest tree in the backpack
- `Simple`/`Restricted` narrow vest behavior stops at empty-slot add; occupied vest replacement is refused
- looted weapon does not trigger patrol reload maintenance with spawned magazines
- after a looted primary reloads, follower death/raid-end cleanup does not leave its ejected original magazine in the teammate's persisted `Simple`/`Restricted` kit
- loose weapon pickup into `FirstPrimaryWeapon` registers as the combat primary, and a pending weapon-taken callback cannot fault after that follower dies
- rejected swap leaves current follower gear untouched
- accepted swap updates tracking and does not duplicate or orphan old gear
- rig magazine space remains stable
