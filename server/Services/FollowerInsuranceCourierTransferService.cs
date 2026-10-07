using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

/// <summary>Durable handoff from original follower coverage to an actually claimed courier item.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class FollowerInsuranceCourierTransferService(
    FriendlyTeammateStorage storage,
    ProfileHelper profileHelper,
    SaveServer saveServer,
    FriendlyServerSettingsService settingsService,
    TradersTable tradersTable,
    JsonUtil jsonUtil,
    ISptLogger<FollowerInsuranceCourierTransferService> logger)
{
    private const string Document = "insurance-courier-transfers.json";

    public void CancelUnclaimed(MongoId sessionId)
    {
        var transfers = storage.Read<FollowerInsuranceCourierTransfers>(sessionId, Document);
        if (transfers == null) return;
        int cancelled = 0;
        foreach (var transfer in transfers.Items.Where(t => t.State is "pending" or "authorized"))
        {
            transfer.State = "cancelled";
            cancelled++;
        }
        if (cancelled == 0) return;
        storage.Write(sessionId, Document, transfers);
        logger.Info($"[FollowerInsurance:CourierTransfer] state=cancelled items={cancelled} reason=insurance-mode-exit");
    }

    // Called only after stock mail accepts the return. The stock mail service clones and re-IDs
    // the flat list in order; a changed shape is not safe to map and therefore grants nothing.
    public void ObserveDelivered(MongoId sessionId, FriendlyPostRaidReturnItemsRequest request,
        IReadOnlyList<Item> returnedItems, IReadOnlyList<Item>? mailedItems,
        IReadOnlyCollection<string> deliveredSourceIds, IReadOnlyCollection<string> acceptedMailIds)
    {
        if (string.IsNullOrWhiteSpace(request.InsuranceServerId)
            || !Guid.TryParseExact(request.InsuranceReportId, "N", out _)
            || request.InsuranceSourceItemIdByReturnId == null
            || returnedItems.Count != mailedItems?.Count) return;

        var delivered = deliveredSourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accepted = acceptedMailIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceMap = request.InsuranceSourceItemIdByReturnId;
        var sourceCounts = sourceMap.Values.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var transfers = storage.Read<FollowerInsuranceCourierTransfers>(sessionId, Document)
            ?? new FollowerInsuranceCourierTransfers();
        int added = 0;
        for (int i = 0; i < returnedItems.Count; i++)
        {
            var before = returnedItems[i];
            var after = mailedItems![i];
            if (before == null || after == null || before.Template != after.Template
                || !sourceMap.TryGetValue(before.Id.ToString(), out string? sourceId)
                || !delivered.Contains(sourceId) || sourceCounts[sourceId] != 1
                || !MongoId.IsValidMongoId(sourceId) || !MongoId.IsValidMongoId(after.Id.ToString())
                || !accepted.Contains(after.Id.ToString())) continue;

            string mailId = after.Id.ToString();
            if (transfers.Items.Any(t => string.Equals(t.MailItemId, mailId, StringComparison.OrdinalIgnoreCase))) continue;
            transfers.Items.Add(new FollowerInsuranceCourierTransfer
            {
                ServerId = request.InsuranceServerId!, ReportId = request.InsuranceReportId!,
                SourceItemId = sourceId, MailItemId = mailId, TemplateId = after.Template.ToString(),
            });
            added++;
        }

        if (added == 0) return;
        storage.Write(sessionId, Document, transfers);
        logger.Info($"[FollowerInsurance:CourierTransfer] state=pending serverId='{request.InsuranceServerId}' items={added}");
    }

    public void TryAuthorize(MongoId sessionId, FollowerInsuranceRaidDiagnostic raid)
    {
        var transfers = storage.Read<FollowerInsuranceCourierTransfers>(sessionId, Document);
        if (transfers == null) return;
        int authorized = 0;
        foreach (var transfer in transfers.Items.Where(t => t.ServerId == raid.ServerId && t.State == "pending"))
        {
            var policy = FollowerInsuranceCourierPolicyPlanner.Authorize(raid, transfer);
            if (policy == null || !MongoId.IsValidMongoId(policy.TraderId)
                || tradersTable.GetTrader(new MongoId(policy.TraderId)) == null
                || transfers.Items.Count(t => string.Equals(t.SourceItemId, transfer.SourceItemId, StringComparison.OrdinalIgnoreCase)
                    && t.ServerId == transfer.ServerId) != 1)
                continue;
            var participant = raid.Participants.SingleOrDefault(p => p.Aid == policy.OwnerAid);
            var equipment = participant == null ? null : jsonUtil.Deserialize<List<Item>>(participant.InitialEquipmentJson);
            if (equipment?.Count(item => item.Id.ToString() == policy.ItemId
                && item.Template.ToString() == policy.TemplateId) != 1) continue;

            transfer.TraderId = policy.TraderId;
            transfer.State = "authorized";
            authorized++;
        }
        if (authorized == 0) return;
        storage.Write(sessionId, Document, transfers);
        logger.Info($"[FollowerInsurance:CourierTransfer] state=authorized raidId='{raid.RaidId}' items={authorized}");
    }

    // Run after SPT's item-event handler has moved mail items into the PMC inventory.
    public void TryApplyClaimed(MongoId sessionId)
    {
        if (!InsuranceModeEnabled())
        {
            CancelUnclaimed(sessionId);
            return;
        }
        foreach (string raidDocument in new[] { "insurance-raid-diagnostic.json", "insurance-previous-raid-diagnostic.json" })
        {
            var raid = storage.Read<FollowerInsuranceRaidDiagnostic>(sessionId, raidDocument);
            if (raid != null) TryAuthorize(sessionId, raid);
        }

        var transfers = storage.Read<FollowerInsuranceCourierTransfers>(sessionId, Document);
        var pmc = profileHelper.GetPmcProfile(sessionId);
        var profile = saveServer.GetProfile(sessionId);
        if (transfers == null || pmc?.Inventory?.Items == null || pmc.InsuredItems == null
            || profile.InsuranceList == null) return;

        var packageIds = profile.InsuranceList.SelectMany(package => package.Items ?? [])
            .Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var transfer in transfers.Items.Where(t => t.State == "authorized").ToArray())
        {
            if (!MongoId.IsValidMongoId(transfer.MailItemId) || !MongoId.IsValidMongoId(transfer.TraderId)
                || packageIds.Contains(transfer.MailItemId) || packageIds.Contains(transfer.SourceItemId)
                || pmc.Inventory.Items.Any(item => item.Id.ToString() == transfer.SourceItemId)
                || transfers.Items.Count(t => t.ServerId == transfer.ServerId
                    && string.Equals(t.SourceItemId, transfer.SourceItemId, StringComparison.OrdinalIgnoreCase)) != 1)
                continue;
            var items = pmc.Inventory.Items.Where(item => item.Id.ToString() == transfer.MailItemId).ToList();
            if (items.Count != 1 || items[0].Template.ToString() != transfer.TemplateId) continue;
            var existing = pmc.InsuredItems.Where(item => item.ItemId?.ToString() == transfer.MailItemId).ToList();
            if (existing.Count > 1 || (existing.Count == 1 && existing[0].TId.ToString() != transfer.TraderId))
            {
                logger.Warning($"[FollowerInsurance:CourierTransfer] conflicting PMC policy itemId='{transfer.MailItemId}'; no grant.");
                continue;
            }

            transfer.State = "reserved";
            storage.Write(sessionId, Document, transfers);
            try
            {
                if (existing.Count == 0)
                {
                    pmc.InsuredItems.Add(new InsuredItem
                    {
                        ItemId = new MongoId(transfer.MailItemId), TId = new MongoId(transfer.TraderId),
                    });
                    saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
                }
                transfer.State = "complete";
                storage.Write(sessionId, Document, transfers);
                logger.Info($"[FollowerInsurance:CourierTransfer] state=complete sourceId='{transfer.SourceItemId}' itemId='{transfer.MailItemId}' traderId='{transfer.TraderId}'");
            }
            catch (Exception ex)
            {
                logger.Error($"[FollowerInsurance:CourierTransfer] state=reserved-uncertain itemId='{transfer.MailItemId}'; automatic retry disabled: {ex}");
            }
        }
    }

    private bool InsuranceModeEnabled()
    {
        string mode = settingsService.LoadSettings().LoadoutManagementMode;
        return mode is "Immersive" or "Extreme";
    }
}
