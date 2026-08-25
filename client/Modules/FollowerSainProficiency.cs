using EFT;
using HarmonyLib;
using pitTeam.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Keeps SAIN's proficiency calculations follower-local and anchored to SAIN's built-in
    /// Default preset. The selected preset still owns ordinary bots and non-proficiency policy.
    /// </summary>
    public static class FollowerSainProficiency
    {
        private sealed class FollowerState
        {
            public BotOwner Bot = null!;
            public BotFollowerPlayer? Follower;
            public object Info = null!;
            public object Difficulty = null!;
            public FollowerProficiencyValues Values = null!;
            public WildSpawnType TemplateRole;
            public object? OriginalFileSettings;
            public object? NormalizedFileSettings;
            public BotSettingsInGameModif? AppliedModifier;
            public float OriginalProfileDifficulty;
            public float OriginalProfileDifficultySqrt;
            public float OriginalHearingModifier;
            public float OriginalAggressionModifier;
            public bool Logged;
        }

        private static readonly Dictionary<string, FollowerState> States = new(StringComparer.Ordinal);
        private static readonly object DefaultBundleLock = new();
        private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
            "MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static Type? _sainEnableType;
        private static Type? _presetSyncType;
        private static Type? _locationSettingsExtensionsType;
        private static MethodInfo? _getSainByProfile;
        private static MethodInfo? _serverDefaultsMethod;
        private static MethodInfo? _currentLocationMethod;
        private static object? _defaultBundle;
        private static object? _loadedPreset;
        private static bool _patchesApplied;
        private static bool _globalDefaultInitialized;
        private static bool _missingDefaultLogged;

        [ThreadStatic]
        private static Stack<FollowerSainProficiencyOverrides>? _activeAimValues;

        public static void ApplyPatches(Harmony harmony)
        {
            if (_patchesApplied || harmony == null || !pitFireTeam.IsSAINInstalled)
            {
                return;
            }

            _patchesApplied = true;
            try
            {
                Type? difficultyType = Type.GetType("SAIN.SAINComponent.Classes.BotDifficultyClass, SAIN");
                Type? recoilType = Type.GetType("SAIN.SAINComponent.Classes.WeaponFunction.Recoil, SAIN");
                Type? aimTimePatchType = Type.GetType("SAIN.Patches.Shoot.Aim.AimTimePatch, SAIN");

                PatchMethod(
                    harmony,
                    difficultyType != null ? AccessTools.Method(difficultyType, "UpdateSettings") : null,
                    prefixName: nameof(UseDefaultDifficultyForFollower));

                PatchMethod(
                    harmony,
                    recoilType != null ? AccessTools.PropertyGetter(recoilType, "RecoilMultiplier") : null,
                    postfixName: nameof(UseDefaultFollowerRecoil));

                MethodInfo? calculateAim = aimTimePatchType != null
                    ? AccessTools.Method(aimTimePatchType, "CalculateAim")
                    : null;
                PatchMethod(
                    harmony,
                    calculateAim,
                    prefixName: nameof(BeginDefaultFollowerAim),
                    finalizerName: nameof(EndDefaultFollowerAim));

                PatchMethod(
                    harmony,
                    aimTimePatchType != null ? AccessTools.Method(aimTimePatchType, "CalcFasterCQB") : null,
                    prefixName: nameof(UseDefaultFollowerFasterCqb));
                PatchMethod(
                    harmony,
                    aimTimePatchType != null ? AccessTools.Method(aimTimePatchType, "CalcADSModifier") : null,
                    prefixName: nameof(UseDefaultFollowerAdsAimTime));
                PatchMethod(
                    harmony,
                    aimTimePatchType != null ? AccessTools.Method(aimTimePatchType, "ClampAimTime") : null,
                    prefixName: nameof(UseDefaultFollowerAimClamp));

                Logger.LogInfo("[SAIN] Follower proficiency normalization patches applied.");
            }
            catch (Exception ex)
            {
                Logger.LogError("[SAIN] Failed to apply follower proficiency normalization patches.");
                Logger.LogError(ex);
            }
        }

        public static void ApplyToFollower(BotOwner bot, BotFollowerPlayer? follower = null)
        {
            if (!pitFireTeam.IsSAINInstalled || bot == null || bot.IsDead)
            {
                return;
            }

            try
            {
                object? sainBot = GetSainBot(bot);
                object? info = GetMemberValue(sainBot, "Info");
                object? difficulty = GetMemberValue(info, "Difficulty");
                if (sainBot == null || info == null || difficulty == null || !TryGetDefaultBundle(out object? bundle))
                {
                    return;
                }

                string key = bot.ProfileId;
                if (string.IsNullOrEmpty(key))
                {
                    return;
                }

                if (!States.TryGetValue(key, out FollowerState? state))
                {
                    BotFollowerPlayer? followerOwner = follower ?? BossPlayers.GetFollowerByProfileId(key);
                    object? profile = GetMemberValue(info, "Profile");
                    object? originalFileSettings = GetMemberValue(info, "FileSettings");
                    state = new FollowerState
                    {
                        Bot = bot,
                        Follower = followerOwner,
                        Info = info,
                        Difficulty = difficulty,
                        Values = followerOwner?.Proficiency ?? FollowerProficiency.DefaultValues.Clone(),
                        TemplateRole = bot.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault,
                        OriginalFileSettings = originalFileSettings,
                        OriginalProfileDifficulty = GetFloat(profile, "DifficultyModifier", 1f),
                        OriginalProfileDifficultySqrt = GetFloat(profile, "DifficultyModifierSqrt", 1f),
                        OriginalHearingModifier = GetFloat(difficulty, "HearingDistanceModifier", 1f),
                        OriginalAggressionModifier = GetFloat(difficulty, "AggressionModifier", 1f),
                    };
                    if (!TryResolveFollowerValues(state, bundle))
                    {
                        return;
                    }
                    States[key] = state;
                }
                else if (follower != null)
                {
                    state.Follower = follower;
                    if (!ReferenceEquals(state.Values, follower.Proficiency))
                    {
                        follower.Proficiency.ReplaceSainOverrides(state.Values.Sain);
                        state.Values = follower.Proficiency;
                    }
                }

                object? preset = GetLoadedPreset();
                if (!TryApplyDefaultDifficulty(state, preset))
                {
                    RestoreFollower(bot);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SAIN] Failed to normalize follower proficiency for {bot.Profile?.Nickname ?? bot.name}.");
                Logger.LogError(ex);
            }
        }

        public static void RestoreFollower(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) || !States.TryGetValue(bot.ProfileId, out FollowerState? state))
            {
                return;
            }

            try
            {
                DismissAppliedModifier(state);
                SetMemberValue(state.Info, "_fileSettings", state.OriginalFileSettings);

                object? profile = GetMemberValue(state.Info, "Profile");
                SetMemberValue(profile, "<DifficultyModifier>k__BackingField", state.OriginalProfileDifficulty);
                SetMemberValue(profile, "<DifficultyModifierSqrt>k__BackingField", state.OriginalProfileDifficultySqrt);
                SetMemberValue(state.Difficulty, "<HearingDistanceModifier>k__BackingField", state.OriginalHearingModifier);
                SetMemberValue(state.Difficulty, "<AggressionModifier>k__BackingField", state.OriginalAggressionModifier);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SAIN] Failed to restore SAIN proficiency state for {bot.Profile?.Nickname ?? bot.name}.");
                Logger.LogError(ex);
            }
            finally
            {
                States.Remove(bot.ProfileId);
            }
        }

        public static void SetTemplateRole(BotOwner bot, WildSpawnType role)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (!States.ContainsKey(bot.ProfileId))
            {
                ApplyToFollower(bot);
            }

            if (States.TryGetValue(bot.ProfileId, out FollowerState? state))
            {
                state.TemplateRole = role;
                if (TryGetDefaultBundle(out object? bundle))
                {
                    TryResolveFollowerValues(state, bundle);
                }
            }
        }

        /// <summary>
        /// Creates a follower-local settings object that preserves the selected preset's policy
        /// while replacing its aim, vision, hearing, weapon-control, and combat-movement values
        /// with the exact values from SAIN's server-generated Default preset.
        /// </summary>
        public static object? CreateNormalizedFileSettings(BotOwner bot, object? selectedSettings)
        {
            if (selectedSettings == null || !TryGetActiveState(bot, out FollowerState? state))
            {
                return selectedSettings;
            }

            FollowerSainProficiencyOverrides sain = state.Values.Sain;
            object clone = MemberwiseCloneMethod.Invoke(selectedSettings, null);
            ApplyCategoryValues(clone, selectedSettings, "Difficulty", sain.Difficulty);
            ApplyCategoryValues(clone, selectedSettings, "Core", sain.Core);
            ApplyCategoryValues(clone, selectedSettings, "Aiming", sain.Aiming);
            ApplyCategoryValues(clone, selectedSettings, "Shoot", sain.Shoot);
            ApplyCategoryValues(clone, selectedSettings, "Mind", sain.Mind);
            ApplyCategoryValues(clone, selectedSettings, "Move", sain.Move);
            return clone;
        }

        private static void PatchMethod(
            Harmony harmony,
            MethodInfo? target,
            string? prefixName = null,
            string? postfixName = null,
            string? finalizerName = null)
        {
            if (target == null)
            {
                Logger.LogError("[SAIN] A SAIN 4.5 follower proficiency patch target was not found.");
                return;
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            HarmonyMethod? prefix = prefixName != null
                ? new HarmonyMethod(typeof(FollowerSainProficiency).GetMethod(prefixName, flags))
                : null;
            HarmonyMethod? postfix = postfixName != null
                ? new HarmonyMethod(typeof(FollowerSainProficiency).GetMethod(postfixName, flags))
                : null;
            HarmonyMethod? finalizer = finalizerName != null
                ? new HarmonyMethod(typeof(FollowerSainProficiency).GetMethod(finalizerName, flags))
                : null;
            PatchProcessor processor = harmony.CreateProcessor(target);
            if (prefix != null)
            {
                processor.AddPrefix(prefix);
            }
            if (postfix != null)
            {
                processor.AddPostfix(postfix);
            }
            if (finalizer != null)
            {
                processor.AddFinalizer(finalizer);
            }
            processor.Patch();
        }

        [HarmonyPrefix]
        private static bool UseDefaultDifficultyForFollower(object __instance, object[] __args)
        {
            try
            {
                BotOwner? bot = GetMemberValue(__instance, "BotOwner") as BotOwner;
                if (bot == null)
                {
                    return true;
                }

                if (!States.TryGetValue(bot.ProfileId, out FollowerState? state) && BossPlayers.IsFollower(bot))
                {
                    ApplyToFollower(bot);
                    States.TryGetValue(bot.ProfileId, out state);
                }

                if (state == null)
                {
                    return true;
                }

                object? preset = __args != null && __args.Length > 0 ? __args[0] : GetLoadedPreset();
                return !TryApplyDefaultDifficulty(state, preset);
            }
            catch (Exception ex)
            {
                Logger.LogError("[SAIN] Follower Default difficulty interception failed; SAIN will use its original path.");
                Logger.LogError(ex);
                return true;
            }
        }

        [HarmonyPostfix]
        private static void UseDefaultFollowerRecoil(object __instance, ref float __result)
        {
            try
            {
                BotOwner? bot = GetMemberValue(__instance, "BotOwner") as BotOwner;
                if (!TryGetActiveState(bot, out FollowerState? state))
                {
                    return;
                }

                FollowerSainProficiencyOverrides sain = state.Values.Sain;
                __result = Mathf.Round(
                    sain.Shoot.RecoilMultiplier * sain.Global.BOT_RECOIL_COEF * 100f) / 100f;
            }
            catch
            {
                // Preserve SAIN's result if the optional integration changes shape.
            }
        }

        [HarmonyPrefix]
        private static void BeginDefaultFollowerAim(object[] __args, out bool __state)
        {
            __state = false;
            try
            {
                object? sainBot = __args != null && __args.Length > 0 ? __args[0] : null;
                BotOwner? bot = GetMemberValue(sainBot, "BotOwner") as BotOwner;
                if (!TryGetActiveState(bot, out FollowerState? state))
                {
                    return;
                }

                (_activeAimValues ??= new Stack<FollowerSainProficiencyOverrides>()).Push(state.Values.Sain);
                __state = true;
            }
            catch
            {
            }
        }

        [HarmonyFinalizer]
        private static Exception? EndDefaultFollowerAim(Exception? __exception, bool __state)
        {
            if (__state && _activeAimValues?.Count > 0)
            {
                _activeAimValues.Pop();
            }
            return __exception;
        }

        [HarmonyPrefix]
        private static bool UseDefaultFollowerFasterCqb(object[] __args, ref float __result)
        {
            if (!TryGetActiveAimValues(out FollowerSainProficiencyOverrides? values))
            {
                return true;
            }

            try
            {
                float distance = Convert.ToSingle(__args[0]);
                float aimTime = Convert.ToSingle(__args[1]);
                if (!values.Global.FasterCQBReactionsGlobal ||
                    !values.Aiming.FasterCQBReactions)
                {
                    __result = aimTime;
                    return false;
                }

                float maxDistance = values.Aiming.FasterCQBReactionsDistance;
                if (distance > maxDistance || maxDistance <= 0f)
                {
                    __result = aimTime;
                    return false;
                }

                float minimum = values.Aiming.FasterCQBReactionsMinimum;
                __result = Mathf.Clamp(aimTime * (distance / maxDistance), minimum, aimTime);
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool UseDefaultFollowerAdsAimTime(object[] __args, ref float __result)
        {
            if (!TryGetActiveAimValues(out FollowerSainProficiencyOverrides? values))
            {
                return true;
            }

            try
            {
                bool aiming = Convert.ToBoolean(__args[0]);
                float aimTime = Convert.ToSingle(__args[1]);
                float multiplier = values.Global.AimDownSightsAimTimeMultiplier;
                __result = aiming ? aimTime * multiplier : aimTime;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool UseDefaultFollowerAimClamp(object[] __args, ref float __result)
        {
            if (!TryGetActiveAimValues(out FollowerSainProficiencyOverrides? values))
            {
                return true;
            }

            try
            {
                float aimTime = Convert.ToSingle(__args[0]);
                float minimum = values.Global.MinAimTime;
                float maximum = values.Aiming.MAX_AIM_TIME;
                __result = Mathf.Clamp(aimTime, minimum, maximum);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool TryApplyDefaultDifficulty(FollowerState state, object? selectedPreset)
        {
            if (state.Bot == null || state.Bot.Settings?.Current == null || state.Values?.Sain == null)
            {
                return false;
            }

            BotDifficulty botDifficulty = state.Bot.Profile?.Info?.Settings?.BotDifficulty ?? BotDifficulty.normal;
            object? selectedSettings = GetPresetBotSettings(selectedPreset, state.TemplateRole, botDifficulty)
                ?? GetMemberValue(state.Info, "FileSettings");
            object? normalizedSettings = CreateNormalizedFileSettings(state.Bot, selectedSettings);
            if (normalizedSettings == null)
            {
                return false;
            }

            DismissAppliedModifier(state);
            state.NormalizedFileSettings = normalizedSettings;
            SetMemberValue(state.Info, "_fileSettings", normalizedSettings);

            FollowerSainProficiencyOverrides sain = state.Values.Sain;
            var modifier = new BotSettingsInGameModif();
            modifier.AccuratySpeedCoef = sain.RuntimeDifficulty.AccuratySpeedCoef;
            modifier.PrecicingSpeedCoef = sain.RuntimeDifficulty.PrecicingSpeedCoef;
            modifier.VisibleDistCoef = sain.RuntimeDifficulty.VisibleDistCoef;
            modifier.ScatteringCoef = sain.RuntimeDifficulty.ScatteringCoef;
            modifier.RuntimeVisionEffectK = sain.RuntimeDifficulty.RuntimeVisionEffectK;
            modifier.HearingDistCoef = sain.RuntimeDifficulty.HearingDistCoef;
            state.Bot.Settings.Current.Apply(modifier);
            state.AppliedModifier = modifier;

            float hearing = sain.RuntimeDifficulty.HearingDistCoef;
            float aggression = CalculateSelectedAggression(selectedPreset, normalizedSettings, state.Info);
            SetMemberValue(state.Difficulty, "<HearingDistanceModifier>k__BackingField", hearing);
            SetMemberValue(state.Difficulty, "<AggressionModifier>k__BackingField", aggression);

            float profileDifficulty = ApplyProfileDifficulty(state);
            if (!state.Logged)
            {
                state.Logged = true;
                Logger.LogInfo(
                    $"[SAIN] Follower proficiency uses SAIN Default follower={state.Bot.Profile?.Nickname ?? state.Bot.name} " +
                    $"role={state.TemplateRole} difficulty={botDifficulty} profile={profileDifficulty:0.##} hearing={hearing:0.##}");
            }
            return true;
        }

        private static float ApplyProfileDifficulty(FollowerState state)
        {
            FollowerSainProficiencyOverrides sain = state.Values.Sain;
            object? profile = GetMemberValue(state.Info, "Profile");
            SetMemberValue(profile, "<DifficultyModifier>k__BackingField", sain.ProfileDifficultyModifier);
            SetMemberValue(profile, "<DifficultyModifierSqrt>k__BackingField", sain.ProfileDifficultyModifierSqrt);
            return sain.ProfileDifficultyModifier;
        }

        private static float CalculateSelectedAggression(object? preset, object normalizedSettings, object info)
        {
            object? selectedGlobal = GetMemberValue(preset, "GlobalSettings");
            object? selectedGlobalDifficulty = GetMemberValue(selectedGlobal, "Difficulty");
            object? selectedBotDifficulty = GetMemberValue(normalizedSettings, "Difficulty");
            object? selectedPersonalityDifficulty = GetMemberValue(GetMemberValue(info, "PersonalitySettingsClass"), "Difficulty");
            object? selectedLocationDifficulty = GetCurrentLocationDifficulty(selectedGlobal);
            return MultiplyValues(
                "AggressionCoef",
                selectedGlobalDifficulty,
                selectedBotDifficulty,
                selectedPersonalityDifficulty,
                selectedLocationDifficulty);
        }

        private static bool TryResolveFollowerValues(FollowerState state, object bundle)
        {
            EnsureGlobalDefaultInitialized(bundle);

            BotDifficulty botDifficulty = state.Bot.Profile?.Info?.Settings?.BotDifficulty ?? BotDifficulty.normal;
            if (!TryGetDefaultBotSettings(state.TemplateRole, botDifficulty, out object? defaultSettings) ||
                defaultSettings == null)
            {
                return false;
            }

            FollowerSainProficiencyOverrides values = FollowerProficiency.DefaultValues.Sain.Clone();
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Difficulty"), values.Difficulty);
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Core"), values.Core);
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Aiming"), values.Aiming);
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Shoot"), values.Shoot);
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Mind"), values.Mind);
            CaptureCategoryValues(GetMemberValue(defaultSettings, "Move"), values.Move);

            object? defaultGlobal = GetMemberValue(bundle, "GlobalSettings");
            object? globalDifficulty = GetMemberValue(defaultGlobal, "Difficulty");
            object? botDifficultyValues = GetMemberValue(defaultSettings, "Difficulty");
            object? personalityDifficulty = GetDefaultPersonalityDifficulty(bundle, state.Info);
            object? locationDifficulty = GetCurrentLocationDifficulty(defaultGlobal);
            ResolveRuntimeDifficulty(
                values.RuntimeDifficulty,
                globalDifficulty,
                botDifficultyValues,
                personalityDifficulty,
                locationDifficulty);

            float groupModifier = TryGetDefaultBotGroup(bundle, state.TemplateRole, out object? group)
                ? GetFloat(group, "DifficultyModifier", 1f)
                : 1f;
            values.ProfileDifficultyModifier = Mathf.Round(
                groupModifier * GetProfileDifficultyMultiplier(botDifficulty) * 100f) / 100f;
            state.Values.ReplaceSainOverrides(values);
            return true;
        }

        private static void EnsureGlobalDefaultInitialized(object bundle)
        {
            lock (DefaultBundleLock)
            {
                if (_globalDefaultInitialized)
                {
                    return;
                }

                object? defaultGlobal = GetMemberValue(bundle, "GlobalSettings");
                FollowerSainProficiencyOverrides defaults = FollowerProficiency.DefaultValues.Sain;
                CaptureCategoryValues(GetMemberValue(defaultGlobal, "Shoot"), defaults.Global);
                CaptureCategoryValues(GetMemberValue(defaultGlobal, "Aiming"), defaults.Global);

                if (TryGetDefaultBotSettings(WildSpawnType.pmcUSEC, BotDifficulty.hard, out object? hardPmcSettings) &&
                    hardPmcSettings != null)
                {
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Difficulty"), defaults.Difficulty);
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Core"), defaults.Core);
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Aiming"), defaults.Aiming);
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Shoot"), defaults.Shoot);
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Mind"), defaults.Mind);
                    CaptureCategoryValues(GetMemberValue(hardPmcSettings, "Move"), defaults.Move);
                    ResolveRuntimeDifficulty(
                        defaults.RuntimeDifficulty,
                        GetMemberValue(defaultGlobal, "Difficulty"),
                        GetMemberValue(hardPmcSettings, "Difficulty"));
                }

                if (TryGetDefaultBotGroup(bundle, WildSpawnType.pmcUSEC, out object? hardPmcGroup))
                {
                    defaults.ProfileDifficultyModifier = Mathf.Round(
                        GetFloat(hardPmcGroup, "DifficultyModifier", 1f) * 1.5f * 100f) / 100f;
                }

                _globalDefaultInitialized = true;
            }
        }

        private static void ResolveRuntimeDifficulty(
            FollowerSainRuntimeDifficultyValues target,
            params object?[] difficultySources)
        {
            target.AccuratySpeedCoef = MultiplyValues("ACCURACY_SPEED_COEF", difficultySources);
            target.PrecicingSpeedCoef = MultiplyValues("PRECISION_SPEED_COEF", difficultySources);
            target.VisibleDistCoef = MultiplyValues("VisibleDistCoef", difficultySources);
            target.ScatteringCoef = MultiplyValues("ScatteringCoef", difficultySources);
            target.RuntimeVisionEffectK = MultiplyValues("GainSightCoef", difficultySources);
            target.HearingDistCoef = MultiplyValues("HearingDistanceCoef", difficultySources);
        }

        private static float GetProfileDifficultyMultiplier(BotDifficulty difficulty)
        {
            return difficulty switch
            {
                BotDifficulty.easy => 0.5f,
                BotDifficulty.normal => 1f,
                BotDifficulty.hard => 1.5f,
                BotDifficulty.impossible => 1.75f,
                _ => 1f,
            };
        }

        private static void DismissAppliedModifier(FollowerState state)
        {
            if (state.AppliedModifier == null || state.Bot?.Settings?.Current == null)
            {
                return;
            }

            state.Bot.Settings.Current.Dismiss(state.AppliedModifier);
            state.AppliedModifier = null;
        }

        private static float MultiplyValues(string memberName, params object?[] sources)
        {
            float result = 1f;
            foreach (object? source in sources)
            {
                result *= GetFloat(source, memberName, 1f);
            }
            return result;
        }

        private static object? GetDefaultPersonalityDifficulty(object bundle, object info)
        {
            object? personality = GetMemberValue(info, "Personality");
            IDictionary? personalities = GetMemberValue(bundle, "Personalities") as IDictionary;
            object? settings = FindDictionaryValue(personalities, personality?.ToString());
            return GetMemberValue(settings, "Difficulty");
        }

        private static object? GetCurrentLocationDifficulty(object? globalSettings)
        {
            object? location = GetMemberValue(globalSettings, "Location");
            if (location == null)
            {
                return null;
            }

            _locationSettingsExtensionsType ??= Type.GetType("SAIN.Extensions.LocationSettingsExtensions, SAIN");
            _currentLocationMethod ??= _locationSettingsExtensionsType != null
                ? AccessTools.GetDeclaredMethods(_locationSettingsExtensionsType)
                    .FirstOrDefault(method => method.Name == "Current" && method.GetParameters().Length == 1)
                : null;
            try
            {
                return _currentLocationMethod?.Invoke(null, new[] { location });
            }
            catch
            {
                return null;
            }
        }

        private static object? GetPresetBotSettings(object? preset, WildSpawnType role, BotDifficulty difficulty)
        {
            object? botSettings = GetMemberValue(preset, "BotSettings");
            MethodInfo? getSettings = botSettings != null
                ? AccessTools.Method(botSettings.GetType(), "GetSAINSettings", new[] { typeof(WildSpawnType), typeof(BotDifficulty) })
                : null;
            try
            {
                return getSettings?.Invoke(botSettings, new object[] { role, difficulty });
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetDefaultBotSettings(WildSpawnType role, BotDifficulty difficulty, out object? settings)
        {
            settings = null;
            if (!TryGetDefaultBundle(out object? bundle) || !TryGetDefaultBotGroup(bundle, role, out object? group))
            {
                return false;
            }

            IDictionary? difficultySettings = GetMemberValue(group, "Settings") as IDictionary;
            settings = FindDictionaryValue(difficultySettings, difficulty.ToString());
            return settings != null;
        }

        private static bool TryGetDefaultBotGroup(object bundle, WildSpawnType role, out object? group)
        {
            IDictionary? botSettings = GetMemberValue(bundle, "BotSettings") as IDictionary;
            group = FindDictionaryValue(botSettings, role.ToString());
            return group != null;
        }

        private static bool TryGetDefaultBundle(out object? bundle)
        {
            if (_defaultBundle != null)
            {
                bundle = _defaultBundle;
                return true;
            }

            lock (DefaultBundleLock)
            {
                if (_defaultBundle != null)
                {
                    bundle = _defaultBundle;
                    return true;
                }

                try
                {
                    _presetSyncType ??= Type.GetType("SAIN.Preset.Server.PresetSync, SAIN");
                    _serverDefaultsMethod ??= _presetSyncType != null
                        ? AccessTools.Method(_presetSyncType, "ServerDefaults")
                        : null;
                    if (_serverDefaultsMethod?.Invoke(null, null) is IEnumerable defaults)
                    {
                        foreach (object? candidate in defaults)
                        {
                            object? info = GetMemberValue(candidate, "Info");
                            object? baseDifficulty = GetMemberValue(info, "BaseSAINDifficulty");
                            if (string.Equals(baseDifficulty?.ToString(), "hard", StringComparison.OrdinalIgnoreCase))
                            {
                                _defaultBundle = candidate;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_missingDefaultLogged)
                    {
                        _missingDefaultLogged = true;
                        Logger.LogError("[SAIN] Could not read SAIN's server-generated Default preset.");
                        Logger.LogError(ex);
                    }
                }
            }

            bundle = _defaultBundle;
            if (bundle == null && !_missingDefaultLogged)
            {
                _missingDefaultLogged = true;
                Logger.LogError("[SAIN] Server-generated Default preset is unavailable; follower proficiency will keep SAIN's original values.");
            }
            return bundle != null;
        }

        private static object? GetLoadedPreset()
        {
            Type? sainPlugin = Type.GetType("SAIN.SAINPlugin, SAIN");
            PropertyInfo? property = sainPlugin != null ? AccessTools.Property(sainPlugin, "LoadedPreset") : null;
            _loadedPreset = property?.GetValue(null) ?? _loadedPreset;
            return _loadedPreset;
        }

        private static object? GetSainBot(BotOwner bot)
        {
            _sainEnableType ??= Type.GetType("SAIN.SAINEnableClass, SAIN");
            _getSainByProfile ??= _sainEnableType != null
                ? AccessTools.Method(_sainEnableType, "GetSAIN", new[] { typeof(string), _sainEnableType.Assembly.GetType("SAIN.Components.BotComponent")!.MakeByRefType() })
                : null;
            if (_getSainByProfile == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return null;
            }

            object?[] args = { bot.ProfileId, null };
            return _getSainByProfile.Invoke(null, args) is true ? args[1] : null;
        }

        private static bool TryGetActiveState(BotOwner? bot, out FollowerState? state)
        {
            state = null;
            return bot != null &&
                !string.IsNullOrEmpty(bot.ProfileId) &&
                States.TryGetValue(bot.ProfileId, out state);
        }

        private static bool TryGetActiveAimValues(out FollowerSainProficiencyOverrides? values)
        {
            values = _activeAimValues?.Count > 0 ? _activeAimValues.Peek() : null;
            return values != null;
        }

        private static void ApplyCategoryValues(
            object targetSettings,
            object selectedSettings,
            string categoryName,
            object values)
        {
            object? selectedCategory = GetMemberValue(selectedSettings, categoryName);
            if (selectedCategory == null)
            {
                return;
            }

            object categoryClone = MemberwiseCloneMethod.Invoke(selectedCategory, null);
            foreach (PropertyInfo valueProperty in values.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (valueProperty.CanRead)
                {
                    SetMemberValue(categoryClone, valueProperty.Name, valueProperty.GetValue(values));
                }
            }
            SetMemberValue(targetSettings, categoryName, categoryClone);
        }

        private static void CaptureCategoryValues(object? source, object target)
        {
            if (source == null || target == null)
            {
                return;
            }

            foreach (PropertyInfo targetProperty in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!targetProperty.CanWrite)
                {
                    continue;
                }

                object? sourceValue = GetMemberValue(source, targetProperty.Name);
                if (sourceValue == null)
                {
                    continue;
                }

                try
                {
                    object converted = Convert.ChangeType(sourceValue, targetProperty.PropertyType);
                    targetProperty.SetValue(target, converted);
                }
                catch
                {
                    // Keep the local fallback if a future SAIN version changes a value's type.
                }
            }
        }

        private static object? FindDictionaryValue(IDictionary? dictionary, string? keyName)
        {
            if (dictionary == null || string.IsNullOrEmpty(keyName))
            {
                return null;
            }

            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.Equals(entry.Key?.ToString(), keyName, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
            return null;
        }

        private static object? GetMemberValue(object? instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            Type? type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            while (type != null)
            {
                FieldInfo? field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(instance);
                }
                type = type.BaseType;
            }
            return null;
        }

        private static bool SetMemberValue(object? instance, string name, object? value)
        {
            if (instance == null)
            {
                return false;
            }

            Type? type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property?.CanWrite == true)
            {
                property.SetValue(instance, value);
                return true;
            }

            while (type != null)
            {
                FieldInfo? field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        private static float GetFloat(object? instance, string name, float fallback)
        {
            object? value = GetMemberValue(instance, name);
            try
            {
                return value != null ? Convert.ToSingle(value) : fallback;
            }
            catch
            {
                return fallback;
            }
        }

    }
}
