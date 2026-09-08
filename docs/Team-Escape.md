# Team Escape

Date: 2026-05-15

## Scope

This document tracks the player-death squad escape system.

The feature exists for the specific case where the player dies before raid end. In that case, surviving squadmates can independently escape the raid and keep the post-raid systems usable:

- escaped squadmates are marked alive in their teammate profile
- lost squadmates stay dead for roster/spawn logic
- player-given tracked follower loot can still be returned if an escaped squadmate has room after higher-priority death gear
- recoverable player gear can be carried out by escaped squadmates and returned by mail
- in `Immersive` and `Realistic` / internal `Extreme`, fallen teammate gear can also be carried out and returned by mail
- recovered death gear is not kept in escaped teammate inventories

This is separate from the normal player-extract flow. If the player extracts alive, follower loot and follower gear continue to use the normal raid-end behavior.

Related docs:

- `docs/Loadout-Management.md`
- `docs/My-Squad-Screen.md`

## Trigger

The player-death outcome system runs when the player boss dies and `BossPlayers.KillPlayerBoss(...)` tears down the active boss/follower relationship.

The `Team Escape` raid setting controls whether surviving teammates roll for escape after player death. It is enabled by default. If disabled, player death skips survivor escape rolls, tracked follower loot recovery, and death-gear recovery. Live and already-dead teammates are still posted as lost outcomes so raid stats, roster death state, and `Immersive` / `Realistic` Default gear loss are persisted.

Authoritative client files:

- `client/Modules/FollowerDeathEscapeResolver.cs`
- `client/Modules/FollowerDeathEscapeResolver.RouteThreat.cs`
- `client/Modules/FollowerDeathEscapeResolver.GearSnapshot.cs`
- `client/Modules/FollowerDeathEscapeResolver.GearRecovery.cs`
- `client/Modules/BossPlayers.cs`
- `client/Patches/PlayerPatch.cs`
- `client/Modules/InteractableObjects.cs`

Authoritative server files:

- `server/Callbacks/FriendlyPostRaidCallbacks.cs`
- `server/Routers/Static/FriendlyPostRaidRouter.cs`
- `server/Services/FriendlyTeammateService.cs`
- `server/Services/FriendlyPostRaidService.cs`
- `server/Services/FriendlyServerSettingsService.cs`

Routes:

- `POST /singleplayer/pitfireteam/teammate/raid-outcomes`
- `POST /singleplayer/pitfireteam/teammate/death-escape` legacy compatibility alias
- `GET /singleplayer/pitfireteam/lostondeath`
- `POST /singleplayer/returnitems`

## Client Flow

The resolver is split into focused partials:

- `FollowerDeathEscapeResolver.cs` owns orchestration: capture shared context, ask the server to roll live follower escape outcomes, attach fallen outcomes, run recovery, and post final outcomes.
- `FollowerDeathEscapeResolver.RouteThreat.cs` owns route corridor filtering and boss/follower threat multipliers.
- `FollowerDeathEscapeResolver.GearSnapshot.cs` owns fallen-squadmate snapshots, player-death equipment snapshots, tracked-loot filtering, and SPT `lostondeath` loading.
- `FollowerDeathEscapeResolver.GearRecovery.cs` owns carrier-space simulation, recovered gear mail-return, and escaped teammate equipment serialization.

Return mail is centralized through `InteractableObjects.SendReturnItems(...)`. Normal stored follower loot and death-escape recovered gear both use the same `/singleplayer/returnitems` payload path.

When pitFireTeam return mail sends an item that is also insured, the server removes that item id from insurance tracking. This mirrors SPT's BTR delivery behavior and prevents the same item from later being returned by both teammate mail and insurance. The cleanup covers:

- active `PmcData.InsuredItems` entries before SPT creates the insurance package
- already-scheduled `InsuranceList` package item trees if the package already exists

## Escape Roll

Each squadmate rolls independently. With three followers, any result is possible: three escape, two escape, one escapes, or none escape.

Only followers alive at the moment the player dies can escape. Followers who already died before player death are included as lost outcomes, but they do not roll.

Current probability inputs:

