#if DEBUG
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches
{
    // Scope identity around native setters without altering reports or their exceptions.
    internal static class GroupSightRecorderPatch
    {
        private sealed class Scope { internal Scope Previous; internal string Enemy, Reporter; }
        private sealed class Bucket { internal float Next; internal int Suppressed; }
        private sealed class GroupState { internal readonly Dictionary<string, Bucket> Buckets = new(); }
        private static readonly ConditionalWeakTable<BotsGroup, GroupState> Groups = new();
        [ThreadStatic] private static Scope current;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.ReportAboutEnemy)),
                    prefix: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(Report)),
                    finalizer: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(EndScope)));
                harmony.Patch(AccessTools.PropertySetter(typeof(BotGroupEnemyInfo), nameof(BotGroupEnemyInfo.EnemyLastVisiblePosition)),
                    prefix: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(Position)),
                    finalizer: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(EndScope)));
                harmony.Patch(AccessTools.PropertySetter(typeof(BotsGroup), nameof(BotsGroup.EnemyLastSeenTimeReal)),
                    prefix: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(BeforeWrite)),
                    postfix: new HarmonyMethod(typeof(GroupSightRecorderPatch), nameof(AfterWrite)));
            }
            catch (Exception ex) { Modules.Logger.LogError($"[BattleRecorder] Group sight hooks unavailable: {ex}"); }
        }
        private static void Report(BotsGroup __instance, IPlayer enemy, BotOwner reporter, out Scope __state)
        {
            __state = null;
            if (!BattleRecorder.IsRecordingEnabled || __instance is not BotsGroupPlayer) return;
            try
            {
                __state = new Scope { Previous = current, Enemy = enemy?.ProfileId, Reporter = reporter?.ProfileId };
                current = __state;
            }
            catch { }
        }
        private static void Position(BotGroupEnemyInfo __instance, out Scope __state)
        {
            __state = null;
            if (!BattleRecorder.IsRecordingEnabled || __instance._botGroup is not BotsGroupPlayer) return;
            try
            {
                __state = new Scope { Previous = current, Enemy = __instance.Player?.ProfileId, Reporter = current?.Reporter };
                current = __state;
            }
            catch { }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null) current = __state.Previous;
            return __exception;
        }
        private static void BeforeWrite(BotsGroup __instance, out float __state) => __state =
            BattleRecorder.IsRecordingEnabled && __instance is BotsGroupPlayer ? __instance.EnemyLastSeenTimeReal : 0f;
        private static void AfterWrite(BotsGroup __instance, float __state)
        {
            if (!BattleRecorder.IsRecordingEnabled || __instance is not BotsGroupPlayer group) return;
            try
            {
                BotOwner follower = null;
                foreach (var candidate in group.Boss.Followers)
                    if (candidate?.BotsGroup == group && BattleRecorder.IsRecordingFor(candidate)) { follower = candidate; break; }
                if (follower == null) return;
                var state = Groups.GetOrCreateValue(group);
                string key = (current?.Enemy ?? "unknown") + ":" + (current?.Reporter ?? "unknown");
                if (!state.Buckets.TryGetValue(key, out var bucket))
                {
                    if (state.Buckets.Count >= 16) key = "overflow";
                    if (!state.Buckets.TryGetValue(key, out bucket)) state.Buckets[key] = bucket = new Bucket();
                }
                if (Time.time < bucket.Next) { bucket.Suppressed++; return; }
                bucket.Next = Time.time + 2f;
                BattleRecorder.RecordGroupSightWrite(follower, current?.Enemy, current?.Reporter,
                    __state, group.EnemyLastSeenTimeReal, Environment.StackTrace, bucket.Suppressed);
                bucket.Suppressed = 0;
            }
            catch { /* Recorder instrumentation cannot change native reporting. */ }
        }
    }
}
#endif
