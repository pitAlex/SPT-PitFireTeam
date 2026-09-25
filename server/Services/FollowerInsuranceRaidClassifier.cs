using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

/// <summary>Pure, conservative classification. A lost candidate needs the separate settlement gate.</summary>
public static class FollowerInsuranceRaidClassifier
{
    public static List<FollowerInsuranceRaidItem> DisplayPolicies(FollowerInsuranceRaidDiagnostic raid)
    {
        if (!raid.Enabled || raid.EndReceived) return [];
        return raid.InsuredItems.GroupBy(item => item.ItemId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => (item.OwnerAid, item.TraderId, item.TemplateId)).Distinct().Count() == 1)
            .Select(group => group.First()).ToList();
    }

    public static List<string> DeliveredSourceIds(IEnumerable<string> deliveredIds,
        IReadOnlyDictionary<string, List<string>>? sourceIdsByRoot)
    {
        var delivered = deliveredIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return delivered.Concat(sourceIdsByRoot?.Where(pair => delivered.Contains(pair.Key))
                .SelectMany(pair => pair.Value ?? []) ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<FollowerInsuranceRaidFinding> Classify(FollowerInsuranceRaidDiagnostic raid)
    {
        var player = raid.PlayerItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var courier = raid.CourierItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var transfers = raid.TransferItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var saved = raid.Participants.Where(p => p.OutcomeReceived)
            .SelectMany(p => p.SavedItemIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var escaped = raid.Participants.Where(p => p.OutcomeReceived && p.Escaped)
            .SelectMany(p => p.EscapedItemIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool outcomesKnown = raid.Participants.Count > 0 && raid.Participants.All(p =>
            p.OutcomeReceived && (!p.Escaped || p.EquipmentSnapshotKnown));
        var conflicts = raid.InsuredItems.GroupBy(i => i.ItemId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(i => (i.OwnerAid, i.TraderId)).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return raid.InsuredItems.Distinct().OrderBy(i => i.ItemId, StringComparer.Ordinal)
            .ThenBy(i => i.OwnerAid, StringComparer.Ordinal).Select(item =>
            {
                (string status, string reason) = !raid.Enabled ? ("ineligible", "insurance-mode-disabled")
                    : conflicts.Contains(item.ItemId) ? ("unresolved", "conflicting-snapshot-ownership")
                    : raid.AwaitingTransit ? ("deferred", "transit-chain-not-finished")
                    : !raid.EndReceived ? ("pending", "waiting-for-player-raid-end")
                    : courier.Contains(item.ItemId) ? ("recovered", "pitfireteam-courier")
                    : transfers.Contains(item.ItemId) ? ("recovered", "stock-transfer-service")
                    : player.Contains(item.ItemId) ? ("retained", "player-final-inventory")
                    : saved.Contains(item.ItemId) ? ("retained", "teammate-saved-equipment")
                    : escaped.Contains(item.ItemId) ? ("unresolved", "escaped-carrier-awaiting-destination")
                    : raid.IncompleteTransit ? ("unresolved", "transit-evidence-incomplete")
                    : !raid.PlayerInventoryKnown ? ("unresolved", "player-final-inventory-missing")
                    : !outcomesKnown ? ("unresolved", "teammate-outcomes-incomplete")
                    : !FollowerInsuranceRaidBarrier.IsComplete(raid) ? ("unresolved", "raid-reports-incomplete")
                    : ("lost-candidate", "absent-from-all-observed-return-paths");
                return new FollowerInsuranceRaidFinding(item.ItemId, item.TraderId, item.OwnerAid, status, reason);
            }).ToList();
    }
}
