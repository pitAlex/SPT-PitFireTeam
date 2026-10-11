using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    // Debug-only observation. Retains IDs, not cloned/live inventory or view references.
    internal static class GearSwapInsuranceDiagnostics
    {
        private const int MaximumItems = 512;
        private static readonly HashSet<string> Watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LastIcons = new Dictionary<string, string>();
        private static string exchange;
        private static float until;

        internal static bool Watches(string itemId) => itemId != null && Active() && Watched.Contains(itemId);

        [Conditional("DEBUG")]
        internal static void Begin(string followerId, IEnumerable<string> itemIds)
        {
            Clear();
            exchange = Guid.NewGuid().ToString("N").Substring(0, 8);
            until = float.PositiveInfinity;
            foreach (string id in itemIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumItems)) Watched.Add(id);
            Write($"[SwapGear:Insurance] exchange='{exchange}' phase='open' follower='{followerId}' watched={Watched.Count}");
        }

        [Conditional("DEBUG")]
        internal static void Summary(string phase, string cache, int playerCovered, int followerCovered)
        {
            if (!Active()) return;
            Write($"[SwapGear:Insurance] exchange='{exchange}' phase='{phase}' playerCovered={playerCovered} followerCovered={followerCovered} {cache}");
        }

        [Conditional("DEBUG")]
        internal static void Item(string phase, string itemId, string templateId, string location, bool nativeInsured, bool raidInsured)
        {
            if (!Active() || !Watched.Contains(itemId)) return;
            Write($"[SwapGear:Insurance] exchange='{exchange}' phase='{phase}' item='{itemId}' template='{templateId}' location='{location}' nativeInsured={nativeInsured} raidInsured={raidInsured}");
        }

        [Conditional("DEBUG")]
        internal static void Icon(string itemId, string viewType, bool draft, bool nativeEnabled, bool raidInsured,
            bool borderActive, bool iconActive, bool iconVisible, string cache)
        {
            if (!Active() || !Watched.Contains(itemId)) return;
            string key = itemId + "/" + viewType + "/" + draft;
            string state = $"nativeEnabled={nativeEnabled} raidInsured={raidInsured} borderActive={borderActive} iconActive={iconActive} iconVisible={iconVisible} {cache}";
            if (LastIcons.TryGetValue(key, out string previous) && previous == state) return;
            if (!LastIcons.ContainsKey(key) && LastIcons.Count >= MaximumItems) return;
            LastIcons[key] = state;
            Write($"[SwapGear:InsuranceIcon] exchange='{exchange}' item='{itemId}' view='{viewType}' draft={draft} {state}");
        }

        [Conditional("DEBUG")]
        internal static void End(bool committed)
        {
            if (!Active()) return;
            Write($"[SwapGear:Insurance] exchange='{exchange}' phase='close' committed={committed} observingLiveIconsForSeconds=60");
            until = Time.time + 60f;
        }

        [Conditional("DEBUG")]
        internal static void Clear()
        {
            exchange = null;
            until = 0f;
            Watched.Clear();
            LastIcons.Clear();
        }

        private static bool Active()
        {
            if (exchange == null) return false;
            if (Time.time <= until) return true;
            Clear();
            return false;
        }

        private static void Write(string message)
        {
            // A logging failure must not reject an otherwise valid exchange or icon refresh.
            try { Logger.LogInfo(message); }
            catch { }
        }
    }
}
