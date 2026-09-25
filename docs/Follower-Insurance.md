# Follower Insurance

Updated: 2026-09-25

## Scope

This document is the source of truth for pitFireTeam follower insurance as it is built. It records current behavior, safety boundaries, and validation evidence.

The system applies to saved teammates edited through their real equipment loadout. `Default` is the storage name for that real equipment and is not an insurance eligibility condition.

## Availability

Follower insurance follows the loadout-management loss model:

- `Immersive`: available
- `Realistic` (internal value `Extreme`): available
- `Restricted`: unavailable

Individual items must pass EFT's stock client-side insurance eligibility check. The purchase service also validates template restrictions, restricted item classes, discard limits, and locked attachment slots on the server.

## Current Behavior

Purchases charge real roubles and save coverage. A completed, correlated raid report schedules truly lost insured follower items through SPT's normal insurance pipeline. SPT later rolls its trader return chance and sends its usual mail. The in-raid shield, actual return mail, and retained follower-to-player coverage have been confirmed in game; dated evidence is below.

1. Right-clicking an eligible item in the follower side of `EDIT LOADOUT` exposes EFT's stock `Insure` action in Immersive and Realistic.
2. Selecting `Insure` opens EFT's stock single-item insurance window.
3. The window enumerates the eligible selected item and eligible descendants using `InsuranceCompany`.
4. EFT requests prices from `/client/insurance/items/list/cost` for every available insurer.
5. The normal SPT insurance router prices player-owned item IDs first.
6. pitFireTeam runs after that router and fills only missing prices for requested IDs found in a saved teammate's equipment-root tree.
7. Follower prices use SPT's `InsuranceService.GetRoublePriceToInsureItemWithTrader`, including the active PMC profile and selected trader.
8. The stock window displays the resulting price and lets the player change insurer.
9. Final confirmation commits any pending real equipment/stash changes through the same service-preparation boundary used by repair.
10. The client submits exact item IDs, the selected trader, and the displayed total to `/singleplayer/pitfireteam/teammate/profile/insurance`.
11. The server validates ownership/eligibility, recomputes prices with the stock service, charges through stock payment, and persists exact item/trader policies.
12. The client refreshes money, skills, trader relations, and insurance from the server while keeping the loadout editor open like repair. Staged rouble stacks are updated/removed in place and the post-payment save baseline is captured before further editing.

### Purchase safety

- Ownership comes from the saved teammate's equipment tree, not client item data. Every requested ID must be eligible; invalid mixed selections are rejected before payment.
- The client total is only a maximum charge authorized by confirmation. The server's stock price is authoritative; an increased price requires a new quote.
- Duplicate requested IDs are billed once. Already-covered IDs are not billed again and keep their existing insurer. Repeating a successfully persisted request cannot charge again for those IDs.
- Payment runs on cloned player/settings state. Insufficient money or validation failure grants no coverage and publishes no partial payment state.
- Stock payment may consume roubles from player equipment or secure containers after stash money. The authoritative money snapshot synchronizes those stacks too, alongside stock trader spending and Charisma changes.
- Built-in soft armor inserts receive coverage with the purchased parent armor, matching stock behavior; they cannot be purchased individually through locked slots.
- Purchases for one player session are serialized. The editor blocks further saves and repairs while the insurance response is uncertain; a read-only state refresh must succeed before further editing.
- Pending loadout moves committed before purchase remain committed even if the purchase is rejected. Final confirmation is a service/save boundary; cancelling the editor afterward does not undo it.
- No follower policy is added to the live PMC insured list at purchase. Only a settled, lost raid item can enter a later stock insurance-return package.
- Normal player insurance outside the follower purchase interception keeps its stock path. Quote augmentation failure leaves the original stock response usable.

Items newly staged from the player stash can already be priced by the stock router because the server still sees them in the player's saved stash until `Done` commits the editor. Existing follower-owned items require the pitFireTeam price augmentation.

### Persistence failure boundary

Player payment is saved first, followed by follower settings. A caught save failure restores the previous in-memory state and attempts compensating writes. These two stores are not crash-atomic: process termination between saves can leave a paid premium without coverage. A persistent transaction journal is deferred; ordinary response-loss recovery is handled by reloading authoritative state.

## Diagnostic Logs

Insurance client/server log markers begin with `FollowerInsurance`.

### Insurance window opened

