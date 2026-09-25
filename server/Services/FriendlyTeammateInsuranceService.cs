using pitTeam.Server.Models;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Insurance;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace pitTeam.Server.Services;

/// <summary>
/// Owns follower-insurance quotes, purchase validation/payment, and existing-policy transfers.
/// </summary>
[Injectable]
public class FriendlyTeammateInsuranceService(
    FriendlyTeammateStorage storage,
    FriendlyServerSettingsService settingsService,
    ProfileHelper profileHelper,
    InsuranceService insuranceService,
    PaymentService paymentService,
    ItemHelper itemHelper,
    TradersTable tradersTable,
    GlobalTable globalTable,
    ISptLogger<FriendlyTeammateInsuranceService> logger
)
{
    // Mirrors InsuranceCompany's restricted runtime item classes.
    private static readonly MongoId[] RestrictedClasses =
    [
        BaseClasses.THROW_WEAP, BaseClasses.MEDS, BaseClasses.FOOD_DRINK, BaseClasses.STACKABLE_ITEM,
        BaseClasses.SPEC_ITEM, BaseClasses.KNIFE, BaseClasses.ARM_BAND, BaseClasses.COMPASS,
        BaseClasses.RADIO_TRANSMITTER, BaseClasses.MOB_CONTAINER, BaseClasses.REPAIR_KITS,
    ];

    public int Purchase(MongoId sessionId, PmcData paymentProfile, BotBase teammate,
        FriendlyTeammateSettings settings, FriendlyTeammateInsuranceRequest request, ItemEventRouterResponse output)
    {
        if (!IsFollowerInsuranceModeEnabled()) throw new FriendlyTeammateException("FollowerInsuranceUnavailable");
        if (string.IsNullOrWhiteSpace(request.TraderId)) throw new FriendlyTeammateException("FollowerInsuranceInvalidSelection");
        var traderId = new MongoId(request.TraderId);
        if (tradersTable.GetTrader(traderId)?.Base.Insurance?.Availability != true
            || paymentProfile.TradersInfo == null
            || !paymentProfile.TradersInfo.TryGetValue(traderId, out var relation)
            || relation.Unlocked != true || relation.Disabled == true)
            throw new FriendlyTeammateException("FollowerInsuranceUnavailable");

        var equipmentIds = GetEquipmentTreeIds(teammate);
        var equipment = (teammate.Inventory?.Items ?? []).ToDictionary(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        var covered = settings.InsuredItems.Select(policy => policy.ItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<Item> chargeable;
        int total;
        try
        {
            (chargeable, total) = FollowerInsurancePurchasePlanner.Plan(request.ItemIds ?? [], equipment, covered,
                item => equipmentIds.Contains(item.Id.ToString()) && item.Id != teammate.Inventory?.Equipment
                    && CanInsure(item, equipment),
                item => insuranceService.GetRoublePriceToInsureItemWithTrader(paymentProfile, item, traderId), request.QuotedTotal);
        }
        catch (InvalidOperationException ex) { throw new FriendlyTeammateException(ex.Message); }
        if (chargeable.Count == 0) return 0; // Lost response / repeat confirmation: no second payment.

        paymentService.PayMoney(paymentProfile, new ProcessBuyTradeRequestData
        {
            SchemeItems = [new IdWithCount { Id = Money.ROUBLES, Count = total }],
            TransactionId = traderId, Action = "SptInsure", Type = string.Empty,
            ItemId = MongoId.Empty(), Count = 0, SchemeId = 0,
        }, sessionId, output);
        if (output.Warnings is { Count: > 0 }) throw new FriendlyTeammateException("FollowerInsurancePaymentFailed");

        foreach (var item in chargeable)
        {
            if (covered.Add(item.Id.ToString()))
                settings.InsuredItems.Add(new() { ItemId = item.Id.ToString(), TraderId = traderId.ToString() });
            // Stock InsuranceController covers built-in soft inserts with the armor's premium.
            if (!itemHelper.ArmorItemHasRemovableOrSoftInsertSlots(item.Template)) continue;
            foreach (var insert in equipment.Values.Where(child => child.ParentId == item.Id.ToString()
                && !string.IsNullOrWhiteSpace(child.SlotId) && itemHelper.IsSoftInsertId(child.SlotId.ToLowerInvariant())))
                if (covered.Add(insert.Id.ToString()))
                    settings.InsuredItems.Add(new() { ItemId = insert.Id.ToString(), TraderId = traderId.ToString() });
        }
        profileHelper.AddSkillPointsToPlayer(paymentProfile, SkillTypes.Charisma, total / 200000d, true);
        return total;
    }

    private bool CanInsure(Item item, Dictionary<string, Item> equipment)
    {
        var template = itemHelper.GetItem(item.Template);
        var properties = template.Value?.Properties;
        if (!template.Key || properties == null || properties.InsuranceDisabled == true) return false;
        // A locked attachment (notably a built-in armor insert) is covered with its parent, never purchased separately.
        if (item.ParentId != null && equipment.TryGetValue(item.ParentId, out var parent))
        {
            var slot = itemHelper.GetItem(parent.Template).Value?.Properties?.Slots?
                .FirstOrDefault(slot => string.Equals(slot.Name, item.SlotId, StringComparison.OrdinalIgnoreCase));
            if (slot?.Properties?.Filters?.Any(filter => filter.Locked == true) == true) return false;
        }
        return properties.IsAlwaysAvailableForInsurance == true
            || ((!globalTable.Configuration.DiscardLimitsEnabled || properties.DiscardLimit.GetValueOrDefault(-1) < 0)
                && !RestrictedClasses.Any(baseClass => itemHelper.IsOfBaseclass(item.Template, baseClass)));
    }

    public static List<FriendlyTeammateInsuredItem> GetPlayerPolicies(BotBase player) =>
        (player.InsuredItems ?? []).Where(policy => policy.ItemId != null)
            .Select(policy => new FriendlyTeammateInsuredItem
            {
                ItemId = policy.ItemId!.Value.ToString(),
                TraderId = policy.TId.ToString(),
            }).ToList();

    public void ReconcileEquipmentCommit(BotBase player, BotBase teammate, FriendlyTeammateSettings settings,
        HashSet<string> originalPlayerIds, HashSet<string> originalFollowerIds,
        IReadOnlyDictionary<string, string> followerIdRemaps)
    {
        var playerItems = player.Inventory?.Items
            ?? throw new InvalidOperationException("Missing player inventory for insurance transfer.");
        var result = FollowerInsuranceReconciler.Reconcile(GetPlayerPolicies(player), settings.InsuredItems,
            originalPlayerIds, originalFollowerIds,
            playerItems.Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            GetEquipmentTreeIds(teammate), followerIdRemaps, IsFollowerInsuranceModeEnabled());
        player.InsuredItems = result.Player.Select(policy => new InsuredItem
        {
            ItemId = new MongoId(policy.ItemId), TId = new MongoId(policy.TraderId),
        }).ToList();
        settings.InsuredItems = result.Follower;
        logger.Info($"[FollowerInsurance:Transfer] teammateAid='{teammate.Aid}' playerPolicies={result.Player.Count} followerPolicies={result.Follower.Count}");
    }

    public bool PrunePolicies(BotBase teammate, FriendlyTeammateSettings settings)
    {
        var existing = settings.InsuredItems ?? [];
        var ids = GetEquipmentTreeIds(teammate);
        settings.InsuredItems = IsFollowerInsuranceModeEnabled()
            ? existing.Where(policy => ids.Contains(policy.ItemId)).Distinct().ToList()
            : [];
        return settings.InsuredItems.Count != existing.Count;
    }

    public void AugmentInsuranceCosts(
        MongoId sessionId,
        GetInsuranceCostRequestData request,
        GetInsuranceCostResponseData response)
    {
        try
        {
            if (!IsFollowerInsuranceModeEnabled()
                || request?.Items == null
                || request.Items.Count == 0
                || request.Traders == null
                || request.Traders.Count == 0)
            {
                return;
            }

            var requestedItemIds = request.Items
                .Select(itemId => itemId.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<FollowerInsuranceQuoteItem> followerItems = FindRequestedFollowerEquipmentItems(sessionId, requestedItemIds);
            if (followerItems.Count == 0)
            {
                return;
            }

            var pmcData = profileHelper.GetPmcProfile(sessionId);
            foreach (MongoId traderId in request.Traders)
            {
                if (!response.TryGetValue(traderId, out Dictionary<MongoId, double>? traderPrices))
                {
                    traderPrices = [];
                    response[traderId] = traderPrices;
                }

                foreach (FollowerInsuranceQuoteItem followerItem in followerItems)
                {
                    MongoId templateId = followerItem.Item.Template;
                    bool addedFollowerPrice = false;
                    if (!traderPrices.TryGetValue(templateId, out double price))
                    {
                        price = insuranceService.GetRoublePriceToInsureItemWithTrader(pmcData, followerItem.Item, traderId);
                        traderPrices[templateId] = price;
                        addedFollowerPrice = true;
                    }

                    logger.Info(
                        $"[FollowerInsurance:Quote] teammateAid='{followerItem.TeammateAid}' " +
                        $"itemId='{followerItem.Item.Id}' templateId='{templateId}' traderId='{traderId}' " +
                        $"priceRoubles={price} responseSource='{(addedFollowerPrice ? "follower" : "stock-or-cached")}'");
                }
            }
        }
        catch (Exception ex)
        {
            // Quote augmentation must never break ordinary player insurance.
            logger.Warning($"[FollowerInsurance:Quote] Unable to augment follower insurance prices: {ex.Message}");
        }
    }

    private List<FollowerInsuranceQuoteItem> FindRequestedFollowerEquipmentItems(
        MongoId sessionId,
        HashSet<string> requestedItemIds)
    {
        var result = new List<FollowerInsuranceQuoteItem>();
        foreach (BotBase teammate in storage.ReadProfiles(sessionId))
        {
            List<Item>? inventoryItems = teammate.Inventory?.Items;
            if (inventoryItems == null || inventoryItems.Count == 0 || teammate.Inventory?.Equipment == null)
            {
                continue;
            }

            HashSet<string> equipmentTreeIds = GetEquipmentTreeIds(teammate);
            string teammateAid = teammate.Aid?.ToString() ?? string.Empty;
            foreach (Item item in inventoryItems)
            {
                string itemId = item.Id.ToString();
                if (requestedItemIds.Contains(itemId) && equipmentTreeIds.Contains(itemId))
                {
                    result.Add(new FollowerInsuranceQuoteItem(teammateAid, item));
                }
            }
        }

        return result;
    }

    internal static HashSet<string> GetEquipmentTreeIds(BotBase teammate)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Item>? items = teammate.Inventory?.Items;
        string? equipmentRootId = teammate.Inventory?.Equipment?.ToString();
        if (items == null || items.Count == 0 || string.IsNullOrWhiteSpace(equipmentRootId))
        {
            return result;
        }

        result.Add(equipmentRootId);
        bool addedItem;
        do
        {
            addedItem = false;
            foreach (Item item in items)
            {
                string itemId = item.Id.ToString();
                string? parentId = item.ParentId?.ToString();
                if (!result.Contains(itemId)
                    && !string.IsNullOrWhiteSpace(parentId)
                    && result.Contains(parentId)
                    && result.Add(itemId))
                {
                    addedItem = true;
                }
            }
        }
        while (addedItem);

        return result;
    }

    private bool IsFollowerInsuranceModeEnabled()
    {
        string mode = settingsService.LoadSettings().LoadoutManagementMode?.Trim() ?? string.Empty;
        return string.Equals(mode, "Immersive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Extreme", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "Realistic", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FollowerInsuranceQuoteItem(string TeammateAid, Item Item);
}
