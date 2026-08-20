using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Read-only access to the external SAIN mod's current enemy selection. SAIN keeps its own
    /// GoalEnemy and mirrors that EnemyInfo into EFT memory; core follower combat uses this bridge
    /// only to verify the exact established target before an unscoped EFT clear is allowed.
    /// </summary>
    internal static class SainGoalEnemyBridge
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

        private static Type? sainEnemyType;
        private static PropertyInfo? enemyProfileIdProperty;
        private static PropertyInfo? enemyKnownProperty;
        private static PropertyInfo? enemyLastKnownPositionProperty;
        private static PropertyInfo? enemyInfoProperty;

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
                object? sainEnemy = controllerGoalEnemyProperty?.GetValue(enemyController) ??
                                    controllerGoalEnemyField?.GetValue(enemyController);
                if (sainEnemy == null)
                {
                    return false;
                }

                ResolveEnemyAccessors(sainEnemy.GetType());
                string? profileId = enemyProfileIdProperty?.GetValue(sainEnemy) as string;
                object? lastKnownPositionValue = enemyLastKnownPositionProperty?.GetValue(sainEnemy);
                if (!string.Equals(profileId, expectedProfileId, StringComparison.Ordinal) ||
                    enemyKnownProperty?.GetValue(sainEnemy) is not bool enemyKnown ||
                    !enemyKnown ||
                    lastKnownPositionValue is not Vector3 retainedPosition ||
                    !IsFinite(retainedPosition))
                {
                    return false;
                }

                EnemyInfo? enemyInfo = enemyInfoProperty?.GetValue(sainEnemy) as EnemyInfo;
                if (!ReferenceEquals(enemyInfo, expectedEnemyInfo) ||
                    !string.Equals(enemyInfo.ProfileId, expectedProfileId, StringComparison.Ordinal) ||
                    enemyInfo.Person?.HealthController?.IsAlive != true)
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
        }

        private static void LogAccessorFailureOnce(Exception exception)
        {
            if (accessorFailureLogged)
            {
                return;
            }

            accessorFailureLogged = true;
            pitFireTeam.Log.LogWarning($"[SAIN] Failed to read retained follower GoalEnemy: {exception.GetBaseException().Message}");
        }
    }
}
