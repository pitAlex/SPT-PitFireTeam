# SAIN Integration Ownership

## Addon-only SAIN hook ownership (2026-09-16)

Patches used only by the SAIN addon belong in `addon/`. `SAINAddonPatches` installs player-squad leadership, squad decisions, native decision-publication filtering, push-target preference and cover selection under the addon Harmony ID. Failed installation rolls back the complete addon hook set; shutdown releases follower state/membership before removing hooks. `SainManPersonality` also lives in the addon. Public SAIN APIs and enum types are referenced directly; cached reflection remains only for private setters/methods and internal action types.

Core retains compatibility needed without the addon: vision recovery/foliage, aim/recoil/proficiency, enemy synchronization, speech, friendly fire, grenade routing and native layer/weapon/reload guards. The mixed leader-assignment hook was split: core still prevents core followers becoming native AI leaders, while the addon owns human-led squad behavior. Core has no typed SAIN reference. `SainAddonBridge` carries passive leadership diagnostics and lifecycle notifications; shared core navigation/cover helpers remain in core. This supersedes older instructions placing all SAIN interception in core.

Validation: Debug core/addon build passed with zero warnings or errors; 569 production combat checks, 52 leadership/ownership checks, 13 replica comparisons, 49 compatibility checks and 32 proficiency checks passed. Compiled metadata confirms the moved integration types exist only in the addon and core has no typed SAIN/addon dependency. In-raid qualification remains required. Deployed the matching Debug core/addon DLLs and PDBs plus license on 2026-09-16 at 05:48; all five installed SHA-256 hashes match the build outputs. See ADDON-ANALYSIS.md for installed DLL hashes and backup location.

## Picked-up SAIN bots default to SainMan (2026-09-16)

The centralized `BossPlayers.AddBotFollower` pickup branch now selects SainMan when SAIN and the addon are installed and the recruited bot already has a native SAIN component. Detection reuses `BotFollowerPlayer`'s cached native accessor before follower construction/Init. Saved squadmates retain their configured tactic; non-SAIN recruits, missing plugins and failed detection retain Rifleman. Recruit aggression/proficiency rules and recruitment acceptance are unchanged. Actual addon activation still requires the existing per-follower layer/native/player readiness gates, with core fallback while unready.


## Regroup/cover churn and publication handoff (2026-09-16)

Customs record `20260915-174004-bigmap.jsonl` shows regroup completed at 1435.921, boss cover reached at 1439.69, another auto regroup at 1443.008, completion at 1446.710 and return to the same cover at 1450.36. The three-second arrival hold worked, but ordinary cover selection could undo regroup. The record also contains 21 regroup action instances shorter than 50 ms: the objective completed before native publication, and the layer repeatedly reselected the stale Regroup result.

`SAINFollowerRegroupObjective.Complete` now hands the completion radius to follower-local cover state and releases the old native cover latch. For the same remembered contact, ordinary cover selection accepts only same-floor, complete-route destinations inside the completed regroup area, capped below the current auto trigger. An empty/rejected local selection is handled without native outward fallback. The existing bounded boss-area finder is reused. The area follows the real player rather than a frozen world point. Renewed visible shootable contact, an enemy change or last-known anchor moved eight metres, accepted Go Forward, independent mode and combat cleanup release the constraint. Urgent under-fire/recent-hit/retreat/medical selection keeps native recovery fallback. Existing committed movement and cover arrival holds are not redirected merely because the player moves. This preserves the main mod's terminal boss-local cover intent without copying its complete regroup planner.

The squad replica now retains a quiet completed regroup action while native CurrentSquadDecision still says Regroup, draining stale reset events. Movement/steering are already inactive after objective completion. The next native publication, medical/urgent preemption and combat handoff retain ownership; the addon does not force an extra decision publication.

Validation: 565 production addon checks (24 new churn checks) and 13 replica comparisons pass. Tests cover actual completion clearing the previous cover, ordinary and native fallback rejection, route/distance boundaries, failed movement, urgent/independent/contact/order exceptions, cleanup, repeated pre-publication polls and medical/support/solo handoff. Unity navigation and the full recorded scenario still need in-raid qualification.


## SAIN follower body-first aim selection (2026-09-15)

