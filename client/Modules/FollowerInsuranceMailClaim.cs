using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using JsonType;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace pitTeam.Modules
{
    // Add only authoritative coverage for this completed mail move, never replace player policies.
    internal static class FollowerInsuranceMailClaim
    {
        internal static HashSet<string> ClaimedIds(object[] commands, Profile profile)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (commands == null || profile?.Inventory == null) return ids;
            var claims = commands.OfType<MoveOperation.MoveCommand>().Where(command =>
                command.FromOwner?.Type == EOwnerType.Mail && (command.ToOwner == null
                || (command.ToOwner.Type == EOwnerType.Profile && command.ToOwner.Id == profile.Id))).ToArray();
            if (claims.Length == 0) return ids;
            var items = profile.Inventory.AllRealPlayerItems.ToArray();
            foreach (var command in claims)
            {
                // EFT omits ToOwner for the current main profile (AbstractOperation.CreateOwner).
                var roots = items.Where(item => item.Id == command.Item).ToArray();
                if (roots.Length != 1) continue;
                var root = roots[0];
                ids.Add(root.Id);
                if (root is CompoundItem compound)
                    foreach (var child in compound.GetAllItems()) ids.Add(child.Id);
            }
            return ids;
        }

        internal static IEnumerator Refresh(ClientBackendSession session, HashSet<string> claimedIds)
        {
            var profile = session.Profile;
            var insurance = session.InsuranceCompany;
            string payload = JsonConvert.SerializeObject(new { ItemIds = claimedIds.ToArray() });
            var request = Task.Run(() => RequestHandler.PostJson(
                "/singleplayer/pitfireteam/insurance/mail-claimed", payload));
            while (!request.IsCompleted) yield return null;
            try
            {
                if (request.IsFaulted) throw request.Exception;
                if (request.IsCanceled || !ReferenceEquals(session.Profile, profile)
                    || !ReferenceEquals(session.InsuranceCompany, insurance)) yield break;
                var body = JObject.Parse(request.Result);
                if (body["err"]?.Value<int?>() != 0) throw new InvalidOperationException("Mail insurance refresh failed.");
                var policies = body["data"]?.ToObject<List<Policy>>() ?? new List<Policy>();
                int applied = Apply(session, profile, claimedIds, policies);
                if (applied > 0) Logger.LogInfo($"[FollowerInsurance:MailDisplay] refreshed={applied}");
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[FollowerInsurance:MailDisplay] Coverage remains saved; client refresh failed: {ex}");
            }
        }

        internal static int Apply(ClientBackendSession session, Profile profile, HashSet<string> claimedIds,
            IEnumerable<Policy> policies)
        {
            if (!ReferenceEquals(session.Profile, profile) || profile?.Inventory == null
                || session.InsuranceCompany == null) return 0;
            var items = profile.Inventory.AllRealPlayerItems.GroupBy(item => (string)item.Id)
                .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
            var existing = (profile.InsuredItems ?? Array.Empty<InsuredProfileItems>()).ToList();
            int applied = 0;
            foreach (var group in policies.Where(policy => policy != null).GroupBy(policy => policy.ItemId))
            {
                if (group.Count() != 1) continue;
                var policy = group.Single();
                if (string.IsNullOrWhiteSpace(policy.ItemId) || string.IsNullOrWhiteSpace(policy.TraderId)
                    || !claimedIds.Contains(policy.ItemId) || !items.TryGetValue(policy.ItemId, out var item)) continue;
                var covered = existing.Where(entry => entry.ItemId == policy.ItemId).ToArray();
                if (covered.Length > 1 || (covered.Length == 1 && covered[0].TraderId != policy.TraderId)) continue;
                var entry = covered.FirstOrDefault() ?? new InsuredProfileItems
                    { ItemId = policy.ItemId, TraderId = policy.TraderId };
                if (covered.Length == 0)
                {
                    existing.Add(entry);
                    profile.InsuredItems = existing.ToArray();
                }
                if (session.InsuranceCompany.Insured(policy.ItemId)) continue;
                var registered = session.InsuranceCompany.RegisterSingleItem(entry, item);
                if (registered != null)
                {
                    session.InsuranceCompany.OnItemInsured.Invoke(registered);
                    applied++;
                }
            }
            return applied;
        }

        internal sealed class Policy
        {
            public string ItemId { get; set; }
            public string TraderId { get; set; }
        }
    }
}
