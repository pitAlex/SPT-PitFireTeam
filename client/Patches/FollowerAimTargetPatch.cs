using EFT;
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
    /// Lets EFT or SAIN choose the native visible part first, then gives follower Precision one
    /// bounded opportunity to promote a non-head choice to a verified head lane.
    /// </summary>
    internal sealed class FollowerAimTargetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.GetVisiblePartToShoot));
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PatchPostfix(EnemyInfo __instance, ref Vector3 __result)
        {
            try
            {
                EnemyPart? nativePart = __instance.LastPartToShoot;
                bool nativeSelectedHead = nativePart?.BodyPartType == BodyPartType.head;
                if (FollowerSainAimTargetPatch.TryDeferEnemyInfoSelection(
                        __instance,
                        nativeSelectedHead))
                {
                    return;
                }

                if (FollowerAimTargetPolicy.TryEnhanceFollowerShootPoint(
                        __instance,
                        __result,
                        nativeSelectedHead,
                        nativePart,
                        out Vector3 enhancedPoint))
                {
                    __result = enhancedPoint;
                }
            }
            catch
            {
                // Fail open: retain the point already selected by EFT/SAIN.
            }
        }
    }

    internal static class FollowerSainAimTargetPatch
    {
        [ThreadStatic]
        private static EnemyInfo? _activeEnemyInfoSelection;

        [ThreadStatic]
        private static bool _nestedNativeSelectionKnown;

        [ThreadStatic]
        private static bool _nestedNativeSelectedHead;

        private static Func<object, EnemyInfo?>? _getEnemyInfo;
        private static Func<object, bool>? _isNativeHeadSelected;

        private readonly struct SainAimTargetScope
        {
            internal SainAimTargetScope(
                bool active,
                EnemyInfo? previousEnemyInfo,
                bool previousSelectionKnown,
                bool previousSelectedHead)
            {
                Active = active;
                PreviousEnemyInfo = previousEnemyInfo;
                PreviousSelectionKnown = previousSelectionKnown;
                PreviousSelectedHead = previousSelectedHead;
            }

            internal bool Active { get; }
            internal EnemyInfo? PreviousEnemyInfo { get; }
            internal bool PreviousSelectionKnown { get; }
            internal bool PreviousSelectedHead { get; }
        }

        internal static bool TryDeferEnemyInfoSelection(
            EnemyInfo enemyInfo,
            bool nativeSelectedHead)
        {
            if (!ReferenceEquals(_activeEnemyInfoSelection, enemyInfo))
            {
                return false;
            }

            _nestedNativeSelectionKnown = true;
            _nestedNativeSelectedHead = nativeSelectedHead;
            return true;
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type? shootDataType = Type.GetType("SAIN.SAINComponent.Classes.SAINShootData, SAIN");
                Type? enemyType = shootDataType?.Assembly.GetType(
                    "SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
                MethodInfo? getAimTarget = shootDataType != null
                    ? AccessTools.Method(shootDataType, "GetAimTarget")
                    : null;
                MethodInfo? getEnemyInfo = enemyType != null
                    ? AccessTools.PropertyGetter(enemyType, "EnemyInfo")
                    : null;
                ParameterInfo[]? parameters = getAimTarget?.GetParameters();
                if (getAimTarget == null ||
                    !getAimTarget.IsStatic ||
                    getAimTarget.ReturnType != typeof(Vector3?) ||
                    parameters == null ||
                    parameters.Length < 1 ||
                    parameters.Length > 2 ||
                    parameters[0].ParameterType != enemyType ||
                    getEnemyInfo == null ||
                    getEnemyInfo.IsStatic ||
                    getEnemyInfo.ReturnType != typeof(EnemyInfo))
                {
                    Modules.Logger.LogError("[SAIN] Follower aim-target enhancement skipped: unsupported native selector layout.");
                    return;
                }

                ParameterExpression enemy = Expression.Parameter(typeof(object), "enemy");
                UnaryExpression typedEnemy = Expression.Convert(enemy, enemyType!);
                _getEnemyInfo = Expression.Lambda<Func<object, EnemyInfo?>>(
                    Expression.Call(typedEnemy, getEnemyInfo),
                    enemy).Compile();
                _isNativeHeadSelected = TryCompileNativeHeadAccessor(enemyType!);

                harmony.Patch(
                    getAimTarget,
                    prefix: new HarmonyMethod(
                        typeof(FollowerSainAimTargetPatch).GetMethod(
                            nameof(BeginSainAimTarget), BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(
                        typeof(FollowerSainAimTargetPatch).GetMethod(
                            nameof(EndSainAimTarget), BindingFlags.Static | BindingFlags.NonPublic)));
                Modules.Logger.LogInfo("[SAIN] Follower body-first aim selection and Precision head enhancement applied.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower aim-target enhancement could not be applied: {ex}");
            }
        }

        private static Func<object, bool>? TryCompileNativeHeadAccessor(Type enemyType)
        {
            MethodInfo? getAimTarget = AccessTools.PropertyGetter(enemyType, "AimTarget");
            Type? aimTargetType = getAimTarget?.ReturnType;
            MethodInfo? getChosenPart = aimTargetType != null
                ? AccessTools.PropertyGetter(aimTargetType, "ChosenPart")
                : null;
            Type? chosenPartType = getChosenPart?.ReturnType;
            Type? chosenPartValueType = chosenPartType != null
                ? Nullable.GetUnderlyingType(chosenPartType)
                : null;
            if (getAimTarget == null ||
                getAimTarget.IsStatic ||
                getChosenPart == null ||
                getChosenPart.IsStatic ||
                chosenPartValueType == null ||
                !chosenPartValueType.IsEnum ||
                !Enum.IsDefined(chosenPartValueType, "Head"))
            {
                // SAIN 4.5.0 has no weighted AimTarget object; EnemyInfo supplies its choice.
                return null;
            }

            ParameterExpression enemy = Expression.Parameter(typeof(object), "enemy");
            UnaryExpression typedEnemy = Expression.Convert(enemy, enemyType);
            MethodCallExpression aimTarget = Expression.Call(typedEnemy, getAimTarget);
            ParameterExpression chosenPart = Expression.Variable(chosenPartType!, "chosenPart");
            BinaryExpression captureChosenPart = Expression.Assign(
                chosenPart,
                Expression.Call(aimTarget, getChosenPart));
            MemberExpression hasChosenPart = Expression.Property(chosenPart, "HasValue");
            MemberExpression chosenPartValue = Expression.Property(chosenPart, "Value");
            object headValue = Enum.Parse(chosenPartValueType, "Head");
            return Expression.Lambda<Func<object, bool>>(
                Expression.Block(
                    new[] { chosenPart },
                    captureChosenPart,
                    Expression.AndAlso(
                        hasChosenPart,
                        Expression.Equal(
                            chosenPartValue,
                            Expression.Constant(headValue, chosenPartValueType)))),
                enemy).Compile();
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void BeginSainAimTarget(object __0, out SainAimTargetScope __state)
        {
            __state = default;
            try
            {
                if (__0 == null || _getEnemyInfo == null)
                {
                    return;
                }

                EnemyInfo? enemyInfo = _getEnemyInfo(__0);
                BotOwner? botOwner = enemyInfo?.Owner;
                if (botOwner == null ||
                    !FollowerProficiency.TryGetValues(botOwner, out FollowerProficiencyValues? proficiency) ||
                    proficiency == null)
                {
                    return;
                }

                __state = new SainAimTargetScope(
                    true,
                    _activeEnemyInfoSelection,
                    _nestedNativeSelectionKnown,
                    _nestedNativeSelectedHead);
                _activeEnemyInfoSelection = enemyInfo;
                _nestedNativeSelectionKnown = false;
                _nestedNativeSelectedHead = false;
            }
            catch
            {
                __state = default;
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void EndSainAimTarget(
            object __0,
            ref Vector3? __result,
            SainAimTargetScope __state)
        {
            try
            {
                EnemyInfo? enemyInfo = _activeEnemyInfoSelection;
                if (!__state.Active || !__result.HasValue || enemyInfo == null)
                {
                    return;
                }

                bool nativeSelectedHead = _nestedNativeSelectionKnown
                    ? _nestedNativeSelectedHead
                    : _isNativeHeadSelected?.Invoke(__0) == true;
                EnemyPart? nativePart = null;
                Vector3 nativePoint = __result.Value;
                if (FollowerAimTargetPolicy.TryGetBodyFirstShootPoint(
                        enemyInfo, out EnemyPart? bodyPart, out Vector3 bodyPoint))
                {
                    nativePart = bodyPart;
                    nativePoint = bodyPoint;
                    nativeSelectedHead = false;
                }
                else if (nativeSelectedHead &&
                    enemyInfo._allParts.TryGetValue(BodyPartType.head, out nativePart) &&
                    _nestedNativeSelectionKnown)
                {
                    // SAIN 4.5.0 can lower an already selected head to its global center-mass
                    // height after EnemyInfo returns. Preserve the native part, not that clamp.
                    nativePoint = nativePart.GetPartPositionWithOffset();
                }
                else if (_nestedNativeSelectionKnown)
                {
                    nativePart = enemyInfo.LastPartToShoot;
                }

                if (FollowerAimTargetPolicy.TryEnhanceFollowerShootPoint(
                        enemyInfo,
                        nativePoint,
                        nativeSelectedHead,
                        nativePart,
                        out Vector3 enhancedPoint))
                {
                    __result = enhancedPoint;
                }
            }
            catch
            {
                // Fail open: retain SAIN's native point.
            }
            finally
            {
                if (__state.Active)
                {
                    _activeEnemyInfoSelection = __state.PreviousEnemyInfo;
                    _nestedNativeSelectionKnown = __state.PreviousSelectionKnown;
                    _nestedNativeSelectedHead = __state.PreviousSelectedHead;
                }
            }
        }
    }
}
