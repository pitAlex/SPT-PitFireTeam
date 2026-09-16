using System;
using HarmonyLib;
using System.Runtime.CompilerServices;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace pitTeam.SAINAddon;

// Only the follower brain extends native enemy-safety checks. Native item selection,
// timing, decision publication and medicine execution still run once.
internal static class SainMedicalDecisionBridge
{
    private static Action<BotSurgery, bool> setAreaClear;
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
        if (firstAid?.ReturnType != typeof(bool) || surgery?.ReturnType != typeof(bool) ||
            area?.ReturnType != typeof(bool) || setter == null)
            throw new MissingMemberException("SAIN addon medical boundary changed.");
        setAreaClear = (Action<BotSurgery, bool>)Delegate.CreateDelegate(typeof(Action<BotSurgery, bool>), setter);
        harmony.Patch(firstAid, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(FirstAid)));
        harmony.Patch(surgery, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(Surgery)));
        harmony.Patch(area, postfix: new HarmonyMethod(typeof(SainMedicalDecisionBridge), nameof(PublishArea)));
        installed = true;
    }
    internal static void Reset() { setAreaClear = null; reported = installed = false; areas = new(); }
    internal static void Restore(BotComponent bot) => RestoreArea(bot?.Medical?.Surgery);
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