Core's existing SAIN aim-target postfix now prefers the follower's verified body point before applying the shared Precision head enhancement. This matches the main mod's body-first intent: an available torso takes priority over SAIN's weighted limb/head baseline; the existing bounded head promotion remains. `FollowerAimTargetPolicy.TryGetBodyFirstShootPoint` reuses corrected head/body eligibility, falling back to shootable-and-visible part flags when no fresh correction is cached. A blocked torso retains native exposed-part fallback. Native visibility/shootability entry gates, underbarrel targeting, ordinary SAIN bots, shared aim weights and the main mod's own targeting path are unchanged. The addon owns no new patch. Bush visibility is unchanged.

`Verify-SainCompatibility.ps1` passes 49 real-Harmony routing/compatibility checks, including 14 new checks using production body eligibility for SAIN 4.5.0/4.5.1 boundaries, torso priority, one head enhancement, corrected blockage, cache absence, launcher exclusion and ordinary-bot isolation. Head-roll outcome and physical lanes remain controlled fixture inputs; actual shot distribution requires raid verification.


## Core shared SAIN vision recovery (2026-09-15)

The Shoreline EFT log at 03:38:47.818 records a collection-modified exception in native `SAINBotLookClass.UpdateLookForEnemies`, escaping the shared `VisionRaycastJob.UpdateEFTVision` coroutine. This can halt sight updates across SAIN bots. The initiating collection mutation is not yet attributed; the near-simultaneous core acquisition is a lead, not proof.

Core `SainVisionRecoveryPatch` wraps the native iterator factory for all SAIN bots, with or without the addon. On failure, `SainVisionRecoveryEnumerator` logs the exception, disposes the failed iterator, waits one second and creates a fresh native iterator inside the same Unity coroutine. It never starts another Unity coroutine, resets bot combat, clears enemy memory, or restarts the separate raycast job. Native yields and normal completion are preserved. Native job disposal, destroyed controller and explicit wrapper disposal stop retries. Factory/update/current/disposal failures are contained; repeated reports are limited to 30 seconds per wrapper and include cumulative failures. A persistent underlying fault can still prevent a full vision pass; this is recovery containment, not a claim that the collection-mutation cause is repaired.

`tests/Verify-SainVisionRecovery.ps1` validates the installed native factory/lifecycle signatures and runs 23 checks against the production wrapper and real Harmony boundary, including a real HashSet mutation, successful retry, repeated faults, factory failure, logging failure and teardown. Full in-raid recovery still requires qualification. The hook must be installed before the raid's native coroutine starts; this build cannot retroactively repair an already-running old session.


## Rejected automatic push yields to regroup (2026-09-15)

Raid `20260915-021113-Interchange.jsonl` showed automatic push stuck in `Assessing / hold.riskScore`: after reaching cover at 1031.368 and finishing its arrival hold at 1034.368, the follower stayed in SeekCover while player separation exceeded 83 m. Later cover reselection continued until commanded regroup at 1051.246. The objective coordinator previously considered regroup only for an Exhausted push, bypassing the normal fallback for rejected advancement.

Core Rifleman is the reference: `FollowerCombatRiflemanEngagement` returns Engage, Hold or Regroup, and settled-cover handling checks the boss objective before passive repositioning. The addon coordinator now lets an automatic Assessing push reach the existing `TryBeginAuto` checks using its resulting SeekCover decision. It does not mark the push exhausted or clear its retained target. Regroup owns movement once admitted; the push remains paused. Committed native movement, initial cover selection, arrival holds, compromised cover, visible contacts, medical/recovery work, player-path/distance gates, recent-fight grace, orders and On Your Own retain their existing protections. Ordered pushes and productive committed advances do not use this new handoff.

Twelve production regressions cover local hold, moving/assigned cover, arrival expiry and one-publication handoff, retained regroup ownership, exhausted cover selection, recovery, visible fire, incomplete player routes, independence and ordered intent. In-raid qualification is still required.

## Medical handoff deadline and status admission (2026-09-15)

