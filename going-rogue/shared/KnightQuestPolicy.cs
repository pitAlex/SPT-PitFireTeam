#nullable enable

namespace pitTeam.GoingRogue.Quests;

internal static class KnightQuestPolicy
{
    internal const string Competition = "6775d9957e2dbcb3bd0a02c7";
    internal const string MyLand = "6775da597e2dbcb3bd0a02c9";
    internal const string BadgeBandit = "67768936fa281ca31708b17c";
    internal const string EnemySpotted = "6776899afa281ca31708b17e";
    internal const string OffLimits = "677689ebfa281ca31708b180";
    internal const string OffLimitsKills = "677fe82bf539f6d9230066f2";
    internal const string OffLimitsFailure = "677fe82bf539f6d9230066f3";
    internal const string PierZone = "pier_zone01";
    internal const string OffLimitsImage = "/files/quest/icon/KnightCoverMe.jpg";
    internal const string AFavorKills = "677fe999f539f6d9230066fb";
    internal const string AFavorFailure = "677fe9d5f539f6d923006700";
    internal const string AFavorImage = "/files/quest/icon/KnightPartizan.jpg";
    internal const string AFavor = "67768a41fa281ca31708b182";
    internal const string Thieves = "677687f2fa281ca31708b17a";
    internal const string StolenLauncher = "64e3b0f4e3b6f5a4a37e8c1d";
    internal const string WoodsKills = "67768ec3fa281ca31708b190";
    internal const string ShorelineKills = "67768f38fa281ca31708b194";
    internal const string LighthouseKills = "6776900dfa281ca31708b198";
    internal const string MyLandKills = "6776926bfa281ca31708b1a2";
    internal const string CompetitionFailure = "67769052fa281ca31708b19c";
    internal const string MyLandFailure = "677fea2ff539f6d923006705";
    internal const string BadgeBanditKills = "677fe6a9f539f6d9230066e7";
    internal const string BadgeBanditFailure = "677fe755f539f6d9230066e8";
    internal const string EnemySpottedKills = "677fe7a6f539f6d9230066ec";
    internal const string EnemySpottedFailure = "677fe7a6f539f6d9230066ed";
    internal const string EnemySpottedImage = "/files/quest/icon/KnightBirdEye.jpg";
    internal const string BadgeBanditImage = "/files/quest/icon/KnightRobocop.jpg";
    internal const string Image = "/files/quest/icon/KnightCompetitionQuest.jpg";
    internal const string MyLandImage = "/files/quest/icon/KnightMyLandQuest.jpg";
    internal const string ThievesImage = "/files/quest/icon/KnightThieves.jpg";
    internal static string ImageFor(string quest) => quest == AFavor ? AFavorImage : quest == OffLimits ? OffLimitsImage : quest == EnemySpotted ? EnemySpottedImage : quest == BadgeBandit ? BadgeBanditImage : quest == Thieves ? ThievesImage : quest == MyLand ? MyLandImage : Image;

    internal static bool Owns(string quest) => quest == Competition || quest == MyLand || quest == Thieves || quest == BadgeBandit || quest == EnemySpotted || quest == OffLimits || quest == AFavor;
    internal static bool PlayerKills(string counter) => counter == WoodsKills || counter == ShorelineKills || counter == LighthouseKills;
    internal static bool OwnsCounter(string counter) => PlayerKills(counter) || counter == MyLandKills ||
        counter == CompetitionFailure || counter == MyLandFailure || counter == BadgeBanditKills || counter == BadgeBanditFailure ||
        counter == EnemySpottedKills || counter == EnemySpottedFailure || counter == OffLimitsKills || counter == OffLimitsFailure || counter == AFavorKills || counter == AFavorFailure;

    internal static bool CanCredit(string counter, bool goingRogue, bool livingPlayer, bool livingKnight,
        float knightDistance, bool playerKill, bool knightKill, bool knightParticipated = false)
    {
        if (!OwnsCounter(counter)) return true;
        if (!goingRogue) return false;
        if (counter == CompetitionFailure || counter == MyLandFailure || counter == BadgeBanditFailure || counter == EnemySpottedFailure || counter == OffLimitsFailure || counter == AFavorFailure) return livingKnight || knightParticipated;
        if (counter == AFavorKills) return livingPlayer && livingKnight && knightDistance <= 120f && (playerKill || knightKill);
        if (counter == EnemySpottedKills) return livingPlayer && livingKnight && knightDistance <= 120f && !playerKill && knightKill;
        return livingPlayer && livingKnight && knightDistance <= 80f &&
            (PlayerKills(counter) ? playerKill : playerKill || knightKill);
    }

    internal static string? ObjectiveForMap(string quest, string location)
    {
        string map = (location ?? "").ToLowerInvariant();
        if (quest == Competition)
            return map == "woods" ? WoodsKills : map == "shoreline" ? ShorelineKills : map == "lighthouse" ? LighthouseKills : null;
        if (quest == BadgeBandit && map == "tarkovstreets") return BadgeBanditKills;
        if (quest == EnemySpotted && map == "woods") return EnemySpottedKills;
        if (quest == AFavor && map == "lighthouse") return AFavorKills;
        if (quest == OffLimits && map == "shoreline") return OffLimitsKills;
        return quest == MyLand && map == "bigmap" ? MyLandKills : null;
    }

    internal static bool UsesMap(string quest, string location) => ObjectiveForMap(quest, location) != null;

    internal static bool NeedsCompanion(string quest, string location, System.Func<string, bool> objectiveCompleted)
    {
        string? objective = ObjectiveForMap(quest, location);
        return objective != null && objectiveCompleted != null && !objectiveCompleted(objective);
    }
}
