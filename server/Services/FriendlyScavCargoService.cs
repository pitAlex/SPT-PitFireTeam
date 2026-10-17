using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Helpers.Traders;

namespace pitTeam.Server.Services;

[Injectable(InjectionType.Singleton)]
public sealed class FriendlyScavCargoService(
    FriendlyTeammateStorage storage,
    FriendlyServerSettingsService settings,
    JsonUtil json,
    FileUtil files,
    TraderConfig traders,
    SaveServer saves,
    InRaidConfig inRaid,
    TraderHelper traderHelper,
    MailSendService mail,
    FriendlyPostRaidService returns,
    FollowerInsuranceRaidDiagnostics insurance,
    FriendlyLanguageService language,
    ISptLogger<FriendlyScavCargoService> logger)
{
    private const string Document = "scav-cargo.json";
    private const double SimulatedCoopStandingGain = 0.025;
    private const double StandingReceiptScale = 1_000_000;
    private readonly object gate = new();

    public sealed class StartIdentity
    {
        [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    }

    public void BeginRaid(MongoId sessionId, string? output)
    {
        FriendlyScavCargoHooks.Register(this);
        var body = json.Deserialize<FriendlyTeammateBodyResponse<StartIdentity>>(output ?? string.Empty);
        if (body?.Err is not (null or 0) || string.IsNullOrWhiteSpace(body?.Data?.ServerId)) return;
        lock (gate)
        {
            var ledger = Read(sessionId);
            if (ledger?.ServerId == body.Data.ServerId) return;
            Write(sessionId, new() { ServerId = body.Data.ServerId, Allegiance = settings.LoadSettings().IsAllegiance });
        }
    }

    public void Queue(MongoId sessionId, FriendlyScavCargoRequest request)
    {
        lock (gate)
        {
            var ledger = Read(sessionId);
            if (!FriendlyScavCargoHooks.Enabled || ledger?.Allegiance != true || ledger.ServerId != request.ServerId ||
                !Guid.TryParseExact(request.InsuranceReportId, "N", out _) ||
                string.IsNullOrWhiteSpace(request.ExitName) ||
                request.ScavProfileIds.Count is < 1 or > 3 || request.ScavProfileIds.Any(string.IsNullOrWhiteSpace) ||
                request.Items.Count > 2000 || request.Items.Any(i => i == null) ||
                request.Items.Select(i => i.Id).Distinct().Count() != request.Items.Count)
                throw new ArgumentException("Invalid Scav cargo raid report.");
            // First capture owns this raid. A retry cannot replace cargo or its partner evidence.
            if (ledger.Cargo != null) return;
            ledger.Cargo = request;
            ledger.MessageId = new MongoId().ToString();
            ledger.State = "queued";
            Write(sessionId, ledger);
            logger.Info($"[ScavCargo] queued serverId='{request.ServerId}' exit='{request.ExitName}' partners={request.ScavProfileIds.Count} items={request.Items.Count}");
        }
    }

    public static bool Matches(FriendlyScavCargoLedger? ledger, EndLocalRaidRequestData request) =>
        ledger?.Allegiance == true && ledger.Cargo != null && ledger.ServerId == request.ServerId &&
        !string.IsNullOrWhiteSpace(ledger.Cargo.ExitName) &&
        ledger.Cargo.ServerId == request.ServerId && ledger.Cargo.ExitName == request.Results?.ExitName &&
        ledger.Cargo.ScavProfileIds.Count > 0 &&
        ledger.Cargo.Transit == (request.Results?.Result == ExitStatus.TRANSIT) &&
        (request.Results?.Result is ExitStatus.SURVIVED or ExitStatus.RUNNER ||
            (request.Results?.Result == ExitStatus.TRANSIT && request.LocationTransit != null)) &&
        (string.Equals(request.Results.Profile?.Info?.Side, "Usec", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.Results.Profile?.Info?.Side, "Bear", StringComparison.OrdinalIgnoreCase));

    public void FinishRaid(MongoId sessionId, EndLocalRaidRequestData request)
    {
        FriendlyScavCargoHooks.Register(this);
        lock (gate)
        {
            var ledger = Read(sessionId);
            if (ledger == null || ledger.ServerId != request.ServerId || ledger.Cargo == null) return;
            if (Matches(ledger, request))
            {
                ApplySimulatedCoopStanding(sessionId, ledger);
                Deliver(sessionId, ledger, request);
            }
            else
            {
                ledger.State = "rejected";
                Write(sessionId, ledger);
                insurance.ObserveCourier(sessionId, [], ledger.ServerId, ledger.Cargo.InsuranceReportId);
            }
        }
    }

    private void ApplySimulatedCoopStanding(MongoId sessionId, FriendlyScavCargoLedger ledger)
    {
        // Use the same exit-name classification as native raid completion. Real co-op
        // rewards (including SVM-paid conversions and disabled gifts) are entirely native.
        // Transit settles a departing Scav's message/cargo only; it is not a new co-op reward.
        if (ledger.State == "rejected" || ledger.Cargo!.Transit || inRaid.CoopExtracts.Contains(ledger.Cargo.ExitName!)) return;
        var profile = saves.GetProfile(sessionId);
        var pmc = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("PMC profile unavailable.");
        var pmcFence = pmc.TradersInfo?[Traders.FENCE] ?? throw new InvalidOperationException("PMC Fence standing unavailable.");
        var scavFence = profile.CharacterData?.ScavData?.TradersInfo?[Traders.FENCE]
            ?? throw new InvalidOperationException("Scav Fence standing unavailable.");
        string key = "pitFireTeam/scav-coop/" + ledger.ServerId;
        string standingKey = key + "/standing";
        profile.SptData ??= new();
        profile.SptData.Migrations ??= [];
        if (profile.SptData.Migrations.TryGetValue(standingKey, out long standingReceipt))
        {
            // Native raid-end replay imports the client's pre-reward standing again.
            // Restore this raid's committed result, even after cargo was completed.
            pmcFence.Standing = Math.Clamp(standingReceipt / StandingReceiptScale,
                traders.Fence.PlayerRepMin, traders.Fence.PlayerRepMax);
        }
        else
        {
            // Older receipts recorded only that a gain happened. Do not guess a result
            // or award twice when upgrading an already completed legacy raid.
            if (ledger.State == "complete" || profile.SptData.Migrations.ContainsKey(key)) return;
            double before = pmcFence.Standing ?? throw new InvalidOperationException("Fence standing unavailable.");
            if (!double.IsFinite(before)) throw new InvalidOperationException("Invalid Fence standing.");
            pmcFence.Standing = Math.Round(Math.Clamp(before + SimulatedCoopStandingGain,
                traders.Fence.PlayerRepMin, traders.Fence.PlayerRepMax), 6);
            // Both receipt and authoritative result persist alongside native standing.
            standingReceipt = checked((long)Math.Round(pmcFence.Standing.Value * StandingReceiptScale));
            profile.SptData.Migrations[key] = 1;
            profile.SptData.Migrations[standingKey] = standingReceipt;
        }
        traderHelper.LevelUp(Traders.FENCE, pmc);
        pmcFence.LoyaltyLevel = Math.Max((int)pmcFence.LoyaltyLevel!, 1);
        scavFence.Standing = pmcFence.Standing;
        scavFence.LoyaltyLevel = pmcFence.LoyaltyLevel;

        if (saves.IsProfileInvalidOrUnloadable(sessionId)) throw new IOException("Scav extraction profile is not saveable.");
        saves.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
        var saved = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Profile.SptProfile>(
            files.ReadFile(Path.Combine("user", "profiles", $"{sessionId}.json")));
        if (saved?.SptData?.Migrations?.ContainsKey(key) != true ||
            saved.SptData.Migrations.TryGetValue(standingKey, out long savedReceipt) != true || savedReceipt != standingReceipt ||
            saved.CharacterData?.PmcData?.TradersInfo?[Traders.FENCE].Standing != pmcFence.Standing ||
            saved.CharacterData?.ScavData?.TradersInfo?[Traders.FENCE].Standing != scavFence.Standing)
            throw new IOException("Simulated co-op standing persistence was not confirmed.");
    }

    private void Deliver(MongoId sessionId, FriendlyScavCargoLedger ledger, EndLocalRaidRequestData request)
    {
        if (ledger.State is "complete" or "rejected")
        {
            insurance.ObserveCourier(sessionId, ledger.State == "complete" ? ledger.DeliveredIds : [],
                ledger.ServerId, ledger.Cargo!.InsuranceReportId);
            return;
        }
        var cargo = ledger.Cargo!;
        // One durable message ID identifies this delivery in both the ledger and profile.
        // If profile save succeeds but the receipt write fails, its mail proves delivery.
        var existing = saves.GetProfile(sessionId).DialogueRecords?.Values
            .SelectMany(dialog => dialog.Messages ?? []).FirstOrDefault(m => m.Id.ToString() == ledger.MessageId);
        if (existing != null)
        {
            ConfirmDelivery(sessionId, ledger.MessageId!, ledger.ExpectedMailItemCount ?? ledger.DeliveredIds.Count);
            ledger.State = "complete";
            Write(sessionId, ledger);
            insurance.ObserveCourier(sessionId, ledger.DeliveredIds, ledger.ServerId, cargo.InsuranceReportId);
            return;
        }
        var items = PrepareItems(sessionId, cargo, request);
        var ids = items.Select(i => i.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ledger.State = "prepared";
        ledger.NativeCoopGift = false;
        ledger.DeliveredIds = ids.ToList();
        ledger.ExpectedMailItemCount = items.Count;
        Write(sessionId, ledger);
        // Empty-handed companions still receive thanks at ordinary exits and transit.
        // Real co-op exits retain their native message/reward flow, including disabled gifts.
        if (items.Count > 0 || cargo.Transit || !inRaid.CoopExtracts.Contains(cargo.ExitName!))
        {
            returns.RemoveReturnedItemsFromInsurance(sessionId, items);
            var details = new SendMessageDetails
            {
                RecipientId = sessionId, Trader = Traders.FENCE,
                Sender = items.Count > 0 ? MessageType.MessageWithItems : MessageType.NpcTraderMessage,
                DialogType = MessageType.NpcTraderMessage,
                MessageText = language.GetStringArray(sessionId, items.Count > 0 ? "scavCargoReturn" : "scavTransitFarewell").First(),
                Items = items,
                ItemsMaxStorageLifetimeSeconds = (long)(traders.Fence.CoopExtractGift.GiftExpiryHours * 3600)
            };
            using (FriendlyScavCargoHooks.UseMessageId(details, new MongoId(ledger.MessageId!)))
                mail.SendMessageToPlayer(details);
            ConfirmDelivery(sessionId, ledger.MessageId!, ids.Count);
        }
        ledger.State = "complete";
        Write(sessionId, ledger);
        insurance.ObserveCourier(sessionId, ids, ledger.ServerId, cargo.InsuranceReportId);
        logger.Info($"[ScavCargo] complete serverId='{ledger.ServerId}' message='{ledger.MessageId}' items={items.Count}");
    }

    private List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> PrepareItems(MongoId sessionId, FriendlyScavCargoRequest cargo, EndLocalRaidRequestData request)
    {
        var items = json.Deserialize<List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>>(json.Serialize(cargo.Items)!) ?? [];
        // A transferred tree cannot also be claimed from the player's extracted inventory,
        // BTR delivery or protected saved squadmate equipment.
        var alreadyReturned = (request.Results?.Profile?.Inventory?.Items ?? [])
            .Concat(request.TransferItems?.Values.SelectMany(x => x) ?? [])
            .Select(i => i.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        items.RemoveAll(i => alreadyReturned.Contains(i.Id.ToString()));
        returns.FilterReturnItems(sessionId, items);
        var ids = items.Select(i => i.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string stash = new MongoId().ToString();
        foreach (var item in items)
            if (item.ParentId == null || !ids.Contains(item.ParentId)) { item.ParentId = stash; item.SlotId = "main"; item.Location = null; }
        return items;
    }

    internal sealed record CoopCargoDelivery(MongoId MessageId,
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> Items, bool Replay, Action<int> Prepared);

    internal CoopCargoDelivery? PrepareCoopCargo(MongoId sessionId, EndLocalRaidRequestData request)
    {
        lock (gate)
        {
            var ledger = Read(sessionId);
            if (ledger == null || ledger.Cargo?.Transit == true || !Matches(ledger, request) || request.Results?.ExitName is not string exitName ||
                !inRaid.CoopExtracts.Contains(exitName)) return null;
            var existing = saves.GetProfile(sessionId).DialogueRecords?.Values.SelectMany(d => d.Messages ?? [])
                .Any(m => m.Id.ToString() == ledger!.MessageId) == true;
            // Only suppress replay of our already combined native gift. Legacy/separate mail
            // must not suppress the ordinary native co-op reward.
            if (ledger!.NativeCoopGift && (existing || ledger.State == "complete"))
                return new(new MongoId(ledger.MessageId!), [], true, _ => { });
            if (ledger.State is "complete" or "rejected") return null;
            var items = PrepareItems(sessionId, ledger.Cargo!, request);
            if (items.Count == 0) return null;
            ledger.State = "prepared";
            ledger.NativeCoopGift = true;
            ledger.DeliveredIds = items.Select(i => i.Id.ToString()).ToList();
            ledger.ExpectedMailItemCount = null;
            Write(sessionId, ledger);
            returns.RemoveReturnedItemsFromInsurance(sessionId, items);
            return new(new MongoId(ledger.MessageId!), items, false, count =>
            {
                lock (gate)
                {
                    var current = Read(sessionId);
                    if (current == null || current.ServerId != ledger.ServerId || current.MessageId != ledger.MessageId)
                        throw new IOException("Scav cargo raid changed during native co-op delivery.");
                    current.ExpectedMailItemCount = count;
                    Write(sessionId, current);
                }
            });
        }
    }

    private FriendlyScavCargoLedger? Read(MongoId id) => storage.ReadModeDocument<FriendlyScavCargoLedger>(id, Document, true);
    private void Write(MongoId id, FriendlyScavCargoLedger ledger) => storage.WriteModeDocuments(id, true,
        new Dictionary<string, string> { [Document] = json.Serialize(ledger)! });

    private void ConfirmDelivery(MongoId sessionId, string messageId, int itemCount)
    {
        if (saves.IsProfileInvalidOrUnloadable(sessionId)) throw new IOException("Scav cargo profile is not saveable.");
        saves.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
        var saved = json.Deserialize<SPTarkov.Server.Core.Models.Eft.Profile.SptProfile>(
            files.ReadFile(Path.Combine("user", "profiles", $"{sessionId}.json")));
        var message = saved?.DialogueRecords?.Values.SelectMany(d => d.Messages ?? [])
            .FirstOrDefault(m => m.Id.ToString() == messageId);
        if (message == null || (message.Items?.Data?.Count ?? 0) != itemCount)
            throw new IOException("Scav cargo delivery persistence was not confirmed.");
    }
}
