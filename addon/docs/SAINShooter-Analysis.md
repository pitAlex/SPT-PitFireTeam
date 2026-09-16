# SAINShooter: Core Marksman feasibility analysis

Date: 2026-09-17. Analysis only; no SAINShooter runtime implementation is included.

Checkpoint preceding this investigation: `c4cc006` (`feat(sain): add combat gestures and stabilize follower objectives`), originally created as `d64834b` and subsequently amended by concurrent workspace cleanup during this analysis. Existing SAINGrunt behavior remains the baseline. `SainMan` is its persisted identity, despite the displayed name.

## Conclusion

Most of Core Marksman's tactical intent can be reproduced in the SAIN addon. SAIN provides movement, firing, perception, cover, medicine and squad infrastructure. The missing work is a Marksman objective policy that chooses and retains useful firing/support positions, manages arrival and weapon preparation, and prevents native pursuit from replacing that intent.

This is substantial policy work, rather than another personality assignment. Reuse the current two addon combat-layer replicas and select the appropriate follower policy inside them. Do not duplicate both layers again or execute the Core Marksman brain alongside SAIN.

Here, **Core** means pitFireTeam's main mod. External SAIN means the inspected SAIN 4.5.1 source, located using `LOCAL.md`. This document reports source feasibility, not proven in-raid equivalence.

## Core behavior to preserve

The principal reference is [Combat-Tactics.md](../../docs/Combat-Tactics.md), checked against `FollowerCombatSniper`, `FollowerCombatSniperObjective`, `FollowerSniperCombatLogic`, `FollowerCombatNeedSniperObjective`, `FollowerCombatRegroupObjective` and the shared helpers in `FollowerCombatCommon`.

| Core Marksman behavior | SAINShooter treatment |
| --- | --- |
| Prefer an existing useful shot, firing cover and support positions; avoid generic assault | Implement a Marksman objective in the addon. Preserve native urgent/self-action priority while controlling ordinary movement decisions. |
| Retain a committed route/hold until a concrete successor exists | Reuse addon ownership/claim/lifecycle infrastructure, but give Marksman its own commitment and arrival rules. A failed scan must not end a useful hold. |
| Support a teammate's push/search, the player under attack, and engaged allies | Add a support objective fed by shared squad intent and accepted SAIN contacts. Native squad actions alone do not reproduce these priorities or positions. |
| Need Sniper is a separate ordered support objective | Extend command eligibility and create the addon equivalent, preserving target commitment, self-preservation, bounded search and completion. |
| Go Forward does not make Core Marksman assault | Preserve that behavior. Core emits PushEnemy but Marksman's `ClearAggressiveRequests` discards it; do not send SAINShooter through SAINGrunt's 100% aggression/ordered-push handler. A different meaning would be a deliberate later feature. |
| Defensive automatic support from second primary or holster | Reuse Core weapon eligibility/ammunition rules and implement bounded addon weapon preparation and retention. Do not assume native weapon selection provides this. |
| Proactive close automatic search needs aggression, threat/ammo eligibility and a real safe destination | Adapt the Core Marksman rules to SAIN-known contacts. SAINGrunt's Rifleman assessment contains useful shared inputs, but is not the complete Marksman policy. |
| Larger Marksman regroup envelope and support-aware regroup exits | Reuse the regroup objective with role-aware admission/distances; do not inherit all SAINGrunt constants. |
| There, Come here, explicit regroup, accepted-goal gating, linger and post-combat healing | Generalize existing addon tactic gates and reuse these systems, preserving their existing priority and ownership. |
| Marksman proficiency plus saved Vision/Precision/Reaction | Reuse Core's existing Marksman baselines, including its SAIN section. Preserve shared aim/recoil/body-first compatibility and per-follower modifiers. |

### Position selection and arrival are central

Core autonomous support prefers appropriate backline/support geometry, complete routes, usable lanes, sensible floors, separation from alternate threats and reduced interference with the player's firing lane. `TryCreateSupportFiringPositionDecision` also mutates Core movement state; it cannot simply be called as a passive SAIN candidate finder. Extract/reuse geometry and validation where practical, supplying an explicit knowledge anchor, and let the addon own native movement and commitments.

Preserve these separate contracts:

- **Proactive automatic close search:** validate a distinct cover-backed tactical destination before drawing the support weapon; maintain at least **16 m horizontal enemy standoff** and a complete route of at most **90 m**. It must not fall back to walking straight to the enemy.
- **Autonomous firing-position arrival:** try a real shot first. If none exists, allow one adjustment within **15 m of the original arrival**, displaced more than **2 m**, with a complete route at most **30 m**, valid lane/floor/threat checks, and the automatic-support standoff where applicable. Then spend one **1.5-2.5 s** wait if still unproductive. Neither adjustment nor wait can restart itself. After release, consider fire/recovery and boss regroup before another opportunistic search.
- **Other tactical cover holds:** preserve the **10 s** tactical window and concrete-successor checks. No-action holds use the initial **1 s** check and subsequent **4 s** retries rather than restarting every poll.
- **Need Sniper:** retain its own **2 s** arrival settle and bounded no-lane retry. Explicit support can consider closer, forward and lateral positions, including complete routes up to **140 m** in its firing-position branches. It is not the autonomous arrival transaction.
- **Recovery and regroup:** retain their distinct survival/arrival policies. A firing perch is not automatically acceptable medical cover.

