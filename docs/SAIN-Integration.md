# SAIN Integration Ownership

## SainMan status contact tracking (2026-09-14)

Status Report and automatic enemy markers now consume a passive `SainEnemyContact` through core `SainAddonBridge` for ready SainMan followers. The addon reports only its native selected, living, active, known enemy and SAIN's last-known location. The yellow `!` updates when that knowledge changes; hidden enemy movement is never sampled to refresh it. Fresh native visible/shootable contact retains the live red reticle and the existing sight-age limit. Native target release, forgetting or removal of its known place removes that follower's report even if EFT still holds a goal. Another follower reporting the same target can keep the shared marker alive. A handled empty/failed native report does not fall back to the EFT goal.

The UI does not select enemies, evaluate decisions, change search completion, clear memory or alter the accepted-goal combat gate. SAIN's search can extend contact retention beyond its memory time until all known places are searched; reaching a place alone is not forced to mean forgetting. Other tactics and absent/unready addon state keep core marker behavior. Existing death-marker retention/settings and report-position sound/direction behavior remain unchanged. Core owns rendering and marker aggregation; the addon supplies data only.

## Accepted-goal combat entry and recovery handoff (2026-09-14)

Ready SainMan followers no longer enter or continue ordinary solo/squad combat solely for native SAIN contacts while core has no accepted living EFT goal. The gate filters the already-calculated publication and both layers' shared handoff; it does not erase native perception or living memory. On Your Own permits investigation. At entry the addon derives combat independence from the same saved patrol/requested intent as core; later combat commands can revoke active independence without erasing patrol intent. Completed/explicit release clears active independence. Medical selections, ongoing medicine and native grenade avoidance retain their exceptions; urgent native layers remain available. Peaceful regroup commands are left for core instead of being consumed by the squad provider.

Addon linger completion and explicit combat release now call the existing core `BeginPostCombatFullHeal` bridge. Renewed enemy combat cancels that recovery before it resumes fighting. Repeated layer polls do not restart recovery. This repairs the missing post-combat handoff; it does not replace SAIN's combat first-aid/surgery policy.

Schema-13 addon snapshots add `enemyCombatAllowed` and passive `medical` inputs: native health status, time since hit, selected first-aid item/body part, cached bleeding state, known-enemy count and up to 32 known enemies' sight/hearing/path-distance timers when treatment context is relevant. Diagnostics do not invoke `ShallStartUse`, select medication, or evaluate native decision providers. Non-goal enemies matter because native first aid checks every known enemy. Exact combat-heal rejection reasons and medicine-effect resources were absent from the Interchange recording, so the remaining combat-healing delay is not yet attributed conclusively.

## Boss-oriented cover and arrival use (2026-09-14)

`SAINFollowerCover` and `SAINFollowerCoverFinder` extend native SeekCover selection for ready SainMan followers. Core combat is the reference: `FollowerCombatCommon.ScoreBossCover` and `GetCommittedCoverHoldDuration` are shared without changing core behavior. Ordinary selection prefers safe cover around the real player, then a safe intermediate cover toward a distant player. On Your Own retains native selection. Incoming fire, very recent hits, retreat and medical recovery retain native immediate-cover selection.

The addon runs one player-area collider query with at most 32 native cover-creation probes per meaningful geometry change (player/bot sector, enemy identity or last-known anchor). It uses SAIN's own cover/path validator, the core search radius and score, floor checks and destination reservations; it can find cover outside SAIN's bot-centred five-point pool. No valid preferred cover, or rejected movement, falls back to native selection. Committed paths are not redirected merely because the player moves. Selected cover is revalidated; invalidation releases the owned cover and matching path for native reselection while preserving a different movement destination.

Arrival arms the core three-second boss-cover hold, or 3.5-second recovery hold. It blocks automatic regroup and ordinary Search/MoveToEngage/ShiftCover reselection while the reached cover remains usable. It does not force movement when time expires. Visible shootable contact, native urgent combat, self-actions, squad support and explicit orders retain priority; accepted Go Forward ends the arrival hold. Repeated selection of the same reached position cannot rearm the timer. Combat release, opt-out and native-component replacement release follower-local state and claims without removing a newer owner's destination claim.

