# SAIN addon progress and session handoff

Updated: 2026-09-14. Current branch: `1.0.0`. This is the current implementation ledger; the original investigation is retained in [the historical rework plan](docs/SAIN-Addon-Rework-Plan.md).

## Current state

SainMan is an opt-in follower tactic when the external SAIN plugin and our addon are available. A ready SainMan follower uses two addon-owned replicas of SAIN 4.5.1 PMC combat: solo at priority 74 and squad at 75. Other tactics and absent/unready addon state retain core combat. The human player is the squad leader and tactical anchor; no substitute bot represents the player.

Aggression-based personality settings are implemented: **100% GigaChad, 70% Chad, 50% Normal, 30% Rat, 0% Coward**. The addon blends General/Search/Rush/Cover and tactical AggressionCoef between loaded-preset anchors; combat switches and native identity use the nearer anchor, with ties choosing the higher one. Speech, assignment and mechanical difficulty stay neutral; begging, fake death and taunting stay disabled. Combat HoldPosition applies temporary 0%, GoForward applies temporary 100%, and Gogogo restores saved aggression. Each follower owns a complete private settings copy. [SAIN personalities and aggression](docs/SAIN-Personalities-and-Aggression.md) records the implementation choices, source findings and qualification limits.

## Implemented progress

| Area | Current implementation |
|---|---|
| Phase-one cleanup and tactic selection | Removed the obsolete one-layer addon and legacy general patches. SainMan selects both replicas through per-follower readiness; the saved selection survives fallback. Native internal actions are resolved/validated once. |
| Player leadership | Core `SainPlayerSquadBridge` maintains real membership, human leader identity/liveness/distance and cleanup. `LeaderComponent` remains null because its type cannot represent a human. Ordinary squads retain native election. |
| Decision publication | Core `SainSquadDecisionBridge` dispatches the follower's native squad-provider call to the addon and filters one already-calculated result for fallback. SAIN's native manager publishes decisions/events once. Handled None goes to native solo selection. |
| Accepted-goal entry | Ordinary solo/squad combat requires a core-accepted living EFT goal. On Your Own allows native investigation; existing patrol/requested independence is initialized on combat entry. Medical/urgent exceptions and living memory are preserved. |
| Status contact tracking | Ready SainMan reports native selected contact and last-known position through the passive `SainEnemyContact` bridge. Hidden movement is not revealed; forgetting/release removes the report, while other followers can retain the shared marker. Fresh native sight retains the red reticle. Core tactics and kill-marker timers are unchanged. |
| Full recovery handoff | Linger completion and explicit release begin core post-combat recovery; renewed combat cancels it. Native combat-healing selection remains unchanged and is diagnosed passively. |
| Solo and squad lifecycle | Both replicas retain native routing around explicit extensions. Native urgent-threat/flash reactions remain above them; core owns patrol and ordinary requests. Player-aware regroup/group-search actions use the real leader. |
| Post-combat linger | `SAINFollowerCombatHandoff` shares one three-second timer across both replicas. The dedicated linger action cancels inherited path/fire, keeps horizontal look and scans once before handoff. Renewed combat/urgent threats interrupt it; stale dead targets cannot restart it. |
| Personality | `SAINFollowerPersonality` blends combat settings into a private copy at five loaded-preset aggression anchors; core installs/restores them and refreshes native caches. Stable input does not reroll timers. Hold Position applies 0%, Go Forward applies 100%, Gogogo restores saved aggression. Speech/assignment/mechanics are excluded; proficiency and engagement attempts are preserved. |
| Proficiency | Core `FollowerSainEftCoreProjection` repairs missing finalized vision/scatter baselines; Vision, Precision and Reaction continue applying once. This works with or without the addon when external SAIN is installed. |
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
| Both-layer readiness / lifecycle | [SAINFollowerRuntime](addon/SAINFollowerRuntime.cs) |
| Linger / aggregate combat handoff | [SAINFollowerCombatHandoff](addon/SAINFollowerCombatHandoff.cs), [SAINFollowerLingerAction](addon/SAINFollowerLingerAction.cs) |
| Regroup policy / action / shared geometry | [SAINFollowerRegroupObjective](addon/SAINFollowerRegroupObjective.cs), [SAINFollowerSquadRegroupAction](addon/SAINFollowerSquadRegroupAction.cs), [SainRegroupBridge](client/Modules/SainRegroupBridge.cs) |
| Bounded firing-position engagement | [SAINFollowerEngageAttempt](addon/SAINFollowerEngageAttempt.cs), [SAINFollowerMoveToEngageAction](addon/SAINFollowerMoveToEngageAction.cs) |
| Recorder | [SAINFollowerRecorder](addon/SAINFollowerRecorder.cs), [SainCombatRecorderBridge](client/Modules/SainCombatRecorderBridge.cs), [BattleRecorder](client/Modules/BattleRecorder.cs) |
| Cover policy / discovery | [SAINFollowerCover](addon/SAINFollowerCover.cs), [SAINFollowerCoverFinder](addon/SAINFollowerCoverFinder.cs), [SainCoverSelectionBridge](client/Modules/SainCoverSelectionBridge.cs) |
| Core native integration | [SainPlayerSquadBridge](client/Modules/SainPlayerSquadBridge.cs), [SainSquadDecisionBridge](client/Modules/SainSquadDecisionBridge.cs), [SainAddonBridge](client/Modules/SainAddonBridge.cs) |
| Aggression policy / settings interpolation | [SAINFollowerPersonality](addon/SAINFollowerPersonality.cs) |
| Native personality setup / proficiency | [SainManPersonality](client/Modules/SainManPersonality.cs), [FollowerSainProficiency](client/Modules/FollowerSainProficiency.cs), [FollowerSainEftCoreProjection](client/Modules/FollowerSainEftCoreProjection.cs) |

