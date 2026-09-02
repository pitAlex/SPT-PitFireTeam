using EFT;
using Newtonsoft.Json;
using pitTeam.Components;
using System;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// One global starting object. Every BotFollowerPlayer clones this object at construction,
    /// before either vanilla or optional SAIN adapters apply their values.
    /// </summary>
    public static class FollowerProficiency
    {
        public static FollowerProficiencyValues DefaultValues { get; } = new();

        public static bool TryGetValues(BotOwner bot, out FollowerProficiencyValues? values)
        {
            BotFollowerPlayer? follower = bot != null
                ? BossPlayers.GetFollowerByProfileId(bot.ProfileId)
                : null;
            values = follower?.Proficiency;
            return values != null;
        }
    }

    /// <summary>
    /// Universal follower-owned proficiency state. The tactic selects numerical proficiency
    /// values here; combat decision policy remains in the tactic implementation.
    /// </summary>
    public sealed class FollowerProficiencyValues
    {
        public FollowerVanillaProficiencyValues Vanilla { get; private set; } = new();
        public FollowerSainProficiencyOverrides Sain { get; private set; } = new();
        public FollowerProficiencyModifierValues Modifiers { get; private set; } = new();
        public FollowerCombatTactic CombatTactic { get; private set; } = FollowerCombatTactic.Balanced;

        public FollowerProficiencyValues Clone()
        {
            return new FollowerProficiencyValues
            {
                Vanilla = Vanilla.Clone(),
                Sain = Sain.Clone(),
                Modifiers = Modifiers.Clone(),
                CombatTactic = CombatTactic,
            };
        }

        public void ReplaceModifiers(FollowerProficiencyModifierValues? values)
        {
            Modifiers = (values ?? new FollowerProficiencyModifierValues()).Clone();
        }

        /// <summary>
        /// Captures the selected vanilla role template into this follower-owned object, then
        /// applies the small proficiency differences owned by the selected PMC combat tactic.
        /// Rifleman/Protector keep the captured follower baseline unchanged.
        /// </summary>
        public void FinalizeForTactic(BotSettings template, FollowerCombatTactic tactic)
        {
            if (template == null)
            {
                return;
            }

            CombatTactic = tactic;
            Vanilla.CaptureTemplateValues(template);
            ApplyVanillaTacticValues();
            ApplySainTacticValues();
        }

        internal void ReplaceSainOverrides(FollowerSainProficiencyOverrides values)
        {
            Sain = values ?? new FollowerSainProficiencyOverrides();
            ApplySainTacticValues();
        }

        private void ApplyVanillaTacticValues()
        {
            if (CombatTactic != FollowerCombatTactic.Marksman)
            {
                return;
            }

            // A marksman is only moderately stronger at clear-LOS distance. Bush vision stays
            // close to the ordinary follower baseline and never adopts boss-style penetration.
            Vanilla.Core.VisibleDistance = 210f;
            Vanilla.Core.ScatteringPerMeter = 0.043f;
            Vanilla.Vision.VISIBILITY_CHANGE_SPEED = 1.5f;
            Vanilla.Vision.MAX_VISION_GRASS_METERS = 1.1f;
            Vanilla.Aiming.SCATTERING_DIST_MODIF = 0.64f;

            // Preserve today's supported aim floor: 0.06 * 0.25 == 0.05 * 0.30. The weakness is
            // mobile/unsupported close combat, especially rapid turns and automatic fire.
            Vanilla.Aiming.BOTTOM_COEF = 0.06f;
            Vanilla.Aiming.COEF_FROM_COVER = 0.25f;
            Vanilla.Aiming.COEF_IF_MOVE = 1.15f;
            Vanilla.Aiming.TIME_COEF_IF_MOVE = 1.25f;
            Vanilla.Shooting.AUTOMATIC_FIRE_SCATTERING_COEF = 1.7f;
            Vanilla.Shooting.WAIT_NEXT_SINGLE_SHOT = 0.13f;
        }

        private void ApplySainTacticValues()
        {
            if (CombatTactic != FollowerCombatTactic.Marksman)
            {
                return;
            }

            Sain.Core.VisibleDistance = 275f;
            Sain.Core.ScatteringPerMeter = 0.07f;
            Sain.Aiming.DistanceAimTimeMultiplier = 0.9f;
            Sain.Aiming.AngleAimTimeMultiplier = 1.15f;
            Sain.Aiming.FasterCQBReactions = true;
            Sain.Aiming.FasterCQBReactionsDistance = 15f;
            Sain.Aiming.FasterCQBReactionsMinimum = 0.45f;
            Sain.Aiming.MAX_AIMING_UPGRADE_BY_TIME = 0.2f;
            Sain.Aiming.COEF_IF_MOVE = 1.75f;
            Sain.Aiming.TIME_COEF_IF_MOVE = 1.75f;
            Sain.Shoot.AUTOMATIC_FIRE_SCATTERING_COEF = 1.6f;
        }
    }

    /// <summary>
    /// Per-follower percentage controls. Raw values remain suitable for persistence and UI;
    /// runtime-safe factors protect EFT modifier dismissal and inverse calculations at 0%.
    /// </summary>
    public sealed class FollowerProficiencyModifierValues
    {
        public const float MinimumPercent = 0f;
        public const float MaximumPercent = 200f;
        public const float DefaultPercent = 100f;
        public const float MinimumRuntimeFactor = 0.05f;

        public float VisionDistance { get; set; } = DefaultPercent;
        public float VisionSpeed { get; set; } = DefaultPercent;
        public float AimSpeed { get; set; } = DefaultPercent;
        public float Accuracy { get; set; } = DefaultPercent;

        [JsonIgnore]
        public float VisionDistanceFactor => ToFactor(VisionDistance);

        [JsonIgnore]
        public float VisionSpeedFactor => ToFactor(VisionSpeed);

        [JsonIgnore]
        public float AimSpeedFactor => ToFactor(GetAimSpeedPercent());

        [JsonIgnore]
        public float AccuracyFactor => ToFactor(Accuracy);

        [JsonIgnore]
        public float SafeVisionDistanceFactor => ToRuntimeFactor(VisionDistance);

        [JsonIgnore]
        public float SafeVisionSpeedFactor => ToRuntimeFactor(VisionSpeed);

        [JsonIgnore]
        public float SafeAimSpeedFactor => ToRuntimeFactor(GetAimSpeedPercent());

        [JsonIgnore]
        public float SafeAccuracyFactor => ToRuntimeFactor(Accuracy);

        public FollowerProficiencyModifierValues Clone()
        {
            FollowerProficiencyModifierValues clone = new()
            {
                VisionDistance = NormalizePercent(VisionDistance),
                VisionSpeed = NormalizePercent(VisionSpeed),
                Accuracy = NormalizePercent(Accuracy),
            };
            clone.RefreshDerivedAimSpeed();
            return clone;
        }

        public float GetVisionPercent()
        {
            return NormalizePercent(VisionDistance);
        }

        public void SetVisionPercent(float value)
        {
            VisionDistance = NormalizePercent(value);
        }

        public float GetPrecisionPercent()
        {
            return NormalizePercent(Accuracy);
        }

        public void SetPrecisionPercent(float value)
        {
            Accuracy = NormalizePercent(value);
            RefreshDerivedAimSpeed();
        }

        public float GetReactionPercent()
        {
            return NormalizePercent(VisionSpeed);
        }

        public void SetReactionPercent(float value)
        {
            VisionSpeed = NormalizePercent(value);
            RefreshDerivedAimSpeed();
        }

        public float GetAimSpeedPercent()
        {
            return GetCompositePercent(Accuracy, VisionSpeed);
        }

        public float ScaleReactionDelay(float baselineSeconds)
        {
            return Mathf.Max(0f, baselineSeconds) / SafeVisionSpeedFactor;
        }

        public static float NormalizePercent(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? DefaultPercent
                : Mathf.Clamp(value, MinimumPercent, MaximumPercent);
        }

        private static float ToFactor(float percent)
        {
            return NormalizePercent(percent) / DefaultPercent;
        }

        private static float GetCompositePercent(float first, float second)
        {
            return (NormalizePercent(first) + NormalizePercent(second)) * 0.5f;
        }

        private void RefreshDerivedAimSpeed()
        {
            AimSpeed = GetAimSpeedPercent();
        }

        private static float ToRuntimeFactor(float percent)
        {
            return Mathf.Max(MinimumRuntimeFactor, ToFactor(percent));
        }
    }

    /// <summary>
    /// Values pitFireTeam writes into vanilla EFT proficiency calculations.
    /// </summary>
    public sealed class FollowerVanillaProficiencyValues
    {
        public BotDifficulty TemplateDifficulty { get; set; } = BotDifficulty.hard;
        public FollowerVanillaRuntimeDifficultyValues RuntimeDifficulty { get; private set; } = new();
        public FollowerVanillaCoreValues Core { get; private set; } = new();
        public FollowerVanillaAimingValues Aiming { get; private set; } = new();
        public FollowerVanillaVisionValues Vision { get; private set; } = new();
        public FollowerVanillaHearingValues Hearing { get; private set; } = new();
        public FollowerVanillaShootingValues Shooting { get; private set; } = new();
        public FollowerVanillaBossValues Boss { get; private set; } = new();
        public FollowerVanillaBirdEyeValues BirdEye { get; private set; } = new();

        public FollowerVanillaProficiencyValues Clone()
        {
            return new FollowerVanillaProficiencyValues
            {
                TemplateDifficulty = TemplateDifficulty,
                RuntimeDifficulty = RuntimeDifficulty.Clone(),
                Core = Core.Clone(),
                Aiming = Aiming.Clone(),
                Vision = Vision.Clone(),
                Hearing = Hearing.Clone(),
                Shooting = Shooting.Clone(),
                Boss = Boss.Clone(),
                BirdEye = BirdEye.Clone(),
            };
        }

        internal void CaptureTemplateValues(BotSettings template)
        {
            Core.AccuratySpeed = template.FileSettings.Core.AccuratySpeed;
            Core.VisibleDistance = template.FileSettings.Core.VisibleDistance;
            Core.VisibleAngle = template.FileSettings.Core.VisibleAngle;
            Core.ScatteringPerMeter = template.FileSettings.Core.ScatteringPerMeter;
            Core.ScatteringClosePerMeter = template.FileSettings.Core.ScatteringClosePerMeter;

            Aiming.TIME_COEF_IF_MOVE = template.FileSettings.Aiming.TIME_COEF_IF_MOVE;
            Aiming.SCATTERING_DIST_MODIF = template.FileSettings.Aiming.SCATTERING_DIST_MODIF;
            Aiming.SCATTERING_DIST_MODIF_CLOSE = template.FileSettings.Aiming.SCATTERING_DIST_MODIF_CLOSE;
            Vision.VISIBILITY_CHANGE_SPEED = template.FileSettings.Look.VISIBILITY_CHANGE_SPEED;
            Shooting.AUTOMATIC_FIRE_SCATTERING_COEF =
                template.FileSettings.Shoot.AUTOMATIC_FIRE_SCATTERING_COEF;
        }
    }

    public sealed class FollowerVanillaCoreValues
    {
        public float AccuratySpeed { get; set; } = 0.15f;
        public float VisibleDistance { get; set; } = 185f;
        public float VisibleAngle { get; set; } = 200f;
        public float ScatteringPerMeter { get; set; } = 0.045f;
        public float ScatteringClosePerMeter { get; set; } = 0.12f;

        public FollowerVanillaCoreValues Clone() =>
            (FollowerVanillaCoreValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaRuntimeDifficultyValues
    {
        public float PrecicingSpeedCoef { get; set; } = 1f;
        public float AccuratySpeedCoef { get; set; } = 1f;
        public float LayChanceDangerCoef { get; set; } = 1f;
        public float VisibleDistCoef { get; set; } = 0.9f;
        public float RuntimeVisionEffectK { get; set; } = 1f;
        public float ScatteringCoef { get; set; } = 1f;
        public float HearingDistCoef { get; set; } = 1f;
        public float PriorityScatteringCoef { get; set; } = 1f;
        public float TriggerDownDelay { get; set; } = 1f;

        public FollowerVanillaRuntimeDifficultyValues Clone() =>
            (FollowerVanillaRuntimeDifficultyValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaAimingValues
    {
        public float COEF_IF_MOVE { get; set; } = 1f;
        public float TIME_COEF_IF_MOVE { get; set; } = 1.1f;
        public float BOTTOM_COEF { get; set; } = 0.05f;
        public float COEF_FROM_COVER { get; set; } = 0.3f;
        public float PANIC_COEF { get; set; } = 1f;
        public float MAX_AIMING_UPGRADE_BY_TIME { get; set; } = 0.15f;
        public float SHPERE_FRIENDY_FIRE_SIZE { get; set; } = 0.5f;
        public float DIST_TO_SHOOT_TO_CENTER { get; set; } = 0f;
        public int AIMING_TYPE { get; set; } = 6;
        public float ANY_PART_SHOOT_TIME { get; set; } = 15f;
        public float ANYTIME_LIGHT_WHEN_AIM_100 { get; set; } = 50f;
        public int BAD_SHOOTS_MAX { get; set; } = 2;
        public int BAD_SHOOTS_MIN { get; set; } = 1;
        public float FIRST_CONTACT_ADD_CHANCE_100 { get; set; } = 20f;
        public float BASE_HIT_AFFECTION_DELAY_SEC { get; set; } = 0.2f;
        public float BASE_HIT_AFFECTION_MAX_ANG { get; set; } = 10f;
        public float BASE_HIT_AFFECTION_MIN_ANG { get; set; } = 2f;
        public float DAMAGE_PANIC_TIME { get; set; } = 10f;
        public float DAMAGE_TO_DISCARD_AIM_0_100 { get; set; } = 30f;
        public float SCATTERING_DIST_MODIF { get; set; } = 0.67f;
        public float SCATTERING_DIST_MODIF_CLOSE { get; set; } = 0.6f;

        public FollowerVanillaAimingValues Clone() =>
            (FollowerVanillaAimingValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaVisionValues
    {
        public float MINIMUM_VISIBLE_DIST { get; set; } = 15f;
        public bool CAN_USE_LIGHT { get; set; } = true;
        public float NIGHT_VISION_ON { get; set; } = 100f;
        public float NIGHT_VISION_OFF { get; set; } = 110f;
        public float NIGHT_VISION_DIST { get; set; } = 160f;
        public float VISIBLE_ANG_NIGHTVISION { get; set; } = 120f;
        public float LOOK_THROUGH_PERIOD_BY_HIT { get; set; } = 5f;
        public float LightOnVisionDistance { get; set; } = 40f;
        public float LOOK_LAST_POSENEMY_IF_NO_DANGER_SEC { get; set; } = 25f;
        public float VISIBLE_ANG_LIGHT { get; set; } = 45f;
        public float VISIBLE_DISNACE_WITH_LIGHT { get; set; } = 65f;
        public float GOAL_TO_FULL_DISSAPEAR { get; set; } = 1.1f;
        public float GOAL_TO_FULL_DISSAPEAR_GREEN { get; set; } = 2f;
        public float VISIBILITY_CHANGE_SPEED { get; set; } = 1.2f;
        public float MAX_VISION_GRASS_METERS { get; set; } = 1f;
        public float NO_GREEN_DIST { get; set; } = 4f;
        public float NO_GRASS_DIST { get; set; } = 5f;
        public bool CHECK_HEAD_ANY_DIST { get; set; } = true;
        public bool MIDDLE_DIST_CAN_SHOOT_HEAD { get; set; } = true;
        public bool LOOK_THROUGH_GRASS { get; set; } = false;
        public bool SHOOT_FROM_EYES { get; set; } = true;

        public FollowerVanillaVisionValues Clone() =>
            (FollowerVanillaVisionValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaHearingValues
    {
        public float DISPERSION_COEF { get; set; } = 1.6f;
        public float CLOSE_DIST { get; set; } = 7f;
        public float FAR_DIST { get; set; } = 35f;

        public FollowerVanillaHearingValues Clone() =>
            (FollowerVanillaHearingValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaShootingValues
    {
        public float AUTOMATIC_FIRE_SCATTERING_COEF { get; set; } = 1.5f;
        public float WAIT_NEXT_SINGLE_SHOT { get; set; } = 0.1f;
        public float WAIT_NEXT_SINGLE_SHOT_LONG_MAX { get; set; } = 1.8f;
        public float NEXT_SINGLE_SHOT_PAUSE { get; set; } = 3f;

        public FollowerVanillaShootingValues Clone() =>
            (FollowerVanillaShootingValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaBossValues
    {
        public bool LOOK_THROUGH_GRASS { get; set; } = false;

        public FollowerVanillaBossValues Clone() =>
            (FollowerVanillaBossValues)MemberwiseClone();
    }

    public sealed class FollowerVanillaBirdEyeValues
    {
        public float VisibleDistCoef { get; set; } = 0.8f;
        public float SOUND_TO_GET_SPOTTED { get; set; } = 10f;
        public float SPOTTED_COVERS_RADIUS { get; set; } = 12f;
        public float LOW_DIST_TO_CHANGE_WEAPON { get; set; } = 30f;
        public float FAR_DIST_TO_CHANGE_WEAPON { get; set; } = 68f;
        public float DIST_TO_CHANGE_TO_MAIN { get; set; } = 60f;
        public float SCATTERING_DIST_MODIF { get; set; } = 0.2f;
        public float HARD_AIM { get; set; } = 0.9f;
        public float MAX_VISION_GRASS_METERS { get; set; } = 1.5f;

        public FollowerVanillaBirdEyeValues Clone() =>
            (FollowerVanillaBirdEyeValues)MemberwiseClone();
    }

    /// <summary>
    /// SAIN-specific overrides stored inside the universal follower object.
    /// Only the external SAIN adapter reads and applies these fields.
    /// </summary>
    public sealed class FollowerSainProficiencyOverrides
    {
        public FollowerSainGlobalProficiencyValues Global { get; private set; } = new();
        public FollowerSainRuntimeDifficultyValues RuntimeDifficulty { get; private set; } = new();
        public FollowerSainDifficultyValues Difficulty { get; private set; } = new();
        public FollowerSainCoreValues Core { get; private set; } = new();
        public FollowerSainAimingValues Aiming { get; private set; } = new();
        public FollowerSainShootValues Shoot { get; private set; } = new();
        public FollowerSainMindValues Mind { get; private set; } = new();
        public FollowerSainMoveValues Move { get; private set; } = new();

        public float ProfileDifficultyModifier { get; set; } = 1.5f;

        public float ProfileDifficultyModifierSqrt =>
            (float)Math.Round(Math.Sqrt(ProfileDifficultyModifier) * 100f) / 100f;

        public FollowerSainProficiencyOverrides Clone()
        {
            return new FollowerSainProficiencyOverrides
            {
                Global = Global.Clone(),
                RuntimeDifficulty = RuntimeDifficulty.Clone(),
                Difficulty = Difficulty.Clone(),
                Core = Core.Clone(),
                Aiming = Aiming.Clone(),
                Shoot = Shoot.Clone(),
                Mind = Mind.Clone(),
                Move = Move.Clone(),
                ProfileDifficultyModifier = ProfileDifficultyModifier,
            };
        }
    }

    /// <summary>
    /// SAIN global proficiency controls from its built-in Default preset.
    /// </summary>
    public sealed class FollowerSainGlobalProficiencyValues
    {
        public float BOT_RECOIL_COEF { get; set; } = 0.5f;
        public bool FasterCQBReactionsGlobal { get; set; } = true;
        public float AimDownSightsAimTimeMultiplier { get; set; } = 0.7f;
        public float MinAimTime { get; set; } = 0f;

        public FollowerSainGlobalProficiencyValues Clone() =>
            (FollowerSainGlobalProficiencyValues)MemberwiseClone();
    }

    /// <summary>
    /// Final Default difficulty stack applied to EFT's follower-local runtime settings.
    /// These values already include SAIN global, bot, personality, and location multipliers.
    /// </summary>
    public sealed class FollowerSainRuntimeDifficultyValues
    {
        public float AccuratySpeedCoef { get; set; } = 0.8f;
        public float PrecicingSpeedCoef { get; set; } = 1f;
        public float VisibleDistCoef { get; set; } = 1f;
        public float ScatteringCoef { get; set; } = 0.75f;
        public float RuntimeVisionEffectK { get; set; } = 1f;
        public float HearingDistCoef { get; set; } = 1f;

        public FollowerSainRuntimeDifficultyValues Clone() =>
            (FollowerSainRuntimeDifficultyValues)MemberwiseClone();
    }

    public sealed class FollowerSainDifficultyValues
    {
        public float VisibleDistCoef { get; set; } = 1f;
        public float GainSightCoef { get; set; } = 1f;
        public float ScatteringCoef { get; set; } = 1f;
        public float HearingDistanceCoef { get; set; } = 1f;
        public float PRECISION_SPEED_COEF { get; set; } = 1f;
        public float ACCURACY_SPEED_COEF { get; set; } = 1f;

        public FollowerSainDifficultyValues Clone() =>
            (FollowerSainDifficultyValues)MemberwiseClone();
    }

    public sealed class FollowerSainCoreValues
    {
        public float VisibleAngle { get; set; } = 170f;
        public float VisibleDistance { get; set; } = 250f;
        public float GainSightCoef { get; set; } = 0.2f;
        public float AccuratySpeed { get; set; } = 0.3f;
        public float ScatteringPerMeter { get; set; } = 0.08f;
        public float ScatteringClosePerMeter { get; set; } = 0.12f;
        public float HearingDistanceMulti { get; set; } = 1f;

        public FollowerSainCoreValues Clone() =>
            (FollowerSainCoreValues)MemberwiseClone();
    }

    public sealed class FollowerSainAimingValues
    {
        public bool AimCenterMass { get; set; } = true;
        public bool AimForHead { get; set; } = false;
        public float AimForHeadChance { get; set; } = 33f;
        public float DistanceAimTimeMultiplier { get; set; } = 1f;
        public float AngleAimTimeMultiplier { get; set; } = 1f;
        public bool FasterCQBReactions { get; set; } = true;
        public float FasterCQBReactionsDistance { get; set; } = 30f;
        public float FasterCQBReactionsMinimum { get; set; } = 0.33f;
        public float MAX_AIMING_UPGRADE_BY_TIME { get; set; } = 0.25f;
        public float DIST_TO_SHOOT_NO_OFFSET { get; set; } = 3f;
        public float COEF_IF_MOVE { get; set; } = 1.5f;
        public float TIME_COEF_IF_MOVE { get; set; } = 1.5f;
        public float MAX_AIM_TIME { get; set; } = 2f;
        public int AIMING_TYPE { get; set; } = 1;
        public float DAMAGE_TO_DISCARD_AIM_0_100 { get; set; } = 100f;
        public float BASE_HIT_AFFECTION_DELAY_SEC { get; set; } = 0.65f;
        public float MIN_TIME_DISCARD_AIM_SEC { get; set; } = 0.5f;
        public float MAX_TIME_DISCARD_AIM_SEC { get; set; } = 1.5f;
        public float ANY_PART_SHOOT_TIME { get; set; } = 2f;
        public float FIRST_CONTACT_ADD_SEC { get; set; } = 0.2f;
        public float FIRST_CONTACT_ADD_CHANCE_100 { get; set; } = 100f;
        public float OFFSET_RECAL_ANYWAY_TIME { get; set; } = 30f;

        public FollowerSainAimingValues Clone() =>
            (FollowerSainAimingValues)MemberwiseClone();
    }

    public sealed class FollowerSainShootValues
    {
        public float RecoilMultiplier { get; set; } = 1f;
        public float BurstMulti { get; set; } = 1.5f;
        public float FireratMulti { get; set; } = 1.5f;
        public float MaxPointFireDistance { get; set; } = 150f;
        public float AUTOMATIC_FIRE_SCATTERING_COEF { get; set; } = 1.4f;

        public FollowerSainShootValues Clone() =>
            (FollowerSainShootValues)MemberwiseClone();
    }

    public sealed class FollowerSainMindValues
    {
        public float WeaponProficiency { get; set; } = 0.5f;

        public FollowerSainMindValues Clone() =>
            (FollowerSainMindValues)MemberwiseClone();
    }

    public sealed class FollowerSainMoveValues
    {
        public float STRAFE_SPEED { get; set; } = 0.8f;
        public bool LEAN_TOGGLE { get; set; } = true;
        public bool LEAN_INCOVER_TOGGLE { get; set; } = true;

        public FollowerSainMoveValues Clone() =>
            (FollowerSainMoveValues)MemberwiseClone();
    }
}
