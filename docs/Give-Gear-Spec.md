# Give Gear specification

Status: implemented; raid qualification in progress. Updated: 2026-10-19.

This document records the Core Give Gear contract and its initial implementation. Implementation was authorized on 2026-10-17. The clarified disposition, nesting, invitation and insurance rules are collected in section 10. Build/fixture success does not establish live raid behavior.

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
- Give Items uses the same centered header row and bold native Back-button styling as Apply Changes, with a layout-independent wrapper repositioned after native layout. Quick interaction, dropdown and native command-name text all resolve Give Gear explicitly.
- The grid starts empty. Existing recruit equipment and contents are never exposed or moved into the visible grid.
- The player can add, rearrange and withdraw their own offered items before submission. Native restrictions on genuinely non-transferable items still apply.
- Give Items uses the same native loading indicator and localized do-not-close transfer notice as Swap Gear's Apply Changes. The action is hidden while processing; if a correctable failure keeps the draft open, restore the action and hide the transfer feedback. Closing the panel destroys the session-owned feedback with its header wrapper.
- Restricted-mode protected teammate equipment may be handed over and used temporarily during this raid. It follows normal staged transfer and upgrade checks, without a special preview/Stage/Apply prohibition. Ownership remains with the original saved teammate: borrowed gear is not a permanent donation, does not cancel its owner's insurance, grants no permanent-gift slot credit, is not returned by courier/Fence and is stripped from recruit invitations.
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
3. Compare that candidate against the current primary. An occupied, supported primary is replaced only by a meaningful usable upgrade. A depleted primary that cannot be replenished may instead be replaced by a fully supported weaker candidate under the readiness fallback below. A compatible empty primary does not require beating a nonexistent weapon.
4. If no primary upgrade is selected, retain the current primary and continue the other categories.

Weapon quality must be grounded in usable combat capability, not price alone. Incomplete weapons and ammunition that cannot actually be used do not become an upgrade merely through a high score. The usable source pool is the offering plus permitted follower-carried supplies, never items left in the player's inventory.

The initial centralized scorer uses compatible ammunition penetration × 2 plus damage × 0.2, at most 90 available rounds × 0.4, ergonomics × 0.4, minus recoil × 0.05. Loaded compatible ammunition determines quality first; otherwise the best permitted available ammunition is used. An occupied primary requires a score above current × 1.10 + 1. Functional parts, condition, compatible ammunition, native readiness and real reload space are hard gates independent of the score. Candidates are tried best-first with stable item-ID tie-breaking; an unsupported candidate rolls back before the next one. These are initial calibration values, not an exhaustive cross-product search.

Apply a bounded cadence multiplier to that score for both candidate ordering and comparison with the current primary. Native `WeaponTemplate.BoltAction` gets ×0.75. Otherwise, weapons with full-auto/burst capability get no modifier, independent of the currently selected fire mode. A single/semiauto-capable weapon without either faster mode uses its template `SingleFireRate`, locally floored at EFT's native 240 RPM: at 360 RPM or above use ×1; below 360 interpolate linearly from ×0.75 at 240 to ×1 at 360 (300 gives ×0.875). Missing/invalid rate or mode metadata gets no guessed penalty. These are initial comparison calibrations, not measured bot firing rates or a hard rejection of slow weapons. Large ammunition/quality advantages can still outweigh the modifier; every readiness, outfit and reload gate remains mandatory.

Do not read `Weapon.SingleFireRate` for this calculation: its getter repairs invalid shared templates. Read the template field without writes and mirror only the local floor. Do not use displayed `bFirerate` as semi-auto cadence: installed pistols/SKS can show very low displayed rates while their native single-shot cooldown limit is 450 RPM. The installed STM-9 and MPX both use 450 and receive no penalty. `ManualBoltCatch` is not a per-shot manual-action flag. Comparison logs include candidate/current cadence multipliers and reasons; no combat firing, selector state or shared settings are changed.

After cadence, add a separate general-purpose effective-range credit: `0.2 × clamp(WeaponTemplate.bEffDist, 0, 300)` points, capped at 60. This is the same native metadata used for EFT's EffectiveDist attribute, not sight zeroing, optic magnification, maximum projectile travel or a new physical ballistics estimate. Missing/non-positive metadata adds nothing. Capping at 300 prevents a 1000 m sniper or an extreme mod value from overwhelming the rest of the comparison; a 300 m and a 1000 m weapon get the same maximum credit. Add this credit after cadence so slow cycling does not erase the value of usable rifle reach. The installed SV-98 (1000 m) gains 60 points; MP-133 (70 m) gains 14. Both STM-9 and MPX list 500 m and get equal capped credit. Candidate ordering and current-primary comparison use the same final score; quality margin, ammunition readiness, functional condition and actual reload support remain mandatory. These values are initial selection calibration, not measured hit probability or an unconditional rifle-over-shotgun rule.

Cadence diagnostics describe the native single-shot cooldown rather than calling every single-mode weapon a semi-auto. In particular, the MP-133 is pump-action despite BoltAction=false and SingleFireRate=450; that cooldown metadata alone does not establish its full animation-limited cycling rate. The range change does not add an equal pump/bolt penalty or change either weapon's real firing behavior. Comparison logs include raw effective distance and capped range credit for both weapons.

Readiness fallback: before rejecting a weaker candidate, check the current primary's shared native ammunition readiness. If below the normal minimum, first try to restore it using eligible carried/offered magazines and loose ammunition, with the existing bounded current/separate-armor/armored-rig arrangements. Keep the stronger primary if this succeeds. If every replenishment attempt fails, waive only the quality-upgrade threshold for the best-first replacement candidates; they must still pass the complete ordinary functional, ammunition, fast-access and reload-space gates. A mere ammunition-quantity advantage cannot displace a ready or replenishable primary, and two unready packages do not force a swap. Failed replenishment/replacement trials restore exact equipment and ammunition quantities. This happens only during explicit Give Items evaluation, not as a new autonomous cargo-weapon scanner. Logs distinguish current replenishment and `currentPrimaryUnready` fallback from normal quality rejection.

The shared minimum is caliber-based actual usable rounds, not magazine capacity: pistol/SMG and intermediate rifle calibers require 60; 9×39/.366 TKM 40; 7.62×51/7.62×54R/6.8×51 30; .338 Lapua 20; shotgun calibers 12. Unknown/custom calibers conservatively require 60. Detachable weapons count compatible loaded magazine rounds in the weapon and fast access plus usable chambered rounds; loose ammunition counts only after successful loading. Internal/chamber-fed weapons count settled loaded rounds and eligible loose reserves against the same caliber target. No half-full or five-round-magazine credits remain. Specialized launcher readiness stays on its existing separate contract. See [primary readiness](looting/Weapon-Pickup-Primary-Slot-Available.md).

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

The current primary remains mandatory until an incoming candidate qualifies through the normal upgrade comparison or the depleted-primary fallback and also passes operational support checks. An inferior offering cannot demote a ready/replenishable primary; incomplete/unready replacements never permit an armor/vest change that loses the retained current primary's required magazine/reload support. A trial for a valid primary or improved outfit temporarily defers secondary/holster weapons so their magazine shapes cannot veto the primary's arrangement; unsuccessful trials restore the exact prior equipment. This also applies in later gear-only handovers, where a previously displaced primary is already secondary.

### 4.3 Primary magazines take priority

Allocate the selected primary's usable magazine/ammunition support before attempting additional secondary or holster acquisition.

Minimum primary readiness is an admission gate, not a magazine-collection cap. Extract additional compatible usable magazines offered loose or directly inside an incoming vest/backpack into the retained tactical vest or normal pockets whenever real placement and the shared reload-reserve check permit. Keeping the current vest must not leave fitting support magazines inside the unused offered vest. Apply the same support pass when a later handover supplies magazines without replacing the primary. Magazines that cannot fit safely remain cargo in their original offered container; ordinary nested loot is not unpacked by this support step.

