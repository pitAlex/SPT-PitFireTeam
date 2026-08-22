using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    // Route follower gesture commands through pitAIBossPlayer command handling, not vanilla BotReceiver.method_6 logic.
    internal class BotReceiverGestureOverridePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnGestusShow));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, BotReceiverGestus data)
        {
            if (data?.Player == null)
            {
                return true;
            }

            BotOwner botOwner = __instance._owner;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (!BossPlayers.IsPlayerBoss(data.Player.ProfileId))
            {
                return true;
            }

            switch (data.Gesture)
            {
                case EInteraction.ComeWithMeGesture:
                case EInteraction.HoldGesture:
                case EInteraction.ThereGesture:
                case (EInteraction)CustomGestures.OverThere:
                    return false;
                default:
                    return true;
            }
        }
    }
}
