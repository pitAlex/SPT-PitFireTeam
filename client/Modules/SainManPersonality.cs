using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;

namespace pitTeam.Modules
{
    // The addon supplies policy and a private settings copy. This core adapter only
    // installs/restores native state; shared presets and proficiency stay untouched.
    public static class SainManPersonality
    {
        private sealed class State
        {
            public object Info;
            public object OriginalPersonality;
            public object OriginalSettings;
            public object AppliedSettings;
            public float OriginalAggression;
            public object Preset;
        }
        private static readonly ConditionalWeakTable<BotOwner, State> States = new ConditionalWeakTable<BotOwner, State>();
        private static PropertyInfo _personality, _settings, _difficulty, _aggression, _forgetTime;
        private static MethodInfo _setPersonality, _updateDifficulty, _searchTime, _holdTime;
        private static PropertyInfo _bot, _talk, _enemyTalk, _action;
        private static MethodInfo _refreshTalk;
        private static Type _searchAction;
        private static FieldInfo _searchSprint, _searchSprintTimer;
        private static bool _ready;

        public static void Initialize()
        {
            if (_ready) return;
            Type info = Type.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass, SAIN", true);
            _personality = AccessTools.Property(info, "Personality") ?? throw new MissingMemberException("SAIN personality");
            _settings = AccessTools.Property(info, "PersonalitySettingsClass");
            _difficulty = AccessTools.Property(info, "Difficulty") ?? throw new MissingMemberException("SAIN personality difficulty");
            _forgetTime = AccessTools.Property(info, "ForgetEnemyTime");
            _setPersonality = AccessTools.Method(info, "SetPersonality", new[] { _personality.PropertyType });
            _updateDifficulty = AccessTools.Method(_difficulty.PropertyType, "UpdateSettings");
            _aggression = AccessTools.Property(_difficulty.PropertyType, "AggressionModifier");
            _searchTime = AccessTools.Method(info, "CalcTimeBeforeSearch", Type.EmptyTypes);
            _holdTime = AccessTools.Method(info, "CalcHoldGroundDelay", Type.EmptyTypes);
            _bot = AccessTools.Property(info, "Bot");
            _talk = _bot == null ? null : AccessTools.Property(_bot.PropertyType, "Talk");
            _enemyTalk = _talk == null ? null : AccessTools.Property(_talk.PropertyType, "EnemyTalk");
            _refreshTalk = _enemyTalk == null ? null : AccessTools.Method(_enemyTalk.PropertyType, "UpdatePresetSettings");
            _action = _bot == null ? null : AccessTools.Property(_bot.PropertyType, "CurrentAction");
            _searchAction = Type.GetType("SAIN.Layers.Combat.Solo.SearchAction, SAIN", true);
            _searchSprint = AccessTools.Field(_searchAction, "_sprintEnabled");
            _searchSprintTimer = AccessTools.Field(_searchAction, "_sprintTimer");
            if (_personality.GetSetMethod(true) == null || _settings?.GetSetMethod(true) == null || _setPersonality == null || _updateDifficulty == null ||
                _refreshTalk == null || _action == null || _searchSprint?.FieldType != typeof(bool) || _searchSprintTimer?.FieldType != typeof(float) ||
                _forgetTime?.GetSetMethod(true) == null || _aggression?.GetSetMethod(true) == null || _searchTime == null || _holdTime == null)
                throw new MissingMemberException("Unsupported SAIN personality lifecycle");
            _ready = true;
        }

        public static bool Apply(BotOwner owner, object info, object preset, object personality, object settings)
        {
            if (!_ready || !SainAddonBridge.IsSainManSelected(owner) || owner.IsDead || info == null || preset == null ||
                !_personality.PropertyType.IsInstanceOfType(personality) || !_settings.PropertyType.IsInstanceOfType(settings)) return false;
            object bot = _bot.GetValue(info);
            object talk = bot == null ? null : _talk.GetValue(bot);
            if (talk == null || _enemyTalk.GetValue(talk) == null) return false;
            if (!States.TryGetValue(owner, out State state) || !ReferenceEquals(state.Info, info))
            {
                // A replacement native component must not strand our copy on the old one.
                if (state != null) Restore(owner);
                state = new State { Info = info, OriginalPersonality = _personality.GetValue(info),
                    OriginalSettings = _settings.GetValue(info),
                    OriginalAggression = (float)_aggression.GetValue(_difficulty.GetValue(info)), Preset = preset };
                States.Add(owner, state);
            }
            if (Equals(_personality.GetValue(info), personality) && ReferenceEquals(settings, _settings.GetValue(info)) && ReferenceEquals(state.Preset, preset)) return true;
            object previousPersonality = _personality.GetValue(info), previousSettings = _settings.GetValue(info);
            float previousAggression = (float)_aggression.GetValue(_difficulty.GetValue(info));
            try
            {
                _setPersonality.Invoke(info, new[] { personality });
                if (!Equals(_personality.GetValue(info), personality)) return false;
                _settings.GetSetMethod(true).Invoke(info, new[] { settings });
                // Existing core interception applies mechanical proficiency exactly once,
                // while consuming the supplied personality's tactical AggressionCoef.
                _updateDifficulty.Invoke(_difficulty.GetValue(info), new[] { preset });
                RefreshTimers(owner, info);
                RefreshBehaviorCaches(info, preset);
                state.AppliedSettings = settings;
                state.Preset = preset;
            }
            catch
            {
                _setPersonality.Invoke(info, new[] { previousPersonality });
                _settings.GetSetMethod(true).Invoke(info, new[] { previousSettings });
                _aggression.GetSetMethod(true).Invoke(_difficulty.GetValue(info), new object[] { previousAggression });
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
            if (owner.IsDead || !ReferenceEquals(state.AppliedSettings, _settings.GetValue(state.Info))) return;
            _setPersonality.Invoke(state.Info, new[] { state.OriginalPersonality });
            if (ReferenceEquals(state.AppliedSettings, _settings.GetValue(state.Info)))
            {
                // A removed original profile cannot leave our private copy installed.
                _personality.GetSetMethod(true).Invoke(state.Info, new[] { state.OriginalPersonality });
                _settings.GetSetMethod(true).Invoke(state.Info, new[] { state.OriginalSettings });
            }
            // Do not re-enrol a dismissed bot in proficiency normalization after core restored it.
            _aggression.GetSetMethod(true).Invoke(_difficulty.GetValue(state.Info), new object[] { state.OriginalAggression });
            RefreshTimers(owner, state.Info);
            RefreshBehaviorCaches(state.Info, state.Preset);
        }

        private static void RefreshBehaviorCaches(object info, object preset)
        {
            object bot = _bot.GetValue(info);
            object talk = bot == null ? null : _talk.GetValue(bot);
            object enemyTalk = talk == null ? null : _enemyTalk.GetValue(talk);
            if (enemyTalk != null) _refreshTalk.Invoke(enemyTalk, new[] { preset });
            object action = bot == null ? null : _action.GetValue(bot);
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

        private static void RefreshTimers(BotOwner owner, object info)
        {
            // SAIN also writes EFT enemy-memory duration while calculating search timing.
            var mind = owner.Settings.FileSettings.Mind;
            float remember = mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC;
            object sainRemember = _forgetTime.GetValue(info);
            try { _searchTime.Invoke(info, null); _holdTime.Invoke(info, null); }
            finally
            {
                mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = remember;
                _forgetTime.GetSetMethod(true).Invoke(info, new[] { sainRemember });
            }
        }
    }
}