Use existing readiness, ammunition-compatibility, fast-access placement and reload-reserve rules, including the appropriate internal-magazine/chamber-fed handling. This feature must not create a second, inconsistent ammunition-readiness model.

For detachable-magazine weapons, top off the inserted compatible magazine from eligible loose ammunition before filling spares and before the readiness decision. EFT prohibits direct loading while it is inserted: remove, refill and reinsert it through replayable staged operations, rolling back exactly if it cannot be refilled. Keep existing ownership/protected-loan and mixed-stack restrictions. Insufficient usable ammunition still cannot qualify a new primary.

Reloads are sequential. Retained weapons may share the same free magazine landing opening when each magazine fits it independently; do not reserve simultaneous ejection cells for every weapon. Preserve native reload safety for every weapon that remains operational.

Register accepted magazines against the exact weapon through existing weapon-package bookkeeping. Magazines belonging only to a weapon no longer used as equipment do not retain operational vest-space priority merely because they were once registered.

### 4.4 Run existing secondary and holster analysis afterward

Once the proposed primary and supporting gear are decided, apply the shared secondary/holster usability, magazine-compatibility and reload-reserve contracts against that arrangement and its remaining real capacity. Give Gear adapts those contracts through replayable draft operations rather than calling the live looting action's source-move chain.

- Consider the displaced old primary as a secondary candidate if the secondary slot is available and its ammunition package can be supported after the new primary's needs.
- Other unselected offered weapons may be eligible support candidates under the existing rules.
- Do not force the old primary into secondary solely because it was previously equipped.
- Do not add a new occupied-secondary or occupied-holster upgrade scorer. Existing support acquisition fills compatible empty slots; it does not replace occupied support slots.
- Reuse their usability, magazine-placement, ammunition and shared reload-space decisions rather than duplicating them.
- Explicit Give Gear authorization replaces the ordinary pickup-setting gate for this draft only, without modifying saved pickup settings.
- Failure to acquire an additional support weapon must not undo an otherwise valid primary upgrade merely to make room for that additional weapon.

After the primary/outfit is settled, deferred original secondary/holster weapons get first chance to return to their original slots, before ordinary empty-slot acquisition. Their supplies use only remaining space. A deferred weapon that cannot retain safe reload geometry stays cargo or is dropped; its magazines follow the same carry/drop rules. Do not keep an equipped, un-reloadable secondary by bypassing the final all-equipped-weapons reserve check. Existing optional spares from the old vest remain deferred until this pass instead of being packed into the new vest ahead of primary support. Current-primary rejection and whole-draft rollback restore all deferred weapons and supplies.

Existing retained support equipment remains subject to final compatibility/reload validation. The selected better primary's magazines take fast-access priority over the old primary, secondary or pistol's spare supply. Those older spares may remain cargo or be dropped if they do not fit safely; do not reject a valid primary upgrade just to retain them in the vest. One real opening may serve all three inserted magazines sequentially when each shape fits. This is not a general inventory optimizer or multi-vest/plate-transfer search; earlier carried weapons may be reconsidered, but there is no new historical-gear or automatic plate-rebuilding system.

## 5. Other equipment decisions

### Body armor and helmets

An occupied armor/helmet position is replaced for higher usable protection, not cosmetic or resale value alone. Examine installed protection and remaining condition, including plate-based carriers and helmet parts; do not treat an empty carrier as fully protected because of its name or price.

Armor can improve while the primary remains unchanged. A helmet upgrade is independent of the primary/vest decision, subject to actual equipment conflicts. Each component scores class × 100 × (0.25 + 0.75 × remaining/template durability), with the durability ratio clamped to 0–1. Only installed slot descendants contribute, never loose protective items inside a vest's grid. Actual native slot conflicts still apply.

Body armor compares actual native front/rear chest coverage, including installed ArmorSlot restrictions, rather than the single best component anywhere on the item. For each direction take the strongest usable installed layer (plate or soft armor); do not add overlapping layers. Destroyed components (less than 0.01 durability) contribute nothing, while remaining soft armor still contributes its real coverage. Side plates and collars cannot substitute for chest protection. Compare the complete compatible armor/rig arrangement by front × 0.75 + back × 0.25, an initial kit-selection calibration that favors front protection, not a ballistic simulation. A good rear/side plate must not conceal a destroyed front plate. A smaller armored rig may replace separate armor plus a larger plain rig when its resulting protection is better and existing magazine/readiness/reload checks pass. Comparison logs include incoming and current front/rear scores. Helmet scoring remains the best usable installed component; this change does not alter helmet selection or transplant plates.

Keep installed plates and attachments with their parent equipment. General automatic plate replacement or weapon attachment optimization is outside this initial feature; loose parts can remain ordinary offered cargo.

For a protective helmet upgrade, try native equipment admission with the current face cover first. If that fails, a non-protective face cover (for example a cosmetic beard) may be staged out and the helmet retried. Accept that removal only if the retry succeeds; otherwise restore both original headwear and face cover before trying another candidate. Protective face gear, headsets, eyewear and all other native conflicts remain untouched. A compatible face cover is never removed. Displaced cosmetic gear follows normal carry/drop handling with exact rollback and ownership preservation; no native conflict filters are bypassed.

The reverse applies when filling an empty FaceCover slot with a protective mask: try native admission first, then retry without non-protective headwear (for example an R2 cap) only if needed. A successful retry carries or drops the displaced cap normally. Protective headwear, occupied FaceCover slots and other equipment conflicts are not overridden; cosmetic masks cannot displace headwear. A failed retry restores the cap before another candidate is tried. Compatible caps stay equipped. This also applies to masks offered one container level deep.

### Backpack

- Fill an empty compatible backpack slot when possible.
- Replace an occupied backpack only with a larger-capacity candidate, not merely a more expensive one.
- Before acceptance, prove that all current backpack contents can be moved into the candidate using real placement rules, accounting for contents already inside it.
- Do not accept the replacement by silently deleting or dropping current contents to make it fit.
- Failed migration leaves the current backpack and contents intact; other donation categories can still succeed.
- Carry the displaced backpack shell if possible; otherwise drop it in favor of the accepted better backpack. All current contents must migrate first.

Backpack evaluation may establish available carrying capacity before final generic cargo placement. It must not require an arbitrary ordering of the player's drag operations.

### Remaining items

Other equipment uses compatible-empty-slot acquisition, not occupied-slot upgrades. For example, an offered headset may fill an empty earpiece slot but does not initiate a price-based replacement of an occupied one. Secondary/holster weapon acquisition remains owned by the existing support planners.

Items not equipped are carried in valid available space. Player-offered items that cannot be equipped or carried are dropped on the ground after submission. They are not deleted, left in a disappearing virtual grid or returned to the player through an invented mail path.

Giving medical supplies or grenades does not independently enable prohibited medical/combat behavior or override existing bot permissions.

## 6. Commit, interruption and conservation

Give Items must first produce a complete, validated plan with explicit equip, carry and drop outcomes. No partial live experimentation while comparing candidate gear.

Decision logs use a per-handover/attempt identity and label draft decisions `phase=plan`, including rejected comparison, support, reload-space, native ammo/provenance and cargo-placement reasons. Only successful live ownership tracking emits `phase=committed`; aborted Apply rolls back the plan explicitly. Logging is passive and a failed diagnostic sink must never affect selection, replay, conservation or rollback.

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

