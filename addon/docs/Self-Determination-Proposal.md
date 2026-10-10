# SAIN Self Determination adaptation

**Core base:** [Self Determination specification](../../docs/Self-Determination-Proposal.md), [Core combat](../../docs/Combat-Tactics.md) and [commands](../../docs/Commands.md). **Status: proposed, not implemented**, 2026-10-10. This document owns addon-specific changes and the independently identified faults. Existing behavior remains documented in [Combat](Combat.md), [Squad support](Squad-Support.md) and [SAINShooter](SAINShooter.md).

## Scope and ownership

Consume Core's resolved determination and command state only for ready SAINGrunt/SAINShooter followers. Core fallback consumes the same setting. Ordinary SAIN bots, shared presets and native personality difficulty remain unchanged. Aggression continues to own `SainManPersonality`; Self-Determination must not map to another personality.

At 100, reuse current addon On Your Own behavior, including its existing investigation/search exception and native independent combat routing. This does not create new hearing, sight or fire permission. Core still owns admission and shot safety. At intermediate values, the addon keeps using SAIN actions with follower-local objective/cover policy. Native decision publication remains single-owned.

## Verified raid evidence

The following are recorder `time` values, not elapsed time since the first raidStart record. Files are under the battle-record location in LOCAL.md.

| Record | Evidence | Attribution limit |
|---|---|---|
| `20261009-035237-Interchange.jsonl` | At 97.9 s Zver/Hawk are about 16/14 m from the player. At 124.4 s they are 43.1/54.0 m away; Zver has SeekCover with `regroupNoCover`, Hawk is in Regroup. Neither has a recorded trigger event in 116..130 s. Both are non-independent. | Player movement contributes, but bot positions also move away from the player. This is not evidence that every cover move is an ambush decision. |
| `20261009-060834-Interchange.jsonl` | At 1795.1 s Zver/Strelok are roughly 2.4/1.5 m away; at 1810.6 s they are 41.8/43.6 m away. Zver has pushArrivalHold; Strelok has SeekCover + Reload. No recorded trigger events in 1807..1812 s. | Mixed push/reload/cover commitments produce the gap; SeekCover alone does not prove inactivity. |
| `20261009-171202-Woods.jsonl` | Mattdokn starts the last recorded fight about 10.6 m away. Last recorded trigger event: 1260.18 s. At 1331.94 s he is 40.7 m away; at 1380.07 s, 53.5 m. He is healthy, stationary in HoldInCover, with no self action, no current visible/shootable EFT goal, determination predecessor `combatIndependent=false`, and no arrival hold remaining. His automatic push has `weaponNotReady`; regroup's last evaluation remains 1270.38684 s. | The stale regroup reason is an old evaluation, not a fresh coverTravel rejection at each later snapshot. Other squad members do shoot in this fight. The record does not identify which low-level weapon-readiness input remained false. |

These findings justify a liveness correction independently of Self-Determination. They do not establish that all squad members always hide or that the player was shot during each recorded separation.

## Fix A: blocked automatic push must yield arbitration

Source chain:

1. [SainPushRiskBridge](../../client/Modules/SainPushRiskBridge.cs) reads actual active-primary/ammunition readiness, manager readiness and selector changing state. [SAINFollowerPushAssessment](../SAINFollowerPushAssessment.cs) combines these into `WeaponBlocked` and `weaponNotReady`.
2. [SAINFollowerPushObjective.GetDecision](../SAINFollowerPushObjective.cs) treats weapon blockage or medical/pressure blockage as recovery, returns SeekCover and reports the decision handled. Its movement timeout advances only while its own movement executes.
3. `Observe` can change phase back to Approach on a materially changed enemy anchor while the weapon blockage remains. The pressure latch prevents a fresh Recovery transition, but SeekCover continues. The phase label therefore cannot reliably describe useful movement ownership.
4. [SAINFollowerObjectives.FilterDecisions](../SAINFollowerObjectives.cs) permits regroup after a handled push only for Exhausted or Assessing. Recovery/Approach can return early indefinitely.
5. [SAINFollowerSquadSupportObjective](../SAINFollowerSquadSupportObjective.cs) rejects new support whenever Push.Active is true, even if that push has no destination or useful activity.

Required correction:

- Represent whether the automatic push has a productive leg, real arrival hold, brief recovery grace or a passive unresolved blocker. Share that predicate with support and regroup instead of inferring it separately from Active/phase labels.
- Preserve actual reload/draw/medicine, immediate danger, native cover travel and existing bounded arrival/recovery holds. Do not force readiness, switch weapons from a diagnostic read or shorten legitimate treatment.
- Once recovery grace expires and the bot is genuinely settled/passive, allow one ordinary support/regroup evaluation even if the automatic mission identity remains. Being unable to push is not permission to remain indefinitely far from the player.
- If no support/regroup action is valid, retain safe cover and bounded reassessment; do not churn clear/begin automatic pushes every publication.
- Keep ordered mission/contact semantics separate. A stalled automatic push fix must not erase a Go Forward order or its target simply because a weapon is temporarily unavailable.
- On yielding, retire only owned movement/reservation state. Do not clear living enemy memory. Preserve failed-attempt context through regroup so the same impossible advance does not restart on arrival.
- Split passive readiness diagnostics into active-primary, loaded-rounds threshold, manager-ready and selector-changing facts if needed to resolve Mattdokn's exact input. The record reports adequate ammunition but does not prove the other predicates; do not label the gun itself broken from the aggregate reason.

