# Version 1 release validation

## 2026-10-22 — source cleanup and Release testing handoff

The completed temporary release-preparation checklist has been retired. Current player-facing feature coverage lives in [the main description](../../ModDescription.md) and [the addon description](../../AddonDescription.md); behavior contracts remain under the [documentation index](../README.md). Historical implementation/qualification evidence remains in the topic validation ledgers and Git.

Removed unused Give Gear comparison/decision trace plumbing, periodic full-inventory/medical refill observations, repeated patrol-gate snapshots and temporary cargo/light diagnostic lines. Transaction errors, rollback/ownership/insurance guards and event-based battle recording remain. Installed logs, battle records, player profiles and teammate databases are not cleanup targets.

### Outstanding live qualification

- Confirm Restricted protected-loan exclusion from a PMC invitation.
- Confirm player/recipient death specifically during Give Items; combat interruption alone does not qualify that path.
- Retain the outstanding Core/vanilla Allegiance and Swap Gear raid checks from the local task list as unqualified.
- Re-test deferred ammunition handover, refill and extraction cases listed in [Give Gear validation](Give-Gear.md). Prior Debug evidence does not certify the rebuilt Release artifacts.
- Finish the user's screenshot/video review before publication. This testing handoff does not publish a GitHub release.

Previously user-qualified behavior includes basic Give Gear selection/ammunition behavior, Scav cargo return, retained PMC gifts in accepted invitations without insurance shields, insured corpse recovery and Restricted lending exercised with a Scav. Those observations do not substitute for the remaining cases above.

### Offline verification

Release Core/client and matching SAIN addon build: zero warnings/errors. Release server build: zero warnings/errors. Installed SPT 4.1.6 reference verification passes for 199 server types/772 members and 891 client types/2,224 members; 451 server-mod types load. Mod-facing metadata remains 1.0.0. These checks do not start the game or certify other runtime patch versions.

Production-linked checks pass: Give Gear 1,411; Scav cargo client 109; native Scav/Fence delivery 2,920; recruit command routing 55; caliber readiness 992; donation world-drop preparation/rollback 38; gift insurance client/server 25/10; gameplay configuration 69; mode storage/onboarding 231; hostility/selection 5,887; recruitment/speech/personality/input/greeting/interaction 890; loot request/finalizer/eligibility 18,080/15/21; follower returns 426; grenade integration 178; addon combat 1,678; medical recovery 40. Existing magazine, Swap Gear safety/clone/insurance, quick-interaction, Debug/Release spawn, encounter-penalty and runtime-diagnostic checks pass too.

Logging-only assertions were removed or replaced with inventory-outcome assertions; planner conservation and rollback coverage remains. Test harnesses report known offline NuGet vulnerability-feed and controlled-stub compiler warnings, not production build failures. The gameplay-mode rollback test deliberately prints its simulated server-failure exception and passes.
