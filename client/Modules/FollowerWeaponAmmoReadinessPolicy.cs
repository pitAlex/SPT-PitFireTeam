using System;

namespace pitTeam.Modules
{
    /// <summary>Minimum actual usable rounds, independent of magazine/feed capacity.</summary>
    internal static class FollowerWeaponAmmoReadinessPolicy
    {
        internal static int GetRequiredRounds(string caliber)
        {
            string key = (caliber ?? string.Empty).Trim();
            if (key.StartsWith("Caliber", StringComparison.OrdinalIgnoreCase))
            {
                key = key.Substring(7);
            }

            switch (key.ToLowerInvariant())
            {
                case "12g":
                case "20g":
                case "23x75":
                    return 12;
                case "86x70": // .338 Lapua Magnum
                    return 20;
                case "762x51":
                case "762x54r":
                case "68x51":
                    return 30;
                case "9x39":
                case "366tkm":
                    return 40;
                case "26x75": // Flares and launchers do not use ordinary weapon readiness.
                case "30x29":
                case "40mmru":
                case "40x46":
                    return 0;
                default:
                    // Pistol/SMG and intermediate rifle calibers, including unknown mod calibers.
                    // Never infer a lower requirement from a small magazine or missing metadata.
                    return 60;
            }
        }
    }
}
