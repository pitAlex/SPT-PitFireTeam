# AI Role: pitFireTeam AI Mod Engineer

You are an AI engineering agent working on `pitFireTeam`, a C# mod for Single Player Tarkov built with BepInEx, Harmony, BigBrain, and optional SAIN integration.

Your job is to make safe, context-aware changes that preserve runtime stability, respect current architecture, and avoid assumptions about Tarkov/SPT/SAIN internals.

You must think like a maintainer of a fragile gameplay-AI integration project, not like a generic C# assistant.

## Terminology

- **SAIN Plugin** (or "SAIN mod", "SAIN") — the third-party SAIN mod by Sol (`me.sol.sain`). This is an external dependency. See `LOCAL.md` for the machine-local source path when source inspection is needed.
- **SAIN Addon** — our own addon DLL (`addon/`, plugin ID `xyz.pit.fireteam.sainaddon`) that integrates SAIN brain layers with followers. This is our code.

Never confuse these two. When the user says "SAIN plugin" or "SAIN mod" they mean the external SAIN mod, not our addon.

## Working Rules

Read code first. Assume nothing.

Check `LOCAL.md` for machine-local deployment paths and current local runtime notes. `LOCAL.md` is intentionally ignored by git; do not treat it as shared project documentation.

Use the current-version release notes file listed in `LOCAL.md` to note user-facing changes for the version currently in progress. When a change is intended for the current beta/release line, add it under the matching version heading there before building or packaging.

Check `docs/Localization.md` before adding or changing user-facing text. UI/server text must use the centralized pitFireTeam language model and embedded English fallback instead of hardcoded per-callsite fallback strings.

If a method, class, property, or runtime behavior is unclear:

- inspect the project source code
- inspect SAIN or BigBrain source if involved
- inspect decompiled EFT/SPT references when necessary

Never invent APIs, properties, or behaviors that do not exist. Only reference methods, properties, and classes that are verified in the source code.

Separate vanilla and SAIN reasoning. Every behavior should be classified as one of:

- vanilla / core plugin path
- SAIN addon / SAIN-owned path (runtime-gated by SAIN + addon presence)

Do not mix these paths unless the code clearly bridges them.

When fixing bugs or implementing changes:

- make the smallest correct change
- avoid broad refactors unless explicitly requested
- preserve current architecture and naming style
- prefer stability over elegance

Do not leave left overs. When going with a different approach, clean up (or revert) the old approach

Always think centralization giving the fact that we can have different types of followers with different tactics that may share some behaviors or functionality.

## Decision Priority

When multiple approaches are possible, prefer:

1. runtime stability
2. preserving existing architecture
3. minimal code changes
4. improved clarity or debugging
5. improved elegance

---

# pitFireTeam: Current Implementation Summary

**Last updated:** 2026-04-28  
**Scope:** Runtime behavior across `pitFireTeam/client`, `pitFireTeam/addon`, and `pitFireTeam/server`.  
**SAIN Addon is optional and runtime-gated**

## Project Overview

**pitFireTeam** is a three-tier modular architecture:

1. **CLIENT** (`client/`) — Game-side follower control and team UI.
    - Implements BigBrain layers for follower movement, commands, and decision-making.
    - Patches game systems (bot recruitment, group handling, loot/door interaction).
    - Manages UI for team management and teammate creation.

2. **SERVER** (`server/`) — Backend teammate management and social integration.
    - REST API for teammate CRUD operations.
    - Social list/profile patching to merge teammates with stock friends.
    - Group invite and raid-spawning routes.
    - Post-raid item/escape handling.

3. **SAIN ADDON** (`addon/`) — Optional SAIN-specific combat layer and retention system.
    - Custom follower combat layer replacing SAIN squad layer.
    - Decision routing for suppression, search, help, and regroup actions.
    - Enemy retention gating and forced acquisition assistance.
    - Follower-specific aim/vision/hearing tuning patches.

---

## Architecture Key Constraint

- Server backend is **limited to teammate profile creation/storage/social flows** and is **not** a general bot profile generator.
- For debug/runtime follower spawn: use existing game-side `ISession.LoadBots` profile loading (not BE-dependent).
- If a spawn flow needs BE profile data and local profile is unavailable, fail fast with a clear reason.

---

# CLIENT SIDE: Follower AI & Team Management

**Plugin ID:** `xyz.pit.fireteam`  
**Main entry:** `client/friendlyPlugin.cs`

## 0a) Teammate System Status (In Progress)

Current verified custom teammate feature state:

- Dedicated Team Management FE is the primary entry point:
    - main menu now has a localized `My Squad` entry that opens the real `MatchMakerSideSelectionScreen` in squad mode
    - roster/settings panels from `SquadControlMenuUi` are injected into side-selection and controlled by EFT-style animated tabs (`Roster` / `Settings`)
    - roster tab supports add/remove teammate flows, delayed sequential portrait loading, teammate profile open/return, and scrolling layout for larger squads
    - settings tab exposes the main pitFireTeam config set in a stock-style scrollable UI using EFT toggle/slider controls for checkbox and ranged settings
    - settings entries are grouped/reordered for the current squad-management UX and the duplicated BepInEx ConfigurationManager view is hidden for those settings
    - squad-mode lifecycle is explicit-action based: mode is cleared on side-selection `Back`, bottom-bar `MainMenu` root return, and `Play` transition
- Teammate creation flow is implemented through the stock appearance screen:
    - name entry
    - player-side forced automatically
    - head and voice selection
    - localized validation/prompt text overrides
    - custom back/submit handling
    - submit posts `{ nickname, voice, head }` to `/singleplayer/pitfireteam/teammate/create`
    - server creates a PMC bot of the player side and stores it under `user/mods/pitFireTeam-ServerMod/Resources/teammates/<sessionId>/<aid>.json`
- Stock social/profile flows are bridged for teammates:
    - teammates are merged into `/client/friend/list`
    - teammate profile view is merged into `/client/profile/view`
    - teammate deletion is bridged through `/client/friend/delete`
    - add-success refresh updates the visible list without restarting the game
    - teammate ids now use stock `HashUtil.GenerateAccountId()` collision-checked allocation
    - 4.x invite popup is patched separately because stock `Commando` and `SPT` chat bots share the same `Aid`
- Team grouping flow is functional but not fully parity-complete:
    - teammate appears in right-click invite/group flows
    - teammate can accept group invite
    - pre-raid ready screen and loading screen can show player + teammate
    - persisted `Auto Join` can preload selected teammates into the next PMC ready flow
    - teammate portrait right-click exposes `Invite to group`, `View profile`, and `Auto join on/off`
    - removing a teammate from the ready/group flow suppresses that teammate for the current auto-join cycle until re-added or reset
    - ready-screen preview rehydrates teammate visual health and shows a secure-container contents summary
    - local/offline raid guard is enforced late in `TarkovApplication` and `MainMenuController.method_52()`
    - teammate path preserves the normal PMC insurance screen before the custom ready screen
- Server teammate routes include current client compatibility paths:
    - `/client/game/bot/followergenerate`
    - `/client/game/bot/followerdetails`
- Teammate profile view now has completed profile-side customization features:
    - hideout/report actions are hidden for teammate profiles
    - stock clothes dropdowns are reused for teammate suit selection
    - custom loadout dropdown is injected below the clothes selector
    - loadout choices come from the player profile's saved equipment builds plus `Default`
    - selected teammate loadout persists through `/singleplayer/pitfireteam/teammate/profile/loadout`
    - persisted loadout selection flows through follower details and shows as the current teammate equipment name
    - teammate rename is implemented through a custom overlay + backend rename route
    - stock `SkillsScreen` is cloned into teammate profile view with filtered follower-relevant skills
    - teammate-only profile UI resets correctly when switching back to a normal player profile
- Teammate custom loadout editor is implemented for the current cloned/local editing model:
    - teammate profile has an `Edit Loadout` entry point
    - modal shell and fake local inventory session exist
    - left side renders a cloned fake stash
    - right side renders a cloned follower equipment/inventory view
    - secure container and dogtag are hidden from the follower-side container display
    - drag header / overlay movement is implemented
    - edits stay local until `Done`
    - custom player equipment builds can be overwritten or saved under a new preset name
    - editing `Default` saves directly as the teammate default equipment without opening the preset naming dialog
- Current backend/social/profile/runtime limitations:
    - voice/head customization from profile screen is not implemented yet
    - custom loadout editor still uses cloned/local items; immersive real stash consumption is not implemented
    - Team screen `Settings` tab covers checkbox/ranged settings and keybind capture; staged save/cancel/default parity is still pending if needed later
    - teammate invite/group flow still needs more parity with old plugin around pre-raid screen sequencing and group state handling
    - old chatbot-style teammate management is not ported; current management path is the roster/context-menu flow
    - teammate profiles remain mod-owned bot JSON, not full stock `SptProfile` accounts

## 0) Project Context

- Old plugin codebase: see `LOCAL.md` reference paths.
- Old client reference (3.11): see `LOCAL.md` reference paths.
- New client reference (4.x): see `LOCAL.md` reference paths.
- SAIN plugin reference: see `LOCAL.md` reference paths.
- Positioning:
    - `pitFireTeam` is both:
        - a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment,
        - and an alternative plugin implementation with new BigBrain-native follower layers/actions.

## 1) Core Runtime Model

- Plugin: `xyz.pit.fireteam` (`client/friendlyPlugin.cs`)
- Dependency: BigBrain (`xyz.drakia.bigbrain`)
- Optional integration: SAIN (`me.sol.sain`) detected at runtime.
- Optional SAIN addon integration: `xyz.pit.fireteam.sainaddon` (separate DLL in `addon/`)
- Core runtime flags in `client/friendlyPlugin.cs`:
    - `UseSainFollowerCombat` = SAIN installed + addon present
    - `ShouldDisableSainForFollowers` = SAIN installed + addon missing
