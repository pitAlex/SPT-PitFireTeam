using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;

namespace pitTeam.Modules
{
    // Core owns this external-SAIN setup boundary. Only SainMan combat requests it.
    // Native personality objects are selected, never edited. Follower proficiency stays core-owned.
    public static class SainManPersonality
    {
        private sealed class State
        {
            public object Info;
            public object OriginalPersonality;
            public object AppliedSettings;
            public float OriginalAggression;
        }
        private static readonly ConditionalWeakTable<BotOwner, State> States = new ConditionalWeakTable<BotOwner, State>();
        private static PropertyInfo _personality, _settings, _difficulty, _aggression, _forgetTime;
        private static MethodInfo _setPersonality, _updateDifficulty, _searchTime, _holdTime;
        private static object _chad;
        private static bool _ready;

        public static void Initialize()
        {
            if (_ready) return;
            Type info = Type.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass, SAIN", true);
            _personality = AccessTools.Property(info, "Personality") ?? throw new MissingMemberException("SAIN personality");
            _settings = AccessTools.Property(info, "PersonalitySettingsClass");
            _difficulty = AccessTools.Property(info, "Difficulty") ?? throw new MissingMemberException("SAIN personality difficulty");
            _forgetTime = AccessTools.Property(info, "ForgetEnemyTime");
            _chad = Enum.Parse(_personality.PropertyType, "Chad");
            _setPersonality = AccessTools.Method(info, "SetPersonality", new[] { _personality.PropertyType });
            _updateDifficulty = AccessTools.Method(_difficulty.PropertyType, "UpdateSettings");
            _aggression = AccessTools.Property(_difficulty.PropertyType, "AggressionModifier");
            _searchTime = AccessTools.Method(info, "CalcTimeBeforeSearch", Type.EmptyTypes);
            _holdTime = AccessTools.Method(info, "CalcHoldGroundDelay", Type.EmptyTypes);
            if (_settings == null || _setPersonality == null || _updateDifficulty == null ||
                _forgetTime?.GetSetMethod(true) == null || _aggression?.GetSetMethod(true) == null || _searchTime == null || _holdTime == null)
                throw new MissingMemberException("Unsupported SAIN personality lifecycle");
            _ready = true;
        }

        public static bool ApplyChad(BotOwner owner, object info, object preset)
        {
            if (!_ready || !SainAddonBridge.IsSainManSelected(owner) || owner.IsDead || info == null || preset == null) return false;
            if (!States.TryGetValue(owner, out State state) || !ReferenceEquals(state.Info, info))
            {
                States.Remove(owner);
                state = new State { Info = info, OriginalPersonality = _personality.GetValue(info),
                    OriginalAggression = (float)_aggression.GetValue(_difficulty.GetValue(info)) };
                States.Add(owner, state);
            }
            if (Equals(_personality.GetValue(info), _chad) && ReferenceEquals(state.AppliedSettings, _settings.GetValue(info))) return true;
            _setPersonality.Invoke(info, new[] { _chad });
            if (!Equals(_personality.GetValue(info), _chad)) return false;
            // UpdateSettings is intercepted by core's existing proficiency normalization.
            _updateDifficulty.Invoke(_difficulty.GetValue(info), new[] { preset });
            RefreshTimers(owner, info);
            state.AppliedSettings = _settings.GetValue(info);
            Logger.LogInfo($"[SAIN] SainMan personality=Chad follower={owner.ProfileId}");
            return true;
        }

        public static void Restore(BotOwner owner)
        {
            if (owner == null || !States.TryGetValue(owner, out State state)) return;
            States.Remove(owner);
            if (owner.IsDead) return;
            _setPersonality.Invoke(state.Info, new[] { state.OriginalPersonality });
            // Do not re-enrol a dismissed bot in proficiency normalization after core restored it.
            _aggression.GetSetMethod(true).Invoke(_difficulty.GetValue(state.Info), new object[] { state.OriginalAggression });
            RefreshTimers(owner, state.Info);
        }

        private static void RefreshTimers(BotOwner owner, object info)
        {
            // SAIN recalculates its private search timer but also writes EFT enemy-memory duration.
            // Preserve the core follower memory contract while refreshing native behavior timers.
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