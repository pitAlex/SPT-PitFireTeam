# SAIN addon commands and status

**Base contract:** [Core commands](../../docs/Commands.md). Core owns phrase/gesture input, targeting, range/visibility checks, command state and peaceful request execution. This document describes how a ready SAINGrunt consumes those commands during addon combat; it does not redefine core input rules.

[SAINShooter](SAINShooter.md#commands-regroup-and-lifecycle) shares gestures, regroup, status and lifecycle. Its Need Sniper, Go Forward, suppression and independent-mode differences are described there; the assault/aggression rules below describe SAINGrunt.

## Explicit Contact and friendly targets

Core owns Contact candidate eligibility, including protected roles, player/teammate exclusions, existing fights and perception. For an accepted prioritized, goal-promoting contact on a ready addon tactic, `SainContactEnemyBridge` changes only that registration's `Enemy.MakeEnemy` call from the ambient cause to Core's existing explicit `addPlayer` cause. This permits a commanded neutral Scav to become an enemy through the normal group API before Core seeds its report and synchronizes SAIN. Non-promoting contacts, automatic/non-prioritized reports, ordinary bots, Core tactics and unready-addon fallback retain the original call.

After confirmed group admission, the adapter removes stale ally/neutral membership for that exact target, including inconsistent pre-existing enemy entries. SAIN's native ally cleanup can therefore no longer discard the commanded contact for that reason. Core target retention, native enemy activity/forgetting, actual sight and shot safety remain intact; no reverse hostility, global preset or SAIN enemy-provider patch is added. The hook runs only during command registration, with no recurring scan. `sainContactOverride` records target identity, prior ally/neutral membership and admission without evaluating decisions. Actual raid qualification remains required.

## Aggression commands

- **Hold Position:** temporary 0% aggression, mapped to Coward combat policy.
- **Go Forward:** temporary 100% aggression, mapped to GigaChad, plus the retained push objective below.
- **Go Go Go:** restores saved aggression and releases the push objective through the existing cancellation path.

Speech, begging, fake death, taunting, assignment and mechanical proficiency are excluded from the personality mapping. See [Personalities and aggression](Personalities-and-Aggression.md).

## Go Forward

Accepted orders capture the selected target in `SAINFollowerPushObjective`; no core `PushEnemy` remains pending. The addon replaces prior push/regroup/relocation intent and uses the [shared approach, risk and interruption contract](Combat.md#push-objectives). Core command eligibility and recruit restrictions remain authoritative. Native useful fire, urgent combat and medicine may interrupt execution without deleting the order. Confirmed death, cancellation/replacement, release or expired contact grace end it.

## Suppress

SAINGrunt consumes Core's accepted suppression command into a bounded Squad-layer objective. It retains the requested known target, can use Core's fresh-report fallback when SAIN has no usable suppression point, and uses native firing with shared foliage/hard-cover and friendly-lane safety, and ends after up to two seconds of actual fire or the attempt deadline. It can prepare one native firing position when the current lane is unavailable. Survival, replacement orders and contact loss interrupt it. Grenade launchers are excluded. SAINShooter supports Core's eligible automatic secondary/holster command fallback with bounded weapon preparation. See [squad suppression and ally support](Squad-Support.md) for the full adaptation and limits.

## Regroup and Exit Located

The addon consumes `RegroupNearBoss` once into its squad objective, including tight Exit Located. It uses the same core arrival distances with native path/floor checks. Survival work can interrupt it without cancelling intent; peaceful regroup stays core-owned. [Combat regroup](Combat.md#regroup) describes automatic admission, movement and publication handling.

## There and Come here

Ready SAINGrunt followers now consume the existing `CombatMoveToPointTactical` and `CombatComeToBossCover` commands through `SAINFollowerRelocationObjective`, before push/regroup and ordinary native squad work. The existing player gesture routing, selected follower, visibility/range checks and 30m There limit are unchanged. Peaceful commands stay Core-owned.

- **There:** complete-path tactical walking to the sampled command point, with destination reservations. The point stays fixed when the player moves.
- **Come here:** bounded native cover discovery around the player at consumption, restricted to the Core boss-cover radius and at least one metre of progress toward the player. No suitable cover uses Core's shared complete-path fallback, stopping 1.5m back along the final path segment and within 2m of the sampled player position. Once selected, the destination is committed.
- The addon uses a dedicated action with native SAIN walking/shooting/steering. Point movement yields to a visible shootable enemy or incoming fire, while cover approach can keep walking and firing. No new blind-suppression policy is added. Native medicine, retreat, grenade handling, melee and dogfight retain priority; pending commands keep their original eight-second expiry. Active relocation yields to survival work.
- Core geometry and thresholds are shared in `FollowerCombatCommandGeometry`: 1.25m arrival, 0.35m progress and a four-second stall bound. The extraction leaves Core's boss-approach algorithm unchanged. Arrival uses the existing three-second settle with fire/safety preemption. Cover planning is bounded to eight seconds and retains four native probes per finder per frame. Repeated polls cannot rearm arrival or switch a completed action into stale native MoveToEngage before publication.
- There/Come here replace previous push/regroup intent; new commands and Go Forward replace relocation. Explicit gestures also work during On Your Own. Invalid/no-path destinations give Core's Negative/NoGesture feedback. Enemy loss, opt-out and teardown release owned paths/claims without clearing another owner's destination.
- `sainObjective` events and the objective snapshot include relocation mode, destination, planning/movement/arrival/failure reasons and stall duration. No new Harmony hooks or typed SAIN reference in Core are introduced. Friendly-fire review remains deferred.

## On Your Own and Attention

On Your Own retains native automatic investigation/approach, disables automatic regroup and removes boss-oriented ordinary cover constraints. Explicit combat orders can still act while independent. Core owns the saved patrol/request intent and Attention routing; addon force-release/reset callbacks clean up only addon-owned state. See [admission and recovery](Combat.md#admission-and-recovery).

## Status Report and enemy markers

Core owns rendering, aggregation, display settings, death-marker timers and report sound/direction. Ready SAINGrunt supplies a passive `SainEnemyContact` instead of using EFT's goal as the displayed contact.

- A living accepted EFT goal is required, including On Your Own. The selected native enemy must be valid, known, active, alive and have a last-known position. Exact identity equality between the EFT and native goals is not required.
- The yellow `!` uses SAIN's last-known location. Genuine sight/hearing/shared knowledge may move it; hidden live movement cannot. Red requires native visible/shootable contact and a finite sight age from 0 to 0.35 seconds.
- Native forgetting, release or loss of the known place removes that follower's report. Another follower may keep the shared marker alive. A handled empty or failed native contact does not fall back to retained EFT memory.
- Enemy-status text uses the same provider. `In Combat` requires a finite native sight age from 0 to less than five seconds; otherwise a valid contact gives `Enemy Detected`. No valid native contact gives neither enemy text. Healing and Wants to heal retain priority. This source fix is separate from the historical deployed checkpoints.
- UI reads never select enemies, refresh evidence, clear memory or drive combat. Reaching a last-known point does not itself mean native forgetting.

Other tactics and absent/unready addon state use [core status behavior](../../docs/Commands.md#phrases-and-gestures).

## Command limits

Do not infer full core command parity from shared input. Dedicated addon protection remains separate work. SAINShooter automatic support-weapon transactions are described in [SAINShooter](SAINShooter.md#automatic-support-weapons). Ordered suppression and bounded ally support are documented in [Squad support](Squad-Support.md); they do not reproduce all Core support arbitration. Implemented translations are the ones listed above. See [remaining work](Roadmap.md).

## Pending-heal status

Core's `Wants to heal` label means EFT reports pending first aid or surgical work while the follower is not using medicine (or Core has selected movement to heal). It does not claim that SAIN selected FirstAid/Surgery or approved the position. Native treatment still checks item eligibility, recent damage and all known enemies; the addon can admit a stationary, physically protected cover position through its existing medical exception. See [medical cover](Combat.md) and [raid evidence](Validation.md) for the observed delay and limits.