- Follower control model:
    - Combat remains vanilla/SAIN-owned.
    - Friendly follow logic is implemented as a BigBrain custom layer/action (`FollowerPatrolLayer` + `FollowAction`).
    - Regroup request execution is split by runtime context:
        - vanilla regroup path for no-SAIN or out-of-combat,
        - SAIN combat path is handled by addon `SAINFollowerCombatLayer` (custom SAIN squad-layer replacement for followers).
    - If SAIN is installed but the addon is missing:
        - core keeps follower combat on the vanilla/core BigBrain path,
        - core suppresses SAIN follower layer takeover so SAIN does not pause or own followers,
        - non-followers continue using SAIN normally.

## 0b) Follower Core Components

**State & Management:** (`client/Components/`)

- `BotFollowerPlayer.cs` — Per-follower state container (active command, healing status, lifecycle)
- `AIBossPlayer.cs` — Boss command handler (TeamStatus, gestures, loot/door requests, attention)
- `BossFollowerPlayer.cs` — Boss-side follower roster management
- `SquadControlMenuUi.cs` — Team Management UI (My Squad roster/settings screens)

**Follower Lifecycle:**

- Recruit flow through `BotReceiverFollowMeRecruitPatch`
- On conversion: brain/layer reset, conflict cleanup, group assignment, enemy/friendly list adjustment
- Dismiss: trigger `OnFollowerDismiss` event for addon cleanup
- English voice assignment applied at profile-load time via `SessionLoadBotsEnglishVoicePatch`
- Followers now persist raid-earned experience and common-skill progression through the backend follower-progress route
- Teammate raid outcomes now persist profile counters for sessions, survived exits, and deaths so derived stats such as survival rate and K/D remain grounded in saved teammate data
- Follower kills now contribute to player kill-quest progress and legacy-style raid XP counters when eligibility checks pass
- Transit-ready teammates are carried forward through the synthetic raid group path between raids/maps

## 0c) BigBrain Follower Decision System

**Layer Stack (Priority Order):**

1. **FollowerRequestLayer** (priority 73) — Active command execution (hold/there/come/loot/door)
2. **FollowerCombatLayer** (priority 72, vanilla/core combat only; inactive when SAIN addon combat is active)
3. **FollowerPatrolLayer** (priority 71) — Idle follow patrol toward boss

**Core Files:**

- `client/BigBrain/FollowerCombatLayer.cs` — Core follower PMC combat logic and decision routing when SAIN follower combat is unavailable
- `client/BigBrain/FollowerPatrolLayer.cs` — Follow logic, state recovery, action selection
- `client/BigBrain/FollowerRequestLayer.cs` — Command request detection and activation
- `client/BigBrain/Actions/FollowAction.cs` — Chase and cover-settle movement
- `client/BigBrain/Actions/GestureCommandAction.cs` — Command execution pipeline (12 action types)
- `client/BigBrain/Actions/HealAction.cs` — Medical work with timeout safety
- `client/BigBrain/Actions/FollowerCombatActionBase.cs` — Shared base/data wrapper for split combat actions
- Combat actions are split into individual files under `client/BigBrain/Actions/` (no monolithic `FollowerCombatDecisionActions.cs` anymore)
- Additional actions: `PeacefulAction.cs`, `PeaceHardAimAction.cs`, `GoToCoverPointAction.cs`, `EatDrinkAction.cs`

**Core Follower Combat Behavior:**

- `FollowerPmcCombatLayer` only runs on the core/vanilla path (`!UseSainFollowerCombat`)
- Layer activation now uses a valid-living-enemy gate instead of raw `Memory.HaveEnemy`:
    - stale dead `GoalEnemy` entries should not keep followers in combat,
    - `FollowerCombatLayer.IsCurrentActionEnding()` now force-ends combat actions when no valid enemy remains,
    - healing/stimulator actions are the explicit exception and are allowed to finish without a live enemy.
- Current supported BigBrain combat actions are file-split and mapped from `BotLogicDecision`:
    - `CombatHoldPositionAction`
    - `CombatRunToCoverAction`
    - `CombatAttackMovingAction`
    - `CombatAttackMovingWithSuppressAction`
    - `CombatAttackRetreatAction`
    - `CombatDogFightAction`
    - `CombatShootFromPlaceAction`
    - `CombatShootFromCoverAction`
    - `CombatGoToEnemyAction`
    - `CombatRunToEnemyAction`
    - `CombatGoToPointAction`
    - `CombatRegroupRunAction`
    - `CombatGoToPointTacticalAction`
    - `HealAction`
    - `HealStimulatorsAction`
    - `CombatSearchAction`
    - `CombatSuppressGrenadeAction`
    - `CombatSuppressFireAction`
    - `CombatShootToSmokeAction`
- `CombatGoToEnemyAction` is now the custom old-plugin-style advance action:
    - cover-aware advance,
    - periodic path refresh,
    - short committed approach point to reduce wall-side dithering,
    - progress-stall detection to force repath when the bot is not actually advancing,
    - blind advance now prefers looking toward the current move destination,
    - a short wall probe flips look control to movement direction when destination-facing would pin the bot into nearby geometry.
- `CombatRunToEnemyAction` is now also custom-owned on the core path:
    - no longer delegates directly to vanilla `GClass227`,
    - keeps a committed run target,
    - refreshes pathing when stair/vertical pushes stop making progress.
- `runToEnemy` remains the decisive sprint path and is still allowed to end when the bot reaches a valid firing state.
- `goToPointTactical` end routing is now split by reason:
    - `enemySearch` uses old-plugin-style `EndEnemySearch` logic,
    - other tactical-point usage uses old `EndGoToCoverPointTactical`-style checks,
    - group-search follower tactical movement no longer relies on a dedicated separate tactical end path.
- Core combat manager ownership changed:
    - old `FollowerCombatManager` usage was removed from the core path,
    - `AIBossPlayer` no longer updates a separate follower combat manager,
    - combat decisions are now follower-local inside `FollowerCombatDefault`.
- Core combat layer handoff:
    - live-enemy loss no longer immediately drops combat in all cases,
    - `FollowerCombatLayer` can enter a short `linger` hold for post-combat release/handoff timing,
    - normal action completion is delegated to tactic/common end logic instead of a layer-level action-enum comparison.
- Core combat routing is now objective-based on the vanilla/core path:
    - `FollowerCombatLogicBase` now owns the shared objective router and objective lifecycle
    - `FollowerCombatLogicBase` constructs the shared objective set: `FollowerCombatDefaultObjective`, `FollowerCombatSniperObjective`, and `FollowerCombatRegroupObjective`
    - concrete combat logic classes select their primary objective instead of replacing a generic default slot
    - `FollowerPmcCombatLogic` is the PMC combat entry point and selects the concrete logic from follower tactic
    - default/balanced PMC logic uses `FollowerCombatDefaultObjective` as its primary objective
    - marksman PMC logic uses `FollowerCombatSniper` with `FollowerCombatSniperObjective` as its primary objective
    - both default and marksman logic reuse the same `FollowerCombatRegroupObjective` path through `FollowerCombatLogicBase`
    - objective selection is combat-owned, not request-layer-owned
    - regroup command in combat is consumed immediately and converted into the regroup objective state
    - explicit `PushEnemy` during regroup switches back to the active tactic's primary objective
- Combat tactic construction rules:
    - keep routing/lifecycle in a `FollowerCombatLogicBase` subclass, keep the tactic decision tree in a dedicated class such as `FollowerCombatDefault` or `FollowerCombatSniper`, and keep the objective wrapper thin like `FollowerCombatDefaultObjective` / `FollowerCombatSniperObjective`
    - do not merge tactic decision trees into each other; `FollowerCombatDefault` and `FollowerCombatSniper` should be separate primary stacks that only share primitives through `FollowerCombatCommon`
    - `FollowerCombatCommon` should contain shared primitives and reusable decision helpers, not default-only or marksman-only policy
    - every movement decision that depends on cover or point data must set that destination first through committed cover, assigned cover, `GoToSomePointData.SetPoint(...)`, or a verified action-owned destination path
    - if a tactic cannot find a valid destination for a movement decision, return false and let the next branch decide instead of emitting a stale/no-target movement action
    - regroup stays objective-level and shared; default, marksman, protector, and future tactic stacks should activate regroup through `CreateRegroupObjectiveDecision()` rather than duplicating regroup movement logic

## 0c-1) Cover Contract (Authoritative)

All follower combat tactics must treat cover as a shared lifecycle contract, not tactic-local ad hoc behavior.

Required cover lifecycle:

1. select cover
2. move to cover
3. detect arrival (`IsInCover`, committed-cover proximity, or arrived point)
4. transition into hold/think window (`HoldFor`/`HoldCoverForMaxDuration`)
5. allow break into next decision when higher-priority conditions appear

Mandatory break priority while holding in cover:

- immediate fire opportunity (`ShouldShootImmediately` / valid visible shoot lane)
- taking fire or recent hit pressure
- ally engagement support opportunity
- boss-under-attack support/protection opportunity
- cover compromised while under pressure (left cover / unstable no-cover hold under fire)
- boss-distance regroup objective pressure when no stronger local fight reason exists

Heal-cover exception:

- heal cover movement (`runToHeal` / `moveToHeal`) must break immediately on arrival and hand off to heal decision.
- do not force a normal hold-think timer before `healInCover`.

Implementation guidance:

- keep arrival detection and hold arming in shared `FollowerCombatCommon` end-condition paths so all tactics inherit consistent behavior.
- tactic classes should only add/override policy-level break gating; do not fork base arrival semantics unless strictly necessary.
- do not leave movement actions running after destination arrival due to delayed EFT cover flags; use committed-cover/proximity and arrived-point fallbacks.

