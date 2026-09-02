using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Core-owned access to the external SAIN mod's enemy state. This bridge is reflection-only so
    /// the main plugin remains loadable without SAIN. It can mirror an explicit follower contact
    /// into SAIN and read SAIN's exact retained target/pressure state, but it never grants EFT sight,
    /// aim, or fire permission.
    /// </summary>
    public static class SainGoalEnemyBridge
    {
        private static bool sainAccessorResolved;
        private static bool accessorFailureLogged;
        private static MethodInfo? getSainByBotOwnerMethod;
        private static MethodInfo? getSainByProfileMethod;

        private static Type? sainBotType;
        private static PropertyInfo? sainBotHasEnemyProperty;
        private static PropertyInfo? sainBotEnemyControllerProperty;

        private static Type? enemyControllerType;
        private static PropertyInfo? controllerGoalEnemyProperty;
        private static FieldInfo? controllerGoalEnemyField;
        private static MethodInfo? controllerCheckAddEnemyMethod;
        private static MethodInfo? controllerChooseEnemyMethod;
        private static MethodInfo? controllerGoalEnemySetter;
        private static MethodInfo? controllerSetGoalEnemyMethod;

        private static Type? sainEnemyType;
        private static PropertyInfo? enemyProfileIdProperty;
        private static PropertyInfo? enemyKnownProperty;
        private static PropertyInfo? enemyLastKnownPositionProperty;
        private static PropertyInfo? enemyInfoProperty;
        private static PropertyInfo? enemyLookingAtMeProperty;
        private static MethodInfo? enemyUpdateLastSeenPositionMethod;
        private static readonly ConditionalWeakTable<BotOwner, EnemyLookCache> EnemyLookCaches =
            new ConditionalWeakTable<BotOwner, EnemyLookCache>();

        private sealed class EnemyLookCache
        {
            public EnemyInfo? EnemyInfo;
            public float RefreshAt;
            public bool HasValue;
            public bool LookingAtFollower;
        }

        public static bool TrySyncEnemyState(BotOwner owner, Player enemyPlayer, bool prioritizeAsGoal)
        {
            if (!pitFireTeam.IsSAINInstalled ||
                owner == null ||
                enemyPlayer == null ||
                string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            try
            {
                object? sainBot = TryGetSainBot(owner);
                if (sainBot == null)
                {
                    return false;
                }

                ResolveSainBotAccessors(sainBot.GetType());
                object? enemyController = sainBotEnemyControllerProperty?.GetValue(sainBot);
                if (enemyController == null)
                {
                    return false;
                }

                ResolveEnemyControllerAccessors(enemyController.GetType());
                if (controllerCheckAddEnemyMethod == null)
                {
                    return false;
                }

                object? sainEnemy = controllerCheckAddEnemyMethod.Invoke(
                    enemyController,
                    new object[] { enemyPlayer });
                if (sainEnemy == null)
                {
                    return false;
                }

                ResolveEnemyAccessors(sainEnemy.GetType());
                if (enemyUpdateLastSeenPositionMethod == null)
                {
                    return false;
                }

                enemyUpdateLastSeenPositionMethod.Invoke(
                    sainEnemy,
                    new object[] { enemyPlayer.Position, Time.time });

                object? currentGoal = controllerGoalEnemyProperty?.GetValue(enemyController) ??
                                      controllerGoalEnemyField?.GetValue(enemyController);
                if (prioritizeAsGoal || currentGoal == null)
                {
                    SetGoalEnemy(enemyController, sainEnemy);
                }
                else
                {
                    controllerChooseEnemyMethod?.Invoke(enemyController, Array.Empty<object>());
                    currentGoal = controllerGoalEnemyProperty?.GetValue(enemyController) ??
                                  controllerGoalEnemyField?.GetValue(enemyController);
                    if (currentGoal == null)
                    {
                        SetGoalEnemy(enemyController, sainEnemy);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogAccessorFailureOnce(ex);
                return false;
            }
        }

        public static bool IsEnemyLookingAtFollower(BotOwner owner, EnemyInfo expectedEnemyInfo)
        {
            if (!pitFireTeam.IsSAINInstalled)
            {
                return owner?.IsEnemyLookingAtMe(expectedEnemyInfo) == true;
            }

            if (owner == null || expectedEnemyInfo == null)
            {
                return false;
            }

            EnemyLookCache cache = EnemyLookCaches.GetOrCreateValue(owner);
            if (ReferenceEquals(cache.EnemyInfo, expectedEnemyInfo) && Time.time < cache.RefreshAt)
            {
                return cache.HasValue && cache.LookingAtFollower;
            }

            cache.EnemyInfo = expectedEnemyInfo;
            cache.RefreshAt = Time.time + 0.1f;
            cache.HasValue = TryGetEnemyLookingAtFollower(owner, expectedEnemyInfo, out bool lookingAtFollower);
            cache.LookingAtFollower = lookingAtFollower;
            return cache.HasValue && cache.LookingAtFollower;
        }

        public static bool TryGetEnemyLookingAtFollower(
            BotOwner owner,
            EnemyInfo expectedEnemyInfo,
            out bool lookingAtFollower)
        {
            lookingAtFollower = false;
            try
            {
                if (!TryGetExactSainGoalEnemy(owner, expectedEnemyInfo, out object? sainEnemy) ||
                    enemyLookingAtMeProperty?.GetValue(sainEnemy) is not bool result)
                {
                    return false;
                }

                lookingAtFollower = result;
                return true;
            }
            catch (Exception ex)
            {
                LogAccessorFailureOnce(ex);
                return false;
            }
        }

        public static bool TryGetRetainedSameGoalEnemy(
            BotOwner owner,
            EnemyInfo expectedEnemyInfo,
            out Vector3 lastKnownPosition)
        {
            lastKnownPosition = Vector3.zero;
            string? expectedProfileId = expectedEnemyInfo?.ProfileId;
            if (!pitFireTeam.IsSAINInstalled ||
                owner == null ||
                expectedEnemyInfo == null ||
                string.IsNullOrEmpty(expectedProfileId))
            {
                return false;
            }

            try
            {
                if (!TryGetExactSainGoalEnemy(owner, expectedEnemyInfo, out object? sainEnemy))
                {
                    return false;
                }

                object? lastKnownPositionValue = enemyLastKnownPositionProperty?.GetValue(sainEnemy);
                if (enemyKnownProperty?.GetValue(sainEnemy) is not bool enemyKnown ||
                    !enemyKnown ||
                    lastKnownPositionValue is not Vector3 retainedPosition ||
                    !IsFinite(retainedPosition))
                {
                    return false;
                }

                lastKnownPosition = retainedPosition;
                return true;
            }
            catch (Exception ex)
            {
                LogAccessorFailureOnce(ex);
                lastKnownPosition = Vector3.zero;
                return false;
            }
        }

        private static bool TryGetExactSainGoalEnemy(
            BotOwner owner,
            EnemyInfo expectedEnemyInfo,
            out object? sainEnemy)
        {
            sainEnemy = null;
            string? expectedProfileId = expectedEnemyInfo?.ProfileId;
            if (!pitFireTeam.IsSAINInstalled ||
                owner == null ||
                expectedEnemyInfo == null ||
                string.IsNullOrEmpty(expectedProfileId))
            {
                return false;
            }

            object? sainBot = TryGetSainBot(owner);
            if (sainBot == null)
            {
                return false;
            }

            ResolveSainBotAccessors(sainBot.GetType());
            if (sainBotHasEnemyProperty?.GetValue(sainBot) is not bool hasEnemy || !hasEnemy)
            {
                return false;
            }

            object? enemyController = sainBotEnemyControllerProperty?.GetValue(sainBot);
            if (enemyController == null)
            {
                return false;
            }

            ResolveEnemyControllerAccessors(enemyController.GetType());
            sainEnemy = controllerGoalEnemyProperty?.GetValue(enemyController) ??
                        controllerGoalEnemyField?.GetValue(enemyController);
            if (sainEnemy == null)
            {
                return false;
            }

            ResolveEnemyAccessors(sainEnemy.GetType());
            string? profileId = enemyProfileIdProperty?.GetValue(sainEnemy) as string;
            EnemyInfo? enemyInfo = enemyInfoProperty?.GetValue(sainEnemy) as EnemyInfo;
            return string.Equals(profileId, expectedProfileId, StringComparison.Ordinal) &&
                   ReferenceEquals(enemyInfo, expectedEnemyInfo) &&
                   string.Equals(enemyInfo.ProfileId, expectedProfileId, StringComparison.Ordinal) &&
                   enemyInfo.Person?.HealthController?.IsAlive == true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static object? TryGetSainBot(BotOwner owner)
        {
            ResolveSainAccessor();
            if (getSainByBotOwnerMethod != null)
            {
                return getSainByBotOwnerMethod.Invoke(null, new object[] { owner });
            }

            if (getSainByProfileMethod == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return null;
            }

            object?[] arguments = { owner.ProfileId, null };
            bool found = getSainByProfileMethod.Invoke(null, arguments) is bool result && result;
            return found ? arguments[1] : null;
        }

        private static void ResolveSainAccessor()
        {
            if (sainAccessorResolved)
            {
                return;
            }

            sainAccessorResolved = true;
            Type? sainEnableType = AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                                   AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");
            if (sainEnableType == null)
            {
                return;
            }

            foreach (MethodInfo method in sainEnableType.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "GetSAIN", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                {
                    getSainByBotOwnerMethod = method;
                }
                else if (parameters.Length == 2 &&
                         parameters[0].ParameterType == typeof(string) &&
                         parameters[1].IsOut)
                {
                    getSainByProfileMethod = method;
                }
            }
        }

        private static void ResolveSainBotAccessors(Type runtimeType)
        {
            if (sainBotType == runtimeType)
            {
                return;
            }

            sainBotType = runtimeType;
            sainBotHasEnemyProperty = AccessTools.Property(runtimeType, "HasEnemy");
            sainBotEnemyControllerProperty = AccessTools.Property(runtimeType, "EnemyController");
        }

        private static void ResolveEnemyControllerAccessors(Type runtimeType)
        {
            if (enemyControllerType == runtimeType)
            {
                return;
            }

            enemyControllerType = runtimeType;
            controllerGoalEnemyProperty = AccessTools.Property(runtimeType, "GoalEnemy");
            controllerGoalEnemyField = AccessTools.Field(runtimeType, "_goalEnemy");
            controllerCheckAddEnemyMethod = AccessTools.Method(
                runtimeType,
                "CheckAddEnemy",
                new[] { typeof(IPlayer) });
            controllerChooseEnemyMethod = AccessTools.Method(runtimeType, "ChooseEnemy", Type.EmptyTypes);
            controllerGoalEnemySetter = AccessTools.PropertySetter(runtimeType, "GoalEnemy");
            controllerSetGoalEnemyMethod = AccessTools.Method(
                runtimeType,
                "setGoalEnemy",
                new[] { typeof(EnemyInfo) });
        }

        private static void ResolveEnemyAccessors(Type runtimeType)
        {
            if (sainEnemyType == runtimeType)
            {
                return;
            }

            sainEnemyType = runtimeType;
            enemyProfileIdProperty = AccessTools.Property(runtimeType, "EnemyProfileId");
            enemyKnownProperty = AccessTools.Property(runtimeType, "EnemyKnown");
            enemyLastKnownPositionProperty = AccessTools.Property(runtimeType, "LastKnownPosition");
            enemyInfoProperty = AccessTools.Property(runtimeType, "EnemyInfo");
            enemyLookingAtMeProperty = AccessTools.Property(runtimeType, "EnemyLookingAtMe");
            enemyUpdateLastSeenPositionMethod = AccessTools.Method(
                runtimeType,
                "UpdateLastSeenPosition",
                new[] { typeof(Vector3), typeof(float) });
        }

        private static void SetGoalEnemy(object enemyController, object sainEnemy)
        {
            controllerGoalEnemySetter?.Invoke(enemyController, new[] { sainEnemy });
            if (enemyInfoProperty?.GetValue(sainEnemy) is EnemyInfo enemyInfo)
            {
                controllerSetGoalEnemyMethod?.Invoke(enemyController, new object[] { enemyInfo });
            }
        }

        private static void LogAccessorFailureOnce(Exception exception)
        {
            if (accessorFailureLogged)
            {
                return;
            }

            accessorFailureLogged = true;
            pitFireTeam.Log.LogWarning($"[SAIN] Failed to access follower enemy state: {exception.GetBaseException().Message}");
        }
    }
}