`SainCoverSelectionBridge` is core-owned optional-mod interception at native `SAINCoverClass.FindCoverPoint`; its prefix/postfix signatures and sprint field are validated. The addon contains no Harmony patches, and native cover movement/state/action execution remain in SAIN. Other tactics and ordinary SAIN bots are untouched. Schema-13 recordings add `sainCover` selection/arrival/invalidation events and a `coverPolicy` snapshot. Geometry, scan cost and full raid behavior still require in-game qualification.

## Current progress and next extension (2026-09-14)

Both SainMan combat replicas, player leadership, linger, proficiency baseline repair, two-mode regroup, bounded engagement and schema-13 recording are implemented. [ADDON-ANALYSIS.md](../ADDON-ANALYSIS.md) is the current progress/validation/deployment ledger; [SAIN-Addon-Phase1.md](SAIN-Addon-Phase1.md) preserves the narrower historical checkpoint.

The addon now interpolates follower-local personality settings from the loaded SAIN preset at 100% GigaChad, 70% Chad, 50% Normal, 30% Rat and 0% Coward. Combat numeric fields blend linearly; combat switches and native identity use the nearest anchor, with midpoint ties selecting the higher one. The scope is General/Search/Rush/Cover and tactical AggressionCoef; speech, assignment and mechanical difficulty stay neutral, with begging, fake death and taunting disabled. Combat HoldPosition applies temporary 0%, GoForward applies temporary 100%, and Gogogo restores saved aggression. [SAIN-Personalities-and-Aggression.md](SAIN-Personalities-and-Aggression.md) records the implementation choices and source findings. Core owns native installation/restoration, memory-preserving timer refresh and cached talk/search-sprint refresh. Mechanical proficiency stays normalized; no addon Harmony patches, shared preset mutation, or squad-category rerolls are introduced.

## Bounded engagement and SAIN recording (2026-09-13)

`SAINFollowerMoveToEngageAction` now commits one firing position per remembered contact through follower-local `SAINFollowerEngageAttempt`. An attempt fails after 20 seconds of active execution, six seconds without progress, two seconds at the position without a firing opportunity, or a rejected/missing path target. Medical/layer interruptions pause the execution budget. Replacing SAIN's candidate or restarting the action cannot reset it.

A failed attempt can yield to automatic regroup when the player is far away, subject to the existing safety, order, and distance gates. Otherwise the published solo fallback is `SeekCover`. The failure remains after regroup, preventing repeated outward engagement for the same contact. A new target, an enemy last-known anchor moved at least eight metres, or a visible shootable enemy permits a fresh attempt. Combat release clears it. `On Your Own` retains native unrestricted engagement and disables automatic regroup.

`SAINFollowerRecorder` integrates both replicas with core `BattleRecorder` through the optional data-only `SainCombatRecorderBridge`. Schema 13 opens one addon-owned combat episode across solo, squad, and linger; records native decisions, selected/ended action instances, and engagement failures; and supplies native SAIN enemy, cover, path, and regroup state to periodic snapshots. Native movement ownership replaces stale EFT movement targets. Capture is passive, subscriptions are released with follower state, and recording failures cannot interrupt combat. The recorder remains Debug-only and respects its existing enable setting.

## Squad regroup extension (2026-09-13)

SainMan now supports two follower-local regroup modes through `SAINFollowerRegroupObjective` and `SAINFollowerSquadRegroupAction`. Both modes publish `ESquadDecision.Regroup` through SAIN's native decision manager. Commands and ongoing regroup use the squad-provider bridge; new automatic regroup is considered only after native combat selection. Other native squad branches and solo action routing remain unchanged.

