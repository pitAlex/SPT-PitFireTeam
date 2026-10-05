# SAIN addon validation and raid evidence

## 2026-10-06 - Core Cooperation response compatibility

Source and installed SAIN 4.5.1 metadata confirm that native `BotTalk.Say`/`Player.Say` prefixes suppress recruitment replies before the candidate becomes a follower, while native friendly conversation can generate mumbling. Core's existing timed phrase gate now marks recruitment replies separately: only the exact candidate reply bypasses those blockers, and candidate EFT `ManualUpdate` remains SAIN-owned. Initial/deferred replies use immediate EFT speech with SAIN installed; no-SAIN requests retain their original `TrySay` path. Native chatter generation and final output suppress only mumbling/taunting during the candidate's scope, preserving native warnings and injury speech. Existing conversion, eligibility, combat refusal and raid-scoped level refusal policies are unchanged. This is [Core SAIN compatibility](../../docs/SAIN-Compatibility.md#runtime-ownership), independent of addon installation.

Validation: installed signatures verified; 30 production recruitment-policy checks and 32 production voice-gate checks with real Harmony passed. Voice checks cover both native blockers, queue avoidance, newly generated/already-pending chatter, other-bot isolation, warning/injury output, expiry, conversion, scope replacement/clearing and SAIN absence. Matching Debug Core/addon builds passed with zero warnings/errors; scoped whitespace checks passed. Reuses the existing gate/cache and speech hooks; no new subscriptions, frame loops, world scans or navigation/physics probes. Actual audible replies during recruitment still require raid confirmation.

With Tarkov closed, deployed matching DLLs/symbols without backups and verified SHA-256: Core DLL `CAA6301CC7DE36231A4E9849E4AE1462B97F7AF648C7E168D1E411F10F36996D`; Core PDB `757827D69420B45E7CEB330FB30BF5375BE6F699EDAA0861CEC133D066501ABD`; addon DLL `EC93B38752861FDEB799289556B11132F93DDC27A1793CB47193968A07D92D40`; addon PDB `72726DA5741D14B31B2007C4BBB2DF6E41D5F027C93B562215094E71FB8A310F`.

## 2026-10-05 - Core Need Sniper arrival commitment

`20261005-180256-Woods.jsonl` records Zero running **Core Marksman**, with Brick on SAINGrunt. The second recorded Need Sniper order selected cover 5824 at raid time 401.76 and reached it at 409.00. The objective armed its two-second arrival hold, immediately selected `sniper.NeedSniper.immediateShoot` and marked itself complete. At 409.14 the firing action ended with `enemyCannotShoot` before aim completed, the arrival hold was cleared, and automatic regroup took over at roughly 43 m from the player.

The scoped correction is Core-owned in `FollowerCombatNeedSniperObjective`: an arrival's original two-second deadline now survives temporary `shootFromPlace`/`shootFromCover` selection and a shot-ready hold break. Shared firing termination still stops unsafe fire; the remaining committed hold can resume without a new route, new position or extended deadline. Hold expiry or a shot ending after expiry releases order ownership. Pre-arrival immediate fire, medical/close-danger interruption, target death and command replacement retain their prior behavior. This does not change SAINShooter, terrain/floor candidate filtering, general immediate-fire classification or regroup distances. Current command behavior is documented in [Core Need Sniper](../../docs/Commands.md#need-sniper).

Validation: 47 full-production-objective arrival/flicker/survival checks, 88 existing Marksman arrival checks and 37 existing Marksman engagement checks passed. The engagement fixture's missing shared tracking-distance stand-in was restored. Matching Debug Core/addon builds passed with zero warnings/errors; scoped whitespace checks passed. The change adds only deadline comparisons and uses the existing shared position commitment; no new scans, probes, timers or frame loops. Actual aim, cover/navigation and raid regroup still require in-game confirmation.

With Tarkov closed, deployed both DLLs and symbols without backups and verified SHA-256: Core DLL `AD07EADC01B49007BD7A7E4DE6AD9DE805950F5ABBE9522A7476A8A36113D218`; Core PDB `F4E111C3D05FF3EC9EDB0FEB618868F8034FEA3A542F3A46944B64E24290085E`; addon DLL `061C2483C50989D290E183B83939EBCD604A73105BD797F29C4D4FFCD6D04FA0`; addon PDB `AB9087D303B44B7651C77DF632ECB0A76F802DB5FC8CC248339E9ABE6D498C39`.

## 2026-10-05 - Accepted hostile contact activation

Investigated `20261005-021411-Woods.jsonl`: the final Scav was admitted into all followers' EFT goals, while OptimusChad repeatedly published native `StandAndShoot` and immediately released to patrol with no public native target; Zero remained in Core Marksman combat. Native 4.5.1 metadata confirms that `BotComponent.GoalEnemy` filters AI targets through `Enemy.IsEnemyActive`, including target lifecycle and standby. The exact rejected activity flag is absent from this recording; sleeping standby remains a conditional repair, not a proven recorded state.

Core's existing goal-setter patch now uses a postfix to activate an already-admitted living hostile AI target from sleeping EFT standby via the existing native wake helper. Rejected setters, mismatched goal/owner, neutral targets, cleared goals and knowledge without a goal cannot initiate activation. Neither native SAIN activity checks nor addon handoff/firing gates are loosened. Direct `EnemyInfo.Owner` access avoids extra reflection; the hook adds no frame loop, world scan, navigation/physics probes or per-contact cache. General external-SAIN compatibility remains Core-owned, including without the addon.

Validation: 24 production contact-activation checks, 54 production post-combat handoff checks, 66 hostility checks and 1,352 addon combat checks passed. The older handoff fixture's tracking stand-ins and enemy-map key were aligned with current production dependencies. Installed EFT `BotStandBy.Activate` and SAIN filtered-target metadata were verified. Matching Debug Core/addon builds completed with zero warnings/errors. Raid perception and confirmation that this resolves the observed final contact still require in-game testing.

With Tarkov closed, deployed and hash-verified both DLLs and symbols without backups: Core DLL `31B0613D14D08FC675C7D3CD95438D96A7241E9B8541CBA7E848067E4F2390E7`; Core PDB `026F938EECDF3FBD38C54BA61CE7090218BA1987CCCD4962B84D3A227C0167BC`; addon DLL `CFF7A8DEAE17747F4BF2EC8A319328B610E409434AB2857CB947327927BF7FD2`; addon PDB `9E34C0965FB43B43C3EB8EB842F253032746CA11864581CD92EF12F2C84D8DC0`.

## 2026-10-05 - Single Enemy Tracking row and requested description

Corrected the previous two-row interpretation: one Enemy Tracking name/description on the left, with Simple above Realistic on the right. Both controls retain the shared Loadout Management styling and click handling. Their vertical placement accounts for the native 42px control at 0.86 scale, leaving a 15px gap. The tracking row alone is 136px tall to fit the description and choices. Updated the centralized English description to the user's corrected wording and synchronized Russian/Chinese translations. Full Debug solution build passed with zero warnings/errors; locale JSON, scoped whitespace checks and localization callsite checks passed. In-game visual/interaction confirmation remains pending.

With Tarkov closed, deployed matching Core/addon DLLs and symbols plus the three language resources and verified hashes. Core DLL `8841767F0EFE3B599629349DB60D099F0D556E6E23F6F1ABBB805F65B47AA084`; Core PDB `D1F884E88DC7E00E1206E25DC164DFBDA77479B8776E8BB8357CDBF2A56E192F`; addon DLL `7D9318F72EF41EBF393D1125CEC9342877A33F572A0230ACE68F9179A95980F7`; addon PDB `387F15B0AF75E2BBF1A397856B412A426321ADA7FEDCABA6815ED16E55E97235`. Server code was built for validation but no server binary was deployed.

## 2026-10-05 - Tracking uses the Loadout Management control

After the user supplied the desired Loadout Management appearance, replaced the compact tracking selector with separate Simple/Realistic rows. Both settings now call shared `CreateModeSelectionEntryRow`, `CreateModeRadioControl` and `CreateModeClickOverlay` builders, preserving the existing loadout dimensions, native selected-state styling, CanvasGroup binding and hover/click handling. The abandoned tracking-specific compact implementation was removed. Localized labels, enum persistence, exclusive selection and raid restrictions remain. Matching Debug builds passed with zero warnings/errors and scoped whitespace validation passed; live visual/interaction verification remains pending.

With Tarkov closed, deployed both DLLs and symbols and verified SHA-256: Core DLL `EB04C162FD261AC4473B8381C84FEC46476024586EDC06C4E9B8EC73D975281E`; Core PDB `B2B30BD038523F34B1D641EAE7BB5C88923503F7AE02BE2ED22026B355A846AE`; addon DLL `30C0C8ED1ADA425B34D95909EFC34AA8536DB19E6C352AAA1D2DD6D5A49A88CC`; addon PDB `206BA25019D317B0E7C58B6DBA471616F7A1D6DDEC965F42D3580E7CCA15508E`.

## 2026-10-05 - Enemy Tracking radio layout

Replaced My Squad's side-by-side tracking buttons with vertically stacked Simple/Realistic choices using the existing native radio template and a local exclusive `ToggleGroup`. Selection still saves the same enum; raid disabling and localized labels are retained. Matching Debug Core/addon builds passed with zero warnings/errors and the scoped whitespace check passed. With Tarkov closed, deployed both DLLs and symbols and verified hashes: Core DLL `4BFC573A9E71E5018E131B81D4E97DF869E4F1B176395A902E1C27598C5B673B`; Core PDB `FB5B1AF3B3298E206F4290A829AEF03D0E47D52F2E3837170E29C23833C7CCD2`; addon DLL `1F2E8958B3CA48EEBFA7F4433815E6FCD600DD0957A952E909EED99E51440DCC`; addon PDB `15F8921C224C83A93DBC6D6AC849AEC86157901B024A292B1730D5A65E27B093`. In-game visual/interaction verification remains pending; tactical tracking was not changed.

## 2026-10-05 - Simple marker refresh cadence

Deployment follow-up **2026-10-05**: after the user closed Tarkov, copied the validated Debug Core/addon DLLs and PDBs into the live plugin directory and verified all four SHA-256 hashes. Core DLL `ABECA2901776C6083A5E0918247DD65430208DA4948931D8BC10ECC50FA33021`; Core PDB `A668EF20AC5D41A7949B90C13D0E5310F0D8762719E181DC0ABD378B450F78A7`; addon DLL `A4D6B616E3D6A05E0D551BBE1889567D02076444E0A7C68324C17AE245B5568E`; addon PDB `4E44F80D64C5F1E56BD197036780F5E91BC59A9E9D139774F618EE0E88F0B323`. This resolves the deployment pending below; raid verification remains pending.

Restricted `PingTeamates.RefreshEnemyMarkerContacts`' SAIN-knowledge refresh bypass to Realistic mode. Simple addon contacts now retain the Core ping/first-display/five-second hidden-marker cadence; visible-marker updates and tactical position reads are unchanged. Core and addon Realistic refresh paths are unchanged.

`Verify-EnemyTracking.ps1` passed 56 behavior checks, including nine checks against the extracted production marker condition, plus installed native metadata/hearing-accessor checks and production IL validation (14 methods, 25 replacements). Matching Core/addon Debug builds passed with zero warnings/errors, and `git diff --check` passed. Deployment was stopped before copying because `EscapeFromTarkov` was running. Built outputs include the existing unrelated working-tree UI changes. Deployment and raid verification remain pending.

## 2026-10-04 - User testing checkpoint

The user reports that **Core Realistic tracking appears good so far** with the deployed build. This is positive raid feedback, not exhaustive qualification of perception, navigation or every command combination. **SAIN Simple tracking still requires user raid testing**; its existing fixture/metadata checks do not establish in-game behavior. Continue qualification from the deployment hashes and checks recorded below.

## 2026-10-03 - Core hearing parity and exhausted-search release

Deployment follow-up **2026-10-04**: with Tarkov closed, rebuilt Debug Core/addon (zero warnings/errors), copied both DLLs and PDBs into the live plugin directory, and verified all four SHA-256 hashes. No backups were created. Core DLL `750507E835097FAB02ED0AEE094E890CBCEA060A1A6F00A3C8A3DD68F4F10EDE`; Core PDB `F00E5A03EF232C78E8E06BD1469AE3805A70F6B9E5A63853094C39D859EA8886`; addon DLL `67E118C8B7530A01F232271CACED9D0BEB1A2F11878AFD2D208C5E94940FE7D2`; addon PDB `9D011808336218F854468C19622E228DCAAE9D703A29FE24903E2B8019C1B835`. This resolves the deployment pending at the implementation check below; raid qualification remains pending.

Core peaceful sound orientation now covers 25 m local movement/voice and audible gunfire directed at the squad, including native SAIN hearing without the addon. Realistic Core search performs a three-second inspection after verified arrival, then releases the exhausted report from active combat while preserving living memory. Simple and native addon search semantics remain unchanged. See [Core combat](../../docs/Combat-Tactics.md#peaceful-sound-orientation) and [tracking](../../docs/Enemy-Tracking.md).

Debug Core/addon build passed with zero warnings/errors. Production Core sound fixture: **28 checks**; tracking fixture: **47 checks**; addon combat regression: **1352 checks**. Core native hearing accessors compiled against the installed SAIN assembly; native tracking IL verification passed for 14 methods / 25 replacements. `git diff --check` passed. Deployment is pending because EscapeFromTarkov was running. Actual Mono hook installation, arrival/look-around animation, navigation and perception still need raid validation.

## 2026-10-03 - Core Realistic invalid-memory recovery

Restored `Enemy.RepairPersonalMemory` for hidden Realistic contacts as the deliberate compatibility exception described in [Core tracking](../../docs/Enemy-Tracking.md). The production repair method now participates in `Verify-EnemyTracking.ps1`: 42 behavior checks passed, including 10 recovery checks for invalid fields, repeat-call stability, expiry, sense-only admission and missing group state. Installed metadata validation and production IL validation passed (14 native methods, 25 replacements). Core and matching addon Debug builds passed with zero warnings/errors. This does not establish the cause of the earlier wait/no-marker raid symptom; runtime confirmation remains pending.

With the game closed, deployed Core/addon DLLs and PDBs and verified SHA-256 at **2026-10-03 03:01:26 +03:00**. Core DLL `F62A7CC069914AFCF8602B4321C8FE7A5203340D70887A5B840823FB7F881984`; Core PDB `8FC6729823F8E5B0509DD74F885558A6701EBCA2C61751C576302677BD2408FE`; addon DLL `052C3D302B017ECE62EABF2D2485780BCDAB5064C75345ED81F589C9FF702727`; addon PDB `6F620C51F094F64F412693DF58A3FE14A0AC302B54F766E2473B151ABE8351B8`. Build includes the existing working-tree changes; this fix changes only the Core repair gate and associated tests/documentation.

## 2026-10-02 - Shared player-based hostility

`20261002-220352-Lighthouse.jsonl` records Noisy (`exUsec`) killing Core Marksman Zero at 22:34:58 local time, about 219 m away, with the player 8 m from Zero. The player confirmed through bot debug that the Rogues ignored him while attacking followers. Source and installed DLL inspection identified a Core activation repair that forced different-side follower enemies (including a raw dictionary fallback), and one-way group sharing that did not add the player to the attacker's enemies.

The [shared Core relationship policy](../../docs/Combat-Tactics.md#enemy-acquisition-and-retention) now mirrors player status during ambient acquisition in either direction, preserves explicit Contact and real damage as overrides, and shares established hostility across the living squad. The existing damage hook captures actual hits because native AI retaliation can use `checkAddTODO`, also used by sight scans. No SAIN decisions or presets were patched. See `tests/Verify-FollowerHostility.ps1` for 66 production policy/acquisition/Harmony checks, including native rejection, deferred construction, mixed follower/player factions, Contact against neutral Rogues/Scavs, retaliation, protected roles, recursion, and ordinary-bot isolation. The 1,352 existing addon checks also passed. Matching Debug Core/addon build: zero warnings/errors. No per-frame scan, geometry query, or navigation work was added; fixture performance checks cover redundant native writes, not Unity frame time.

Deployed Core/addon DLL/PDB pairs without backups at **2026-10-02 23:18:40 +03:00**, while Tarkov was closed. All four installed hashes matched outputs. Core DLL SHA-256: `972F374857A922D3A39B541C58FF2179C8999F9A4A119007E9B319EF9920CEEB`; addon DLL: `87AEFDA5B214B51128212D495F1D46578CD89D7812D2566DE1ACF425D97318A3`. New-raid validation remains pending.


## 2026-10-02 - Core wait recorder expansion

The Lighthouse recording `20261002-192803-Lighthouse.jsonl` plus the normal plugin log identify Core `recentGroupContact` as the patrol blocker, including waits before the first Contact command. They do not identify the writer that keeps the global group sight timestamp recent. No `trackingExpired` transitions occurred; the recorded combat ended through dead-goal cleanup. Root cause attribution to the tracking change remains pending.

Core recorder schema 14 now captures wait entry, blocker changes, five-second heartbeats and exit even without combat/recovery/commands, and bounded actual group-sight timestamp writes with identity scopes and sampled stacks. See the [Core recording contract](../../docs/Combat-Tactics.md). Combat and readiness policy are unchanged. Installed method/field metadata checks passed, as did 17 focused production-hook/wait-lifecycle checks. The wait method is extracted from production into the controlled fixture; the hooks themselves execute through Harmony on stand-ins. Actual Unity hook execution and diagnostic frame cost require the next raid. Final matching Debug build passed with zero warnings/errors; tracked whitespace checks passed.

With Tarkov closed, deployed and SHA-256 verified Core/addon DLL/PDB files at **2026-10-02 20:00:16 +03:00**. Core DLL `DF89257830B66BB78D9FE3E848A001C078EDFD269BDB97C9764165137B98FFCA`; Core PDB `B32F7CE07020D02D4492917A3DF7BA2E742F32C3D08292F28DF2AE31A0F9B103`; addon DLL `F52350103A990F880B215DC0DA509EF928AA0522ABFA43280EE17075A173677B`; addon PDB `11B4B2FAD01F1B0214F2E5E2A5776E8B7FE93D3D46C0CD55AFB318F2B8524A30`. No server files or private references were deployed.

## 2026-10-02 - Contact target selection and passive failure diagnostics

`20261002-062433-factory4_night.jsonl` captured the first player Contact at time 538.6066 (06:25:34): both followers accepted Golani at `(30.9303, 1.2470, 36.2061)`. Within about 0.08 seconds native SAIN selected Floppa's older nearby report at `(8.6804, -0.7607, 41.2887)`. Core rejected the corresponding memory-only goal replacement and retained Golani, but the addon marker and combat provider already read Floppa. This proves a selection mismatch, not movement of Golani's remembered point. Core and addon were ready and Realistic/60 seconds was configured. No tracking installation error appeared in the logs.

The existing native selection adapter now honors Core's finite prioritized Contact retention before publication, subject to valid known native evidence and existing urgent-threat/objective priority. The reproduction fixture exercises the real adapter and marker methods. **1,352 production addon combat checks**, **32 tracking checks**, and **17 diagnostic hook checks** passed. Installed metadata checks validate the three new diagnostic hook signatures/argument names and owner fields; tracking IL still validates 25 replacements across 14 methods. Matching Debug Core/addon build completed with zero warnings/errors. Fixture-only warnings remain. Controlled raid validation is pending.

The raid also contained preview and aiming-reset animation exceptions without an identifiable mod-owned cause. EFT closes group preview for local raids, so Core's explicit squad-preview Show remains necessary and was preserved. The Grizzly warning at 06:26:15.927 followed Medved's combat-layer stop by about 0.6 seconds, but the record lacks inventory event history. Decompiled `ItemController.ProcessActivity` emits this warning for an unmatched non-Begin activity; it is not proof of a detached item or lost inventory. Debug-only passive diagnostics now retain eight follower drain events and report unmatched completions, preview weapon creation failures and aiming-reset failures, bounded to three reports per owning controller. Original execution and exceptions are preserved. Another reproduction is needed to establish causality for these errors. The Borkel night-vision shader error belongs to the external shader bundle.

With Tarkov closed, deployed matching Debug Core/addon DLL/PDB files at **2026-10-02 06:59:11 +03:00**, all SHA-256 verified. Core DLL: `E6BDE60561EBF6E42294A93456990DB6055626426E066909FF71BD4B1B5D8C1F`; Core PDB: `2E8D2A112E43AE3063444A43FF0C3071747E75126484FCEE8740A54659CD351E`; addon DLL: `80121EF7201933839F05886270160D30F30E2EEC27CF1DA01E8031C563D5FAE8`; addon PDB: `95672759D50A8187442BDE29636D1DF94072E70BF72BECD3C8EC1FC36F92520C`. Required installed SAIN attribution was preserved. No private references or server files were deployed. Tracked whitespace checks passed.

## 2026-10-01 - Shared Simple and Realistic enemy tracking

Implemented the [shared tracking contract](../../docs/Enemy-Tracking.md) and [native addon adaptation](Enemy-Tracking.md). Realistic is the default and uses remembered reports with active unfinished-search extension, bounded at 400 seconds after genuine evidence. Simple projects eligible accepted targets through walls in both combat owners without renewing observation timestamps or changing sight/fire permission. Mode and ordinary remember duration are captured at raid start. Core search uses route/floor-aware completion and fresh same-point evidence can reopen it. Passive recorder snapshots expose tracking provenance and deadlines.

Final matching Debug Core/addon build passed with zero warnings/errors. **32 production tracking behavior checks** and **1,331 production addon combat regression checks** passed; the existing regression fixture emits its pre-existing stand-in warnings. Installed SAIN 4.5.1 metadata validated 21 tactical reads in the explicitly scoped types plus eligibility/search/timer signatures. Executing the production transpiler validated **25 replacements across 14 native methods**, including the separate enemy look-point consumer, with matching stack signatures. Language JSON and tracked diff whitespace checks passed.

An attempted headless Harmony detour check failed because Windows CLR cannot prepare Unity engine ECall methods. The successful IL check does not establish actual Unity patch installation. Controlled raids still must qualify both modes/backends, markers/steering/cover, genuine sound/report updates, native partial routes, floors/walls, orders/expiry, multi-contact handoff and frame time. Core-only sensing does not reproduce SAIN hearing dispersion or squad transmission probability.

With Tarkov closed, deployed matching current-working-tree Debug Core/addon DLL/PDB files and English/Russian/Chinese language resources at **2026-10-01 23:56:08 +03:00**, all SHA-256 verified. Existing required SAIN-LICENSE.txt remains installed. Core DLL: `EF0B7E244476FE7F64D78D604E013BC901D8705251D55C07C8A3E9136CE976F6`; Core PDB: `690CADC3D8EC9018F05C53BFD5008D370B481CCB2283146C20FFB74182DB0504`; addon DLL: `8DA55DDD20A7765D00719171C5A951463F78ED1F005C6EBFB385FE0B480ED4BF`; addon PDB: `E0D7A55C5FD35761874CB50AF27A19A5FFD2EA07CA347840DA03A212C2B296DE`. The pair includes the previously validated sound-awareness work. No backup was created and no private dependency references were deployed.

## 2026-10-01 - Personally heard sounds retain peaceful facing

`20261001-173806-RezervBase.jsonl` shows the final two contacts entering addon combat immediately after player Contact at recorder times 1605.406 and 1670.035. Before the latter, native memory-only goal attempts were blocked while the followers had no personal sight; initial combat snapshots faced 152/121 degrees away from that enemy. The earlier 832-846 window shows known native contact and repeated Core threat flags during HoldPosition without sustained facing. Individual sound categories were not recorded, so the raid cannot identify every missed footstep/bush event. Installed SAIN IL and startup logging confirm its enabled `TryPlayShootSoundPatch` suppresses EFT's ordinary gunshot dispatcher; installed Core's sound hook still targets that dispatcher.

The addon now observes the real native audibility result and preserves a bounded orientation-only bearing during peaceful actions. It does not change hearing outcomes, goal admission, fire permission, independent combat, or cross-floor preparation movement. See [sound awareness](Combat.md#sound-awareness-during-peaceful-actions). Passive `sainSoundReaction` events make the response boundary reviewable outside combat.

Debug Core/addon compilation passed with zero warnings/errors. The installed audibility/action signatures passed validation and **1,331 production addon combat checks** passed, covering native audibility, hold overwrite, remembered positions, local range, elevation, directed shots at player/follower/sibling, Attention, command look, ownership exclusions, cleanup and bounded recording. Manual performance review found no new physics/route work or per-frame allocations; a repeated-sound fixture confirms native hearing is evaluated once and no navigation work is added. Unity orientation and perception still need raid validation.

The workspace Core DLL differs from installed Core, so a separate Debug addon build was compiled directly against the installed Core DLL using an ignored temporary project under `addon/obj/hearing-installed-core`; that build also passed with zero warnings/errors. With Tarkov closed, deployed only the addon DLL/PDB at **2026-10-01 18:41:29 +03:00**, both hash-verified. DLL SHA-256: `E1BE20888C2DD806E84074FAE3E3C46F0C8D5BECAE54416ABE98BB0EB314A968`; PDB: `AA4A072D637A915AB63AE8CBAADE143C1FE324691A927BB1A038794F70D801B8`. Installed Core remained `3137A3B2DC2993A65E64AB68B4B1433D630A54EE065C26153A5129F4DCF68C71`. No backups were created.

## 2026-09-30 - Post-combat SAIN lean during Core healing movement

A newly recruited SAINGrunt was observed leaning left through post-combat `runToHeal`, then straightening later. The live log confirms its addon combat readiness, but the new battle-record file was empty, so it does not establish the exact lean value or action transition. Source inspection shows Core patrol resets EFT tilt once on entry, while SAIN 4.5.1 keeps updating native lean with a retained active native enemy even when its own layers have yielded; SAIN's running check does not see Core `runToHeal` movement. The addon now routes selected followers through SAIN's normal lean reset while native layers are inactive, without changing Core, enemy retention or ordinary SAIN bots. See [post-combat posture handoff](Combat.md#post-combat-posture-handoff).

Debug Core/addon build passed with zero warnings/errors. Installed SAIN 4.5.1 `LeanClass.CheckCanLeanByState(out bool)` metadata matched the hook. **1,287 production addon combat checks** passed, including inactive Grunt/Shooter reset, active Grunt native behavior, Core tactic and ordinary SAIN bot exclusions, and hook lifecycle. The game was running, and the current Core build differs from the installed Core because of unrelated local changes, so no binaries were deployed. A later raid must confirm the visual lean releases during Core healing movement.

## 2026-09-26 - SAINShooter initialization fallback in two Reserve raids

Records `20260926-063032-RezervBase.jsonl` and `20260926-072342-RezervBase.jsonl` both selected `SAINShooter` for Zero, but every recorded Zero combat snapshot was owned by Core (`FollowerCombatSniperObjective`); no `sainMarksman` or addon action was recorded for him. Brick used the ready addon in those same raids. This was a regression, not an original limitation: the 2026-09-17 Shoreline record has 43 `sainMarksman` events and 3,973 Zero snapshots owned by `sainAddon`; Git shows the failing weapon-helper call was introduced on 2026-09-19 with support-weapon handling. Core chose `sniper.startPosition.runToCover`, reposition/recovery cover and repeated `sniper.NeedSniper.retry.noLane` holds. The records therefore cannot qualify the SAINShooter firing-position policy or attribute Zero's indoor/underground route to it.

The live log reported `MissingMethodException: Default constructor not found for type pitTeam.BigBrain.FollowerCombatCommon` while preparing Zero in both raids. Installed addon IL showed `Activator.CreateInstance(Type, Boolean)` for the first Marksman weapon-helper construction because EFT `BotOwner` converts to `bool`; Core exposes only `FollowerCombatCommon(BotOwner)`. The addon now passes `new object[] { bot.BotOwner }`. Rebuilt IL calls `Activator.CreateInstance(Type, Object[])` for both helper constructors. Debug Core/addon build passed with zero warnings/errors; installed SAIN 4.5.1 boundary checks and 1,282 production addon combat fixture checks passed with the BotOwner Boolean conversion reproduced in the fixture. Raid confirmation is still required: Zero should log SAINShooter combat ready, record `combatOwner=sainAddon` and `sainMarksman` events, then the native finder/cover behavior can be judged.

Deployed the Debug addon DLL/PDB only at **2026-09-26 08:01:55 +03:00** with Tarkov closed, installed Core matching the built Core, and both installed addon hashes matching build output. DLL: `39AC18437D4E783F40035D3574977A1F6FAD63309852E6D9E9FC29A1671E0B63`; PDB: `3E172AB0CE587F7F30C80A81ECE6E88DC84AD36B1A9B28EC40C9BC59451D5052`. No backup was created.

## 2026-09-25 - Attention ignores dismissed hearing contacts until sector change

Accepted Attention snapshots native current/known contacts for all ready followers before Core clears the shared enemy list. The addon keeps those identities ineligible for heard-only preparation until the player moves more than the shared regroup-sector distance (10 m normally / 8 m on Factory-Labs). Native perception and normal combat admission remain unchanged. See [Attention contract](Commands.md#on-your-own-and-attention).

Debug Core/addon build passed with zero warnings/errors. **1,282 production addon combat checks** passed, including the production Harmony hook on accepted/debounced Attention, both-follower capture before cleanup, recreated contact identities and all three defensive choices, no time-based expiry, player-versus-follower movement, exact sector boundaries, periodic release before another hearing decision, new enemy eligibility, normal combat/independence/urgent-action admission, repeated Attention, opt-out and dismissal cleanup; voice and movement sound-type admission at the requested shared 25 m boundary; gunshot, impact and other-sound exclusion; and rejection of stale steps after a newer sound. The installed Core IL was checked for one acceptance timestamp assignment before enemy cleanup; source/fixture success does not establish raid behavior.

Deployed matching current-working-tree Debug Core/addon DLLs and PDBs at **2026-09-25 00:19:35 +03:00**, with Tarkov and SPT.Server closed and all four installed SHA-256 hashes matching the build outputs. Core DLL: `C4FA95197FDC5A04730AF2F3B9F69C01987D9416A11A5239A1193AB705B8B1F2`; addon DLL: `4830187AEC86BB9CD16D40B53F54EC6EFA8429AFEB0A606C642DD40F5AC1996C`. No backups were created; server/resources were not deployed. Raid qualification remains: Attention during heard preparation, repeated footsteps in the same sector, genuine contact/damage interruptions, and resuming preparation after player movement.

## 2026-09-24 - Heard-contact preparation before accepted combat

The addon admits SAIN 4.5.1's published Freeze, SeekCover and ShiftCover for an eligible hostile heard-from-peace contact without a Core goal enemy. A dedicated solo preparation action faces native knowledge and can make one local cover move. Distant/incomplete cross-floor routes retain a stationary wait. Native combat executors, heard-only Search and suppression remain excluded until normal admission. See [combat contract](Combat.md#admission-and-recovery).

The Debug Core/addon build passed with zero warnings/errors against installed game/SAIN references. **1,230 production addon combat checks** and **9 native replica parity checks** passed, including preparation admission and noncombat release, no hidden-position tracking or firing/suppression, empty-search stability, one committed move across defensive decision changes, exact arrival, stall fallback, command/medical/accepted-goal interruption, distant/incomplete and short complete floor connections, new-floor knowledge, same-floor local cover, path length limits, destination reservation cleanup, unrelated-path preservation and the four-probe planning budget. Existing fixture-only warnings remain.

This source/fixture pass does not establish Unity hearing, navigation, facing during movement, or frame time. The current changes are not deployed; in-raid qualification remains for heard enemies separated by distant stairs/ramps, nearby connected floors, cover arrival and command handoff.


## 2026-09-23 - SAINGrunt emergency backup before reload

Installed SAIN 4.5.1 skips vanilla `FightShallReload` and its empty-gun `ShallChangeIfNoAmmo` path. The addon now admits one explicit loaded second-primary/holster draw from an empty first primary against a visible shootable living known enemy within 10 m. Core loaded-round/launcher helpers are reused; native selector permission, reload/medical/urgent-action protection and decision publication remain in place. Accepted draws wait at most three seconds for actual hands/readiness; accepted and rejected requests reserve 25 seconds before another attempt. See [combat contract](Combat.md#emergency-backup-weapon).

Validation: **52 production Harmony emergency-weapon checks**, **1,169 existing addon combat checks**, and **9 replica parity checks** passed. Focused coverage includes role/readiness/admission, hidden/dead/blocked targets, range boundaries, one remaining round, empty/launcher/malfunctioning backups, loaded pistol fallback, selector capability, busy hands, protected self-actions, accepted/rejected asynchronous draws, actual hands confirmation, timeout and retry, release/medical interruption, independent native-component state and passive accepted-draw events. Installed self-action and Core helper signatures were checked. Debug addon-only build passed with zero warnings/errors against Core output matching the live Core hash. Existing addon fixture warnings remain limited to its stand-in types/unused fields and parameters.

The addon DLL/PDB were deployed with Tarkov closed at 2026-09-23 03:04:33 +03:00 and verified against build SHA-256:

- Addon DLL: `D6BCAA0AC4CE385123D9417A337D1BBA09D936C80BD1C59AABD433BFEB1C640D`
- Addon PDB: `BB49D727CAA7451EBC0A961C4DEC29C70BAAE339C200BD295DAABCAE1B7AE6F6`
- Matching existing Core DLL: `3D884631EEC6AD656219137F4BAB7B107600054598BE1AFC6FBBEB05D3F63D00`

Raid qualification remains: physical secondary/pistol draw completion, immediate native firing, normal reloads outside the emergency window, and native/Core return to primary after danger passes. `sainEmergencyWeapon` records accepted draws. No Core/server source changes were required by this change.

## 2026-09-23 - Shared effect-based stim selection

Core's existing stim item selection and pain predicates were extracted into `FollowerStimulatorPolicy`. The addon extends native `startUseStims` for ready SAINGrunt/SAINShooter followers, using native stim safety or the existing reached hard-cover exception. Core's call order and refresh-on-miss behavior remain; addon misses preserve the native cached item. Native scheduling, execution, cooldowns and publication remain single-owned. See [stimulants](Combat.md#stimulants).

Debug Core/addon build passed with zero warnings/errors. Installed SAIN 4.5.1 stim-selection and static enemy-safety signatures were validated. **1,169 production addon checks**, **9 replica-parity checks** and **25 Core medical lifecycle checks** passed. The stim cases exercise the real shared selector and addon Harmony prefix against simulated inventory/native surfaces: black-stomach/limb pain, active painkillers, positive versus negative regeneration, stale HaveSmt, secure-slot routing, cooldown/reload/active medicine, protected-cover and non-goal-threat gates, native fallback without item mutation, Core/ordinary-bot/unready/accepted-goal exclusions, and addon hook lifecycle. They do not establish actual EFT injection animation or raid outcomes.

With Tarkov closed, deployed matching Debug Core/addon DLLs and PDBs at **2026-09-23 02:52:38 +03:00**. Existing installed SAIN attribution was retained. SHA-256 matches build outputs:

- Core DLL: `3D884631EEC6AD656219137F4BAB7B107600054598BE1AFC6FBBEB05D3F63D00`
- Core PDB: `2F4693BD690CBC4E304D72C4E65232834FD136F3ADAF3FF885724E8DCCBEB8DE`
- Addon DLL: `D352CB4640D0724E295649DFF30C55293F5454AF416C2F0A7F4D5995693FD935`
- Addon PDB: `DF48E2AFE5BAF545EE5FE5F436580DABE251B235160F98428FF0A23B13DA03B3`

Pending raid qualification: Adrenaline pain relief with a blacked stomach/limb, serious-injury regeneration, interruption by real threats, and continuation through native hands/item callbacks. No server changes were required by this fix.

## 2026-09-23 - Peaceful fire-mode and inspection guard

Source inspection of SAIN 4.5.1 `Firemode.CheckSwapFireMode` confirmed idle mode selection from aim distance, followed by optional magazine/chamber inspection. `BotWeaponInfoClass.ManualUpdate` invokes the routine independently of having a goal. The addon now skips that routine for its selected tactics without a living, known, admitted combat goal. [Contract](Combat.md#peaceful-weapon-handling). Combat/native independent combat, ordinary SAIN bots and Core tactics retain their existing behavior; Core combat was not edited.

The installed assembly signature was validated. Debug addon build against existing Core output passed with zero warnings/errors. **1,143 production addon checks** passed, including 1,000 repeated idle invocations per tactic, combat re-entry, unaccepted heard contact, independent combat, dead/forgotten targets, Core tactic and ordinary-bot exclusions. The new prefix contains no geometry/enemy scans, provider evaluation or per-call state allocation. Runtime animation and frame time remain pending raid validation.

With Tarkov closed, deployed addon DLL/PDB only at **2026-09-23 02:31:11 +03:00**, without backups. Verified SHA-256: DLL `F4615911996B24CB2E111DEBE25AD341DCB462F7EEB4E9E13248C82B191BE368`, PDB `99C44D58A875EC1253AFC7350757B65905C1E46BC3A65A4C74C29400321A8064`. Installed Core remained `81A06B0A393B00EFFB016F30423FAA65EA1F01AE285C5BB8FA7F6D6086B27979`. This output also retains the preceding search-party changes; the reviewed stale searcher-name display issue remains separate and unfixed.

## 2026-09-21 - Temporary search-party leadership

The Streets record `20260921-180033-TarkovStreets.jsonl` showed Brick following the player under GroupSearch while Nux searched the shared enemy. Between 18:09:24 and 18:09:37 local time, Nux moved from about 52 m to 106 m from the player while Brick stopped about 2.8 m away. The decision detected a searching teammate but the action explicitly targeted the player. The addon now retains that searching bot as a temporary party lead; overall player leadership is unchanged. [Search-party contract](Squad-Support.md#search-party-cooperation).

Debug addon build against the existing Core output passed with zero warnings/errors. **1,127 production addon combat checks** passed, including searcher targeting/naming, stable selection, world-origin movement, ended/replaced/dead/medical/dismissed/tactic-changed/forgotten-contact invalidation, loop prevention, command regroup, independent cooperation, preservation of a newer action's path and prepared Shooter support without repeated scans. **Nine source-parity checks** passed. The changed search assignment/action lifecycle is now behaviorally tested rather than asserted identical to native player/squad-leader following; native steering and nearby-point geometry still compare directly. Diff whitespace checks passed.

No new Harmony hook, decision publisher or broad geometry scan was added. Selection traverses existing squad members at native decision boundaries; movement validates a retained member. Shooter reuses its existing support range checks and shared finder cadence. These checks establish bounded invocation behavior, not measured Unity frame time.

With Tarkov closed, deployed only Debug addon DLL/PDB at **2026-09-21 18:35:25 +03:00**, with no backups. Verified SHA-256: DLL `A665AD541A331A99431D7B53C3B9F7ACFCED8D6BF163B4B9CD4F047614A0AEDE`; PDB `ABD778CD6536ED0E23AAE152191F50BC91389A6CB7221E070953680D345DD9EC`. Installed Core remained `81A06B0A393B00EFFB016F30423FAA65EA1F01AE285C5BB8FA7F6D6086B27979`.

Pending raid qualification: two and three Grunts sharing a hidden target, searcher movement/path completion, interrupted/resumed search, explicit regroup during search, On Your Own and Shooter support from both settled cover and the ordinary native firing-position entry. No claim of in-game navigation or combat outcome validation is made by the fixture results.

## 2026-09-20 - General review: bounded cover planning

Addressed the two reproduced addon performance findings without changing Core, external SAIN source, recording, or live firing checks. Forward planning linecasts and post-regroup cover-to-player routes now share the finder's existing four-operation frame budget with discovery/native validation. Positive and negative planning results are cached for one second with endpoint/context invalidation, retained through an unfinished pass, bounded to 128 entries per cache and cleared on finder reset. Route admission compares the current radius with the cached distance; budget exhaustion preserves ranked pending selection.

Controlled production-code harness results for 32 candidates:

| Work | Before | After |
|---|---|---|
| Forward firing-lane planning rays across a completed initial scan | 144 across eight polls | 32 across sixteen polls; discovery and rays share the budget |
| Post-regroup route probes in one poll | 32 | At most 4, sharing the same budget |
| Routes on the immediate unchanged failed-selection retry | 32 | 0 |

Both 0.05-second and 0.3-second forward polling complete without starvation. Regression checks also cover repeated same-frame polls, cached rejection, expiry/recovery, moved cover/threat/player invalidation, current-radius enforcement and retained pending ownership. Debug addon build: zero warnings/errors. **1,099 addon checks** and **13 source-parity checks** passed; diff whitespace checks passed. These are query-count bounds, not measured Unity frame time. The added dictionaries retain bounded planning results rather than allocating per poll after capacity stabilizes; native query internals retain their existing costs.

After Tarkov closed, deployed the Debug addon DLL/PDB at 2026-09-20 18:05:49 +03:00 and verified SHA-256 against the build: DLL `45CBA162712035E8FA628B2E927098C9E729C594BB56E625BC5C88B89AA6B3D6`, PDB `F72AEC4D2A58C378918DC68933C170CA01093209C1F2D711E1BCEB7730BC74F4`. Installed Core remained `81A06B0A393B00EFFB016F30423FAA65EA1F01AE285C5BB8FA7F6D6086B27979`. This deploy includes the preceding regroup friendly-fire guard and Contact correction. No backups were created. Raid qualification should include dense cover, an unreachable player route, slow decision cadence and moving-player regroup selection.

## 2026-09-20 - Regroup suppression friendly-fire guard

The live `20260920-173238-Interchange.jsonl` captured Brick firing about 14 rounds in automatic regroup at raid time 679.34-680.36 while Medved lost about 33 chest and 34 left-arm health. Player and teammate positions were ahead of Brick near his firing direction. The record has no per-hit attacker attribution or player damage timeline, so it supports a suspected friendly-fire event rather than proving both hits came from Brick.

Regroup now retains native target selection and cadence but adds Core suppression-target and current-muzzle friendly-lane checks before manual suppression starts. Its action checks active bursts before movement throttling and clears suppression at entry, sprint start and stop. The typed trigger hook is scoped to the current addon regroup action and is removed with the addon hook set. Core source is unchanged by this correction.

Validation: Debug addon build passed with zero warnings/errors; 1,018 addon combat checks and 13 native source-parity checks passed. The installed manual-fire method signature was verified. New checks exercise initial target/muzzle rejection, interruption of ongoing bursts, clear-lane recovery, invalid geometry, handoff cleanup, native-action bypass, passive guards, and hook lifecycle.

Performance review: the active-burst guard performs at most two linear passes over the boss/follower list using existing Core geometry. Inactive suppression exits before geometry work; ordinary actions bypass the trigger hook before lane checks. No new physics casts, NavMesh queries, enemy scans, decision/provider calls, logging or clear-lane cache. Compiled hot methods contain no newobj/newarr/box instructions; inspected Core lane helpers use indexed iteration without collections or reflection. This is source/IL evidence, not measured Unity frame time. Crossing allies, moving suppression and safe resumption require raid qualification.

Not deployed during this pass: EscapeFromTarkov was running. The Debug addon output is ready for the next deployment; no backups were created.

## 2026-09-20: prioritized Contact relationship override

User-confirmed friendly Scav in `20260920-052413-bigmap.jsonl`, followed by an explicit Contact order. Medved retained the Scav from raid time 1672.04; Core blocked 665 clears through 1754.09 while SAIN entered/released combat 18 times before normal fighting resumed. That retention reflects the command and is not removed by this fix. The record lacks historical ally/activity flags, so it does not independently establish every native rejection reason.

Source inspection found Contact registering via ambient `checkAddTODO`, which the Core Scav hostile-intent gate may reject even for the explicit command; personal memory/synchronization can still proceed. Native SAIN removes allies from its enemy collection. The addon now supplies Core's existing explicit cause only for prioritized, goal-promoting registration, and reconciles stale ally/neutral membership only after actual group admission. Non-prioritized automatic reports and Core/unready ownership retain the original path. No Core source or SAIN provider is changed.

Validation: **1,004 production addon checks** (18 added Contact checks), **13 source-parity checks**, clean Debug addon build (zero warnings/errors), and scoped whitespace check. The Contact fixture executes the production Harmony adapter with controlled Core/native relationship behavior; installed Core metadata verifies the real registration signature, argument indexes and single enemy-creation call. It covers accepted/repeated Contact, stale ally membership, native cleanup, protected targets, rejected group admission, unchanged ambient/background reports, both addon tactics, fallback and hook removal. This is not proof of Unity raid behaviour or of target activity/standby transitions.

Deployed at **2026-09-20 06:34:12 +03:00**, game closed, addon DLL/PDB only, no backups. Source/destination SHA-256 matched:

| File | SHA-256 |
|---|---|
| pitFireTeam.SAINAddon.dll | `2FE3D2DA18414310590C5D941A2862A1DE1DF2320E4E13BDDF19F85234F0C1C0` |
| pitFireTeam.SAINAddon.pdb | `9F020CC650F18E7E2F04DE3A301F46C122791680C5C8217A112BF9B8B7F684A8` |
| Existing Core DLL, unchanged | `81A06B0A393B00EFFB016F30423FAA65EA1F01AE285C5BB8FA7F6D6086B27979` |

Fresh-raid qualification: a friendly Scav remains ignored without a command; prioritized Contact admits that exact target (`sainContactOverride`), native combat persists through the next enemy update, and real target death releases into normal follow. Native activity and shot-safety gates remain authoritative.


## 2026-09-19 — Bounded visible-fire flicker continuity

- Replaced the open blanket trigger-cutoff proposal with Core-style bounded continuity inside the addon support action. A visible target's `CanShoot` flicker may retain an already-running burst for 0.5 seconds after the last verified native shot. The addon captures its actual aim point/position and rechecks Core direct geometry, target/muzzle friendly lanes, native weapon readiness, 18-degree actual muzzle alignment and 0.75 m stationary tolerance. Grace does not renew itself or restart a finished burst; hidden suppression and support-priority gates are unchanged. No Core source changes or new SAIN patches.
- **986 production addon checks passed**, including 13 added cases for permitted flicker, fixed remembered aim, passive diagnostics, expiry without renewal, both friendly lanes, hard obstruction versus foliage permission, muzzle alignment, displacement, weapon readiness, ended-burst protection, verified-shot renewal and pause ownership. Installed metadata verifies the Core direct-lane binding and public native weapon-readiness API. Debug addon build: **zero warnings/errors**; whitespace checks passed. Actual in-raid weapon timing remains to be qualified.
- Deployed addon DLL/PDB only at **2026-09-19 01:35:33 +03:00** after checking Tarkov was closed, with copied hashes verified and no backups. DLL SHA-256: `04238DB7EC82C1C9320A37F4A96070B0B83E24ED9973B187122C481204CC4691`; PDB: `7C8337E3DC0E26343DD1935E10A1EA6E18AF86450CCB417586851E9A4E3ED978`. Installed Core remained `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`.


## 2026-09-19 — Fresh-report suppression ownership fix

- Fixed the reviewed native-update conflict for `coreRecentReport`: the addon releases native suppression ownership and uses public `ManualShoot.TryShoot` with friendly checking enabled. Native-point suppression keeps `SuppressPosition`. Manual shot/status cleanup follows source changes, loss of permission, command replacement, medical work and completion; point-source changes cannot renew the burst deadline. No new SAIN patches or Core source changes. Existing active-push support gating is unchanged; the separate visible-target `CanShoot` trigger-cleanup finding remains open in [Roadmap](Roadmap.md).
- **973 production addon checks passed**, including 13 added ownership/transition checks covering absent and rejected native points, intervening native suppression updates, friendly-lane loss, trigger rejection, command cancellation, native/report source handoff, fixed burst expiry and boss-support medical interruption. Installed metadata verifies public manual-fire/reset signatures. Debug addon build: **zero warnings/errors**; addon/test diff whitespace checks passed. Runtime navigation, actual weapon timing and raid AI still need qualification.
- Deployed addon DLL/PDB only at **2026-09-19 01:24:46 +03:00**, after confirming Tarkov had closed. Source/installed hashes verified; no backups. This also deploys the previously pending Grunt support changes. DLL SHA-256: `0151216E120A064C9C4C6E4D0668A183FEB8822D0A1B281A37B990AB34C374DD`; PDB: `6E2B1ECBB26400661E0C8CE61623C7E75FA6AD242EC79F68916B0D032082051A`. Installed Core remained `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`.


## 2026-09-19 — SAINGrunt boss and ally support

- Added addon-owned `BossSupport` and `PushSupport` intents, stationary supporting fire, bounded boss-threat suppression, and native cached-cover preference before the existing native firing finder. Boss admission follows Core's hit/willingness/personal-contact/independence gates; push positions reuse Core's existing pure pusher-relative predicate through a cached delegate. No Core source changes or new SAIN patches were introduced by this pass.
- **960 production addon checks passed**, including 35 added checks for boss cues and known-target admission, exact-target fire, firing/retry budgets, medical/order/movement/cover priorities, On Your Own activation/cancellation, native cover recheck movement, bounded geometry and failed planning, ally fire/cover preference, pusher-relative positioning, helper distance and native Squad Help publication. The installed Core predicate signature is verified; its actual source is extracted into the fixture. Debug addon build: **zero warnings/errors**. Addon/test diff whitespace checks passed.
- Built DLL SHA-256: `0640E2D1C7B988AFEE18490CC83DC364D1CFBBDB39B024ABB0B1AEDA825839C1`; PDB: `5EC438F809245424B385CBA14F61471AE24FB8128024ECE241CDADE6D5180BA3`.
- **Deployment pending:** Tarkov was still running at 2026-09-19 01:06:49 +03:00, so no installed files were replaced and no backups were created. Installed addon remained `5AE26BEB72DB99791A432B013F32FEA8D74A2B372DEBA297DFE7072A864A4E40`. Installed Core matches the build reference at `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`.
- Pending raid qualification: real firing-cover availability, native navigation and arrival use, usefulness in mixed Core/addon pushes, boss-threat reactions and frame time. This implements bounded native equivalents, not Core's complete support arbitration or broad geometry search.


## 2026-09-18 — SAINShooter weapon-transition review fixes

- Corrected all four reproduced review findings: medical/survival protection now covers primary restoration and cancellation through both incoming publication context and live native state; defensive preparation ends when close danger leaves while retaining late-callback restoration; ordered suppression has a separate bounded selector-settle phase before its accepted draw; and automatic destinations are revalidated once at the readiness-to-movement handoff.
- Validation: **925 production addon checks passed**, including 20 added transition checks for medical/grenade cleanup and resumption, late defensive draws, selector settling/timeout, separate draw and suppression execution budgets, changed enemy separation, invalidated route, and no repeated handoff path checks during movement. Installed Core binding verification includes the reused selector-settled predicate. Debug addon-only build: zero warnings/errors. Addon/test whitespace checks passed. Core source was not changed in this fix.
- Deployed DLL/PDB only at **2026-09-18 23:58:13 +03:00** after confirming Tarkov was closed; no backups. DLL SHA-256: `5AE26BEB72DB99791A432B013F32FEA8D74A2B372DEBA297DFE7072A864A4E40`; PDB: `ADBE132E086BE73D8D85E3C93FD9E3D6DBE80DBF725A97BBC6DE46539A75EB8A`. Copied hashes verified. Installed Core stayed `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`.
- Raid qualification remains required for actual hands/medical transitions, secondary versus holster animations and Unity navigation. Fixtures establish state ownership and gates, not runtime animation or frame-time behavior.


## 2026-09-18 — SAINShooter automatic secondary/holster support

- Added Core-backed automatic support weapon eligibility, one accepted asynchronous draw, three-second preparation and four-second failed-request retry, readiness-gated native close-search movement, defensive close-threat retention, primary return, and automatic-secondary ordered suppression. Core source was not changed.
- Validation: 905 addon checks passed, including switch timeout/late callback, shot interruption, no-destination/no-draw, zero-aggression defense, ammo-threat rejection, pending medicine, suppression preparation and scoped candidate-hook tests. All new reflected Core signatures and the single command exclusion were verified against the installed Core assembly. Native replica parity: 13 checks passed. Extracted Core Marksman reference: 37 boundary checks passed. Addon-only Debug build: zero warnings/errors; addon/test diff whitespace check passed.
- Deployed addon DLL/PDB only at **2026-09-18 22:34:40 +03:00**, after confirming Tarkov was closed. No backups. DLL SHA-256: `1C5A443F75658E77A94E6F4F97D3F2F996BC0B07F11F6D30B3D8C7FCF2D29E63`; PDB: `A6D5FB98425E6B4525A812F21E5E9F4111AB0317A92B8BF3B453DC7F861A902E`.
- Installed Core remained `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`. Existing attribution retained. This deployment also includes the previously built protective Grunt cover preference.
- Pending raid qualification: second-primary versus automatic-holster hands transitions, native close-search candidate availability, interrupted draws/medical transitions, return to primary, ordered suppression timing and actual navigation/frame time. Defensive movement and autonomous suppression remain native SAIN; Core's broad geometry planner is not copied.


This is an evidence ledger, not a live installation status. Entries below preserve previously recorded build/test/deployment results. Recheck hashes and the working tree before deploying; the documentation reorganization itself builds or deploys no binaries. [Combat](Combat.md), [Commands](Commands.md) and [Integration](Integration.md) own current behavior.

## Recorded 2026-09-18 SAINGrunt protective-cover preference

User-reported behavior: ordinary SeekCover could favor a nearby rear position and leave the player in front. This pass changes the requested policy; it does not attribute a specific raid's candidate rejection without a new record. Core was rechecked at the complete cover lifecycle, `TryFindBossCover`, boss-under-attack support, `IsSupportPositionBehindBossLine` and the destination fire-lane geometry. Native `CoverAnalyzer` retains cover validity, enemy separation, complete-path and maximum-path checks.

The addon gives ordinary dependent SAINGrunt cover selection a bounded forward-side preference, using the existing candidate pool and path lengths. It favors cover in the player-to-known-enemy sector, outside the central firing lane and inside the existing player area/route bound. Existing nearby/player ranking breaks ties and supplies fallback. Shooter, explicit Come here/push ranking, independent mode, emergency/medical selection, claims, post-regroup constraints and committed travel/arrival retain their existing contracts. No Core source or native SAIN algorithm was changed. See [Combat](Combat.md#cover-and-arrival) for the exact policy.

Validation: **879 addon combat checks** passed, including 22 new checks for forward versus rear selection, known-position geometry, alternate bearings, shortest route within the preferred group, fire-lane/wide-side/long-detour/boss-line exclusions, beyond-enemy cover, native rejection, reservation/movement failure, regroup limits, Shooter, independence, survival, gestures, stable travel/arrival and unchanged query/probe counts. The addon-only Debug build passed with zero warnings/errors. The fixture retains its existing stand-in compilation warnings; live geometry, useful cover availability and frame time remain raid qualification items.

Deployment was attempted with a process preflight and **not performed because Tarkov was running**. No installed files or backups were written. The validated Debug build is ready for deployment after the game closes; the live install still has the preceding suppression-fallback build from this session.

## Recorded 2026-09-18 suppression report fallback

In `20260918-180049-Shoreline.jsonl`, Nux accepted `SuppressEnemy` at local **18:11:58.815** (seq 1292), consumed it and began the Squad objective at **18:11:58.898** (seq 1301), then ended with `attemptComplete` at **18:12:04.896** (seq 1371). Thirty snapshots during the action show no firing; ammunition was available, with no medical/incoming-fire interruption. Native visibility was false although shoot/LOS flags were true. Core sensed-report age became fresh later in the attempt while SAIN's remembered point stayed old. The old recorder omitted native suppression-point and lane/trigger rejections, so the record alone does not establish foliage as the sole cause.

The addon now resolves an admissible native point first, then reuses Core's exact two-second suppression report policy for the same non-visible EFT contact. Both paths check the Core obstruction mask, foliage-only classifier and friendly lane immediately before native `SuppressPosition`. It preserves global native suppression enablement, zombie exclusion, native alignment/weapon/friendly/trigger safety, target identity and bounded lifetime. Failed alignment/trigger no longer overwrites the admitted point with another native look point. Planning lane checks retain a two-second cadence. Cached `sainSuppressionFire` diagnostics distinguish target source/point, lane result, no fresh report, movement/ammunition gates, alignment and native trigger rejection; snapshot reads perform no additional work. Core source and combat behavior were not changed.

Validation: **857 addon combat checks**, including the production Core report policy, and **13 native-source parity checks** passed. Tests cover fresh sensed/personal/visual report pairing, expiry/future timestamps, no hidden-live fallback, identity/global/zombie gates, foliage/hard/friendly lanes, stable alignment, native trigger rejection, scan cadence and passive recorder reads. Installed Core resolver and native `SuppressPosition` metadata were verified. The addon-only Debug build passed with zero warnings/errors; the fixture retains its existing stand-in compilation warnings. Unity trigger behavior and raid geometry remain unqualified.

Deployed Debug addon DLL/PDB at **2026-09-18 18:27:47 +03:00**, with Tarkov closed and matching installed/output hashes. Core was not rebuilt/copied and retained SHA-256 `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`. Existing attribution was retained; no server/resources or backups were written.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.SAINAddon.dll | `F029DBE1F935917EF60D3CE9B910CF801A937F41337B517F27B89519FE3D753D` |
| pitFireTeam.SAINAddon.pdb | `4B00904339F65E4378431FF1D474BCE63656C7B499068972D4CAEC7BFB5CC226` |

Next raid: repeat an ordered suppression against a reported enemy behind foliage, then a hard obstacle and friendly crossing. Inspect the new execution gates if the native trigger still refuses the admitted point.

## Recorded 2026-09-18 squad suppression and ally support

Implemented the bounded [squad support contract](Squad-Support.md): SAINGrunt's firearm suppression orders and both roles' prepared ally firing support. All new intent/action routing lives in the addon Squad layer. The Marksman objective shares its native finder/timer and preserves failed positions across squad interruptions. Core source/combat was not changed for this work.

Validation: **838 production addon combat checks** and **13 native-source parity checks** passed. The runner also verified installed SAIN's private exact-target aiming signature and installed Core's suppression command/weapon/lane bindings. Regressions cover exact target retention, bounded waiting for contact, actual-trigger burst timing, shared lane protection, replacement/death/medical interruption, inert cancelled actions, real Squad action routing, player/Core teammate/Core push/addon push cues, failed positions, independence and the shared Marksman scan budget. The fixture retains its existing stand-in type/unread-parameter warnings. The addon-only Debug build (`BuildProjectReferences=false`) passed with zero warnings/errors; Core output matched installed Core. These are controlled fixtures and source/API checks, not Unity navigation, raid trigger or frame-time qualification.

Deployed only the Debug addon DLL/PDB with Tarkov closed; verified at **2026-09-18 17:43:32 +03:00**. Both installed hashes matched the built outputs. Core was neither rebuilt nor copied and retained SHA-256 `11717BD79D78E296A9465804B172745A7AF455B7A87EDD91829E3FA608AE024E`. No backups were created. Existing installed `SAIN-LICENSE.txt` was retained and verified byte-identical to the native source LICENSE; its missing working-tree/output copy remains the pre-existing packaging issue recorded in [References](References.md). Server/resources were not deployed.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.SAINAddon.dll | `2A858C135E27595BDD66BF6E628B194B24099190A6044DEA49080537D2B9B639` |
| pitFireTeam.SAINAddon.pdb | `4C5946425A662BC963313E241240F765AA56CDC132D86EC72E6BEA1B2D660B98` |

Next raid: suppress visible and hidden known enemies, cancel during firing, block the lane, and test both tactics supporting an engaged player/teammate from settled cover. Check `sainSquadSupport` and its passive objective snapshot for chosen source/target/destination/end reason. Qualify mixed-role navigation and frame time; broad friendly-fire investigation, grenade launchers and Shooter's automatic-secondary ordered suppression remain outside this implementation.

## Recorded 2026-09-18 nearby-first SeekCover preference

In `20260918-045909-bigmap.jsonl`, the third main fight starts around local 05:04:34 (sequence 1174 onward). Nux and Zero selected native fallback cover at 05:04:35 / 05:04:36 after 32 discovery probes, about 50 m / 58 m from the player while the followers were only 15 m / 19 m away. Neither was under fire, injured or independent. Cover travel blocked automatic regroup; Zero regrouped after invalidation at 05:04:48, while Nux arrived and completed the three-second hold before regrouping at 05:04:54. Nux selected another outward native cover at 05:05:14 because Reload used the recovery bypass. The record does not establish the individual candidate rejection reasons or the building interior.

The requested addon policy now prefers the shortest safe nearby route (25 m, 12 m on Factory/Labs), then the closest safe player-area cover, then native SAIN fallback. Routine reload shares that ordering. Core source and combat policy are unchanged; the addon mirrors its internal combat-start ranges using the current map. Emergency/medical recovery, independence, completed-regroup constraints, native validation, reservations, committed movement, and command-specific relocation keep their boundaries. Selection reuses existing candidates and probe budgets without additional navigation or physics queries. Current behavior is documented in [Combat](Combat.md#cover-and-arrival).

Validation: the new nearby-first regression failed against the old policy. **785 production addon combat checks** then passed, including map boundaries, route detours, closest-player ranking, rejected movement, unsafe/occupied cover, reload and emergency handling, independent/native fallback, preserved Come here ranking and existing frame-budget/arrival/regroup tests. The fixture retains its existing stand-in type/unread-parameter warnings. The Debug addon-only build (`BuildProjectReferences=false`) passed with zero warnings/errors against Core output matching the installed Core hash. No native replica code was changed. Unity navigation and raid behavior remain unqualified.

Deployed only the Debug addon DLL/PDB at **2026-09-18 05:38:13 +03:00**, with Tarkov closed. Both installed hashes matched the outputs. No backups were created. Core was neither rebuilt nor copied; its installed SHA-256 remained `238F5963F3D6D807B707FDB037A0525BCF0D35F082D6BB6303F2E15EC7D1BF97`. Server/resources were not deployed.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.SAINAddon.dll | `7684C8AEB26A3BEAFF442B75F2B7EA83F518D746B343EB30EF81E0AABB05CC77` |
| pitFireTeam.SAINAddon.pdb | `9B831218F4E51FC2657315170D3161021B31A762B784F53E13B39DB04C6166CE` |

## Recorded 2026-09-18 passive-cover regroup correction

Record `20260918-022414-Shoreline.jsonl` was inspected through sequence 2244 / local 02:38:47, without a raid-end event. The recorded Grunt is Brick, alongside Zero (SAINShooter). In the fight after the marksman, Brick reached 82.46 m from the player, with no shooting in the pre-regroup combat snapshots. His ordered push failed at about 02:27:41 (`approachStepInvalid`) but retained its ordered flag. A player hit at 02:27:56 produced `bossRangedThreatWatch:longRangeBossHit`; his personal target contact was then about 53 seconds old. Against Partisan, the pre-command gaps reached 103.66 m (Brick) and 67.88 m (Zero), both stationary in valid cover with no current visibility or medical work. Only commanded regroup activated. The old record omits automatic rejection reasons and native LOS, so it cannot establish every earlier veto. Dead-goal cleanup from the previous correction is recorded for both followers after kills.

Core was rechecked at `FollowerCombatRiflemanEngagement.Evaluate`, `FollowerCombatDefault.ShouldDeferBossDistanceRegroupForCommitment`, `FollowerCombatCommon.ShouldBreakCommittedCoverForBossObjective` / `ShouldDeferAutonomousRegroupAfterRecentFight`, the Marksman regroup path, and the ordered-push objective. The addon now preserves useful visible-and-shootable contact but lets passive non-shootable sight use bounded recent-fight grace; geometric native LOS alone no longer vetoes regroup. Its exhausted-order adaptation permits same-target bossward recovery while retaining the failure latch, target and contact-grace safeguards. Assigned recovery-cover movement, native useful fire, survival, arrival holds, completed-regroup constraints and On Your Own retain priority. Core Rifleman's player-hit support also requires recent personal contact; no blanket hit-triggered movement/target promotion or full player-support planner was added.

Validation: the added cold-LOS regression failed against the old policy, then **765 production addon combat checks**, **13 native-source parity checks**, and a matching Debug Core/addon build passed (zero build warnings/errors). The harness has its existing stand-in type/unread-parameter warnings. Regressions include hidden-lane passive holds near/far, grace expiry, other-enemy usable shots, rejected Grunt advancement, the distant Shooter cover hold, exhausted-order recovery without resetting its failure, survival/independence, assigned recovery cover, and cached diagnostic reads. Recorded fields now include the actual auto-regroup evaluation reason/time/distance/trigger and native sight state without extra decision or navigation evaluation.

Deployed the matching current-working-tree Debug Core/addon DLLs and PDBs at **2026-09-18 03:31:27 +03:00**, with Tarkov/SPT.Server closed. All four installed hashes matched the validated build outputs. Previous installed files and a hash manifest were backed up outside the game directory in `pitFireTeam-before-regroup-correction-20260918-033127`. Server/resources were not deployed. This is not an isolated single-fix release build or raid qualification. Next test should verify spontaneous bossward recovery from settled unproductive cover, preserved active fighting/medical/arrival commitments, no repeated failed outward push after regroup, and the exact new rejection reason if any follower still stays behind.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `07DE20EB487DE0F51A2571F8D6694A3870E339C1B3D31307E09AE336FAA2416D` |
| pitFireTeam.pdb | `E4906C72903486ED1E10BB0AD0F8EA2B7C04E5715427A46FBE80BF39BA0DA40C` |
| pitFireTeam.SAINAddon.dll | `E7322792CCE7625F47A9E59900A3842D994BF1D14223D900CB107B72AAF3C090` |
| pitFireTeam.SAINAddon.pdb | `2A365DD65D36E41B3EB23BFF3700A2069A429DCB34F3B07CF9F934B12B9B8790` |

## Recorded 2026-09-17 dead-goal squad synchronization correction

In `20260917-180200-Shoreline.jsonl`, Zero's accepted enemy died at about local 18:03:09. The addon released combat after its three-second deadline, but the dead EFT goal remained while Nux retained a living accepted enemy. Core's existing squad-report recipient gate requires a null goal, and its dead-goal cleanup excluded ready addon followers. Need Sniper explicitly replaced Zero's dead goal at 18:03:27; the linger action then ended after 17.327 recorded seconds. The correction removes only the addon exclusion from Core's invalid-goal cleanup, reopening the existing stable squad-report path without changing living native memory, admission or firing gates. Patrol readiness and linger timing are unchanged.

Validation: the focused regression failed before the fix at `AddonDeadGoal_ClearedWithoutReplacement`, then **54 production-method handoff checks** and **748 addon combat checks** passed. Checks cover stale dead/removed goals becoming eligible for squad reports, preserving living goals and tracked contacts, Attention suppression, unscoped memory-only rejection and scoped report acceptance. The matching Debug Core/addon build passed with zero warnings/errors; addon fixture compilation emitted its existing stand-in type-conflict/unread-parameter warnings. These checks do not simulate the native scheduler or establish raid behavior.

Deployed the matching current-working-tree Debug Core/addon DLLs and PDBs at **2026-09-17 23:00:59 +03:00**, with Tarkov/SPT.Server closed. All four copied hashes matched the build outputs. The previous installed files were backed up with a manifest outside the game directory. Server/resources were not deployed. This is not a release package or an isolated build of only this turn's source change. Next-raid qualification: after one follower's target dies while another keeps a living accepted enemy, observe `deadGoalEnemyCleanup` and the existing `contactEnemy:fillEmpty` promotion without needing Need Sniper.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `A2FF88AFD0433B643B5CF3D582F25ACD960040F1B487DF59A172E33EFE4B8072` |
| pitFireTeam.pdb | `1BA21C7D3508DEC04E1AD7E28EFF5DF461CED3DCE4EC679336D73833A078D99B` |
| pitFireTeam.SAINAddon.dll | `F82A4BB52F4CA071C774D7C297E200757C2C9C34518F4F235CABED428C183E06` |
| pitFireTeam.SAINAddon.pdb | `9D3C0A29444684D1D01D2A7A105552D894C14805B9279488C54E5ADB50C2B6AB` |

## Recorded 2026-09-17 Shoreline movement corrections

Record `20260917-030354-Shoreline.jsonl` contains 11,386 events, including 7,995 snapshots. Zero used SAINShooter; Nux used SAINGrunt. Eight Need Sniper commands were consumed, but the same-contact latch suppressed replanning after failed/empty positions. At local 03:13:27 an order reused a position; arrival failed at 03:13:32, and the 03:13:42 order retained that failure. Nine native firing positions were selected across the raid, alongside 22 undifferentiated no-position results. The correction uses bounded native retries, distinct-position admission and four failed-position slots rather than a whole-enemy ban; snapshots now record candidate/rejection/retry details.

Of 522 Nux snapshots with Push Approach and the shared MoveToEngage action, 521 were not sprinting. The action's `!pushing` gate prevented native run requests. Long committed legs now request native sprint when not under fire, retaining native walking fallback. Regroup separately rechecks personal sight while executing: after 2.5 seconds without personal contact, long movement can run. Native LOS, own firing and group updates no longer renew withdrawal; run steering stops initiating suppression. The broader automatic-regroup admission grace remains unchanged.

Validation: **748 production combat checks** (16 new checks plus updated expectations), **13 source replica comparisons**, and a matching Debug core/addon build with **zero warnings/errors** pass. Checks exercise ongoing walk-to-run-to-withdraw transitions, native sprint for a committed push route, stationary retry throttling, failed-position jitter, admission of a different position, stale movement-cache replacement, deferred support during cover travel, ordered retry permission and the finite four-position budget. Native Unity geometry, practical sniper usefulness and measured frame time remain raid qualification items.

Deployed at **2026-09-17 05:11:32 +03:00**, with Tarkov/SPT.Server closed. Four client/addon DLL/PDB hashes match the source outputs; the matching Core pair was unchanged. Installed SAIN attribution was preserved. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-sain-movement-fixes-20260917-051200`.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `DC072515F9D288BE2D8E382267467327E57A711CFC025748CA054DB9C034D5CC` |
| pitFireTeam.pdb | `97CBA3112C22BA9C5931EE7D17F607D881F0C7219B056C9B69FB6AF88B84D310` |
| pitFireTeam.SAINAddon.dll | `1FC599AD9FCFFED52539A9B1C8A35F3A162CC4C92120832D9CEF34CB0DBEE715` |
| pitFireTeam.SAINAddon.pdb | `1BEF4F7B8B5F2C1DDF8AE16865EC5671DAD536C9E46B590C9628C817576960E6` |

Medical investigation from the same raid: `Wants to heal` comes from Core's passive pending FirstAid/Surgery flags, not a selected native medical action. Nux bled while native contacts had `inLineOfSight=true`; the follower protected-cover exception recorded cover-unready, active threat, exposed cover and pressure. He began post-combat first aid at approximately 03:21:12 with about 294 HP. Recorded statuses were Healthy/Injured for both followers; this raid does not qualify BadlyInjured/Dying behavior. Native `ShallFirstAidCheckEnemy` rejects LOS before its health-specific thresholds, so critical health alone cannot bypass that gate. No healing policy or status wording was changed by this correction.

## Recorded 2026-09-17 SAINShooter implementation

[SAINShooter](SAINShooter.md) implements the bounded native Marksman adaptation, including tactic selection/persistence, Core Marksman fallback/proficiency, native firing-position support, Need Sniper, shared relocation/regroup and role lifecycle. The earlier broader analysis is superseded by that implemented contract; wider Core geometry, support-weapon transactions and exact action parity are deliberately outside its scope.

Validation: **732 production addon combat checks**, **61 leadership/ownership checks**, **13 replica source comparisons**, **49 compatibility checks** and **38 proficiency checks** pass. The 35 new combat checks exercise native-proposal translation, bounded same-contact planning/execution, pending survival/medical work, useful fire, native suppression, command replacement, old movement-destination retirement after regroup, role switching and Core fallback. Leadership checks cover selection/storage/default aggression and real Harmony player-leader membership/opt-out for Shooter. The installed SAIN 4.5.1 public firing-position finder signature is verified; native candidate geometry remains controlled fixture input. Matching Debug core/addon and server builds pass with zero warnings/errors. English localization, addon documentation file links and `git diff --check` pass.

Deployed at **2026-09-17 02:27:35 +03:00**, with Tarkov and SPT.Server closed. Eight installed files match their source SHA-256 hashes. The pre-existing installed SAIN attribution notice was preserved unchanged. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-sainshooter-20260917-022609`. This is a Debug deployment of the current working tree, not a release package or a claim of isolated unrelated worktree changes.

| Installed file | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `DC072515F9D288BE2D8E382267467327E57A711CFC025748CA054DB9C034D5CC` |
| pitFireTeam.pdb | `97CBA3112C22BA9C5931EE7D17F607D881F0C7219B056C9B69FB6AF88B84D310` |
| pitFireTeam.SAINAddon.dll | `A6F87404957730EECAA39A72C62C40365039631262F65780359BB0AE1BF4C93C` |
| pitFireTeam.SAINAddon.pdb | `585A9B9345DFD727FC3B679325DD6DA563DC37FF91C102BD8E01C3B6708E4935` |
| pitFireTeam.Server.dll | `A5706A2732C0212B118A47AE5A6C26125F1C08CFD7751E320CB4ED4617B94721` |
| pitFireTeam.Server.pdb | `6DA3894B42C990E7443F8B872E9FE389B500C52D6EBB34583B61594DD0DA1119` |
| pitFireTeam.Server.deps.json | `2153F06CCE9447DB2D74423E07E66D7383C5AFFC5C2BC1E10E7B818564312FAC` |
| server Resources/lang/en.json | `95894CE406E19D17B7EFAFF1813752A0C860F7CB59959D9B304DF74F89A166B9` |
| SAIN-LICENSE.txt (preserved) | `7047B3162F2663E5DCC062FB2AFF07E00E26DBA13BB19D722CF8A24D8466DC57` |

In-raid navigation, firing-position usefulness, command transitions, mixed Grunt/Shooter behavior, shot safety and frame time remain unqualified. The implementation adds no Harmony hook or extra Core candidate scan; this bounds added work but does not establish measured runtime performance.

## Recorded 2026-09-17 combat-gesture checkpoint

Validation: 697 production addon combat checks (48 new gesture checks) pass, including real production command setters/timeouts, publication/layer routing, cover/progress/fallback geometry, navigation failure, reservations, scan budget, arrival, interruption and cleanup. Matching Debug core/addon build passed with zero warnings/errors; 54 leadership/ownership checks and 13 replica comparisons pass. Both extracted Core path methods compare unchanged to their prior bodies. Deployed matching Debug core/addon DLLs and PDBs plus license on 2026-09-17 00:17:50 +03:00 with Tarkov closed. All five installed SHA-256 hashes match. Backup and manifest: `C:/Users/alexa/AppData/Local/Temp/pitFireTeam-before-combat-gestures-20260917-001749`. In-raid navigation and combat presentation still need qualification.

Combat-gesture deployment hashes:

| File | SHA-256 |
| --- | --- |
| pitFireTeam.dll | `CA2941FA165A37EFFB15F9DF5000FF506CF6783ECC6338138187A1EAF1249443` |
| pitFireTeam.pdb | `E925510144EC2EF91F355EEA7D1DACFACD85EA1956220395500A06A6BD0693B3` |
| pitFireTeam.SAINAddon.dll | `8C4CCBF7F0BD330CB5269F6E82A13C2418C96B713C2D3ED8D60FD65451DA4E61` |
| pitFireTeam.SAINAddon.pdb | `099C08EABC3640A5AEA8C96AD6FD77CF5703B38C48F7E821C4017AE5F36EBA91` |
| SAIN-LICENSE.txt | `7047B3162F2663E5DCC062FB2AFF07E00E26DBA13BB19D722CF8A24D8466DC57` |

## Recorded 2026-09-16 ordered-contact checkpoint

Shoreline `20260916-183446-Shoreline.jsonl`: Go Forward at 1939.037 starts Brick's ordered push. Native last-known knowledge disappears at 1949.327 and the addon clears it as `targetLost`; EFT goal briefly clears at 1949.35986 and returns at 1949.37659. The same native contact is back by 1949.47656, but Brick creates an Automatic push at 1949.57666, fails its risk score and regroups before SeekCover. Medved retains Ordered mode. This is an addon intent-lifetime bug.

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

## Fixture coverage

- `tests/Verify-SainAddonCombat.ps1`: **569 production combat/personality/cover/handoff/status-marker/objective/risk checks**. The fixture uses real SAIN.Preset.Shared 4.5.1 settings/enums and Unity's netstandard facade, controlled game/native instances, and real Harmony publication boundaries. Covers every scoped combat anchor setting, excluded speech/assignment/mechanics, accepted Go Forward dispatch and core fallback, all interpolation segments, discrete boundaries, temporary overrides, native caches, preset/component replacement, rollback/restoration, follower isolation, recorder serialization and retained engagement failure, alongside the existing combat extensions. Investigation/recovery regressions cover rejected native-only contacts, both independence modes, command preservation, medical/urgent exceptions, lifecycle cleanup and passive non-goal medical context. Cover regressions exercise the production selector/finder with controlled native geometry, the real Harmony bridge, bounded discovery, boss preference, native fallback, arrivals, orders, invalidation and destination ownership.
- Twelve raid-derived checks cover the fixed medical handoff deadline, treatment completion after expiry, rejected self-action publication, core-owned recovery recording, renewed combat and missing/dead accepted goals for markers.
- The 34 risk additions cover shared threat results across 300 combinations, equipment/role/cluster influences, player pull, pending treatment, badly injured health, active weapon and ammunition gates, cautious ordered cover, risk holds, commitment preservation and urgent recovery.
- The 56 push additions cover objective command capture and replacement, native target preference/publication, target binding, cover-first approach and provisional upgrades, stationary arrival action, native firing/urgent/medical priorities, paused execution budgets, failed-attempt retention, different-target regroup safety, independence, cleanup and shared core geometry.
- The 19 marker checks execute the production contact provider and extracted production marker synchronization/resolution/refresh methods. They cover stale EFT selection, hidden movement, fresh knowledge, visible-to-hidden transitions, forgetting/cleared places, target release/death, invalid positions, shared followers, tactic opt-out and core/unready fallback. UI rendering itself still needs raid verification.
- Installed SAIN metadata validates **11 resolved native action constructors**, the decision publisher, native target-selection result boundary, cover selection method/sprint field, personality setters/timers, cached talk refresh and SearchAction sprint fields.

These are descriptions of the earlier 569-check checkpoint, not the latest count. Later documented runs reached 627, 649 and 697 addon checks, with 54 leadership, 49 core compatibility, 38 proficiency and 13 source-parity checks recorded at their respective checkpoints. A passing fixture is not evidence of Unity navigation, real medical use, shot distribution or measured frame time.

## Additional recorded checkpoints

| Historical DLL (2026-09-16 16:42) | SHA-256 |
|---|---|
| pitFireTeam.dll | `4353A14D771475A79259A6BB4AA9CB90E7B1AA5F82C56B570FE7BCD8142C704A` |
| pitFireTeam.SAINAddon.dll | `F31EA52D7DEFD6AD1740336DBEAC67459635D2B58AC567DC01CCDB8ED9813914` |

These binaries include addon-owned SAIN hooks with typed references, automatic SainMan selection for picked-up SAIN bots, the post-regroup cover-area constraint and quiet publication handoff, follower body-first SAIN aiming, core shared SAIN vision-loop recovery, aggression-interpolated personality settings, boss-oriented cover selection/arrival use, accepted-goal combat entry, core recovery handoff with a fixed medical linger deadline, passive medical diagnostics, accepted-goal-gated native-knowledge Status Report markers and the follower push objective with Rifleman risk assessment and automatic regroup from rejected advancement. A restart/new raid is required. Unity movement/presentation, temporary-command behavior and the complete in-raid lifecycle still need qualification.

Deployment backup: `C:\Users\alexa\AppData\Local\Temp\pitFireTeam-before-addon-ownership-20260916-054848`. Matching Debug core/addon DLLs and PDBs plus license were deployed at 05:48 on 2026-09-16, with all five SHA-256 hashes verified. Validation passed 569 combat, 52 leadership/ownership, 13 parity, 49 compatibility and 32 proficiency checks.

## Raid evidence and limits

`20260914-042148-Interchange.jsonl` is complete (3,355 events). At raid time 898.547 the addon entered SeekCover and then Search with native contacts but no EFT goal; the first accepted EFT goal in that episode appears near 929.98. Attention cleared the remaining enemy state and released Search at 993.788. This motivated the accepted-goal gate.

The same raid records manual Force Heal at 923.212 and 981.664 in `LogOutput.log`. Automatic native first aid did execute around 940.64â€“948.46 and 953.91â€“961.72, with remaining damage and surgical work afterward. The addon never enabled `postCombatFullHealActive`, and its release paths lacked the existing full-recovery bridge call; that omission is repaired. The separate combat delay cannot be assigned to a particular non-goal threat, item, or treatment failure from the old record. New medical inputs improve that evidence without changing combat-heal policy. The earlier suspected friendly fire also remains unconfirmed; this change does not alter suppression safety.

`20260914-033024-Shoreline.jsonl` captured Medved's native SAIN actions and death (836 events). At 250.889 he was 33.4m from the player but selected cover 68.8m from the player, walked about 47m along its route, then activated automatic regroup at 262.379 with `passiveCoverHold`. This motivated the boss-cover/arrival-use extension. The record does not prove that suitable player-area cover existed; the new events expose the selection policy for subsequent testing.

The same recording exposed two separate findings outside this cover change: the Go Forward aggression override persisted into a later fight, with patrol readiness repeatedly postponing its clear deadline, and native first aid continued after the decision changed to Dog Fight. The inspected SAIN `TryCancelHeal` has commented-out cancellation calls. Neither issue is fixed by the cover work; neither proves that the recorded headshot death was preventable.

The completed `20260913-225814-Shoreline.jsonl` contains 139 events, including one follower death, but no normal combat snapshots/decisionSelected/combatStart events. That recorder version did not recognize addon combat episodes. It cannot retrospectively prove the reported MoveToEngage rearming loop.

Medved used SainMan. Commanded regroup began around raid time 260.767 and ended at 269.323 before linger. One recorded automatic regroup ran from 346.069 to 350.122. At 382.308 the cultist priest killed him with a chest shot; the death snapshot reports addon SoloCombat / SeekCover, with the player about 28.5 m away. This does not establish that regroup or MoveToEngage caused the death. Schema 13 now supplies the previously absent native decision/action/path sequence for the next recording.


## Run validation

Use machine-local paths from `LOCAL.md`. These commands name existing scripts; run the checks appropriate to the changed boundary rather than treating historical counts as requirements:

```powershell
dotnet build client/pitFireTeam.csproj -c Debug -p:BuildSAINAddon=false
dotnet build addon/pitFireTeam.SAINAddon.csproj -c Debug -p:BuildSAINAddon=false
./tests/Verify-SainAddonCombat.ps1 -RepositoryRoot '<repository>' -GameRoot '<game>'
./tests/Verify-SainPlayerLeadership.ps1 -GameRoot '<game>'
./tests/Verify-SainReplicaParity.ps1 -RepositoryRoot '<repository>' -SainSourceRoot '<SAIN source>'
```

Check each script's parameter declaration before execution. General compatibility/proficiency checks belong to [Core SAIN compatibility](../../docs/SAIN-Compatibility.md).
