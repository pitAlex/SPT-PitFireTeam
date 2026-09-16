# SAIN personalities and aggression

## SAINGrunt tactic display name (2026-09-16)

The addon tactic is displayed as **SAINGrunt** in the profile selector and follower Status Report. The persisted `SainMan` identifier, enum value and `ProfileTacticSainMan` localization key remain stable, so existing squads and pickup selection retain the same behavior without migration. The embedded English fallback and English language resource supply the new name. Historical/code references to SainMan below refer to this same tactic.

## Addon-only SAIN hook ownership (2026-09-16)

Patches used only by the SAIN addon belong in `addon/`. `SAINAddonPatches` installs player-squad leadership, squad decisions, native decision-publication filtering, push-target preference and cover selection under the addon Harmony ID. Failed installation rolls back the complete addon hook set; shutdown releases follower state/membership before removing hooks. `SainManPersonality` also lives in the addon. Public SAIN APIs and enum types are referenced directly; cached reflection remains only for private setters/methods and internal action types.

Core retains compatibility needed without the addon: vision recovery/foliage, aim/recoil/proficiency, enemy synchronization, speech, friendly fire, grenade routing and native layer/weapon/reload guards. The mixed leader-assignment hook was split: core still prevents core followers becoming native AI leaders, while the addon owns human-led squad behavior. Core has no typed SAIN reference. `SainAddonBridge` carries passive leadership diagnostics and lifecycle notifications; shared core navigation/cover helpers remain in core. This supersedes older instructions placing all SAIN interception in core.

Updated: 2026-09-14. Status: implemented in the optional addon; fresh-raid qualification remains required.

## Aggression mapping

SainMan uses these exact anchors from the currently loaded SAIN preset, including customized personality values:

| Aggression | Required personality |
|---|---|
| 100% | GigaChad |
| 70% | Chad |
| 50% | Normal |
| 30% | Rat |
| 0% | Coward |

Aggression means willingness to pursue and fight in SAIN's style, not adopting the whole personality. The exact anchor contract covers **General, Search, Rush, Cover and Difficulty.AggressionCoef**. Numeric combat settings blend linearly between anchors; boolean/enum settings use the nearer anchor, with midpoint ties choosing the higher anchor. The native personality enum follows the same nearest anchor for combat reactions such as grenade response. Wreckless, SnappingTurtle and Timmy are not anchors.

**Talk, random-assignment settings and mechanical difficulty are excluded.** These categories use independent neutral native defaults; begging, fake death and taunting remain disabled at every aggression value. Core continues to own follower speech and finalized Vision, Precision and Reaction.