World drops use the native prefab and `CreateLootWithRigidbody` path. All rejected roots first receive inactive, unpublished world owners synchronously inside the reversible transaction. Preparation failure destroys only those unpublished owners and restores exact items to escrow before inventory rollback. Only after ownership commits are they activated and throw/event callbacks published. Subscriber failures cannot authorize restoring a second player copy. This is not a claim that already published world effects can be rolled back.

## 7. Ownership and persistence boundaries

- Before commitment, all offered items remain the player's real property; clones do not register insurance or return ownership.
- The staging container is never saved, insured, mailed, spawned or treated as follower equipment.
- Do not register the recruit's original kit as player-owned loot simply because it participated in a comparison or container migration.
- Staging and committed handovers preserve exact-item insurance during the raid. Protected squad equipment may be loaned for raid use without transferring its original saved ownership. At raid end, cancel coverage only for exact donated IDs still carried by a living recruited PMC. Gifts recovered from a dead recruit, taken back by the player or dropped retain their original coverage; no new insurance is granted. Borrowed protected gear and the recruit's original kit are not cancellation sources. Scav donations retain their separate existing insurance/Fence rules, excluding loaned protected gear.
- Do not broaden saved-squad return admission. A PMC field recruit remains a field recruit; its existing invitation/profile flow is separate from squad return mail.
- Scav field cargo has a separate Fence flow and no saved roster profile. Committed player donations enter that exact-item return flow, including equipped donations, but never the Scav's original kit. Existing living-companion, outcome, remaining-quantity, protection and insurance gates still apply.
- Give Gear classifies protected loan IDs separately from permanent player gifts before planning. The Scav cargo ledger excludes loan IDs and native split children, while operational weapon history may still reconsider a loaned weapon in a later explicit handover. Exact protected IDs are checked again at final Scav return capture. No transfer unregisters the original teammate's protection or edits its saved equipment.
- PMC donations remain with the recruit, including in its invitation profile; they are not separately mailed. Each distinct supported equipment slot actually filled or replaced by a committed donation increases that recruit's invitation chance by 10 percentage points, capped at 100%. Count a slot at most once per recruit per raid; staging, cargo-only transfers, rejected gear and Scav donations grant no bonus. Ordinary invitation eligibility remains unchanged.
- Weapon/magazine operational registration is distinct from mail-return ownership. Successful support registration alone must not grant a field recruit saved-squad return eligibility.
- Giving gear must never produce both a retained/invitation copy and a mailed copy of the same item tree.
- Restricted invitation creation, pending-invite preview and acceptance independently strip exact protected squad equipment IDs and their descendants from captured recruit inventory before importing/remapping it. Protected roots also lose their donation-slot credit. Immersive/Realistic recovered gear and unrelated native/gifted kit are unchanged. This guards invitation persistence even when equipment arrived through another route; it is not an automatic repair of already accepted roster profiles.

PMC cancellation is reported through the existing exact-raid completion barrier, after capturing final live ownership and before follower teardown/report sealing. Player-death teardown has the same idempotent capture. Only living field PMCs count, including an original recipient dismissed earlier or another recruited PMC now carrying the gift; saved teammates do not become gift recipients. Transit settles departing PMC recruits on the old map. The durable ID ledger removes active PMC policies, original saved-follower policies and queued/staged stock insurance, and suppresses custom follower loss claims. Raid-end observation reapplies deletion after native processing. The client removes those final retained IDs from both live/backend profile policies, native insurance presentation and the follower-shield cache; no templates or unrelated policies are changed. A failed final ownership snapshot blocks custom settlement instead of guessing ownership or deleting coverage. A report failure is logged, never treated as an inventory rollback or permission to recreate the gift.

## 8. ORBIT reference and implementation boundaries

ORBIT 2.0.0 source supplied for this investigation contains `WeaponScorer`, `ArmorScorer`, `RigScorer`, `BackpackScorer`, `HeadsetScorer` and corresponding swappers under `Orbit/Looting/WeaponSwap`.

Useful reference behavior includes ammo-aware weapon ranking, per-category comparisons, improvement margins and pre-swap content migration. It is not the complete Give Gear policy:

- Its 1.10 swap margin informed the initial centralized calibration above; the additional absolute margin and readiness gates remain Give Gear policy.
- Its ordinary NPC Scav path generally equips empty slots only; Give Gear must allow the agreed primary/armor/backpack upgrades for recruited Scavs too.
- Its rig swapper excludes armored-rig/separate-armor transitions; Give Gear needs the bounded supporting transition described above.
- Its swaps can send displaced gear back to a corpse/source address. Give Gear cannot expose original gear in the visible donation grid or leave real equipment in a temporary container.
- Its price contributions and cell-count guards do not replace usable-protection checks or actual grid-fit simulation.
- Its live transaction orchestrators must not be transplanted as the staged handover engine.

Keep this feature in Core and independent of an installed ORBIT plugin or the optional SAIN addon. Preserve MIT attribution if substantial ORBIT code is reused. Machine-local source locations belong in LOCAL.md, not portable setup instructions here.

Implementation ownership:

- `client/Patches/QuickPanelPatch.cs` and `client/Patches/PlayerPatch.cs`: quick interaction visibility and dispatch.
- `client/Modules/TeammateBackpackInspection.cs`: targeting and peaceful/busy safety helpers; immediate inspection remains unchanged.
- `client/Modules/TeammateGearSwap.cs`, `GearSwapInventoryController.cs`, `GearSwapEdit.cs`, `GearSwapSnapshot.cs`, `GearSwapTransaction.cs`: staged ownership and replay primitives.
- `client/Modules/GearSwapApplyScope.cs`, `GearSwapHandsTransition.cs`, `GearSwapBodyRefresh.cs` and `client/Patches/GearSwapApplyProtectionPatch.cs`: bounded transfer lifecycle.
- `client/BigBrain/Actions/GestureCommandAction.GearSwapping.cs` and related readiness/magazine planners: secondary/holster and fast-access support rules.
- `client/Modules/FollowerLootedPrimaryWeaponBinding.cs`: post-commit native weapon-cache reconciliation.
- `client/Modules/InteractableObjects.cs`, `FollowerReturnPolicy.cs`, `AllegianceScavCargo.cs` and `BossPlayers.cs`: registration, ownership and post-raid boundaries.
- `client/Modules/TeammateGearSwap.GiveGear.cs`, `GiveGearPlanner.cs`, `GiveGearWorldDrops.cs` and `client/Patches/GiveGearPanelPatch.cs`: private donation staging, bounded comparison and prepared drop ownership.
- `client/Modules/GiveGearInsurance.cs`, server `GiveGearInsuranceService` and the existing raid-insurance ledger: exact committed PMC-gift cancellation.

New interaction/button/warning text must use the central language model and embedded fallback, with matching locale resources. Do not add per-callsite English fallbacks.

## 9. Acceptance and qualification matrix

