using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.Patches
{
    // General manual-fire compatibility covers every follower action, without an addon dependency.
    internal static class FollowerSainManualFireSafety
    {
        private static object _suppressReason;
        private static Func<BotOwner, object> _getManual;
        private static Func<object, Vector3> _manualTarget;
        private static Func<object, bool> _manualActive;
        private static Func<object, bool> _manualSuppressing;
        private static Action<object> _stopManual;
        private static Action<object> _stopSuppression;

        internal static void Apply(Harmony harmony)
        {
            Type botType = Type.GetType("SAIN.Components.BotComponent, SAIN");
            if (botType == null) return;
            Type manualType = AccessTools.Property(botType, "ManualShoot").PropertyType;
            PropertyInfo reason = AccessTools.Property(manualType, "Reason");
            _suppressReason = Enum.Parse(reason.PropertyType, "Suppress");
            var input = Expression.Parameter(typeof(object), "manual");
            var manual = Expression.Convert(input, manualType);
            Expression activeReason = Expression.Property(manual, reason);
            Expression suppressing = Expression.Equal(activeReason, Expression.Constant(_suppressReason, reason.PropertyType));
            _manualTarget = Expression.Lambda<Func<object, Vector3>>(
                Expression.Property(manual, "ShootPosition"), input).Compile();
            _manualActive = Expression.Lambda<Func<object, bool>>(Expression.NotEqual(activeReason,
                Expression.Constant(Enum.Parse(reason.PropertyType, "None"), reason.PropertyType)), input).Compile();
            _manualSuppressing = Expression.Lambda<Func<object, bool>>(suppressing, input).Compile();
            _stopSuppression = Expression.Lambda<Action<object>>(
                Expression.Call(Expression.Property(Expression.Property(manual, "Bot"), "Suppression"), "ResetSuppressing", Type.EmptyTypes), input).Compile();
            _stopManual = Expression.Lambda<Action<object>>(Expression.IfThenElse(suppressing,
                Expression.Call(Expression.Property(Expression.Property(manual, "Bot"), "Suppression"), "ResetSuppressing", Type.EmptyTypes),
                Expression.Call(manual, "Reset", Type.EmptyTypes)), input).Compile();

            MethodInfo getSain = AccessTools.Method(Type.GetType("SAIN.SAINEnableClass, SAIN"), "GetSAIN",
                new[] { typeof(string), botType.MakeByRefType() });
            var owner = Expression.Parameter(typeof(BotOwner), "owner");
            var bot = Expression.Variable(botType, "bot");
            _getManual = Expression.Lambda<Func<BotOwner, object>>(Expression.Block(new[] { bot },
                Expression.Condition(Expression.AndAlso(
                    Expression.Call(getSain, Expression.Property(owner, nameof(BotOwner.ProfileId)), bot),
                    Expression.NotEqual(bot, Expression.Constant(null, botType))),
                    Expression.Convert(Expression.Property(bot, "ManualShoot"), typeof(object)),
                    Expression.Constant(null, typeof(object)))), owner).Compile();

            MethodInfo tryShoot = AccessTools.GetDeclaredMethods(manualType).Single(m => m.Name == "TryShoot" && m.GetParameters().Length == 4);
            harmony.Patch(tryShoot, prefix: new HarmonyMethod(AccessTools.Method(typeof(FollowerSainManualFireSafety),
                nameof(CheckManualFire)).MakeGenericMethod(reason.PropertyType)) { priority = Priority.First });
        }

        private static bool CheckManualFire<T>(object __instance, Vector3 __1, T __3, ref bool __result)
        {
            bool blocked = false;
            try
            {
                BotOwner owner = SainBotOwnerAccessor.Get(__instance);
                if (owner == null || !BossPlayers.IsFollower(owner) ||
                    !FollowerFireSafety.IsUnsafe(owner, __1, EqualityComparer<T>.Default.Equals(__3, (T)_suppressReason))) return true;
                blocked = true;
                __result = false;
                FollowerFireSafety.Stop(owner);
                // Reason is assigned only after TryShoot starts the trigger, so
                // also reset this instance when blocking a first manual attempt.
                if (EqualityComparer<T>.Default.Equals(__3, (T)_suppressReason)) _stopSuppression(__instance);
                else _stopManual(__instance);
                return false;
            }
            catch { return !blocked; }
        }

        internal static bool TryGetManualTarget(BotOwner owner, out Vector3 target, out bool suppression)
        {
            target = Vector3.zero;
            suppression = false;
            try
            {
                object manual = owner == null ? null : _getManual?.Invoke(owner);
                if (manual == null || !_manualActive(manual)) return false;
                target = _manualTarget(manual);
                suppression = _manualSuppressing(manual);
                return true;
            }
            catch { return false; } // Optional state failure must not disable Core's muzzle check.
        }

        internal static void StopManualFire(BotOwner owner)
        {
            object manual = owner == null ? null : _getManual?.Invoke(owner);
            if (manual != null && _manualActive(manual)) _stopManual(manual);
        }

    }
}
