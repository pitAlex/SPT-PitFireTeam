using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private List<Item> AvailableTreatmentMedkits(PmcData player)
    {
        var items = player.Inventory?.Items?.ToList() ?? [];
        // Ambiguous ownership must never offer a spendable item.
        if (items.GroupBy(item => item.Id).Any(group => group.Count() != 1)) return [];
        var byId = ToItemDictionary(items);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { player.Inventory?.Stash, player.Inventory?.Equipment, player.Inventory?.SortingTable })
            if (root.HasValue) roots.Add(root.Value.ToString());
        return items.Where(item => IsTreatmentMedkit(item) && TreatmentMedkitResource(item) > 0
            && AccessibleTreatmentItem(item, byId, roots)
            // A medicine item should be a leaf; do not delete a malformed/modded container tree.
            && !items.Any(child => child.ParentId == item.Id.ToString())).ToList();
    }

    private static bool AccessibleTreatmentItem(Item item, IReadOnlyDictionary<string, Item> byId, HashSet<string> roots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Add(item.Id.ToString()))
        {
            if (item.Upd?.PinLockState == PinLockState.Locked) return false;
            if (roots.Contains(item.Id.ToString())) return true;
            if (item.ParentId == null || !byId.TryGetValue(item.ParentId, out item!)) return false;
        }
        return false;
    }

    private bool IsTreatmentMedkit(Item item)
    {
        var template = item.Template;
        var visited = new HashSet<MongoId>();
        while (visited.Add(template) && templateTable.Items.TryGetValue(template, out var definition))
        {
            if (template == BaseClasses.MED_KIT) return true;
            template = definition.Parent;
        }
        return false;
    }

    private double TreatmentMedkitResource(Item item)
    {
        if (!templateTable.Items.TryGetValue(item.Template, out var definition) || definition.Properties?.MaxHpResource is not > 0)
            return 0;
        double value = item.Upd?.MedKit?.HpResource ?? definition.Properties.MaxHpResource.Value;
        return double.IsFinite(value) && value > 0 && value <= definition.Properties.MaxHpResource.Value ? value : 0;
    }

    private Item FindTreatmentMedkit(PmcData player, string id) => AvailableTreatmentMedkits(player)
        .SingleOrDefault(item => item.Id.ToString() == id) ?? throw new FriendlyTeammateException("ProfileHealingMedkitUnavailable");

    private void PlanMedkitTreatment(PmcData player, FriendlyTeammateTreatmentJournal journal)
    {
        FriendlyTeammateTreatmentPricing.MissingHealth(journal.HealthAfter);
        var item = FindTreatmentMedkit(player, journal.MedkitId!);
        if (jsonUtil.Serialize(item) != journal.MedkitBefore)
            throw new FriendlyTeammateException("ProfileHealingMedkitUnavailable");
        double available = TreatmentMedkitResource(item);
        double originalResource = available;
        // Native HP treatment spends one medkit point per HP. hpResourceRate controls
        // animation throughput, not conversion. No effect-removal or surgery costs apply.
        foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
        {
            var hp = journal.HealthAfter.BodyParts![part].Health!;
            double restored = Math.Min(available, hp.Maximum!.Value - hp.Current!.Value);
            hp.Current += restored;
            available -= restored;
        }
        journal.MedkitResourceUsed = originalResource - available;
    }

    private void DeductTreatmentMedkit(PmcData player, FriendlyTeammateTreatmentJournal journal)
    {
        var item = FindTreatmentMedkit(player, journal.MedkitId!);
        double remaining = TreatmentMedkitResource(item) - journal.MedkitResourceUsed;
        if (remaining <= 0) player.Inventory!.Items!.Remove(item);
        else
        {
            item.Upd ??= new();
            item.Upd.MedKit = new() { HpResource = remaining };
        }
    }

    private string TreatmentPaymentSignature(PmcData player, FriendlyTeammateTreatmentJournal journal) => journal.MedkitId == null
        ? TreatmentPlayerSignature(player)
        : jsonUtil.Serialize(player.Inventory?.Items?.Where(item => item.Id.ToString() == journal.MedkitId).ToList())!;

    private FriendlyTeammateTreatmentResponse TreatmentReceipt(MongoId sessionId, BotBase teammate, FriendlyTeammateTreatmentJournal journal)
    {
        var result = TreatmentSnapshot(sessionId, teammate);
        result.CompletedToken = journal.State == "complete" ? journal.Token : null;
        if (journal.MedkitId != null)
        {
            result.UsedMedkitId = journal.MedkitId;
            var item = GetPlayerProfile(sessionId).Inventory?.Items?.SingleOrDefault(item => item.Id.ToString() == journal.MedkitId);
            result.MedkitItem = cloner.Clone(item);
            result.MedkitResourceRemaining = item == null ? 0 : TreatmentMedkitResource(item);
        }
        return result;
    }
}
