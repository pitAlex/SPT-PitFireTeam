using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

/// <summary>Pure claim gate; no client item data can create a policy or bypass missing raid evidence.</summary>
public static class FollowerInsuranceSettlementPlanner
{
    public static List<FollowerInsuranceRaidItem> Plan(FollowerInsuranceRaidDiagnostic raid)
    {
        if (!HasCompleteFinalEvidence(raid) || !string.IsNullOrEmpty(raid.SettlementState)) return [];
        var findings = FollowerInsuranceRaidClassifier.Classify(raid);
        if (findings.Any(f => f.Status is "pending" or "deferred" or "unresolved" or "ineligible")) return [];
        var lost = findings.Where(f => f.Status == "lost-candidate").ToList();
        if (lost.Count == 0 || lost.Select(f => f.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lost.Count)
            return [];
        var claims = lost.Select(f => raid.InsuredItems.FirstOrDefault(item => item.ItemId == f.ItemId
            && item.OwnerAid == f.OwnerAid && item.TraderId == f.TraderId)).ToList();
        return claims.Any(item => item == null) ? [] : claims.Cast<FollowerInsuranceRaidItem>().ToList();
    }

    public static List<FollowerInsuranceRaidItem> PlanPlayerPolicyTransfer(FollowerInsuranceRaidDiagnostic raid)
    {
        if (!HasCompleteFinalEvidence(raid) || !string.IsNullOrEmpty(raid.PlayerPolicyTransferState)) return [];
        var findings = FollowerInsuranceRaidClassifier.Classify(raid);
        if (findings.Any(f => f.Status is "pending" or "deferred" or "unresolved" or "ineligible")) return [];
        var retained = findings.Where(f => f.Status == "retained" && f.Reason == "player-final-inventory").ToList();
        if (retained.Count == 0 || retained.Select(f => f.ItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != retained.Count)
            return [];
        // Player ownership must be exclusive; the classifier favors the player when both saved inventories contain an ID.
        var saved = raid.Participants.SelectMany(p => p.SavedItemIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (retained.Any(f => saved.Contains(f.ItemId))) return [];
        var policies = new List<FollowerInsuranceRaidItem>();
        foreach (var finding in retained)
        {
            var matches = raid.InsuredItems.Distinct().Where(item => item.ItemId == finding.ItemId
                && item.OwnerAid == finding.OwnerAid && item.TraderId == finding.TraderId).ToList();
            if (matches.Count != 1) return [];
            policies.Add(matches[0]);
        }
        return policies;
    }

    private static bool HasCompleteFinalEvidence(FollowerInsuranceRaidDiagnostic raid) =>
        raid.Enabled && raid.SettlementEligible && raid.EndReceived && !raid.AwaitingTransit && !raid.IncompleteTransit
        && raid.PlayerInventoryKnown && FollowerInsuranceRaidBarrier.IsComplete(raid)
        && raid.Participants.Count > 0
        && raid.Participants.All(p => p.OutcomeReceived && (!p.Escaped || p.EquipmentSnapshotKnown));
}
