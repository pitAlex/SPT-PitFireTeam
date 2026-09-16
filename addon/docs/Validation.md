# SAIN addon validation and raid evidence

This is an evidence ledger, not a live installation status. Entries below preserve previously recorded build/test/deployment results. Recheck hashes and the working tree before deploying; the documentation reorganization itself builds or deploys no binaries. [Combat](Combat.md), [Commands](Commands.md) and [Integration](Integration.md) own current behavior.

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
