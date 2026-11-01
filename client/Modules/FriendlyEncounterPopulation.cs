using System.Collections.Generic;
using Comfort.Common;
using EFT;

namespace pitTeam.Modules
{
    // Admission only. Deaths change the next admission budget, never existing diplomacy.
    internal static class FriendlyEncounterPopulation
    {
        internal static bool CanAdmit(BotOwner candidate, out int alive, out int friendly, out int limit)
            => CanAdmit(new[] { candidate }, out alive, out friendly, out limit);

        private static bool IsScav(BotOwner bot) => bot.Side == EPlayerSide.Savage &&
            (bot.Profile?.Info?.Settings?.Role == WildSpawnType.assault ||
             bot.Profile?.Info?.Settings?.Role == WildSpawnType.marksman ||
             bot.Profile?.Info?.Settings?.Role == WildSpawnType.cursedAssault ||
             bot.Profile?.Info?.Settings?.Role == WildSpawnType.assaultGroup ||
             bot.Profile?.Info?.Settings?.Role == WildSpawnType.crazyAssaultEvent);

        private static bool Eligible(BotOwner bot, bool scav) => bot != null && !bot.IsDead &&
            bot.GetPlayer?.HealthController?.IsAlive == true && !string.IsNullOrEmpty(bot.ProfileId) &&
            !BossPlayers.IsFollower(bot) && (scav ? IsScav(bot) :
                (bot.Profile?.Info?.Settings?.Role == WildSpawnType.pmcUSEC ||
                 bot.Profile?.Info?.Settings?.Role == WildSpawnType.pmcBEAR) &&
                !AllegiancePmcFriendship.IsGrouped(bot));

        // Count selected encounters only. Companion-granted neutrality has its own trust rule
        // and does not make a group a selected/recruitable encounter.
        private static bool Friendly(BotOwner bot) => AllegiancePmcFriendship.HasPopulationFriendship(bot);

        private static bool CanAdmit(IList<BotOwner> candidates, out int alive, out int friendly, out int limit)
        {
            alive = friendly = limit = 0;
            var world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null || candidates.Count == 0 || candidates[0] == null) return false;
            bool scav = IsScav(candidates[0]);
            var population = new Dictionary<string, BotOwner>();
            foreach (var player in world.AllAlivePlayersList)
            {
                var bot = player?.AIData?.BotOwner;
                if (Eligible(bot, scav)) population[bot.ProfileId] = bot;
            }
            // Activation may precede the world's next published alive-list update.
            foreach (var bot in candidates)
                if (Eligible(bot, scav)) population[bot.ProfileId] = bot;
            foreach (var bot in population.Values)
            {
                alive++;
                if (Friendly(bot)) friendly++;
            }
            limit = FriendlyEncounterPopulationPolicy.Limit(alive, AllegiancePmcFriendship.GetSettingPercent(), !scav);
            int additional = 0;
            var unique = new HashSet<string>();
            foreach (var bot in candidates)
            {
                if (!Eligible(bot, scav)) continue;
                if (unique.Add(bot.ProfileId) && !Friendly(bot)) additional++;
            }
            // An already-friendly candidate adds no new friendship, even above the reduced cap.
            return unique.Count > 0 && (additional == 0 || friendly + additional <= limit);
        }
    }
}
