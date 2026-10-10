# Self Determination specification

**Status: proposed, not implemented.** Prepared 2026-10-10 from the current Core, SAIN 4.5.1 and addon source, with recent raid evidence. Confirmed product requirements and recommended calibration are distinguished below. This does not change the current contracts in [Combat tactics](Combat-Tactics.md), [Commands](Commands.md), [Proficiency](Friendly-AI-Performance-Settings.md) or [My Squad](My-Squad-Screen.md).

The goal is to let a saved teammate fight as a close protector, a balanced guard or an independent combatant without introducing cover, push or regroup churn. The [addon specification](../addon/docs/Self-Determination-Proposal.md) owns SAIN-specific adaptation and faults; Core remains the shared policy and command reference.

## Confirmed requirements

- Add **Self-Determination** under **Proficiency**, from 0 to 100, with default 50. Keep it separate from Aggression, as confirmed by the user.
- Lower values increase concern for the player's position and combat situation. Zero is close escort, including for Marksman. Fifty aims at Reshala-guard-style combat. One hundred uses existing combat On Your Own behavior.
- Aggression still controls offensive willingness, existing distance/threat calculations and addon personality. Self-Determination is tactical dependence on the player, not accuracy, hearing, vision, target acquisition or obedience probability.
- Apply the policy to Core and our ready SAINGrunt/SAINShooter addon tactics. Ordinary SAIN bots and shared presets are outside its scope.
- Raid pickups have a fixed baseline of 50 for this setting. They do not get a configurable slider. Their existing command acceptance rules remain separate.
- During combat, Cover Me caps an effective value above 50 to 50 and regroups. It does not raise a value below 50. Commands never rewrite the saved preference.
- Add a visible percent sign beside every percentage value in the Proficiency dialog: Aggression, Self-Determination, Vision, Precision and Reaction. Preserve existing ranges of the other controls.
- Churn prevention takes priority over closer cosmetic imitation of native guards.

## What the source establishes

Vanilla `FollowerBullyLayersStrategy` puts `FollowerBullyProtectLayer` above ordinary assault combat. It inherits `PmcLayer`: immediate combat comes first; firing cover is selected around the boss; fresh contact can authorize pursuit; otherwise it holds or returns near the boss. Installed followerbully difficulty settings use a 25 m boss-cover bound, 1.5 seconds for the group-sighting pursuit check and 8.5 seconds for the other protection sight-history check. The fallback compares squared boss distance with 100, meaning 10 m. These are conditional decisions, not a permanent 10 m combat leash.

The 50% target is that **behavioral balance**, not a copy of its timers. Core already adds health, equipment, enemy-group and weapon threat, command objectives, contact admission and stable movement commitments. Those safeguards and existing aggression curves remain.

Current Core integration points:

| Boundary | Existing behavior | Required adaptation |
|---|---|---|
| [FollowerCombatRiflemanEngagement](../client/BigBrain/FollowerCombatRiflemanEngagement.cs) | One cached Engage/Hold/Regroup calculation; an accepted engagement can exceed the regroup trigger | Add effective dependence to this evaluation, its cache key and diagnostic snapshot; do not add a competing veto in each action |
| [FollowerCombatCommon](../client/BigBrain/FollowerCombatCommon.cs) | Shared cover scoring, commitments, boss-hit checks, support and search helpers | Apply policy only to relevant automatic intents; preserve safety, medical cover and action end semantics |
| [FollowerCombatDefault](../client/BigBrain/FollowerCombatDefault.cs) | Rifleman/Protector routing, protection and regroup selection | Use the same policy snapshot for decision selection and hold breakers |
| [FollowerCombatSniper](../client/BigBrain/FollowerCombatSniper.cs) | Separate firing-position, boss support and regroup policy | Scale escort dependence without converting its weapon/firing-position role into Rifleman assault |
| [FollowerCombatRegroupObjective](../client/BigBrain/FollowerCombatRegroupObjective.cs) | Fixed role/map arrival distances shared by automatic and ordered activation | Record activation origin; automatic completion must be inside its own trigger; ordered distances remain command-owned |
| [FollowerCombatAnchor](../client/Modules/FollowerCombatAnchor.cs) | Boss anchor normally; nearby follower within 35 m or self when independent; commanded regroup can force the boss | Keep real identity and explicit destination separate from tactical independence |
| [BotFollowerPlayer](../client/Components/BotFollowerPlayer.cs), [AIBossPlayer](../client/Components/AIBossPlayer.cs) | Saved tactics, transient independence, patrol mode and commands | Own preference/override lifetime once for both brains |

