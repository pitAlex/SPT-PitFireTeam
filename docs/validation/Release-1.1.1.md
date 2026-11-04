# Core 1.1.1 snapshot validation

Date: 2026-11-04. This is the source/version commit checkpoint, not a Release package or a new live qualification.

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