Raid `20260915-004406-Interchange.jsonl` contains 1,681 events. Medved lost accepted combat at 707.604, selected FirstAid at 709.188 and used it from the 709.688 snapshot. From 719.211 through 733.319, repeated Surgery selections switched briefly out of linger and then back (mostly about 0.1 s later). The old handoff treated each medical selection as fresh combat, cleared the linger deadline and armed another three seconds when the selection disappeared. Core recovery did not start until 736.321. Native first aid then reentered the addon at 737.222 even though core recovery was already active. Core healing at 748.445 and 759.018 also appeared as addon Combat episodes despite the patrol layer owning the action.

The handoff now arms a single three-second deadline from accepted enemy loss. Medical selection can use the remaining window, but cancellation/reselection cannot extend it. Already-running native medicine may finish beyond that deadline; completion then releases immediately into core recovery. Released native medical decisions cannot reopen addon combat, including with a null native goal. The core publication bridge clears self-action along with a fully rejected solo/squad result, while native all-None resets remain untouched. Core-owned healing stays Released in addon recording. Renewed accepted enemy combat and urgent grenade avoidance retain their normal priority.

The recording also shows native SAIN contacts while `enemyCombatAllowed=false` in patrol (for example 766.856 onward). The ready SainMan marker provider now requires an accepted living EFT GoalEnemy **before** reporting any SAIN contact. A missing/dead EFT goal yields a handled empty report, including On Your Own, without erasing native perception or falling back to an EFT position. With a valid accepted goal, the marker still uses native selected enemy/last-known coordinates; exact identity matching is not required. Other followers' valid reports and non-SainMan marker behavior retain their existing handling.

## Follower objectives and prudent push (2026-09-14)

`SAINFollowerObjectives` coordinates the existing regroup objective and the new `SAINFollowerPushObjective` at the native decision-publication boundary. Objectives retain intent across actions; native SAIN still calculates and publishes each decision once. The addon owns the interception, including a validated `SAINEnemyController.SelectEnemy` result hook that can prefer the mission target before native target publication. These addon-only hooks leave shared presets unchanged.

Accepted combat Go Forward now captures the accepted enemy into an addon-owned ordered push, in addition to temporary 100% aggression. It replaces the previous order/regroup without leaving core PushEnemy pending. A missing native target can bind for up to three seconds without activating combat; death, forgetting, loss of usable knowledge, replacement commands, Gogogo, core support cancellation, release and opt-out end the objective. Native visible threats, dogfight and recent attackers can temporarily take target priority; the remembered mission resumes afterward. It does not refresh enemy memory or chase hidden live coordinates.

Ordered and automatic approaches share the same execution policy:

- Prefer native-validated forward firing cover. Core and addon share `FollowerPushGeometry`: at least 2 m closer to the known target, forward dot at least 0.2, and a complete route no longer than 30 m. Floor and destination claims are checked. Discovery reuses the bounded native cover finder with at most 32 probes per changed geometry sector; a potential firing-lane ray never grants sight or shooting permission.
- Without suitable cover, walk a complete provisional route toward the last-known location, at most 20 m per step and 30 m along the route. Recheck forward cover every two seconds. Player movement and hidden enemy movement do not redirect a committed destination.
- `SAINFollowerMoveToEngageAction` executes the approach through SAIN movement, shooting and steering. On arrival, a three-second `SAINFollowerPushHoldAction` retains native cover posture and shooting/steering but removes native StandAndShoot's random entry swing. Repeated polls do not restart this hold.
- Under fire, very recent damage, heavy suppression, dying health or low ammunition yields native SeekCover with a three-second recovery window. Useful native fire, medical/self-actions and urgent combat retain priority without erasing the mission. Automatic pushes also yield to native squad support. Interrupted movement does not spend its execution budget.
- A leg fails after 20 active seconds, six seconds without progress, invalidated cover or rejected movement. Reaching the remembered area without finding a shot also ends further outward attempts. The failure survives action restarts, repeated same-target orders and regroup; an 8 m change in known position or a visible shootable contact can reopen it. Existing unreachable/sniper firing-position failure remains authoritative. Failed automatic pushes may regroup through the existing safety/distance gates; ordered failures seek cover until the order ends or contact changes.