No new shooting, movement, healing or grenade action class is required by this feature. Regroup objective state and action-selection inputs change.

## State and command contract

Use three distinct values: configured determination, the current command override, and resolved effective determination. Do not encode the setting as an OR against the existing independence boolean: a saved 100 would otherwise defeat Cover Me.

Resolution order:

1. Explicit combat override: On Your Own resolves to 100; Cover Me resolves to `min(value before the command, 50)`.
2. Existing out-of-combat On Your Own patrol intent supplies 100 at combat entry unless superseded by a combat override.
3. Saved teammate preference, or fixed pickup baseline 50.

Recommended override lifetime, pending the user's answer: until **aggregate follower combat ends**, or a later applicable command replaces it. Do not expire on an enemy switch, one kill, temporary GoalEnemy gap, native action change or regroup arrival. Repeated Cover Me is idempotent at the same effective value and cannot continually reset a settled destination/hold. Repeated On Your Own does not create new engagement attempts.

| Input or transition | Required result |
|---|---|
| Combat On Your Own | Effective 100; keep current command cancellation/eligibility semantics unless deliberately superseded below; return native independence behavior through the existing ownership paths |
| Combat Cover Me | Cap as above, cancel conflicting automatic/ordered push intent, issue one ordinary combat regroup through existing Core/addon routing; an already close follower can settle without moving |
| Explicit Regroup / Come here / There | Obey the accepted destination even at 100; do not edit the preference or permanently cap determination |
| Ordered push / Need Sniper / suppression | Preserve current tactic eligibility, target commitment and medical/survival priority; determination cannot silently veto a valid explicit order |
| Follow Me / Cooperation | Preserve current reset behavior; clear applicable command independence state and resolve the saved preference; never change the saved percentage |
| Actual combat end | Clear combat-only override and local retry/commitment state; keep saved value and existing peaceful patrol intent |
| Tactic change / dismissal / component replacement | Release old-brain local state and claims; no old override or destination leaks into another owner |

Cover Me currently only cancels ordered-push pressure and disables combat independence. Its new regroup is an intentional command change. Current combat On Your Own does not follow all cleanup claims in Commands.md; inspect the actual handler when updating the command documentation rather than copying that prose.

Configured 100 affects combat. It does not enable peaceful roaming merely by loading the profile. Peaceful On Your Own and Follow Me retain their separate meaning.

For pickups, fixed 50 refers to the new baseline; accepted explicit commands retain their existing effect. Existing random/level-based command refusal remains untouched. Existing pickup protection willingness must not be multiplied into the new escort radii or protection weight a second time: that would make the purported fixed-50 baseline variable. Keep command personality separate from this combat policy.

## Decision contract

Resolve one small immutable policy snapshot per follower decision. Recommended normalized weight is `bossInfluence = 2 * (1 - determination / 100)`: 2 at zero, 1 at fifty, 0 at one hundred. This is an influence weight, not a probability of obeying or helping.