- **Auto:** follows the main combat priority rule: useful fighting, firing-position search, pursuit, squad support, survival, and committed movement take precedence. The fresh native decision must have fallen through to `SeekCover`, with its action already stationary in valid cover or cover selection exhausted, or it must be `MoveToEngage` with the bounded attempt already failed. Productive firing-position movement remains protected. Initial cover selection, travel, cover shifts, compromised cover, and visible enemy opportunities cannot trigger auto regroup, even at extreme player distance. Only then apply the configured radius, pickup-follower protection willingness, independent/pending-order gates, complete route and same-floor urban-detour checks. Four seconds of recent-fight grace applies below 1.6 times the trigger; that exception never bypasses the combat/commitment gates. The inner completion radius and retry delay are secondary safeguards, not the activation policy.
- **Command:** consumes `RegroupNearBoss` once into objective state, including tight `Exit Located`. It overrides ordinary movement and automatic fight grace, uses the real player even in independent mode, and survives the command's original timeout. New orders replace it without being consumed by this extension.
- Native medical, dogfight, melee, grenade-throw, and grenade-avoidance work may interrupt movement without erasing regroup. An order waiting for one of those actions stays pending. Enemy loss releases regroup into the existing linger/patrol handoff.
- Hot movement commits to valid, unspotted SAIN cover inside the player regroup envelope, falling back to a complete player/spread path. Cooled movement uses native run with walk fallback. Targets refresh when the player changes sector; target claims and paths are released only while still owned.
- Arrival requires a complete path, the conservative larger of path and direct distance, and 1.75m floor tolerance. Command arrival reuses core's 18m normal / 10m Factory-Labs / 4m tight distances. A selected cover destination must also be reached; entering the player radius alone does not abandon that move. A short 1.5-second settle yields immediately to renewed combat or native squad support.

Core `SainRegroupBridge` exposes shared distance, navigation and destination-reservation helpers. Core `SainSquadDecisionBridge` passes the already-calculated result from native `BotDecisionManager.SetDecisions` to the ready SainMan addon before publication; native state, timing and events still publish once. It never evaluates the solo provider a second time. Both bridge hooks are validated together and disabled on installation failure. All new combat policy remains addon-owned; the addon contains no Harmony patches and shared SAIN settings are unchanged. Other combat command translations remain deferred. Validation: 383 production addon combat/personality/cover fixture checks, 13 native-source parity checks, and the existing 32 proficiency checks passed. The matching Debug core/addon build has zero warnings or errors. In-raid movement and behavior still require verification.


The core references for this ordering are `FollowerCombatDefault.ShouldDeferBossDistanceRegroupForCommitment`, `EndCommittedHolder`, the Rifleman engagement arbitration, and `FollowerCombatCommon.CreateBlockedEnemySearchDecision`. These protect commitments and concrete combat successors before choosing regroup as a fallback. In SAIN, the corresponding proof is its fresh native result: `MoveToEngage` includes the firing-position attempt for an unreachable enemy; the addon now bounds that attempt and retains its failure across regroup. Shooting and support remain higher priority than passive `SeekCover`; ordinary `Search` also does once the reached-cover arrival hold is finished or interrupted. Cover state and movement are checked as well because `SeekCover` can still represent useful travel. A follower that completes regroup and resumes productive pursuit/search therefore remains in combat when it crosses the radius again. Regression coverage includes repeated crossings after the retry delay expires, every native combat/squad result, cover travel/arrival, reset publication, and one evaluation/event handoff.


**Status:** authoritative architecture contract

This document defines the boundary between the external SAIN plugin, the pitFireTeam core plugin, and the optional pitFireTeam SAIN addon.

## Terminology

- **SAIN plugin / SAIN mod**: the external `me.sol.sain` plugin.
- **SAIN addon**: pitFireTeam's optional `xyz.pit.fireteam.sainaddon` DLL under `addon/`.
- **Core combat**: pitFireTeam's follower combat brain implemented through the core/vanilla BigBrain path under `client/BigBrain`.
- **SAIN-addon combat**: pitFireTeam's alternative follower combat brain implemented through custom SAIN-derived layers under `addon/`. It is not the stock SAIN Squad layer and it is not a general SAIN patch collection.

