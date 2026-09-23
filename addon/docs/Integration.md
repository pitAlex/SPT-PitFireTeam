# SAIN addon integration

**Scope:** the optional SAINGrunt and SAINShooter combat brains. Core is the base: read [Core architecture](../../docs/Architecture.md), [Core combat](../../docs/Combat-Tactics.md), and [Core SAIN compatibility](../../docs/SAIN-Compatibility.md) first. This document describes only how the addon takes ownership and extends that base.

## Selection and fallback

The Grunt tactic is displayed as **SAINGrunt**. The persisted `SainMan` identifier, enum value and `ProfileTacticSainMan` localization key remain unchanged. Saved teammates retain their selected tactic. A recruited bot with a native SAIN component defaults to this tactic when both plugins are installed; other recruits use Rifleman.

`UseSainFollowerCombat(botOwner)` requires the selected tactic, both plugins, both constructed addon layers, native state, a real player leader, and registered readiness callbacks. Missing/unready addon state uses core fallback without erasing the saved selection. Installation alone does not switch combat ownership.

SAINShooter is a separate persisted tactic using these same layers with [Marksman policy](SAINShooter.md). It falls back to Core Marksman; SAINGrunt falls back to Core Rifleman. Shared role/capability mapping preserves existing SainMan identifiers and recruitment defaults.

## Layers and native publication

- `SAINFollowerSoloCombatLayer` uses priority 74 and `ESAINLayer.Combat`.
- `SAINFollowerSquadCombatLayer` uses priority 75 and `ESAINLayer.Squad`.
- Both derive from public `SAINLayer`; SAIN 4.5.1's concrete layers are internal. Internal native action types are resolved and constructor-validated once by `SAINActionTypes`.
- Preserve native routing/lifecycle except for the documented [combat extensions](Combat.md) and [commands](Commands.md), including native surgery transitions when the combat enum is unchanged.
- SAIN's decision manager remains the single publisher of decisions and events. The addon filters the already-calculated publication; it does not run the solo provider twice, create another decision manager, or maintain a competing decision state.
- A handled `None` squad result permits native solo selection; it must not rerun the native AI-leader squad calculator.

## Human leadership

`SainPlayerSquadBridge` owns real SAIN membership, human leader identity, liveness, distance, election and cleanup. It follows the actual player; it does not create a fake bot leader. `LeaderComponent` stays null because the native component type cannot represent a human. Ordinary AI squad election remains native. Lifecycle and boss-group subscriptions maintain membership independently of combat readiness.

