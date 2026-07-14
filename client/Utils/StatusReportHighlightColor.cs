using UnityEngine;

namespace pitTeam.Utils
{
    internal static class StatusReportHighlightColor
    {
        internal const string DefaultHex = "#00FF00";
        private const float OutlineAlpha = 0.75f;

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

    }
}