## 0c-2) Tactic-Specific Cover Usage

`Balanced` / default tactic:

- uses committed cover as primary stabilization primitive.
- on reached cover, should hold briefly and continuously rescan: immediate fire, visible fire, ally support, boss-under-attack, regroup pressure.

`Protector` tactic:

- prioritizes boss-local support cover and boss protection over generic pressure cover.
- when boss-under-attack is active, protection support routes should preempt passive hold.

`Marksman` tactic:

- prefers shoot-capable firing positions and sticky support/reposition holds.
- still must honor the same arrival -> hold -> break contract.
- sticky cover/cooldown behavior must not block valid regroup break conditions once no higher-priority fire/support action exists.

`Regroup` objective:

- regroup owns bossward movement/cover selection and should not re-enter default/sniper push stacks while active.
- regroup may use bossward cover in hot contact, but completion remains objective-owned.

Cross-tactic rule:

- any new tactic (or objective) must explicitly define how it uses shared cover lifecycle hooks, and must preserve the mandatory hold break priorities above.
- `FollowerCombatDefaultObjective` wraps the existing `FollowerCombatDefault` tree:
    1. pre-fight grenade check and one-shot opener from `PrepareStartDecision`
    2. committed push continuation
    3. explicit ordered push (`GoForward` in combat)
    4. low-aggression regroup trigger
    5. immediate visible-enemy handling
    6. recovery / safe-cover behavior
    7. boss-under-attack protection routing
    8. ally engagement support scan
    9. generic blind advance / engage
    10. boss-distance regroup trigger
    11. committed cover continuation and passive hold fallback
- `FollowerCombatSniper` is separate from default PMC combat logic:
    - `FollowerCombatSniperObjective` owns the marksman decision stack
    - marksman ignores explicit push/suppression/grenade orders that should not make a sniper rush
    - close-quarter marksman handling can switch to a full-auto secondary and avoids forcing `runToEnemy` back to primary
    - marksman repositioning prefers shoot-capable cover/firing-position movement and hands boss-distance regroup to the shared regroup objective
- `FollowerCombatRegroupObjective` is a separate combat decision stack:
    - objective is "reach the boss / bossward cover" and it does not re-enter default push/hold logic while active
    - if enemy contact is still hot:
        - boss behind follower relative to enemy -> `regroup.withdraw.backward` via `attackMoving`
        - boss ahead -> `regroup.withdraw.forward` via `attackMoving`
        - boss lateral -> `regroup.withdraw.side` via `attackMoving`
        - hot regroup can use bossward cover, but the objective remains regroup-owned
    - if contact cools off -> `regroup.run` through `CombatRegroupRunAction` toward the boss/nav-sampled boss position, not intermediate cover hops
    - dogfight and heal/stim interruptions are allowed, but they do not change the regroup objective by themselves
- Visible-enemy handling is now more aggressive:
    - `ShouldShootImmediately()` is checked before `CanShootFromCurrentCover()`,
    - visible shootable enemies prefer `shootFromPlace` / `shootFromCover` before passive hold logic,
    - very-close visible enemies collapse into `dogFight`,
    - visible but not currently shootable enemies can still hand off into engage pressure.
- Combat cover is now follower-local and committed:
    - one cover point is committed and reused until invalid or intentionally abandoned,
    - the same committed move action/reason is preserved while moving to that cover to reduce stop/recommit thrash,
    - committed cover reached-state prefers immediate fire, then direct visible fire, then active pressure, before passive `coverHold`.
    - after the initial cover settle/commit window, stale committed cover can be released for boss-distance regroup if there is no real shooting or push opportunity.
- Core push behavior now has committed push state again:
    - push actions are tagged with stable reasons such as `push.run`, `push.goToEnemy`, `push.attackMoving`, `push.runToCover`, and `push.search`,
    - once one of those push decisions is chosen, `FollowerCombatDefault` keeps returning the same decision until a hard interrupt or action completion path ends it,
    - push selection itself is still delegated to the restored `FollowerCombatPush.EngageEnemy(...)` helper.
- Combat heal/stim flow now has explicit state handling in `FollowerCombatCommon`:
    - `healStimulators` is fully wired (decision -> action -> end handler),
    - stim use no longer preempts pending first-aid/surgery work,
    - heal-cover movement reuses one committed heal cover instead of repeatedly picking new points,
    - heal and stim actions have timeout exits to avoid stuck medical states.
- Core combat cover search is back on the mod-owned routing path:
    - `TryCommitCombatCover()` again prefers `PointToShoot`, retreat/attack cover, and boss-cover fallback,
    - `Covers.cs` now seeds candidate space from EFT `CoverSearchData` / `CoverPointMaster`, then applies pitFireTeam filtering and final selection,
    - so the current experiment is "our routing and filters, vanilla-backed candidate discovery".
- Combat cover is no longer boss-leashed by EFT `MAX_DIST_COVER_BOSS_SQRT`:
    - general combat cover uses a mod-owned 120m max distance from the follower,
    - a valid shoot/fight cover is allowed even if it is far from the boss.
- Boss-local protection/regroup cover is now separate from generic combat cover:
    - `BossCoverSearchRadius` is 30m,
    - boss-protection and hot-regroup cover prefer valid cover near the boss within that radius,
    - if no boss-local cover exists, fallback is a direct move to the boss position.
- Boss-distance escort behavior now participates in combat objective selection:
    - if the follower drifts far enough from the live boss position, default combat switches into the regroup objective instead of issuing one-off reanchor actions,
    - regroup chooses how to move by classifying the boss as front/back/side relative to the follower and current enemy direction,
    - low aggression can force regroup sooner unless the enemy is already close enough to demand local engagement,
    - explicit `PushEnemy` still overrides this and forces engagement.
- Hold behavior now distinguishes `coverHold` and `bossHold`:
    - both can break early for renewed combat opportunity,
    - `bossHold` and committed cover/shoot-cover states can break into regroup when boss-distance pressure wins.
- Core suppress-fire now supports a short recent-contact pressure window on the vanilla/core path:
    - if a follower just lost clean LOS, `suppressFire` can keep shooting briefly toward `EnemyLastPositionReal`,
    - this is intentionally narrower than broad blind-fire logic and is only used for suppression-style fire continuity,
    - `FollowerShotSafety` remains the hard stop; no recent-contact pressure shot is allowed through the player boss or other followers.
- Combat hold look priority now anchors to the current enemy direction first:
    - `CombatHoldPositionAction` / `EnemyFacingHoldLogic` tries current enemy or last known enemy look before recent-threat or ally-look fallback,
    - this keeps hold orientation aligned with ping/callout direction when the follower already has a combat enemy.
- Dedicated regular grenade use is now explicit on the core path:
    - no hidden opportunistic grenade throw inside dogfight,
    - grenade throw goes through `throwGrenadeFromPlace` with a dedicated safety gate.
- Combat talk frequency is now gated by the `botTalk` config on both vanilla `BotTalk` and SAIN `PlayerComponent.PlayVoiceLine` follower paths.

**`PrepareStartDecision` — Combat Entry Decision Tree (`FollowerCombatCommon`):**

Called once per combat activation to pick the opening decision stored in `initialDecision`, consumed via `ConsumeInitialDecision()`. Priority order:

1. **Visible + close cover with shoot lane** (`≤25m` nav-path) → `attackMoving` (`startVisCloseCover`)
2. **Unseen + under fire:**
    - close cover → `attackMovingWithSuppress` (`startSuppressionCover`)
    - far cover → `runToCover` (`startUnderFireRunCover`)
    - no cover → `suppressFire` (`startUnderFireSuppress`)
3. **Unseen + not under fire + ally actively engaging enemy** (`TryGetAllyEngagementEnemy`):
    - cover found near ally enemy →`attackMovingWithSuppress` if `≤30m`, else `runToCover` (`startAllySupportSuppress` / `startAllySupportRun`)
4. **Unseen + low-threat enemy** (`IsEnemyLowThreat`, single enemy) → `goToEnemy` or `runToEnemy` based on distance (`startWeakEnemyPush`)
5. **Far cover fallback** → `runToCover` (`startVisFarCover` / `startBlindFarCover`)

If none match, `initialDecision` stays null and the layer falls through to `DecideCombat()`.

**Patrol Readiness (Post-Combat Handoff):**

- Wait for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of fixed timeout
- SAIN-installed: uses addon-registered bridge callback (`SainAddonBridge.IsReadyForPatrolAfterCombat`)
- Fails closed with explicit error log if SAIN present but bridge unavailable
- Sprint thrash prevention via `FollowerSprintStateDirectionPatch` (SAIN-aware)

## 0d) Command Execution Pipeline

**Request Layer activates when `BotFollowerPlayer.CurrentCommand != null`**

Supported commands via `GestureCommandAction`:

- **HoldPosition** — Stop, optional crouch, periodic look-around (no timeout, persists until replaced)
    - Standard gesture-initiated: applies crouch by default
    - Phrase-initiated (STOP): applies no crouch, released by distance >25m or boss out-of-range
- **ComeCloser** — Move within ~1m of boss, then resume prior hold
- **MoveToPoint** ("There") — Walk to NavMesh-validated target, brief arrival look-around
    - Gesture-initiated: single-follower move-to-point
    - Phrase-initiated: still used for explicit point movement when follower has no combat enemy
    - if movement is not accepted, the chosen follower can still receive a brief command-look override toward the pointed location
