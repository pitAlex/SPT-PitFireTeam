# Command System Notes

Last updated: 2026-07-30

## Scope

This document summarizes boss-issued follower commands as implemented in the client runtime.

Detailed looting behavior, filtered-loot rules, and gear-swap design constraints are tracked in `docs/Looting.md`.

Authoritative files:

- `client/Components/AIBossPlayer.cs` - boss-side phrase/gesture router and command producers.
- `client/Components/BotFollowerPlayer.cs` - per-follower command state.
- `client/BigBrain/FollowerRequestLayer.cs` - out-of-combat command layer gate.
- `client/BigBrain/Actions/GestureCommandAction.cs` - out-of-combat command execution.
- `client/BigBrain/FollowerCombatLogicBase.cs` - core combat objective/command handoff.
- `client/BigBrain/FollowerCombatDefault.cs` - rifleman/default combat handling.
- `client/BigBrain/FollowerCombatSniper.cs` - marksman combat handling.
- `client/BigBrain/FollowerCombatRegroupObjective.cs` - core combat regroup.
- `client/BigBrain/FollowerCombatSuppressionObjective.cs` - core ordered suppression.
- `client/BigBrain/FollowerCombatNeedSniperObjective.cs` - core marksman support order.
- `addon/SAINFollowerCombatLayer.cs` - optional SAIN addon regroup/hold-override handling.
- `client/Patches/BotReceiverPhraseOverridePatch.cs` and `client/Patches/BotReceiverGestureOverridePatch.cs` - vanilla receiver suppression for mod-owned commands.
- `client/Patches/GestureMenuPatch.cs` - command menu injection/localization/filtering.

## Runtime Ownership

Command input starts in `pitAIBossPlayer.PhraseSaid(...)` or `pitAIBossPlayer.GestusShown(...)`.

Most durable commands are stored on `BotFollowerPlayer` as a single active `FollowerCommandType`. This state is intentionally simple: one command, one optional target, and either a timeout or a combat-objective handoff. New timed commands usually replace any previous active command unless they are the same command type.

There are three execution paths:

1. **Request layer / out of combat**
   - `FollowerRequestLayer` activates only when the follower has an active command and is ready for patrol after combat.
   - It rejects combat-only orders such as `PushEnemy`, `SuppressEnemy`, and `NeedSniper`.
   - It clears most request-layer commands when the follower acquires a known enemy.
   - `GestureCommandAction` executes the actual hold, move, regroup, loot, and door behavior.

2. **Core combat**
   - `FollowerCombatLogicBase` owns objective selection and command handoff.
   - `RegroupNearBoss`, `SuppressEnemy`, and `NeedSniper` are consumed into combat objectives.
   - `PushEnemy` is consumed into the ordered-push objective.
   - Combat gesture commands (`CombatComeToBossCover`, `CombatMoveToPointTactical`) break hold commitments and ordinary combat movement, while protected movement such as heal relocation is allowed to finish.

3. **SAIN addon combat**
   - Only active when SAIN plugin and the pitFireTeam SAIN addon are both present.
   - `RegroupNearBoss` is seen by `SAINFollowerCombatLayer` and translated into SAIN `ESquadDecision.Regroup`.
   - Temporary `HoldPosition` combat aggression override is also treated as regroup/protection intent by the SAIN addon.

## Command State

`FollowerCommandType` currently contains:

| Command | Stored target | Timeout behavior | Primary consumer |
| --- | --- | --- | --- |
| `HoldPosition` | none | infinite | `GestureCommandAction` |
| `MoveToPoint` | sampled world/nav point | infinite | `GestureCommandAction` |
| `ComeCloser` | boss position snapshot owned by action | timed unless resuming a hold | `GestureCommandAction` |
| `RegroupNearBoss` | none | timed | `GestureCommandAction`, core combat regroup, or SAIN addon |
| `TakeLootItem` | reserved current loot item, not `_commandTarget` | timed | `GestureCommandAction` |
| `OpenDoor` | reserved current door, not `_commandTarget` | timed | `GestureCommandAction` |
| `PushEnemy` | current combat enemy | consumed into objective | core ordered-push objective |
| `SuppressEnemy` | current combat enemy | timed | core rifleman suppression objective |
| `NeedSniper` | current/support enemy | timed | core marksman support objective |
| `CombatComeToBossCover` | none | timed | core combat gesture handoff |
| `CombatMoveToPointTactical` | sampled world/nav point | timed | core combat gesture handoff |

`TryGetActiveCommand(...)` is not a pure read. It clears the current command when:

- the follower is actively using first aid or surgery
- the current BigBrain decision is `heal`
- the command timeout has expired

`TryPeekActiveCommand(...)` is a pure peek and does not clear timed-out or healing-interrupted state.

## Phrases And Gestures

### Team Status

Input:

- custom phrase `CustomPhrases.TeamStatus`

Behavior:

- Debounced in `AIBossPlayer`.
- Calls `PingTeamates.Instance.Ping(this)`.
- Highlights each living teammate with an outline for the configured Status Report display time when the highlight option is enabled.
- The outline color is configurable with a `#RRGGBB` value and defaults to green (`#00FF00`).
- Name, distance, combat status, HP, and tactic (`MD`) can each be toggled under `My Squad > Settings > Base Settings`.
- Disabling every text field while leaving the highlight enabled produces a highlight-only Status Report.
- Nearby active followers without enemies play `FriendlyGesture`.
- Does not create `FollowerCommandType` state.

