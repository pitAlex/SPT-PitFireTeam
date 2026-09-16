# SAIN addon progress and session handoff

## SAIN combat There / Come here (2026-09-17)

Ready SAINGrunt followers now consume the existing `CombatMoveToPointTactical` and `CombatComeToBossCover` commands through `SAINFollowerRelocationObjective`, before push/regroup and ordinary native squad work. The existing player gesture routing, selected follower, visibility/range checks and 30m There limit are unchanged. Peaceful commands stay Core-owned.

- **There:** complete-path tactical walking to the sampled command point, with destination reservations. The point stays fixed when the player moves.
- **Come here:** bounded native cover discovery around the player at consumption, restricted to the Core boss-cover radius and at least one metre of progress toward the player. No suitable cover uses Core's shared complete-path fallback, stopping 1.5m back along the final path segment and within 2m of the sampled player position. Once selected, the destination is committed.
- The addon uses a dedicated action with native SAIN walking/shooting/steering. Point movement yields to a visible shootable enemy or incoming fire, while cover approach can keep walking and firing. No new blind-suppression policy is added. Native medicine, retreat, grenade handling, melee and dogfight retain priority; pending commands keep their original eight-second expiry. Active relocation yields to survival work.
- Core geometry and thresholds are shared in `FollowerCombatCommandGeometry`: 1.25m arrival, 0.35m progress and a four-second stall bound. The extraction leaves Core's boss-approach algorithm unchanged. Arrival uses the existing three-second settle with fire/safety preemption. Cover planning is bounded to eight seconds and retains four native probes per finder per frame. Repeated polls cannot rearm arrival or switch a completed action into stale native MoveToEngage before publication.
- There/Come here replace previous push/regroup intent; new commands and Go Forward replace relocation. Explicit gestures also work during On Your Own. Invalid/no-path destinations give Core's Negative/NoGesture feedback. Enemy loss, opt-out and teardown release owned paths/claims without clearing another owner's destination.
- `sainObjective` events and the objective snapshot include relocation mode, destination, planning/movement/arrival/failure reasons and stall duration. No new Harmony hooks or typed SAIN reference in Core are introduced. Friendly-fire review remains deferred.

Validation: 697 production addon combat checks (48 new gesture checks) pass, including real production command setters/timeouts, publication/layer routing, cover/progress/fallback geometry, navigation failure, reservations, scan budget, arrival, interruption and cleanup. Matching Debug core/addon build passed with zero warnings/errors; 54 leadership/ownership checks and 13 replica comparisons pass. Both extracted Core path methods compare unchanged to their prior bodies. Deployed matching Debug core/addon DLLs and PDBs plus license on 2026-09-17 00:17:50 +03:00 with Tarkov closed. All five installed SHA-256 hashes match. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-combat-gestures-20260917-001749`. In-raid navigation and combat presentation still need qualification.

Combat-gesture deployment hashes:

| File | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `CA2941FA165A37EFFB15F9DF5000FF506CF6783ECC6338138187A1EAF1249443` |
| pitFireTeam.pdb | `E925510144EC2EF91F355EEA7D1DACFACD85EA1956220395500A06A6BD0693B3` |
| pitFireTeam.SAINAddon.dll | `8C4CCBF7F0BD330CB5269F6E82A13C2418C96B713C2D3ED8D60FD65451DA4E61` |
| pitFireTeam.SAINAddon.pdb | `099C08EABC3640A5AEA8C96AD6FD77CF5703B38C48F7E821C4017AE5F36EBA91` |
| SAIN-LICENSE.txt | `7047B3162F2663E5DCC062FB2AFF07E00E26DBA13BB19D722CF8A24D8466DC57` |



## Ordered push contact interruption (2026-09-16)

Shoreline `20260916-183446-Shoreline.jsonl`: Go Forward at 1939.037 starts Brick's ordered push. Native last-known knowledge disappears at 1949.327 and the addon clears it as `targetLost`; EFT goal briefly clears at 1949.35986 and returns at 1949.37659. The same native contact is back by 1949.47656, but Brick creates an Automatic push at 1949.57666, fails its risk score and regroups before SeekCover. Medved retains Ordered mode. This is an addon intent-lifetime bug.

Bound ordered pushes now retain intent for a fixed three-second contact interruption, including a brief missing accepted EFT goal. Advancement pauses, its owned path stops, and native combat admission/knowledge are unchanged. The same valid contact resumes Ordered mode with the committed destination and existing movement/failure budgets; changed native knowledge keeps the existing new-contact handling. Repeated polls and same-target orders cannot extend the deadline. Confirmed target death, replacement/cancel commands, explicit release and lifecycle cleanup remain immediate; sustained loss expires. Automatic pushes keep their existing immediate contact-loss behavior. Retained orders cannot fall through to automatic regroup. Recorder reasons `contactInterrupted` / `contactRestored` and `contactGraceRemaining` expose the interruption.

Validation: 649 production addon combat checks (22 additional checks) and 13 replica comparisons pass. Matching Debug core/addon build passed with zero warnings/errors. Contact-gap tests cover the recorded interruption, accepted-goal handoff, stopped movement, retained leg/stall/failure limits, timeout, repeated commands, late restoration, death, replacement orders, medical priority and automatic-push isolation. Deployed matching Debug core/addon DLLs and PDBs plus license on 2026-09-16 22:21:24 +03:00 with the game closed; all five installed SHA-256 hashes match. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-push-contact-retention-20260916-222124`.