- **PushEnemy** (`GoForward` in combat) — Force combat engage routing against the follower's current enemy
    - `AIBossPlayer.ApplyGoForwardPhrase()` targets the looked-at follower if one is selected, otherwise it broadcasts to active followers
    - command acceptance is no longer limited by the old phrase-distance gate
    - if the follower already has an enemy, `GoForward` is converted into `PushEnemy` and handled inside `FollowerCombatDefault` through `FollowerCombatPush.EngageEnemy(true, ...)`
    - combat hold/committed-cover states now break so the ordered push can take over
- **HoldPosition combat phrase** (`EPhraseTrigger.HoldPosition`) — Temporarily applies `0%` effective combat aggression to targeted followers
    - stored as a temporary override in `BotFollowerPlayer`, leaving the persisted profile aggression untouched
    - core/vanilla combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`
    - SAIN addon combat treats the override as boss-protection/regroup intent through `SAINFollowerCombatLayer`
    - marksman close auto-search is suppressed while this temporary hold-position override is active
    - override clears when combat/patrol handoff reports the follower safely out of combat, or when `Gogogo` is issued
- **Gogogo combat phrase** (`EPhraseTrigger.Gogogo`) — Clears any temporary combat-aggression override and returns followers to their persisted aggression value
- **LootGeneric** / **LootWeapon** — Closest eligible follower assigned via `InteractableObjects.SetTaker(...)`, moves + inventory transfer
    - Now resilient to transient state changes: pins selected loot and attempts taker recovery before aborting
    - Supports all item types with improved state consistency
- **OpenDoor** — Closest eligible follower assigned via `InteractableObjects.SetOpener(...)`, moves + `DoorOpener.Interact(Open)`
- **Regroup** — Vanilla: converge to boss-near cover; SAIN: via addon `SAINFollowerCombatRegroupAction`
    - core-path combat regroup no longer runs through `GestureCommandAction`
    - during combat it is now an objective trigger consumed by the active `FollowerCombatLogicBase` implementation
    - out of combat it still uses the request-layer regroup command path
- **Attention** (Look) — Clear enemy state, release command, force attention to boss/point
- Directional quick phrases and contact cues now share a command-look override path:
    - `OverThere`, `Contact`, `Front`, `Left`, `Right`, and `OnSix` feed a temporary look target relative to the boss look direction
    - combat hold, core combat movement, and follow movement consume the same override so followers can briefly reorient without permanently stealing action ownership

**Visibility Requirements:**

- `HoldGesture` / `ThereGesture` — Follower sees boss gesture target (head or torso visibility sufficient)
- `ComeWithMeGesture` — Bidirectional visibility (boss sees follower AND follower sees boss)

**Interruption/Clearing:**

- Hit while executing command
- New command received (replaces prior)
- Attention/Look command (overrides)
- `FollowMe` / `Cooperation` phrase (clears request)
- Timeout or invalid execution state
- On regroup success: follower says `EPhraseTrigger.OnPosition`
- Combat transition boundaries now clear outstanding gesture/request commands:
    - `FollowerCombatLayer.Start()` clears follower commands on combat entry
    - `FollowerCombatLayer.Stop()` clears follower commands on combat exit

## 0e) Recruit & Group Patching

**Files:**

- `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase -> follower conversion
- `client/Patches/BotRecruitPatch.cs` — Request interception
- 35+ patches total across bot/group/follower/UI stability

**Core Bot/Group/Follower Stability Patches:**

- `BotGroupAddEnemyPatch`, `BotGroupReportEnemyPatch`, `BotGroupUsecEnemyPatch` — Enemy propagation safety
- `BotGroupCalcGoalPatch` — Enemy acquisition assist hook
- `BotControllerEnemyPropagationSafetyPatch` — Validate player refs before propagation
- `BotOwnerIsFolowerPatch`, `BotOwnerManualUpdatePatch`, `BotOwnerActivatePatch` — Bot activation/update flow
- `BotMemoryDamagePatch`, `ExUsecBrainHitPatch` — Damage/hit reaction safety
- `LootPatrolActiveLayerListPatch`, `LootPatrolDecisionBypassPatch` — Prevent LootPatrol interference
- `AdvAssaultTargetFollowerGuardPatch`, `PatrolDataFollowerUpdateGuardPatch`, `AvoidDangerFollowerGuardPatch` — Vanilla follower recovery guards
- `AICoreAgentUpdatePatch` — Log/rethrow update exceptions

**Movement & Sprint Patches:**

- `FollowerSprintPatch` (when core follower movement owns sprint, including SAIN-without-addon) — Sprint behavior tuning
- `FollowerSprintStateDirectionPatch` — Prevent sprint/transition thrash during boss chase

**Recruit/Request Patches:**

- `BotReceiverFollowMeRecruitPatch` — Convert recruit requests to follower
- `FollowRequestPatch`, `HoldRequestPatch`, `OpenDoorRequestPatch` — Request type routing
- `BotReceiverGestureOverridePatch` — Gesture override handling
- `BotReceiverPhraseOverridePatch` — Route STOP phrase through pitAIBossPlayer instead of vanilla BotReceiver

**Spawn/Raid Patches:**

- `BotsControllerPatch`, `BotsControllerStopPatch` — Bot controller lifecycle
- `LocalGameCleanupPatch`, `LocalGameCtorPatch` — Local game init/cleanup
- `BotsEventsControllerSpawnPatch`, `BossSpawnWaveManagerClassPatch` — Wave/event spawning
- `RaidStartPatch` — Raid initialization

**UI/Command/Item Patches:**

- `AIDataContructPatch` — AI model data
- `QuickPanelPatch` — Cooperation UI entry (non-follower AI only)
- `GestureMenuPatch`, `GestureMenuAvailablePhrasesPatch` — Gesture availability
- `EPhraseTriggerPatch`, `PlayPhraseOrGesturePatch` — Custom phrase/gesture routing
- `ItemSpecificationPanelPatch`, `ModRaidModdablePatch`, `UnlootableComponentPatch` — Item interaction
- `BotTalkTrySayPatch`, `BotTalkSayPatch` — Speech routing
- `GrenadeThrowPatch`, `GrenadeTryThrowSafetyPatch` — Grenade safety
- `BulletImpactPatch`, `HearingSensorPatch`, `FootstepSoundPatch`, `PlayerSayPatch` — Sound/reaction flow

**Teammate/Social UI Patches:**

- `AddTeammateCreationFlowPatch`, `AddTeammateHeadSelectionPatch` — Teammate creation screen
- `SocialPatch`, `ChatFriendsPanelPatch`, `OtherPlayerProfileScreenPatch` — Social UI integration
- `MenuScreenSquadControlPatch` — Main-menu `My Squad` entry/button wiring
- `MatchMakerSideSelectionScreenPatch` — squad-mode side-selection screen takeover, tab injection, and teardown/restore
- `CurrentScreenTryReturnToRootScreenPatch` — clears squad-mode on explicit root return (`MainMenu` bottom tab)
- `MainMenuControllerReadyScreenGatePatch` — clears squad-mode on `Play` transition
- `MatchMakerAcceptScreenPatch` / related raid-start patches — synthetic teammate injection, preview rebuild, auto-join preload, transit re-add, and ready-screen parity guards

**Current Group Enemy Sync Model:**

- Group enemy sharing is now driven from `AIBossPlayer.OnBossGroupStaticUpdate()`
- Core behavior:
    - if one active follower has a stable enemy and is in a valid combat state,
    - and another active follower has no enemy,
    - core calls `BotsGroup.ReportAboutEnemy(...)` once for that update pass
- `BotGroupReportEnemyPatch` postfix remains the passive consumer that applies the report to idle followers
- Old overlapping follower-side sticky/adopt/team-sync logic was removed from `BotFollowerPlayer`

## 0f) Utility Modules & bridges

**File:** `client/Modules/`

**Addon Bridge Contract:**

- `SainAddonBridge.cs` — Delegate interface for SAIN addon callbacks
    - `IsReadyForPatrolAfterCombat(BotOwner)` — Patrol readiness query
    - `OnFollowerDismiss(BotOwner)` — Lifecycle event for addon cleanup
    - addon-available combat/reset/sync calls are only attempted from core when `UseSainFollowerCombat == true`

**Shared Utilities:**

- `BotOwnerUpdateHub.cs` — Centralized bot-owner update coordination
- `FollowerCalcGoalEnemyAcquire.cs` — Forward-scan enemy acquisition (runtime-neutral, assists vanilla + SAIN)
- `FollowerEnemyEnforceSuppression.cs` — Attention/Look suppression enforcement
- `InteractableObjects.cs` — Loot/door interaction state, boss visibility tracking, taker/opener assignment
- `AddTeammateCreationFlow.cs` — Teammate profile creation form state
- `BossPlayers.cs` — Boss/player roster management
- `FollowerTalkFrequencyGate.cs` — Follower-only combat talk throttling shared by vanilla and SAIN talk hooks
- `PingTeamates.cs` — Teammate enemy marker UI & callout system (throttled callouts, radio/visual markers)
- `TeammateAutoJoinRuntime.cs` — Per-ready-cycle suppression tracking for persisted auto-join teammates
- `FollowerRecovery.cs`, `FollowerAwareness.cs`, `Enemy.cs`, `Utils.cs` — Helper utilities

**Localization:**

- `TempEnglishLanguageProvider.cs` — Custom English text (teammate creation, squad menu, validation)

---

# SERVER SIDE: Teammate Backend & Social Integration

**Plugin ID:** `xyz.pit.fireteam` (server component)  
**Main entry:** `server/pitFireTeam.Server.cs`

## Backend Responsibilities

**Core System:**

- PMC armband enforcement: Forces all PMC-type bots to wear side armbands (BLUE for USEC/Assault, RED for BEAR)
- Plugin priority: `PostDBModLoader + 1` (applies after database loads)

**Profile Management:**

