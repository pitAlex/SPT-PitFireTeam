using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class FollowerPmcCombatSuppression
    {
        public static bool Prefix(BotOwner botOwner, ref bool result)
        {
            if (pitFireTeam.UseSainFollowerCombat)
            {
                return true;
            }

            if (botOwner != null && BossPlayers.IsFollower(botOwner))
            {
                result = false;
                return false;
            }

            return true;
        }
    }

    internal sealed class PmcBearCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(PmcBearLayer).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(PmcBearLayer __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?._owner, ref __result);
        }
    }

    internal sealed class PmcUsecCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(PmcUsecLayer).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(PmcUsecLayer __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?._owner, ref __result);
        }
    }

    internal sealed class PmcFlankCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(PmcLayer).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(PmcLayer __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?._owner, ref __result);
        }
    }
}