## Proficiency baseline repair (2026-09-13)

The core SAIN adapter now projects finalized follower vision and scatter baselines into follower-local EFT Core settings. SAIN 4.5.1's config application leaves those fields untouched, so normalization of `Info.FileSettings` alone was insufficient. Existing Vision/Precision/Reaction factors and aim/recognition timing remain unchanged.

## First extension after the phase-one checkpoint: post-combat linger (2026-09-13)

`SAINFollowerLingerAction` now owns a three-second transition after the last active living enemy is gone. Both replicas share one follower-local handoff timer; squad combat yields to solo for linger. The action cancels the previous SAIN path and firing, keeps a horizontal look, and makes one lateral scan before normal patrol/command handoff. A live known enemy or ongoing medical use prevents premature linger; renewed combat and native grenade avoidance interrupt it. Dead remembered targets and stale decision events cannot restart combat or extend the timer. Pending commands and living enemy memory remain intact. If BigBrain retains an inactive layer while another combat signal blocks patrol, its fallback stays quiet in the dedicated action.

This is the first intentional behavior extension beyond the replicated phase-one checkpoint described below. Other custom combat command policies remain deferred; squad regroup is now implemented as documented above. Automated regression checks cover the handoff; movement and presentation still require in-raid verification.

## Historical phase-one checkpoint (2026-09-13)

The following replication scope is historical; the extensions above describe current behavior.

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

## Long-term addon purpose

The SAIN addon has exactly one responsibility:

> Create a pitFireTeam **Follower Combat Brain Layer based on SAIN**, used instead of the core/vanilla BigBrain follower combat layer.

The runtime ownership matrix is:

| Runtime | Follower combat-brain owner |
|---|---|
| SAIN not installed | pitFireTeam core/vanilla BigBrain combat |
| SAIN installed, addon absent | pitFireTeam core/vanilla BigBrain combat |
| SAIN installed, addon unready or another tactic selected | pitFireTeam core/vanilla BigBrain combat |
| SAIN installed, ready addon, SainMan selected | pitFireTeam addon solo and squad replicas |


Addon absence is a supported runtime mode, not a compatibility failure. External SAIN can still patch low-level EFT calculations in that mode, but pitFireTeam core continues to own follower combat decisions.

## SAIN solo/squad layer model and later extensions

The addon uses two replicated layers derived from public `SAINLayer`, categorized as `ESAINLayer.Combat` and `ESAINLayer.Squad`. SAIN 4.5.1 makes its concrete layers internal; replicate their small routing classes and extend our versions. Both layer replicas and player-leader squad adaptation were implemented in phase one. The explicit later extensions above include squad regroup and bounded solo engagement; other combat command translations remain deferred.

Its permitted responsibilities are limited to the custom follower brain itself:

- replace native solo/squad combat ownership for ready SainMan followers while preserving core fallback and preventing competing layers from owning the same follower;
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

Core owns the narrow registration/ownership and decision-publication interception needed by the ready follower brain. The addon supplies its calculator and fallback result through those validated bridges; native SAIN still publishes state/events once. This does not authorize general SAIN behavior patches in the addon.

`UseSainFollowerCombat(botOwner)` is exclusively a per-follower combat-brain ownership gate. It may gate the custom layer, its commands, its actions, and its lifecycle. It must never gate general external-SAIN compatibility.

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

Grenade awareness follows the active avoidance owner. In core-combat mode with SAIN installed, `FollowerSainGrenadeAwarenessPatch` restores the `BotsController.OnGrenadeThrow` notifications that SAIN 4.5.0/4.5.1 skips for SAIN-enabled followers, forwarding them to native `BewareGrenade.AddGrenadeDanger`. Followers already receiving native notifications are not notified twice or given another recognition roll. The corresponding SAIN `EnemyGrenadeThrown` tracker is bypassed for core-controlled followers to prevent duplicate warnings and fallback registrations. Native reaction probability, delay, smoke handling, cover selection, escape movement, and danger expiry remain authoritative. With ready SainMan addon combat active, SAIN's existing grenade routing remains in place. Core tactics retain the core notification route. In-raid escape and return-to-command behavior still require verification.

