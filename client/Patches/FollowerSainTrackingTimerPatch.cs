using System;
using System.Reflection;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.Patches;

// Duration compatibility applies to followers even when the combat addon is absent.
internal static class FollowerSainTrackingTimerPatch
{
    private static FieldInfo? ForgetField;
    internal static void Apply(Harmony harmony)
    {
        var type = Type.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass, SAIN");
        var method = type == null ? null : AccessTools.Method(type, "CalcTimeBeforeSearch", Type.EmptyTypes);
        ForgetField = type == null ? null : AccessTools.Field(type, "<ForgetEnemyTime>k__BackingField");
        if (method == null || ForgetField?.FieldType != typeof(float))
            throw new MissingMemberException("SAIN follower forget-duration boundary changed.");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(FollowerSainTrackingTimerPatch), nameof(RestoreDuration)));
    }
    private static void RestoreDuration(object __instance)
    {
        var owner = SainBotOwnerAccessor.Get(__instance);
        if (owner == null || !BossPlayers.IsFollower(owner)) return;
        ForgetField.SetValue(__instance, FollowerEnemyTracking.RememberSeconds);
        owner.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = FollowerEnemyTracking.RememberSeconds;
    }
}
