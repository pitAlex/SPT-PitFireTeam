# AI Role: pitFireTeam AI Mod Engineer

## SAIN combat There / Come here (2026-09-17)

Ready SAINGrunt followers now consume the existing `CombatMoveToPointTactical` and `CombatComeToBossCover` commands through `SAINFollowerRelocationObjective`, before push/regroup and ordinary native squad work. The existing player gesture routing, selected follower, visibility/range checks and 30m There limit are unchanged. Peaceful commands stay Core-owned.

- **There:** complete-path tactical walking to the sampled command point, with destination reservations. The point stays fixed when the player moves.
- **Come here:** bounded native cover discovery around the player at consumption, restricted to the Core boss-cover radius and at least one metre of progress toward the player. No suitable cover uses Core's shared complete-path fallback, stopping 1.5m back along the final path segment and within 2m of the sampled player position. Once selected, the destination is committed.
- The addon uses a dedicated action with native SAIN walking/shooting/steering. Point movement yields to a visible shootable enemy or incoming fire, while cover approach can keep walking and firing. No new blind-suppression policy is added. Native medicine, retreat, grenade handling, melee and dogfight retain priority; pending commands keep their original eight-second expiry. Active relocation yields to survival work.
- Core geometry and thresholds are shared in `FollowerCombatCommandGeometry`: 1.25m arrival, 0.35m progress and a four-second stall bound. The extraction leaves Core's boss-approach algorithm unchanged. Arrival uses the existing three-second settle with fire/safety preemption. Cover planning is bounded to eight seconds and retains four native probes per finder per frame. Repeated polls cannot rearm arrival or switch a completed action into stale native MoveToEngage before publication.
- There/Come here replace previous push/regroup intent; new commands and Go Forward replace relocation. Explicit gestures also work during On Your Own. Invalid/no-path destinations give Core's Negative/NoGesture feedback. Enemy loss, opt-out and teardown release owned paths/claims without clearing another owner's destination.
- `sainObjective` events and the objective snapshot include relocation mode, destination, planning/movement/arrival/failure reasons and stall duration. No new Harmony hooks or typed SAIN reference in Core are introduced. Friendly-fire review remains deferred.

Validation: 697 production addon combat checks (48 new gesture checks) pass, including real production command setters/timeouts, publication/layer routing, cover/progress/fallback geometry, navigation failure, reservations, scan budget, arrival, interruption and cleanup. Matching Debug core/addon build passed with zero warnings/errors; 54 leadership/ownership checks and 13 replica comparisons pass. Both extracted Core path methods compare unchanged to their prior bodies. Deployed matching Debug core/addon DLLs and PDBs plus license on 2026-09-17 00:17:50 +03:00 with Tarkov closed. All five installed SHA-256 hashes match. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-combat-gestures-20260917-001749`. In-raid navigation and combat presentation still need qualification.


## Ordered push contact interruption (2026-09-16)

Shoreline `20260916-183446-Shoreline.jsonl`: Go Forward at 1939.037 starts Brick's ordered push. Native last-known knowledge disappears at 1949.327 and the addon clears it as `targetLost`; EFT goal briefly clears at 1949.35986 and returns at 1949.37659. The same native contact is back by 1949.47656, but Brick creates an Automatic push at 1949.57666, fails its risk score and regroups before SeekCover. Medved retains Ordered mode. This is an addon intent-lifetime bug.

Bound ordered pushes now retain intent for a fixed three-second contact interruption, including a brief missing accepted EFT goal. Advancement pauses, its owned path stops, and native combat admission/knowledge are unchanged. The same valid contact resumes Ordered mode with the committed destination and existing movement/failure budgets; changed native knowledge keeps the existing new-contact handling. Repeated polls and same-target orders cannot extend the deadline. Confirmed target death, replacement/cancel commands, explicit release and lifecycle cleanup remain immediate; sustained loss expires. Automatic pushes keep their existing immediate contact-loss behavior. Retained orders cannot fall through to automatic regroup. Recorder reasons `contactInterrupted` / `contactRestored` and `contactGraceRemaining` expose the interruption.

Validation: 649 production addon combat checks (22 additional checks) and 13 replica comparisons pass. Matching Debug core/addon build passed with zero warnings/errors. Contact-gap tests cover the recorded interruption, accepted-goal handoff, stopped movement, retained leg/stall/failure limits, timeout, repeated commands, late restoration, death, replacement orders, medical priority and automatic-push isolation. Deployed matching Debug core/addon DLLs and PDBs plus license on 2026-09-16 22:21:24 +03:00 with the game closed; all five installed SHA-256 hashes match. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-push-contact-retention-20260916-222124`. Raid qualification remains required.


## Performance and push recovery (2026-09-16)

The SAIN addon cover finder now spreads candidate creation and revalidation across frames (four native probes per finder per frame, at most 32 discovered candidates per geometry scan). Stable validation is reused for one second; an unfinished pass retains its completed probes so slower decision publication cannot starve it. Enemy/position changes and native bad/spotted flags invalidate reuse. Failed selection retries after 0.5 seconds or meaningful context changes. Selected-cover observation still revalidates independently at its existing cadence. An addon-owned `DogFightMove` prefix prevents native no-cover aggressive fallback while ordinary cover selection is pending; urgent/defensive combat remains native. Pending push planning uses the stationary push hold.

Inactive regroup no longer measures player routes. Push assessment shares the existing half-second player-distance measurement, and core distance probes reuse a thread-local NavMeshPath. Core's optional SAIN compatibility hooks cache compiled BotOwner access and bind aim/friendly-fire parameters directly, removing repeated reflection and Harmony argument-array boxing on those paths. Disabled recording exits before phase formatting/decision payloads; push risk diagnostics format only on changes, and unconditional regroup info logging is removed. Enabled battle recording still has its existing snapshot/serialization cost; no FPS improvement is claimed without a raid comparison.

Shoreline `20260916-165635-Shoreline.jsonl` recorded Go Forward for both followers at 772.860. Medved exhausted his approach at 773.677 despite a complete native route; Brick retained the command in medical recovery. `SAINFollowerApproachRoute` now supplies a maximum 20-metre walking leg along a newly verified complete route to the remembered location when direct stepping fails. A detour may initially increase direct distance. Endpoint/corner completeness, a maximum 30-metre actual leg route, destination reservations, existing risk gates and the six-second stall/twenty-second execution limits remain. Repeated orders do not rearm a failed contact. Recorder failure reasons distinguish sampling, incomplete routes, invalid corners/legs and reservations.

`SainMedicalDecisionBridge` extends native first-aid enemy checks and surgery safety only for ready addon combat. Reached usable SAIN cover must be stationary, free of pressure/recent hits, and protected from every relevant known contact using core `Covers.IsHardCoverFromThreat` chest/head rays. Visible/shootable/recently seen or very close threats reject the exception; heard-only contacts do not require a nonexistent sight timestamp. Item eligibility, bleeding-before-surgery, native decision publication and medical execution remain native. Physical protection checks are cached briefly; cover/position/knowledge and immediate danger still gate each call. The recorder exposes the last passive `medicalCover` reason/time in cover policy snapshots.

Installed SAIN 4.5.1 `BotSurgery.CheckAreaClearForSurgery` returns clearance without assigning `AreaClearForSurgery`, although native continuation and action execution read that property. The addon publishes the returned value (including false) through a cached private setter for its ready followers. No shared preset or ordinary SAIN bot policy is changed. All new hooks belong to the addon installer and participate in rollback/removal. The original native surgery flag is restored on combat release, opt-out, dismissal, shutdown and native-component replacement, without overwriting a different later value.

Validation: 627 production combat checks, 54 leadership/ownership checks, 49 compatibility checks, 38 proficiency checks and 13 replica comparisons pass. Tests exercise bounded probe work, slow-cadence completion, no-cover fallback suppression, shared route caching, bent-route walking, failed/reserved paths, real Harmony parameter bindings, core hard-cover geometry with controlled physics, native medicine prerequisites, clearance publication, retained push resumption and full hook cleanup. Unity navigation, actual healing and measured frame time still require raid qualification. Matching Debug core/addon DLLs and PDBs plus license were deployed at 18:06 on 2026-09-16; all five installed hashes match. See ADDON-ANALYSIS.md for hashes and the backup location.

## SAINGrunt tactic display name (2026-09-16)

The addon tactic is displayed as **SAINGrunt** in the profile selector and follower Status Report. The persisted `SainMan` identifier, enum value and `ProfileTacticSainMan` localization key remain stable, so existing squads and pickup selection retain the same behavior without migration. The embedded English fallback and English language resource supply the new name. Historical/code references to SainMan below refer to this same tactic.

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

## Rifleman risk conditions in SainMan (2026-09-14)

SainMan's core reference is Rifleman; role threat refers to the **enemy**, not another follower tactic. `FollowerPushRiskPolicy` shares core threat/player-pull/magazine formulas with `SAINFollowerPushAssessment`. Native automatic Search/rush proposals must pass effective aggression versus route, known local enemies, gear ratio, enemy role/ammunition threat and player separation. Ordered push retains its target but adapts toward protected cover and pauses for health, pending treatment or unavailable weapons. Core `SainPushRiskBridge` reads existing ammunition and passive medical conditions without executing core decisions or refreshing medicine. Only SAIN-known positions count enemies; no hidden world scan. SAIN owns switching, so only the actual active weapon counts as ready. Assessment happens between committed legs; safety interrupts immediately, while three-second reassessment and native recovery-cover commitments prevent churn. See [the implementation contract](docs/SAIN-Integration.md#rifleman-risk-assessment-implemented-2026-09-14) and [validation/deployment](ADDON-ANALYSIS.md).

## SAIN push objective handoff (2026-09-14)

