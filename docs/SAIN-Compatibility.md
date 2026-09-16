# Core compatibility with the external SAIN plugin

**Scope:** compatibility owned by pitFireTeam core and needed whether the optional addon is installed or not. SAIN means the external `me.sol.sain` plugin. The addon is a separate combat brain documented under [addon/docs](../addon/docs/README.md).

## Runtime ownership

| Runtime | Follower combat owner |
|---|---|
| SAIN absent | Core |
| SAIN installed, addon absent | Core |
| Addon unready or a core tactic selected | Core |
| Both plugins ready, SAINGrunt selected | [Addon solo/squad replicas](../addon/docs/Integration.md) |

External SAIN may patch low-level EFT calculations even when core owns combat. General compatibility is gated by external-SAIN presence, not by `UseSainFollowerCombat`. Core has no typed SAIN dependency. Ordinary bots retain native SAIN behavior except for the shared vision-loop recovery below.

## Core ownership while SAIN is installed

General external-SAIN compatibility belongs to the main plugin and is gated by `IsSAINInstalled`, not addon presence. It must behave consistently in both configurations:

- SAIN installed, addon absent;
- SAIN installed, addon present.

Core owns, among other things:

- follower-local proficiency normalization and the finalized Vision, Precision, and Reaction contract;
- final aim-time, recoil, body-part, and other calculation compatibility required because external SAIN patches EFT;
- follower/enemy friendship and hostility repair;
- contact propagation, enemy-state synchronization, target acquisition, and perception compatibility;
- general friendly-fire and shot-safety compatibility;
- any reflection or Harmony boundary required to keep external SAIN compatible with pitFireTeam followers in all supported runtime modes.

Core compatibility must be follower-scoped and must not mutate SAIN's shared preset objects. The addon may consume the already-finalized follower state, but it cannot rewrite that state through general SAIN patches.

Grenade awareness follows the active avoidance owner. In core-combat mode with SAIN installed, `FollowerSainGrenadeAwarenessPatch` restores the `BotsController.OnGrenadeThrow` notifications that SAIN 4.5.0/4.5.1 skips for SAIN-enabled followers, forwarding them to native `BewareGrenade.AddGrenadeDanger`. Followers already receiving native notifications are not notified twice or given another recognition roll. The corresponding SAIN `EnemyGrenadeThrown` tracker is bypassed for core-controlled followers to prevent duplicate warnings and fallback registrations. Native reaction probability, delay, smoke handling, cover selection, escape movement, and danger expiry remain authoritative. With ready SainMan addon combat active, SAIN's existing grenade routing remains in place. Core tactics retain the core notification route. In-raid escape and return-to-command behavior still require verification.

Tripwire awareness is core-owned through `FollowerTripwireAwarenessPatch`. It observes the real trigger and activation pin sound, confirms the tripping follower or nearby followers who hear the unobstructed sound, warns with `Spreadout`, and records the actual grenade in native `BewareGrenade`. The danger lifetime covers the real fuse; per-grenade/per-follower deduplication prevents duplicate warnings or SAIN tracking. This preserves native escape and cover selection while adding reliable recognition of a confirmed tripwire.

After the native escape, core `FollowerTripwireLayer` (priority 101) keeps a confirmed live tripwire above ordinary combat, requests, and follow. It reuses `FollowerCombatLayer.CreateBigBrainAction`: our combat hold/shoot actions defend in place, `CombatDogFightAction` handles incoming fire, and `HealAction` runs only in occupied safe cover without immediate enemy pressure. An optional movement predicate on the shared dogfight payload rejects paths through the grenade radius; ordinary combat has no predicate. Unsafe positions and other immediate dangers yield to native avoidance. Detonation or danger expiry releases the layer so combat, requests, or follow can resume. This general follower behavior is core-owned in all SAIN/addon modes.

Follower sound reactions run from a postfix on `GlobalEventDispatcher.PlaySound`, independently of EFT's per-bot hearing subscription. This preserves the existing follower hostility, audibility, cooldown, and threat-priority filters when SAIN 4.5.1 skips `BotHearingSensor.Init`. The old `BotHearingSensor.OnSoundPlayed` postfix is replaced, so SAIN 4.5.0 and no-SAIN runs also dispatch each sound to the follower handler once. No follower lifecycle subscriptions or changes to ordinary bot hearing are required.

Follower aim-point enhancement wraps `SAINShootData.GetAimTarget`: SAIN 4.5.0 takes `(Enemy, BotComponent)`, while 4.5.1 takes `(Enemy)`. A validated, compiled accessor captures the follower's `EnemyInfo`, then the complete native SAIN selector runs first. For followers, the postfix first substitutes an eligible body point using `FollowerAimTargetPolicy.TryGetBodyFirstShootPoint`, then applies the existing verified-head promotion once per retarget window. If the body is blocked, the native choice remains the baseline, including a native exposed-head choice. Calls that pass through EFT's `GetVisiblePartToShoot` are marked and enhanced only once at the outer SAIN boundary. The separate EFT patch is also a postfix, so vanilla/core calls follow the same native-first rule without patching SAIN's `BodyPartToShootPatch` itself. SAIN 4.5.1's optional weighted `AimTarget.ChosenPart` is read through a compiled accessor, while 4.5.0 uses `EnemyInfo.LastPartToShoot` and restores a native head after that version's later center-mass height clamp. Ordinary bots remain unchanged; follower native fallbacks remain when no body point or head enhancement is eligible. Shared aim weights and general SAIN presets are never modified.