- extract distance from the player death position
- surviving squadmate count
- follower health ratio
- follower equipment power
- average enemy equipment power in the current fight at player death
- average enemy equipment power along the route to extract
- secure-container meds

The roll is two-stage when a current fight is detected: first estimate whether the follower survives/disengages from the active fight, then apply the normal route escape estimate. The final result is clamped between the configured minimum and maximum chance in code.

## Raid Stats And Progress

Every persisted teammate raid outcome updates the teammate profile counters that EFT derives raid stats from:

- `Sessions/Pmc` is incremented for every posted outcome
- `ExitStatus/Survived/Pmc` is incremented when the teammate escapes
- `Deaths` is incremented when the teammate is lost

This keeps derived profile stats such as survival rate and kill/death ratio grounded in the saved teammate profile. Kills, raid-earned XP, raid seconds, and common-skill progress are persisted separately through `/client/game/bot/followerprogress`.

Normal player extraction posts living active squadmates as escaped outcomes. Player-death Team Escape posts the resolved escaped/lost outcomes after the server rolls escape chances. If Team Escape is disabled, live squadmates do not roll or recover gear, but they are still posted as lost outcomes so the raid session does not vanish from their stats.

The Team Escape resolver also posts squadmate progress itself before sending escape outcomes, while live follower state is still available. The later boss cleanup path may attempt the same save, but client-side per-raid duplicate protection prevents double-counting the same teammate profile.

## Extract And Distance

The resolver chooses an available extract snapshot and measures direct distance from the player death position.

The `Team Escape: Use Any Extraction Point` raid setting controls the extract pool:

- enabled by default: route selection can use any normal map extraction point that is present and usable
- disabled: route selection is restricted to extraction points assigned to the player by the raid profile/spawn entry point

This intentionally uses a simple route assumption. It does not do detailed enemy-path simulation or full map navigation. The goal is a stable post-death survival model, not a second raid simulation.

Farther extract means lower chance.

## Route Threat

Enemy threat is calculated from living enemies between the squad/player death position and the chosen extract.

The route check uses a corridor around the straight line to extract. Enemies outside that corridor do not affect the roll.

Threat uses `Player.AIData.PowerOfEquipment` and applies role multipliers for bosses and boss followers. Some roles are ignored or reduced because they are not useful for this escape model.

Ignored roles:

- `bossZryachiy`
- `followerZryachiy`
- `bossTagillaAgro`
- `bossKillaAgro`
- test boss/follower roles

High-threat examples:

- `followerBirdEye`
- `bossKnight`
- `followerBigPipe`
- `bossTagilla`
- `bossKilla`

Lower-threat examples:

- `bossBully`
- `bossPartisan`

Unknown roles fall back by name:

- role name starting with `boss` gets a boss multiplier
- role name starting with `follower` gets a follower multiplier

## Current Fight Threat

Current-fight threat is calculated separately from route threat so enemies being fought at player death still matter even when the chosen extract is away from the fight.

The fight pass considers:

- enemies in the boss/group enemy list
- enemies in follower goal/enemy memory
- enemies whose current goal target is the player or a squadmate
- only living hostile AI near the player death position or escaping follower

This uses the same equipment-power and boss/follower role multipliers as route threat. If no current fight enemies are found, this stage has no penalty.
- everything else uses raw equipment power

## Fallen Squadmate Snapshot

When a squadmate dies, the client records a fallen-gear snapshot.

The snapshot has two separate uses:

- recoverable top-level gear candidates are used only if the player later dies within the fallen-teammate pickup radius, currently `50m`
- the full death-time equipment state is sent with the fallen teammate's lost outcome so `Restricted` + `Field Upkeep` can persist damage/consumable state without reading gear after corpse looting

The snapshot exists because by the time player-death escape resolution runs, normal raid cleanup may already be invalidating live bot state.

The snapshot also records identity data so the server receives a lost outcome for a squadmate who died before the player. That lost outcome is what lets `Immersive` and `Realistic` strip the dead teammate's saved `Default` gear, and lets `Restricted` + `Field Upkeep` save the fallen teammate's death-time `Default` state.

## Gear Recovery

Gear recovery happens after escape rolls, using escaped squadmates as possible carriers.