**Automatic admission is now filtered through Rifleman risk assessment:** native solo Search/RushEnemy and squad PushSuppressedEnemy propose an approach; shared scoring and health/weapon conditions must allow it. On Your Own retains native autonomous approach and disables automatic regroup. Explicit ordered push remains an order in independent mode, with health/readiness and cautious-execution conditions. Other native squad actions remain native. Native forgetting continues on its own timer; arrival does not delete an enemy or fabricate search completion.

Schema-13 recordings include `sainObjective` transitions and a passive `objectives` snapshot with target, mode, phase, destination, timing and failure reason. Navigation geometry, pressure recovery and the resulting in-raid pacing still require raid qualification.

## Rifleman risk assessment implemented (2026-09-14)

SainMan is the SAIN equivalent of **Rifleman**. Its push objective now applies Rifleman-style tactical conditions as well as movement. `FollowerPushRiskPolicy` is shared by core `FollowerCombatRiflemanEngagement`/`FollowerCombatPush` and addon `SAINFollowerPushAssessment`. Extraction preserves core's scoring and magazine thresholds.

- Automatic Search/RushEnemy/PushSuppressedEnemy is a native proposal, then must pass shared scoring: enemy-route distance / 80 m * 100, relative equipment power, enemy role threat, local enemy numbers, current ammunition threat and projected player separation, compared with effective aggression. Required aggression remains clamped to 0–150. Failed assessment selects cover; ordinary stationary-cover regroup remains subject to the existing fallback gates.
- Enemy clustering uses distinct living, known SAIN contacts within 17 m of the target's last-known location. It never imports core world-overlap scans or hidden squad-member positions. Personal sight/hearing is required for automatic pursuit; shared-only knowledge can inform the local risk count but cannot independently authorize that pursuit.
- Core `SainPushRiskBridge` reads relative equipment power, the existing role/loaded-ammunition threat classification, reported healing work and critical-wound conditions. It does not execute core decisions, refresh medicine, switch weapons, or acquire enemies. Read-only helpers retain follower-local caches; no shared SAIN objects change.
- Current weapon readiness must be real: a loaded long gun, ready weapon manager and no in-progress switch. Existing Rifleman magazine conditions include the ten-round normal threshold, precision-magazine constraints and six-round close-shotgun exception. SAIN owns weapon switching, so an unused automatic secondary does not make the active weapon suitable. The old native half-magazine check no longer overrides core loaded-round readiness.
- Ordered push retains the accepted target and is not rejected by the automatic aggression score. Local clusters, worse equipment, dangerous enemy roles and ammunition threat favor cautious protected approach cover, including useful cover without an immediate firing lane. If none exists, ordered provisional walking remains available. A cautious automatic push with no protected approach pauses in cover and reassesses.
- Pending treatment, critical wounds, badly injured/dying health, incoming pressure and unavailable weapons pause forward movement. Native urgent combat, shooting and medicine keep their priority. Recovery waits for native cover travel/arrival use instead of cancelling it as soon as health/readiness improves.
- Ordinary risk changes are applied between committed movement legs. A three-second reassessment hold and existing cover commitments prevent score fluctuations from repeatedly restarting movement; health/weapon safety can interrupt immediately. Risk holds retain the target and do not erase failed-engagement safeguards.

The passive `risk` snapshot records local enemy count, equipment ratio, enemy-role multiplier, ammunition policy, health/weapon gates, caution, aggression/requirement, route distances and player pull. `sainObjective` risk/hold transitions identify the selection reason. Navigation checks are cached for at most half a second and refreshed for changed geometry; contact/medical/weapon inputs are reread at decisions.

The source reference is `FollowerCombatRiflemanEngagement` for scoring, `FollowerCombatPush` for execution conditions, and `FollowerCombatCommon` for shared ammunition/medical readers. This adapts those conditions to SAIN knowledge and actions; it does not reproduce every core weapon-switch routine, world scan or corner-search action. Raid behavior remains to be qualified.

## Planned Enemy Tracking modes

[Enemy-Tracking.md](Enemy-Tracking.md) is the investigation and future design for Simple (core-style tracking) and Realistic (SAIN-style remembered contacts), selectable independently of combat tactic. It is not implemented. Core continues owning optional-mod bridges and general compatibility; any future SainMan tracking policy stays follower-local and addon-owned.

## SainMan status contact tracking (2026-09-14)