- Create mod-owned teammate profiles (PMC bot + custom nickname/voice/head)
- Store on disk: `user/mods/pitFireTeam-ServerMod/Resources/teammates/<sessionId>/<aid>.json`
- Fetch full profiles for team UI (roster, profile view)
- Persistence: clothes, head, voice, loadout, auto-join flag, raid-earned XP, and common skills per teammate
- Teammate IDs use stock `HashUtil.GenerateAccountId()` collision-checked allocation

## REST API Routes

**Teammate Management** (`/singleplayer/pitfireteam/`):

- `POST /teammate/create` — Create custom teammate (nickname/voice/head from UI) → saves to disk
- `GET /teammates` — List all teammates for session
- `GET /teammate/profile` — Fetch full profile for profile view screen
- `GET /teammate/profile/options` — Available customization options (clothes/loadouts)
- `POST /teammate/profile/suit` — Update clothes/head selection
- `POST /teammate/profile/loadout` — Change loadout
- `POST /teammate/profile/rename` — Rename teammate
- `POST /teammate/autojoin` — Persist teammate auto-join enabled/disabled state
- `POST /teammate/delete` — Delete teammate permanently

**Synthetic Team Ready Flow** (`/singleplayer/`):

- `GET /autoteam` — List teammate account ids currently flagged for persisted auto-join in the next PMC ready flow

**Social List Merging** (`/client/`):

- `GET /friend/list` — **Merge** teammates into stock friend list
- `GET /friend/request/list/inbox` — **Merge** recruit requests with stock friend requests
- `GET /profile/view` — **Intercept** teammate profile views (custom UI layout)
- `POST /friend/request/accept` — Accept friend/recruit request
- `POST /friend/request/accept-all` — Accept all requests
- `POST /friend/request/decline` — Decline request
- `POST /friend/delete` — **Intercept** to also delete teammates

**Group & Raid** (`/client/`):

- `POST /match/group/invite/send` — Send group invite; **auto-accept** for teammates + notify
- `POST /game/bot/followergenerate` — Generate teammate spawn profile for raid
- `GET /game/bot/followerdetails` — Fetch follower details (tactic/equipment/voice/head, currently "Default")
- `POST /game/bot/followerprogress` — Persist follower raid-earned XP/common skill progress to teammate storage

**Post-Raid** (`/singleplayer/`):

- `POST /returnitems` — Return teammate items via mail (NOT YET POSTED in client runtime)
- `POST /teamescaped` — Log escape/death outcome + notify teammates
- `POST /pitfireteam/teammate/raid-outcomes` — Persist teammate escaped/lost raid outcomes, health/death state, raid-stat counters, and eligible loadout-management equipment state
- `POST /pitfireteam/recruitpickup` — Queue defeated NPC candidates as friend requests

## Backend Services

**`FriendlyTeammateService`** — Core CRUD & Profile

- Create teammate from appearance form (name, voice, head)
- List/fetch teammates by session
- Get/set profile (full fetch, clothes/loadout persistence)
- Persist teammate auto-join flag in sidecar settings JSON
- Rename teammate
- Delete teammate + remove from social lists
- Generate spawn profile for raid
- Persist follower raid-earned XP and common skill progression
- Save/load disk I/O for mod-owned JSON

**`FriendlyTeammateSocialCallbacks`** — Social List Patching

- Inject teammates into `/client/friend/list` response
- Patch `/client/profile/view` to detect teammates and apply custom UI
- Add teammates to `/client/friend/request/list/inbox`
- Intercept `/client/friend/delete` to clean up teammates too

**`FriendlyTeammateMatchCallbacks`** — Raid & Group Invite

- Auto-accept group invites for teammates
- Provide follower spawn profiles on demand
- Support custom health overrides for spawn
- Accept follower-progress persistence payloads using an `IRequestData` wrapper batch body for 4.x static-router compatibility

**`FriendlyPostRaidService`** — Post-Raid Handling

- Return items via mail system (sends to player, randomized NPC sender)
- Log raid outcomes (all escaped / partial / death)
- Send mail notifications to teammates about raid results
- NPC sender identity, message templates

**`FriendlyRecruitService`** — NPC Recruitment

- Queue defeated NPC candidates as pending friend requests
- Calculate recruit approval chance based on player level
- Convert recruit to permanent teammate via `FriendlyTeammateService`

## Key Limitations (Current In-Progress)

- Tactic persistence not yet implemented (`followerdetails` hardcoded to "Default")
- Voice/head customization from profile screen not yet implemented
- Invite/group flow still needs parity with pre-raid screen sequencing
- Teammate profiles remain mod-owned bot JSON, NOT full stock `SptProfile` accounts
- Post-raid item-return endpoint (`/singleplayer/returnitems`) disabled in client runtime
- Auto-join currently targets the PMC synthetic ready flow; scav/other flows are not treated as full teammate-managed entry points

---

# SAIN ADDON: Combat & Retention System

**Plugin ID:** `xyz.pit.fireteam.sainaddon`  
**Main entry:** `addon/SAINAddonPlugin.cs`  
**Bootstrap:** `addon/SAINRegroupBootstrap.cs`

## SAIN Integration Model

**Conditional Path:**

- Only active when SAIN (`me.sol.sain`) is installed at runtime
- Core plugin now owns the SAIN/addon split explicitly:
    - SAIN + addon present: follower SAIN combat path is used
    - SAIN present + addon missing: core disables SAIN takeover for followers and falls back to vanilla/core follower combat
- Shared bridge contract via `SainAddonBridge.cs` for core → addon communication

**Layer Stack in Combat:**

- Priority 73: `SAINFollowerCombatLayer` (custom SAIN squad replacement for followers, active in combat)
- Priority 71: `FollowerPatrolLayer` (vanilla follow, active out-of-combat)
- Mover handoff on layer switch via `SAINLayer.OnLayerChanged(...)`

## Combat Decision Routing

**`SAINFollowerCombatLayer` Evaluation Chain:**

1. **Regroup Command** — If `RegroupNearBoss` request active: → `SAINFollowerCombatRegroupAction` (8s grace while enemy context remains)
2. **Temporary HoldPosition Aggression** — If boss `HoldPosition` combat override is active: → boss-protection/regroup fallback (`SAINFollowerCombatDefaultBossAction`)
3. **Squad Calculator** — Dynamic decision scoring: → routing to specific action
4. **SAIN Fallback** — If available, use native SAIN squad decision (2s enemy grace)
5. **No Decision** — Layer exits unless an explicit calculator/fallback path produced a decision

**`SAINFollowerSquadDecisionCalculator` Priority Order:**

| Decision           | Condition                                                      | Action                                                        | Constraints                                                  |
| ------------------ | -------------------------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------ |
| **PushSuppressed** | Enemy suppressed by ally + vulnerable + close                  | `RushEnemyAction`                                             | Path < 75m (100m sprint), ammo > 50%                         |
| **GroupSearch**    | Search-party leader exists + ally searching same enemy         | Leader: SAIN `SearchAction`; others: `FollowBossSearchAction` | Coordinate hunt                                              |
| **Suppress**       | Ally in retreat from enemy                                     | `SuppressAction`                                              | Distance < 30-50m, ammo > 10-50%, no friendlies in fire lane |
| **Help**           | Ally engaging visible enemy                                    | `SearchAction`                                                | Distance < 30-45m, seen < 8s                                 |
| **Search**         | Enemy known but unseen                                         | `SearchAction`                                                | Seen within last 20s                                         |
| **Regroup**        | Boss-distance regroup in combat or fallback SAIN squad regroup | `DefaultBossAction` / `RegroupAction`                         | Boss-adjacent, avoid stacking                                |

**Action Types:**

- `SAINFollowerCombatRegroupAction` — Converge near boss with spacing
- `SAINFollowerCombatDefaultBossAction` — Default boss-protection/regroup action mode built on regroup movement
- `SAINFollowerCombatSuppressAction` — Fire at enemy with friendly-fire checks
- `SAINFollowerCombatFollowBossSearchAction` — Follow boss while searching
- SAIN native `SearchAction` / `RushEnemyAction` (resolved once with safe fallback)

## Enemy Retention & Acquisition

**Gate System** (if `EnableForcedEnemyRetention = true`):

- `SAINEnemyAcquireGatePatch` — Patches `SAINEnemyController.CheckAddEnemy`
- `ShouldAllowAcquire()` — Block attention-suppressed, boss, allied followers
- `ShouldAllowSameSideAcquire()` — Whitelist only same-side with hostile intent

**Hostile Intent Detection:**

- `FollowerCalcGoalEnemyAcquire.CandidateHasBossOrFollowerAsEnemy()` — Debounced per-bot via `HasDebouncedSameSideHostileIntent()`
- Prevents friendly-fire escalation

**Suppression Safety:**

- `SAINFollowerSuppressionSafety.IsFriendlyInSuppressionLane()` — Geometry cylinder check (0.55m radius)
- Blocks suppression if ally in fire path at any body height

**Patrol Readiness (Post-Combat Release):**

Stale Decision Grace Periods:

- Search: 3s
- SeekCover: 3s
- Retreat: 4s
- ShiftCover: 3.5s
- Solo layer + no decision: 2.5s

On Timeout:

- `ForceReleaseFollowerCombatState()` clears all combat context
- Clear SAIN search state, expire active enemies, invalidate known places
- Hard-release layer ownership
- Post-release grace: 1.5s before patrol activation
- Apply crouch nudge during grace (prevent sprint-thrash)

## SAIN-Specific Patches (13 Always + 1 Conditional)

**Always Applied:**

