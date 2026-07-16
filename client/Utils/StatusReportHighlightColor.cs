using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class StatusReportHighlightColor
    {
        internal const string DefaultHex = "#00FF00";
        internal const string FullHealthDefaultHex = "#00FF00";
        internal const string MediumHealthDefaultHex = "#FFFF00";
        internal const string LowHealthDefaultHex = "#FF0000";

        private const float OutlineAlpha = 0.75f;
        private const float LowHealthBreakpoint = 0.30f;
        private const float MediumHealthBreakpoint = 0.65f;
        private const float StomachRiskWeight = 0.40f;

        private static readonly EBodyPart[] HealthBodyParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.RightArm,
            EBodyPart.LeftArm,
            EBodyPart.RightLeg,
            EBodyPart.LeftLeg
        };

        internal static Color GetConfiguredColor()
        {
            Color color = HexColorSetting.GetConfiguredColor(
                pitFireTeam.statusReportHighlightColor?.Value,
                DefaultHex);
            color.a = OutlineAlpha;
            return color;
        }

        internal static Color GetConfiguredTextColor()
        {
            return HexColorSetting.GetConfiguredColor(
                pitFireTeam.statusReportHighlightColor?.Value,
                DefaultHex);
        }

        internal static bool IsHealthColoringEnabled => pitFireTeam.statusReportHealthColoring?.Value == true;

        internal static Color GetConfiguredHealthColor(BotOwner teammate)
        {
            float healthScore = CalculateHealthScore(teammate);
            Color fullColor = HexColorSetting.GetConfiguredColor(
                pitFireTeam.statusReportFullHealthColor?.Value,
                FullHealthDefaultHex);
            Color mediumColor = HexColorSetting.GetConfiguredColor(
                pitFireTeam.statusReportMediumHealthColor?.Value,
                MediumHealthDefaultHex);
            Color lowColor = HexColorSetting.GetConfiguredColor(
                pitFireTeam.statusReportLowHealthColor?.Value,
                LowHealthDefaultHex);

            Color color;
            if (healthScore <= LowHealthBreakpoint)
            {
                color = lowColor;
            }
            else if (healthScore < MediumHealthBreakpoint)
            {
                float blend = Mathf.InverseLerp(LowHealthBreakpoint, MediumHealthBreakpoint, healthScore);
                color = Color.Lerp(lowColor, mediumColor, blend);
            }
            else
            {
                float blend = Mathf.InverseLerp(MediumHealthBreakpoint, 1f, healthScore);
                color = Color.Lerp(mediumColor, fullColor, blend);
            }

            color.a = OutlineAlpha;
            return color;
        }

        internal static float CalculateHealthScore(BotOwner teammate)
        {
            if (teammate?.HealthController == null)
            {
                return 1f;
            }

            float currentTotal = 0f;
            float maximumTotal = 0f;
            float headRatio = 1f;
            float chestRatio = 1f;
            float stomachRatio = 1f;
            bool hasHead = false;
            bool hasChest = false;
            bool hasStomach = false;

            for (int i = 0; i < HealthBodyParts.Length; i++)
            {
                EBodyPart bodyPart = HealthBodyParts[i];
                ValueStruct health = teammate.HealthController.GetBodyPartHealth(bodyPart, true);
                if (health.Maximum <= 0f)
                {
                    continue;
                }

                float ratio = Mathf.Clamp01(health.Current / health.Maximum);
                currentTotal += Mathf.Max(0f, health.Current);
                maximumTotal += health.Maximum;

                switch (bodyPart)
                {
                    case EBodyPart.Head:
                        headRatio = ratio;
                        hasHead = true;
                        break;
                    case EBodyPart.Chest:
                        chestRatio = ratio;
                        hasChest = true;
                        break;
                    case EBodyPart.Stomach:
                        stomachRatio = ratio;
                        hasStomach = true;
                        break;
                }
            }

            if (maximumTotal <= 0f)
            {
                return 1f;
            }

            float overallRatio = Mathf.Clamp01(currentTotal / maximumTotal);
            float effectiveRatio = overallRatio;
            if (hasHead)
            {
                effectiveRatio = Mathf.Min(effectiveRatio, headRatio);
            }

            if (hasChest)
            {
                effectiveRatio = Mathf.Min(effectiveRatio, chestRatio);
            }

            if (hasStomach && stomachRatio < overallRatio)
            {
                float stomachAdjustedRatio = Mathf.Lerp(overallRatio, stomachRatio, StomachRiskWeight);
                effectiveRatio = Mathf.Min(effectiveRatio, stomachAdjustedRatio);
            }

            return Mathf.Clamp01(effectiveRatio);
        }

    }
}
