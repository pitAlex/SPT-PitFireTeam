# Give Gear specification

Status: proposed, not implemented. Updated: 2026-10-16.

This document records the agreed requirements for a new Core interaction. It is not a description of current functionality or authorization to begin implementation. Explicitly unresolved policies are collected at the end rather than silently chosen here.

Related contracts: [Swap Gear](Swap-Gear.md), [looting](looting/Looting.md), [primary acquisition](looting/Weapon-Pickup-Primary-Slot-Available.md), [secondary support](looting/Weapon-Pickup-Secondary-Slot.md), [holster support](looting/Weapon-Pickup-Holster-Slot.md), [loadout ownership](Loadout-Management.md) and [friendly Scav cargo](looting/Friendly-Scav-Cargo.md).

## 1. Purpose and scope

Give Gear is a one-way, staged handover to a follower recruited during the raid. It allows the player to offer useful equipment without exposing the recruit's current inventory for manual stripping.

| Target / interaction | Required behavior |
|---|---|
| Spawned saved teammate | Existing Swap Gear remains available under its existing rules |
| Recruited PMC follower | Give Gear is available |
| Recruited Scav follower | Give Gear is available with the same equipment-upgrade rules as a PMC recruit |
| Unrecruited friendly or another player's follower | Give Gear is unavailable |
| View Backpack | Existing eligibility and immediate live backpack transfers remain unchanged |

PMC/Scav here identifies the recipient. This feature does not broaden recruitment eligibility or player-side rules. A field recruit receiving gear does not thereby become a saved squad member.

Give Gear is independent of saved Pickup Weapons / Pickup Gear settings. Its explicit handover authorizes evaluation of the offered items for this session only; it must not alter saved toggles or later autonomous looting.

There is no exhaustive search for an optimal kit. The new comparison work is deliberately limited to a primary-first decision, armor/helmet protection, the supporting vest arrangement and backpack capacity. Existing secondary/holster logic handles support roles afterward. Other equipment follows compatible-empty-slot acquisition rather than occupied-slot upgrade comparisons.

## 2. Interaction and staging UI

- Use the existing close-range follower targeting convention: 2.5 m and an 18-degree look cone.
- Admit only the player's living, active, currently recruited follower. Preserve peaceful-state, healing, reload, hands, grenade, looting and inventory-busy safety checks.
- Open an inventory screen with the player's normal inventory on the left and a donation container grid on the right, not the recruit's equipment slots.
- The donation grid is 10 columns by 12 rows: the dimensions verified in the installed standard airdrop supply/weapon container data.
- Borrow those dimensions only. The staging surface is not a real airdrop, backpack or transferable item. It must not inherit an airdrop item's weight, become loot, or modify a shared item template.
- The right-side header identifies the recipient and provides **Give Items**, replacing the role of Swap Gear's Apply Changes action.
- The grid starts empty. Existing recruit equipment and contents are never exposed or moved into the visible grid.
- The player can add, rearrange and withdraw their own offered items before submission. Native restrictions on genuinely non-transferable items still apply.
- All edits affect independent clones. No live inventory, item ownership, insurance, equipment effects, hands or world loot changes before Give Items.
- Draft discard, consume, repair and other non-replayable operations are unavailable, as with the existing staged editor.
- Closing, escaping, combat, actor death, lost follower ownership or raid teardown before commitment discards the draft. No temporary container or draft item may survive into a profile or raid snapshot.

The anti-stripping boundary applies to shortcuts and nested windows as well as visible controls. The player may retrieve only their own staged offering before submission, never the follower's existing gear.

## 3. Independent, incremental handovers

Every Give Items submission evaluates the bot's current live kit, not its spawn kit and not a kit remembered from a previous editor session. Revalidate that live state before committing.

Useful pieces may arrive together, separately or never. A weapon upgrade is not a prerequisite for accepting other upgrades, and a gear upgrade is not a prerequisite for accepting a weapon that the current kit can support.

Required examples:

