# Combat hold-facing validation

Core contract: [combat tactics](../Combat-Tactics.md). This ledger distinguishes captured regression inputs and production-method checks from live steering qualification.

## 2026-10-30 — remembered-contact facing at arrival

The captured cases in `tests/fixtures/combat/knight-arrival-facing.json` come from Customs `20261030-000219-bigmap` and Woods `20261029-224913-Woods`. They reproduce a stationary hold facing away from the admitted remembered contact after pursuit.

`CombatHoldPositionAction` now uses `CombatAttackMoveLook.TryGetHoldThreatLookPoint`. Visible contacts retain the existing visible-target path. Realistic remembered contacts must still be eligible, alive, unsearched and useful, but do not lose hold-facing merely because the shorter attack-movement steering freshness window elapsed. Facing does not grant visibility or permission to fire.

Run `tests/Verify-CombatThreatFacing.ps1` to compile the production facing, movement and hold methods against controlled EFT/Unity stand-ins and replay the captured cases. These boundary checks do not establish live animation, navigation or raid behavior; those still require in-game confirmation.

The isolated Core 1.1.0 staged snapshot passed **119** boundary checks on 2026-10-30, including the captured arrival-facing cases. Core Release client/server builds also passed; this is independent of the later Going Rogue progress snapshot.