The carrier simulation answers one question:

Can any escaped squadmate near this gear fit it in an empty equipment slot or container?

If yes, the item is mailed to the player. The recovered item is removed from the simulated carrier equipment before escaped teammate state is sent to the server.

Current pickup radius:

- `70m` from carrier to fallen/death gear position

Current recovery priority:

Backpack shells have a special capacity-first pre-pass before the ordered recovery pass below. This lets an escaped squadmate use a recovered backpack's empty grids as extra carrier space for later gear. Backpack contents are still treated as separate low-priority candidates; recovering a backpack shell does not automatically recover everything inside it.

1. first primary weapon
2. armor or armored tactical vest
3. helmet
4. second primary weapon
5. holster weapon
6. tactical vest
7. tactical vest contents, only if the vest was not recovered
8. backpack
9. pocket contents
10. backpack contents
11. remaining recoverable gear
12. tracked follower loot the player gave to or ordered followers to pick up

Armor and tactical vest recovery is container-aware:

- if armor is recovered, attached plates and armor parts ride with it
- if a tactical vest is recovered, its non-tracked grid contents ride with it
- tactical vest contents are only tried as separate low-priority items if the vest itself was not recovered
- recovered container-tree descendants are tracked defensively so vest contents cannot be mailed both inside the vest and as loose duplicates
- tracked follower-loot items are stripped from recovered vest snapshots so they stay in the final tracked-loot recovery pass

The simulation can use empty live slots on escaped teammates:

- second primary weapon slot
- first primary weapon slot
- holster
- armor vest
- tactical vest
- headwear
- earpiece
- face cover
- eyewear
- backpack

Carrier limits:

- each escaped teammate can carry up to their EFT walking-drain threshold
- this threshold uses the same client formula as player weight limits:
  `WalkOverweightLimits.x * Strength/health/stim relative carry modifiers + health/stim absolute carry modifier`
- current follower gear counts toward that per-carrier threshold
- the follower's worn backpack shell does not count toward carrier start weight; backpack contents still count
- secure containers are excluded from carrier start weight in every mode, because they are protected/special-purpose containers rather than general squad recovery space
- recovered gear uses EFT `Item.TotalWeight`, so armor plates, attachments, and contained vest items count with their parent item
- recovered backpack shells count as `0kg` carry load while still providing container space; recovered backpack contents still count as normal item weight
- each escaped teammate can recover up to two backpack items if they started without a backpack
- each escaped teammate can recover one additional backpack item if they already had a backpack
- the backpack cap is checked separately from grid/slot availability, and recovered backpacks can be carried externally instead of requiring a normal equipment slot
- only actual backpack items count against the backpack cap; loose items found inside a backpack do not consume backpack carry slots

The simulation can also use available container space in:

- backpack
- tactical vest
- pockets

Secure containers are not used as available recovery space in any mode.

Backpacks keep the previous capacity-first behavior. Player and fallen-teammate backpacks can be recovered first as capacity. Their grid contents are split into separate lower-priority candidates, so the backpack shell can increase available carry space without automatically dragging all contents with it. If a recovered backpack cannot fit in a normal slot or grid, it can still count as an externally carried backpack within the per-carrier backpack limit, and its empty grids become available for later recovery checks.

## Player Gear

Player gear recovery applies in every loadout-management mode. An escaped squadmate must be within the recovery radius of the player's death position to carry captured player death gear. Fallen teammate gear also keeps its separate nearby-death snapshot rule, so already-dead squadmates must have died close enough to the player death position to be recoverable.

Pocket recovery follows SPT's `lostondeath` `PocketItems` setting. The permanent `Pockets` container is never returned, but its contents are recoverable when pocket items would be lost on death.

- `Restricted`
- `Immersive`
- `Realistic` / internal `Extreme`

The client asks the server for SPT `lostondeath` equipment-slot rules. Player equipment slots are only considered recoverable if SPT would remove that slot on death.

This prevents the escape system from mailing items the player was not going to lose anyway.