| Offering / current situation | Expected decision |
|---|---|
| Inferior weapon, better usable armor/gear | Keep current primary; evaluate the gear around that weapon |
| Better weapon that current vest/armor can support | Accept the weapon without requiring new gear |
| Better weapon plus a suitable incoming vest | Evaluate that supporting arrangement for the selected weapon |
| Better gear arrives in a later handover | Evaluate against the weapons and magazines carried at that time |
| A previous offered weapon remains carried as cargo and later gear makes it usable | Reconsider that actually carried candidate during the later handover |
| A previous offering was dropped, consumed or otherwise lost | Do not count it as available equipment or supply |
| No weapon offered | Continue armor/helmet/backpack and empty-slot decisions using the current primary |

A failed weapon comparison or failed support check must not cause an early exit that skips unrelated useful donations. Do not wait for a promised future gift or reserve imaginary future capacity.

Reconsideration of earlier carried gifts is part of an explicit later Give Items evaluation, not a new autonomous world-loot scanner or a permanent pending-upgrade queue.

## 4. Bounded primary-first evaluation

### 4.1 Select the primary candidate first

1. Identify incoming primary-eligible weapons, plus eligible earlier Give Gear weapons still carried as cargo when reconsideration applies.
2. Rank the weapons before choosing a vest arrangement. With two incoming weapons, choose the best primary candidate first; drag order must not decide the winner.
3. Compare that candidate against the current primary. An occupied primary is replaced only by a meaningful usable upgrade. A compatible empty primary does not require beating a nonexistent weapon.
4. If no primary upgrade is selected, retain the current primary and continue the other categories.

Weapon quality must be grounded in usable combat capability, not price alone. Incomplete weapons and ammunition that cannot actually be used do not become an upgrade merely through a high score. The usable source pool is the offering plus permitted follower-carried supplies, never items left in the player's inventory.

Exact scoring weights, improvement margin and the runner-up fallback policy are not yet approved; see section 10. This specification does not authorize a cross-product search of all weapons, armor pieces and vests.

### 4.2 Establish support for the selected primary

After selecting the primary candidate, check whether it can be accepted using current gear or a coherent incoming gear arrangement.

- Current armor/vest may remain when already sufficient.
- Incoming armor/vest may be used when compatible and useful for the selected primary or the armor improvement.
- The current arrangement may be an armored rig while the incoming arrangement is separate body armor plus a tactical vest, or the reverse. Evaluate that local transition together; do not equip incompatible layers one at a time.
- A smaller incoming vest is not automatically a downgrade. It is acceptable when the resulting armor arrangement is appropriate and its real grids/pockets support the selected primary's magazines and reload requirements.
- Tactical vest selection is a supporting decision, not a separate general-purpose rig optimizer.
- Actual dimensions, rotation, grid filters and occupied cells matter. Total cell count alone is insufficient.
- Preserve item identities, attachments, installed plates, durability and stack quantities. Existing contents need real destinations; replacing a container cannot delete its contents.

For example, better separate armor plus a smaller vest and a better primary can be accepted even when the current armored rig has more total cells, provided the resulting outfit is compatible and the operational supplies fit.

If the primary candidate cannot be supported, do not force it into service. Continue independent gear decisions against the retained current primary. Unused offered weapons follow carry/drop handling.

### 4.3 Primary magazines take priority

Allocate the selected primary's usable magazine/ammunition support before attempting additional secondary or holster acquisition.

Use existing readiness, ammunition-compatibility, fast-access placement and reload-reserve rules, including the appropriate internal-magazine/chamber-fed handling. This feature must not create a second, inconsistent ammunition-readiness model.

Reloads are sequential. Retained weapons may share the same free magazine landing opening when each magazine fits it independently; do not reserve simultaneous ejection cells for every weapon. Preserve native reload safety for every weapon that remains operational.

Register accepted magazines against the exact weapon through existing weapon-package bookkeeping. Magazines belonging only to a weapon no longer used as equipment do not retain operational vest-space priority merely because they were once registered.

### 4.4 Run existing secondary and holster analysis afterward

Once the proposed primary and supporting gear are decided, invoke the existing support planners against that proposed arrangement and its remaining real capacity, before live transfer.

