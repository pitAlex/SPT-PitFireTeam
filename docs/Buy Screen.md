# Buy Screen

Date: 2026-05-14

## Scope

This document tracks the teammate `KIT LOADOUTS` flow.

The current implementation reuses EFT's stock `EquipmentBuildsScreen` and applies pitFireTeam-only changes when the screen is opened from a teammate profile. The purchase action is committed through a pitFireTeam server route that charges the player, consumes selected stash items when requested, saves the selected kit as the teammate's new `Default`, and returns to the teammate profile.

## Entry Flow

The teammate profile screen shows `KIT LOADOUTS` for non-`Simple` loadout-management modes, where saved loadout selection is hidden and `Default` is the real editable gear surface.

Pressing the button:

1. sets a teammate-buy-screen flag
2. captures the active teammate profile and profile-screen back override
3. disables the normal menu task bar while this custom screen flow is active
4. opens the stock `EquipmentBuildsScreen` through `EquipmentBuildsScreen.GClass3870`
5. uses the live player backend inventory controller so stock build-screen data and player build storage remain available

Authoritative client file:

- `client/Patches/TeammateEquipmentBuildsScreenPatch.cs`

Patch registration:

- `client/friendlyPlugin.cs`

## Screen Ownership

The stock `EquipmentBuildsScreen` is reused, so the mod must treat it as a shared screen instance.

The buy-mode flag is active only for the teammate buy flow. When the screen closes, returns to the teammate profile, or is opened normally from the player character screen, pitFireTeam restores stock UI state and clears the custom mode.

Back handling is custom while buy mode is active:

- stock back button returns to the teammate profile screen
- alternate back path returns to the teammate profile screen
- `Escape` returns to the teammate profile screen
- normal navigation history is bypassed to avoid returning to stale or unintended screens

## Teammate Preview

The center preview keeps the selected stock build equipment, but swaps the displayed character identity to the teammate profile.

The preview uses:

- teammate nickname
- teammate level
- teammate side and member category
- teammate customization
- selected build equipment from the stock screen

This lets the user preview how the selected build looks on the teammate without changing the selected build itself.

## UI Changes In Buy Mode

The screen is modified only while the teammate buy flag is active.

Build list rows:

- hide `DeleteHolder`
- replace `WeightIcon` with `icon_info_money_roubles_big.png`
- replace weight value with estimated rouble kit price
- suppress stock overweight warning colors

Right gear panel:

- change `EQUIP` button text to `Purchase` / `EQUIP`
- add an off-by-default `Use items in stash` checkbox after `Purchase` / `EQUIP`, cloned from EFT's `EditBuildScreen/Toggle Group/OnlyAvailable` toggle
- hide `CanEquip` icon
- hide `AdditionalInfoPanel/WeightPanel/CurrentWeight`
- suppress the stock red missing-item marking in the gear preview

Bottom screen chrome:

- hide stock bottom panels so the screen behaves like a focused teammate-buy surface

Context interactions:

- hide `Edit build`
- hide `Filter by item`
- hide `Linked search`
- hide `Required search`

Inspect overlay:

- hide `InteractionButtonsPanel` inside the item info window while buy mode is active

## Price Display

The first implementation used Prapor's sell-to-trader valuation, which made kits far too cheap. That path was wrong because `TraderClass.GetUserItemPrice(...)` answers "what will this trader pay the player for this item", not "what does this item cost to acquire".

The current display uses a market-facing bundled-kit estimate:

1. when the buy screen opens, the client requests `/client/items/prices` through `ISession.RagfairGetPrices(...)`
2. the result is cached as `templateId -> price`
3. the screen refreshes visible selected-build and build-list prices when the price table returns
4. each build price walks every equipped item tree with an explicit recursive traversal and sums every nested item, including weapon mods, rig contents, armor inserts, and armor plates
5. price adjustment uses EFT's single-item buyout helper so durability, resources, med HP, food/drink resource, repair-kit resource, and similar item state still influence value
6. for each weapon tree, the client first looks for live Ragfair rouble offers on the same base weapon template
7. if a Ragfair offer covers at least 80% of the selected weapon tree by raw matched value and is close enough to the selected weapon's condition, the quote treats that assembled offer as the base price and then adjusts only the unmatched attachment difference
8. if no useful Ragfair near-match exists, the client looks for the best available exact assembled trader/barter offer at the player's current trader loyalty
9. if no useful exact assembled trader offer exists, weapon roots and installed weapon parts use the conditional pitFireTeam kit discount fallback
10. the primary weapon fallback can reach a 40% discount only when the kit has armor or an armored rig, a helmet, and either one spare compatible magazine for external-mag weapons or compatible loose ammo for non-magazine weapons; missing pieces reduce the discount in 10% steps, and a weapon-only preset receives no fallback kit discount
11. the secondary weapon fallback is only eligible when the primary fallback reaches 40%, and can reach a 25% discount when it has spare compatible magazines or compatible loose ammo
12. the pistol fallback is only eligible when the primary and secondary weapon trees already received fallback discounts, and can reach a 40% discount when it has a spare compatible magazine or compatible loose ammo
13. armor, helmets, rigs, backpacks, loose/grid-contained items, ammo, meds, keys, cards, coins, and carried loot stay at full value, except armored vests and armored rigs with plate systems can discount the armor setup itself by up to 20%, scaled by how many of their available plate slots are populated
14. if a template is missing from the market table, the fallback is `HandbookClass.GetBasePrice(...)`

The estimate intentionally does not:

- call `TraderClass.GetUserItemPrice(...)`
- apply Prapor's sell-to-trader modifier
- use stock equipment-build weight
- use stock overweight coloring
- include the `Dogtag` equipment slot, because dogtags belong to profiles and are not kit-sale items
- include the `SecuredContainer` slot or its contents outside `Realistic`, because non-Realistic modes auto-manage teammate secure containers during spawn preparation

This estimate is the price submitted to the server-side buy-kit route. The route still owns the actual profile mutation and can reject the request if the player no longer has enough roubles or required stash items.

## Missing Item Availability

Stock `EquipmentBuildsScreen` checks whether the selected build can be assembled from the player's stash. It passes the missing item list into the preview item context, which marks missing gear and containers with red warning state.

In teammate buy mode, pitFireTeam keeps the availability data but suppresses the stock red marking:

1. `EquipmentBuildsScreen.method_10(...)` receives stock `notFoundItems`
2. the mod copies those items and summarizes them as `templateId -> missing count`
3. the mod passes an empty missing list back into stock preview rendering
4. stock build preview renders without missing-item red state

The retained missing-count data is part of the quote source of truth. The selected build provides the full deep requirement list, and stock `notFoundItems` provides the deep requirements EFT could not source while simulating the build.

The teammate buy quote then caps stock availability with a nested scan of `Inventory.Stash`. This third list is the actual `Use items in stash` set: items EFT can source, minus anything that is only available because it is currently equipped on the player. The stash scan walks through boxes, backpacks, assembled guns, armor plate slots, and other nested containers under the stash root. Each match retains its exact live stash item id and quantity. A container or assembled item is only matched as a whole when all of its non-structural descendants are also selected; otherwise the parent stays in the stash and independently safe nested items can still be matched.

The visible checkbox uses the label `Use items in stash` and is reset to off each time the teammate buy screen opens. It is cloned from the stock edit-build `OnlyAvailable` checkbox so it keeps EFT's native checkbox visuals and hover/click behavior.

## Current Action Button

The `Purchase` / `EQUIP` button opens a confirmation overlay and then performs the real kit transaction.

When clicked in buy mode:

- stock equip behavior is blocked
- the selected equipment build is read from `EquipmentBuildsScreen`
- the visible quote is calculated from the same market-facing pricing path used by the build list
- if `Use items in stash` is off, the quote uses the full kit price
- if `Use items in stash` is on, the quote uses a pitFireTeam stash-only availability pass and does not count gear currently equipped on the player
- the quote builds its requirement list from the full selected equipment build collection, not only visible slot roots, so nested weapon mods, armor plates, armor inserts, magazine contents, and container contents are itemized
- structural EFT container roots such as the saved equipment object and pockets container are ignored as purchasable requirements; their child items are still scanned and counted
- built-in armor/helmet inserts such as integral material layers are ignored because they are descriptive/internal components, not buyable or swappable kit parts
- when stash items are matched, the overlay shows each matched quantity as a checked, left-aligned row with a 40x40 item icon and divider, for example `60 x ammo`, `2 x flashlight`, or `grip`
- icon frames and text rows are laid out first; item icon sprite generation is started afterward in a batch so delayed EFT icon generation does not block or disturb layout
- EFT's icon sprite requests are not directly cancellable, so closing the overlay invalidates the current icon-generation token, unsubscribes icon-change bindings, and ignores any late sprite callbacks or retries
- the player can uncheck matched stash entries before confirming; unchecked entries are moved back into the purchased list and the quoted price/action button updates immediately
- when the stash only covers part of the selected kit, or when the player unchecks matched stash entries, the overlay lists the remaining deep item requirements that will be purchased as matching icon rows with dividers
- repeated lines with the same visible item name are grouped for overlay readability
- the stash-used list prefers full localized item names; EFT short names are only a fallback because some stock short names are intentionally truncated
- the stash-used list is scrollable so large nested kits do not hide entries behind ellipsis
- before the confirmation overlay opens and again before purchase, the client checks current stash roubles and rebuilds the exact `Use items in stash` id/quantity plan
- if resources are not available, a `Not enough resources to purchase {name} kit` overlay opens with an `OK` button and no purchase action
- a confirmation overlay asks the user to purchase the selected kit for the quoted price; its header is `{kit name} - {price}` and updates whenever stash-use checkboxes change
- if `Use items in stash` covers every required kit item, the overlay skips the purchase question, shows only the stash-take list, and the action button is `EQUIP`
- pressing `Purchase` / `EQUIP` inside the overlay shows the loader, posts the selected build tree and only the currently checked stash-use entries to `/singleplayer/pitfireteam/teammate/profile/buy-kit`, refreshes the live player stash from the server response, queues the teammate roster portrait for regeneration, closes the overlay, and returns to the teammate profile

Server-side transaction behavior:

- validates the teammate and selected build tree
- if `Use items in stash` is enabled, consumes only the exact item ids and quantities behind the checked summary rows from the player's stash tree; changed, locked, missing, duplicate, and unsafe partial-tree selections are rejected
- removing a selected container or assembled item requires every non-structural descendant that would be removed with it to be selected in full, preventing unrelated contents from disappearing
- if a selected container still has unselected contents, the client shows a localized rejection warning telling the player to empty or deselect that container
- deducts the quoted rouble price from rouble stacks under the player's stash tree
- sends the teammate's previous active kit roots through the pitFireTeam courier before replacing the teammate equipment, so stash space is not part of the buy transaction
- when delivering the previous kit, the equipment root and dogtag are not delivered; pocket contents and special-slot items are delivered as normal reward roots; non-Realistic managed secure containers are skipped; Realistic secure containers and their contents are delivered
- clones the selected build with fresh ids and saves it to the teammate as the current equipment and new `Default`
- switches teammate loadout selection back to `Default`
- saves the player profile and teammate profile together, rolling both back if the commit throws
- returns the saved player stash tree so the client can refresh without a restart

## Restore Requirements

Because the screen is shared with the stock player character flow, every buy-mode UI mutation must be restored on exit.

Currently restored state includes:

- equip button text
- equip button raw-text mode
- buy-mode `Use items in stash` checkbox removal
- `CanEquip` visibility
- hidden bottom panel visibility
- hidden current-weight visibility
- build-list delete holder visibility
- build-list weight icon sprite/enabled/preserve-aspect state

Any future UI changes on this screen should follow the same capture-then-restore pattern.

## Open Work

The current buy-kit commit removes matching stash resources, sends the teammate's previous active kit by courier delivery, and equips a fresh-id clone of the selected build. A later hardening pass can make the teammate equipment preserve exact live stash item durability/resource state for every consumed item instead of using the selected build's saved item state.
