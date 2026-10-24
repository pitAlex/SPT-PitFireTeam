using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Modules
{
    // Optional Core compatibility: suspend objectives while we own the follower,
    // then return its original permission without replacing its quest or brain.
    internal static class FollowerQuestingBots
    {
        private const string Subscriber = "FollowerQuestingBots";
        private sealed class PausedBot
        {
            internal Component Component;
            internal bool? OriginalAllowed;
            internal bool Changed;
            internal float NextCheck;
            internal IHealthController Health;
            internal Action<EDamageType> Died;
        }

        private static readonly Dictionary<BotOwner, PausedBot> Paused = new Dictionary<BotOwner, PausedBot>();
        private static bool resolved, failureLogged;
        private static Type managerType;
        private static PropertyInfo allowed, initialized;
        private static MethodInfo stop, restore;

        private static bool Resolve()
        {
            if (resolved) return stop != null;
            resolved = true;
            managerType = AccessTools.TypeByName("QuestingBots.Components.BotObjectiveManager");
            if (managerType == null) return false;
            allowed = AccessTools.Property(managerType, "IsQuestingAllowed");
            initialized = AccessTools.Property(managerType, "HasInitialQuestBeenSelected");
            var candidate = AccessTools.Method(managerType, "StopQuesting", Type.EmptyTypes);
            restore = allowed?.GetSetMethod(true);
            if (candidate?.ReturnType != typeof(void) || candidate.IsStatic || restore == null ||
                allowed?.PropertyType != typeof(bool) || initialized?.PropertyType != typeof(bool))
            {
                LogFailure(new MissingMemberException("Unsupported Questing Bots objective permission API"));
                return false;
            }
            stop = candidate;
            return true;
        }

        internal static void Pause(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer?.HealthController?.IsAlive != true || bot.IsDead) return;
            try
            {
                if (!Resolve() || Paused.ContainsKey(bot)) return;
                var state = new PausedBot { Health = bot.GetPlayer.HealthController };
                state.Died = _ => Release(bot, false);
                Paused.Add(bot, state);
                state.Health.DiedEvent += state.Died;
                BotOwnerUpdateHub.RegisterFollower(Subscriber, Tick);
                Tick(bot);
            }
            catch (Exception ex) { LogFailure(ex); }
        }

        private static void Tick(BotOwner bot)
        {
            if (ReferenceEquals(bot, null) || !Paused.TryGetValue(bot, out var state)) return;
            if (bot == null || bot.IsDead || state.Health.IsAlive != true)
            {
                Release(bot, false);
                return;
            }
            if (Time.time < state.NextCheck) return;
            state.NextCheck = Time.time + 0.5f;
            try
            {
                if (state.Component == null)
                {
                    state.Component = bot.GetPlayer?.gameObject.GetComponent(managerType);
                    state.OriginalAllowed = null;
                    state.Changed = false;
                }
                if (state.Component == null || !(bool)initialized.GetValue(state.Component)) return;
                // Initial selection can enable questing several frames after recruitment.
                // Snapshot only after that native setup, including bots disabled by QB.
                bool current = (bool)allowed.GetValue(state.Component);
                if (!state.OriginalAllowed.HasValue) state.OriginalAllowed = current;
                if (!current) return;
                state.Changed = true;
                stop.Invoke(state.Component, null);
            }
            catch (Exception ex) { LogFailure(ex); }
        }

        internal static void Release(BotOwner bot, bool resume = true)
        {
            // Unity's destroyed-object null must not hide a retained managed key.
            if (ReferenceEquals(bot, null) || !Paused.TryGetValue(bot, out var state)) return;
            Paused.Remove(bot);
            if (Paused.Count == 0) BotOwnerUpdateHub.UnregisterFollower(Subscriber);
            try
            {
                state.Health.DiedEvent -= state.Died;
                if (resume && bot != null && state.Changed && state.OriginalAllowed.HasValue && !bot.IsDead &&
                    state.Health.IsAlive && state.Component != null)
                    restore.Invoke(state.Component, new object[] { state.OriginalAllowed.Value });
            }
            catch (Exception ex) { LogFailure(ex); }
        }

        internal static void ClearRaid()
        {
            try
            {
                foreach (var bot in new List<BotOwner>(Paused.Keys)) Release(bot, false);
            }
            finally
            {
                Paused.Clear();
                BotOwnerUpdateHub.UnregisterFollower(Subscriber);
            }
        }

        private static void LogFailure(Exception ex)
        {
            if (failureLogged) return;
            failureLogged = true;
            Logger.LogError("[QuestingBots] Follower objective handoff failed: " + ex);
        }
    }
}