Status Report and automatic enemy markers now consume a passive `SainEnemyContact` through core `SainAddonBridge` for ready SainMan followers. The addon first requires a living accepted EFT goal, then reports its native selected, living, active, known enemy and SAIN's last-known location. The yellow `!` updates when that knowledge changes; hidden enemy movement is never sampled to refresh it. Fresh native visible/shootable contact retains the live red reticle and the existing sight-age limit. Native target release, forgetting or removal of its known place removes that follower's report even if EFT still holds a goal. Another follower reporting the same target can keep the shared marker alive. A handled empty/failed native report does not fall back to the EFT goal.

The UI does not select enemies, evaluate decisions, change search completion, clear memory or alter the accepted-goal combat gate. SAIN's search can extend contact retention beyond its memory time until all known places are searched; reaching a place alone is not forced to mean forgetting. Other tactics and absent/unready addon state keep core marker behavior. Existing death-marker retention/settings and report-position sound/direction behavior remain unchanged. Core owns rendering and marker aggregation; the addon supplies data only.

## Accepted-goal combat entry and recovery handoff (2026-09-14)

Ready SainMan followers no longer enter or continue ordinary solo/squad combat solely for native SAIN contacts while core has no accepted living EFT goal. The gate filters the already-calculated publication and both layers' shared handoff; it does not erase native perception or living memory. On Your Own permits investigation. At entry the addon derives combat independence from the same saved patrol/requested intent as core; later combat commands can revoke active independence without erasing patrol intent. Completed/explicit release clears active independence. Medical selections retain an exception during the original enemy-loss handoff window; already-running native medicine may finish, while post-release recovery stays core-owned. Native grenade avoidance and urgent native layers remain available. Peaceful regroup commands are left for core instead of being consumed by the squad provider.

Addon linger completion and explicit combat release now call the existing core `BeginPostCombatFullHeal` bridge. Renewed enemy combat cancels that recovery before it resumes fighting. Repeated layer polls do not restart recovery. This repairs the missing post-combat handoff; it does not replace SAIN's combat first-aid/surgery policy.

Schema-13 addon snapshots add `enemyCombatAllowed` and passive `medical` inputs: native health status, time since hit, selected first-aid item/body part, cached bleeding state, known-enemy count and up to 32 known enemies' sight/hearing/path-distance timers when treatment context is relevant. Diagnostics do not invoke `ShallStartUse`, select medication, or evaluate native decision providers. Non-goal enemies matter because native first aid checks every known enemy. Exact combat-heal rejection reasons and medicine-effect resources were absent from the Interchange recording, so the remaining combat-healing delay is not yet attributed conclusively.

## Boss-oriented cover and arrival use (2026-09-14)

`SAINFollowerCover` and `SAINFollowerCoverFinder` extend native SeekCover selection for ready SainMan followers. Core combat is the reference: `FollowerCombatCommon.ScoreBossCover` and `GetCommittedCoverHoldDuration` are shared without changing core behavior. Ordinary selection prefers safe cover around the real player, then a safe intermediate cover toward a distant player. On Your Own retains native selection. Incoming fire, very recent hits, retreat and medical recovery retain native immediate-cover selection.

The addon runs one player-area collider query with at most 32 native cover-creation probes per meaningful geometry change (player/bot sector, enemy identity or last-known anchor). It uses SAIN's own cover/path validator, the core search radius and score, floor checks and destination reservations; it can find cover outside SAIN's bot-centred five-point pool. No valid preferred cover, or rejected movement, falls back to native selection. Committed paths are not redirected merely because the player moves. Selected cover is revalidated; invalidation releases the owned cover and matching path for native reselection while preserving a different movement destination.

Arrival arms the core three-second boss-cover hold, or 3.5-second recovery hold. It blocks automatic regroup and ordinary Search/MoveToEngage/ShiftCover reselection while the reached cover remains usable. It does not force movement when time expires. Visible shootable contact, native urgent combat, self-actions, squad support and explicit orders retain priority; accepted Go Forward ends the arrival hold. Repeated selection of the same reached position cannot rearm the timer. Combat release, opt-out and native-component replacement release follower-local state and claims without removing a newer owner's destination claim.

