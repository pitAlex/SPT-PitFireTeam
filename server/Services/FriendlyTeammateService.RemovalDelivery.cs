using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    // A player-side receipt survives mail collection/deletion. Only the latest outbox can be active.
    private const string RemovalDeliveryReceiptKey = "pitFireTeam/removal-courier";

    private FriendlyTeammateDeletionJournal PrepareTeammateRemovalDelivery(MongoId sessionId, BotBase teammate, int price)
    {
        var journal = new FriendlyTeammateDeletionJournal { Aid = teammate.Aid!.Value, Price = price };
        var items = BuildCurrentTeammateKitDeliveryItems(teammate, IsCurrentLoadoutManagementModeExtreme());
        if (items.Count == 0) return journal;
        journal.DeliverySourceIds = items.Select(item => item.Id.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ids = journal.DeliverySourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var player = GetPlayerProfile(sessionId);
        if (player.Inventory?.Items?.Any(item => ids.Contains(item.Id.ToString())) == true
            || storage.ReadProfiles(sessionId).Any(other => other.Aid != teammate.Aid
                && other.Inventory?.Items?.Any(item => ids.Contains(item.Id.ToString())) == true))
            throw new FriendlyTeammateException("TeammateDeleteFailed");

        var profile = profileHelper.GetFullProfile(sessionId);
        journal.DeliveryReceipt = Math.Max(DateTime.UtcNow.Ticks,
            (profile.SptData?.Migrations?.GetValueOrDefault(RemovalDeliveryReceiptKey) ?? 0) + 1);
        var normalized = itemHelper.ReplaceIDs(items, null).ToList();
        var stash = new MongoId();
        foreach (var root in normalized.Where(item => item.ParentId == null))
        {
            root.ParentId = stash.ToString();
            root.SlotId = "main";
            root.Location = null;
        }
        string text = GetLanguageValue(languageService.GetStringMap(sessionId, "socialUi"),
            "RemovedTeammateEquipmentDelivery", "RemovedTeammateEquipmentDelivery");
        journal.Delivery = new Message
        {
            Id = new MongoId(), UserId = FriendlyCourierTraderProfile.CourierTraderId,
            MessageType = MessageType.NpcTraderMessage, DateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Text = text, HasRewards = true, RewardCollected = false, MaxStorageTime = 86400,
            Items = new MessageItems { Stash = stash, Data = normalized }
        };
        return journal;
    }

    private void FinishTeammateRemovalDelivery(MongoId sessionId, FriendlyTeammateDeletionJournal journal)
    {
        if (journal.Delivery == null || journal.DeliveryReceipt <= 0)
            throw new FriendlyTeammateException("TeammateDeleteDeliveryFailed");
        var profile = profileHelper.GetFullProfile(sessionId);
        profile.SptData ??= new Spt();
        profile.SptData.Migrations ??= [];
        bool alreadyDelivered = profile.SptData.Migrations.GetValueOrDefault(RemovalDeliveryReceiptKey) == journal.DeliveryReceipt;
        if (!alreadyDelivered)
        {
            profile.DialogueRecords ??= [];
            var trader = FriendlyCourierTraderProfile.CourierTraderId;
            if (!profile.DialogueRecords.TryGetValue(trader, out var dialogue))
                profile.DialogueRecords[trader] = dialogue = new Dialogue
                { Id = trader, Type = MessageType.NpcTraderMessage, Messages = [], Pinned = false, New = 0, AttachmentsNew = 0 };
            dialogue.Messages ??= [];
            if (!dialogue.Messages.Any(message => message.Id == journal.Delivery.Id))
            {
                // An interrupted outbox may resume days later; start the pickup window on delivery.
                journal.Delivery.DateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                dialogue.Messages.Add(cloner.Clone(journal.Delivery)!);
                dialogue.New = (dialogue.New ?? 0) + 1;
                dialogue.AttachmentsNew = (dialogue.AttachmentsNew ?? 0) + 1;
            }
            ClearRemovalDeliveryInsurance(profile, journal.DeliverySourceIds);
            profile.SptData.Migrations[RemovalDeliveryReceiptKey] = journal.DeliveryReceipt;
        }
        try
        {
            insuranceDiagnostics.ObserveMemberRemovalCourier(sessionId, journal.DeliverySourceIds);
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
            var saved = ReadSavedPlayerProfile(sessionId);
            if (saved?.SptData?.Migrations?.GetValueOrDefault(RemovalDeliveryReceiptKey) != journal.DeliveryReceipt)
                throw new IOException("Removal delivery receipt was not saved to the player profile");
            journal.State = "complete";
            storage.Write(sessionId, DeletionDocument, journal);
        }
        catch (Exception ex)
        {
            logger.Warning($"Removal courier remains pending for '{journal.Aid}': {ex}");
            throw new FriendlyTeammateException("TeammateDeleteDeliveryFailed");
        }
        if (!alreadyDelivered) _ = NotifyTeammateRemovalDelivery(sessionId, journal.Delivery);
    }

    private async Task NotifyTeammateRemovalDelivery(MongoId sessionId, Message delivery)
    {
        try { await notificationSendHelper.SendMessageAsync(sessionId, notifierHelper.CreateNewMessageNotification(delivery)); }
        catch (Exception ex) { logger.Warning($"Removal courier notification failed: {ex}"); }
    }

    private static void ClearRemovalDeliveryInsurance(SptProfile profile, IEnumerable<string> sourceIds)
    {
        var ids = sourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pmc = profile.CharacterData?.PmcData;
        if (pmc?.InsuredItems != null)
            pmc.InsuredItems = pmc.InsuredItems.Where(policy => policy.ItemId == null || !ids.Contains(policy.ItemId.ToString()!)).ToList();
        foreach (var package in profile.InsuranceList ?? [])
        {
            if (package.Items == null) continue;
            var remove = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool changed;
            do
            {
                changed = false;
                foreach (var item in package.Items)
                    if (item.ParentId != null && remove.Contains(item.ParentId)) changed |= remove.Add(item.Id.ToString());
            } while (changed);
            package.Items = package.Items.Where(item => !remove.Contains(item.Id.ToString())).ToList();
        }
    }
}
