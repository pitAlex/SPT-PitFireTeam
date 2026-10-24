# Give Gear

Status: implemented; deferred-ammunition transfer, registered-secondary, packed-vest and backpack-replacement corrections require new raid qualification. Earlier main raid behavior was verified by the user. Updated: 2026-10-23.

This document owns the current Core Give Gear contract. The approved disposition, nesting, invitation and insurance rules are collected in section 10. Dated evidence belongs in the [validation ledger](validation/Give-Gear.md); build/fixture success does not establish live raid behavior.

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

A player Scav may donate through the same editor to a native-recruited regular Scav. Remaining donated/command-collected cargo returns through the shared Fence courier when the human extracts alive with that living field follower, in either gameplay mode; it does not grant a Scav-with-Scav cooperation bonus. See [Scav cargo admission and delivery](looting/Friendly-Scav-Cargo.md).

The same staged planner/transaction also serves explicit command loot for field recruits: loose firearms, Loot & Get Weapon packages and the complete corpse kit for Loot & Get Gear. This is a headless exchange at the reserved world source, not an editor or a temporary transfer through the player's inventory. Source snapshot/identity validation joins the existing transaction boundary; player hands/UI remain untouched. Spawned teammates retain their existing loot behavior, and recruited body commands still require no living spawned teammate. See the [command-loot bridge](looting/Looting.md#recruited-follower-upgrade-bridge) for exact source selection and command limits. This does not add autonomous upgrades or broaden standalone Get Weapon/Get Gear.

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

The shared minimum remains caliber-based: pistol/SMG and intermediate rifle calibers require 60; 9×39/.366 TKM 40; 7.62×51/7.62×54R/6.8×51 30; .338 Lapua 20; shotgun calibers 12. Unknown/custom calibers conservatively require 60. Give Gear admission now uses a read-only allocation of the final equipped package: compatible loaded rounds plus loose rounds which fit the retained magazines' actual capacity. Each round and shared fast-access magazine is allocated once, primary before secondary/holster. This does not declare predicted rounds actually loaded: ordinary firing/reload readiness still uses settled game state. Internal/chamber-fed weapons use loaded and eligible loose reserves. Specialized launcher handling is unchanged. See [primary readiness](looting/Weapon-Pickup-Primary-Slot-Available.md).

### 4.2 Establish support for the selected primary

After selecting the primary candidate, check whether it can be accepted using current gear or a coherent incoming gear arrangement.

- Current armor/vest may remain when already sufficient.
- Incoming armor/vest may be used when compatible and useful for the selected primary or the armor improvement.
- The current arrangement may be an armored rig while the incoming arrangement is separate body armor plus a tactical vest, or the reverse. Evaluate that local transition together; do not equip incompatible layers one at a time.
- A smaller incoming vest is not automatically a downgrade. It is acceptable when the resulting armor arrangement is appropriate and its real grids/pockets support the selected primary's magazines and reload requirements.
- Tactical vest selection considers eligible offered vests and spare vests carried directly in the follower's normal backpack/vest/pocket grids. A later armor handover may therefore combine with an earlier carried tactical vest. Secure-container gear is not added to this candidate pool.
- With unchanged protection, a strictly larger vest can be a storage upgrade. Less capacity is acceptable when a better-protected complete outfit still supports the retained/selected primary and its real reload geometry. Capacity alone never authorizes losing protection or mandatory primary support.
- Try candidate vests in stable protection/capacity order with one selected armor choice, then consider retaining the current rig where compatible. This is a bounded supporting-gear decision, not a cross-product kit optimizer.
- Actual dimensions, rotation, grid filters and occupied cells matter. Total cell count alone is insufficient.
- Preserve item identities, attachments, installed plates, durability and stack quantities. Existing contents need real destinations; replacing a container cannot delete its contents.

For example, better separate armor plus a smaller vest and a better primary can be accepted even when the current armored rig has more total cells, provided the resulting outfit is compatible and the operational supplies fit.