### Contact / Over There

Input:

- `EPhraseTrigger.OnRepeatedContact`
- custom gesture `CustomGestures.OverThere`

Behavior:

- Calls `ProcessContactCommand(...)`.
- Builds candidate enemies from interactable seen-enemy cache, boss visible enemies, SAIN contact fallback, and directed visible candidates.
- Registers valid enemies into follower memory and can promote a prioritized enemy as `GoalEnemy`.
- Applies a short look override toward the boss's look direction for followers without their own visible goal.
- Custom `OverThere` also forwards an `OnRepeatedContact` phrase event to visible followers so they can play normal voice/receiver feedback.
- This is a combat cue, not a `FollowerCommandType`.
- Contact injection clears most active request-layer commands after the follower now has an enemy, except `PushEnemy` and `SuppressEnemy`.
- If the contact enemy and player can see each other, Contact acts as a quick Need Help cue for nearby followers: followers within `50m` of the player cancel ordered push and prioritize that enemy through the boss-under-attack/help path.

### Directional Look

Input:

- `EPhraseTrigger.InTheFront`
- `EPhraseTrigger.LeftFlank`
- `EPhraseTrigger.RightFlank`
- `EPhraseTrigger.OnSix`

Behavior:

- Applies a short command look override to active followers within phrase range.
- Direction is relative to the boss planar look direction.
- Does not create `FollowerCommandType` state.

### Attention / Look

Input:

- `EPhraseTrigger.Look`

Behavior:

- Debounced in `AIBossPlayer`.
- Clears active command state and temporary combat aggression override.
- Temporarily suppresses enemy enforcement.
- Clears current goal enemy and known enemy memory.
- Soft-resets follower recovery and forces upright pose.
- If SAIN addon combat is active, asks the addon bridge to force-release follower combat state.
- Followers answer `Roger`.
- Does not create new command state.

### Follow Me / Cooperation

Input:

- `EPhraseTrigger.FollowMe`
- `EPhraseTrigger.Cooperation`

Behavior:

- Calls `ClearFollowerCommands()`.
- Clears active `FollowerCommandType` state on all active followers.
- Disables patrol-radius mode by setting `CanPatrol` false.
- Does not otherwise change combat objective state directly.
- For a same-side non-follower, the receiver path instead attempts in-raid recruitment.
- When tiered PMC recruitment rejects that bot because of the level-based acceptance decision, the refusal is remembered for the rest of the raid. Repeating `Follow Me` or `Cooperation` cannot reroll that bot's decision.

### On Your Own

Input:

- `EPhraseTrigger.OnYourOwn`

State:

- Does not create `FollowerCommandType` state.
- Sets `BotFollowerPlayer.CanPatrol` true for selected out-of-combat followers.
- In combat, sets combat-independent mode instead of changing out-of-combat patrol state.

Targeting:

- Broadcast to all active followers every time.

Behavior:

- Clears current request command state and temporary combat aggression override.
- Enables patrol-radius intent in `FollowAction`.
- `FollowMe` / `Cooperation` clears this mode.
- Combat use does not create a request command; it only asks the current combat layer to stop anchoring behavior around the boss.

### Cover Me

Input:

- `EPhraseTrigger.CoverMe`

Behavior:

- Broadcast to all active followers.
- Outside combat, disables patrol-radius mode by setting `CanPatrol` false.
- In combat, clears ordered-push objective pressure and disables combat-independent mode.
- Does not clear active request command state.
- Does not set boss protection, combat aggression, regroup, or a new combat objective state.

Execution:

- `FollowAction` checks `followerData.CanPatrol` every update.
- When disabled, the action uses normal close follow/settle behavior.
- When enabled, `CanPatrol` is treated as patrol intent, not immediate patrol ownership:
  - before patrol arms, boss/player movement resets the patrol runtime gate and the follower behaves like normal follow
  - if the follower is outside normal follow range, the follower catches up with normal follow behavior before patrol can arm
  - after the boss/player has been still for about 5 seconds and the follower is in range, patrol runtime arms
  - once armed, the follower patrols around the current area using `patrolRadius`
  - while armed, small boss/player movement inside the patrol anchor radius does not cancel the current patrol point
  - if the boss/player moves about 20m from the patrol anchor, patrol runtime reanchors and the follower returns to normal follow until the stillness/range checks pass again
  - choose random reachable nav points inside the configured `patrolRadius`
  - avoid points too close to the boss or other followers
  - walk slowly between patrol points and pause 6-10 seconds at each point
  - run peaceful look/actions while waiting when available

## Out-Of-Combat Commands

### Hold Gesture

Input:

- `EInteraction.HoldGesture`

Command state:

- `SetHoldPosition(float.PositiveInfinity, crouch: true)`

Targeting:

- If boss is looking at a valid follower within hold distance, only that follower is commanded.
- Otherwise broadcasts to nearby visible active followers without enemies.

Execution:

- `GestureCommandAction.HandleHoldPosition()`
- Stops movement.
- Forces crouch when configured by command state.
- Applies command look override if present; otherwise random look-around.
- Persistent until replaced, cleared, or interrupted by command management.

Picked-up follower behavior:

- Recruited/picked-up followers roll command acceptance before taking the hold.
- Higher-level picked-up followers are more likely to refuse with `Negative` because they do not fully accept the player as their boss.
- Lower-level picked-up followers are more likely to accept, with a small stable personality bias per follower.
- The same personality bias also feeds autonomous combat protection: less loyal pickups tolerate more distance from the player and are less likely to break their own fight to protect the boss.

### Stop Phrase

Input:

- `EPhraseTrigger.Stop`

Command state:

- `SetHoldPosition(float.PositiveInfinity, crouch: false)`

Targeting:

- Similar focused-then-broadcast routing to hold gesture, but phrase range is larger and uses phrase reaction checks.

Execution:

- Same `HoldPosition` request-layer action as hold gesture.
- Does not force crouch.

Picked-up follower behavior:

- Uses the same level/personality acceptance roll as the hold gesture.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers, so pitFireTeam owns the command.

### Come With Me Gesture

Input:

- `EInteraction.ComeWithMeGesture`

Out-of-combat command state:

- `SetComeCloser(10f)`

Targeting:

- Focused only. Boss must be looking at one valid follower within max distance and gesture visibility gates.

Execution:

- `GestureCommandAction.HandleComeCloser()`
- Snapshots boss position and boss pose at command start.
- Moves to within about 1.5m of the snapshotted boss position.
- If issued while the follower was in `HoldPosition`, `CompleteComeCloser()` restores `HoldPosition` after arrival.
- Otherwise it clears the command after a short arrival pause.

Combat variant:

- If the selected follower has an active combat enemy, the gesture stores `CombatComeToBossCover` instead of `ComeCloser`.

### There Gesture

Input:

- `EInteraction.ThereGesture`

Out-of-combat command state:

- `SetMoveToPoint(commandTarget, 0f)`

Targeting:

- Chooses the closest active follower within gesture command distance that can react to the boss gesture.
- Samples the boss interaction ray or planar look direction to a nav point.
- Uses `pitFireTeam.goToDistance` for normal out-of-combat target range.

Execution:

- `GestureCommandAction.HandleMoveToPoint(target)`
- Walks to the target.
- Validates path periodically.
- On arrival, stops and performs a short look-around before clearing the command.
- If a command look override exists, it looks at that override instead of random scanning.

Combat variant:

- If the selected follower has an active combat enemy, the gesture stores `CombatMoveToPointTactical`.
- Combat target range is hard-limited to 30m from the boss and does not use `goToDistance`.

### Go Forward Phrase Outside Combat

Input:

- `EPhraseTrigger.GoForward`

Out-of-combat command state:

- Falls back to `SetMoveToPoint(commandTarget, 0f)` when the follower does not have an active combat enemy.

Targeting:

- Optional focused follower if boss is looking at one within phrase range.
- Otherwise iterates active followers.
- Uses the same point sampling as normal `There`.

Combat variant:

- Becomes `PushEnemy` when the follower has active combat enemy state.

### Regroup Phrase Outside Combat

Input:

- `EPhraseTrigger.Regroup`

Command state:

- `SetRegroup(20f)`
- Disables patrol-radius mode by setting `CanPatrol` false.

Targeting:

- Broadcast to active followers.
- Ignored for followers already close enough to boss on the same level, or healing.
- Patrol-radius mode is disabled before this ignore check, so `Regroup` still returns followers to normal follow mode even when no regroup movement command is created.

Execution:

- `GestureCommandAction.HandleRegroupNearBoss()`
- Picks a boss-near regroup target, preferring cover points near boss and falling back to spread destinations.
- Reserves regroup destinations through `CombatEvents` to reduce crowding.
- Runs when far enough, otherwise walks.
- Completes when within close nav distance and same-level tolerance.

Combat variant:

- Core combat consumes `RegroupNearBoss` into `FollowerCombatRegroupObjective`.
- SAIN addon can consume it as `ESquadDecision.Regroup` when SAIN route is enabled.

### Loot Phrases

Loot and pickup selections enter `AIBossPlayer` through the player's `OnPhraseSay` event. Assignment diagnostics record the quick-menu action, live and stored targets, phrase arrival, follower eligibility, reservation, and final command state so a lost order can be located at its exact boundary.

Input:

- `EPhraseTrigger.LootGeneric`
- `EPhraseTrigger.LootWeapon`

Command state:

- `SetTakeLootItem(35f)`

Targeting:

- Requires `InteractableObjects.GetCurLootItem()`.
- Chooses the active follower with the shortest complete NavMesh path to the loot item.
- Ignores followers with enemies or active loot/pickup commands.
- Reserves taker ownership through `InteractableObjects.SetTaker(...)`.

Execution:

- `GestureCommandAction.HandleTakeLootItem()`
- Moves to loot.
- Checks inventory space and executes one pickup transaction.
- For a commanded loose long gun, uses explicit destination order: ready first primary, otherwise empty second primary, otherwise backpack.
- When both shoulder slots are empty, a commanded loose detachable-magazine long gun may adopt only its compatible magazines and loose ammunition already carried in the follower's backpack. Magazine top-off or insertion and reload-safe vest/pocket placement settle before the normal live-readiness destination check; unrelated backpack cargo remains strict.
- If those fallback destinations are unavailable and first primary is empty, a non-dangerously-low inserted magazine permits last-resort first-primary placement; that visible right-shoulder weapon is always registered as the bot's usable primary.
- Releases pickup hand state before registering/selecting a first-primary weapon.
- Stores item through `InteractableObjects.StoreItem(...)` for squadmates.
- Clears command on success/failure/invalid state.

### Body Loot Phrases

Input:

- `EPhraseTrigger.CheckHim`
- `EPhraseTrigger.LootBody`

Command state:

- `SetTakeBodyGear(75f)`

Targeting:

- Requires `InteractableObjects.GetCurBodyLootTarget()`.
- Only saved teammates spawned through the raid squad flow can be assigned to body-loot commands; recruited/picked-up followers are ignored.
- Chooses the active follower with the shortest complete NavMesh path for teammate corpses.
- Chooses the active follower with the shortest complete NavMesh path of 22m or less for non-teammate corpses, ignoring followers with no free backpack/pocket grid space.
- Ignores followers with enemies or active loot/pickup commands.
- Reserves corpse ownership through `InteractableObjects.SetBodyLootTaker(...)`.
- Direct `Check Him` / `Loot Body` orders may revisit a corpse after a previous follower search completed.
- The completed-body marker is reserved for autonomous `Go loot` filtering, so automatic selection skips corpses already searched by a follower.
- A live corpse reservation still blocks duplicate assignment while another follower is approaching or looting it.

Execution:

- `GestureCommandAction.HandleTakeBodyGear()`
- Moves to the corpse.
- Checks whether at least one eligible item can be moved, plays the loot search sound, and waits briefly before moving items. Delay is based on the total grid cells searched from corpse pockets, backpack, and vest containers, with a short bounded cap so it reads as searching without matching full player search time.
- After the search delay, says one pickup-confirmation phrase when the first real non-dogtag loot move is queued, then waits a short beat before executing that move so the pickup confirmation does not run into `Ready`. `EPhraseTrigger.LootWeapon` means the executable plan makes a weapon the follower's combat primary during this search. Every non-primary weapon result, including second-primary support, holster, backpack cargo, future-potential packages, and under-ready left-shoulder holders, uses `EPhraseTrigger.LootGeneric`.
- Plans one live inventory transaction at a time.
- Teammate corpses use the protected recovery path:
    - uses empty compatible equipment slots as cargo space when possible, but does not swap or throw away the follower's current kit
    - tries backpack, rig, and pocket carry containers for the remaining body gear
    - treats backpacks and rigs as whole cargo: if the follower can carry the container, its contents ride with it; if the container cannot be carried, the command does not pull items out of it
    - pockets are not a movable cargo container, so pocket contents are still considered individually
    - in `Simple` and `Restricted`, skips roots that are protected follower equipment. Non-protected containers may carry protected descendants, and post-raid filtering strips those protected descendants before extraction or return delivery
- In `Immersive` and `Realistic`, protected-equipment skipping is not applied because fallen teammate gear is lootable in those modes.
- Non-teammate corpses use filtered looting:
    - tries to take the corpse dogtag first only for non-teammate USEC/BEAR bodies; dogtags bypass the min/max price filter but still require a valid backpack/pocket move
    - dogtag-only body looting still says `EPhraseTrigger.LootNothing`
    - checks backpack contents first, then pockets, then vest contents
    - normal filtered carry looting does not take the corpse's worn backpack as a container shortcut
    - when `Pickup Gear` is enabled, worn armor, armored rigs, tactical rigs, and headwear are priced and moved as whole trees before eligible fallback contents are considered
    - pocket and vest contents skip magazines during normal filtered looting so follower reload space is not disturbed; when `Allow Gear Swapping` is active, compatible loaded magazines from the loot source may move into tactical vest or pockets for an accepted primary, or for a working-primary support equip when `Pickup Weapons` is also enabled
    - loose armor plates remain excluded; an installed plate is a fallback candidate only after its parent armor/rig remains at the source, it passes `Pickup Gear` and price, and current durability is at least 50 percent of maximum
    - normal cargo weapons are priced and moved as whole weapon trees, including attached mods, instead of being stripped part by part; missing-primary acquisition and implemented true swaps ignore min/max price and are controlled by `Allow Gear Swapping`, while optional secondary/holster weapon additions additionally require `Pickup Weapons`
    - with `Allow Gear Swapping` active, an empty-primary long gun equips only when its inserted magazine plus compatible loaded fast-access magazines satisfy the readiness policy
    - if the found detachable-magazine weapon has an empty magazine slot, the most-loaded compatible source magazine is inserted through a real inventory transaction before normal weapon planning resumes; the existing loaded-weapon rules then decide fitting spares and weapon destination from live inventory
    - an under-ready empty-magazine package remains ordinary potential cargo and still requires `Pickup Weapons`, whole-tree price, and backpack fit
    - successful empty-primary weapon equip rebuilds the follower weapon-manager primary info and requests a main-hand switch so combat can use the new weapon
    - with `Pickup Weapons` enabled, a working first primary, and an empty second primary, a usable found long gun becomes a registered vanilla support weapon; only compatible source magazines that fit fast access while preserving reload landing space join it, and this non-primary result uses `EPhraseTrigger.LootGeneric`
    - once first and second primary are occupied, later long guns are ordinary filtered cargo and cannot recruit compatible magazines through the future-primary package bypass; an occupied holster applies the same boundary to pistols
    - other weapons that do not qualify for equipment still try an empty compatible slot, such as holster, then fall back to backpack/pocket space
    - tactical vests are eligible for gear handling only when `Allow Gear Swapping` is active: an empty tactical vest slot may be filled directly in any mode; occupied vest replacement is only allowed in Immersive/Realistic, and only when the found vest is a protection upgrade, the old vest has no non-plate contents, and the old vest can be moved as a whole tree into the backpack first
    - category filters from `Looting Settings` are checked before price for ordinary cargo: `Pickup Food`, `Pickup Meds`, `Pickup Valuables`, `Pickup Weapons`, and `Pickup Gear`; corpse dogtags, missing-primary acquisition, and implemented true swaps bypass these category filters, but optional second-primary/holster weapon additions require `Pickup Weapons`
    - compatible loaded magazines moved as support for an accepted weapon equip bypass the normal loot filters entirely; they must be loaded, safe to take, and able to fit in tactical vest or pockets with the shared reload reserve preserved; overflow stays at the source
    - with `Allow Gear Swapping` active, an equipped detachable-magazine primary may top off empty or partial compatible vest/pocket magazines independently of `Pickup Weapons`; carried loose ammunition is used first, and Immersive/Realistic may use searched-source rounds after carried supply; top-off never unloads or replaces existing cartridges
    - ordinary cargo item price is compared once against `Looting Settings -> Minimum Price` and `Maximum Price`; `0` disables that bound; money ignores these price bounds when `Pickup Valuables` is enabled
    - non-weapon successful moves only target the follower's backpack and pockets, never the follower's rig