```text
[FollowerInsurance:Open] teammateAid='...' itemId='...' templateId='...' name='...'
```

This identifies the teammate and the root item selected from the context menu.

### Server quote

```text
[FollowerInsurance:Quote] teammateAid='...' itemId='...' templateId='...' traderId='...' priceRoubles=... responseSource='...'
```

`responseSource='follower'` means pitFireTeam added the price. `stock-or-cached` means the response already contained a price for that template.

### Final confirmation

```text
[FollowerInsurance:Confirm] teammateAid='...' traderId='...' quotedRoubles=... itemIds='...'
```

This captures the selected insurer, displayed charge ceiling, and exact requested item IDs.

The server records the persisted purchase and the client records the acknowledged charge:

```text
[FollowerInsurance:Purchase] teammateAid='...' traderId='...' paidRoubles=... requested=... policies=...
[FollowerInsurance:Paid] teammateAid='...' paidRoubles=...
```

Failures use `FollowerInsurance:Purchase`, with response-loss refresh failures under `FollowerInsurance:Recovery`. `FollowerInsurance:Transfer` records policy reconciliation during equipment saves.

## Persistent Policies and Equipment Transfers

Existing insurance is stored in `InsuredItems` in the teammate's `<aid>-settings.json` document inside the encrypted teammate database. Each entry contains:

- exact item ID
- insurer/trader ID

The server reconciles existing policies when equipment is committed with `Done` (also when pending equipment changes are committed before repair or insurance). It uses validated final ownership, never a client-supplied policy or a template match. Stash-to-follower transfers move coverage from the PMC insured list to follower settings; reverse transfers move it back with the original trader and no charge. Each attachment is independent: detaching an insured scope retains its coverage, attaching an uninsured scope does not create coverage. Cancel performs no transfer unless a paid service has already committed the staged changes.

An in-raid move from a follower to the player's backpack is a separate ownership path. After stock raid-end persistence and a complete matching report barrier, an exact insured ID in the final PMC inventory transfers its original insurer to `PmcData.InsuredItems` without charging again. The server verifies the original raid-start equipment, final item ID/template, exclusive saved ownership, and absence from courier, stock transfer and pending insurance packages. It then saves the player profile and removes the old follower-settings policy. The durable transfer reservation prevents late callbacks or a restart from re-granting coverage after an item is sold. An uncertain cross-store write fails closed; the item must not be returned by insurance while it remains with the player.

Only existing server policies may migrate. Duplicate matching entries collapse; conflicting insurers or final ownership are rejected. Legacy ID collision repair remaps only a proven follower policy, never the colliding player's coverage. Settings load/save prunes absent item IDs and clears follower coverage in Restricted. An insured player item transferred to a Restricted follower loses its coverage.

Profile options and equipment-save responses carry authoritative policies. The client temporarily registers player plus selected-follower policies against the editor's item instances. Closing the editor restores only player policies against live player items; follower policies are never added to the live PMC profile. Restart reloads follower coverage from settings.

In raid, the server exposes a read-only policy snapshot tied to the exact stock `serverId` after a saved follower profile is generated. The client fetches it off-thread without delaying follower spawn and caches those exact item IDs for that raid only. EFT's grid/slot item views use the stock `InsuranceCompany` for shields, which contains only PMC policies outside Edit Loadout; a narrow item-view postfix adds the follower shield and border for cached IDs. It does not add policies to `InsuranceCompany`, change stock return requests, or mark unrelated items by template. The cache clears at raid construction and teardown; missing/mismatched raid evidence leaves the icon off without affecting paid coverage or settlement. If a corpse inventory opens before the optional display fetch completes, reopening it refreshes the view.

After transfer back to the PMC, stock sales/discards remove coverage for the removed item tree. Flea listing also removes coverage; subsequent mail return does not recreate it. Kit replacement delivers the previous kit by courier and clears its follower policies; newly generated kit IDs do not inherit coverage. Repairs retaining item IDs preserve coverage.

Equipment and settings use the existing teammate database batch; the player profile is saved separately. A caught save failure restores inventory/insurance state and compensates a completed teammate write. These two stores are not a crash-atomic transaction. Abrupt termination between writes remains subject to existing duplicate-inventory recovery.

### Qualification: 2026-09-19