If the primary candidate cannot be supported, do not force it into service. Continue independent gear decisions against the retained current primary. Unused offered weapons follow carry/drop handling.

The current primary remains mandatory until an incoming candidate qualifies through the normal upgrade comparison or the depleted-primary fallback and also passes operational support checks. An inferior offering cannot demote a ready/replenishable primary; incomplete/unready replacements never permit an armor/vest change that loses the retained current primary's required magazine/reload support. A trial for a valid primary or improved outfit temporarily defers secondary/holster weapons so their magazine shapes cannot veto the primary's arrangement; unsuccessful trials restore the exact prior equipment. This also applies in later gear-only handovers, where a previously displaced primary is already secondary.

### 4.3 Primary magazines take priority

Allocate the selected primary's usable magazine/ammunition support before attempting additional secondary or holster acquisition.

Minimum primary readiness is an admission gate, not a magazine-collection cap. Extract additional compatible usable magazines offered loose, directly inside an incoming vest/backpack, or directly inside an eligible vest packed in that backpack. The same supply exception applies to eligible carried spare vests. A vest hidden behind another backpack is not scanned. Snapshot these sources before any planning moves; only magazines/ammunition receive this extra vest-depth exception. Keeping the current vest must not leave fitting support magazines inside the unused offered vest. Apply the same support pass when a later handover supplies magazines without replacing the primary.

A full replacement vest is staged out by direct grid roots and repacked before final admission. Selected primary supplies get space first, optional secondary/holster support follows, then remaining cargo uses safe real capacity or drop escrow. Stage each direct content item intact; do not unpack nested bags or promote deeper weapons/armor into equipment candidates merely because staging exposed them. Original equipped firearms and the original eligible weapon snapshot, not transient overflow contents, define secondary candidates. Excess magazines may remain cargo or be dropped rather than filling the last reload opening. Trial rollback restores exact container nesting, identities and ammunition quantities.

Use existing readiness, ammunition-compatibility, fast-access placement and reload-reserve rules, including the appropriate internal-magazine/chamber-fed handling. This feature must not create a second, inconsistent ammunition-readiness model.

Give Gear never tops off magazines, splits stacks or merges rounds, in either the draft or Apply. It transfers magazines and whole loose stacks intact and leaves filling to regular maintenance. Fast-access magazine placement precedes loose-ammo storage; normal cargo placement preserves reload room, with the secure container as an ammunition fallback. Actual native filters and capacity apply. A lone magazine which needs filling may be left in fast access for normal filling followed by native reload; the draft must still prove its future landing opening. Never count incompatible, unavailable, mixed-ownership or excess-capacity rounds toward acceptance.

`FollowerWeaponSupplyBudget` is the shared read-only allocator for Give Gear and later refill requests. The primary claims compatible approved magazines first and protects its refill needs plus the existing tactical stock target (1.5 times caliber readiness, including allocated loaded rounds). Secondary/holster refills may use only non-conflicting magazines and surplus compatible ammunition. Different-caliber packages are independent. Native combat refill requests use the same finite allocation; ordinary bots are unchanged. Normal maintenance fills reachable spares first and uses native reload to cycle them into service. A committed handover re-arms patrol's maintenance window without filling during Apply. Successful Scav refill callbacks preserve donated-versus-native quantities when stack IDs change.

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

After the primary/outfit is settled, deferred original secondary/holster weapons get first chance to return to their original slots, before ordinary empty-slot acquisition. Their supplies use only remaining space. Existing optional spares from the old vest remain deferred until this pass instead of being packed into the new vest ahead of primary support. Current-primary rejection and whole-draft rollback restore all deferred weapons and supplies.

An otherwise empty SecondPrimaryWeapon slot can retain a complete offered/deferred long gun without ammunition or magazine support, before backpack cargo or dropping. This is physical storage, not primary readiness. The committed exchange always rebuilds its native BotWeaponInfo and selector record, even while empty; do not create an occupied but unregistered slot. Operational support candidates take precedence over this storage fallback. Holster acquisition retains its existing usability requirement. Native compatibility, functional condition and primary supply protection remain mandatory. If a seated magazine cannot land in the remaining fast-access space, detach that magazine to normal cargo/drop handling rather than bypassing the final reload-space check; the gun may remain stowed in secondary.

