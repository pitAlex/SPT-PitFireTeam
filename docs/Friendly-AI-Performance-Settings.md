# Friendly AI Performance Settings Investigation

**Status:** implemented per-follower proficiency controls; gameplay calibration pending

**Target:** SPT 4.1.3, SAIN 4.5.0, pitFireTeam `0.10.1`

**Investigated:** 2026-08-24

This document identifies the settings and runtime calculations that determine follower vision and firearm performance. It also documents the implemented user-control boundary, which changes execution proficiency without changing combat tactics or reintroducing action churn.

For frame-time cost, multi-follower scaling, and Battle Recorder A/B settings, see `docs/Runtime-Performance-Testing.md`.

The SAIN Default baseline, follower-local values model, persistent per-teammate percentages, profile UI, runtime modifier, and final aim-time patch are implemented. Runtime gameplay calibration across the validation matrix remains pending. `docs/SAIN-Integration.md` is authoritative for the addon boundary.

## Executive conclusions

1. EFT has separable controls for:
   - maximum vision distance,
   - visual-acquisition speed,
   - time to finish an aim plan,
   - aim-offset size and convergence,
   - recoil and trigger cadence.
2. There is no single native **reaction-time** value. Observed reaction time is a pipeline:
   - target enters the vision envelope,
   - visibility accumulates to confirmed contact,
   - the target becomes or replaces `GoalEnemy`,
   - the aiming controller builds and completes an aim plan,
   - the shooting worker passes state and trigger-cooldown gates.
3. The implemented user surface groups the source-accurate runtime controls into three understandable proficiency values:
   - **Vision** applies one percentage to maximum vision distance only.
   - **Precision** applies one percentage to aim-offset convergence/scatter and contributes half of final aim speed. External-SAIN compatibility for this value is core-owned and must not depend on the addon.
   - **Reaction** applies one percentage to visual-acquisition speed, contributes the other half of final aim speed, and scales the core close-dogfight direct-fire gate.
4. Reaction does not modify hearing, memory, tactics, target selection, decision cadence, `WAIT_NEW_SENSOR`, or `WAIT_NEW__LOOK_SENSOR`.
5. User values must be follower-local, neutral by default, and applied at the final calculation boundary. Direct per-follower edits to most `BotSettings.FileSettings` objects are unsafe because SPT 4.1.3's `BotSettingsComponents.Copy()` shallow-copies every category except `Core`.
6. SAIN calculation patches remain active for a SAIN bot even when SAIN combat layers are not active. Disabling SAIN layer ownership is not sufficient to preserve a pitFireTeam setting.
7. Accuracy must not be implemented by changing tactical selection. Low- and high-skill followers should choose comparable cover, support, regroup, heal, and engagement decisions; only shot execution should differ.

## Source scope

pitFireTeam:

- `client/Components/BotFollowerPlayer.cs`
- `client/Components/BossFollowerPlayer.cs`
- `client/Modules/SainAddonBridge.cs`
- `client/Modules/BattleRecorder.cs`
- `addon/SAINFollowerPersonalityPatch.cs`
- `addon/SAINFollowerAimSwayPatch.cs`
- `addon/SAINFollowerHitAccuracyPatch.cs`
- `addon/SAINFollowerRecoilPatch.cs`
- `addon/SAINFollowerLowLightVisionPatch.cs`
- `addon/SAINFollowerBushVisionPatch.cs`

SPT 4.1.3 `Assembly-CSharp`:

- `BotSettingsInGameModif.cs`
- `BotCurrentSettings.cs`
- `BotSettings.cs`
- `BotSettingsComponents.cs`
- `BotAimingData.cs`
- `BotScatteringData.cs`
- `BotTargeting.cs`
- `EnemyInfo.cs`
- `EnemyPartVision.cs`
- `LookSensor.cs`
- `EFT/BotMemory.cs`
- `ShootData.cs`

SAIN 4.5.0:

- `SAIN/Patches/Shoot/AimDataPatches.cs`
- `SAIN/Patches/Shoot/RateofFirePatch.cs`
- `SAIN/Patches/Aim/BodyPartToShootPatch.cs`
- `SAIN/Patches/VisionPatches.cs`
- `SAIN/Classes/Bot/Sense/SAINVisionClass.cs`
- `SAIN/Classes/Bot/EnemyClasses/Vision/EnemyGainSightClass.cs`
- `SAIN/Classes/Bot/EnemyClasses/Vision/EnemyVisionDistanceClass.cs`
- `SAIN/Classes/Bot/WeaponFunction/Recoil.cs`
- `SAIN/Classes/Bot/WeaponFunction/Firerate.cs`
- `SAIN/Extensions/SainSettingsExtensions.cs`
- `SAIN/Classes/Bot/Info/BotDifficultyClass.cs`
- `SAINServerMod/Extensions/PresetTunerExtensions.cs`

