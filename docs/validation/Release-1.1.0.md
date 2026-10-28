# Core 1.1.0 validation

## 2026-10-28 — pending Goons updates and Birdeye test roster deployed

After the user finished testing and authorized deployment, all **6** pending Core client/Going Rogue client/server Debug DLL/PDB files were backed up, deployed and hash verified with Tarkov/SPT stopped. Includes the Birdeye regroup correction, normal Big Pipe kit generation and Birdeye's one-time test-roster seed. **469** storage checks and server metadata validation passed; Core server output already matched installed. Offline inspection confirmed no Birdeye seed receipt in the active Going Rogue database; automatic generation occurs at next roster load. No game/server process was launched. [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-pending-goons-updates-and-birdeye-test-roster--deployed) records manifest, hashes and live qualification boundaries. This supersedes pending deployment entries below.

## 2026-10-28 — Birdeye positioning/regroup correction built; deployment deferred

Core's optional Marksman positioning callback now defers when the existing distance/recent-fight policy calls for regroup. Ordinary Marksman routing without a provider and protected combat work retain their behavior. The fixture reproduced the failure before the correction, then passed **26** routing checks; **661** Goons, **98** arrival and **30** weapon-retention checks passed. Debug Core/Going Rogue client build was clean; **549** API references/**10** patch targets resolved. No deployment or profile/process changes during the user's testing hold. [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-birdeye-position-search-respects-regroup--built-deployment-deferred) owns the detailed evidence, staged hashes and pending live qualification.

## 2026-10-28 — Big Pipe forced launcher removal built; deployment deferred

Removed Going Rogue's temporary forced-M32 generator/stock-template/APBS-bypass hooks and their test-only fixture. Normal native/SPT or installed APBS equipment generation owns fresh Big Pipe raid kits again. Client launcher behavior/reload, role profiles and last-kit preview behavior are unchanged. Debug build passed without warnings/errors; **466** remaining gameplay storage checks passed, with **28** obsolete override checks removed. SPT 4.1.6 addon metadata resolved **21 types / 33 members**, and the removed override type is absent from the built assembly. The user is still testing and explicitly deferred deployment; no installed files/processes changed. [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-big-pipe-forced-launcher-test-removed--built-user-deferred-deployment) records staged server-only script, hashes and remaining fresh-raid qualification.

## 2026-10-28 — live encounter admission limit and 2x Fence curve deployed

The user clarified that deaths must preserve existing friendships. Implemented an admission-only live limit from Friendly Encounter Chance: separate ordinary-Scav and solo-PMC pools, whole-number floor, a two-slot minimum opportunity for solo PMCs, and no forced successful rolls. Existing selected/calmed actors can remain over a reduced limit after deaths; subsequent spawns cannot gain friendliness until there is room. Quota-rejected actors retain their one-time decision. Living world counts exclude followers and special roles, combine selected/calmed Scavs without double counting, and allow the currently activating actor before alive-list publication. Both normal selection and recruited-Scav calming use the same check. Calming waits for an authored group to complete, admits its full size, and locks accepted groups against later native wave reuse. No new per-frame loop, diplomacy rollback or death-triggered betrayal was added.

Fence retains its original multiplier from reputation 0 to 1; above 1, `1 + (standing - 1) / 5` reaches **2x** at 6. At the user's 15% setting and 4.89 reputation, the Scav roll is approximately **20.0025%** before penalties; the population percentage remains **15%**. Embedded English and English/Chinese/Russian setting descriptions explain both controls and the death behavior. Server DLLs/database data were unchanged.

Debug build passed with **0 warnings/errors**. Passed **207** production capacity arithmetic checks, **30** production world/population checks, **5,949** production hostility/selection/diplomacy checks (including the real counter wired into selection/calming), **203** encounter penalty checks and **190** gameplay configuration checks. The latter intentionally simulates a failed server synchronization before PASS. Embedded English exactly matches the server fallback; all localized JSON files parse. Installed Goons API validation resolved **549** references and **10** patch targets; SPT 4.1.6 Core/client compatibility and extracted Rifleman planner parity passed. Fixtures use controlled EFT world/relationships and do not prove raid spawn ordering or native group reuse.

Guarded Debug deployment backed up and SHA-256 verified **7** files: Core/Going Rogue client DLL/PDB plus three localized server language resources. Both Tarkov/SPT were stopped; no process was started or killed. Script: `tests/artifacts/Deploy-EncounterPopulationFence.ps1`; manifest: `tests/artifacts/encounter-population-fence-deploy-6eae7a0f263e4cf996496ab6a9f099db/manifest.json`. Installed DLL hashes: Core `B084A6D10513D1C9A24A475FA7909A27486408CB034CBA41C10312A6E2A5C048`, Going Rogue `C59D12E7BB0CDDE823DB0BA7883095A170EFB7F0922F81447DC9774B26F3E09F`. Manifest retains PDB/resource/previous hashes. Pending live checks: initial/wave spawn sequencing, mixed faction solo-PMC cap, calming groups, dead/despawned bot counts and preservation of excess existing friendships.

## 2026-10-28 — boss-kill exemption/profile repair and pending combat work deployed

Core excludes registered Goon allies from traitor kill classification, and the server checks addon companion `IgnoreEncounterKillPenalty` against the stored victim role before accepting stale reports. Ordinary recruits retain their penalty. The active user's sole Big Pipe test-kill ledger entry was removed through the offline Database Tool with automatic encrypted backup and re-export verification; other documents were retained. Knight trader consequences remain roadmap-only. Friendly encounter rolls still have no number/fraction quota; none was requested for implementation after the user's clarification.

