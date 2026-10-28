# Big Pipe combat

Core base: [combat and cover](../../docs/Combat-Tactics.md), [commands](../../docs/Commands.md), [architecture](../../docs/Architecture.md). Shared Goons contracts: [Knight combat](Knight-Combat.md). Mode/profile contract: [Going Rogue](../README.md). Dated evidence: [validation](../Validation.md).

## Ownership and lifecycle

`BigPipeCombatLayer` runs at priority **76** on the native `BigPipe` brain, role `followerBigPipe`. `GoonCombatLayer` admits only a living, active, registered Going Rogue follower of the local human player. Readiness matches both profile ID and role. Native strategy initialization must succeed before readiness is published. Hostile Big Pipe and other modes retain native behavior.

The original `FollowerBigPipeLayersStrategy` and human follower link remain installed. Big Pipe is a native follower; his adapter does not create Knight BossLogic or move him under an AI leader. The reversible `GoonLayerLease` suspends conflicting native fight/building/warning/request and external SAIN combat/squad layers on this actor. Native danger (80) and maintenance (78) retain priority. Temporary interruptions preserve the objective; failure, spawn abort, mode exit and teardown release the driver and coordination and restore existing native layers without duplicate activation. Detached or dead brains are not reactivated. Core has no assembly dependency on Going Rogue, and the addon has no typed SAIN dependency.

Big Pipe uses the same saved-profile spawn/registration path as Knight. Native `SetToFollow` nevertheless requests a `groupMoving` chooser specifically for Big Pipe/Birdeye, whose route lookup assumes an AI leader. Core's [human-leader compatibility](../../docs/Architecture.md) selects the ordinary chooser for this managed human link while retaining native follower mode and initialization. Core also preserves pre-marked Goons' native activation roles through SPT's PMC-side brain hook. These compatibility fixes do not change the character's native role, saved side or combat selector.

## Decisions and execution

`GoonCombatObjective` centralizes the existing Knight reload, dogfight, committed grenade, medicine/retry, rifle recovery, movement/cover commitments, Need Help and Rifleman ally-support handling. `BigPipeCombatLogic` hands accepted Regroup, There, PushEnemy and SuppressEnemy orders to Core's existing objectives. The role-specific selector and launcher opportunities stay in Going Rogue.

Committed arrival holds use Core's exact transaction/deadline end handler, shared with Knight. Generic cover-flag hold endings must not restart a physically arrived recovery hold every frame or keep it active after its deadline. Expiry returns control to normal reload, medical and tactical selection; it does not force a push or bypass shot safety.

If current-cover fire is unavailable, the selector will not publish another firing-cover move to a candidate already inside Core's shared two-meter arrival envelope. It falls through to suppression or threat-qualified search/recovery instead. This prevents resetting the same arrival/settle cycle at a physically unchanged point. Productive fire from the current cover and genuine moves to different points remain available. Cover-arrival visibility/pressure interrupts also prepare a concrete successor, as described in the shared Goons contract.

`BigPipeVanillaCombat` adapts the current native `BigPipeFightLayer`/`BossKillaLayer` mid-range policy using common actions. It does not instantiate a second native layer or duplicate its event subscriptions.

| Native intent | Going Rogue execution |
|---|---|
| Unseen longer than `KILLA_START_SEARCH_SEC` | Common memory-only search, using tracked knowledge |
| Reloading | Bounded shared hold, after urgent shared reload/recovery work |
| Visible and shootable | Common stationary shooting with normal follower fire safety |
| Initial cover hold / old group contact | Native setting supplies duration, bounded to 0.25–3 seconds; visible fire or recovery pressure releases it. The stale-contact wait occurs once per unchanged enemy/report, then uses memory-only search; new visible contact or a different report rearms it |
| Mid-range pressure | Current-cover fire, an admitted Knight position, shared shooting-cover commitment, then a safe suppression opportunity |
| Repeated suppression | Native 20-second cadence and at least 2m target movement, with personal-contact and common suppression safety gates |
| Unsafe approach | Shared recovery or bounded no-cover fallback under pressure, at the shared Far band (96m), or above the shared Goons boss-relative ordinary-target threat gate; elite targets retain the previous low-threat check |
| Remaining nearby pressure/search | Common memory-only search; no hidden live target transform or copied native zigzag path |

Native reasons use `bigPipe.native.*`; role cover/support reasons use `bigPipe.*`. Actual shots, cover publication, treatment and movement endings remain single-owned by Core. Diagnostics use the existing recorder and never run decision/medical providers solely to collect state.

## Grenade launcher and hand grenades

Big Pipe uses normal native SPT equipment generation, including the installed APBS inventory path when that mod owns generation. Going Rogue does not force an M32A1, override secondary-slot chances or bypass APBS. A grenade launcher can still appear through the normal loadout roll; when present, the existing planned launcher combat and peaceful reload policies apply. His next fresh raid generation determines the new kit, while the saved profile continues to show the last generated equipment until then.