- User confirmed the deployed persistence fix retains coverage after restart. This confirms the earlier transfer/persistence boundary, not the new purchase path.
- `dotnet run --project tests/FollowerInsurance/FollowerInsurance.csproj`: 27 checks pass, covering transfers, partial attachment returns, distinct traders, settings serialization, mode clearing, missing items, conflict rejection, collision remaps, repeated saves, duplicate/repeated purchase selections, ownership/eligibility rejection, underquotes, and invalid/overflowing prices.
- Debug client/server builds pass with zero warnings/errors against the configured reference baseline. This does not establish runtime purchase behavior.
- Debug client/server and server resources deployed to the configured Windows SPT 4.1.5 target with the game/server closed; DLL and English-resource SHA-256 hashes match the build/source outputs. Purchase runtime qualification is pending.
- Required purchase test: note roubles, insure a saved follower item with each trader, and verify the editor stays open with the charged amount and policy reflected. Insure another item, then move equipment and press Done; verify spent roubles do not return. Restart game/server; verify coverage survives and repeated attempts do not charge again. Repeat for an uninsured attachment on an insured weapon and an item newly staged from stash.
- Check insufficient funds, roubles carried outside stash, detached/sold parts after transfer back, and leaving Immersive/Realistic. Response-loss handling and abrupt process termination remain separately qualified failure scenarios.
- Lost coverage from the earlier diagnostic implementation cannot be reconstructed automatically. Start the test with a gun that is currently insured in the player stash.
- This historical qualification predates the return implementation; it did not test an actual lost-item return.

### User qualification: 2026-09-20

- User confirmed the insurance purchase test passed.
- User insured a helmet and its night vision on a teammate, sold the insured night vision, attached a different previously owned night vision, and restarted. The helmet retained its insurance icon and the replacement night vision remained uninsured. This qualifies that exact-item/attachment replacement scenario; it does not qualify raid returns.

The server is authoritative for ownership, eligibility, price, payment, and policy persistence.

## Raid Evidence and Settlement

The server observes the existing lifecycle without changing its outcomes:

1. After stock `/client/match/local/start`, record the server/transition identity and a generated diagnostic raid ID.
2. At `/client/game/bot/followergenerate`, snapshot each generated teammate's equipment tree and existing server-owned policies. This includes gear inside backpacks/rigs, not just worn slots. The snapshot is independent of later policy pruning and death stripping. A generation request without a final outcome remains unresolved, not proof of a successful spawn or loss.
3. After existing teammate-outcome persistence, observe the actual saved equipment plus the reported escaped carrier inventory. An insured item on another teammate counts too. Saved permanent gear on a dead teammate is retained, not lost.
4. Observe original IDs accepted by the existing courier mail service before that service remaps IDs. Client paths that clone return gear with new IDs attach diagnostic source-ID provenance keyed by the actual mailed root; only provenance whose delivery tree survived server filters is observed. Existing clone/mail behavior is unchanged. Observe the supported stock BTR/transit transfer item IDs at player raid end.
5. After stock `/client/match/local/end`, inspect the saved PMC inventory, not the request's potentially unstripped corpse inventory.
6. Outcome and courier sends carry the captured stock `serverId` and a unique report ID. Only matching-raid observations enter the ledger. At teardown, the client waits off-thread for all registered sends, then posts their ID manifest to `/singleplayer/pitfireteam/insurance/raid-reports-complete`. Negative loss conclusions require this manifest and every matching server receipt, plus no recorded preparation/send failure. Positive retained/recovered evidence remains usable while the barrier is incomplete.
7. Recompute a revisioned report as follower outcomes or courier evidence arrives. Identical classifications do not emit duplicate report revisions. Only a complete, matching report barrier permits a lost-item claim.

The encrypted teammate database holds `insurance-raid-diagnostic.json` and one preceding independent raid in `insurance-previous-raid-diagnostic.json`. The active document retains the initial full equipment JSON, exact insured item/template/trader/parent/slot identity, observed final IDs, raid identity, latest findings, and a durable one-time settlement state across server restarts. Observation failures are logged and must not interrupt spawn, inventory persistence, or mail delivery.

