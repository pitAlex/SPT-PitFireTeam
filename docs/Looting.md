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
- phase 3 gear-swap design constraints

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
- registers looted weapon trees through `RegisterLootedWeaponTree(...)`
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
- command timeout is 75 seconds

Execution:

- moves to the corpse
- starts a search phase before moving items
- says `EPhraseTrigger.LootGeneric` after the simulated search, when the first real non-dogtag loot move is queued
- waits a short beat after `LootGeneric` before executing that first move, so pickup confirmation does not run into `Ready`
- says `EPhraseTrigger.LootNothing` if no eligible non-dogtag item can be moved
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
- the corpse's worn backpack, armor, armored rig, and tactical vest are not taken as whole equipment in the current phase
- backpack contents are checked item by item
- pocket and vest contents skip magazines entirely so follower reload space is not disturbed
- backpack and container magazines can still be looted if filters, price, and fit pass
- armor plates are ignored, including loose cargo plates and installed plates inside armor or plate-carrier trees
- weapons are priced and moved as whole weapon trees; they are not stripped into parts
- weapon moves first try empty compatible weapon slots, such as second primary or holster, then fall back to backpack/pockets
- non-weapon successful moves target only backpack and pockets, never the follower's tactical vest

## Container Looting

Input:

- `EPhraseTrigger.LootContainer`

Current assignment:

- requires `InteractableObjects.GetCurLootContainerTarget()`
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
- says `EPhraseTrigger.LootGeneric` after the simulated search, when the first real loot move is queued
- waits a short beat after `LootGeneric` before executing that first move, so pickup confirmation does not run into `Ready`
- says `EPhraseTrigger.LootNothing` if no eligible item can be moved
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

`Allow Gear Swapping` defaults off. It is the explicit phase 3 gate for gear equip/swap behavior and is only treated as active when loadout management is `Immersive` or `Realistic`.

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

Successful squadmate moves call `InteractableObjects.StoreItem(...)`.

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

## Phase 3 Gear Equip And Swap Contract

Gear swapping is partially live. This section records the implemented first slice and the remaining constraints for later swap work.

The first phase 3 implementation starts with easy weapon opportunities. Primary weapon replacement is intentionally deferred because vanilla bot weapon state is cached beyond the physical inventory slots. Tactical-vest replacement is still planned, but not active yet.

General rules:

- expose gear equip/swap through `Allow Gear Swapping`, separate from the existing `Pickup Gear` category filter
- only run gear equip/swap behavior in `Immersive` or `Realistic` loadout management
- keep `Simple` and `Restricted` loadout management on carry-only looting behavior
- add easy gear equip as an explicit planner before the current carry-space planner
- keep destructive swaps disabled; the narrow tactical-vest upgrade path below is still a design target, not active behavior
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

- if `FirstPrimaryWeapon` is empty and at least one compatible spare magazine can be placed in the follower's tactical vest, a valid long gun may be equipped into first primary
- compatible spare magazines must come from the same loot source and must physically fit in the tactical vest grid as operational magazines, not cargo
- a compatible spare magazine must be loaded to count as operational support
- when a compatible loaded spare magazine fits, the accepted weapon equip queues that magazine as the next move into the follower's tactical vest
- magazine fit must use the actual magazine shape, not just total cell count; two-cell, three-cell vertical, and two-by-two magazines have different practical vest requirements
- oversized compatible spare magazines that cannot fit in the tactical vest do not count as operational spares
- if `FirstPrimaryWeapon` is empty and no compatible spare magazine fits in the tactical vest, the weapon may still become primary only when its installed magazine is full
- if `FirstPrimaryWeapon` is empty, the installed magazine is not full, and no compatible spare magazine fits in the tactical vest, do not make the weapon the fighting primary
- in that no-vest-mag-space case, use empty `SecondPrimaryWeapon` as support/cargo if available
- if no empty secondary is available, fall back to ordinary cargo pickup for the weapon tree only
- if `FirstPrimaryWeapon` is occupied and `SecondPrimaryWeapon` is empty, a valid long gun may still be equipped into second primary as cargo/support
- if `Holster` is empty, a valid pistol may be equipped there
- if the matching slot is occupied, do not replace it in the empty-slot phase
- still register the moved weapon tree as looted so patrol reload maintenance does not feed spawned magazines into cargo/support weapons
- after equip, refresh the weapon selector cache and verify the item is in the intended equipment slot

Easy weapon equip should still respect price filters, category filters, whole-tree pricing, and found-space rules for any extra magazines or ammunition.

### Planned Narrow Tactical-Vest Upgrade

Tactical-vest replacement is an early gear-swap candidate, but only under strict preflight because it touches operational magazine space and, for plate carriers, protection.

This path is not active yet.

Eligible cases:

- current tactical vest has no plate-carrier capability, the found vest has plate-carrier capability, the found vest can end the transaction with usable protection, and the follower is not wearing separate armor
- current tactical vest is a plate carrier, and the found plate carrier is meaningfully better

Plate-carrier comparison rules:

- compare the found vest as a whole equipment tree, including installed plates
- if current plates are compatible with the found vest and the found plates are poor, prefer moving the current plates into the found vest
- if plates are not compatible, require the found protection level to be higher and the found plates to have enough remaining hit points to justify the swap
- do not take plates as standalone loot outside the accepted vest-upgrade transaction

Vest-upgrade transaction rules:

- preflight current vest contents and magazine positions before changing anything
- if the found vest is smaller, all required current vest contents must fit in the found vest before it can be equipped
- preserve operational magazines and compatible contents wherever possible
- do not use the tactical vest as general cargo during the swap; its purpose is operational space
- try to move the old vest tree into the follower's backpack if there is room
- if the old vest cannot fit in the backpack, it may be thrown down only after the new vest, contents, and plate plan are confirmed valid
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

Mag migration should not be part of the first phase 3 implementation.

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
- eligible non-dogtag items -> search sound, wait, `LootGeneric`, short beat, move items, `Ready`
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

Filtered body/container rules:

- dogtag is attempted first on non-teammate USEC/BEAR bodies
- dogtag-only body looting still says `LootNothing`
- teammate dogtags are not treated as filtered dogtag loot
- backpack magazines can be looted if eligible
- pocket and vest magazines are skipped
- armor plates are ignored
- tactical vest is not used as follower carry space, except operational magazine placement during an accepted weapon equip or vest upgrade
- price minimum and maximum apply to whole item trees
- category filters apply before price

Phase 3 swap tests:

- missing-primary weapon equip requires either one compatible spare magazine that physically fits in the tactical vest or a full installed magazine
- large compatible spare magazines that do not fit the vest grid do not count as operational spares
- no-vest-mag-space weapon pickup falls back to empty secondary or ordinary cargo
- narrow vest upgrade remains deferred
- looted weapon does not trigger patrol reload maintenance with spawned magazines
- rejected swap leaves current follower gear untouched
- accepted swap updates tracking and does not duplicate or orphan old gear
- rig magazine space remains stable
