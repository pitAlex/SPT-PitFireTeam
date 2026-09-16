using pitTeam.BigBrain;

namespace pitTeam.Modules
{
    // Shared main-mod cover geometry, without any SAIN dependency or interception.
    public static class SainCoverGeometry
    {
        public static float SearchRadius => CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius();
        public static float ArrivalHoldSeconds(bool recovery) =>
            FollowerCombatCommon.GetCommittedCoverHoldDuration(recovery ? "retreatSafeCover" : "bossCover");
        public static float Score(float pathDistance, float bossDistance) =>
            FollowerCombatCommon.ScoreBossCover(pathDistance, bossDistance);

    }
}