The earlier SAIN 4.4 personality and weather reports were used as leads and rechecked against the 4.5.0 source. The weather conclusions remain valid. The personality report is now stale in material ways; current pitFireTeam code forces some spawned-follower personalities and wires several follower shooting patches that were not active in the older report.

## Runtime ownership matrix

| Runtime | Combat decisions | Vision and shooting calculations |
|---|---|---|
| No SAIN | pitFireTeam core BigBrain | EFT calculations |
| SAIN installed, addon missing | pitFireTeam core BigBrain for followers | Mixed: SAIN `GetSAIN` patches still replace aim time, fire rate, body-part choice, vision speed, and vision distance |
| SAIN plus addon | pitFireTeam custom SAIN Squad-derived follower combat layer | The same core-owned proficiency and external-SAIN compatibility boundary; addon differences come only from its combat decisions/actions |

This is the central compatibility constraint. A control that works only by changing the pre-SAIN EFT file settings can be clamped, overwritten, or replaced later.

### SAIN addon boundary

The optional addon exists solely to replace pitFireTeam core/vanilla BigBrain combat with a custom follower combat layer based on SAIN's Squad model, making the human player the leader and allowing custom SAIN actions. Vision, Precision, Reaction, SAIN Default normalization, and compensation for external SAIN calculation conflicts belong to core and must work with or without the addon. The addon must not tune these calculations through general SAIN patches or shared settings-object mutation.

## Current follower baseline

### Core follower settings

Every `BotFollowerPlayer` now owns one generic `FollowerProficiencyValues` object cloned from `FollowerProficiency.DefaultValues` at construction. The object has separate `Vanilla` and `Sain` sections so both calculation paths share one follower lifetime without mixing their field formats.

`BotFollowerPlayer.SetFollowerSettings()` applies the `Vanilla` section. It selects the configured vanilla template difficulty and reads the follower runtime modifier, aiming, vision, hearing, and shooting values from that object instead of embedding the proficiency numbers in the application code. Boss/BirdEye proficiency exceptions are held in the same vanilla section. Tactical and capability settings—cover policy, healing, loyalty, patrol, grenade permission, and similar behavior—remain outside the proficiency object intentionally.

Saved PMC squadmates finalize that follower-owned object after their combat tactic has been assigned and before the vanilla runtime modifier is constructed. Rifleman and Protector retain the current PMC baseline. Marksman applies a narrow trade: moderately better clear-LOS range, recognition, and distance scatter, with a modest penalty while moving, turning quickly, or firing automatically in close combat. Marksman grass vision is only `1.1` versus the ordinary follower's `1.0`, and both keep `LOOK_THROUGH_GRASS = false`; boss-style foliage penetration is not part of marksman proficiency.

The selected vanilla role template is captured before those tactic values are applied. This preserves the original templates of recruitable non-PMC followers instead of replacing their core proficiency with PMC constants.

| Vanilla value | Rifleman / current PMC | Marksman |
|---|---:|---:|
| `Core.VisibleDistance` | `185` | `210` |
| `Look.VISIBILITY_CHANGE_SPEED` | `1.2` | `1.5` |
| `Core.ScatteringPerMeter` | `0.045` | `0.043` |
| `Aiming.SCATTERING_DIST_MODIF` | `0.67` | `0.64` |
| `Aiming.BOTTOM_COEF` | `0.05` | `0.06` |
| `Aiming.COEF_FROM_COVER` | `0.30` | `0.25` |
| `Aiming.COEF_IF_MOVE` | `1.0` | `1.15` |
| `Aiming.TIME_COEF_IF_MOVE` | `1.1` | `1.25` |
| `Shoot.AUTOMATIC_FIRE_SCATTERING_COEF` | `1.5` | `1.7` |
| `Shoot.WAIT_NEXT_SINGLE_SHOT` | `0.10` | `0.13` |
| `Look.MAX_VISION_GRASS_METERS` | `1.0` | `1.1` |

Relevant current values include:

- follower-local runtime modifier:
  - `VisibleDistCoef = 0.9`
  - all accuracy, precision, gain-sight, hearing, and trigger coefficients remain `1.0`
- aim time:
  - `BOTTOM_COEF = 0.05`
  - `COEF_FROM_COVER = 0.3`
  - `PANIC_COEF = 1.0`
  - `COEF_IF_MOVE = 1.0`
