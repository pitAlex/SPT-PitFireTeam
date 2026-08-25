# Friendly AI Performance Settings Investigation

**Status:** source investigation plus implemented SAIN Default proficiency baseline

**Target:** SPT 4.1.3, SAIN 4.5.0, pitFireTeam `0.10.0`

**Investigated:** 2026-08-24

This document identifies the settings and runtime calculations that determine follower vision and firearm performance. It also defines a safe boundary for future user controls without changing combat tactics or reintroducing action churn.

The SAIN Default baseline and follower-local values model are implemented. Per-follower UI controls are not implemented yet.

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
3. If Vision Speed and Aim Speed are exposed separately, a fifth generic Reaction slider would overlap both. The source-accurate granular surface is therefore:
   - **Vision Distance**
   - **Vision Speed**
   - **Aim Speed**
   - **Accuracy**
4. If a simpler surface is preferred, the planned `Reaction` value from `Combat-Tactics.md` should be treated as a composite of Vision Speed and Aim Speed, not as another independent runtime statistic. It must not change vision range, hearing, memory, tactics, or decision cadence.
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
| SAIN plus addon | pitFireTeam SAIN-addon combat layer | SAIN calculations plus pitFireTeam follower-specific addon patches |

This is the central compatibility constraint. A control that works only by changing the pre-SAIN EFT file settings can be clamped, overwritten, or replaced later.

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

### SAIN addon baseline

Saved squadmates receive a cloned `followerBigPipe` SAIN template. Its proficiency fields pass through the same built-in Default normalization before the addon rebuilds SAIN difficulty state and sets a role-based personality:

- PMC-like followers and BigPipe: `Chad`
- Knight: `GigaChad`
- BirdEye: `Normal`
- recruited non-squad followers: preserve SAIN's assigned personality

SAIN's stock 4.5 personality defaults leave shooting-related `DifficultySettings` multipliers at neutral `1.0`; their default differences are primarily behavior/search policy. Custom SAIN presets may change those multipliers.

The addon also applies strong final shooting assistance:

- disables SAIN random aim sway while a follower has a visible, shootable target,
- skips SAIN's aim-hit displacement for followers,
- reduces SAIN recoil after it is calculated:
  - automatic fire uses a tuning denominator of `7`,
  - single/semi fire uses a tuning denominator of `10`,
- reduces only SAIN's low-light **gain-sight time** penalty to 40% of its original distance from neutral,
- restores vanilla foliage fields during follower look checks.

The low-light patch does not remove SAIN's time-of-day distance reduction, weather distance reduction, or weather gain-sight penalty.

### SAIN template overwrite details

`SainSettingsExtensions.SetConfigValues()` overwrites selected EFT categories after the core follower baseline is installed. It applies SAIN Aiming, Look, Mind, Scattering, Shoot, Grenade, and Boss settings. The addon now supplies a follower-local clone whose proficiency fields have already been normalized to Default, so this overwrite no longer reintroduces the selected preset's easier/harder proficiency values.

Material examples:

- SAIN can replace `MAX_AIMING_UPGRADE_BY_TIME`, `COEF_IF_MOVE`, `MAX_AIM_TIME`, first-contact delay, and hit-recovery fields.
- `SetConfigValues()` does **not** call the existing `SAINCoreSettings.Apply()` helper. Base `VisibleDistance`, `GainSightCoef`, `AccuratySpeed`, and base per-meter scattering therefore continue to come from the EFT settings object; SAIN modifies them mainly through stacked `BotSettingsInGameModif` difficulty layers.
- The SAIN addon explicitly reruns the difficulty modifier stack after core replaces `bot.Settings`. That is necessary because runtime modifiers belong to the old `BotCurrentSettings` object otherwise.

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

The EFT result already contains follower-local `RuntimeVisionEffectsK`, so a user recognition factor remains effective even after SAIN divides the result. The addon low-light patch subsequently reduces only the time-of-day component of SAIN's penalty.

SAIN raycasts at a nominal 30 Hz and updates the two bot look groups on alternating fixed-update passes. That creates a small scheduling floor which no coefficient can remove.

### Recommended control

**Vision Speed** should multiply `RuntimeVisionEffectK` through a follower-owned runtime modifier.

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

**Aim Speed** should be an authoritative follower-only postfix on the final regular-firearm aim time:

```text
final follower aim time = clamp(
    result from EFT or SAIN / user AimSpeed factor,
    pitFireTeam safe minimum,
    pitFireTeam safe maximum)
```

Benefits:

- works whether SAIN ran its prefix or EFT ran the original,
- remains independent of vision speed,
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
- a narrow final SAIN recoil scale through the addon bridge.

Recommended relationships:

```text
ScatteringCoef       *= 1 / Accuracy factor
PrecicingSpeedCoef   *= Accuracy factor
SAIN final recoil    *= 1 / Accuracy factor
```

Keep the existing stable-shooting patches active at every skill level. Re-enabling random visible-target sway or hit displacement for low Accuracy risks recreating unstable aim behavior instead of producing controlled inaccuracy.

Do not initially map Accuracy to:

- tactics or objectives,
- target selection,
- vision or hearing,
- enemy memory,
- base damage,
- grenade precision,
- a large fire-rate change.

### Body-part preference is not accuracy

Core sets `AIMING_TYPE = 6`, but SAIN's `BodyPartToShootPatch` replaces EFT body-part selection and uses `AimForHead` plus `AimForHeadChance`. SAIN's actual target point can also be limited by the global center-mass setting.

