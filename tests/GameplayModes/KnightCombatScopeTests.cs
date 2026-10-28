using pitTeam.GoingRogue.Combat;

internal static class KnightCombatScopeTests
{
    internal static void Run(Action<bool, string> check)
    {

        check(GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", "bossKnight", "Knight", true, true, true),
            "a living player-led Knight follower is eligible independently of implementation readiness");
        foreach (string mode in new[] { "GunsForHire", "Allegiance", "", null })
            check(!GoonCombatScope.Matches("bossKnight", "Knight", mode, "bossKnight", "Knight", true, true, true),
                "Knight combat excludes other or missing modes: " + mode);
        foreach (string role in new[] { "exUsec", "followerBigPipe", "followerBirdEye", "pmcUSEC", "", null })
            check(!GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", role, "Knight", true, true, true),
                "a Knight brain alone cannot admit another role: " + role);
        foreach (string brain in new[] { "PmcUsec", "BigPipe", "BirdEye", "KnightFLW", "", null })
            check(!GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", "bossKnight", brain, true, true, true),
                "Knight requires the verified native brain identity: " + brain);
        check(!GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", "bossKnight", "Knight", false, true, true),
            "ordinary hostile Knight remains outside follower combat");
        check(!GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", "bossKnight", "Knight", true, false, true),
            "dead or inactive Knight cannot own combat");
        check(!GoonCombatScope.Matches("bossKnight", "Knight", "GoingRogue", "bossKnight", "Knight", true, true, false),
            "AI-led or orphaned Knight cannot own player follower combat");
    }
}