- accuracy:
  - `MAX_AIMING_UPGRADE_BY_TIME = 0.15` for non-Goons
  - `BAD_SHOOTS_MIN = 1`, `BAD_SHOOTS_MAX = 2`
  - `AIMING_TYPE = 6`
- first contact:
  - `FIRST_CONTACT_ADD_CHANCE_100 = 20` for non-Goons
- hit disturbance:
  - `BASE_HIT_AFFECTION_DELAY_SEC = 0.2`
  - smaller hit-angle limits than the EFT defaults
- core single-shot cadence:
  - `WAIT_NEXT_SINGLE_SHOT = 0.1`
  - `WAIT_NEXT_SINGLE_SHOT_LONG_MAX = 1.8`
  - `NEXT_SINGLE_SHOT_PAUSE = 3.0`

BirdEye additionally gets `VisibleDistCoef = 0.8`, which multiplies the base follower `0.9`, plus `SCATTERING_DIST_MODIF = 0.2` and `HARD_AIM = 0.9`.

### SAIN Default proficiency normalization

When the external SAIN plugin is installed, `FollowerSainProficiency` now anchors follower proficiency to SAIN 4.5's server-generated built-in `Default` preset. The selected SAIN preset continues to affect ordinary bots and follower policy settings, but it no longer makes followers easier or harder through the proficiency fields covered here.

After each follower's SAIN Default role/difficulty values are resolved, the same follower-local tactic is reapplied. SAIN Marksman receives a modest long-range range/scatter/aim advantage while its faster-CQB window, turning aim, moving aim, and automatic-fire control are weaker than Rifleman. Rifleman remains at the normalized SAIN Default baseline rather than receiving a broad long-range nerf.

| SAIN value | Rifleman / Default baseline | Marksman |
|---|---:|---:|
| `Core.VisibleDistance` | `250` | `275` |
| `Core.ScatteringPerMeter` | `0.08` | `0.07` |
| `Aiming.DistanceAimTimeMultiplier` | `1.0` | `0.9` |
| `Aiming.AngleAimTimeMultiplier` | `1.0` | `1.15` |
| `Aiming.FasterCQBReactionsDistance` | `30` | `15` |
| `Aiming.FasterCQBReactionsMinimum` | `0.33` | `0.45` |
| `Aiming.MAX_AIMING_UPGRADE_BY_TIME` | `0.25` | `0.20` |
| `Aiming.COEF_IF_MOVE` | `1.5` | `1.75` |
| `Aiming.TIME_COEF_IF_MOVE` | `1.5` | `1.75` |
| `Shoot.AUTOMATIC_FIRE_SCATTERING_COEF` | `1.4` | `1.6` |

The normalization is follower-local and applies in both runtime configurations: core combat with the SAIN addon absent, and addon-owned combat when the addon is present. It:

- stores every normalized SAIN value in the `Sain` section of the follower-owned `BotFollowerPlayer.Proficiency` object,
- starts from the same global `FollowerProficiency.DefaultValues` object as vanilla, then resolves only the follower's SAIN section for its exact SAIN role and difficulty,
- clones the selected role/difficulty settings instead of mutating SAIN's shared preset objects,
- replaces only vision, aiming, hearing, weapon proficiency, recoil/fire-rate, strafe, and lean values with the corresponding `Default` values,
- preserves selected-preset behavior settings such as search, cover, patrol, extraction, talk, and other policy fields,
- reapplies SAIN's built-in Default global/bot/personality/location difficulty coefficients after `BotFollowerPlayer` replaces `BotCurrentSettings`,
- restores the Default profile difficulty scalar and cached hearing value,
- uses follower-filtered final patches for SAIN's global recoil multiplier and global aim-time controls,
- dismisses the immutable follower modifier and restores the prior SAIN file-settings reference when the follower is dismissed.

For a hard PMC under stock SAIN 4.5 Default data, the important normalized values include global scatter `0.75`, accuracy-speed coefficient `0.8`, precision/vision/hearing coefficients `1.0`, global recoil `0.5`, field of view `170`, semiautomatic fire-rate multiplier `1.5`, strafe speed `0.8`, and enabled faster-CQB reactions. The typed object's initializers document both vanilla and SAIN fallback numbers in one file, while SAIN's generated Default bundle hydrates the SAIN section and each follower's exact role/difficulty overrides at runtime.

### Legacy nonconforming addon patch inventory

The current addon source still gives saved squadmates a cloned `followerBigPipe` SAIN template, rebuilds SAIN difficulty state, and sets a role-based personality:

- PMC-like followers and BigPipe: `Chad`
- Knight: `GigaChad`
- BirdEye: `Normal`
- recruited non-squad followers: preserve SAIN's assigned personality

