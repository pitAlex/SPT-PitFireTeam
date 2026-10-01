using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using Path = System.IO.Path;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private static bool IsPaymentMoney(Item item) =>
        item.Template.ToString() == Constants.FriendlyItemTemplateIds.Currency.Roubles;

    private List<Item> SnapshotRefundMoney(IEnumerable<Item> items) =>
        cloner.Clone(items.Where(IsPaymentMoney).ToList())!;

    private void SavePlayerMoneyVerified(MongoId sessionId, string expected, string errorKey)
    {
        try
        {
            if (saveServer.IsProfileInvalidOrUnloadable(sessionId)) throw new IOException("Player profile is not saveable");
            saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
            // SPT can cache the attempted hash before a failed file write. A successful retry
            // may therefore skip writing: verify the file, not just the in-memory profile.
            var saved = ReadSavedPlayerProfile(sessionId);
            if (saved?.CharacterData?.PmcData == null || CreationMoneySignature(saved.CharacterData.PmcData) != expected)
                throw new IOException("Saved player money does not match the transaction");
        }
        catch (Exception ex)
        {
            logger.Warning($"Payment persistence verification failed for '{sessionId}': {ex}");
            throw new FriendlyTeammateException(errorKey);
        }
    }

    private SptProfile? ReadSavedPlayerProfile(MongoId sessionId) =>
        jsonUtil.Deserialize<SptProfile>(fileUtil.ReadFile(Path.Combine("user", "profiles", $"{sessionId}.json")));

    private void RestoreRefundMoney(PmcData player, List<Item>? refundItems,
        string? before, string? after, string errorKey)
    {
        string current = CreationMoneySignature(player);
        if (refundItems == null || (current != before && current != after))
            throw new FriendlyTeammateException(errorKey);
        var restored = player.Inventory!.Items!.Where(item => !IsPaymentMoney(item)).ToList();
        restored.AddRange(cloner.Clone(refundItems)!);
        var probe = cloner.Clone(player)!;
        probe.Inventory!.Items = restored;
        if (CreationMoneySignature(probe) != before) throw new FriendlyTeammateException(errorKey);
        player.Inventory.Items = restored;
    }
}
