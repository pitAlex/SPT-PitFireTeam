# Version 1 release preparation

This is a release-preparation inventory, not a release certification or published description. Updated 2026-10-20. The comparison baseline is the latest existing beta tag, `beta-0.10.3`; the implementation checkpoint is `3b4727d` (57 commits after that tag). Local release notes and bug-tracker locations are defined in [LOCAL.md](../LOCAL.md).

## Player-facing changes to cover

| Area | Version 1 description content | Owning contract |
|---|---|---|
| Gameplay modes | Explain configurable Guns for Hire versus recruitment-only Allegiance, first-visit choice, separate rosters and one-time welcome invitation | [Gameplay modes](Gameplay-Modes.md), [Guns for Hire](Guns-for-Hire.md), [Allegiance](Allegiance.md) |
| Squad economy | Explain paid hiring, real equipment costs, saved kits and raid-recruit removal fees; do not copy abandoned punitive stash-seizure proposals | [Loadout Management](Loadout-Management.md) |
| Equipment modes | Restricted, Immersive and Realistic; Immersive default, shared choice across gameplay modes, no Simple mode | [Loadout Management](Loadout-Management.md) |
| Insurance | Insure eligible teammate equipment through native traders, retain exact-item coverage through supported transfers, avoid return duplicates; explain Restricted exclusions | [Follower Insurance](Follower-Insurance.md) |
| Manual exchange | Spawned teammates: full staged Swap Gear, including pockets/backpack, Apply Changes and cancellation. View Backpack remains immediate and backpack-only | [Swap Gear](Swap-Gear.md) |
| Recruit donations | Give Gear for field PMCs/Scavs, empty 10-by-12 offer grid and Give Items; basic usable upgrades, one-level contents, cargo/drop fallback and later handovers | [Give Gear](Give-Gear-Spec.md) |
| Donation ownership | Surviving PMC gifts stay with the recruit and can improve invitation chance per accepted slot; cancel gift insurance only at final retention. Scavs return remaining given cargo through Fence. Restricted teammate loans are not permanent gifts | [Give Gear](Give-Gear-Spec.md), [Scav cargo](looting/Friendly-Scav-Cargo.md) |
| Commanded loot | Corpse-only five CMD priorities, native Check Him phrase, selective weapon support, dogtag identification and recruited fallback when no spawned teammate remains alive | [Looting](looting/Looting.md), [Commands](Commands.md) |
| Weapon readiness | Actual compatible ammunition, caliber-based support, secondary/holster use and shared reload landing space; ordinary looting does not replace occupied gear | [Primary acquisition](looting/Weapon-Pickup-Primary-Slot-Available.md), [secondary support](looting/Weapon-Pickup-Secondary-Slot.md) |
| Friendly encounters | Current percentage-based chance, greetings/cooperation, same-faction Guns for Hire and either-faction Allegiance recruits, encounter penalties and recruitment limits | [Allegiance](Allegiance.md), [Guns for Hire](Guns-for-Hire.md) |
| Scav companions | Allegiance-only field recruitment, Fence-standing chance, group neutrality/trust, remaining cargo returns at ordinary extraction or transit; no roster invitation | [Allegiance](Allegiance.md), [Scav cargo](looting/Friendly-Scav-Cargo.md), [Fence karma](Fence-Karma.md) |
| Progression and karma | Persistent teammate progression; distinguish PMC karma, Fence reputation and Allegiance encounter penalties | [PMC karma](PMC-Karma.md), [Fence karma](Fence-Karma.md), [Loadout Management](Loadout-Management.md) |
| Tactical customization | Separate Self-Determination from Aggression, command overrides/reset and five Proficiency sliders | [Self-Determination](Self-Determination.md), [Proficiency](Friendly-AI-Performance-Settings.md) |
| Combat and tracking | Core Rifleman/Marksman, Simple/Realistic tracking, squad shot safety, cover/healing/reload recovery and map hazards. Avoid promising perfect navigation or autonomous behavior | [Combat tactics](Combat-Tactics.md), [Enemy tracking](Enemy-Tracking.md) |
| Optional SAIN addon | Implemented SAINGrunt/SAINShooter roles; SAIN plugin alone is not the addon. Explain prerequisites and remaining parity limits | [Addon index](../addon/docs/README.md), [SAIN tactics](../addon/docs/Combat.md) |
| Transit and persistence | Spawned follower transit including Labyrinth, field-recruit departure differences, profile-local storage and account-scoped wipe | [Architecture](Architecture.md), [Teammate storage](Teammate-Storage.md), [Scav cargo](looting/Friendly-Scav-Cargo.md) |