- Stores successful cargo moves through `InteractableObjects.StoreItem(...)` for squadmates. Additive equipped gear moves are also stored in `Simple` and `Restricted`; Immersive/Realistic equipped gear can persist as the teammate's kit instead.
- On completion, says `EPhraseTrigger.Ready` when at least one non-dogtag item was moved, `EPhraseTrigger.Negative` when eligible loot existed but no executable move could be built, or `EPhraseTrigger.LootNothing` when no eligible non-dogtag item existed.
- Once searching starts, normal replacement commands are ignored until the loot command completes; combat, timeout, and safety invalidation can still stop the command.
- Clears command on success/failure/invalid state.

### Container Loot Phrase

Input:

- `EPhraseTrigger.LootContainer`

Command state:

- `SetTakeContainerLoot(75f)`

Targeting:

- Requires `InteractableObjects.GetCurLootContainerTarget()`.
- Direct `Loot Container` orders may revisit a container after a previous follower search completed.
- Autonomous `Go loot` selection skips containers marked completed by a follower.
- A live container reservation still blocks duplicate assignment while another follower is approaching or looting it.
- Only saved teammates spawned through the raid squad flow can be assigned to container-loot commands; recruited/picked-up followers are ignored.
- Chooses the active follower with the shortest complete NavMesh path of 22m or less, ignoring followers with no free backpack/pocket grid space.
- Ignores followers with enemies or active loot/pickup commands.
- Reserves container ownership through `InteractableObjects.SetContainerLootTaker(...)`.

Execution:

- `GestureCommandAction.HandleTakeContainerLoot()`
- Moves to the container.
- Opens the container if it is shut.
- Checks whether at least one eligible item can be moved, plays the loot search sound, and waits briefly before moving items. Delay is based on the total grid cells in the container tree, with a short bounded cap so it reads as searching without matching full player search time.
- After the search delay, says one pickup-confirmation phrase when the first real loot move is queued, then waits a short beat before executing that move so the pickup confirmation does not run into `Ready`. `EPhraseTrigger.LootWeapon` means the executable plan makes a weapon the follower's combat primary during this search. Every non-primary weapon result, including second-primary support, holster, backpack cargo, future-potential packages, and under-ready left-shoulder holders, uses `EPhraseTrigger.LootGeneric`.
- Searches container contents through the same filtered-loot planner used for non-teammate bodies.
- Applies `Looting Settings` category filters before price for ordinary cargo: `Pickup Food`, `Pickup Meds`, `Pickup Valuables`, `Pickup Weapons`, and `Pickup Gear`.
- Compares each ordinary cargo candidate item tree against `Looting Settings -> Minimum Price` and `Maximum Price`; `0` disables that bound. Money ignores these price bounds when `Pickup Valuables` is enabled.
- With `Pickup Gear` enabled, helmets, armor, armored rigs, and tactical rigs found in a container are tried as complete cargo trees first. If armor or a rig stays at the source, its eligible contents are reconsidered individually; installed plates additionally require at least 50 percent durability, while loose plates remain excluded.
- Moves non-weapon candidates only into the follower's backpack and pockets, never the follower's rig.
- Normal cargo weapons are priced and moved as whole weapon trees. With `Allow Gear Swapping` active, a long gun can equip into an empty first-primary slot without min/max price or `Pickup Weapons` when its loaded state and compatible fast-access magazines satisfy readiness. A working-primary follower may add a registered second-primary support weapon only when `Pickup Weapons` is enabled and second primary is empty. For an empty magazine slot, the most-loaded compatible body/container magazine is inserted first as a staging transaction, then the same live loaded-weapon planner decides its destination. Once a primary or Pickup-Weapons-authorized support plan is accepted, compatible loaded source magazines bypass normal loot filters only while they fit reload-safe fast access.
- Equipped-primary magazine top-off follows the same body-loot rule: `Allow Gear Swapping`, not `Pickup Weapons`, owns the operation; carried loose ammunition is preferred, searched loose ammunition may be used in Immersive/Realistic, and no loaded cartridge is removed or replaced.
- When a later search supplies compatible ammunition for a tracked secondary or backpack weapon candidate, acquired inserted/source magazines are topped off first, operational magazine placement and readiness are then recalculated, and only the remaining accepted loose rounds enter the normal protected ammo-storage order.
- Successful empty-primary weapon equip rebuilds the follower weapon-manager primary info and requests a main-hand switch so combat can use the new weapon.
- Once first and second primary are occupied, later long guns are ordinary filtered cargo and do not automatically take compatible magazines. An occupied holster applies the same rule to later pistols. With `Pickup Weapons` enabled, other eligible weapons may still use an empty compatible slot before backpack/pocket fallback.
- Tactical vests follow the same narrow gear rule: fill an empty tactical vest slot directly in any mode, or replace an occupied vest only in Immersive/Realistic when the found vest is a protection upgrade, the old vest has no non-plate contents, and the old vest can be moved as a whole tree into the backpack first.
- Missing-primary acquisition and implemented true swaps bypass min/max price and the `Pickup Weapons` category filter. Optional second-primary/holster weapon additions require `Pickup Weapons`; ordinary weapon and wearable cargo fallbacks respect their separate category filters plus price.
- Closes the container on normal completion. Combat, timeout, or safety interruption can leave it open.
- Stores successful cargo moves through `InteractableObjects.StoreItem(...)` for squadmates. Additive equipped gear moves are also stored in `Simple` and `Restricted`; Immersive/Realistic equipped gear can persist as the teammate's kit instead.
- On completion, says `EPhraseTrigger.Ready` when at least one item was moved, `EPhraseTrigger.Negative` when eligible loot existed but no executable move could be built, or `EPhraseTrigger.LootNothing` when no eligible item existed.
- Once searching starts, normal replacement commands are ignored until the loot command completes; combat, timeout, and safety invalidation can still stop the command.
- Clears command on success/failure/invalid state.

### View Backpack Quick Interaction

Input:

- custom `EPhraseTrigger` value `CustomPhrases.ViewBackpack`
- exposed through the lower-left quick interaction panel as `View Backpack`

Targeting:

- Uses `TeammateBackpackInspection.CanShowQuickInteraction(...)`.
- Requires the player to look at an alive spawned squadmate within the close interaction range.
- Filters to `BotFollowerPlayer.IsSquadMate`, so recruited/picked-up allies are not valid backpack targets.
- Requires the target to have a searchable item in the `Backpack` equipment slot.
- Does not overlap with follower healing or active/pending follower loot-pickup work.

Execution:

- `QuickPanelPatch` keeps the custom phrase available and refreshes whether it can be shown.
- `QuickMumbleStartViewBackpackPatch` and `PlayerPatch.PlayPhraseOrGesture` route the phrase to `TeammateBackpackInspection.TryOpenFromQuickInteraction(...)`.
- Opens the target follower's live backpack through `GamePlayerOwner.ShowInventoryScreenLoot(...)`.
- Marks the backpack tree searched/known only for this local inspection session, without permanently examining unknown templates for the player.
- Sets `BotFollowerPlayer.IsBackpackInspectionActive`, which makes follow/patrol logic hold the follower still while the backpack screen is open.
- Closes the inspection if the player dies, the target becomes invalid, the follower starts healing/pickup work, or combat pressure appears (`HasKnownEnemy`, `Memory.HaveEnemy`, or `Memory.IsUnderFire`).

Loot tracking:

- On close, new items placed into the follower backpack are registered through `InteractableObjects.StoreItem(...)` so they behave like handed-over follower loot.
- Previously tracked items removed from the backpack are unregistered through `InteractableObjects.RemoveStoredItem(...)` so post-raid return handling does not duplicate items the player already took back.

### Open Door Phrase

Input:

- `EPhraseTrigger.OpenDoor`

Command state:

- `SetOpenDoor(12f)`

Targeting:

- Requires `InteractableObjects.GetCurDoor()`.
- Locked doors produce a nearby `Negative` response and do not create a command.
- Chooses closest active follower to the door.
- Ignores followers with enemies.
- Reserves opener ownership through `InteractableObjects.SetOpener(...)`.

Execution:

- `GestureCommandAction.HandleOpenDoor()`
- Samples nav point near door, moves there, and calls `BotOwner.DoorOpener.Interact(...)`.
- Clears command when interaction ends, door is already open, path is invalid, timeout hits, or target disappears.

