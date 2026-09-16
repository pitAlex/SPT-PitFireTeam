using System;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.SubComponents.CoverFinder;

namespace pitTeam.SAINAddon
{
    internal static class SainCoverSelectionBridge
    {
        public delegate bool Selector(BotOwner owner, bool sprint, out CoverPoint point);
        public delegate void SelectionObserver(BotOwner owner, CoverPoint point);
        private static Selector _selector;
        private static SelectionObserver _observer;
        private static bool _reported;
        public static bool IsAvailable { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (IsAvailable) return;
            var method = AccessTools.Method(typeof(SAINCoverClass), "FindCoverPoint", Type.EmptyTypes);
            var sprint = AccessTools.Field(typeof(SAINCoverClass), "_shallSprint");
            if (method?.ReturnType != typeof(CoverPoint) || sprint?.FieldType != typeof(bool))
                throw new MissingMemberException("SAIN addon cover boundary changed.");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(SainCoverSelectionBridge), nameof(Select)),
                postfix: new HarmonyMethod(typeof(SainCoverSelectionBridge), nameof(Observe)));
            IsAvailable = true;
        }
        internal static void Reset()
        {
            IsAvailable = false;
            _selector = null;
            _observer = null;
            _reported = false;
        }
        public static void Register(Selector selector, SelectionObserver observer)
        {
            if (!IsAvailable) throw new InvalidOperationException("SAIN addon cover hook unavailable.");
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }
        public static void Unregister(Selector selector, SelectionObserver observer)
        {
            if (_selector == selector) _selector = null;
            if (_observer == observer) _observer = null;
        }
        private static bool Select(SAINCoverClass __instance, bool ____shallSprint, ref CoverPoint __result)
        {
            if (_selector == null) return true;
            try
            {
                if (!pitFireTeam.UseSainFollowerCombat(__instance.BotOwner) ||
                    !_selector(__instance.BotOwner, ____shallSprint, out CoverPoint point)) return true;
                __result = point;
                return false;
            }
            catch (Exception ex) { Report(ex); return true; }
        }
        private static void Observe(SAINCoverClass __instance, CoverPoint __result)
        {
            if (_observer == null || __result == null) return;
            try
            {
                if (pitFireTeam.UseSainFollowerCombat(__instance.BotOwner)) _observer(__instance.BotOwner, __result);
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
