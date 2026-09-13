using System;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace pitTeam.Modules
{
    // Core owns the optional-mod interception; the addon owns the selected follower's calculator.
    public static class SainSquadDecisionBridge
    {
        // true = handled, including decision=None; false = use the native provider.
        public delegate bool Provider(BotOwner owner, object enemy, out int decision);
        private static Provider _provider;
        private static Func<object, BotOwner> _getOwner;
        private static object _none;
        public static bool IsAvailable { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (IsAvailable) return;
            try
            {
                Type type = Type.GetType("SAIN.SAINComponent.Classes.Decision.SquadDecisionClass, SAIN", true);
                Type enemy = Type.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy, SAIN", true);
                MethodInfo method = AccessTools.Method(type, "GetDecision");
                var parameters = method?.GetParameters();
                if (method == null || method.ReturnType != typeof(bool) || parameters.Length != 2 ||
                    !parameters[0].IsOut || !parameters[0].ParameterType.IsByRef || parameters[1].ParameterType != enemy)
                    throw new MissingMethodException("SAIN squad decision provider changed.");
                Type decision = parameters[0].ParameterType.GetElementType();
                _none = Enum.Parse(decision, "None");
                PropertyInfo owner = AccessTools.Property(type, "BotOwner") ?? throw new MissingMemberException("SAIN BotOwner");
                var instance = Expression.Parameter(typeof(object), "instance");
                _getOwner = Expression.Lambda<Func<object, BotOwner>>(
                    Expression.Property(Expression.Convert(instance, type), owner), instance).Compile();
                harmony.Patch(method, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(SainSquadDecisionBridge), nameof(Dispatch)).MakeGenericMethod(decision)));
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SAIN] Squad decision bridge unavailable; addon combat will remain on core fallback. {ex}");
            }
        }

        public static void Register(Provider provider)
        {
            if (!IsAvailable) throw new InvalidOperationException("SAIN squad decision bridge is unavailable.");
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static void Unregister(Provider provider)
        {
            if (_provider == provider) _provider = null;
        }

        private static bool Dispatch<T>(object __instance, ref T __0, object __1, ref bool __result)
        {
            BotOwner owner = _getOwner(__instance);
            if (_provider == null || !pitFireTeam.UseSainFollowerCombat(owner) ||
                !_provider(owner, __1, out int decision))
                return true;

            __0 = (T)Enum.ToObject(typeof(T), decision);
            __result = !Equals(__0, _none);
            return false;
        }
    }
}