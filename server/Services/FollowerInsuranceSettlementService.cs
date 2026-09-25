using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Insurance;
using SPTarkov.Server.Core.Models.Spt.Services;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

/// <summary>Transfers retained policies to the PMC and schedules fully observed loss claims through SPT.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class FollowerInsuranceSettlementService(
    FriendlyTeammateStorage storage,
    ProfileHelper profileHelper,
    SaveServer saveServer,
    InsuranceService insuranceService,
    TradersTable tradersTable,
    JsonUtil jsonUtil,
    ISptLogger<FollowerInsuranceSettlementService> logger)
{
    public void TryTransferRetainedPlayerPolicies(MongoId sessionId, FollowerInsuranceRaidDiagnostic raid, string document)
    {
        var retained = FollowerInsuranceSettlementPlanner.PlanPlayerPolicyTransfer(raid);
        if (retained.Count == 0) return;

        var pmc = profileHelper.GetPmcProfile(sessionId);
        var profile = saveServer.GetProfile(sessionId);
        if (pmc?.Inventory?.Items == null || pmc.InsuredItems == null || profile.InsuranceList == null) return;

        var pendingIds = profile.InsuranceList.SelectMany(package => package.Items ?? [])
            .Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (insuranceService.InsuranceDictionaryExists(sessionId))
            pendingIds.UnionWith(insuranceService.GetInsurance(sessionId)?.Values
                .SelectMany(items => items).Select(item => item.Id.ToString()) ?? []);
        var additions = new List<InsuredItem>();
        var settingsByAid = new Dictionary<string, FriendlyTeammateSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in retained)
        {
            if (!MongoId.IsValidMongoId(policy.ItemId) || !MongoId.IsValidMongoId(policy.TraderId)
                || pendingIds.Contains(policy.ItemId)
                || tradersTable.GetTrader(new MongoId(policy.TraderId)) == null)
            {
                logger.Warning($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' invalid or pending itemId='{policy.ItemId}'; transfer deferred.");
                return;
            }
            var currentItems = pmc.Inventory.Items.Where(item =>
                string.Equals(item.Id.ToString(), policy.ItemId, StringComparison.OrdinalIgnoreCase)).ToList();
            var existing = pmc.InsuredItems.Where(item =>
                string.Equals(item.ItemId?.ToString(), policy.ItemId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (currentItems.Count != 1 || currentItems[0].Template.ToString() != policy.TemplateId
                || existing.Count > 1 || (existing.Count == 1
                    && !string.Equals(existing[0].TId.ToString(), policy.TraderId, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Warning($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' item or insurer conflict itemId='{policy.ItemId}'; transfer deferred.");
                return;
            }
            var participant = raid.Participants.SingleOrDefault(p => p.Aid == policy.OwnerAid);
            List<Item>? equipment = participant == null ? null : jsonUtil.Deserialize<List<Item>>(participant.InitialEquipmentJson);
            if (equipment?.Count(item => string.Equals(item.Id.ToString(), policy.ItemId, StringComparison.OrdinalIgnoreCase)
                && item.Template.ToString() == policy.TemplateId) != 1)
            {
                logger.Warning($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' starting item not verified itemId='{policy.ItemId}'; transfer deferred.");
                return;
            }
            if (existing.Count == 0)
                additions.Add(new InsuredItem { ItemId = new MongoId(policy.ItemId), TId = new MongoId(policy.TraderId) });

            if (!settingsByAid.TryGetValue(policy.OwnerAid, out var settings))
            {
                settings = storage.Read<FriendlyTeammateSettings>(sessionId, $"{policy.OwnerAid}-settings.json")
                    ?? new FriendlyTeammateSettings();
                settingsByAid.Add(policy.OwnerAid, settings);
            }
            if (settings.InsuredItems.Any(item => string.Equals(item.ItemId, policy.ItemId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.TraderId, policy.TraderId, StringComparison.OrdinalIgnoreCase)))
            {
                logger.Warning($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' follower insurer conflict itemId='{policy.ItemId}'; transfer deferred.");
                return;
            }
        }

        // Reserve before writing another store. An uncertain failure must not re-grant a policy after sale.
        raid.PlayerPolicyTransferState = "reserved";
        raid.PlayerPolicyTransferItemIds = retained.Select(item => item.ItemId).ToList();
        storage.Write(sessionId, document, raid);
        try
        {
            pmc.InsuredItems.AddRange(additions);
            if (additions.Count > 0) saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();

            var documents = new Dictionary<string, string>();
            foreach (var (aid, settings) in settingsByAid)
            {
                var ownedIds = retained.Where(item => item.OwnerAid == aid)
                    .Select(item => item.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int before = settings.InsuredItems.Count;
                settings.InsuredItems = settings.InsuredItems.Where(item => !ownedIds.Contains(item.ItemId)).ToList();
                if (settings.InsuredItems.Count != before)
                    documents[$"{aid}-settings.json"] = jsonUtil.Serialize(settings)
                        ?? throw new InvalidOperationException("Unable to serialize follower insurance settings.");
            }
            raid.PlayerPolicyTransferState = "complete";
            documents[document] = jsonUtil.Serialize(raid)
                ?? throw new InvalidOperationException("Unable to serialize raid insurance transfer.");
            storage.WriteBatch(sessionId, documents);
            logger.Info($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' state=complete items={retained.Count} added={additions.Count}");
        }
        catch (Exception ex)
        {
            raid.PlayerPolicyTransferState = "reserved";
            logger.Error($"[FollowerInsurance:PlayerTransfer] raidId='{raid.RaidId}' state=reserved-uncertain; automatic retry disabled: {ex}");
        }
    }

    public void TrySettle(MongoId sessionId, FollowerInsuranceRaidDiagnostic raid, string document)
    {
        if (!string.IsNullOrEmpty(raid.SettlementState)) return;
        var claims = FollowerInsuranceSettlementPlanner.Plan(raid);
        if (claims.Count == 0) return;

        var pmc = profileHelper.GetPmcProfile(sessionId);
        var profile = saveServer.GetProfile(sessionId);
        if (pmc?.Inventory?.Items == null || pmc.InsuredItems == null || profile.InsuranceList == null
            || string.IsNullOrWhiteSpace(raid.Location)) return;

        var playerIds = pmc.Inventory.Items.Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var playerPolicies = pmc.InsuredItems.Where(item => item.ItemId != null)
            .Select(item => item.ItemId!.Value.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pendingIds = profile.InsuranceList.SelectMany(package => package.Items ?? [])
            .Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (insuranceService.InsuranceDictionaryExists(sessionId)
            && insuranceService.GetInsurance(sessionId)?.Values.Any(items => items.Count > 0) == true)
        {
            logger.Warning($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' stock insurance staging is not empty; claim deferred.");
            return;
        }

        var packages = new List<InsuranceEquipmentPkg>();
        foreach (var claim in claims)
        {
            if (!MongoId.IsValidMongoId(claim.ItemId) || !MongoId.IsValidMongoId(claim.TraderId)
                || playerIds.Contains(claim.ItemId) || playerPolicies.Contains(claim.ItemId) || pendingIds.Contains(claim.ItemId))
            {
                logger.Warning($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' conflicting or invalid claim itemId='{claim.ItemId}'; no package scheduled.");
                return;
            }
            var participant = raid.Participants.SingleOrDefault(p => p.Aid == claim.OwnerAid);
            List<Item>? equipment = participant == null ? null : jsonUtil.Deserialize<List<Item>>(participant.InitialEquipmentJson);
            var source = equipment?.SingleOrDefault(item => item.Id.ToString() == claim.ItemId);
            if (source == null || source.Template.ToString() != claim.TemplateId
                || IsProtectedSource(source, equipment!)
                || tradersTable.GetTrader(new MongoId(claim.TraderId))?.Dialogue == null)
            {
                logger.Warning($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' source or insurer not verified itemId='{claim.ItemId}'; no package scheduled.");
                return;
            }
            packages.Add(new InsuranceEquipmentPkg
            {
                SessionId = sessionId, PmcData = pmc, TraderId = new MongoId(claim.TraderId), ItemToReturnToPlayer = source
            });
        }

        // This write is the durable idempotency boundary. On any uncertain crash/failure we fail closed,
        // rather than mint a second package after SPT may already have mailed the first one.
        raid.SettlementState = "reserved";
        raid.SettlementItemIds = claims.Select(claim => claim.ItemId).ToList();
        storage.Write(sessionId, document, raid);
        try
        {
            var existingPackages = profile.InsuranceList.ToArray();
            insuranceService.StoreGearLostInRaidToSendLater(sessionId, packages);
            insuranceService.StartPostRaidInsuranceLostProcess(pmc, sessionId, raid.Location);
            // SPT removes a processed package by trader + displayed date/time + location, not by
            // object identity. Player and follower packages made in the same second must be one
            // package or processing the earlier due time can erase the other without a return.
            foreach (var created in profile.InsuranceList.Where(package =>
                !existingPackages.Any(previous => ReferenceEquals(previous, package))).ToArray())
            {
                var matching = existingPackages.FirstOrDefault(previous =>
                    previous.TraderId == created.TraderId
                    && previous.SystemData?.Date == created.SystemData?.Date
                    && previous.SystemData?.Time == created.SystemData?.Time
                    && previous.SystemData?.Location == created.SystemData?.Location);
                if (matching == null) continue;
                matching.Items ??= [];
                matching.Items.AddRange(created.Items ?? []);
                profile.InsuranceList.Remove(created);
                logger.Info($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' merged same-second stock package traderId='{created.TraderId}'");
            }
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
            raid.SettlementState = "scheduled";
            storage.Write(sessionId, document, raid);
            logger.Info($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' state=scheduled items={packages.Count} traders={packages.Select(p => p.TraderId).Distinct().Count()} location='{raid.Location}'");
        }
        catch (Exception ex)
        {
            logger.Error($"[FollowerInsurance:Settlement] raidId='{raid.RaidId}' state=reserved-uncertain; automatic retry disabled: {ex}");
        }
    }

    private static bool IsProtectedSource(Item source, List<Item> equipment)
    {
        var byId = equipment.ToDictionary(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Item? current = source;
        while (current != null && visited.Add(current.Id.ToString()))
        {
            if (current.SlotId?.StartsWith("specialslot", StringComparison.OrdinalIgnoreCase) == true
                || string.Equals(current.SlotId, "SecuredContainer", StringComparison.OrdinalIgnoreCase)) return true;
            current = current.ParentId != null && byId.TryGetValue(current.ParentId, out var parent) ? parent : null;
        }
        return current != null; // Cyclic/malformed ancestry cannot become a claim.
    }
}
