using System;
using FollowerStimulatorPolicy = pitTeam.Utils.FollowerStimulatorPolicy;
using HarmonyLib;
using System.Runtime.CompilerServices;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace pitTeam.SAINAddon;

// The follower brain extends medical safety and shares Core stim selection.
// Native timing, decision publication and medicine execution still run once.
internal static class SainMedicalDecisionBridge
{
    private static Action<BotSurgery, bool> setAreaClear;
    private static Func<Enemy, bool> nativeStimSafety;
    private static ConditionalWeakTable<BotComponent, FollowerStimulatorPolicy> stimulators = new();
    private static bool reported, installed;
    private sealed class AreaState { internal bool Original, Published; }
    private static ConditionalWeakTable<BotSurgery, AreaState> areas = new();
    internal static void Apply(Harmony harmony)
    {
        if (installed) return;
        var firstAid = AccessTools.Method(typeof(SelfActionDecisionClass), "ShallFirstAidCheckEnemy", new[] { typeof(Enemy) });
        var surgery = AccessTools.Method(typeof(BotSurgery), "CheckEnemies", Type.EmptyTypes);
        var area = AccessTools.Method(typeof(BotSurgery), nameof(BotSurgery.CheckAreaClearForSurgery), Type.EmptyTypes);
        var setter = AccessTools.PropertySetter(typeof(BotSurgery), nameof(BotSurgery.AreaClearForSurgery));
        var startStims = AccessTools.Method(typeof(SelfActionDecisionClass), "startUseStims", Type.EmptyTypes);
        var stimSafety = AccessTools.Method(typeof(SelfActionDecisionClass), "ShallUseStimsCheckEnemy", new[] { typeof(Enemy) });
        if (firstAid?.ReturnType != typeof(bool) || surgery?.ReturnType != typeof(bool) ||
            area?.ReturnType != typeof(bool) || setter == null || startStims?.ReturnType != typeof(bool) ||
            startStims.IsStatic || stimSafety?.ReturnType != typeof(bool) || !stimSafety.IsStatic)
            throw new MissingMemberException("SAIN addon medical boundary changed.");
        nativeStimSafety = (Func<Enemy, bool>)Delegate.CreateDelegate(typeof(Func<Enemy, bool>), stimSafety);
        setAreaClear = (Action<BotSurgery, bool>)Delegate.CreateDelegate(typeof(Action<BotSurgery, bool>), setter);
        harmony.Patch(firstAid, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(FirstAid)));
        harmony.Patch(surgery, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(Surgery)));
        harmony.Patch(area, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(PublishArea)));
        harmony.Patch(startStims, prefix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(StartStims)));
        installed = true;
    }
    internal static void Reset() { setAreaClear = null; nativeStimSafety = null; reported = installed = false; areas = new(); stimulators = new(); }
    internal static void Restore(BotComponent bot)
    {
        RestoreArea(bot?.Medical?.Surgery);
        if (bot != null) stimulators.Remove(bot);
    }
    private static void RestoreArea(BotSurgery surgery)
    {
        if (surgery == null || !areas.TryGetValue(surgery, out AreaState state)) return;
        try { if (surgery.AreaClearForSurgery == state.Published) setAreaClear?.Invoke(surgery, state.Original); }
        catch (Exception ex) { Report(ex); }
        areas.Remove(surgery);
    }
    private static bool Ready(BotComponent bot) => pitFireTeam.UseSainFollowerCombat(bot.BotOwner) &&
        SAINFollowerCombatHandoff.AllowsEnemyCombat(bot.BotOwner);
    private static bool Safe(BotComponent bot)
    {
        try { return Ready(bot) && SAINFollowerRuntime.GetCover(bot.BotOwner)?.CanTreatAtCover() == true; }
        catch (Exception ex) { Report(ex); return false; }
    }
    private static void FirstAid(SelfActionDecisionClass __instance, ref bool __result)
    {
        if (!__result && Safe(__instance.Bot)) __result = true;
    }
    private static bool StartStims(SelfActionDecisionClass __instance, ref bool __result)
    {
        try
        {
            BotComponent bot = __instance.Bot;
            if (!Ready(bot) || bot.BotOwner.Medecine?.Using == true ||
                !FollowerStimulatorPolicy.CanUseStimulatorNow(bot.BotOwner, out var stims)) return true;

            bool stomachPain = FollowerStimulatorPolicy.NeedsBlackStomachPainRelief(bot.BotOwner);
            bool health = bot.Memory.Health.Dying || bot.Memory.Health.BadlyInjured;
            // Inspect advertised surgery work; do not run its provider to decide on a stim.
            bool limbPain = FollowerStimulatorPolicy.ShouldUsePainStimForDestroyedPartAtHealCover(bot.BotOwner, refreshSurgeryPart: false);
            if ((!stomachPain && !health && !limbPain) || !CanUseStimsSafely(bot)) return true;

            var selection = stimulators.GetValue(bot, _ => new FollowerStimulatorPolicy());
            if ((stomachPain && selection.TrySelectPainStimulator(bot.BotOwner, stims, refreshOnMiss: false)) ||
                (health && selection.TrySelectPositiveHealthRateStimulator(bot.BotOwner, stims, refreshOnMiss: false)) ||
                (limbPain && selection.TrySelectPainStimulator(bot.BotOwner, stims, refreshOnMiss: false)))
            {
                __result = true;
                return false;
            }
        }
        catch (Exception ex) { Report(ex); }
        // A miss preserves native selection, including its cached item.
        return true;
    }
    private static bool CanUseStimsSafely(BotComponent bot)
    {
        if (bot.EnemyController.AtPeace || bot.Decision.RunningToCover) return true;
        foreach (Enemy enemy in bot.EnemyController.KnownEnemies)
            if (!nativeStimSafety(enemy)) return Safe(bot);
        return true;
    }
    private static void Surgery(BotSurgery __instance, ref bool __result)
    {
        if (!__result && Safe(__instance.Bot)) __result = true;
    }
    private static void PublishArea(BotSurgery __instance, bool __result)
    {
        // Installed 4.5.1 returns this result but never assigns the flag consumed by
        // its continuation check/action. Publish the actual result for our followers.
        try
        {
            if (!pitFireTeam.UseSainFollowerCombat(__instance.BotOwner)) { RestoreArea(__instance); return; }
            var state = areas.GetValue(__instance, surgery => new AreaState { Original = surgery.AreaClearForSurgery });
            setAreaClear(__instance, __result); state.Published = __result;
        }
        catch (Exception ex) { Report(ex); }
    }
    private static void Report(Exception ex)
    {
        if (reported) return;
        reported = true;
        pitTeam.Modules.Logger.LogError($"[SAIN] Follower recovery check failed; retaining native medicine policy. {ex}");
    }
}
