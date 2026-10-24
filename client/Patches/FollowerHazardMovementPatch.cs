using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class FollowerHazardMovementPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(MovementContext), nameof(MovementContext.DirectApplyMotion), new[] { typeof(Vector3), typeof(float) });

        [PatchPrefix]
        private static void Prefix(Player ____player, ref Vector3 motion)
        {
            if (!FollowerHazardAvoidance.ShouldStop(____player, motion)) return;
            // Keep the route/objective and vertical physics. The next safe motion resumes normally.
            motion.x = 0f;
            motion.z = 0f;
            ____player.EnableSprint(false);
            ____player.Move(Vector2.zero);
        }
    }
}