SAIN's stock 4.5 personality defaults leave shooting-related `DifficultySettings` multipliers at neutral `1.0`; their default differences are primarily behavior/search policy. Custom SAIN presets may change those multipliers.

It also currently applies strong final shooting assistance:

- disables SAIN random aim sway while a follower has a visible, shootable target,
- skips SAIN's aim-hit displacement for followers,
- reduces SAIN recoil after it is calculated:
  - automatic fire uses a tuning denominator of `7`,
  - single/semi fire uses a tuning denominator of `10`,
- reduces only SAIN's low-light **gain-sight time** penalty to 40% of its original distance from neutral,
- restores vanilla foliage fields during follower look checks.

These entries describe legacy source, not permitted addon ownership. General proficiency, aim, recoil, vision, foliage, personality, and difficulty compatibility must move to a core-owned boundary or be removed. If the alternate brain needs different tactics, express that difference through `SAINFollowerCombatLayer`, its decision calculator, or a custom SAIN action without patching general SAIN methods or rewriting shared/general settings objects.

### Legacy SAIN template overwrite details

`SainSettingsExtensions.SetConfigValues()` currently overwrites selected EFT categories after the core follower baseline is installed. It applies SAIN Aiming, Look, Mind, Scattering, Shoot, Grenade, and Boss settings. This describes legacy code only; settings/proficiency application is not a valid addon responsibility and must be core-owned if still required.

Material examples:

- SAIN can replace `MAX_AIMING_UPGRADE_BY_TIME`, `COEF_IF_MOVE`, `MAX_AIM_TIME`, first-contact delay, and hit-recovery fields.
- `SetConfigValues()` does **not** call the existing `SAINCoreSettings.Apply()` helper. Base `VisibleDistance`, `GainSightCoef`, `AccuratySpeed`, and base per-meter scattering therefore continue to come from the EFT settings object; SAIN modifies them mainly through stacked `BotSettingsInGameModif` difficulty layers.
- The legacy addon explicitly reruns the difficulty modifier stack after core replaces `bot.Settings`. This behavior must move to core follower initialization or be removed; it may not remain as addon-owned settings rewriting.

## Vision distance

### EFT path

The stable follower-local control is `BotSettingsInGameModif.VisibleDistCoef`.

The base relationship is:

```text
CurrentVisibleDistance = FileSettings.Core.VisibleDistance * cumulative VisibleDistCoef
```

Vanilla `LookSensor.CalcVisibleDistance()` then applies:

```text
clear distance = CurrentVisibleDistance
               * time-of-day curve
               * rain/fog coefficient

clear distance = clamp(clear distance, MINIMUM_VISIBLE_DIST, 9999)
visible distance = night-vision adjustment(light adjustment(clear distance))
```

The current follower baseline already contributes a `0.9` coefficient. A new user factor must multiply that baseline rather than replace it.

### SAIN path

SAIN disables the regular EFT look task for non-excluded SAIN bots and updates vision distance itself approximately every 5 seconds:

```text
weather-capped distance = clamp(
    CurrentVisibleDistance * SAIN weather coefficient,
    weather minimum distance,
    CurrentVisibleDistance)

clear distance = weather-capped distance * SAIN time-of-day coefficient
visible distance = night-vision adjustment(light adjustment(clear distance))
```

SAIN also adds an enemy-specific distance delta based on angle, movement, gear stealth, flare, and recent fire. That delta is derived from `LookSensor.VisibleDist`, so a follower-local base-distance multiplier continues to scale most SAIN outcomes naturally.

Exceptions and clamps still matter:

- known-position logic can grant an effectively very large additional distance,
- weather applies a minimum distance before the time-of-day factor,
- NVG and light logic can impose their own results,
- AI-vs-AI range limiting can restrict non-current enemies.

### Recommended control

**Vision Distance** should multiply the existing follower `VisibleDistCoef` at spawn.

It must not change:

- field of view,
- acquisition speed,
- hearing,
- enemy memory,
- the current enemy-selection policy.

## Vision speed / recognition time

### EFT path

`EnemyInfo.GetVisibilityChangeSpeedK()` calculates a recognition-speed coefficient from:

- normalized distance,
- flare,
- pose visibility,
- `RuntimeVisionEffectsK`,
- repeated contact,
- view angle,
- infected-event modifiers,
- foliage,
- weather.

`EnemyPartVision` accumulates visibility with:

```text
visibility delta = deltaTime
                 * visibilityChangeSpeedK
                 * Look.VISIBILITY_CHANGE_SPEED
```

An enemy becomes visible when the level reaches `1.0`. Ignoring changing conditions, approximate recognition time is therefore:

