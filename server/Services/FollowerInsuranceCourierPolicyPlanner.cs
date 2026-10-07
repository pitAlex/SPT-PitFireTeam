using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

public static class FollowerInsuranceCourierPolicyPlanner
{
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
