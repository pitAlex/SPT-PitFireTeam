using System;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace pitTeam.Modules
{
    // Core owns optional-mod interception; the addon owns the selected follower's policy.
    public static class SainSquadDecisionBridge
    {
        // true = handled, including decision=None; false = use the native provider.
        public delegate bool Provider(BotOwner owner, object enemy, out int decision);
        // Inspect one already-calculated result; the addon supplies a follower-local replacement.
        public delegate bool CombatFallbackProvider(BotOwner owner, object enemy, int solo, int squad, int self, out int nextSolo, out int nextSquad);
        private static Provider _provider;
        private static CombatFallbackProvider _combatFallback;
        private static Func<object, BotOwner> _getOwner, _getManagerOwner;
        private static object _none;
        public static bool IsAvailable { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (IsAvailable) return;
            MethodInfo method = null, publish = null, dispatch = null, filter = null;
            try
            {
                Type type = Type.GetType("SAIN.SAINComponent.Classes.Decision.SquadDecisionClass, SAIN", true);
                Type enemy = Type.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy, SAIN", true);
                method = AccessTools.Method(type, "GetDecision");
                var parameters = method?.GetParameters();
                if (method == null || method.ReturnType != typeof(bool) || parameters.Length != 2 ||
                    !parameters[0].IsOut || !parameters[0].ParameterType.IsByRef || parameters[1].ParameterType != enemy)
                    throw new MissingMethodException("SAIN squad decision provider changed.");
                Type decision = parameters[0].ParameterType.GetElementType();
                Type manager = Type.GetType("SAIN.SAINComponent.Classes.Decision.BotDecisionManager, SAIN", true);
                publish = AccessTools.Method(manager, "SetDecisions");
                var inputs = publish?.GetParameters();
                if (publish == null || publish.IsStatic || publish.ReturnType != typeof(void) || inputs.Length != 4 ||
                    inputs[0].ParameterType.FullName != "SAIN.Preset.Shared.Enums.ECombatDecision" ||
                    inputs[1].ParameterType != decision ||
                    inputs[2].ParameterType.FullName != "SAIN.Preset.Shared.Enums.ESelfActionType" || inputs[3].ParameterType != enemy)
                    throw new MissingMethodException("SAIN decision publisher changed.");
                // Enum types live in SAIN.Preset.Shared; resolve them from the verified API.
                Type combat = inputs[0].ParameterType;
                Type self = inputs[2].ParameterType;
                _none = Enum.Parse(decision, "None");
                _getOwner = OwnerGetter(type);
                _getManagerOwner = OwnerGetter(manager);
                dispatch = AccessTools.Method(typeof(SainSquadDecisionBridge), nameof(Dispatch)).MakeGenericMethod(decision);
                filter = AccessTools.Method(typeof(SainSquadDecisionBridge), nameof(Filter)).MakeGenericMethod(combat, decision, self);
                harmony.Patch(method, prefix: new HarmonyMethod(dispatch));
                harmony.Patch(publish, prefix: new HarmonyMethod(filter));
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                // Roll back only this bridge if either half cannot be installed.
                if (method != null && dispatch != null) harmony.Unpatch(method, dispatch);
                if (publish != null && filter != null) harmony.Unpatch(publish, filter);
                Logger.LogError($"[SAIN] Squad decision bridge unavailable; addon combat will remain on core fallback. {ex}");
            }
        }

        private static Func<object, BotOwner> OwnerGetter(Type type)
        {
            PropertyInfo owner = AccessTools.Property(type, "BotOwner") ?? throw new MissingMemberException("SAIN BotOwner");
            var instance = Expression.Parameter(typeof(object), "instance");
            return Expression.Lambda<Func<object, BotOwner>>(
                Expression.Property(Expression.Convert(instance, type), owner), instance).Compile();
        }

        public static void Register(Provider provider, CombatFallbackProvider combatFallback)
        {
            if (!IsAvailable) throw new InvalidOperationException("SAIN squad decision bridge is unavailable.");
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (combatFallback == null) throw new ArgumentNullException(nameof(combatFallback));
            _provider = provider;
            _combatFallback = combatFallback;
        }

        public static void Unregister(Provider provider, CombatFallbackProvider combatFallback)
        {
            if (_provider == provider) _provider = null;
            if (_combatFallback == combatFallback) _combatFallback = null;
        }

        private static bool Dispatch<T>(object __instance, ref T __0, object __1, ref bool __result)
        {
            if (_provider == null) return true;
            BotOwner owner = _getOwner(__instance);
            if (!pitFireTeam.UseSainFollowerCombat(owner) || !_provider(owner, __1, out int decision)) return true;
            __0 = (T)Enum.ToObject(typeof(T), decision);
            __result = !Equals(__0, _none);
            return false;
        }

        private static void Filter<TCombat, TSquad, TSelf>(object __instance, ref TCombat __0, ref TSquad __1, TSelf __2, object __3)
        {
            if (_combatFallback == null || __3 == null) return; // Native resets must stay resets.
            BotOwner owner = _getManagerOwner(__instance);
            if (!pitFireTeam.UseSainFollowerCombat(owner) ||
                !_combatFallback(owner, __3, Convert.ToInt32(__0), Convert.ToInt32(__1), Convert.ToInt32(__2), out int solo, out int squad)) return;
            __0 = (TCombat)Enum.ToObject(typeof(TCombat), solo);
            __1 = (TSquad)Enum.ToObject(typeof(TSquad), squad);
            // Always run native SetDecisions: it owns previous/current state, timing and events.
        }
    }
}
