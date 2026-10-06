using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private const string CreationDocument = "pending-creation.json";
    private const int HiringPricingVersion = 2;
    // The service can be resolved by multiple routes; serialize quote/payment transitions across instances.
    internal static readonly object CreationLock = new();

    public FriendlyTeammateCreationPreview PrepareTeammateCreation(MongoId sessionId, FriendlyTeammateCreateRequest request)
    {
        if (settingsService.LoadSettings().IsAllegiance) throw new FriendlyTeammateException("SettingsUnavailableInAllegiance");
        lock (CreationLock)
        {
            RecoverTeammateCreation(sessionId);
            var teammate = GenerateNewTeammate(sessionId, request);
            SetHiringBayonet(teammate);
            int price;
            try
            {
                price = CalculateRecruitmentGearPrice(teammate);
            }
            catch (Exception ex)
            {
                logger.Warning($"Unable to quote teammate equipment: {ex}");
                throw new FriendlyTeammateException("TeammateHirePriceFailed");
            }
            var quote = new FriendlyTeammateCreationQuote
            {
                Token = Guid.NewGuid().ToString("N"), Teammate = teammate, Price = price, PricingVersion = HiringPricingVersion,
                Mode = NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode),
                ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
            };
            storage.Write(sessionId, CreationDocument, quote);
            return new() { QuoteToken = quote.Token, Aid = teammate.Aid!.Value.ToString(), Price = price };
        }
    }

    public bool TryGetPendingTeammateProfile(MongoId sessionId, string? aid, out GetOtherProfileResponse? profile)
    {
        lock (CreationLock)
        {
            profile = null;
            var quote = storage.Read<FriendlyTeammateCreationQuote>(sessionId, CreationDocument);
            if (quote?.State != "pending" || quote.PricingVersion != HiringPricingVersion
                || quote.ExpiresUtc <= DateTime.UtcNow || quote.Teammate.Aid?.ToString() != aid) return false;
            profile = ToOtherProfileResponse(quote.Teammate);
            return true;
        }
    }

    public FriendlyTeammateBuyKitResponse CreateTeammate(MongoId sessionId, FriendlyTeammateCreateRequest request)
    {
        if (settingsService.LoadSettings().IsAllegiance) throw new FriendlyTeammateException("SettingsUnavailableInAllegiance");
        lock (CreationLock)
        {
            var quote = RequireCreationQuote(sessionId, request.QuoteToken);
            RecoverTeammateCreation(sessionId);
            quote = RequireCreationQuote(sessionId, request.QuoteToken);
            var player = GetPlayerProfile(sessionId);
            if (quote.State == "complete") return new() { PlayerStashItems = GetPlayerStashItems(player) };
            if (saveServer.IsProfileInvalidOrUnloadable(sessionId)) throw new FriendlyTeammateException("TeammateHirePurchaseFailed");
            if (quote.State != "pending" || quote.PricingVersion != HiringPricingVersion || quote.ExpiresUtc <= DateTime.UtcNow
                || quote.Mode != NormalizeLoadoutManagementMode(settingsService.LoadSettings().LoadoutManagementMode))
                throw new FriendlyTeammateException("TeammateHireQuoteExpired");
            EnsureNicknameIsUnique(sessionId, quote.Teammate.Info!.Nickname!);
            if (request.WithoutKit)
            {
                // Commit the stripped candidate/default and receipt in the same database batch.
                // No player payment/save is needed. Failure leaves the original quote intact.
                var withoutKit = cloner.Clone(quote)!;
                StripToPermanentEquipment(withoutKit.Teammate, keepSecureContainer: true, clearSecureContents: true);
                withoutKit.WithoutKit = true;
                withoutKit.Price = 0;
                CompleteTeammateCreation(sessionId, withoutKit);
                return new() { PlayerStashItems = GetPlayerStashItems(player) };
            }
            var originalItems = cloner.Clone(player.Inventory!.Items)!;
            quote.MoneyBefore = CreationMoneySignature(player);
            try { DeductRoublesFromPlayerStash(player, quote.Price); }
            catch (FriendlyTeammateException) { throw new FriendlyTeammateException("TeammateHireInsufficientFunds"); }
            quote.MoneyAfter = CreationMoneySignature(player);
            quote.State = "paying";
            try
            {
                // Journal first: a restart between the player save and teammate save is recoverable.
                storage.Write(sessionId, CreationDocument, quote);
                SavePlayerMoneyVerified(sessionId, quote.MoneyAfter!, "TeammateHireRecoveryFailed");
                CompleteTeammateCreation(sessionId, quote);
            }
            catch
            {
                // Never restore money after the receipt and teammate were atomically committed.
                if (storage.Read<FriendlyTeammateCreationQuote>(sessionId, CreationDocument)?.State != "complete")
                {
                    quote.State = "refunding";
                    quote.RefundMoneyItems = SnapshotRefundMoney(originalItems);
                    try { storage.Write(sessionId, CreationDocument, quote); }
                    finally { player.Inventory.Items = originalItems; }
                    SavePlayerMoneyVerified(sessionId, quote.MoneyBefore!, "TeammateHireRecoveryFailed");
                    quote.State = "pending";
                    quote.RefundMoneyItems = null;
                    storage.Write(sessionId, CreationDocument, quote);
                }
                throw;
            }
            return new() { PlayerStashItems = GetPlayerStashItems(player) };
        }
    }

    public void CancelTeammateCreation(MongoId sessionId, FriendlyTeammateCreateRequest request)
    {
        lock (CreationLock)
        {
            RecoverTeammateCreation(sessionId);
            var quote = storage.Read<FriendlyTeammateCreationQuote>(sessionId, CreationDocument);
            if (quote == null || quote.Token != request.QuoteToken || quote.State != "pending") return;
            quote.State = "cancelled";
            storage.Write(sessionId, CreationDocument, quote);
        }
    }

    private FriendlyTeammateCreationQuote RequireCreationQuote(MongoId sessionId, string? token)
    {
        var quote = storage.Read<FriendlyTeammateCreationQuote>(sessionId, CreationDocument);
        if (string.IsNullOrWhiteSpace(token) || quote == null || quote.Token != token)
            throw new FriendlyTeammateException("TeammateHireQuoteExpired");
        return quote;
    }

    public void RecoverTeammateCreation(MongoId sessionId)
    {
        lock (CreationLock)
        {
            var quote = storage.Read<FriendlyTeammateCreationQuote>(sessionId, CreationDocument);
            if (quote?.State is not ("paying" or "refunding")) return;
            var player = GetPlayerProfile(sessionId);
            if (quote.State == "refunding")
                RestoreRefundMoney(player, quote.RefundMoneyItems, quote.MoneyBefore, quote.MoneyAfter, "TeammateHireRecoveryFailed");
            string money = CreationMoneySignature(player);
            if (money == quote.MoneyAfter)
            {
                SavePlayerMoneyVerified(sessionId, quote.MoneyAfter!, "TeammateHireRecoveryFailed");
                CompleteTeammateCreation(sessionId, quote);
            }
            else if (money == quote.MoneyBefore)
            {
                SavePlayerMoneyVerified(sessionId, quote.MoneyBefore!, "TeammateHireRecoveryFailed");
                quote.State = "pending";
                quote.RefundMoneyItems = null;
                storage.Write(sessionId, CreationDocument, quote);
            }
            else throw new FriendlyTeammateException("TeammateHireRecoveryFailed");
        }
    }

    private void CompleteTeammateCreation(MongoId sessionId, FriendlyTeammateCreationQuote quote)
    {
        quote.State = "complete";
        SaveTeammateWithDefaultEquipment(sessionId, quote.Teammate, IsExtremeLoadoutManagementMode(quote.Mode),
            CreateDefaultTeammateSettings(quote.Teammate.Customization),
            new Dictionary<string, string> { [CreationDocument] = jsonUtil.Serialize(quote)! });
        logger.Info($"Created teammate '{quote.Teammate.Aid}' price={quote.Price} withoutKit={quote.WithoutKit} quote={quote.Token}");
    }

    private static string CreationMoneySignature(PmcData player) => string.Join(";", player.Inventory?.Items?
        .Where(item => item.Template.ToString() == Constants.FriendlyItemTemplateIds.Currency.Roubles)
        .OrderBy(item => item.Id.ToString(), StringComparer.Ordinal)
        .Select(item => $"{item.Id}:{item.Upd?.StackObjectsCount ?? 1}:{item.ParentId}:{item.SlotId}") ?? []);

    private void SetHiringBayonet(BotBase teammate)
    {
        string rootId = GetEquipmentRootId(teammate);
        var items = teammate.Inventory!.Items!;
        RemoveItemTreesById(items, items.Where(item =>
            string.Equals(item.ParentId, rootId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.SlotId, nameof(EquipmentSlots.Scabbard), StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Id.ToString()).ToArray());
        items.Add(new Item
        {
            Id = new MongoId(),
            Template = new MongoId(Constants.FriendlyItemTemplateIds.Weapon.HiringBayonet),
            ParentId = rootId,
            SlotId = nameof(EquipmentSlots.Scabbard),
            Upd = new Upd { StackObjectsCount = 1, SpawnedInSession = false }
        });
    }
}
