using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    /// <summary>
    /// Gives followers first ownership of visible-part selection. HarmonyX still runs later
    /// prefixes, so the reflected SAIN patch also guards SAIN's competing EnemyInfo prefix.
    /// </summary>
    internal sealed class FollowerAimTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.GetVisiblePartToShoot));
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore(new[] { "BodyPartToShootPatch" })]
        private static bool PatchPrefix(EnemyInfo __instance, ref Vector3 __result)
        {
            try
            {
                if (!FollowerAimTargetPolicy.TrySelectFollowerShootPoint(
                        __instance,
                        out Vector3 shootPoint,
                        out bool hasShootPoint) ||
                    !hasShootPoint)
                {
                    return true;
                }

                __result = shootPoint;
                return false;
            }
            catch
            {
                // Fail open so an unexpected EFT body-part shape retains vanilla/SAIN selection.
                return true;
            }
        }
    }

    internal static class FollowerSainAimTargetPatch
    {
        private static Func<object, EnemyInfo?>? _getVisibleEnemyInfo;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type? shootDataType = Type.GetType("SAIN.SAINComponent.Classes.SAINShootData, SAIN");
                Type? enemyType = shootDataType?.Assembly.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
                MethodInfo? getAimTarget = shootDataType != null ? AccessTools.Method(shootDataType, "GetAimTarget") : null;
                Type? bodyPartPatchType = shootDataType?.Assembly.GetType("SAIN.Patches.Aim.BodyPartToShootPatch");
                MethodInfo? bodyPartPrefix = bodyPartPatchType != null
                    ? AccessTools.Method(bodyPartPatchType, "Patch", new[] { typeof(Vector3).MakeByRefType(), typeof(EnemyInfo) })
                    : null;
                MethodInfo? getEnemyInfo = enemyType != null ? AccessTools.PropertyGetter(enemyType, "EnemyInfo") : null;
                MethodInfo? getVisible = enemyType != null ? AccessTools.PropertyGetter(enemyType, "IsVisible") : null;
                MethodInfo? getCanShoot = enemyType != null ? AccessTools.PropertyGetter(enemyType, "CanShoot") : null;
                ParameterInfo[]? parameters = getAimTarget?.GetParameters();
                if (getAimTarget == null || !getAimTarget.IsStatic || getAimTarget.ReturnType != typeof(Vector3?) ||
                    parameters == null || parameters.Length < 1 || parameters.Length > 2 || parameters[0].ParameterType != enemyType ||
                    getEnemyInfo == null || getEnemyInfo.IsStatic || getEnemyInfo.ReturnType != typeof(EnemyInfo) ||
                    getVisible == null || getVisible.IsStatic || getVisible.ReturnType != typeof(bool) ||
                    getCanShoot == null || getCanShoot.IsStatic || getCanShoot.ReturnType != typeof(bool) ||
                    bodyPartPrefix == null || !bodyPartPrefix.IsStatic || bodyPartPrefix.ReturnType != typeof(bool))
                {
                    Modules.Logger.LogError("[SAIN] Follower aim-target patch skipped: unsupported target-selection layout.");
                    return;
                }

                // 4.5.0 takes (Enemy, BotComponent); 4.5.1 takes (Enemy). Both must pass
                // their native visibility/shootability gate before our policy chooses a part.
                // Compile the accessor once so shooting does not invoke reflection each frame.
                ParameterExpression enemy = Expression.Parameter(typeof(object), "enemy");
                UnaryExpression typedEnemy = Expression.Convert(enemy, enemyType!);
                _getVisibleEnemyInfo = Expression.Lambda<Func<object, EnemyInfo?>>(
                    Expression.Condition(
                        Expression.AndAlso(Expression.Call(typedEnemy, getVisible), Expression.Call(typedEnemy, getCanShoot)),
                        Expression.Call(typedEnemy, getEnemyInfo),
                        Expression.Constant(null, typeof(EnemyInfo))),
                    enemy).Compile();

                harmony.Patch(
                    getAimTarget,
                    prefix: new HarmonyMethod(
                        typeof(FollowerSainAimTargetPatch).GetMethod(
                            nameof(UseFollowerAimTarget), BindingFlags.Static | BindingFlags.NonPublic)));
                // HarmonyX executes SAIN's prefix even when our earlier EnemyInfo prefix returns
                // false. Guard the competing selector itself so it cannot replace our point or
                // LastPartToShoot. Reusing the policy preserves its existing selection timer.
                harmony.Patch(
                    bodyPartPrefix,
                    prefix: new HarmonyMethod(
                        typeof(FollowerSainAimTargetPatch).GetMethod(
                            nameof(UseFollowerEnemyInfoTarget), BindingFlags.Static | BindingFlags.NonPublic)));
                Modules.Logger.LogInfo("[SAIN] Follower aim-target routing applied (shoot path and EnemyInfo prefix).");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower aim-target patch could not be applied: {ex}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool UseFollowerEnemyInfoTarget(ref Vector3 __0, EnemyInfo __1, ref bool __result)
        {
            try
            {
                if (!FollowerAimTargetPolicy.TrySelectFollowerShootPoint(
                        __1, out Vector3 shootPoint, out bool hasShootPoint) || !hasShootPoint)
                {
                    return true;
                }

                __0 = shootPoint;
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool UseFollowerAimTarget(object __0, ref Vector3? __result)
        {
            try
            {
                if (__0 == null || _getVisibleEnemyInfo == null ||
                    !FollowerAimTargetPolicy.TrySelectFollowerShootPoint(
                        _getVisibleEnemyInfo(__0), out Vector3 shootPoint, out bool hasShootPoint))
                {
                    return true;
                }

                // Null is deliberate when no verified follower lane exists; do not fall through
                // to SAIN's weighted/center-mass selector and manufacture a different target.
                __result = hasShootPoint ? shootPoint : (Vector3?)null;
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}