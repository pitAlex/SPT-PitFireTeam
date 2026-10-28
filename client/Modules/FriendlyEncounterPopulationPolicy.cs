using System;

namespace pitTeam.Modules
{
    internal static class FriendlyEncounterPopulationPolicy
    {
        // The PMC minimum reserves opportunities; it does not replace a chance roll.
        internal static int Limit(int alive, int settingPercent, bool soloPmc)
        {
            if (alive <= 0) return 0;
            int percent = Math.Max(0, Math.Min(100, settingPercent));
            int proportional = (int)((long)alive * percent / 100L);
            return Math.Min(alive, Math.Max(soloPmc ? 2 : 0, proportional));
        }
    }
}
