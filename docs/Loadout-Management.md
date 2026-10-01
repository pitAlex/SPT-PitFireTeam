# Loadout Management

Date: 2026-09-08

## Scope

This document tracks the current `Loadout Management` setting and the first implementation phase around default teammate loadouts.

Current phase focus:

- expose the mode in `My Squad -> Settings`
- preserve each teammate's saved `Default` gear and migrate legacy preset selections to `Default`
- test default-loadout spawn preparation for the three modes
- test Immersive-style death gear loss for teammates using `Default`

This is not yet the full real-stash economy implementation. Default-loadout real item ownership transfer is implemented for `Restricted`, `Immersive`, and `Realistic` (internal mode value: `Extreme`). The profile UI replaces the saved-loadout dropdown with `EDIT LOADOUT` and uses `Default` as the editable real-gear surface, with `KIT LOADOUTS` reserved as the way to acquire a full kit.

See `docs/Buy Screen.md` for the current stock `EquipmentBuildsScreen` reuse, buy-mode UI changes, and kit price-display behavior.

See `docs/Team-Escape.md` for the player-death squad escape, recovered death-gear mail, and escape outcome persistence behavior.

See [Follower Insurance](Follower-Insurance.md) for real purchases, persistent exact-item policies, transfer safety, and stock trader returns for verified raid losses. Insurance confirmation commits pending loadout changes like repair and keeps the editor open after refreshing live and staged payment state.

## UI Behavior

`Loadout Management` is a dedicated settings group in the `My Squad` settings tab, placed after `Combat Settings`.

The group is hidden while the settings panel is opened from a raid-restricted context, including the in-raid `Squad Settings` overlay.

It contains three mutually exclusive mode choices:

- `Restricted`
- `Immersive`
- `Realistic` (stored internally as `Extreme`)

The UI uses cloned Ragfair `UIAnimatedToggleSpawner` controls under a Unity `ToggleGroup`. Each option is rendered as its own vertical row: description text on the left, radio-style toggle on the right.

Selecting a different mode saves the config, syncs it to the server, and refreshes the visible toggle and roster. All modes use `Default`, so there is no preset-selection confirmation.

Opening `EDIT LOADOUT` resets the cloned player-stash panel to the top after its grid and viewport layout is built, stopping inherited scroll momentum. The reset runs only on opening; scrolling within the editor works normally.

## Modes

### Restricted

`Restricted` is the default. Legacy `Simple`, missing, and invalid settings resolve to `Restricted`; existing `Immersive` and `Realistic` settings retain their behavior.

Target behavior:

- any gear used for a follower loadout is taken from the player's stash
- gear is not lost on follower death
- spawned follower gear cannot be extracted with
- optional `Field Upkeep` can preserve raid damage and consumed non-secure-container supplies without enabling death gear loss

Current phase behavior:

- mode exists in UI and server settings
- when `Restricted` is active, the settings UI can show the `Field Upkeep` checkbox row between `Restricted` and `Immersive`; it is off by default
- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit
- editing `Default` uses real player stash item ids instead of cloned ids
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- spawned follower gear is protected from raid loss and cannot be extracted with
- when `Field Upkeep` is off, raid outcome does not persist durability damage, consumable use, or death loss
- when `Field Upkeep` is on, escaped `Default` teammates persist their live in-raid equipment state like `Immersive`, with tracked follower-loot/player-given item ids, other teammates' protected gear ids, and the non-Realistic managed secure-container tree stripped before saving
- original saved magazines remain protected during upkeep after extraction or death: missing magazine shells are restored empty, while surviving magazines retain their live cartridges; acquired loot magazines are not restored
- magazine recovery uses free compatible grid space or the magazine's original empty weapon slot; if recovery cannot fit safely, maintenance retains the previous saved equipment and logs the failure instead of committing magazine loss
- `Field Upkeep` does not enable death gear loss; dead teammates are not stripped down like `Immersive`
- if a `Default` teammate dies while `Field Upkeep` is on, the server saves that teammate's death-time equipment state so durability/resource changes at death are preserved, but later corpse looting cannot consume or move the fallen teammate's saved gear

### Immersive

Target behavior:

- same stash-consumption rules as `Restricted`
- follower equipment can be damaged
- if a follower dies, their gear is lost
- dead follower gear can be looted

Current phase behavior for `Default` only:

- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit
- editing `Default` uses real player stash item ids instead of cloned ids
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- if a teammate dies while using `Default`, saved default equipment is stripped down to equipment root plus permanent bot identity slots: dogtag, armband, `Scabbard`/knife, and special slots
- if a teammate extracts while using `Default`, the live in-raid equipment state is saved back as the new `Default`, with the generated secure-container tree stripped before persistence
- dogtag, scabbard/knife, armband, and special slots are preserved because bots do not lose those slots
- if a future edit removes the knife, spawn preparation injects a fallback knife before the teammate is used

### Realistic

`Realistic` is stored internally as `Extreme`. It is Immersive-like with an additional secure-container restriction.

Target behavior:

- same death/loss direction as `Immersive`
- the secure container slot is no longer auto-managed
- no protected meds or spare ammo are injected into the secure container

Current phase behavior:

- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- switching into or out of this mode strips any existing secure-container tree from saved teammate `Default` loadouts to prevent carrying a hidden managed container into the editable Realistic slot
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit; the editable secure container is included in that delivery
- newly created teammates receive an initial editable secure container based on level: Beta below 15, Epsilon below 30, Gamma at 30+
- editing `Default` uses real player stash item ids instead of cloned ids
- the edit-loadout panel shows the teammate secure container slot so it can be edited
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- if a teammate dies while using `Default`, saved default equipment is stripped down to equipment root plus permanent bot identity slots: dogtag, armband, `Scabbard`/knife, secure container, and special slots
- if a teammate extracts while using `Default`, the live in-raid equipment state, including the editable secure-container tree, is saved back as the new `Default`
- spawn preparation does not add, replace, or fill the secure container
- any secure-container contents are whatever the saved `Default` currently has

## Server Behavior

The server receives the active mode through the normal server-settings sync path.

When `loadoutManagementMode` changes:

1. the server keeps every teammate's saved inventory and `Default` equipment snapshot unchanged
2. if the change moves into or out of `Realistic` / internal `Extreme`, the saved `Default` secure-container tree is removed and the default snapshot is overwritten

Every teammate is migrated on load: a legacy custom preset selection restores the saved `Default` equipment before the profile can be viewed, edited, purchased over, delivered, or spawned. The profile is saved before the selection so an interrupted migration can retry. The saved `Default` snapshot is preserved. No custom preset can be selected for free, and clone-only equipment saves are rejected by the server.

The server also receives `restrictedGearMaintenance`. This conditional `Restricted` setting defaults to `false` and does not change the selected loadout or saved `Default` snapshot when toggled.

## Real Default Loadout Commit

Real item ownership transfer currently applies only when all of these are true:

- the active mode is `Restricted`, `Immersive`, or `Realistic`/`Extreme`
- the user is editing a teammate's `Default` loadout
- the user presses `Done`

The edit overlay itself is still staged. Dragging items in the editor does not change the saved profile or live stash until `Done`.

On `Done`, the client sends two item snapshots to the server:

- sanitized teammate equipment
- staged player stash

The server is authoritative for the commit:

1. validate the submitted player stash root matches the active profile stash
2. build the allowed moved-item set from the current player stash tree plus the current teammate inventory
3. reject submitted equipment or stash items outside that allowed set
4. reject overlap where the same item remains both in teammate equipment and player stash
5. reject player-equipped items being submitted as teammate equipment
6. replace the player stash tree in the profile with the submitted staged stash
7. replace teammate default equipment with the submitted staged equipment, preserving special follower-only items where needed
8. save the player profile, teammate profile, teammate settings, and default snapshot

After the save succeeds, the server returns the saved player stash snapshot to the client.

## Teammate Addition Cost