- Consider the displaced old primary as a secondary candidate if the secondary slot is available and its ammunition package can be supported after the new primary's needs.
- Other unselected offered weapons may be eligible support candidates under the existing rules.
- Do not force the old primary into secondary solely because it was previously equipped.
- Do not add a new occupied-secondary or occupied-holster upgrade scorer. Existing support acquisition fills compatible empty slots; it does not replace occupied support slots.
- Reuse their usability, magazine-placement, ammunition and shared reload-space decisions rather than duplicating them.
- Their normal pickup-authorization gates need a session-owned Give Gear entry point, without modifying saved pickup settings.
- Failure to acquire an additional support weapon must not undo an otherwise valid primary upgrade merely to make room for that additional weapon.

Existing retained support equipment remains subject to final compatibility/reload validation. The new feature must not silently leave a retained weapon with a broken reload arrangement.

## 5. Other equipment decisions

### Body armor and helmets

An occupied armor/helmet position is replaced for higher usable protection, not cosmetic or resale value alone. Examine installed protection and remaining condition, including plate-based carriers and helmet parts; do not treat an empty carrier as fully protected because of its name or price.

Armor can improve while the primary remains unchanged. A helmet upgrade is independent of the primary/vest decision, subject to actual equipment conflicts. Higher-protection comparison details remain to be calibrated before implementation.

Keep installed plates and attachments with their parent equipment. General automatic plate replacement or weapon attachment optimization is outside this initial feature; loose parts can remain ordinary offered cargo.

### Backpack

- Fill an empty compatible backpack slot when possible.
- Replace an occupied backpack only with a larger-capacity candidate, not merely a more expensive one.
- Before acceptance, prove that all current backpack contents can be moved into the candidate using real placement rules, accounting for contents already inside it.
- Do not accept the replacement by silently deleting or dropping current contents to make it fit.
- Failed migration leaves the current backpack and contents intact; other donation categories can still succeed.
- Disposition of the displaced backpack shell follows the unresolved original-gear policy in section 10.

Backpack evaluation may establish available carrying capacity before final generic cargo placement. It must not require an arbitrary ordering of the player's drag operations.

### Remaining items

Other equipment uses compatible-empty-slot acquisition, not occupied-slot upgrades. For example, an offered headset may fill an empty earpiece slot but does not initiate a price-based replacement of an occupied one. Secondary/holster weapon acquisition remains owned by the existing support planners.

Items not equipped are carried in valid available space. Player-offered items that cannot be equipped or carried are dropped on the ground after submission. They are not deleted, left in a disappearing virtual grid or returned to the player through an invented mail path.

Giving medical supplies or grenades does not independently enable prohibited medical/combat behavior or override existing bot permissions.

## 6. Commit, interruption and conservation

Give Items must first produce a complete, validated plan with explicit equip, carry and drop outcomes. No partial live experimentation while comparing candidate gear.

Implementation should reuse the existing clone/snapshot, replay/rollback, hands-transition and body-refresh primitives. The current Swap Gear controller/session and protection hooks are session-specific; share or adapt the necessary contracts explicitly rather than weakening Swap Gear admission or pretending a recruit is spawned.

Required safety properties:

1. Revalidate recipient identity/ownership, actor safety and opening inventory snapshots before commitment. Never overwrite intervening damage, ammunition consumption or inventory changes with a stale clone.
2. Retain correctable drafts when validation fails before any live change, so the player can revise an offering.
3. Extend the bounded Apply-only transfer protection to the actual Give Gear session/recipient. Editing remains unprotected and cancellable; ordinary bots, the player and shared AI settings remain untouched.
4. Replay reversible inventory changes on the Unity thread with rollback for failed pre-commit settlement. Keep item IDs, quantities and ownership consistent.
5. Refresh native weapon records/selection, magazine registrations, equipment events, grenade selection and body/hands bindings before releasing the recipient.
6. Execute actual world drops last and account for each root exactly once. A world drop is an irreversible boundary, not an ordinary clone move.
7. Failed drop creation must leave the real item accounted for with a real owner. Do not delete it, clone a replacement or claim full success for an incomplete handover.
8. Combat/death/raid teardown must distinguish pre-commit cancellation from post-commit cleanup. After authoritative commitment, do not restore player items that the bot or world already owns. Stop late callbacks and release every temporary hold/protection.

