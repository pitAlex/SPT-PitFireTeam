# SAIN addon remaining work

**Base:** [Core roadmap](../../TASKS.md) and [Core combat](../../docs/Combat-Tactics.md). This page records only addon-specific gaps and qualification; it does not authorize speculative gameplay changes.

## Raid qualification

- [SAINShooter](SAINShooter.md): native firing-position usefulness, Need Sniper, failed/blocked approaches, survival interruptions, role switching and mixed-squad behavior. Its documented parity limits are intentional.

- [Squad support](Squad-Support.md): exact-target ordered fire, obstruction/friendly lanes, cancellation, mixed Core/addon engagement and push cues, Grunt boss protection and pusher-relative support, bounded native cover/firing positions and simultaneous Shooter planning.
- Ordered/automatic push: forward cover, provisional/bent walking, pressure/medical recovery, contact gaps, failed approaches, replacement orders and On Your Own.
- SAINGrunt protective cover: forward-side choices with the player behind, lateral offsets, empty/unsafe forward sectors and stable travel/arrival when player or threat geometry changes.
- Regroup: productive-combat priority, complete-route/floor arrival, post-regroup cover constraints and publication handoff.
- There/Come here: destination commitment, cover discovery/fallback, live firing/medicine interruptions, arrival and cleanup.
- Aggression anchors/interpolation and temporary command overrides in a fresh raid.
- Native first aid/surgery, release into core recovery, markers/status text and recorder ownership.
- Measured frame time with comparable conditions; fixture counts do not establish FPS or navigation quality.

## Unimplemented extensions

- [Enemy Tracking](Enemy-Tracking.md): addon Simple adaptation of the common planned mode contract. No setting is implemented.
- Grenade-launcher support is excluded from the current suppression scope at the user's request.
- Core's complete support arbitration and broad support-position sampling are not replicated. Implemented Grunt boss protection/ally support and Shooter automatic support-weapon transactions use the bounded native capabilities described in their topic documents; they do not establish full Core parity.
- Investigate observed override lifetime and native medical cancellation using fresh recordings before changing policy; see [raid evidence](Validation.md#raid-evidence-and-limits).

The old blanket claim that SAIN followers ignore player distance when selecting cover is superseded by the implemented boss-cover policy. Its adequacy remains a raid qualification item. The deferred friendly-fire investigation belongs to [core compatibility](../../docs/SAIN-Compatibility.md#deferred-friendly-fire-investigation), since shot safety must also work without the addon.
