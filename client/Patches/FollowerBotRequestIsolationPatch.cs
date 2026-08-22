using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using EventInfo = GlobalEventDispatcher.PhraseDelegateInfo;

namespace pitTeam.Patches
{
    internal class FollowerBotRequestTakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotRequest), nameof(BotRequest.Take));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotRequest __instance, BotOwner executor)
        {
            if (executor == null || !BossPlayers.IsFollower(executor))
            {
                return true;
            }

            return FollowerBotRequestGate.TryConsume(executor, __instance.BotRequestType);
        }
    }

    internal class FollowerBotReceiverHardAimIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnHardAimDelegate));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, IPlayer player, bool status)
        {
            BotOwner botOwner = __instance?._owner;
            return botOwner == null || !BossPlayers.IsFollower(botOwner);
        }
    }

    internal class FollowerBotReceiverTiltIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnQETilt));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, IPlayer player)
        {
            BotOwner botOwner = __instance?._owner;
            return botOwner == null || !BossPlayers.IsFollower(botOwner);
        }
    }

    internal class FollowerBotReceiverPhraseIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnPhraseSay));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            BotOwner botOwner = __instance?._owner;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (info?.PlayerRequester == null || !BossPlayers.IsPlayerBoss(info.PlayerRequester.ProfileId))
            {
                return false;
            }

            return info.phrase == EPhraseTrigger.Stop;
        }
    }

    internal class FollowerBotReceiverGestureIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnGestusShow));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, BotReceiverGestus data)
        {
            BotOwner botOwner = __instance?._owner;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (data?.Player == null || !BossPlayers.IsPlayerBoss(data.Player.ProfileId))
            {
                return false;
            }

            return data.Gesture == EInteraction.ComeWithMeGesture ||
                   data.Gesture == EInteraction.HoldGesture ||
                   data.Gesture == EInteraction.ThereGesture ||
                   data.Gesture == (EInteraction)CustomGestures.OverThere;
        }
    }
}