`SainCoverSelectionBridge` is addon-owned interception at native `SAINCoverClass.FindCoverPoint`; its prefix/postfix signatures and sprint field are validated. Native cover movement/state/action execution remain in SAIN. Other tactics and ordinary SAIN bots are untouched. Schema-13 recordings add `sainCover` selection/arrival/invalidation events and a `coverPolicy` snapshot. Geometry, scan cost and full raid behavior still require in-game qualification.

## Current progress and next extension (2026-09-14)

Both SainMan combat replicas, player leadership, linger, proficiency baseline repair, two-mode regroup, bounded engagement and schema-13 recording are implemented. [ADDON-ANALYSIS.md](../ADDON-ANALYSIS.md) is the current progress/validation/deployment ledger; [SAIN-Addon-Phase1.md](SAIN-Addon-Phase1.md) preserves the narrower historical checkpoint.

The addon now interpolates follower-local personality settings from the loaded SAIN preset at 100% GigaChad, 70% Chad, 50% Normal, 30% Rat and 0% Coward. Combat numeric fields blend linearly; combat switches and native identity use the nearest anchor, with midpoint ties selecting the higher one. The scope is General/Search/Rush/Cover and tactical AggressionCoef; speech, assignment and mechanical difficulty stay neutral, with begging, fake death and taunting disabled. Combat HoldPosition applies temporary 0%, GoForward applies temporary 100%, and Gogogo restores saved aggression. [SAIN-Personalities-and-Aggression.md](SAIN-Personalities-and-Aggression.md) records the implementation choices and source findings. The addon owns native installation/restoration, memory-preserving timer refresh and cached talk/search-sprint refresh. Mechanical proficiency stays normalized; no shared preset mutation or squad-category rerolls are introduced.

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

Core `SainRegroupBridge` exposes shared distance, navigation and destination-reservation helpers. Addon `SainSquadDecisionBridge` passes the already-calculated result from native `BotDecisionManager.SetDecisions` to the ready SainMan addon before publication; native state, timing and events still publish once. It never evaluates the solo provider a second time. Both bridge hooks are validated together and disabled on installation failure. All new combat policy remains addon-owned; the addon owns its required hooks and shared SAIN settings are unchanged. Other combat command translations remain deferred. Validation: 383 production addon combat/personality/cover fixture checks, 13 native-source parity checks, and the existing 32 proficiency checks passed. The matching Debug core/addon build has zero warnings or errors. In-raid movement and behavior still require verification.


The core references for this ordering are `FollowerCombatDefault.ShouldDeferBossDistanceRegroupForCommitment`, `EndCommittedHolder`, the Rifleman engagement arbitration, and `FollowerCombatCommon.CreateBlockedEnemySearchDecision`. These protect commitments and concrete combat successors before choosing regroup as a fallback. In SAIN, the corresponding proof is its fresh native result: `MoveToEngage` includes the firing-position attempt for an unreachable enemy; the addon now bounds that attempt and retains its failure across regroup. Shooting and support remain higher priority than passive `SeekCover`; ordinary `Search` also does once the reached-cover arrival hold is finished or interrupted. Cover state and movement are checked as well because `SeekCover` can still represent useful travel. A follower that completes regroup and resumes productive pursuit/search therefore remains in combat when it crosses the radius again. Regression coverage includes repeated crossings after the retry delay expires, every native combat/squad result, cover travel/arrival, reset publication, and one evaluation/event handoff.


**Status:** authoritative architecture contract

This document defines the boundary between the external SAIN plugin, the pitFireTeam core plugin, and the optional pitFireTeam SAIN addon.

## Terminology

- **SAIN plugin / SAIN mod**: the external `me.sol.sain` plugin.
- **SAIN addon**: pitFireTeam's optional `xyz.pit.fireteam.sainaddon` DLL under `addon/`.
- **Core combat**: pitFireTeam's follower combat brain implemented through the core/vanilla BigBrain path under `client/BigBrain`.
- **SAIN-addon combat**: pitFireTeam's alternative follower combat brain implemented through custom SAIN-derived layers under `addon/`. It is not the stock SAIN Squad layer and it is not a general SAIN patch collection.

## Proficiency baseline repair (2026-09-13)

