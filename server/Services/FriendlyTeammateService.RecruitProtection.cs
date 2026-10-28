using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    // Invitations are another extraction destination, not an exemption from Restricted ownership.
    // Run before pricing/storage, and again for legacy pending previews and acceptance.
    internal bool SanitizeRecruitCandidateEquipment(MongoId sessionId, FriendlyRecruitPickupCandidate candidate)
    {
        if (!TryDeserializeRecruitProfile(candidate, out var recruit) || recruit.Inventory?.Items == null) return false;
        var protectedIds = GetProtectedTeammateItemIdsForExtraction(sessionId);
        protectedIds.UnionWith(FriendlyPostRaidService.SnapshotProtectedRaidItemIds(sessionId));
        if (protectedIds.Count == 0) return false;
        List<Item> items = recruit.Inventory.Items;
        int before = items.Count;
        var removedSlots = items.Where(item => item.ParentId == recruit.Inventory.Equipment?.ToString()
                && protectedIds.Contains(item.Id.ToString())).Select(item => item.SlotId).ToHashSet(StringComparer.Ordinal);
        if (!RemoveItemTreesById(items, protectedIds)) return false;
        candidate.ProfileJson = jsonUtil.Serialize(recruit)
            ?? throw new InvalidOperationException("Unable to serialize sanitized recruit equipment.");
        candidate.GivenEquipmentSlots = (candidate.GivenEquipmentSlots ?? [])
            .Where(slot => !removedSlots.Contains(slot)).ToArray();
        logger.Warning($"[RecruitProtection] Removed {before - items.Count} protected teammate item(s) from recruit '{candidate.ProfileId}'.");
        return true;
    }
}
