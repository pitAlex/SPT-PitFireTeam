using EFT;
using EFT.Ballistics;
using HarmonyLib;
using pitTeam.Modules;
using System;
using System.Reflection;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerHitAccuracyPatch
    {
        private static PropertyInfo? _botOwnerProperty;

        public static void Apply(Harmony harmony)
        {
            Type? aimHitEffectType = AccessTools.TypeByName("SAIN.SAINComponent.Classes.AimHitEffectClass");
            if (aimHitEffectType == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN AimHitEffectClass for follower hit-accuracy patch.");
                return;
            }

            MethodInfo? target = AccessTools.Method(aimHitEffectType, "GetHit", new[] { typeof(DamageInfo) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find AimHitEffectClass.GetHit for follower hit-accuracy patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerHitAccuracyPatch), nameof(Prefix_GetHit)));
            Modules.Logger.LogInfo("[Init] SAIN follower hit-accuracy patch applied.");
        }

        private static bool Prefix_GetHit(object __instance)
        {
            try
            {
                if (__instance == null) return true;

                _botOwnerProperty ??= AccessTools.Property(__instance.GetType(), "BotOwner");
                BotOwner? owner = _botOwnerProperty?.GetValue(__instance) as BotOwner;
                if (owner == null) return true;

                // Followers should not receive SAIN aim hit-effect penalty.
                if (BossPlayers.IsFollower(owner))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower hit-accuracy patch.");
                Modules.Logger.LogError(ex);
            }

            return true;
        }
    }
}
