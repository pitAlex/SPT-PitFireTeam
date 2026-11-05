using System;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches;

// Capture only a completed native hearing reaction, after audibility, dispersion
// and native publication. Never call its sensor/planner a second time to record it.
internal static class FollowerSainTrackingReportPatch
{
    private static Func<object, BotOwner> owner;
    private static Func<object, EnemyInfo> enemy;
    private static Func<object, object> nativeEnemy, soundType, sourceEvent;
    private static bool failed;
    [ThreadStatic] private static Reaction current;
    private sealed class Reaction
    {
        internal Reaction Previous;
        internal object Sound;
        internal Vector3 Point;
        internal float At;
        internal bool HasPoint;
    }

    internal static void Apply(Harmony harmony)
    {
        if (!pitFireTeam.IsSAINInstalled) return;
        MethodInfo heard = null, flyby = null, dispersion = null;
        var before = AccessTools.Method(typeof(FollowerSainTrackingReportPatch), nameof(Before));
        var capture = AccessTools.Method(typeof(FollowerSainTrackingReportPatch), nameof(Capture));
        var finish = AccessTools.Method(typeof(FollowerSainTrackingReportPatch), nameof(Finish));
        try
        {
            var data = Type.GetType("SAIN.Components.PlayerComponentSpace.AISoundData, SAIN", true);
            var sensor = Type.GetType("SAIN.Classes.Bot.Sense.Hearing.HearingSensor, SAIN", true);
            var calculator = Type.GetType("SAIN.Classes.Bot.Sense.Hearing.HearingDispersion, SAIN", true);
            heard = AccessTools.Method(sensor, "ReactToHeardSound", new[] { data });
            flyby = AccessTools.Method(sensor, "ReactToBulletFlyBy", new[] { data, typeof(float) });
            dispersion = AccessTools.Method(calculator, "CalcRandomizedPosition", new[] { data, typeof(float) });
            if (heard?.ReturnType != typeof(void) || flyby?.ReturnType != typeof(void) || dispersion?.ReturnType != typeof(Vector3))
                throw new MissingMethodException("SAIN hearing report boundary changed");
            BindReaders(data);
            harmony.Patch(dispersion, postfix: new HarmonyMethod(capture));
            harmony.Patch(heard, prefix: new HarmonyMethod(before), finalizer: new HarmonyMethod(finish));
            harmony.Patch(flyby, prefix: new HarmonyMethod(before), finalizer: new HarmonyMethod(finish));
        }
        catch (Exception ex)
        {
            foreach (var method in new[] { heard, flyby, dispersion })
                if (method != null)
                    foreach (var patch in new[] { before, capture, finish }) harmony.Unpatch(method, patch);
            LogOnce(ex);
        }
    }

    private static void BindReaders(Type data)
    {
        var input = Expression.Parameter(typeof(object), "sound");
        var value = Expression.Convert(input, data);
        Expression Read(string name) => Expression.PropertyOrField(value, name);
        owner = Expression.Lambda<Func<object, BotOwner>>(Expression.PropertyOrField(Read("Bot"), "BotOwner"), input).Compile();
        enemy = Expression.Lambda<Func<object, EnemyInfo>>(Expression.PropertyOrField(Read("Enemy"), "EnemyInfo"), input).Compile();
        nativeEnemy = Expression.Lambda<Func<object, object>>(Expression.Convert(Read("Enemy"), typeof(object)), input).Compile();
        soundType = Expression.Lambda<Func<object, object>>(Expression.Convert(Read("SoundType"), typeof(object)), input).Compile();
        sourceEvent = Expression.Lambda<Func<object, object>>(Expression.Convert(Read("Sound"), typeof(object)), input).Compile();
    }

    private static void Before(object __0, out Reaction __state)
    {
        __state = null;
        try
        {
            if (!pitFireTeam.IsSAINInstalled || __0 == null) return;
            var bot = owner(__0); var info = enemy(__0);
            if (!FollowerEnemyTracking.IsRealistic(info) || info.Owner != bot ||
                bot.EnemiesController?.EnemyInfos == null ||
                info.Person == null || !bot.EnemiesController.EnemyInfos.TryGetValue(info.Person, out var accepted) || !ReferenceEquals(info, accepted)) return;
            __state = new Reaction { Sound = __0, Previous = current };
            current = __state;
        }
        catch (Exception ex) { LogOnce(ex); }
    }

    [HarmonyPriority(Priority.Last)]
    private static void Capture(object __0, Vector3 __result)
    {
        if (current == null || __0 == null || !FollowerEnemyTracking.IsFinite(__result)) return;
        try
        {
            // AISoundData is a value type; Harmony boxes a different copy at each call.
            if (!Equals(sourceEvent(current.Sound), sourceEvent(__0)) || owner(current.Sound) != owner(__0)) return;
            current.Point = __result; current.At = Time.time; current.HasPoint = true;
        }
        catch (Exception ex) { LogOnce(ex); }
    }

    private static Exception Finish(Exception __exception, Reaction __state)
    {
        if (__state == null) return __exception;
        current = __state.Previous;
        if (__exception != null || !__state.HasPoint) return __exception;
        try
        {
            var info = enemy(__state.Sound);
            // If the native squad path already published this observation, the
            // bridge does nothing. A missing native squad still gets a heard place.
            var native = nativeEnemy(__state.Sound);
            if (!SainGoalEnemyBridge.TrySyncTrackingReport(info, __state.Point, __state.At, false,
                    native, soundType(__state.Sound)) ||
                !SainGoalEnemyBridge.TryReadTrackingReport(info, native, out var point, out var at) || at < __state.At) return __exception;
            // Native squad publication may adjust the dispersed point's height.
            // Publish its final accepted coordinate, never the earlier local copy.
            FollowerEnemyTracking.Report(info, point, at, "sainHearing");
        }
        catch (Exception ex) { LogOnce(ex); }
        return __exception;
    }

    private static void LogOnce(Exception ex)
    {
        if (failed) return;
        failed = true;
        Modules.Logger.LogError($"Follower SAIN tracking report adapter unavailable: {ex}");
    }
}