Tripwire awareness is core-owned through `FollowerTripwireAwarenessPatch`. It observes the real trigger and activation pin sound, confirms the tripping follower or nearby followers who hear the unobstructed sound, warns with `Spreadout`, and records the actual grenade in native `BewareGrenade`. The danger lifetime covers the real fuse; per-grenade/per-follower deduplication prevents duplicate warnings or SAIN tracking. This preserves native escape and cover selection while adding reliable recognition of a confirmed tripwire.

After the native escape, core `FollowerTripwireLayer` (priority 101) keeps a confirmed live tripwire above ordinary combat, requests, and follow. It reuses `FollowerCombatLayer.CreateBigBrainAction`: our combat hold/shoot actions defend in place, `CombatDogFightAction` handles incoming fire, and `HealAction` runs only in occupied safe cover without immediate enemy pressure. An optional movement predicate on the shared dogfight payload rejects paths through the grenade radius; ordinary combat has no predicate. Unsafe positions and other immediate dangers yield to native avoidance. Detonation or danger expiry releases the layer so combat, requests, or follow can resume. This general follower behavior is core-owned in all SAIN/addon modes.

Follower sound reactions run from a postfix on `GlobalEventDispatcher.PlaySound`, independently of EFT's per-bot hearing subscription. This preserves the existing follower hostility, audibility, cooldown, and threat-priority filters when SAIN 4.5.1 skips `BotHearingSensor.Init`. The old `BotHearingSensor.OnSoundPlayed` postfix is replaced, so SAIN 4.5.0 and no-SAIN runs also dispatch each sound to the follower handler once. No follower lifecycle subscriptions or changes to ordinary bot hearing are required.

Follower aim-point enhancement wraps `SAINShootData.GetAimTarget`: SAIN 4.5.0 takes `(Enemy, BotComponent)`, while 4.5.1 takes `(Enemy)`. A validated, compiled accessor captures the follower's `EnemyInfo`, then the complete native SAIN selector runs first. The postfix preserves a native head choice; only a native non-head result is offered to `FollowerAimTargetPolicy` for one verified-head promotion roll per retarget window. Calls that pass through EFT's `GetVisiblePartToShoot` are marked and enhanced only once at the outer SAIN boundary. The separate EFT patch is also a postfix, so vanilla/core calls follow the same native-first rule without patching SAIN's `BodyPartToShootPatch` itself. SAIN 4.5.1's optional weighted `AimTarget.ChosenPart` is read through a compiled accessor, while 4.5.0 uses `EnemyInfo.LastPartToShoot` and restores a native head after that version's later center-mass height clamp. Ordinary bots and native non-head points that cannot be safely promoted remain unchanged; shared aim weights and general SAIN presets are never modified.

Follower foliage compatibility removes foliage/grass from SAIN 4.5's line-of-sight, vision, and shooting ray masks at every distance, replacing the previous 10-metre exception. Hard geometry remains in those follower rays; non-followers retain SAIN's original masks. This bypass changes only SAIN's binary foliage veto: EFT look checks, `FollowerEnemyInfoCorrection` (including its own 10-metre foliage exception), suppression safety, and decision/commitment timing are unchanged. It does not disable foliage handling throughout the follower pipeline.

The mask bypass runs inside SAIN's native command builder through a validated startup transpiler. Follower status and the three query parameters are selected once per enemy; native body-part sampling, command order, geometry, and job cadence are preserved. The former reflection-based replacement builder is removed. An unsupported instruction/member layout logs an error and leaves the native builder unchanged. Compare the parent `VisionRaycastJob.EnemyVisionJob` timing when profiling this optimization, since removing the old separately measured prefix changes attribution as well as runtime overhead.

