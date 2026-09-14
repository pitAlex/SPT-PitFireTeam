using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using pitTeam.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset.Shared.Personalities.BasePersonality;
using SAIN.Preset.Shared.Personalities.BasePersonality.Categories;

namespace pitTeam.SAINAddon;

// The addon owns aggression policy; core only installs/restores the resulting
// follower-local settings through the optional native SAIN boundary.
internal sealed class SAINFollowerPersonality
{
    private static readonly float[] Anchors = { 0f, 30f, 50f, 70f, 100f };
    private static readonly EPersonality[] Personalities = {
        EPersonality.Coward, EPersonality.Rat, EPersonality.Normal, EPersonality.Chad, EPersonality.GigaChad
    };
    private static readonly SettingsPlan GeneralPlan = new SettingsPlan(typeof(PersonalityGeneralSettings));
    private static readonly SettingsPlan SearchPlan = new SettingsPlan(typeof(PersonalitySearchSettings));
    private static readonly SettingsPlan RushPlan = new SettingsPlan(typeof(PersonalityRushSettings));
    private static readonly SettingsPlan CoverPlan = new SettingsPlan(typeof(PersonalityCoverSettings));
    private object? preset;
    private PersonalitySettingsClass? lowerSettings, upperSettings, applied;
    private float aggression = float.NaN;
    private bool temporary;
    internal object? Snapshot { get; private set; }

    internal static void Resolve(float value, out int lower, out int upper, out float fraction)
    {
        value = Normalize(value);
        lower = 0;
        while (lower < Anchors.Length - 1 && value >= Anchors[lower + 1]) lower++;
        upper = value == Anchors[lower] ? lower : lower + 1;
        fraction = lower == upper ? 0f : (value - Anchors[lower]) / (Anchors[upper] - Anchors[lower]);
    }

    private static float Normalize(float value) => float.IsNaN(value) || float.IsInfinity(value)
        ? 50f : Math.Max(0f, Math.Min(100f, value));

    internal static PersonalitySettingsClass Blend(PersonalitySettingsClass lower, PersonalitySettingsClass upper, float fraction)
    {
        // Only combat willingness follows the anchors. Fresh native defaults keep speech,
        // assignment and mechanical difficulty independent of aggression. In particular,
        // Coward must never enable begging, and aggressive anchors must not enable taunts.
        var nearest = fraction < 0.5f ? lower : upper;
        var result = new PersonalitySettingsClass { Name = nearest.Name, Description = nearest.Description };
        result.Behavior.General = (PersonalityGeneralSettings)GeneralPlan.Blend(lower.Behavior.General, upper.Behavior.General, fraction);
        result.Behavior.Search = (PersonalitySearchSettings)SearchPlan.Blend(lower.Behavior.Search, upper.Behavior.Search, fraction);
        result.Behavior.Rush = (PersonalityRushSettings)RushPlan.Blend(lower.Behavior.Rush, upper.Behavior.Rush, fraction);
        result.Behavior.Cover = (PersonalityCoverSettings)CoverPlan.Blend(lower.Behavior.Cover, upper.Behavior.Cover, fraction);
        result.Difficulty.AggressionCoef = BlendNumber(lower.Difficulty.AggressionCoef, upper.Difficulty.AggressionCoef, fraction, "AggressionCoef");
        result.InitList();
        result.Behavior.InitList();
        return result;
    }

    internal bool Apply(BotComponent bot)
    {
        var follower = BossPlayers.Instance?.GetFollower(bot.BotOwner);
        var selectedPreset = SAINPlugin.LoadedPreset;
        var profiles = selectedPreset?.PersonalityManager?.PersonalityDictionary;
        if (follower == null || profiles == null) return false;
        // This getter also advances the existing post-combat override-clear lifecycle.
        float value = Normalize(follower.EffectiveCombatAggression);
        bool isTemporary = follower.IsTemporaryCombatAggressionOverrideActive;
        Resolve(value, out int lower, out int upper, out float fraction);
        EPersonality identity = Personalities[fraction < 0.5f ? lower : upper];
        if (!profiles.TryGetValue(Personalities[lower], out var low) || low == null ||
            !profiles.TryGetValue(Personalities[upper], out var high) || high == null) return false;

        bool refresh = applied == null || value != aggression || !ReferenceEquals(preset, selectedPreset) ||
            !ReferenceEquals(lowerSettings, low) || !ReferenceEquals(upperSettings, high) ||
            !ReferenceEquals(bot.Info.PersonalitySettingsClass, applied) || bot.Info.Personality != identity;
        if (refresh)
        {
            // Build and validate the complete copy before publishing any native state.
            var settings = Blend(low, high, fraction);
            if (!SainManPersonality.Apply(bot.BotOwner, bot.Info, selectedPreset, identity, settings)) return false;
            applied = settings;
            preset = selectedPreset; lowerSettings = low; upperSettings = high;
        }
        if (refresh || temporary != isTemporary)
        {
            aggression = value; temporary = isTemporary;
            Snapshot = new {
                aggression, source = temporary ? "temporaryOverride" : "savedAggression",
                personality = identity.ToString(), lower = Personalities[lower].ToString(),
                upper = Personalities[upper].ToString(), fraction,
                reason = lower == upper ? "anchor" : "interpolated"
            };
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainPersonality", Snapshot);
            pitTeam.Modules.Logger.LogInfo($"[SAIN] SainMan aggression={value:0.##} personality={identity} blend={Personalities[lower]}/{Personalities[upper]}:{fraction:0.###} temporary={temporary} follower={bot.BotOwner.ProfileId}");
        }
        return true;
    }

    private static float BlendNumber(float low, float high, float fraction, string field)
    {
        if (float.IsNaN(low) || float.IsInfinity(low) || float.IsNaN(high) || float.IsInfinity(high))
            throw new InvalidOperationException($"Non-finite SAIN setting: {field}");
        // Preserve the exact native value at each anchor.
        return fraction <= 0f ? low : fraction >= 1f ? high : (float)((double)low + ((double)high - low) * fraction);
    }

    // Cache serialized fields only within the four combat categories. Other personality
    // categories cannot become aggression inputs just because SAIN adds a new field.
    private sealed class SettingsPlan
    {
        private readonly Type type;
        private readonly FieldInfo[] fields;
        internal SettingsPlan(Type type)
        {
            this.type = type;
            var members = new List<FieldInfo>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.IsDefined(typeof(DataMemberAttribute), true)) members.Add(field);
            fields = members.ToArray();
            if (fields.Length == 0 || type.GetConstructor(Type.EmptyTypes) == null)
                throw new NotSupportedException($"Unsupported SAIN combat settings type: {type.FullName}");
            foreach (var field in fields)
                if (field.FieldType != typeof(float) && field.FieldType != typeof(bool) && !field.FieldType.IsEnum)
                    throw new NotSupportedException($"Unsupported SAIN combat setting: {type.Name}.{field.Name}");
        }

        internal object Blend(object low, object high, float fraction)
        {
            if (low == null || high == null) throw new InvalidOperationException($"Missing SAIN combat category: {type.Name}");
            object result = Activator.CreateInstance(type);
            foreach (var field in fields)
            {
                object a = field.GetValue(low), b = field.GetValue(high);
                object value = field.FieldType == typeof(float)
                    ? BlendNumber((float)a, (float)b, fraction, $"{type.Name}.{field.Name}")
                    : fraction < 0.5f ? a : b;
                field.SetValue(result, value);
            }
            return result;
        }
    }
}
