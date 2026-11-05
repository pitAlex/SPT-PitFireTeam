# Team Split validation

## 2026-11-05 — Attention reset and Birdeye sniper exception

Attention now bypasses split recipient exclusion for everyone and uses Core's original unconditional personal/shared-group memory reset. The split-specific `clearSharedGroup` option is removed; the existing group deduplication, two-second enforcement suppression, protected loot transaction and native/Core/SAIN reset owners remain unchanged. The split latch is not combined or discarded. Birdeye also bypasses split exclusion for Need Sniper only, entering the existing Marksman handler without adding a Help-distance limit. Knight, Pipe and Rogues retain ordinary split restrictions.

`Verify-TeamSplit.ps1` passes **394** checks. It executes the production admission policy, selected Core command handlers and actual personal/shared-memory cleanup with controlled native hosts, covering Knight/Pipe commanders, Birdeye/Rogue subordinates, a shared enemy cache, both sides' personal memory, one group removal, retained split, sniper delivery at 90m, busy refusal and unchanged bounded Help/other orders. The existing SAIN addon combat suite passes **1,705** checks, including Attention contact capture/reset lifecycle. Release client build passes with zero warnings/errors. No new decision provider, perception rule or enemy-forgetting mechanism is added beyond the explicit existing Attention command. Live command reception and reacquisition still require raid qualification.

Refreshed the Core 1.1.1 and Going Rogue 1.0.0 Release ZIPs with verified archive entries and version metadata. With Tarkov/SPT.Server stopped, deployed all 42 matching runtime/resource/symbol files with backups and installed hash verification. [Deployment receipt](../../tests/artifacts/release-test-20261105/deployed-8f8f470efb4d4f6d958386023daf0b14/manifest.json). No profile/database writes or game/server launches were performed. Current ZIP hashes and the superseded 0.1.0 deployment are distinguished in [release validation](Release-Test-2026-11-05.md).

## 2026-11-05 — Command category correction

Team Split and Team Combine now belong to Command in both normal and supported-only phrase menus. Team Status retains Status Report. Fixed IDs, eligibility and key binding persistence are unchanged. `Verify-TeamSplit.ps1` passes 326 production behavior checks plus category/ID and installed native-binding checks; Core Debug builds with zero warnings/errors. The initial deployment guard found Tarkov PID 20700 and copied no files. After the user authorized deployment with the game closed, the staged Core client DLL/PDB were deployed with verified backups and matching installed SHA-256 hashes: [receipt](../../tests/artifacts/team-split-menu/deployed-9297985ce6374f858e8eb15df184d7cd/manifest.json). No server files were changed. Live menu layout remains unobserved.

## 2026-11-05 — implementation and controlled checks

Current contracts: [Core commands](../Commands.md#team-split--team-combine), [Self-Determination](../Self-Determination.md), [Going Rogue policy](../../going-rogue/docs/Knight-Combat.md#team-split).

- `Verify-TeamSplit.ps1`: 308 checks execute production addon split state, Core input scopes, follower split/help state, extracted effective-value properties and the actual Regroup/Hold/Need Help handlers. Covers Knight/Pipe commanders, Birdeye/Rogue subordinates, separate player members, peaceful arming, combat reactivation, repeat inputs, nested gesture scopes, native-receiver admission, individual 39.99/40/40.01m and vertical Help boundaries, target death/expiry, runtime restoration and composition/mode/ownership invalidation. Stable phrase IDs, both menu construction paths and installed native binding/persistence signatures are checked separately.
- `Verify-CombatRegroupRecovery.ps1`: 83 production end-router checks, including prepared Split successors and protected medical, close-defense, firing, reload, switch, grenade, pressure and airborne cases. Rejected preparation preserves the existing action.
- `KnightCombat`: 1,330 selector/lifecycle checks, including explicit Help during independent combat and no diversion from shared player-hit state alone.
- `GoonCoordination`: 886 leadership checks, including detached members retaining commander authority when ordinary player-proximity delegation would permit advancement.
- `CompanionAnchors`: 130 checks, including a split commander's self-anchor without another Goon baseline, subordinate cohesion, temporary real-player Help anchoring and combat-gesture priority.
- Existing checks: Self-Determination 88; focused On Your Own 342; peaceful command acceptance 332; native gesture-menu filtering 13; suppression reply 24; exUsec Rifleman routing 58; SAIN addon combat 1,705. These cover 5,299 controlled behavior assertions in total, plus installed API/signature checks.
- Core and Going Rogue Debug projects build with zero warnings/errors. The SAIN fixture retains its existing stand-in/type-shadowing warnings. Core has no typed Going Rogue or SAIN dependency.

No server assembly or profile schema is required. The new English labels use the embedded fallback and central language model. Saved combat settings, native follower ownership/groups, profile equipment, contact admission and shot safety retain their existing owners.

Pending live qualification: phrase layout with both menu settings; assigning both hotkeys and retaining them through Split/Combine and eligibility loss; mixed-squad commands; Knight/Pipe-led combat; Help/gesture interruption and resumption; peaceful behavior between fights; death/removal and transit teardown. Controlled checks and deployment hashes do not establish these Unity/raid outcomes.

Deployment receipt is generated under `tests/artifacts/team-split/` with source/destination SHA-256 hashes and backups.


Deployment completed with Tarkov stopped; all four installed DLL/PDB hashes match the frozen build and all replaced files have verified backups. Receipt: [manifest](../../tests/artifacts/team-split/deployed-ad7b969206e944f3bcf626f7f828cc5f/manifest.json). Core DLL SHA-256 `CF72AC5618538B0C577DE54B7501544FA6A3356575D4EF929B8032A04165087D`; Going Rogue DLL `41054BB211B77BA872F4A8FBE78DE333C49D2AE42861E5FAD37102DD41A56480`. Client files only; no server assembly or player-profile deployment.


## 2026-11-05 — Combine also regroups to the player

A successful explicit Combine now releases split state before Core calls its existing ordinary Regroup handler for the reunited squad. Combat admission sets the real-player anchor; peaceful use retains normal peaceful Regroup handling. Repeated Combine while already combined and automatic composition invalidation do not restart orders.

`Verify-TeamSplit.ps1` now passes 326 production checks, including actual Combine and Regroup handlers, both peaceful/combat paths, all squad recipients, human-anchor selection, repeated-input stability and no rearming. The existing 83 recovery/Regroup handoff and 130 companion-anchor checks also pass (539 targeted checks). Installed API verification resolves 751 references and 20 patch targets. Core and Going Rogue Debug builds have zero warnings/errors; live regroup behavior remains unqualified.

Deployed matching Core/Going Rogue DLL/PDB files with Tarkov stopped, verified backups and destination hashes. [Deployment receipt](../../tests/artifacts/team-split/deployed-946669f9544c4e99b2c5a3cd23bac6e0/manifest.json). Core DLL SHA-256 `D2EE8D5267B87CE5BC24E58F2CD49F8875180B2A1F70AAB7BE3636B7BF0B2242`; Going Rogue DLL `200BC288A1861440A85E40416A8894308E114AF7EDC4B0F1B999E4C7088B5B96`.
