using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

using EventInfo = GlobalEventDispatcher.PhraseDelegateInfo;

namespace pitTeam.Patches
{
    // Minimal recruit trigger for 4.x:
    // Intercept FollowMe/Cooperation phrase receipt and forward it to the existing follow-request flow.
    internal class BotReceiverFollowMeRecruitPatch : ModulePatch
    {
        private const float RecruitPhraseDistance = 15f;

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

            if (phrase != EPhraseTrigger.Cooperation && phrase != EPhraseTrigger.FollowMe)
            {
                return true;
            } else if (BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            IPlayer requester = ReadRequester(info);
            if (requester == null) return true;
            if (!BossPlayers.IsPlayerBoss(requester.ProfileId)) return true;

            // Cooperation should apply only to the currently interacted bot target.
            // FollowMe is an ambient phrase and can be heard by nearby bots.
            if (phrase == EPhraseTrigger.Cooperation &&
                requester is Player requesterPlayer &&
                requesterPlayer.InteractablePlayer != null)
            {
                BotOwner interactedBot = requesterPlayer.InteractablePlayer.AIData?.BotOwner;
                if (interactedBot != null && interactedBot != botOwner)
                {
                    return false;
                }
            }

            // Keep vanilla behavior at longer range.
            if ((botOwner.Position - requester.Position).sqrMagnitude > RecruitPhraseDistance * RecruitPhraseDistance) return true;

            botOwner.BotsGroup?.RequestsController?.TryAskFollowMeRequest(requester, botOwner);

            // Request was handled by the mod flow, suppress vanilla duplicate processing.
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
