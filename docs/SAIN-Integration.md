# SAIN Integration Ownership

**Status:** authoritative architecture contract

This document defines the boundary between the external SAIN plugin, the pitFireTeam core plugin, and the optional pitFireTeam SAIN addon.

## Terminology

- **SAIN plugin / SAIN mod**: the external `me.sol.sain` plugin.
- **SAIN addon**: pitFireTeam's optional `xyz.pit.fireteam.sainaddon` DLL under `addon/`.
- **Core combat**: pitFireTeam's follower combat brain implemented through the core/vanilla BigBrain path under `client/BigBrain`.
- **SAIN-addon combat**: pitFireTeam's alternative follower combat brain implemented as a custom SAIN Squad-based layer under `addon/`. It is not the stock SAIN Squad layer and it is not a general SAIN patch collection.

## Sole addon purpose

The SAIN addon has exactly one responsibility:

> Create a pitFireTeam **Follower Combat Brain Layer based on SAIN**, used instead of the core/vanilla BigBrain follower combat layer.

The runtime ownership matrix is:

| Runtime | Follower combat-brain owner |
|---|---|
| SAIN not installed | pitFireTeam core/vanilla BigBrain combat |
| SAIN installed, addon absent | pitFireTeam core/vanilla BigBrain combat |
| SAIN installed, addon present | pitFireTeam custom SAIN-addon follower combat layer |

Addon absence is a supported runtime mode, not a compatibility failure. External SAIN can still patch low-level EFT calculations in that mode, but pitFireTeam core continues to own follower combat decisions.

## SAIN Squad-derived layer model

The addon implements its brain as a custom `SAINFollowerCombatLayer` derived from `SAINLayer` and categorized as `ESAINLayer.Squad`.

Its permitted responsibilities are limited to the custom follower brain itself:

- replace the native SAIN Squad layer for pitFireTeam followers while the custom layer is active, preventing two squad-combat layers from owning the same follower;
- follow SAIN's Squad-layer decision/action model while making the human player boss the squad leader and tactical anchor;
- translate pitFireTeam combat commands and objective state into decisions owned by that custom layer;
- use appropriate native SAIN actions where they fit the player-led follower model;
- create custom SAIN actions for follower-specific regroup, protection, suppression, search, movement, or other combat behavior;
- own only the follower-local decision, action, movement, and lifecycle state required to enter, run, reset, and release that custom combat brain.

Behavior may differ from core combat because the custom layer can choose different SAIN decisions and actions. Such differences must be implemented inside the layer, its decision calculator, its custom actions, or their follower-local state.

## Forbidden addon responsibilities

The addon is not a compatibility-patch project and must not become one.

It must not:

- create general compatibility patches for the external SAIN plugin;
- Harmony-patch, replace, or post-process general SAIN methods merely to change follower proficiency, accuracy, recoil, vision, hearing, personality, target acquisition, speech, door behavior, search steering, friendly-fire policy, or similar systems;
- overwrite or mutate shared SAIN presets, global/static settings, singleton-owned settings, shared configuration objects, or other objects consumed by ordinary SAIN bots;
- use a follower-only predicate as justification for altering a general SAIN method from the addon;
- own follower Vision, Precision, Reaction, proficiency normalization, or compensation for external SAIN calculations;
- own general follower/enemy relationship repair, contact propagation, enemy-state synchronization, target acquisition, perception compatibility, or friendly-fire compatibility;
- require its callbacks for behavior that must work when SAIN is installed but the addon is absent.

The only narrow interception allowed for layer ownership is the follower-specific registration/handoff needed to run the custom layer and prevent the native SAIN Squad layer from simultaneously owning those same followers. This exception does not authorize general SAIN behavior patches.

`UseSainFollowerCombat` is exclusively a combat-brain ownership gate. It may gate the custom layer, its commands, its actions, and its lifecycle. It must never gate general external-SAIN compatibility.

## Core ownership while SAIN is installed

General external-SAIN compatibility belongs to the main plugin and is gated by `IsSAINInstalled`, not addon presence. It must behave consistently in both configurations:

- SAIN installed, addon absent;
- SAIN installed, addon present.

Core owns, among other things:

- follower-local proficiency normalization and the finalized Vision, Precision, and Reaction contract;
- final aim-time, recoil, body-part, and other calculation compatibility required because external SAIN patches EFT;
- follower/enemy friendship and hostility repair;
- contact propagation, enemy-state synchronization, target acquisition, and perception compatibility;
- general friendly-fire and shot-safety compatibility;
- any reflection or Harmony boundary required to keep external SAIN compatible with pitFireTeam followers in all supported runtime modes.

Core compatibility must be follower-scoped and must not mutate SAIN's shared preset objects. The addon may consume the already-finalized follower state, but it cannot rewrite that state through general SAIN patches.

Follower sound reactions run from a postfix on `GlobalEventDispatcher.PlaySound`, independently of EFT's per-bot hearing subscription. This preserves the existing follower hostility, audibility, cooldown, and threat-priority filters when SAIN 4.5.1 skips `BotHearingSensor.Init`. The old `BotHearingSensor.OnSoundPlayed` postfix is replaced, so SAIN 4.5.0 and no-SAIN runs also dispatch each sound to the follower handler once. No follower lifecycle subscriptions or changes to ordinary bot hearing are required.