```text
time to visible ~= 1 / (visibilityChangeSpeedK * VISIBILITY_CHANGE_SPEED)
```

The follower-local control is `BotSettingsInGameModif.RuntimeVisionEffectK`. Higher values make recognition faster. It does not extend the sensor distance.

### SAIN path

SAIN keeps the EFT visibility accumulator but postfixes its calculated speed:

```text
final visibility speed = EFT visibility speed / SAIN gain-sight modifier
```

SAIN's modifier accounts for body-part visibility, gear, weather, time of day, movement, elevation, third-party angle, peripheral angle, pose, and prior seen/heard positions. Larger SAIN modifiers mean slower recognition.

The EFT result already contains follower-local `RuntimeVisionEffectsK`, so a user recognition factor remains effective even after SAIN divides the result. The historical addon low-light patch that subsequently changes SAIN's penalty is nonconforming; any required compatibility belongs at a core follower-only boundary.

SAIN raycasts at a nominal 30 Hz and updates the two bot look groups on alternating fixed-update passes. That creates a small scheduling floor which no coefficient can remove.

### Implemented control

**Reaction** multiplies `RuntimeVisionEffectK` through a follower-owned runtime modifier.

It must not alter `VisibleDistCoef`. This preserves a useful distinction:

- high range plus low speed: notices distant exposed targets slowly,
- low range plus high speed: reacts quickly only after targets are close enough.

## Aim speed / aim time

### EFT path

For normal firearms, `BotAimingData.CalcTimeShoot()` uses approximately:

```text
base = BOTTOM_COEF * (in cover ? COEF_FROM_COVER : 1)
curve = angle curve * distance curve * CurrentAccuratySpeed * panic coefficient
aim time = (base + curve + queued aiming delay) * moving coefficient
aim time = min(aim time, MAX_AIM_TIME)
```

`CurrentAccuratySpeed` is:

```text
FileSettings.Core.AccuratySpeed * cumulative AccuratySpeedCoef
```

Lower values produce a shorter initial aim plan. First contact can queue an additional randomized delay through `SetNextAimingDelay()`.

When `GoalEnemy` changes, `BotMemory` calls `LoseTarget()`. The next aim target therefore starts a new aim plan. There is no separate verified target-handoff timer in SPT 4.1.3; the handoff cost is primarily the new aim plan plus any first-contact delay.

`BotSettingsInGameModif.AccuratySpeedCoef` also feeds the dormant `BotTargeting` scatter-recovery path. The active SPT 4.1.3 `AimingManager` instantiates `BotAimingData` and the underbarrel controller, not `BotTargeting`, so that coupling is not currently part of normal firearm aim. It is still an avoidable cross-version coupling.

### SAIN path

SAIN prefixes `BotAimingData.CalcTimeShoot()` whenever `GetSAIN(profileId)` succeeds. This patch does not require SAIN combat layers to be active.

SAIN's replacement still uses `CurrentAccuratySpeed`, then adds or applies:

- SAIN angle and distance aim-time multipliers,
- cover, panic, and movement modifiers,
- global ADS aim-time multiplier,
- faster-CQB reaction scaling and its minimum,
- equipment/target scatter modifier,
- global minimum aim time,
- per-bot maximum aim time.

A pre-calculation coefficient can therefore be attenuated by CQB logic or erased by the min/max clamps.

### Recommended control

**Aim Speed** is an authoritative follower-only postfix on the final regular-firearm aim time. Its factor is derived equally from Precision and Reaction:

```text
aim speed factor = (Precision + Reaction) / 200

final follower aim time = clamp(
    result from EFT or SAIN / aim speed factor,
    pitFireTeam safe minimum,
    pitFireTeam safe maximum)
```

Benefits:

- works whether SAIN ran its prefix or EFT ran the original,
- makes aim completion respond equally to firearm Precision and target-acquisition Reaction,
- avoids relying on `AccuratySpeedCoef`'s broader semantic coupling,
- preserves all distance, angle, movement, stance, equipment, and environment relationships,
- gives neutral settings exact current behavior.

The first implementation should apply only to the normal firearm controller. Underbarrel/grenade aim should remain a separate capability unless explicitly designed later.

## Accuracy

Accuracy is also not one value. It is the final result of target point, aim offset, convergence, recoil, movement, injury, stance, and burst behavior.

### EFT aim offset

The regular firearm aim-offset radius starts approximately as:

```text
spread by distance = pow(weapon BaseShift + distance, SCATTERING_DIST_MODIF)
                   * CurrentScattering

CurrentScattering = Core.ScatteringPerMeter * cumulative ScatteringCoef
```