A later Give Items handover reevaluates eligible earlier gifts even when already equipped in secondary. If the gun is a qualified primary upgrade, allocate its magazines/ammunition first and try retaining the displaced primary as secondary using the remaining capacity. If not, service the retained secondary with compatible magazines that fit fast access without consuming the primary's allocation. Post-commit equipment reconciliation re-registers both slot records and all compatible retained fast-access magazines. The same reevaluation runs for recruited followers explicitly told Take This on a loose magazine or ammunition stack, not just a loose firearm. Whole-backpack pickup remains ordinary pickup and does not unpack a kit.

No backpack or empty normal grid is required merely to exchange shoulder slots. Existing session-owned secure staging, then the private 64-by-64 overflow container, holds temporary moves during the validated synchronous transaction. The live replay constructs matching escrow containers, preserves exact IDs/state and rolls the complete operation list back on failure. Original secure contents are untouched, and temporary weapons leave staging for final slots/cargo/drop destinations. This reuses the shared exchange rather than adding a live ad-hoc slot-move chain.

Existing retained support equipment remains subject to final compatibility/reload validation. The selected better primary's magazines take fast-access priority over the old primary, secondary or pistol's spare supply. Those older spares may remain cargo or be dropped if they do not fit safely; do not reject a valid primary upgrade just to retain them in the vest. One real opening may serve all three inserted magazines sequentially when each shape fits. Earlier carried weapons and directly carried spare vests may be reconsidered during an explicit handover; this is not a general inventory optimizer, historical gear queue or automatic plate-rebuilding system.

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
- Decide primary/support weapons, armor/vest, helmet and empty accessories before backpack migration. Equipment actually used from an incoming backpack leaves its grids; unused contents remain and consume real storage.
- Before acceptance, prove that all remaining current backpack contents can be moved into the candidate using real placement rules, accounting for the unused contents already inside it.
- Do not accept the replacement by silently deleting or dropping current contents to make it fit.
- Failed migration leaves the current backpack and contents intact; other donation categories can still succeed.
- Always drop the emptied displaced backpack after a successful upgrade; never try to carry its shell inside the new bag, vest or secure container. All current contents must migrate first. The real drop still uses the existing prepared-world-owner commit/rollback boundary.

Backpack evaluation establishes available carrying capacity after equipment/support selection and before final generic cargo placement. It must not require an arbitrary ordering of the player's drag operations. A native grid search may return the item's existing address; treat that as an already-satisfied placement, preserving reload validation without recording a redundant native move.

### Remaining items

Other equipment uses compatible-empty-slot acquisition, not occupied-slot upgrades. For example, an offered headset may fill an empty earpiece slot but does not initiate a price-based replacement of an occupied one. Secondary/holster weapon acquisition remains owned by the existing support planners.

Items not equipped are carried in valid available space. Player-offered items that cannot be equipped or carried are dropped on the ground after submission. They are not deleted, left in a disappearing virtual grid or returned to the player through an invented mail path.

The secure container may also hold compatible items temporarily while planning the exchange. These remain clone-only moves until protected Apply replays them. Existing secure contents are preserved; temporary non-ammunition items must end equipped, in ordinary cargo or in the existing ground-drop escrow, never silently retained as hidden cargo. Failed trials restore both inventory and temporary-staging bookkeeping. The existing virtual escrow remains the fallback when secure capacity/filters do not permit staging.

Giving medical supplies or grenades does not independently enable prohibited medical/combat behavior or override existing bot permissions.

## 6. Commit, interruption and conservation

Give Items must first produce a complete, validated plan with explicit equip, carry and drop outcomes. No partial live experimentation while comparing candidate gear.

