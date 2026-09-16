using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Preset;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset.Shared.Personalities.BasePersonality;
using SAIN.SAINComponent.Classes.Info;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Talk;

namespace pitTeam.SAINAddon
{
    // The addon supplies policy and a private settings copy. This adapter only
    // installs/restores native state; shared presets and proficiency stay untouched.
    internal static class SainManPersonality
    {
        private sealed class State
        {
            public SAINBotInfoClass Info;
            public EPersonality OriginalPersonality;
            public PersonalitySettingsClass OriginalSettings;
            public PersonalitySettingsClass AppliedSettings;
            public float OriginalAggression;
            public SAINPresetClass Preset;
        }
        private static readonly ConditionalWeakTable<BotOwner, State> States = new ConditionalWeakTable<BotOwner, State>();
        private static PropertyInfo _personality, _settings, _aggression, _forgetTime;
        private static MethodInfo _refreshTalk;
        private static Type _searchAction;
        private static FieldInfo _searchSprint, _searchSprintTimer;
        private static bool _ready;

        public static void Initialize()
        {
            if (_ready) return;
            // SAIN exposes these properties for reads, but their setters are private.
            _personality = AccessTools.Property(typeof(SAINBotInfoClass), nameof(SAINBotInfoClass.Personality));
            _settings = AccessTools.Property(typeof(SAINBotInfoClass), nameof(SAINBotInfoClass.PersonalitySettingsClass));
            _forgetTime = AccessTools.Property(typeof(SAINBotInfoClass), nameof(SAINBotInfoClass.ForgetEnemyTime));
            _aggression = AccessTools.Property(typeof(BotDifficultyClass), nameof(BotDifficultyClass.AggressionModifier));
            _refreshTalk = AccessTools.Method(typeof(EnemyTalk), "UpdatePresetSettings", new[] { typeof(SAINPresetClass) });
            _searchAction = Type.GetType("SAIN.Layers.Combat.Solo.SearchAction, SAIN", true); // Native action is internal.
            _searchSprint = AccessTools.Field(_searchAction, "_sprintEnabled");
            _searchSprintTimer = AccessTools.Field(_searchAction, "_sprintTimer");
            if (_personality?.GetSetMethod(true) == null || _settings?.GetSetMethod(true) == null ||
                _refreshTalk == null || _searchSprint?.FieldType != typeof(bool) || _searchSprintTimer?.FieldType != typeof(float) ||
                _forgetTime?.GetSetMethod(true) == null || _aggression?.GetSetMethod(true) == null)
                throw new MissingMemberException("Unsupported SAIN personality lifecycle");
            _ready = true;
        }

        public static bool Apply(BotOwner owner, SAINBotInfoClass info, SAINPresetClass preset, EPersonality personality, PersonalitySettingsClass settings)
        {
            if (!_ready || !SainAddonBridge.IsSainManSelected(owner) || owner.IsDead || info == null || preset == null || settings == null) return false;
            if (info.Bot?.Talk?.EnemyTalk == null) return false;
            if (!States.TryGetValue(owner, out State state) || !ReferenceEquals(state.Info, info))
            {
                // A replacement native component must not strand our copy on the old one.
                if (state != null) Restore(owner);
                state = new State { Info = info, OriginalPersonality = info.Personality,
                    OriginalSettings = info.PersonalitySettingsClass,
                    OriginalAggression = info.Difficulty.AggressionModifier, Preset = preset };
                States.Add(owner, state);
            }
            if (Equals(info.Personality, personality) && ReferenceEquals(settings, info.PersonalitySettingsClass) && ReferenceEquals(state.Preset, preset)) return true;
            EPersonality previousPersonality = info.Personality;
            PersonalitySettingsClass previousSettings = info.PersonalitySettingsClass;
            float previousAggression = info.Difficulty.AggressionModifier;
            try
            {
                info.SetPersonality(personality);
                if (!Equals(info.Personality, personality)) return false;
                _settings.GetSetMethod(true).Invoke(info, new[] { settings });
                // Existing core interception applies mechanical proficiency exactly once,
                // while consuming the supplied personality's tactical AggressionCoef.
                info.Difficulty.UpdateSettings(preset);
                RefreshTimers(owner, info);
                RefreshBehaviorCaches(info, preset);
                state.AppliedSettings = settings;
                state.Preset = preset;
            }
            catch
            {
                info.SetPersonality(previousPersonality);
                _settings.GetSetMethod(true).Invoke(info, new[] { previousSettings });
                _aggression.GetSetMethod(true).Invoke(info.Difficulty, new object[] { previousAggression });
                RefreshTimers(owner, info);
                RefreshBehaviorCaches(info, preset);
                throw;
            }
            return true;
        }

        public static void Restore(BotOwner owner)
        {
            if (owner == null || !States.TryGetValue(owner, out State state)) return;
            States.Remove(owner);
            // Do not overwrite a newer preset/native owner's settings.
            if (owner.IsDead || !ReferenceEquals(state.AppliedSettings, state.Info.PersonalitySettingsClass)) return;
            state.Info.SetPersonality(state.OriginalPersonality);
            if (ReferenceEquals(state.AppliedSettings, state.Info.PersonalitySettingsClass))
            {
                // A removed original profile cannot leave our private copy installed.
                _personality.GetSetMethod(true).Invoke(state.Info, new object[] { state.OriginalPersonality });
                _settings.GetSetMethod(true).Invoke(state.Info, new[] { state.OriginalSettings });
            }
            // Do not re-enrol a dismissed bot in proficiency normalization after core restored it.
            _aggression.GetSetMethod(true).Invoke(state.Info.Difficulty, new object[] { state.OriginalAggression });
            RefreshTimers(owner, state.Info);
            RefreshBehaviorCaches(state.Info, state.Preset);
        }

        private static void RefreshBehaviorCaches(SAINBotInfoClass info, SAINPresetClass preset)
        {
            var enemyTalk = info.Bot?.Talk?.EnemyTalk;
            if (enemyTalk != null) _refreshTalk.Invoke(enemyTalk, new[] { preset });
            var action = info.Bot?.CurrentAction;
            if (action != null && _searchAction.IsInstanceOfType(action))
            {
                // Native SearchAction never clears an old positive sprint roll when its
                // new chance is zero. Reevaluate only that roll, retaining the search/path.
                _searchSprint.SetValue(action, false);
                _searchSprintTimer.SetValue(action, 0f);
            }
            // Freeze eligibility and rush permission read settings on each native decision.
            // Existing freeze deadlines, search pauses, cover and target commitments survive.
        }

        private static void RefreshTimers(BotOwner owner, SAINBotInfoClass info)
        {
            // SAIN also writes EFT enemy-memory duration while calculating search timing.
            var mind = owner.Settings.FileSettings.Mind;
            float remember = mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC;
            float sainRemember = info.ForgetEnemyTime;
            try { info.CalcTimeBeforeSearch(); info.CalcHoldGroundDelay(); }
            finally
            {
                mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = remember;
                _forgetTime.GetSetMethod(true).Invoke(info, new object[] { sainRemember });
            }
        }
    }
}
