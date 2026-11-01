namespace pitTeam.GoingRogue.Combat
{
    internal enum BigPipeFightChoice { Search, ReloadHold, Shoot, CoverHold, StaleContactHold, Pressure }

    // Branch order from native BigPipeFightLayer.MainFight / MidDistPipe.
    // Execution and shot/cover/medical admission remain shared.
    internal static class BigPipeDecisionPolicy
    {
        internal static BigPipeFightChoice Select(bool visible, bool canShoot, float unseenAge,
            float searchDelay, bool reloading, bool inCover, float coverAge, float holdDelay, float groupSeenAge)
        {
            if (!visible && unseenAge > searchDelay) return BigPipeFightChoice.Search;
            if (reloading) return BigPipeFightChoice.ReloadHold;
            if (visible && canShoot) return BigPipeFightChoice.Shoot;
            if (inCover && coverAge < holdDelay) return BigPipeFightChoice.CoverHold;
            if (!visible && groupSeenAge > 30f) return BigPipeFightChoice.StaleContactHold;
            return BigPipeFightChoice.Pressure;
        }

        internal static bool InLauncherRange(float distance) => distance >= 27f && distance <= 130f;
    }
}