The offset is then affected by panic, hard aim, prone state, movement, and mandatory bad-shot logic.

After aim readiness, offset convergence uses:

```text
time coefficient = clamp(
    (MAX_AIM_PRECICING - elapsed * CurrentPrecicingSpeed) / MAX_AIM_PRECICING,
    MAX_AIMING_UPGRADE_BY_TIME,
    1)
```

Higher `PrecicingSpeedCoef` converges faster. Lower `MAX_AIMING_UPGRADE_BY_TIME` permits a smaller final offset.

### SAIN aim offset and recoil

While SAIN combat is active, its aim-offset patch writes:

```text
EndTargetPoint = RealTargetPoint + standard aim offset * time coefficient
```

It intentionally omits EFT's bad-shot offset and vanilla `RecoilData.RecoilOffset`. SAIN then rotates bot look direction with its separate recoil system, and can add movement-controller random sway.

For addon followers today:

- visible-target random sway is disabled,
- SAIN hit displacement is disabled,
- SAIN recoil is reduced after calculation by the follower tuning ratio.

This means an EFT-only scattering slider is effective for the standard aim offset, but it does not fully own final SAIN recoil.

### Recommended control

**Accuracy** should centrally control:

- inverse aim-offset/scattering scale,
- precision-convergence speed,
- a narrow final SAIN recoil scale through the core follower-only external-SAIN compatibility boundary.

Recommended relationships:

```text
ScatteringCoef       *= 1 / Accuracy factor
PrecicingSpeedCoef   *= Accuracy factor
SAIN final recoil    *= 1 / Accuracy factor
```

Keep the existing stable-shooting patches active at every skill level. Re-enabling random visible-target sway or hit displacement for low Accuracy risks recreating unstable aim behavior instead of producing controlled inaccuracy.

Do not map Accuracy to:

- tactics or objectives,
- vision or hearing,
- enemy memory,
- base damage,
- grenade precision,
- a large fire-rate change.

### Precision-owned body-part preference

Core sets `AIMING_TYPE = 6`, but SAIN's `BodyPartToShootPatch` replaces EFT body-part selection and uses `AimForHead` plus `AimForHeadChance`. SAIN's actual target point can also be limited by the global center-mass setting.

Precision deliberately owns one conservative target-selection value in addition to firearm execution: head preference is `10%` at Precision `0`, `33%` at `100`, and `60%` at `200`, with piecewise-linear interpolation between those points. This is a preference among valid firing solutions, not a hit guarantee. The follower target resolver first removes every body part that is not both visible and shootable; only then does it roll head versus non-head. A sole exposed head is selected regardless of probability, while no shootable body part produces no shot instead of an invented torso target.

The target resolver retains the chosen valid part for EFT's normal body-part retarget interval. It does not allocate or reroll every shot. Core direct fire consumes this resolver directly instead of `CurrentEnemyTargetPosition(false)`, because that EFT helper always returns the body position and bypasses both normal body-part selection and the follower Precision policy. When external SAIN owns firing, the main plugin also bypasses SAIN 4.5's global center-mass height clamp for registered followers because that clamp ignores SAIN's own per-bot `AimCenterMass` value and can move a valid exposed-head target down behind cover.

### Burst control is not pure accuracy

SAIN owns semi-auto intervals and full-auto burst length through `FireratMulti`, `BurstMulti`, weapon/ammo shootability, equipment difficulty, and global clamps. Changing those values affects DPS, suppression, and ammunition use.

For the first release, use recoil scaling to express controllability and leave cadence unchanged. A later **Weapon Control** setting can own burst length and follow-up timing explicitly.

## Reaction-time interpretation

For measurement, observed first-shot reaction should be decomposed as:

```text
candidate in range/sector
    -> first valid LOS sample
    -> visibility reaches 1.0
    -> GoalEnemy selected or changed
    -> aim plan begins
    -> aim ready
    -> ShootData/SAIN trigger cooldown passes
    -> first trigger
```

The important intervals are:

- **sensor latency:** candidate to first LOS sample,
- **recognition time:** first LOS sample to visible,
- **selection latency:** visible to `GoalEnemy`,
- **aim time:** goal/aim start to aim ready,
- **trigger latency:** aim ready to first trigger.

The implemented controls keep the vision envelope separate from recognition: Vision adjusts range, Reaction adjusts LOS-to-visible speed, and Precision adjusts shot execution. Precision and Reaction each contribute half of final aim speed because players perceive both target processing and weapon control in the aim-ready interval. Selection latency remains combat/enemy-selection policy, while weapon cadence remains unchanged.