- **Advance/search:** retain the current aggression/threat calculation and scale its existing player-separation term. Use the same result for automatic start, hold release and regroup fallback. At zero, a new unordered offensive destination must remain inside the close escort envelope or reduce existing separation; a distant enemy can still be shot from near the player. At intermediate values, apply the dependence score rather than adding different hard enemy-distance limits to each action. One hundred removes player-pull and boss-regroup restrictions through the existing independent mode.
- **Cover:** score valid candidates with boss influence and the existing travel cost. At low values, favor nearby useful protection/firing cover around the player, including a safe forward/lateral position for Rifleman. A cover's ability to provide a real firing/support lane outranks simply being geographically close. Existing anti-crowding, floor, route, reservation and threat safety checks stay authoritative. Do not invent an interpolated world-space boss position between player and follower: it may lie inside a wall or on the wrong floor.
- **Protection:** scale priority and permissible support travel, not perception or firing permission. Remove the new policy's dependence on the old binary 0.45 willingness cutoff; otherwise protection would disappear around 55% if fed `1 - d`. Prefer a prepared useful successor over an idle position. Current direct fire, survival, medicine and explicit missions remain protected. At 100, retain existing On Your Own semantics: automatic boss-protection diversion stops, while ordinary ally cooperation can still occur.
- **Combat situation:** consume existing admitted player-engagement, boss-hit and squad-support evidence. The player merely looking in a direction does not prove an enemy or authorize a shot. Existing contact requirements and boss-attacker identity checks are preserved; broadening them is separate work.
- **Marksman:** determination changes escort dependence, not the role's firing-position/weapon rules. At zero its automatic positions stay near the player; at fifty and above its role-specific spacing can reappear. Explicit Need Sniper and emergency weapons keep their own commitments.
- **Survival:** immediate danger may require outward safe cover at any value. Do not force a bot to remain exposed or use unsafe near-player cover. Once travel and genuine recovery finish, apply the ordinary support/regroup arbitration. Distance alone cannot invalidate safe treatment cover mid-treatment.

## Proposed initial distance calibration

These numbers are an implementation starting point for raid qualification, not measured equivalence to Reshala or an additional user setting. Keep the existing configured Regroup Radius as the 50% trigger reference; keep map and role context. Do not expand cover enumeration or path-search budgets with determination.

Let `R` be the configured Rifleman regroup trigger (currently default 18 m). Let `R50` be the role's existing trigger before pickup-personality multiplication: `R` for Rifleman, the current Marksman multiplier for Marksman. For `d < 100`:

- Trigger at 0: 10 m for either role. Interpolate linearly from 10 to `R50` over 0..50, and from `R50` to `2 * R50` over 50..100.
- Preferred automatic completion at 0: 6 m. At 50: 10 m for Rifleman; the existing map-specific ordered completion distance for Marksman. Interpolate these values over 0..50; over 50..100 interpolate toward the existing role/map ordered completion distance.
- Final automatic completion is at most `trigger - 2 m`; use that same envelope for arrival cover and settle tests. Preserve floor/reachability checks and the existing sector refresh distance.
- At 100, automatic boss-distance regroup is disabled; these automatic trigger/completion distances are unused. Explicit regroup still uses its existing ordinary/tight distances.

Example with the current default 18 m radius on an ordinary map:

| Determination | Rifleman auto trigger / completion | Marksman auto trigger / completion |
|---|---|---|
| 0% | 10 / 6 m | 10 / 6 m |
| 25% | 14 / 8 m | 18.5 / 15 m |
| 50% | 18 / 10 m | 27 / 24 m |
| 75% | 27 / 14 m | 40.5 / 24 m |
| 100% | No automatic boss regroup | No automatic boss regroup |

These are **fallback regroup thresholds**, not distances that cancel active fire or an accepted push. The explicit 100 endpoint selects the existing independent mode. At 50, use the existing 25 m ordinary / 12 m Factory-Labs combat-start range as the nearby-cover reference, favoring player-relevant firing cover. Do not treat it as a universal cap on every survival action.

## Churn prevention invariants