The core SAIN adapter now projects finalized follower vision and scatter baselines into follower-local EFT Core settings. SAIN 4.5.1's config application leaves those fields untouched, so normalization of `Info.FileSettings` alone was insufficient. Existing Vision/Precision/Reaction factors and aim/recognition timing remain unchanged. See [SAIN-Proficiency-Audit.md](SAIN-Proficiency-Audit.md) for runtime evidence, measured percentage effects, scope and validation.

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

Addon SainPlayerSquadBridge owns real SAIN membership, human leader identity/liveness/distance, and election/cleanup. LeaderComponent remains null because its BotComponent type cannot represent a human. Addon SainSquadDecisionBridge dispatches the ready follower's native provider call to the addon calculator. SAIN's existing decision manager still publishes results and events. Handled None goes to native solo selection; it does not call the native AI-leader squad provider again.

UseSainFollowerCombat(botOwner) requires SainMan, both plugins, both constructed addon layers, native state, player binding, and readiness callbacks. Native solo/squad/extract/debug layers are suppressed only for followers; ready SainMan retains native urgent-threat and flash reactions above both replicas. General external-SAIN compatibility stays core-owned, while addon-only hooks live in the addon.

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

Behavior may differ from core combat because the custom layer can choose different SAIN decisions and actions. The addon owns these policies and any native hooks required exclusively to implement them.

## Forbidden addon responsibilities

The addon is not a compatibility-patch project and must not become one.

It must not:

- create general compatibility patches for the external SAIN plugin;
- move general proficiency, accuracy, recoil, vision, hearing, target acquisition, speech, door or friendly-fire compatibility into the addon when it is also required without addon combat;
- overwrite or mutate shared SAIN presets, global/static settings, singleton-owned settings, shared configuration objects, or other objects consumed by ordinary SAIN bots;
- treat a follower-only predicate as proof of addon ownership: the deciding question is whether the behavior is needed without the addon;
- own follower Vision, Precision, Reaction, proficiency normalization, or compensation for external SAIN calculations;
- own general follower/enemy relationship repair, contact propagation, enemy-state synchronization, target acquisition, perception compatibility, or friendly-fire compatibility;
- require its callbacks for behavior that must work when SAIN is installed but the addon is absent.

The addon owns decision-publication, cover-selection and human-led squad hooks needed only by its follower brain. Core exposes optional data/callback contracts and shared main-mod helpers. Native SAIN still publishes state/events once. Mixed hooks must be split so core compatibility also works without the addon.

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

Follower aim-point enhancement wraps `SAINShootData.GetAimTarget`: SAIN 4.5.0 takes `(Enemy, BotComponent)`, while 4.5.1 takes `(Enemy)`. A validated, compiled accessor captures the follower's `EnemyInfo`, then the complete native SAIN selector runs first. For followers, the postfix first substitutes an eligible body point using `FollowerAimTargetPolicy.TryGetBodyFirstShootPoint`, then applies the existing verified-head promotion once per retarget window. If the body is blocked, the native choice remains the baseline, including a native exposed-head choice. Calls that pass through EFT's `GetVisiblePartToShoot` are marked and enhanced only once at the outer SAIN boundary. The separate EFT patch is also a postfix, so vanilla/core calls follow the same native-first rule without patching SAIN's `BodyPartToShootPatch` itself. SAIN 4.5.1's optional weighted `AimTarget.ChosenPart` is read through a compiled accessor, while 4.5.0 uses `EnemyInfo.LastPartToShoot` and restores a native head after that version's later center-mass height clamp. Ordinary bots remain unchanged; follower native fallbacks remain when no body point or head enhancement is eligible. Shared aim weights and general SAIN presets are never modified.

Follower foliage compatibility removes foliage/grass from SAIN 4.5's line-of-sight, vision, and shooting ray masks at every distance, replacing the previous 10-metre exception. Hard geometry remains in those follower rays; non-followers retain SAIN's original masks. This bypass changes only SAIN's binary foliage veto: EFT look checks, `FollowerEnemyInfoCorrection` (including its own 10-metre foliage exception), suppression safety, and decision/commitment timing are unchanged. It does not disable foliage handling throughout the follower pipeline.