`FiringPositionArrivalState` already represents the one-adjustment/one-wait lifecycle independently of the full Core brain. It is a good candidate for minimal shared exposure or adaptation, with the existing Core behavior preserved.

The prose aggression description is simplified: `IsMarksmanFiringPositionAllowed` also changes forward-position eligibility above **55%** aggression. Preserve actual source conditions, including the enemy-marksman exception, rather than translating only the documentation's broad description.

### Weapon policy needs explicit ownership

Core keeps a viable visible shot on the current weapon. For unseen proactive close search, it validates the destination first, requests an eligible automatic second-primary/holster weapon, and waits up to **3 s** for the actual weapon, selector and weapon manager to be ready. The destination survives that preparation. An unavailable/rejected switch must not create a move or repeatedly rearm preparation.

At **0% aggression**, defensive automatic support remains allowed; only proactive closing is disabled. Temporary HoldPosition blocks proactive close search. The loaded-ammunition/penetration rules, threat and nearby-enemy conditions must remain applicable. Automatic support is retained while close danger remains, including stationary holds, then returns to the primary at appropriate range or combat handoff.

The native `SAINShootData.SelectWeapon` path is not a ready replacement. In the inspected source its call is in the no-ammunition holster recovery branch, and Core's `SAINPatch.GuardFollowerSainWeaponSelection` currently blocks its `TryChangeWeapon` for **all followers**, including addon followers. Native self-action/reload and dogfight code can still call EFT selectors directly. A future implementation must audit those actual call sites and own only the bounded Marksman selection transaction; it must not add a blanket second weapon/reload controller.

Any new SAINShooter-only native interception belongs in `addon/`. Shared compatibility required without the addon remains in Core.

## What native SAIN actually supplies

Source references below are relative to the external SAIN `SAIN/` directory.

| Native facility | Verified behavior | Consequence |
| --- | --- | --- |
| `Classes/Bot/Decision/FiringPositionFinder.cs` | Searches around the bot at 10/20/32 m radii in 12 directions, throttled to 2 s; checks the last-known head point, effective weapon range, raycast and reachability; keeps a candidate until arrival or 20 s. | Useful candidate-search pattern, but it lacks Core's full support/backline/alternate-threat/arrival policy. It also performs its candidate loop synchronously. |
| `EnemyDecisionClass.ShallMoveToEngage` | For an unseen enemy with recent enough knowledge, chooses this when the enemy route is incomplete or the enemy is marked as a sniper, provided a firing point exists. | Native MoveToEngage is not a general Marksman firing-position planner. Here `IsSniper` describes the enemy, not a follower tactic. |
| `Layers/Combat/Solo/MoveToEngageAction.cs` | Moves to the native finder's position, stops for a shootable enemy, and may sprint beyond 15 m. | Reuse native mover/shooting operations through an addon action whose destination and arrival belong to the Marksman objective. The native action itself reads the native finder's destination. |
| `Layers/Combat/Solo/StandAndShootAction.cs` | On start it may choose an approximately 6 m lateral swing against a target within 50 m. Both StandAndShoot and ShootDistantEnemy route here. | Do not assume this is stationary fire. A retained support perch needs a controlled action/wrapper so native lateral movement cannot discard it. |
| `Layers/Combat/Solo/SearchAction.cs` | Runs the native search system; a visible search target can trigger dogfight movement. | Ordinary Search is not the bounded cover-backed Marksman approach or its arrival adjustment. Preserve native tracking without delegating the entire support objective to Search. |
| `Layers/Combat/Squad/CombatSquadLayer.cs` | Help uses Search, PushSuppressedEnemy uses RushEnemy, and GroupSearch follows/leads the search party. | Both solo and squad routing must honor the Marksman role. Guarding only the solo Search branch would still allow squad assault behavior. |
| `Layers/Combat/Squad/SuppressAction.cs` | Walks along the path to the enemy and performs native shooting/suppression on steering ticks. | Native suppression primitives are useful; the complete action does not preserve a stationary support position or Core's bounded suppression lifecycle. |

Native SeekCover, retreat, self-actions, grenade avoidance and combat shooting remain useful execution infrastructure. SAINShooter should add the missing objective decisions and only the custom actions required to preserve them. Keep the native manager's single decision publication and existing urgent-action handoffs.

## Shared addon infrastructure and required integration changes

