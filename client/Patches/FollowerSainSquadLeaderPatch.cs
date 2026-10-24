using System;
using System.Linq.Expressions;
using EFT;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.Patches
{
    // Core followers must not become native AI squad leaders, including without the addon.
    internal static class FollowerSainSquadLeaderPatch
    {
        private static Func<object, BotOwner> _owner;
        internal static void Apply(Harmony harmony)
        {
            try
            {
                var squad = Type.GetType("SAIN.BotController.Classes.Squad, SAIN", true);
                var bot = Type.GetType("SAIN.Components.BotComponent, SAIN", true);
                var method = AccessTools.Method(squad, "assignSquadLeader", new[] { bot });
                var owner = AccessTools.Property(bot, "BotOwner");
                if (method?.ReturnType != typeof(void) || owner?.PropertyType != typeof(BotOwner))
                    throw new MissingMemberException("SAIN squad leader compatibility boundary changed.");
                var instance = Expression.Parameter(typeof(object));
                _owner = Expression.Lambda<Func<object, BotOwner>>(
                    Expression.Property(Expression.Convert(instance, bot), owner), instance).Compile();
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(FollowerSainSquadLeaderPatch), nameof(Prefix)));
            }
            catch (Exception ex) { Logger.LogError($"[SAIN] Core squad leader guard unavailable. {ex}"); }
        }
        private static bool Prefix(object __0)
        {
            if (__0 == null) return true;
            BotOwner owner = _owner(__0);
            return !BossPlayers.IsFollower(owner) || (SainAddonBridge.HasSquadProvider && SainAddonBridge.IsAddonTacticSelected(owner));
        }
    }
}
