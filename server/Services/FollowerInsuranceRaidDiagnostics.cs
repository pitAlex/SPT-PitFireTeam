using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

/// <summary>
/// Raid-correlated evidence ledger. Settlement is delegated only after every report is complete.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class FollowerInsuranceRaidDiagnostics(
    FriendlyTeammateStorage storage,
    FriendlyServerSettingsService settingsService,
    ProfileHelper profileHelper,
    JsonUtil jsonUtil,
    FollowerInsuranceSettlementService settlementService,
    ISptLogger<FollowerInsuranceRaidDiagnostics> logger)
{
    private const string Document = "insurance-raid-diagnostic.json";
    private const string PreviousDocument = "insurance-previous-raid-diagnostic.json";
    private readonly ConcurrentDictionary<string, object> locks = new();

    public List<FollowerInsuranceRaidItem> GetDisplayPolicies(MongoId sessionId, string? serverId)
    {
        try
        {
            lock (locks.GetOrAdd(sessionId.ToString(), _ => new object()))
            {
                var raid = storage.Read<FollowerInsuranceRaidDiagnostic>(sessionId, Document);
                if (raid == null || !FollowerInsuranceRaidBarrier.Matches(raid, serverId) || !ModeEnabled()) return [];
                return FollowerInsuranceRaidClassifier.DisplayPolicies(raid);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"[FollowerInsurance:RaidDisplay] Unable to read exact-raid policies: {ex}");
            return [];
        }
    }

    public void BeginRaid(MongoId sessionId, StartLocalRaidRequestData request, string? output) => Guard(sessionId, () =>
    {
        var body = jsonUtil.Deserialize<FriendlyTeammateBodyResponse<StartIdentity>>(output ?? string.Empty);
        if (body?.Err is not (null or 0) || string.IsNullOrWhiteSpace(body?.Data?.ServerId)) return;
        var identity = body.Data;
        var previous = storage.Read<FollowerInsuranceRaidDiagnostic>(sessionId, Document);
        if (previous?.ServerId == identity.ServerId) return;
        bool continuation = previous?.AwaitingTransit == true
            && !string.IsNullOrWhiteSpace(identity.Transition?.TransitionRaidId)
            && identity.Transition.TransitionRaidId == previous.TransitionId;
        if (previous != null && !continuation) storage.Write(sessionId, PreviousDocument, previous);
        var raid = continuation ? previous! : new FollowerInsuranceRaidDiagnostic();
        if (!continuation) raid.SettlementEligible = true;
        raid.IncompleteTransit |= !continuation && identity.Transition?.TransitionCount > 0;
        raid.ServerId = identity.ServerId!;
        raid.TransitionId = identity.Transition?.TransitionRaidId ?? string.Empty;
        raid.Location = request.Location ?? string.Empty;
        raid.Enabled = (!continuation || raid.Enabled) && ModeEnabled()
            && string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase);
        raid.EndReceived = false;
        raid.AwaitingTransit = false;
        raid.PlayerInventoryKnown = false;
        // An uncompleted transit segment can still have an unobserved courier delivery.
        if (continuation && !FollowerInsuranceRaidBarrier.IsComplete(raid)) raid.IncompleteTransit = true;
        raid.ReportsComplete = false;
        raid.ReportsFailed = false;
        raid.ExpectedReportIds.Clear();
        raid.ReceivedReportIds.Clear();
        raid.PlayerItemIds.Clear();
        if (continuation)
        {
            // Do not retain a previous map's carrier inventory as proof of final extraction.
            foreach (var participant in raid.Participants.Where(p => !p.OutcomeReceived || p.Escaped))
            {
                participant.OutcomeReceived = false;
                participant.EquipmentSnapshotKnown = false;
                participant.SavedItemIds.Clear();
                participant.EscapedItemIds.Clear();
            }
        }
        SaveAndReport(sessionId, raid, continuation ? "transit-start" : "raid-start");
        logger.Info($"[FollowerInsurance:RaidStart] raidId='{raid.RaidId}' serverId='{raid.ServerId}' enabled={raid.Enabled} continuation={continuation}");
    });

    public void CaptureGeneratedFollower(MongoId sessionId, BotBase? teammate) => Update(sessionId, "spawn", raid =>
    {
        if (!raid.Enabled || raid.EndReceived || teammate?.Aid == null) return;
        string aid = teammate.Aid.Value.ToString();
        if (raid.Participants.Any(p => p.Aid == aid)) return; // Retry or transit may not replace the initial policy snapshot.
        var settings = storage.Read<FriendlyTeammateSettings>(sessionId, $"{aid}-settings.json");
        var treeIds = FriendlyTeammateInsuranceService.GetEquipmentTreeIds(teammate);
        var items = (teammate.Inventory?.Items ?? []).Where(i => treeIds.Contains(i.Id.ToString())).ToList();
        raid.Participants.Add(new()
        {
            Aid = aid, ProfileId = teammate.Id.ToString() ?? string.Empty, Nickname = teammate.Info?.Nickname ?? aid,
            InitialEquipmentJson = jsonUtil.Serialize(items) ?? string.Empty,
        });
        foreach (var policy in settings?.InsuredItems ?? [])
        {
            var item = items.FirstOrDefault(i => i.Id.ToString() == policy.ItemId);
            if (item == null) continue;
            raid.InsuredItems.Add(new(item.Id.ToString(), item.Template.ToString(), policy.TraderId, aid,
                item.ParentId ?? string.Empty, item.SlotId ?? string.Empty));
            logger.Info($"[FollowerInsurance:RaidSnapshot] raidId='{raid.RaidId}' aid='{aid}' itemId='{item.Id}' templateId='{item.Template}' traderId='{policy.TraderId}' slot='{item.SlotId}'");
        }
    });

    public void ObserveOutcomes(MongoId sessionId, IEnumerable<FriendlyTeammateDeathEscapeEntry> entries,
        string? serverId, string? reportId) =>
        Update(sessionId, "outcomes", raid =>
        {
            if (!raid.Enabled || !AcceptReport(raid, serverId, reportId)) return;
            foreach (var entry in entries)
            {
                var participant = raid.Participants.FirstOrDefault(p => p.Aid == entry.Aid && p.ProfileId == entry.ProfileId);
                if (participant == null) continue;
                var saved = storage.Read<BotBase>(sessionId, $"{entry.Aid}.json");
                if (saved == null) continue;
                participant.OutcomeReceived = true;
                participant.Escaped = entry.Escaped;
                participant.EquipmentSnapshotKnown = entry.EquipmentItems is { Count: > 0 };
                participant.SavedItemIds = FriendlyTeammateInsuranceService.GetEquipmentTreeIds(saved).ToList();
                participant.EscapedItemIds = entry.Escaped ? ItemIds(entry.EquipmentItems) : [];
            }
        });

    // Call only after the existing mail service accepts the items, retaining IDs from before it mutates them.
    public void ObserveCourier(MongoId sessionId, IEnumerable<string> ids, string? serverId, string? reportId) =>
        Update(sessionId, "courier", raid =>
        {
            if (!AcceptReport(raid, serverId, reportId)) return;
            raid.CourierItemIds = raid.CourierItemIds.Concat(ids).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        });

    public void CompleteReports(MongoId sessionId, FollowerInsuranceRaidCompletionRequest request) =>
        Update(sessionId, "reports-complete", raid =>
        {
            bool accepted = FollowerInsuranceRaidBarrier.Complete(raid, request.InsuranceServerId, request.ReportIds, request.Failed);
            logger.Info($"[FollowerInsurance:ReportBarrier] serverId='{request.InsuranceServerId}' accepted={accepted} complete={FollowerInsuranceRaidBarrier.IsComplete(raid)} expected={raid.ExpectedReportIds.Count} received={raid.ReceivedReportIds.Count} failed={raid.ReportsFailed}");
        });

    private bool AcceptReport(FollowerInsuranceRaidDiagnostic raid, string? serverId, string? reportId)
    {
        bool accepted = FollowerInsuranceRaidBarrier.Observe(raid, serverId, reportId);
        if (!accepted)
            logger.Warning($"[FollowerInsurance:ReportRejected] expectedServerId='{raid.ServerId}' receivedServerId='{serverId}' reportId='{reportId}'");
        return accepted;
    }

    public void EndRaid(MongoId sessionId, EndLocalRaidRequestData request) => Update(sessionId, "raid-end", raid =>
    {
        if (!raid.Enabled || raid.ServerId != request.ServerId) return;
        raid.Enabled = ModeEnabled();
        raid.EndReceived = true;
        raid.AwaitingTransit = request.Results?.Result == ExitStatus.TRANSIT;
        if (raid.AwaitingTransit && !string.IsNullOrWhiteSpace(request.LocationTransit?.TransitionRaidId))
            raid.TransitionId = request.LocationTransit.TransitionRaidId;
        // This hook runs AFTER stock death/extraction processing. The request can still contain corpse gear.
        var playerItems = profileHelper.GetPmcProfile(sessionId)?.Inventory?.Items;
        raid.PlayerInventoryKnown = request.Results?.Result != null && playerItems != null;
        raid.PlayerItemIds = ItemIds(playerItems);
        raid.TransferItemIds = raid.TransferItemIds.Concat(
            request.TransferItems?.Where(pair => pair.Key == $"{Traders.BTR}_btr" || pair.Key == $"{Traders.BTR}_transit")
                .SelectMany(pair => ItemIds(pair.Value)) ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    });

    private void Update(MongoId sessionId, string source, Action<FollowerInsuranceRaidDiagnostic> change) => Guard(sessionId, () =>
    {
        var raid = storage.Read<FollowerInsuranceRaidDiagnostic>(sessionId, Document);
        if (raid == null) return;
        raid.Enabled &= ModeEnabled();
        change(raid);
        SaveAndReport(sessionId, raid, source);
    });

    private void SaveAndReport(MongoId sessionId, FollowerInsuranceRaidDiagnostic raid, string source)
    {
        var findings = FollowerInsuranceRaidClassifier.Classify(raid);
        string signature = jsonUtil.Serialize(findings) ?? string.Empty;
        bool changed = signature != raid.LastReportSignature;
        raid.Findings = findings;
        if (changed) { raid.LastReportSignature = signature; raid.Revision++; }
        storage.Write(sessionId, Document, raid);
        if (changed && findings.Count > 0)
        {
            foreach (var finding in findings)
                logger.Info($"[FollowerInsurance:RaidItem] raidId='{raid.RaidId}' revision={raid.Revision} source='{source}' aid='{finding.OwnerAid}' itemId='{finding.ItemId}' traderId='{finding.TraderId}' status='{finding.Status}' reason='{finding.Reason}'");
            logger.Info($"[FollowerInsurance:RaidReport] raidId='{raid.RaidId}' revision={raid.Revision} insured={findings.Count} retained={findings.Count(f => f.Status == "retained")} recovered={findings.Count(f => f.Status == "recovered")} lostCandidates={findings.Count(f => f.Status == "lost-candidate")} unresolved={findings.Count(f => f.Status == "unresolved")} deferred={raid.AwaitingTransit}");
        }
        settlementService.TryTransferRetainedPlayerPolicies(sessionId, raid, Document);
        settlementService.TrySettle(sessionId, raid, Document);
    }

    private void Guard(MongoId sessionId, Action action)
    {
        try { lock (locks.GetOrAdd(sessionId.ToString(), _ => new object())) action(); }
        catch (Exception ex) { logger.Warning($"[FollowerInsurance:RaidDiagnostic] Raid observation or settlement failed: {ex}"); }
    }

    private bool ModeEnabled()
    {
        string mode = settingsService.LoadSettings().LoadoutManagementMode?.Trim() ?? string.Empty;
        return mode.Equals("Immersive", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Extreme", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("Realistic", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ItemIds(IEnumerable<Item>? items) =>
        items?.Select(i => i.Id.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

    public sealed class StartIdentity
    {
        [JsonPropertyName("serverId")] public string? ServerId { get; set; }
        [JsonPropertyName("transition")] public Transition? Transition { get; set; }
    }
}