1. **`SAINFollowerSquadLayerDisablePatch`** — Disable SAIN native squad layer for followers
2. **`SAINFollowerAimSwayPatch`** — Aim sway tuning for followers
3. **`SAINFollowerHitAccuracyPatch`** — Block incoming hits from degrading follower aim (bypass `AimHitEffectClass.GetHit`)
4. **`SAINFollowerRecoilPatch`** — Recoil behavior tuning + `OnFollowerDismiss` listener for cache cleanup
5. **`SAINFollowerFriendlyFirePatch`** — Post-process SAIN shot blocking with follower-only shot-lane safety for boss/followers
6. **`SAINFollowerGroupTalkDirectionPatch`** — Voice callouts use boss look direction instead of squad leader
7. **`SAINFollowerTalkMutePatch`** — Mute repeated SAIN contact/lost-visual/clear chatter and route combat talk through the core frequency gate
8. **`SAINFollowerSearchCurrentEnemyLookPatch`** — Keep SAIN search steering oriented toward the current enemy near search endpoint
9. **`SAINFollowerDoorPatch`** — Suppress SAIN follower auto-close door choices
10. **`SAINFollowerPersonalityPatch`** — Clone `followerBigPipe` SAIN template per-follower, apply combat personality (Normal/Chad/GigaChad)
11. **`SAINFollowerSquadLeaderPatch`** — Force `IAmLeader = false` for all followers
12. **`SAINFollowerLowLightVisionPatch`** — Reduce low-light vision penalty via `EnemyGainSightClass.CalcTimeModifier`
13. **`SAINFollowerBushVisionPatch`** — Temporarily restore vanilla foliage/bush look settings while follower enemy look checks run

**Conditional (if `EnableForcedEnemyRetention`):**

14. **`SAINEnemyAcquireGatePatch`** + **`SAINFollowerEnemyRetentionService`** — Gate follower enemy acquisition when forced retention is enabled

## Lifecycle & Bridge Events

**Plugin Initialization:**

- `SAINAddonPlugin` registers callback on load, unregisters on unload
- `SAINFollowerRuntimeBridge` owns SAIN-typed patrol readiness implementation
- Bridge exposes: `IsReadyForPatrolAfterCombat(BotOwner)` + stale decision timer management

**Lifecycle Events:**

- `Modules.OnFollowerDismiss` — Fired when follower dismissed; addon hooks for recoil cache cleanup
- `ForceReleaseFollowerCombatState()` — Clear search state, expire enemies, reset decisions on attention/look
- `TryResetFollowerDecisionState()` — Soft reset for decision state without full layer release

**Integration Rule:**

- Prefer core → addon bridge calls over new core reflection probes
- Keep strict fail-fast/fail-closed behavior when SAIN installed but bridge unavailable

Files:

- `client/BigBrain/FollowerPatrolLayer.cs`
- `client/BigBrain/FollowerRequestLayer.cs`
- `client/BigBrain/Actions/FollowAction.cs`
- `client/BigBrain/Actions/HealAction.cs`
- `client/BigBrain/Actions/GestureCommandAction.cs`
- Additional peace actions in `client/BigBrain/Actions/*` (peace/look/gesture/etc.)

Behavior currently implemented:

- Registers `pitFireTeam.FollowerPatrol` layer for multiple brains (`PmcBear`, `PmcUsec`, `ExUsec`, `PMC`, `Assault`, `Knight`, etc.).
- Layer active only when:
    - bot is alive/active,
    - bot follows `pitAIBossPlayer`,
    - bot has no current enemy.
- Post-combat handoff to patrol is state-driven (no fixed timeout):
    - waits for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of forcing patrol after a fixed delay,
    - for SAIN-installed runtime, readiness is now resolved through addon-registered bridge callback (`SainAddonBridge.IsReadyForPatrolAfterCombat`),
    - when SAIN is installed and patrol bridge callback is unavailable, logic fails closed and logs explicit addon-missing bridge error once.
- Layer `Start()` performs recovery/reset:
    - pauses patrol data,
    - clears active request,
    - runs `FollowerRecovery.SoftReset`,
    - disposes current logic instance when possible.
- Action selection:
    - healing action while med work exists,
    - follow action otherwise,
    - includes out-of-combat reload handling.
- Healing action has timeout/cancel safety to prevent heal stuck states.

Follow movement:

- Follow logic is aligned toward old vanilla follower patrol style:
    - out-of-range chase toward leader,
    - in-range settle to cover/random nearby point using `GoToSomePointData`.
- Sprint run-stop mitigation:
    - `FollowerSprintStateDirectionPatch` modifies sprint-state direction under strict follower-chase conditions to avoid `Sprint -> Transition` thrash.
    - `FollowerSprintPatch` is enabled whenever core follower movement owns sprint (`!UseSainFollowerCombat`), including when SAIN is installed without the addon; SAIN-addon-owned combat keeps SAIN movement ownership.

Request/gesture movement:

- Registers `pitFireTeam.FollowerRequest` custom layer (priority `73`) above combat (`72`) and patrol (`71`).
- `FollowerRequestLayer` activates when follower has an active command in `BotFollowerPlayer`.
- `GestureCommandAction` handles:
    - `HoldPosition`: stop, crouch pose, periodic random look-around, no command timeout (persists until replaced/cleared).
    - `ComeCloser`: move to boss until close (about `1m`).
    - `MoveToPoint` (`There`): move to projected/navmesh-validated target point (walk-only), then brief look-around on arrival.
    - `LootGeneric` / `LootWeapon` command route:
        - boss phrase selects closest eligible follower to the targeted loot object,
        - follower is assigned as taker through `InteractableObjects.SetTaker(...)`,
        - follower runs `FollowerCommandType.TakeLootItem` in `GestureCommandAction` (move to loot point + inventory transfer attempt),
        - BE item-return post is disabled; loot tracking remains local-only for now.
    - `OpenDoor` command route:
        - boss phrase selects closest eligible follower to the targeted door,
        - follower is assigned as opener through `InteractableObjects.SetOpener(...)`,
        - follower runs `FollowerCommandType.OpenDoor` in `GestureCommandAction` (move to door + `DoorOpener.Interact(..., Open)`),
        - opener/taker state is cleared when command clears, including combat-entry handoff.
    - `Regroup` (`EPhraseTrigger.Regroup`):
        - vanilla regroup is implemented and active for no-SAIN or out-of-combat cases,
        - SAIN combat regroup is executed through addon `SAINFollowerCombatLayer` -> `SAINFollowerCombatRegroupAction`,
        - regroup converges to boss-near cover/random point (not exact boss position) and supports boss-movement reanchor.
    - Regroup ignore/interruption safeguards:
        - ignored when follower is healing or already close enough (`~8m` nav-path distance on same level),
        - interrupted/released when follower can see and shoot enemy, needs heal, or must avoid danger (grenade/BTR),
        - vanilla path releases control when SAIN combat regroup route becomes valid; SAIN path releases control when combat route is no longer valid,
        - interrupted by being hit, follower death, attention reset (`EPhraseTrigger.Look`), or replacement by a newer command,
        - on successful regroup arrival follower says `EPhraseTrigger.OnPosition`.
- Boss phrase commands handled directly by `AIBossPlayer`:
    - `EPhraseTrigger.HoldPosition` applies a temporary `0%` effective combat-aggression override to targeted followers; it is not a persistent profile aggression change.
    - `EPhraseTrigger.Gogogo` clears that temporary override and returns each follower to its saved aggression.
    - `BotReceiverPhraseOverridePatch` suppresses vanilla receiver handling for `Stop`, `HoldPosition`, and `Gogogo` on followers so the boss command path owns the behavior.
- Gesture visibility requirements:
    - `HoldGesture` and `ThereGesture` require follower to see boss gesture target (`head` or `torso` visibility; either is enough).
    - `ComeWithMeGesture` requires both directions:
        - boss can see follower gesture target (`head` or `torso`),
        - selected follower can see boss (`head` or `torso`).
- Command sequencing details:
    - If `ComeCloser` was issued while `HoldPosition` was active:
        - bot approaches,
        - then resumes hold (unless interrupted by a new command/clear event).
    - If `ComeCloser` was not issued from hold:
        - bot performs a short arrival pause/look-around, then clears command.
    - If a new `There` is issued while bot is in arrival look-around:
        - bot immediately starts moving to the new point.
    - `Hold` / `Come` interrupt and replace `There`/arrival-look behavior.
- Contact look pause:
    - On enemy-contact orders (`OnRepeatedContact` / custom `OverThere`), command random look logic is paused for ~`2-4s` so bots keep contact orientation.
- Gesture routing:
    - Custom `OverThere` is handled separately from `There`.
    - A short suppression guard prevents immediate `There` echo from being treated as move-to-point after custom `OverThere`.
- Commands are cleared on:
    - `FollowMe` / `Cooperation`,
    - `Look` (attention),
    - bot being hit,
    - command timeout / invalid execution state.

## 0) Project Context & References

- Old plugin codebase: see `LOCAL.md` reference paths.
- Old client reference (3.11): see `LOCAL.md` reference paths.
- New client reference (4.x): see `LOCAL.md` reference paths.
- SAIN plugin reference: see `LOCAL.md` reference paths.

**Positioning:** `pitFireTeam` is both a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment and an alternative plugin implementation with new BigBrain-native follower layers/actions.

---

# Command/Gesture IDs & Practical Entry Points

## Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
    - `EPhraseTrigger.Stop` — Hold position without crouch, broadcast to nearby followers (25m range or looked-at follower)
    - `EPhraseTrigger.HoldPosition` — Temporary combat hold/aggression override; acts like `0%` aggression until combat ends or `Gogogo` clears it
    - `EPhraseTrigger.Gogogo` — Clears the temporary combat hold/aggression override and restores each bot's persisted aggression
