# My Squad Current State Review

Date: 2026-07-30

## Goal

Document the current verified implementation of the `My Squad` experience as it exists today in `pitFireTeam`, split into:

1. `Roster`
2. `Mode`
3. `Settings`
4. `Profile Screen`

This is a current-state review, not a target design doc. It should be read alongside:

- [Core architecture](Architecture.md) for current ownership and entry points
- `docs/Loadout-Management.md` for the dedicated loadout-management mode behavior and implementation status
- `docs/Team-Escape.md` for player-death squad escape and roster refresh behavior after escape outcomes

## High-Level Shape

`My Squad` is not one single screen implementation.

Today it is split across two UI hosts:

- `MatchMakerSideSelectionScreen` in a custom "squad mode" for the `Roster`, `Settings` and `Mode` tabs
- `OtherPlayerProfileScreen` for the selected teammate `Profile Screen`

That means the current flow is:

1. main menu `My Squad` button
2. open stock side-selection screen in squad mode
3. hide native PMC/Scav selection widgets
4. inject pitFireTeam roster/mode/settings panels
5. open teammate profile from roster tile
6. patch stock other-player profile into teammate management UI
7. return back into `My Squad`

Authoritative files:

- `client/Patches/MenuScreenSquadControlPatch.cs`
- `client/Modules/SquadSideSelectionFlow.cs`
- `client/Patches/MatchMakerSideSelectionScreenPatch.cs`
- `client/Components/SquadControlMenuUi.cs`
- `client/Components/SquadControlMenuUi.Roster.cs`
- `client/Components/SquadControlMenuUi.Mode.cs`
- `client/Components/SquadControlMenuUi.Settings.cs`
- `client/Components/SquadControlMenuUi.ContextMenu.cs`
- `client/Components/SquadControlMenuUi.Backend.cs`
- `client/Patches/OtherPlayerProfileScreenPatch.cs`
- `client/Patches/OtherPlayerProfileScreenPatch.LoadoutUi.cs`
- `server/Resources/lang/*.json`
- `server/Services/FriendlyLanguageService.cs`

## Entry Flow

### Main menu entry

`MenuScreen.Show(...)` is patched so `SquadControlMenuUi` is attached to the live `MenuScreen`.

The mod clones the stock player button to create a new `My Squad` button and positions it in the same left-side menu stack. The button calls `SquadSideSelectionFlow.Open()`.

Verified behavior:

- the button is a cloned `DefaultUIButton`, not a custom prefab
- it uses a custom icon and localized title
- pitFireTeam declares Menu Overhaul as a soft BepInEx dependency, so when both mods are installed the `My Squad` button is injected after Menu Overhaul finishes rebuilding the main-menu layout
- when Menu Overhaul (`com.moxopixel.menuoverhaul`) is loaded, the icon uses `squad-inverse.png`; if that asset is missing, it falls back to `squad.png`
- reconnect/minimized menu states re-sync its visibility

### Squad mode host

`SquadSideSelectionFlow.Open()` uses reflection into `MainMenuControllerClass.method_44()` to open the stock `MatchMakerSideSelectionScreen`.

Before the screen opens it:

- marks `SquadModeActive = true`
- captures the current matchmaker group snapshot
- hides the side-selection alpha label
- enables temporary `PlayerModelView.Show(...)` suppression so the stock side-selection player model views do not render

When squad mode closes or is aborted it:

- clears squad-mode flags
- restores the alpha label
- clears the opening group snapshot

### Side-selection patching

`MatchMakerSideSelectionScreen.Show(...)` is patched to detect `SquadModeActive`.

In squad mode it:

- hides native side-selection widgets such as PMC/Scav panels, health/random controls, descriptions, and stock model views
- rewrites the main caption to `My Squad`
- spawns three stock-style animated tabs by cloning Ragfair toggles, ordered left to right:
    - `Roster`
    - `Mode`
    - `Settings`
- injects the pitFireTeam panels into the live side-selection screen transform
- rewires the stock back button so squad-mode back always exits to root and disables squad mode

On close it restores the hidden stock elements and retracts the injected panels.

## Localization

`My Squad` UI text is now loaded through the shared pitFireTeam language model.

Current behavior:

- client reads the active game language through `SharedGameSettingsClass`
- client posts the normalized locale plus embedded English fallback JSON to `/singleplayer/pitfireteam/lang`
- server creates or repairs `server/Resources/lang/en.json` from the embedded English when it is missing, corrupted, or missing keys
- server returns `server/Resources/lang/<locale>.json` merged with the editable English fallback
- built-in client fallback comes from `EmbeddedEnglishLanguageProvider`
- language is checked periodically at runtime and reloaded when the game language changes

Verified bundled language resources today:

- `en`
- `ru`

## Part 1: Roster

### What the roster tab is

The roster tab is the main `My Squad` landing tab. It is built by `SquadControlMenuUi` and hosted inside the squad-mode side-selection screen.

When stock trader chrome is available, the roster uses a cloned trader-card shell as its main panel background. If that template is not available, the code falls back to a plain custom panel.

### Data source

Roster entries are loaded from:

- `GET /singleplayer/pitfireteam/teammates`

Each tile is built from backend teammate data:

- `Aid` / account id
- social member id
- nickname
- level
- auto-join enabled flag

The roster is rebuilt on first injection and on explicit refresh requests. It also supports lighter tile-only refreshes for specific account ids after profile-side edits.

Roster entries use ascending profile registration time, then account id. Newly accepted raid recruits receive the acceptance timestamp so their captured bot registration date cannot place them ahead of existing squadmates. Opening a pending recruit preview does not change this timestamp. Previously accepted profiles retain their saved ordering.