Debug builds passed; **203** client penalty, **494** gameplay storage, **661** Goons combat, **5,930** hostility, **19** companion-routing and **30** weapon-retention checks passed. Installed addon APIs (**549** references/**10** patch targets) and SPT 4.1.6 Core/client/Going Rogue server checks passed. After automatic review initially preserved the user's testing hold, the user explicitly approved **Deploy all pending updates now**. All eight Core/Going Rogue client/server Debug DLL/PDB files were backed up, copied with both processes stopped and hash verified. This includes Birdeye and the idle no-cover regroup fix recorded below. No processes were launched or killed. [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-boss-kill-exemption-pipe-ledger-repair-and-combined-deployment--deployed) records exact hashes, repair backup/evidence and deployment manifest. Live combat/navigation and next-raid presentation remain qualification.

## 2026-10-28 — Shoreline idle Goons regroup fix staged

The first fight in `20261028-012939-Shoreline.jsonl` showed 11 repeated no-cover threat holds per Goon while the player advanced beyond 100m, with no productive visible/shootable contact or active movement. Going Rogue now reuses Core's normal regroup objective/distance/recent-fight gates at fresh ordinary decisions, after protected work, rather than perpetually renewing recovery. Enemy memory and explicit player order ownership remain unchanged. Core production source is unchanged by this correction.

Scav investigation found five of eight eligible encounter selections friendly at 46.76%; Goons were already excluded from ordinary Scav-companion calming. Added explicit all-Goon/presentation-side isolation checks; friendship probability is unchanged. Debug build passed; **661** Goons, **44** Regroup, **106** coordination, **5,930** hostility checks and **549** addon API references/**10** patch targets passed. [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-shoreline-first-fight-idle-recovery-and-scav-audit--staged) records evidence, current staged hash and the combined Birdeye deployment script. Deployment remains deferred at the user's request while testing.

## 2026-10-28 — Birdeye brain built; user deferred deployment

Core Marksman gains one optional fresh-position callback, retaining the original constructor and ordinary behavior. Going Rogue supplies Birdeye's native sniper distance preference and Knight-assigned firing positions through that boundary; the existing Marksman implementation owns secondary preparation/retention, rifle return, commands, recovery and arrival. The addon scopes runtime Marksman initialization before proficiency-template capture and leases both native sniper combat publishers. Server Birdeye readiness is enabled; automatic test seeding remains disabled.

Debug builds passed with no warnings/errors. Relevant production checks passed: **589** combat/lifecycle, **106** coordination, **99** advance orders, **44** Regroup, **19** callback routing, **30** weapon retention, **256** Marksman preparation/engagement, **98** arrival, **47** ordered-sniper arrival, **487** storage. Installed addon API validation resolved **536** references and **10** patch boundaries; Core/client/Going Rogue server checks passed against SPT 4.1.6. Native raid behavior remains unqualified. The user is still testing and explicitly deferred deployment. Staged script/hashes are in [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-birdeye-marksmansniper-brain--built-deployment-deferred); no installed files/processes changed.

## 2026-10-28 — Knight push leadership deployed

Core adds an optional admitted advance-order context with one sequence per broadcast and the existing confidence evaluator; Going Rogue owns shared Knight verdicts, local Pipe delegation and latched authority. Exact-point planning stays non-publishing until a safe admitted-target successor is prepared. Explicit leadership reassignment can end an ordered-push mission without clearing living contact memory or resuming the superseded push. Need Help and Regroup retain priority.

Debug build passed without warnings/errors. Production fixtures passed: combat/handoff **551**, coordination **106**, advance dispatch **99**, target transition/rollback **28**, Regroup/recovery **44**, Marksman arrival **98**, peaceful launcher reload **32**, gameplay modes **190**. Installed addon metadata resolved **523** API references and **9** patch targets; SPT 4.1.6 compatibility check passed. Current Core/Going Rogue client DLL/PDB files were backed up, deployed with Tarkov stopped and hash verified. Server unchanged. Manifest and exact DLL hashes are in [Going Rogue validation](../../going-rogue/Validation.md#2026-10-28-shared-knight-push-leadership-and-player-help-priority--deployed). Native combat, navigation and hands timing remain raid qualification.

## 2026-10-27 — safe secondary-launcher patrol reload deployed

The existing patrol maintenance queue now admits owned reusable secondary launchers after first-primary maintenance, using shared native loose-ammo reload and queued rifle return after hands completion. Post-combat launcher fallback cannot fight the queue's temporary draw/reload. Threats, pressure/recent hits, explosive danger, medicine, player commands and inspection defer the optional work; unsafe pending draws return to the rifle without cancelling actual reloads. Single-use and acquired-weapon rules remain conservative.

Debug build, **32** new patrol queue/safety/priority checks and existing launcher/magazine/reload-start/stance regressions pass. Installed addon/reload APIs resolve. Deployed and hash-verified four matching client DLL/PDB files with stopped-client checks and backups: ignored `tests/artifacts/patrol-launcher-reload-deploy-b926d6d5a25d487990cfce5a847bef65/manifest.json`. No server/data changes or process launches. [Going Rogue evidence](../../going-rogue/Validation.md) retains hashes and the pending real-raid peaceful-cylinder reload check.

## 2026-10-27 — Big Pipe launcher test APBS compatibility deployed

The installed launcher-test addon was present, but APBS replaces stock inventory generation with separate role/tier weapon/chance selection. Going Rogue now permits stock inventory generation only for saved Big Pipe's temporary launcher-test scope and validates an equipped M32A1 with loaded compatible ammunition before returning the kit. Other APBS actors/settings are unchanged; Core has no code change in this correction.

Debug server addon build and **487** gameplay storage checks pass, including actual installed APBS prefix/Harmony and final-kit rejection cases. Deployed only two Going Rogue server DLL/PDB files after both processes stopped, with backups and matching hashes: ignored `tests/artifacts/bigpipe-launcher-apbs-deploy-3154d03f732044d894ca28b0d8c2adc5/manifest.json`. [Going Rogue evidence](../../going-rogue/Validation.md) retains diagnosis, hashes and pending fresh-raid qualification. No server/game launch or profile/data/config edits.

## 2026-10-27 — Big Pipe willingness and Knight coordination deployed

Independent Big Pipe always accepts a valid admitted Go Forward order; a living, active Knight in the local squad restores Knight's existing confidence policy. The addon now routes actual native Knight firing-position assignments and managed member recalculations to shared exact-candidate firing-position admission. Initial seed positions, different targets, foreign followers and player commands cannot act as valid Knight assignments. Urgent work and existing commitments retain priority. Birdeye's shared coordination is prepared; his raid lock/sniper push restriction remain.

Debug build, **483** combat, **187** gameplay config, **92** command, **25** coordinator/presence and **98** firing-position/arrival checks pass, plus installed compatibility/API checks. Deployed and hash-verified four client DLL/PDB files with stopped-client guards and backups: ignored `tests/artifacts/goon-leadership-deploy-e3b1e200421e483c86c22da2ac05a0c0/manifest.json`. No server/data changes or process launches. [Going Rogue evidence](../../going-rogue/Validation.md) retains source comparison, hashes and live-raid qualification limits.

## 2026-10-27 — queued Going Rogue changes deployed

With Tarkov and the SPT server stopped, deployed and SHA-256 verified six Debug DLL/PDB files covering native boss trash talk, Big Pipe's guaranteed launcher test kit and friendly Scav encounters alongside Goons with no forced Bad Guy. PMC encounters remain disabled during Rogue context. Backups and exact hashes: ignored `tests/artifacts/goingrogue-pending-deploy-33c016e3825e41ceb0f4c258190b5eb3/manifest.json`. No server/game launch, profile edits or database changes. The user's eventual exUsec/quest progression remains an explicitly unimplemented [roadmap](../../going-rogue/Roadmap.md). Earlier entries retain build/fixture results; live raid qualification remains open.

## 2026-10-27 — Going Rogue Scav encounter diplomacy built; deployment pending

Going Rogue no longer forces Bad Guy at setup, setting/flag resolution or transit. The shared encounter service now allows eligible Scavs alongside allied Goons and restores recruited-Scav diplomacy; PMC encounters remain disabled during Rogue context, as explicitly requested. Existing chance/Fence, checkbox, solo/group, neutrality and actual-aggression rules remain authoritative. Legacy behavior outside Going Rogue is unchanged.

Debug Core/Going Rogue client build, **5,912** hostility, **184** gameplay config, **470** combat, speech/recruitment and installed addon API checks pass. The user has deferred deployment while testing. The latest combined guarded queue is ignored `tests/artifacts/Deploy-GoingRoguePending.ps1`, covering this change, native boss chatter and the server launcher's test kit; it supersedes earlier pending deployment scripts. [Going Rogue evidence](../../going-rogue/Validation.md) retains hashes and qualification limits. No installed files or running processes changed.

## 2026-10-27 — native allied-boss trash talk built; deployment pending

Core's `FollowerTalkFrequencyGate` now exempts the centralized native Goon roles (Knight, Big Pipe and Birdeye) before its percentage roll. Native EFT talk timing, command silence and forced-response priority remain authoritative; ordinary follower frequency behavior is unchanged. Debug client build passes with zero warnings/errors. Existing recruitment/speech/greeting checks pass, including 34 actual-Harmony speech checks; these do not establish live boss chatter timing.

Tarkov was running (PID 6872); the user explicitly deferred deployment while testing. Prepared guarded two-file Core deployment in ignored `tests/artifacts/Deploy-BossNativeSpeech.ps1`. Built DLL SHA-256: `B0D4E05A3A870BF2704FAFFAD4ACDC3BF2C3BE62434FB2322992023E2DF49773`; PDB: `748EA388DD3333C9E31BDDFEFCA7EBF8CF01F0D7B7C828E6DC7B8586B638B3F2`. No installed files or running processes changed.

## 2026-10-27 — final-fight cover/dogfight/suppression corrections deployed

The full final fight in the earlier `20261027-172726-bigmap` record shows five no-op recommits of cover 22820, arrival-hold visibility/pressure self-restarts, dogfight movement facing away from a lost close contact, and suppression ending for reload without a real successor. Fixed the owning boundaries: already-arrived firing-cover candidates do not generate another move; Goons early hold interrupts prepare real progress; dogfight requires credible contact/bearing, stops old motion on release and protects facing through 18m; suppression requires a different prepared reload/retreat successor. Existing firing, medical and target-memory contracts remain intact.

The repeated native stale-contact wait now occurs once per unchanged enemy/report before a memory-search handoff. Debug build, **470** combat, **99** facing/movement, **56** rifle suppression, launcher/medical/command/cover-fire regressions, installed compatibility and **484** addon API references/eight patch targets pass. Four matching client files were backed up/deployed/hash-verified; no server launch or data edits. [Going Rogue validation](../../going-rogue/Validation.md) retains exact times, source ownership, final manifest and fresh-raid qualification. This analysis used the earlier raid and does not claim it ran these corrections.

## 2026-10-27 — boss-relative PMC/Scav push confidence deployed

Knight/Big Pipe now include their boss advantage in both autonomous approach assessment and Go Forward confidence. The addon weights own equipment power **1.5x versus PMCs** and **2x versus ordinary Scavs** for comparison only. Native item power/stats/health remain unchanged. Elite/unknown roles remain neutral; numbers, equipment mismatch, pressure, tracked contact and weapon/survival gates still apply. The generic Core policy retains its original constructor and neutral defaults.

Debug build, **441** combat, **88** command, **184** gameplay configuration, **81** medical and **44** Regroup checks pass, along with installed compatibility and **483** addon API references/eight targets. Deployed four matching client DLL/PDB files with stopped-target guards, backups and matching hashes; no server changes or background instances. [Going Rogue validation](../../going-rogue/Validation.md) owns the exact policy, manifest and remaining raid tuning qualification.

## 2026-10-27 — Goons recovery-hold hesitation fix deployed

Latest Big Pipe record confirms successful spawn but **880** recovery-hold restarts and a later arrival hold active **37.29s** past its committed deadline. His empty rifle also waited **9.34s** before recorded reload activity. Going Rogue now uses Core's existing exact committed-position end handler for these holds, retaining arrival grace and finite expiry instead of generic cover-flag endings. This change does not adjust aggression/confidence or firing safety.

**326** Knight/Big Pipe, **81** medical, **44** Regroup and **28** prepared cover-fire checks pass. Debug build and **469** addon runtime API references/eight patch targets pass. With Tarkov closed, deployed four matching client DLL/PDB files with backups and matching hashes; the user-run server and all data were untouched. [Going Rogue validation](../../going-rogue/Validation.md) retains the record times, manifest and pending corrected-raid qualification. No background server launch.

## 2026-10-27 — Big Pipe human-follow fix deployed

User clarified automatic deployment as part of completed fixes. With Tarkov closed, deployed four matching Core/Going Rogue client DLL/PDB files with backups and SHA-256 verification; the existing SPT server and its files were untouched. No server/background instance was started. Debug build, **48** native activation regression checks, installed signatures, addon metadata and support parity pass. Exact hashes/manifest and pending corrected-raid qualification are in [Going Rogue validation](../../going-rogue/Validation.md).

## 2026-10-27 — Big Pipe native human-follow activation fix, no deployment

The real spawn log shows Big Pipe is created but registration throws in native group patrol selection for a human leader. Core now retains the common native `SetToFollow` path and follower mode while selecting Knight's ordinary chooser for a managed local-human group request. Native AI-led Goons retain group patrol. A separate scoped SPT activation guard preserves pre-marked/registered Goons' native role/brain instead of applying a PMC-only brain table based on their USEC/BEAR side; ordinary PMCs remain native. These are Core lifecycle/compatibility fixes and do not change the addon combat selector, profile side, role or spawn timeout.

**48** native-source/actual-Harmony activation checks reproduce the old errors and validate the corrected boundary and exclusions. **47** FIR, **16** rollback and **290** combat checks pass. Debug clients build with zero warnings/errors; installed compatibility, **467** addon API references/eight patch targets and support parity pass. [Going Rogue validation](../../going-rogue/Validation.md) records the exact log evidence and live qualification limits. No deployment or background server startup.

## 2026-10-27 — Big Pipe squad addition and deployment

With the user-requested SPT shutdown complete and Tarkov stopped, deployed matching Core/Going Rogue Debug DLLs and symbols: eight files backed up and SHA-256 verified. Through the deployed server's normal native-generation/seed path, added Big Pipe to pitAlex's Going Rogue roster (account **1433419**, native `followerBigPipe`, level **79**, portrait **#C9A044**, raid-kit/generator eligible). Knight remains present. Repeated loading and a full server restart confirm the same single Big Pipe entry persists. The server is running again for the user's newly opened game.

Exact hashes, startup/request evidence, backups and qualification limits are in [Going Rogue validation](../../going-rogue/Validation.md). This is deployment and real roster/persistence evidence, not in-raid combat qualification. No deployment copied assemblies over an active game/server.

## 2026-10-27 — Big Pipe combat and shared Goons ownership, no deployment

Going Rogue now supplies Big Pipe's native-style combat selector, planned launcher opportunities, command hooks and one-time test-companion definition. Knight and Big Pipe share addon-owned eligibility/readiness/layer suspension, reload/medical/commitment and support handling; role selectors remain separate. No new Core combat policy or typed addon dependency was added. The addon extends the existing fixed-kit restrictions to all three Goons; Birdeye remains raid-locked. Native activation's unplanned launcher draw uses Core's existing safe primary fallback.

Debug client/server builds pass with zero warnings/errors. **290** production Knight/Big Pipe objective/selector/ownership checks, **459** storage and **181** gameplay configuration checks pass, alongside medical retry, combat command, launcher, donation and loot boundary checks. Installed SPT 4.1.6 compatibility, **467** addon API references/eight patch targets and Rifleman support parity pass. See [Going Rogue validation](../../going-rogue/Validation.md) for exact checks and remaining live raid qualification. No deployment, live data changes, release archive or commit.

## 2026-10-27 — combat point/push acceptance, no deployment

Core combat There now shares ordinary recruit Go Forward acceptance while retaining its pointed destination; optional companion command policy lets the three Goons always accept valid combat point moves. Going Rogue owns Knight/Big Pipe's equipment/known-threat confidence calculation; level/personality are excluded. Birdeye and ordinary snipers explicitly refuse combat Go Forward. Knight's missing accepted-push handoff is wired to Core ordered push. Registration replacement/unregister clears the policy; the original registration signature and ordinary ownership remain intact. Big Pipe/Birdeye brains remain raid-locked.

**72** production advance-command dispatch/policy, **175** Knight, **181** gameplay provider/configuration, **332** peaceful/combat acceptance, **44** Regroup and **24** native suppression-reply checks pass. Core/Going Rogue Debug clients build with zero warnings/errors; Core SPT 4.1.6 compatibility, **444** addon API references/seven patch targets and Rifleman support parity pass. See [Going Rogue validation](../../going-rogue/Validation.md) for scope and qualification. No deployment/server/data edits; Unity execution remains pending.

## 2026-10-27 — suppression replies and passive dispatch diagnostics, no deployment

Explicit suppression acceptance/refusal now bypasses ambient group-talk delay, requests native Covering/Negative and pairs it with an OK/No gesture. Native actor speech/silence scheduling remains authoritative. Busy looters can refuse without changing their task. Passive Debug receipt, debounce, candidate exclusion and response-request events distinguish dispatch from accepted command state without re-running combat/medical providers. The earlier silent Knight attempt cannot be conclusively reconstructed: the four available earlier records have no accepted SuppressEnemy command and lacked these rejection/reply diagnostics. See [Going Rogue validation](../../going-rogue/Validation.md).

**24** production reply/native speech checks reproduce the former group-delay loss and verify the new request/gesture path; **51** rifle suppression, **30** launcher execution and **44** Regroup router checks pass. Core/Going Rogue Debug clients build with zero warnings/errors; Core SPT 4.1.6 compatibility, **441** addon API references/seven patch targets and Rifleman support parity pass. Not deployed; actual audio/gesture playback remains pending.

## 2026-10-27 — recovery hold Regroup handoff, no deployment

Knight's latest raid confirmed two received Regroup orders trapped behind a committed `noEnemyThreatCover` arrival hold after real contact returned. Core now applies that special early end route only while there is no active admitted combat enemy; recovered contact restores normal objective/command interrupts. This shared fix leaves actual missing-enemy survival, medical/firing/point-blank gates and regroup geometry intact. **44** production router checks pass, and the pre-fix tracked router reproduces the failure. **172** Knight, **81** medical retry and **47** heal-retreat checks pass alongside a zero-warning/error Debug client build, Core SPT 4.1.6 compatibility and **441** addon API references/seven patch targets. See [Going Rogue validation](../../going-rogue/Validation.md) for the recorded command times and reproduction. Not deployed; Unity movement remains pending.

## 2026-10-27 — medical retry and Going Rogue fixes deployed

User authorized the matching Core/Going Rogue Debug client/server deployment. Both builds had zero warnings/errors; CIM confirmed Tarkov/SPT stopped before deployment and before each copy. All eight DLL/PDB files were backed up and independently SHA-256 verified after installation. Server dependency manifests already matched. Backup/manifest: `tests/artifacts/goingrogue-loot-medical-deploy-dc0e721a71714c36bd40bb38645f73aa/manifest.json`. The deployment includes shared Rifleman/Marksman/Knight medical retry recovery, boss native inventory protection/no returns, Knight caution, body/container refusal and grenade resupply. See [Going Rogue validation](../../going-rogue/Validation.md) for installed DLL hashes and scope. No live raid confirmation or database/settings edits.

## 2026-10-27 — shared medical retry and boss inventory policy, no deployment

Rifleman, Marksman and Going Rogue Knight now share `TryGetMedicalRetryRecoveryDecision`. A failed due medical search may relocate an exposed actor even when that provider has rearmed its retry cooldown and incoming-fire pressure has faded. Each owning handler probes medicine at most once per evaluation; active treatment and reached safe medical cover retain control. **81** extracted production handler/helper checks, **47** heal-retreat checks, **40** medical-progress/retry/native blocked-grenade checks and **25** post-combat lifecycle checks pass. SAIN addon medicine remains separately owned and does not use the Core hold reason.

Core also accepts an optional companion `ProtectSpawnInventory` policy. Going Rogue applies it to native Goon inventories in every loadout mode with an ammo/meds/food/grenade whitelist; protection uses exact spawn IDs and existing extraction/recruit sanitation. Later gifts remain unprotected, and boss return membership is explicitly false. **456** gameplay/storage checks and **426** return-policy checks pass. Core/Going Rogue Debug builds have zero warnings/errors; Core SPT 4.1.6 binary compatibility and **441** addon API references/seven patch targets pass. Knight caution/record evidence and the approved unchanged front-plate/pocket behavior are recorded in [Going Rogue validation](../../going-rogue/Validation.md).

No deployment. Fixtures/build/metadata checks do not establish live healing, navigation, extraction or combat outcomes.

## 2026-10-27 — fixed-companion grenade resupply, no deployment

The fixed-equipment planner accepts offered native hand grenades into current vest/pocket grids while preserving native magazine reload landing space. The restricted loose-pickup path admits `ThrowWeap` for fixed-kit companions and confirms real inventory membership before success. Ordinary recruits retain their existing pickup path; equipment shells and backpack cargo restrictions remain intact. **1,879** planner, **73** recruit routing/callback and **18** donation-access checks pass, alongside zero-warning Debug client builds and installed compatibility/API checks. No deployment. [Going Rogue validation](../../going-rogue/Validation.md) records the observed rejection and pending native transfer qualification.

## 2026-10-27 — companion body/container prompts and refusal, no deployment

Core honors an optional companion body/container-command permission through the existing loot eligibility boundary. Corpse CMD visibility, quick body/container prompts, assignment and downstream reservations/execution share that permission; loose-loot behavior remains available. A native Negative refusal occurs before command mutation when the squad contains only restricted companions. The Going Rogue addon disables the permission for Knight. The previous three-argument policy constructor retains its default behavior. **41** production eligibility/prompt/refusal checks and the existing loot/recruit/configuration checks pass; matching Core/Going Rogue Debug clients build without warnings/errors and installed API checks pass. No deployment. [Going Rogue validation](../../going-rogue/Validation.md) records detailed coverage and pending native audio/UI qualification.

## 2026-10-27 — native-skill, kit-preview and medical client/server deployment

User authorized the current matching Core/Going Rogue Debug client/server installation. With Tarkov/SPT stopped, backed up and SHA-256 verified eight DLL/PDB files. Core client hash `5F58A88D55219E845FA595ED2DCD77120C8D44798AD4B6482595B579481BA827`; Core server `24D874874E1ABDFD4A11BC286C54B778029EDA978211283C13150D215E588306`. Matching addon hashes and backup manifest are recorded in [Going Rogue validation](../../going-rogue/Validation.md). Server dependency manifests matched already; settings/databases were not directly changed. Skill correction activates at the next native raid-profile generation. Medical retry and last generated-kit preview are included; scatter normalization is unchanged. Live qualification remains pending for these changes. User separately confirmed stationary out-of-combat reload working in a live raid.

## 2026-10-27 — native companion skill baseline receipt, no deployment

Core accepts an optional addon-generated skill correction callback and version. Native companion seeding stamps untouched generated skills; existing companions invoke the callback once against an actual native raid profile. Core atomically persists the corrected roster profile and actor/version receipt under the existing raid-result lock, preserving saved progression on subsequent kit generations. Going Rogue owns removal of its former imposed values and display-row selection. **385** gameplay/storage checks and Core/Going Rogue server Debug builds pass with zero warnings/errors; installed SPT 4.1.6 compatibility passes. No deployment or installed data mutation. [Going Rogue validation](../../going-rogue/Validation.md) records the original-baseline limitation and live qualification boundary.

## 2026-10-27 — companion raid-kit viewing snapshot, no deployment

Core's native companion preparation saves a separate copy of its fully provisioned raid inventory as the current profile-view snapshot. The save re-reads the current roster under the existing persistence lock and changes inventory only; matching fresh-kit policy, identity and role are required. Companion snapshots retain boss reserves through serialization in all modes. Ordinary teammate container serialization and fresh per-raid companion generation retain their existing rules. The native other-profile projection exposes the saved equipment root/items without generating or refilling the kit. **376** gameplay/storage checks, Core/Going Rogue Debug client/server builds, installed SPT 4.1.6 API compatibility, and **437** addon references/seven patch targets pass. No deployment. Knight's corresponding medical retry diagnosis, **124** objective checks and pending live qualification are recorded in [Going Rogue validation](../../going-rogue/Validation.md).

## 2026-10-27 — reload stance client deployment

User authorized deployment of the validated Debug Core/Going Rogue clients. CIM confirmed Tarkov/SPT server stopped; only the exact SPT launcher process was present. The guarded deployment rechecked processes before each copy, backed up all four client DLL/PDB files and verified all installed SHA-256 hashes. This build also includes the previously validated, undeployed Knight fixed-kit/plate policy and boss-ally kill reporting.

Core DLL: `5F58A88D55219E845FA595ED2DCD77120C8D44798AD4B6482595B579481BA827`; Going Rogue DLL: `83DD8A35A1A0C9B67DFB46F52F2E6FB9F4CA9380FE0C2CA9E66CA3C0AF982510`. Backup and four-file hash manifest: ignored local `tests/artifacts/reload-stance-deploy-ac8cd690aa07437d927f20bc969e5688/manifest.json`. Server, settings and databases were not deployed. Fresh stationary/walking reload animation and callback qualification remains pending; installation/hash verification does not establish raid behavior.

## 2026-10-27 — stationary/walking reload stance, no deployment

Latest record `20261027-015253-Shoreline.jsonl` shows Knight entering out-of-combat reload at 352.27, 971.59 and 1230.17 seconds, with `Reloading` clearing about 15.98, 10.82 and 14.85 seconds later. The previous stationary recovery ran at 353.24, 972.50 and 1231.12 without promptly ending those reload states. The first episode also has rejected ejected-magazine refill callbacks after the reload flag clears, with a successful refill only after the next combat starts. Inventory round counts rise at admission and do not prove animation completion. This recording predates hands-operation/patrol-stance fields, so it cannot establish the actual animator state or prove one sole runtime cause.

SAIN 4.5.1 source and installed DLL IL independently confirm `UpdateStance` skips its reload-aware `CanSetPatrol` when desired/current patrol stance already agree. Sprint or enemy acquisition changes that desired stance. New follower-only `BotReload.Reloading` setter postfix releases native patrol stance before magazine or loose-ammo animation starts, excluding stale cached reload objects, other hands, ordinary bots and dead followers. It preserves `BlockFirearms`, native timing, flags, callbacks and operation ownership. Removed the ineffective delayed brain reset; patrol completion now also waits for the actual firearm reload operation to end. Recorder schema 15 adds passive hands-operation/patrol-stance transitions and native reload/block state for follow-up qualification.

`Verify-FollowerReloadStart.ps1`: **32** admission, **28** retained skipped-start/retry and **14** stance assertions pass. The stance fixture executes the production patch, extracted native reload property and unchanged SAIN `UpdateStance`, reproducing the old latch and proving the new admission boundary plus normal post-reload stance restoration. Installed native metadata confirms the setter precedes both reload branches; installed SAIN IL confirms the equality branch bypasses `CanSetPatrol`. `Verify-PatrolMagazineScan.ps1`: **27** checks including a cleared reload flag with a still-active hands operation; `Verify-MagazineSupply.ps1`: **7** checks.

Core/Going Rogue Debug build: **zero warnings/errors**. Core SPT 4.1.6 compatibility resolves **908 types/2280 members**, Going Rogue **429** API references/seven patch targets and existing Rifleman support parity pass. No deployment or installed database/settings changes. Stationary, walking and combat reload animation, callback duration, subsequent refill and weapon return still require a fresh raid; source/fixture/metadata success is not live animation evidence.

## 2026-10-27 — boss-ally raid kill results, no deployment

Registered `BossFollowerPlayer` allies now qualify for raid kill results independently of `IsSquadMate`. Registration and death capture share `ReportsRaidKills`; the native victim-row copy runs before the existing saved-squadmate progression gate. Ordinary recruits/hostile bosses remain excluded from this report, while boss runtime classification, inventory ownership, quest credit and custom XP processing retain their previous boundaries. The existing grouped result UI and dogtag-identification behavior are unchanged.

`Verify-RaidKillReport.ps1 -GameRoot ...` passes **38** production-property/death-gate/report checks with controlled native doubles: saved teammates and all three allied Goons, ordinary recruit/hostile exclusion, independent progression gating, grouping, native field retention, duplicate suppression, capture/snapshot isolation, transit preservation, fresh-raid reset, zero-kill omission and diagnosed missing native victims. Installed SPT 4.1.6 IL confirms both real and dumb statistics managers append native victim rows, matching the inspected death-dispatch source.

Core/Going Rogue client Debug build: **zero warnings/errors**. Core SPT 4.1.6 compatibility, **429** addon API references/seven patch targets, Rifleman support parity and whitespace checks passed. No deployment, server changes or database/settings writes. Live boss kills and raid-results rendering still require an in-game test; fixtures do not render EFT UI.

## 2026-10-27 — magazine-refill and Knight client deployment

User authorized installation of the validated Core refill/greeting and Going Rogue rifle-recovery fixes. Rebuilt matching Debug clients, then rechecked 98 Knight, 20 greeting and 26 refill checks, Core SPT 4.1.6 compatibility and 427 addon API references/seven patch targets. With Tarkov/SPT server stopped, backed up and deployed only the two client DLLs and matching symbols. All four installed hashes passed verification. The SPT launcher remained open; its exact process was excluded from the otherwise unchanged game/server/runtime-host guards after an initial preflight stopped before copying.

Core DLL SHA-256: `EC63539C1C3D961A67874743DC6C1991E903FFA5AE867D0D8E3BCBB88CA0C671`; Going Rogue DLL: `7A6211CBEDE93C7C77F6D994AAE35B24E9B51DFF9E42E44B0D0AF02FFDB7642A`. Full backup/manifest: ignored local `tests/artifacts/knight-combat-deploy-29d46b29038a402fbcf1c2f143a6425f/manifest.json`. Server binaries, installed settings and databases were untouched. Native reload and voice behavior still need a fresh raid test.

## 2026-10-27 — legal autonomous magazine refill, no deployment

Knight's Shoreline record `20261027-011623-Shoreline.jsonl`, sequence 767, showed the shared refill submitting a magazine still inserted in his SCAR to `Magazine.Apply`, rejected as `LoadInsertedMagazineError`. Autonomous carried-weapon refill now prepares only loose spares. Its shared native-refill entry point also rejects any magazine whose ancestor tree includes a weapon. Native reload swaps a prepared spare into the weapon; the existing completion branch refills the ejected magazine only after both native reload and hands readiness settle. No new detach/restore transaction owner or ammunition generation was added. Without a usable spare, external-magazine patrol top-off is unavailable; internal/chamber-fed reloads retain their loose-ammunition behavior. Commanded-loot magazine top-off retains its existing explicit detach/load/restore flow.

Expanded `Verify-PatrolMagazineScan.ps1` to execute the production carried pass, refill method, ancestor guard, top-off admission, spare scan and reload completion with controlled native inventory doubles: **26 checks passed**, including direct/nested installed-magazine rejection before Apply, no-spare admission, internal-feed preservation, finite spare fills and post-ejection refill. Native refill scope: **7 checks**; Swap Gear admission: **32 assertions**; reload-start recovery: **50 assertions**, with installed idle/hands/callback metadata verified. Core/Going Rogue client Debug build passed with **zero warnings/errors**. Core client/server SPT 4.1.6 compatibility, **427** addon API references/seven patch targets, shared Rifleman support parity and whitespace checks passed.

No deployment or installed inventory/settings/database changes. These fixtures verify routing and finite transaction behavior with native stand-ins; live magazine swaps, refill timing and Knight's return to a usable rifle still require a raid test.

## 2026-10-27 — follower spawn FIR preservation

SPT's `PmcFoundInRaidEquipment.ConfigurePMCFindInRaidStatus` promotes most generated-PMC container contents to FIR during native brain activation. Core now bypasses that inventory rewrite for its already-marked follower spawns, retaining incoming equipment/nested cargo flags and the independent native dogtag rule. The existing pending-spawn marker precedes activation in both squad and boss paths, including carried transit profiles.

`tests/Verify-FollowerFoundInRaid.ps1` passes 47 production-prefix checks with controlled native doubles, covering pre-registration admission, registered followers, ordinary generated bots, nested/non-FIR cargo, mixed transit flags, repeated activation and missing dogtags. Installed SPT 4.1.6 metadata confirms the reflected target signature, activation call and the container/dogtag FIR setters. Debug Core client build passes with zero warnings/errors; whitespace checks pass with existing unrelated line-ending notices.

With Tarkov/SPT closed, deployed only the Debug Core client DLL and symbols. Source/installed SHA256 hashes match: DLL `D1B3888FFEC501DF32956A7F14EFF1980750EE3AF7555CE81199336046568C2F`, PDB `932AA92D134BFA1461F2124AF92C50919365CC0F60F5702A8AB4162EAF650668`. Prior files are backed up under ignored `tests/artifacts/FollowerFoundInRaid/2026-10-27/`. Fresh-raid backpack and transit UI confirmation remain pending. No persisted item flags were repaired, and no server binary, release archive or Git commit was produced.

## 2026-10-26 — version and workshop documentation

The [mod description](../../ModDescription.md) marks the teammate workshop as introduced in **1.1.0**, including its server URL, editable fields, native localized head/voice names, equipment/deletion restrictions and managed Going Rogue companion exclusions. The obsolete statement that saved heads/voices cannot change was corrected. The active local release-note heading is now 1.1.0; prior dated 1.0.1 validation remains historical evidence.

Core client/server project versions are 1.1.0. Compiled metadata confirms:

- Client assembly 1.1.0.0, file version 1.1.0 and BepInEx plugin version 1.1.0.
- Server assembly/file version 1.1.0.0 and metadata version 1.1.0.
- Rebuilt SAIN addon keeps plugin version 1.0.0 and references Core 1.1.0.0.
- Rebuilt Going Rogue client/server keep version 0.1.0 and reference their corresponding Core 1.1.0.0 assemblies.

All five Debug builds pass with zero warnings/errors. Direct loading and encrypted storage round trip pass with 941 server/LiteDB types. Installed SPT 4.1.6 compatibility passes for 223 types/838 members and 538 loaded mod types. Scoped whitespace checks pass. This version-only pass does not add raid qualification; [workshop validation](Teammate-Web-Editor.md) records its functional checks.

With Tarkov/SPT closed, deployed Core and matching builds of the three already-installed addon binaries, symbols and existing dependency manifests. No absent addon was installed and no server was started. Source/installed DLL SHA-256 hashes match:

| DLL | SHA-256 |
|---|---|
| Core client | `0669A0C208E71BE3C2B43E5497EE1C80A4BB8D75FAA4CA0E7D402CE190F4EB9B` |
| Core server | `FE7E6B759FD8A0EA8926A4DC4A4B054A55F23D1F89086D26399DBFDAE89919EA` |
| SAIN addon | `F29C59FEE1A6DD49D32B976E7573F2151DF0534E3FE26B8C819F05CF4B5CDA29` |
| Going Rogue client | `7FF1D272149E832D72AE74B95248D82A3719A896F51CB1DB24E2B3FB8DF72FEC` |
| Going Rogue server | `4516FC3BC230C522E7B9F12125EC9DB77FEFC21B760E1E31FF4BA4B98FE44884` |

No Release ZIP was requested or produced. Server remains stopped; the version badge and next game/server launch remain a live check.

## 2026-10-26 — wounded roster cards and raid admission

Wounded means any native body part is below maximum HP; missing/invalid health is not ready. Roster summaries now include that state after elapsed recovery. Visible cards use a reddish background/hover palette, overriding faction/companion colour until full recovery, then restore their normal palette. A mode-gated lightweight health route updates existing cards every three seconds without rebuilding portraits or sending stash/profile snapshots. Hidden rosters stop polling; stale screen/mode responses are ignored and failures back off for 30 seconds. Fully healed checkpoints are not rewritten by these polls.

Manual invitations reuse the native decline/system-message flow with the localized template `{0} is still wounded and cannot join a raid yet`. The server catches up recovery before checking; cached portrait colour does not decide admission. Auto-join omits wounded members without clearing their preference, and the spawn endpoint independently rejects wounded profiles. Existing companion availability and proper-kit gates remain separate. The English embedded fallback and English/Russian/Simplified Chinese resource entries include the new template.

Validation:

- Debug client build passes with zero warnings/errors; server builds against the SPT 4.1.0 baseline. The health fixture retains the known NU1900 advisory-feed warning.
- **77** health/treatment checks pass, including the exact named decline, partial/one-part/full-health readiness, auto-join exclusion with preference retention, blocked direct spawn, lightweight wounded status, completed passive recovery clearing that status, healthy spawn admission, independent kit rejection and no idle full-health checkpoint writes.
- Existing gameplay/storage smoke passes **317** checks. Locale JSON and name placeholders validate; whitespace checks pass, with only existing line-ending notices in unrelated/previous local files.
- With Tarkov/SPT stopped, deployed Core client/server Debug DLLs and symbols. Source/installed SHA256 hashes match: client `F699B61F43B9494E5017CC9927FF45DB8883E82F14D3E4AE5161F6E90899CADF`; server `787FB395120E3396EF7E02AAF899A393849787E10D02673A8CC6CA13FEDE5E68`. Merged only the new wounded-message key into installed English, Russian and Simplified Chinese locale files. Previous binaries/symbols/locales are backed up under ignored `tests/artifacts/TeammateReadiness/2026-10-26/`.

No live profiles, health values, funds or settings were edited. Visual red/hover styling, live decline notification/message delivery and automatic return to the usual colour remain pending the next game/server launch. No release archive or Git commit was created.

## 2026-10-26 — wounded tint target correction

The wounded tint now targets the user-identified native `PortraitRoot/Portrait/Background` image. Its original colour is cached and restored at full health; the outer roster tile retains its faction/companion colour and existing hover/press styling. Health polling still updates existing portraits without rebuilding them. Removed the superseded hover-palette update helper.

Debug Core client build passes with zero warnings/errors. With Tarkov stopped, deployed only the client DLL and symbols; installed/source SHA256 hashes match: DLL `924D19250AD22E764009625684C7144195A79CB5D99AEFC2249320D45AD786A5`, PDB `3C98FF38FB5D423386F7EAED077CBA67EC5988938FD1E396FB81D5C06C0B9D2F`. Previous files are backed up in ignored `tests/artifacts/TeammateReadiness/2026-10-26/before-portrait-background-20261026T163438423/`. No server or live profile changes. The exact tint appearance and restoration remain pending in-game confirmation.
