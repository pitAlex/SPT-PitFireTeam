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
        private static string _questRole;
        private static readonly HashSet<string> QuestRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JToken> QuestProfiles = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        internal static JToken QuestProfileFor(string role) => role != null && QuestProfiles.TryGetValue(role, out var profile) ? profile : null;
        internal static JToken QuestProfile { get; private set; }

        internal static void Reset() { _token = _configuration = _sentMembership = _questRole = null; QuestProfile = null; QuestProfiles.Clear(); QuestRoles.Clear(); _settings = null; Members.Clear(); }
        internal static string TokenFor(string account) => _mode == GameplayModeAddons.Current &&
            account != null && Members.ContainsKey(account) ? _token : null;
        internal static string QuestTokenFor(string role) => _mode == GameplayModeAddons.Current && QuestRoles.Contains(role) ? _token : null;

        internal static void InvalidateQuest()
        {
            if (_questRole == null && QuestProfile == null) return;
            _questRole = null;
            QuestProfile = null;
            QuestProfiles.Clear();
            QuestRoles.Clear();
            _sentMembership = null;
        }

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

        internal static void Prepare(RaidSettings settings, IEnumerable<string> accounts, string questRole = null, IEnumerable<string> questRoles = null)
        {
            if (settings?.IsPmc != true || settings.isInTransition || Utils.Utils.FlagGet("RaidTransit") ||
                GameplayModeAddons.Current != GameplayMode.Allegiance || !GameplayModeAddons.HasExtension(GameplayMode.Allegiance)) return;
            var raidConfiguration = JObject.FromObject(settings);
            // Matchmaking bookkeeping does not change equipment generation.
            raidConfiguration.Remove("keyId");
            raidConfiguration.Remove("onlinePveRaidStates");
            string configuration = raidConfiguration.ToString(Formatting.None);
            if (_token != null && (_mode != GameplayModeAddons.Current || _configuration != configuration)) Reset();
            var selected = new HashSet<string>(accounts.Where(account => !string.IsNullOrWhiteSpace(account)), StringComparer.Ordinal);
            var roles = (questRoles ?? (questRole == null ? Array.Empty<string>() : new[] { questRole }))
                .Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(role => role, StringComparer.Ordinal).ToArray();
            if (selected.Count != 0) roles = Array.Empty<string>();
            questRole = roles.FirstOrDefault();
            if (_token == null && selected.Count == 0 && questRole == null) return;
            _mode = GameplayModeAddons.Current;
            _settings = settings;
            _configuration = configuration;
            if (_token == null) _token = Guid.NewGuid().ToString("N");
            foreach (string account in Members.Keys.Where(account => !selected.Contains(account)).ToArray()) Members.Remove(account);
            foreach (string account in selected)
                if (!Members.ContainsKey(account)) Members.Add(account, Guid.NewGuid().ToString("N"));
            // Repeated views retain each join revision. Rejoining after removal
            // changes only that member, even if a release response was lost.
            string membership = string.Join(";", Members.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value)) + "|" + string.Join(",", roles);
            if (_sentMembership == membership) return;
            _sentMembership = membership;
            _questRole = questRole;
            QuestRoles.Clear();
            QuestRoles.UnionWith(roles);
            QuestProfile = null;
            QuestProfiles.Clear();
            try
            {
                string response = RequestHandler.PostJson("/client/game/bot/followerprepare", JsonConvert.SerializeObject(new
                { token = _token, members = Members.Keys.ToArray(), memberRevisions = Members, raidConfiguration, questCompanionRole = questRole, questCompanionRoles = roles }));
                var body = JObject.Parse(response);
                if ((int?)body["err"] != 0 || !string.Equals((string)(body["data"]?["Token"] ?? body["data"]?["token"]), _token, StringComparison.Ordinal))
                    throw new InvalidOperationException("Companion preparation was not acknowledged: " + (string)body["errmsg"]);
                QuestProfile = body["data"]?["QuestCompanion"] ?? body["data"]?["questCompanion"];
                if (QuestProfile?.Type == JTokenType.Null) QuestProfile = null;
                var companions = body["data"]?["QuestCompanions"] ?? body["data"]?["questCompanions"];
                foreach (var companion in companions as JArray ?? new JArray())
                {
                    string role = (string)(companion["Info"]?["Settings"]?["Role"] ?? companion["info"]?["settings"]?["role"]);
                    if (role != null && roles.Contains(role, StringComparer.OrdinalIgnoreCase)) QuestProfiles[role] = companion;
                }
                if (QuestProfile != null && questRole != null && !QuestProfiles.ContainsKey(questRole)) QuestProfiles[questRole] = QuestProfile;
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