- Phrase broadcast commands (new):
    - `STOP` (from `EPhraseTrigger.Stop`): Sets all targeted followers to `HoldPosition(infinity, crouch: false)`, released when follower moves >25m from boss or on new command
    - `HOLDPOSITION` (from `EPhraseTrigger.HoldPosition`): Applies temporary combat aggression override to targeted followers without changing persisted profile aggression
    - `GOGOGO` (from `EPhraseTrigger.Gogogo`): Clears temporary combat aggression override
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- BigBrain core logic:
    - `client/BigBrain/FollowerPatrolLayer.cs` — Follow patrol out-of-combat
    - `client/BigBrain/FollowerRequestLayer.cs` — Active command execution
    - `client/BigBrain/Actions/FollowAction.cs` — Chase and settle movement
    - `client/BigBrain/Actions/GestureCommandAction.cs` — Command pipeline (hold/there/come/loot/door/regroup)
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase → follower conversion
    - `client/Components/BotFollowerPlayer.cs` — Follower state container
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs` — Boss command handling
- SAIN combat addon (follower combat layer):
    - `addon/SAINFollowerCombatLayer.cs` — Combat decision routing
    - `addon/SAINFollowerSquadDecisionCalculator.cs` — Priority-based decision scoring
    - `addon/SAINFollowerCombatRegroupAction.cs` — Combat regroup execution
    - `addon/SAINFollowerCombatSuppressAction.cs` — Fire support logic
    - `addon/SAINFollowerCombatFollowBossSearchAction.cs` — Coordinated search

---

# Known Issues & Tracking

See the local bug-tracker file listed in `LOCAL.md`.

**Currently Active Risk Areas:**

- SAIN post-combat idle/freeze behavior for followers
- Enemy propagation consistency across all followers
- Reaction-system regression (early detection):
    - Hard guards in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `BotGroupReportEnemyPatch` prevent followers from marking boss/followers as enemies
    - Still active risk area for hearing/voice/bullet reaction paths
- Follow-up behavior when player is hit out of combat
- Follower death reaction:
    - Nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`
    - Corpse-position visibility checked up to ~60s if nobody saw death

---

## DEBUGGING & INVESTIGATION PRACTICES

- treat bugs tracked separately as SAIN and vanilla categories
- always spend time checking SAIN and client sources at the beginning of the session to get proper context
- prefer to check client sources first when some method, class, or property is not clear, rather then making assumptions
- Debug console command: `fs_spawnfollower` spawns one follower in-raid using game-side `ISession.LoadBots` profile flow (NOT BE-dependent)

## Bugs

Bugs are tracked in the local bug-tracker file listed in `LOCAL.md`.

- `BotGroupAddEnemyPatch`
- `BotGroupReportEnemyPatch`
- `BotGroupUsecEnemyPatch`
- `BotGroupCalcGoalPatch`
- `BotControllerEnemyPropagationSafetyPatch`
- `BotMemoryDamagePatch`
- `ExUsecBrainHitPatch`
- `BotOwnerIsFolowerPatch`
- `BotOwnerManualUpdatePatch`
- `BotOwnerActivatePatch`
- `SessionLoadBotsEnglishVoicePatch`
- `LootPatrolActiveLayerListPatch`
- `LootPatrolDecisionBypassPatch`
- `AdvAssaultTargetFollowerGuardPatch`
- `PatrolDataFollowerUpdateGuardPatch`
- `AvoidDangerFollowerGuardPatch`
- `AICoreAgentUpdatePatch` (logs/rethrows update exceptions)

Movement:

- `FollowerSprintPatch` (conditional: whenever core follower movement owns sprint, including SAIN-without-addon)
- `FollowerSprintStateDirectionPatch`

Recruit/request:

- `BotReceiverFollowMeRecruitPatch`
- `FollowRequestPatch`
- `HoldRequestPatch`
- `OpenDoorRequestPatch`
- `BotReceiverGestureOverridePatch`

Spawn/raid:

- `BotsControllerPatch`
- `BotsControllerStopPatch`
- `LocalGameCleanupPatch`
- `LocalGameCtorPatch` (patched via `harmony.CreateClassProcessor(...).Patch()`)
- `BotsEventsControllerSpawnPatch`
- `BossSpawnWaveManagerClassPatch`
- `RaidStartPatch`

Items/equipment:

- `UnlootableComponentPatch`
- `ModRaidModdablePatch`
- `ItemSpecificationPanelPatch`

Combat/hearing/talk:

- `BotTalkTrySayPatch`
- `BotTalkSayPatch`
- `GrenadeThrowPatch`
- `GrenadeTryThrowSafetyPatch` (from `GrenadeThrowPatch.cs`)
- `BulletImpactPatch`
- `HearingSensorPatch`
- `FootstepSoundPatch`
- `PlayerSayPatch`

AI data / command UI:

- `AIDataContructPatch`
- `QuickPanelPatch`
    - cooperation entry is shown for any alive, non-follower AI target (still subject to recruit acceptance checks elsewhere)
- `GestureMenuPatch`
- `GestureMenuAvailablePhrasesPatch`
- `EPhraseTriggerPatch`
- `PlayPhraseOrGesturePatch`
    - intercepts only custom phrase IDs and skips interception when the action is a real player gesture (`GClass3937.IsPlayerGesture(actionId)`), so vanilla gestures are not hijacked

SAIN integration:

- `SAINPatch.PatchSAINIfInstalled(harmony)` applies selective SAIN behavior patches when SAIN assembly is present.
- SAIN combat follower integration is implemented in a separate addon DLL:
    - addon project: `addon/pitFireTeam.SAINAddon.csproj`
    - plugin ID: `xyz.pit.fireteam.sainaddon`
    - runtime path registers custom `SAINFollowerCombatLayer` at priority `73`.
    - this layer replicates SAIN squad-combat decision routing for followers, but re-centers behavior around player boss leadership (instead of vanilla SAIN squad leader ownership).
    - follower action mapping currently routes to:
        - `SAINFollowerCombatRegroupAction`,
        - `SAINFollowerCombatSuppressAction`,
        - `SAINFollowerCombatFollowBossSearchAction`,
        - SAIN solo search/rush action types resolved once from SAIN assembly (`SearchAction` / `RushEnemyAction`) with safe fallback.
- Core plugin validates SAIN/addon presence at runtime:
    - if SAIN is installed but addon is missing, core plugin logs explicit error and SAIN follower combat-layer integration is disabled.
- Shared bridge contract is active for core->addon SAIN readiness handoff:
    - `client/Modules/SainAddonBridge.cs` exposes delegate contract.
    - `addon/SAINAddonPlugin.cs` registers/unregisters bridge callback during addon lifecycle.
    - `addon/SAINFollowerRuntimeBridge.cs` owns SAIN-typed patrol readiness implementation.
- Integration rule for new work:
    - for SAIN-dependent follower behavior, prefer core->addon bridge calls over new core reflection probes.
    - keep strict fail-fast/fail-closed behavior when SAIN is installed but required addon bridge callback is unavailable.
- SAIN layers use their own mover handoff/control path while active (notably in combat):
    - `SAINLayer.OnLayerChanged(...)` stops built-in mover when entering SAIN layer and handles mover/navmesh handoff on layer switch.
    - treat SAIN combat movement issues as SAIN-layer/mover behavior first, then plugin command-layer behavior.
- SAIN addon currently applies follower-focused combat/retention patches from `addon/SAINRegroupBootstrap.cs`:
    - addon is currently disabled for the initial release path; treat the remaining bullets here as deferred addon notes, not release-authoritative behavior,
    - `SAINFollowerFriendlyFirePatch` (for follower shooters, post-processes SAIN shot blocking with core `FollowerShotSafety` lane checks against the player boss and other followers),
    - `SAINFollowerGroupTalkDirectionPatch` (uses boss look direction for directional enemy talk checks),
    - `SAINFollowerTalkMutePatch` (mutes repeated SAIN contact/lost-visual/clear chatter and applies the core combat-talk frequency gate),
    - `SAINFollowerSearchCurrentEnemyLookPatch` (keeps SAIN search steering oriented toward the current enemy near search endpoint),
    - `SAINFollowerDoorPatch` (suppresses SAIN follower auto-close door choices),
    - `SAINEnemyAcquireGatePatch` + `SAINFollowerEnemyRetentionService` (when `SAINAddonToggles.EnableForcedEnemyRetention = true`),
    - `SAINFollowerPersonalityPatch` (injects a per-follower clone of SAIN `followerBigPipe` bot settings as the follower combat template and aligns SAIN difficulty modifier to that template),
    - `SAINFollowerSquadLeaderPatch`,
    - `SAINFollowerLowLightVisionPatch`,
    - `SAINFollowerBushVisionPatch`.
- Follower enemy acquisition split:
    - shared forward-scan acquire assist now lives in core and is triggered from `client/Patches/BotGroupCalcGoalPatch.cs` by patching `BotCalcGoal.CalcGoalForBot()` directly,
    - core handler lives in `client/Modules/FollowerCalcGoalEnemyAcquire.cs`,
    - this path is runtime-neutral and now assists both vanilla and SAIN follower enemy pickup when vanilla goal calculation runs,
    - SAIN addon only keeps the SAIN-specific `CheckAddEnemy` gating path (`SAINEnemyAcquireGatePatch` + `SAINFollowerEnemyRetentionService` same-side/ally filtering),
    - old addon-only wrapper `addon/SAINCalcGoalPatch.cs` was removed; do not describe current addon retention as using that file.
- Follower SAIN tuning rule:
    - current stable path prefers SAIN template settings (`followerBigPipe`) over follower-specific aim/look compensation patches.
    - follower-specific aim sway, hit-accuracy, recoil, low-light, and bush-vision patches are currently wired by bootstrap; use `SAINFollowerPersonalityPatch.ApplyFollowerTemplateFineTuning(...)` for future template-level tuning.