The server also checks for SVM / Server Value Modifier death-protection behavior. If SVM is configured so a killed raid is converted into a gear-preserving result such as `Survived`, `Run Through`, or its ignore-raid sentinel, the response marks player gear as protected and the client skips player gear recovery entirely. This covers SVM Softcore-style setups that save gear by changing the final raid result instead of changing SPT `lostondeath` equipment flags.

Always excluded from death-escape recovery:

- secure container and its contents
- dogtag slot
- armband
- scabbard / knife slot
- special slots

## Teammate Gear

Fallen teammate gear recovery only applies in:

- `Immersive`
- `Realistic` / internal `Extreme`

In `Restricted`, teammate gear is protected and is not recovered from fallen bodies.

In `Restricted` with `Field Upkeep` enabled, fallen teammate gear is still protected from player extraction and is still not returned as recovery mail. The server instead saves the fallen teammate's death-time `Default` equipment state, after removing tracked player-given loot and gear ids owned by other teammates.

If teammate gear is recovered, it is mailed to the player. It is not saved onto escaped teammate profiles.

If a teammate died before the player, that teammate should still receive a lost outcome. In `Immersive` and `Realistic`, the server strips that teammate's saved `Default` equipment according to the loadout-management death-loss rules.

Death stripping keeps permanent teammate identity/layout slots. Pockets are preserved only as an empty structural container so special slots remain anchored; normal pocket contents are still lost.

## Tracked Follower Loot

Tracked follower loot is different from teammate gear.

Tracked loot means items the follower picked up for the player through pitFireTeam's follower-loot system.

When the player dies:

- if no squadmate escapes, tracked follower loot is not returned
- tracked follower loot is removed from escaped carrier snapshots before weight/space is calculated, so it cannot block player gear
- escaped squadmates first try to recover player gear, then eligible fallen teammate gear, then tracked follower loot
- tracked follower loot is returned only if an escaped squadmate can still carry it under the same slot, backpack, distance, and weight rules
- tracked follower loot can be returned even if the original carrier did not escape, provided an escaped squadmate is close enough and has capacity

This behavior only applies to the player-death escape situation. If the player extracts alive, a follower dying with loot before player extraction does not count as escaped loot.

## Escaped Teammate State

Escaped teammates are marked alive by server persistence.

In `Immersive` and `Realistic`, escaped teammates using `Default` can save their live raid equipment state back to their saved `Default` loadout. This preserves their own durability, ammo, and consumed-med state.

Tracked follower loot is removed from the escaped teammate snapshot before saving.

Recovered player or fallen-teammate death gear is also removed from the escaped teammate snapshot before saving, because recovered death gear is returned by mail.

Protected gear ids owned by other teammates are also removed from escaped teammate snapshots before saving. This prevents a player from looting a fallen teammate, handing those items to a survivor, and having the survivor permanently save another teammate's protected gear.

Secure-container persistence follows the loadout-management mode:

- non-`Realistic` modes strip the generated secure-container tree before saving the escaped or stripped `Default` state
- `Realistic` / internal `Extreme` keeps the editable secure-container tree

Secure-container items are excluded from death-escape mail recovery and are never used as carrier space.

## Notifications

Death-escape outcomes are posted to the server with `Notify = true` by default.

The server builds a summary message for escaped and lost squadmates. The message is sent through the post-raid delivery/notification path instead of relying on an in-raid notification screen.

The message text must use the language system. Hardcoded English should not be added in the client for player-facing escape outcome messages.

## Roster Refresh

Death escape can mutate teammate profiles after raid end:

- escaped teammates may be marked alive
- lost teammates may stay dead
- `Immersive` / `Realistic` dead teammates may have their saved gear stripped
- escaped teammates may persist their own updated `Default` equipment state

The client marks the My Squad roster dirty after posting death-escape outcomes. The next My Squad open should rebuild roster portraits from the updated teammate profiles.

## Current Gaps

The recovery system is intentionally simple and does not simulate:

- exact enemy patrol paths
- line-of-sight fights during escape
- detailed stamina/weight travel time
- per-item pickup animation timing
- exact corpse looting permissions beyond the recovery slot filters

The implementation should stay conservative. If more realism is added later, prefer small inputs that are already available at player-death time rather than long-running post-death AI simulation.
