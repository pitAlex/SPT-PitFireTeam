using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using pitTeam.Components;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class OrbitCompatibility
    {
        private const string OrbitPluginId = "com.chazut.orbit";
        private const string OrbitLayerTypeName = "Orbit.Brain.OrbitBrainLayer, ORBIT";
        private const string OrbitManagerTypeName = "Orbit.Core.OrbitManager, ORBIT";
        private const string OrbitRosterTypeName = "Orbit.Core.BotRoster, ORBIT";
        private const string OrbitLootHandlerTypeName = "Orbit.Looting.OrbitLootHandler, ORBIT";

        private sealed class FollowerMarker
        {
        }

        private sealed class BotLayerState
        {
            public readonly List<WeakReference> Layers = new List<WeakReference>();
        }

        private sealed class LayerBinding
        {
            public LayerBinding(BotOwner bot, object brain, Player player)
            {
                Bot = bot;
                Brain = brain;
                Player = player;
            }

            public BotOwner Bot { get; }
            public object Brain { get; }
            public Player Player { get; }
        }

        private static readonly ConditionalWeakTable<BotOwner, FollowerMarker> ClaimedFollowers =
            new ConditionalWeakTable<BotOwner, FollowerMarker>();

        private static readonly ConditionalWeakTable<BotOwner, BotLayerState> LayersByBot =
            new ConditionalWeakTable<BotOwner, BotLayerState>();

        private static readonly ConditionalWeakTable<object, LayerBinding> LayerBindings =
            new ConditionalWeakTable<object, LayerBinding>();

        private static Type orbitLayerType;
        private static Type orbitManagerType;
        private static Type orbitRosterType;
        private static Type orbitLootHandlerType;
        private static FieldInfo orbitLootHandlerBotField;
        private static bool patchAttempted;

        public static void PatchIfInstalled(Harmony harmony)
        {
            if (patchAttempted)
            {
                return;
            }

            patchAttempted = true;
            if (!Chainloader.PluginInfos.ContainsKey(OrbitPluginId))
            {
                return;
            }

            try
            {
                orbitLayerType = Type.GetType(OrbitLayerTypeName, false);
                orbitManagerType = Type.GetType(OrbitManagerTypeName, false);
                orbitRosterType = Type.GetType(OrbitRosterTypeName, false);
                orbitLootHandlerType = Type.GetType(OrbitLootHandlerTypeName, false);
                orbitLootHandlerBotField = orbitLootHandlerType != null
                    ? AccessTools.Field(orbitLootHandlerType, "_bot")
                    : null;

                if (orbitLayerType == null || orbitManagerType == null || orbitRosterType == null)
                {
                    Logger.LogError("ORBIT is installed, but its follower compatibility types could not be resolved");
                    return;
                }

                ConstructorInfo constructor = AccessTools.Constructor(
                    orbitLayerType,
                    new[] { typeof(BotOwner), typeof(int) });
                MethodInfo isActive = AccessTools.Method(orbitLayerType, "IsActive");

                if (constructor == null || isActive == null)
                {
                    Logger.LogError("ORBIT is installed, but OrbitBrainLayer constructor/IsActive could not be resolved");
                    return;
                }

                harmony.Patch(
                    constructor,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(OrbitCompatibility), nameof(OrbitLayerConstructorPostfix))));
                harmony.Patch(
                    isActive,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(OrbitCompatibility), nameof(OrbitLayerIsActivePrefix))));

                PatchOrbitLootHandler(harmony);
                Logger.LogInfo("ORBIT follower compatibility enabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to install ORBIT follower compatibility");
                Logger.LogError(ex);
            }
        }

        public static void ClaimFollower(BotOwner bot)
        {
            if (bot == null || !Chainloader.PluginInfos.ContainsKey(OrbitPluginId))
            {
                return;
            }

            ClaimedFollowers.GetValue(bot, _ => new FollowerMarker());
            TryDetachFollower(bot);
        }

        private static void PatchOrbitLootHandler(Harmony harmony)
        {
            if (orbitLootHandlerType == null)
            {
                return;
            }

            MethodInfo startLooting = AccessTools.Method(orbitLootHandlerType, "StartLooting");
            if (startLooting != null)
            {
                harmony.Patch(
                    startLooting,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(OrbitCompatibility), nameof(OrbitLootStartPrefix))));
            }

            MethodInfo unfreeze = AccessTools.Method(orbitLootHandlerType, "UnfreezeBotAfterLootSession");
            if (unfreeze != null)
            {
                harmony.Patch(
                    unfreeze,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(OrbitCompatibility), nameof(OrbitLootUnfreezePrefix))));
            }
        }

        private static void OrbitLayerConstructorPostfix(object __instance, BotOwner __0)
        {
            if (__instance == null || __0 == null)
            {
                return;
            }

            try
            {
                var binding = new LayerBinding(__0, __0.Brain?.BaseBrain, __0.GetPlayer);
                LayerBindings.Add(__instance, binding);

                BotLayerState state = LayersByBot.GetValue(__0, _ => new BotLayerState());
                lock (state.Layers)
                {
                    state.Layers.Add(new WeakReference(__instance));
                }

                if (IsClaimedFollower(__0))
                {
                    TryDetachFollower(__0);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ORBIT layer registration cleanup failed for a pitFireTeam follower");
                Logger.LogError(ex);
            }
        }

        private static bool OrbitLayerIsActivePrefix(object __instance, ref bool __result)
        {
            if (__instance == null)
            {
                return true;
            }

            BotOwner bot = null;
            if (LayerBindings.TryGetValue(__instance, out LayerBinding binding))
            {
                bot = binding.Bot;
            }
            else
            {
                object agent = ReadMember(__instance, "_agent");
                bot = ReadMember(agent, "Bot") as BotOwner;
            }

            if (!IsClaimedFollower(bot))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool OrbitLootStartPrefix(object __instance)
        {
            BotOwner bot = GetOrbitLootHandlerBot(__instance);
            return !IsClaimedFollower(bot);
        }

        private static bool OrbitLootUnfreezePrefix(object __instance)
        {
            BotOwner bot = GetOrbitLootHandlerBot(__instance);
            return !IsClaimedFollower(bot);
        }

        private static BotOwner GetOrbitLootHandlerBot(object handler)
        {
            try
            {
                return handler != null ? orbitLootHandlerBotField?.GetValue(handler) as BotOwner : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsClaimedFollower(BotOwner bot)
        {
            if (bot == null)
            {
                return false;
            }

            if (ClaimedFollowers.TryGetValue(bot, out _))
            {
                return true;
            }

            try
            {
                return BossPlayers.IsFollower(bot);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDetachFollower(BotOwner bot)
        {
            try
            {
                CancelOrbitLoot(bot);

                object manager = null;
                object agent = null;

                if (LayersByBot.TryGetValue(bot, out BotLayerState state))
                {
                    List<object> liveLayers = SnapshotLiveLayers(state);
                    for (int i = 0; i < liveLayers.Count; i++)
                    {
                        object layer = liveLayers[i];
                        UnhookLayerCallbacks(layer);

                        object layerManager = ReadMember(layer, "_orbit");
                        object layerAgent = ReadMember(layer, "_agent");
                        manager = manager ?? layerManager;
                        agent = agent ?? layerAgent;

                        RestoreDoorCollisionOwnership(bot, layerManager, layer);
                    }
                }

                manager = manager ?? GetSingletonInstance(orbitManagerType);
                agent = agent ?? GetOrbitAgent(bot);

                if (agent == null || manager == null)
                {
                    return;
                }

                ClearOrbitAgentState(agent);

                MethodInfo removeAgent = AccessTools.Method(manager.GetType(), "RemoveAgent");
                if (removeAgent == null)
                {
                    Logger.LogError("ORBIT RemoveAgent could not be resolved while detaching a follower");
                    return;
                }

                removeAgent.Invoke(manager, new[] { agent });

                Logger.LogInfo($"Detached follower {bot.Profile?.Nickname ?? bot.name} from ORBIT runtime ownership");
            }
            catch (TargetInvocationException ex)
            {
                Logger.LogError($"ORBIT follower detach failed for {bot?.Profile?.Nickname ?? bot?.name}");
                Logger.LogError(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                Logger.LogError($"ORBIT follower detach failed for {bot?.Profile?.Nickname ?? bot?.name}");
                Logger.LogError(ex);
            }
            finally
            {
                RestoreFollowerControl(bot);
            }
        }

        private static void RestoreFollowerControl(BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            try
            {
                if (bot.Mover != null)
                {
                    bot.Mover.Pause = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to release ORBIT mover pause for {bot.Profile?.Nickname ?? bot.name}: {ex.Message}");
            }

            try
            {
                bot.PatrollingData?.Pause();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to restore pitFireTeam patrol ownership for {bot.Profile?.Nickname ?? bot.name}: {ex.Message}");
            }

            try
            {
                bot.SetPose(1f);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to restore follower pose after ORBIT detach for {bot.Profile?.Nickname ?? bot.name}: {ex.Message}");
            }
        }

        private static List<object> SnapshotLiveLayers(BotLayerState state)
        {
            var result = new List<object>();
            lock (state.Layers)
            {
                for (int i = state.Layers.Count - 1; i >= 0; i--)
                {
                    object layer = state.Layers[i].Target;
                    if (layer == null)
                    {
                        state.Layers.RemoveAt(i);
                        continue;
                    }

                    result.Add(layer);
                }
            }
            return result;
        }

        private static void CancelOrbitLoot(BotOwner bot)
        {
            if (bot?.GetPlayer?.gameObject == null || orbitLootHandlerType == null)
            {
                return;
            }

            Component handler = bot.GetPlayer.gameObject.GetComponent(orbitLootHandlerType);
            if (handler == null)
            {
                return;
            }

            AccessTools.Method(orbitLootHandlerType, "Cancel")?.Invoke(handler, null);

            object stats = ReadMember(handler, "Stats");
            FieldInfo totalGained = stats != null ? AccessTools.Field(stats.GetType(), "TotalGained") : null;
            totalGained?.SetValue(stats, 0f);
        }

        private static object GetOrbitAgent(BotOwner bot)
        {
            object roster = GetSingletonInstance(orbitRosterType);
            MethodInfo getAgent = roster != null ? AccessTools.Method(roster.GetType(), "GetAgent") : null;
            return getAgent?.Invoke(roster, new object[] { bot });
        }

        private static object GetSingletonInstance(Type instanceType)
        {
            if (instanceType == null)
            {
                return null;
            }

            Type singletonType = typeof(Singleton<>).MakeGenericType(instanceType);
            PropertyInfo instance = AccessTools.Property(singletonType, "Instance");
            return instance?.GetValue(null, null);
        }

        private static void ClearOrbitAgentState(object agent)
        {
            SetField(agent, "IsActive", false);
            SetField(agent, "SoloExtractRequested", false);
            SetField(agent, "SoloExtractIsEmergency", false);
            SetField(agent, "SoloExtractReason", null);
            SetField(agent, "SoloExtractTarget", null);
            SetField(agent, "EmergencyLowSince", -1f);
            SetField(agent, "OwnKillCorpseLocId", 0);
        }

        private static void RestoreDoorCollisionOwnership(BotOwner bot, object manager, object layer)
        {
            object doorSystem = manager != null ? ReadMember(manager, "DoorSystem") : null;
            if (doorSystem == null)
            {
                return;
            }

            Collider botCollider = ReadMember(layer, "_botCollider") as Collider;
            if (botCollider == null)
            {
                botCollider = bot?.GetPlayer?.CharacterController?.GetCollider();
            }

            Collider pomCollider = bot?.GetPlayer?.POM?.Collider;
            AccessTools.Method(doorSystem.GetType(), "UnregisterBot")?.Invoke(doorSystem, new object[] { botCollider });

            Array doors = ReadMember(doorSystem, "Doors") as Array;
            if (doors == null)
            {
                return;
            }

            for (int i = 0; i < doors.Length; i++)
            {
                if (!(doors.GetValue(i) is Door door) || door.Collider == null)
                {
                    continue;
                }

                if (pomCollider != null)
                {
                    Physics.IgnoreCollision(pomCollider, door.Collider, false);
                }
                if (botCollider != null)
                {
                    PhysicsExtensions.IgnoreCollision(botCollider, door.Collider, false);
                }
            }
        }

        private static void UnhookLayerCallbacks(object layer)
        {
            if (layer == null || !LayerBindings.TryGetValue(layer, out LayerBinding binding))
            {
                return;
            }

            RemoveEventHandler(binding.Brain, "OnLayerChangedTo", layer, "OnLayerChanged");
            RemoveEventHandler(binding.Player, "OnPlayerDead", layer, "OnDead");
        }

        private static void RemoveEventHandler(object eventSource, string eventName, object subscriber, string methodName)
        {
            if (eventSource == null || subscriber == null)
            {
                return;
            }

            EventInfo eventInfo = FindEvent(eventSource.GetType(), eventName);
            MethodInfo handlerMethod = AccessTools.Method(subscriber.GetType(), methodName);
            if (eventInfo?.EventHandlerType == null || handlerMethod == null)
            {
                return;
            }

            Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, subscriber, handlerMethod, false);
            if (handler != null)
            {
                eventInfo.RemoveEventHandler(eventSource, handler);
            }
        }

        private static EventInfo FindEvent(Type type, string eventName)
        {
            while (type != null)
            {
                EventInfo eventInfo = type.GetEvent(
                    eventName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (eventInfo != null)
                {
                    return eventInfo;
                }
                type = type.BaseType;
            }
            return null;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            FieldInfo field = AccessTools.Field(instance.GetType(), name);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo property = AccessTools.Property(instance.GetType(), name);
            return property?.GetValue(instance, null);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance != null ? AccessTools.Field(instance.GetType(), name) : null;
            field?.SetValue(instance, value);
        }
    }
}