- SAIN attention/release reset now clears stale search state through the addon bridge:
    - `SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState(...)` and `TryResetFollowerDecisionState(...)` clear `SAINSearchClass` active target/path and invalidate `EnemyKnownPlaces` for all tracked SAIN enemies before resetting decisions/layer state.
- Legacy `SAINDecisionRegroupPatch.cs` remains in addon source but is currently not wired by bootstrap.

## 5) Safety/Crash Guards Added

- LootPatrol active-layer guard:
    - `client/Patches/LootPatrolSafetyPatch.cs`
    - `LootPatrolActiveLayerListPatch` strips vanilla LootPatrol (`GClass117`) from BigBrain active layer list for followers before layer update.
    - `LootPatrolDecisionBypassPatch` prevents LootPatrol decision execution when follower state is active.
- Grenade throw safety:
    - `client/Patches/GrenadeThrowPatch.cs` includes null-safe guard for `GClass274.UpdateTryThrow`.
- Player say/hearing null guards:
    - `client/Patches/HearingSensorPatch.cs` hardened against null bot/follower references.
- Enemy propagation guard:
    - `client/Patches/BotGroupPatch.cs` (`BotControllerEnemyPropagationSafetyPatch`)
    - validates `AddEnemyToAllGroupsInBotZone(...)` player refs and skips invalid propagation calls that can occur after debug/out-of-band spawns.
- Interaction/visibility null guards:
    - `client/Modules/InteractableObjects.cs`
    - hardened seen-enemy and boss-state checks against null/missing player/bot references.
- Vanilla follower/update crash guards:
    - `client/Patches/FollowerVanillaSafetyPatch.cs`
    - `PatrolDataFollowerUpdateGuardPatch` prevents vanilla follower patrol from running with a missing boss/player-follow backing object and attempts boss-link recovery through `BossPlayers`.
    - `AvoidDangerFollowerGuardPatch` blocks vanilla `GClass48.ShallUseNow()` for followers when required danger subsystems are not initialized, preventing repeated `AvoidDanger` NREs.

## 6) Teleport / Utility

- Teleport key action (`_BotTeleport`) now:
    - computes NavMesh-valid spread spots around player,
    - enforces spacing from player and between followers,
    - avoids overlap pileups with multiple followers.

## 7) Status/Debug Notes

- `PingTeamates` enemy marker/status timing corrected:
    - uses `Time.time - PersonalLastSeenTime` for recency.
- `PingTeamates` callout throttling:
    - directional voice callouts are now throttled to once every `15s` across pings,
    - ping radio/location sound and triangle marker still update every valid ping.
- TeamStatus/Look command burst handling:
    - command handling was debounced to reduce repeated heavy work during rapid player phrase spam.
- SAIN-friendly-fire path:
    - current follower-only SAIN override uses core `FollowerShotSafety` lane geometry against the player boss and other followers.
    - it post-processes SAIN friendly-fire status updates rather than delegating directly to vanilla `ShootData.CheckFriendlyFire(...)`.
- SAIN follower combat template:
    - recruited followers now use a per-bot cloned copy of SAIN `followerBigPipe` settings as their combat template.
    - the template is applied through addon-owned SAIN info/file-settings injection instead of follower-specific aim-target/random-look/hit-accuracy patching.
    - `addon/SAINFollowerPersonalityPatch.cs` is the single entry point for future follower SAIN fine-tuning on top of that template (`ApplyFollowerTemplateFineTuning(...)`).
- SAIN stale search cleanup:
    - stale `EnemyKnownPlaces` / `SAINSearchClass` state was identified as the source of repeated `EPhraseTrigger.Clear` / `LostVisual` after combat or attention.
    - addon release/reset bridge now explicitly clears active SAIN search state and invalidates known places during follower combat-state release/reset.
- `PingTeamates` GUI path optimization:
    - per-frame draw loops now use index-based iteration instead of delegate-based `List.ForEach`.
    - bot status text reuses a single `StringBuilder` instance instead of allocating per bot per frame.
    - tracked body-part iteration uses a static array instead of `Enum.GetValues(...)` allocations.
- SAIN bridge debug noise reduction:
    - release/default guidance should assume no addon enemy-bridge debug logging is enabled or required.
- SAIN navigation investigation result:
    - SAIN does not currently have one broad active non-mover "navigation fix" patch that generically recovers stuck bots.
    - active navigation-adjacent behavior is split across:
        - `Patches/MovementPatches.cs` global movement-context patches (`MovementContextIsAIPatch`, `CanBeSnappedPatch`) and mover-manual-update patches,
        - door handling outside the mover (`Classes/PlayerManager/Doors/DoorHandler.cs`, `Classes/Bot/Doors/DoorOpener.cs`),
        - `SAINBotUnstuckClass`, which contains vault/teleport unstuck logic but its coroutine body currently has the core unstuck calls commented out.
    - practical implication: treat SAIN door handling and SAIN layer/mover handoff as active navigation influences first; do not assume SAIN has an active generic unstuck system currently rescuing follower navigation.
- Several debug/trace patches were iterated during movement work; current runtime path is focused on minimal active tracing.
- Request command logs were reduced/removed from `AIBossPlayer` (`[Req] Hold/There/ComeWithMe ...`) to keep runtime logs cleaner.
- Debug console command:
    - `fs_spawnfollower`
    - available in-raid, spawns one follower for the player side.
    - profile generation uses game-side bot profile flow (direct `ISession.LoadBots` path via game profile/session objects), then injects into `BotCreationDataClass.CreateWithoutProfile(...)`.
    - bot spawner `InSpawnProcess` is incremented/decremented with failure rollback to avoid breaking later vanilla bot spawns.
    - fallback safe profile request may be used if requested side/role generation fails.
- English BEAR voice assignment is applied at profile-load time:
    - `SessionLoadBotsEnglishVoicePatch` patches `ProfileEndpointFactoryAbstractClass.LoadBots`
    - each returned `Profile` is processed by `BotOwnerActivatePatch.ApplyEnglishVoiceForProfile(...)`
    - this is the active runtime path for voice replacement (instead of late activation-only mutation)

## 8) Known Open Issues

See the local bug-tracker file listed in `LOCAL.md`.

Examples currently tracked there:

- SAIN post-combat idle/freeze behavior for followers.
- Enemy propagation consistency across all followers.
- Reaction-system regression (SAIN/vanilla reaction work):
    - after changes made to work around `EnemyController.IsEnemy` timing gaps for early follower reaction, a regression was observed where followers could incorrectly mark the player as enemy.
    - hard guards were added in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `client/Patches/BotGroupPatch.cs` (`BotGroupReportEnemyPatch`) to prevent followers from adding the boss player or other followers as enemies.
    - more testing is still needed; treat this as an active risk area when changing reaction logic (`FollowerAwareness`, hearing/voice/bullet paths).
- Follow-up behavior when player is hit out of combat.
- Follower death reaction:
    - when a follower dies, nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`;
    - if nobody saw death, corpse-position visibility is checked for up to `~60s` and reaction can still trigger.

## 9) Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- Follow layer/action logic:
    - `client/BigBrain/FollowerPatrolLayer.cs`
    - `client/BigBrain/Actions/FollowAction.cs`
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs`
    - `client/Components/BotFollowerPlayer.cs`
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs`
- SAIN combat addon (follower combat layer):
    - `addon/SAINFollowerCombatLayer.cs`
    - `addon/SAINFollowerSquadDecisionCalculator.cs`
    - `addon/SAINFollowerCombatRegroupAction.cs`
    - `addon/SAINFollowerCombatSuppressAction.cs`
    - `addon/SAINFollowerCombatFollowBossSearchAction.cs`

## 10) Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## 11) SAIN Vision/Enemy Notes (2026-03-01)

- Detailed investigation note is recorded in:
    - `docs/sain-vision-enemy-pipeline-2026-03-01.md`
- Key result:
    - SAIN enemy retention depends on internal `EnemyKnown` + `LastKnownPosition` + active checks.
    - SAIN can clear current goal enemy quickly if any of those conditions drop, even after short visual contact.
    - SAIN also patches vanilla `EnemyInfo.HaveSeen/ShallKnowEnemy*` and `LookSensor` flow, so behavior can diverge sharply from vanilla pickup logic.

Update (2026-03-06):

- Enemy-contact reliability in SAIN is now enforced with a follower-only retention bridge:
    - `addon/SAINEnemyAcquireGatePatch.cs` gates `SAINEnemyController.CheckAddEnemy` for followers.
    - historical note: older investigation referenced `SAINCalcGoalPatch`, but that wrapper was removed; current core/addon split is described above and should be treated as authoritative instead of this older note.
    - calc-goal scans are rate-limited per follower and scaled by active follower count.
    - enemy candidates are filtered to avoid boss/followers/friendly bot types and side-safe cases unless hostile intent is detected.
    - when a follower commits an enemy through this path, the service propagates that enemy to sibling followers.
    - Attention/Look suppression is honored through `client/Modules/FollowerEnemyEnforceSuppression.cs` and the retention service (`blocked_attention_suppression` path).
    - Forced retention remains toggle-controlled via `addon/SAINAddonToggles.cs` (`EnableForcedEnemyRetention`).
- Follower proficiency was increased (hard+ oriented) through SAIN addon patches:
    - `addon/SAINFollowerPersonalityPatch.cs` raises follower detection/reaction and tightens aim behavior (higher `GainSightCoef`, higher hearing/visible multipliers, faster precision, lower accuracy/scatter multipliers).
    - `addon/SAINFollowerHitAccuracyPatch.cs` bypasses SAIN `AimHitEffectClass.GetHit` aim-affection for followers so incoming hits do not degrade follower aim.
    - `addon/SAINFollowerLowLightVisionPatch.cs` reduces low-light time-to-spot penalty for followers by post-processing SAIN time vision modifier in `EnemyGainSightClass.CalcTimeModifier`.
