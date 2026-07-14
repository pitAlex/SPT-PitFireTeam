using UnityEngine;

namespace pitTeam.Utils
{
    internal static class HexColorSetting
    {
        internal static Color GetConfiguredColor(string value, string defaultHex)
        {
            return TryNormalize(value, defaultHex, out _, out Color color)
                ? color
                : GetDefaultColor(defaultHex);
        }

        internal static bool TryNormalize(
            string value,
            string defaultHex,
            out string normalized,
            out Color color)
        {
            color = GetDefaultColor(defaultHex);
            normalized = "#" + ColorUtility.ToHtmlStringRGB(color);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string candidate = value.Trim();
            if (!candidate.StartsWith("#"))
            {
                candidate = "#" + candidate;
            }

            if (candidate.Length != 7 || !ColorUtility.TryParseHtmlString(candidate, out Color parsed))
            {
                return false;
            }

            parsed.a = 1f;
            normalized = "#" + ColorUtility.ToHtmlStringRGB(parsed);
            color = parsed;
            return true;
        }

        private static Color GetDefaultColor(string defaultHex)
        {
            if (ColorUtility.TryParseHtmlString(defaultHex, out Color color))
            {
                color.a = 1f;
                return color;
            }

            return Color.white;
        }
    }
}
