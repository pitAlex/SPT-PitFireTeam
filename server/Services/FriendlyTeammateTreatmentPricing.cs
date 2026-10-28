using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public static class FriendlyTeammateTreatmentPricing
{
    public static readonly string[] BodyParts = ["Head", "Chest", "Stomach", "LeftArm", "RightArm", "LeftLeg", "RightLeg"];

    public static float MissingHealth(BotBaseHealth? health)
    {
        float missing = 0;
        foreach (string name in BodyParts)
        {
            if (health?.BodyParts == null || !health.BodyParts.TryGetValue(name, out var part)
                || part?.Health?.Current is not double current || part.Health.Maximum is not double maximum
                || !double.IsFinite(current) || !double.IsFinite(maximum) || maximum <= 0 || current < 0 || current > maximum
                || part.Effects?.Count > 0)
                throw new FriendlyTeammateException("ProfileHealingUnavailable");
            // EFT HealthObserver rounds each body part before summing, using float HP.
            missing += (float)Math.Round((float)maximum - (float)current, 2, MidpointRounding.AwayFromZero);
        }
        return missing;
    }

    public static int Calculate(float missing, float pointPrice, float loyaltyCoefficient,
        int loyaltyLevel, int charismaLevel, float charismaDiscount, float charismaTraderDiscount, bool free)
    {
        if (free) return 0;
        float cost = missing * pointPrice * Math.Clamp(loyaltyCoefficient / 100f, 0.05f, 10f);
        int level = Math.Clamp(charismaLevel, 0, 50);
        // HealthTreatmentServiceView.Show and ApplyTreatments: float skill buff, double
        // combined discount, and a single final ceiling across all selected HP.
        double discount = 1d - (level * charismaDiscount + (double)(level * (loyaltyLevel - 1)) / 3d * charismaTraderDiscount);
        double total = Math.Ceiling(cost * discount);
        if (!double.IsFinite(total) || total < 0 || total > int.MaxValue)
            throw new FriendlyTeammateException("ProfileHealingUnavailable");
        return (int)total;
    }
}
