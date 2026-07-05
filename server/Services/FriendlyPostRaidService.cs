using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Concurrent;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyPostRaidService(
    MailSendService mailSendService,
    NotificationSendHelper notificationSendHelper,
    DialogueHelper dialogueHelper,
    FriendlyLanguageService languageService,
    FriendlyTeammateService teammateService,
    TimeUtil timeUtil,
    SaveServer saveServer,
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    ISptLogger<FriendlyPostRaidService> logger
)
{
    private const string KillMessageKindTraitor = "traitor";
    private const string KillMessageKindJerk = "jerk";
    private static readonly ConcurrentDictionary<string, Dictionary<string, KillMessageRecord>> KillMessageRecords = new();
    private static readonly ConcurrentDictionary<string, HashSet<string>> ProtectedRaidItemIds = new();

    private static readonly string[] ReturnItemsMessages =
    [
        "Items received from your teammate. Ready for you to claim.",
        "Your teammate transferred these. Awaiting your confirmation.",
        "Your items are attached. Select 'Receive all' to collect them.",
        "Transfer from your teammate completed. Items are ready to be claimed.",
        "All items from your teammate are prepared. Confirm to receive.",
        "These were handed over by your teammate. Ready for pickup.",
    ];

    private static readonly string[] TeamEscapedMessages =
    [
        "Nice! We managed to get out.",
        "And that's a wrap! We made it out.",
        "Good run. Everyone made extract.",
    ];

    private static readonly string[] TeamSomeEscapedMessages =
    [
        "Well it's a shame about {0}, but at least the rest of us made it.",
        "A few of us got clipped, but some managed to get out alive.",
    ];

    private static readonly string[] FriendlyEscapedMessages =
    [
        "Glad we made it. Thanks for letting me tag along.",
        "Whew, glad I found you. Thanks for the help.",
        "Thanks for the help. I hauled what I could back.",
    ];

    public void HandleReturnItems(MongoId sessionId, FriendlyPostRaidReturnItemsRequest request)
    {
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> items = request.Items ?? [];
        StripProtectedTeammateItemsFromReturnItems(sessionId, items);
        if (items.Count == 0)
        {
            return;
        }

        RemoveReturnedItemsFromInsurance(sessionId, items);

        UserDialogInfo sender = GetDeliverySender();
        SendMessageDetails details = new()
        {
            RecipientId = sessionId,
            Sender = MessageType.NpcTraderMessage,
            DialogType = MessageType.NpcTraderMessage,
            SenderDetails = sender,
            Trader = FriendlyCourierTraderProfile.CourierTraderIdValue,
            MessageText = PickRandom(ReturnItemsMessages),
            Items = items,
            ItemsMaxStorageLifetimeSeconds = 86400,
        };

        mailSendService.SendMessageToPlayer(details);
        EnsureDialogHasSender(sessionId, sender);
    }

    private void StripProtectedTeammateItemsFromReturnItems(
        MongoId sessionId,
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        HashSet<string> protectedIds = teammateService.GetProtectedTeammateItemIdsForExtraction(sessionId);
        string sessionKey = sessionId.ToString();
        if (ProtectedRaidItemIds.TryGetValue(sessionKey, out HashSet<string>? registeredIds))
        {
            lock (registeredIds)
            {
                protectedIds.UnionWith(registeredIds);
            }
        }

        if (protectedIds.Count == 0)
        {
            return;
        }

        int removed = RemoveItemTreesById(items, protectedIds);
        if (removed > 0)
        {
            logger.Info($"Removed {removed} protected teammate item(s) from returned follower-loot delivery.");
        }
    }

    private void RemoveReturnedItemsFromInsurance(MongoId sessionId, List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> returnedItems)
    {
        HashSet<MongoId> returnedItemIds = returnedItems
            .Where(item => item != null)
            .Select(item => item.Id)
            .ToHashSet();
        if (returnedItemIds.Count == 0)
        {
            return;
        }

        try
        {
            SptProfile profile = saveServer.GetProfile(sessionId);
            PmcData? pmcData = profile?.CharacterData?.PmcData;
            int activeInsuranceRemoved = 0;
            int scheduledInsuranceRemoved = 0;

            // Match SPT's BTR delivery behavior: once an item is returned by a delivery service,
            // remove its insurance marker so post-raid insurance cannot schedule a duplicate copy.
            if (pmcData?.InsuredItems != null)
            {
                int beforeCount = pmcData.InsuredItems.Count;
                pmcData.InsuredItems = pmcData.InsuredItems
                    .Where(insuredItem => insuredItem?.ItemId == null || !returnedItemIds.Contains(insuredItem.ItemId.Value))
                    .ToList();
                activeInsuranceRemoved = beforeCount - pmcData.InsuredItems.Count;
            }

            if (profile?.InsuranceList != null)
            {
                foreach (SPTarkov.Server.Core.Models.Eft.Profile.Insurance insurancePackage in profile.InsuranceList.Where(package => package?.Items != null))
                {
                    HashSet<MongoId> packageRemovalIds = BuildReturnedInsuranceRemovalIds(insurancePackage.Items!, returnedItemIds);
                    if (packageRemovalIds.Count == 0)
                    {
                        continue;
                    }

                    int beforeCount = insurancePackage.Items!.Count;
                    insurancePackage.Items = insurancePackage.Items
                        .Where(item => item != null && !packageRemovalIds.Contains(item.Id))
                        .ToList();
                    scheduledInsuranceRemoved += beforeCount - insurancePackage.Items.Count;
                }
            }

            if (activeInsuranceRemoved > 0 || scheduledInsuranceRemoved > 0)
            {
                logger.Info(
                    $"Removed pitFireTeam-returned item(s) from insurance tracking: active={activeInsuranceRemoved}, scheduled={scheduledInsuranceRemoved}.");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to remove returned pitFireTeam item(s) from insurance tracking: {ex.Message}");
        }
    }

    private static HashSet<MongoId> BuildReturnedInsuranceRemovalIds(
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> insuranceItems,
        HashSet<MongoId> returnedItemIds)
    {
        HashSet<MongoId> idsToRemove = insuranceItems
            .Where(item => item != null && returnedItemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToHashSet();

        bool added;
        do
        {
            added = false;
            foreach (SPTarkov.Server.Core.Models.Eft.Common.Tables.Item item in insuranceItems)
            {
                if (item == null ||
                    idsToRemove.Contains(item.Id) ||
                    string.IsNullOrWhiteSpace(item.ParentId))
                {
                    continue;
                }

                if (idsToRemove.Any(parentId => string.Equals(item.ParentId, parentId.ToString(), StringComparison.Ordinal)))
                {
                    added |= idsToRemove.Add(item.Id);
                }
            }
        }
        while (added);

        return idsToRemove;
    }

    public void HandleTeamEscaped(MongoId sessionId, FriendlyPostRaidTeamEscapedRequest request)
    {
        FriendlyPostRaidMember? member = request.Member;
        if (member == null)
        {
            return;
        }

        UserDialogInfo sender = ToSenderInfo(member);
        FriendlyPostRaidSquadInfo squad = member.SquadInfo ?? new FriendlyPostRaidSquadInfo();

        string message = PickRandom(FriendlyEscapedMessages);
        if (squad.Mate)
        {
            message = PickRandom(TeamEscapedMessages);

            if (squad.Partial)
            {
                string lostText = "the others";
                if (squad.Lost.Count > 0 && squad.Lost.Count < 3)
                {
                    lostText = JoinNames(squad.Lost);
                }

                message = string.Format(PickRandom(TeamSomeEscapedMessages), lostText);
            }
        }

        notificationSendHelper.SendMessageToPlayer(sessionId, sender, message, MessageType.UserMessage);
    }

    public void RecordKillMessage(MongoId sessionId, FriendlyPostRaidKillMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VictimProfileId) || string.IsNullOrWhiteSpace(request.MessageKind))
        {
            return;
        }

        string kind = NormalizeKillMessageKind(request.MessageKind);
        if (string.IsNullOrEmpty(kind))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return;
        }

        Dictionary<string, KillMessageRecord> sessionRecords = KillMessageRecords.GetOrAdd(
            sessionId.ToString(),
            _ => new Dictionary<string, KillMessageRecord>(StringComparer.Ordinal));

        lock (sessionRecords)
        {
            var record = new KillMessageRecord(kind, request.MessageText);
            sessionRecords[request.VictimProfileId] = record;

            if (!string.IsNullOrWhiteSpace(request.VictimAccountId))
            {
                sessionRecords[$"aid:{request.VictimAccountId}"] = record;
            }
        }
    }

    public void RegisterProtectedRaidItems(MongoId sessionId, FriendlyPostRaidProtectedItemsRequest request)
    {
        if (request == null)
        {
            return;
        }

        string context = request.Context ?? "client registration";
        RemoveProtectedRaidItemIds(sessionId, request.RemoveItemIds, context);
        RegisterProtectedRaidItemIds(sessionId, request.ItemIds, context);
    }

    public void RegisterProtectedRaidItemIds(MongoId sessionId, IEnumerable<string>? itemIds, string context)
    {
        string[] ids = itemIds?
            .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (ids.Length == 0)
        {
            return;
        }

        HashSet<string> protectedIds = ProtectedRaidItemIds.GetOrAdd(
            sessionId.ToString(),
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        lock (protectedIds)
        {
            foreach (string itemId in ids)
            {
                protectedIds.Add(itemId);
            }
        }

        logger.Info($"Registered {ids.Length} protected teammate raid item id(s). context='{context}'.");
    }

    private void RemoveProtectedRaidItemIds(MongoId sessionId, IEnumerable<string>? itemIds, string context)
    {
        string[] ids = itemIds?
            .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (ids.Length == 0)
        {
            return;
        }

        string sessionKey = sessionId.ToString();
        if (!ProtectedRaidItemIds.TryGetValue(sessionKey, out HashSet<string>? protectedIds))
        {
            return;
        }

        int removed = 0;
        lock (protectedIds)
        {
            foreach (string itemId in ids)
            {
                if (protectedIds.Remove(itemId))
                {
                    removed++;
                }
            }

            if (protectedIds.Count == 0)
            {
                ProtectedRaidItemIds.TryRemove(sessionKey, out _);
            }
        }

        if (removed > 0)
        {
            logger.Info($"Unregistered {removed} protected teammate raid item id(s). context='{context}'.");
        }
    }

    public void RemoveProtectedTeammateItemsFromExtractedProfile(MongoId sessionId, EndLocalRaidRequestData request)
    {
        List<Item>? profileItems = request.Results?.Profile?.Inventory?.Items;
        if (profileItems == null || profileItems.Count == 0)
        {
            ProtectedRaidItemIds.TryRemove(sessionId.ToString(), out _);
            return;
        }

        string sessionKey = sessionId.ToString();
        // Server-side profile JSON covers the teammate's saved/default gear. Client-registered
        // ids cover live-only ownership events the backend cannot infer after raid end.
        HashSet<string> protectedIds = teammateService.GetProtectedTeammateItemIdsForExtraction(sessionId);
        if (ProtectedRaidItemIds.TryRemove(sessionKey, out HashSet<string>? registeredIds))
        {
            lock (registeredIds)
            {
                protectedIds.UnionWith(registeredIds);
            }
        }

        if (protectedIds.Count == 0)
        {
            return;
        }

        int requestRemoved = RemoveItemTreesById(profileItems, protectedIds);
        int savedRemoved = RemoveProtectedItemsFromSavedProfile(sessionId, protectedIds);
        int removed = requestRemoved + savedRemoved;
        if (removed > 0)
        {
            logger.Info(
                $"Removed protected teammate item(s) from extracted player inventory: request={requestRemoved}, savedProfile={savedRemoved}.");
        }
    }

    public void HandleEndLocalRaidKillMessages(MongoId sessionId, EndLocalRaidRequestData request)
    {
        List<Victim> pmcVictims = request.Results?.Profile?.Stats?.Eft?.Victims?
            .Where(IsPmcVictim)
            .Where(victim => victim?.ProfileId is not null)
            .Cast<Victim>()
            .ToList()
            ?? [];

        if (pmcVictims.Count == 0)
        {
            ClearKillMessageRecords(sessionId);
            return;
        }

        long recentMessageThreshold = timeUtil.GetTimeStamp() - 60;
        foreach (Victim victim in pmcVictims)
        {
            KillMessageRecord? record = GetPostRaidKillMessageRecord(sessionId, victim);
            if (record == null)
            {
                continue;
            }

            RemoveRecentVanillaPmcResponse(sessionId, victim, recentMessageThreshold);

            if (record.Kind == KillMessageKindTraitor || record.Kind == KillMessageKindJerk)
            {
                SendVictimMessage(sessionId, victim, record.MessageText);
            }
        }

        ClearKillMessageRecords(sessionId);
    }

    private int RemoveItemTreesById(List<Item> inventoryItems, IEnumerable<string> rootItemIds)
    {
        if (inventoryItems.Count == 0)
        {
            return 0;
        }

        HashSet<string> protectedIds = rootItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (protectedIds.Count == 0)
        {
            return 0;
        }

        Dictionary<string, Item> byId = inventoryItems
            .Where(item => item?.Id != null)
            .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Ammo can be split or merged into another stack, losing the original item id lineage.
        // Treat loose ammo as an explicit anti-farming exception, but re-key the extracted
        // player copy so the teammate profile can keep owning the original protected id.
        int copiedLooseAmmoRoots = AssignFreshIdsToExtractedProtectedCopies(inventoryItems, byId, protectedIds);
        if (copiedLooseAmmoRoots > 0)
        {
            logger.Info($"Assigned fresh item ids to {copiedLooseAmmoRoots} extracted protected ammo stack(s).");
        }

        if (protectedIds.Count == 0)
        {
            return 0;
        }

        HashSet<string> removeIds = BuildRemovalIdClosure(inventoryItems, protectedIds);
        // If a player-owned mod hangs from a protected teammate-owned parent, try to move that
        // player-owned child tree into the equipped backpack. If it does not fit, it remains in
        // removeIds and is lost with the protected parent; anti-farming wins over recovery.
        SalvageUnprotectedChildrenIntoBackpack(inventoryItems, protectedIds, removeIds);

        return inventoryItems.RemoveAll(item => item?.Id != null && removeIds.Contains(item.Id.ToString()));
    }

    private bool IsAmmoItem(Item? item)
    {
        return item?.Template != null
            && !item.Template.IsEmpty
            && itemHelper.IsOfBaseclass(item.Template, BaseClasses.AMMO);
    }

    private int AssignFreshIdsToExtractedProtectedCopies(
        List<Item> inventoryItems,
        Dictionary<string, Item> byId,
        HashSet<string> protectedIds)
    {
        var copyRootIds = protectedIds
            .Where(id =>
                byId.TryGetValue(id, out Item? item)
                && IsCopyableExtractedProtectedItem(item)
                && !HasProtectedAncestor(item, byId, protectedIds))
            .ToList();
        if (copyRootIds.Count == 0)
        {
            return 0;
        }

        HashSet<string> usedIds = inventoryItems
            .Where(item => item?.Id != null)
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int copied = 0;
        foreach (string rootId in copyRootIds)
        {
            if (!byId.TryGetValue(rootId, out Item? root)
                || root?.Id == null
                || !string.Equals(root.Id.ToString(), rootId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            HashSet<string> oldTreeIds = BuildItemTreeIds(inventoryItems, rootId);
            if (oldTreeIds.Count == 0)
            {
                continue;
            }

            var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string oldId in oldTreeIds)
            {
                if (!byId.ContainsKey(oldId))
                {
                    continue;
                }

                string newId;
                do
                {
                    newId = new MongoId().ToString();
                }
                while (!usedIds.Add(newId));

                idMap[oldId] = newId;
            }

            if (idMap.Count == 0)
            {
                continue;
            }

            foreach ((string oldId, string newId) in idMap)
            {
                if (byId.TryGetValue(oldId, out Item? item) && item != null)
                {
                    item.Id = new MongoId(newId);
                }
            }

            foreach (Item item in inventoryItems)
            {
                if (!string.IsNullOrWhiteSpace(item?.ParentId)
                    && idMap.TryGetValue(item.ParentId, out string? newParentId))
                {
                    item.ParentId = newParentId;
                }
            }

            foreach (string oldId in idMap.Keys)
            {
                protectedIds.Remove(oldId);
            }

            copied++;
        }

        return copied;
    }

    private bool IsCopyableExtractedProtectedItem(Item? item)
    {
        return IsAmmoItem(item);
    }

    private static bool HasProtectedAncestor(
        Item? item,
        Dictionary<string, Item> byId,
        HashSet<string> protectedIds)
    {
        string? parentId = item?.ParentId;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId))
        {
            if (protectedIds.Contains(parentId))
            {
                return true;
            }

            parentId = byId.TryGetValue(parentId, out Item? parent) ? parent.ParentId : null;
        }

        return false;
    }

    private static HashSet<string> BuildItemTreeIds(List<Item> inventoryItems, string rootId)
    {
        HashSet<string> treeIds = new(StringComparer.OrdinalIgnoreCase);
        AddItemTreeIds(inventoryItems, rootId, treeIds);
        return treeIds;
    }

    private static void AddItemTreeIds(List<Item> inventoryItems, string itemId, HashSet<string> treeIds)
    {
        if (!treeIds.Add(itemId))
        {
            return;
        }

        foreach (Item child in inventoryItems.Where(item =>
                     item?.Id != null &&
                     string.Equals(item.ParentId, itemId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            AddItemTreeIds(inventoryItems, child.Id.ToString(), treeIds);
        }
    }

    private static HashSet<string> BuildRemovalIdClosure(List<Item> inventoryItems, HashSet<string> rootItemIds)
    {
        HashSet<string> removeIds = new(rootItemIds, StringComparer.OrdinalIgnoreCase);
        bool foundChild = true;
        while (foundChild)
        {
            foundChild = false;
            foreach (Item item in inventoryItems)
            {
                if (item?.Id == null ||
                    string.IsNullOrWhiteSpace(item.ParentId) ||
                    !removeIds.Contains(item.ParentId))
                {
                    continue;
                }

                foundChild |= removeIds.Add(item.Id.ToString());
            }
        }

        return removeIds;
    }

    private int SalvageUnprotectedChildrenIntoBackpack(
        List<Item> inventoryItems,
        HashSet<string> protectedIds,
        HashSet<string> removeIds)
    {
        if (inventoryItems.Count == 0 || protectedIds.Count == 0 || removeIds.Count == 0)
        {
            return 0;
        }

        Item? backpack = FindPlayerBackpack(inventoryItems);
        if (backpack?.Id == null || removeIds.Contains(backpack.Id.ToString()))
        {
            return 0;
        }

        Dictionary<string, Item> byId = inventoryItems
            .Where(item => item?.Id != null)
            .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<Item>> byParent = inventoryItems
            .Where(item => item?.Id != null && !string.IsNullOrWhiteSpace(item.ParentId))
            .GroupBy(item => item.ParentId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        int salvaged = 0;
        foreach (Item candidate in inventoryItems.ToList())
        {
            if (candidate?.Id == null ||
                string.IsNullOrWhiteSpace(candidate.ParentId) ||
                protectedIds.Contains(candidate.Id.ToString()) ||
                !removeIds.Contains(candidate.Id.ToString()) ||
                !removeIds.Contains(candidate.ParentId) ||
                IsAmmoItem(candidate) ||
                HasUnprotectedRemovedAncestor(candidate, byId, protectedIds, removeIds))
            {
                continue;
            }

            // Candidate is the highest non-protected item under a protected chain. Its children
            // move together so linked mod trees stay coherent when possible.
            List<Item> candidateTree = BuildSalvageTree(candidate, byParent, protectedIds);
            if (candidateTree.Count == 0)
            {
                continue;
            }

            if (!TryPlaceItemTreeInBackpack(inventoryItems, backpack, candidateTree, removeIds))
            {
                continue;
            }

            foreach (Item salvagedItem in candidateTree)
            {
                if (salvagedItem?.Id != null)
                {
                    removeIds.Remove(salvagedItem.Id.ToString());
                }
            }

            salvaged++;
        }

        return salvaged;
    }

    private static Item? FindPlayerBackpack(List<Item> inventoryItems)
    {
        Dictionary<string, Item> byId = inventoryItems
            .Where(item => item?.Id != null)
            .GroupBy(item => item.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return inventoryItems.FirstOrDefault(item =>
            item?.Id != null &&
            string.Equals(item.SlotId, nameof(EquipmentSlots.Backpack), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.ParentId) &&
            byId.TryGetValue(item.ParentId, out Item? parent) &&
            string.IsNullOrWhiteSpace(parent.ParentId));
    }

    private static bool HasUnprotectedRemovedAncestor(
        Item item,
        Dictionary<string, Item> byId,
        HashSet<string> protectedIds,
        HashSet<string> removeIds)
    {
        string? parentId = item.ParentId;
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(parentId) && removeIds.Contains(parentId) && visited.Add(parentId))
        {
            if (!protectedIds.Contains(parentId))
            {
                return true;
            }

            parentId = byId.TryGetValue(parentId, out Item? parent) ? parent.ParentId : null;
        }

        return false;
    }

    private static List<Item> BuildSalvageTree(
        Item root,
        Dictionary<string, List<Item>> byParent,
        HashSet<string> protectedIds)
    {
        List<Item> tree = [];
        Queue<Item> queue = new();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Item item = queue.Dequeue();
            if (item?.Id == null || protectedIds.Contains(item.Id.ToString()))
            {
                continue;
            }

            tree.Add(item);
            if (!byParent.TryGetValue(item.Id.ToString(), out List<Item>? children))
            {
                continue;
            }

            foreach (Item child in children)
            {
                queue.Enqueue(child);
            }
        }

        return tree;
    }

    private bool TryPlaceItemTreeInBackpack(
        List<Item> inventoryItems,
        Item backpack,
        List<Item> itemTree,
        HashSet<string> removeIds)
    {
        try
        {
            if (backpack?.Id == null || itemTree.Count == 0)
            {
                return false;
            }

            int[,] blankMap = inventoryHelper.GetContainerSlotMap(backpack.Template);
            int horizontal = blankMap.GetLength(1);
            int vertical = blankMap.GetLength(0);
            List<Item> inventoryAfterProtectedRemoval = inventoryItems
                .Where(item => item?.Id != null && !removeIds.Contains(item.Id.ToString()))
                .ToList();
            int[,] backpackMap = inventoryHelper.GetContainerMap(horizontal, vertical, inventoryAfterProtectedRemoval, backpack.Id);
            var placement = inventoryHelper.PlaceItemInContainer(backpackMap, itemTree, backpack.Id.ToString(), "main");
            return placement.Success.GetValueOrDefault(false);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to salvage teammate-linked child item '{itemTree.FirstOrDefault()?.Id}' into backpack: {ex.Message}");
            return false;
        }
    }

    private int RemoveProtectedItemsFromSavedProfile(MongoId sessionId, HashSet<string> protectedIds)
    {
        try
        {
            SptProfile profile = saveServer.GetProfile(sessionId);
            List<Item>? savedItems = profile?.CharacterData?.PmcData?.Inventory?.Items;
            return savedItems == null ? 0 : RemoveItemTreesById(savedItems, protectedIds);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to strip protected teammate item(s) from saved profile fallback: {ex.Message}");
            return 0;
        }
    }

    public void HandleDeathEscapeSummary(MongoId sessionId, FriendlyTeammateDeathEscapeSummary summary)
    {
        if (summary.EscapedNames.Count == 0 && summary.LostNames.Count == 0)
        {
            return;
        }

        Dictionary<string, string> deathEscapeText = languageService.GetStringMap(sessionId, "deathEscape");
        string madeItOutTemplate = GetLanguageValue(deathEscapeText, "MadeItOut");
        string lostTemplate = GetLanguageValue(deathEscapeText, "Lost");
        string extractRouteTemplate = GetLanguageValue(deathEscapeText, "ExtractRoute");

        // Player-death escape reports are separate from the normal "team escaped with player"
        // messages because the player did not extract with the squad. Put each result on its own
        // line so mixed escaped/lost outcomes stay readable in post-raid mail.
        var parts = new List<string>();
        if (summary.EscapedNames.Count > 0)
        {
            parts.Add(string.Format(madeItOutTemplate, JoinNames(summary.EscapedNames)));
        }

        if (summary.LostNames.Count > 0)
        {
            parts.Add(string.Format(lostTemplate, JoinNames(summary.LostNames)));
        }

        if (!string.IsNullOrWhiteSpace(summary.ExtractName))
        {
            parts.Add(string.Format(extractRouteTemplate, summary.ExtractName));
        }

        string[] deathEscapeMessages = languageService.GetStringArray(sessionId, "deathEscapeMessages");
        string messageTemplate = deathEscapeMessages.Length > 0 ? PickRandom(deathEscapeMessages) : "{0}";
        string message = string.Format(messageTemplate, string.Join("\n", parts));

        UserDialogInfo sender = GetDeliverySender();
        SendMessageDetails details = new()
        {
            RecipientId = sessionId,
            Sender = MessageType.NpcTraderMessage,
            DialogType = MessageType.NpcTraderMessage,
            SenderDetails = sender,
            Trader = FriendlyCourierTraderProfile.CourierTraderIdValue,
            MessageText = message,
        };

        mailSendService.SendMessageToPlayer(details);
        EnsureDialogHasSender(sessionId, sender);
    }

    private KillMessageRecord? GetPostRaidKillMessageRecord(MongoId sessionId, Victim victim)
    {
        if (teammateService.IsTeammateIdentity(sessionId, victim.ProfileId, victim.AccountId))
        {
            return new KillMessageRecord("teammate", string.Empty);
        }

        return GetRecordedKillMessageRecord(sessionId, victim);
    }

    private KillMessageRecord? GetRecordedKillMessageRecord(MongoId sessionId, Victim victim)
    {
        if (!KillMessageRecords.TryGetValue(sessionId.ToString(), out var records))
        {
            return null;
        }

        lock (records)
        {
            if (victim.ProfileId is not null && records.TryGetValue(victim.ProfileId.Value.ToString(), out KillMessageRecord? byProfileId))
            {
                return byProfileId;
            }

            if (!string.IsNullOrWhiteSpace(victim.AccountId) && records.TryGetValue($"aid:{victim.AccountId}", out KillMessageRecord? byAid))
            {
                return byAid;
            }
        }

        return null;
    }

    private void RemoveRecentVanillaPmcResponse(MongoId sessionId, Victim victim, long recentMessageThreshold)
    {
        if (victim.ProfileId is null)
        {
            return;
        }

        try
        {
            Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.Dialogue> dialogs = dialogueHelper.GetDialogsForProfile(sessionId);
            if (!dialogs.TryGetValue(victim.ProfileId.Value, out var dialog) || dialog.Messages == null)
            {
                return;
            }

            int removeCount = dialog.Messages.RemoveAll(message =>
                message.UserId == victim.ProfileId.Value
                && message.MessageType == MessageType.UserMessage
                && message.Items is null
                && message.DateTime >= recentMessageThreshold);

            if (removeCount > 0 && dialog.New is > 0)
            {
                dialog.New = Math.Max(0, dialog.New.Value - removeCount);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to remove vanilla PMC response for victim '{victim.ProfileId}': {ex.Message}");
        }
    }

    private void SendVictimMessage(MongoId sessionId, Victim victim, string message)
    {
        if (victim.ProfileId is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        notificationSendHelper.SendMessageToPlayer(sessionId, ToSenderInfo(victim), message, MessageType.UserMessage);
    }

    private static UserDialogInfo ToSenderInfo(Victim victim)
    {
        int aid = 0;
        if (!string.IsNullOrWhiteSpace(victim.AccountId))
        {
            int.TryParse(victim.AccountId, out aid);
        }

        return new UserDialogInfo
        {
            Id = victim.ProfileId!.Value,
            Aid = aid,
            Info = new UserDialogDetails
            {
                Nickname = string.IsNullOrWhiteSpace(victim.Name) ? "PMC" : victim.Name,
                Side = string.IsNullOrWhiteSpace(victim.Side) ? "Usec" : victim.Side,
                Level = victim.Level ?? 1,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private static bool IsPmcVictim(Victim? victim)
    {
        return string.Equals(victim?.Role, "pmcBEAR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(victim?.Role, "pmcUSEC", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKillMessageKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            KillMessageKindTraitor => KillMessageKindTraitor,
            KillMessageKindJerk => KillMessageKindJerk,
            _ => string.Empty,
        };
    }

    private static void ClearKillMessageRecords(MongoId sessionId)
    {
        KillMessageRecords.TryRemove(sessionId.ToString(), out _);
    }

    private sealed record KillMessageRecord(string Kind, string MessageText);

    private void EnsureDialogHasSender(MongoId sessionId, UserDialogInfo sender)
    {
        try
        {
            Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.Dialogue> dialogs = dialogueHelper.GetDialogsForProfile(sessionId);
            if (!dialogs.TryGetValue(sender.Id, out var dialog) || dialog is null)
            {
                return;
            }

            dialog.Users ??= [];
            if (dialog.Users.All(user => user.Id != sender.Id))
            {
                dialog.Users.Add(sender);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to ensure sender details in post-raid dialog: {ex.Message}");
        }
    }

    private static UserDialogInfo GetDeliverySender()
    {
        return new UserDialogInfo
        {
            Id = FriendlyCourierTraderProfile.CourierTraderId,
            Aid = FriendlyCourierTraderProfile.CourierAid,
            Info = new UserDialogDetails
            {
                Nickname = FriendlyCourierTraderProfile.CourierNickname,
                Side = "Usec",
                Level = 1,
                MemberCategory = MemberCategory.Trader,
                SelectedMemberCategory = MemberCategory.Trader,
            },
        };
    }

    private static UserDialogInfo ToSenderInfo(FriendlyPostRaidMember member)
    {
        int parsedAid = 0;
        if (!string.IsNullOrWhiteSpace(member.Aid))
        {
            int.TryParse(member.Aid, out parsedAid);
        }

        MongoId senderId;
        if (!TryParseMongoId(member.Id, out senderId))
        {
            senderId = new MongoId("67b0f29e151899410b04aacb");
        }

        return new UserDialogInfo
        {
            Id = senderId,
            Aid = parsedAid,
            Info = member.Info ?? new UserDialogDetails
            {
                Nickname = "Squadmate",
                Side = "Usec",
                Level = 1,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private static bool TryParseMongoId(string? value, out MongoId mongoId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                mongoId = new MongoId(value);
                return true;
            }
            catch
            {
                // Fallback handled by caller.
            }
        }

        mongoId = default;
        return false;
    }

    private static string JoinNames(List<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count == 2)
        {
            return $"{names[0]} and {names[1]}";
        }

        return string.Join(", ", names.Take(names.Count - 1)) + $", and {names[^1]}";
    }

    private static string PickRandom(string[] values)
    {
        if (values.Length == 0)
        {
            return string.Empty;
        }

        int index = Random.Shared.Next(values.Length);
        return values[index];
    }

    private static string GetLanguageValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "{0}";
    }
}
