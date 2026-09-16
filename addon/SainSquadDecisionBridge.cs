using System;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace pitTeam.SAINAddon
{
    // Addon-only interception. Native SAIN still publishes state, timing and events once.
    internal static class SainSquadDecisionBridge
    {
        public delegate bool Provider(BotOwner owner, Enemy enemy, out ESquadDecision decision);
        public delegate bool CombatFallbackProvider(BotOwner owner, Enemy enemy, ECombatDecision solo,
            ESquadDecision squad, ESelfActionType self, out ECombatDecision nextSolo, out ESquadDecision nextSquad);
        private static Provider _provider;
        private static CombatFallbackProvider _combatFallback;
        private static Func<BotOwner, Enemy, Enemy> _enemyPreference;
        public static bool IsAvailable { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (IsAvailable) return;
            var provider = AccessTools.Method(typeof(SquadDecisionClass), nameof(SquadDecisionClass.GetDecision),
                new[] { typeof(ESquadDecision).MakeByRefType(), typeof(Enemy) });
            var publish = AccessTools.Method(typeof(BotDecisionManager), "SetDecisions",
                new[] { typeof(ECombatDecision), typeof(ESquadDecision), typeof(ESelfActionType), typeof(Enemy) });
            var select = AccessTools.Method(typeof(SAINEnemyController), "SelectEnemy", Type.EmptyTypes);
            if (provider?.ReturnType != typeof(bool) || publish?.ReturnType != typeof(void) || select?.ReturnType != typeof(Enemy))
                throw new MissingMethodException("SAIN addon decision boundary changed.");
            harmony.Patch(provider, prefix: new HarmonyMethod(typeof(SainSquadDecisionBridge), nameof(Dispatch)));
            harmony.Patch(publish, prefix: new HarmonyMethod(typeof(SainSquadDecisionBridge), nameof(Filter)));
            harmony.Patch(select, postfix: new HarmonyMethod(typeof(SainSquadDecisionBridge), nameof(PreferEnemy)));
            IsAvailable = true;
        }

        internal static void Reset()
        {
            IsAvailable = false;
            _provider = null;
            _combatFallback = null;
            _enemyPreference = null;
        }
        public static void Register(Provider provider, CombatFallbackProvider combatFallback)
        {
            if (!IsAvailable) throw new InvalidOperationException("SAIN addon decision hooks unavailable.");
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _combatFallback = combatFallback ?? throw new ArgumentNullException(nameof(combatFallback));
        }
        public static void Unregister(Provider provider, CombatFallbackProvider combatFallback)
        {
            if (_provider == provider) _provider = null;
            if (_combatFallback == combatFallback) _combatFallback = null;
        }
        public static void RegisterEnemyPreference(Func<BotOwner, Enemy, Enemy> provider) => _enemyPreference = provider;
        public static void UnregisterEnemyPreference(Func<BotOwner, Enemy, Enemy> provider)
        {
            if (_enemyPreference == provider) _enemyPreference = null;
        }
        private static void PreferEnemy(SAINEnemyController __instance, ref Enemy __result)
        {
            if (_enemyPreference != null && __result != null && pitFireTeam.UseSainFollowerCombat(__instance.BotOwner))
                __result = _enemyPreference(__instance.BotOwner, __result) ?? __result;
        }
        private static bool Dispatch(SquadDecisionClass __instance, ref ESquadDecision __0, Enemy __1, ref bool __result)
        {
            if (_provider == null || !pitFireTeam.UseSainFollowerCombat(__instance.BotOwner) ||
                !_provider(__instance.BotOwner, __1, out ESquadDecision decision)) return true;
            __0 = decision;
            __result = decision != ESquadDecision.None;
            return false;
        }
        private static void Filter(BotDecisionManager __instance, ref ECombatDecision __0, ref ESquadDecision __1,
            ref ESelfActionType __2, Enemy __3)
        {
            if (_combatFallback == null || (__0 == ECombatDecision.None && __1 == ESquadDecision.None && __2 == ESelfActionType.None)) return;
            if (!pitFireTeam.UseSainFollowerCombat(__instance.BotOwner) ||
                !_combatFallback(__instance.BotOwner, __3, __0, __1, __2, out ECombatDecision solo, out ESquadDecision squad)) return;
            __0 = solo;
            __1 = squad;
            // Rejected combat cannot reopen medical ownership. Native all-None resets remain untouched.
            if (solo == ECombatDecision.None && squad == ESquadDecision.None) __2 = ESelfActionType.None;
        }
    }
}