Follower aim-point selection is intercepted at `SAINShootData.GetAimTarget`: SAIN 4.5.0 takes `(Enemy, BotComponent)`, while 4.5.1 takes `(Enemy)`. A validated, cached accessor preserves SAIN's visibility/shootability gate and passes the target's `EnemyInfo` to `FollowerAimTargetPolicy` before either the old center-mass or new weighted selector runs. A follower with no verified shot lane returns no target; non-followers retain native selection. The separate EFT `GetVisiblePartToShoot` patch continues to own core follower selection. HarmonyX runs SAIN's later prefix even when the follower prefix returns false, so core also intercepts `SAIN.Patches.Aim.BodyPartToShootPatch.Patch`. When the shared follower policy supplies a valid point, this guard returns it without running SAIN's selector or allowing it to overwrite `LastPartToShoot`. Ordinary bots and the existing EFT no-point fallback retain native behavior. This preserves the existing follower target probabilities and retarget timer without changing recoil, spread, or aim speed. The old center-mass/fallback patches and their SAIN-component registry are removed; neither shared aim weights nor general SAIN presets are modified.

Follower foliage compatibility removes foliage/grass from SAIN 4.5's line-of-sight, vision, and shooting ray masks at every distance, replacing the previous 10-metre exception. Hard geometry remains in those follower rays; non-followers retain SAIN's original masks. This bypass changes only SAIN's binary foliage veto: EFT look checks, `FollowerEnemyInfoCorrection` (including its own 10-metre foliage exception), suppression safety, and decision/commitment timing are unchanged. It does not disable foliage handling throughout the follower pipeline.

The mask bypass runs inside SAIN's native command builder through a validated startup transpiler. Follower status and the three query parameters are selected once per enemy; native body-part sampling, command order, geometry, and job cadence are preserved. The former reflection-based replacement builder is removed. An unsupported instruction/member layout logs an error and leaves the native builder unchanged. Compare the parent `VisionRaycastJob.EnemyVisionJob` timing when profiling this optimization, since removing the old separately measured prefix changes attribution as well as runtime overhead.

Player-visual contact promotion is one example of this core boundary. A target genuinely seen by the player is reported as visual contact rather than sense-only contact. If the follower has not independently seen the target, current `IsVisible` and `CanShoot` remain false while core seeds the complete personal contact record at the promotion timestamp. The addon is not involved in that compatibility path.

## Existing legacy addon patches

The current addon source contains historical patches for aim sway, hit accuracy, recoil, low light, foliage, personality/templates, enemy acquisition, speech, doors, friendly fire, and search steering. Their presence in source does not make them valid addon architecture and does not grandfather them.

Each legacy patch must follow one of these outcomes before the addon is re-enabled:

1. **Move to core** if it is general external-SAIN compatibility that must work with or without the addon.
2. **Reimplement inside the custom layer or a custom SAIN action** if it is genuinely part of the alternate follower combat brain and can be expressed without overwriting a general SAIN method or shared object.
3. **Remove it** if neither boundary applies.

A mixed patch must be separated along the same boundary. The addon may keep only the layer/action-local behavior; general compatibility moves to core.

## Bridge contract

Core-to-addon callbacks exist only to operate the optional custom brain:

- determine whether addon-owned combat state is ready to release;
- pass a combat command into the custom layer;
- reset or release the custom layer and its follower-local action state;
- clean up addon-owned follower-local state when a follower is dismissed.

General external-SAIN synchronization must call a core-owned service directly and must not be routed through `SainAddonBridge`.

## Review rule

For every proposed addon change, answer these questions in order:

1. Does it implement a decision, action, movement, command translation, or lifecycle operation inside the custom SAIN Squad-derived follower combat brain? If not, it does not belong in the addon.
2. Can it be implemented through the custom layer, a custom SAIN action, or follower-local addon state without patching a general SAIN method or mutating a shared SAIN object? If not, it does not belong in the addon.
3. Is it compatibility required when external SAIN is installed but addon combat is absent? If yes, it belongs in core and must use `IsSAINInstalled` rather than `UseSainFollowerCombat`.
4. Does it change shared presets, global settings, ordinary SAIN bots, or the persisted proficiency contract? If yes, the design is invalid.
5. Is `UseSainFollowerCombat` being used for anything other than selecting, operating, or releasing the custom addon combat brain? If yes, the gate is wrong.

## SAIN 4.5.0 / 4.5.1 compatibility checks

On Windows, run `./tests/Verify-SainCompatibility.ps1 -GameRoot '<SPT game root>'`. The harness compiles the production targeting patch and sound-dispatch methods with controlled EFT/SAIN stand-ins, then exercises them with the project's real Harmony DLL under .NET Framework. It checks both target signatures, native visibility gates, no-target behavior, ordinary bots, recruitment/dismissal, teardown, and exactly one follower callback with or without an EFT hearing subscription. The existing body-part policy and sound filters are stand-ins, so the checks do not establish in-raid perception or shooting quality.

Raid validation should cover a fresh teammate and a recruited bot hearing hostile gunshots, neutral/friendly sounds remaining ignored, partially exposed enemies retaining follower aim-point selection, and startup logging `Follower aim-target routing applied.` with no unsupported-layout error. Repeat with the supported SAIN version and keep the optional addon disabled unless its separate qualification is intended.
