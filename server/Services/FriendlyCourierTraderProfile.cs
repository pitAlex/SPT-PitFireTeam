using pitTeam.Server.Constants;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

internal static class FriendlyCourierTraderProfile
{
    public const string CourierTraderIdValue = "67d3a28a3d6f4f7dbd09ed13";
    public const int CourierAid = 1113680;
    public const string CourierNickname = "-P|T- Comms";
    public const string CourierLocation = "Friendly Squad Logistics";
    public const string CourierDescription = "Handles item transfers from your squadmates.";
    public const string CourierAvatarFileName = "pitfireteam-courier.png";
    public const string CourierAvatarPath = "/files/trader/avatar/" + CourierAvatarFileName;

    public static readonly MongoId CourierTraderId = new(CourierTraderIdValue);

    public static void GetLocalizedIdentity(string? locale, out string nickname, out string location, out string description)
    {
        string normalized = NormalizeLocale(locale);
        if (normalized == "ru")
        {
            nickname = "ДоставкаОтряда";
            location = "Логистика дружественного отряда";
            description = "Возвращает предметы, переданные вашими бойцами.";
            return;
        }

        nickname = CourierNickname;
        location = CourierLocation;
        description = CourierDescription;
    }

    public static Trader CreateTrader()
    {
        return new Trader
        {
            Base = new TraderBase
            {
                Id = CourierTraderId,
                // EFT excludes raid-only service traders (like BTR) from regular trader card flows.
                AvailableInRaid = true,
                Avatar = CourierAvatarPath,
                BalanceDollar = 0,
                BalanceEuro = 0,
                BalanceRub = 0,
                BuyerUp = false,
                Currency = CurrencyType.RUB,
                CustomizationSeller = false,
                Discount = 0,
                DiscountEnd = 0,
                GridHeight = 120,
                Insurance = new TraderInsurance
                {
                    Availability = false,
                    ExcludedCategory = [],
                    MaxReturnHour = 0,
                    MaxStorageTime = 48,
                    MinPayment = 0,
                    MinReturnHour = 0,
                },
                ItemsBuy = CreateEmptyItemBuyData(),
                ItemsBuyProhibited = CreateEmptyItemBuyData(),
                ItemsSell = [],
                IsAvailableInPVE = true,
                IsCanTransferItems = false,
                IsCanTransferItemsFromPve = false,
                TransferableItems = CreateEmptyItemBuyData(),
                ProhibitedTransferableItems = CreateEmptyItemBuyData(),
                Location = CourierLocation,
                LoyaltyLevels =
                [
                    new TraderLoyaltyLevel
                    {
                        BuyPriceCoefficient = 0,
                        ExchangePriceCoefficient = 0,
                        HealPriceCoefficient = 0,
                        InsurancePriceCoefficient = 0,
                        MinLevel = 1,
                        MinSalesSum = 0,
                        MinStanding = 0,
                        RepairPriceCoefficient = 0,
                    },
                ],
                Medic = false,
                Name = CourierNickname,
                NextResupply = 0,
                Nickname = CourierNickname,
                Repair = new TraderRepair
                {
                    Availability = false,
                    Currency = FriendlyItemTemplateIds.Currency.Roubles,
                    CurrencyCoefficient = 1,
                    ExcludedCategory = [],
                    ExcludedIdList = [],
                    Quality = 0,
                    PriceRate = 1,
                },
                SellCategory = [],
                Surname = string.Empty,
                UnlockedByDefault = false,
            },
            Assort = new TraderAssort
            {
                NextResupply = 0,
                Items = [],
                BarterScheme = [],
                LoyalLevelItems = [],
            },
            Dialogue = [],
            QuestAssort = new Dictionary<string, Dictionary<MongoId, MongoId>>
            {
                ["started"] = [],
                ["success"] = [],
                ["fail"] = [],
            },
            Suits = [],
            Services = [],
        };
    }

    private static ItemBuyData CreateEmptyItemBuyData()
    {
        return new ItemBuyData
        {
            Category = [],
            IdList = [],
        };
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        string normalized = locale.Trim().ToLowerInvariant();
        int separatorIndex = normalized.IndexOfAny(['-', '_']);
        return separatorIndex > 0 ? normalized[..separatorIndex] : normalized;
    }
}