### Tile composition

Each roster entry is a runtime-created tile containing:

- background image + hover styling
- diagonal corner overlay
- portrait area
- level display
- nickname label
- delete button
- auto-join badge
- in-group badge

For raid-recruited members with recorded `RecruitmentGearPrice` metadata (including a zero value), the root `UI.Image` on `pitFireTeam_RosterTile_<accountId>` uses faction RGBA colors: USEC `(0.025, 0.547, 1, 0.408)` and BEAR `(1, 0.287, 0, 0.408)`. Manually added members, legacy unmarked members and other sides keep the original neutral background. Hover/press styling restores the appropriate background and nickname color when the pointer leaves or the card is disabled. Cached cards therefore return without a stale highlight after closing the roster, switching tabs or viewing a member profile.

Portraits are loaded asynchronously and sequentially. The queue fetches `GetOtherPlayerProfile(accountId)` and then uses `PlayerIconImage.SetPresetIcon(...)`.

Important implementation details:

- portrait loading is deferred through a queue to avoid blasting the UI all at once
- loading indicators are tracked per account id
- tile rebuilds are versioned so stale portrait callbacks do not paint onto a replaced tile

### Empty state and add flow

If there are no teammates, the roster shows:

- an empty-state label
- a centered `+ Add teammate` button

The add button calls:

- `AddTeammateCreationFlow.Start(SquadSideSelectionFlow.Open)`

So teammate creation still reuses the stock account appearance flow, and successful completion returns back into `My Squad`.

#### Hiring preview

After choosing a head, voice and nickname, `NEXT` prepares a server-owned candidate and opens `OtherPlayerProfileScreen` in hiring-preview mode. This is the final step before the teammate is added. The layout is:

```text
                                               BACK
                    REGENERATE
              [rotatable character preview]
                [nickname and experience]
                 GEAR PRICE: … ₽
              CANCEL     ADD TEAMMATE
                  ADD WITH NO GEAR
```

The native faction emblem, nickname and experience remain with the model. Prestige, level and account-category icons are hidden. The former standalone nickname above the model is replaced by `REGENERATE`. Price and action rows use compact spacing, with the no-gear button centered beneath the first two buttons.

| Action | Behavior |
|---|---|
| `REGENERATE` | Generates another profile and gear quote while retaining the chosen nickname, head and voice. Successful preparation replaces the previous pending candidate without charging or adding anyone. |
| `BACK` | Returns to head/voice/name selection with the choices retained. |
| `CANCEL` | Cancels the pending addition and returns to My Squad without payment. |
| `ADD TEAMMATE` | Pays the quoted gear price from unlocked stash roubles and saves the exact previewed candidate. Refreshes the live stash and roster. |
| `ADD WITH NO GEAR` | Adds the same candidate for free, preserving the knife and permanent equipment/identity slots while removing the kit and ordinary pocket/secure-container supplies. |

Each manually generated candidate receives a fresh 6Kh5 Bayonet; knives are never charged. The no-gear action is shown only when the server advertises support. Both addition actions wait until the displayed model has loaded, and a failed regeneration keeps them disabled until another preview succeeds. Controls are disabled while requests are in progress.

If the server finds insufficient spendable stash roubles, the vanilla message-window popup shows the localized insufficient-funds message. No payment or teammate is created, and the preview stays open. Pending candidates are not friends or squad members. Quotes expire after 30 minutes; confirmation also rejects a changed loadout mode or outdated pricing version.

