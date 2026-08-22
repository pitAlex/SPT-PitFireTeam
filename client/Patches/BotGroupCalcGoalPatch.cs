using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class BotGroupCalcGoalPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotCalcGoal), "CalcGoalForBot", Type.EmptyTypes);
        }

        [PatchPostfix]
        private static void PatchPostfix(BotCalcGoal __instance)
        {
            BotOwner bot = __instance?._owner;
            if (bot == null)
            {
                return;
            }

            try
            {
                FollowerCalcGoalEnemyAcquire.HandleCalcGoal(bot);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[CalcGoal] follower acquire callback error.");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
