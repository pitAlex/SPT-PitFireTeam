using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset;
using SAIN.Preset.BotSettings;
using SAIN.Preset.Shared.BotSettings.SAINSettings;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerPersonalityPatch
    {
        private static Type? _sainEnableType;
        private static Type? _sainPluginType;
        private static MethodInfo? _getSainByBotOwner;
        private static MethodInfo? _getSainByProfile;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(BossPlayers),
                "AddFollower",
                new[] { typeof(BotOwner), typeof(pitAIBossPlayer), typeof(bool), typeof(WildSpawnType), typeof(string) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find BossPlayers.AddFollower for SAIN follower template patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerPersonalityPatch), nameof(Postfix_AddFollower)));
            Modules.Logger.LogInfo("[Init] SAIN follower template patch applied.");
        }

        private static void Postfix_AddFollower(BotOwner bot, bool squadMate, string tactic, BotFollowerPlayer __result)
        {
            try
            {
                if (!pitFireTeam.IsSAINInstalled) return;
                if (bot == null || __result == null || bot.IsDead) return;

                if (squadMate)
                {
                    // Squad-mate followers get the full BigPipe template + personality + forget-time override.
                    TryApplyFollowerBigPipeTemplate(bot, tactic);
                }
                else
                {
                    // Recruited followers keep SAIN's lottery personality, but still need the
                    // forget time corrected — SAIN's CalcTimeBeforeSearch overwrites it during init.
                    OverrideFollowerForgetTime(bot);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed to apply followerBigPipe template.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void TryApplyFollowerBigPipeTemplate(BotOwner bot, string tactic = "Default")
        {
            if (bot == null) return;
            if (!ResolveSainGetMethods()) return;

            object? sainBot = GetSainBot(bot);
            if (sainBot == null) return;

            object? info = GetInstanceMemberValue(sainBot, "Info");
            if (info == null) return;

            object? loadedPreset = GetSainLoadedPreset();
            if (loadedPreset == null) return;

            object? botSettings = GetInstanceMemberValue(loadedPreset, "BotSettings");
            if (botSettings == null) return;

            MethodInfo? getSettings = AccessTools.Method(
                botSettings.GetType(),
                "GetSAINSettings",
                new[] { typeof(WildSpawnType), typeof(BotDifficulty) });
            if (getSettings == null) return;

            BotDifficulty difficulty = bot.Profile?.Info?.Settings?.BotDifficulty ?? BotDifficulty.normal;
            SAINSettingsClass? selectedTemplate =
                getSettings.Invoke(botSettings, new object[] { WildSpawnType.followerBigPipe, difficulty }) as SAINSettingsClass;
            if (selectedTemplate == null) return;

            FollowerSainProficiency.SetTemplateRole(bot, WildSpawnType.followerBigPipe);
            SAINSettingsClass sourceTemplate =
                FollowerSainProficiency.CreateNormalizedFileSettings(
                    bot,
                    selectedTemplate) as SAINSettingsClass
                ?? selectedTemplate;

            SAINSettingsClass followerTemplate = CloneSettings(sourceTemplate);
            ApplyFollowerTemplateFineTuning(followerTemplate, bot);

            SetInstanceMemberValue(info, "_fileSettings", followerTemplate);
            SetFollowerProfileDifficultyModifier(info, difficulty);
            RebuildSainInfoFromTemplate(info, loadedPreset, followerTemplate, bot);

            TryApplySpawnedFollowerPersonality(info, bot);

            // CalcTimeBeforeSearch (called above) overwrites TIME_TO_FORGOR_ABOUT_ENEMY_SEC and
            // SAIN's ForgetEnemyTime with a SAIN-internal random value. Re-apply our configured value last.
            OverrideFollowerForgetTime(bot);
        }

        private static SAINSettingsClass CloneSettings(SAINSettingsClass source)
        {
            SAINSettingsClass clone = DeepCloneObject(source) as SAINSettingsClass ?? new SAINSettingsClass();
            clone.Init();
            return clone;
        }

        private static void ApplyFollowerTemplateFineTuning(SAINSettingsClass settings, BotOwner bot)
        {
            if (settings == null) return;

            // Keep this method as the single place for follower-specific tuning on top of the BigPipe template.
            // Intentionally empty for now: baseline behavior should mirror followerBigPipe as closely as possible.
            // Note: forget-time override is applied separately via OverrideFollowerForgetTime after all SAIN init.
        }

        /// <summary>
        /// Re-applies the player-configured enemy forget time to both vanilla and SAIN's internal timer.
        /// Must be called AFTER all SAIN initialization (CalcTimeBeforeSearch etc.) to survive SAIN overwrites.
        /// </summary>
        private static void OverrideFollowerForgetTime(BotOwner bot)
        {
            float forgetSeconds = (float)pitFireTeam.enemyRemember.Value;
            if (forgetSeconds <= 0f) return;

            // Vanilla setting — SAIN's CalcTimeBeforeSearch overwrites this, so we restore it here.
            if (bot?.Settings?.FileSettings?.Mind != null)
            {
                bot.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = forgetSeconds;
            }

            // SAIN's EnemyKnownChecker uses Info.ForgetEnemyTime (private setter) independently of the
            // vanilla field, so we also force that backing field to our value.
            if (!ResolveSainGetMethods()) return;
            object? sainBot = GetSainBot(bot);
            if (sainBot == null) return;
            object? info = GetInstanceMemberValue(sainBot, "Info");
            if (info == null) return;

            bool applied = SetInstanceMemberValue(info, "<ForgetEnemyTime>k__BackingField", forgetSeconds);
            if (!applied)
            {
                Modules.Logger.LogInfo("[SAIN] Could not find ForgetEnemyTime backing field on SAINBotInfoClass — forget time may not be respected.");
            }
        }

        private static void TryApplySpawnedFollowerPersonality(object info, BotOwner bot)
        {
            if (info == null || bot == null) return;

            WildSpawnType role = bot.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            if (!TryResolveSpawnedFollowerPersonality(role, out EPersonality personality))
            {
                Modules.Logger.LogInfo($"[SAIN] Preserved existing spawned follower personality follower={bot.Profile?.Nickname ?? bot.name} role={role}");
                return;
            }

            ApplyPersonality(info, personality);
        }

        private static bool TryResolveSpawnedFollowerPersonality(WildSpawnType role, out EPersonality personality)
        {
            switch (role)
            {
                case WildSpawnType.pmcBEAR:
                case WildSpawnType.pmcUSEC:
                case WildSpawnType.pmcBot:
                case WildSpawnType.followerBigPipe:
                    personality = EPersonality.Chad;
                    return true;

                case WildSpawnType.bossKnight:
                    personality = EPersonality.GigaChad;
                    return true;

                case WildSpawnType.followerBirdEye:
                    personality = EPersonality.Normal;
                    return true;
            }

            personality = default;
            return false;
        }

        /// <summary>
        /// Forces a SAIN personality on a SAINBotInfoClass instance and re-evaluates timing values that
        /// depend on personality (search time, hold-ground delay).
        /// Call with EPersonality.Normal, .Chad, or .GigaChad as needed.
        /// </summary>
        private static void ApplyPersonality(object info, EPersonality personality)
        {
            if (info == null) return;

            MethodInfo? setPersonality = AccessTools.Method(info.GetType(), "SetPersonality", new[] { typeof(EPersonality) });
            if (setPersonality == null)
            {
                Modules.Logger.LogError("[SAIN] SetPersonality method not found on SAINBotInfoClass.");
                return;
            }

            setPersonality.Invoke(info, new object[] { personality });

            // Re-run timing helpers because they read from PersonalitySettings.
            AccessTools.Method(info.GetType(), "CalcTimeBeforeSearch", Type.EmptyTypes)?.Invoke(info, null);
            AccessTools.Method(info.GetType(), "CalcHoldGroundDelay", Type.EmptyTypes)?.Invoke(info, null);

            Modules.Logger.LogInfo($"[SAIN] Applied personality={personality} to follower.");
        }

        private static void SetFollowerProfileDifficultyModifier(object info, BotDifficulty difficulty)
        {
            object? profile = GetInstanceMemberValue(info, "Profile");
            if (profile == null) return;

            float modifier = SAINPlugin.LoadedPreset?.BotSettings?.SAINSettings?.TryGetValue(
                WildSpawnType.followerBigPipe,
                out SAINSettingsGroupClass settingsGroup) == true
                ? settingsGroup.DifficultyModifier
                : 1f;

            switch (difficulty)
            {
                case BotDifficulty.easy:
                    modifier *= 0.5f;
                    break;
                case BotDifficulty.normal:
                    modifier *= 1.0f;
                    break;
                case BotDifficulty.hard:
                    modifier *= 1.5f;
                    break;
                case BotDifficulty.impossible:
                    modifier *= 1.75f;
                    break;
            }

            modifier = Mathf.Round(modifier * 100f) / 100f;
            float modifierSqrt = Mathf.Round(Mathf.Sqrt(modifier) * 100f) / 100f;

            SetInstanceMemberValue(profile, "<DifficultyModifier>k__BackingField", modifier);
            SetInstanceMemberValue(profile, "<DifficultyModifierSqrt>k__BackingField", modifierSqrt);
        }

        private static void RebuildSainInfoFromTemplate(object info, object loadedPreset, SAINSettingsClass followerTemplate, BotOwner bot)
        {
            object? difficulty = GetInstanceMemberValue(info, "Difficulty");
            MethodInfo? updateSettings = difficulty != null ? AccessTools.Method(difficulty.GetType(), "UpdateSettings") : null;
            updateSettings?.Invoke(difficulty, new[] { loadedPreset });

            MethodInfo? calcTimeBeforeSearch = AccessTools.Method(info.GetType(), "CalcTimeBeforeSearch", Type.EmptyTypes);
            MethodInfo? calcHoldGroundDelay = AccessTools.Method(info.GetType(), "CalcHoldGroundDelay", Type.EmptyTypes);
            calcTimeBeforeSearch?.Invoke(info, null);
            calcHoldGroundDelay?.Invoke(info, null);

            ApplySettingsToBot(info, followerTemplate);
            Modules.Logger.LogInfo($"[SAIN] Applied followerBigPipe template to follower={bot.Profile?.Nickname ?? bot.name} difficulty={bot.Profile?.Info?.Settings?.BotDifficulty} (personality will be set next)");
        }

        private static void ApplySettingsToBot(object info, SAINSettingsClass settings)
        {
            object? botOwnerObject = GetInstanceMemberValue(info, "BotOwner");
            if (botOwnerObject is not BotOwner botOwner) return;

            Type? extensionsType = AccessTools.TypeByName("SAIN.Extensions.SainSettingsExtensions");
            MethodInfo? setConfigValues = AccessTools.Method(
                extensionsType,
                "SetConfigValues",
                new[] { typeof(SAINSettingsClass), typeof(BotOwner) });
            if (setConfigValues == null)
            {
                Modules.Logger.LogError("[SAIN] SetConfigValues extension was not found; follower template was not applied to EFT settings.");
                return;
            }

            setConfigValues.Invoke(null, new object[] { settings, botOwner });
        }

        private static object? DeepCloneObject(object? source)
        {
            if (source == null) return null;

            Type type = source.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return source;
            }

            if (typeof(IList).IsAssignableFrom(type))
            {
                IList sourceList = (IList)source;
                IList cloneList = (IList)Activator.CreateInstance(type)!;
                foreach (object? item in sourceList)
                {
                    cloneList.Add(DeepCloneObject(item));
                }
                return cloneList;
            }

            object clone = Activator.CreateInstance(type)!;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.IsInitOnly) continue;
                object? fieldValue = field.GetValue(source);
                field.SetValue(clone, DeepCloneObject(fieldValue));
            }

            return clone;
        }

        private static object? GetSainLoadedPreset()
        {
            _sainPluginType ??= AccessTools.TypeByName("SAIN.SAINPlugin");
            if (_sainPluginType == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo? prop = _sainPluginType.GetProperty("LoadedPreset", flags);
            return prop?.CanRead == true ? prop.GetValue(null, null) : null;
        }

        private static object? GetSainBot(BotOwner bot)
        {
            if (_getSainByBotOwner != null)
            {
                return _getSainByBotOwner.Invoke(null, new object[] { bot });
            }
            if (_getSainByProfile != null && !string.IsNullOrEmpty(bot.ProfileId))
            {
                object?[] args = { bot.ProfileId, null };
                bool hasSain = (bool)_getSainByProfile.Invoke(null, args);
                if (hasSain) return args[1];
            }
            return null;
        }

        private static bool ResolveSainGetMethods()
        {
            if (_sainEnableType == null)
            {
                _sainEnableType = AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                                  AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");
            }

            if (_sainEnableType == null) return false;

            if (_getSainByBotOwner == null || _getSainByProfile == null)
            {
                foreach (MethodInfo method in _sainEnableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name != "GetSAIN") continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                    {
                        _getSainByBotOwner = method;
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                    {
                        _getSainByProfile = method;
                    }
                }
            }

            return _getSainByBotOwner != null || _getSainByProfile != null;
        }

        private static object? GetInstanceMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type? type = target.GetType();
            while (type != null)
            {
                PropertyInfo? property = type.GetProperty(memberName, flags);
                if (property?.CanRead == true)
                {
                    return property.GetValue(target, null);
                }

                FieldInfo? field = type.GetField(memberName, flags);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                type = type.BaseType;
            }

            return null;
        }

        private static bool SetInstanceMemberValue(object target, string memberName, object? value)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type? type = target.GetType();
            while (type != null)
            {
                PropertyInfo? property = type.GetProperty(memberName, flags);
                if (property?.CanWrite == true)
                {
                    property.SetValue(target, value, null);
                    return true;
                }

                FieldInfo? field = type.GetField(memberName, flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }
}
