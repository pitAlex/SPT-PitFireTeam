using System;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent.Classes.Mover;

namespace pitTeam.SAINAddon;

// SAIN updates Lean even outside its layers while it retains a native enemy.
// Core patrol movement (including runToHeal) uses a different mover, so native
// Lean cannot infer that the follower is running or that combat has yielded.
internal static class SainFollowerLeanGuard
{
    internal static void Apply(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(LeanClass), "CheckCanLeanByState",
            new[] { typeof(bool).MakeByRefType() });
        if (method?.ReturnType != typeof(bool) || method.IsStatic)
            throw new MissingMethodException("SAIN lean state boundary changed.");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(SainFollowerLeanGuard), nameof(BeforeCheck)));
    }

    private static bool BeforeCheck(LeanClass __instance, ref bool __result, ref bool __0)
    {
        BotComponent bot = __instance.Bot;
        if (bot == null || bot.SAINLayersActive || !SainAddonBridge.IsAddonTacticSelected(bot.BotOwner))
            return true;
        __result = false;
        __0 = true; // Native UpdateLeanSetting releases a held/retained lean.
        return false;
    }
}
