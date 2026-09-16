# Core documentation

These documents describe pitFireTeam core and shared behavior. The optional SAINGrunt brain has a separate [addon documentation index](../addon/docs/README.md). An addon topic references its core base rather than duplicating it here. Compatibility with the external SAIN plugin remains a core responsibility even when the addon is absent.

## Architecture and development

| Document | Purpose |
|---|---|
| [Architecture](Architecture.md) | Client/server ownership, lifecycle invariants and source map |
| [SPT compatibility](SPT-Compatibility.md) | Build/reference baseline and packaging boundaries |
| [SAIN compatibility](SAIN-Compatibility.md) | Core integration with the external plugin, including deferred shot-safety investigation |
| [Localization](Localization.md) | Centralized text and locale maintenance |
| [Proficiency](Friendly-AI-Performance-Settings.md) | Vision, Precision and Reaction calculations and qualification |

## Runtime behavior

| Document | Purpose |
|---|---|
| [Combat tactics](Combat-Tactics.md) | Core Rifleman/Marksman decisions, cover, push, healing, regroup and recording |
| [Commands](Commands.md) | Shared inputs, peaceful execution, core combat orders and status rendering |
| [Looting](Looting.md) | Commanded loot, filters, gear swaps, ownership and return bookkeeping |
| [Primary weapon pickup](Weapon-Pickup-Primary-Slot-Available.md) | Readiness/placement contract and recorded qualification matrix |
| [Secondary weapon support](Weapon-Pickup-Secondary-Slot.md) | Ammo/magazine maintenance and outstanding verification |
| [Holster support](Weapon-Pickup-Holster-Slot.md) | Shared support planner and pistol qualification |
| [Suppression](Suppression.md) | Core player-facing suppression guide |
| [Team Escape](Team-Escape.md) | Escape rolls, recovery, results and notifications |

## Squad UI and persistence

| Document | Purpose |
|---|---|
| [My Squad](My-Squad-Screen.md) | Roster/settings/profile UI and current editor state |
| [Buy Screen](Buy%20Screen.md) | Purchase UI ownership and restore flow |
| [Loadout Management](Loadout-Management.md) | Equipment modes, transactions, spawn and extraction |
| [Teammate Storage](Teammate-Storage.md) | Database representation, migration and recovery |

## Proposals and unfinished work

- [Enemy Tracking](Enemy-Tracking.md): common/core proposal; the setting is **not implemented**. Native SAIN findings and addon adaptation live in [addon tracking](../addon/docs/Enemy-Tracking.md).
- [Follower Insurance](Follower-Insurance.md): current diagnostic phase and proposed settlement work; do not confuse the plan with a complete insurance feature.
- [Core roadmap](../TASKS.md): retained product proposals and future work.
- [Addon roadmap](../addon/docs/Roadmap.md): addon-specific gaps and raid qualification.

## Maintenance

Keep current contracts, source evidence and planned behavior distinct. Completed phase-one/rework handoffs and repeated release diaries have been consolidated into the addon topics; Git retains their history. Detailed weapon-pickup contracts and test matrices remain useful, while their completed session progress log has been removed. Existing local deletions and unrelated code changes are outside this reorganization.

[AGENTS.md](../AGENTS.md) contains engineering rules; [LOCAL.md](../LOCAL.md) contains machine-local paths. Public feature copy stays in [ModDescription.md](../ModDescription.md).
