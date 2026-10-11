# Guns for Hire

> Hire and equip your squad before deploying. Make Tarkov yours, and play by your own rules.

Guns for Hire is the default gameplay mode and preserves pitFireTeam's configurable squad gameplay. Choose it in **My Squad → Mode**. See [Gameplay modes](Gameplay-Modes.md) for switching, separate rosters and settings restoration, or [Allegiance](Allegiance.md) for the recruitment-driven alternative.

## Building a squad

**Add Teammate** remains available. Preview and hire a teammate through the existing creation flow, then manage that member through Roster and the profile screen. Equipment quotes, payment, confirmation and creation recovery follow the [hiring and loadout contract](Loadout-Management.md#teammate-addition-cost); Guns for Hire does not introduce a separate hiring system or recruitment fee.

The Guns for Hire database holds this mode's saved teammates, equipment, teammate settings and pending recruit invitations. Switching to Allegiance leaves that roster intact and displays the other mode's roster. Switching back restores access to the Guns for Hire roster; members are not transferred between modes. Database paths, legacy import and transaction rules are owned by [Teammate storage](Teammate-Storage.md).

## Settings and equipment

Guns for Hire uses the player's saved mod settings, subject to the existing setting-specific and in-raid restrictions. It does not impose Allegiance's fixed values for Bad Guy, Friendly PMC Side, Enemy Tracking, pickup or Team Escape.

- Choose **Simple** or **Realistic** [Enemy Tracking](Enemy-Tracking.md).
- Configure pickup availability, tiered recruitment, maximum pickups and post-raid recruitment through the existing settings.
- Choose the [Loadout Management](Loadout-Management.md) equipment policy, including its ownership, upkeep and death-loss rules. Both gameplay modes use one shared saved choice, defaulting to Immersive; switching mode keeps the current selection.
- Configure [Team Escape](Team-Escape.md), including whether it is enabled and which extraction points followers may use.
- Manual Heal Followers and Squad Health Multiplier remain configurable, with their ordinary restrictions.

**Friendly Chance Multiplier** is available in both gameplay modes and retains one shared saved value across mode switches. In Guns for Hire it has no effect while **Friendly PMC Side** is on. **Bad Guy** also suppresses encounter rolls so its hostility rule remains authoritative.

Saved squadmates retain the normal profile, loadout, tactic, aggression, progression and post-raid workflows. Guns for Hire is a gameplay policy, not a separate combat brain: follower combat continues to use Core or the ready optional SAIN addon according to the selected tactic.

## Recruiting during a raid

Existing same-side recruitment remains available when the configured pickup settings permit it. With Friendly PMC Side and Bad Guy off, Guns for Hire uses the shared friendly-encounter selection after the faction patch, for solo PMCs of the human player's faction only. Their chance is 30% at multiplier 1, then 47.5%, 65%, 82.5% and 100% at multipliers 2–5. Opposite-faction PMCs never roll, even at multiplier 5; opposite-faction recruitment remains Allegiance-only. Saved roster members continue to use the existing leader-side spawn behavior.

The existing solo/group exclusions, one decision per bot and lifetime cap of three selected friendlies per raid apply. Recruitment, death and dismissal do not release a selection slot. Selected friendlies use the same greetings and two-way squad neutrality repair as Allegiance, and actual aggression revokes friendship without a reroll. Encounter selection does not replace the native same-side recruitment admission or configured pickup rules. Player-Scav/Fence behavior remains unchanged. Selection and repair diagnostics use the `[GunsForHire]` prefix. See [Allegiance](Allegiance.md#finding-friendly-pmcs) for the shared selection lifecycle.

Say **Cooperate** from Help or its assigned key. Phrase playback and the Hello gesture work without a target; recruitment dispatch requires an eligible bot within 5 m, in the player's interaction view cone and with clear line of sight. **Follow Me** resumes squad following and does not request recruitment. The contextual quick prompt retains its normal 2.5 m interaction range.

Combat refusals, configured capacity and tiered acceptance still apply. A level-based refusal is remembered for that bot for the raid; repeating the request cannot reroll it. Player-Scav recruitment retains its native side/Fence limits. See [Commands: Follow Me / Cooperation](Commands.md#follow-me--cooperation) for request admission, replies, conversion and captured aggression.

Field recruits and saved roster members retain their existing differences. Recruitment invitations, acceptance, equipment persistence and any deletion fee follow [Teammate storage](Teammate-Storage.md) and [Loadout Management](Loadout-Management.md#raid-recruit-deletion-fee). Switching modes does not move pending invitations to the other roster.

## Friendly kills and PMC karma

Guns for Hire does not create or apply Allegiance's 24-hour Friendly Encounters penalties.

[PMC karma](PMC-Karma.md) still applies independently: killing an eligible raid recruit or unrecruited friendly costs 0.025, and extracting alive with raid recruits restores 0.02 per distinct living recruit. Native sounds play only when the confirmed value changes; karma is clamped to 0–1. Spawned/saved squadmates are excluded. Allegiance's silent 0.005 peaceful-raid recovery does not run in Guns for Hire, and player-Scav/Fence standing is separate.

## Implementation ownership

- [GameplayModeRuntime](../client/Modules/GameplayModeRuntime.cs) selects saved preferences instead of Allegiance overrides.
- [SquadControlMenuUi.Mode](../client/Components/SquadControlMenuUi.Mode.cs) switches the active mode and refreshes roster/settings/social data.
- [BotGroupRequestPatch](../client/Patches/BotGroupRequestPatch.cs) owns recruitment admission and conversion checks shared with Allegiance.
- [Teammate storage](Teammate-Storage.md) owns persistence; [My Squad](My-Squad-Screen.md#part-3-mode) owns screen presentation.
