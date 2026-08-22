using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class AICoreAgentUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AICoreAgent<BotLogicDecision>), "Update");
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(Exception __exception, AICoreAgent<BotLogicDecision> __instance)
        {
            if (__exception != null)
            {
                Modules.Logger.LogError($"AICoreAgent.Update exception");
                Modules.Logger.LogError(__exception);
            }
            return __exception;
        }
    }
}
