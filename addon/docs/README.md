# SAIN addon documentation

The optional addon implements the **SAINGrunt** follower combat brain using replicas of SAIN 4.5.1's solo and squad layers. It builds on [pitFireTeam core](../../docs/README.md); the external SAIN plugin is a separate dependency. Persisted `SainMan` identifiers remain stable.

| Addon topic | Core base | Addon-specific scope |
|---|---|---|
| [Integration](Integration.md) | [Architecture](../../docs/Architecture.md), [SAIN compatibility](../../docs/SAIN-Compatibility.md) | Readiness, layer ownership, human leadership, hooks and lifecycle |
| [Combat](Combat.md) | [Combat tactics](../../docs/Combat-Tactics.md) | SAIN knowledge/actions, objectives, cover, risk, medicine and recording |
| [Commands and status](Commands.md) | [Commands](../../docs/Commands.md) | Combat translations and passive native-contact reporting |
| [Personalities and aggression](Personalities-and-Aggression.md) | [Core aggression](../../docs/Combat-Tactics.md), [proficiency](../../docs/Friendly-AI-Performance-Settings.md) | Loaded-preset tactical interpolation and restoration |
| [Enemy Tracking proposal](Enemy-Tracking.md) | [Common tracking proposal](../../docs/Enemy-Tracking.md) | Native source findings and planned Simple adaptation; not implemented |
| [SAINShooter proposal](SAINShooter-Analysis.md) | [Core Marksman](../../docs/Combat-Tactics.md#marksman-combat-behavior) | Feasibility and planned addon adaptation; not implemented |
| [References](References.md) | [Build baselines](../../docs/SPT-Compatibility.md) | Private 4.5.1 references, source provenance and attribution |
| [Validation](Validation.md) | Core validation at the relevant shared boundary | Dated fixture/deployment evidence and raid limitations |
| [Roadmap](Roadmap.md) | [Core roadmap](../../TASKS.md) | Deferred addon extensions and qualification |

Read the core base first, then the addon differences. Do not copy core contracts into another addon handoff log. General external-SAIN fixes needed without this brain belong in core documentation and code.
