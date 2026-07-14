using UnityEngine;

namespace pitTeam.Utils
{
    internal static class EnemyMarkerColor
    {
        internal const string AlertDefaultHex = "#FFFF00";
        internal const string VisibleDefaultHex = "#FF0000";

        internal static Color GetAlertColor()
        {
            return HexColorSetting.GetConfiguredColor(
                pitFireTeam.enemyMarkerAlertColor?.Value,
                AlertDefaultHex);
        }

        internal static Color GetVisibleColor()
        {
            return HexColorSetting.GetConfiguredColor(
                pitFireTeam.enemyMarkerVisibleColor?.Value,
                VisibleDefaultHex);
        }
    }
}
