using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace pitTeam.Modules
{
    internal static class CompanionRaidPreparation
    {
        private static string _token;
        private static readonly Dictionary<string, string> Members = new Dictionary<string, string>(StringComparer.Ordinal);
        private static GameplayMode _mode;
        private static RaidSettings _settings;
        private static string _configuration, _sentMembership;

        internal static void Reset() { _token = _configuration = _sentMembership = null; _settings = null; Members.Clear(); }
        internal static string TokenFor(string account) => _mode == GameplayModeAddons.Current &&
            account != null && Members.ContainsKey(account) ? _token : null;

        internal static void Remove(string account)
        {
            if (_settings != null && account != null && Members.ContainsKey(account))
                Release(Members.Keys.Where(member => member != account).ToArray());
        }

        internal static void RemoveAll() { if (_settings != null && Members.Count > 0) Release(Array.Empty<string>()); }

        private static void Release(string[] remaining)
        {
            foreach (string account in Members.Keys.Except(remaining, StringComparer.Ordinal).ToArray()) Members.Remove(account);
            _sentMembership = null;
            try { Prepare(_settings, remaining); }
            catch (Exception ex)
            {
                // Removing a member must still complete locally if preparing the
                // release request itself fails. Rejoin receives a fresh identity.
                try { Logger.LogError("[CompanionRaidKit] Kit release failed: " + ex); } catch { }
            }
        }

        internal static void Prepare(RaidSettings settings, IEnumerable<string> accounts)
        {
            if (settings?.IsPmc != true || settings.isInTransition || Utils.Utils.FlagGet("RaidTransit") ||
                GameplayModeAddons.Current != GameplayMode.GoingRogue || !GameplayModeAddons.IsAvailable(GameplayMode.GoingRogue)) return;
            var raidConfiguration = JObject.FromObject(settings);
            // Matchmaking bookkeeping does not change equipment generation.
            raidConfiguration.Remove("keyId");
            raidConfiguration.Remove("onlinePveRaidStates");
            string configuration = raidConfiguration.ToString(Formatting.None);
            if (_token != null && (_mode != GameplayModeAddons.Current || _configuration != configuration)) Reset();
            var selected = new HashSet<string>(accounts.Where(account => !string.IsNullOrWhiteSpace(account)), StringComparer.Ordinal);
            if (_token == null && selected.Count == 0) return;
            _mode = GameplayModeAddons.Current;
            _settings = settings;
            _configuration = configuration;
            if (_token == null) _token = Guid.NewGuid().ToString("N");
            foreach (string account in Members.Keys.Where(account => !selected.Contains(account)).ToArray()) Members.Remove(account);
            foreach (string account in selected)
                if (!Members.ContainsKey(account)) Members.Add(account, Guid.NewGuid().ToString("N"));
            // Repeated views retain each join revision. Rejoining after removal
            // changes only that member, even if a release response was lost.
            string membership = string.Join(";", Members.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value));
            if (_sentMembership == membership) return;
            _sentMembership = membership;
            try
            {
                string response = RequestHandler.PostJson("/client/game/bot/followerprepare", JsonConvert.SerializeObject(new
                { token = _token, members = Members.Keys.ToArray(), memberRevisions = Members, raidConfiguration }));
                var body = JObject.Parse(response);
                if ((int?)body["err"] != 0 || !string.Equals((string)(body["data"]?["Token"] ?? body["data"]?["token"]), _token, StringComparison.Ordinal))
                    throw new InvalidOperationException("Companion preparation was not acknowledged: " + (string)body["errmsg"]);
                Logger.LogInfo($"[CompanionRaidKit] acceptScreen token={_token} members={Members.Count} location={settings.LocationId}");
            }
            catch (Exception ex)
            {
                // Keep the token: actual spawn must not silently reroll a kit after
                // a failed or lost preparation response. Existing spawn errors own UI.
                Logger.LogError("[CompanionRaidKit] Accept-screen preparation failed; spawn will require the reserved kit: " + ex);
            }
        }
    }
}