Follower foliage compatibility removes foliage/grass from SAIN 4.5's line-of-sight, vision, and shooting ray masks at every distance, replacing the previous 10-metre exception. Hard geometry remains in those follower rays; non-followers retain SAIN's original masks. This bypass changes only SAIN's binary foliage veto: EFT look checks, `FollowerEnemyInfoCorrection` (including its own 10-metre foliage exception), suppression safety, and decision/commitment timing are unchanged. It does not disable foliage handling throughout the follower pipeline.

The mask bypass runs inside SAIN's native command builder through a validated startup transpiler. Follower status and the three query parameters are selected once per enemy; native body-part sampling, command order, geometry, and job cadence are preserved. The former reflection-based replacement builder is removed. An unsupported instruction/member layout logs an error and leaves the native builder unchanged. Compare the parent `VisionRaycastJob.EnemyVisionJob` timing when profiling this optimization, since removing the old separately measured prefix changes attribution as well as runtime overhead.

Player-visual contact promotion is one example of this core boundary. A target genuinely seen by the player is reported as visual contact rather than sense-only contact. If the follower has not independently seen the target, current `IsVisible` and `CanShoot` remain false while core seeds the complete personal contact record at the promotion timestamp. The addon is not involved in that compatibility path.


## Shared vision-loop recovery

The Shoreline EFT log at 03:38:47.818 records a collection-modified exception in native `SAINBotLookClass.UpdateLookForEnemies`, escaping the shared `VisionRaycastJob.UpdateEFTVision` coroutine. This can halt sight updates across SAIN bots. The initiating collection mutation is not yet attributed; the near-simultaneous core acquisition is a lead, not proof.

Core `SainVisionRecoveryPatch` wraps the native iterator factory for all SAIN bots, with or without the addon. On failure, `SainVisionRecoveryEnumerator` logs the exception, disposes the failed iterator, waits one second and creates a fresh native iterator inside the same Unity coroutine. It never starts another Unity coroutine, resets bot combat, clears enemy memory, or restarts the separate raycast job. Native yields and normal completion are preserved. Native job disposal, destroyed controller and explicit wrapper disposal stop retries. Factory/update/current/disposal failures are contained; repeated reports are limited to 30 seconds per wrapper and include cumulative failures. A persistent underlying fault can still prevent a full vision pass; this is recovery containment, not a claim that the collection-mutation cause is repaired.

`tests/Verify-SainVisionRecovery.ps1` validates the installed native factory/lifecycle signatures and runs 23 checks against the production wrapper and real Harmony boundary, including a real HashSet mutation, successful retry, repeated faults, factory failure, logging failure and teardown. Full in-raid recovery still requires qualification. The hook must be installed before the raid's native coroutine starts; this build cannot retroactively repair an already-running old session.

## Proficiency baseline projection

Finalized follower vision and scatter baselines are projected into follower-local EFT Core settings through `FollowerSainEftCoreProjection`. SAIN 4.5.1 config application does not populate those fields. Normalizing only `Info.FileSettings` is insufficient. Vision/Precision/Reaction mapping and mechanical normalization remain in [Core proficiency](Friendly-AI-Performance-Settings.md).

## Cost boundaries

Core route probes reuse a thread-local NavMeshPath. The optional native hooks cache compiled BotOwner access and bind aim/friendly-fire parameters directly, avoiding repeated reflection and Harmony argument-array boxing. These are source-level reductions; measured frame-time improvement requires comparable raids.

## Deferred friendly-fire investigation

User explicitly deferred changes. Revisit Shoreline `20260916-183446-Shoreline.jsonl`, around raid time 1661.970 (local time approximately 18:57:41). Medved lost 40.5352 chest health while automatically regrouping about eight metres from Brick. Brick was in native StandAndShoot, with a trigger event about 16.6 ms before the inferred hit; the movement crossed his recorded aiming lane. This strongly suggests friendly fire but the recorder has no attacker identity, so attribution and frequency are not proven.

Compare core's intended-target and actual-shot-direction checks with `FollowerSainFriendlyFirePatch`: both SAIN overloads currently check the supplied barrel direction; the target overload uses the target only for distance. Installed SAIN AimClass calls the friendly-fire check before `NodeUpdate`. Investigate a final pre-shot guard using core's established lane helpers and add passive damage attribution before concluding causality. Include ordinary shooting, suppression/manual shooting, moving squadmates and ordinary-bot isolation. No friendly-fire behavior was changed for the ordered-push fix.

## SAIN 4.5.0 / 4.5.1 compatibility checks

On Windows, run `./tests/Verify-SainCompatibility.ps1 -GameRoot '<SPT game root>'`. The harness compiles the production targeting patch and sound-dispatch methods with controlled EFT/SAIN stand-ins, then exercises them with the project's real Harmony DLL under .NET Framework. It checks both target signatures, native visibility gates, no-target behavior, ordinary bots, recruitment/dismissal, teardown, and exactly one follower callback with or without an EFT hearing subscription. The existing body-part policy and sound filters are stand-ins, so the checks do not establish in-raid perception or shooting quality.

Raid validation should cover a fresh teammate and a recruited bot hearing hostile gunshots, neutral/friendly sounds remaining ignored, partially exposed enemies retaining native aim-point selection plus bounded proficiency enhancement, and startup logging `Native follower aim-target proficiency enhancement applied.` with no unsupported-layout error. Repeat with the supported SAIN version and keep the optional addon disabled unless its separate qualification is intended.
