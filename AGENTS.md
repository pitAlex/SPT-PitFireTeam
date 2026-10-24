# pitFireTeam engineering guidance

You are working on a C# Single Player Tarkov mod built with BepInEx, Harmony and BigBrain. Preserve runtime stability and existing ownership; verify EFT/SPT/SAIN APIs against source or installed metadata rather than guessing.

## Start here

- Read [LOCAL.md](LOCAL.md) for machine-local source, deployment, release-note and bug-tracker paths.
- Use the [core documentation index](docs/README.md) and [architecture map](docs/Architecture.md) to choose the relevant current contract.
- For combat, read [Core combat](docs/Combat-Tactics.md) and [Core commands](docs/Commands.md).
- For SAIN addon work, also read [addon integration](addon/docs/Integration.md) and the relevant topic under [addon/docs](addon/docs/README.md).
- Dated test/deployment evidence is in [addon validation](addon/docs/Validation.md). A previous pass does not prove the current working tree or raid behavior.

## Terminology and ownership

**SAIN plugin / SAIN mod** means the external `me.sol.sain` dependency. **SAIN addon** means our optional `xyz.pit.fireteam.sainaddon` combat brain under `addon/`. **Core** means our main client/server implementation, not unmodified EFT.

Core owns follower lifecycle, peaceful requests, core combat and general external-SAIN compatibility. The addon owns its SAINGrunt and SAINShooter combat policies, actions, local state and hooks required only by that brain. Compatibility needed without the addon stays in core. Shared SAIN presets and ordinary-bot state must not be mutated. Core has no typed SAIN/addon dependency; preserve readiness-gated core fallback.

SAINGrunt is the display name; persisted `SainMan` identifiers remain stable. Do not infer a data migration from a display rename.

## Working rules

1. Read code first. Inspect client, external dependency source and installed metadata when behavior or signatures are unclear. Separate core, external-SAIN compatibility and addon behavior in investigations.
2. Make the smallest correct change, preserve architecture/naming, reuse centralized helpers, and remove abandoned implementations. Prefer runtime stability, existing architecture, minimal changes, clear diagnostics, then elegance.
3. Preserve real contact/admission and shot-safety gates. Tracking knowledge, tactical intent, visibility and permission to fire are separate contracts. Do not clear living enemy memory to force handoff or patrol readiness.
4. Keep shared cover arrival/hold/end semantics centralized. Heal-cover arrival hands off immediately to treatment; do not insert an ordinary think hold. Read the full [cover contract](docs/Combat-Tactics.md#cover-contract) before changing it.
5. Server responsibilities are teammate profile/storage/social and raid-support flows, not a general bot-profile generator. In-raid follower spawning uses the game-side `ISession.LoadBots` flow; `fs_spawnfollower` is the Debug entry point.
6. Read [Localization](docs/Localization.md) before changing user-visible strings. Use the central language model and embedded fallback, not per-callsite English literals.
7. For a user-facing change in the active release, update the matching heading in the release-note file identified by LOCAL.md before building/packaging. Preserve unrelated local work.
8. Validate the changed boundary with the appropriate existing checks. Source/fixture success does not establish Unity navigation, perception, healing or frame time. Trace recorder events and owning code before attributing a raid symptom; add passive diagnostics if causality remains unclear.
9. Keep native decision publication single-owned and diagnostics passive. Never invoke decision/medical providers solely to record their state.

## Documentation maintenance

- `docs/` owns core and shared contracts, including compatibility with external plugins.
- `addon/docs/` owns addon adaptations. Start each addon topic with links to its core base and describe the specific differences there.
- Mixed topics must be split; a short dispatch/link reference in the core document is enough.
- Keep this file as engineering guidance, not a release diary, architecture dump or session handoff.
- Current behavior belongs in topic documents, dated validation in an evidence ledger, and unimplemented work in explicitly marked proposals/roadmaps. Remove completed temporary plans after preserving unique contracts and pending qualification. Git retains implementation history.
- Update links and source paths when moving docs. Machine-local locations stay in LOCAL.md; do not duplicate them as portable setup instructions.
