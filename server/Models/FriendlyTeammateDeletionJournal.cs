using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace pitTeam.Server.Models;

public record FriendlyTeammateDeletionJournal
{
    public int Aid { get; set; }
    public string State { get; set; } = "pending";
    public int Price { get; set; }
    public string MoneyBefore { get; set; } = string.Empty;
    public string MoneyAfter { get; set; } = string.Empty;
    public List<Item>? RefundMoneyItems { get; set; }
    public Message? Delivery { get; set; }
    public long DeliveryReceipt { get; set; }
    public List<string> DeliverySourceIds { get; set; } = [];
}
