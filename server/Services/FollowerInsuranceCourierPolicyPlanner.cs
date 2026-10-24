using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

public static class FollowerInsuranceCourierPolicyPlanner
{
    public static List<FriendlyTeammateInsuredItem> ClaimedPolicies(
        IEnumerable<FollowerInsuranceCourierTransfer> transfers, IEnumerable<FriendlyTeammateInsuredItem> policies,
        IReadOnlyDictionary<string, string> ownedTemplates, IEnumerable<string> requestedIds)
    {
        var requested = requestedIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = policies.GroupBy(policy => policy.ItemId).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        var result = new List<FriendlyTeammateInsuredItem>();
        foreach (var group in transfers.GroupBy(transfer => transfer.MailItemId))
        {
            if (group.Count() != 1) continue;
            var transfer = group.Single();
            if (transfer.State != "complete" || !requested.Contains(transfer.MailItemId)
                || !ownedTemplates.TryGetValue(transfer.MailItemId, out var template) || template != transfer.TemplateId
                || !current.TryGetValue(transfer.MailItemId, out var policy) || policy.TraderId != transfer.TraderId) continue;
            result.Add(new() { ItemId = policy.ItemId, TraderId = policy.TraderId });
        }
        return result;
    }

    public static FollowerInsuranceRaidItem? Authorize(FollowerInsuranceRaidDiagnostic raid,
        FollowerInsuranceCourierTransfer transfer)
    {
        if (transfer.State != "pending" || transfer.ServerId != raid.ServerId
            || !FollowerInsuranceSettlementPlanner.HasCompleteFinalEvidence(raid)
            || !raid.ReceivedReportIds.Contains(transfer.ReportId)
            || !raid.CourierItemIds.Contains(transfer.SourceItemId, StringComparer.OrdinalIgnoreCase)
            || raid.PlayerItemIds.Contains(transfer.SourceItemId, StringComparer.OrdinalIgnoreCase)
            || raid.TransferItemIds.Contains(transfer.SourceItemId, StringComparer.OrdinalIgnoreCase)
            || raid.Participants.Any(p => p.SavedItemIds.Contains(transfer.SourceItemId, StringComparer.OrdinalIgnoreCase))
            || !raid.Participants.Any(p => p.Escaped && p.EscapedItemIds.Contains(transfer.SourceItemId, StringComparer.OrdinalIgnoreCase)))
            return null;

        var findings = FollowerInsuranceRaidClassifier.Classify(raid).Where(f =>
            string.Equals(f.ItemId, transfer.SourceItemId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (findings.Count != 1 || findings[0].Status != "recovered"
            || findings[0].Reason != "pitfireteam-courier") return null;

        var policies = raid.InsuredItems.Distinct().Where(p =>
            string.Equals(p.ItemId, transfer.SourceItemId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.TemplateId, transfer.TemplateId, StringComparison.OrdinalIgnoreCase)
            && p.OwnerAid == findings[0].OwnerAid && p.TraderId == findings[0].TraderId).ToList();
        return policies.Count == 1 ? policies[0] : null;
    }
}