GroupSearch uses a separate temporary [search-party assignment](Squad-Support.md#search-party-cooperation) to follow the initiating bot. It does not change `LeaderComponent`, elect a replacement squad leader or alter player ownership.

## Hook ownership and lifecycle

Addon-only hooks live under `addon/` and are installed by `SAINAddonPatches` under the addon Harmony ID. This includes player leadership, squad decisions, publication filtering, push/support target preference, cover selection, the follower-specific medical extension and ready-addon firearm-only suppression command routing. Public native APIs/enums are referenced directly; cached reflection is reserved for inaccessible members and action types.

Failed installation rolls back the full addon hook set. Shutdown releases follower state and membership before removing hooks. Tactic opt-out, dismissal, combat release and native-component replacement release owned state, paths and reservations without clearing a newer owner's claim or living shared enemy memory. Personality installation/restoration belongs to addon `SainManPersonality`; [personality policy](Personalities-and-Aggression.md) is follower-local.

Core owns compatibility needed without this brain. Do not move proficiency, aim/recoil, perception, relationships, enemy synchronization, speech or general friendly-fire safety into the addon. Never mutate shared SAIN presets or ordinary-bot state. `UseSainFollowerCombat` is a combat ownership gate, not a general compatibility gate.

The addon also owns the [peaceful weapon guard](Combat.md#peaceful-weapon-handling), which suppresses idle SAIN fire-mode changes/inspections only for its selected tactics.

## Bridges

- Core `SainAddonBridge`: readiness, release/reset, lifecycle, player updates and passive enemy-contact callbacks.
- Addon `SainSquadDecisionBridge`: player-led provider dispatch and filtering before native publication.
- Core `SainRegroupBridge`, `FollowerPushGeometry`, `FollowerPushRiskPolicy`, `FollowerCombatCommandGeometry` and `Covers`: shared distance, movement, risk, reservation and protection helpers.
- Addon `SainRegroupFireSafety`: regroup-only manual suppression trigger guard; ongoing burst checks run in the action using Core shot-safety helpers.
- Addon `SainContactEnemyBridge`: accepted Contact relationship conversion before Core/native synchronization; see [Contact](Commands.md#explicit-contact-and-friendly-targets).
- Addon `SainSquadSupportBridge`: current-firearm command restriction and cached native aiming/Core suppression safety bindings; see [Squad support](Squad-Support.md).
- Core `SainCombatRecorderBridge`: optional passive Debug recording; it never drives decisions.

General external-SAIN synchronization calls a core service directly; it must not require an addon callback.

## Source map

| Responsibility | Source |
|---|---|
| Plugin startup / native action resolution | [SAINAddonPlugin](../SAINAddonPlugin.cs), [SAINActionTypes](../SAINActionTypes.cs) |
| Solo / squad replicas | [SAINFollowerSoloCombatLayer](../SAINFollowerSoloCombatLayer.cs), [SAINFollowerSquadCombatLayer](../SAINFollowerSquadCombatLayer.cs) |
| Player-led squad decisions / search movement | [SAINFollowerSquadDecision](../SAINFollowerSquadDecision.cs), [SAINFollowerFollowSearchPartyAction](../SAINFollowerFollowSearchPartyAction.cs) |
| Status contact bridge / marker rendering | [SAINFollowerRuntime](../SAINFollowerRuntime.cs), [SainAddonBridge](../../client/Modules/SainAddonBridge.cs), [PingTeamates](../../client/Utils/PingTeamates.cs) |
| Push objective coordinator / execution | [SAINFollowerObjectives](../SAINFollowerObjectives.cs), [SAINFollowerPushObjective](../SAINFollowerPushObjective.cs), [SAINFollowerMoveToEngageAction](../SAINFollowerMoveToEngageAction.cs), [SAINFollowerPushHoldAction](../SAINFollowerPushHoldAction.cs), [FollowerPushGeometry](../../client/BigBrain/FollowerPushGeometry.cs) |
| Squad suppression / ally support | [SAINFollowerSquadSupportObjective](../SAINFollowerSquadSupportObjective.cs), [SAINFollowerSquadSupportAction](../SAINFollowerSquadSupportAction.cs), [SainSquadSupportBridge](../SainSquadSupportBridge.cs) |
| Combat gesture relocation | [SAINFollowerRelocationObjective](../SAINFollowerRelocationObjective.cs), [FollowerCombatCommandGeometry](../../client/BigBrain/FollowerCombatCommandGeometry.cs) |
| Protected-cover medicine | [SainMedicalDecisionBridge](../SainMedicalDecisionBridge.cs) |
| Both-layer readiness / lifecycle | [SAINFollowerRuntime](../SAINFollowerRuntime.cs) |
| Linger / aggregate combat handoff | [SAINFollowerCombatHandoff](../SAINFollowerCombatHandoff.cs), [SAINFollowerLingerAction](../SAINFollowerLingerAction.cs) |
| Regroup policy / action / shared geometry | [SAINFollowerRegroupObjective](../SAINFollowerRegroupObjective.cs), [SAINFollowerSquadRegroupAction](../SAINFollowerSquadRegroupAction.cs), [SainRegroupBridge](../../client/Modules/SainRegroupBridge.cs) |
| Bounded firing-position engagement | [SAINFollowerEngageAttempt](../SAINFollowerEngageAttempt.cs), [SAINFollowerMoveToEngageAction](../SAINFollowerMoveToEngageAction.cs) |
| Recorder | [SAINFollowerRecorder](../SAINFollowerRecorder.cs), [SainCombatRecorderBridge](../../client/Modules/SainCombatRecorderBridge.cs), [BattleRecorder](../../client/Modules/BattleRecorder.cs) |
| Cover policy / discovery | [SAINFollowerCover](../SAINFollowerCover.cs), [SAINFollowerCoverFinder](../SAINFollowerCoverFinder.cs), [SainCoverSelectionBridge](../SainCoverSelectionBridge.cs) |
| Addon native integration / core event contract | [SainPlayerSquadBridge](../SainPlayerSquadBridge.cs), [SainSquadDecisionBridge](../SainSquadDecisionBridge.cs), [SainAddonBridge](../../client/Modules/SainAddonBridge.cs) |
| Aggression policy / settings interpolation | [SAINFollowerPersonality](../SAINFollowerPersonality.cs) |
| Native personality setup / proficiency | [SainManPersonality](../SainManPersonality.cs), [FollowerSainProficiency](../../client/Modules/FollowerSainProficiency.cs), [FollowerSainEftCoreProjection](../../client/Modules/FollowerSainEftCoreProjection.cs) |

## Review boundary

Classify each change by whether it implements this brain or is needed with the external SAIN plugin alone. Split mixed hooks accordingly. Preserve follower proficiency, accepted-contact rules, native decision publication, player leadership and core fallback. Old single-layer and general addon patch collections are superseded; their Git history is not a current implementation contract.

See [references](References.md) for the 4.5.1 build/source boundary, [validation](Validation.md) for recorded evidence, and [remaining work](Roadmap.md) for qualification and deferred changes.
