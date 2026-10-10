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
| [Gameplay modes](Gameplay-Modes.md) | Guns for Hire/Allegiance comparison, switching, roster separation and settings restoration |
| [Guns for Hire](Guns-for-Hire.md) | Configurable squad hiring, equipment, recruitment and mode-specific boundaries |
| [Allegiance](Allegiance.md) | Field recruitment, friendship rolls, locked settings, greetings and encounter penalties |
| [Combat tactics](Combat-Tactics.md) | Core Rifleman/Marksman decisions, cover, push, healing, regroup and recording |
| [Commands](Commands.md) | Shared inputs, peaceful execution, core combat orders and status rendering |
| [Enemy Tracking](Enemy-Tracking.md) | Shared Simple/Realistic modes, remembered search and bounded contact lifetime |
| [Looting](looting/Looting.md) | Commanded loot, filters, ownership and return bookkeeping |
| [Primary weapon pickup](looting/Weapon-Pickup-Primary-Slot-Available.md) | Primary readiness, empty-slot acquisition and qualification matrix |
| [Secondary weapon support](looting/Weapon-Pickup-Secondary-Slot.md) | Secondary acquisition, ammunition maintenance and qualification |
| [Holster support](looting/Weapon-Pickup-Holster-Slot.md) | Shared support planner and pistol qualification |
| [Suppression](Suppression.md) | Core player-facing suppression guide |
| [Team Escape](Team-Escape.md) | Escape rolls, recovery, results and notifications |
| [PMC karma](PMC-Karma.md) | Independent friendly-kill, recruited extraction and peaceful Allegiance recovery rules |

## Squad UI and persistence

| Document | Purpose |
|---|---|
| [My Squad](My-Squad-Screen.md) | Roster/settings/profile UI, teammate hiring preview and current editor state |
| [Buy Screen](Buy%20Screen.md) | Purchase UI ownership and restore flow |
| [Loadout Management](Loadout-Management.md) | Equipment modes, teammate hiring prices/payment, transactions, spawn and extraction |
| [Swap Gear](Swap-Gear.md) | Staged in-raid manual equipment exchange, Apply, ownership and qualification |
| [Follower Insurance](Follower-Insurance.md) | Purchase, policy transfers, raid evidence, settlement and stock trader returns |
| [Teammate Storage](Teammate-Storage.md) | Database representation, migration and recovery |

## Proposals and unfinished work

- [Self Determination specification](Self-Determination-Proposal.md): proposed Core/shared setting, command overrides, calibration and churn safeguards, with a separate addon adaptation.
- [Core roadmap](../TASKS.md): retained product proposals and future work.
- [Addon roadmap](../addon/docs/Roadmap.md): addon-specific gaps and raid qualification.

## Maintenance

Keep current contracts, source evidence and planned behavior distinct. Existing empty-slot pickup, ammunition support and qualification ledgers remain current. Abandoned occupied-equipment replacement proposals are removed; Git retains their history.

[AGENTS.md](../AGENTS.md) contains engineering rules; [LOCAL.md](../LOCAL.md) contains machine-local paths. Public feature copy stays in [ModDescription.md](../ModDescription.md).