Installed hashes for the contact-retention deployment:

| File | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `CF10A9BDAE67E8E0CC6DCEF50EFEEA3B9DCC1C286FE807DA10B74367BC0396E3` |
| pitFireTeam.pdb | `275191F9F9CEF49839B6444025968FF0E878CD2557C57BEF3C59FB8C67B5CAF3` |
| pitFireTeam.SAINAddon.dll | `42F7566A459C657C94F754FF0D2A489BFB48B14251891AE9F61F4714CFE7951A` |
| pitFireTeam.SAINAddon.pdb | `507243D646C08DE4F4D973F9C41BCA4BD2DDFDEAA5F702BD860B0F2C3C75027F` |
| SAIN-LICENSE.txt | `7047B3162F2663E5DCC062FB2AFF07E00E26DBA13BB19D722CF8A24D8466DC57` |
 Raid qualification remains required.


## Deferred: SAIN follower friendly-fire review (2026-09-16)

User explicitly deferred changes. Revisit Shoreline `20260916-183446-Shoreline.jsonl`, around raid time 1661.970 (local time approximately 18:57:41). Medved lost 40.5352 chest health while automatically regrouping about eight metres from Brick. Brick was in native StandAndShoot, with a trigger event about 16.6 ms before the inferred hit; the movement crossed his recorded aiming lane. This strongly suggests friendly fire but the recorder has no attacker identity, so attribution and frequency are not proven.

Compare core's intended-target and actual-shot-direction checks with `FollowerSainFriendlyFirePatch`: both SAIN overloads currently check the supplied barrel direction; the target overload uses the target only for distance. Installed SAIN AimClass calls the friendly-fire check before `NodeUpdate`. Investigate a final pre-shot guard using core's established lane helpers and add passive damage attribution before concluding causality. Include ordinary shooting, suppression/manual shooting, moving squadmates and ordinary-bot isolation. No friendly-fire behavior was changed for the ordered-push fix.

## Performance and push recovery (2026-09-16)

The SAIN addon cover finder now spreads candidate creation and revalidation across frames (four native probes per finder per frame, at most 32 discovered candidates per geometry scan). Stable validation is reused for one second; an unfinished pass retains its completed probes so slower decision publication cannot starve it. Enemy/position changes and native bad/spotted flags invalidate reuse. Failed selection retries after 0.5 seconds or meaningful context changes. Selected-cover observation still revalidates independently at its existing cadence. An addon-owned `DogFightMove` prefix prevents native no-cover aggressive fallback while ordinary cover selection is pending; urgent/defensive combat remains native. Pending push planning uses the stationary push hold.

Inactive regroup no longer measures player routes. Push assessment shares the existing half-second player-distance measurement, and core distance probes reuse a thread-local NavMeshPath. Core's optional SAIN compatibility hooks cache compiled BotOwner access and bind aim/friendly-fire parameters directly, removing repeated reflection and Harmony argument-array boxing on those paths. Disabled recording exits before phase formatting/decision payloads; push risk diagnostics format only on changes, and unconditional regroup info logging is removed. Enabled battle recording still has its existing snapshot/serialization cost; no FPS improvement is claimed without a raid comparison.

Shoreline `20260916-165635-Shoreline.jsonl` recorded Go Forward for both followers at 772.860. Medved exhausted his approach at 773.677 despite a complete native route; Brick retained the command in medical recovery. `SAINFollowerApproachRoute` now supplies a maximum 20-metre walking leg along a newly verified complete route to the remembered location when direct stepping fails. A detour may initially increase direct distance. Endpoint/corner completeness, a maximum 30-metre actual leg route, destination reservations, existing risk gates and the six-second stall/twenty-second execution limits remain. Repeated orders do not rearm a failed contact. Recorder failure reasons distinguish sampling, incomplete routes, invalid corners/legs and reservations.

