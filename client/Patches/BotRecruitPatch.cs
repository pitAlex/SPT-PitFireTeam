using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using EventInfo = GlobalEventDispatcher.PhraseDelegateInfo;

namespace pitTeam.Patches
{
    // Keep native broadcast recruitment separate from AIBossPlayer command ownership.
    internal class BotReceiverRecruitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), nameof(BotReceiver.OnPhraseSay));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            if (__instance == null || info == null) return true;

            BotOwner? botOwner = __instance._owner;
            if (botOwner == null) return true;

            EPhraseTrigger? phrase = ReadPhrase(info);
            if (!phrase.HasValue) return true;

            if (
                    !BossPlayers.IsFollower(botOwner) &&
                    (
                        phrase == (EPhraseTrigger)CustomPhrases.TeamStatus ||
                        phrase == EPhraseTrigger.OnRepeatedContact
                    )
                )
            {
                return false;
            }

            if (phrase == EPhraseTrigger.FollowMe)
            {
                var leader = ReadRequester(info);
                // Native FollowMe also calls TryAskFollowMeRequest. Suppress that
                // route for the human squad leader; core handles existing followers.
                return leader == null || !BossPlayers.IsPlayerBoss(leader.ProfileId);
            }

            if (phrase != EPhraseTrigger.Cooperation)
            {
                return true;
            } else if (BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            IPlayer requester = ReadRequester(info);
            if (requester == null) return true;
            if (!BossPlayers.IsPlayerBoss(requester.ProfileId)) return true;

            // AIBossPlayer owns player Cooperation dispatch and receiving-bot checks.
            // Suppress the broadcast's native route even when no candidate can react.
            return false;
        }

        private static EPhraseTrigger ReadPhrase(EventInfo info)
        {
            return info.phrase;
        }

        private static IPlayer ReadRequester(EventInfo info)
        {
            return info.PlayerRequester;
        }
    }
}
