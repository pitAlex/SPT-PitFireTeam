using System;

namespace pitTeam.BigBrain;

// Core Rifleman formulas shared with SainMan. Inputs are supplied by each brain's
// own knowledge adapter; this policy cannot acquire enemies or execute actions.
public static class FollowerPushRiskPolicy
{
    public const float ReferenceDistance = 80f;
    public const float MaxRequiredAggression = 150f;
    public const float ClusterRadius = 17f;
    public static float EquipmentRatio(float own, float enemy) => own > 1f && enemy > 0f ? enemy / own : 1f;
    public static float Threat(float equipmentRatio, float roleMultiplier, int enemies, int weaponPolicy)
    {
        float equipment = Clamp((equipmentRatio - 1f) * 10f, -6f, 12f);
        float role = Clamp((roleMultiplier - 1f) * 12f, 0f, 12f);
        float group = enemies <= 1 ? -5f : enemies == 2 ? 2f : enemies == 3 ? 7f : 12f;
        float weapon = weaponPolicy == 1 ? 4f : weaponPolicy == 2 ? 12f : 0f;
        return Clamp(equipment + role + group + weapon, -12f, 30f);
    }
    public static float PlayerPull(float route, float current, float projected)
    {
        float extra = projected - current;
        if (extra <= 0f) return Math.Max(-8f, extra * 0.25f);
        float factor = 0.35f + 0.65f * Clamp((route - 10f) / (ReferenceDistance - 10f), 0f, 1f);
        return Math.Min(20f, extra * 0.5f * factor);
    }
    public static float Required(float route, float threat, float playerPull) =>
        Clamp(route / ReferenceDistance * 100f + threat + playerPull, 0f, MaxRequiredAggression);
    public static bool RestrictMagazine(int rounds, int capacity, bool automatic, bool shotgun, bool precision,
        out bool closeShotgun)
    {
        closeShotgun = rounds >= 6 && rounds < 10 && shotgun;
        return rounds < 10 || (capacity > 0 && capacity < 30 && !shotgun && !automatic && (!precision || capacity < 20));
    }
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
}