## Fix B: an intentionally empty cover result must not authorize outward fallback

[SAINFollowerCover.TrySelect](../SAINFollowerCover.cs) deliberately returns handled-with-no-point when a completed regroup cannot find valid local cover (`regroupNoCover`). Native SAIN `SAINCoverClass.UpdateCover` responds to any null FindCoverPoint result with `DogFightMove(true, enemy)`, an aggressive no-cover movement fallback.

Our existing [SainCoverSelectionBridge](../SainCoverSelectionBridge.cs) suppresses that fallback only while `WaitingForSelection` is true, which currently requires finder work still pending. A completed, deliberately empty constrained result can therefore fall through to outward movement. This is a source-confirmed escape from the stated post-regroup constraint; the Interchange record contains the matching `regroupNoCover` plus movement/coverTravel symptoms, but does not log the native call stack.

Required correction:

- Distinguish pending selection, completed no valid local cover, and permission to use native fallback.
- Reuse the existing addon-filtered no-cover movement hook for the intentional empty-result case. Suppress only the ordinary aggressive fallback; emergency/defensive dogfight, actual survival escape, explicit orders and independent mode retain their existing paths.
- Prefer a prepared stationary shot/support action or a bounded local hold. Reconsider after a meaningful geometry/contact/command change or the existing bounded selection cadence. Do not create repeated cover scans or a permanent frozen bot when no cover exists.
- Continue toward the player through the existing regroup objective when still outside its completion envelope; do not invent a second movement owner inside cover selection.

## Setting adaptation

| Addon area | Integration |
|---|---|
| `SAINFollowerPushAssessment` | Consume the shared dependence coefficient in player-pull; include effective determination in cache invalidation. Preserve native knowledge, paths, threat and weapon safety. |
| `SAINFollowerPushObjective` / `SAINFollowerObjectives` | Keep latched useful movement; publish passive/yield state for Fix A; use the same policy for advance and escort fallback. |
| `SAINFollowerCover` / `SAINFollowerCoverFinder` | Adjust ordinary candidate ranking using cached route/player/threat geometry. Preserve native survival fallback and existing physical validators. Fix B preserves intentional restrictions after regroup. |
| `SAINFollowerRegroupObjective` / `SainRegroupBridge` | Use shared role/map automatic trigger/completion, retain separate command/tight completion. Existing addon `min(orderedCompletion, trigger - 2)` already demonstrates the required arrival invariant. |
| `SAINFollowerSquadSupportObjective` | Replace binary willingness with the shared protection policy for eligible followers; allow settled, unproductive automatic push to yield. Preserve real target admission and prepare the successor before releasing cover. |
| `SAINFollowerMarksmanObjective` | At zero use close escort positions while retaining sniper firing/support behavior and weapon transactions. Do not alter ordinary native Marksman settings or make the bot charge like Grunt. |
| Lifecycle / recorder | Read shared effective state; release local policy caches on owner replacement; record existing evaluation results only. |

SAIN SeekCover includes travel, hold, direct shooting and suppression. Do not classify it as useless from the enum alone. Useful fire, progressing cover movement, medical work and valid arrival holds protect the current action. An idle hold without a firing/support opportunity must eventually reach ordinary arbitration.

Current recovery (`under fire`, very recent hit, Retreat or non-reload self action) bypasses player-preferred cover selection. Preserve the immediate survival priority. The new dependence policy applies on the next ordinary selection and after actual recovery finishes, not by cancelling an escape whenever the player moves.

The player-support inputs already exist: `BossSupportTarget` uses admitted boss-hit evidence; `SupportTarget` can consume `boss.IsPlayerEngaging` and known teammate engagements. The immediate task is to make these existing opportunities reachable and appropriately weighted, not add a global squad coordinator or a guarantee that one bot always remains exposed to cover the player.

## Performance and qualification

Reuse the current 0.5-second path/engagement cadence, bounded native firing finder and cover budgets (four operations per finder/frame, up to 32 discovered candidates per geometry scan). Determination must not grow the collider search radius, allocate a planner per decision or run a second native provider. Share a passive commitment-state result across support/regroup consumers.

Extend the existing [addon fixture](../../tests/Verify-SainAddonCombat.ps1) at the real objective/publication boundary. Required cases: settled weapon-blocked automatic push yields; legitimate reload/medical travel does not; a new enemy report cannot reopen recovery indefinitely; post-regroup empty cover cannot start aggressive DogFight fallback; unrelated native bots and genuine emergency dogfight are unchanged; no failed-attempt rearm on regroup completion; 100 matches On Your Own; Cover Me restores effective 50 or lower and issues one regroup; both addon tactics and Core fallback agree on state lifetime.

For raids, measure player distance, actual displacement, cover arrival, last real shot, current shot opportunity, objective productivity, automatic evaluation age and support/regroup successor. Recheck the Woods blocked-push and Interchange post-regroup patterns. Keep initial survival separation distinct from prolonged idle abandonment. No claim of improved frame time or guard parity follows from fixture success alone.
