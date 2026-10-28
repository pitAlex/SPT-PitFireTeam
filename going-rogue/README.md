# Going Rogue

Core base: [gameplay modes](../docs/Gameplay-Modes.md), [storage](../docs/Teammate-Storage.md), [My Squad](../docs/My-Squad-Screen.md), and [architecture](../docs/Architecture.md).

## Mode and profiles

Going Rogue is an optional client plugin and server module. Both depend on Core. The client registers the third Mode option; the server validates its availability. Core has no assembly dependency on either addon. The description and mode artwork are intentionally pending.

This phase inherits Allegiance's settings locks, encounters and recruitment rules. The stable mode identifier is `GoingRogue`; its per-player encrypted LiteDB lives under Core's `Resources/teammates-going-rogue`. It never imports either existing roster. Switching between the two locked modes preserves the original Guns for Hire settings snapshot. Onboarding remains account-wide, and welcome invitation receipts address their exact mode database. Scav cargo uses Going Rogue's database in that mode.

The server addon supplies companion definitions and presentation permissions. Knight and Big Pipe are generated from their native roles for testing, saved with inventory previews and individual seed receipts, and enabled for invitations/spawning. Receipts prevent duplicate generation and regrant after removal. Birdeye's implemented brain enables invitations/spawning for an existing roster profile; this phase leaves his automatic test seed disabled.

Their actual equipment is generated afresh for each raid through native SPT boss generation. After reserve provisioning, Core saves an independent copy of the exact raid inventory for profile viewing. This includes the boss container/reserves in every loadout mode and retains the last generated kit across raids/restarts. Profile viewing does not generate, repair or refill equipment. Only inventory is replaced on the current saved roster record under the existing persistence lock; health, skills, identity, Proficiency and the Default loadout are not copied from the raid clone. The preview is neither a permanent loadout nor an end-of-raid gear update.

All three Goons expose only the existing Proficiency editor, including aggression and self-determination alongside Vision, Precision and Reaction. Clothing, name/voice, gear and tactic editing are unavailable. View Hideout is hidden on managed profiles and restored for ordinary player profiles. Core enforces the registered restrictions on the server as well as hiding UI controls. Implemented Goons retain recruited-member runtime behavior while saved combat preferences remain effective.

Fresh Goon raid inventories receive a boss secure container and the shared teammate reserves in every loadout mode, including Realistic: Grizzly and Surv12 unless equivalent supplies are already in the backpack, plus ten full stacks of ammunition matching the main weapon. Provisioning runs on the raid clone before its independent viewing snapshot is saved; ordinary teammates retain their existing Realistic equipment rules.

Goons do not accept body or container looting commands. With Goons alone, corpse CMD entries and body/container quick prompts are hidden; a voice or still-valid stale-menu attempt receives native Negative. Eligible other followers can still receive these commands in a mixed squad. Loose-loot commands remain available under their equipment restrictions.

All three Goons use the addon-supplied `GoonGearPolicy` fixed-kit policy. Give Gear retains the actor's primary, existing secondary, holster, helmet and helmet parts, armor/vest shells, accessories and backpack. It does not accept general backpack cargo or replace the backpack. The exceptions are:

- Replace a removable plate in his existing ArmorVest/TacticalVest when a compatible supplied plate has a **strictly higher condition-aware protection score**, using the shared armor-class/remaining-durability comparison. This covers both depleted plates and plates inferior to the offered one; equal or worse replacements are refused. Locked plates, soft armor and helmet parts are unchanged. A supplied vest may donate a direct removable plate without replacing the actor's vest.
- Fill an **empty SecondPrimaryWeapon slot** with a functional weapon whose existing Give Gear score is within **10% either way** of his primary. Native fit, actual ammunition support and reload landing space remain mandatory; the addition cannot reduce his primary's existing supply. An occupied secondary is never replaced. Supporting supplies must fit his existing fast-access space; they do not become backpack cargo.
- Accept supplied hand grenades into existing vest/pocket grids when native placement fits and preserves magazine reload landing space. This applies to Give Gear and commanded loose pickup; surplus grenades stay in the existing drop escrow and never become backpack cargo.

Unused offered items retain normal Give Gear drop-escrow handling. The same restricted planner handles recruited command-gear exchanges and offered loose weapons/plates. Ordinary equipment/cargo pickup routes are blocked for all three Goons so they cannot bypass these restrictions. Ordinary recruits retain their existing rules. These are raid-local changes; each later raid still receives freshly generated equipment.

All three Goons register native spawn-inventory protection independently of Loadout Management. Core snapshots exact raid-start IDs, excluding only ammunition, medicine, food/drink and hand grenades. Native gear and valuables (including keys/coins/medals) cannot survive player extraction, even in Immersive/Realistic. Later player-given items have different IDs and retain existing ownership/recovery, including donated plates and weapons. Transit carries the original protected set rather than snapshotting later gifts. Registered boss allies are explicitly excluded from teammate loot returns. Plate selection remains front-first; magazine reload landing space includes native vest and pocket grids.

