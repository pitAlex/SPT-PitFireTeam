# Allegiance

> Play by Tarkov's rules. Alliances are formed, not bought. Find your allies in the field, and escape together.

Allegiance builds the squad through field recruitment. **Add Teammate** is hidden and manual hiring is rejected on the server. Successfully recruited PMCs can enter this mode's saved roster through the existing post-raid invitation and acceptance workflow; recruited Scavs remain raid-only allies. Allegiance has its own roster, equipment, teammate settings and pending invitations; it starts empty and does not import the Guns for Hire roster.

Choose it on the first **My Squad** visit or later in **My Squad → Mode**. Accounts without a squad can receive the one-time welcome teammate described in [Gameplay modes](Gameplay-Modes.md#first-my-squad-visit). That guide also owns switching, settings snapshots and roster separation; [Guns for Hire](Guns-for-Hire.md) describes the configurable hiring alternative. [Teammate storage](Teammate-Storage.md) owns persistence and invitation acceptance.

## Settings policy

Allegiance disables these controls, with the tooltip `this option is not available in Allegiance Mode`, and enforces their values through the runtime policy:

| Setting | Allegiance value |
| --- | --- |
| Bad Guy | Off |
| Friendly PMC Side | Off |
| Enemy Tracking | Realistic |
| Pickup | On |
| Tiered Pickup | On |
| Maximum Pickup | 2 |
| Recruit Pickup | On |
| Team Escape | On |
| Team Escape: Use Any Extraction Point | Off |

Heal Followers remains available as a manual emergency shortcut in Allegiance. Its key can be configured normally; ordinary follower healing behavior is unchanged.

Squad Health Multiplier is also editable in Allegiance and uses the saved value through the existing follower-spawn customization path. Its range remains 1–10 and its existing in-raid editing restriction still applies.

Loadout Management is editable outside raids in Allegiance, with the same Restricted, Immersive and Realistic equipment policies as Guns for Hire. Both modes use one shared saved choice, defaulting to Immersive. Existing saved choices are preserved, and switching gameplay mode keeps the current choice. See [Loadout Management](Loadout-Management.md) for ownership, supplies, upkeep and death-loss rules.

Fixed values are resolved at runtime rather than written over the saved Guns for Hire preferences. Editing the cfg cannot bypass this policy. Only the nine settings in the table are captured and restored when returning to Guns for Hire. All other settings are shared and keep edits made in either mode; see [settings restoration](Gameplay-Modes.md#saved-settings-and-restoration).

## Finding friendly PMCs

Allegiance selects solo BEAR and USEC PMCs after `FactionHostility.Apply` in the activation hook. `Raid Settings > Friendly Encounter Chance` is an integer percentage slider from 5% to 80%, default 30%, immediately after Bad Guy. The human PMC's own faction uses that chance. Opposite-faction PMCs use the same chance at 10% or below, converge linearly from 10% to 15% as the slider rises from 10% to 30%, and use half the setting at 30% or above. Below 30%, the opposite-side formula is `10 + (setting - 10) / 4` percentage points, except settings at or below 10 use the setting itself.

| Friendly Encounter Chance | Same faction | Opposite faction / Scav base |
|---|---|---|
| 5% | 5% | 5% |
| 10% | 10% | 10% |
| 15% | 15% | 11.25% |
| 20% | 20% | 12.5% |
| 25% | 25% | 13.75% |
| 30% (default) | 30% | 15% |
| 40% | 40% | 20% |
| 60% | 60% | 30% |
| 80% | 80% | 40% |

There is no raid-wide encounter cap for PMCs or Scavs. Each eligible profile has one decision per raid; settings changes, death, recruitment and repeated requests never reroll it. Current groups, original multi-bot spawn groups and groups awaiting additional members are excluded, including their later survivors. A successful solo selection locks that native group for the raid, so later ordinary spawns cannot reuse it. If another system explicitly inserts a member despite the lock, the selected solo loses solo selection/recruitment eligibility; that membership change preserves the existing neutral relationship and does not declare hostility toward the player or followers. Native hostility, explicit Contact and actual aggression still govern escalation. Active recruit capacity is a separate admission rule, still two in Allegiance. The slider remains one shared saved preference across modes and restarts. The former multiplier config entry is replaced by `04 FriendlyEncounterChance`; its 1–5 values are not interpreted as percentages, so the new entry starts at 30%. Player-Scav/Fence hiring remains unchanged.

Guns for Hire rolls only same-faction PMCs using the configured percentage and suppresses encounter rolls while Friendly PMC Side or Bad Guy is on. Changes to the slider and Fence standing affect future candidates only. Existing encounter penalties reduce the final calculated chance proportionally, as described below.

## Friendly Scavs

Regular solo Scavs (`assault`, Savage side) can roll friendly in Allegiance. They use the opposite-faction base from the table above, multiplied by the player's current Fence standing clamped to 0–6; the resulting probability is clamped to 0–100%, then active encounter penalties apply. At the 30% default, the Scav base is 15%: standing 0.2 gives 3%, standing 1 gives 15%, and standing 6 gives 90%. Zero or negative standing gives zero Scav encounters; missing/non-finite standing uses zero. See [Fence chance curve](Fence-Karma.md#scav-encounter-chance). No lifetime encounter cap applies. The active field-pickup limit remains two across PMCs and Scavs. Bosses, their followers and guards, sniper Scavs (`marksman`), rogues, raiders and grouped Scavs remain excluded, even when alone. Guns for Hire does not roll Scav encounters.

Brain names such as Killa do not establish role or friendship. A regular `assault` Scav using that brain remains eligible for the solo roll; an actual `bossKilla` role remains excluded. Being passive or unaware of the human is not evidence of a successful roll and does not authorize Cooperate. After recruiting a Scav, neutral ordinary groups also remain distinct from selected recruitable friendlies.

Recruitment keeps the existing Cooperate sight, combat, friendship, trust and pickup-capacity checks. Scav levels and legacy level refusals do not affect admission. Acceptance instead uses the final encounter chance recorded when that Scav rolled friendly, including the multiplier and penalties: `1 - 0.5 * clamp((encounterChance - 0.15) / 0.35, 0, 1)`. At 15% encounter chance or below, acceptance is guaranteed; it decreases linearly to 50% acceptance at 50% encounter chance and remains 50% above that, including at 100%. The first eligible request records one acceptance/refusal for that candidate for the rest of the raid. Combat or full capacity does not consume the roll; subsequent settings changes and repeated requests cannot reroll it. PMC recruitment retains its tiered level rules, and native player-Scav hiring retains its Fence admission rules.

The existing conversion chooses Rifleman (`Default`), or SAINGrunt (`SainMan`) when the optional brain and native SAIN bot are ready. The Scav retains its Savage side and original role. It never generates a post-raid roster invitation. Order the Scav to collect loot and extract while he is alive and still following you to have Fence return what he still carries. His starting kit is excluded. Extraction together at a non-co-op exit grants a flat +0.025 Fence standing once per raid. Real co-op exits use only their native standing and gift handling. SVM's paid co-op conversions still require a living Scav companion for this feature. See [Friendly Scav cargo](looting/Friendly-Scav-Cargo.md) for companion eligibility, ownership, mail and retry rules.

### Scav groups and trust

A recruited Scav retains membership of his original native `BotsGroup`, its Savage side and his original profile group/team IDs. The player and current followers are allies of that group, not bot members; the Scav is an ally of the PMC `BotsGroupPlayer`, not a member. Human players use native `AddAlly(Player)` because native bot membership accepts `BotOwner`. Core follower ownership and commands still belong to the same human leader. The first Scav recruit may create an empty PMC group for later PMC recruits. Optional SAINGrunt leadership is maintained separately for each native group.

While at least one recruited Scav companion is alive and accompanying the player, ordinary Scav groups use one raid-local 75% neutrality roll toward the player and squad. When the last living companion dies or is removed, Core releases only the neutrality granted by that companionship and restores prior enemy relationships or native admission. Independent neutrality and selected encounter friendship remain intact. Another living recruit can enable the benefit again using the same saved group rolls and trust history. Each companion uses the native health death event; removal and raid reset detach that handler. All members must be regular Savage `assault` bots; bosses, their followers/guards, marksmen and mixed special-role groups are excluded. Neutrality is reciprocal and uses `Neutrals`, not `Allies`. Selected encounter friendlies retain their independent friendship protection. Existing explicit aggression before recruitment is preserved; later real aggression or Contact revokes neutrality without another roll. Native enemy additions publish hostile opponents to the separate companion groups, without lending unrelated groups membership, sight or firing permission.

Neutral Scav kills count toward trust only after the first recruitment, once per victim, including recruited Scav companions. Intent must originate with the human: personally initiate damaging fire, or designate that particular neutral Scav through an accepted explicit Contact command before hostility is published. A PMC follower can finish that target and the kill still counts. Uncommanded PMC aggression never counts, including when the player later finishes the AI-started fight. Scav-companion and outside-actor kills do not count. Contact authorizes only its target, not all members of that target's group.

The `Player.ApplyDamageInfo` prefix captures the first damaging-hit classification before health/death and retaliation callbacks. Contact captures the same classification before declaring hostility. Classification persists for the raid: wounded friendlies still count after retaliation; initially hostile targets cannot later become betrayal kills through relation changes. Zero damage does not capture intent. Each qualifying kill reduces the neutrality threshold by 25 percentage points: 75%, 50%, 25%, then 0%. Existing groups retain their original roll.

At the third qualifying kill, all living Scav companions stop following and all ordinary Scav groups become hostile to the player and PMC squad for the rest of that raid, including existing selected friendlies and later spawns. No regular Scav can roll friendly or accept recruitment again. Bosses, guards, marksmen and mixed special-role groups remain excluded. Departure runs outside the death dispatcher: remove all departing Scavs from follower ownership before publishing hostility so they do not inherit a fight against one another. Borrowed PMC-versus-Scav relationships are removed while direct aggressors remain enemies. Manual dismissal alone does not trigger this raid-wide betrayal state.

These intentional friendly Scav kills also cost [Fence standing](Fence-Karma.md): 0.05 for a recruited companion, or 0.025 for an unrecruited friendly/neutral Scav, including selected friendlies before the first recruitment. Only a recruited victim plays the negative karma sound. Scavs contribute nothing to PMC karma: no loss, extraction gain, quiet-recovery eligibility or positive sound. Trust is raid-local; Fence standing and the existing 24-hour friendly-encounter penalty ledger remain independent. Standing affects future Scav friendship rolls through the curve above.

## Recruiting and squad relationships

Qualification for independent Scav groups: the production hostility and optional leadership fixtures pass, but a live raid must still cover recruiting a Scav before any PMC, recruiting alongside an existing PMC squad, vanilla/Core and ready SAINGrunt command/combat ownership, new group neutrality, and the three-kill departure. The dated addon deployment ledger is [here](../addon/docs/Validation.md#2026-10-11---independent-allegiance-scav-groups).

**Cooperate** remains a regular phrase in Help, assignable by right-click. Playback and the Hello gesture work without a bot in front of the player. Recruitment is checked by the receiver: an eligible candidate must be within 5 m, in the interaction view cone and visible through the shared sight gate. The contextual quick prompt retains its 2.5 m interaction range. **Follow Me** does not request recruitment. See [Commands](Commands.md#follow-me--cooperation) for the complete request and reply contract.

Friendship selection is separate from recruitment acceptance. An eligible friendly can still refuse because of combat, active pickup capacity, PMC level rules or the Scav acceptance roll described above. PMC level refusals and Scav chance refusals are sticky for the raid; repeating the request cannot reroll them. The contextual Cooperate prompt appears for all friendly or neutral non-follower Scavs within native interaction range, independently of recruitment eligibility. Showing the prompt does not make a neutral group member or excluded role recruitable. Allegiance permits at most two active field pickups even though friendly encounters have no raid-wide cap.

Selected candidates become neutral to the player and squad without changing shared bot settings, visibility or shot permission. After each follower finishes group reassignment and registration, Allegiance refreshes neutrality in both directions between that follower and every living, still-friendly selected candidate. This covers BEAR, USEC and selected Scavs equally, including the first recruit forming a squad. The existing profile-alias cleanup removes stale BotOwner/Player enemy keys, clears only those friendly-pair memories and publishes native enemy-removal notifications for external SAIN caches. It never rerolls selection, restores revoked friendship or removes unrelated enemies. Ambient enemy scans cannot undo this relationship, but real aggression or explicit Contact permanently revokes it for the raid. Recruitment requires a selected, unrevoked candidate at request and deferred conversion time; existing temporary combat refusals, raid-sticky PMC level and Scav chance refusals and the two-active-pickup limit still apply. Selection and recruitment-refresh diagnostics use the `[Allegiance]` log prefix.

When Debug battle recording is enabled, passive `friendlyEncounter` events also record selection/exclusion/revocation, Scav acceptance/refusal and directed Cooperate attempts. The bot envelope identifies nickname/profile/brain, while details identify native role, roll/chance or selection state, group size, the already-evaluated sight gate, trust and current reciprocal squad enemy relationships. These events can distinguish an unselected passive bot from a revoked selected friendly. They do not run perception, combat or medical providers, reroll candidates, or change relationships. Attempts are recorded only after the existing 5 m/look-cone gates, not on every quick-panel frame.

Neutrality repair removes enemy keys for both EFT representations of the same profile (`BotOwner` and `Player`). It publishes the native group enemy-removal event even when the group entry is already absent, allowing external SAIN to discard its separate cached contact. Other profiles keep their relationships and memories.

Opposite-faction recruits retain their original side and PMC role in the invitation, saved roster and subsequent Allegiance raid spawns. Their request permissions and squad hostility follow the human leader through the existing follower conversion and shared relationship policy. Guns for Hire keeps its existing same-side recruitment and leader-side spawn behavior.

Positive damage from an outside AI to the human leader explicitly revokes that candidate's friendship, even with no followers present and before initial activation. The existing squad hostility helper shares the relationship without selecting goals or granting sight/fire permission. Zero-damage notifications and friendly fire from squadmates do not revoke it. Player Scavs retain same-side recruitment and Fence limits in either mode.

## Friendly greetings

Each selected, unrevoked friendly can speak up to twice per raid before recruitment: PMCs use `HoldFire`, while regular Scavs use `MumblePhrase`, resolved by EFT to its normal mumble recording. This shared controller also serves selected Guns for Hire PMCs. The first clear sight of the living human PMC within 50 m starts a distance-based delay: `0.2 + 1.8 × distance / 50` seconds, sampled at first sight. This gives 0.38 seconds at 5 m, 1.1 seconds at 25 m and a maximum scheduled delay of 2 seconds at 50 m. The second line is scheduled a random 1–3 seconds after the first starts, then gets one chance roll when contact and speech are eligible: `current distance / 50`, giving 10% at 5 m, 50% at 25 m and 100% at 50 m. A failed roll permanently skips the second line for that raid; moving farther away or meeting again cannot reroll it. A successful roll survives a playback failure without rolling again. Both lines require current range, native view sector, visible distance and an unobstructed head-to-head sight ray. Only actual speaker activation spends a line.

Within 12 m and with clear sight, the Core `FriendlyEncounterLayer` temporarily owns the selected, unrecruited bot above ordinary patrol, Orbit and looting. Priority 65 is below native danger/malfunction layers; EFT/external-SAIN combat and urgent-work gates remain mandatory. Its action stops the native mover and looks toward the human player for a fixed 20 seconds. Moving away or losing sight does not shorten that period: the bot keeps facing the living player until the original deadline. Completion or interruption releases the layer and applies a 10-second cooldown; close clear contact after that can start another wait. No patrol route, shared preset, faction, pose, enemy memory or aiming provider is rewritten.

Each wait schedules a still-pending Hello (`FriendlyGesture`) 0.5–3 seconds after entry. It can start only while the player remains visible within 12 m and looks toward the bot through the existing 18-degree Cooperation cone. Finishing/skipping the voice sequence does not discard Hello. One accepted native Hello spends the raid's visual greeting; native rejection retains it. Native gesture settings/cooldowns, sprinting, mounted/stationary weapon use, simplified skeletons, non-idle hands and occupied/denied animation retain admission. The layer recognizes its own exact Hello animation, so it does not mistake that for another hands interruption. EFT owns animation completion/cancellation.

Combat, incoming fire, shooting, live grenade danger/throwing, treatment, switching, reloads and other hands interactions interrupt waiting and defer passive speech. `AllegianceFriendlyGreeting` keeps the existing update-hub voice timing; only the selected layer action writes movement/facing or attempts the close gesture. Recruitment or friendship revocation cancels both states. Raid teardown unregisters the hub subscriber, drops the invitation owner and discards selection/interaction state. Immediate speech clears its matching exception in `finally`, including failure; no social timeout callback, aim override, shared talk setting or new event subscription survives a handoff.

## Bot invitations and saved acceptance

On close-layer entry, candidates with at least 60% recruitment acceptance probability roll once and retain their answer. This is the existing PMC tiered level curve (`clamp((player level - bot level + 10) / 20, 0, 1)`), 100% when tiering is disabled, or the Scav acceptance curve derived from its saved encounter chance. It is separate from friendship selection. Candidates below 60% roll only when the player asks. The answer and sampled probability persist for the raid and are reused by player requests; approaching again, restarting the layer or changing levels/settings cannot reroll that answer.

A saved YES permits one invitation per wait while the player remains visible within 12 m, pickup is enabled and squad capacity remains available. PMCs use `Cooperation`; Scavs use `FollowMe`. Direct native audio avoids Cooperation's implicit extra Hello and avoids broadcasting an AI recruitment command. Busy/missing playback defers the invitation without arming a response. At most one bot owns a pending invitation across the raid at a time; timeout, interruption, death, recruitment, revocation and reset release ownership.

The human leader can answer with `Roger`/`Negative` or `OkGesture`/`NoGesture`. The response applies only to that inviting bot while it still has close clear sight and a safe interaction. Positive responses use the existing native request and deferred conversion path, including faction, selection, combat, trust and current pickup-capacity rechecks. Negative responses end the wait and suppress that bot's further invitations for the raid, without creating a bot refusal or changing its saved YES. The player can later change their mind and say Cooperate. Walking away simply lets the wait/invitation expire.

Directed Cooperate during an active wait reaches one eligible bot within 12 m using the existing look cone and exclusive contextual target; outside a wait, the normal 5 m receiver remains unchanged. A bot's cached NO is recorded as an actual refusal only when the player requests recruitment. During an active wait, the response marks a refusal flag and timestamp. The layer's IsActive check retains the stopped, player-facing wait for a fixed 1.5 seconds, independent of speech or animation state. Stop clears the flag and timestamp. Combat, treatment, reload and other urgent work still interrupt immediately. No greeting or invitation is issued during the pause, and repeated requests cannot extend it. This then ends its waiting layer permanently for the raid; PMC refusal memory and Scav saved-answer rules remain in control. A refusal outside an active wait does not take control of the bot to create a pause. Combat/full capacity are temporary refusals and do not create this terminal refusal state.

## Friendly Encounters penalties

Killing one of your current raid recruits in Allegiance adds a **15% relative reduction** to the current final PMC and Scav encounter chances for 24 real hours. Reductions stack additively: one active kill retains 85% of the calculated chance, two retain 70%, and seven or more reduce it to zero. This applies after the same/opposite-side curve and Scav Fence multiplier; it does not subtract percentage points. At the default, one penalty changes 30% same-side to 25.5%, 15% opposite-side to 12.75%, and a standing-0.2 Scav chance from 3% to 2.55%. Changing the setting changes the chance being reduced, without changing penalty identity or expiry.

The stored ledger retains kill identity and expiry, including older entries; no database migration is needed. Spawned squadmates, ordinary friendly bots, kills by another aggressor and Guns for Hire kills do not trigger this penalty. The existing `traitor` kill report records it independently of Raid End Messages, including opposite-faction recruits. Reports are deduplicated by raid and victim, and stored per user ID in the Allegiance database. Each kill expires independently after 24 real hours, including time spent outside the game; the mode/config snapshot cannot reset it. Existing bots retain their one-roll decision.

Roster shows `-15% Friendly Encounters (24h)` in red for one active kill, with the existing countdown. Multiple kills retain independent expiries, so the total reduction falls as each expires. The label and countdown presentation are documented in [My Squad](My-Squad-Screen.md#friendly-encounters-label).

## PMC karma

[PMC karma](PMC-Karma.md) is independent of the Friendly Encounters ledger. Killing an eligible raid recruit or unrecruited friendly costs 0.025; extracting alive with raid recruits restores 0.02 per distinct living recruit. These rules apply in both modes, exclude spawned/saved squadmates and play the native sound only when the confirmed value changes. Karma stays between 0 and 1.

Allegiance additionally restores 0.005 once after a completed raid with selected friendlies and no qualifying friendly kill or successful recruitment. This recovery is silent, is per raid rather than per friendly and cannot stack with the recruitment reward. Any successful recruitment suppresses quiet recovery, even if that recruit later dies or is dismissed. See the karma guide for extraction, death, transit and abandonment outcomes.

## Implementation ownership and verification

- [GameplayModeRuntime](../client/Modules/GameplayModeRuntime.cs) owns fixed setting values and coordinated mode transitions.
- [AllegiancePmcFriendship](../client/Modules/AllegiancePmcFriendship.cs) owns the shared raid-local encounter selection, exclusions and revocation, plus Allegiance's selected-candidate recruitment requirement.
- [AllegianceFriendlyGreeting](../client/Modules/AllegianceFriendlyGreeting.cs) owns the sight/timing sequence through `BotOwnerUpdateHub`.
- [BotGroupRequestPatch](../client/Patches/BotGroupRequestPatch.cs) and the existing follower conversion path own recruitment admission and registration.
- [FriendlyEncounterPenaltyRuntime](../client/Modules/FriendlyEncounterPenaltyRuntime.cs), [FriendlyEncounterPenaltyService](../server/Services/FriendlyEncounterPenaltyService.cs) and [FriendlyEncounterPenaltyPolicy](../shared/FriendlyEncounterPenaltyPolicy.cs) own the ledger, scaling, expiry and display data.
- [External-SAIN compatibility](SAIN-Compatibility.md) remains Core-owned, including native enemy-removal notifications and the exact pre-recruitment speech exception. Shared SAIN settings and ordinary-bot state are not changed.

Existing recruitment, hostility and Friendly Encounters checks validate their respective boundaries; the [PMC karma guide](PMC-Karma.md#verification) lists its separate checks. Fixture success alone does not establish raid perception, speech playback or navigation behavior.
