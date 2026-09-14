# SAIN addon phase-one checkpoint

Checkpoint: 2026-09-13, commit `60d725bd60050a7c02ebfaac3ef73772468d2f8e`. Documentation status reviewed 2026-09-14.

This document preserves the original replication/cleanup scope. Later implemented extensions include post-combat linger and recovery, the core proficiency baseline repair, automatic/command regroup, bounded MoveToEngage, boss-oriented cover, accepted-goal combat entry, native-contact status markers and schema-13 SAIN recording. See [current progress](../ADDON-ANALYSIS.md) and [the ownership contract](SAIN-Integration.md) for their current behavior and validation.

Aggression-driven personality settings are implemented at 100% GigaChad, 70% Chad, 50% Normal, 30% Rat and 0% Coward, with interpolation and temporary combat-command overrides. [Personality findings and implementation contract](SAIN-Personalities-and-Aggression.md) records the behavior and remaining raid qualification.

The no-custom-command/no-new-tactical-policy statements below describe the checkpoint only. They do not prohibit the explicitly requested later extensions.

## SAIN addon phase 1 (2026-09-13)

The optional addon exposes **SainMan** as a selectable follower tactic when SAIN and the addon are installed. Choosing it switches that follower's combat ownership to the addon. Other tactics retain core combat; absent or unready addon state uses the existing fallback without erasing the saved selection.

The addon has **two combat-layer replicas** of SAIN 4.5.1's PMC combat:

- **SAINFollowerSoloCombatLayer** (priority 74, ESAINLayer.Combat): native solo routing, activation, action ending, and surgery transitions. No custom follower commands or tactical behavior are added in phase 1.
- **SAINFollowerSquadCombatLayer** (priority 75, ESAINLayer.Squad): native squad routing and lifecycle, with the human player as squad leader.

Both derive from public SAINLayer because SAIN's concrete layers are internal. SAINActionTypes resolves and validates internal native actions once. Registration, per-follower ownership, and activation cleanup are infrastructure differences; native solo behavior is otherwise preserved.

The squad replica uses SAINFollowerSquadDecision, a copy of the native squad provider with only player-leader checks and positions substituted. Native branch order, thresholds, personality inputs, and currently disabled automatic regroup selection stay unchanged. Player-aware copies of RegroupAction and FollowSearchParty retain native movement/steering/search behavior. They follow the real player, never a substitute bot.

Core SainPlayerSquadBridge owns real SAIN membership, human leader identity/liveness/distance, and election/cleanup. LeaderComponent remains null because its BotComponent type cannot represent a human. Core SainSquadDecisionBridge dispatches the ready follower's native provider call to the addon calculator. SAIN's existing decision manager still publishes results and events. Handled None goes to native solo selection; it does not call the native AI-leader squad provider again.

UseSainFollowerCombat(botOwner) requires SainMan, both plugins, both constructed addon layers, native state, player binding, and readiness callbacks. Native solo/squad/extract/debug layers are suppressed only for followers; ready SainMan retains native urgent-threat and flash reactions above both replicas. General external-SAIN compatibility stays core-owned, with no Harmony patches in the addon.

The previously requested Chad assignment remains follower-scoped setup outside solo policy. Shared presets, existing proficiency, and configured enemy-memory durations are preserved. Opt-out/dismiss/raid teardown restore or release state. Native decision reset does not clear living enemy memory; core live combat signals govern patrol handoff.

The premature solo command extension and old one-layer class are removed. Custom combat commands and new follower tactical policies belong to later phases. This phase is replication, cleanup, and player-leader adaptation. Historical notes describing older addon behavior do not establish current command parity.