Player-visual contact promotion is one example of this core boundary. A target genuinely seen by the player is reported as visual contact rather than sense-only contact. If the follower has not independently seen the target, current `IsVisible` and `CanShoot` remain false while core seeds the complete personal contact record at the promotion timestamp. The addon is not involved in that compatibility path.

## Removed legacy addon patches

The historical addon patches and combat implementation were removed during the phase-one cleanup. Do not restore them from history as an initialization shortcut.

Any behavior reconsidered from those patches must follow one of these outcomes:

1. **Move to core** if it is general external-SAIN compatibility that must work with or without the addon.
2. **Reimplement inside the custom layer or a custom SAIN action** if it is genuinely part of the alternate follower combat brain and can be expressed without overwriting a general SAIN method or shared object.
3. **Remove it** if neither boundary applies.

A mixed patch must be separated along the same boundary. The addon may keep only the layer/action-local behavior; general compatibility moves to core.

## Bridge contract

Combat callbacks are registered by `SAINFollowerRuntime.Enable` and released on shutdown. They are required by the per-follower combat readiness gate, not left dormant:

- `SainAddonBridge` carries readiness-for-combat, readiness-for-patrol, force-release and decision-reset callbacks, plus existing lifecycle/boss-update events.
- `SainSquadDecisionBridge` dispatches the native squad provider and passes the already-calculated result through the follower fallback policy before native publication. Its callbacks are addon-owned; interception is core-owned.
- `SainRegroupBridge` exposes shared navigation, distance, arrival and reservation helpers. The addon consumes pending regroup orders from follower state; there is no general callback translating every combat command.
- `SainCombatRecorderBridge` registers optional Debug state-capture/active callbacks and forwards diagnostics to core `BattleRecorder`. Recorder reads cannot drive gameplay.
- `SainPlayerSquadBridge` owns real player-squad membership and leader state through its core lifecycle path. It is not a fake BotComponent leader.

General external-SAIN synchronization must call a core-owned service directly and must not be routed through `SainAddonBridge`.

## Review rule

For every proposed addon change, answer these questions in order:

1. Does it implement a decision, action, movement, command translation, or lifecycle operation inside the custom SAIN solo/squad follower combat brain? If not, it does not belong in the addon.
2. Can it be implemented through the custom layer, a custom SAIN action, or follower-local addon state without patching a general SAIN method or mutating a shared SAIN object? If not, it does not belong in the addon.
3. Is it compatibility required when external SAIN is installed but addon combat is absent? If yes, it belongs in core and must use `IsSAINInstalled` rather than `UseSainFollowerCombat`.
4. Does it change shared presets, global settings, ordinary SAIN bots, or the persisted proficiency contract? If yes, the design is invalid.
5. Is `UseSainFollowerCombat` being used for anything other than selecting, operating, or releasing the custom addon combat brain? If yes, the gate is wrong.

## SAIN 4.5.0 / 4.5.1 compatibility checks

On Windows, run `./tests/Verify-SainCompatibility.ps1 -GameRoot '<SPT game root>'`. The harness compiles the production targeting patch and sound-dispatch methods with controlled EFT/SAIN stand-ins, then exercises them with the project's real Harmony DLL under .NET Framework. It checks both target signatures, native visibility gates, no-target behavior, ordinary bots, recruitment/dismissal, teardown, and exactly one follower callback with or without an EFT hearing subscription. The existing body-part policy and sound filters are stand-ins, so the checks do not establish in-raid perception or shooting quality.

Raid validation should cover a fresh teammate and a recruited bot hearing hostile gunshots, neutral/friendly sounds remaining ignored, partially exposed enemies retaining native aim-point selection plus bounded proficiency enhancement, and startup logging `Native follower aim-target proficiency enhancement applied.` with no unsupported-layout error. Repeat with the supported SAIN version and keep the optional addon disabled unless its separate qualification is intended.