## Validation and deployed checkpoint

Latest completed code validation on 2026-09-14:

- `tests/Verify-SainAddonCombat.ps1`: **427 production combat/personality/cover/handoff/status-marker checks**. The fixture uses real SAIN.Preset.Shared 4.5.1 settings/enums and Unity's netstandard facade, controlled game/native instances, and real Harmony publication boundaries. Covers every scoped combat anchor setting, excluded speech/assignment/mechanics, accepted Go Forward dispatch and core fallback, all interpolation segments, discrete boundaries, temporary overrides, native caches, preset/component replacement, rollback/restoration, follower isolation, recorder serialization and retained engagement failure, alongside the existing combat extensions. Investigation/recovery regressions cover rejected native-only contacts, both independence modes, command preservation, medical/urgent exceptions, lifecycle cleanup and passive non-goal medical context. Cover regressions exercise the production selector/finder with controlled native geometry, the real Harmony bridge, bounded discovery, boss preference, native fallback, arrivals, orders, invalidation and destination ownership.
- The 19 new marker checks execute the production contact provider and extracted production marker synchronization/resolution/refresh methods. They cover stale EFT selection, hidden movement, fresh knowledge, visible-to-hidden transitions, forgetting/cleared places, target release/death, invalid positions, shared followers, tactic opt-out and core/unready fallback. UI rendering itself still needs raid verification.
- Installed SAIN metadata validates **11 resolved native action constructors**, the decision publisher, cover selection method/sprint field, personality setters/timers, cached talk refresh and SearchAction sprint fields.
- `tests/Verify-SainReplicaParity.ps1`: **13 source-parity comparisons** passed.
- `tests/Verify-SainProficiency.ps1`: **32 production proficiency checks** passed. No mechanical-proficiency policy was changed.
- Matching Debug core/addon build: **zero warnings and errors**. Whitespace checks passed.
- Both DLLs, PDBs and `SAIN-LICENSE.txt` deployed to the live plugin folder from `LOCAL.md`; all five source/destination SHA-256 hashes matched.