The confirmed combat command contract uses EffectiveCombatAggression: **Hold Position -> 0% Coward combat behavior; Go Forward -> 100% GigaChad combat behavior; Gogogo -> saved aggression**. Accepted Go Forward orders reach the addon through the core SetPushEnemy boundary only for ready SainMan followers. The addon replaces the prior order/regroup without leaving a durable core push pending. Go Forward now also creates `SAINFollowerPushObjective`, retaining the target and selecting prudent approach/hold/recovery phases through SAIN actions. Its explicit order bypasses discretionary native Freeze/search waiting without rewriting those timers; see [the push contract](SAIN-Integration.md#follower-objectives-and-prudent-push-2026-09-14). Pickup acceptance, command locks, other tactics and out-of-combat movement commands retain their existing paths. Temporary overrides retain the existing post-combat clear lifecycle; saved aggression is never overwritten. Weapon-specific core aggression multipliers are excluded. Finite inputs clamp to 0-100; non-finite inputs use 50.

## Implementation and lifecycle

- [SAINFollowerPersonality](../addon/SAINFollowerPersonality.cs) owns the anchors, interpolation and follower-local copy. It caches serialized fields only within the four combat categories and blends the tactical AggressionCoef separately. Every mutable category remains follower-local; excluded categories cannot inherit personality flavor or random assignment from an anchor.
- [SAINFollowerRuntime](../addon/SAINFollowerRuntime.cs) applies the policy during its existing half-second preparation. It refreshes only after effective aggression, preset/anchor reference, native info, or native personality/settings ownership changes. Native preset reconfiguration also picks up in-place preset edits. Stable ticks do not copy settings or reroll timers.
- [SainManPersonality](../addon/SainManPersonality.cs) is the addon-owned native setup/restore adapter. It installs the supplied copy, refreshes difficulty through existing proficiency interception, preserves both enemy-memory durations around search/hold timer refresh, refreshes native cached talk settings, and clears only a stale SearchAction sprint roll.
- Core still normalizes mechanical difficulty and applies Vision, Precision and Reaction once. Only the tactical `AggressionCoef` follows the chosen/blended profile and feeds SAIN's selected aggression calculation; mechanical personality coefficients stay neutral.
- Existing Freeze deadlines, search pauses, paths, target memory and failed engagement attempts survive settings transitions. Go Forward explicitly replaces prior regroup as a new command. Native decision checks read changed Freeze/search/rush permissions normally. General follower speech remains core-controlled.
- Native state replacement, tactic opt-out, dismissal and teardown restore owned personality state. Missing required profiles retain the saved SainMan selection and supported core fallback. Unknown/non-finite settings fail before publication.
- `sainPersonality` transition events and passive `sain.personality` snapshots record aggression, saved/temporary source, discrete identity, neighboring anchors and fraction. Snapshot reads do not update aggression or gameplay.

Read [AGENTS.md](../AGENTS.md), machine-local `LOCAL.md`, [current progress](../ADDON-ANALYSIS.md), [ownership](SAIN-Integration.md), and [proficiency audit](SAIN-Proficiency-Audit.md) before further changes. The fixed `ApplyChad` assignment loop is removed.

The phase-one WIP checkpoint is `60d725bd60050a7c02ebfaac3ef73772468d2f8e`. This later WIP checkpoint includes the addon extensions and personality implementation. Unrelated insurance/client/server work remains excluded.

## Source and evidence boundary

Findings come from the local SAIN 4.5.1 snapshot identified by `LOCAL.md`, including its sibling `SAIN.Preset.Shared` and `SAINServerMod` projects. Paths below are relative to that snapshot root, which contains all three projects. They are built-in defaults, not a capture of the user's currently selected/customized preset.

The snapshot has no Git metadata, and byte identity with the installed SAIN DLL has not been established. Installed 4.5.1 API validation is separate; see [reference provenance](../addon/SAIN-REFERENCES.md). Do not treat source observations as measured raid behavior.

`PersonalityGenerator.BuildDefaults` creates eight personalities. `EPersonality` additionally declares `None` and `Custom1` through `Custom5`; those are not additional generated built-in behavior profiles.

## Individual personalities (external SAIN source)

The table describes full external SAIN profiles for reference. Their speech and assignment traits are not imported by the addon aggression mapping.

| Personality | Base search delay | Search sprint roll | Main default behavior |
|---|---:|---:|---|
| Wreckless (SAIN spelling) | 0.1 s | 90% | Charges enemies heard from peace; chases distant gunshots; rushes vulnerable enemies; jump pushes; kicks doors; constant/frequent taunts; broad suppression windows. |
| GigaChad | 6 s | 75% | Immediately searches enemies heard from peace; chases distant gunshots; rushes vulnerable enemies; jump pushes; kicks doors; frequent taunts. Search pauses use a 3x multiplier. |
| Chad | 16 s | 60% | Searches from sound and chases distant gunshots; rushes vulnerable enemies; can shift cover; no jump pushes; frequent taunts. Heard-from-peace reaction is Freeze. |
| Normal | 60 s | 10% | Searches seen/heard enemies; does not chase distant gunshots; can shift cover; no opportunistic rush or jump push; can respond to enemy speech. |
| SnappingTurtle | 90 s | 40% | Sneaky search and delayed engagement; does not chase distant gunshots; can rush vulnerable enemies and jump push; can shift cover. Target suppression disabled; search pauses use a 3x multiplier. |
| Timmy | 90 s | 20% | Searches previously seen enemies but not sound-only contacts; no distant-gunshot chasing, cover shifting or opportunistic rush; can beg for life. |
| Rat | 240 s | 0% | Delayed search, including audio, with sneaky movement; no distant-gunshot chasing, cover shifting or opportunistic rush; no taunts; target suppression disabled. |
| Coward | Search disabled | Not applicable | No active enemy search, distant-gunshot chasing, cover shifting or opportunistic rush; short stand-and-return-fire window; can beg for life. |

Search delays are base settings. `CalcTimeBeforeSearch` randomizes by 0.66-1.33 and divides by `Info.AggressionMultiplier`, with a 0.1-second floor and role-specific overrides (Killa/Tagilla and native grouped follower roles). A weak/looting enemy or another eligible branch can start search sooner. Sprint percentages are repeated choices during eligible search, not a measured fraction of time sprinting. Visibility, help/sniper response, equipment, stamina, pathing and other gates still matter.

Chad is not an instruction to keep moving. `EnemyDecisionClass.shallFreezeAndWait` can freeze indoors on an eligible enemy heard from peace, within 70 m of its last-known place, subject to recent-seen/heard conditions. The duration is randomized from 10-120 seconds and divided by aggression. GigaChad uses SearchNow; Wreckless uses Charge. This difference is relevant to the follower's observed waiting, but the old incomplete Shoreline record cannot prove that a particular wait was Freeze.

## What personalities actually influence

| System | Verified influence / consumer |
|---|---|
| Searching | Search permission, sound-only search, distant-gunshot chasing, Freeze/SearchNow/Charge, search delay and pause length; `SearchDecider`, `SearchClass`, `HearingSensor`, `EnemyDecisionClass`. |
| Movement while searching | Sprint rolls, sneaky speed/pose and slowing at corners; `SearchAction`, `SearchClass`, `BotPathData`. Sneaking also depends on global settings and context. |
| Returning fire before cover | `HoldGroundBaseTime` and random range produce `HoldGroundDelay`, consumed by `EnemyDecisionClass`. Bases: Wreckless 2 s; GigaChad 1.25 s; Chad/SnappingTurtle 1.5 s; Normal/Rat 1 s; Timmy 0.5 s; Coward 0.25 s. |
| Rush opportunities | `Rush.CanRushEnemyReloadHeal` allows eligible vulnerable/wounded-enemy rushes and squad pushes against suppressed enemies. Health, ammunition, route and range gates still apply. |
| Jump pushes | `CanJumpCorners`, `JumpCornerChance` and `BunnyHopChance` affect `RushEnemyAction`; bot/global jump settings still gate execution. |
| Cover | `CanShiftCoverPosition` gates cover shifting; the shift action reads speed and pose settings. These are not blanket controls over all SAIN cover travel. |
| Suppression | Target-suppression permission, recent-contact windows, angle/distance and suppression resistance; `SAINBotSuppressClass`. Resistance combines bot-type and personality values. |
| Close combat / sniper response | Dogfight distance/recency thresholds, sniper-distance classification and sprint preferences exist in personality settings. Most are shared defaults rather than differences between the eight generated personalities. |
| Grenade response | In the inspected `GrenadeReactionClass`, Chad/GigaChad/Wreckless prefer Push with an enemy, otherwise Scatter; Rat/Timmy/Coward prefer Retreat; SnappingTurtle Relocate; Normal Scatter. Smoke, occlusion and immediate blast-danger checks take precedence. |
| Speech / doors | Taunts, frequency/chance/distance, enemy-voice replies, fake-death/begging speech and door kicking. Core currently owns follower speech and suppresses general SAIN follower chatter; do not enable it as an incidental personality change. |

Runtime audit found several fields whose labels overstate their current effect: `General.AggressionMultiplier`, `Cover.ShiftCoverTimeMultiplier`, and `Rush.CanBunnyHop` have no runtime reads in the inspected SAIN client source. Actual aggression timing uses `Difficulty.AggressionCoef` via `Info.Difficulty.AggressionModifier`; jump behavior reads the corner permission and bunny-hop chance. Do not implement tuning by changing an unused field.

## Personality, proficiency and assignment are different inputs

Each personality has a `Difficulty` category with vision distance, recognition gain, scatter, hearing distance, aggression, and aim/convergence-speed multipliers. `BotDifficultyClass` consumes these alongside global, bot-type and location settings. The generated personality defaults leave those difficulty multipliers at 1; different presets can edit them.

Equipment power, level, bot type, random assignment, nickname overrides and forced/boss personality rules affect which personality native SAIN chooses. Selecting a personality does not itself equip a different loadout.

Our follower proficiency remains core-owned. `FollowerSainProficiency` normalizes mechanical values to the Default preset and applies follower-local Vision, Precision and Reaction, while preserving selected tactical aggression inputs. A personality transition must not turn into an accidental accuracy, recoil, hearing or vision buff/nerf, stack modifiers, replace the follower's saved percentages, or mutate shared presets. Read the proficiency audit before modifying `UpdateSettings` or timer refresh.

## Separate squad personalities

SAIN derives a squad category from the most frequent personality among living bot members. A human leader is not inserted as a fake BotComponent or assigned a made-up personality.

| Dominant individual personality | Squad category | Vocalization | Coordination | Enemy-position report roll | Stored aggression |
|---|---|---:|---:|---:|---:|
| Chad / GigaChad / Wreckless | GigaChads (66% roll) | 5 | 4 | 85% | 5 |
| Same | Elite (otherwise) | 2 | 5 | 100% | 4 |
| Rat / SnappingTurtle | Rats | 1 | 2 | 55% | 1 |
| Timmy / Coward | TimmyTeam6 | 3 | 1 | 40% | 2 |
| Normal / default | Native random squad category | Depends on selected category | Depends on selected category | Depends on coordination | Depends on selected category |

`Squad.ReportEnemyPosition` uses `25 + 15 * coordination` percent per member. Vocalization has a runtime consumer in `SAINBotTalkClass`. `SquadPersonalitySettings.AggressionLevel` is assigned but has no runtime consumer in the inspected client source.

Separate squad-personality settings keep their native membership/leader lifecycle. This per-follower settings feature does not regenerate or mutate squad objects, so an aggression change alone does not reroll cached squad coordination/vocalization. Player leader identity remains unchanged.

## Validation and remaining raid qualification

The 2026-09-14 validation passed 346 combat/personality checks, 13 native replica comparisons and 32 proficiency checks; the Debug build had zero warnings/errors and matching core/addon outputs were deployed with verified hashes (see the progress ledger).

The production combat fixture uses the real SAIN.Preset.Shared 4.5.1 settings and enums, with controlled native/game instances and real Harmony publication boundaries. It verifies every scoped combat setting at all five anchors, invariant neutral speech/assignment/mechanics, production Go Forward dispatch and core fallback, interpolation within all four segments, midpoint booleans/enums, independent nested copies, temporary override/clear, passive diagnostics, stable updates, preset edits/replacement, native component replacement, opt-out, missing-profile recovery, invalid values, memory preservation and retained engagement failure.

The native squad/solo parity comparisons and existing proficiency checks remain applicable. Fixture success does not establish Unity navigation or presentation. In a fresh raid, compare all anchors and intermediate values under seen/heard/lost contact, medical/urgent interruptions, automatic/command regroup and On Your Own.

## External source map

| Finding | SAIN snapshot-relative source |
|---|---|
| Eight generated profiles / enum slots | `SAINServerMod/Generators/PersonalityGenerator.cs`; `SAIN.Preset.Shared/Models/Preset/Personalities/EPersonality.cs` |
| Exact defaults | `SAINServerMod/Extensions/PersonalityDefaultsExtensions.cs` |
| Setting categories and neutral difficulty defaults | `SAIN.Preset.Shared/Personalities/BasePersonality/Categories/`; `SAIN.Preset.Shared/DifficultySettings.cs` |
| Native selection rules | `SAIN/Models/Preset/Personalities/PersonalityDictionary.cs` |
| Personality setter / search and hold timing | `SAIN/Classes/Bot/Info/SAINBotInfoClass.cs` |
| Multiplier application | `SAIN/Classes/Bot/Info/BotDifficultyClass.cs` |
| Freeze / rush / cover decisions | `SAIN/Classes/Bot/Decision/EnemyDecisionClass.cs`; `SquadDecisionClass.cs` |
| Search behavior / sprint / corners | `SAIN/Classes/Bot/Search/SearchDecider.cs`; `SearchClass.cs`; `SAIN/Layers/Combat/Solo/SearchAction.cs`; `SAIN/Classes/Bot/Mover/BotPathData.cs` |
| Rush / cover action consumers | `SAIN/Layers/Combat/Solo/RushEnemyAction.cs`; `Cover/ShiftCoverAction.cs` |
| Suppression / grenade response | `SAIN/Classes/Bot/WeaponFunction/SAINBotSuppressClass.cs`; `Grenades/GrenadeReactionClass.cs` |
| Talk caching | `SAIN/Classes/Bot/Talk/EnemyTalkClass.cs` |
| Squad category / report roll | `SAIN/Classes/BotManager/SquadPersonalityManager.cs`; `Squad.cs`; `SquadPersonalitySettings.cs` |
