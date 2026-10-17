using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class FollowerBarbedWireContactPatch : ModulePatch
    {
        private static bool reportedFailure;
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(BarbedWire), nameof(BarbedWire.AddPenalty));
        [PatchPostfix]
        private static void Postfix(BarbedWire __instance, IObserverToPlayerBridge player)
        {
            try
            {
                if (!FollowerHazardAvoidance.IsSupportedMap) return;
                if (player?.iPlayer?.IsAI != true) return;
                Player actual = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(player.iPlayer.ProfileId);
                if (!BossPlayers.IsFollower(actual?.AIData?.BotOwner)) return;
                FollowerHazardAvoidance.GetWires();
                FollowerHazardAvoidance.RegisterWire(__instance);
            }
            catch (Exception ex)
            {
                if (reportedFailure) return;
                reportedFailure = true;
                Logger.LogError($"[WireAwareness] Could not register native wire contact: {ex.Message}");
            }
        }
    }
}