| Status | Meaning |
|---|---|
| `pending` | Player raid-end processing has not been observed |
| `deferred` | The player transited; the chain has not ended |
| `retained` | Exact ID remains in the final saved player or teammate inventory |
| `recovered` | Exact ID was accepted by pitFireTeam courier or a stock transfer service |
| `unresolved` | Evidence is missing/conflicting, transit continuity is incomplete, or escaped cargo has no confirmed saved/mail destination |
| `lost-candidate` | Exact insured ID is absent from every observed return path, with player and participating-teammate outcomes available |
| `ineligible` | Insurance mode is disabled |

Neither a surviving owner nor a backpack slot exempts an item from comparison. For example, an insured helmet carried in a follower backpack and discarded in raid can be a lost candidate even if the follower survives. The helmet and attached night vision are checked by their own IDs; retaining one does not imply retaining the other.

Missing outcomes for any generated teammate block negative conclusions because that teammate could be carrying someone else's insured gear. An escaped carrier item filtered out of saved gear remains unresolved until its destination is observed; it must never produce an insurance copy just because its original owner no longer owns it.

Transit carries the original snapshot forward only when the next stock transition identity matches the previous transit end. Intermediate inventory is not treated as final extraction. Missing continuity is flagged and does not establish loss.

### Logs and test

Report logs retain the `FollowerInsurance` prefix. A `lost-candidate` is still an evidence classification; only the settlement log confirms a scheduled package.

- `[FollowerInsurance:RaidStart]`: raid/server identity and whether insurance diagnostics are enabled.
- `[FollowerInsurance:RaidSnapshot]`: insured item, template, insurer, owner, and starting slot.
- `[FollowerInsurance:RaidItem]`: per-item status/reason with raid ID and report revision.
- `[FollowerInsurance:RaidReport]`: retained/recovered/lost-candidate/unresolved counts.
- `[FollowerInsurance:RaidDiagnostic]`: observation/persistence failures.
- `[FollowerInsurance:ReportBarrier]`: matching manifest acceptance, received/expected report counts, and failure state. `[FollowerInsurance:ReportRejected]` identifies missing or mismatched correlation.
- Client `[FollowerInsurance:ReportStart]` and `[FollowerInsurance:ReportsComplete]`: captured map identity and send completion.

The Shoreline loss test below confirmed a complete report barrier, one lost candidate, scheduling, the insurer's start message, and the later stock return mail. The player-retained handoff retest confirmed no claim for the item carried out by the player.

### Settlement safety and remaining limits

Outcome/courier observations are raid-correlated and require a completed-delivery manifest before claiming loss. Missing identity, missing receipts, failed preparation/sends, or a changed completion manifest fail closed. An unexpected report after completion invalidates new negative conclusions. Old clients without tags cannot complete the barrier. A client crash before completion leaves the raid unresolved.

The claim planner requires a normal raid end, known final player inventory, every generated follower outcome, complete matching manifest, intact transit continuity, and no unresolved/conflicting item. It subtracts all observed player, follower, courier, and stock-transfer IDs. It then verifies each lost ID against the server's raid-start insured policy and item snapshot, rejecting duplicate PMC policies, existing pending insurance packages, protected slots, missing source items, invalid IDs, and unknown insurers. Client-supplied item data cannot create a claim.

Only raids started after the return implementation are settlement-eligible. Older diagnostic documents stay historical evidence and cannot be replayed into new rewards after an upgrade.

Before invoking SPT, settlement writes a durable `reserved` marker with the exact item IDs. It schedules through SPT's `InsuranceService`, persists the player profile, then marks `scheduled`. Repeated callbacks and server restarts do not reissue a reserved or scheduled claim. A crash/error after reservation is deliberately **not retried automatically** because the stock package or start mail may already exist; this can lose a legitimate return but prevents duplicate rewards. Inspect the settlement log for `reserved-uncertain`. This two-store boundary is not crash-atomic.

SPT removes processed insurance packages by trader, mail date/time and map rather than by package identity. When a follower package shares that key with a player package from the same raid, settlement merges their item lists so processing one cannot silently erase the other. The existing player's package timing wins in that same-second case.

The original outcome/inventory/mail endpoints retain their existing mutation and retry behavior; report IDs deduplicate evidence receipts, not those transactions. Starting another raid before prior sends complete remains unqualified, and a late prior-raid report is rejected for the current ledger. The previous-raid document is historical evidence, not a replay queue. Transit with incomplete prior evidence remains unresolved. The completion manifest is client-reported and assumes the game client's registered sends are honest and complete.