Debug decision logs use a per-handover/attempt identity and label draft decisions `phase=plan`. They record primary scores, front/back outfit comparison, helmet/current-face protection and the permitted cosmetic-removal retry, backpack capacities/migration, occupied accessory policy, native move error types and enclosing trial failures. Full native equipment/offering snapshots precede planning; the accepted plan records its equipped kit and proposed drop roots. These descriptors and item summaries preserve plates, durability, attachments, ammunition, nested cargo, positions, identities, runtime grid sizes and protected-loan classification even if no invitation follows. Trial details are capped at 256 per submission; snapshots and terminal outcomes bypass the cap. `inventoryCommitted` is recorded only after authoritative inventory commitment; `phase=committed` follows drop publication, while pre-commit failures record `applyRejected` before normal rollback. Logging is passive, has no per-frame polling, and a failed diagnostic sink must never affect selection, replay, conservation or rollback. Release excludes the recording fields/sink and compiles out calls.

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
| Larger backpack with all contents fitting | After equipment selection, migrate old contents while preserving unused donated contents; drop the emptied old backpack, never carry its shell |
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

1. **Displaced original kit.** Prefer carrying it as cargo. If it does not fit, drop it in favor of better accepted gear. Exception: a successfully replaced backpack is always emptied and dropped, never carried as a shell. Do not register the original kit as a player donation or return cargo.
2. **One general container level, with the 2026-10-23 vest-supply exception.** Evaluate equipment offered directly in the donation grid or directly inside an offered container. Additionally scan magazines/ammunition directly inside an eligible vest, including a vest directly inside the offered backpack or directly carried in the follower's normal backpack/vest/pockets. Backpack → vest → magazines is eligible; backpack → backpack → vest → magazines is not. Deeper weapons/armor/accessories do not gain eligibility when repacking a selected vest. Snapshot all candidate/source depths before moving containers, and preserve complete deeper trees as cargo/drop roots. Installed weapon attachments, loaded ammunition and armor plates remain part of their parent.
3. **Different recipient ownership.** Scav donations are returnable through the existing Fence cargo flow. PMC donations are retained and can improve invitation chance by 10 percentage points per distinct accepted equipment slot, as specified above.
4. **Bounded comparison.** Try usable weapon candidates best-first, using actual ammunition and available magazine space. Protection and weapon comparison must be centralized and documented, independent of price-only ranking. No exhaustive kit-combination optimizer.
5. **Permanent gifting cancels insurance at raid end.** A committed PMC handover only records candidate IDs. Cancel only exact donated IDs/descendants still carried by living field PMCs at final capture, excluding player-recovered and protected loan items. Corpse recovery keeps the original policy. Do not create an insurance copy when the recruit later sends an invitation. Scav donations remain return cargo rather than permanent gifts.
6. **Restricted gear may be loaned.** The user explicitly permits temporary handovers of protected teammate kit, including using the recruit's displaced unprotected gear. Keep the original teammate's saved ownership, exclude loaned items from return mail/Fence and future invitations, and do not treat the loan as an insurance-cancelling permanent gift. Existing accepted profiles remain untouched.

## 11. Qualification and diagnostics

The user has accepted equipment selection and ammunition behavior within the intended basic rules and confirmed combat interruptions, later handovers, Scav returns, permanent PMC gift insurance cancellation and insured corpse recovery. Restricted lending was exercised with a Scav. Restricted loan exclusion from a PMC invitation and player/recipient death specifically during Give Items remain unconfirmed; these are qualification cases, not reproduced bugs.

The earlier unbounded runtime handover logs and per-item insurance snapshots remain retired. Debug Give Items submissions now attach the bounded passive callback and snapshots described in section 6 to investigate individual gear refusals. Observations go to the client log and, when enabled, `giveGear` events in the normal per-raid battle ledger, keyed by the same attempt ID. They do not require a later invitation to retain equipment evidence. Release sessions do not attach a logger. Transaction errors, ownership/report failures, rollback and native shield presentation remain unchanged.

See the [dated validation ledger](validation/Give-Gear.md) for investigation history, fixture counts and deployment evidence.