## Core Combat Commands

### Hold Position Phrase In Combat

Input:

- `EPhraseTrigger.HoldPosition`

State:

- Does not create `FollowerCommandType.HoldPosition`.
- Applies `BotFollowerPlayer.SetTemporaryCombatAggressionOverride(0f)`.

Behavior:

- Core combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`.
- Rifleman/default behavior becomes less proactive and more defensive/regroup-oriented.
- Marksman suppresses proactive close-search/auto-search behavior.
- Defensive survival behavior still wins: immediate fire, dogfight, healing, boss protection, and other urgent actions can still run.
- Picked-up followers may refuse this hold with `Negative`; higher-level and more independent recruits are more likely to ignore the order.

SAIN addon:

- `SAINFollowerCombatLayer` treats the temporary override as regroup/protection intent.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers.

### Go Go Go Phrase In Combat

Input:

- `EPhraseTrigger.Gogogo`

State:

- Clears temporary combat aggression override.

Behavior:

- Returns followers to their saved combat aggression/tactic behavior.
- Does not create `FollowerCommandType` state.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers.

### Push Enemy

Input:

- `EPhraseTrigger.GoForward` while follower has an active combat enemy.

Command state:

- `SetPushEnemy(...)`, consumed by combat into a durable ordered-push objective

Picked-up follower behavior:

- Picked-up followers can now accept or refuse the ordered push instead of always refusing it.
- Acceptance is based on a personality-weighted roll using follower level versus player level and follower gear power versus the current enemy's gear power.
- Better gear relative to the enemy increases push acceptance.
- Lower-level picked-up followers are more likely to get scared and refuse; higher-level picked-up followers may refuse because they are cocky or independent.
- Outside direct orders, the same personality model makes picked-up followers less boss-centered than saved squadmates. Low-protection pickups delay autonomous regroup and may ignore boss-under-attack support opportunities.
- `On Your Own` remains accepted; it is the player cutting the recruit loose, not bossing them around.

Core behavior:

- `FollowerRequestLayer` refuses to consume it.
- Core combat consumes it into `FollowerCombatOrderedPushObjective`.
- Rifleman/default latches the current combat enemy as the ordered kill target.
- The objective keeps effective ordered-push pressure active until that target dies or becomes unrecoverable.
- Medical, reload, and immediate survival actions may interrupt the current action, but they do not clear the ordered target. Active or pending medical work blocks new push phases until heal logic starts or the medical work clears.
- Boss-under-attack/help retargets do not cancel the ordered target; only point-blank self-defense may temporarily take over the current action.
- Explicit new boss orders can cancel ordered push. Combat `CoverMe` and `NeedHelp` request ordered-push cancellation before their own support behavior runs.
- Ordered push tries committed firing-position movement first, then falls back to `FollowerCombatPush.EngageEnemy(Ordered)`.
- After reaching an ordered firing-position pressure point, core combat honors the shared arrival hold before selecting another pressure point, so unreachable/marksman-style contacts are fought from the best reached point instead of causing immediate point-reselect churn.
- Push movement is committed as `push.*` and keeps enemy retention refreshed.
- Regroup/suppression/need-sniper objectives can be interrupted by a push order, which activates the ordered-push objective.

Marksman behavior:

- Generic push is not a direct marksman assault.
- Marksman support logic may clear unsupported push/suppress commands or turn the situation into support/reposition behavior.

### Regroup In Combat

Input:

- `EPhraseTrigger.Regroup` while combat regroup context exists.

Command state:

- `SetRegroup(20f)`

Core behavior:

- `FollowerCombatLogicBase` consumes it into `FollowerCombatRegroupObjective`.
- Objective owns movement until complete or replaced.
- Hot contact uses `attackMoving` toward bossward cover or fallback boss destination.
- Cooled contact uses `goToPoint` through `CombatRegroupRunAction`.
- Completion is based on boss nav distance and same-level tolerance.
- Push or suppress orders can end regroup and return to primary/suppression behavior.

SAIN addon:

- `SAINFollowerCombatLayer.TryHandleRegroupCommand(...)` latches the command briefly and returns `ESquadDecision.Regroup`.

### Suppress Enemy

Input:

- `EPhraseTrigger.Suppress`

Command state:

- `SetSuppressEnemy(6f)`

Targeting:

- If the boss is looking at a follower, only that follower receives the order and chooses from its own current enemy or boss-visible contact; the boss look ray is not reused as a launcher target.
- If no follower is focused, eligible followers may suppress together, but the boss skips followers already healing, under immediate fire pressure, actively shooting, dogfighting, or moving/fighting in an emergency.
- Squad suppression allows no more than one grenadier. The selected grenadier is scored by usable hostile target distance, direct launch lane, friendly impact safety, and friendly lane safety.
- Rifleman/default followers use suppress-capable current weapons. Marksman followers only join squad suppression when there is no active Rifleman/default in the squad and the marksman has a loaded automatic second primary.
- Ensures a target by using the follower's current enemy, boss-visible enemies, or, for unfocused launcher selection only, boss order-ray launcher targets within `120m`.

Core behavior:

- `FollowerPmcCombatLogic` marks `SuppressEnemy` consumable.
- `FollowerCombatLogicBase` validates weapon/enemy and activates `FollowerCombatSuppressionObjective`.
- The objective tries dogfight/heal first, then launcher support from the current position or a suppress-from point, then weapon suppression from the current position or a suppress-from point. Marksman fallback suppression can switch to a loaded automatic second primary before planning the weapon burst.
- Suppression can use obstructed known-point suppression when explicitly ordered, subject to shot safety.
- If no launcher or primary support action can be created, the follower answers `Negative`.
- Command is cleared on consume, rejection, completion, missing enemy/target, blocked lane, or weapon rejection.

### Need Sniper

Input:

- `EPhraseTrigger.NeedSniper`

Command state:

- `SetNeedSniper(10f)`

Targeting:

- Boss first seeds contact through `ProcessContactCommand(...)`.
- Only marksman followers receive the command.
- Rejects with `Negative` when marksman is busy with own immediate fight or needs/heals medical work.
- Clears temporary combat aggression override when accepted.

Core behavior:

- `FollowerSniperCombatLogic` marks `NeedSniper` consumable.
- `FollowerCombatLogicBase` rejects if healing, under fire, recently hit, or point-blank visible shootable threat.
- Accepted orders activate `FollowerCombatNeedSniperObjective`.
- Objective tries immediate shoot, current cover fire, support firing cover, or firing-position movement.
- Arrival arms a short `sniper.NeedSniper.positionHold`.
- Completes/rejects when enemy disappears, no lane exists after retry, direct shot is available, or stronger survival interrupts.

### Need Help

Input:

- `EPhraseTrigger.NeedHelp`

State:

- Does not create `FollowerCommandType`.

Behavior:

- Finds closest valid enemy from boss-tracked enemies, boss group enemies, boss visible contact enemies, and SAIN contact fallback.
- Marks boss logic as manually under attack by that enemy.
- Requests ordered-push cancellation before applying the new support signal.
- Calls `PrioritizeEnemy(...)` for each active follower.
- Core combat reacts through existing boss-under-attack protection/support routing.

### Combat Come With Me

Input:

- `EInteraction.ComeWithMeGesture` while selected follower has active combat enemy.

Command state:

- `SetCombatComeToBossCover(8f)`

Core behavior:

- This command interrupts hold/settle states and ordinary combat movement.
- Hold end paths break for the command:
  - committed arrival holds
  - default cover holds
  - default committed holders
  - marksman holds
  - base combat hold
- Heal-related relocation is protected and can defer the command until the command expires or movement finishes.
- On consume, `FollowerCombatCommon.TryCreateBossCoverAttackMovingDecision(...)` finds boss-local cover using `CombatDistanceConfiguration.GetBossCoverSearchRadius()`.
- The decision is forced to `BotLogicDecision.attackMoving` because the action expects a cover point.
- If no valid boss-local cover exists, the follower says `Negative` and plays `NoGesture`.

### Combat There

Input:

- `EInteraction.ThereGesture` while selected follower has active combat enemy.

Command state:

- `SetCombatMoveToPointTactical(commandTarget, 8f)`

Targeting:

- Closest active visible follower within gesture command distance.
- Command point is sampled from boss ray/look direction.
- Hard-limited to 30m from boss; it does not use `goToDistance`.

Core behavior:

- Same hold/settle and movement-interrupt rules as combat `ComeWithMe`.
- On consume, `FollowerCombatCommon.TryCreateBossCommandTacticalPointDecision(...)` sets `GoToSomePointData` and returns `BotLogicDecision.goToPointTactical`.
- Invalid target produces `Negative` and `NoGesture`.

## Receiver Patches And Vanilla Forwarding

Mod-owned phrases suppressed from vanilla follower receiver handling:

- `Stop`
- `HoldPosition`
- `Gogogo`
- `Suppress`
- `NeedSniper`
- `NeedHelp`
- `OnYourOwn`
- `CoverMe`

Mod-owned gestures suppressed from vanilla follower receiver handling:

- `ComeWithMeGesture`
- `HoldGesture`
- `ThereGesture`
- `CustomGestures.OverThere`

Unhandled phrases and gestures are still forwarded to follower receivers from `AIBossPlayer`, so vanilla behavior can continue for commands pitFireTeam does not own.

## Menu Notes

`GestureMenuPatch` injects/modifies menu entries and labels for:

- custom `TeamStatus` phrase
- custom `OverThere` gesture
- `OnRepeatedContact` display text
- optional `hideUnsupportedCommands` filtering

The menu is not authoritative for command behavior. It only controls what the player can see/select and how labels are localized.

## Cleanup Rules

Common command cleanup cases:

- `TryGetActiveCommand(...)` hides queued `PushEnemy` while healing and clears other commands while healing or after timeout.
- `ContactEnemy:RegisterContactEnemyForFollower` clears most request commands when combat enemy state appears.
- `FollowerRequestLayer` clears most known-enemy request commands before combat takes over.
- `GestureCommandAction` clears movement commands on arrival, invalid path, invalid target, danger, healing, grenade/BTR avoidance, and interaction failure.
- Core combat objectives clear command state when consuming or rejecting objective commands.
- Combat gesture orders are cleared when consumed, invalid, or issued while the follower is already moving.
