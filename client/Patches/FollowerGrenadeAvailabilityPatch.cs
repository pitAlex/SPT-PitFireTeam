using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class FollowerGrenadeAvailabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(BotGrenadeController), nameof(BotGrenadeController.HaveGrenade));
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(new[] { "me.sol.sain" })]
        private static void PatchPostfix(BotGrenadeController __instance, ref bool __result)
        {
            BotOwner bot = __instance?._owner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            if (FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
            __result = __instance.grenade != null;
                return;
            }

            if (__result)
            {
                __result = false;
            }
        }
    }
}