The exact native drop/ownership mechanism needs source and installed-metadata verification before coding. This spec does not claim that all world-drop effects can be atomically rolled back with inventory moves.

## 7. Ownership and persistence boundaries

- Before commitment, all offered items remain the player's real property; clones do not register insurance or return ownership.
- The staging container is never saved, insured, mailed, spawned or treated as follower equipment.
- Do not register the recruit's original kit as player-owned loot simply because it participated in a comparison or container migration.
- Preserve exact-item insurance state and existing protected-teammate-item exclusions; copying editor state must not grant or strip coverage.
- Do not broaden saved-squad return admission. A PMC field recruit remains a field recruit; its existing invitation/profile flow is separate from squad return mail.
- Scav field cargo has a separate Fence flow and no saved roster profile. Donation settlement must not blindly call `AllegianceScavCargo.TrackGiven` until the gift-versus-return policy is decided.
- Weapon/magazine operational registration is distinct from mail-return ownership. Successful support registration alone must not grant a field recruit saved-squad return eligibility.
- Giving gear must never produce both a retained/invitation copy and a mailed copy of the same item tree.

## 8. ORBIT reference and implementation boundaries

ORBIT 2.0.0 source supplied for this investigation contains `WeaponScorer`, `ArmorScorer`, `RigScorer`, `BackpackScorer`, `HeadsetScorer` and corresponding swappers under `Orbit/Looting/WeaponSwap`.

Useful reference behavior includes ammo-aware weapon ranking, per-category comparisons, improvement margins and pre-swap content migration. It is not the complete Give Gear policy:

- Its 1.10 swap margin is a reference value, not an approved Give Gear constant.
- Its ordinary NPC Scav path generally equips empty slots only; Give Gear must allow the agreed primary/armor/backpack upgrades for recruited Scavs too.
- Its rig swapper excludes armored-rig/separate-armor transitions; Give Gear needs the bounded supporting transition described above.
- Its swaps can send displaced gear back to a corpse/source address. Give Gear cannot expose original gear in the visible donation grid or leave real equipment in a temporary container.
- Its price contributions and cell-count guards do not replace usable-protection checks or actual grid-fit simulation.
- Its live transaction orchestrators must not be transplanted as the staged handover engine.

Keep this feature in Core and independent of an installed ORBIT plugin or the optional SAIN addon. Preserve MIT attribution if substantial ORBIT code is reused. Machine-local source locations belong in LOCAL.md, not portable setup instructions here.

Existing integration boundaries to inspect during implementation:

- `client/Patches/QuickPanelPatch.cs` and `client/Patches/PlayerPatch.cs`: quick interaction visibility and dispatch.
- `client/Modules/TeammateBackpackInspection.cs`: targeting and peaceful/busy safety helpers; immediate inspection remains unchanged.
- `client/Modules/TeammateGearSwap.cs`, `GearSwapInventoryController.cs`, `GearSwapEdit.cs`, `GearSwapSnapshot.cs`, `GearSwapTransaction.cs`: staged ownership and replay primitives.
- `client/Modules/GearSwapApplyScope.cs`, `GearSwapHandsTransition.cs`, `GearSwapBodyRefresh.cs` and `client/Patches/GearSwapApplyProtectionPatch.cs`: bounded transfer lifecycle.
- `client/BigBrain/Actions/GestureCommandAction.GearSwapping.cs` and related readiness/magazine planners: secondary/holster and fast-access support rules.
- `client/Modules/FollowerLootedPrimaryWeaponBinding.cs`: post-commit native weapon-cache reconciliation.
- `client/Modules/InteractableObjects.cs`, `FollowerReturnPolicy.cs`, `AllegianceScavCargo.cs` and `BossPlayers.cs`: registration, ownership and post-raid boundaries.

New interaction/button/warning text must use the central language model and embedded fallback, with matching locale resources. Do not add per-callsite English fallbacks.