1. **Tactic identity and fallback.** Add a new stable SAINShooter identity without renumbering existing enum values or changing the persisted `SainMan` key. Update server tactic validation/defaults, profile options/localization, parsing, readiness and lifecycle gates. SAINShooter should fall back to **Core Marksman** when unavailable/unready, retaining its saved identity; SAINGrunt keeps Rifleman fallback. Recruited native SAIN bots should continue defaulting to SAINGrunt unless separately requested.
2. **Central role/capability mapping.** Current `IsSainManSelected` checks gate runtime state, leadership, personality, recording, regroup and relocation. Generalize addon ownership once and distinguish Grunt versus Shooter policy. Do not make a growing set of inconsistent one-off string/enum checks. Core's command and proficiency checks also currently compare directly to Marksman.
3. **Objective coordination.** Extend `SAINFollowerObjectives` with Marksman engagement/support intent and explicit NeedSniper. Reuse the two existing layers. Exclude Shooter from Grunt's Push objective and 100% GoForward override. Preserve relocation, commanded regroup, healing and urgent survival priorities. New intent/state must reach recorder snapshots and selection/rejection events.
4. **Role-aware cover and regroup.** Existing boss-oriented cover and arrival state are infrastructure, not exact Marksman policy. Ordinary support must preserve a good lane/perch, while recovery keeps immediate safe-cover fallback. Use `GetRegroupNeededDistanceMarksman`: normally **1.5x** the configured trigger, **2x** in Factory mode. Core's normal commanded completion is **24 m** for Marksman rather than Rifleman's 18 m; Factory mode uses the common 10 m distance, and tight regroup remains separate. `SainRegroupBridge` currently hardcodes the Grunt completion tactic and needs an owner/role input. Support-aware regroup release also needs comparison, not just different numbers.
5. **Mixed-tactic support signals.** Core Marksman consumes `CombatEvents` push intent. The addon Push objective currently does not publish those events. Connect accepted autonomous SAINGrunt advances and Core Rifleman advances to a shared support signal, with target, known anchor, committed destination, liveness and release. Preserve the current distinction that Core ordered pushes are not emitted as automatic assistance events. Never trigger support merely because another bot briefly publishes Search, and do not refresh a shared target from hidden live transforms.
6. **Aggression and proficiency.** Start from Core Marksman's **30%** default and preserve saved aggression and weapon modifiers for its tactical gates. Existing addon personality interpolation can supply native secondary behavior, but cannot replace the role policy: even a Coward identity must still allow necessary firing-position work and defensive support; GigaChad must not turn the tactic into a Grunt assault. `FollowerProficiencyValues.ApplySainTacticValues` already contains Marksman values; broaden role mapping without duplicating the baseline in the addon.

On Your Own should release player-distance/anchor restrictions and retain the established investigation exception while keeping the selected Marksman role. It should not silently swap SAINShooter into SAINGrunt. Explicit relocation/regroup still uses the player as its command anchor.

## Knowledge and performance constraints

Core support helpers sometimes use current/live enemy positions and physical enemy clustering. SAINShooter should use accepted SAIN contacts and their **last-known** positions for planning and counts. Live sight/shootability still governs actual firing. Do not reuse an apparently convenient Core helper if it refreshes the enemy, executes Core decisions or samples hidden enemy movement. The planned Enemy Tracking setting is not part of this change.

Reuse passive equipment/medical inputs where appropriate and port the Marksman eligibility rules explicitly. The existing `SAINFollowerPushAssessment` is Rifleman-oriented; for example, Marksman's close-search cluster and role restrictions differ.

Keep planning bounded and event-driven: retain candidate/path results through movement and drawing, cache by meaningful geometry/contact changes, cap expensive probes, spread new searches across updates, and keep failed-plan cooldowns. Add Marksman planning to the existing cover/performance budget rather than stacking a full native finder scan and a full Core support scan on every decision pass. No decision calculation or medication evaluation should be added to recorder/status polling.

## Recommended implementation order and qualification

1. Introduce role/capability plumbing, persistence/UI, Core Marksman fallback, shared proficiency and command routing; add regression coverage that SAINGrunt behavior is preserved.
2. Implement autonomous Marksman firing/support position commitment and controlled fire/movement actions, including the bounded arrival adjustment/wait. Qualify a hidden enemy, a visible clear shot, blocked lanes, unreachable points, boss separation and repeated publications.
3. Implement NeedSniper and mixed Core/SAIN support signals; validate that help/group-search/rush cannot steal the support objective. Check refusal and completion under medicine, recent hits and close danger.
4. Add defensive automatic support and proactive close search with the real-weapon preparation transaction. Cover full-auto second primary, full-auto holster, empty/ineligible support, low penetration, multiple nearby known enemies, 0/30/50/100% aggression, HoldPosition, switching failure and return to primary.
5. Validate both layers, commands, normal/tight regroup, independent mode, opt-out/dismiss/raid teardown, recording and bounded scan cost in mixed squads. Replay source-backed lifecycle scenarios before in-raid movement and aim qualification.

The implementation can cover the main Marksman behavior, but exact movement, posture, exposure and firing outcomes will differ until tested against native SAIN execution. Do not describe a tactic-selector-only milestone as completed Marksman parity, and do not reopen the deferred friendly-fire behavior change as part of this analysis.
