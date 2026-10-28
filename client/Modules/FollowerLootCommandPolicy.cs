using System.Collections.Generic;
using System.Linq;
using EFT;

namespace pitTeam.Modules
{
    internal static class FollowerLootCommandPolicy
    {
        internal static bool HasBodyContainerSupport(IEnumerable<BotOwner> followers) =>
            followers?.Any(bot => bot != null && !bot.IsDead &&
                BossPlayers.Instance?.GetFollower(bot)?.CanHandleBodyContainerLootCommands == true) == true;

        internal static bool HasRestrictedOnlyFollowers(IEnumerable<BotOwner> followers) =>
            !HasBodyContainerSupport(followers) && followers?.Any(bot => bot != null && !bot.IsDead &&
                BossPlayers.Instance?.GetFollower(bot)?.AllowsBodyContainerLootCommands == false) == true;

        internal static bool TryRefuseBodyContainer(IPlayer requester, IPlayer leader, IEnumerable<BotOwner> followers)
        {
            if (requester == null || leader == null || requester.ProfileId != leader.ProfileId ||
                !HasRestrictedOnlyFollowers(followers)) return false;

            BotOwner refuser = null;
            float nearest = float.PositiveInfinity;
            foreach (BotOwner bot in followers)
            {
                if (bot == null || bot.IsDead || bot.BotState != EBotState.Active ||
                    BossPlayers.Instance?.GetFollower(bot)?.AllowsBodyContainerLootCommands != false) continue;
                float distance = (bot.Position - requester.Position).sqrMagnitude;
                if (distance >= nearest) continue;
                refuser = bot;
                nearest = distance;
            }
            if (refuser == null) return false;
            refuser.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
            return true;
        }
    }
}
