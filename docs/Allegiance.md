# Allegiance

> Play by Tarkov's rules. Alliances are formed, not bought. Settings are locked, and relationships are determined in the field.

Allegiance builds the squad through field recruitment. **Add Teammate** is hidden and manual hiring is rejected on the server. Successfully recruited bots can enter this mode's saved roster through the existing post-raid invitation and acceptance workflow. Allegiance has its own roster, equipment, teammate settings and pending invitations; it starts empty and does not import the Guns for Hire roster.

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
| Loadout Management | Immersive |

Heal Followers remains available as a manual emergency shortcut in Allegiance. Its key can be configured normally; ordinary follower healing behavior is unchanged.

Squad Health Multiplier is also editable in Allegiance and uses the saved value through the existing follower-spawn customization path. Its range remains 1–10 and its existing in-raid editing restriction still applies.

Fixed values are resolved at runtime rather than written over the saved Guns for Hire preferences. Editing the cfg cannot bypass this policy. Returning to Guns for Hire restores the captured preferences; see [settings restoration](Gameplay-Modes.md#saved-settings-and-restoration).

## Finding friendly PMCs

Allegiance selects solo BEAR and USEC PMCs after `FactionHostility.Apply` in the activation hook. At the default Friendly Chance Multiplier of 1, same-faction candidates have a 30% friendship chance and opposite-faction candidates have a 15% chance, relative to the human PMC's side. Each profile has one raid-local decision, with at most three lifetime friendly selections shared across both factions; deaths, recruitment and dismissal never release a slot. Failed rolls do not consume a friendly selection slot and cannot be retried. Current groups, original multi-bot spawn groups and groups awaiting additional members are excluded, including their later survivors. A selected solo joining a group loses this individual friendship. Player-Scav/Fence behavior remains unchanged.

`Miscellaneous > Friendly Chance Multiplier` appears immediately before Squad Health Multiplier. It is an integer BepInEx setting from 1 to 5, enabled only in Allegiance; Guns for Hire displays it disabled with the mode-unavailable tooltip. Each step adds 17.5 percentage points for the player's faction and 21.25 for the opposite faction. Changes affect future candidate rolls only, without resetting existing decisions or the raid cap. Its saved value survives restarts and mode switches independently of the Guns for Hire snapshot.

| Friendly Chance Multiplier | Same faction | Opposite faction |
|---|---|---|
| 1 (default) | 30% | 15% |
| 2 | 47.5% | 36.25% |
| 3 | 65% | 57.5% |
| 4 | 82.5% | 78.75% |
| 5 | 100% | 100% |

## Recruiting and squad relationships

**Cooperate** remains a regular phrase in Help, assignable by right-click. Playback and the Hello gesture work without a bot in front of the player. Recruitment is checked by the receiver: an eligible candidate must be within 5 m, in the interaction view cone and visible through the shared sight gate. The contextual quick prompt retains its 2.5 m interaction range. **Follow Me** does not request recruitment. See [Commands](Commands.md#follow-me--cooperation) for the complete request and reply contract.

Friendship selection is separate from recruitment acceptance. An eligible friendly can still refuse because of combat, level or active pickup capacity. Tiered level refusals are sticky for the raid; repeating the request cannot reroll them. Allegiance permits at most two active field pickups even though up to three friendlies can be selected over the raid's lifetime.

Selected candidates become neutral to the player and squad without changing shared bot settings, visibility or shot permission. After each follower finishes group reassignment and registration, Allegiance refreshes neutrality in both directions between that follower and every living, still-friendly selected candidate. This covers BEAR and USEC equally, including the first recruit forming a squad. The existing profile-alias cleanup removes stale BotOwner/Player enemy keys, clears only those friendly-pair memories and publishes native enemy-removal notifications for external SAIN caches. It never rerolls selection, restores revoked friendship or removes unrelated enemies. Ambient enemy scans cannot undo this relationship, but real aggression or explicit Contact permanently revokes it for the raid. Recruitment requires a selected, unrevoked candidate at request and deferred conversion time; existing temporary combat refusals, raid-sticky tiered acceptance refusals and the two-active-pickup limit still apply. Selection and recruitment-refresh diagnostics use the `[Allegiance]` log prefix.

Neutrality repair removes enemy keys for both EFT representations of the same profile (`BotOwner` and `Player`). It publishes the native group enemy-removal event even when the group entry is already absent, allowing external SAIN to discard its separate cached contact. Other profiles keep their relationships and memories.

Opposite-faction recruits retain their original side and PMC role in the invitation, saved roster and subsequent Allegiance raid spawns. Their request permissions and squad hostility follow the human leader through the existing follower conversion and shared relationship policy. Guns for Hire keeps its existing same-side recruitment and leader-side spawn behavior.

Positive damage from an outside AI to the human leader explicitly revokes that candidate's friendship, even with no followers present and before initial activation. The existing squad hostility helper shares the relationship without selecting goals or granting sight/fire permission. Zero-damage notifications and friendly fire from squadmates do not revoke it. Player Scavs retain same-side recruitment and Fence limits in either mode.

## Friendly greetings

Each selected, unrevoked Allegiance friendly can say `HoldFire` up to twice per raid before recruitment. The first clear sight of the living human PMC within 50 m starts a distance-based delay: `0.2 + 1.8 × distance / 50` seconds, sampled at first sight. This gives 0.38 seconds at 5 m, 1.1 seconds at 25 m and a maximum scheduled delay of 2 seconds at 50 m. The second line is scheduled a random 1–3 seconds after the first starts, then gets one chance roll when contact and speech are eligible: `current distance / 50`, giving 10% at 5 m, 50% at 25 m and 100% at 50 m. A failed roll permanently skips the second line for that raid; moving farther away or meeting again cannot reroll it. A successful roll survives a playback failure without rolling again. Both lines require current range, native view sector, visible distance and an unobstructed head-to-head sight ray. Losing sight or busy speech defers the line without restarting the sequence. Recruitment, hostility revocation and raid teardown stop further greetings. The existing exact pre-recruitment speech scope routes these lines through EFT with or without external SAIN; shared talk settings remain untouched. `AllegianceFriendlyGreeting` runs through `BotOwnerUpdateHub`, normally checking sight twice per second and checking earlier when a phrase deadline falls between those checks. Only actual speaker activation spends a line.

## Friendly Encounters penalties

Killing one of your current raid recruits in Allegiance subtracts `5 × current Friendly Chance Multiplier` percentage points from both friendship chances, after calculating the multiplier's chance boost, with a minimum chance of zero. Each active kill costs 5, 10, 15, 20 or 25 points at multipliers 1 through 5. Scaling uses the current setting whenever chances or the Roster label are evaluated, so killing at a lower multiplier then raising it cannot preserve a smaller penalty. The stored ledger retains kill identity and expiry, including older entries; no database migration is needed. Spawned squadmates, ordinary friendly bots, kills by another aggressor and Guns for Hire kills do not trigger this penalty. The existing `traitor` kill report records it independently of Raid End Messages, including opposite-faction recruits. Reports are deduplicated by raid and victim, and stored per user ID in the Allegiance database. Each kill expires independently after 24 real hours, including time spent outside the game; the mode/config snapshot cannot reset it. Existing bots retain their one-roll decision.

Roster shows the current scaled penalty in red while entries remain active. Multiple kills retain independent expiries, so the total falls as each expires. The label and countdown presentation are documented in [My Squad](My-Squad-Screen.md#friendly-encounters-label).

## PMC karma

[PMC karma](PMC-Karma.md) is independent of the Friendly Encounters ledger. Killing an eligible raid recruit or unrecruited friendly costs 0.025; extracting alive with raid recruits restores 0.02 per distinct living recruit. These rules apply in both modes, exclude spawned/saved squadmates and play the native sound only when the confirmed value changes. Karma stays between 0 and 1.

Allegiance additionally restores 0.005 once after a completed raid with selected friendlies and no qualifying friendly kill or successful recruitment. This recovery is silent, is per raid rather than per friendly and cannot stack with the recruitment reward. Any successful recruitment suppresses quiet recovery, even if that recruit later dies or is dismissed. See the karma guide for extraction, death, transit and abandonment outcomes.

## Implementation ownership and verification

- [GameplayModeRuntime](../client/Modules/GameplayModeRuntime.cs) owns fixed setting values and coordinated mode transitions.
- [AllegiancePmcFriendship](../client/Modules/AllegiancePmcFriendship.cs) owns raid-local selection, exclusions, recruitment eligibility and friendship revocation.
- [AllegianceFriendlyGreeting](../client/Modules/AllegianceFriendlyGreeting.cs) owns the sight/timing sequence through `BotOwnerUpdateHub`.
- [BotGroupRequestPatch](../client/Patches/BotGroupRequestPatch.cs) and the existing follower conversion path own recruitment admission and registration.
- [FriendlyEncounterPenaltyRuntime](../client/Modules/FriendlyEncounterPenaltyRuntime.cs), [FriendlyEncounterPenaltyService](../server/Services/FriendlyEncounterPenaltyService.cs) and [FriendlyEncounterPenaltyPolicy](../shared/FriendlyEncounterPenaltyPolicy.cs) own the ledger, scaling, expiry and display data.
- [External-SAIN compatibility](SAIN-Compatibility.md) remains Core-owned, including native enemy-removal notifications and the exact pre-recruitment speech exception. Shared SAIN settings and ordinary-bot state are not changed.

Existing recruitment, hostility and Friendly Encounters checks validate their respective boundaries; the [PMC karma guide](PMC-Karma.md#verification) lists its separate checks. Fixture success alone does not establish raid perception, speech playback or navigation behavior.