Manual addition quotes the candidate's equipment; there is no separate recruitment fee. Raid-recruit acceptance remains separate. See [My Squad's hiring preview](My-Squad-Screen.md#hiring-preview) for navigation, loading and button behavior.

### Candidate and quote lifecycle

All routes below use the `/singleplayer/pitfireteam` prefix:

| Route | Effect |
|---|---|
| `/teammate/prepare` | Generates a candidate, normalizes its knife, calculates the gear price and persists a pending quote. Returns `quoteToken`, `aid`, `price` and `supportsWithoutKit`. |
| `/teammate/create` | Confirms the current `quoteToken`; optional `withoutKit: true` selects the free no-gear path. Tokenless calls cannot create free teammates. |
| `/teammate/cancel` | Cancels the matching pending token without payment or roster changes. A stale token cannot cancel a replacement quote. |

Regenerate repeats preparation while retaining the chosen nickname, head and voice. Each successful preparation replaces the pending candidate and invalidates its old token without payment. Confirmation saves the exact quoted profile instead of generating another teammate. Pending candidates can be previewed but are not friends or roster members.

The quote lasts 30 minutes. Confirmation rejects an expired quote, a changed loadout mode or an older pricing version, and rechecks nickname uniqueness. Pricing version `2` covers the corrected unit-price calculation and knife exclusion. Completed receipts and interrupted payments remain recoverable across pricing-version changes.

### Charged equipment

Only eligible items directly equipped under the equipment root start a priced tree:

| Equipment slots | Charged items |
|---|---|
| `FirstPrimaryWeapon`, `SecondPrimaryWeapon`, `Holster` | Each weapon with installed attachments and magazines; no ammunition |
| `Headwear` | Helmet/headwear with installed accessories, including mounted devices |
| `FaceCover`, `Eyewear`, `Earpiece` | Face cover, eyewear and headset, plus any eligible installed attachments |
| `TacticalVest`, `ArmorVest` | The vest/armor item and installed removable armor plates |
| `Backpack` | The backpack item itself |

Traversal follows template attachment `Slots`, never container grids. Each item ID is charged at most once. Loaded/chambered ammunition, integral armor inserts, carried supplies, pocket contents, dogtags, armbands, secure-container trees and knives are excluded. Exclusion from the price does not remove an item from a paid candidate's inventory.

After every manual generation, the equipped `Scabbard` knife tree is replaced with one fresh **6Kh5 Bayonet** (`5bffdc370db834001d23eca8`) before preview and pricing. This also applies to regeneration. The knife costs nothing in either purchase option. Dogtags and other slots are preserved; existing saved teammates and raid recruits are unaffected.

### Unit-price calculation

When SPT's `Dynamic.GenerateBaseFleaPrices.UseHandbookPrice` is enabled and the item has a positive handbook value:

1. Start with `PriceMultiplier`; a template-specific override takes precedence, otherwise use the first matching base-class override.
2. If the enabled crafting adjustment applies to an item required by a hideout production recipe, add `HideoutCraftMultiplier` to that multiplier once.
3. Multiply the handbook value by the resulting multiplier.
4. When `PreventPriceBeingBelowTraderBuyPrice` is enabled, floor the result at the highest amount a trader would pay the player for that item.

Hiring computes this value once per template per quote. It bypasses the additive generated flea table observed in SPT 4.1 and does not change the shared economy. When handbook generation is disabled or the item has no positive handbook entry, use a positive dynamic flea-table value, falling back to the handbook value.

This is a configured equipment valuation, not a lookup of the cheapest live flea offer or a trader's selling price. Local SPT data and economy settings can differ substantially from official-game prices; official prices must not be substituted when auditing a local quote. There are no kit, assembled-weapon or condition discounts. Sum eligible individual item prices, then round the total upward to whole roubles once. Missing, non-positive, non-finite or overflowing prices reject preparation rather than silently making equipment free.

### Add With No Gear

`withoutKit: true` confirms the same candidate for **zero roubles**, with no player-money save. The shared permanent-equipment retention helper keeps the equipment root, dogtag, knife, armband and special-slot trees, plus the pockets and secure-container shells. Ordinary kit and ordinary pocket/secure-container cargo are removed. Special-slot trees are preserved separately, including when attached to pockets.

The stripped profile, stripped `Default` snapshot and completion receipt are saved in one teammate-database transaction, so the discarded kit cannot return through Default restoration. A failed save retains the original pending candidate and price. The client offers this action only when preparation advertises `supportsWithoutKit`, preventing an older server from ignoring the flag and treating the request as a paid hire.

### Payment and recovery

Paid confirmation checks and deducts unlocked stash roubles. Insufficient funds leave the quote available, with no partial debit or new teammate; the client displays the localized message in the vanilla message window.

The player's SPT save and teammate database are separate stores. Before saving player money, a persistent `paying` journal records the before/after rouble item identities, counts and locations. The teammate, Default equipment, settings and `complete` receipt then commit together in the teammate database. Retrying the current completed token returns the saved stash without another charge; switching between paid/no-gear buttons cannot create a second teammate or change a completed purchase.

Recovery runs when listing the roster or preparing, confirming or cancelling a hire. Money matching the journal's after-state completes the hire without another debit; matching the before-state restores the pending quote. Ambiguous money changes stop recovery rather than guessing. A failed commit records refund intent and original rouble items before saving the refund. Payment/refund recovery verifies the saved player JSON, so restored memory or an SPT cached-write skip cannot acknowledge an unpersisted refund. If the cached retry leaves disk unchanged, recovery remains blocked until a fresh save (normally after server restart). See [Teammate Storage](Teammate-Storage.md) for the database boundary.

### Source and verification

- [Creation service](../server/Services/FriendlyTeammateService.Creation.cs): prepare/confirm/cancel, bayonet replacement and payment recovery.
- [Equipment selector](../server/Services/TeammateEquipmentPrice.cs) and [unit-price lookup](../server/Services/FriendlyTeammateService.HiringPrices.cs): charged trees and valuation.
- [Permanent-equipment retention](../server/Services/FriendlyTeammateService.EquipmentRetention.cs): no-gear stripping, also shared with existing equipment-loss behavior.
- [Hiring fixtures](../tests/TeammateHiring/Program.cs): run `dotnet run --project tests/TeammateHiring/TeammateHiring.csproj` from the repository root. Covers selection/exclusions, duplicate prevention, price overrides/fallbacks, knife replacement, regeneration, no-gear retention, insufficient funds, retries, rollback and interrupted-payment recovery. These fixtures do not establish Unity layout, navigation or model-loading behavior.

## Raid-Recruit Deletion Fee

An in-raid pickup who later sends a friend invite joins for free when accepted. The server records the gear value from the captured post-raid profile when creating that invite. It uses the same [charged equipment and unit-price rules](#charged-equipment) as manual hiring, including the knife exclusion, but keeps the recruit's actual knife rather than replacing it with a bayonet.

`FriendlyRecruitRequestEntry.RecruitmentGearPrice` stores the server-calculated amount in `recruit-requests.json`; it is not accepted from the client pickup payload. Acceptance copies it into `FriendlyTeammateSettings.RecruitmentGearPrice` with the teammate and Default equipment. Member creation, invitation removal and an acceptance receipt commit in the same database transaction. Repeated requests cannot duplicate the member, and Accept All resumes from only the unfinished invitations after a failure. Legacy pending invites without a price are valued at acceptance, before loadout-mode preparation. Existing roster members without this metadata remain free to delete because their recruitment origin and original equipment value cannot be reconstructed reliably. Manual hires, including Add With No Gear, also remain free to delete.

The recorded amount is fixed: changing equipment, moving gear into the player's stash, buying a kit, losing equipment or changing economy settings does not reprice or erase the fee. Only the initial gear price is retained for this policy; no initial-inventory snapshot or remaining-gear credit is used. Deleting a recorded recruit requires that amount in unlocked stash roubles. Insufficient funds preserve the member, settings, Default equipment and player money. The roster and native social deletion paths display the fee in confirmation and wait for the server before removing the member; they refresh the live stash after success. In-raid dismissal and declining an unaccepted invite do not charge this fee.

`FriendlyTeammateService.Deletion.cs` owns both deletion entry points. A `pending-deletion.json` journal records before/after money state before the SPT player save. Removing the member/profile, settings and Default commits atomically with the prepared courier outbox. Repeating a completed request returns the stash without charging or delivering again. Roster reads reconcile interrupted deletion: a matching after-state completes removal, a matching before-state keeps the member, and an ambiguous state blocks further deletion. The fixed fee has no credit for equipment still on the recruit; removal returns the current gear instead.

## Equipment Return on Member Removal

Removing either a normal or raid-recruited member sends the current saved equipment through the pitFireTeam courier. This applies to existing members without a migration. Both roster and social confirmations include **The equipped items will be mailed via service.** above **This cannot be undone**. Raid recruits still show and pay their recorded fee; manual hires and legacy unmarked members remain free to remove. A declined invitation or in-raid dismissal does not deliver a saved member kit.

The shared kit-delivery selector returns equipped items with complete attachment, armor-insert/plate and container-content trees, including loaded ammo, consumables, knife, armband and special-slot items. Pocket contents return individually; the structural equipment root, pockets shell and dogtag are excluded. The hidden managed secure container and its supplies are excluded outside Realistic; the editable secure-container tree returns in Realistic. Item condition and remaining stack counts are preserved. Only the current saved inventory is returned, never the separate Default snapshot, starting recruit inventory, or a newly generated loadout.

Preparation clones each tree and assigns fresh item IDs with one native mail stash root. It finds each actual root by ID rather than assuming the first saved array entry is the root. Existing player/member ownership collisions block preparation. The player stash does not need space because the return is a mail attachment, with the same 24-hour pickup window as existing kit courier deliveries. The pickup window begins when mail is attached, including when a pending outbox resumes after a restart.

The member deletion and `delivering` outbox commit in one encrypted-database transaction. Before that commit, failures retain the member and refund a paid fee through the verified money journal. After commit, a delivery failure retains the outbox and never refunds or recreates the member. Recovery saves the prepared mail to the SPT player profile and verifies a namespaced player receipt before marking the outbox `complete`. That receipt survives collecting or deleting the mail, preventing a crash/retry from creating another kit. Only one outbox can be active; another removal first recovers it. Unfinished payment journals from before courier returns capture the still-saved member inventory before completing deletion. The vanilla new-message notification follows the verified save.

Returned source IDs are recorded as courier ownership in the current and preceding raid insurance ledgers. Matching PMC policies and already queued insurance trees are removed, preserving unrelated claims. Removed member settings also remove that member's active coverage. Courier observation does not invoke settlement; later deferred claims respect the durable ownership evidence.

Source: [equipment selection](../server/Services/FriendlyTeammateService.EquipmentDelivery.cs), [removal delivery](../server/Services/FriendlyTeammateService.RemovalDelivery.cs) and [deletion coordination](../server/Services/FriendlyTeammateService.Deletion.cs). Run `dotnet run --project tests/TeammateHiring/TeammateHiring.csproj` for pricing, paid/free removal, selection, rollback, failed-save/receipt recovery, collected-mail replay and insurance cleanup cases. Run `dotnet run --project tests/FollowerDatabase/FollowerDatabase.csproj -- --courier-smoke <SPT-runtime-root>` for native SPT tree remapping, mailbox/database JSON round trips and insurance-ledger suppression. Unity confirmation layout and live mail collection still require in-game validation.

## Kit Purchase

`KIT LOADOUTS` is available in every mode: `Restricted`, `Immersive`, and `Realistic` / internal `Extreme`. Saved builds are available through this purchase flow only.

The buy screen reuses EFT's stock `EquipmentBuildsScreen` in a custom teammate-buy mode. The selected build is priced with market-facing item prices, including nested weapon mods, armor plates, armor inserts, magazine contents, and container contents. Weapon trees first try to use the best available overlapping assembled trader/barter offer; any extra unmatched parts then fall back to the gated kit discount where fuller kits can earn deeper weapon-only discounts. Armor, helmets, rigs, backpacks, loose/grid-contained items, ammo, meds, keys, cards, coins, and carried loot remain full price.

When the user confirms a purchase:

1. if `Use items in stash` is enabled, the client submits the exact stash item ids and quantities behind the checked summary rows; the server rejects changed, locked, or unsafe selections instead of choosing another matching item by template, and a selected container with unselected contents produces a warning explaining that it must be emptied or deselected
2. the server deducts the quoted rouble price
3. the server sends the teammate's previous active `Default` kit by courier delivery
4. because the previous kit is delivered by mail, stash space is not checked as part of the buy transaction
5. the selected build is cloned with fresh ids and saved as the teammate's current equipment and new `Default`
6. the player profile and teammate profile are saved together
7. the saved player stash snapshot is returned to the client for live refresh

The old-kit delivery intentionally does not include the teammate equipment root or dogtag. Pocket contents and special-slot items are delivered as normal reward roots. Non-Realistic managed secure containers are skipped because they are generated support inventory, while Realistic secure containers and their contents are delivered because Realistic treats that slot as player-like gear.

## Repair

Repair is available from the teammate loadout editor for repairable teammate equipment in all loadout-management modes.

Repair follows the real teammate-equipment rule:

- the repaired item is updated on the teammate's current `Default` equipment/profile
- player repair kits and repair-related player profile changes are consumed through the stock repair service
- saved player equipment presets are not changed by repair
- pending staged inventory changes are committed through the real stash transfer before repair

## Live Stash Refresh

The client does not use live item-move transactions for this feature. Earlier experiments with direct `Discard`, `Add`, `Move`, or inventory-controller transactions were unsafe for bot-to-player returns.

Instead, the client computes a backend-style stash delta from:

- the current live player stash
- the server-saved player stash returned by the commit route

That delta is applied through EFT's profile updater (`GClass2331.UpdateProfile`) while the loading indicator is visible.

Important behavior:

- player-to-bot transfers usually become `del` entries from the live stash
- bot-to-player transfers become `new` entries into the live stash
- when a returned item is placed inside an existing nested container, the client replaces the containing top-level container tree: delete the live container tree, then add the server-saved container tree
- this nested-container replacement is required because EFT's backend updater can add a top-level item tree into an existing container, but cannot target an existing nested live container for a standalone `new` item
- after EFT applies the delta, the client verifies the full live stash against the server-saved snapshot; silent partial updater failures therefore enter the fallback instead of being treated as success
- if the stock backend-style delta refresh cannot represent the change, the client rebuilds the live stash from the server-saved snapshot and attaches it through the active inventory controller so the normal stash grid retains a valid owner/address

## Spawn Preparation

Follower spawn preparation currently uses the saved teammate profile clone before returning follower details to the client.

For every mode:

- the teammate clone receives the configured health multiplier
- the teammate is guaranteed to have a `Scabbard` knife

For non-Realistic modes:

- saved `Default` snapshots do not keep a secure-container tree
- saved teammate profiles also strip the secure-container tree, so generated meds and ammo are not remembered between raids
- the spawn clone is guaranteed to have the mod-managed hidden secure container
- the secure container is prepared with the existing protected supply package
- this currently includes medical and ammo support items

For `Realistic` / internal `Extreme`:

- the secure container slot is not auto-managed during spawn preparation
- no protected meds or ammo are injected
- the edit-loadout panel shows the secure container slot so the player can add, remove, or change it through the staged `Default` editor

## In-Raid Backpack Inspection

Spawned squadmates can expose their live `Backpack` slot through the lower-left `View Backpack` quick interaction while the player is close and looking at them. Recruited allies are not eligible because the interaction is scoped to saved squadmates.

The screen is the follower's actual live backpack, not a cloned editor surface. The inspection session is intentionally out-of-combat only: active enemy/under-fire state, follower medical work, follower loot-pickup work, target invalidation, or player death closes the screen.

Items placed into the inspected backpack during the session are registered as tracked follower loot when the screen closes. Items that were already tracked and are removed from the backpack are unregistered immediately, so normal post-raid return handling does not try to return something the player already took back.

## Protected Extraction Filtering

`Restricted` allows teammate gear to be physically looted in raid so the player can inspect, reorganize, or recover from inventory edge cases without special slot locks. Commanded fallen-teammate recovery (`Check Him` / `Loot Body` on a teammate corpse) is stricter about protected roots: followers skip protected teammate gear roots, but non-protected backpacks and rigs are recovered as whole containers instead of being emptied item by item. To prevent gear farming, extraction and return-delivery cleanup strip protected teammate item ids from the extracted player profile or returned container tree.

Protected ids come from two sources:

- server fallback: saved teammate equipment in the teammate profile JSON
- client registration: live protected teammate equipment moved through teammate inventory during raid

Player-owned or return-tracked loot that temporarily moves through a teammate backpack is tracked for teammate return handling, not protected extraction stripping. If the player takes a previously return-tracked item back out of a teammate backpack during inspection, the client also unregisters that item tree from the protected raid set for compatibility with already-registered raid state.

The cleanup removes the protected item tree, including nested weapon mods, armor plates, rigs, backpacks, and contained items. If a non-protected player-owned child tree is attached under a protected teammate-owned parent, the server tries to move that child tree into the player's equipped backpack. If it cannot fit, it is lost with the protected parent.

Loose ammo is an explicit exception. Ammo can be split or merged into other stacks and magazines, which destroys the original item id lineage. The filter does not strip by ammo template/count because doing so could remove legitimate ammo found elsewhere in the same raid. When a protected loose ammo stack is extracted, the player profile keeps the same ammo template/count/state but receives a fresh item id so the teammate profile still owns the original protected id. Protected medical supplies are not part of this extraction exception.

## Death Handling

Current death-loss logic applies only to teammates whose selected loadout is `Default`.

When raid outcome persistence sees a teammate who:

- did not escape
- is in `Immersive` or `Realistic` / internal `Extreme`
- is currently using `Default`

the server strips the saved default equipment to:

- equipment root
- dogtag
- `Scabbard`
- knife item under `Scabbard`
- armband
- `SecuredContainer` and its contents, only in `Realistic` / internal `Extreme`
- special slots and their contents
- descendants of preserved items

The default equipment snapshot is then overwritten with that stripped equipment.

This makes a dead default-loadout teammate unable to regain full gear just by reusing the same saved `Default`, while still preserving bot identity slots that are not supposed to be lost.

If the player also dies in the same raid, already-dead teammates are still sent to the server as lost outcomes. This gear-loss persistence is not distance-gated by the player death location and does not depend on the `Team Escape` setting; that setting only controls escape rolls for teammates still alive when the player dies.

## Escape Handling

Current escape-state persistence applies only to teammates whose selected loadout is `Default`.

When an `Immersive`, `Realistic` / internal `Extreme`, or `Restricted` teammate with `Field Upkeep` enabled survives/extracts:

- the client sends the teammate's live equipment snapshot to the server
- follower progress and live-equipment persistence are serialized on the server so their independent post-raid requests cannot overwrite each other
- tracked follower-loot/player-given item ids are sent with the snapshot
- the server removes those tracked item trees before saving the new `Default`
- the server also removes protected gear ids owned by other teammates before saving, so gear looted from a fallen or different squadmate cannot be permanently saved onto the survivor
- durability, remaining ammo, and consumed meds from the teammate's actual raid equipment are preserved
- non-Realistic modes still strip the secure-container tree before saving; Realistic keeps it

When a `Restricted` teammate with `Field Upkeep` enabled dies while using `Default`:

- the fallen teammate's full equipment state is captured at death time
- the server saves that death-time state instead of reading the corpse after players or surviving teammates may have looted it
- tracked player-given loot and other teammates' protected gear ids are removed before saving
- protected teammate gear is still stripped from the extracted player profile and from escaped teammate equipment snapshots
- death gear loss is still not enabled; this is maintenance-state persistence, not Immersive loss

## Current Gaps

Direct custom preset selection has been removed; all equipment changes use real item ownership rules. The remaining planned loadout work is the future `KIT LOADOUTS` purchase hardening where consumed stash items can preserve their exact live durability/resource state when they become teammate equipment instead of using the saved build's item state.

Player-owned items handed to a teammate during raid are currently known only to the client at the moment of handoff. The server can derive saved teammate default gear from teammate profiles, but it cannot independently know every player-owned item that was temporarily moved through a live teammate inventory unless the client reports those item ids. The current protected-extraction filter uses a client side registration route for those handled item ids; a later pass should make this ownership/event reporting more explicit and durable instead of treating it as part of the extraction cleanup flow.