Courier source-ID provenance is client-reported diagnostic evidence, not authorization to create or clear a policy. Older clients without provenance may leave cloned deliveries unresolved; use matching client/server builds for this test phase.

Return packages use the server's starting item snapshot, not the final in-world durability/state of discarded gear. This is a known fidelity limit until a trustworthy final-state capture exists. SPT owns the eventual per-item recovery roll, attachment treatment, trader mail, timing, storage expiry, and no-return locations such as Labs. The in-raid shield is display-only and has been confirmed in game.

### Diagnostic build validation: 2026-09-20

- Debug client and server builds: zero warnings/errors.
- Insurance fixture: 51 checks pass, including 24 diagnostic classification/provenance checks. These cover surviving-owner backpack discard, separate attachment loss, player/other-follower retention, corpse versus saved gear, missing outcomes, late courier evidence, filtered cloned delivery provenance, stock transfers, transit deferral, mode exit, conflicts, repeat classification, and serialized evidence round trips.
- Installed SPT 4.1.5 DLL loader smoke check: 665 types loaded and isolated encrypted LiteDB round trip passed.
- Matching Debug client/server DLLs deployed to the configured Windows installation while both processes were stopped. SHA-256 hashes verified against build outputs. No live teammate/profile data was edited during deployment.
- In-game diagnostic settlement remains pending the discard/retain test above. Source/fixture and loader success do not establish runtime report ordering or transit continuity.

### Observed runtime issues: 2026-09-20

- **Resolved — Missing in-raid shield.** EFT grid/slot views read `InsuranceCompany`, which contained follower policies only during Edit Loadout. The exact-raid, display-only icon path was deployed and the user confirmed the shield appeared in raid. The missing icon had not prevented return scheduling.
- **Resolved — Insurance confirmation closed Edit Loadout.** The stock insurance window closes on success, but the parent editor must remain open. The payment refresh no longer rebuilds the full stash; the later UI retest confirmed `editorOpen=True editorActive=True` after confirmation.

### User raid qualification: 2026-09-20

- Raid `907d28c5d9f54d0189d699f526580917`, fallen teammate Brick (`1862185`): user recovered gear except one insured item. The server's latest revision 3 classified 13 insured pistol/part IDs as `recovered` through pitFireTeam courier and exactly one item as `lost-candidate`: PBS-1 suppressor `6a8e21cae190d366bc0ae6be`, insured with Prapor. Zero unresolved items. This confirms that the missing corpse icons did not prevent snapshot tracking for this raid.
- This runtime pass predates the correlated report/completion barrier. Repeat the recovery/discard scenario with the new matching client/server build; require `ReportBarrier complete=True`, no report rejections, and the same recovered-versus-lost distinction. No insurance mail is expected yet.

### Report-barrier build validation: 2026-09-20

- Debug client and server builds: zero warnings/errors. Insurance fixture: 66 checks pass, including 15 new missing/stale/out-of-order/repeated/failed report, manifest, restart, and classifier-gate checks.
- Installed-runtime direct DLL smoke test: 670 server types loaded; isolated encrypted LiteDB round trip passed. `git diff --check` passed.
- Not deployed: game and server remained running at handoff. Runtime parent-editor behavior, report identity capture, teardown completion ordering, and transit still require the matching build test. Build/test success does not qualify these in-game boundaries.
- UI retest: insure an already-saved follower item, then insure an item after moving it from stash. Stock insurance window should close; Edit Loadout should remain active, show the charge/icon, and retain the correct money after Done. If it closes, inspect the Debug-only `EditorClose` stack and `UiResult` stages before changing another UI boundary.

### Debug deployment: 2026-09-23

- Current client and server Debug builds completed with zero warnings/errors. Both were copied into the configured Windows SPT 4.1.5 installation while the game and server were stopped; deployed SHA-256 hashes matched the build outputs.
- Client: `5294DE7CE1DD1DF16FCEF8407C918EB8D5F594A1AD7A38DEA4B18FF5A9F0911A`. Server: `CFC34CC5598BAF45870560A050FEAACACCBC3115CF98885562E7E24578C1CBFC`.
- Runtime confirmation of the Edit Loadout close and report completion ordering remains pending.

### UI retest: 2026-09-24

