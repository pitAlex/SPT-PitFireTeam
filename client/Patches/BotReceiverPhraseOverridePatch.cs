using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using EventInfo = GlobalEventDispatcher.PhraseDelegateInfo;

namespace pitTeam.Patches
{
    // Route Stop through pitAIBossPlayer instead of vanilla BotReceiver.method_0 logic.
    internal class BotReceiverPhraseOverridePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnPhraseSay));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            if (info?.PlayerRequester == null)
            {
                return true;
            }

            BotOwner botOwner = __instance._owner;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (!BossPlayers.IsPlayerBoss(info.PlayerRequester.ProfileId))
            {
                return true;
            }

            switch (info.phrase)
            {
                case EPhraseTrigger.Stop:
                case EPhraseTrigger.HoldPosition:
                case EPhraseTrigger.Gogogo:
                case EPhraseTrigger.Suppress:
                case EPhraseTrigger.NeedSniper:
                case EPhraseTrigger.NeedHelp:
                case EPhraseTrigger.OnYourOwn:
                case EPhraseTrigger.CoverMe:
                    return false;
                default:
                    return true;
            }
        }
    }
}
