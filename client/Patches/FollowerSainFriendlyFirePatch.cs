using System;
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
    // General compatibility belongs in core: SAIN's single-member fast path omits the human leader.
    internal static class FollowerSainFriendlyFirePatch
    {
        private static Func<object, BotOwner> _getOwner;
        private static object _friendlyBlock;

        internal static void Apply(Harmony harmony)
        {
            Type type = Type.GetType("SAIN.SAINComponent.Classes.SAINFriendlyFireClass, SAIN");
            if (type == null) return;
            MethodInfo[] methods = AccessTools.GetDeclaredMethods(type)
                .Where(m => m.Name == "CheckFriendlyFireStatus" && m.IsStatic && m.GetParameters().Length == 4).ToArray();
            if (methods.Length != 2) throw new MissingMethodException("SAIN friendly-fire overloads changed.");
            Type botType = methods[0].GetParameters()[3].ParameterType;
            PropertyInfo owner = AccessTools.Property(botType, "BotOwner") ?? throw new MissingMemberException("SAIN BotOwner");
            var instance = Expression.Parameter(typeof(object), "instance");
            _getOwner = Expression.Lambda<Func<object, BotOwner>>(
                Expression.Property(Expression.Convert(instance, botType), owner), instance).Compile();
            _friendlyBlock = Enum.Parse(methods[0].ReturnType, "FriendlyBlock");
            foreach (MethodInfo method in methods)
            {
                harmony.Patch(method, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(FollowerSainFriendlyFirePatch), nameof(CheckFollowerLane))
                        .MakeGenericMethod(method.ReturnType)));
            }
        }

        private static void CheckFollowerLane<T>(object[] __args, ref T __result)
        {
            BotOwner owner = _getOwner(__args[3]);
            if (owner == null || !BossPlayers.IsFollower(owner)) return;
            Vector3 origin = (Vector3)__args[1];
            Vector3 direction = (Vector3)__args[2];
            float distance = __args[0] is Vector3 target ? (target - origin).magnitude : (float)__args[0];
            if (FollowerShotSafety.IsFriendlyInShotLane(owner, origin, direction, distance))
                __result = (T)_friendlyBlock;
        }
    }
}