1. A decision owns one destination and one arrival hold. Determination affects selection/reselection; player movement or a small score fluctuation does not reroute a productive leg every tick.
2. A commitment is productive while its matching route makes progress, its prepared firing/support opportunity is usable, or genuine survival/medical work owns it. A boolean Active flag by itself is insufficient.
3. An exhausted, blocked or unproductive automatic objective releases arbitration after its existing bounded recovery/arrival window. It may retain the enemy/mission identity, but cannot indefinitely suppress support and regroup.
4. Automatic regroup records why the prior autonomous attempt yielded. Returning to the player cannot immediately rearm the same failed outward attempt for the same enemy/geometry solely because current boss distance is now smaller.
5. Retry uses a bounded record per follower and target/context. A materially changed enemy anchor, meaningful player-sector change, completed weapon recovery or a new explicit command can reopen the relevant failed option. Repeated remembered-position reports and polling cannot. A newly seen but still unshootable enemy does not by itself erase a failed destination. Existing shared geometry thresholds and failed-cover storage should be reused.
6. Negative selection results are cached. Timer expiry allows a bounded reassessment; it does not force movement or erase an unchanged failure. Distinguish pending work from completed empty results.
7. Automatic trigger, actual completion, terminal cover envelope and post-arrival restrictions agree. Ordered regroup retains a separate origin and does not inherit a dynamic automatic radius mid-route.
8. A medical arrival yields directly to treatment. The ordinary think hold must never delay it.
9. Real contact, living enemy memory and shot safety cannot be cleared or bypassed to obtain patrol, regroup or support.

## Settings and persistence

Keep tactical SelfDetermination alongside Aggression in teammate settings, separate from `FollowerProficiencyModifierValues`' vision/aim multipliers. Add it to profile options, follower details and spawn data. Default missing/invalid values to 50 and clamp finite values to 0..100. Changing tactic must not reset it; Reset in the Proficiency modal restores it to 50.

Prefer extending the existing proficiency-save request with an optional determination field and saving both together. Omission preserves the stored value for older clients. Capture the account id, proficiency clone and determination together when scheduling the existing 0.35-second debounce; switching profile tabs cannot save one teammate's value to another. Apply saved changes at the existing spawn/settings lifecycle, not by adding per-frame HTTP or profile reads.

Use a separate adjacent `%` label for each slider's numeric input. Native EFT `NumberSlider.SetStringValue` parses a plain float and `FormattedValue` uses .NET numeric formatting. Appending `%` to its format would either rescale the number or make the editable text fail parsing. Keep the input numeric, preserve typing, clamping, locale handling and reset behavior; reserve space for `200%` and the localized Self-Determination label.

Add canonical text to embedded English and the English resource through [Localization](Localization.md). Update current topic docs and the active release notes only when the feature is implemented.

## Validation and implementation order

1. Correct the independent addon liveness/fallback faults documented in the [addon specification](../addon/docs/Self-Determination-Proposal.md), with narrow reproductions. Keep that result distinguishable from slider calibration.
2. Add persisted preference and command override state, percentage UI and a pure shared policy. Validate defaults, old-client saves, resets, tactic changes and pickup baseline.
3. Integrate Core Rifleman, then Marksman/Protector shared paths. Reuse existing cover/regroup/action ownership. Add automatic-versus-commanded regroup origin rather than changing all completion distances globally.
4. Integrate our addon through its typed local objectives and existing bridges. Validate Core fallback with an absent/unready addon; do not modify ordinary SAIN bots.
5. Build matching Debug outputs and qualify in raid when implementation is authorized. No backup workflow is introduced.

Required checks cover: 0/25/50/75/100 crossed with low/high Aggression; explicit orders at 100; Cover Me at 0/30/50/70/100; On Your Own after Cover Me; target switches and transient handoffs; low configured regroup radius; automatic versus normal/tight orders; stairs and urban detours; unavailable cover; blocked weapons and legitimate reload/draw/medical work; repeated unchanged negative plans; fixed-50 pickups; Marksman close escort; absent/unready addon and ordinary native bots; editable percentages, locale fallback and legacy data.

Recorder additions must be passive: configured/effective determination, override source, automatic trigger/completion, last arbitration time, productive commitment or blocking reason, and retry context. Preserve existing path/cover caches and update cadences. No new world scan, reflection lookup, native decision-provider call or path computation is allowed merely to read the setting or record it. Source/fixture results do not prove navigation, perceived support or frame time; compare raid timelines around first contact, arrival, separation and actual fire.