Big Pipe considers a planned autonomous launcher opportunity in the native **27–130m** envelope, with a usable equipped launcher, personally acquired contact and no urgent medical/danger/under-fire commitment. A failed probe backs off for one second; successful activation uses the existing five-second Core grenadier opportunity window. Core owns target planning, navigation/trajectory/impact and friendly safety, switching/draw, firing, cooldown, ammunition handling and return to the primary weapon. Unsafe or unavailable plans leave normal rifle combat available. Explicit Suppress uses the same Core command objective.

After combat and the existing readiness/healing checks, Core's shared patrol maintenance prepares Big Pipe's M32A1 for the next fight. His rifle's normal maintenance completes first, then the same queue draws and reloads the launcher from compatible carried grenades and restores the rifle after native hands completion. It can top off a partially used cylinder as well as an empty one. Threats, pressure/recent damage, medicine, explosive danger, player commands or inspection defer this work; launcher maintenance does not run a second weapon-selection owner. No ammunition is fabricated. See the [shared patrol reload contract](../../docs/Combat-Tactics.md).

Native `BigPipeBotWeaponSelector.ManualUpdate` automatically switches between rifle and launcher by distance; its current `AlliesNearEnemy()` implementation always returns false. A narrowly scoped prefix suppresses that autonomous update only while this addon owns managed Big Pipe. Explicit planned weapon changes remain available, and releasing ownership restores native selection. Native activation can draw the launcher before the addon claims him; when no planned opportunity takes over, his rifle selector requests Core's existing primary fallback and waits for that safe switch. Native mass-artillery publication is not used; explosive fire must pass the common planner. Hand grenades use the existing shared committed-throw logic.

## Knight coordination and companion rules

`KnightSquadCoordinator` exposes actual native member-position outputs tied to the same living target. The shared Goon objective evaluates that exact assigned candidate through Core's firing-position admission after urgent work and existing commitments, ahead of optional launcher/ally support. Initial current-position seeds do not act as commands. Native member recalculation requests reach this managed route without changing the human follower link. Knight's enemy sharing retains its admitted-target, explicit-order, medical, incoming-fire and recent-personal-contact gates.

Knight leads Go Forward through one shared verdict per command/target, assessed with Knight's equipment and known threats; Big Pipe inherits acceptance or refusal. Pipe accepts independently without Knight, or within 30m of the player when Knight is beyond 50m, actively fighting a different target, or lacks knowledge to assess the requested target. Accepted authority stays latched until completion or a fresh, safely prepared Knight directive replaces the task. Routine recalculation and distance changes cannot churn ownership; a replaced player push never resumes automatically. Need Help and Regroup retain player priority. Gear/ownership classification, target admission and urgent movement/fire safety remain intact. Birdeye keeps his sniper push restriction; see the [shared command contract](Knight-Combat.md#combat-command-willingness).

The server enables Big Pipe invitations and a one-time test seed in Going Rogue's separate database. His role-generated skills remain native and later progression persists. Fresh raid equipment, the saved viewing preview, boss secure-container/reserves, Proficiency-only editing, hidden top raid statistics, fixed-kit donation limits, native spawn-inventory protection and no loot returns use the established Goons contracts. The fixed-kit policy now covers all three Goons; Birdeye's [sniper brain](BirdEye-Combat.md) is raid-ready; his automatic test seed remains disabled.

## Source comparison and qualification

Fresh ordinary decisions share [Knight's idle-contact rejoin rule](Knight-Combat.md#commands-and-teammate-support). A completed no-cover retry cannot keep Pipe stationary indefinitely after the player exceeds the normal regroup distance. Existing commitments and useful/recent fighting retain priority.

The native source comparison used `BigPipeFightLayer`, `BossKillaLayer`, `BigPipeBotWeaponSelector` and `FollowerBigPipeLayersStrategy` from the current reference tree, with compiled calls and patch targets checked against installed metadata. The legacy `BigPipeArtilleryLayer` inherited its Knight adapter and delegated support to an older native helper. Its obsolete APIs, unchecked artillery publication and whole-brain ownership were not imported.

The external SAIN 4.5.1 source uses `SAINEnableClass.ExcludeGoons` and `BigBrainHandler.ToggleVanillaLayersForGoons` to choose native versus SAIN ownership for all three Goons according to `VanillaGoons`. Going Rogue leases whichever initialized conflicting layers are actually present on its managed actor. Both configuration paths need a live raid check; the local SAIN source snapshot is not proof of installed byte identity. No SAIN setting, preset or ordinary-bot state is changed, and Big Pipe does not route through our separate SAIN addon.

Fixtures validate production selector branches, urgent priority, launcher handoff, command hooks, ownership/readiness, interruption and cleanup with controlled EFT inputs. The shared launcher fixture separately exercises planning/draw/discharge/fallback. Native test-companion generation and roster persistence have server evidence; corrected in-raid spawning, live navigation, actual launcher ballistics and simultaneous Knight/Big Pipe coordination still require raid qualification. Dated deployment evidence is recorded in the validation ledger.
