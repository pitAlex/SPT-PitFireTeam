using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using System;
using System.Reflection;

namespace pitTeam.Modules
{
    internal static class AILimitCompatibility
    {
        private const string AILimitPluginId = "com.dvize.AILimit";
        private const string AILimitComponentTypeName = "AILimit.AILimitComponent";
        private static bool patchAttempted;

        public static void PatchIfInstalled(Harmony harmony)
        {
            if (patchAttempted)
            {
                return;
            }

            patchAttempted = true;
            if (!Chainloader.PluginInfos.ContainsKey(AILimitPluginId))
            {
                return;
            }

            try
            {
                Type componentType = AccessTools.TypeByName(AILimitComponentTypeName);
                MethodInfo processPlayer = componentType != null
                    ? AccessTools.Method(componentType, "ProcessPlayer", new[] { typeof(Player) })
                    : null;
                MethodInfo prefix = AccessTools.Method(
                    typeof(AILimitCompatibility),
                    nameof(ProcessPlayerPrefix));

                if (processPlayer == null || prefix == null)
                {
                    Logger.LogError("AILimit is installed, but AILimitComponent.ProcessPlayer could not be resolved");
                    return;
                }

                harmony.Patch(processPlayer, prefix: new HarmonyMethod(prefix));
                Logger.LogInfo("AILimit follower compatibility enabled");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to install AILimit follower compatibility");
                Logger.LogError(ex);
            }
        }

        private static bool ProcessPlayerPrefix(Player __0)
        {
            if (__0 == null || __0.IsYourPlayer)
            {
                return false;
            }

            BotOwner owner = __0.AIData?.BotOwner;
            if (owner == null || owner.Memory == null)
            {
                return false;
            }

            return !BossPlayers.IsFollower(owner);
        }
    }
}
