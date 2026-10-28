# Going Rogue progression plan

**Planned behavior, not implemented by the current combat test builds.** Current behavior and qualification are in [the mode contract](README.md) and [validation](Validation.md).

Going Rogue starts with Allegiance's encounter and recruitment rules, then gradually becomes Rogue through Knight's quests.

## While accompanied by the Goons

- Ordinary Scavs can occasionally be friendly.
- After completing the first Goons quest, exUsecs are always friendly, subject to the hostile-quest restriction below.
- Everyone else is hostile; friendly PMC encounters do not occur.

Friendly relationships and willingness to join are separate: an exUsec being friendly does not guarantee recruitment acceptance.

## Knight quest progression

Completing the first Goons quest unlocks friendship with all exUsecs, including raids without the Goons. Their initial chance of accepting recruitment is low; friendship does not guarantee acceptance.

Further quest progress increases the chance that an exUsec accepts recruitment and becomes a squad member. At the same time, friendly PMC encounters become less likely when the player raids without the Goons.

At the Going Rogue ending, PMCs no longer have friendly encounters and exUsecs are the only recruitable people. Occasional friendly Scavs can remain friendly without becoming recruitable.

## Boss ally deaths and Knight standing (future)

Killing Knight, Big Pipe or Birdeye may reduce Knight trader standing in the future, following the old plugin's trader relationship model. This is separate from ordinary recruited-follower Friendly Encounters penalties. Goons currently receive no encounter-kill penalty; native spawn-loot restrictions remain the present consequence. No Knight trader penalty, standing change or quest response is implemented in this phase.

## Conflicting exUsec-kill quests

An active mission requiring exUsec kills overrides the friendship unlock: exUsecs must not be made friendly, and the player must not be allowed to spawn with the Goons. Future implementation must cover both invitations and actual raid-spawn admission so an existing invitation cannot bypass the restriction.

Retain this guard from the old plugin when implementing progression. Legacy reference: `friendlypmc/server/src/KnightChat.ts` uses `_PlayerHasHostileQuests` to deny invitations; Big Pipe and Birdeye reuse the same check. The client friendship check is `friendlypmc/client/Utils/Utils.cs::PlayerHasKnightQuest`. Quest IDs, relevant statuses and cancellation/completion behavior must be verified against the future quest set rather than copied blindly.

Quest milestones, probability curves and the final transition are future work. This plan does not enable exUsec friendship or recruitment, change quest state, or alter encounter probabilities in the current build.