| Installed DLL | SHA-256 |
|---|---|
| pitFireTeam.dll | `E4184F0DED2966EC996C3FFF836F1442A2BC21E2CD106124E4AB072D0385B808` |
| pitFireTeam.SAINAddon.dll | `7ABD09582D7CFCCABA260159778811550A059AB404BD883760B7B3CE9CC22396` |

These binaries include aggression-interpolated personality settings, boss-oriented cover selection/arrival use, accepted-goal combat entry, core recovery handoff, passive medical diagnostics and native-knowledge Status Report markers. A restart/new raid is required. Unity movement/presentation, temporary-command behavior and the complete in-raid lifecycle still need qualification.

## Raid evidence and limits

`20260914-042148-Interchange.jsonl` is complete (3,355 events). At raid time 898.547 the addon entered SeekCover and then Search with native contacts but no EFT goal; the first accepted EFT goal in that episode appears near 929.98. Attention cleared the remaining enemy state and released Search at 993.788. This motivated the accepted-goal gate.

The same raid records manual Force Heal at 923.212 and 981.664 in `LogOutput.log`. Automatic native first aid did execute around 940.64–948.46 and 953.91–961.72, with remaining damage and surgical work afterward. The addon never enabled `postCombatFullHealActive`, and its release paths lacked the existing full-recovery bridge call; that omission is repaired. The separate combat delay cannot be assigned to a particular non-goal threat, item, or treatment failure from the old record. New medical inputs improve that evidence without changing combat-heal policy. The earlier suspected friendly fire also remains unconfirmed; this change does not alter suppression safety.

`20260914-033024-Shoreline.jsonl` captured Medved's native SAIN actions and death (836 events). At 250.889 he was 33.4m from the player but selected cover 68.8m from the player, walked about 47m along its route, then activated automatic regroup at 262.379 with `passiveCoverHold`. This motivated the boss-cover/arrival-use extension. The record does not prove that suitable player-area cover existed; the new events expose the selection policy for subsequent testing.

The same recording exposed two separate findings outside this cover change: the Go Forward aggression override persisted into a later fight, with patrol readiness repeatedly postponing its clear deadline, and native first aid continued after the decision changed to Dog Fight. The inspected SAIN `TryCancelHeal` has commented-out cancellation calls. Neither issue is fixed by the cover work; neither proves that the recorded headshot death was preventable.

The completed `20260913-225814-Shoreline.jsonl` contains 139 events, including one follower death, but no normal combat snapshots/decisionSelected/combatStart events. That recorder version did not recognize addon combat episodes. It cannot retrospectively prove the reported MoveToEngage rearming loop.

Medved used SainMan. Commanded regroup began around raid time 260.767 and ended at 269.323 before linger. One recorded automatic regroup ran from 346.069 to 350.122. At 382.308 the cultist priest killed him with a chest shot; the death snapshot reports addon SoloCombat / SeekCover, with the player about 28.5 m away. This does not establish that regroup or MoveToEngage caused the death. Schema 13 now supplies the previously absent native decision/action/path sequence for the next recording.

## Next session / remaining scope

- Qualify aggression interpolation and temporary override transitions in a fresh raid using [the implementation contract](docs/SAIN-Personalities-and-Aggression.md).
- Qualify the latest recorder, engagement and regroup changes in a fresh raid; do not claim fixture success proves navigation or presentation.
- Other custom combat command translations remain deferred. Saved/temporary aggression now drives personality settings; the former hold/protection addon objective is not present.
- Keep general external-SAIN compatibility in core, no Harmony patches or shared preset mutation in the addon, and preserve follower proficiency, enemy memory, player leadership and safe fallback.
- The phase-one WIP checkpoint is `60d725bd60050a7c02ebfaac3ef73772468d2f8e`. This later WIP checkpoint includes the extensions above; unrelated insurance/client/server work remains excluded.

See [reference provenance](addon/SAIN-REFERENCES.md) for private 4.5.1 assembly references and source limits. Copied SAIN code retains the upstream MIT attribution in `addon/SAIN-LICENSE.txt`.