For local qualification, Debug-only `fs_spawnfollower` creates a field recruit eligible for Give Gear and a deterministic PMC post-raid invitation through the normal captured-kit/protection path. It randomly chooses USEC/BEAR in Allegiance and matches the player in Guns for Hire. Debug-only `fs_spawnscavfollower` creates a recruited Scav; in Allegiance it uses normal Scav diplomacy/cargo tracking, never a PMC invitation. See [Debug recruit spawning](Commands.md#debug-recruit-spawning) for mode boundaries, the one-invitation limit, logged normal bonus chance and Debug client/server requirement. Ordinary gameplay recruitment is unchanged.

| Scenario | Required result |
|---|---|
| Own recruited PMC or Scav | Give Gear opens under the same safe admission rules |
| Unrecruited, dead, hostile, dismissed or foreign-owned target | No opening or transfer |
| Spawned saved teammate | Existing Swap Gear/View Backpack behavior unchanged |
| Move items into/out of the donation grid; cancel | Both live inventories unchanged |
| Attempt to access the recruit's kit or transfer the virtual container | Rejected |
| Two incoming long guns | Best primary candidate considered first, regardless of drag order |
| Equipment inside an offered backpack/container, with matching magazines in a separately offered vest | Evaluate all immediate contents together under the ordinary upgrade/readiness rules; no requirement to offer the weapon/armor loose |
| Equipment inside a container inside another offered container | Not a selection candidate; preserve the deeper cargo tree rather than recursively unpacking it |
| Better primary, no incoming vest | Current suitable gear permits the weapon upgrade |
| Keep current vest; compatible mags inside offered vest/backpack | Extract every safely fitting usable magazine into vest/pockets, beyond minimum readiness and on later supply-only handovers |
| First grid is the only reload opening; another fast-access grid fits a spare | Try the other grid instead of rejecting the spare or consuming the reload opening |
| Worse weapon, better armor/helmet/backpack | Weapon failure does not skip independent useful gear |
| Depleted primary, no eligible replenishment, weaker but fully supplied incoming gun | Permit the weaker gun only after normal operational/readiness/reload checks pass |
| Depleted primary, enough eligible carried/offered ammunition and usable gear | Replenish/retain it rather than waive the normal upgrade comparison |
| Both current and incoming packages below readiness | Do not force a primary swap; preserve quantities and continue independent gifts |
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

## 10. Approved implementation policies

Clarified by the user on 2026-10-17, with the container-depth interpretation corrected on 2026-10-19:

1. **Displaced original kit.** Prefer carrying it as cargo. If it does not fit, drop it in favor of better accepted gear. Do not register the original kit as a player donation or return cargo.
2. **One container level, for every category.** Evaluate every item offered directly in the donation grid or directly inside an offered container's grids: weapons, armor, helmets, vests, backpacks, accessories and operational magazines/ammunition all use the same limit. Armor directly inside an offered backpack is eligible; armor inside a backpack inside that backpack is not. Snapshot that depth before planning moves any container, so equipping or moving a container cannot expose another level for selection during the same handover. Do not recursively unpack deeper trees; keep them intact as cargo if possible, otherwise drop them. Installed weapon attachments, loaded ammunition and armor plates remain part of their equipped parent, not loose candidates. This corrects the earlier overly narrow interpretation of the user's nesting requirement.
3. **Different recipient ownership.** Scav donations are returnable through the existing Fence cargo flow. PMC donations are retained and can improve invitation chance by 10 percentage points per distinct accepted equipment slot, as specified above.
4. **Bounded comparison.** Try usable weapon candidates best-first, using actual ammunition and available magazine space. Protection and weapon comparison must be centralized and documented, independent of price-only ranking. No exhaustive kit-combination optimizer.
5. **Permanent gifting cancels insurance at raid end.** A committed PMC handover only records candidate IDs. Cancel only exact donated IDs/descendants still carried by living field PMCs at final capture, excluding player-recovered and protected loan items. Corpse recovery keeps the original policy. Do not create an insurance copy when the recruit later sends an invitation. Scav donations remain return cargo rather than permanent gifts.
6. **Restricted gear may be loaned.** The user explicitly permits temporary handovers of protected teammate kit, including using the recruit's displaced unprotected gear. Keep the original teammate's saved ownership, exclude loaned items from return mail/Fence and future invitations, and do not treat the loan as an insurance-cancelling permanent gift. Existing accepted profiles remain untouched.

## 11. Initial offline verification — 2026-10-17

Production-derived fixtures cover bounded equipment planning and conservation, prepared world-drop ownership/rollback, donation access boundaries, invitation slot bonuses, native Scav ammunition-split provenance and exact insurance deletion. Existing Swap Gear, quick interaction, reload-reserve, admission/reload-start and insurance fixtures remain regression checks. Installed metadata verifies native stash/drop/insurance APIs; client and server use the supported reference baseline.

The 2026-10-17 offline pass completed 110 planner/invitation, 38 world-drop, 14 access, 10 server gift-insurance, 9 client gift-insurance and 65 Scav client checks. Regression checks passed: 313 Swap Gear, 36 quick-interaction, 27 reload-reserve, 32 admission, 50 reload-start, 426 return-policy, 107 insurance, 26 mail-display, 504 native Scav/Fence and 16 native removal-courier checks, plus installed API/source guards. Client, server and matching addon Debug builds have zero warnings/errors; dependency-backed fixtures reported only unavailable NuGet vulnerability-data warnings.

Windows Debug deployment completed with Tarkov/SPT stopped and recoverable backups. Installed SHA-256 matches the validated outputs: client `163FDB27A3AD67EC49DFBC5988A99B4EC6949DD39EC5F7CFB2B12AE2DF8486B2`, server `9022AD0610105E11FE543CFF973EF2BEC146CB2583D732E34471D2853F242850`, matching addon `28A2871D7AB0FC55D50E1F5781B02A9C054ECAF57B901214613ECB450999F886`. EN/RU/CHS resources were also deployed and hash-verified. No runtime qualification is implied.

Live qualification remains pending: open the donation grid for recruited PMC and Scav followers, cancel drafts, submit repeated partial kits, observe actual displaced drops and weapon/armor refresh, then test firing/reload, PMC invitations and Scav Fence returns. Also test combat/death/raid teardown around Give Items. No fixture proves those runtime boundaries.

### First raid feedback and corrections — 2026-10-18

The user reported Give Items positioned at the left, a View Backpack label for Give Gear, and protected fallen-teammate gear showing a green donation preview before snapping back. The button now uses the full header-row center after layout and all phrase-name paths explicitly handle Give Gear. The first correction added explicit protected-item rejection, but the user subsequently clarified that temporary handover is allowed: that rejection patch, Stage/Apply guards and rejection text have been removed. Protection belongs to original ownership and post-raid settlement, not transfer admission.

The user also found fallen-teammate gear retained by an invited PMC in Restricted mode. Captured recruit import lacked its own protected-equipment filter. The server now checks saved squad ownership plus current registered spawn IDs before invitation chance/storage, pending preview and acceptance, removing protected roots/descendants and their slot credit. Production database checks cover protected weapon/helmet/attachments, live-only registered trees, exact retention of unrelated gear, unchanged original teammate equipment, idempotent pending storage and unchanged Immersive/Realistic behavior. Existing accepted profiles are deliberately untouched, as requested by the user.

Validation: 189 native-backed gameplay-storage checks, 315 Swap Gear checks, 39 quick-interaction checks, 21 donation-access checks, 110 planner/invitation checks, 38 world-drop checks and 426 return-policy checks pass, together with installed native move/preview API and locale/source guards. Client, matching addon and server Debug builds pass with zero warnings/errors. Dependency-backed storage fixtures reported unavailable NuGet vulnerability data.

After both processes stopped, the correction was deployed with recoverable backups and SHA-256 verification for the client, matching addon, server and EN/RU/CHS locales. Installed hashes: client `A04C7C6CA59AB1D40DC3B05B9FD650ECEE44732E0376EAF11C21995791221F57`, server `FAB653A02A9506FE0E67E6750033E5F0A3D271125EF6992E781B19676C0251B7`, addon `649B76014A855FD48F2197557C92E5A1420BEAB460607D820B8083676B717E0B`. No live profiles were edited. Fresh UI and invitation raid qualification remains pending.

### Temporary Restricted loans — 2026-10-18

After the ownership clarification, protected teammate gear can be staged, rearranged and considered by the ordinary upgrade planner. Protected IDs are removed from permanent-gift provenance before planning; borrowed retained weapons remain eligible for later operational reconsideration without gaining gift credit. Scav cargo tracking excludes borrowed trees and clears stale gift records, with a final protected-ID filter at settlement. PMC gift-insurance cancellation excludes protected items. The server invitation filter remains unchanged, and no existing accepted profiles are repaired.

Validation passed: 18 production access/ownership assertions, 115 planner/invitation checks, 67 Scav client checks, 9 client gift-insurance checks, 189 native-backed gameplay-storage checks, 313 Swap Gear checks, 39 quick-interaction checks and 426 return-policy checks, plus installed API, locale/source guards and clean diff whitespace. Client and matching addon Debug builds have zero warnings/errors; dependency-backed fixtures reported only unavailable NuGet vulnerability data. The server artifact is unchanged from the preceding validated build.

With Tarkov and SPT stopped, the client, matching addon, server and EN/RU/CHS locales were backed up under `20261018-021113-give-gear-loans`, deployed and SHA-256 verified. Installed hashes: client `BF5319709142EDCB5BA9038F79AF83781762F5C92B0E3DA89D67D58C9430FCE9`, server `FAB653A02A9506FE0E67E6750033E5F0A3D271125EF6992E781B19676C0251B7`, addon `66955B9DB94830590442355F516950C2E4B6CE13519A07ACF17673008B7B9405`. No live profiles were edited. Raid qualification still needs to cover the borrowed handover, unchanged original teammate ownership/insurance, no Fence return and exclusion from a future invitation.

### Offered-vest magazine extraction — 2026-10-18

The player verified general in-raid equipment changes, then reported a primary upgrade retaining only one spare while additional matching magazines stayed inside an unused offered vest. The planner stopped primary support collection at minimum readiness. Production-derived regression cases reproduced that failure before removing the collection cap. A final primary-support pass also handles later supply-only handovers, and per-grid reload validation retries another vest grid or normal pockets when the first fitting grid must remain the reload opening. Incompatible magazines, incompatible loaded cartridges and magazines that cannot fit safely remain cargo; ordinary nested loot is not unpacked.

Validation passed: 175 production planner/invitation checks, including retained-vest extraction, later supplies, capacity-limited acceptance, larger-magazine rejection with a later fitting candidate, alternative vest/pocket grids, compatibility, rollback and item/ammunition conservation. Regressions passed: 18 donation-access, 38 world-drop, 313 Swap Gear, 27 reload-reserve, 67 Scav cargo and 426 return-policy assertions, plus installed API and locale/source guards. Client and matching addon Debug builds have zero warnings/errors. The Scav fixture reported unavailable NuGet vulnerability data only.

Deployment was initially withheld because Tarkov and SPT were both running. After the user closed both, process checks confirmed they were stopped and the validated client/matching addon DLLs and PDBs were backed up under `20261018-035250-give-gear-magazines`, deployed and SHA-256 verified. Installed client: `6F1583F213C45DAC377BFFD27644A6E4741B00B5C8C2BD3F7E14C9D0422D2C6F`; addon: `E8F715F2232767C1D7AB6FC8E8D1C82670D253C36E9054C472854929829A30E4`. Server files, locales and live profiles were unchanged. Raid qualification still needs the weapon plus magazine-filled vest handover while retaining the current vest, checking the accepted spare magazines and actual reload behavior.

### Debug recruit test entry point — 2026-10-19

The user requested that `fs_spawnfollower` create a recruit rather than a squadmate and provide a post-raid invitation for testing, with the command available only under `DEBUG`. The actual conversion now uses `squadMate: false`; only the command-created PMC receives a raid-local invitation marker. Both Debug request models carry that marker, while the server still validates identity, sanitizes protected gear and calculates/logs the normal donation-adjusted chance before its per-recruit roll bypass. Debug candidates receive priority within the unchanged single-invitation selection. The marker is not set on pending entries, and no ordinary recruit settings or global chance override are changed.

The follow-up adds a shared Debug-only command handler: `fs_spawnfollower` uses the active mode (equal USEC/BEAR choice in Allegiance, player faction otherwise), while `fs_spawnscavfollower` always requests Savage/assault. Allegiance Scavs receive a locked independent native group and a Debug selected-friendly encounter identity before conversion, so ordinary diplomacy and cargo registration run unchanged. Native bot-generation retries now preserve the selected faction/role and reject mismatched profiles, instead of silently substituting a Scav. The test encounter helper and faction policy are also absent from Release.

Validation passed: 42 compiled Debug/Release metadata and source guards; 30 production-derived faction/encounter-policy assertions; 210 Debug and 192 Release native-backed storage checks, including marked-request serialization, normal/Scav exclusion, forced-candidate priority, protected gear sanitation and injected Debug JSON ignored by the real Release model. Existing checks passed: 736 recruitment, 175 Give Gear planner/invitation, 67 Scav cargo client, 18 donation-access and 39 quick-interaction assertions, plus installed native API/source guards. Debug client, matching addon and server builds pass with zero warnings/errors; Release builds are validation artifacts only. Dependency-backed fixtures reported unavailable NuGet vulnerability data.

Deployed on 2026-10-19 after verifying Tarkov and SPT.Server were closed: Debug client, matching addon, server and changed English locale; all 40 deployment files match their source SHA-256 hashes. Seven changed files were backed up before replacement. Installed DLL hashes: client `AB86A13D0AD4B03ADA992FCBB1CF717C08B45E209393CEA90F3838187F81B633`, addon `F176E6B6F64743C66CA4273A0AD1FAC52D9EC7B9161A2CBCE7680FBAAB3FDBE4`, server `1AE34F9CC9498517B142ADC1CA16EF98C20AE5A072B1FA6674EC2B678EFD9207`.

Live qualification remains pending: in a PMC raid, use `fs_spawnfollower`, Give Gear and a living post-raid capture; inspect the invitation's actual kit and the logged normal bonus chance. Check repeated Allegiance spawns for both PMC factions, Guns for Hire for same-faction spawns, and `fs_spawnscavfollower` in Allegiance for Give Gear plus living extraction/transit Fence returns. No live profiles were edited.

### One-level equipment selection correction — 2026-10-19

The user clarified that "nested" means equipment inside a container inside another offered container, not equipment directly inside an offered backpack/vest. The old planner incorrectly evaluated only root equipment while allowing one-level supplies. A production-derived regression reproduced the packed weapon/helmet/armor failure before correction.

The planner now snapshots the offered roots and their immediate grid contents once, using that same pool for all equipment categories, supplies and retained donated-weapon history. Root cargo handling stays separate, preserving unselected/deeper container trees. Equipping an offered backpack does not cause the depth limit to expand, and accessories inside that newly equipped backpack remain eligible. Existing upgrade/readiness, reload-space, ownership, protection, insurance and transaction rules are unchanged.

Validation passed: 217 planner/invitation assertions, covering the reported packed kit plus separate magazine vest in both drag orders, generic containers, every equipment slot category, two-level exclusion after container movement, exact rollback and item/ammunition conservation. Also passed: 18 donation-access, 67 Scav cargo client, 38 world-drop/rollback, 9 client gift-insurance and 39 quick-interaction checks, with installed native API/source guards. Client and matching addon Debug builds pass with zero warnings/errors; the Scav fixture reported unavailable NuGet vulnerability data.

Deployed Debug client/addon DLLs and symbols after verifying both game processes were closed; all four source/destination SHA-256 pairs match, and the unchanged installed Debug server matches its source artifact. DLL hashes: client `24CF49B2C496FCC4902AA019F991DCA237A46134120FBCBB1791260E2A18A064`, addon `E58CE9425C37BA829179F8833DA640A23285DD57DF8C1B29868EEC53C1133D9B`. Previous artifacts were backed up. Live retest of the original Scav handover and subsequent Fence returns remains pending.

### Inserted magazine top-off and decision diagnostics — 2026-10-19

The user reaffirmed that a selected better primary's magazines take priority over older weapons' spare magazines, while all retained seated magazines may share one sequential landing opening when their shapes fit. The requested changes are inserted-magazine top-off and useful rejection diagnostics, not a multi-vest search or historical plate/carrier reconstruction.

The planner now tops off a compatible seated detachable magazine before processing spares/readiness. Native EFT `Magazine.ApplyItem` explicitly rejects an inserted magazine; the plan therefore removes it into staging overflow, refills it through the existing provenance-checked native operations and reinserts it. Failed/unsupported refill restores its original seat and counts. Existing readiness and shared reload-reserve gates remain authoritative. Scoped logs report provisional selection/rejection/cargo/drop outcomes and native ammo/provenance errors; a separate live ownership-tracking line confirms commitment, and aborted Apply reports plan rollback. Diagnostic formatting and callbacks are isolated from transaction behavior.

Validation passed: 280 production planner/invitation assertions, including native-style inserted-magazine loading rejection, top-off-before-spares, sufficient/insufficient ammunition, exact rollback, logger failure isolation and rejection reasons. Also passed: 18 donation-access, 313 shared Swap Gear, 27 reload-reserve, 67 Scav cargo client, 9 client gift-insurance, 38 world-drop/rollback and 39 quick-interaction checks, plus installed magazine restriction/native API and locale/source guards. Debug client and matching addon builds have zero warnings/errors. The Scav fixture reported unavailable NuGet vulnerability data only.

Deployed Debug client/addon DLLs and symbols with both game processes closed; all four SHA-256 pairs match and the unchanged Debug server was verified. DLL hashes: client `66AE90947F9BBDF0923091E26601D429AF8AD22D158801EF4F4946DDFFB854A0`, addon `F76186736E13277C8D2DA4595FA3534C47384E3DB5F4434BC6F2F6EA6AEE6456`. Previous artifacts were backed up. Live qualification remains pending: offer a partially loaded primary plus compatible spare/loose rounds, confirm top-off and usable kit in raid, then verify handover decisions against final Fence/invitation results.

### Debug PMC spawn side correction — 2026-10-19

Live logs showed `fs_spawnfollower` rejecting the correct `pmcBEAR` role because its generated profile still had raw side `Savage`. SPT's `PmcBotSidePatch` normally corrects this in `BotCreationData.ChooseProfile`, which the direct Debug activation flow does not call. The Debug helper now checks the requested role, applies that same role-based PMC side correction, then verifies the final side before activation. It never rewrites an assault/marksman role to disguise it as a PMC, and ordinary/Scav spawn behavior remains unchanged. Rejection messages now include both requested and received identity.

Validation passed: 44 Debug/Release metadata/source guards and 62 production-derived mode/profile/encounter-policy checks, including raw Savage PMC profiles, already normalized profiles, real Scav preservation, genuinely wrong roles and missing profile data. Debug client, Release guard-validation client and matching Debug addon build with zero warnings/errors. Deployment was initially blocked by running Tarkov/SPT processes.

Deployed on 2026-10-19 with the peaceful-command correction and shared Give Items transfer feedback after both processes closed. All four Debug client/addon DLL/symbol hashes match, with previous artifacts backed up and existing SAIN attribution retained. DLL hashes: client `ED59BD447BA9842FB47EF7A2D2E3EF0D5F3762B75CF1ACBEC430C774C69B3955`, addon `F19ADA55CD14E662E1C9B81F4493626A02FD85AC7E7A6F746ADCE462C167D717`. Debug recruit (106), donation access (18) and shared Swap Gear (313) checks passed again. Live PMC spawning/invitation and Give Items progress-display qualification remain pending.

### Front/rear body protection comparison — 2026-10-19

Reported case: the recruit retained armor with a spent front plate instead of an offered TV carrier. Handover `07685acf` rejected that rig before capacity checks (`401.61` incoming versus `461.74` current). The former maximum-component scorer could conceal the missing front behind a stronger rear/side component. Logs did not record individual plate condition, so the exact live per-direction state cannot be reconstructed from those old scores alone.

Body comparison now uses installed native plate masks and soft-armor colliders per direction, following the section 5 calibration. The installed `ArmorComponent` coverage APIs and enum boundaries were checked against metadata and their slot-intersection behavior against source. Helmet scoring, item identities, installed plates, one-level offering depth, reload space, ownership and return rules remain unchanged.

Validation: 390 production planner/invitation checks passed, including reproduction of the old ranking, destroyed/damaged fronts, strong rear/side/collar components, soft armor, healthy higher-class rejection, smaller carriers with retained weapon magazines, insufficient reload space, conservation and exact rollback. Also passed: 18 donation-access checks plus installed API/source/locale guards, 313 shared Swap Gear checks, 27 reload-reserve checks and 332 peaceful-command regression checks. Client and matching addon Debug builds passed with zero warnings/errors.

Deployed after confirming Tarkov and SPT.Server were closed; all four DLL/symbol SHA-256 pairs match and previous artifacts were backed up. Client `CBFE93955128EA42FEC0FB0FDD79FFC961E8940BBB5F9B5152394497BF3D6ABC`; addon `39B5DF7C8CDA5B9D97D56AFCAF9B2A8DB56B88FFBAE434F2D7C17F964304958C`. No server change or profile edits. Live qualification remains pending: repeat the spent-front/separate-armor-plus-vest to balanced-carrier handover, inspect the front/back decision scores, retained supplies and final body appearance/ownership.

### Helmet blocked by cosmetic face cover — 2026-10-19

The earlier handover `5eda336c` rejected ACHHC headwear because of the recruit's red beard in FaceCover. The planner now tries ordinary helmet admission first, then one reversible retry without a non-protective face cover. Only a successful native retry retains that displacement; other equipment restrictions remain native-owned, and normal cargo/drop planning conserves the removed item. Protective masks and compatible cosmetics stay equipped. Failed retries restore the headwear and face cover before any later candidate.

Validation: 477 production planner/invitation checks passed, covering both conflict directions, empty/beanie headwear, carry/drop outcomes, exact rollback, compatible cosmetics, protective masks, inferior helmets and a second blocking headset. Donation-access (18) and shared Swap Gear (313) checks plus installed API/source/locale guards passed. Client and matching addon Debug builds passed with zero warnings/errors.

Deployed with Tarkov and SPT.Server closed; all four DLL/symbol SHA-256 pairs match, previous artifacts are backed up, and existing SAIN attribution was retained. Client `E27B3FA0DAE1AA461267B809E2F903F5904FDEE5C80B41A9CB80FDE925CFE77A`; addon `CFCA9BC1BD42D48567035175F4F018BDE5A1D1C329BD83C1AE0E808C5E71CE9C`. No server or profile changes. Live qualification remains pending: give a better helmet to a beard-wearing recruit, verify final worn helmet and displaced face-cover ownership, then check the post-raid cargo/invitation outcome.

### Primary/outfit priority before optional secondary support — 2026-10-19

The second recruited Scav's original M60/Umka arrangement lacked a free 2×2 landing opening. Earlier bundled attempts rejected the offered SAG at the quality gate and rolled back at final reload validation; the later loose weapon/magazine handover accepted it as primary and moved the M60 to secondary. The following gear handover committed other gifts but still rejected the new armor/vest while the M60's secondary magazine shape participated in primary outfit validation. The user clarified that a valid selected primary's kit comes first; an inferior or unsupported offering must not make the current primary optional. Offering-depth and weapon-score policy are unchanged; quality rejections now include both numeric scores.

Primary and usable-outfit trials now defer secondary/holster weapons reversibly, establish primary support, and keep non-primary old-vest magazines deferred. The final support pass prefers original support slots, then ordinary empty-slot candidates. Unsupported support weapons/supplies become cargo or ground-drop plans instead of vetoing the primary outfit; the final all-equipped-weapons reload guard remains unchanged. Failed primary/outfit trials restore deferred equipment exactly. Later gear-only handovers use the same ordering against the established primary.

Validation: a production-derived regression failed before the correction. The expanded planner/invitation suite passes 730 checks, including old-primary and already-secondary M60 cases, later gear-only handovers, separate armor versus armored rigs, reversed offering order, cargo/drop outcomes, original support-slot preference, inferior/unready incoming weapons, current-primary protection, exact rollback and item/ammunition conservation. Donation access (18), shared Swap Gear (313), reload reserves (27), world-drop preparation/rollback (38) and peaceful-command regression (332) checks passed, with installed native API/source/locale guards. Client and matching addon Debug builds passed with zero warnings/errors.

Deployed with both Tarkov and SPT.Server closed; four DLL/symbol SHA-256 pairs match and prior artifacts are backed up, with existing SAIN attribution retained. Client `59ADE213F06A64309A1AF57266C2736C7F2D01E6FC76C0659AA2A85EA005F626`; addon `79F104C0AE8005505BDD44D14C2A9E03F9FF52767F19C32A7C70FAA893EE6F95`. No server or profile edits. Live qualification remains pending: offer a better supported primary plus a smaller protective outfit together, then a gear-only upgrade after the M60 is secondary; verify the primary kit, optional M60 cargo/drop ownership and post-raid results. Repeat with an inferior/unready candidate to confirm that the current M60's required support still blocks an unsuitable outfit.

### Depleted-primary fallback — 2026-10-19

Handover `f304bb70` retained a VSS with 8 rounds inserted and 9 in its spare while carrying the offered PP-19 as cargo. The PP-19 magazines held 19 inserted plus three 20-round spares. Normal quality scoring favored the VSS (`124.8` versus `91.92`) even though its final support check reported below-readiness ammunition and no eligible loose resupply. The user confirmed the bot fired the VSS, but clarified that this depleted package should yield to a properly supplied weaker gun. The old result was consistent with the formula, not a sufficient practical kit decision.

The quality gate now checks whether a below-readiness primary can actually be replenished first, using the existing bounded support/outfit operations. A successful replenishment retains ordinary quality comparison. Only when it cannot recover may weaker candidates proceed to the unchanged full operational/readiness/reload tests. This is explicit Give Gear evaluation only; no new autonomous scan or unconditional quantity-based replacement. Diagnostics identify resupply attempts and `currentPrimaryUnready` fallback, and draft rollback preserves exact equipment and round counts.

Validation: the depleted-VSS/supplied-weaker-gun regression failed before the correction. The planner/invitation suite passes 854 checks, including ready-current retention, compatible carried loose ammo, offered one-level magazines, carried backpack magazines, wrong-caliber resupply rejection, two unready packages, incoming gear enabling current replenishment, offering order, exact rollback and conservation. Donation access (18), shared Swap Gear (313), reload reserves (27), world-drop preparation/rollback (38), peaceful-command regression (332), installed API/source/locale guards and client/matching-addon Debug builds passed; both builds have zero warnings/errors.

Deployed with Tarkov and SPT.Server closed; all four DLL/symbol SHA-256 pairs match and previous artifacts are backed up, with existing attribution retained. Client `50813B78B3207DADD024E34B9E947FC7E5F92102035421340238337BD5F079D3`; addon `CF7CF1F26CFC2D19608C337A47166B40C0AA8E744FD83EEA77BDB6AD0AFF6E77`. No server or profile edits. Live qualification remains pending: repeat the depleted VSS versus supplied PP-19 handover, then repeat while providing sufficient eligible VSS ammunition to verify replenishment/retention. Inspect firing, reloads and displaced-kit cargo/drop ownership.

### Caliber-based actual ammunition readiness — 2026-10-19

Replaced capacity/half-full/five-round credits with the shared caliber target above for actual and projected detachable, internal and chamber-fed ordinary weapons. Detachable chamber rounds count once using cartridge compatibility (not loose-item eligibility). The existing native fast-access scan, ownership eligibility, incompatible seated-magazine veto, loading order and independent reload landing-space gates remain. Specialized internal launcher two-load readiness is retained. Ordinary tactical stocking uses 1.5 times the caliber target; shotgun stocking remains the existing three-stack policy. All unlisted calibers conservatively use 60 rounds. Installed weapon caliber identifiers were checked against the local SPT item database.

Validation: `dotnet run --project tests/WeaponReadiness/WeaponReadiness.csproj -c Debug` passes 992 checks against linked production readiness code plus all 27 embedded detachable/internal/chamber scenarios. This covers all target boundaries across five magazine capacities, actual/projected state separation, deduplication, spent chamber exclusion, fast-access eligibility, wrong seated ammunition, required insertion and independent landing-space checks. Give Gear passes 888 planner/invitation checks, including VSS 39-round fallback versus 40-round ready retention. Donation access (18), shared Swap Gear (313), reload reserve (27), loot request (18080), follower eligibility (21), installed API/source/localization guards and clean core/matching-addon Debug builds passed. Controlled EFT doubles do not prove live inventory/hands behavior.

Deployed with Tarkov and SPT.Server closed, four DLL/PDB SHA-256 matches and prior artifacts backed up; existing SAIN attribution retained. Client `653B166C9853A3F9BE984D16587820922EDC57C64E784C328F739959A004E332`; addon `8D05E3B623F190A66F275F3517093A4A26DC3E5B9E1EBE2E04362A1C83CC69D5`. No server/profile changes. Raid qualification pending: repeat VSS 8+9 versus PP-19 19+60, verify VSS retention at 40 usable rounds, exercise a five-round full-power rifle at its 30-round target and a break-action shotgun at 11 versus 12 shells, then inspect firing, reloads, cargo and post-raid ownership.

### Armored mask blocked by cosmetic cap — 2026-10-19

Handover `327ed01d` rejected the class-4 ballistic mask because native FaceCover admission required the Headwear slot occupied by a class-0 R2 cap. The prior helmet retry only covered a cosmetic face cover blocking incoming headwear. Empty-accessory admission now first tries the normal operation, then permits one reversible cap-removal retry for protective masks only. A successful retry logs the displaced cap and retains normal carry/drop handling; compatible caps, protective headwear, occupied face-cover slots and other native equipment conflicts remain untouched.

Validation: the new production-derived cap/mask regression failed before the fix. The Give Gear planner/invitation suite now passes 993 checks, including both conflict directions, loose/one-level masks, carry/drop outcomes, exact rollback, compatible caps, protective helmets, cosmetic masks, occupied FaceCover and a second blocking headset. Donation access (18), shared Swap Gear (313), reload reserve (27), world-drop preparation/rollback (38) and installed API/source/locale guards passed. Core and matching addon Debug builds passed with zero warnings/errors.

Initial deployment was blocked by SPT.Server. After the user closed it, confirmed Tarkov/SPT.Server were both closed and deployed the validated Debug client/addon pair with matching symbols. All four source/destination SHA-256 pairs matched; prior artifacts were backed up and existing SAIN attribution retained. Client `5BEDE06696717BA48817CB5C0367CAB98E8B0F8E1DADEBA89D9C53B05007F020`; addon `07261F7488093D28A3F8B21881E7BFE1FA667C94A8C4E12199D5CB0381484161`. No server/profile changes. Live qualification remains pending: retest an armored mask against the R2 cap and inspect displaced-cap ownership and final worn appearance.

### Bounded bolt/slow-semi cadence comparison — 2026-10-19

The later MPX handover did reconsider the retained weapon, but normal scores favored the supported STM-9 (83.13 versus 81.88). Both installed templates use a 450 RPM single-shot cooldown limit; neither should receive a blanket semi-auto penalty. The user approved instead discounting bolt action and genuinely slow semi-only cooldown capability. The shared Give Gear scorer now applies the bounded multiplier in section 4.1 to ordering and current-primary comparison, with passive candidate/current cadence diagnostics. Native fire mode, combat cadence, decision state and shared templates are unchanged.

Source verification: `WeaponTemplate.BoltAction`, `SingleFireRate` and `weapFireType`; `Weapon.SingleFireRate` local 240 RPM floor and its invalid-template repair; `Player.FirearmController.FireOperation.Start` single-fire cooldown `60 / SingleFireRate`. Installed template metadata/enum checks were added to `tests/Verify-GiveGear.ps1`. Displayed `bFirerate` was deliberately excluded; it is not the semi-auto cooldown limit. Only the native BoltAction flag identifies manual cycling here; bolt-catch flags and weapon class names do not create guessed penalties.

Validation: the new close-comparison regression failed before the correction. The production-linked planner/invitation suite passes 1211 checks: exact multipliers and boundaries, fast STM-9/MPX equivalence, slow single/semiauto, burst/full-auto availability, invalid/missing metadata without mutation, strong-ammunition bolt retention, unready candidate rejection, best-first order, passive diagnostics, reload safety, conservation and exact rollback. Also passed: 992 readiness checks plus embedded feed scenarios, 18 donation-access checks, 313 shared Swap Gear checks, 27 reload-reserve checks, 38 world-drop checks and installed source/API/locale guards. Core and matching addon Debug builds have zero warnings/errors. These checks do not establish measured bot firing rate or raid selection behavior.

Deployed with Tarkov and SPT.Server closed, backups and all four DLL/PDB SHA-256 pairs verified; existing attribution retained. Client `CB8581B2234DD6F4DFE078148F969CF053D5BABBC45E0ED1799507683380E5EF`; addon `CE6A2492358225E430E1A986BD14F906100F21C772E18ACC45EBB30A29491C5D`. No server/profile changes. Raid qualification pending: compare supported near-equal bolt versus self-loading packages, verify a substantially stronger bolt can remain, repeat the fast STM-9/MPX handover, and inspect cadence reasons plus actual ammunition/reload and cargo outcomes.

### Effective-range credit after cadence — 2026-10-19

Handover `6ec06e40` reconsidered the SV-98 but kept the MP-133 as primary: 68.10 versus 86.13, with the bolt penalty and no effective-range term. The SV-98 was accepted as secondary. This was a comparison rejection before proving primary ammunition support, not a forgotten gift. The user requested useful rifle reach to participate rather than allowing the cadence term alone to outweigh it. The scorer now adds the bounded native effective-range credit in section 4.1 after cadence for both candidate ranking and current-primary comparison. Pump/bolt cycling behavior is unchanged; single-shot cooldown diagnostics no longer mislabel the MP-133 as semi-auto.

Native evidence: `WeaponTemplate.bEffDist` is the integer read by the game's EffectiveDist attribute (`Weapon.CG_Ctor24/25`). Installed metadata lists SV-98 1000 m, MP-133 70 m, and both STM-9/MPX 500 m. This is not SightingRange (100 m on those samples). The installed metadata guard now checks bEffDist's field/type/public visibility. Replaying the logged pre-range totals gives SV-98 128.10 versus MP-133 100.13, above the unchanged upgrade margin; that is not a claim that the actual raid's complete primary support would pass.

Validation: a production-linked long-range bolt versus short-range primary regression failed before the correction. The planner/invitation suite passes 1425 checks, including exact range boundaries and read-only metadata, capped extreme values, missing data, observed-score replay, best-first order, backpack/secondary retained-gift reconsideration, equal-range retention, rifle-to-short-range downgrade rejection, strong ammunition retaining the current weapon, broken candidates, and independently proven primary-readiness/reload-geometry rejection after a high range-aware comparison. Conservation and exact rollback pass. Also passed: 992 readiness checks plus embedded feed scenarios, 18 donation-access checks, 313 shared Swap Gear checks, 27 reload-reserve checks, 38 world-drop checks and installed source/API/locale guards. Core and matching addon Debug builds passed with zero warnings/errors.

Initial deployment was blocked by SPT.Server. After the user closed it, confirmed Tarkov and SPT.Server were both closed and deployed the tested Debug client/addon pair with symbols, backups and all four source/destination SHA-256 pairs verified; existing SAIN attribution retained. Client `78065CA5CA7334BB9904823012D845A3C94E96F376E748A31FC2AA0DE4B99730`; addon `2D852C0A131182C0DE3AA8B4EF87D47DFD1C67E89ECC8DB2D8D034FD3C5A5DF3`. No server/profile changes. Live qualification remains pending: repeat the supported SV-98 versus MP-133 handover and inspect final primary, actual ammunition/reload support, displaced-shotgun ownership and logged effective-distance credits. Range values and score calibration are not measured live hit probabilities.

### Recruited PMC insurance remains active during the raid — 2026-10-20

Committed Give Items now records exact candidate gift IDs without deleting policies or shields. Final live ownership capture before LocalGame.Stop/follower teardown cancels only gifts still carried by surviving field PMCs, excluding player-recovered IDs and protected loans. Original recipient references cover dismissal; current field PMCs cover gifts changing hands. Player-death teardown uses the same idempotent capture before follower progress and insurance report sealing. New-raid construction resets candidates. Missing live inventory/state fails the custom settlement barrier instead of guessing ownership. Existing exact-raid server cancellation/reapplication and duplicate-return suppression are unchanged; Scav/Fence mail behavior and previously cancelled policies are not rewritten.

Validation: 25 linked-production client insurance checks cover in-raid preservation, corpse recovery, detached attachment recovery, dead native health before BotOwner death, dropped/loaned items, another living field PMC, saved teammate exclusion, Scav isolation, multiple handovers/dismissed recipient, player death, reset, repeat capture and unavailable inventories/state. Existing server cancellation (10), follower insurance (107), Give Gear planner/invitation (1425), donation access (18), shared Swap Gear (313) and installed API/source/locale guards pass. Core and matching addon Debug builds have zero warnings/errors. This is not live corpse/UI qualification.

Deployed with Tarkov and SPT.Server closed, previous artifacts backed up, existing attribution retained and all four DLL/PDB SHA-256 pairs verified. Client `BBE8198711659F1488A02205475C49F48C08E86AEAF203ED14AC35C68BB62E0F`; addon `7AA09451800096899F34F074812DE1BBF95E3D4EFCE5F61CCA99FCF5F27992E2`. No server/profile changes. Pending raid test: give an insured item to a field PMC, verify the shield during handover and on his corpse, recover it and extract without editing loadout. Separately let a surviving PMC retain a gift and confirm the invitation cannot also produce an insurance copy.

### User raid qualification — 2026-10-20

The user accepted gear selection and ammunition behavior within the intended basic rules, confirmed combat interruptions and subsequent equipment changes, and received donated cargo back. The reported missing insured Scav attachment was subsequently found in the return; no cargo correction was needed. The user also confirmed an insured PMC gift remained in the accepted recruit's equipment without the insurance shield and without an insurance message, and confirmed the corpse-recovery insurance case. Restricted protected-equipment lending was exercised with a Scav.

Remaining unconfirmed critical variants are Restricted protected gear being excluded from a PMC invitation and player/recipient death specifically during Give Items. Combat occurring during an exchange is not proof of the death-during-commit variant. These pending cases do not imply a reproduced defect.