The mask bypass runs inside SAIN's native command builder through a validated startup transpiler. Follower status and the three query parameters are selected once per enemy; native body-part sampling, command order, geometry, and job cadence are preserved. The former reflection-based replacement builder is removed. An unsupported instruction/member layout logs an error and leaves the native builder unchanged. Compare the parent `VisionRaycastJob.EnemyVisionJob` timing when profiling this optimization, since removing the old separately measured prefix changes attribution as well as runtime overhead.

Player-visual contact promotion is one example of this core boundary. A target genuinely seen by the player is reported as visual contact rather than sense-only contact. If the follower has not independently seen the target, current `IsVisible` and `CanShoot` remain false while core seeds the complete personal contact record at the promotion timestamp. The addon is not involved in that compatibility path.

## Removed legacy addon patches

The historical addon patches and combat implementation were removed during the phase-one cleanup. Do not restore them from history as an initialization shortcut.

Any behavior reconsidered from those patches must follow one of these outcomes:

1. **Move to core** if it is general external-SAIN compatibility that must work with or without the addon.
2. **Keep in the addon** if it exists only for the alternate follower combat brain, including its necessary native hooks. Prefer direct public SAIN APIs; use reflection only for inaccessible members.
3. **Remove it** if neither boundary applies.

A mixed patch must be separated along the same boundary. The addon keeps the addon-only behavior and its hooks; general compatibility stays in core.

## Bridge contract

Combat callbacks are registered by `SAINFollowerRuntime.Enable` and released on shutdown. They are required by the per-follower combat readiness gate, not left dormant:

- `SainAddonBridge` carries readiness-for-combat, readiness-for-patrol, force-release and decision-reset callbacks, plus existing lifecycle/boss-update events.
- `SainSquadDecisionBridge` dispatches the native squad provider and passes the already-calculated result through the follower fallback policy before native publication. Its typed callbacks and interception are addon-owned.
- `SainRegroupBridge` exposes shared navigation, distance, arrival and reservation helpers. The addon consumes pending regroup orders from follower state; there is no general callback translating every combat command.
- `SainCombatRecorderBridge` registers optional Debug state-capture/active callbacks and forwards diagnostics to core `BattleRecorder`. Recorder reads cannot drive gameplay.
- `SainPlayerSquadBridge` owns real player-squad membership and leader state through addon subscriptions to core lifecycle events. It is not a fake BotComponent leader.

General external-SAIN synchronization must call a core-owned service directly and must not be routed through `SainAddonBridge`.

## Review rule

For every proposed addon change, answer these questions in order:

1. Does it implement a decision, action, movement, command translation, or lifecycle operation inside the custom SAIN solo/squad follower combat brain? If not, it does not belong in the addon.
2. Is a native hook needed only for that addon behavior? If so, the hook belongs in the addon and uses direct SAIN types where public. Shared presets remain immutable.
3. Is it compatibility required when external SAIN is installed but addon combat is absent? If yes, it belongs in core and must use `IsSAINInstalled` rather than `UseSainFollowerCombat`.
4. Does it change shared presets, global settings, ordinary SAIN bots, or the persisted proficiency contract? If yes, the design is invalid.
5. Is `UseSainFollowerCombat` being used for anything other than selecting, operating, or releasing the custom addon combat brain? If yes, the gate is wrong.

## SAIN 4.5.0 / 4.5.1 compatibility checks

On Windows, run `./tests/Verify-SainCompatibility.ps1 -GameRoot '<SPT game root>'`. The harness compiles the production targeting patch and sound-dispatch methods with controlled EFT/SAIN stand-ins, then exercises them with the project's real Harmony DLL under .NET Framework. It checks both target signatures, native visibility gates, no-target behavior, ordinary bots, recruitment/dismissal, teardown, and exactly one follower callback with or without an EFT hearing subscription. The existing body-part policy and sound filters are stand-ins, so the checks do not establish in-raid perception or shooting quality.

Raid validation should cover a fresh teammate and a recruited bot hearing hostile gunshots, neutral/friendly sounds remaining ignored, partially exposed enemies retaining native aim-point selection plus bounded proficiency enhancement, and startup logging `Native follower aim-target proficiency enhancement applied.` with no unsupported-layout error. Repeat with the supported SAIN version and keep the optional addon disabled unless its separate qualification is intended.