Core `CombatDogFightAction` has a narrow exception because its safe close-contact helpers can call `ShootData.Shoot()` before the ordinary aim worker reports ready. Those helpers use a `0.2s / Reaction factor` gate per contact/target. The ordinary aim/shoot worker still runs and may fire independently, so the gate removes the instantaneous bypass without delaying a normally completed shot.

## Implemented user surface

### Composite surface

The `Proficiency` dialog exposes three persistent percentage values per saved teammate. Each value ranges from `0` to `200`, with `100` preserving the follower's finalized class/tactic baseline:

| Setting | 0-200 meaning | Neutral default | Runtime ownership |
|---|---|---|---|
| Vision | shorter to farther vision range | preserves class-specific distance | core runtime modifier |
| Precision | wider to tighter firearm execution plus `10%..60%` head preference | preserves class-specific scatter and precision, uses `33%` head preference, and supplies half of aim speed | core modifier, valid-part target resolver, plus final external-SAIN-compatible accuracy boundary |
| Reaction | slower to faster recognition/response | preserves class-specific recognition; supplies half of aim speed | core vision-speed modifier, final aim boundary, and core dogfight direct-fire gate |

The percentage converts directly to a multiplier:

```text
factor(value) = value / 100
```

- `0` is stored and displayed as `0%`; the runtime-safe factor is floored to `0.05x`
- `100` maps to `1.0x`
- `150` maps to `1.5x`
- `200` maps to `2.0x`

The class/tactic proficiency baseline is finalized before these factors are applied. A Marksman therefore keeps the Marksman-specific defaults described above: `150` Vision means `1.5x` the finalized Marksman distance, not `1.5x` the Rifleman or global starting values. The same ordering applies to Precision and Reaction.

### Internal granular mapping

The persistent compatibility object retains four granular fields even though the UI exposes three values:

- **Vision** owns `VisionDistance`.
- **Reaction** owns `VisionSpeed`.
- **Precision** owns `Accuracy`.
- `AimSpeed` is derived as `(Accuracy + VisionSpeed) / 2` for persistence and recorder compatibility.

The server and client independently clamp the three authoritative fields and always recalculate `AimSpeed`. Existing saved profiles naturally initialize Reaction from their former `VisionSpeed` value; neutral profiles remain `100` across all three controls.

## Implemented architecture

### 1. Persistent data

Neutral-default values flow through the teammate settings and API DTO chain:

```text
FriendlyTeammateSettings
    -> profile-options response/update
    -> follower-details response
    -> client BotDetails
    -> BossPlayers.AddFollower
    -> BotFollowerPlayer performance snapshot
```

Saved teammates own the persistent values. Recruited/picked-up followers use neutral `100%` defaults.

### 2. Central mapping

The core-owned `FollowerProficiencyModifierValues` object is responsible for:

- clamping raw percentage values to `0..200`,
- migration defaults,
- converting raw values to runtime factors,
- deriving aim speed equally from Precision and Reaction,
- exposing configured and effective values to the recorder,
- preventing tactics from reading or mutating the values,
- retaining raw `0..200` values for UI/persistence while flooring unsafe runtime factors to `0.05x` where EFT restoration or inverse calculations cannot accept zero.

Do not put mapping formulas in actions, tactics, UI, server services, or addon patches.

### 3. Core runtime application

At follower initialization, after tactic-specific proficiency is finalized:

- snapshot the persistent values,
- build one follower-local `BotSettingsInGameModif`,
- multiply the existing `0.9` vision baseline rather than replacing it,
- apply vision speed, scattering, and precision factors before the modifier is applied.

Do not mutate a `BotSettingsInGameModif` after `BotCurrentSettings.Apply()`. `Dismiss()` divides by the modifier's current field values; mutating an applied object would corrupt restoration. Treat performance as a spawn-time snapshot, or dismiss the old immutable modifier before applying a new object.

### 4. Final aim-time patch

Regular-firearm `BotAimingData.CalcTimeShoot()` has a follower-only postfix. It runs after either the EFT original or SAIN's replacement prefix, making it the authoritative compatibility boundary.

It uses a project-owned safety clamp and records both the incoming and final result.

### 5. External SAIN compatibility

Any SAIN calculation that would otherwise overwrite or bypass a follower's finalized Vision, Precision, or Reaction in either combat mode must be compensated at a core-owned follower-only boundary gated by external SAIN presence. The addon may consume the finalized values while choosing decisions/actions in its custom combat layer, but it must not alter those calculations through general SAIN patches or shared-object mutation.

### 6. UI and localization

The per-teammate Vision, Precision, and Reaction sliders live in the draggable `Proficiency` profile dialog. Aggression remains a separate `0..100` behavior control in the same dialog. All labels use the centralized localization model and embedded English fallback described in `docs/Localization.md`.

