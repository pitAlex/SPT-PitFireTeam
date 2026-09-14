using System;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.BigBrain;

namespace pitTeam.Modules
{
    // Optional-mod interception only. The addon supplies follower-local cover policy;
    // native SAIN retains its cover state, action lifecycle and movement execution.
    public static class SainCoverSelectionBridge
    {
        public delegate bool Selector(BotOwner owner, bool sprint, out object point);
        public delegate void SelectionObserver(BotOwner owner, object point);
        private static Selector _selector;
        private static SelectionObserver _observer;
        private static Func<object, BotOwner> _owner;
        private static Func<object, bool> _sprint;
        private static bool _reported;
        public static bool IsAvailable { get; private set; }
        public static float SearchRadius => CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius();
        public static float ArrivalHoldSeconds(bool recovery) =>
            FollowerCombatCommon.GetCommittedCoverHoldDuration(recovery ? "retreatSafeCover" : "bossCover");
        public static float Score(float pathDistance, float bossDistance) =>
            FollowerCombatCommon.ScoreBossCover(pathDistance, bossDistance);

        internal static void Apply(Harmony harmony)
        {
            if (IsAvailable) return;
            MethodInfo method = null, prefix = null, postfix = null;
            try
            {
                Type type = Type.GetType("SAIN.SAINComponent.Classes.SAINCoverClass, SAIN", true);
                Type point = Type.GetType("SAIN.SAINComponent.SubComponents.CoverFinder.CoverPoint, SAIN", true);
                method = AccessTools.Method(type, "FindCoverPoint");
                FieldInfo sprint = AccessTools.Field(type, "_shallSprint");
                PropertyInfo owner = AccessTools.Property(type, "BotOwner");
                if (method == null || method.IsStatic || method.GetParameters().Length != 0 || method.ReturnType != point ||
                    sprint?.FieldType != typeof(bool) || owner?.PropertyType != typeof(BotOwner))
                    throw new MissingMemberException("SAIN cover selection boundary changed.");
                var instance = Expression.Parameter(typeof(object), "instance");
                var converted = Expression.Convert(instance, type);
                _owner = Expression.Lambda<Func<object, BotOwner>>(Expression.Property(converted, owner), instance).Compile();
                _sprint = Expression.Lambda<Func<object, bool>>(Expression.Field(converted, sprint), instance).Compile();
                prefix = AccessTools.Method(typeof(SainCoverSelectionBridge), nameof(Select)).MakeGenericMethod(point);
                postfix = AccessTools.Method(typeof(SainCoverSelectionBridge), nameof(Observe)).MakeGenericMethod(point);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                if (method != null && prefix != null) harmony.Unpatch(method, prefix);
                if (method != null && postfix != null) harmony.Unpatch(method, postfix);
                Logger.LogError($"[SAIN] Cover selection bridge unavailable; addon combat will remain on core fallback. {ex}");
            }
        }

        public static void Register(Selector selector, SelectionObserver observer)
        {
            if (!IsAvailable) throw new InvalidOperationException("SAIN cover selection bridge is unavailable.");
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }
        public static void Unregister(Selector selector, SelectionObserver observer)
        {
            if (_selector == selector) _selector = null;
            if (_observer == observer) _observer = null;
        }
        private static bool Select<T>(object __instance, ref T __result) where T : class
        {
            if (_selector == null) return true;
            try
            {
                BotOwner owner = _owner(__instance);
                if (!pitFireTeam.UseSainFollowerCombat(owner) || !_selector(owner, _sprint(__instance), out object point)) return true;
                __result = (T)point;
                return false;
            }
            catch (Exception ex) { Report(ex); return true; }
        }
        private static void Observe<T>(object __instance, T __result) where T : class
        {
            if (_observer == null || __result == null) return;
            try
            {
                BotOwner owner = _owner(__instance);
                if (pitFireTeam.UseSainFollowerCombat(owner)) _observer(owner, __result);
            }
            catch (Exception ex) { Report(ex); }
        }
        private static void Report(Exception ex)
        {
            if (_reported) return;
            _reported = true;
            Logger.LogError($"[SAIN] Follower cover callback failed; retaining native cover fallback. {ex}");
        }
    }
}
