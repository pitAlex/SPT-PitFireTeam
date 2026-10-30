namespace pitTeam.GoingRogue.Quests;

internal static class KnightQuestPolicy
{
    internal const string Competition = "6775d9957e2dbcb3bd0a02c7";
    internal const string MyLand = "6775da597e2dbcb3bd0a02c9";
    internal const string Thieves = "677687f2fa281ca31708b17a";
    internal const string StolenLauncher = "64e3b0f4e3b6f5a4a37e8c1d";
    internal const string WoodsKills = "67768ec3fa281ca31708b190";
    internal const string ShorelineKills = "67768f38fa281ca31708b194";
    internal const string LighthouseKills = "6776900dfa281ca31708b198";
    internal const string MyLandKills = "6776926bfa281ca31708b1a2";
    internal const string CompetitionFailure = "67769052fa281ca31708b19c";
    internal const string MyLandFailure = "677fea2ff539f6d923006705";
    internal const string Image = "/files/quest/icon/KnightCompetitionQuest.jpg";
    internal const string MyLandImage = "/files/quest/icon/KnightMyLandQuest.jpg";
    internal const string ThievesImage = "/files/quest/icon/KnightThieves.jpg";
    internal static string ImageFor(string quest) => quest == Thieves ? ThievesImage : quest == MyLand ? MyLandImage : Image;

    internal static bool Owns(string quest) => quest == Competition || quest == MyLand || quest == Thieves;
    internal static bool PlayerKills(string counter) => counter == WoodsKills || counter == ShorelineKills || counter == LighthouseKills;
    internal static bool OwnsCounter(string counter) => PlayerKills(counter) || counter == MyLandKills ||
        counter == CompetitionFailure || counter == MyLandFailure;

    internal static bool CanCredit(string counter, bool goingRogue, bool livingPlayer, bool livingKnight,
        float knightDistance, bool playerKill, bool knightKill, bool knightParticipated = false)
    {
        if (!OwnsCounter(counter)) return true;
        if (!goingRogue) return false;
        if (counter == CompetitionFailure || counter == MyLandFailure) return livingKnight || knightParticipated;
        return livingPlayer && livingKnight && knightDistance <= 80f &&
            (PlayerKills(counter) ? playerKill : playerKill || knightKill);
    }

    internal static bool UsesMap(string quest, string location)
    {
        string map = (location ?? "").ToLowerInvariant();
        return quest == Competition ? map == "woods" || map == "shoreline" || map == "lighthouse" :
            quest == MyLand && map == "bigmap";
    }
}
