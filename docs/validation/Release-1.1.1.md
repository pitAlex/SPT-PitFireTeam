# Core 1.1.1 snapshot validation

Current release-test metadata is Core 1.1.1 / Going Rogue 1.0.0. The older 0.1.0 snapshot statements below are historical; see the [Release test record](Release-Test-2026-11-05.md) for the version correction and deployment.

Date: 2026-11-04. This is the source/version commit checkpoint, not a Release package or a new live qualification.

## 2026-11-05 - Release review fixes

Core now stores generic teammate `addonRequired` ownership, hides unavailable entries from roster/social/profile and web-editor access, preserves their saved data/account IDs, and restores availability when their owner is reinstalled. Going Rogue marks both Goons and recruited Rogues. Companion preparation permits exactly one retry with unchanged reservation identity, then logs one final error; unchanged UI redisplay remains bounded. NPC extraction recipients distinguish empty non-boss roles from real bosses. Partizan's addon override and legacy Core forcing entry are removed; native/ABPS event, chance and timing remain authoritative. Public installation text explains persistent trader/quest/item changes and the need for a pre-installation profile backup.

The targeted storage, trader/quests, preparation, preview, wave and message suites pass **2,899 checks**, plus installed API/compatibility checks. Core/Going Rogue client/server and SAIN addon Debug builds pass with zero warnings/errors. Ten matching DLL/PDB files were backed up and SHA256-verified after deployment with both processes stopped. See [Going Rogue validation](../../going-rogue/Validation.md) for scope and the deployment receipt. No offline real-profile edit or new live raid qualification was performed. Existing focused-command changes in this working tree were preserved.

## 2026-11-05 - Focused On Your Own

Core's shared On Your Own receiver now resolves the same looked-at follower as Hold Position/Go Go Go once per command (30 m, 0.15 m sphere and 4-degree alignment, with existing head/body sight checks). A selected recipient receives the existing peaceful patrol or combat determination/role-policy changes; other followers keep their current state and do not acknowledge the order. No focus retains broadcast. Existing active/death/loot gates remain, and a selected but ineligible follower cannot redirect the command to others. Core, SAIN and Going Rogue all receive this targeting through the same handler; their combat ownership and saved settings are unchanged.

Goon/Rogue movement leadership remains a separate runtime contract: Knight, then Pipe, then the human. Focusing the commander does not erase member anchors or change ordinary player-side followers. No new leader election, perception rule, Harmony patch, per-frame scan or combat provider is introduced. Only the existing command-time targeting query is added.

The receiver regression failed before the fix; afterward **342** receiver/role-policy checks, **874** companion coordination/hierarchy checks, **88** percentage-state/calibration checks and **1,705** addon combat checks pass (**3,009 total**). Coverage includes mixed role groups in peaceful/combat states, narrow targeting, fallback broadcast, feedback only from recipients and no broadcast after focused eligibility rejection. The determination fixture received a missing stand-in for the current server combat-settings lock helper; server code is unchanged. Core, SAIN and Going Rogue client Debug builds pass with zero warnings/errors. Native aiming/sight selection and mixed-squad movement remain subject to raid qualification.

After the user closed Tarkov, the client-only guard passed. Deployed matching Debug Core, SAIN addon and Going Rogue client DLLs/PDBs and verified all six SHA-256 hashes. DLL hashes: Core `3AF2E41B5DA5F891A1E62C2F2DB68A53E7AD3BD5470CE1BE559B91A8454977E6`; SAIN addon `A436CFA75585C45CBD34C4DC3A59C2BF545FD2DE14C878DC4899021130264017`; Going Rogue `20405EBED349A2AD3E563E9D8F82FDA44D8D29671CB2DA13035942B64EFC2951`. Server files were untouched; SPT.Server does not need to stop for this boundary. No backup or commit was made. Live focused selection and mixed-squad commander following remain to be tested.

## Version and builds

- Core client project, BepInEx metadata, assembly version and file version are 1.1.1; Core server project and SPT mod metadata are 1.1.1. Compiled client/server assemblies report 1.1.1.0, and compiled plugin/mod metadata reports 1.1.1.
- Going Rogue retains addon version 0.1.0. Its client dependency and server mod dependency now require Core 1.1.1 or later because this snapshot adds companion APIs used by the addon.
- Debug builds pass with zero warnings/errors for Core client/server, Going Rogue client/server and the optional SAIN addon.
- Installed SPT 4.1.6 compatibility validation resolves 225 types/862 members for Core server and 925 types/2,334 members for Core client; 561 mod types load. Going Rogue resolves 762 installed API references and 20 Harmony targets; shared planner parity passes.

## Snapshot checks

- Production SAIN report synchronization checks pass: 31, including installed hearing/report target validation.
- Production Rogue alliance checks pass: 257, including hostile-task ambient relations and existing betrayal/report behavior.
- `git diff --check` passes. Commit candidates were checked to exclude generated binaries, test artifacts, local profile backups and private databases; ignored machine-local files remain outside Git.
- Earlier feature-specific controlled checks and deployment receipts remain in [Going Rogue validation](../../going-rogue/Validation.md), [SAIN tracking](SAIN-Tracking-Reports.md), [roster portraits](Roster-Portraits-2026-11-04.md), [pinned Goons](Goon-Roster-2026-11-04.md) and [medkit treatment](Teammate-Medkits-2026-11-03.md).

Compiled/source checks do not establish new live EFT UI, navigation, perception, medical or combat behavior. The 1.1.1 metadata build is not claimed as deployed by this checkpoint; previous deployment receipts retain their original scope and versions. No Release archive or tag was created.