Changing head preference with Accuracy changes lethality and hit location, not just precision. Keep current body-part behavior at neutral in the first release. If users need it, expose a separate advanced **Aim Target** option after accuracy calibration.

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

Only recognition time and aim time should be user-adjustable in the first granular implementation. Selection latency belongs to combat/enemy-selection stability, and trigger latency is heavily tied to weapon cadence and existing action state.

## Recommended user surface

### Granular surface

Expose four persistent 0-100 values per saved teammate:

| Setting | 0-100 meaning | Neutral default | Runtime ownership |
|---|---|---|---|
| Vision Distance | shorter to farther range | preserves current `0.9` follower baseline | core runtime modifier |
| Vision Speed | slower to faster recognition | preserves current gain-sight behavior | core runtime modifier |
| Aim Speed | slower to faster aim readiness | preserves current EFT/SAIN final result | core final Harmony postfix |
| Accuracy | wider to tighter shot execution | preserves current scatter/precision/recoil | core modifier plus addon recoil bridge |

The default value should map to a factor of `1.0`, so existing profiles and migrated profiles behave exactly as they do before the feature.

Use a centered exponential mapping rather than a linear zero-to-max multiplier. One candidate is:

```text
factor(value, span) = span ^ ((value - 50) / 50)
```

For `span = 1.5`:

- `0` maps to about `0.67x`,
- `50` maps to `1.0x`,
- `100` maps to `1.5x`.

This is an initial conservative calibration range, not a final balance claim. Validate it with recorder data before expanding the endpoints.

### Simple surface

If only two user-facing values are desired:

- **Reaction** applies the same centered skill factor to Vision Speed and Aim Speed, but not Vision Distance.
- **Accuracy** applies the accuracy mapping above.

The storage model may still keep the granular values internally so the UI can be expanded without another migration.

## Recommended implementation architecture

### 1. Persistent data

Add neutral-default values to the teammate settings and API DTO chain:

```text
FriendlyTeammateSettings
    -> profile-options response/update
    -> follower-details response
    -> client BotDetails
    -> BossPlayers.AddFollower
    -> BotFollowerPlayer performance snapshot
```

Saved teammates own the persistent values. Recruited/picked-up followers use neutral defaults or a future global recruited-follower default.

### 2. Central mapping

Create one core-owned value object, for example `FollowerPerformanceProfile`, responsible for:

- clamping raw 0-100 values,
- migration defaults,
- converting raw values to runtime factors,
- exposing configured and effective values to the recorder,
- preventing tactics from reading or mutating the values.

Do not put mapping formulas in actions, tactics, UI, server services, or addon patches.

### 3. Core runtime application

At follower initialization:

- snapshot the persistent values,
- build one follower-local `BotSettingsInGameModif`,
- multiply the existing `0.9` vision baseline rather than replacing it,
- apply vision speed, scattering, and precision factors before the modifier is applied.

Do not mutate a `BotSettingsInGameModif` after `BotCurrentSettings.Apply()`. `Dismiss()` divides by the modifier's current field values; mutating an applied object would corrupt restoration. Treat performance as a spawn-time snapshot, or dismiss the old immutable modifier before applying a new object.

### 4. Final aim-time patch

Patch regular-firearm `BotAimingData.CalcTimeShoot()` with a follower-only postfix. A postfix runs after either the EFT original or SAIN's replacement prefix, making it the authoritative compatibility boundary.

Use a project-owned safety clamp and log both the incoming and final result in debug/recorder builds.

### 5. SAIN addon bridge

Extend the existing narrow `SainAddonBridge` contract so the addon can query the follower's effective Accuracy factor or receive it during the follower lifecycle event.

Use that factor only at the addon-owned final recoil patch initially. Keep SAIN template cloning, personality assignment, and combat logic independent.

### 6. UI and localization

Place per-teammate sliders beside the existing tactic and aggression profile controls. All names, descriptions, status text, validation, and migration notices must use the centralized localization model and embedded English fallback described in `docs/Localization.md`.

Suggested descriptions should state what each control does **not** change, especially that Vision Speed does not increase range and Accuracy does not change tactics.

## Hazards to resolve before implementation

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
- low/high Vision Distance changes range without materially changing recognition at a fixed in-range distance,
- low/high Vision Speed changes LOS-to-visible time without changing range,
- low/high Aim Speed changes aim-start-to-ready time without changing tactical decisions,
- low/high Accuracy changes dispersion/hit rate/recoil without changing tactical decisions,
- tactical decision/reason sequences remain comparable for identical encounters,
- no new action end/reselect churn appears,
- no setting leaks to non-followers or other follower profiles.

## Recommended delivery sequence

1. Add recorder stages and effective-stat snapshots.
2. Add persistent neutral data and migration.
3. Add centralized mapping with no non-neutral UI exposure yet.
4. Implement Vision Distance and Vision Speed through the follower-local modifier.
5. Implement final Aim Speed postfix for regular firearms.
6. Implement Accuracy scatter/precision and the narrow SAIN recoil bridge.
7. Add localized per-profile UI.
8. Calibrate conservative endpoints across the runtime matrix.
9. Consider a simple composite Reaction UI only after granular behavior is proven.

This sequence proves execution ownership before exposing balance ranges and keeps the shooting-performance work separate from the recently stabilized combat decision architecture.
