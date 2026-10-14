using System;
using Comfort.Common;
using EFT;
using EFT.Counters;
using EFT.UI;

namespace pitTeam.Modules
{
    internal static class ScavFenceKarma
    {
        internal static float EncounterChance(float baseChance, double standing)
        {
            if (double.IsNaN(standing) || double.IsInfinity(standing)) standing = 0d;
            // Fence standing is the multiplier; reputation above 6 adds no further bonus.
            return (float)Math.Max(0d, Math.Min(1d, baseChance * Math.Max(0d, Math.Min(6d, standing))));
        }

        internal static float RecruitmentAcceptanceChance(float encounterChance)
        {
            if (float.IsNaN(encounterChance) || float.IsInfinity(encounterChance)) encounterChance = 0.15f;
            float fraction = Math.Max(0f, Math.Min(1f, (encounterChance - 0.15f) / 0.35f));
            return 1f - 0.5f * fraction;
        }

        internal static void RecordKill(Player human, bool recruited)
        {
            try
            {
                // Native raid results already carry this trader standing to both server
                // profiles. Do not also send a PMC-karma or separate standing request.
                var fence = human?.Profile?.FenceInfo;
                if (fence == null) throw new InvalidOperationException("Fence standing unavailable.");
                double loss = recruited ? 0.05d : 0.025d;
                fence.AddStanding(-loss, EFenceStandingSource.ScavKill, false);
                if (recruited) Singleton<GUISounds>.Instance?.PlayKarmaSound(false);
                Logger.LogInfo($"[ScavRelations] fence-kill standing={fence.Standing:F3} recruited={recruited}");
            }
            catch (Exception ex) { Logger.LogError("[ScavRelations] Fence penalty failed: " + ex); }
        }
    }
}
