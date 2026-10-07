namespace pitTeam.Server.Models;

// Kept independently of the two-raid diagnostic window until the mail is claimed.
public sealed class FollowerInsuranceCourierTransfers
{
    public List<FollowerInsuranceCourierTransfer> Items { get; set; } = [];
}

public sealed class FollowerInsuranceCourierTransfer
{
    public string ServerId { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public string SourceItemId { get; set; } = string.Empty;
    public string MailItemId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string TraderId { get; set; } = string.Empty;
    // pending -> authorized -> reserved -> complete. A reserved uncertainty fails closed.
    public string State { get; set; } = "pending";
}