Ready SainMan now uses `SAINFollowerObjectives` to coordinate push and regroup. Go Forward retains an addon-owned target plus temporary 100% aggression. Ordered and native-admitted automatic pushes use shared core forward-cover geometry, controlled provisional walking, a stationary three-second arrival action, pressure recovery and bounded failed approaches. Useful fire, urgent actions and medicine interrupt without erasing intent. Core owns the validated native target-selection preference hook; addon policy never changes shared SAIN settings or living memory. On Your Own keeps native automatic approach. Rifleman risk assessment is implemented as documented above. See [SAIN-Integration.md](docs/SAIN-Integration.md#follower-objectives-and-prudent-push-2026-09-14) for precise scope and [ADDON-ANALYSIS.md](ADDON-ANALYSIS.md) for validation/deployment.

## Enemy Tracking investigation (2026-09-14)

[docs/Enemy-Tracking.md](docs/Enemy-Tracking.md) records the source-backed comparison and future plan for `Enemy Tracking: Simple / Realistic` across core and SainMan. The setting is not implemented. It covers native evidence/search/forgetting, core live-position and retention paths, both adaptation directions, duration ownership, migration choices and qualification. Read it before implementing tracking; preserve admission, real sight/fire safety, command ownership and bounded engagement.

## SainMan status contact tracking (2026-09-14)

Status Report and automatic enemy markers now consume a passive `SainEnemyContact` through core `SainAddonBridge` for ready SainMan followers. The addon first requires a living accepted EFT goal, then reports its native selected, living, active, known enemy and SAIN's last-known location. The yellow `!` updates when that knowledge changes; hidden enemy movement is never sampled to refresh it. Fresh native visible/shootable contact retains the live red reticle and the existing sight-age limit. Native target release, forgetting or removal of its known place removes that follower's report even if EFT still holds a goal. Another follower reporting the same target can keep the shared marker alive. A handled empty/failed native report does not fall back to the EFT goal.

The UI does not select enemies, evaluate decisions, change search completion, clear memory or alter the accepted-goal combat gate. SAIN's search can extend contact retention beyond its memory time until all known places are searched; reaching a place alone is not forced to mean forgetting. Other tactics and absent/unready addon state keep core marker behavior. Existing death-marker retention/settings and report-position sound/direction behavior remain unchanged. Core owns rendering and marker aggregation; the addon supplies data only.

## Accepted-goal combat entry and recovery handoff (2026-09-14)

Ready SainMan followers no longer enter or continue ordinary solo/squad combat solely for native SAIN contacts while core has no accepted living EFT goal. The gate filters the already-calculated publication and both layers' shared handoff; it does not erase native perception or living memory. On Your Own permits investigation. At entry the addon derives combat independence from the same saved patrol/requested intent as core; later combat commands can revoke active independence without erasing patrol intent. Completed/explicit release clears active independence. Medical selections retain an exception during the original enemy-loss handoff window; already-running native medicine may finish, while post-release recovery stays core-owned. Native grenade avoidance and urgent native layers remain available. Peaceful regroup commands are left for core instead of being consumed by the squad provider.

Addon linger completion and explicit combat release now call the existing core `BeginPostCombatFullHeal` bridge. Renewed enemy combat cancels that recovery before it resumes fighting. Repeated layer polls do not restart recovery. This repairs the missing post-combat handoff; it does not replace SAIN's combat first-aid/surgery policy.

Schema-13 addon snapshots add `enemyCombatAllowed` and passive `medical` inputs: native health status, time since hit, selected first-aid item/body part, cached bleeding state, known-enemy count and up to 32 known enemies' sight/hearing/path-distance timers when treatment context is relevant. Diagnostics do not invoke `ShallStartUse`, select medication, or evaluate native decision providers. Non-goal enemies matter because native first aid checks every known enemy. Exact combat-heal rejection reasons and medicine-effect resources were absent from the Interchange recording, so the remaining combat-healing delay is not yet attributed conclusively.

## SAIN addon current handoff (2026-09-14)

Read [ADDON-ANALYSIS.md](ADDON-ANALYSIS.md) for current progress, validation and deployed binaries, [docs/SAIN-Integration.md](docs/SAIN-Integration.md) for ownership, and [docs/SAIN-Personalities-and-Aggression.md](docs/SAIN-Personalities-and-Aggression.md) before the next personality implementation session.

SainMan uses both solo/squad replicas, player leadership, linger, two-mode regroup, bounded engagement and schema-13 recording. `SAINFollowerPersonality` now interpolates follower-local settings from the loaded SAIN preset at **100% GigaChad, 70% Chad, 50% Normal, 30% Rat, 0% Coward**. Combat numeric fields blend linearly; combat switches and native identity use the nearer anchor, with midpoint ties choosing the higher one. Only General/Search/Rush/Cover and tactical AggressionCoef follow anchors; speech, assignment and mechanical difficulty remain neutral. Begging, fake death and taunting stay disabled. Combat HoldPosition applies temporary 0%, GoForward applies temporary 100%, and Gogogo restores saved aggression; weapon-specific core modifiers are excluded. Addon `SainManPersonality.Apply` installs/restores the copy and refreshes native caches. Proficiency, shared presets, living memory and existing combat commitments remain unchanged. See the personality document for implementation choices and raid-verification limits.

## Boss-oriented cover and arrival use (2026-09-14)

`SAINFollowerCover` and `SAINFollowerCoverFinder` extend native SeekCover selection for ready SainMan followers. Core combat is the reference: `FollowerCombatCommon.ScoreBossCover` and `GetCommittedCoverHoldDuration` are shared without changing core behavior. Ordinary selection prefers safe cover around the real player, then a safe intermediate cover toward a distant player. On Your Own retains native selection. Incoming fire, very recent hits, retreat and medical recovery retain native immediate-cover selection.

The addon runs one player-area collider query with at most 32 native cover-creation probes per meaningful geometry change (player/bot sector, enemy identity or last-known anchor). It uses SAIN's own cover/path validator, the core search radius and score, floor checks and destination reservations; it can find cover outside SAIN's bot-centred five-point pool. No valid preferred cover, or rejected movement, falls back to native selection. Committed paths are not redirected merely because the player moves. Selected cover is revalidated; invalidation releases the owned cover and matching path for native reselection while preserving a different movement destination.

Arrival arms the core three-second boss-cover hold, or 3.5-second recovery hold. It blocks automatic regroup and ordinary Search/MoveToEngage/ShiftCover reselection while the reached cover remains usable. It does not force movement when time expires. Visible shootable contact, native urgent combat, self-actions, squad support and explicit orders retain priority; accepted Go Forward ends the arrival hold. Repeated selection of the same reached position cannot rearm the timer. Combat release, opt-out and native-component replacement release follower-local state and claims without removing a newer owner's destination claim.

`SainCoverSelectionBridge` is addon-owned interception at native `SAINCoverClass.FindCoverPoint`; its prefix/postfix signatures and sprint field are validated. Native cover movement/state/action execution remain in SAIN. Other tactics and ordinary SAIN bots are untouched. Schema-13 recordings add `sainCover` selection/arrival/invalidation events and a `coverPolicy` snapshot. Geometry, scan cost and full raid behavior still require in-game qualification.

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
- Arrival requires a complete path, the conservative larger of path and direct distance, and 1.75m floor tolerance. Command arrival reuses core's 18m normal / 10m Factory-Labs / 4m tight distances. A short 1.5-second settle yields immediately to renewed combat or native squad support.

Core `SainRegroupBridge` exposes shared distance, navigation and destination-reservation helpers. Addon `SainSquadDecisionBridge` passes the already-calculated result from native `BotDecisionManager.SetDecisions` to the ready SainMan addon before publication; native state, timing and events still publish once. It never evaluates the solo provider a second time. Both bridge hooks are validated together and disabled on installation failure. All new combat policy remains addon-owned; the addon owns its required hooks and shared SAIN settings are unchanged. Other combat command translations remain deferred. Regression fixtures cover these boundaries; in-raid movement and behavior still require verification.


You are an AI engineering agent working on `pitFireTeam`, a C# mod for Single Player Tarkov built with BepInEx, Harmony, BigBrain, and optional SAIN integration.

Your job is to make safe, context-aware changes that preserve runtime stability, respect current architecture, and avoid assumptions about Tarkov/SPT/SAIN internals.

You must think like a maintainer of a fragile gameplay-AI integration project, not like a generic C# assistant.

## Terminology

- **SAIN Plugin** (or "SAIN mod", "SAIN") — the third-party SAIN mod by Sol (`me.sol.sain`). This is an external dependency. See `LOCAL.md` for the machine-local source path when source inspection is needed.
- **SAIN Addon** — our optional addon DLL (`addon/`, plugin ID `xyz.pit.fireteam.sainaddon`) whose sole purpose is to provide a pitFireTeam follower combat brain implemented through replicas of SAIN PMC solo and squad combat layers instead of the core/vanilla BigBrain combat layer. It makes the human player the squad leader/tactical anchor and may implement custom SAIN actions inside that brain. It owns hooks needed only by this brain; compatibility required without the addon stays in core. Shared SAIN presets must not be mutated. See `docs/SAIN-Integration.md`.

Never confuse these two. When the user says "SAIN plugin" or "SAIN mod" they mean the external SAIN mod, not our addon.

## First extension after the phase-one checkpoint: post-combat linger (2026-09-13)

`SAINFollowerLingerAction` now owns a three-second transition after the last active living enemy is gone. Both replicas share one follower-local handoff timer; squad combat yields to solo for linger. The action cancels the previous SAIN path and firing, keeps a horizontal look, and makes one lateral scan before normal patrol/command handoff. A live known enemy or ongoing medical use prevents premature linger; renewed combat and native grenade avoidance interrupt it. Dead remembered targets and stale decision events cannot restart combat or extend the timer. Pending commands and living enemy memory remain intact. If BigBrain retains an inactive layer while another combat signal blocks patrol, its fallback stays quiet in the dedicated action.

This is the first intentional behavior extension beyond the replicated phase-one checkpoint described below. Other custom combat command policies remain deferred; squad regroup is now implemented as documented above. Automated regression checks cover the handoff; movement and presentation still require in-raid verification.

## Historical SAIN addon phase-one checkpoint (2026-09-13)

The no-custom-policy statements below describe the original checkpoint; later extensions and the next requested work are recorded above.

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

## Working Rules

Read code first. Assume nothing.

Check `LOCAL.md` for machine-local deployment paths and current local runtime notes. `LOCAL.md` is intentionally ignored by git; do not treat it as shared project documentation.

Use the current-version release notes file listed in `LOCAL.md` to note user-facing changes for the version currently in progress. When a change is intended for the current beta/release line, add it under the matching version heading there before building or packaging.

Check `docs/Localization.md` before adding or changing user-facing text. UI/server text must use the centralized pitFireTeam language model and embedded English fallback instead of hardcoded per-callsite fallback strings.

If a method, class, property, or runtime behavior is unclear:

- inspect the project source code
- inspect SAIN or BigBrain source if involved
- inspect decompiled EFT/SPT references when necessary

Never invent APIs, properties, or behaviors that do not exist. Only reference methods, properties, and classes that are verified in the source code.

Separate vanilla and SAIN reasoning. Every behavior should be classified as one of:

- vanilla / core plugin path
- SAIN addon custom follower-combat-layer path (runtime-gated by SAIN + addon presence)

Do not mix these paths unless the code clearly bridges them.

When fixing bugs or implementing changes:

- make the smallest correct change
- avoid broad refactors unless explicitly requested
- preserve current architecture and naming style
- prefer stability over elegance

Do not leave left overs. When going with a different approach, clean up (or revert) the old approach

Always think centralization giving the fact that we can have different types of followers with different tactics that may share some behaviors or functionality.

## Decision Priority

When multiple approaches are possible, prefer:

1. runtime stability
2. preserving existing architecture
3. minimal code changes
4. improved clarity or debugging
5. improved elegance

---

# pitFireTeam: Current Implementation Summary

**Last updated:** 2026-08-30 (general summary); SAIN addon sections synchronized 2026-09-14.

**Scope:** Runtime behavior across `pitFireTeam/client`, `pitFireTeam/addon`, and `pitFireTeam/server`.  
**SAIN Addon is optional and runtime-gated**

## Project Overview

**pitFireTeam** is a three-tier modular architecture:

1. **CLIENT** (`client/`) — Game-side follower control and team UI.
    - Implements BigBrain layers for follower movement, commands, and decision-making.
    - Patches game systems (bot recruitment, group handling, loot/door interaction).
    - Manages UI for team management and teammate creation.

2. **SERVER** (`server/`) — Backend teammate management and social integration.
    - REST API for teammate CRUD operations.
    - Social list/profile patching to merge teammates with stock friends.
    - Group invite and raid-spawning routes.
    - Post-raid item/escape handling.

3. **SAIN ADDON** (`addon/`) — Optional custom follower combat brain.
    - Replaces core combat only for ready SainMan followers with `SAINFollowerSoloCombatLayer` (74) and `SAINFollowerSquadCombatLayer` (75). Other tactics and unready/missing addon state retain core combat.
    - Re-centers SAIN Squad decisions around the human player as leader and tactical anchor.
    - May use native SAIN actions and create custom SAIN actions, with follower-local decision/action/lifecycle state.
    - Owns addon-only SAIN hooks with direct native references where public. General external-SAIN compatibility belongs in core and must work when the addon is absent; shared presets are never mutated.

---

## Architecture Key Constraint

- Server backend is **limited to teammate profile creation/storage/social flows** and is **not** a general bot profile generator.
- For debug/runtime follower spawn: use existing game-side `ISession.LoadBots` profile loading (not BE-dependent).
- If a spawn flow needs BE profile data and local profile is unavailable, fail fast with a clear reason.

---

# CLIENT SIDE: Follower AI & Team Management

**Plugin ID:** `xyz.pit.fireteam`  
**Main entry:** `client/friendlyPlugin.cs`

## 0a) Teammate System Status (In Progress)

Current verified custom teammate feature state:

- Dedicated Team Management FE is the primary entry point:
    - main menu now has a localized `My Squad` entry that opens the real `MatchMakerSideSelectionScreen` in squad mode, using `squad-inverse.png` for its icon when Menu Overhaul is loaded
    - roster/settings panels from `SquadControlMenuUi` are injected into side-selection and controlled by EFT-style animated tabs (`Roster` / `Settings`)
    - roster tab supports add/remove teammate flows, delayed sequential portrait loading, teammate profile open/return, and scrolling layout for larger squads
    - settings tab exposes the main pitFireTeam config set in a stock-style scrollable UI using EFT toggle/slider controls for checkbox and ranged settings
    - settings entries are grouped/reordered for the current squad-management UX and the duplicated BepInEx ConfigurationManager view is hidden for those settings
    - squad-mode lifecycle is explicit-action based: mode is cleared on side-selection `Back`, bottom-bar `MainMenu` root return, and `Play` transition
- Teammate creation flow is implemented through the stock appearance screen:
    - name entry
    - player-side forced automatically
    - head and voice selection
    - localized validation/prompt text overrides
    - custom back/submit handling
    - submit posts `{ nickname, voice, head }` to `/singleplayer/pitfireteam/teammate/create`
    - server creates a PMC bot of the player side and stores it under `user/mods/pitFireTeam-ServerMod/Resources/teammates/<sessionId>.db` (encrypted JSON records; imported originals are retained in <sessionId>.backup folders, see `docs/Teammate-Storage.md`)
- Stock social/profile flows are bridged for teammates:
    - teammates are merged into `/client/friend/list`
    - teammate profile view is merged into `/client/profile/view`
    - teammate deletion is bridged through `/client/friend/delete`
    - add-success refresh updates the visible list without restarting the game
    - teammate ids now use stock `HashUtil.GenerateAccountId()` collision-checked allocation
    - 4.x invite popup is patched separately because stock `Commando` and `SPT` chat bots share the same `Aid`
- Team grouping flow is functional but not fully parity-complete:
    - teammate appears in right-click invite/group flows
    - teammate can accept group invite
    - pre-raid ready screen and loading screen can show player + teammate
    - persisted `Auto Join` can preload selected teammates into the next PMC ready flow
    - teammate portrait right-click exposes `Invite to group`, `View profile`, and `Auto join on/off`
    - removing a teammate from the ready/group flow suppresses that teammate for the current auto-join cycle until re-added or reset
    - ready-screen preview rehydrates teammate visual health and shows a secure-container contents summary
    - local/offline raid guard is enforced late in `TarkovApplication` and `MainMenuController.method_52()`
    - teammate path preserves the normal PMC insurance screen before the custom ready screen
- Server teammate routes include current client compatibility paths:
    - `/client/game/bot/followergenerate`
    - `/client/game/bot/followerdetails`
- Teammate profile view now has completed profile-side customization features:
    - hideout/report actions are hidden for teammate profiles
    - stock clothes dropdowns are reused for teammate suit selection
    - `EDIT LOADOUT` edits the teammate's real `Default` equipment; `KIT LOADOUTS` acquires saved equipment builds through the purchase flow
    - `Restricted` is the default; `Simple` and free preset selection are removed
    - legacy custom selections restore saved `Default` equipment on load before inventory is exposed
    - teammate rename is implemented through a custom overlay + backend rename route
    - stock `SkillsScreen` is cloned into teammate profile view with filtered follower-relevant skills
    - the draggable `Proficiency` dialog persists separate aggression plus follower-local `0..200` Vision, Precision, and Reaction percentages; Vision owns distance, Precision owns accuracy plus half of aim speed, Reaction owns recognition speed plus half of aim speed, neutral `100` is applied after the selected tactic's baseline when the teammate spawns, and the bottom Reset button restores all three percentages plus tactic-default aggression
    - teammate-only profile UI resets correctly when switching back to a normal player profile
- Teammate custom loadout editor is implemented for the current cloned/local editing model:
    - teammate profile has an `Edit Loadout` entry point
    - modal shell and fake local inventory session exist
    - left side renders a cloned fake stash
    - right side renders a cloned follower equipment/inventory view
    - secure container and dogtag are hidden from the follower-side container display
    - drag header / overlay movement is implemented
    - edits stay local until `Done`
    - saving commits real stash ownership transfers and teammate `Default` equipment without a preset naming dialog
- Current backend/social/profile/runtime limitations:
    - voice/head customization from profile screen is not implemented yet
    - custom loadout editor stages local inventory copies with real ids; saving commits real stash transfers in all three modes
    - Team screen `Settings` tab covers checkbox/ranged settings and keybind capture; staged save/cancel/default parity is still pending if needed later
    - teammate invite/group flow still needs more parity with old plugin around pre-raid screen sequencing and group state handling
    - old chatbot-style teammate management is not ported; current management path is the roster/context-menu flow
    - teammate profiles remain mod-owned bot JSON, not full stock `SptProfile` accounts

## 0) Project Context

- Old plugin codebase: see `LOCAL.md` reference paths.
- Old client reference (3.11): see `LOCAL.md` reference paths.
- New client reference (4.x): see `LOCAL.md` reference paths.
- SAIN plugin reference: see `LOCAL.md` reference paths.
- Positioning:
    - `pitFireTeam` is both:
        - a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment,
        - and an alternative plugin implementation with new BigBrain-native follower layers/actions.

## 1) Core Runtime Model

- Plugin: `xyz.pit.fireteam` (`client/friendlyPlugin.cs`)
- Dependency: BigBrain (`xyz.drakia.bigbrain`)
- Optional integration: SAIN (`me.sol.sain`) detected at runtime.
- Optional SAIN addon integration: `xyz.pit.fireteam.sainaddon` (separate DLL in `addon/`)
- Core runtime flags in `client/friendlyPlugin.cs`:
    - `UseSainFollowerCombat(botOwner)` = SainMan selected + both plugins + both constructed addon layers + native state/player binding + registered callbacks/readiness
    - `ShouldDisableSainForFollower(botOwner)` = follower is core-owned; native solo/squad/extract/debug remain suppressed for followers, while ready SainMan retains urgent/flash reactions
- Follower control model:
    - Follower combat is owned either by pitFireTeam core/vanilla BigBrain combat or, when `UseSainFollowerCombat` is active, by pitFireTeam's ready SainMan solo/squad replicas.
    - Friendly follow logic is implemented as a BigBrain custom layer/action (`FollowerPatrolLayer` + `FollowAction`).
    - When the external SAIN plugin is installed, follower proficiency is normalized to SAIN 4.5's server-generated built-in `Default` preset through `FollowerSainProficiency`:
        - ordinary bots remain controlled by the selected SAIN preset,
        - follower aim, vision, hearing, recoil/fire-rate, weapon proficiency, strafe, and lean values use Default,
        - selected-preset search/cover/patrol/extraction/talk and other policy settings are preserved,
        - `FollowerProficiency.DefaultValues` is the one global starting object and every `BotFollowerPlayer.Proficiency` owns an independent generic clone with separate `Vanilla` and `Sain` sections,
        - pitFireTeam's vanilla template difficulty, runtime coefficients, aim, vision, hearing, shooting, and boss/BirdEye proficiency overrides are centralized in the `Vanilla` section; tactical/capability policy remains outside it,
        - saved teammates also own a cloned `Modifiers` section retaining four granular compatibility fields behind three class-relative controls; Vision owns distance, Precision owns accuracy, Reaction owns recognition speed, stored aim speed is derived equally from Precision and Reaction, the immutable follower modifier applies vision/core accuracy, final aim time uses a follower-only postfix, and core `FollowerSainProficiency` applies the Accuracy factor to SAIN's final calculated recoil independently of addon presence,
        - `FollowerSainProficiency` is only the external-SAIN adapter; its runtime patches consume the follower's `Sain` section, clone follower-local SAIN categories, and never mutate SAIN's shared preset objects,
        - all general external-SAIN proficiency compatibility is core-owned and must work with or without the optional SAIN addon; the addon may consume the finalized follower state only through decisions/actions inside its custom combat brain.
    - Regroup request execution is split by runtime context:
        - vanilla regroup path for no-SAIN or out-of-combat,
        - the SainMan combat path is handled by `SAINFollowerRegroupObjective` and `SAINFollowerSquadRegroupAction`, published through native SAIN decisions.
    - If SAIN is installed but the addon is missing:
        - core keeps follower combat on the vanilla/core BigBrain path,
        - core suppresses SAIN follower layer takeover so SAIN does not pause or own followers,
        - non-followers continue using SAIN normally.

## 0b) Follower Core Components

**State & Management:** (`client/Components/`)

- `BotFollowerPlayer.cs` — Per-follower state container (active command, healing status, lifecycle)
- `AIBossPlayer.cs` — Boss command handler (TeamStatus, gestures, loot/door requests, attention)
- `BossFollowerPlayer.cs` — Boss-side follower roster management
- `SquadControlMenuUi.cs` — Team Management UI (My Squad roster/settings screens)

**Follower Lifecycle:**

- Recruit flow through `BotReceiverFollowMeRecruitPatch`
- On conversion: brain/layer reset, conflict cleanup, group assignment, enemy/friendly list adjustment
- Dismiss: trigger `OnFollowerDismiss` event for addon cleanup
- English voice assignment applied at profile-load time via `SessionLoadBotsEnglishVoicePatch`
- Followers now persist raid-earned experience and common-skill progression through the backend follower-progress route
- Teammate raid outcomes now persist profile counters for sessions, survived exits, and deaths so derived stats such as survival rate and K/D remain grounded in saved teammate data
- Follower kills now contribute to player kill-quest progress and legacy-style raid XP counters when eligibility checks pass
- Transit-ready teammates are carried forward through the synthetic raid group path between raids/maps

## 0c) BigBrain Follower Decision System

**Layer Stack (Priority Order):**

1. **FollowerRequestLayer** (priority 73) — Active command execution (hold/there/come/loot/door)
2. **FollowerCombatLayer** (priority 72, vanilla/core combat only; inactive when SAIN addon combat is active)
3. **FollowerPatrolLayer** (priority 71) — Idle follow patrol toward boss

**Core Files:**

- `client/BigBrain/FollowerCombatLayer.cs` — Core follower PMC combat logic and decision routing when SAIN follower combat is unavailable
- `client/BigBrain/FollowerPatrolLayer.cs` — Follow logic, state recovery, action selection
- `client/BigBrain/FollowerRequestLayer.cs` — Command request detection and activation
- `client/BigBrain/Actions/FollowAction.cs` — Chase and cover-settle movement
- `client/BigBrain/Actions/GestureCommandAction.cs` — Command execution pipeline (12 action types)
- `client/BigBrain/Actions/HealAction.cs` — Medical work with timeout safety
- `client/BigBrain/Actions/FollowerCombatActionBase.cs` — Shared base/data wrapper for split combat actions
- Combat actions are split into individual files under `client/BigBrain/Actions/` (no monolithic `FollowerCombatDecisionActions.cs` anymore)
- Additional actions: `PeacefulAction.cs`, `PeaceHardAimAction.cs`, `GoToCoverPointAction.cs`, `EatDrinkAction.cs`

**Core Follower Combat Behavior:**

- `FollowerPmcCombatLayer` only runs on the core/vanilla path (`!UseSainFollowerCombat`)
- Layer activation now uses a valid-living-enemy gate instead of raw `Memory.HaveEnemy`:
    - stale dead `GoalEnemy` entries should not keep followers in combat,
    - `FollowerCombatLayer.IsCurrentActionEnding()` now force-ends combat actions when no valid enemy remains,
    - healing/stimulator actions are the explicit exception and are allowed to finish without a live enemy.
- Current supported BigBrain combat actions are file-split and mapped from `BotLogicDecision`:
    - `CombatHoldPositionAction`
    - `CombatPostCombatLingerAction`
    - `CombatRunToCoverAction`
    - `CombatAttackMovingAction`
    - `CombatAttackMovingWithSuppressAction`
    - `CombatAttackRetreatAction`
    - `CombatDogFightAction`
    - `CombatShootFromPlaceAction`
    - `CombatShootFromCoverAction`
    - `CombatGoToEnemyAction`
    - `CombatRunToEnemyAction`
    - `CombatGoToPointAction`
    - `CombatRegroupRunAction`
    - `CombatGoToPointTacticalAction`
    - `HealAction`
    - `HealStimulatorsAction`
    - `CombatSearchAction`
    - `CombatSuppressGrenadeAction`
    - `CombatSuppressFireAction`
    - `CombatShootToSmokeAction`
- `CombatGoToEnemyAction` is now the custom old-plugin-style advance action:
    - cover-aware advance,
    - periodic path refresh,
    - short committed approach point to reduce wall-side dithering,
    - progress-stall detection to force repath when the bot is not actually advancing,
    - blind advance now prefers looking toward the current move destination,
    - a short wall probe flips look control to movement direction when destination-facing would pin the bot into nearby geometry.
- `CombatRunToEnemyAction` is now also custom-owned on the core path:
    - no longer delegates directly to vanilla `GClass227`,
    - keeps a committed run target,
    - refreshes pathing when stair/vertical pushes stop making progress,
    - distinguishes a requested sprint from actual player sprint/path engagement; repeated engagement failure enters the existing stable walking fallback instead of re-requesting sprint every frame.
- `runToEnemy` remains the decisive sprint path and is still allowed to end when the bot reaches a valid firing state.
- `goToPointTactical` end routing is now split by reason:
    - `enemySearch` uses old-plugin-style `EndEnemySearch` logic,
    - other tactical-point usage uses old `EndGoToCoverPointTactical`-style checks,
    - group-search follower tactical movement no longer relies on a dedicated separate tactical end path.
- Core combat manager ownership changed:
    - old `FollowerCombatManager` usage was removed from the core path,
    - `AIBossPlayer` no longer updates a separate follower combat manager,
    - combat decisions are now follower-local inside `FollowerCombatDefault`.
- Core combat layer handoff:
    - live-enemy loss no longer immediately drops combat in all cases,
    - `FollowerCombatLayer` enters a short `linger` action for post-combat release/handoff timing,
    - `CombatPostCombatLingerAction` owns only presentation: it preserves the inherited look/pose briefly, then samples one standing/half/crouched stance and one horizontal left/right glance without invoking vanilla no-enemy prone behavior,
    - the last normally-expiring core linger performs one squad-level `35%` `EPhraseTrigger.Clear` roll and selects at most one participating follower; automatic vanilla/SAIN `Clear` remains muted outside the short selected-speaker permit,
    - normal action completion is delegated to tactic/common end logic instead of a layer-level action-enum comparison.
- Core combat routing is now objective-based on the vanilla/core path:
    - `FollowerCombatLogicBase` now owns the shared objective router and objective lifecycle
    - `FollowerCombatLogicBase` constructs the shared objective set: `FollowerCombatDefaultObjective`, `FollowerCombatSniperObjective`, and `FollowerCombatRegroupObjective`
    - concrete combat logic classes select their primary objective instead of replacing a generic default slot
    - `FollowerPmcCombatLogic` is the PMC combat entry point and selects the concrete logic from follower tactic
    - default/balanced PMC logic uses `FollowerCombatDefaultObjective` as its primary objective
    - marksman PMC logic uses `FollowerCombatSniper` with `FollowerCombatSniperObjective` as its primary objective
    - both default and marksman logic reuse the same `FollowerCombatRegroupObjective` path through `FollowerCombatLogicBase`
    - objective selection is combat-owned, not request-layer-owned
    - regroup command in combat is consumed immediately and converted into the regroup objective state
    - explicit `PushEnemy` during regroup switches back to the active tactic's primary objective
- Combat tactic construction rules:
    - keep routing/lifecycle in a `FollowerCombatLogicBase` subclass, keep the tactic decision tree in a dedicated class such as `FollowerCombatDefault` or `FollowerCombatSniper`, and keep the objective wrapper thin like `FollowerCombatDefaultObjective` / `FollowerCombatSniperObjective`
    - do not merge tactic decision trees into each other; `FollowerCombatDefault` and `FollowerCombatSniper` should be separate primary stacks that only share primitives through `FollowerCombatCommon`
    - `FollowerCombatCommon` should contain shared primitives and reusable decision helpers, not default-only or marksman-only policy
    - every movement decision that depends on cover or point data must set that destination first through committed cover, assigned cover, `GoToSomePointData.SetPoint(...)`, or a verified action-owned destination path
    - if a tactic cannot find a valid destination for a movement decision, return false and let the next branch decide instead of emitting a stale/no-target movement action
    - regroup stays objective-level and shared; default, marksman, protector, and future tactic stacks should activate regroup through `CreateRegroupObjectiveDecision()` rather than duplicating regroup movement logic

## 0c-1) Cover Contract (Authoritative)

All follower combat tactics must treat cover as a shared lifecycle contract, not tactic-local ad hoc behavior.

Required cover lifecycle:

1. select cover
2. move to cover
3. detect arrival (`IsInCover`, committed-cover proximity, or arrived point)
4. transition into hold/think window (`HoldFor`/`HoldCoverForMaxDuration`)
5. allow break into next decision when higher-priority conditions appear

Mandatory break priority while holding in cover:

- immediate fire opportunity (`ShouldShootImmediately` / valid visible shoot lane)
- taking fire or recent hit pressure
- ally engagement support opportunity
- boss-under-attack support/protection opportunity
- cover compromised while under pressure (left cover / unstable no-cover hold under fire)
- boss-distance regroup objective pressure when no stronger local fight reason exists

Heal-cover exception:

- heal cover movement (`runToHeal` / `moveToHeal`) must break immediately on arrival and hand off to heal decision.
- do not force a normal hold-think timer before `healInCover`.

Implementation guidance:

- keep arrival detection and hold arming in shared `FollowerCombatCommon` end-condition paths so all tactics inherit consistent behavior.
- tactic classes should only add/override policy-level break gating; do not fork base arrival semantics unless strictly necessary.
- do not leave movement actions running after destination arrival due to delayed EFT cover flags; use committed-cover/proximity and arrived-point fallbacks.

## 0c-2) Tactic-Specific Cover Usage

`Balanced` / default tactic:

- uses committed cover as primary stabilization primitive.
- on reached cover, should hold briefly and continuously rescan: immediate fire, visible fire, ally support, boss-under-attack, regroup pressure.

`Protector` tactic:

- prioritizes boss-local support cover and boss protection over generic pressure cover.
- when boss-under-attack is active, protection support routes should preempt passive hold.

`Marksman` tactic:

- prefers shoot-capable firing positions and sticky support/reposition holds.
- still must honor the same arrival -> hold -> break contract.
- uses shared recovery-qualified cover movement, arrival stabilization, and bounded no-cover recovery commitments rather than firing-position selection for survival movement.
- close automatic search waits for an eligible selected primary to become active and weapon-ready, then uses a cover-backed destination that stays at least 16 meters from the enemy anchor.
- hold and committed-cover movement breaks must retain a concrete fire/recovery successor; a failed opportunity keeps the current commitment instead of ending in hope.
- sticky cover/cooldown behavior must not block valid regroup break conditions once no higher-priority fire/support action exists.

`Regroup` objective:

- regroup owns bossward movement/cover selection and should not re-enter default/sniper push stacks while active.
- regroup may use bossward cover in hot contact, but completion remains objective-owned.

Cross-tactic rule:

- any new tactic (or objective) must explicitly define how it uses shared cover lifecycle hooks, and must preserve the mandatory hold break priorities above.
- `FollowerCombatDefaultObjective` wraps the existing `FollowerCombatDefault` tree:
    1. pre-fight grenade check and one-shot opener from `PrepareStartDecision`
    2. committed push continuation
    3. explicit ordered push (`GoForward` in combat)
    4. low-aggression regroup trigger
    5. immediate visible-enemy handling
    6. recovery / safe-cover behavior
    7. boss-under-attack protection routing
    8. ally engagement support scan
    9. generic blind advance / engage
    10. boss-distance regroup trigger
    11. committed cover continuation and passive hold fallback
- `FollowerCombatSniper` is separate from default PMC combat logic:
    - `FollowerCombatSniperObjective` owns the marksman decision stack
    - marksman ignores explicit push/grenade and generic suppression orders that should make a sniper rush; the only suppression-order exception is the boss-selected automatic-secondary fallback objective
    - close-quarter marksman handling preserves immediate fire with the current weapon, otherwise waits for an eligible full-auto secondary to be active and ready before close-search movement, and avoids forcing `runToEnemy` back to primary
    - autonomous marksman suppression uses the shared bounded follower-suppression lifecycle
    - marksman repositioning prefers shoot-capable cover/firing-position movement and hands boss-distance regroup to the shared regroup objective
- `FollowerCombatRegroupObjective` is a separate combat decision stack:
    - objective is "reach the boss / bossward cover" and it does not re-enter default push/hold logic while active
    - if enemy contact is still hot:
        - boss behind follower relative to enemy -> `regroup.withdraw.backward` via `attackMoving`
        - boss ahead -> `regroup.withdraw.forward` via `attackMoving`
        - boss lateral -> `regroup.withdraw.side` via `attackMoving`
        - hot regroup can use bossward cover, but the objective remains regroup-owned
    - if contact cools off -> `regroup.run` through `CombatRegroupRunAction` toward the boss/nav-sampled boss position, not intermediate cover hops
    - dogfight and heal/stim interruptions are allowed, but they do not change the regroup objective by themselves
- Visible-enemy handling is now more aggressive:
    - `ShouldShootImmediately()` is checked before `CanShootFromCurrentCover()`,
    - visible shootable enemies prefer `shootFromPlace` / `shootFromCover` before passive hold logic,
    - very-close visible enemies collapse into `dogFight`,
    - visible but not currently shootable enemies can still hand off into engage pressure.
- Combat cover is now follower-local and committed:
    - one cover point is committed and reused until invalid or intentionally abandoned,
    - the same committed move action/reason is preserved while moving to that cover to reduce stop/recommit thrash,
    - committed cover reached-state prefers immediate fire, then direct visible fire, then active pressure, before passive `coverHold`.
    - after the initial cover settle/commit window, stale committed cover can be released for boss-distance regroup if there is no real shooting or push opportunity.
- Core push behavior now has committed push state again:
    - push actions are tagged with stable reasons such as `push.run`, `push.goToEnemy`, `push.attackMoving`, `push.runToCover`, and `push.search`,
    - once one of those push decisions is chosen, `FollowerCombatDefault` keeps returning the same decision until a hard interrupt or action completion path ends it,
    - push selection itself is still delegated to the restored `FollowerCombatPush.EngageEnemy(...)` helper.
- Combat heal/stim flow now has explicit state handling in `FollowerCombatCommon`:
    - `healStimulators` is fully wired (decision -> action -> end handler),
    - stim use no longer preempts pending first-aid/surgery work,
    - heal-cover movement reuses one committed heal cover instead of repeatedly picking new points,
    - heal and stim actions have timeout exits to avoid stuck medical states.
- Core combat cover search is back on the mod-owned routing path:
    - `TryCommitCombatCover()` again prefers `PointToShoot`, retreat/attack cover, and boss-cover fallback,
    - `Covers.cs` now seeds candidate space from EFT `CoverSearchData` / `CoverPointMaster`, then applies pitFireTeam filtering and final selection,
    - so the current experiment is "our routing and filters, vanilla-backed candidate discovery".
- Combat cover is no longer boss-leashed by EFT `MAX_DIST_COVER_BOSS_SQRT`:
    - general combat cover uses a mod-owned 120m max distance from the follower,
    - a valid shoot/fight cover is allowed even if it is far from the boss.
- Boss-local protection/regroup cover is now separate from generic combat cover:
    - `BossCoverSearchRadius` is 30m,
    - boss-protection and hot-regroup cover prefer valid cover near the boss within that radius,
    - if no boss-local cover exists, fallback is a direct move to the boss position.
- Boss-distance escort behavior now participates in combat objective selection:
    - if the follower drifts far enough from the live boss position, default combat switches into the regroup objective instead of issuing one-off reanchor actions,
    - regroup chooses how to move by classifying the boss as front/back/side relative to the follower and current enemy direction,
    - low aggression can force regroup sooner unless the enemy is already close enough to demand local engagement,
    - explicit `PushEnemy` still overrides this and forces engagement.
- Hold behavior now distinguishes `coverHold` and `bossHold`:
    - both can break early for renewed combat opportunity,
    - `bossHold` and committed cover/shoot-cover states can break into regroup when boss-distance pressure wins.
- Core suppress-fire now supports a short recent-contact pressure window on the vanilla/core path:
    - if a follower just lost clean LOS, `suppressFire` can keep shooting briefly toward `EnemyLastPositionReal`,
    - this is intentionally narrower than broad blind-fire logic and is only used for suppression-style fire continuity,
    - `FollowerShotSafety` remains the hard stop; no recent-contact pressure shot is allowed through the player boss or other followers.
- Combat hold look priority now anchors to the current enemy direction first:
    - `CombatHoldPositionAction` / `EnemyFacingHoldLogic` tries current enemy or last known enemy look before recent-threat or ally-look fallback,
    - this keeps hold orientation aligned with ping/callout direction when the follower already has a combat enemy.
- Dedicated regular grenade use is now explicit on the core path:
    - no hidden opportunistic grenade throw inside dogfight,
    - grenade throw goes through `throwGrenadeFromPlace` with a dedicated safety gate.
- Combat talk frequency is now gated by the `botTalk` config on both vanilla `BotTalk` and SAIN `PlayerComponent.PlayVoiceLine` follower paths.
- Autonomous `OnFirstContact` / `OnRepeatedContact` speech is held for one second and emitted only when the same living `GoalEnemy` remains retained, filtering transient enemy-acquisition flicker without using current visibility as the confirmation; player-issued Contact / Over There suppression remains authoritative and cancels a matching pending callout.
- Post-combat `Clear` is separate from combat trash talk: automatic follower `Clear` remains muted, while the core linger coordinator can temporarily permit one selected squad speaker after a single `35%` team roll. General SAIN talk compatibility is core-owned; custom addon actions may control only their own speech.

**`PrepareStartDecision` — Combat Entry Decision Tree (`FollowerCombatCommon`):**

Called once per combat activation to pick the opening decision stored in `initialDecision`, consumed via `ConsumeInitialDecision()`. Priority order:

1. **Visible + close cover with shoot lane** (`≤25m` nav-path) → `attackMoving` (`startVisCloseCover`)
2. **Unseen + under fire:**
    - close cover → `attackMovingWithSuppress` (`startSuppressionCover`)
    - far cover → `runToCover` (`startUnderFireRunCover`)
    - no cover → `suppressFire` (`startUnderFireSuppress`)
3. **Unseen + not under fire + ally actively engaging enemy** (`TryGetAllyEngagementEnemy`):
    - cover found near ally enemy →`attackMovingWithSuppress` if `≤30m`, else `runToCover` (`startAllySupportSuppress` / `startAllySupportRun`)
4. **Unseen + low-threat enemy** (`IsEnemyLowThreat`, single enemy) → `goToEnemy` or `runToEnemy` based on distance (`startWeakEnemyPush`)
5. **Far cover fallback** → `runToCover` (`startVisFarCover` / `startBlindFarCover`)

If none match, `initialDecision` stays null and the layer falls through to `DecideCombat()`.

**Patrol Readiness (Post-Combat Handoff):**

- Wait for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of fixed timeout
- `UseSainFollowerCombat`: uses the addon-registered readiness callback for the active custom SAIN follower brain
- SAIN installed without the addon: uses the normal core-combat readiness path; addon absence is supported and is not a bridge failure
- Sprint thrash prevention via `FollowerSprintStateDirectionPatch` (SAIN-aware)

## 0d) Command Execution Pipeline

**Request Layer activates when `BotFollowerPlayer.CurrentCommand != null`**

Supported commands via `GestureCommandAction`:

- **HoldPosition** — Stop, optional crouch, periodic look-around (no timeout, persists until replaced)
    - Standard gesture-initiated: applies crouch by default
    - Phrase-initiated (STOP): applies no crouch, released by distance >25m or boss out-of-range
- **ComeCloser** — Move within ~1m of boss, then resume prior hold
- **ContactApproach** — A second Contact phrase within 5s moves eligible out-of-combat same-floor followers toward the boss's snapshotted position while they keep looking along the commanded Contact bearing
- **MoveToPoint** ("There") — Walk to NavMesh-validated target, brief arrival look-around
    - Gesture-initiated: single-follower move-to-point
    - Phrase-initiated: still used for explicit point movement when follower has no combat enemy
    - if movement is not accepted, the chosen follower can still receive a brief command-look override toward the pointed location
- **PushEnemy** (`GoForward` in combat) — Force combat engage routing against the follower's current enemy
    - `AIBossPlayer.ApplyGoForwardPhrase()` targets the looked-at follower if one is selected, otherwise it broadcasts to active followers
    - command acceptance is no longer limited by the old phrase-distance gate
    - if the follower already has an enemy, `GoForward` is converted into `PushEnemy` and handled inside `FollowerCombatDefault` through `FollowerCombatPush.EngageEnemy(true, ...)`
    - combat hold/committed-cover states now break so the ordered push can take over
- **HoldPosition combat phrase** (`EPhraseTrigger.HoldPosition`) — Temporarily applies `0%` effective combat aggression to targeted followers
    - stored as a temporary override in `BotFollowerPlayer`, leaving the persisted profile aggression untouched
    - core/vanilla combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`
    - SainMan uses the effective override to apply Coward settings; it does not create the former protection objective
    - marksman close auto-search is suppressed while this temporary hold-position override is active
    - override clears when combat/patrol handoff reports the follower safely out of combat, or when `Gogogo` is issued
- **Gogogo combat phrase** (`EPhraseTrigger.Gogogo`) — Clears any temporary combat-aggression override and returns followers to their persisted aggression value
- **LootGeneric** / **LootWeapon** — Command-driven loose-item, body, and container looting
    - loose-item pickup selects the eligible follower with the shortest complete NavMesh path, reserves taker ownership through `InteractableObjects.SetTaker(...)`, and executes `TakeLootItem`
    - body/container searches are restricted to saved teammates spawned through the raid squad flow and execute `TakeBodyGear` / `TakeContainerLoot`
    - non-teammate body/container assignment uses the shortest complete NavMesh path within `22m`; active combat, loot-command ownership, and target reservations prevent unsafe or duplicate assignment
    - body/container searches use simulated search timing, one settled inventory transaction at a time, filtered cargo rules, weapon-readiness/support planning, and command-locked cleanup
    - loose pickup pins the selected item and attempts taker recovery across transient state changes before aborting
    - accepted squadmate loot is tracked through `InteractableObjects.StoreItem(...)`; normal raid-end returns and player-death escape recovery use the centralized return-items path
    - detailed policy and current runtime-verification debt live in `docs/Looting.md` and the three `docs/Weapon-Pickup-*.md` trackers
- **OpenDoor** — Closest eligible follower assigned via `InteractableObjects.SetOpener(...)`, moves + `DoorOpener.Interact(Open)`
- **Regroup** — Vanilla: converge to boss-near cover; SAIN: via addon `SAINFollowerCombatRegroupAction`
    - core-path combat regroup no longer runs through `GestureCommandAction`
    - during combat it is now an objective trigger consumed by the active `FollowerCombatLogicBase` implementation
    - out of combat it still uses the request-layer regroup command path
- **ExitLocated** — Reuses regroup ownership in tight extraction mode
    - out-of-combat and SAIN-addon arrival requires `2.5m` NavMesh distance on the boss level
    - core combat completes inside `4m`
    - tight target failure falls back to the boss position, never normal boss-near cover
- **Attention** (Look) — Clear enemy state, release command, force attention to boss/point
- Directional quick phrases and contact cues now share a command-look override path:
    - `OverThere`, `Contact`, `Front`, `Left`, `Right`, and `OnSix` feed a temporary look target relative to the boss look direction
    - combat hold, core combat movement, and follow movement consume the same override so followers can briefly reorient without permanently stealing action ownership

**Visibility Requirements:**

- `HoldGesture` / `ThereGesture` — Follower sees boss gesture target (head or torso visibility sufficient)
- `ComeWithMeGesture` — Bidirectional visibility (boss sees follower AND follower sees boss)

**Interruption/Clearing:**

- Hit while executing command
- New command received (replaces prior)
- Attention/Look command (overrides)
- `FollowMe` / `Cooperation` phrase (clears request)
- Timeout or invalid execution state
- On regroup success: follower says `EPhraseTrigger.OnPosition`
- Combat transition boundaries now clear outstanding gesture/request commands:
    - `FollowerCombatLayer.Start()` clears follower commands on combat entry
    - `FollowerCombatLayer.Stop()` clears follower commands on combat exit

## 0e) Recruit & Group Patching

**Files:**

- `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase -> follower conversion
- `client/Patches/BotRecruitPatch.cs` — Request interception
- 35+ patches total across bot/group/follower/UI stability

**Core Bot/Group/Follower Stability Patches:**

- `BotGroupAddEnemyPatch`, `BotGroupReportEnemyPatch`, `BotGroupUsecEnemyPatch` — Enemy propagation safety
- `FactionHostility` — Default-on activation-time faction relationship repair: reciprocal BEAR/USEC and Scav/Scav-boss/PMC hostility, Cultist/Raider/Rogue warning-neutral relationships toward Scavs, player-Scav karma hostility preservation, and protected special-role exclusions including Partisan so his stock karma/zone/proximity behavior remains authoritative; follower groups additionally require a Scav to show hostile intent against the player or any follower before accepting the Scav as an enemy
- `BotGroupCalcGoalPatch` — Enemy acquisition assist hook
- `BotControllerEnemyPropagationSafetyPatch` — Validate player refs before propagation
- `BotOwnerIsFolowerPatch`, `BotOwnerManualUpdatePatch`, `BotOwnerActivatePatch` — Bot activation/update flow
- `BotMemoryDamagePatch`, `ExUsecBrainHitPatch` — Damage/hit reaction safety
- `LootPatrolActiveLayerListPatch`, `LootPatrolDecisionBypassPatch` — Prevent LootPatrol interference
- `AdvAssaultTargetFollowerGuardPatch`, `PatrolDataFollowerUpdateGuardPatch`, `AvoidDangerFollowerGuardPatch` — Vanilla follower recovery guards
- `AICoreAgentUpdatePatch` — Log/rethrow update exceptions

**Movement & Sprint Patches:**

- `FollowerSprintPatch` (when core follower movement owns sprint, including SAIN-without-addon) — Sprint behavior tuning
- `FollowerSprintStateDirectionPatch` — Prevent sprint/transition thrash during boss chase

**Recruit/Request Patches:**

- `BotReceiverFollowMeRecruitPatch` — Convert recruit requests to follower; tiered PMC level-based refusals are remembered per bot for the rest of the raid
- `FollowRequestPatch`, `HoldRequestPatch`, `OpenDoorRequestPatch` — Request type routing
- `BotReceiverGestureOverridePatch` — Gesture override handling
- `BotReceiverPhraseOverridePatch` — Route STOP phrase through pitAIBossPlayer instead of vanilla BotReceiver

**Spawn/Raid Patches:**

- `BotsControllerPatch`, `BotsControllerStopPatch` — Bot controller lifecycle
- `LocalGameCleanupPatch`, `LocalGameCtorPatch` — Local game init/cleanup
- `BotsEventsControllerSpawnPatch`, `BossSpawnWaveManagerClassPatch` — Wave/event spawning
- `RaidStartPatch` — Raid initialization

**UI/Command/Item Patches:**

- `AIDataContructPatch` — AI model data
- `QuickPanelPatch` — Cooperation UI entry (non-follower AI only)
- `GestureMenuPatch`, `GestureMenuAvailablePhrasesPatch` — Gesture availability
- `EPhraseTriggerPatch`, `PlayPhraseOrGesturePatch` — Custom phrase/gesture routing
- `ItemSpecificationPanelPatch`, `ModRaidModdablePatch`, `UnlootableComponentPatch` — Item interaction
- `BotTalkTrySayPatch`, `BotTalkSayPatch` — Speech routing
- `GrenadeThrowPatch`, `GrenadeTryThrowSafetyPatch` — Grenade safety
- `BulletImpactPatch`, `HearingSensorPatch`, `FootstepSoundPatch`, `PlayerSayPatch` — Sound/reaction flow

**Teammate/Social UI Patches:**

- `AddTeammateCreationFlowPatch`, `AddTeammateHeadSelectionPatch` — Teammate creation screen
- `SocialPatch`, `ChatFriendsPanelPatch`, `OtherPlayerProfileScreenPatch` — Social UI integration
- `MenuScreenSquadControlPatch` — Main-menu `My Squad` entry/button wiring
- `MatchMakerSideSelectionScreenPatch` — squad-mode side-selection screen takeover, tab injection, and teardown/restore
- `CurrentScreenTryReturnToRootScreenPatch` — clears squad-mode on explicit root return (`MainMenu` bottom tab)
- `MainMenuControllerReadyScreenGatePatch` — clears squad-mode on `Play` transition
- `MatchMakerAcceptScreenPatch` / related raid-start patches — synthetic teammate injection, preview rebuild, auto-join preload, transit re-add, and ready-screen parity guards

**Current Group Enemy Sync Model:**

- Group enemy sharing is now driven from `AIBossPlayer.OnBossGroupStaticUpdate()`
- Core behavior:
    - if one active follower has a stable enemy and is in a valid combat state,
    - and another active follower has no enemy,
    - core calls `BotsGroup.ReportAboutEnemy(...)` once for that update pass
- `BotGroupReportEnemyPatch` postfix remains the passive consumer that applies the report to idle followers
- Old overlapping follower-side sticky/adopt/team-sync logic was removed from `BotFollowerPlayer`

## 0f) Utility Modules & bridges

**File:** `client/Modules/`

**Addon Bridge Contract:**

- `SainAddonBridge.cs` — Delegate interface for SAIN addon callbacks
    - `IsReadyForPatrolAfterCombat(BotOwner)` — Patrol readiness query
    - `OnFollowerDismiss(BotOwner)` — Lifecycle event for addon cleanup
    - addon-owned combat-layer readiness, reset, release, and lifecycle callbacks are only attempted from core when `UseSainFollowerCombat == true`
    - general external-SAIN friendship, contact, enemy-state, acquisition, perception, and calculation compatibility is core-owned, gated by `IsSAINInstalled`, and must never require an addon callback

**Shared Utilities:**

- `BotOwnerUpdateHub.cs` — Centralized bot-owner update coordination
- `FollowerCalcGoalEnemyAcquire.cs` — Forward-scan enemy acquisition (runtime-neutral, assists vanilla + SAIN)
- `FollowerEnemyEnforceSuppression.cs` — Attention/Look suppression enforcement
- `InteractableObjects.cs` — Loot/door interaction state, boss visibility tracking, taker/opener assignment
- `AddTeammateCreationFlow.cs` — Teammate profile creation form state
- `BossPlayers.cs` — Boss/player roster management
- `FollowerTalkFrequencyGate.cs` — Follower-only combat talk throttling shared by vanilla and SAIN talk hooks
- `PingTeamates.cs` — Teammate enemy marker UI & callout system (throttled callouts, radio/visual markers)
- `TeammateAutoJoinRuntime.cs` — Per-ready-cycle suppression tracking for persisted auto-join teammates
- `FollowerRecovery.cs`, `FollowerAwareness.cs`, `Enemy.cs`, `Utils.cs` — Helper utilities

**Localization:**

- `TempEnglishLanguageProvider.cs` — Custom English text (teammate creation, squad menu, validation)

---

# SERVER SIDE: Teammate Backend & Social Integration

**Plugin ID:** `xyz.pit.fireteam` (server component)  
**Main entry:** `server/pitFireTeam.Server.cs`

## Backend Responsibilities

**Core System:**

- PMC armband enforcement: Forces all PMC-type bots to wear side armbands (BLUE for USEC/Assault, RED for BEAR)
- Plugin priority: `PostDBModLoader + 1` (applies after database loads)

**Profile Management:**

- Create mod-owned teammate profiles (PMC bot + custom nickname/voice/head)
- Store on disk: `user/mods/pitFireTeam-ServerMod/Resources/teammates/<sessionId>.db` (encrypted JSON records; imported originals are retained in <sessionId>.backup folders, see `docs/Teammate-Storage.md`)
- Fetch full profiles for team UI (roster, profile view)
- Persistence: clothes, head, voice, loadout, auto-join flag, raid-earned XP, and common skills per teammate
- Teammate IDs use stock `HashUtil.GenerateAccountId()` collision-checked allocation

## REST API Routes

**Teammate Management** (`/singleplayer/pitfireteam/`):

- `POST /teammate/create` — Create custom teammate (nickname/voice/head from UI) → saves to disk
- `GET /teammates` — List all teammates for session
- `GET /teammate/profile` — Fetch full profile for profile view screen
- `GET /teammate/profile/options` — Available customization options (clothes/loadouts)
- `POST /teammate/profile/suit` — Update clothes/head selection
- `POST /teammate/profile/loadout` — Change loadout
- `POST /teammate/profile/rename` — Rename teammate
- `POST /teammate/autojoin` — Persist teammate auto-join enabled/disabled state
- `POST /teammate/delete` — Delete teammate permanently

**Synthetic Team Ready Flow** (`/singleplayer/`):

- `GET /autoteam` — List teammate account ids currently flagged for persisted auto-join in the next PMC ready flow

**Social List Merging** (`/client/`):

- `GET /friend/list` — **Merge** teammates into stock friend list
- `GET /friend/request/list/inbox` — **Merge** recruit requests with stock friend requests
- `GET /profile/view` — **Intercept** teammate profile views (custom UI layout)
- `POST /friend/request/accept` — Accept friend/recruit request
- `POST /friend/request/accept-all` — Accept all requests
- `POST /friend/request/decline` — Decline request
- `POST /friend/delete` — **Intercept** to also delete teammates

**Group & Raid** (`/client/`):

- `POST /match/group/invite/send` — Send group invite; **auto-accept** for teammates + notify
- `POST /game/bot/followergenerate` — Generate teammate spawn profile for raid
- `GET /game/bot/followerdetails` — Fetch follower details (tactic/equipment/voice/head, currently "Default")
- `POST /game/bot/followerprogress` — Persist follower raid-earned XP/common skill progress to teammate storage

**Post-Raid** (`/singleplayer/`):

- `POST /returnitems` — Return teammate items via mail (NOT YET POSTED in client runtime)
- `POST /teamescaped` — Log escape/death outcome + notify teammates
- `POST /pitfireteam/teammate/raid-outcomes` — Persist teammate escaped/lost raid outcomes, health/death state, raid-stat counters, and eligible loadout-management equipment state
- `POST /pitfireteam/recruitpickup` — Queue defeated NPC candidates as friend requests

## Backend Services

**`FriendlyTeammateService`** — Core CRUD & Profile

- Create teammate from appearance form (name, voice, head)
- List/fetch teammates by session
- Get/set profile (full fetch, clothes/loadout persistence)
- Persist teammate auto-join flag in the profile database's settings record
- Rename teammate
- Delete teammate + remove from social lists
- Generate spawn profile for raid
- Persist follower raid-earned XP and common skill progression
- Save/load through FriendlyTeammateStorage and TeammateDatabase, including encrypted recovery snapshots

**`FriendlyTeammateSocialCallbacks`** — Social List Patching

- Inject teammates into `/client/friend/list` response
- Patch `/client/profile/view` to detect teammates and apply custom UI
- Add teammates to `/client/friend/request/list/inbox`
- Intercept `/client/friend/delete` to clean up teammates too

**`FriendlyTeammateMatchCallbacks`** — Raid & Group Invite

- Auto-accept group invites for teammates
- Provide follower spawn profiles on demand
- Support custom health overrides for spawn
- Accept follower-progress persistence payloads using an `IRequestData` wrapper batch body for 4.x static-router compatibility

**`FriendlyPostRaidService`** — Post-Raid Handling

- Return items via mail system (sends to player, randomized NPC sender)
- Log raid outcomes (all escaped / partial / death)
- Send mail notifications to teammates about raid results
- NPC sender identity, message templates

**`FriendlyRecruitService`** — NPC Recruitment

- Queue defeated NPC candidates as pending friend requests
- Calculate recruit approval chance based on player level
- Convert recruit to permanent teammate via `FriendlyTeammateService`

## Key Limitations (Current In-Progress)

- Tactic persistence not yet implemented (`followerdetails` hardcoded to "Default")
- Voice/head customization from profile screen not yet implemented
- Invite/group flow still needs parity with pre-raid screen sequencing
- Teammate profiles remain mod-owned bot JSON, NOT full stock `SptProfile` accounts
- Post-raid item-return endpoint (`/singleplayer/returnitems`) disabled in client runtime
- Auto-join currently targets the PMC synthetic ready flow; scav/other flows are not treated as full teammate-managed entry points

---

# SAIN ADDON: Current follower combat brain

The optional addon is active only for ready SainMan followers. It registers both solo/squad replicas and the combat lifecycle/decision/recorder callbacks. See [ADDON-ANALYSIS.md](ADDON-ANALYSIS.md) for the implementation map and latest 346 combat/personality / 13 source-parity / 32 proficiency validation record.

Addon `SainPlayerSquadBridge` binds real follower members to the human leader, preserves ordinary-squad election and uses null `LeaderComponent` because SAIN's type cannot represent a human. Existing lifecycle/boss-group updates maintain membership independently of per-follower combat readiness.

The implemented extensions are dedicated linger, shared two-mode regroup, one bounded firing-position attempt with retained failure, and passive native SAIN recording. General proficiency, relationships, perception, speech and safety remain core-owned. Removed legacy addon patches and the former single-layer implementation must not be restored.

Aggression-driven personality settings and source findings are in [docs/SAIN-Personalities-and-Aggression.md](docs/SAIN-Personalities-and-Aggression.md). Both combat layers are already integrated; the original leadership-only stage is historical.

# Core follower patrol and requests

Files:

- `client/BigBrain/FollowerPatrolLayer.cs`
- `client/BigBrain/FollowerRequestLayer.cs`
- `client/BigBrain/Actions/FollowAction.cs`
- `client/BigBrain/Actions/HealAction.cs`
- `client/BigBrain/Actions/GestureCommandAction.cs`
- Additional peace actions in `client/BigBrain/Actions/*` (peace/look/gesture/etc.)

Behavior currently implemented:

- Registers `pitFireTeam.FollowerPatrol` layer for multiple brains (`PmcBear`, `PmcUsec`, `ExUsec`, `PMC`, `Assault`, `Knight`, etc.).
- Layer active only when:
    - bot is alive/active,
    - bot follows `pitAIBossPlayer`,
    - bot has no current enemy.
- Post-combat handoff to patrol is state-driven (no fixed timeout):
    - waits for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of forcing patrol after a fixed delay,
    - while `UseSainFollowerCombat` is active, readiness is resolved through the addon-registered callback for the custom SAIN follower brain,
    - when SAIN is installed without the addon, patrol uses the normal core-combat readiness path; no addon callback is expected.
- Layer `Start()` performs recovery/reset:
    - pauses patrol data,
    - clears active request,
    - runs `FollowerRecovery.SoftReset`,
    - disposes current logic instance when possible.
- Action selection:
    - healing action while med work exists,
    - follow action otherwise,
    - includes out-of-combat reload handling.
- Healing action has timeout/cancel safety to prevent heal stuck states.

Follow movement:

- Follow logic is aligned toward old vanilla follower patrol style:
    - out-of-range chase toward leader,
    - in-range settle to cover/random nearby point using `GoToSomePointData`.
- Sprint run-stop mitigation:
    - `FollowerSprintStateDirectionPatch` modifies sprint-state direction under strict follower-chase conditions to avoid `Sprint -> Transition` thrash.
    - `FollowerSprintPatch` is enabled whenever core follower movement owns sprint (`!UseSainFollowerCombat`), including when SAIN is installed without the addon; SAIN-addon-owned combat keeps SAIN movement ownership.

Request/gesture movement:

- Registers `pitFireTeam.FollowerRequest` custom layer (priority `73`) above combat (`72`) and patrol (`71`).
- `FollowerRequestLayer` activates when follower has an active command in `BotFollowerPlayer`.
- `GestureCommandAction` handles:
    - `HoldPosition`: stop, crouch pose, periodic random look-around, no command timeout (persists until replaced/cleared).
    - `ComeCloser`: move to boss until close (about `1m`).
    - `ContactApproach`: phrase-only second-step Contact check using the boss position and look bearing snapshotted on the second phrase; it reuses ComeCloser movement, complete-path validation, distance-based walk/run, and same-floor gating, but deliberately discards a prior Hold so completion returns to normal follow.
    - `MoveToPoint` (`There`): move to projected/navmesh-validated target point (walk-only), then brief look-around on arrival.
    - loose `LootGeneric` / `LootWeapon` command route:
        - boss phrase selects the eligible follower with the shortest complete NavMesh path to the targeted loot object,
        - follower is assigned as taker through `InteractableObjects.SetTaker(...)`,
        - follower runs `FollowerCommandType.TakeLootItem` in `GestureCommandAction` (move to loot point + inventory transfer attempt),
        - successful pickup stores the accepted item through `InteractableObjects.StoreItem(...)` for post-raid return handling.
    - body/container loot command route:
        - only saved teammates spawned through the raid squad flow are eligible,
        - `CheckHim` / `LootBody` creates `FollowerCommandType.TakeBodyGear`; a loot phrase aimed at a searchable container creates `FollowerCommandType.TakeContainerLoot`,
        - the selected follower reserves the target, becomes command-locked, performs the search simulation, and executes filtered inventory moves sequentially,
        - combat, invalid state, timeout, or transaction failure releases command and reservation ownership through the centralized cleanup paths.
    - `OpenDoor` command route:
        - boss phrase selects closest eligible follower to the targeted door,
        - follower is assigned as opener through `InteractableObjects.SetOpener(...)`,
        - follower runs `FollowerCommandType.OpenDoor` in `GestureCommandAction` (move to door + `DoorOpener.Interact(..., Open)`),
        - opener/taker state is cleared when command clears, including combat-entry handoff.
    - `Regroup` (`EPhraseTrigger.Regroup`):
        - vanilla regroup is implemented and active for no-SAIN or out-of-combat cases,
        - SainMan combat regroup is executed through `SAINFollowerRegroupObjective` -> `SAINFollowerSquadRegroupAction`,
        - regroup converges to boss-near cover/random point (not exact boss position) and supports boss-movement reanchor.
    - `ExitLocated` (`EPhraseTrigger.ExitLocated`):
        - reuses the same regroup command and combat-objective ownership in tight mode,
        - out-of-combat completes at `2.5m` NavMesh distance on the boss level; core and SainMan combat use a `4m` tight arrival envelope,
        - prefers tight follower spacing and falls back directly to the boss position without normal regroup cover acquisition.
    - Regroup ignore/interruption safeguards:
        - ignored when follower is healing or already close enough (`~8m` nav-path distance on same level),
        - interrupted/released when follower can see and shoot enemy, needs heal, or must avoid danger (grenade/BTR),
        - vanilla path releases control when SAIN combat regroup route becomes valid; SAIN path releases control when combat route is no longer valid,
        - interrupted by being hit, follower death, attention reset (`EPhraseTrigger.Look`), or replacement by a newer command,
        - on successful regroup arrival follower says `EPhraseTrigger.OnPosition`.
- Boss phrase commands handled directly by `AIBossPlayer`:
    - `EPhraseTrigger.HoldPosition` applies a temporary `0%` effective combat-aggression override to targeted followers; it is not a persistent profile aggression change.
    - `EPhraseTrigger.Gogogo` clears that temporary override and returns each follower to its saved aggression.
    - `BotReceiverPhraseOverridePatch` suppresses vanilla receiver handling for `Stop`, `HoldPosition`, and `Gogogo` on followers so the boss command path owns the behavior.
- Gesture visibility requirements:
    - `HoldGesture` and `ThereGesture` require follower to see boss gesture target (`head` or `torso` visibility; either is enough).
    - `ComeWithMeGesture` requires both directions:
        - boss can see follower gesture target (`head` or `torso`),
        - selected follower can see boss (`head` or `torso`).
- Command sequencing details:
    - If `ComeCloser` was issued while `HoldPosition` was active:
        - bot approaches,
        - then resumes hold (unless interrupted by a new command/clear event).
    - If `ComeCloser` was not issued from hold:
        - bot performs a short arrival pause/look-around, then clears command.
    - If a new `There` is issued while bot is in arrival look-around:
        - bot immediately starts moving to the new point.
    - `Hold` / `Come` interrupt and replace `There`/arrival-look behavior.
- Contact look pause:
    - On enemy-contact orders (`OnRepeatedContact` / custom `OverThere`), command random look logic is paused for ~`2-4s` so bots keep contact orientation.
    - A second `OnRepeatedContact` phrase within `5s` consumes the phrase pair and creates an out-of-combat `ContactApproach` for eligible followers; custom `OverThere` remains look/contact-only and does not participate in the pair.
- Gesture routing:
    - Custom `OverThere` is handled separately from `There`.
    - A short suppression guard prevents immediate `There` echo from being treated as move-to-point after custom `OverThere`.
- Commands are cleared on:
    - `FollowMe` / `Cooperation`,
    - `Look` (attention),
    - bot being hit,
    - command timeout / invalid execution state.

## 0) Project Context & References

- Old plugin codebase: see `LOCAL.md` reference paths.
- Old client reference (3.11): see `LOCAL.md` reference paths.
- New client reference (4.x): see `LOCAL.md` reference paths.
- SAIN plugin reference: see `LOCAL.md` reference paths.

**Positioning:** `pitFireTeam` is both a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment and an alternative plugin implementation with new BigBrain-native follower layers/actions.

---

# Command/Gesture IDs & Practical Entry Points

## Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
    - `EPhraseTrigger.Stop` — Hold position without crouch, broadcast to nearby followers (25m range or looked-at follower)
    - `EPhraseTrigger.HoldPosition` — Temporary combat hold/aggression override; acts like `0%` aggression until combat ends or `Gogogo` clears it
    - `EPhraseTrigger.Gogogo` — Clears the temporary combat hold/aggression override and restores each bot's persisted aggression
- Phrase broadcast commands (new):
    - `STOP` (from `EPhraseTrigger.Stop`): Sets all targeted followers to `HoldPosition(infinity, crouch: false)`, released when follower moves >25m from boss or on new command
    - `HOLDPOSITION` (from `EPhraseTrigger.HoldPosition`): Applies temporary combat aggression override to targeted followers without changing persisted profile aggression
    - `GOGOGO` (from `EPhraseTrigger.Gogogo`): Clears temporary combat aggression override
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- BigBrain core logic:
    - `client/BigBrain/FollowerPatrolLayer.cs` — Follow patrol out-of-combat
    - `client/BigBrain/FollowerRequestLayer.cs` — Active command execution
    - `client/BigBrain/Actions/FollowAction.cs` — Chase and settle movement
    - `client/BigBrain/Actions/GestureCommandAction.cs` — Command pipeline (hold/there/come/loot/door/regroup)
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase → follower conversion
    - `client/Components/BotFollowerPlayer.cs` — Follower state container
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs` — Boss command handling
- SAIN combat addon (follower combat layer):
    - `addon/SAINFollowerSoloCombatLayer.cs` / `addon/SAINFollowerSquadCombatLayer.cs` — Combat action routing
    - `addon/SAINFollowerSquadDecisionCalculator.cs` — Priority-based decision scoring
    - `addon/SAINFollowerCombatRegroupAction.cs` — Combat regroup execution
    - `addon/SAINFollowerCombatSuppressAction.cs` — Fire support logic
    - `addon/SAINFollowerCombatFollowBossSearchAction.cs` — Coordinated search

---

# Known Issues & Tracking

See the local bug-tracker file listed in `LOCAL.md`.

**Currently Active Risk Areas:**

- SAIN post-combat idle/freeze behavior for followers
- Enemy propagation consistency across all followers
- Reaction-system regression (early detection):
    - Hard guards in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `BotGroupReportEnemyPatch` prevent followers from marking boss/followers as enemies
    - Still active risk area for hearing/voice/bullet reaction paths
- Follow-up behavior when player is hit out of combat
- Follower death reaction:
    - Nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`
    - Corpse-position visibility checked up to ~60s if nobody saw death

---

## DEBUGGING & INVESTIGATION PRACTICES

- treat bugs tracked separately as SAIN and vanilla categories
- always spend time checking SAIN and client sources at the beginning of the session to get proper context
- prefer to check client sources first when some method, class, or property is not clear, rather then making assumptions
- Debug console command: `fs_spawnfollower` spawns one follower in-raid using game-side `ISession.LoadBots` profile flow (NOT BE-dependent)

## Bugs

Bugs are tracked in the local bug-tracker file listed in `LOCAL.md`.

- `BotGroupAddEnemyPatch`
- `BotGroupReportEnemyPatch`
- `BotGroupUsecEnemyPatch`
- `BotGroupCalcGoalPatch`
- `BotControllerEnemyPropagationSafetyPatch`
- `BotMemoryDamagePatch`
- `ExUsecBrainHitPatch`
- `BotOwnerIsFolowerPatch`
- `BotOwnerManualUpdatePatch`
- `BotOwnerActivatePatch`
- `SessionLoadBotsEnglishVoicePatch`
- `LootPatrolActiveLayerListPatch`
- `LootPatrolDecisionBypassPatch`
- `AdvAssaultTargetFollowerGuardPatch`
- `PatrolDataFollowerUpdateGuardPatch`
- `AvoidDangerFollowerGuardPatch`
- `AICoreAgentUpdatePatch` (logs/rethrows update exceptions)

Movement:

- `FollowerSprintPatch` (conditional: whenever core follower movement owns sprint, including SAIN-without-addon)
- `FollowerSprintStateDirectionPatch`

Recruit/request:

- `BotReceiverFollowMeRecruitPatch`
- `FollowRequestPatch`
- `HoldRequestPatch`
- `OpenDoorRequestPatch`
- `BotReceiverGestureOverridePatch`

Spawn/raid:

- `BotsControllerPatch`
- `BotsControllerStopPatch`
- `LocalGameCleanupPatch`
- `LocalGameCtorPatch` (patched via `harmony.CreateClassProcessor(...).Patch()`)
- `BotsEventsControllerSpawnPatch`
- `BossSpawnWaveManagerClassPatch`
- `RaidStartPatch`

Items/equipment:

- `UnlootableComponentPatch`
- `ModRaidModdablePatch`
- `ItemSpecificationPanelPatch`

Combat/hearing/talk:

- `BotTalkTrySayPatch`
- `BotTalkSayPatch`
- `GrenadeThrowPatch`
- `GrenadeTryThrowSafetyPatch` (from `GrenadeThrowPatch.cs`)
- `BulletImpactPatch`
- `HearingSensorPatch`
- `FootstepSoundPatch`
- `PlayerSayPatch`

AI data / command UI:

- `AIDataContructPatch`
- `QuickPanelPatch`
    - cooperation entry is shown for any alive, non-follower AI target (still subject to recruit acceptance checks elsewhere)
- `GestureMenuPatch`
- `GestureMenuAvailablePhrasesPatch`
- `EPhraseTriggerPatch`
- `PlayPhraseOrGesturePatch`
    - intercepts only custom phrase IDs and skips interception when the action is a real player gesture (`GClass3937.IsPlayerGesture(actionId)`), so vanilla gestures are not hijacked

SAIN integration:

- `SAINPatch.PatchSAINIfInstalled(harmony)` owns general compatibility when external SAIN is present.
- `addon/pitFireTeam.SAINAddon.csproj` / `xyz.pit.fireteam.sainaddon` registers `SAINFollowerSoloCombatLayer` (74) and `SAINFollowerSquadCombatLayer` (75). Only ready SainMan followers use them; other tactics and absent/unready addon state retain core combat.
- `SAINFollowerRuntime` registers/releases the `SainAddonBridge` lifecycle/readiness callbacks, `SainSquadDecisionBridge` provider/fallback callbacks and optional Debug `SainCombatRecorderBridge` callbacks. Native decision publication remains authoritative.
- `SainPlayerSquadBridge` keeps the real player as leader. The addon uses player-aware squad decisions, regroup and follow-search actions, plus dedicated linger and bounded MoveToEngage actions.
- `SainManPersonality` is the addon-owned follower-local native setup/restore boundary. The addon supplies aggression-interpolated follower-local settings; the fixed Chad loop is removed. Follower proficiency is still core-owned.
- SAIN's native mover owns path execution while its layers are active. Recorder schema 13 records native paths and cover, with explicit movement ownership; do not diagnose SAIN movement from an old EFT target.
- All legacy addon Harmony/tuning/acquisition patches and the broad runtime reset bridge were removed. The addon has no general SAIN patches, does not mutate shared settings and does not clear living enemy memory to obtain patrol readiness.
- Core `FollowerCalcGoalEnemyAcquire`, triggered by `BotGroupCalcGoalPatch`, owns shared forward-scan acquisition. There is no remaining addon `CheckAddEnemy`/forced-retention path to enable.
- General compatibility, perception, proficiency, speech and shot-safety work belongs in core and must function without the addon. Brain-specific policy/actions/state belong in the addon through validated core boundaries.
- Current behavior, validation, pending raid qualification and source provenance are linked from [ADDON-ANALYSIS.md](ADDON-ANALYSIS.md).

## 5) Safety/Crash Guards Added

- LootPatrol active-layer guard:
    - `client/Patches/LootPatrolSafetyPatch.cs`
    - `LootPatrolActiveLayerListPatch` strips vanilla LootPatrol (`GClass117`) from BigBrain active layer list for followers before layer update.
    - `LootPatrolDecisionBypassPatch` prevents LootPatrol decision execution when follower state is active.
- Grenade throw safety:
    - `client/Patches/GrenadeThrowPatch.cs` includes null-safe guard for `GClass274.UpdateTryThrow`.
- Player say/hearing null guards:
    - `client/Patches/HearingSensorPatch.cs` hardened against null bot/follower references.
- Enemy propagation guard:
    - `client/Patches/BotGroupPatch.cs` (`BotControllerEnemyPropagationSafetyPatch`)
    - validates `AddEnemyToAllGroupsInBotZone(...)` player refs and skips invalid propagation calls that can occur after debug/out-of-band spawns.
- Interaction/visibility null guards:
    - `client/Modules/InteractableObjects.cs`
    - hardened seen-enemy and boss-state checks against null/missing player/bot references.
- Vanilla follower/update crash guards:
    - `client/Patches/FollowerVanillaSafetyPatch.cs`
    - `PatrolDataFollowerUpdateGuardPatch` prevents vanilla follower patrol from running with a missing boss/player-follow backing object and attempts boss-link recovery through `BossPlayers`.
    - `AvoidDangerFollowerGuardPatch` blocks vanilla `GClass48.ShallUseNow()` for followers when required danger subsystems are not initialized, preventing repeated `AvoidDanger` NREs.

## 6) Teleport / Utility

- Teleport key action (`_BotTeleport`) now:
    - computes NavMesh-valid spread spots around player,
    - enforces spacing from player and between followers,
    - avoids overlap pileups with multiple followers.

## 7) Status/Debug Notes

- `PingTeamates` enemy marker/status timing corrected:
    - uses `Time.time - PersonalLastSeenTime` for recency.
- `PingTeamates` callout throttling:
    - directional voice callouts are now throttled to once every `15s` across pings,
    - ping radio/location sound and triangle marker still update every valid ping.
- TeamStatus/Look command burst handling:
    - command handling was debounced to reduce repeated heavy work during rapid player phrase spam.
- Legacy addon compatibility paths:
    - follower-friendly-fire post-processing, cloned SAIN combat-template injection, personality/file-settings rewriting, general talk muting, and broad SAIN search-state invalidation still appear in historical source notes.
    - none are valid merely because they are follower-filtered. General compatibility belongs in core; alternate-brain behavior must be implemented in the current solo/squad replicas or their custom SAIN actions; shared/general SAIN object or method mutation must be removed.
    - addon release/reset may clean only its active follower's custom layer/action state.
- `PingTeamates` GUI path optimization:
    - per-frame draw loops now use index-based iteration instead of delegate-based `List.ForEach`.
    - bot status text reuses a single `StringBuilder` instance instead of allocating per bot per frame.
    - tracked body-part iteration uses a static array instead of `Enum.GetValues(...)` allocations.
- SAIN bridge debug noise reduction:
    - release/default guidance should assume no addon enemy-bridge debug logging is enabled or required.
- SAIN navigation investigation result:
    - SAIN does not currently have one broad active non-mover "navigation fix" patch that generically recovers stuck bots.
    - active navigation-adjacent behavior is split across:
        - `Patches/MovementPatches.cs` global movement-context patches (`MovementContextIsAIPatch`, `CanBeSnappedPatch`) and mover-manual-update patches,
        - door handling outside the mover (`Classes/PlayerManager/Doors/DoorHandler.cs`, `Classes/Bot/Doors/DoorOpener.cs`),
        - `SAINBotUnstuckClass`, which contains vault/teleport unstuck logic but its coroutine body currently has the core unstuck calls commented out.
    - practical implication: treat SAIN door handling and SAIN layer/mover handoff as active navigation influences first; do not assume SAIN has an active generic unstuck system currently rescuing follower navigation.
- Several debug/trace patches were iterated during movement work; current runtime path is focused on minimal active tracing.
- Request command logs were reduced/removed from `AIBossPlayer` (`[Req] Hold/There/ComeWithMe ...`) to keep runtime logs cleaner.
- Debug console command:
    - `fs_spawnfollower`
    - available in-raid, spawns one follower for the player side.
    - profile generation uses game-side bot profile flow (direct `ISession.LoadBots` path via game profile/session objects), then injects into `BotCreationDataClass.CreateWithoutProfile(...)`.
    - bot spawner `InSpawnProcess` is incremented/decremented with failure rollback to avoid breaking later vanilla bot spawns.
    - fallback safe profile request may be used if requested side/role generation fails.
- English BEAR voice assignment is applied at profile-load time:
    - `SessionLoadBotsEnglishVoicePatch` patches `ProfileEndpointFactoryAbstractClass.LoadBots`
    - each returned `Profile` is processed by `BotOwnerActivatePatch.ApplyEnglishVoiceForProfile(...)`
    - this is the active runtime path for voice replacement (instead of late activation-only mutation)

## 8) Known Open Issues

See the local bug-tracker file listed in `LOCAL.md`.

Examples currently tracked there:

- SAIN post-combat idle/freeze behavior for followers.
- Enemy propagation consistency across all followers.
- Reaction-system regression (SAIN/vanilla reaction work):
    - after changes made to work around `EnemyController.IsEnemy` timing gaps for early follower reaction, a regression was observed where followers could incorrectly mark the player as enemy.
    - hard guards were added in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `client/Patches/BotGroupPatch.cs` (`BotGroupReportEnemyPatch`) to prevent followers from adding the boss player or other followers as enemies.
    - more testing is still needed; treat this as an active risk area when changing reaction logic (`FollowerAwareness`, hearing/voice/bullet paths).
- Follow-up behavior when player is hit out of combat.
- Follower death reaction:
    - when a follower dies, nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`;
    - if nobody saw death, corpse-position visibility is checked for up to `~60s` and reaction can still trigger.

## 9) Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- Follow layer/action logic:
    - `client/BigBrain/FollowerPatrolLayer.cs`
    - `client/BigBrain/Actions/FollowAction.cs`
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs`
    - `client/Components/BotFollowerPlayer.cs`
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs`
- SAIN combat addon:
    - `addon/SAINFollowerSoloCombatLayer.cs` and `addon/SAINFollowerSquadCombatLayer.cs`
    - `addon/SAINFollowerSquadDecision.cs` and `addon/SAINFollowerRuntime.cs`
    - `addon/SAINFollowerRegroupObjective.cs` and `addon/SAINFollowerSquadRegroupAction.cs`
    - `addon/SAINFollowerFollowSearchPartyAction.cs`
    - `addon/SAINFollowerCombatHandoff.cs` and `addon/SAINFollowerLingerAction.cs`
    - `addon/SAINFollowerEngageAttempt.cs` and `addon/SAINFollowerMoveToEngageAction.cs`
    - `addon/SAINFollowerRecorder.cs` and `client/Modules/SainCombatRecorderBridge.cs`
    - `addon/SainManPersonality.cs` for native personality setup/restoration

## 10) Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## 11) SAIN Vision/Enemy Notes (2026-03-01)

- Detailed investigation note is recorded in:
    - `docs/sain-vision-enemy-pipeline-2026-03-01.md`
- Key result:
    - SAIN enemy retention depends on internal `EnemyKnown` + `LastKnownPosition` + active checks.
    - SAIN can clear current goal enemy quickly if any of those conditions drop, even after short visual contact.
    - SAIN also patches vanilla `EnemyInfo.HaveSeen/ShallKnowEnemy*` and `LookSensor` flow, so behavior can diverge sharply from vanilla pickup logic.

Update (2026-03-06):

- Legacy, nonconforming enemy-contact implementation:
    - `addon/SAINEnemyAcquireGatePatch.cs` and addon retention code historically gated `SAINEnemyController.CheckAddEnemy`; enemy/contact/acquisition compatibility is core-owned and this general-method patch must move or be removed before addon re-enable.
    - historical note: older investigation referenced `SAINCalcGoalPatch`, but that wrapper was removed; `docs/SAIN-Integration.md` is authoritative.
- Legacy, nonconforming proficiency implementation: follower proficiency was increased through general SAIN addon patches:
    - `addon/SAINFollowerPersonalityPatch.cs` raises follower detection/reaction and tightens aim behavior (higher `GainSightCoef`, higher hearing/visible multipliers, faster precision, lower accuracy/scatter multipliers).
    - `addon/SAINFollowerHitAccuracyPatch.cs` bypasses SAIN `AimHitEffectClass.GetHit` aim-affection for followers so incoming hits do not degrade follower aim.
    - `addon/SAINFollowerLowLightVisionPatch.cs` reduces low-light time-to-spot penalty for followers by post-processing SAIN time vision modifier in `EnemyGainSightClass.CalcTimeModifier`.
    - these patches are not permitted addon architecture. Move required general compatibility to core, express actual brain behavior inside custom layer/actions, or remove it.