The server owns the price, candidate, funds check and receipt. See [Loadout Management](Loadout-Management.md#teammate-addition-cost) for the complete charged-slot list, price formula, no-gear retention and payment-recovery contract.

The preview retains the stock drag-rotation input and model-loading spinner. Fetching the quote/profile uses the normal preloader. The closed appearance controller is removed from the preview's return history so Back can open a fresh appearance session without first reopening and closing the stale faction-selection state.

Source: [AddTeammateCreationFlow](../client/Modules/AddTeammateCreationFlow.cs), [AddTeammateHeadSelectionPatch](../client/Patches/AddTeammateHeadSelectionPatch.cs) and [TeammateHiringPreview](../client/Modules/TeammateHiringPreview.cs). Hiring state and temporary profile-screen changes are restored when the preview closes.

### Tile interactions

Left click:

- opens teammate profile via `ItemUiContext.Instance.ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile)`

Right click:

- opens a context menu with:
    - `Invite to group` or `Remove from group`
    - `View profile`
    - `Auto join: On/Off`

The context menu prefers cloning the stock matchmaker/simple-context-menu template when available, and falls back to a custom runtime menu otherwise.

### Group integration

Roster group state is live-linked to `MatchmakerPlayerControllerClass`.

Supported group actions:

- invite teammate to group
- remove teammate from group through the stock confirmation UI
- detect in-group state for badges
- show toast feedback for accepted/failed invite and removal flows

Canceling the stock `Remove from group` confirmation is treated as a normal no-op: the teammate stays in the group and no removal-failed toast is shown.

The roster also uses the opening group snapshot from `SquadSideSelectionFlow` so group badges can stay coherent while the side-selection screen is being opened.

### Auto-join integration

Auto-join toggles post to:

- `POST /singleplayer/pitfireteam/teammate/autojoin`

On success the roster updates the badge immediately and also updates `TeammateAutoJoinRuntime` suppression state locally.

### Delete flow

Delete is a modal confirmation overlay on top of the roster tab.

The confirmation includes the stored deletion fee for raid recruits. Manual hires and legacy unmarked members keep the ordinary free-deletion prompt. Both include **The equipped items will be mailed via service.** immediately above **This cannot be undone**; the panel height accommodates the added line. Removing either member type returns the current saved gear through the courier, including attachments and contents. See [Raid-Recruit Deletion Fee](Loadout-Management.md#raid-recruit-deletion-fee) for how this amount is recorded and preserved.

The roster posts to `/singleplayer/pitfireteam/teammate/delete` and closes the confirmation and rebuilds the roster after the server confirms removal. Item-bearing responses use EFT JSON converters to retain stack counts and locations; shared stash refresh identifies the active stash root instead of relying on server array order. A subsequent live stash or friends-list refresh error cannot undo confirmed removal or keep its confirmation open; a stash refresh failure displays the existing restart-before-inventory-changes message. The native social `RemoveFromFriendsList` path is intercepted for teammates to use the same payment flow and fee confirmation; ordinary friends retain native removal. Insufficient funds use the vanilla message window and leave the member present. A shared client busy guard prevents duplicate in-flight submissions, and durable server/player receipts prevent repeat charges or duplicate courier kits after a lost response. If removal commits but delivery saving fails, a localized message explains that the member was removed and delivery remains pending; reopening My Squad retries it. A failed SPT save hash may require a server restart before the profile can be verified. See [equipment returns](Loadout-Management.md#equipment-return-on-member-removal) for item selection and recovery.

Source: [TeammateDeletion](../client/Modules/TeammateDeletion.cs) and [server deletion service](../server/Services/FriendlyTeammateService.Deletion.cs).

### Current roster limitations

- the roster itself does not contain a right-side detail pane; teammate detail still jumps into `OtherPlayerProfileScreen`
- portrait loading is sequential and intentionally delayed, so large rosters are stable but not instant
- paid deletion needs the active player inventory controller to refresh the live stash

## Part 2: Settings

### What the settings tab is

The settings tab is another `SquadControlMenuUi` panel injected into the same squad-mode side-selection host.

It is not a stock `SettingsScreen` controller. It is a custom panel that tries to clone stock controls where possible.

### Panel construction

The settings shell is sized to match the roster shell height so tab switching feels like one coherent screen.

The panel builds:

- a scrollable viewport
- section headers
- one row per config entry

Where possible it clones stock EFT controls from `GameSettingsTab`:

- `UpdatableToggle` for booleans
- `NumberSlider` for integer ranges

If a stock template cannot be found, it falls back to basic runtime-created controls.

### Current editing model

Important difference from the March design investigation:

- the current `Settings` tab writes directly to live BepInEx config entries
- it saves immediately through `pitFireTeam.Instance?.Config.Save()`
- it does not use a temporary view-model
- it does not have save/cancel/default buttons
- it does not prompt for unsaved changes

So the current tab is a runtime config editor, not a stock-style staged settings screen.

### Current section split

Verified sections built today:

- `Base Settings`
- `Follow Settings`
- `Combat Settings`
- `Raid Settings`
- `Looting Settings`
- `Loadout Management`
- `Input Settings`
- `Miscellaneous`

Verified entry groups:

- `Base Settings`
    - `spawnPoint`
    - `englishBear`
    - `pingRadioVolume`
    - `pingTime`
    - `statusReportHighlightColor`
    - `statusReportHighlight`
    - `statusReportHealthColoring`
    - `statusReportFullHealthColor`
    - `statusReportMediumHealthColor`
    - `statusReportLowHealthColor`
    - `statusReportAlwaysHighlight`
    - `statusReportShowName`
    - `statusReportShowDistance`
    - `statusReportShowHealth`
    - `statusReportShowTactic`
    - `statusReportShowCombatStatus`
- `Follow Settings`
    - `goToDistance`
- `Combat Settings`
    - `botGrenades`
    - `enemyMarker`
    - `enemyKilledDisplayTime`
    - `enemyKilledRetainTime`
    - `statusSound`
    - `enemyRemember`
    - `scanDistance`
    - `botTalk`
- `Raid Settings`
    - `teamEscape`
    - `teamEscapeUseAnyExtract`
    - `pickupEnabled`
    - `tieredPickup`
    - `maximumPickup`
    - `recruitPickup`
    - `npcSendMessage`
    - `pitFireTeamFLAG`
    - `badGuy`
    - `factionHostilities`
    - `pmcArmbands`
- `Looting Settings`
    - `Minimum Price`
    - `Maximum Price`
    - `Pickup Food`
    - `Pickup Meds`
    - `Pickup Valuables`
    - `Pickup Weapons`
    - `Pickup Gear`
- `Loadout Management`
    - `Restricted`
    - `Field Upkeep` (visible only while `Restricted` is active)
    - `Immersive`
    - `Realistic`
- `Input Settings`
    - `hideUnsupportedCommands`
    - `pingKey`
    - `contactKey`
    - `overThereKey`
- `Miscellaneous`
    - `teleportKey`
    - `healKey`
    - `heatlhMultiplier`
    - `botPrefetch`
    - debug builds also show `battleRecorderEnabled` and `battleRecorderSnapshotIntervalMs`

### Current control behavior

Supported control types:

- `bool` -> toggle
- ranged `int` -> slider
- loot price ranged `int` settings -> integer input field
- hex color settings -> validated `#RRGGBB` input with a color preview
    - Status Report color applies to report text and the teammate outline
    - optional Health Status coloring replaces the outline color per teammate while leaving report text on the normal Status Report color
    - health colors blend continuously through Low at 30%, Medium at 65%, and Full at 100%
    - the health score starts from total body HP, is capped by head/thorax health, and is partially reduced by stomach damage so critical torso damage cannot be hidden by healthy limbs
    - optional Always Highlight keeps only the teammate outline active between Status Reports; the Status Report highlight master toggle must also be enabled, and report text remains timed
    - triggering Status Report while Always Highlight is active clears the outline renderer state for one frame before rebuilding it, matching an Off/On cycle so stale EFT LOD or equipment renderers do not leave stray lines
    - Enemy Marker colors apply when the enemy is visible or out of sight
- `LoadoutManagementMode` -> mutually exclusive radio-style toggle rows
- `KeyboardShortcut` -> press-to-capture button
- everything else -> read-only text fallback

Fresh configurations and Reset to Defaults use:

- Minimum Price: `20000`
- Maximum Price: `5000000`
- Pickup Food, Pickup Meds, and Pickup Gear: disabled
- Pickup Weapons and Pickup Valuables: enabled
- Loadout Management: `Restricted`
- Field Upkeep: disabled

### Raid faction hostility setting

`Faction Hostilities` is a default-on Raid setting that registers BEAR and USEC as opposing factions and registers PMCs against Scavs, Scav bosses, and their followers when bots activate. Follower groups do not accept a Scav from that faction matrix alone: the Scav must first have the player or any follower in its enemy relationship, actively target one of them, or enter through a direct aggression cause. This keeps a follower from initiating against a Scav that remains neutral to the whole squad while still reacting when the Scav is hostile to the player but has not separately targeted the follower. Scavs start neutral toward Cultists, Raiders, and Rogues; those three factions warn Scavs and can turn hostile if the warning is ignored. Partisan is excluded so his stock karma, zone, and proximity hostility logic remains authoritative. A player Scav already marked hostile by Fence karma or as free-to-kill keeps the game's existing hostility instead of being reset to neutral. Existing non-combat/quest-protected roles remain excluded. It does not make same-side PMCs hostile or bypass normal sight and hearing. The setting is disabled while a raid is active because existing bot relationships cannot be safely undone or rebuilt mid-raid.

### Loadout Management setting

`Loadout Management` is its own settings group placed after `Raid Settings`.

The group is hidden in raid-restricted settings contexts, including the in-raid `Squad Settings` overlay, so the loadout economy mode cannot be changed while a raid is active.

It is rendered as three mutually exclusive rows using cloned Ragfair `UIAnimatedToggleSpawner` controls under one `ToggleGroup`:
- `Restricted`
- `Immersive`
- `Realistic` (stored internally as `Extreme`)

The rows are intentionally vertical: each row shows the mode description on the left and the selectable mode toggle on the right.

When `Restricted` is the active mode, a `Field Upkeep` checkbox row appears between `Restricted` and `Immersive`. It defaults off and uses the same settings-row layout as other checkbox settings instead of joining the radio `ToggleGroup`.

Changing modes applies immediately and syncs to the server. `Restricted` is the default; all three modes edit real `Default` equipment. Legacy preset selections restore the saved `Default` automatically.

When a mode change is applied, the client saves the BepInEx setting, syncs the new mode to the server, and rebuilds the settings entries so conditional rows such as `Field Upkeep` appear or disappear immediately. The settings scroll position is captured before this rebuild and restored after Unity finishes recalculating the layout, so the view does not jump back to the top.

### Looting setting

`Looting Settings` is placed after `Raid Settings` and controls follower-commanded looting from non-teammate bodies and containers.

Detailed looting behavior is documented in `docs/Looting.md`.

`Minimum Price` is the lowest rouble value an item tree must have before a follower will take it. `Maximum Price` is the highest rouble value an item tree may have before a follower will take it. A value of `0` disables that bound. Money ignores both bounds when `Pickup Valuables` is enabled.

These thresholds apply to each candidate item tree once: weapons include attached mods, helmets include attached devices, and armor or rigs include their installed plates and carried contents. The command still requires the complete item to fit in the follower's backpack or pockets. If armor or a rig stays behind, eligible contents can be considered separately; installed plates require at least 50 percent durability, while loose plates remain excluded.

The category checkboxes default on and are applied before price:

- `Pickup Food` covers food and drinks.
- `Pickup Meds` covers usable medical items, drugs, stimulators, and med kits.
- `Pickup Valuables` covers barter items, keys, special items, info items, money, and other non-gear loot.
- `Pickup Weapons` covers weapons, ammunition, magazines, weapon mods, and grenades.
- `Pickup Gear` covers helmets, body armor, armored rigs, and tactical rigs.
- Empty-slot acquisition needs no separate toggle: `Pickup Weapons` or a weapon request enables weapon acquisition, and `Pickup Gear` or a gear request enables existing empty-vest acquisition. Occupied equipment is never replaced. Equipped acquisitions stay in the escaped teammate's kit in every mode; only loose cargo remains eligible for squad-member returns. Field recruits do not send returns.

Crossing into or out of `Realistic` also strips the secure-container tree from saved teammate `Default` loadouts before the next profile/edit view can expose it.

Detailed gameplay behavior, current server-side mode-switch behavior, and pending implementation gaps are documented in `docs/Loadout-Management.md`.

Shortcut capture behavior:

- opens capture mode when its action button is clicked
- `Escape` cancels capture
- `Backspace` or `Delete` clears the shortcut
- otherwise the next non-modifier key becomes the main key and current Ctrl/Shift/Alt state becomes modifiers

### Raid overlay path

There is also a separate in-raid-style access point for the settings panel:

- `Squad Settings` button cloned from the menu `hide/resume` button

This opens the same settings content inside `screenRoot` as a standalone overlay with a cloned back button. It shows only the settings tab and is separate from the side-selection-hosted `My Squad` entry flow.

The completed settings hierarchy is retained while its menu/raid restriction context is unchanged. When the in-raid pause menu exposes the `Squad Settings` button, the raid-restricted version is prepared while the overlay is still hidden, so opening the overlay does not normally destroy and recreate every settings row. A context change between menu and raid still triggers one rebuild so hidden and disabled settings remain correct.

### Current settings limitations

- no staged save/cancel flow
- no settings search/filter
- no per-setting dependency/disable logic beyond what each control directly supports
- still tightly coupled to BepInEx config entries rather than a dedicated persisted UI model

## Part 3: Mode

The `Mode` tab follows `Settings` (Roster, Settings, Mode) and uses its own panel in the same side-selection host. Its shell matches the settings panel dimensions. Opening My Squad still starts on `Roster`; switching tabs shows only the selected panel. All three panels are retracted when the host closes, and all three cloned tab controls are cleaned up.

The panel contains two mutually exclusive, vertically arranged options:

- `Guns for Hire` (initial selection)
- `Allegiance`

Guns for Hire preserves existing gameplay. Allegiance hides Add Teammate and rejects manual hiring on the server; teammates must be recruited in the field. Each mode has its own roster database, including equipment, teammate settings, and pending recruitment requests. Switching clears the outgoing squad selection and refreshes roster/social data. Switching is unavailable during raids. The in-raid `Squad Settings` overlay continues to show only Settings.

Allegiance selects solo BEAR and USEC PMCs after `FactionHostility.Apply` in the activation hook. At the default Friendly Chance Multiplier of 1, same-faction candidates have a 30% friendship chance and opposite-faction candidates have a 15% chance, relative to the human PMC's side. Each profile has one raid-local decision, with at most three lifetime friendly selections shared across both factions; deaths, recruitment and dismissal never release a slot. Failed rolls do not consume a friendly selection slot and cannot be retried. Current groups, original multi-bot spawn groups and groups awaiting additional members are excluded, including their later survivors. A selected solo joining a group loses this individual friendship. Player-Scav/Fence behavior remains unchanged.

`Miscellaneous > Friendly Chance Multiplier` appears immediately before Squad Health Multiplier. It is an integer BepInEx setting from 1 to 5, enabled only in Allegiance; Guns for Hire displays it disabled with the mode-unavailable tooltip. Each step adds 17.5 percentage points for the player's faction and 21.25 for the opposite faction. Changes affect future candidate rolls only, without resetting existing decisions or the raid cap. Its saved value survives restarts and mode switches independently of the Guns for Hire snapshot.

| Friendly Chance Multiplier | Same faction | Opposite faction |
|---|---|---|
| 1 (default) | 30% | 15% |
| 2 | 47.5% | 36.25% |
| 3 | 65% | 57.5% |
| 4 | 82.5% | 78.75% |
| 5 | 100% | 100% |

Killing one of your current raid recruits in Allegiance subtracts `5 × current Friendly Chance Multiplier` percentage points from both friendship chances, after calculating the multiplier's chance boost, with a minimum chance of zero. Each active kill costs 5, 10, 15, 20 or 25 points at multipliers 1 through 5. Scaling uses the current setting whenever chances or the Roster label are evaluated, so killing at a lower multiplier then raising it cannot preserve a smaller penalty. The stored ledger retains kill identity and expiry, including older entries; no database migration is needed. Spawned squadmates, ordinary friendly bots, kills by another aggressor and Guns for Hire kills do not trigger this penalty. The existing `traitor` kill report records it independently of Raid End Messages, including opposite-faction recruits. Reports are deduplicated by raid and victim, and stored per user ID in the Allegiance database. Each kill expires independently after 24 real hours, including time spent outside the game; the mode/config snapshot cannot reset it. Existing bots retain their one-roll decision.

While a penalty is active, Roster displays a small red label beneath the Roster tab, aligned with its left edge: `-5 points in Friendly Encounters (24h)` at multiplier 1, or `-25 points in Friendly Encounters (24h)` at multiplier 5. The label belongs to the roster panel rather than the moving card shell. Multiple penalties show their scaled total reduction and the countdown to the next expiry; the total then drops by `5 × current multiplier` points. Changing the multiplier refreshes the displayed total immediately, without resetting any expiry. The displayed time rounds up in half-hour steps above two hours, 0.1-hour steps from two hours to one hour, and one-minute steps below one hour. Actual expiry is exact and the label disappears when no penalties remain. The countdown updates while Roster is open without per-frame string formatting or HTTP polling.

Selected candidates become neutral to the player and squad without changing shared bot settings, visibility or shot permission. After each follower finishes group reassignment and registration, Allegiance refreshes neutrality in both directions between that follower and every living, still-friendly selected candidate. This covers BEAR and USEC equally, including the first recruit forming a squad. The existing profile-alias cleanup removes stale BotOwner/Player enemy keys, clears only those friendly-pair memories and publishes native enemy-removal notifications for external SAIN caches. It never rerolls selection, restores revoked friendship or removes unrelated enemies. Ambient enemy scans cannot undo this relationship, but real aggression or explicit Contact permanently revokes it for the raid. Recruitment requires a selected, unrevoked candidate at request and deferred conversion time; existing temporary combat refusals, raid-sticky tiered acceptance refusals and the two-active-pickup limit still apply. Selection and recruitment-refresh diagnostics use the `[Allegiance]` log prefix.

Neutrality repair removes enemy keys for both EFT representations of the same profile (`BotOwner` and `Player`). It publishes the native group enemy-removal event even when the group entry is already absent, allowing external SAIN to discard its separate cached contact. Other profiles keep their relationships and memories.

Each selected, unrevoked Allegiance friendly can say `HoldFire` up to twice per raid before recruitment. The first clear sight of the living human PMC within 50 m starts a distance-based delay: `0.2 + 1.8 × distance / 50` seconds, sampled at first sight. This gives 0.38 seconds at 5 m, 1.1 seconds at 25 m and a maximum scheduled delay of 2 seconds at 50 m. The second line is scheduled a random 1–3 seconds after the first starts, then gets one chance roll when contact and speech are eligible: `current distance / 50`, giving 10% at 5 m, 50% at 25 m and 100% at 50 m. A failed roll permanently skips the second line for that raid; moving farther away or meeting again cannot reroll it. A successful roll survives a playback failure without rolling again. Both lines require current range, native view sector, visible distance and an unobstructed head-to-head sight ray. Losing sight or busy speech defers the line without restarting the sequence. Recruitment, hostility revocation and raid teardown stop further greetings. The existing exact pre-recruitment speech scope routes these lines through EFT with or without external SAIN; shared talk settings remain untouched. `AllegianceFriendlyGreeting` runs through `BotOwnerUpdateHub`, normally checking sight twice per second and checking earlier when a phrase deadline falls between those checks. Only actual speaker activation spends a line.

Opposite-faction recruits retain their original side and PMC role in the invitation, saved roster and subsequent Allegiance raid spawns. Their request permissions and squad hostility follow the human leader through the existing follower conversion and shared relationship policy. Guns for Hire keeps its existing same-side recruitment and leader-side spawn behavior.

Positive damage from an outside AI to the human leader explicitly revokes that candidate's friendship, even with no followers present and before initial activation. The existing squad hostility helper shares the relationship without selecting goals or granting sight/fire permission. Zero-damage notifications and friendly fire from squadmates do not revoke it. Player Scavs retain same-side recruitment and Fence limits in either mode.

The selected mode persists in the hidden `00 GameplayMode` config entry. Before entering Allegiance, `GameplayModeRuntime` atomically saves all currently bound mod settings except the mode and Allegiance-only Friendly Chance Multiplier in `<config path>.guns-for-hire.json`. Returning restores that snapshot, including after restarting the game. Allegiance now leaves the saved config preferences intact: gameplay consumers, the Settings UI, and server synchronization resolve the fixed values through `GameplayModeRuntime.GetEffectiveValue`. Editing or reloading the BepInEx cfg cannot override these rules while Allegiance is active. The selected mode remains a separate coordinated transition; reloading a different mode value during the session is rejected. A missing or invalid snapshot fails restoration without overwriting it. Mode transitions wait for registered post-raid reports, serialize server settings requests, and invalidate delayed invitations from the outgoing mode. The server serializes teammate operations with database selection; switching databases does not convert the inactive roster's loadouts.

Allegiance disables these controls, with the tooltip `this option is not available in Allegiance Mode`, and enforces their values through the runtime policy:

| Setting | Allegiance value |
| --- | --- |
| Bad Guy | Off |
| Friendly PMC Side | Off |
| Enemy Tracking | Realistic |
| Pickup | On |
| Tiered Pickup | On |
| Maximum Pickup | 2 |
| Recruit Pickup | On |
| Team Escape | On |
| Team Escape: Use Any Extraction Point | Off |
| Loadout Management | Immersive |

Heal Followers remains available as a manual emergency shortcut in Allegiance. Its key can be configured normally; ordinary follower healing behavior is unchanged.

Squad Health Multiplier is also editable in Allegiance and uses the saved value through the existing follower-spawn customization path. Its range remains 1–10 and its existing in-raid editing restriction still applies.

Each option uses the same row presentation as Loadout Management: a dark full-width row with a gold divider, its name and description on the left, and the compact radio-style selection control on the right. Selecting either control highlights it and clears the other.

The Guns for Hire description is: "Build and equip your squad before deploying. Customize and play by your own rules."

The Allegiance description is: "Play by Tushonka's rules. Alliances are formed, not bought. Settings are locked, and relationships are determined in the field." Relationships continue to use the existing recruitment and contact rules with the enforced settings above.

Controls reuse the existing cloned Ragfair radio-style toggle helper, right-side control dimensions and click/hover behavior with a basic-toggle fallback. Names and descriptions use the central `socialUi` language entries and embedded English fallback.

## Part 4: Profile Screen

### What the profile screen is

The profile screen is not part of the side-selection host. It is the stock `OtherPlayerProfileScreen`, patched when the viewed profile is a teammate rather than the local player.

This is the current detail/customization surface for a selected squad member.

### Entry and return path

Roster profile open calls:

- `ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile)`

Before opening, the code sets a pending back override. When the profile screen closes, that override re-opens `My Squad`, so the user returns to the roster rather than being dropped somewhere else in menu history.

If EFT cannot fetch the teammate profile and returns no profile-screen controller, the pending back override is cleared and a localized corruption/fetch warning is shown instead of failing silently.

### Teammate gating

The profile patch only activates when:

- the viewed profile is not the local player
- teammate profile options load successfully from:
    - `POST /singleplayer/pitfireteam/teammate/profile/options`
- at least one loadout option exists

If those conditions fail, the stock profile mostly stays in charge.

### What gets changed on teammate profile

For teammate profiles the patch:

- hides stock report actions
- clears the stock right-side profile content blocks
- reuses the stock clothing panel for suit selection
- combines unlocked BEAR, USEC, and Savage clothing with each teammate's persisted wardrobe while keeping each suite id unique, so generated clothing remains selectable even when the player has not unlocked it
- injects a cloned second clothing-style row for equipment editing + tactic
- replaces the loadout dropdown side with `EDIT LOADOUT` in `Restricted`, `Immersive`, and `Realistic`, leaving the tactic dropdown intact
- injects a `PROFICIENCY` button row below that
- injects a `KIT LOADOUTS` button row below that
- clones and hosts a filtered `SkillsScreen`
- moves the faction badge down to fit the custom rows
- turns the stock hideout button into `EDIT NAME`

### Persisted profile actions

Verified persisted actions today:

- suit/body/feet change
    - `POST /singleplayer/pitfireteam/teammate/profile/suit`
- rename
    - `POST /singleplayer/pitfireteam/teammate/profile/rename`
- real `Default` equipment transfer
    - `POST /singleplayer/pitfireteam/teammate/profile/default-equipment`
- tactic
    - `POST /singleplayer/pitfireteam/teammate/profile/tactic`
- aggression
    - `POST /singleplayer/pitfireteam/teammate/profile/aggression`
- proficiency percentages
    - `POST /singleplayer/pitfireteam/teammate/profile/proficiency`

After successful profile-side persistence the code marks the squad roster dirty so the next `My Squad` reopen can refresh changed tiles.

Pending recruit friend requests also open through `OtherPlayerProfileScreen`, but remain read-only. Their stock empty favorite-item and achievement sections are replaced with the same filtered follower-skills panel used for accepted teammates; teammate management controls stay unavailable until the recruit is accepted.

### Loadout and tactic selectors

The loadout/tactic row is still based on `InventoryClothingSelectionPanel`.

Upper control in `Restricted`, `Immersive`, and `Realistic`:

- saved-loadout selection is hidden
- the row becomes `EDIT LOADOUT`
- `Default` is the real editable gear surface
- full kit acquisition is handled by the separate `KIT LOADOUTS` button, which sends the teammate's previous active kit back through the pitFireTeam courier before equipping the newly purchased kit

Lower dropdown:

- current tactic
- populated from backend tactic options, with a client fallback list of:
    - `Rifleman`
    - `Marksman`
- `Protector` is intentionally hidden for the beta release and old persisted values normalize back to `Rifleman`

Equipment saves and kit purchases persist through the backend and refresh the live profile visualization.

### Proficiency dialog

`PROFICIENCY` opens the teammate-profile modal that owns follower proficiency controls. Its panel uses the same draggable header behavior and title styling as `Edit Loadout`.

Behavior:

- `Aggression` is functional: its current value comes from teammate profile options, values are clamped to `0..100`, and persistence is delayed/debounced before posting to the backend
- marksman tactic uses aggression as a tactic-relative offensive auto-search control
- `Vision`, `Precision`, and `Reaction` are functional `0..200` percentage sliders with neutral default `100`
- `Vision` changes only the follower's detection-distance multiplier
- `Precision` changes shot accuracy, contributes half of the final aim-speed multiplier, and sets a conservative native non-head-to-head enhancement chance from `10%` at Precision `0` through `40%` at `100` to `70%` at `200`
- `Reaction` changes visual-recognition speed, contributes the other half of final aim speed, and scales the short direct-fire gate used by core close dogfights
- the bottom `RESET` button restores Vision, Precision, and Reaction to `100`, restores Aggression to the active tactic's default (`50` for Rifleman or `30` for Marksman), updates all four visible sliders immediately, and persists one aggression update plus one proficiency-object update
- the current values load from teammate profile options; changing any slider updates one follower-local proficiency object and persists the whole object after a short debounce
- saved values flow through follower details and are snapshotted when that teammate spawns

#### Proficiency percentage contract

The three proficiency values are direct percentage modifiers:

```text
effective multiplier = slider value / 100
```

- `0` represents `0%`
- `100` represents the unchanged class/tactic default (`1.0x`)
- `120` represents `120%` (`1.2x`)
- `150` represents `150%` (`1.5x`)
- `200` represents `200%` (`2.0x`)

The modifiers are stored per saved teammate and applied **after** that follower's role and combat tactic have finalized their own proficiency values. They never replace those values with one global baseline. Changing a teammate between Rifleman and Marksman therefore changes the baseline that `100%` represents, while the saved percentages remain relative to the selected class.

Final aim speed deliberately combines two player-facing qualities:

```text
vision range factor = Vision / 100
accuracy factor = Precision / 100
recognition-speed factor = Reaction / 100
aim-speed factor = (Precision + Reaction) / 200
head preference = 10 + 0.30 * Precision                         (Precision 0..100)
head preference = 40 + 0.30 * (Precision - 100)                (Precision 100..200)
```

This means Precision `100` plus Reaction `100` keeps normal aim speed, either value at `200` while the other stays at `100` produces `1.5x` aim speed, and both at `200` are required for `2.0x` aim speed.

For profile compatibility and recorder detail, storage retains the four granular fields `VisionDistance`, `VisionSpeed`, `AimSpeed`, and `Accuracy`. Their authoritative mapping is `VisionDistance = Vision`, `VisionSpeed = Reaction`, and `Accuracy = Precision`; `AimSpeed` is a derived compatibility field recalculated as the average of Precision and Reaction. Existing saved profiles naturally carry their former recognition-speed value into the new Reaction slider.

The applied runtime modifier is an immutable spawn-time snapshot. Profile changes affect the teammate the next time that follower is spawned, normally in the next raid; they do not rewrite the settings object of an already-active follower.

Concrete `Vision` distance examples:

| Runtime path | Rifleman at `100` | Marksman at `100` | Marksman at `150` |
|---|---:|---:|---:|
| Vanilla/core `VisibleDistance` | `185m` | `210m` | `315m` (`210 x 1.5`) |
| SAIN-normalized `VisibleDistance` | `250m` | `275m` | `412.5m` (`275 x 1.5`) |

Meaning of each percentage:

- `Vision`: multiplies only the finalized detection distance. `150` sees up to `1.5x` farther than that class's default; it does not make recognition faster.
- `Precision`: multiplies shot-execution accuracy, tightening scatter, accelerating precision convergence, and reducing external-SAIN recoil at the core compatibility boundary. It supplies half of the combined aim-speed value and maps piecewise to a `10%` / `40%` / `70%` chance to promote a native non-head target to a verified head target at slider values `0` / `100` / `200`.
- `Reaction`: multiplies the visual visibility-gain rate and supplies the other half of combined aim speed. On the core combat path it also scales the `0.2s` close-dogfight direct-fire gate; this gate is `0.2s` at `100`, `0.1s` at `200`, and never delays the normal aim/shoot worker once that worker is independently ready.

Vision, Precision, Reaction, and general compatibility with the external SAIN plugin are core-owned and must work in all three runtime modes: no SAIN, SAIN installed without the addon, and SAIN with the addon. The optional addon consumes this base proficiency contract; its [combat ownership](../addon/docs/Integration.md) does not change these percentages or shared presets. The combined Precision/Reaction aim-speed factor is applied after the final EFT-or-SAIN aim-time calculation, and Precision's Accuracy factor is applied to SAIN's final calculated recoil by the main plugin rather than the addon.

EFT chooses the body part first. External-SAIN compatibility first prefers an eligible body point after native selection, retaining exposed-part fallback if the body is blocked. Precision then operates on that baseline; an already selected head is preserved without another probability roll. If it is not the head, Precision gets one opportunity per normal retarget interval to promote the choice to the head, and only when the shared correction verifies a visible, shootable head lane. A hidden head leaves the native target untouched; a sole exposed head can still replace an invalid covered-body fallback. The core direct-fire overlay calls EFT's native `GetVisiblePartToShoot()` selector rather than the body-only `CurrentEnemyTargetPosition(false)` helper, then receives the same enhancement. A promoted head stays selected for the retarget interval while it remains shootable, so this does not reroll on every shot. The older SAIN 4.5.0 global center-mass height clamp is corrected only when it lowers a head that SAIN already selected for a pitFireTeam follower.

Reaction does **not** change EFT's `WAIT_NEW_SENSOR` or `WAIT_NEW__LOOK_SENSOR`. Those remain the game's independent ambient look/hearing refresh and stationary-cover look-switch timers.

The raw `0` value remains saved and displayed as `0%`. EFT modifier restoration and inverse calculations cannot safely consume a literal zero, so runtime application uses a project-owned minimum effective factor of `0.05x` (`5%`) for values from `0` through `5`; values above `5` map directly to their displayed percentage.

### Rename flow

Rename uses a custom overlay on top of the profile screen.

Confirmed implementation:

- clones a live stock `NicknameField`
- reuses stock nickname validation
- uses a cloned stock button template for save
- persists through the teammate rename route
- refreshes social list and squad roster state after success

### Skills panel

The profile patch also clones a stock `SkillsScreen`, builds a filtered skills profile snapshot, and shows follower-relevant skills inside the profile right side.

This is teammate-only UI and is destroyed/reset on profile close.

### Current profile limitations

- teammate profile still lives inside `OtherPlayerProfileScreen`, not a dedicated squad detail screen
- voice/head editing is not implemented in this screen
- right-side content is heavily patched and reset-sensitive, so this remains a fragile area

## Current Custom Loadout Editor Status

This is the status of the `Edit Loadout` overlay specifically.

### Current behavior

The `Edit Loadout` button opens a full-screen modal overlay on top of the teammate profile screen.

The overlay currently builds:

- draggable header bar
- subtitle explaining staged real item movement
- left section: cloned fake player stash
- right section: cloned follower inventory/equipment view
- cancel button
- done button

Confirmed implementation details:

- the left stash is a staged stash view built from the player stash
- the right follower inventory is built from staged teammate equipment and a local editor inventory controller
- `Restricted`, `Immersive`, and `Realistic` preserve item ids while editing `Default` so `Done` can commit real item movement
- repair is available for repairable teammate gear in all modes; it updates teammate equipment and player repair resources, not saved player equipment presets
- secure container is removed from the edited equipment before display/save except in `Realistic`
- teammates created while `Realistic` is active start with an editable secure container based on level: Beta below 15, Epsilon below 30, Gamma at 30+
- the follower containers panel currently renders only:
    - `TacticalVest`
    - `Pockets`
    - `Backpack`
    - `SecuredContainer` in `Realistic`
- the follower equipment tab currently renders:
    - scabbard
    - holster
    - both primary weapons
    - eyewear/face cover/headwear/earpiece
    - armor vest
    - armband
- dogtag is removed/hidden from the follower-side editor view
- the `ChracterGear` image is hidden by disabling its `Image`
- the header text is manually rewritten to the teammate name

Verified save behavior:

- the editor uses a staged local stash + follower equipment session
- item edits stay local until `Done`
- the editor opens the teammate's current `Default` equipment
- `Done` saves teammate equipment and the real player stash, then refreshes the live stash view
- no preset naming or overwrite dialog is shown

### Current limitations

- real-item movement is currently limited to `Default`
- spawn preparation and death-stripping behavior are tracked separately in `docs/Loadout-Management.md`