Descriptions state that Vision owns range, Precision owns shot accuracy plus conservative valid-part head preference, Reaction owns recognition speed, and Precision plus Reaction share aim speed without changing tactics or objectives.

## Implementation hazards and boundaries

### Shallow settings copy

`BotSettings.Copy()` calls `FileSettings.Copy()`, but `BotSettingsComponents.Copy()` reuses Aiming, Look, Shoot, Move, Mind, Scattering, and other category objects. Only `Core` is constructed as a new object.

Therefore a direct change such as:

```text
settings.FileSettings.Aiming.X = follower-specific value
```

can mutate a shared hard-role template and leak into other bots or followers. Existing baseline mutations already carry this risk. New user-specific controls should use follower-local runtime modifiers and final patches instead of adding more direct mutations.

### SAIN layer state is not a calculation gate

The following SAIN patches use `GetSAIN` rather than follower combat-layer ownership:

- aim time,
- rate of fire,
- body-part selection,
- vision speed,
- vision distance,
- some hit/aim effects.

Every new control must be tested with SAIN installed and the addon absent.

### Preset and environmental clamps

SAIN global minimum aim time, per-template maximum aim time, faster-CQB minimum, weather minimum distance, night-time factors, and AI range limits can flatten a pre-calculation setting. This is why final aim-time ownership and effective-value telemetry are required.

### Personality ordering

The current addon rebuilds difficulty modifiers, then forces the spawned follower personality and recalculates search/hold timing. It does not rerun the difficulty modifier stack after that personality change. Stock 4.5 personality shooting modifiers are neutral, so this is harmless with defaults, but custom presets can make effective personality stat ownership ambiguous.

The proposed final performance layer must not depend on personality ordering.

### Headshots and perceived accuracy

Hit rate, headshot rate, and time-to-kill are different measurements. SAIN's head-target selection can make a bot feel more accurate or lethal without changing spread. Record them separately and do not calibrate the Accuracy slider solely from kills.

## Recorder and validation requirements

Extend `BattleRecorder` before balancing the slider ranges.

Per follower, record:

- configured raw values,
- mapped factors,
- `CurrentVisibleDistance`,
- `RuntimeVisionEffectsK`,
- `CurrentPrecicingSpeed`,
- `CurrentScattering` and close scattering,
- raw aim-time result before the pitFireTeam factor,
- final aim-time result,
- current SAIN/addon ownership mode,
- final SAIN recoil factor when applicable.

For each acquired target, record timestamps for:

- first eligible in-range/sector observation,
- first valid LOS,
- visible transition,
- `GoalEnemy` selection/change,
- aim-plan start,
- aim ready,
- first trigger,
- first hit and hit body part.

Validation matrix:

1. no SAIN,
2. SAIN installed without addon combat,
3. SAIN plus addon,
4. clear day, heavy weather, night without NVG, night with NVG,
5. stationary and moving follower,
6. close, medium, and long range,
7. semi-auto and automatic weapons,
8. default and custom SAIN presets,
9. Balanced and Marksman tactics using otherwise identical equipment.

Acceptance criteria:

- neutral values reproduce the current effective baseline,
- low/high Vision changes range without changing LOS-to-visible speed,
- low/high Precision changes dispersion/hit rate/recoil, valid-part head preference, and half of aim speed without changing tactical decisions,
- low/high Reaction changes LOS-to-visible time, half of aim speed, and the narrow core dogfight direct-fire gate without changing the two sensor-wait settings,
- tactical decision/reason sequences remain comparable for identical encounters,
- no new action end/reselect churn appears,
- no setting leaks to non-followers or other follower profiles.

## Implementation status

1. Recorder effective-stat snapshots: implemented.
2. Persistent neutral data and migration defaults: implemented.
3. Centralized follower-local percentage mapping: implemented.
4. Vision distance core runtime modifier: implemented.
5. Combined Precision/Reaction final aim-speed postfix: implemented.
6. Precision scatter/convergence and core-owned final external-SAIN recoil compatibility: implemented independently of addon presence.
7. Reaction recognition-speed modifier and core dogfight direct-fire gate: implemented.
8. Localized four-control profile UI including Aggression: implemented.
9. Allocation-free visible-and-shootable body-part selection, Precision head preference, and follower-only SAIN center-mass bypass: implemented.
10. Conservative endpoint calibration across the runtime matrix: pending gameplay tests.
11. Granular advanced controls remain internal unless a later calibration need justifies exposing them.

The implementation keeps shooting-performance ownership separate from the combat decision architecture. Changing these values does not select different actions or tactics.
