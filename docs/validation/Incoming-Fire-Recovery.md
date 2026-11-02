# Incoming-fire suppression and recovery

## 2026-11-02 — Core commit qualification

Exported the exact staged Core changes independently from pending Going Rogue integration and combat-support edits. That isolated client Debug build passed with zero warnings/errors. The isolated snapshot also passed 27 incoming-fire/recovery checks and the complete Swap Gear suite, including 22 replay-scope and 47 ammunition checks. Reference junctions and generated snapshot remain under ignored `tests/artifacts/progress-core-snapshot-2c36132fbbac4dacb496fc79dcfa728d/`. This establishes the Core commit's build/fixture boundary, not fresh Unity raid behavior. This commit preparation performed no deployment or live profile changes.

## 2026-11-02 — Shoreline investigation

Record: `20261102-042128-Shoreline.jsonl`, schema 15. The record contains Big Pipe and Birdeye, not Knight. Neither follower fires or consumes a round during its recorded combat episode. No lethal-hit event is present; `raidEnd` reports `pluginShutdown`. Last sampled HP is 910 for Pipe and 787.083 for Birdeye, so the recorder cannot reconstruct both deaths.

- Pipe: automatic regroup/arrival decisions from 345.638 to 356.719; two no-cover recovery holds until 361.737. Recovery rejects the same cover at 356.719 and 359.228 as `unsafeThreatPath`. Incoming-fire overlay suppression is rejected for a friendly lane at 360.536 and 361.537. He reaches weak recovery cover, then remains in arrival holds until his last sampled update at 366.689.
- Birdeye: repeated firing-position movement/arrival adjustments without a visible/shootable contact. Incoming-fire suppression is hard-blocked at 366.996 and later. At 368.317 he loses 7.917 chest HP immediately after arriving at weak recovery cover. The destination is blocked as `marksmanExposedAtCommittedCover`, and two no-cover holds follow before another position attempt at 373.338.

These events establish failed firing/recovery opportunities, not that every physical lane rejection was erroneous. The ground-level bullet estimate is a source-confirmed defect; the record does not identify which collider caused each hard block. Friendly-lane rejection remains a safety requirement. Opponent decisions are not recorded, so their exact native SAIN action cannot be inferred from this follower-only file.

## Implementation boundary

- Core `FollowerAwareness.BulletFelt` adds native `STAY_HEIGHT` to the frozen shooter-root estimate. Planar uncertainty, impact-protection checks and evidence lifetime are preserved. Direct-hit body reports are not offset again.
- `FollowerSuppressTargetPolicy.TryGetTargetForOwner` retains current visual/recent-report priority, then admits existing incoming-fire evidence only for the selected living attacker. The source must still be active and hostile; no live hidden-position reads occur when resolving the stored point. Common selection and the rifle action use the same lookup. Launcher plans retain their separate target ownership.
- Holds allow return suppression throughout existing recent-threat pressure, rather than requiring EFT's shorter under-fire flag. Existing execution checks are unchanged.
- Recovery retries at most four ranked retreat candidates, excluding rejected routes within that scan and the existing failed-cover blacklist. The cached pool, hard-first/weak fallback, native destination publication, navigation, final route checks and arrival lifecycle remain centralized. Ordinary non-recovery retreat calls retain their signature and ranking. This is not a new free-space lateral movement policy or a guarantee that suitable cover exists.

Regular Core followers and all three managed Goons share these mechanics. No boss-specific aggression, equipment/skill bonus, sniper range change, native SAIN decision change or Core-to-addon dependency is introduced. Marksman and assault role selection retain their existing owners.

## Verification

`Verify-IncomingFireRecovery.ps1` compiles production bullet reaction, evidence resolution, full fire overlay and retreat-selection methods against controlled EFT/Unity inputs. It covers the height/uncertainty correction, frozen hidden movement, same-source ownership, dead/inactive/non-hostile/unknown/expired/invalid evidence, visible-body priority, firing safety gates, protected impacts, alternative retreat selection, native publication, ordinary-call isolation, weak fallback and the four-route budget.

- Incoming-fire/recovery production fixture: **27** checks passed.
- Rifle suppression: **56** checks passed; launcher suppression: **30**.
- Shared follower fire safety: **37** checks passed, including Core, native SAIN, recruited/ordinary actor boundaries and actual muzzle lanes.
- Threat facing: **119** checks passed; Marksman arrival: **98**; recovery/Regroup routing: **44**.
- Enemy tracking: **56** behavior checks passed, with installed metadata and native-hearing compilation checks.
- Going Rogue compatibility: **684** API references and **17** patch targets resolved against built Core/installed EFT. Shared Rifleman push-support extraction parity passed.
- Core client Debug build: **0 warnings, 0 errors**. Scoped diff whitespace checks passed.

Deployment was initially **pending at the user's request**. The guarded deployment found Tarkov PID 44360 and SPT.Server PID 16680 and stopped before any installed file was changed. The user selected “Keep deployment pending.” Frozen client DLL/PDB, hashes and the guarded script are under ignored `tests/artifacts/incoming-fire-recovery`; the pin file is `deploy-files.json`.

These checks cannot establish actual Unity perception, navigation, bullet trajectories or survival. Observe future ordinary raids after deployment; the exact prior attack sequence is not required.

## Deployment completed

The user later authorized deployment. Rebuilt the Core and Going Rogue clients together into isolated `tests/artifacts/solo-pipe-final-build/` (0 warnings/errors), and checked the compiled incoming-fire and recovery methods before deployment. The earlier stale Core staging file was not installed. Installed the Core client DLL/PDB together with the matching Pipe-initiative addon client DLL/PDB, using stopped-process guards and backups. Independent source/installed SHA-256 checks passed for all four files. Receipt: `tests/artifacts/solo-pipe-installed-5c889062268d46479cc8d4e396f40478/manifest.json`. This establishes deployment, not live combat qualification.
