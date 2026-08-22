using EFT;
using EFT.Interactive;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class FollowerDoorAutoClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotDoorOpener), nameof(BotDoorOpener.WantPassTheDoor), new[] { typeof(Vector3), typeof(NavMeshDoorLink) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotDoorOpener __instance, NavMeshDoorLink infoStruct, ref bool __result)
        {
            if (!__result || __instance?._owner == null || infoStruct?.Door == null)
            {
                return;
            }

            // Followers should still auto-open blocked shut doors, but should not spend time
            // auto-closing doors that are already open while pathing through combat spaces.
            if (BossPlayers.IsFollower(__instance._owner) && infoStruct.Door.DoorState == EDoorState.Open)
            {
                __result = false;
            }
        }
    }
}