## 9. Acceptance and qualification matrix

| Scenario | Required result |
|---|---|
| Own recruited PMC or Scav | Give Gear opens under the same safe admission rules |
| Unrecruited, dead, hostile, dismissed or foreign-owned target | No opening or transfer |
| Spawned saved teammate | Existing Swap Gear/View Backpack behavior unchanged |
| Move items into/out of the donation grid; cancel | Both live inventories unchanged |
| Attempt to access the recruit's kit or transfer the virtual container | Rejected |
| Two incoming long guns | Best primary candidate considered first, regardless of drag order |
| Better primary, no incoming vest | Current suitable gear permits the weapon upgrade |
| Worse weapon, better armor/helmet/backpack | Weapon failure does not skip independent useful gear |
| Better weapon plus separate armor/smaller vest replacing armored rig | Accept when compatible, better as required and operational supplies fit |
| Enough total cells but no fitting magazine geometry | Reject that support arrangement |
| Several retained weapons sharing one fitting reload opening | Do not demand simultaneous ejection capacity |
| New primary uses available support space | Optional old-primary/other support acquisition cannot steal required capacity |
| Secondary/holster already occupied | No new occupied-slot upgrade comparison for those slots |
| Empty support slot and usable offered/demoted weapon | Existing planner decides ammunition and placement eligibility |
| Pickup Weapons/Gear disabled in saved settings | Explicit Give Gear evaluation still works; settings unchanged |
| Empty headset slot / occupied headset slot | Compatible empty-slot fill / no headset upgrade replacement |
| Larger backpack with all contents fitting | Replace and conserve both old contents and donated contents |
| Larger backpack whose shape/filters/occupied space prevents migration | Keep current backpack; no forced content loss |
| Weapon first, better gear in a later handover | Later decision uses the current kit |
| Previously gifted weapon still carried; new supplies/gear now support it | Eligible reconsideration during the later handover |
| Earlier gift no longer carried | Not counted or recreated |
| Unusable/full-capacity offered item | Real carry/drop outcome, never silent disappearance |
| Split/merge or nested donated tree | Quantity/provenance conservation; evaluate nested equipment only under the agreed nesting policy |
| Combat, death or raid end while editing | Draft discarded without live transfer |
| Transfer/drop failure, actor death or teardown during Give Items | No duplicate IDs, orphaned items, late continuation or leaked protection |
| Successful new weapon and magazines | Correct held/body models, firing/reloading and exact package registration |
| Extraction/invitation/Fence/insurance settlement | No original-kit leakage or retained-plus-mailed duplication |

Use production-derived planner/transaction fixtures, installed API checks and Debug builds for offline verification. Separately raid-test PMC and Scav recruits under Core and ready external-SAIN/addon paths, including repeated handovers, cancellation, world drops and extraction. Fixture/build success is not live UI, combat or persistence qualification.

## 10. Explicitly unresolved policies

The requirements above do not silently settle these points. Resolve them before implementing the affected item-disposition paths:

1. **Displaced original kit and its contents.** Can it be carried and then dropped if it does not fit, or must an upgrade be refused unless the original gear remains on the follower? Ground-dropping original kit can permit equipment farming even though the editor never exposes it. The approved drop fallback for player-offered overflow does not automatically authorize discarding the recruit's original kit.
2. **Nested offerings.** Should weapons/supplies inside an offered backpack or rig be automatically unpacked/evaluated, or should that tree stay intact unless the player separates it in the grid? The earlier recommendation was to evaluate contained gifts without stripping installed attachments, but that recommendation has not been explicitly approved.
3. **Scav gift versus Fence-return ownership.** Do donations stay with the Scav, enter Fence-return cargo, or distinguish equipped kit from carried leftovers? Do not infer the answer from existing commanded-loot tracking.
4. **Scoring calibration and bounded fallback.** Final weapon/protection weights and improvement thresholds remain to be chosen. If the highest-ranked weapon cannot be supported, whether to try the next-ranked weapon in a simple linear pass or keep the current primary is also unresolved. Neither choice permits an exhaustive kit-combination optimizer.