Boss character level/XP use the full native experience-table cap (79 in the checked runtime). Skill values come directly from the actual native-generated profile when the companion is first created. Profile preparation sets no skill minima or character specialties; it only corrects character level/XP. The skill panel selects relevant rows without assigning progress. Saved skill progression remains on the roster while later raids generate only fresh equipment.

Existing profiles created with the former imposed floors are corrected once at their next native raid generation. The addon takes the actual generated skills, removes its historical imposed contribution, and preserves progress above those floors along with unrelated native/learned skills, mastering and skill points. The original pre-floor roll was not recorded, so it cannot be reconstructed exactly. Core saves the corrected profile and an actor/version-scoped receipt atomically; newly seeded companions receive that receipt with their untouched generated skills. Further profile views and raid kit generations never rerun the correction. Gear, recovered HP, identity, seed receipts and Proficiency settings retain their existing ownership.

All three Goons' top raid-statistics block (raids, kills, leave/survival rates, K/D and online time) is hidden through an addon presentation flag. Other profiles retain their existing statistics visibility; the native panel state is restored on close or profile switch.

The [raid-results kill list](../docs/Raid-Results.md) includes registered boss allies under their own names, including Knight with `IsSquadMate == false`. His recruited-member runtime classification and hidden profile statistics panel do not suppress those result rows.

Killing an allied Goon does not apply the ordinary recruited-follower Friendly Encounters penalty. Core excludes the three registered Goon roles before kill reporting; server companion definitions register the corresponding exemption for stale reports by saved profile identity/role. Ordinary recruit penalties and native Goon spawn-loot restrictions remain intact. Future Knight trader-standing consequences are recorded only in the [roadmap](Roadmap.md#boss-ally-deaths-and-knight-standing-future).

The role-specific skill display focuses on assault for Knight, heavy weapons for Big Pipe and precision weapons for Birdeye, alongside the native template's skill rows. These are display selections only; no numeric role bonuses are added.

## Raid policy preparation

The intended Allegiance-to-Rogue quest progression is recorded in the [future roadmap](Roadmap.md). It is separate from the current combat testing policy below.

The addon also supplies Goon combat command willingness through Core's `CompanionCommandPolicy` bridge. All three Goons accept valid combat There point moves; Knight/Big Pipe use equipment confidence and known enemy threat for Go Forward, while Birdeye refuses assault pushes. Level and pickup personality are excluded from boss push confidence. Knight and Big Pipe consume accepted pushes through Core's ordered-push objective. All three have separate [combat layers](docs/README.md) with shared Goons lifecycle, recovery and assistance. Birdeye follows Knight's firing positions using native sniper distance preferences and Core Marksman secondary handling. See [combat command willingness](docs/Knight-Combat.md#combat-command-willingness).

The client addon resolves selected saved roles before raid setup and covers the initial follower conversion as well as living registered companions. Rogue presence keeps Allegiance's Bad Guy setting off, including raid transit, without changing saved Guns for Hire settings. Selected Goons block hostile Goons waves and native role spawning. Automatic exUsec friendship is deferred during combat testing. PMC friendship encounters remain disabled during Rogue context. Friendly Scav encounters use the existing checkbox, chance/Fence calculation, solo eligibility, greetings, recruitment, separate-group diplomacy and trust rules; selected Scavs are neutral to both the human and allied Goons. Combat, late-spawn and retaliation behavior still require live raid qualification.

Further exUsec recruitment work, removal of PMC requests, the Knight trader and quest progression are not part of this phase. Friendly Scav encounters remain supported alongside allied Goons.

## Build and qualification

The [Core teammate web editor](../docs/Teammate-Web-Editor.md) displays **Going Rogue** only while this server addon is registered. Knight, Big Pipe and Birdeye remain visible but disabled there, using the same registered `ProficiencyOnly` role policy as their client customization restrictions. Core rejects their editor load/save requests even if renamed; ordinary recruits, including exUsec, remain editable. Without the addon, the web editor neither opens nor initializes Going Rogue storage and does not load an addon assembly.

Build Debug with `dotnet build going-rogue/client/pitFireTeam.GoingRogue.csproj -c Debug -p:BuildSAINAddon=false` and `dotnet build going-rogue/server/pitFireTeam.GoingRogue.Server.csproj -c Debug`. Use Core's existing minimum game/SPT references. Deploy matching Core binaries and both addon outputs; do not distribute reference assemblies or databases.

The gameplay-mode and follower-database fixtures cover configuration restoration, mode isolation and companion restrictions. Knight/Big Pipe fixtures cover objective priorities, assistance, cover timing, launcher handoff, readiness and layer restoration; installed metadata checks cover the compiled APIs and patch targets. EFT UI and raid behavior require live qualification.

See [dated validation](Validation.md) for current checks and deployment evidence.

Going Rogue's database is intentionally outside launcher wipe handling in this phase. Existing Guns for Hire/Allegiance wipe behavior is unchanged.