## Existing description gaps

- Remove the obsolete first-beta notice and the blanket Looting WIP admonition; retain honest feature limits and known issues.
- Add a clear version 1 overview and dedicated gameplay-mode explanation. Current opening recruitment wording does not cover Allegiance's opposite-faction/Scav paths.
- Add the two distinct equipment interfaces, Give Gear and Swap Gear. Keep View Backpack's live-transfer distinction explicit.
- Add Self-Determination and update the obsolete four-slider wording to five. Describe command overrides using the current contract.
- Qualify the existing statement that occupied gear is never replaced: it is true for ordinary commanded looting, not for Give Gear evaluation or manual Swap Gear.
- Update the loose-item command instructions: extra CMD interactions are corpse-only. Do not revive the removed container/loose-item CMD prompts.
- Remove the abandoned Restricted-to-Standard roadmap entry unless the user explicitly revives it. Keep future addon proposals separate from implemented SAIN tactics and Allegiance Scav companions.
- Review known-issue attributions against evidence. A symptom is not automatically proven to be a vanilla/SAIN defect.
- Keep links/screenshots/video placement for user review; do not publish or replace external media automatically. Required new captures: mode choice, five-slider Proficiency, Give Gear and full Swap Gear.

## Qualification and release gates

- [x] User accepted Give Gear selection/ammunition behavior within intended basic rules.
- [x] User confirmed Scav cargo return, including the initially reported missing insured attachment.
- [x] User confirmed retained PMC gift in accepted invitation without insurance shield/message.
- [x] User confirmed insured corpse recovery; Restricted lending exercised with a Scav.
- [ ] Confirm Restricted protected-loan exclusion from a PMC invitation.
- [ ] Confirm player/recipient death specifically during Give Items; combat interruption alone is not this test.
- [ ] Confirm the user's outstanding Core/vanilla Allegiance and Swap Gear test from the local task list, or retain it as unqualified.
- [ ] Complete description wording/media review and check current known issues.
- [ ] Consolidate the local version 1 development changelog into release-facing highlights; preserve superseded implementation details in Git/validation rather than publishing the chronological debug diary.
- [ ] Build and inspect Release artifacts only after an explicit release-build request. Debug validation does not certify Release packaging.

## Cleanup disposition

- `3b4727d` checkpoints the implementation, tests and user-qualification record before cleanup.
- Build outputs are ignored for every test project; no DLL/PDB/bin/obj files belong in source commits.
- Give Gear's current contract is separate from its [dated validation ledger](validation/Give-Gear.md). Historical pending statements are evidence for old builds, not the latest test status.
- Temporary per-item insurance traces and runtime Give Gear decision-log wiring are retired. Keep transaction failures, policy/report errors, rollback guards, native shield behavior and the battle recorder.
- Eighteen old repository-root build/test logs were moved into a local, recoverable test-artifact archive. Installed raid logs and battle records were not touched.
- Debug recruit spawn commands remain deliberately DEBUG-only testing tools, as requested; they are not release features.
- Audit older progress-heavy contracts incrementally. Preserve unique behavior and unresolved qualification before retiring duplicate notes. Do not delete installed raid logs, player profiles, teammate databases or battle records as source cleanup.
- This checklist is temporary release work. Move final feature copy into the public description and retain only unique qualification evidence before retiring it.