`SainMedicalDecisionBridge` extends native first-aid enemy checks and surgery safety only for ready addon combat. Reached usable SAIN cover must be stationary, free of pressure/recent hits, and protected from every relevant known contact using core `Covers.IsHardCoverFromThreat` chest/head rays. Visible/shootable/recently seen or very close threats reject the exception; heard-only contacts do not require a nonexistent sight timestamp. Item eligibility, bleeding-before-surgery, native decision publication and medical execution remain native. Physical protection checks are cached briefly; cover/position/knowledge and immediate danger still gate each call. The recorder exposes the last passive `medicalCover` reason/time in cover policy snapshots.

Installed SAIN 4.5.1 `BotSurgery.CheckAreaClearForSurgery` returns clearance without assigning `AreaClearForSurgery`, although native continuation and action execution read that property. The addon publishes the returned value (including false) through a cached private setter for its ready followers. No shared preset or ordinary SAIN bot policy is changed. All new hooks belong to the addon installer and participate in rollback/removal. The original native surgery flag is restored on combat release, opt-out, dismissal, shutdown and native-component replacement, without overwriting a different later value.

Validation: 627 production combat checks, 54 leadership/ownership checks, 49 compatibility checks, 38 proficiency checks and 13 replica comparisons pass. Tests exercise bounded probe work, slow-cadence completion, no-cover fallback suppression, shared route caching, bent-route walking, failed/reserved paths, real Harmony parameter bindings, core hard-cover geometry with controlled physics, native medicine prerequisites, clearance publication, retained push resumption and full hook cleanup. Unity navigation, actual healing and measured frame time still require raid qualification. See ADDON-ANALYSIS.md for deployment details.

### Current Debug deployment