- User confirmed insurance confirmation now leaves Edit Loadout open. The client log shows a successful Prapor purchase for teammate `1335720`, paid 713 roubles; `UiResult` reports `editorOpen=True editorActive=True` after payment refresh, after the stock callback, and after selector refresh.
- The later `EditorClose` occurred at `outside-purchase` through a Unity button click, not within the insurance confirmation callback. This qualifies the reported premature parent-editor close for this tested purchase. Staged stash-to-follower purchase and post-Done money persistence were not separately reported.

### Retained-gear raid test: 2026-09-24

- User completed a normal Tarkov Streets raid with insured follower gear. Client report start and completion used server ID `TarkovStreets.Pmc 1790203490`; `ReportsComplete` sent two reports with `failed=False`. Server accepted the matching report manifest with `complete=True`, expected=2, received=2, failed=False.
- Latest server revision 4 classified all 14 insured item IDs as `retained` on saved teammate equipment. Recovered=0, lost-candidates=0, unresolved=0. This qualifies the matching-raid report barrier and retained-item path for this normal extraction. It does not test the new barrier with a lost item or enable trader returns.

### Return implementation validation: 2026-09-24

- Server and matching client Debug builds completed with zero warnings/errors against the configured compatibility references. The follower-insurance fixture passed 75 checks, including loss/retention, report-barrier, old-raid replay, reservation/restart, transit, and conflicting ownership gates.
- SPT 4.1.3 source confirms its `InsuranceService` schedules profile packages and its `InsuranceController` later applies trader return odds, mail, storage lifetime, and Labs/Labyrinth exclusion. A same-second player/follower package collision is merged because stock package cleanup keys by trader, mail date/time, and map.
- The changed Debug server DLL was deployed to the configured Windows SPT 4.1.5 installation with the game and server closed; source and installed SHA-256 both equal `FA6ADB1005F8F24A0C97E10E998025813B2D2F16C2058B932DA936BE27D74B9F`. The client DLL already matched the current build (`5294DE7CE1DD1DF16FCEF8407C918EB8D5F594A1AD7A38DEA4B18FF5A9F0911A`) and was not recopied. No SVM or player data was modified.
- The user's `pitPreset.json` in the installed SVM mod specifies Prapor, Therapist and attachment return chances of 100%, but Labs insurance is disabled. The preset file was inspected, not changed; whether SVM actually applies that preset on launch remains a runtime check.
- At this build-validation point no live return had yet been verified; the later Shoreline raid and trader mail below supplied that evidence.

### Shoreline loss and in-raid shield test: 2026-09-24

- Client completed two matching reports for `Shoreline.Pmc 1790209228`; server barrier was complete with expected=2, received=2 and failed=false. The final raid report classified 13 insured IDs retained, one exact ID (`6ab331ea1bcb7090740b2e6d`, an AK-74 60-round magazine on teammate `1878221`) lost, and zero unresolved. Settlement logged `state=scheduled items=1 traders=1 location='Shoreline'`. The user saw Prapor's insurance-start message and later confirmed the actual item-return mail.
- The user still saw no insurance shield on insured follower gear while in raid. Source inspection traced this to `GridItemView.EnableInsuranceIcons` reading `InsuranceCompany.Insured`, while follower policies were registered only for Edit Loadout. The target method was also confirmed in the installed SPT 4.1.5 assembly metadata. A display-only exact-raid policy route and item-view postfix were added; the client/server Debug builds passed with zero warnings/errors and the fixture passed 78 checks. Both Debug DLLs were deployed to the configured SPT 4.1.5 installation with the game and server closed; installed SHA-256 matched the source builds (client `FCF4195884874A96D2EED482C9BF30E9151122CD908A330BBEAD0E9308B1D79C`, server `7B893DDA92F12E29C7A1FF77BBC0343B29373E8A43E60539536A1D447864B431`). The user then confirmed the in-raid shield appeared.

### In-raid follower-to-player policy handoff: 2026-09-24

- In `RezervBase.Pmc 1790218583`, the user insured follower item `6ab48f3d6807f51830bcc341`, moved it to the player's backpack in raid, and died with loose-item loss disabled. The complete raid report classified this exact ID `retained` with reason `player-final-inventory`; no return claim was scheduled. The item nevertheless lost its shield after the raid. The same handoff gap applies to a live extraction.
- Stock SPT preserves existing PMC insured entries through its post-raid inventory replacement, but the item had only a follower-settings policy. The new completed-raid transfer path adds the original insurer to the retained exact PMC item, removes the source follower policy, and reserves the transfer against replay. The server Debug build passed with zero warnings/errors, and the insurance fixture passed 83 checks. The changed server DLL was deployed to the closed Windows SPT 4.1.5 installation; source and installed SHA-256 both equal `F655F6ABD5F0285518F91FD4EC0484E5C7FF1F0A820432914C0D388A621F0EE7`. Live retests were pending at deployment.

