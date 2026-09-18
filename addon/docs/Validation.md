# SAIN addon validation and raid evidence

This is an evidence ledger, not a live installation status. Entries below preserve previously recorded build/test/deployment results. Recheck hashes and the working tree before deploying; the documentation reorganization itself builds or deploys no binaries. [Combat](Combat.md), [Commands](Commands.md) and [Integration](Integration.md) own current behavior.

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