Deployed 2026-09-16 18:06:49 +03:00 to `E:/SPTushanka/BepInEx/plugins/pitFireTeam`. The game client was closed. All five installed files match their build/source SHA-256 hashes. Backup and deployment manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-sain-performance-push-20260916-180352`.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `C0137EE4F02F9D178751083F1CF1EF25F7D5DDC50D7896B8D54B58813A5B5BCC` |
| pitFireTeam.pdb | `FE68A81A345131DC2F10E38100D0CC197336B2611598CCC5C93A9A5D44680703` |
| pitFireTeam.SAINAddon.dll | `2009D6922F757D7A6BB35978EC3F50A51ACD93D60B55E5874AE39324F377BE25` |
| pitFireTeam.SAINAddon.pdb | `D2ED978F8201C1D44570A328225A84040F967012F197537C17CE92B8ADBCE3A9` |
| SAIN-LICENSE.txt | `7047B3162F2663E5DCC062FB2AFF07E00E26DBA13BB19D722CF8A24D8466DC57` |

## SAINGrunt tactic display name (2026-09-16)

The addon tactic is displayed as **SAINGrunt** in the profile selector and follower Status Report. The persisted `SainMan` identifier, enum value and `ProfileTacticSainMan` localization key remain stable, so existing squads and pickup selection retain the same behavior without migration. The embedded English fallback and English language resource supply the new name. Historical/code references to SainMan below refer to this same tactic.

Validation/deployment: clean Debug core/addon build; English JSON and both UI lookup paths verified. Deployed on 2026-09-16 at 16:42 with all five binary/license hashes matching, plus the single live English label updated. Backup: `C:\Users\alexa\AppData\Local\Temp\pitFireTeam-before-saingrunt-name-20260916-164209`. The historical 16:42 DLL hash table below describes this rename build; earlier check counts are from the unchanged combat implementation.

## Addon-only SAIN hook ownership (2026-09-16)

Patches used only by the SAIN addon belong in `addon/`. `SAINAddonPatches` installs player-squad leadership, squad decisions, native decision-publication filtering, push-target preference and cover selection under the addon Harmony ID. Failed installation rolls back the complete addon hook set; shutdown releases follower state/membership before removing hooks. `SainManPersonality` also lives in the addon. Public SAIN APIs and enum types are referenced directly; cached reflection remains only for private setters/methods and internal action types.

Core retains compatibility needed without the addon: vision recovery/foliage, aim/recoil/proficiency, enemy synchronization, speech, friendly fire, grenade routing and native layer/weapon/reload guards. The mixed leader-assignment hook was split: core still prevents core followers becoming native AI leaders, while the addon owns human-led squad behavior. Core has no typed SAIN reference. `SainAddonBridge` carries passive leadership diagnostics and lifecycle notifications; shared core navigation/cover helpers remain in core. This supersedes older instructions placing all SAIN interception in core.

Validation: Debug core/addon build passed with zero warnings or errors; 569 production combat checks, 52 leadership/ownership checks, 13 replica comparisons, 49 compatibility checks and 32 proficiency checks passed. Compiled metadata confirms the moved integration types exist only in the addon and core has no typed SAIN/addon dependency. In-raid qualification remains required. Deployed the matching Debug core/addon DLLs and PDBs plus license on 2026-09-16 at 05:48; all five installed SHA-256 hashes match the build outputs. See ADDON-ANALYSIS.md for installed DLL hashes and backup location.

## Picked-up SAIN bots default to SainMan (2026-09-16)

The centralized `BossPlayers.AddBotFollower` pickup branch now selects SainMan when SAIN and the addon are installed and the recruited bot already has a native SAIN component. Detection reuses `BotFollowerPlayer`'s cached native accessor before follower construction/Init. Saved squadmates retain their configured tactic; non-SAIN recruits, missing plugins and failed detection retain Rifleman. Recruit aggression/proficiency rules and recruitment acceptance are unchanged. Actual addon activation still requires the existing per-follower layer/native/player readiness gates, with core fallback while unready.

Validation: the Debug core/addon build completed with zero warnings or errors, and all 565 addon checks passed. Included in the 2026-09-16 05:48 deployment recorded below.


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


Updated: 2026-09-15. Current branch: `1.0.0`. This is the current implementation ledger; the original investigation is retained in [the historical rework plan](docs/SAIN-Addon-Rework-Plan.md).

## Rejected automatic push yields to regroup (2026-09-15)

Raid `20260915-021113-Interchange.jsonl` showed automatic push stuck in `Assessing / hold.riskScore`: after reaching cover at 1031.368 and finishing its arrival hold at 1034.368, the follower stayed in SeekCover while player separation exceeded 83 m. Later cover reselection continued until commanded regroup at 1051.246. The objective coordinator previously considered regroup only for an Exhausted push, bypassing the normal fallback for rejected advancement.

Core Rifleman is the reference: `FollowerCombatRiflemanEngagement` returns Engage, Hold or Regroup, and settled-cover handling checks the boss objective before passive repositioning. The addon coordinator now lets an automatic Assessing push reach the existing `TryBeginAuto` checks using its resulting SeekCover decision. It does not mark the push exhausted or clear its retained target. Regroup owns movement once admitted; the push remains paused. Committed native movement, initial cover selection, arrival holds, compromised cover, visible contacts, medical/recovery work, player-path/distance gates, recent-fight grace, orders and On Your Own retain their existing protections. Ordered pushes and productive committed advances do not use this new handoff.

Twelve production regressions cover local hold, moving/assigned cover, arrival expiry and one-publication handoff, retained regroup ownership, exhausted cover selection, recovery, visible fire, incomplete player routes, independence and ordered intent. In-raid qualification is still required.

## Medical handoff deadline and status admission (2026-09-15)

Raid `20260915-004406-Interchange.jsonl` contains 1,681 events. Medved lost accepted combat at 707.604, selected FirstAid at 709.188 and used it from the 709.688 snapshot. From 719.211 through 733.319, repeated Surgery selections switched briefly out of linger and then back (mostly about 0.1 s later). The old handoff treated each medical selection as fresh combat, cleared the linger deadline and armed another three seconds when the selection disappeared. Core recovery did not start until 736.321. Native first aid then reentered the addon at 737.222 even though core recovery was already active. Core healing at 748.445 and 759.018 also appeared as addon Combat episodes despite the patrol layer owning the action.

The handoff now arms a single three-second deadline from accepted enemy loss. Medical selection can use the remaining window, but cancellation/reselection cannot extend it. Already-running native medicine may finish beyond that deadline; completion then releases immediately into core recovery. Released native medical decisions cannot reopen addon combat, including with a null native goal. The core publication bridge clears self-action along with a fully rejected solo/squad result, while native all-None resets remain untouched. Core-owned healing stays Released in addon recording. Renewed accepted enemy combat and urgent grenade avoidance retain their normal priority.

The recording also shows native SAIN contacts while `enemyCombatAllowed=false` in patrol (for example 766.856 onward). The ready SainMan marker provider now requires an accepted living EFT GoalEnemy **before** reporting any SAIN contact. A missing/dead EFT goal yields a handled empty report, including On Your Own, without erasing native perception or falling back to an EFT position. With a valid accepted goal, the marker still uses native selected enemy/last-known coordinates; exact identity matching is not required. Other followers' valid reports and non-SainMan marker behavior retain their existing handling.

## Current state

SainMan is an opt-in follower tactic when the external SAIN plugin and our addon are available. A ready SainMan follower uses two addon-owned replicas of SAIN 4.5.1 PMC combat: solo at priority 74 and squad at 75. Other tactics and absent/unready addon state retain core combat. The human player is the squad leader and tactical anchor; no substitute bot represents the player.

Aggression-based personality settings are implemented: **100% GigaChad, 70% Chad, 50% Normal, 30% Rat, 0% Coward**. The addon blends General/Search/Rush/Cover and tactical AggressionCoef between loaded-preset anchors; combat switches and native identity use the nearer anchor, with ties choosing the higher one. Speech, assignment and mechanical difficulty stay neutral; begging, fake death and taunting stay disabled. Combat HoldPosition applies temporary 0%, GoForward applies temporary 100%, and Gogogo restores saved aggression. Each follower owns a complete private settings copy. [SAIN personalities and aggression](docs/SAIN-Personalities-and-Aggression.md) records the implementation choices, source findings and qualification limits.

SainMan now has a follower objective coordinator and prudent push objective for accepted Go Forward and native-admitted automatic Search/rush. It owns the target, approach, arrival hold, recovery and bounded failure; SAIN actions execute each phase. See [the objective contract](docs/SAIN-Integration.md#follower-objectives-and-prudent-push-2026-09-14). Rifleman risk scoring and health/weapon conditions are now shared and adapted to SAIN knowledge.

## Implemented progress

| Area | Current implementation |
|---|---|
| Push risk assessment | [SAINFollowerPushAssessment](addon/SAINFollowerPushAssessment.cs), shared [FollowerPushRiskPolicy](client/BigBrain/FollowerPushRiskPolicy.cs) and passive [SainPushRiskBridge](client/Modules/SainPushRiskBridge.cs); automatic admission and ordered caution follow Rifleman conditions. |
| Push objectives | Ordered and automatic approaches share forward firing-cover selection, controlled walking, stationary arrival use, native recovery and retained target/failure state. The addon owns target preference interception; recorder includes `sainObjective` transitions. |
| Phase-one cleanup and tactic selection | Removed the obsolete one-layer addon and legacy general patches. SainMan selects both replicas through per-follower readiness; the saved selection survives fallback. Native internal actions are resolved/validated once. |
| Player leadership | Addon `SainPlayerSquadBridge` maintains real membership, human leader identity/liveness/distance and cleanup. `LeaderComponent` remains null because its type cannot represent a human. Ordinary squads retain native election. |
| Decision publication | Addon `SainSquadDecisionBridge` dispatches the follower's native squad-provider call to the addon and filters one already-calculated result for fallback. SAIN's native manager publishes decisions/events once. Handled None goes to native solo selection. |
| Accepted-goal entry | Ordinary solo/squad combat requires a core-accepted living EFT goal. On Your Own allows native investigation; existing patrol/requested independence is initialized on combat entry. Medical/urgent exceptions and living memory are preserved. |
| Status contact tracking | Ready SainMan reports native selected contact and last-known position through the passive `SainEnemyContact` bridge. Hidden movement is not revealed; forgetting/release removes the report, while other followers can retain the shared marker. Fresh native sight retains the red reticle. Core tactics and kill-marker timers are unchanged. |
| Full recovery handoff | Linger completion and explicit release begin core post-combat recovery; renewed combat cancels it. Native combat-healing selection remains unchanged and is diagnosed passively. |
| Solo and squad lifecycle | Both replicas retain native routing around explicit extensions. Native urgent-threat/flash reactions remain above them; core owns patrol and ordinary requests. Player-aware regroup/group-search actions use the real leader. |
| Post-combat linger | `SAINFollowerCombatHandoff` shares one three-second timer across both replicas. The dedicated linger action cancels inherited path/fire, keeps horizontal look and scans once before handoff. Renewed combat/urgent threats interrupt it; stale dead targets cannot restart it. |
| Personality | `SAINFollowerPersonality` blends combat settings into a private copy at five loaded-preset aggression anchors; core installs/restores them and refreshes native caches. Stable input does not reroll timers. Hold Position applies 0%, Go Forward applies 100%, Gogogo restores saved aggression. Speech/assignment/mechanics are excluded; proficiency and engagement attempts are preserved. |
| Proficiency | Core `FollowerSainEftCoreProjection` repairs missing finalized vision/scatter baselines; Vision, Precision and Reaction continue applying once. This works with or without the addon when external SAIN is installed. See [audit](docs/SAIN-Proficiency-Audit.md). |
| Command regroup | `RegroupNearBoss`, including tight Exit Located, is consumed once into `SAINFollowerRegroupObjective`. It survives temporary medical/urgent interruptions and the original command timeout. Normal arrival reuses core distances; tight combat arrival is 4 m. |
| Boss-oriented cover | Ordinary SeekCover prefers native-validated cover around the player, including a bounded player-area scan outside the native five-point pool. Shared core score/arrival holds; native urgent/independent fallback; no immediate regroup or ordinary movement reselection on arrival. |
| Automatic regroup | Considered after useful native choices: passive SeekCover with exhausted selection/stationary valid cover, or a failed bounded MoveToEngage attempt. Distance alone cannot interrupt productive combat, squad support, survival or committed movement. Existing path/floor/order/independence/recent-fight gates apply. |
| Regroup execution | Hot movement uses valid unspotted bossward SAIN cover or a complete spread/player path. Cooled movement runs with walk fallback. Owned targets/claims/path cleanup, arrival hysteresis, settle and retry timing prevent uncontrolled replacement. |
| Bounded engagement | `SAINFollowerMoveToEngageAction` commits one native firing position. Failure after 20 active seconds, six seconds without progress, two seconds at the point without a shot opportunity, or missing/rejected target/path is retained across action restarts, candidate replacement and regroup. Failed MoveToEngage yields cover or eligible auto regroup. |
| Engagement reset / independence | New enemy, last-known anchor moved at least 8 m, a visible shootable enemy, or combat release permits reset. Player movement does not. On Your Own retains unrestricted native engagement and suppresses automatic regroup. |
| Recorder | Schema 13 uses `SainCombatRecorderBridge` and `SAINFollowerRecorder` to open one addon combat episode across solo/squad/linger, record native decisions/action instances/attempt failures, and supply native enemy, cover and path data. Reads are passive, errors isolated, subscriptions cleaned up, and Debug recorder settings respected. |

[SAIN-Integration.md](docs/SAIN-Integration.md) is the detailed ownership and behavior contract. [SAIN-Addon-Phase1.md](docs/SAIN-Addon-Phase1.md) preserves the narrower completed checkpoint; its original no-custom-behavior scope does not erase later extensions.

## Implementation map

| Responsibility | Source |
|---|---|
| Plugin startup / native action resolution | [SAINAddonPlugin](addon/SAINAddonPlugin.cs), [SAINActionTypes](addon/SAINActionTypes.cs) |
| Solo / squad replicas | [SAINFollowerSoloCombatLayer](addon/SAINFollowerSoloCombatLayer.cs), [SAINFollowerSquadCombatLayer](addon/SAINFollowerSquadCombatLayer.cs) |
| Player-led squad decisions / search movement | [SAINFollowerSquadDecision](addon/SAINFollowerSquadDecision.cs), [SAINFollowerFollowSearchPartyAction](addon/SAINFollowerFollowSearchPartyAction.cs) |
| Status contact bridge / marker rendering | [SAINFollowerRuntime](addon/SAINFollowerRuntime.cs), [SainAddonBridge](client/Modules/SainAddonBridge.cs), [PingTeamates](client/Utils/PingTeamates.cs) |
| Push objective coordinator / execution | [SAINFollowerObjectives](addon/SAINFollowerObjectives.cs), [SAINFollowerPushObjective](addon/SAINFollowerPushObjective.cs), [SAINFollowerMoveToEngageAction](addon/SAINFollowerMoveToEngageAction.cs), [SAINFollowerPushHoldAction](addon/SAINFollowerPushHoldAction.cs), [FollowerPushGeometry](client/BigBrain/FollowerPushGeometry.cs) |
| Both-layer readiness / lifecycle | [SAINFollowerRuntime](addon/SAINFollowerRuntime.cs) |
| Linger / aggregate combat handoff | [SAINFollowerCombatHandoff](addon/SAINFollowerCombatHandoff.cs), [SAINFollowerLingerAction](addon/SAINFollowerLingerAction.cs) |
| Regroup policy / action / shared geometry | [SAINFollowerRegroupObjective](addon/SAINFollowerRegroupObjective.cs), [SAINFollowerSquadRegroupAction](addon/SAINFollowerSquadRegroupAction.cs), [SainRegroupBridge](client/Modules/SainRegroupBridge.cs) |
| Bounded firing-position engagement | [SAINFollowerEngageAttempt](addon/SAINFollowerEngageAttempt.cs), [SAINFollowerMoveToEngageAction](addon/SAINFollowerMoveToEngageAction.cs) |
| Recorder | [SAINFollowerRecorder](addon/SAINFollowerRecorder.cs), [SainCombatRecorderBridge](client/Modules/SainCombatRecorderBridge.cs), [BattleRecorder](client/Modules/BattleRecorder.cs) |
| Cover policy / discovery | [SAINFollowerCover](addon/SAINFollowerCover.cs), [SAINFollowerCoverFinder](addon/SAINFollowerCoverFinder.cs), [SainCoverSelectionBridge](addon/SainCoverSelectionBridge.cs) |
| Addon native integration / core event contract | [SainPlayerSquadBridge](addon/SainPlayerSquadBridge.cs), [SainSquadDecisionBridge](addon/SainSquadDecisionBridge.cs), [SainAddonBridge](client/Modules/SainAddonBridge.cs) |
| Aggression policy / settings interpolation | [SAINFollowerPersonality](addon/SAINFollowerPersonality.cs) |
| Native personality setup / proficiency | [SainManPersonality](addon/SainManPersonality.cs), [FollowerSainProficiency](client/Modules/FollowerSainProficiency.cs), [FollowerSainEftCoreProjection](client/Modules/FollowerSainEftCoreProjection.cs) |

## Validation and deployed checkpoint

Latest completed code validation on 2026-09-16:

- `tests/Verify-SainAddonCombat.ps1`: **569 production combat/personality/cover/handoff/status-marker/objective/risk checks**. The fixture uses real SAIN.Preset.Shared 4.5.1 settings/enums and Unity's netstandard facade, controlled game/native instances, and real Harmony publication boundaries. Covers every scoped combat anchor setting, excluded speech/assignment/mechanics, accepted Go Forward dispatch and core fallback, all interpolation segments, discrete boundaries, temporary overrides, native caches, preset/component replacement, rollback/restoration, follower isolation, recorder serialization and retained engagement failure, alongside the existing combat extensions. Investigation/recovery regressions cover rejected native-only contacts, both independence modes, command preservation, medical/urgent exceptions, lifecycle cleanup and passive non-goal medical context. Cover regressions exercise the production selector/finder with controlled native geometry, the real Harmony bridge, bounded discovery, boss preference, native fallback, arrivals, orders, invalidation and destination ownership.
- Twelve raid-derived checks cover the fixed medical handoff deadline, treatment completion after expiry, rejected self-action publication, core-owned recovery recording, renewed combat and missing/dead accepted goals for markers.
- The 34 risk additions cover shared threat results across 300 combinations, equipment/role/cluster influences, player pull, pending treatment, badly injured health, active weapon and ammunition gates, cautious ordered cover, risk holds, commitment preservation and urgent recovery.
- The 56 push additions cover objective command capture and replacement, native target preference/publication, target binding, cover-first approach and provisional upgrades, stationary arrival action, native firing/urgent/medical priorities, paused execution budgets, failed-attempt retention, different-target regroup safety, independence, cleanup and shared core geometry.
- The 19 marker checks execute the production contact provider and extracted production marker synchronization/resolution/refresh methods. They cover stale EFT selection, hidden movement, fresh knowledge, visible-to-hidden transitions, forgetting/cleared places, target release/death, invalid positions, shared followers, tactic opt-out and core/unready fallback. UI rendering itself still needs raid verification.
- Installed SAIN metadata validates **11 resolved native action constructors**, the decision publisher, native target-selection result boundary, cover selection method/sprint field, personality setters/timers, cached talk refresh and SearchAction sprint fields.
- `tests/Verify-SainPlayerLeadership.ps1`: **52 leadership and patch-ownership checks** passed, including addon removal, failed-install rollback and standalone core guard behavior.
- `tests/Verify-SainCompatibility.ps1`: **49 shared compatibility checks** passed.
- `tests/Verify-SainReplicaParity.ps1`: **13 source-parity comparisons** passed.
- `tests/Verify-SainProficiency.ps1`: **32 production proficiency checks** passed. No mechanical-proficiency policy was changed.
- Matching Debug core/addon build: **zero warnings and errors**. Whitespace checks passed.
- Both DLLs, PDBs and `SAIN-LICENSE.txt` deployed to the live plugin folder from `LOCAL.md`; all five source/destination SHA-256 hashes matched.

| Historical DLL (2026-09-16 16:42) | SHA-256 |
|---|---|
| pitFireTeam.dll | `4353A14D771475A79259A6BB4AA9CB90E7B1AA5F82C56B570FE7BCD8142C704A` |
| pitFireTeam.SAINAddon.dll | `F31EA52D7DEFD6AD1740336DBEAC67459635D2B58AC567DC01CCDB8ED9813914` |

These binaries include addon-owned SAIN hooks with typed references, automatic SainMan selection for picked-up SAIN bots, the post-regroup cover-area constraint and quiet publication handoff, follower body-first SAIN aiming, core shared SAIN vision-loop recovery, aggression-interpolated personality settings, boss-oriented cover selection/arrival use, accepted-goal combat entry, core recovery handoff with a fixed medical linger deadline, passive medical diagnostics, accepted-goal-gated native-knowledge Status Report markers and the follower push objective with Rifleman risk assessment and automatic regroup from rejected advancement. A restart/new raid is required. Unity movement/presentation, temporary-command behavior and the complete in-raid lifecycle still need qualification.

Deployment backup: `C:\Users\alexa\AppData\Local\Temp\pitFireTeam-before-addon-ownership-20260916-054848`. Matching Debug core/addon DLLs and PDBs plus license were deployed at 05:48 on 2026-09-16, with all five SHA-256 hashes verified. Validation passed 569 combat, 52 leadership/ownership, 13 parity, 49 compatibility and 32 proficiency checks.

## Raid evidence and limits

`20260914-042148-Interchange.jsonl` is complete (3,355 events). At raid time 898.547 the addon entered SeekCover and then Search with native contacts but no EFT goal; the first accepted EFT goal in that episode appears near 929.98. Attention cleared the remaining enemy state and released Search at 993.788. This motivated the accepted-goal gate.

The same raid records manual Force Heal at 923.212 and 981.664 in `LogOutput.log`. Automatic native first aid did execute around 940.64–948.46 and 953.91–961.72, with remaining damage and surgical work afterward. The addon never enabled `postCombatFullHealActive`, and its release paths lacked the existing full-recovery bridge call; that omission is repaired. The separate combat delay cannot be assigned to a particular non-goal threat, item, or treatment failure from the old record. New medical inputs improve that evidence without changing combat-heal policy. The earlier suspected friendly fire also remains unconfirmed; this change does not alter suppression safety.

`20260914-033024-Shoreline.jsonl` captured Medved's native SAIN actions and death (836 events). At 250.889 he was 33.4m from the player but selected cover 68.8m from the player, walked about 47m along its route, then activated automatic regroup at 262.379 with `passiveCoverHold`. This motivated the boss-cover/arrival-use extension. The record does not prove that suitable player-area cover existed; the new events expose the selection policy for subsequent testing.

The same recording exposed two separate findings outside this cover change: the Go Forward aggression override persisted into a later fight, with patrol readiness repeatedly postponing its clear deadline, and native first aid continued after the decision changed to Dog Fight. The inspected SAIN `TryCancelHeal` has commented-out cancellation calls. Neither issue is fixed by the cover work; neither proves that the recorded headshot death was preventable.

The completed `20260913-225814-Shoreline.jsonl` contains 139 events, including one follower death, but no normal combat snapshots/decisionSelected/combatStart events. That recorder version did not recognize addon combat episodes. It cannot retrospectively prove the reported MoveToEngage rearming loop.

Medved used SainMan. Commanded regroup began around raid time 260.767 and ended at 269.323 before linger. One recorded automatic regroup ran from 346.069 to 350.122. At 382.308 the cultist priest killed him with a chest shot; the death snapshot reports addon SoloCombat / SeekCover, with the player about 28.5 m away. This does not establish that regroup or MoveToEngage caused the death. Schema 13 now supplies the previously absent native decision/action/path sequence for the next recording.

## Planned Enemy Tracking setting

The documentation-only [Enemy Tracking investigation](docs/Enemy-Tracking.md) maps native SAIN tracking and core behavior for a future `Simple / Realistic` setting independent of tactic. It includes core Realistic and SAIN Simple plans, retention conflicts, search completion versus forgetting, native duration-recalculation risks and validation scenarios. No setting or runtime behavior has been added for this plan.

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

## Next session / remaining scope

- Qualify ordered and automatic push: forward-cover choices, provisional walking, arrival holds, pressure recovery, temporary threat interruption, failed approach/regroup, replacement orders and On Your Own.
- Qualify aggression interpolation and temporary override transitions in a fresh raid using [the implementation contract](docs/SAIN-Personalities-and-Aggression.md).
- Qualify the latest recorder, engagement and regroup changes in a fresh raid; do not claim fixture success proves navigation or presentation.
- Other custom combat command translations remain deferred. Saved/temporary aggression now drives personality settings; the former hold/protection addon objective is not present.
- Keep general external-SAIN compatibility in core, addon-only hooks in the addon, no shared preset mutation, and preserve follower proficiency, enemy memory, player leadership and safe fallback.
- The phase-one WIP checkpoint is `60d725bd60050a7c02ebfaac3ef73772468d2f8e`. This later WIP checkpoint includes the extensions above; unrelated insurance/client/server work remains excluded.

See [reference provenance](addon/SAIN-REFERENCES.md) for private 4.5.1 assembly references and source limits. Copied SAIN code retains the upstream MIT attribution in `addon/SAIN-LICENSE.txt`.
