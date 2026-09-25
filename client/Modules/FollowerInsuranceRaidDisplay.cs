using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace pitTeam.Modules
{
    // UI-only exact-ID lookup. Never changes the player's stock insurance policies or return requests.
    internal static class FollowerInsuranceRaidDisplay
    {
        private const string Route = "/singleplayer/pitfireteam/insurance/raid-display";
        private static readonly object Gate = new object();
        private static readonly HashSet<string> InsuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string raidServerId;

        internal static void Clear()
        {
            lock (Gate)
            {
                raidServerId = null;
                InsuredIds.Clear();
            }
        }

        internal static bool IsInsured(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;
            string currentServerId = FollowerInsuranceRaidReports.CurrentServerId;
            lock (Gate)
                return currentServerId != null && currentServerId == raidServerId && InsuredIds.Contains(itemId);
        }

        internal static void Refresh()
        {
            string serverId = FollowerInsuranceRaidReports.CurrentServerId;
            if (string.IsNullOrWhiteSpace(serverId)) return;
            try
            {
                string response = RequestHandler.PostJson(Route, JsonConvert.SerializeObject(new { InsuranceServerId = serverId }));
                var body = JObject.Parse(response);
                if (body["err"]?.Value<int?>() != 0)
                    throw new InvalidOperationException("Raid display policy request failed.");
                var policies = body["data"]?.ToObject<List<Policy>>() ?? new List<Policy>();
                if (FollowerInsuranceRaidReports.CurrentServerId != serverId) return;
                lock (Gate)
                {
                    if (raidServerId != serverId)
                    {
                        raidServerId = serverId;
                        InsuredIds.Clear();
                    }
                    foreach (var policy in policies)
                        if (!string.IsNullOrWhiteSpace(policy?.ItemId)) InsuredIds.Add(policy.ItemId);
                    Logger.LogInfo($"[FollowerInsurance:RaidDisplay] serverId='{serverId}' received={policies.Count} visibleIds={InsuredIds.Count}");
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[FollowerInsurance:RaidDisplay] Exact-raid icon policies unavailable: {ex}");
            }
        }

        private sealed class Policy
        {
            public string ItemId { get; set; }
        }
    }
}