### Player-retained handoff retest: 2026-09-25

- In `RezervBase.Pmc 1790298101`, teammate `1335720` started with insured item `6ab49ff96807f51830c17af6` under Prapor. The matching report barrier completed (expected=2, received=2, failed=false). At raid end the exact item was `retained` in `player-final-inventory`, with no loss candidate or return claim. The server logged `FollowerInsurance:PlayerTransfer state=complete items=1 added=1`. The user confirmed the insurance shield remained afterward. This qualifies that player-retained in-raid handoff; the keep-loose-items-on-death variant has not been separately retested.

## Actual Return Contract

Settlement is raid-wide:

```text
lost insured IDs
= insured IDs belonging to participating teammates
- all insured IDs successfully leaving the raid
```

The retained set must include the player's final inventory, every surviving teammate's final equipment, gear carried out by another follower, and gear already placed in pitFireTeam recovery/return mail.

The raid ID plus durable settlement reservation prevents repeat scheduling. A lost item may be scheduled once only; an item extracted by any squad member may not produce an insurance duplicate.

After raid end and the complete report barrier, lost items pass into SPT's stock insurance-package pipeline. SPT performs its normal recovery/deletion roll when the package matures, then sends the trader message and items according to stock return-time, storage-time, and location rules.

Transit raids must defer settlement until the final raid in the transit chain.

## Implementation Ownership

- Client UI visibility, opening logs, and follower purchase interception:
  `client/Patches/LoadoutEditorEquipmentRootContext.cs`
- Patch registration:
  `client/friendlyPlugin.cs`
- Server quote augmentation, eligibility, and stock payment:
  `server/Services/FriendlyTeammateInsuranceService.cs`
- Pure policy partitioning, purchase planning, and tests:
  `server/Services/FollowerInsuranceReconciler.cs`, `server/Services/FollowerInsurancePurchasePlanner.cs`, `tests/FollowerInsurance/`
- Raid evidence and classification:
  `server/Services/FollowerInsuranceRaidDiagnostics.cs`, `server/Services/FollowerInsuranceRaidClassifier.cs`, `server/Services/FollowerInsuranceRaidBarrier.cs`, `server/Models/FollowerInsuranceRaidDiagnostic.cs`
- Claim and player-policy transfer planning, durable reservations, and stock scheduling:
  `server/Services/FollowerInsuranceSettlementPlanner.cs`, `server/Services/FollowerInsuranceSettlementService.cs`
- Read-only raid display snapshot and item-view shield:
  `server/Models/FollowerInsuranceRaidDisplayRequest.cs`, `server/Services/FollowerInsuranceRaidDiagnostics.cs`, `client/Modules/FollowerInsuranceRaidDisplay.cs`, `client/Patches/FollowerInsuranceRaidIconPatch.cs`
- Captured raid identity, tracked send receipts, and completion manifest:
  `client/Modules/FollowerInsuranceRaidReports.cs`, `server/Models/FollowerInsuranceRaidCompletionRequest.cs`
- Courier original-ID provenance (diagnostics only):
  `client/Modules/InteractableObjects.cs`, `client/Modules/FollowerDeathEscapeResolver.GearRecovery.cs`, `server/Services/FriendlyPostRaidService.cs`
- Purchase orchestration, state recovery, editor policy reload, and live display cleanup:
  `client/Patches/OtherPlayerProfileScreenPatch.Insurance.cs`
- Equipment commit integration, purchase persistence, and settings:
  `server/Services/FriendlyTeammateService.cs`, `server/Models/FriendlyTeammateSettings.cs`
- Stock insurance-cost route extension and follower purchase endpoint:
  `server/Routers/Static/FriendlyTeammateStaticRouter.cs`
  and `server/Callbacks/FriendlyTeammateCallbacks.cs`
- Localized purchase errors:
  embedded English plus `server/Resources/lang/en.json`
